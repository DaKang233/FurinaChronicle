// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.App.UI.TechnicalPreview;

internal sealed class UnsupportedMaterialBottomSheetPreview
    : IMaterialBottomSheetPreview
{
    public Task ShowAsync(CancellationToken cancellationToken = default)
    {
        throw new PlatformNotSupportedException(
            "The Material bottom-sheet preview is available only on Android.");
    }
}
