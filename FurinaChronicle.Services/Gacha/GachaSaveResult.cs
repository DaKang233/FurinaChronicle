// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Services.Gacha
{
    public enum GachaRecordConflictPolicy
    {
        PreserveExisting = 0,
        ReplaceExisting = 1
    }

    public sealed record GachaSaveResult(
        int InsertedCount,
        int DuplicateCount,
        int UpdatedCount = 0);
}
