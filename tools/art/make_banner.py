"""Generates the GitHub banner (assets/banner.png) from the app logo, in the app's graphite + cherry palette.

Usage (needs Pillow and Windows' Segoe UI fonts), from the repository root:
    python tools/art/make_banner.py [out_path]
The logo comes from assets/brand/sharkbaby-source.png (the same art as src/SharkBabyDrivers/Assets/logo.png).
"""
import math
import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parents[2]
OUT = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "assets" / "banner.png"
LOGO = ROOT / "assets" / "brand" / "sharkbaby-source.png"

SS = 3  # supersampling factor
W0, H0 = 1280, 640

# Themes/Dark.xaml
BG_TOP = (21, 24, 31)      # Surface #15181F
BG_BOTTOM = (12, 14, 18)   # Bg #0C0E12
ACCENT = (240, 68, 82)     # Accent #F04452
TEXT = (244, 245, 247)     # Text #F4F5F7
TEXT_SOFT = (207, 212, 222)
CHIP_BG = (28, 32, 40)     # Surface2 #1C2028


def wave(x0, x1, y, amp, thick, periods, phase=0.0):
    top, bottom = [], []
    n = 240
    for i in range(n + 1):
        x = x0 + (x1 - x0) * i / n
        yy = y + amp * math.sin(i / n * periods * 2 * math.pi + phase)
        top.append((x, yy - thick / 2))
        bottom.append((x, yy + thick / 2))
    return top + bottom[::-1]


def vertical_gradient(size, top, bottom):
    g = Image.new("RGB", (1, 256))
    for i in range(256):
        t = i / 255
        g.putpixel((0, i), tuple(int(top[k] + (bottom[k] - top[k]) * t) for k in range(3)))
    return g.resize(size).convert("RGBA")


def banner():
    W, H, u = W0 * SS, H0 * SS, SS
    img = vertical_gradient((W, H), BG_TOP, BG_BOTTOM)

    # cherry glow behind the logo
    glow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ImageDraw.Draw(glow).ellipse((40 * u, 70 * u, 540 * u, 570 * u), fill=ACCENT + (115,))
    img = Image.alpha_composite(img, glow.filter(ImageFilter.GaussianBlur(105 * u)))

    # faint keycap grid on the right, fading in towards the edge
    keys = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    kd = ImageDraw.Draw(keys)
    for row in range(10):
        for col in range(14):
            x = 660 * u + col * 50 * u - row * 12 * u
            y = 20 * u + row * 62 * u
            kd.rounded_rectangle((x, y, x + 42 * u, y + 42 * u), radius=8 * u, outline=(255, 255, 255, 255), width=2 * u)
    fade = Image.linear_gradient("L").rotate(90).resize((W, H))
    fade = fade.point(lambda v: max(0, v - 165) * 34 // 90)
    keys.putalpha(ImageChops.multiply(keys.getchannel("A"), fade))
    img = Image.alpha_composite(img, keys)

    # bottom waves
    waves = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    wd = ImageDraw.Draw(waves)
    wd.polygon(wave(0, W, H - 74 * u, 10 * u, 6 * u, 3), fill=ACCENT + (150,))
    wd.polygon(wave(0, W, H - 44 * u, 8 * u, 4 * u, 4, 1.2), fill=ACCENT + (70,))
    img = Image.alpha_composite(img, waves)

    # logo with a soft drop shadow
    size = 360 * u
    logo = Image.open(LOGO).convert("RGBA")
    logo = logo.crop(logo.getchannel("A").getbbox())
    scale = size / max(logo.size)
    logo = logo.resize((round(logo.width * scale), round(logo.height * scale)), Image.LANCZOS)
    lx, ly = 70 * u + (size - logo.width) // 2, 130 * u + (size - logo.height) // 2
    shadow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    shadow.paste((0, 0, 0, 150), (lx + 6 * u, ly + 12 * u), logo.getchannel("A"))
    img = Image.alpha_composite(img, shadow.filter(ImageFilter.GaussianBlur(14 * u)))
    img.alpha_composite(logo, (lx, ly))

    d = ImageDraw.Draw(img)
    fonts = "C:/Windows/Fonts/"
    title = ImageFont.truetype(fonts + "segoeuib.ttf", 88 * u)
    sub = ImageFont.truetype(fonts + "segoeui.ttf", 33 * u)
    chip = ImageFont.truetype(fonts + "segoeuib.ttf", 23 * u)
    x = 470 * u
    d.text((x, 196 * u), "Shark", font=title, fill=TEXT)
    sw = d.textlength("Shark", font=title)
    d.text((x + sw, 196 * u), "Baby", font=title, fill=ACCENT)
    bw = d.textlength("Baby", font=title)
    d.text((x + sw + bw, 196 * u), "Drivers", font=title, fill=TEXT)
    d.text((x + 4 * u, 312 * u), "Mouse e teclado Attack Shark num app só", font=sub, fill=TEXT_SOFT)
    cx = x + 4 * u
    for label in ["Mouse X11", "Teclado X68 PRO HE", "SignalRGB"]:
        tw = d.textlength(label, font=chip)
        d.rounded_rectangle((cx, 386 * u, cx + tw + 36 * u, 430 * u), radius=22 * u,
                            fill=CHIP_BG, outline=ACCENT, width=2 * u)
        d.text((cx + 18 * u, 391 * u), label, font=chip, fill=TEXT)
        cx += tw + 50 * u
    return img.resize((W0, H0), Image.LANCZOS).convert("RGB")


banner().save(OUT, optimize=True)
print(f"ok: {OUT}")
