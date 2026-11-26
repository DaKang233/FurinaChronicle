FurinaChronicle v7 platform app icons

Windows.ico: multi-resolution WinUI and MAUI Windows EXE icon (16-256 px).
Windows.png: v7 rounded icon, used for MAUI Windows tiles and visual assets.
Android-background.png / Android-foreground.png: full-bleed adaptive layers. Use MAUI ForegroundScale=0.58, BaseSize=128,128, Resize=True; the launcher supplies the mask.

All artwork comes from approved Ousia v7. No image-generation redraw during export.
Regenerate with: python eng/branding/Generate-AppIcons.py (requires Pillow).

Implementation references:
https://learn.microsoft.com/en-us/dotnet/maui/user-interface/images/app-icons
https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.appwindow.seticon
https://developer.android.com/develop/ui/views/launch/icon_design_adaptive
