"""Turn a model's upscaled frames, or paintings, back into palette art: a pack the game reads.

    scripts/upscale/.venv/Scripts/python scripts/upscale/pack.py --model nxbrz [--file shapes|sprites|gumps|faces|paperdolls|chunks]
        [--margin M] [--paintings DIR]

For each frame generate.py made (work/<model>/<file>/<shape>_<frame>.png):
  * its shape: the original opacity upscaled smoothly (bicubic), kept as
    straight alpha, so silhouettes lose their stairs (the game's classic
    painters count a pixel from half opaque, its true colour blends the
    rest); a painted drawing with a transparent ground (sets.py) keeps its
    own alpha instead; a piece of a floor, wall or roof keeps its exact
    stairs, so two pieces that meet still meet;
  * colour-cycling pixels (water, flames) become the entry of their own
    cycling range nearest the painting's colour, texel by texel, so the sea's
    glints and the fires cycle at the painting's resolution in Exult's
    rotation; a translucent shape's see-through pixels keep their original
    index, blown up, so the translucency tables still apply to them;
  * every other pixel the model's colour as two palette indices and how far
    between them it lies (the world shader mixes the two), the first the
    nearest single colour, so the mix can be turned off;
  * and the model's colour itself, for true colour (the game's
    `art truecolour on`): the nearest index is its anchor, through which
    the palettes and translucency tables change it;
  * optional maps beside the painting, carried for the lighting milestones:
    <shape>_<frame>_normal.png (RGB, the normal's x and y kept),
    _height.png and _emissive.png (grey).

A painting is the frame's size times an integer scale (its own: a pack may
mix 2x, 4x and 8x frames), plus --margin M pixels on every side for a halo
or shadow that overhangs the frame (the hotspot moves in by M). Any other
size is reported and left out. With --paintings DIR, pictures named
<shape>_<frame>.png or face_<shape>_frame_<frame>.png are fitted to the
frame by height at whatever scale that gives (the painted faces).

The pack (assets/hires/<model>/<file>.hrp) holds each frame as HiresPack.cs
reads it (version 3, docs/RENDERER.md 5.4): header "U7HR", version, the
usual scale, count; entries (shape, frame, offset, length); each frame
zlib-compressed: its scale times 16, extents (xleft, yabove, xright, ybelow
at that scale), flags (1 RLE, 2 mixed, 4 true colour, 8 alpha, 16 normal,
32 height, 64 emissive), scan count, scans (x, y, length from the hotspot;
they may overhang the extents), the indices, the mix plane (second index,
0-255 towards it), the colour plane (RGBA: the colour, A 255 where it
shows, 0 for a kept pixel, which is its index alone), then a byte a pixel
of straight alpha, two of normal, one of height and one of emissive, each
only where flagged. The game still reads versions 1 and 2.
"""

from __future__ import annotations

import argparse
import os
import re
import struct
import zlib
from dataclasses import dataclass
from pathlib import Path

import numpy as np
from PIL import Image

import u7art
from generate import BUILT, FILES, SCALE, fills_frame, is_translucent, work_dir

MAGIC = b"U7HR"
VERSION = 3
# Palette entries the model's colours may become: not the cycling ones (they
# would start to cycle), not the translucent ones, not 0xFF (transparent to Exult's reflect).
ALLOWED = u7art.CYCLING_FIRST
LUT_BITS = 6
NEAREST = 8
# Exult rotatecolours' ranges (WorldPalette.Ranges): first index and count.
RANGES = [(0xFC, 3), (0xF8, 4), (0xF4, 4), (0xF0, 4), (0xE8, 8), (0xE0, 8)]
PAINTING = re.compile(r"^(?:face_)?(\d+)_(?:frame_)?(\d+)\.png$", re.IGNORECASE)


def srgb_to_lab(rgb: np.ndarray) -> np.ndarray:
    c = rgb / 255.0
    lin = np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
    m = np.array([[0.4124, 0.3576, 0.1805], [0.2126, 0.7152, 0.0722], [0.0193, 0.1192, 0.9505]])
    xyz = lin @ m.T / np.array([0.95047, 1.0, 1.08883])
    f = np.where(xyz > 216 / 24389, np.cbrt(xyz), (24389 / 27 * xyz + 16) / 116)
    return np.stack([116 * f[..., 1] - 16, 500 * (f[..., 0] - f[..., 1]), 200 * (f[..., 1] - f[..., 2])], axis=-1)


def build_lut() -> tuple[np.ndarray, np.ndarray]:
    """For each cell of a 64^3 RGB grid: the nearest allowed palette colour
    (CIELAB) and the second colour whose mix with it comes nearest."""
    path = u7art.HIRES / "lut.npz"
    if path.exists():
        z = np.load(path)
        return z["first"], z["second"]
    pal = u7art.day_palette()[:ALLOWED].astype(np.float64)
    pal_lab = srgb_to_lab(pal)
    n = 1 << LUT_BITS
    step = 256 // n
    axis = np.arange(n) * step + step / 2 - 0.5
    grid = np.stack(np.meshgrid(axis, axis, axis, indexing="ij"), axis=-1).reshape(-1, 3)
    lab = srgb_to_lab(grid)
    first = np.empty(len(grid), np.uint8)
    second = np.empty(len(grid), np.uint8)
    for lo in range(0, len(grid), 16384):
        g, gl = grid[lo:lo + 16384], lab[lo:lo + 16384]
        d = ((gl[:, None, :] - pal_lab[None]) ** 2).sum(-1)
        near = np.argsort(d, axis=1)[:, :NEAREST]
        i1 = near[:, 0]
        p1 = pal[i1]
        best = np.full(len(g), np.inf)
        best_i2 = i1.copy()
        for k in range(1, NEAREST):
            i2 = near[:, k]
            p2 = pal[i2]
            seg = p2 - p1
            t = np.clip(((g - p1) * seg).sum(-1) / np.maximum((seg * seg).sum(-1), 1e-9), 0, 1)
            err = ((srgb_to_lab(p1 + t[:, None] * seg) - gl) ** 2).sum(-1)
            better = err < best
            best[better] = err[better]
            best_i2[better] = i2[better]
        first[lo:lo + 16384] = i1
        second[lo:lo + 16384] = best_i2
    np.savez(path, first=first, second=second)
    return first, second


def quantize(rgb: np.ndarray, lut: tuple[np.ndarray, np.ndarray]) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    """(h, w, 3) uint8 -> first index, second index, mix (0-255 towards the second)."""
    first, second = lut
    q = rgb.astype(np.int32) >> (8 - LUT_BITS)
    cell = (q[..., 0] << (2 * LUT_BITS)) | (q[..., 1] << LUT_BITS) | q[..., 2]
    i1, i2 = first[cell], second[cell]
    pal = u7art.day_palette().astype(np.float32)
    p1, p2 = pal[i1], pal[i2]
    seg = p2 - p1
    t = ((rgb.astype(np.float32) - p1) * seg).sum(-1) / np.maximum((seg * seg).sum(-1), 1e-6)
    t = np.clip(t, 0, 1)
    mix = np.round(t * 255).astype(np.uint8)
    return i1, i2, mix


def cycling_in_range(index: np.ndarray, rgb: np.ndarray) -> np.ndarray:
    """For the pixels whose original index cycles: the entry of that index's
    range nearest (CIELAB) the painting's colour there, so a painted sea
    cycles at its own resolution in the palette's rotation. Other pixels
    keep their index."""
    out = index.copy()
    lab_pal = srgb_to_lab(u7art.day_palette().astype(np.float64))
    lab = None
    for first, count in RANGES:
        inside = (index >= first) & (index < first + count)
        if not inside.any():
            continue
        if lab is None:
            lab = srgb_to_lab(rgb.astype(np.float64))
        candidates = lab_pal[first:first + count]
        d = ((lab[inside][:, None, :] - candidates[None]) ** 2).sum(-1)
        out[inside] = (first + d.argmin(axis=1)).astype(np.uint8)
    return out


def upscale_mask(frame: u7art.Frame, scale: int = SCALE) -> np.ndarray:
    """The original opacity at the scale as straight alpha (0-255): bicubic,
    so silhouettes lose their stairs; a piece of a floor, wall or roof keeps
    its exact outline (its stairs meet the next piece's, and a smooth edge
    would let what is under show)."""
    opaque = frame.opaque
    if not frame.rle:
        return np.full((opaque.shape[0] * scale, opaque.shape[1] * scale), 255, np.uint8)
    if fills_frame(frame, BUILT):
        return np.repeat(np.repeat(opaque, scale, 0), scale, 1).astype(np.uint8) * 255
    # Edge-padded, so a shape cut straight by its frame stays straight there.
    pad = np.pad(opaque.astype(np.float32), 2, mode="edge")
    h, w = pad.shape
    big = np.asarray(Image.fromarray(pad, "F").resize((w * scale, h * scale), Image.BICUBIC))
    o = 2 * scale
    big = big[o:o + opaque.shape[0] * scale, o:o + opaque.shape[1] * scale]
    return np.round(np.clip(big, 0, 1) * 255).astype(np.uint8)


def own_outline(frame: u7art.Frame, alpha: np.ndarray | None) -> np.ndarray | None:
    """A painted drawing's own alpha, for a piece that stands alone; None
    where the original's outline holds: no alpha, terrain, and pieces of
    floors, walls and roofs, whose outlines must meet the next one's."""
    if alpha is None or not frame.rle or fills_frame(frame, BUILT) or alpha.min() >= 128:
        return None
    return alpha


@dataclass
class Maps:
    """Optional planes beside a painting, each the painting's size or None."""
    normal: np.ndarray | None = None  # (h, w, 2) uint8
    height: np.ndarray | None = None  # (h, w) uint8
    emissive: np.ndarray | None = None  # (h, w) uint8


def load_maps(png: Path) -> Maps:
    """The maps beside a painting: <stem>_normal.png, _height.png, _emissive.png."""
    maps = Maps()
    for name in ("normal", "height", "emissive"):
        path = png.with_name(f"{png.stem}_{name}.png")
        if not path.exists():
            continue
        with Image.open(path) as img:
            if name == "normal":
                maps.normal = np.asarray(img.convert("RGB"))[..., :2].copy()
            else:
                setattr(maps, name, np.asarray(img.convert("L")).copy())
    return maps


def encode(frame: u7art.Frame, ai: np.ndarray, translucent: bool, lut, alpha: np.ndarray | None = None, scale: int = SCALE,
           margin: int = 0, maps: Maps | None = None) -> bytes:
    """A painting of the frame at an integer scale (plus a margin round it) as a pack frame."""
    near = np.repeat(np.repeat(frame.index, scale, 0), scale, 1)
    near_opaque = np.repeat(np.repeat(frame.opaque, scale, 0), scale, 1)
    keep = np.repeat(np.repeat(u7art.special_mask(frame, translucent), scale, 0), scale, 1)
    if margin:
        near = np.pad(near, margin)
        near_opaque = np.pad(near_opaque, margin)
        keep = np.pad(keep, margin)
    own = own_outline(frame, alpha)
    if own is None:
        mask = upscale_mask(frame, scale)
        if margin:
            mask = np.pad(mask, margin)
            if alpha is not None:
                # The margin's pixels are the painting's own (a halo, a shadow); inside, the original's silhouette holds.
                outside = np.ones(mask.shape, bool)
                outside[margin:-margin, margin:-margin] = False
                mask = np.where(outside, alpha, mask)
    else:
        mask = own
    keep &= near_opaque
    i1, i2, mix = quantize(ai, lut)
    i1 = np.where(keep, cycling_in_range(near, ai), i1)
    i2 = np.where(keep, i1, i2)
    mix = np.where(keep, 0, mix)
    colour = np.concatenate([ai[..., :3], np.full(ai.shape[:2] + (1,), 255, np.uint8)], -1)
    colour[keep] = 0
    xleft, yabove = frame.xleft * scale, frame.yabove * scale
    xright, ybelow = frame.xright * scale + scale - 1, frame.ybelow * scale + scale - 1
    return encode_planes(scale * 16, (xleft, yabove, xright, ybelow), frame.rle, mask, i1, i2, mix, colour, origin=(xleft + margin, yabove + margin),
                         maps=maps)


def encode_planes(scale16: int, extents: tuple[int, int, int, int], rle: bool, alpha: np.ndarray, index: np.ndarray,
                  second: np.ndarray | None, mix: np.ndarray | None, colour: np.ndarray | None, origin: tuple[int, int] | None = None,
                  maps: Maps | None = None, soft: bool = True) -> bytes:
    """A pack frame from dense planes over the painting's rectangle: the
    alpha (scans cover every pixel above 0), the indices, the mix (second
    index and how far) and the RGBA colour, each None where the frame has
    none; origin is the hotspot's pixel in the planes (the extents' corner
    by default); with soft off, the alpha plane is left out (binary coverage)."""
    xleft, yabove, xright, ybelow = extents
    ox, oy = origin if origin is not None else (xleft, yabove)
    maps = maps or Maps()
    scans, pixels, seconds, colours, alphas, normals, heights, emissives = [], [], [], [], [], [], [], []
    for row in range(alpha.shape[0]):
        line = alpha[row] > 0
        cols = np.flatnonzero(np.diff(np.concatenate(([0], line.astype(np.int8), [0]))))
        for start, end in zip(cols[::2], cols[1::2]):
            scans.append(struct.pack("<hhh", start - ox, row - oy, end - start))
            pixels.append(index[row, start:end].tobytes())
            if second is not None:
                seconds.append(np.stack([second[row, start:end], mix[row, start:end]], -1).tobytes())
            if colour is not None:
                colours.append(colour[row, start:end].tobytes())
            alphas.append(alpha[row, start:end].tobytes())
            if maps.normal is not None:
                normals.append(maps.normal[row, start:end].tobytes())
            if maps.height is not None:
                heights.append(maps.height[row, start:end].tobytes())
            if maps.emissive is not None:
                emissives.append(maps.emissive[row, start:end].tobytes())
    flags = (1 if rle else 0) | (2 if second is not None else 0) | (4 if colour is not None else 0) | (8 if soft else 0)
    flags |= (16 if maps.normal is not None else 0) | (32 if maps.height is not None else 0) | (64 if maps.emissive is not None else 0)
    body = struct.pack("<HhhhhBI", scale16, xleft, yabove, xright, ybelow, flags, len(scans))
    body += b"".join(scans) + b"".join(pixels) + b"".join(seconds) + b"".join(colours)
    if soft:
        body += b"".join(alphas)
    body += b"".join(normals) + b"".join(heights) + b"".join(emissives)
    return zlib.compress(body, 6)


def encode_painting(frame: u7art.Frame, rgba: np.ndarray, lut, translucent: bool = False, maps: Maps | None = None) -> bytes:
    """A picture of any size fitted to the frame by height (the painted
    faces): its own scale, straight alpha, true colour; kept pixels aren't
    told apart (the picture is a new painting of the subject)."""
    ph, pw = rgba.shape[:2]
    scale16 = max(1, round(16 * ph / max(frame.height, 1)))
    ai = rgba[..., :3]
    i1, i2, mix = quantize(ai, lut)
    colour = np.concatenate([ai, np.full((ph, pw, 1), 255, np.uint8)], -1)
    xleft = round(frame.xleft * scale16 / 16)
    yabove = round(frame.yabove * scale16 / 16)
    extents = (xleft, yabove, pw - xleft - 1, ph - yabove - 1)
    return encode_planes(scale16, extents, frame.rle, rgba[..., 3], i1, i2, mix, colour, maps=maps)


def chunk_entries(model: str, lut) -> tuple[list[tuple[int, int]], list[bytes]]:
    """The painted chunk templates (chunks.py: work/<model>/chunks/TTTT.png),
    each as frame 0 of its template: a 128 x 128 terrain frame at 4x, its
    colour cycling pixels quantised within their ranges. A square painting
    of another size is scaled to fit."""
    from chunks import SIDE, template_frame

    entries, blobs = [], []
    templates = len(u7art.chunk_templates()[0])
    for png in sorted(work_dir(model, "chunks").glob("*.png")):
        if not png.stem.isdigit() or int(png.stem) >= templates:
            continue
        with Image.open(png) as img:
            if img.width != img.height:
                print(f"  {png.name}: {img.width}x{img.height}, not square: left out", flush=True)
                continue
            img = img.convert("RGB")
            if img.width != SIDE * SCALE:
                img = img.resize((SIDE * SCALE, SIDE * SCALE), Image.LANCZOS)
            ai = np.asarray(img)
        t = int(png.stem)
        blobs.append(encode(template_frame(t), ai, False, lut))
        entries.append((t, 0))
    return entries, blobs


def painting_scale(frame: u7art.Frame, size: tuple[int, int], margin: int) -> int | None:
    """The integer scale a painting of (h, w) is at, with the margin round it; None if it is no frame's size."""
    h, w = size
    for scale in range(1, 17):
        if (frame.height * scale + 2 * margin, frame.width * scale + 2 * margin) == (h, w):
            return scale
    return None


def build(model: str, file: str, margin: int = 0, paintings: Path | None = None) -> Path | None:
    lut = build_lut()
    entries = []
    blobs = []
    if file == "chunks":
        entries, blobs = chunk_entries(model, lut)
        return write_pack(model, file, entries, blobs)
    vga = u7art.game_file(file)
    if vga is None:
        print(f"{file}: not there (Serpent Isle isn't installed)", flush=True)
        return None
    src = paintings if paintings is not None else work_dir(model, file)
    translucent = u7art.translucent_shapes()
    wrong = []
    for png in sorted(src.glob("*.png")):
        m = PAINTING.match(png.name)
        if m is None:
            continue
        shape, frame_no = int(m.group(1)), int(m.group(2))
        frame = vga.frame(shape, frame_no)
        if frame is None:
            wrong.append(f"  {png.name}: no such frame")
            continue
        with Image.open(png) as img:
            alpha = np.asarray(img.getchannel("A")).copy() if "A" in img.getbands() else None
            ai = np.asarray(img.convert("RGB"))
        maps = load_maps(png)
        if paintings is not None:
            rgba = np.concatenate([ai, (alpha if alpha is not None else np.full(ai.shape[:2], 255, np.uint8))[..., None]], -1)
            blobs.append(encode_painting(frame, rgba, lut, is_translucent(file, shape, translucent), maps))
            entries.append((shape, frame_no))
            continue
        scale = painting_scale(frame, ai.shape[:2], margin)
        if scale is None:
            want = f"{frame.width * SCALE + 2 * margin}x{frame.height * SCALE + 2 * margin}"
            wrong.append(f"  {png.name}: {ai.shape[1]}x{ai.shape[0]}, wants {want} at 4x (or another whole scale)")
            continue
        blobs.append(encode(frame, ai, is_translucent(file, shape, translucent), lut, alpha, scale, margin, maps))
        entries.append((shape, frame_no))
    if wrong:
        print(f"{file}: {len(wrong)} paintings left out:", flush=True)
        for line in wrong[:40]:
            print(line, flush=True)
        if len(wrong) > 40:
            print(f"  ... and {len(wrong) - 40} more", flush=True)
    return write_pack(model, file, entries, blobs)


def write_pack(model: str, file: str, entries: list[tuple[int, int]], blobs: list[bytes], scale: int = SCALE) -> Path:
    out = u7art.HIRES / model / f"{file}.hrp"
    out.parent.mkdir(parents=True, exist_ok=True)
    header = MAGIC + struct.pack("<III", VERSION, scale, len(entries))
    offset = len(header) + 12 * len(entries)
    table = []
    for (shape, frame_no), blob in zip(entries, blobs):
        table.append(struct.pack("<HHII", shape, frame_no, offset, len(blob)))
        offset += len(blob)
    # Written beside it and swapped in, so a running game's open pack can be replaced.
    tmp = out.with_suffix(".tmp")
    tmp.write_bytes(header + b"".join(table) + b"".join(blobs))
    os.replace(tmp, out)
    print(f"{out}: {len(entries)} frames, {out.stat().st_size / 1e6:.1f} MB", flush=True)
    return out


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", required=True)
    ap.add_argument("--file", default="shapes", choices=sorted(FILES) + ["chunks"])
    ap.add_argument("--margin", type=int, default=0, help="pixels of overhang round every painting")
    ap.add_argument("--paintings", type=Path, help="pictures fitted to their frames by height (the painted faces)")
    args = ap.parse_args()
    build(args.model, args.file, args.margin, args.paintings)


if __name__ == "__main__":
    main()
