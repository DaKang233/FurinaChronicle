// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Core.Records;

public readonly record struct AcquisitionBatchId
{
    public AcquisitionBatchId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                "Acquisition batch ID cannot be empty.",
                nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static AcquisitionBatchId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}
