// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FurinaChronicle.Core.Gacha;

namespace FurinaChronicle.Services.Gacha.Portable;

public static class GachaPortablePackageFingerprint
{
    public static string Compute(GachaPortablePackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var value = new StringBuilder();
        Append(value, package.GeneratedAt.ToString("O", CultureInfo.InvariantCulture));
        Append(value, package.SourceArchive.ArchiveReference.ToString("D"));
        Append(value, package.SourceArchive.Name);
        foreach (GachaPortableAccount account in package.Accounts)
        {
            Append(value, account.AccountReference.ToString("D"));
            Append(value, account.RoleIdentity.Id.ToString());
            Append(value, account.RoleIdentity.NaturalIdentity.GameBiz);
            Append(value, account.RoleIdentity.NaturalIdentity.Server);
            Append(value, account.RoleIdentity.NaturalIdentity.Uid);
            Append(value, account.DisplayName);
            foreach (GachaRecord record in account.Records)
            {
                Append(value, record.ExternalRecordId);
                Append(value, record.ItemName);
                Append(value, record.ItemId);
                Append(value, record.ItemType);
                Append(value, record.GachaType);
                Append(value, record.UigfGachaType);
                Append(value, record.RankType?.ToString(CultureInfo.InvariantCulture));
                Append(value, record.Count.ToString(CultureInfo.InvariantCulture));
                Append(value, record.Time.ToString("O", CultureInfo.InvariantCulture));
                Append(value, ((int)record.Provenance.Origin)
                    .ToString(CultureInfo.InvariantCulture));
                AppendTimestamp(value, record.Provenance.Timestamps.ObservedAt);
                AppendTimestamp(value, record.Provenance.Timestamps.FetchedAt);
                AppendTimestamp(value, record.Provenance.Timestamps.ImportedAt);
                Append(value, record.Provenance.Source?.Provider);
                Append(value, record.Provenance.Source?.SourceRecordId);
                Append(value, record.Provenance.Source?.SourceSnapshotId);
                Append(value, record.Provenance.AcquisitionBatchId?.ToString());
            }
        }

        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value.ToString())))
            .ToLowerInvariant();
    }

    private static void AppendTimestamp(
        StringBuilder target,
        DateTimeOffset? value) =>
        Append(
            target,
            value?.ToString("O", CultureInfo.InvariantCulture));

    private static void Append(StringBuilder target, string? value)
    {
        if (value is null)
        {
            target.Append("-1:");
            return;
        }

        target
            .Append(value.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(value);
    }
}
