// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.App.UI.Gacha;

public sealed class GachaAppearanceSettings(IPreferences preferences)
{
    internal const string ClassicBackgroundResource = "GachaUseClassicItemBackground";
    private const string ClassicBackgroundKey = "gacha.appearance.useClassicItemBackground";

    public bool UseClassicBackground => preferences.Get(ClassicBackgroundKey, false);

    public void ApplyTo(ResourceDictionary resources) =>
        resources[ClassicBackgroundResource] = UseClassicBackground;

    public void SetClassicBackground(bool useClassicBackground, ResourceDictionary resources)
    {
        preferences.Set(ClassicBackgroundKey, useClassicBackground);
        resources[ClassicBackgroundResource] = useClassicBackground;
    }
}
