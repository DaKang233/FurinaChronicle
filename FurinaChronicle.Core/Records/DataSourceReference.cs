// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Core.Records;

public sealed record DataSourceReference
{
    public DataSourceReference(
        string? provider = null,
        string? sourceRecordId = null,
        string? sourceSnapshotId = null)
    {
        Provider = Normalize(provider);
        SourceRecordId = Normalize(sourceRecordId);
        SourceSnapshotId = Normalize(sourceSnapshotId);

        if (Provider is null &&
            SourceRecordId is null &&
            SourceSnapshotId is null)
        {
            throw new ArgumentException(
                "A source reference must contain at least one reference value.");
        }
    }

    public string? Provider { get; }

    public string? SourceRecordId { get; }

    public string? SourceSnapshotId { get; }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
