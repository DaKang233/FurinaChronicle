// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Wishes;
using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Services.Wishes.Importing
{
    public sealed record WishReadResult(IReadOnlyList<WishRecord> Records, IReadOnlyList<WishImportError> Errors);
}
