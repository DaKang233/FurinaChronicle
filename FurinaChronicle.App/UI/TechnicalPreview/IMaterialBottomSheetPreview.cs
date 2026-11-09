// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.App.UI.TechnicalPreview;

public interface IMaterialBottomSheetPreview
{
    Task ShowAsync(CancellationToken cancellationToken = default);
}
