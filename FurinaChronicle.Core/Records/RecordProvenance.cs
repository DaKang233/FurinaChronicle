// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Core.Records;

public sealed record RecordProvenance
{
    public RecordProvenance(
        DataOrigin origin,
        RecordTimestamps? timestamps = null,
        DataSourceReference? source = null,
        AcquisitionBatchId? acquisitionBatchId = null)
    {
        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(
                nameof(origin),
                origin,
                "The data origin is not defined.");
        }

        Origin = origin;
        Timestamps = timestamps ?? RecordTimestamps.Unknown;
        Source = source;
        AcquisitionBatchId = acquisitionBatchId;
    }

    public static RecordProvenance Unknown { get; } =
        new(DataOrigin.Unknown);

    public DataOrigin Origin { get; }

    public RecordTimestamps Timestamps { get; }

    public DataSourceReference? Source { get; }

    public AcquisitionBatchId? AcquisitionBatchId { get; }
}
