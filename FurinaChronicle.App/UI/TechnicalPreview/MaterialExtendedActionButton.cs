// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.App.UI.TechnicalPreview;

internal sealed class MaterialExtendedActionButton : View
{
    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text),
        typeof(string),
        typeof(MaterialExtendedActionButton),
        string.Empty);

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public event EventHandler? Clicked;

    internal void SendClicked()
    {
        if (IsEnabled)
        {
            Clicked?.Invoke(this, EventArgs.Empty);
        }
    }
}
