// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Core.Records;

/// <summary>
/// Describes completeness relative to a scope declared by the consuming
/// domain type. The enum alone never defines that scope.
/// </summary>
/// <remarks>
/// A lower-level <see cref="Complete"/> value must not be used to infer that
/// its containing observation, data set, account history, or archive is also
/// complete.
/// </remarks>
public enum DataCompleteness
{
    Unknown = 0,
    Partial = 1,
    Complete = 2,
}
