// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Services.Gacha.Importing
{
    public sealed record GachaRecordImportResult(int TotalCount, int ImportedCount, int DuplicateCount, int InvalidCount);
}
