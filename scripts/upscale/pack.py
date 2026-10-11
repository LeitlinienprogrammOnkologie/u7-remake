"""Turn a model's upscaled frames back into palette art: a pack the game reads.

    scripts/upscale/.venv/Scripts/python scripts/upscale/pack.py --model nxbrz [--file shapes|sprites]

For each frame generate.py made:
  * its shape: the original opacity upscaled smoothly (bicubic, cut at a half),
    so silhouettes lose their stairs while two pieces that meet still meet;
  * colour-cycling pixels (water, flames) and a translucent shape's
    see-through pixels keep their original index, blown up, so Exult's
    palette rotation and translucency tables still apply to them;
  * every other pixel the model's colour as two palette indices and how far
    between them it lies (the world shader mixes the two), the first the
    nearest single colour, so the mix can be turned off.

The pack (assets/hires/<model>/<file>.hrp) holds each frame as HiresPack.cs
reads it: header "U7HR", version, scale, count; entries (shape, frame,
offset, length); each frame zlib-compressed: extents (xleft, yabove, xright,
ybelow at 4x), flags (1 RLE, 2 mixed), scan count, scans (x, y, length from
the hotspot), the indices, then the mix plane (second index, 0-255 towards it).
"""

from __future__ import annotations

import argparse
import os
import struct
import zlib
from pathlib import Path

import numpy as np
from PIL import Image

import u7art
from generate import BUILT, FILES, SCALE, fills_frame, work_dir

MAGIC = b"U7HR"
VERSION = 1
# Palette entries the model's colours may become: not the cycling ones (they
# would start to cycle), not the translucent ones, not 0xFF (transparent to Exult's reflect).
ALLOWED = u7art.CYCLING_FIRST
LUT_BITS = 6
NEAREST = 8


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


def upscale_mask(frame: u7art.Frame) -> np.ndarray:
    opaque = frame.opaque
    if not frame.rle:
        return np.ones((opaque.shape[0] * SCALE, opaque.shape[1] * SCALE), bool)
    if fills_frame(frame, BUILT):
        # A piece of a floor, wall or roof keeps its exact outline: its stairs
        # meet the next piece's, and a smooth edge would let what is under show.
        return np.repeat(np.repeat(opaque, SCALE, 0), SCALE, 1)
    # Edge-padded, so a shape cut straight by its frame stays straight there.
    pad = np.pad(opaque.astype(np.float32), 2, mode="edge")
    h, w = pad.shape
    big = np.asarray(Image.fromarray(pad, "F").resize((w * SCALE, h * SCALE), Image.BICUBIC))
    o = 2 * SCALE
    return big[o:o + opaque.shape[0] * SCALE, o:o + opaque.shape[1] * SCALE] > 0.5


def encode(frame: u7art.Frame, ai: np.ndarray, translucent: bool, lut) -> bytes:
    near = np.repeat(np.repeat(frame.index, SCALE, 0), SCALE, 1)
    near_opaque = np.repeat(np.repeat(frame.opaque, SCALE, 0), SCALE, 1)
    keep = np.repeat(np.repeat(u7art.special_mask(frame, translucent), SCALE, 0), SCALE, 1)
    mask = upscale_mask(frame)
    i1, i2, mix = quantize(ai, lut)
    keep &= near_opaque
    i1 = np.where(keep, near, i1)
    i2 = np.where(keep, near, i2)
    mix = np.where(keep, 0, mix)

    xleft, yabove = frame.xleft * SCALE, frame.yabove * SCALE
    xright, ybelow = frame.xright * SCALE + SCALE - 1, frame.ybelow * SCALE + SCALE - 1
    scans = []
    pixels = []
    mixes = []
    for row in range(mask.shape[0]):
        line = mask[row]
        cols = np.flatnonzero(np.diff(np.concatenate(([0], line.astype(np.int8), [0]))))
        for start, end in zip(cols[::2], cols[1::2]):
            scans.append(struct.pack("<hhh", start - xleft, row - yabove, end - start))
            pixels.append(i1[row, start:end].tobytes())
            mixes.append(np.stack([i2[row, start:end], mix[row, start:end]], -1).tobytes())
    flags = (1 if frame.rle else 0) | 2
    body = struct.pack("<hhhhBI", xleft, yabove, xright, ybelow, flags, len(scans))
    body += b"".join(scans) + b"".join(pixels) + b"".join(mixes)
    return zlib.compress(body, 6)


def build(model: str, file: str) -> Path:
    vga = u7art.VgaFile(FILES[file])
    src = work_dir(model, file)
    lut = build_lut()
    translucent = u7art.translucent_shapes()
    entries = []
    blobs = []
    for png in sorted(src.glob("*.png")):
        shape, frame_no = (int(p) for p in png.stem.split("_"))
        frame = vga.frame(shape, frame_no)
        if frame is None:
            continue
        ai = np.asarray(Image.open(png).convert("RGB"))
        if ai.shape[:2] != (frame.height * SCALE, frame.width * SCALE):
            continue
        is_translucent = file == "sprites" or shape in translucent
        blobs.append(encode(frame, ai, is_translucent, lut))
        entries.append((shape, frame_no))

    out = u7art.HIRES / model / f"{file}.hrp"
    out.parent.mkdir(parents=True, exist_ok=True)
    header = MAGIC + struct.pack("<III", VERSION, SCALE, len(entries))
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
    ap.add_argument("--file", default="shapes", choices=sorted(FILES))
    args = ap.parse_args()
    build(args.model, args.file)


if __name__ == "__main__":
    main()
