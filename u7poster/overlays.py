"""Overview / diff / vignette overlays and before-crops."""
from __future__ import annotations
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

from .config import CONFIG
from .registration import Registration

Image.MAX_IMAGE_PIXELS = None
FONT_CANDIDATES = [r"C:\Windows\Fonts\arialbd.ttf", r"C:\Windows\Fonts\arial.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"]


def font(size: int):
    for f in FONT_CANDIDATES:
        if Path(f).exists():
            return ImageFont.truetype(f, size)
    return ImageFont.load_default()


def label(draw: ImageDraw.ImageDraw, xy, text, fnt, fill=(255, 255, 0), bg=(0, 0, 0)):
    x, y = xy
    bbox = draw.textbbox((x, y), text, font=fnt)
    draw.rectangle((bbox[0] - 2, bbox[1] - 1, bbox[2] + 2, bbox[3] + 1), fill=bg)
    draw.text((x, y), text, font=fnt, fill=fill)


CAT_COLOURS = {"city": (255, 40, 40), "castle": (255, 120, 0), "town": (255, 200, 0), "shrine": (0, 255, 255),
               "moongate": (80, 80, 255), "keep": (255, 150, 150), "abbey": (255, 150, 150), "cave": (160, 100, 40),
               "shipwreck": (0, 200, 120), "npc_home": (200, 200, 200)}


def overview_image(render_3072: np.ndarray, landmarks, out_path: Path):
    """1 px per tile render with landmark centroids marked and labelled by id."""
    img = Image.fromarray(render_3072).convert("RGB")
    draw = ImageDraw.Draw(img)
    fnt = font(18)
    small = font(13)
    for l in landmarks:
        col = CAT_COLOURS.get(l.category, (255, 255, 255))
        x, y = l.tile_x, l.tile_y
        if l.w and l.h and l.category not in ("npc_home",):
            draw.rectangle((x - l.w / 2, y - l.h / 2, x + l.w / 2, y + l.h / 2), outline=col, width=2)
        r = 5 if l.priority < 3 else 3
        draw.ellipse((x - r, y - r, x + r, y + r), fill=col, outline=(0, 0, 0))
        label(draw, (x + 6, y - 8), l.id, fnt if l.priority < 3 else small, fill=col)
    ImageDraw.Draw(img).text((10, 10), "overview: 1 px = 1 tile; labels = Landmarks.id", font=fnt, fill=(255, 255, 255))
    img.save(out_path)
    return img


def diff_overlay(coarse_rgb: np.ndarray, masks: dict, defects, out_path: Path):
    tint = {"water-land": (255, 0, 0), "river": (0, 120, 255), "road": (255, 160, 0), "mountain": (255, 0, 255),
            "structure": (255, 255, 0), "texture": (0, 255, 0), "text": (0, 255, 255)}
    img = coarse_rgb.astype(np.float32)
    for k, m in masks.items():
        if m is None or not m.any():
            continue
        c = np.array(tint.get(k, (255, 255, 255)), np.float32)
        a = 0.45 if k not in ("texture", "text") else 0.18
        img[m] = img[m] * (1 - a) + c * a
    im = Image.fromarray(np.clip(img, 0, 255).astype(np.uint8))
    draw = ImageDraw.Draw(im)
    fnt = font(22)
    for d in defects:
        c = tint.get(d.dclass, (255, 255, 255))
        draw.rectangle((d.cx - d.w / 2, d.cy - d.h / 2, d.cx + d.w / 2, d.cy + d.h / 2), outline=c, width=2)
        label(draw, (d.cx - d.w / 2 + 2, d.cy - d.h / 2 + 2), d.id, fnt, fill=c)
    y = 10
    for k, c in tint.items():
        draw.rectangle((10, y, 40, y + 20), fill=c)
        draw.text((48, y), k, font=fnt, fill=(0, 0, 0))
        y += 28
    im.save(out_path)
    return im


def vignette_overlay(coarse_rgb: np.ndarray, reg: Registration, landmarks, lm_cm: dict, npcs, npc_cm: dict,
                     monsters, mon_cm: dict, cfg, out_path: Path):
    """Boxes of suggested_print_cm at poster scale, labelled by id."""
    N = coarse_rgb.shape[0]
    px_per_cm = N / cfg["poster_w_cm"]
    im = Image.fromarray(coarse_rgb.copy())
    draw = ImageDraw.Draw(im, "RGBA")
    fnt = font(16)
    fnt_s = font(12)

    def box(tx, ty, cm, col, ident, f):
        x, y = reg.tile_to_px(tx, ty)
        s = cm * px_per_cm / 2
        draw.rectangle((x - s, y - s, x + s, y + s), outline=col + (255,), fill=col + (40,), width=2)
        label(draw, (x - s + 2, y - s + 2), ident, f, fill=col)

    for r in monsters:
        box(r.tile_x, r.tile_y, mon_cm.get(r.id, 1.5), (0, 200, 0), r.id, fnt_s)
    for r in npcs:
        box(r.home_x, r.home_y, npc_cm.get(r.id, 2.0), (0, 120, 255), r.id, fnt_s)
    for l in landmarks:
        box(l.tile_x, l.tile_y, lm_cm.get(l.id, 3.0), (255, 40, 40), l.id, fnt)
    draw.text((10, 10), f"vignette footprints at poster scale ({cfg['poster_w_cm']:.0f} cm wide): red=Landmarks blue=NPCs green=Monsters",
              font=fnt, fill=(0, 0, 0))
    im.save(out_path)
    return im


def write_crops(coarse_rgb: np.ndarray, render_3072: np.ndarray, reg: Registration, defects, out_dir: Path, cfg) -> dict[str, str]:
    """before/<id>.png (coarse crop) and before/<id>_ref.png (data render of the same region)."""
    out_dir.mkdir(parents=True, exist_ok=True)
    for old in out_dir.glob("F[0-9][0-9][0-9]*.png"):   # stale crops from a previous run
        old.unlink()
    N = coarse_rgb.shape[0]
    size = int(cfg["crop_px"])
    margin = int(cfg["crop_margin_px"])
    paths = {}
    coarse_img = Image.fromarray(coarse_rgb)
    ref_img = Image.fromarray(render_3072)
    fnt = font(20)
    for d in defects:
        side = max(d.w, d.h) + 2 * margin
        x0, y0 = int(d.cx - side / 2), int(d.cy - side / 2)
        x1, y1 = x0 + side, y0 + side
        crop = coarse_img.crop((x0, y0, x1, y1)).resize((size, size), Image.LANCZOS)
        dr = ImageDraw.Draw(crop)
        sc = size / side
        dr.rectangle(((d.cx - d.w / 2 - x0) * sc, (d.cy - d.h / 2 - y0) * sc, (d.cx + d.w / 2 - x0) * sc, (d.cy + d.h / 2 - y0) * sc),
                     outline=(255, 0, 0), width=2)
        label(dr, (6, 6), f"{d.id} {d.dclass}", fnt)
        p = out_dir / f"{d.id}.png"
        crop.save(p)
        # reference: same region in tile space (1 px per tile) scaled to the crop
        tx0, ty0 = reg.px_to_tile(x0, y0)
        tx1, ty1 = reg.px_to_tile(x1, y1)
        ref = ref_img.crop((int(round(float(tx0))), int(round(float(ty0))), int(round(float(tx1))), int(round(float(ty1))))).resize((size, size), Image.NEAREST)
        dr = ImageDraw.Draw(ref)
        dr.rectangle(((d.cx - d.w / 2 - x0) * sc, (d.cy - d.h / 2 - y0) * sc, (d.cx + d.w / 2 - x0) * sc, (d.cy + d.h / 2 - y0) * sc),
                     outline=(255, 0, 0), width=2)
        label(dr, (6, 6), f"{d.id} reference (data)", fnt)
        ref.save(out_dir / f"{d.id}_ref.png")
        paths[d.id] = f"before/{d.id}.png"
    return paths
