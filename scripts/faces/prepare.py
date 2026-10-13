"""The painted faces, made ready for the game: the user's portraits
(u7-new-faces/portraits for Black Gate, si-new-faces/portraits for Serpent
Isle, face_SSS_frame_FFF.png by FACES.VGA shape and frame) without the
numbers burned in under them on the sheets they were cut from, trimmed to
what is left and at most MAX_SIZE pixels, into assets/faces/<game>/ where
Rendering/PaintedFaces reads them. Pictures without transparency (Serpent
Isle's, cut with the sheet's black round them) get the black round the
frame made see-through first.

    python scripts/faces/prepare.py [--check sheet.png]

--check also writes contact sheets of every prepared face's bottom edge
(sheet_N.png), to see that no number is left and no frame was cut, and of
the faces made see-through, whole on magenta (sheet_<game>_keyed_N.png).
"""

from __future__ import annotations

import argparse
import re
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage

ROOT = Path(__file__).resolve().parents[2]
SOURCES = {"bg": ROOT / "u7-new-faces" / "portraits", "si": ROOT / "si-new-faces" / "portraits"}
OUT = ROOT / "assets" / "faces"
NAME = re.compile(r"face_(\d+)_frame_(\d+)\.png$", re.IGNORECASE)

# What counts as painted: the sheets' cut-outs keep faint fringes.
SOLID = 16
# Firmer paint, apart from the haze that joins a number to its frame.
CORE = 64
# The numbers: white with a dark outline, about 25 pixels high, in the
# picture's last rows, mostly apart from the frame's art.
LABEL_BAND = 48
LABEL_HEIGHT = 36
TEXT_ROWS = 32
GREY = 48
# Some numbers are tinted a little.
GREY_SHARE = 0.6
# The longest side kept: Serpent Isle's are 4x blow-ups of ~250 pixel paintings.
MAX_SIZE = 512
# The sheet's black (the brightest channel at most this), and how far into
# the frame its soft edge reaches (pixels, at the blow-up's size).
BLACK = 20
EDGE = 80
RIM = 6
# What is left of a number cut through at a picture's bottom (or of the one
# above it, at its top), as a share of its height.
REMNANT_BAND = 0.12
# Specks the blow-up's ringing leaves in the black.
SPECK = 64


def strip_label(rgba: np.ndarray) -> tuple[np.ndarray, bool]:
    """The picture without its number, and whether it had one."""
    found = strip_apart(rgba) or strip_touching(rgba)
    if found:
        sweep(rgba)
    return rgba, found


def sweep(rgba: np.ndarray) -> None:
    """What a number leaves low down: its faint haze and specks of its
    outline, apart from the frame."""
    h = rgba.shape[0]
    alpha = rgba[:, :, 3]
    blobs, _ = ndimage.label(alpha > SOLID, structure=np.ones((3, 3)))
    for i, box in enumerate(ndimage.find_objects(blobs), start=1):
        if box[0].start < h - LABEL_BAND:
            continue
        mine = blobs[box] == i
        if mine.sum() < 400 or alpha[box][mine].max() <= 2 * CORE:
            rgba[box][mine] = 0


def strip_apart(rgba: np.ndarray) -> bool:
    """Numbers apart from the frame (a faint haze aside): grey blobs low
    down, taken with their haze."""
    h = rgba.shape[0]
    alpha = rgba[:, :, 3]
    blobs, _ = ndimage.label(alpha > CORE, structure=np.ones((3, 3)))
    label = np.zeros(alpha.shape, dtype=bool)
    for i, box in enumerate(ndimage.find_objects(blobs), start=1):
        top, bottom = box[0].start, box[0].stop
        if top < h - LABEL_BAND or bottom - top > LABEL_HEIGHT:
            continue
        pixels = rgba[box][blobs[box] == i][:, :3].astype(np.int32)
        spread = pixels.max(axis=1) - pixels.min(axis=1)
        if np.mean(spread < GREY) >= GREY_SHARE:
            label |= blobs == i
    if not label.any():
        return False
    haze = ndimage.binary_dilation(label, iterations=3) & ((alpha <= CORE) | label)
    rgba[haze] = 0
    return True


def strip_touching(rgba: np.ndarray) -> bool:
    """A number touching the frame's art: its white digits, a line of text
    centred in the last rows, and round them the grey and black of the
    digits and their outline, leaving the frame's colours."""
    h, w = rgba.shape[:2]
    rgb = rgba[:, :, :3].astype(np.int32)
    spread = rgb.max(axis=2) - rgb.min(axis=2)
    white = (rgb.min(axis=2) > 200) & (spread < 30) & (rgba[:, :, 3] > 200)
    white[:h - TEXT_ROWS] = False
    white[:, :int(w * 0.25)] = False
    white[:, int(w * 0.75):] = False
    if white.sum() < 40:
        return False
    rows = np.where(white.any(axis=1))[0]
    cols = np.where(white.any(axis=0))[0]
    if rows.max() - rows.min() > 24 or not 30 <= cols.max() - cols.min() <= 110:
        return False
    top, left = max(0, rows.min() - 6), max(0, cols.min() - 8)
    box = (slice(top, h), slice(left, min(w, cols.max() + 9)))
    rgba[box][(spread[box] < GREY) | (rgba[box][:, :, 3] <= CORE)] = 0
    return True


def key_black(rgb: np.ndarray) -> np.ndarray:
    """The sheet's black round the frame made see-through: the black
    reaching the picture's edge, and on its rim the colour taken as painted
    over black (alpha from the brightest channel, the colour divided by it).
    Black inside the frame (hair, shadows) stays."""
    m = rgb.max(axis=2).astype(np.float32)
    blobs, _ = ndimage.label(m <= BLACK)
    edge = np.unique(np.concatenate([blobs[0], blobs[-1], blobs[:, 0], blobs[:, -1]]))
    back = np.isin(blobs, edge[edge > 0])
    alpha = np.full(m.shape, 1.0, dtype=np.float32)
    alpha[back] = 0
    rim = ndimage.binary_dilation(back, iterations=RIM) & ~back
    alpha[rim] = np.clip(m[rim] / EDGE, 0, 1)
    colour = rgb.astype(np.float32)
    colour[rim] = np.clip(colour[rim] / np.maximum(alpha[rim], 1 / 255)[:, None], 0, 255)
    colour[back] = 0
    return np.dstack([colour, alpha * 255]).round().astype(np.uint8)


def strip_remnants(rgba: np.ndarray) -> bool:
    """What a crop caught of the numbers under and above it, apart from the
    frame once the black is gone: grey blobs in the last or first rows; and
    the black's specks."""
    h = rgba.shape[0]
    alpha = rgba[:, :, 3]
    blobs, _ = ndimage.label(alpha > SOLID, structure=np.ones((3, 3)))
    found = False
    for i, box in enumerate(ndimage.find_objects(blobs), start=1):
        mine = blobs[box] == i
        if mine.sum() < SPECK:
            rgba[box][mine] = 0
            continue
        low = box[0].start >= h * (1 - REMNANT_BAND)
        high = box[0].stop <= h * REMNANT_BAND
        if not (low or high) or box[0].stop - box[0].start > h * REMNANT_BAND:
            continue
        pixels = rgba[box][mine][:, :3].astype(np.int32)
        if np.mean(pixels.max(axis=1) - pixels.min(axis=1) < GREY) >= GREY_SHARE:
            rgba[box][mine] = 0
            found = True
    return found


def shrink(rgba: np.ndarray) -> np.ndarray:
    h, w = rgba.shape[:2]
    if max(h, w) <= MAX_SIZE:
        return rgba
    scale = MAX_SIZE / max(h, w)
    image = Image.fromarray(rgba)
    # Resized with the colour weighted by its alpha, so the see-through black doesn't darken the edge.
    premultiplied = image.convert("RGBa").resize((max(1, round(w * scale)), max(1, round(h * scale))), Image.LANCZOS)
    return np.array(premultiplied.convert("RGBA"))


def trim(rgba: np.ndarray) -> np.ndarray:
    rows = np.where((rgba[:, :, 3] > SOLID).any(axis=1))[0]
    cols = np.where((rgba[:, :, 3] > SOLID).any(axis=0))[0]
    return rgba[rows.min():rows.max() + 1, cols.min():cols.max() + 1]


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", type=Path, help="contact sheet of the prepared faces' bottom edges")
    args = parser.parse_args()
    strips = []
    for game, folder in SOURCES.items():
        if not folder.is_dir():
            continue
        out = OUT / game
        out.mkdir(parents=True, exist_ok=True)
        files = sorted(p for p in folder.iterdir() if NAME.search(p.name))
        stripped = keyed = 0
        whole = []
        for path in files:
            shape, frame = (int(n) for n in NAME.search(path.name).groups())
            image = Image.open(path)
            if "A" in image.getbands():
                rgba, removed = strip_label(np.array(image.convert("RGBA")))
            else:
                rgba = key_black(np.array(image.convert("RGB")))
                removed = strip_remnants(rgba)
                keyed += 1
            rgba = shrink(trim(rgba))
            stripped += removed
            Image.fromarray(rgba).save(out / f"face_{shape:03d}_frame_{frame:03d}.png", optimize=True)
            if args.check:
                strips.append((f"{shape}.{frame}", rgba[-60:]))
                if "A" not in image.getbands():
                    whole.append(rgba)
        print(f"{game}: {len(files)} faces into {out}, {keyed} made see-through, a number taken from {stripped}")
        if args.check and whole:
            write_gallery(whole, args.check.with_name(f"{args.check.stem}_{game}_keyed{args.check.suffix}"))
    if args.check and strips:
        write_check(strips, args.check)


def write_check(strips: list[tuple[str, np.ndarray]], path: Path) -> None:
    """The bottom edges on magenta, four to a row, in sheets of 32
    (path_0.png, path_1.png, ...)."""
    per_row, per_sheet, cell_w, cell_h = 4, 32, 420, 64
    for n in range(0, len(strips), per_sheet):
        part = strips[n:n + per_sheet]
        rows = (len(part) + per_row - 1) // per_row
        sheet = Image.new("RGBA", (per_row * cell_w, rows * cell_h), (255, 0, 255, 255))
        for i, (_, strip) in enumerate(part):
            x, y = (i % per_row) * cell_w, (i // per_row) * cell_h
            image = Image.fromarray(strip)
            image.thumbnail((cell_w - 4, cell_h - 4))
            sheet.alpha_composite(image, (x + 2, y + 2))
        sheet.save(path.with_name(f"{path.stem}_{n // per_sheet}{path.suffix}"))


def write_gallery(faces: list[np.ndarray], path: Path) -> None:
    """Whole faces on magenta, eight to a row, in sheets of 40 (path_0.png, ...)."""
    per_row, per_sheet, cell = 8, 40, 200
    for n in range(0, len(faces), per_sheet):
        part = faces[n:n + per_sheet]
        rows = (len(part) + per_row - 1) // per_row
        sheet = Image.new("RGBA", (per_row * cell, rows * cell), (255, 0, 255, 255))
        for i, face in enumerate(part):
            image = Image.fromarray(face)
            image.thumbnail((cell - 4, cell - 4))
            sheet.alpha_composite(image, ((i % per_row) * cell + 2, (i // per_row) * cell + 2))
        sheet.save(path.with_name(f"{path.stem}_{n // per_sheet}{path.suffix}"))


if __name__ == "__main__":
    main()
