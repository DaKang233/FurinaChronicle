// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Services.Gacha.Portable;

namespace FurinaChronicle.Infrastructure.Gacha.Portable;

public sealed partial class GachaPortablePackageWriter
{
    public async Task<GachaPortableWriteResult> WriteAsync(
        Stream destination,
        IGachaPortableExportSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!destination.CanWrite)
        {
            throw new ArgumentException(
                "The destination stream is not writable.",
                nameof(destination));
        }

        ValidateSnapshot(snapshot);
        var manifests = new List<GachaPortableAccountManifestDto>(
            snapshot.Accounts.Count);
        long totalPayloadBytes = 0;
        int totalRecordCount = 0;

        using (var archive = new ZipArchive(
            destination,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach (GachaPortableExportAccount account in snapshot.Accounts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                StreamingPassResult uigfPass = default;
                PortablePayloadDescriptor uigf = await WritePayloadAsync(
                    archive,
                    GachaPortableRules.GetUigfPath(account.AccountReference),
                    async stream =>
                    {
                        uigfPass = await WriteStreamingUigfAsync(
                            stream,
                            snapshot,
                            account,
                            cancellationToken);
                    },
                    cancellationToken);

                StreamingPassResult supplementPass = default;
                PortablePayloadDescriptor supplement = await WritePayloadAsync(
                    archive,
                    GachaPortableRules.GetSupplementPath(account.AccountReference),
                    async stream =>
                    {
                        supplementPass = await WriteStreamingSupplementAsync(
                            stream,
                            snapshot,
                            account,
                            cancellationToken);
                    },
                    cancellationToken);

                if (uigfPass.RecordCount != account.RecordCount ||
                    supplementPass.RecordCount != account.RecordCount)
                {
                    throw GachaPortableRules.Error(
                        GachaPortableErrorCode.RecordCountMismatch,
                        "The export snapshot changed while it was being read.");
                }

                if (!CryptographicOperations.FixedTimeEquals(
                        uigfPass.Fingerprint,
                        supplementPass.Fingerprint))
                {
                    throw GachaPortableRules.Error(
                        GachaPortableErrorCode.RecordMismatch,
                        "The UIGF and supplement passes did not read the same records.");
                }

                totalRecordCount = checked(
                    totalRecordCount + account.RecordCount);
                totalPayloadBytes = checked(
                    totalPayloadBytes + uigf.Length + supplement.Length);
                EnsureUncompressedLimit(totalPayloadBytes);
                manifests.Add(ToManifest(account, uigf, supplement));
            }

            GachaPortableManifestDto manifest = CreateManifest(
                snapshot,
                manifests,
                totalRecordCount);
            ZipArchiveEntry manifestEntry = archive.CreateEntry(
                GachaPortableRules.ManifestPath,
                CompressionLevel.Optimal);
            await using Stream manifestStream = manifestEntry.Open();
            await using var countingManifest =
                new HashingWriteStream(manifestStream);
            await JsonSerializer.SerializeAsync(
                countingManifest,
                manifest,
                JsonOptions,
                cancellationToken);
            await countingManifest.FlushAsync(cancellationToken);
            if (countingManifest.ByteCount > limits.MaximumManifestBytes)
            {
                throw GachaPortableRules.Error(
                    GachaPortableErrorCode.ResourceLimitExceeded,
                    "manifest.json exceeds the Portable v1 limit.",
                    GachaPortableRules.ManifestPath);
            }

            EnsureUncompressedLimit(
                checked(totalPayloadBytes + countingManifest.ByteCount));
        }

        return new GachaPortableWriteResult(
            snapshot.Accounts.Count,
            totalRecordCount,
            totalPayloadBytes);
    }

    private void ValidateSnapshot(IGachaPortableExportSnapshot snapshot)
    {
        if (snapshot.SourceArchive.ArchiveReference == Guid.Empty ||
            snapshot.SourceArchive.Name is null)
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.InvalidPackage,
                "The source archive reference and name are required.");
        }

        if (snapshot.Accounts.Count is < 1 ||
            snapshot.Accounts.Count > limits.MaximumAccountCount)
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.ResourceLimitExceeded,
                "The account count is outside the Portable v1 limit.");
        }

        int recordCount = 0;
        var accountReferences = new HashSet<Guid>();
        var naturalIdentities = new HashSet<string>(StringComparer.Ordinal);
        var identitiesById = new Dictionary<Guid, string>();
        foreach (GachaPortableExportAccount account in snapshot.Accounts)
        {
            if (account.AccountReference == Guid.Empty ||
                !accountReferences.Add(account.AccountReference))
            {
                throw GachaPortableRules.Error(
                    GachaPortableErrorCode.DuplicateReference,
                    "account_ref must be non-empty and unique.");
            }

            if (account.RecordCount < 0)
            {
                throw GachaPortableRules.Error(
                    GachaPortableErrorCode.InvalidPackage,
                    "record_count cannot be negative.");
            }

            recordCount = checked(recordCount + account.RecordCount);
            GachaPortableRules.ValidateNaturalIdentity(
                account.RoleIdentity.NaturalIdentity);
            string naturalName =
                account.RoleIdentity.NaturalIdentity.ToDeterministicName();
            if (!naturalIdentities.Add(naturalName))
            {
                throw GachaPortableRules.Error(
                    GachaPortableErrorCode.DuplicateReference,
                    "A source archive cannot contain duplicate natural identities.");
            }

            Guid roleId = account.RoleIdentity.Id.Value;
            if (identitiesById.TryGetValue(roleId, out string? existing) &&
                !string.Equals(existing, naturalName, StringComparison.Ordinal))
            {
                throw GachaPortableRules.Error(
                    GachaPortableErrorCode.InvalidReference,
                    "A role identity GUID refers to different natural identities.");
            }

            identitiesById[roleId] = naturalName;
        }

        if (recordCount > limits.MaximumRecordCount)
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.ResourceLimitExceeded,
                "The record count exceeds the Portable v1 limit.");
        }
    }

    private static async Task<StreamingPassResult> WriteStreamingUigfAsync(
        Stream destination,
        IGachaPortableExportSnapshot snapshot,
        GachaPortableExportAccount account,
        CancellationToken cancellationToken)
    {
        using var fingerprint = new GachaRecordFingerprint();
        int count = 0;
        await using var writer = new Utf8JsonWriter(destination);
        writer.WriteStartObject();
        writer.WritePropertyName("info");
        writer.WriteStartObject();
        writer.WriteNumber(
            "export_timestamp",
            snapshot.GeneratedAt.ToUnixTimeSeconds());
        writer.WriteString("export_app", "FurinaChronicle");
        writer.WriteString("export_app_version", "portable-1.0");
        writer.WriteString("version", GachaPortableRules.UigfVersion);
        writer.WriteEndObject();
        writer.WritePropertyName("hk4e");
        writer.WriteStartArray();
        writer.WriteStartObject();
        writer.WriteString(
            "uid",
            account.RoleIdentity.NaturalIdentity.Uid);
        writer.WriteNumber("timezone", 0);
        writer.WritePropertyName("list");
        writer.WriteStartArray();
        await foreach (GachaRecord record in snapshot.ReadRecordsAsync(
            account.AccountReference,
            cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            GachaPortableRules.ValidateRecord(
                record,
                account.AccountReference);
            WriteUigfRecord(writer, record);
            fingerprint.Append(record);
            count = checked(count + 1);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndArray();
        writer.WriteEndObject();
        await writer.FlushAsync(cancellationToken);
        return new StreamingPassResult(count, fingerprint.Complete());
    }

    private async Task<StreamingPassResult> WriteStreamingSupplementAsync(
        Stream destination,
        IGachaPortableExportSnapshot snapshot,
        GachaPortableExportAccount account,
        CancellationToken cancellationToken)
    {
        using var fingerprint = new GachaRecordFingerprint();
        int count = 0;
        await foreach (GachaRecord record in snapshot.ReadRecordsAsync(
            account.AccountReference,
            cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            GachaPortableRules.ValidateRecord(
                record,
                account.AccountReference);
            byte[] line = JsonSerializer.SerializeToUtf8Bytes(
                new GachaPortableSupplementDto
                {
                    AccountReference = account.AccountReference,
                    ExternalRecordId = record.ExternalRecordId,
                    OccurredAtUtcTicks = record.Time.UtcDateTime.Ticks,
                    OccurredAtOffsetMinutes =
                        checked((int)record.Time.Offset.TotalMinutes),
                    Origin = GachaPortableRules.FormatOrigin(
                        record.Provenance.Origin),
                    FetchedAt = record.Provenance.Timestamps.FetchedAt,
                    ImportedAt = record.Provenance.Timestamps.ImportedAt,
                    AcquisitionBatchId =
                        record.Provenance.AcquisitionBatchId?.Value,
                },
                LineJsonOptions);
            if (line.Length > limits.MaximumNdjsonLineBytes)
            {
                throw GachaPortableRules.Error(
                    GachaPortableErrorCode.ResourceLimitExceeded,
                    "A supplement line exceeds the Portable v1 limit.",
                    GachaPortableRules.GetSupplementPath(
                        account.AccountReference),
                    record.ExternalRecordId);
            }

            await destination.WriteAsync(line, cancellationToken);
            await destination.WriteAsync("\n"u8.ToArray(), cancellationToken);
            fingerprint.Append(record);
            count = checked(count + 1);
        }

        return new StreamingPassResult(count, fingerprint.Complete());
    }

    private static GachaPortableAccountManifestDto ToManifest(
        GachaPortableExportAccount account,
        PortablePayloadDescriptor uigf,
        PortablePayloadDescriptor supplement)
    {
        return new GachaPortableAccountManifestDto
        {
            AccountReference = account.AccountReference,
            RoleIdentityId = account.RoleIdentity.Id.Value,
            NaturalIdentity = new GachaPortableNaturalIdentityDto
            {
                GameBiz = account.RoleIdentity.NaturalIdentity.GameBiz,
                Server = account.RoleIdentity.NaturalIdentity.Server,
                Uid = account.RoleIdentity.NaturalIdentity.Uid,
            },
            DisplayName = account.DisplayName,
            UigfPath = uigf.Path,
            SupplementPath = supplement.Path,
            RecordCount = account.RecordCount,
            UigfLength = uigf.Length,
            UigfSha256 = uigf.Sha256,
            SupplementLength = supplement.Length,
            SupplementSha256 = supplement.Sha256,
        };
    }

    private static GachaPortableManifestDto CreateManifest(
        IGachaPortableExportSnapshot snapshot,
        List<GachaPortableAccountManifestDto> accounts,
        int recordCount)
    {
        return new GachaPortableManifestDto
        {
            Format = GachaPortableRules.Format,
            FormatVersion = GachaPortableRules.CurrentVersion,
            Game = GachaPortableRules.Game,
            GeneratedAt = snapshot.GeneratedAt,
            SourceArchive = new GachaPortableArchiveDto
            {
                ArchiveReference = snapshot.SourceArchive.ArchiveReference,
                Name = snapshot.SourceArchive.Name,
            },
            Scope = new GachaPortableScopeDto
            {
                Kind = "selected_accounts",
                AccountCount = accounts.Count,
                RecordCount = recordCount,
                CompletenessAssertion = "none",
            },
            Accounts = accounts.Cast<GachaPortableAccountManifestDto?>().ToList(),
        };
    }

    private readonly record struct StreamingPassResult(
        int RecordCount,
        byte[] Fingerprint);

    private sealed class GachaRecordFingerprint : IDisposable
    {
        private readonly IncrementalHash hash =
            IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        public void Append(GachaRecord record)
        {
            AppendString(record.ExternalRecordId);
            AppendString(record.ItemName);
            AppendString(record.ItemId);
            AppendString(record.ItemType);
            AppendString(record.GachaType);
            AppendString(record.UigfGachaType);
            AppendString(record.RankType?.ToString(CultureInfo.InvariantCulture));
            AppendString(record.Count.ToString(CultureInfo.InvariantCulture));
            AppendString(record.Time.UtcDateTime.Ticks.ToString(
                CultureInfo.InvariantCulture));
            AppendString(record.Time.Offset.TotalMinutes.ToString(
                CultureInfo.InvariantCulture));
            AppendString(((int)record.Provenance.Origin).ToString(
                CultureInfo.InvariantCulture));
            AppendTimestamp(record.Provenance.Timestamps.FetchedAt);
            AppendTimestamp(record.Provenance.Timestamps.ImportedAt);
            AppendString(record.Provenance.AcquisitionBatchId?.Value.ToString("D"));
        }

        public byte[] Complete() => hash.GetHashAndReset();

        public void Dispose() => hash.Dispose();

        private void AppendTimestamp(DateTimeOffset? value)
        {
            AppendString(value?.UtcDateTime.Ticks.ToString(
                CultureInfo.InvariantCulture));
            AppendString(value?.Offset.TotalMinutes.ToString(
                CultureInfo.InvariantCulture));
        }

        private void AppendString(string? value)
        {
            byte[] bytes = value is null
                ? []
                : Encoding.UTF8.GetBytes(value);
            Span<byte> length = stackalloc byte[sizeof(int)];
            BitConverter.TryWriteBytes(length, value is null ? -1 : bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }
    }
}
