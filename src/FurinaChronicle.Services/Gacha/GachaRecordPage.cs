// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;

namespace FurinaChronicle.Services.Gacha;

public sealed record GachaRecordPage(
    IReadOnlyList<GachaRecord> Records,
    int TotalCount,
    int PageNumber,
    int PageSize)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling((double)TotalCount / PageSize));
}
