"""Upscale the original art 4x with an AI model, frame by frame.

    scripts/upscale/.venv/Scripts/python scripts/upscale/generate.py --model nxbrz [--file shapes|sprites|gumps|faces|paperdolls] [--shapes 0-1023]

Each frame is shown to the model in the day palette with its edge colours
bled outward (so it doesn't mix in black), and terrain tiles in the context
of their most frequent neighbours (so they still meet without seams). The
model's colours go to assets/hires/work/<model>/<file>/<shape>_<frame>.png,
cropped to the frame; pack.py turns them back into palette indices. Frames
already done are skipped, so a run can be stopped and resumed.
"""

from __future__ import annotations

import argparse
import time
from pathlib import Path

import numpy as np
from PIL import Image

import u7art
from models import MODELS, Upscaler

SCALE = 4
# Bled edge colour round each frame, so the model sees no border.
PAD = 8
# Opaque along this share of each edge: a piece built into something larger
# (a floor, wall or roof), shown to the model among copies of itself.
BUILT = 0.85

# The files as the game builds them (u7art.game_file): Black Gate's, Serpent
# Isle's imports and paperdolls, Exult's own art.
FILES = {"shapes": "SHAPES.VGA", "sprites": "SPRITES.VGA", "gumps": "GUMPS.VGA", "faces": "FACES.VGA",
         "paperdolls": "PAPERDOL.VGA"}


def is_translucent(file: str, shape: int, translucent: frozenset[int]) -> bool:
    """Sprites are; the others by the shapes' TFA flag, as the packs were made (the paperdolls never)."""
    return file == "sprites" or (file != "paperdolls" and shape in translucent)


def work_dir(model: str, file: str) -> Path:
    return u7art.HIRES / "work" / model / file


def bleed(rgb: np.ndarray, known: np.ndarray, steps: int = 48) -> np.ndarray:
    """Unknown pixels take the mean of their known neighbours, ring by ring outward."""
    rgb = rgb.astype(np.float32).copy()
    known = known.copy()
    for _ in range(steps):
        if known.all():
            break
        acc = np.zeros_like(rgb)
        cnt = np.zeros(known.shape, np.float32)
        padded = np.pad(rgb * known[..., None], ((1, 1), (1, 1), (0, 0)))
        kpad = np.pad(known.astype(np.float32), 1)
        h, w = known.shape
        for dy in (0, 1, 2):
            for dx in (0, 1, 2):
                if dx == 1 and dy == 1:
                    continue
                acc += padded[dy:dy + h, dx:dx + w]
                cnt += kpad[dy:dy + h, dx:dx + w]
        grow = (~known) & (cnt > 0)
        rgb[grow] = acc[grow] / cnt[grow][:, None]
        known = known | grow
    if not known.all():
        rgb[~known] = rgb[known].mean(axis=0) if known.any() else 0.5
    return rgb


def fills_frame(frame: u7art.Frame, share: float = 0.9) -> bool:
    """Whether the frame is opaque along all four edges (floors, walls,
    roofs): such pieces meet others, mostly copies of themselves."""
    o = frame.opaque
    return min(o[0].mean(), o[-1].mean(), o[:, 0].mean(), o[:, -1].mean()) >= share


def frame_input(frame: u7art.Frame, translucent: bool) -> np.ndarray:
    """The frame's RGB in [0, 1] with its edges bled, padded by PAD: wrapped
    round for a piece that fills its frame, so the model sees it continue
    into a copy of itself instead of an edge (a seam every piece)."""
    pal = u7art.day_palette().astype(np.float32) / 255.0
    rgb = pal[frame.index]
    # Translucent pixels have no colour of their own: what is round them leads.
    seen = frame.opaque.copy()
    if translucent:
        seen &= ~((frame.index >= u7art.FIRST_TRANSLUCENT) & (frame.index <= 0xFE))
    rgb = bleed(rgb, seen)
    wrap = fills_frame(frame) and frame.height >= PAD and frame.width >= PAD
    return np.pad(rgb, ((PAD, PAD), (PAD, PAD), (0, 0)), mode="wrap" if wrap else "symmetric")


def built_context(frame: u7art.Frame, shape: int, frame_no: int, translucent: bool) -> tuple[np.ndarray, np.ndarray] | None:
    """A piece of a floor, wall or roof among copies of itself a footprint
    apart, painted as the world paints them (north and west first, so the
    south and east ones cover the edges they overlap): the model then sees
    its visible edges meet floor, not the slab side the next piece hides.
    Returns the input (padded by PAD) and which of the piece's pixels stay
    visible there; None for a piece that isn't built into anything."""
    if not frame.rle or not fills_frame(frame, BUILT):
        return None
    dx, dy = u7art.shape_dims().get(shape, (1, 1))
    if frame_no >= 32:
        dx, dy = dy, dx
    step_x, step_y = dx * u7art.TILE, dy * u7art.TILE
    o = frame.opaque
    across = o[:, 0].mean() >= BUILT and o[:, -1].mean() >= BUILT
    down = o[0].mean() >= BUILT and o[-1].mean() >= BUILT
    if not (across or down) or step_x >= frame.width + PAD or step_y >= frame.height + PAD:
        return None
    pal = u7art.day_palette().astype(np.float32) / 255.0
    rgb = pal[frame.index]
    h, w = frame.height, frame.width
    mx, my = (step_x if across else 0), (step_y if down else 0)
    canvas = np.zeros((h + 2 * my, w + 2 * mx, 3), np.float32)
    owner = np.full(canvas.shape[:2], -1, np.int8)
    order = [(iy, ix) for iy in ((-1, 0, 1) if down else (0,)) for ix in ((-1, 0, 1) if across else (0,))]
    coloured = np.zeros(canvas.shape[:2], bool)
    # Translucent pixels have no colour of their own, as in frame_input.
    own = o & ~((frame.index >= u7art.FIRST_TRANSLUCENT) & (frame.index <= 0xFE)) if translucent else o
    for n, (iy, ix) in enumerate(order):
        y0, x0 = my + iy * step_y, mx + ix * step_x
        region = (slice(y0, y0 + h), slice(x0, x0 + w))
        canvas[region][o] = rgb[o]
        owner[region][o] = n
        coloured[region][o] = own[o]
    centre = order.index((0, 0))
    canvas = bleed(canvas, coloured)
    canvas = np.pad(canvas, ((PAD, PAD), (PAD, PAD), (0, 0)), mode="symmetric")
    # Cut to the piece and PAD round it, as frame_input.
    cut = canvas[my:my + h + 2 * PAD, mx:mx + w + 2 * PAD]
    visible = owner[my:my + h, mx:mx + w] == centre
    return cut, visible


def tile_input(vga: u7art.VgaFile, shape: int, frame: int) -> np.ndarray:
    """A terrain tile among its most frequent neighbours (3x3 tiles), padded by PAD."""
    pal = u7art.day_palette().astype(np.float32) / 255.0
    centre = vga.frame(shape, frame).index
    neighbours = u7art.tile_neighbours().get(shape << 5 | frame, {})
    grid = np.zeros((3 * u7art.TILE, 3 * u7art.TILE), np.uint8)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            tile = centre
            if (dx, dy) in neighbours:
                n = neighbours[(dx, dy)]
                tile = vga.frame(n >> 5, n & 31).index
            else:
                # No flat neighbour there: the tile repeated, as most terrain repeats.
                tile = centre
            y0, x0 = (dy + 1) * u7art.TILE, (dx + 1) * u7art.TILE
            grid[y0:y0 + u7art.TILE, x0:x0 + u7art.TILE] = tile
    return np.pad(pal[grid], ((PAD, PAD), (PAD, PAD), (0, 0)), mode="symmetric")


def run(model: str, file: str, shapes: range | None, limit: int | None, redo_filled: bool = False) -> int:
    """Upscales the frames not done yet; returns how many it made."""
    vga = u7art.game_file(file)
    if vga is None:
        print(f"{file}: not there (Serpent Isle isn't installed)", flush=True)
        return 0
    if shapes is None:
        shapes = range(len(vga.records))
    out = work_dir(model, file)
    out.mkdir(parents=True, exist_ok=True)
    up = Upscaler(model)
    translucent = u7art.translucent_shapes()
    done = 0
    started = time.time()
    for shape in shapes:
        if shape >= len(vga.records):
            break
        for f in range(vga.frame_count(shape)):
            dest = out / f"{shape:04d}_{f:02d}.png"
            frame = vga.frame(shape, f) if redo_filled or not dest.exists() else None
            if frame is None or not frame.opaque.any():
                continue
            if redo_filled and not (frame.rle and fills_frame(frame, BUILT)):
                continue
            if not frame.rle:
                big = up(tile_input(vga, shape, f))
                o = (PAD + u7art.TILE) * SCALE
                crop = big[o:o + u7art.TILE * SCALE, o:o + u7art.TILE * SCALE]
            else:
                see_through = is_translucent(file, shape, translucent)
                big = up(frame_input(frame, see_through))
                o = PAD * SCALE
                crop = big[o:o + frame.height * SCALE, o:o + frame.width * SCALE]
                built = built_context(frame, shape, f, see_through)
                if built is not None:
                    # What shows of the piece in the world from its context; what its neighbours hide, alone.
                    context, visible = built
                    inside = up(context)[o:o + frame.height * SCALE, o:o + frame.width * SCALE]
                    shown = np.repeat(np.repeat(visible, SCALE, 0), SCALE, 1)
                    crop = np.where(shown[..., None], inside, crop)
            Image.fromarray((crop * 255.0 + 0.5).astype(np.uint8)).save(dest, compress_level=1)
            done += 1
            if limit is not None and done >= limit:
                print(f"{done} frames in {time.time() - started:.0f}s", flush=True)
                return done
            if done % 500 == 0:
                print(f"{model} {file}: {done} frames, shape {shape}, {time.time() - started:.0f}s", flush=True)
    print(f"{model} {file}: {done} frames in {time.time() - started:.0f}s", flush=True)
    return done


def parse_range(text: str) -> range:
    lo, _, hi = text.partition("-")
    return range(int(lo), int(hi or lo) + 1)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", required=True, choices=sorted(MODELS))
    ap.add_argument("--file", default="shapes", choices=sorted(FILES))
    ap.add_argument("--shapes", help="a range such as 0-1023; all of the file by default")
    ap.add_argument("--limit", type=int)
    ap.add_argument("--redo-filled", action="store_true", help="again the pieces built into floors, walls and roofs")
    args = ap.parse_args()
    run(args.model, args.file, parse_range(args.shapes) if args.shapes else None, args.limit, args.redo_filled)


if __name__ == "__main__":
    main()
