// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Services.Gacha.Importing
{
    public sealed class GachaRecordImportFormatException : Exception
    {
        public GachaRecordImportFormatException(string message) : base(message)
        {
        }

        public GachaRecordImportFormatException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
