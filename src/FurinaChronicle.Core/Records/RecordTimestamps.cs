// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Core.Records;

public sealed record RecordTimestamps(
    DateTimeOffset? ObservedAt = null,
    DateTimeOffset? FetchedAt = null,
    DateTimeOffset? ImportedAt = null)
{
    public static RecordTimestamps Unknown { get; } = new();
}
