# Copyright (c) 2026 DaKang233.
# SPDX-License-Identifier: MIT

"""Export platform icons from the approved v7 layers without redrawing the mascot.

Run from any directory with Python 3 and Pillow installed.
"""

from hashlib import sha256
from io import BytesIO
from pathlib import Path
import json

from PIL import Image, ImageChops, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "assets/branding/concepts/2026-11-22"
EXPORT = ROOT / "assets/branding/app-icons/v7"
WINUI = ROOT / "src/FurinaChronicle.Windows/Assets/AppIcon"
MAUI = ROOT / "src/FurinaChronicle.App/Resources/AppIcon"
ICO_SIZES = (16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
ANDROID_FOREGROUND_SCALE = 0.58


def write_if_changed(path: Path, data: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if not path.exists() or path.read_bytes() != data:
        path.write_bytes(data)


def save_png(image: Image.Image, path: Path) -> None:
    buffer = BytesIO()
    image.save(buffer, format="PNG", optimize=True)
    write_if_changed(path, buffer.getvalue())


def generate() -> None:
    EXPORT.mkdir(parents=True, exist_ok=True)
    WINUI.mkdir(parents=True, exist_ok=True)
    rounded = Image.open(SOURCE / "furina-ousia-app-icon-layered-v7.png").convert("RGBA")
    foreground = Image.open(SOURCE / "ousia-v7-layers/foreground-logo.png").convert("RGBA")
    background = Image.open(SOURCE / "ousia-v7-layers/background-source.png").convert("RGB")
    background = background.resize((1024, 1024), Image.Resampling.LANCZOS)

    # Windows keeps v7's existing, composited rounded tile. Android supplies full-bleed
    # layers and lets the launcher apply its own mask; never bake Windows' corners in.
    save_png(rounded, MAUI / "Windows/appicon.png")
    save_png(background, MAUI / "Android/appicon.png")
    save_png(foreground, MAUI / "Android/appiconfg.png")
    icon = BytesIO()
    rounded.save(icon, format="ICO",
                 sizes=[(size, size) for size in ICO_SIZES])
    write_if_changed(WINUI / "FurinaChronicle.ico", icon.getvalue())
    write_if_changed(MAUI / "Windows/FurinaChronicle.ico", icon.getvalue())

    # Preview the Android 108 dp layers through the launcher's central 72 dp viewport.
    adaptive_foreground = Image.new("RGBA", (1024, 1024))
    scaled_size = round(1024 * ANDROID_FOREGROUND_SCALE)
    scaled = foreground.resize((scaled_size, scaled_size), Image.Resampling.LANCZOS)
    offset = (1024 - scaled_size) // 2
    adaptive_foreground.alpha_composite(scaled, (offset, offset))
    bounds = adaptive_foreground.getchannel("A").getbbox()
    safe_min, safe_max = 1024 * (21 / 108), 1024 * (87 / 108)
    assert bounds and min(bounds[:2]) >= safe_min and max(bounds[2:]) <= safe_max
    save_png(adaptive_foreground, EXPORT / "android-adaptive-foreground-preview.png")
    full = Image.alpha_composite(background.convert("RGBA"), adaptive_foreground)
    inset = round(1024 * (18 / 108))
    visible = full.crop((inset, inset, 1024 - inset, 1024 - inset))
    mask = Image.new("L", visible.size)
    ImageDraw.Draw(mask).ellipse((0, 0, visible.width - 1, visible.height - 1), fill=255)
    android_round = visible.copy()
    android_round.putalpha(mask)
    # No non-transparent foreground pixels may fall outside the launcher's circle.
    visible_foreground = adaptive_foreground.crop((inset, inset, 1024 - inset, 1024 - inset))
    clipped = ImageChops.multiply(visible_foreground.getchannel("A"), ImageChops.invert(mask))
    assert clipped.getbbox() is None, "Android launcher mask clips the mascot"
    save_png(android_round, EXPORT / "android-round-preview.png")

    preview = Image.new("RGB", (640, 350), "#e5eaf2")
    draw = ImageDraw.Draw(preview)
    font = ImageFont.load_default(size=22)
    for index, (name, image) in enumerate((("Windows / WinUI + MAUI", rounded),
                                          ("Android / MAUI", android_round))):
        thumb = image.resize((256, 256), Image.Resampling.LANCZOS)
        preview.paste(thumb, (32 + 320 * index, 30), thumb)
        draw.text((20 + 320 * index, 307), name, fill="#172950", font=font)
    save_png(preview, EXPORT / "platform-preview.png")

    with Image.open(WINUI / "FurinaChronicle.ico") as ico:
        assert ico.ico.sizes() == {(size, size) for size in ICO_SIZES}
    with Image.open(MAUI / "Windows/appicon.png") as saved:
        assert saved.convert("RGBA").tobytes() == rounded.tobytes()
    manifest = {
        "source": str(SOURCE.relative_to(ROOT)).replace("\\", "/"),
        "version": "v7", "generation": "Deterministic export and layer composition only",
        "source_logo_sha256": sha256((SOURCE / "furina-ousia-logo-v7.png").read_bytes()).hexdigest(),
        "winui": {"ico_sizes": list(ICO_SIZES), "file": "src/FurinaChronicle.Windows/Assets/AppIcon/FurinaChronicle.ico"},
        "maui": {"windows": "Resources/AppIcon/Windows/appicon.png",
                 "windows_ico": "Resources/AppIcon/Windows/FurinaChronicle.ico",
                 "android_background": "Resources/AppIcon/Android/appicon.png",
                 "android_foreground": "Resources/AppIcon/Android/appiconfg.png",
                 "foreground_scale": ANDROID_FOREGROUND_SCALE,
                 "adaptive_foreground_bbox_1024": list(bounds)},
        "checks": ["Windows PNG equals v7 image pixels", "ICO contains every requested size",
                   "Android foreground fits 66/108 safe area and circular launcher mask"]
    }
    write_if_changed(EXPORT / "export-manifest.json", (json.dumps(manifest, indent=2) + "\n").encode("utf-8"))
    print(json.dumps(manifest, indent=2))


if __name__ == "__main__":
    generate()
