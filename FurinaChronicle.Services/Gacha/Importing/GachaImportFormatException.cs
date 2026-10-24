// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Services.Gacha.Importing;

public sealed class GachaImportFormatException : Exception
{
    public GachaImportFormatException(string message)
        : base(message)
    {
    }

    public GachaImportFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
