// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Services.Gacha.Portable;

namespace FurinaChronicle.Infrastructure.Gacha.Portable;

public sealed class GachaPortablePackageWriter(
    GachaPortableLimits? limits = null)
    : IGachaPortablePackageWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        MaxDepth = GachaPortableLimits.DefaultMaximumJsonDepth,
    };

    private static readonly JsonSerializerOptions LineJsonOptions = new()
    {
        WriteIndented = false,
        MaxDepth = GachaPortableLimits.DefaultMaximumJsonDepth,
    };

    private readonly GachaPortableLimits limits = limits ?? new();

    public async Task<GachaPortableWriteResult> WriteAsync(
        Stream destination,
        GachaPortablePackage package,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(package);
        if (!destination.CanWrite)
        {
            throw new ArgumentException(
                "The destination stream is not writable.",
                nameof(destination));
        }

        ValidatePackage(package);

        var manifests = new List<GachaPortableAccountManifestDto>(
            package.Accounts.Count);
        long totalPayloadBytes = 0;
        using (var archive = new ZipArchive(
            destination,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach (GachaPortableAccount account in package.Accounts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PortablePayloadDescriptor uigf = await WritePayloadAsync(
                    archive,
                    GachaPortableRules.GetUigfPath(account.AccountReference),
                    stream => WriteUigfAsync(
                        stream,
                        package.GeneratedAt,
                        account,
                        cancellationToken),
                    cancellationToken);
                PortablePayloadDescriptor supplement = await WritePayloadAsync(
                    archive,
                    GachaPortableRules.GetSupplementPath(account.AccountReference),
                    stream => WriteSupplementAsync(
                        stream,
                        account,
                        cancellationToken),
                    cancellationToken);
                totalPayloadBytes = checked(
                    totalPayloadBytes + uigf.Length + supplement.Length);
                EnsureUncompressedLimit(totalPayloadBytes);

                manifests.Add(ToManifest(account, uigf, supplement));
            }

            GachaPortableManifestDto manifest = CreateManifest(package, manifests);
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
            package.Accounts.Count,
            package.RecordCount,
            totalPayloadBytes);
    }

    private void ValidatePackage(GachaPortablePackage package)
    {
        if (package.SourceArchive.ArchiveReference == Guid.Empty ||
            package.SourceArchive.Name is null)
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.InvalidPackage,
                "The source archive reference and name are required.");
        }

        if (package.Accounts.Count is < 1 ||
            package.Accounts.Count > limits.MaximumAccountCount)
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.ResourceLimitExceeded,
                "The account count is outside the Portable v1 limit.");
        }

        if (package.RecordCount > limits.MaximumRecordCount)
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.ResourceLimitExceeded,
                "The record count exceeds the Portable v1 limit.");
        }

        var accountReferences = new HashSet<Guid>();
        var naturalIdentities = new HashSet<string>(StringComparer.Ordinal);
        var identitiesById = new Dictionary<Guid, string>();
        foreach (GachaPortableAccount account in package.Accounts)
        {
            if (account.AccountReference == Guid.Empty ||
                !accountReferences.Add(account.AccountReference))
            {
                throw GachaPortableRules.Error(
                    GachaPortableErrorCode.DuplicateReference,
                    "account_ref must be non-empty and unique.");
            }

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
            var recordIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (GachaRecord record in account.Records)
            {
                GachaPortableRules.ValidateRecord(
                    record,
                    account.AccountReference);
                if (!recordIds.Add(record.ExternalRecordId))
                {
                    throw GachaPortableRules.Error(
                        GachaPortableErrorCode.DuplicateReference,
                        "Duplicate external_record_id within an account.",
                        recordId: record.ExternalRecordId);
                }
            }
        }
    }

    private async Task<PortablePayloadDescriptor> WritePayloadAsync(
        ZipArchive archive,
        string path,
        Func<Stream, Task> write,
        CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        await using Stream entryStream = entry.Open();
        await using var hashingStream = new HashingWriteStream(entryStream);
        await write(hashingStream);
        await hashingStream.FlushAsync(cancellationToken);
        if (hashingStream.ByteCount > limits.MaximumEntryBytes)
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.ResourceLimitExceeded,
                $"Entry exceeds the Portable v1 limit: {path}.",
                path);
        }

        return new PortablePayloadDescriptor(
            path,
            hashingStream.ByteCount,
            hashingStream.CompleteHash());
    }

    private static async Task WriteUigfAsync(
        Stream destination,
        DateTimeOffset generatedAt,
        GachaPortableAccount account,
        CancellationToken cancellationToken)
    {
        await using var writer = new Utf8JsonWriter(destination);
        writer.WriteStartObject();
        writer.WritePropertyName("info");
        writer.WriteStartObject();
        writer.WriteNumber("export_timestamp", generatedAt.ToUnixTimeSeconds());
        writer.WriteString("export_app", "FurinaChronicle");
        writer.WriteString("export_app_version", "portable-1.0");
        writer.WriteString("version", GachaPortableRules.UigfVersion);
        writer.WriteEndObject();
        writer.WritePropertyName("hk4e");
        writer.WriteStartArray();
        writer.WriteStartObject();
        writer.WriteString("uid", account.RoleIdentity.NaturalIdentity.Uid);
        writer.WriteNumber("timezone", 0);
        writer.WritePropertyName("list");
        writer.WriteStartArray();
        foreach (GachaRecord record in account.Records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteUigfRecord(writer, record);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndArray();
        writer.WriteEndObject();
        await writer.FlushAsync(cancellationToken);
    }

    private static void WriteUigfRecord(
        Utf8JsonWriter writer,
        GachaRecord record)
    {
        writer.WriteStartObject();
        writer.WriteString("gacha_type", record.GachaType);
        writer.WriteString("item_id", record.ItemId);
        writer.WriteString(
            "count",
            record.Count.ToString(CultureInfo.InvariantCulture));
        writer.WriteString(
            "time",
            record.Time.UtcDateTime.ToString(
                GachaPortableRules.TimeFormat,
                CultureInfo.InvariantCulture));
        if (record.ItemName is not null)
        {
            writer.WriteString("name", record.ItemName);
        }

        if (record.ItemType is not null)
        {
            writer.WriteString("item_type", record.ItemType);
        }

        if (record.RankType is int rankType)
        {
            writer.WriteString(
                "rank_type",
                rankType.ToString(CultureInfo.InvariantCulture));
        }

        writer.WriteString("id", record.ExternalRecordId);
        writer.WriteString("uigf_gacha_type", record.UigfGachaType);
        writer.WriteEndObject();
    }

    private async Task WriteSupplementAsync(
        Stream destination,
        GachaPortableAccount account,
        CancellationToken cancellationToken)
    {
        foreach (GachaRecord record in account.Records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] line = JsonSerializer.SerializeToUtf8Bytes(
                new GachaPortableSupplementDto
                {
                    AccountReference = account.AccountReference,
                    ExternalRecordId = record.ExternalRecordId,
                    OccurredAtUtcTicks = record.Time.UtcDateTime.Ticks,
                    OccurredAtOffsetMinutes = (int)record.Time.Offset.TotalMinutes,
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
                    GachaPortableRules.GetSupplementPath(account.AccountReference),
                    record.ExternalRecordId);
            }

            await destination.WriteAsync(line, cancellationToken);
            await destination.WriteAsync("\n"u8.ToArray(), cancellationToken);
        }
    }

    private static GachaPortableAccountManifestDto ToManifest(
        GachaPortableAccount account,
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
            RecordCount = account.Records.Count,
            UigfLength = uigf.Length,
            UigfSha256 = uigf.Sha256,
            SupplementLength = supplement.Length,
            SupplementSha256 = supplement.Sha256,
        };
    }

    private static GachaPortableManifestDto CreateManifest(
        GachaPortablePackage package,
        List<GachaPortableAccountManifestDto> accounts)
    {
        return new GachaPortableManifestDto
        {
            Format = GachaPortableRules.Format,
            FormatVersion = GachaPortableRules.CurrentVersion,
            Game = GachaPortableRules.Game,
            GeneratedAt = package.GeneratedAt,
            SourceArchive = new GachaPortableArchiveDto
            {
                ArchiveReference = package.SourceArchive.ArchiveReference,
                Name = package.SourceArchive.Name,
            },
            Scope = new GachaPortableScopeDto
            {
                Kind = "selected_accounts",
                AccountCount = accounts.Count,
                RecordCount = accounts.Sum(account => account.RecordCount),
                CompletenessAssertion = "none",
            },
            Accounts = accounts.Cast<GachaPortableAccountManifestDto?>().ToList(),
        };
    }

    private void EnsureUncompressedLimit(long bytes)
    {
        if (bytes > limits.MaximumUncompressedBytes)
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.ResourceLimitExceeded,
                "The uncompressed payload exceeds the Portable v1 limit.");
        }
    }
}
