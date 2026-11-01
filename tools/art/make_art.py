"""Generates the SharkBabyDrivers icon (PNG + multi-size ICO) and the GitHub banner.

Usage (needs Pillow and Windows' Segoe UI fonts):
    python tools/art/make_art.py <out_dir>
Then copy: icon.ico → src/SharkBabyDrivers/Assets/icon.ico, icon-256.png → src/SharkBabyDrivers/Assets/logo.png,
banner.png and icon.png → assets/.
"""
import math
import sys
from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

OUT = sys.argv[1]
SS = 4  # supersampling factor

RED_TOP = (255, 77, 86)
RED_BOTTOM = (150, 18, 30)
WHITE = (255, 255, 255)


def bezier(p0, p1, p2, p3, n=80):
    pts = []
    for i in range(n + 1):
        t = i / n
        a = (1 - t) ** 3
        b = 3 * (1 - t) ** 2 * t
        c = 3 * (1 - t) * t ** 2
        d = t ** 3
        pts.append((a * p0[0] + b * p1[0] + c * p2[0] + d * p3[0], a * p0[1] + b * p1[1] + c * p2[1] + d * p3[1]))
    return pts


def fin(cx, base, h, w):
    """Shark fin: convex leading edge, hooked (concave) trailing edge."""
    left = (cx - w * 0.55, base)
    tip = (cx + w * 0.18, base - h)
    right = (cx + w * 0.45, base)
    lead = bezier(left, (cx - w * 0.35, base - h * 0.45), (cx - w * 0.05, base - h * 0.95), tip)
    trail = bezier(tip, (cx + w * 0.05, base - h * 0.55), (cx + w * 0.12, base - h * 0.15), right)
    return lead + trail[1:]


def wave(x0, x1, y, amp, thick, periods):
    top, bottom = [], []
    n = 200
    for i in range(n + 1):
        x = x0 + (x1 - x0) * i / n
        yy = y + amp * math.sin(i / n * periods * 2 * math.pi)
        top.append((x, yy - thick / 2))
        bottom.append((x, yy + thick / 2))
    return top + bottom[::-1]


def gradient(size, top, bottom):
    g = Image.new("RGB", (1, 256))
    for i in range(256):
        t = i / 255
        g.putpixel((0, i), tuple(int(top[k] + (bottom[k] - top[k]) * t) for k in range(3)))
    return g.resize(size)


def icon(size=1024):
    S = size * SS
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    # rounded square with red gradient
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, S - 1, S - 1), radius=int(S * 0.23), fill=255)
    bg = gradient((S, S), RED_TOP, RED_BOTTOM).convert("RGBA")
    img.paste(bg, (0, 0), mask)
    # soft highlight
    glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(glow).ellipse((-S * 0.3, -S * 0.55, S * 0.9, S * 0.45), fill=(255, 255, 255, 38))
    glow = glow.filter(ImageFilter.GaussianBlur(S * 0.04))
    img = Image.alpha_composite(img, Image.composite(glow, Image.new("RGBA", (S, S)), mask))

    d = ImageDraw.Draw(img)
    u = S / 1024
    base = 690 * u
    # water: the fins rise out of a wave band
    shadow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    sd = ImageDraw.Draw(shadow)
    sd.polygon(fin(430 * u, base + 18 * u, 430 * u, 420 * u), fill=(60, 0, 8, 110))
    sd.polygon(fin(740 * u, base + 18 * u, 210 * u, 210 * u), fill=(60, 0, 8, 110))
    shadow = shadow.filter(ImageFilter.GaussianBlur(14 * u))
    img = Image.alpha_composite(img, Image.composite(shadow, Image.new("RGBA", (S, S)), mask))
    d = ImageDraw.Draw(img)
    d.polygon(fin(430 * u, base, 430 * u, 420 * u), fill=WHITE)          # the shark
    d.polygon(fin(740 * u, base, 210 * u, 210 * u), fill=WHITE)          # the baby shark
    d.polygon(wave(130 * u, 894 * u, base + 40 * u, 16 * u, 56 * u, 2.5), fill=WHITE)
    d.polygon(wave(210 * u, 814 * u, base + 140 * u, 12 * u, 34 * u, 2.0), fill=(255, 255, 255, 150))
    # keep everything inside the rounded square
    img.putalpha(Image.composite(img.getchannel("A"), Image.new("L", (S, S), 0), mask))
    return img.resize((size, size), Image.LANCZOS)


def banner(w=1280, h=640):
    W, H = w * SS, h * SS
    u = SS
    img = Image.new("RGBA", (W, H), (16, 12, 13, 255))
    # red glow behind the icon
    glow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ImageDraw.Draw(glow).ellipse((20 * u, 60 * u, 560 * u, 600 * u), fill=(232, 50, 60, 120))
    glow = glow.filter(ImageFilter.GaussianBlur(110 * u))
    img = Image.alpha_composite(img, glow)
    # faint grid of "keycaps" on the right, fading out towards the text (drawn on its own layer to blend)
    keys = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    kd = ImageDraw.Draw(keys)
    for row in range(10):
        for col in range(14):
            x = 660 * u + col * 50 * u - row * 12 * u
            y = 20 * u + row * 62 * u
            kd.rounded_rectangle((x, y, x + 42 * u, y + 42 * u), radius=8 * u, outline=(255, 255, 255, 255), width=2 * u)
    # horizontal fade: 0 left of x≈60 %, rising to ~9 % opacity at the right edge
    fade = Image.linear_gradient("L").rotate(90).resize((W, H))
    fade = fade.point(lambda v: max(0, v - 165) * 34 // 90)
    keys.putalpha(ImageChops.multiply(keys.getchannel("A"), fade))
    img = Image.alpha_composite(img, keys)
    # bottom waves
    waves = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    wd = ImageDraw.Draw(waves)
    wd.polygon(wave(0, W, H - 74 * u, 10 * u, 6 * u, 3), fill=(232, 50, 60, 150))
    wd.polygon(wave(0, W, H - 44 * u, 8 * u, 4 * u, 4), fill=(232, 50, 60, 70))
    img = Image.alpha_composite(img, waves)
    d = ImageDraw.Draw(img)

    ic = icon(280).resize((280 * u, 280 * u), Image.LANCZOS)
    img.alpha_composite(ic, (90 * u, 170 * u))

    fonts = "C:/Windows/Fonts/"
    title = ImageFont.truetype(fonts + "segoeuib.ttf", 92 * u)
    sub = ImageFont.truetype(fonts + "segoeui.ttf", 34 * u)
    chip = ImageFont.truetype(fonts + "segoeuib.ttf", 24 * u)
    x = 410 * u
    d.text((x, 190 * u), "Shark", font=title, fill=WHITE)
    sw = d.textlength("Shark", font=title)
    d.text((x + sw, 190 * u), "Baby", font=title, fill=(255, 77, 86))
    bw = d.textlength("Baby", font=title)
    d.text((x + sw + bw, 190 * u), "Drivers", font=title, fill=WHITE)
    d.text((x + 4 * u, 310 * u), "Mouse e teclado Attack Shark num app só", font=sub, fill=(214, 200, 204))
    cx = x + 4 * u
    for label in ["Mouse X11", "Teclado X68 PRO HE", "Backup automático"]:
        tw = d.textlength(label, font=chip)
        d.rounded_rectangle((cx, 385 * u, cx + tw + 36 * u, 431 * u), radius=23 * u,
                            fill=(37, 29, 32), outline=(232, 50, 60), width=2 * u)
        d.text((cx + 18 * u, 391 * u), label, font=chip, fill=(242, 236, 237))
        cx += tw + 52 * u
    return img.resize((w, h), Image.LANCZOS).convert("RGB")


big = icon(1024)
big.save(f"{OUT}/icon.png")
icon(256).save(f"{OUT}/icon-256.png")
# Multi-size ICO; small sizes rendered separately so they stay crisp.
sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
frames = [icon(s) for s in sizes]
frames[-1].save(f"{OUT}/icon.ico", format="ICO", sizes=[(s, s) for s in sizes], append_images=frames[:-1])
banner().save(f"{OUT}/banner.png", optimize=True)
print("ok")
