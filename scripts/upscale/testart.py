"""Synthetic test art for the renderer's hi-res milestones (docs/RENDERER.md,
R4): packs made from the original art alone, with no model and no painter,
so every feature of pack version 3 can be proven without viable hi-res art.

    scripts/upscale/.venv/Scripts/python scripts/upscale/testart.py

Makes assets/hires/testart/ (shapes.hrp, sprites.hrp, faces.hrp) and
assets/hires/placement/shapes.hrp:

  testart
    * the avatar (721) and Iolo (465), every frame at 4x: bicubic colours,
      the silhouette bicubic as straight alpha (soft edges);
    * the lamp post (889) at 4x with an 8 px margin holding a warm halo that
      overhangs the frame, and normal, height and emissive maps beside it;
    * the sign (379): frames alternately at 2x and 8x (a scale per frame);
    * the sea tiles (19, 64, 30, 26, 20, 65, 2, 8) at 4x: the original's colours
      with noise texel by texel, so the cycling pixels quantise within their
      range to a 4x pattern that cycles in Exult's rotation;
    * the grass tiles (146, 147, 148, 31, 32) at 4x from one seamless
      texture, so the ground runs on from tile to tile without a seam;
    * sprite 7 (the teleport) at 4x, soft;
    * faces.hrp: the painted faces of assets/faces/bg fitted by height
      (pack.py --paintings).
  placement
    * the avatar and Iolo at 2x (even frames) and 8x (odd frames) with the
      original's own indices and binary coverage, no colour: painted at 4x
      they must equal the original blown up, pixel for pixel (the console's
      `art check`, the suite's placement check).
"""

from __future__ import annotations

import numpy as np
from PIL import Image

import pack
import u7art
from generate import bleed

AVATAR, IOLO, LAMP_POST, SIGN, TELEPORT = 721, 465, 889, 379, 7
SEA = [19, 64, 30, 26, 20, 65, 2, 8]
GRASS = [146, 147, 148, 31, 32]
HALO = 8


def rgb_of(frame: u7art.Frame) -> np.ndarray:
    """The frame's colours in the day palette, its edges bled outward."""
    pal = u7art.day_palette().astype(np.float32) / 255.0
    return bleed(pal[frame.index], frame.opaque)


def bicubic(rgb: np.ndarray, scale: int) -> np.ndarray:
    h, w = rgb.shape[:2]
    img = Image.fromarray(np.round(np.clip(rgb, 0, 1) * 255).astype(np.uint8))
    return np.asarray(img.resize((w * scale, h * scale), Image.BICUBIC))


def soft_frames(vga: u7art.VgaFile, shape: int, scale: int, lut, frames=None) -> list[tuple[tuple[int, int], bytes]]:
    out = []
    for f in frames if frames is not None else range(vga.frame_count(shape)):
        frame = vga.frame(shape, f)
        if frame is None or frame.width <= 0:
            continue
        s = scale if not callable(scale) else scale(f)
        out.append(((shape, f), pack.encode(frame, bicubic(rgb_of(frame), s), False, lut, scale=s)))
    return out


def halo_frames(vga: u7art.VgaFile, shape: int, lut) -> list[tuple[tuple[int, int], bytes]]:
    """Every frame at 4x with a warm halo in an 8 px margin round it, fading
    out, and maps beside it (a flat normal, a height rising up the frame, the
    halo emissive)."""
    out = []
    scale = pack.SCALE
    for f in range(vga.frame_count(shape)):
        frame = vga.frame(shape, f)
        if frame is None or frame.width <= 0:
            continue
        rgb = bicubic(rgb_of(frame), scale)
        h, w = rgb.shape[:2]
        canvas = np.full((h + 2 * HALO, w + 2 * HALO, 3), (255, 200, 90), np.uint8)
        canvas[HALO:HALO + h, HALO:HALO + w] = rgb
        inside = pack.upscale_mask(frame, scale)
        # The halo: fading with the distance from the silhouette (grown a ring at a time), out to the margin.
        grown = np.pad(inside >= 128, HALO)
        dist = np.where(grown, 0, HALO + 1).astype(np.float32)
        for step in range(1, HALO + 1):
            ring = grown.copy()
            for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)):
                ring |= np.roll(grown, (dy, dx), (0, 1))
            dist[ring & ~grown] = step
            grown = ring
        halo = np.clip(1 - dist / HALO, 0, 1) * 160
        alpha = np.round(halo).astype(np.uint8)
        alpha[HALO:HALO + h, HALO:HALO + w] = np.maximum(alpha[HALO:HALO + h, HALO:HALO + w], inside)
        maps = pack.Maps(normal=np.full(canvas.shape[:2] + (2,), 128, np.uint8),
                         height=np.round(np.linspace(255, 0, canvas.shape[0]))[:, None].repeat(canvas.shape[1], 1).astype(np.uint8),
                         emissive=alpha.copy())
        out.append(((shape, f), pack.encode(frame, canvas, False, lut, alpha, scale, HALO, maps)))
    return out


def noisy_tiles(vga: u7art.VgaFile, shapes: list[int], lut, seed: int) -> list[tuple[tuple[int, int], bytes]]:
    """Tiles at 4x: the original's colours with noise texel by texel."""
    rng = np.random.default_rng(seed)
    pal = u7art.day_palette()
    out = []
    for shape in shapes:
        for f in range(vga.frame_count(shape)):
            frame = vga.frame(shape, f)
            if frame is None:
                continue
            big = np.repeat(np.repeat(pal[frame.index], pack.SCALE, 0), pack.SCALE, 1).astype(np.int32)
            big += rng.integers(-14, 15, big.shape)
            out.append(((shape, f), pack.encode(frame, np.clip(big, 0, 255).astype(np.uint8), False, lut)))
    return out


def seamless_tiles(vga: u7art.VgaFile, shapes: list[int], lut) -> list[tuple[tuple[int, int], bytes]]:
    """Tiles at 4x painted from one seamless 32 x 32 texture in the tiles'
    mean colour, the same for all of them, so they join without a seam."""
    pal = u7art.day_palette().astype(np.float32)
    colours = []
    frames = []
    for shape in shapes:
        for f in range(vga.frame_count(shape)):
            frame = vga.frame(shape, f)
            if frame is not None:
                frames.append((shape, f, frame))
                colours.append(pal[frame.index].mean(axis=(0, 1)))
    base = np.mean(colours, axis=0)
    side = u7art.TILE * pack.SCALE
    y, x = np.mgrid[0:side, 0:side] * (2 * np.pi / side)
    # Sums of whole-frequency waves repeat every tile exactly.
    noise = (np.sin(3 * x + 2 * y) + np.sin(5 * y - x) * 0.6 + np.sin(7 * x + 4 * y) * 0.4 + np.sin(2 * x - 9 * y) * 0.3) / 2.3
    texture = np.clip(base[None, None, :] + noise[..., None] * np.array([18, 26, 12]), 0, 255).astype(np.uint8)
    return [((shape, f), pack.encode(frame, texture, False, lut)) for shape, f, frame in frames]


def placement_frames(vga: u7art.VgaFile, shape: int) -> list[tuple[tuple[int, int], bytes]]:
    """Every frame at 2x (even) or 8x (odd) as the original's own indices, binary coverage, no colour or mix."""
    out = []
    for f in range(vga.frame_count(shape)):
        frame = vga.frame(shape, f)
        if frame is None or frame.width <= 0:
            continue
        scale = 2 if f % 2 == 0 else 8
        index = np.repeat(np.repeat(frame.index, scale, 0), scale, 1)
        alpha = np.repeat(np.repeat(frame.opaque, scale, 0), scale, 1).astype(np.uint8) * 255
        extents = (frame.xleft * scale, frame.yabove * scale, frame.xright * scale + scale - 1, frame.ybelow * scale + scale - 1)
        out.append(((shape, f), pack.encode_planes(scale * 16, extents, frame.rle, alpha, index, None, None, None, soft=False)))
    return out


def write(model: str, file: str, entries: list[tuple[tuple[int, int], bytes]]) -> None:
    pack.write_pack(model, file, [e for e, _ in entries], [b for _, b in entries])


def main() -> None:
    lut = pack.build_lut()
    shapes = u7art.game_file("shapes")
    sprites = u7art.game_file("sprites")
    entries = []
    entries += soft_frames(shapes, AVATAR, pack.SCALE, lut)
    entries += soft_frames(shapes, IOLO, pack.SCALE, lut)
    entries += halo_frames(shapes, LAMP_POST, lut)
    entries += soft_frames(shapes, SIGN, lambda f: 2 if f % 2 == 0 else 8, lut)
    entries += noisy_tiles(shapes, SEA, lut, seed=7)
    entries += seamless_tiles(shapes, GRASS, lut)
    write("testart", "shapes", entries)
    write("testart", "sprites", soft_frames(sprites, TELEPORT, pack.SCALE, lut))
    faces = u7art.ROOT / "assets" / "faces" / "bg"
    if faces.is_dir():
        pack.build("testart", "faces", paintings=faces)
    write("placement", "shapes", placement_frames(shapes, AVATAR) + placement_frames(shapes, IOLO))


if __name__ == "__main__":
    main()
