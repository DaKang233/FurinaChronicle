// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Services.Gacha.Importing
{
    public sealed record GachaRecordReadResult(IReadOnlyList<GachaRecord> Records, IReadOnlyList<GachaRecordImportError> Errors);
}
