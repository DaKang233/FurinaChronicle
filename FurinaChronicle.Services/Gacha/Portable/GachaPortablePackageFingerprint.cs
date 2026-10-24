// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Security.Cryptography;
using FurinaChronicle.Core.Gacha;

namespace FurinaChronicle.Services.Gacha.Portable;

public static class GachaPortablePackageFingerprint
{
    public static string Compute(GachaPortablePackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        using IncrementalHash hash = IncrementalHash.CreateHash(
            HashAlgorithmName.SHA256);
        Append(hash, package.GeneratedAt.ToString("O", CultureInfo.InvariantCulture));
        Append(hash, package.SourceArchive.ArchiveReference.ToString("D"));
        Append(hash, package.SourceArchive.Name);
        foreach (GachaPortableAccount account in package.Accounts)
        {
            Append(hash, account.AccountReference.ToString("D"));
            Append(hash, account.RoleIdentity.Id.ToString());
            Append(hash, account.RoleIdentity.NaturalIdentity.GameBiz);
            Append(hash, account.RoleIdentity.NaturalIdentity.Server);
            Append(hash, account.RoleIdentity.NaturalIdentity.Uid);
            Append(hash, account.DisplayName);
            foreach (GachaRecord record in account.Records)
            {
                Append(hash, record.ExternalRecordId);
                Append(hash, record.ItemName);
                Append(hash, record.ItemId);
                Append(hash, record.ItemType);
                Append(hash, record.GachaType);
                Append(hash, record.UigfGachaType);
                Append(hash, record.RankType?.ToString(CultureInfo.InvariantCulture));
                Append(hash, record.Count.ToString(CultureInfo.InvariantCulture));
                Append(hash, record.Time.ToString("O", CultureInfo.InvariantCulture));
                Append(hash, ((int)record.Provenance.Origin)
                    .ToString(CultureInfo.InvariantCulture));
                AppendTimestamp(hash, record.Provenance.Timestamps.ObservedAt);
                AppendTimestamp(hash, record.Provenance.Timestamps.FetchedAt);
                AppendTimestamp(hash, record.Provenance.Timestamps.ImportedAt);
                Append(hash, record.Provenance.Source?.Provider);
                Append(hash, record.Provenance.Source?.SourceRecordId);
                Append(hash, record.Provenance.Source?.SourceSnapshotId);
                Append(hash, record.Provenance.AcquisitionBatchId?.ToString());
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset())
            .ToLowerInvariant();
    }

    private static void AppendTimestamp(
        IncrementalHash target,
        DateTimeOffset? value) =>
        Append(
            target,
            value?.ToString("O", CultureInfo.InvariantCulture));

    private static void Append(IncrementalHash target, string? value)
    {
        if (value is null)
        {
            target.AppendData("-1:"u8);
            return;
        }

        string prefix = value.Length.ToString(CultureInfo.InvariantCulture);
        target.AppendData(System.Text.Encoding.UTF8.GetBytes(prefix));
        target.AppendData(":"u8);
        target.AppendData(System.Text.Encoding.UTF8.GetBytes(value));
    }
}
