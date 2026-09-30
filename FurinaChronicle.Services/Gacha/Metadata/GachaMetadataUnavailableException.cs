// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Services.Gacha.Metadata;

public sealed class GachaMetadataUnavailableException
    : Exception
{
    public GachaMetadataUnavailableException(string message, Exception innerException) : base(message, innerException)
    {
    }
}