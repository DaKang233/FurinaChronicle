// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using Google.Android.Material.FloatingActionButton;
using Microsoft.Maui.Handlers;

namespace FurinaChronicle.App.UI.TechnicalPreview;

internal sealed class AndroidMaterialExtendedActionButtonHandler
    : ViewHandler<MaterialExtendedActionButton, ExtendedFloatingActionButton>
{
    private static readonly IPropertyMapper<
        MaterialExtendedActionButton,
        AndroidMaterialExtendedActionButtonHandler> Mapper =
        new PropertyMapper<
            MaterialExtendedActionButton,
            AndroidMaterialExtendedActionButtonHandler>(ViewMapper)
        {
            [nameof(MaterialExtendedActionButton.Text)] = MapText
        };

    public AndroidMaterialExtendedActionButtonHandler()
        : base(Mapper)
    {
    }

    protected override ExtendedFloatingActionButton CreatePlatformView()
    {
        return new ExtendedFloatingActionButton(Context)
        {
            Text = VirtualView.Text,
            ContentDescription =
                SemanticProperties.GetDescription(VirtualView) ??
                VirtualView.Text
        };
    }

    protected override void ConnectHandler(ExtendedFloatingActionButton platformView)
    {
        base.ConnectHandler(platformView);
        platformView.Click += OnPlatformClick;
    }

    protected override void DisconnectHandler(ExtendedFloatingActionButton platformView)
    {
        platformView.Click -= OnPlatformClick;
        base.DisconnectHandler(platformView);
    }

    private static void MapText(
        AndroidMaterialExtendedActionButtonHandler handler,
        MaterialExtendedActionButton view)
    {
        handler.PlatformView.Text = view.Text;
        handler.PlatformView.ContentDescription =
            SemanticProperties.GetDescription(view) ?? view.Text;
    }

    private void OnPlatformClick(object? sender, EventArgs e)
    {
        VirtualView.SendClicked();
    }
}
