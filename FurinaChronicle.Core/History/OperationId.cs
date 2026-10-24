// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Core.History;

public readonly record struct OperationId
{
    public OperationId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                "Operation ID cannot be empty.",
                nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static OperationId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}
