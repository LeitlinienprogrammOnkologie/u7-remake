#!/usr/bin/env python3
"""Render the full Black Gate world map to a PNG.

Mirrors the Godot loader/painter (GameMap.cs, WorldView.cs, ShapeCache.cs):

* U7MAP / U7CHUNKS  -> 192x192 chunks of 16x16 8px tiles (24576 px per side)
* U7IFIXxx          -> fixed scenery (lift-aware)
* GAMEDAT/U7IREGxx  -> moveable objects (top-level only, eggs hidden except moongates)
* draw order        -> (tx + ty) * 64 + tz * 4, same as U7Object.RenderOrder

The full-res image (24576x24576 RGB, ~1.8 GB raw) is streamed to disk in
2048 px strips so it never has to fit in memory at once.  A downscaled
overview PNG is written alongside it.

Usage:
    python scripts/render_map.py [--out assets/maps/britannia.png]
                                 [--overview 6144] [--no-ireg] [--scale 1]
"""

from __future__ import annotations

import argparse
import csv
import struct
import sys
import time
import zlib
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
STATIC = ROOT / "u7" / "STATIC"
GAMEDAT = ROOT / "u7" / "GAMEDAT"
SHAPES = ROOT / "assets" / "graphics" / "shapes"
DATA = ROOT / "assets" / "data"

TILE = 8
CHUNK_TILES = 16
CHUNK_PX = TILE * CHUNK_TILES  # 128
CHUNKS_PER_SC = 16
SC_PER_SIDE = 12
NUM_CHUNKS = SC_PER_SIDE * CHUNKS_PER_SC  # 192
MAP_PX = NUM_CHUNKS * CHUNK_PX  # 24576
TERRAIN_COUNT = 3072
TILE_SHAPE_COUNT = 150
STRIP_PX = CHUNKS_PER_SC * CHUNK_PX  # 2048 (one superchunk row)
MOONGATE_SHAPE = 305


# --------------------------------------------------------------------------
# Shape metadata
# --------------------------------------------------------------------------
class FrameInfo:
    __slots__ = ("w", "h", "xleft", "yabove", "is_tile")

    def __init__(self, w, h, xleft, yabove, is_tile):
        self.w, self.h, self.xleft, self.yabove, self.is_tile = w, h, xleft, yabove, is_tile


def _int(s, default):
    try:
        return int(s)
    except (TypeError, ValueError):
        return default


def load_frames() -> dict[tuple[int, int], FrameInfo]:
    frames: dict[tuple[int, int], FrameInfo] = {}
    with open(DATA / "shape_frames.csv", newline="") as f:
        for row in csv.DictReader(f):
            shape = _int(row["shape"], -1)
            frame = _int(row["frame"], -1)
            if shape < 0 or frame < 0:
                continue
            is_tile = row["is_tile"].lower() == "true"
            w = _int(row["width"], 8)
            h = _int(row["height"], 8)
            xleft = _int(row["hotspot_x_from_left"], 0 if is_tile else max(0, w - 1))
            yabove = _int(row["hotspot_y_from_top"], 0 if is_tile else max(0, h - 1))
            frames[(shape, frame)] = FrameInfo(w, h, xleft, yabove, is_tile)
    return frames


def load_shape_classes() -> dict[int, int]:
    classes: dict[int, int] = {}
    with open(DATA / "typeflags.csv", newline="") as f:
        for row in csv.DictReader(f):
            classes[_int(row["shape"], -1)] = _int(row["shape_class"], 0)
    return classes


class SpriteCache:
    """Extracted PNG -> RGBA numpy array, with Exult-style reflected frames."""

    def __init__(self, frames):
        self.frames = frames
        self._cache: dict[tuple[int, int], np.ndarray | None] = {}

    def get(self, shape, frame):
        key = (shape, frame)
        if key in self._cache:
            return self._cache[key]
        path = SHAPES / f"{shape:04d}" / f"{shape:04d}_f{frame:04d}.png"
        img = None
        if path.exists():
            img = np.asarray(Image.open(path).convert("RGBA"))
        elif frame & 32:
            base = self.get(shape, frame & 0x1F)
            if base is not None:
                img = np.ascontiguousarray(base.transpose(1, 0, 2))  # NW/SE reflect
        self._cache[key] = img
        return img

    def info(self, shape, frame):
        fi = self.frames.get((shape, frame))
        if fi is None and frame & 32:
            base = self.frames.get((shape, frame & 0x1F))
            if base is not None:
                fi = FrameInfo(base.h, base.w, base.yabove, base.xleft, base.is_tile)
        if fi is None:
            fi = self.frames.get((shape, 0))
        return fi


# --------------------------------------------------------------------------
# Map data
# --------------------------------------------------------------------------
def load_terrains(frames):
    data = (STATIC / "U7CHUNKS").read_bytes()
    raw = np.frombuffer(data[: TERRAIN_COUNT * 512], dtype="<u2").reshape(TERRAIN_COUNT, 256)
    shapes = (raw & 0x3FF).astype(np.int32)
    frs = ((raw >> 10) & 0x1F).astype(np.int32)
    return shapes, frs


def load_map():
    data = (STATIC / "U7MAP").read_bytes()
    tmap = np.zeros((NUM_CHUNKS, NUM_CHUNKS), dtype=np.int32)  # [cy, cx]
    off = 0
    for sc in range(SC_PER_SIDE * SC_PER_SIDE):
        scy = CHUNKS_PER_SC * (sc // SC_PER_SIDE)
        scx = CHUNKS_PER_SC * (sc % SC_PER_SIDE)
        block = np.frombuffer(data[off : off + 512], dtype="<u2").reshape(16, 16)
        off += 512
        tmap[scy : scy + 16, scx : scx + 16] = block
    return tmap


def flex_entries(path: Path):
    data = path.read_bytes()
    count = struct.unpack_from("<I", data, 84)[0]
    for i in range(count):
        off, size = struct.unpack_from("<II", data, 128 + 8 * i)
        yield data[off : off + size] if size else b""


def nibble_swap(b):
    return ((b & 0xF) << 4) | (b >> 4)


def load_ifix(objs: list):
    for sc in range(SC_PER_SIDE * SC_PER_SIDE):
        path = STATIC / f"U7IFIX{sc:02X}"
        if not path.exists():
            continue
        scy = CHUNKS_PER_SC * (sc // SC_PER_SIDE)
        scx = CHUNKS_PER_SC * (sc % SC_PER_SIDE)
        for idx, entry in enumerate(flex_entries(path)):
            cx = scx + idx % 16
            cy = scy + idx // 16
            for i in range(0, len(entry) - 3, 4):
                e0, e1, e2, e3 = entry[i : i + 4]
                tx = (e0 >> 4) & 0xF
                ty = e0 & 0xF
                tz = e1 & 0xF
                shape = e2 + 256 * (e3 & 3)
                frame = e3 >> 2
                objs.append((cx * 16 + tx, cy * 16 + ty, tz, shape, frame))


def parse_ireg(data: bytes, i: int, scx: int, scy: int, nested: bool, classes, objs) -> int:
    """Port of GameMap.ParseIreg; only top-level objects are emitted."""
    n = len(data)
    while i < n:
        entlen = data[i]
        i += 1
        if entlen in (0, 1):
            if nested:
                return i
            continue
        if entlen == 2:
            i += 2
            continue
        extended = False
        if entlen in (253, 254):
            extended = entlen == 254
            if i >= n:
                break
            entlen = data[i]
            i += 1
        if entlen == 255:
            if i < n:
                kind = data[i]
                i += 1
                if kind == 1 and i + 2 <= n:
                    ln = struct.unpack_from("<H", data, i)[0]
                    i += 2 + ln
            continue
        if i + entlen > n:
            break
        entry = data[i : i + entlen]
        i += entlen
        testlen = entlen - (1 if extended else 0)
        if extended:
            shape = entry[2] + 256 * entry[3]
            frame = entry[4]
            lift_idx = 5
        else:
            shape = entry[2] + 256 * (entry[3] & 3)
            frame = entry[3] >> 2
            lift_idx = 4
        cx = entry[0] >> 4
        cy = entry[1] >> 4
        tilex = entry[0] & 0xF
        tiley = entry[1] & 0xF
        cls = classes.get(shape, 0)
        wcx, wcy = scx + cx, scy + cy
        in_world = (not nested) and 0 <= wcx < NUM_CHUNKS and 0 <= wcy < NUM_CHUNKS

        if cls == 7:  # hatchable / egg
            off = 1 if extended else 0
            if 4 + off + 8 <= len(entry) and in_world and shape == MOONGATE_SHAPE:
                lift = nibble_swap(entry[9 + off]) & 0xF
                objs.append((wcx * 16 + tilex, wcy * 16 + tiley, lift, shape, frame))
            continue

        if testlen in (12, 13):  # container
            ctype = entry[4] + 256 * entry[5]
            lift = nibble_swap(entry[9]) & 0xF
            if in_world and cls != 9:
                objs.append((wcx * 16 + tilex, wcy * 16 + tiley, lift, shape, frame))
            if ctype != 0:
                i = parse_ireg(data, i, scx, scy, True, classes, objs)
            continue

        if testlen not in (6, 10, 18):
            continue
        if len(entry) <= lift_idx:
            continue
        lift = nibble_swap(entry[lift_idx]) & 0xF
        if in_world:
            objs.append((wcx * 16 + tilex, wcy * 16 + tiley, lift, shape, frame))
    return i


def load_ireg(objs: list, classes):
    found = 0
    for sc in range(SC_PER_SIDE * SC_PER_SIDE):
        path = GAMEDAT / f"U7IREG{sc:02X}"
        if not path.exists():
            continue
        found += 1
        scy = CHUNKS_PER_SC * (sc // SC_PER_SIDE)
        scx = CHUNKS_PER_SC * (sc % SC_PER_SIDE)
        parse_ireg(path.read_bytes(), 0, scx, scy, False, classes, objs)
    return found


# --------------------------------------------------------------------------
# Streaming PNG writer
# --------------------------------------------------------------------------
class PngWriter:
    def __init__(self, path: Path, width: int, height: int, level: int = 6):
        self.f = open(path, "wb")
        self.f.write(b"\x89PNG\r\n\x1a\n")
        self._chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
        self.z = zlib.compressobj(level)
        self.width = width

    def _chunk(self, tag: bytes, payload: bytes):
        self.f.write(struct.pack(">I", len(payload)))
        self.f.write(tag)
        self.f.write(payload)
        self.f.write(struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF))

    def write_rows(self, rgb: np.ndarray):
        # rgb: (rows, width, 3) uint8; prepend filter byte 0 to each row
        rows = rgb.shape[0]
        buf = np.empty((rows, self.width * 3 + 1), dtype=np.uint8)
        buf[:, 0] = 0
        buf[:, 1:] = rgb.reshape(rows, -1)
        # Compress in slices so IDAT chunks stay a manageable size.
        step = 256
        for y in range(0, rows, step):
            out = self.z.compress(buf[y : y + step].tobytes())
            if out:
                self._chunk(b"IDAT", out)

    def close(self):
        out = self.z.flush()
        if out:
            self._chunk(b"IDAT", out)
        self._chunk(b"IEND", b"")
        self.f.close()


# --------------------------------------------------------------------------
# Rendering
# --------------------------------------------------------------------------
def blit(dst: np.ndarray, sprite: np.ndarray, x: int, y: int):
    """Alpha-mask blit of RGBA sprite onto RGB dst at (x, y), clipped."""
    h, w = sprite.shape[:2]
    H, W = dst.shape[:2]
    x0, y0 = max(x, 0), max(y, 0)
    x1, y1 = min(x + w, W), min(y + h, H)
    if x0 >= x1 or y0 >= y1:
        return
    src = sprite[y0 - y : y1 - y, x0 - x : x1 - x]
    a = src[:, :, 3] > 127
    region = dst[y0:y1, x0:x1]
    region[a] = src[:, :, :3][a]


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--out", type=Path, default=ROOT / "assets" / "maps" / "britannia_full.png")
    ap.add_argument("--overview", type=int, default=6144, help="overview side in px (0 = skip)")
    ap.add_argument("--no-ireg", action="store_true", help="skip GAMEDAT moveable objects")
    ap.add_argument("--strips", type=str, default="", help="only render superchunk rows, e.g. 8-9 (debug)")
    ap.add_argument("--level", type=int, default=6, help="zlib level for the full PNG")
    args = ap.parse_args()

    t0 = time.time()
    frames = load_frames()
    classes = load_shape_classes()
    sprites = SpriteCache(frames)
    ter_shapes, ter_frames = load_terrains(frames)
    tmap = load_map()
    print(f"metadata + terrain loaded in {time.time() - t0:.1f}s", flush=True)

    # Objects: IFIX + IREG + RLE terrain cells (chunk cells whose shape is not an 8x8 flat).
    objs: list[tuple[int, int, int, int, int]] = []
    load_ifix(objs)
    n_ifix = len(objs)
    n_ireg_files = 0
    if not args.no_ireg:
        n_ireg_files = load_ireg(objs, classes)
    n_ireg = len(objs) - n_ifix

    is_tile_shape = np.zeros(1024, dtype=bool)
    for (s, f), fi in frames.items():
        if f == 0:
            is_tile_shape[s] = fi.is_tile
    rle_mask = ~is_tile_shape[ter_shapes]  # (terrain, cell)
    for cy in range(NUM_CHUNKS):
        for cx in range(NUM_CHUNKS):
            t = tmap[cy, cx]
            if t >= TERRAIN_COUNT:
                continue
            cells = np.nonzero(rle_mask[t])[0]
            for c in cells:
                objs.append((cx * 16 + (c % 16), cy * 16 + (c // 16), 0, int(ter_shapes[t, c]), int(ter_frames[t, c])))
    n_rle = len(objs) - n_ifix - n_ireg
    print(f"objects: ifix={n_ifix} ireg={n_ireg} (files={n_ireg_files}) rle={n_rle} total={len(objs)}", flush=True)

    # Vectorise sprite placement.
    arr = np.array(objs, dtype=np.int32)
    del objs
    tx, ty, tz, shp, frm = arr.T
    order = (tx + ty) * 64 + tz * 4
    idx = np.argsort(order, kind="stable")
    arr, tx, ty, tz, shp, frm, order = arr[idx], tx[idx], ty[idx], tz[idx], shp[idx], frm[idx], order[idx]

    n = len(arr)
    px = np.zeros(n, dtype=np.int32)
    py = np.zeros(n, dtype=np.int32)
    ph = np.zeros(n, dtype=np.int32)
    valid = np.zeros(n, dtype=bool)
    info_cache: dict[tuple[int, int], FrameInfo | None] = {}
    for k in range(n):
        key = (int(shp[k]), int(frm[k]))
        fi = info_cache.get(key)
        if fi is None and key not in info_cache:
            fi = sprites.info(*key)
            info_cache[key] = fi
        if fi is None:
            continue
        lift = 4 * int(tz[k])
        hx = (int(tx[k]) + 1) * TILE - 1 - lift
        hy = (int(ty[k]) + 1) * TILE - 1 - lift
        px[k] = hx - fi.xleft
        py[k] = hy - fi.yabove
        ph[k] = fi.h
        valid[k] = True
    print(f"placed {int(valid.sum())}/{n} objects in {time.time() - t0:.1f}s", flush=True)

    # Terrain chunk flats, built lazily per terrain template.
    flat_cache: dict[int, np.ndarray] = {}

    def chunk_flat(t: int) -> np.ndarray:
        img = flat_cache.get(t)
        if img is None:
            img = np.zeros((CHUNK_PX, CHUNK_PX, 3), dtype=np.uint8)
            for c in range(256):
                if rle_mask[t, c]:
                    continue
                sp = sprites.get(int(ter_shapes[t, c]), int(ter_frames[t, c]))
                if sp is None:
                    continue
                lx, ly = (c % 16) * TILE, (c // 16) * TILE
                h, w = min(8, sp.shape[0]), min(8, sp.shape[1])
                img[ly : ly + h, lx : lx + w] = sp[:h, :w, :3]
            flat_cache[t] = img
        return img

    strips = range(SC_PER_SIDE)
    if args.strips:
        a, _, b = args.strips.partition("-")
        strips = range(int(a), int(b or a) + 1)
    height = len(strips) * STRIP_PX

    args.out.parent.mkdir(parents=True, exist_ok=True)
    writer = PngWriter(args.out, MAP_PX, height, args.level)
    overview = None
    ov_scale = 0
    if args.overview:
        ov_scale = MAP_PX // args.overview
        overview = np.zeros((height // ov_scale, MAP_PX // ov_scale, 3), dtype=np.uint8)

    strip = np.zeros((STRIP_PX, MAP_PX, 3), dtype=np.uint8)
    missing: set[tuple[int, int]] = set()
    for si, s in enumerate(strips):
        ts = time.time()
        y_start = s * STRIP_PX
        y_end = y_start + STRIP_PX
        strip[:] = 0
        for cy in range(s * CHUNKS_PER_SC, (s + 1) * CHUNKS_PER_SC):
            ly = (cy - s * CHUNKS_PER_SC) * CHUNK_PX
            for cx in range(NUM_CHUNKS):
                t = tmap[cy, cx]
                if t < TERRAIN_COUNT:
                    strip[ly : ly + CHUNK_PX, cx * CHUNK_PX : (cx + 1) * CHUNK_PX] = chunk_flat(t)

        sel = np.nonzero(valid & (py < y_end) & (py + ph > y_start))[0]
        for k in sel:
            key = (int(shp[k]), int(frm[k]))
            sp = sprites.get(*key)
            if sp is None:
                missing.add(key)
                continue
            blit(strip, sp, int(px[k]), int(py[k]) - y_start)

        writer.write_rows(strip)
        if overview is not None:
            small = Image.fromarray(strip).resize((MAP_PX // ov_scale, STRIP_PX // ov_scale), Image.BOX)
            oy = si * (STRIP_PX // ov_scale)
            overview[oy : oy + STRIP_PX // ov_scale] = np.asarray(small)
        print(f"strip {s:2d}/{SC_PER_SIDE - 1}: {len(sel):7d} objects, {time.time() - ts:5.1f}s "
              f"(total {time.time() - t0:.0f}s)", flush=True)

    writer.close()
    print(f"wrote {args.out} ({MAP_PX}x{height}, {args.out.stat().st_size / 1e6:.0f} MB)")
    if overview is not None:
        ov_path = args.out.with_name(args.out.stem.replace("_full", "") + f"_{args.overview}.png")
        Image.fromarray(overview).save(ov_path, optimize=False)
        print(f"wrote {ov_path} ({overview.shape[1]}x{overview.shape[0]})")
    if missing:
        print(f"{len(missing)} shape/frame sprites missing (skipped), e.g. {sorted(missing)[:8]}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
