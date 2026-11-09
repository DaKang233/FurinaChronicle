// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Services.Gacha.Importing
{
    public interface IGachaRecordReader
    {
        Task<GachaRecordReadResult> ReadAsync(Stream source, Guid gameAccountId, CancellationToken cancellationToken = default);
    }
}
