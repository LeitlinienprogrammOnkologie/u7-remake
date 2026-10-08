"""Top-down sprite render of the whole map from SHAPES.VGA.

Layout follows Exult's painter: each tile is 8 px, an object at hot-spot
tile (tx, ty) with lift tz is drawn with its hot spot (Shape_frame xleft /
yabove) at pixel ((tx+1)*8 - 1 - 4*tz, (ty+1)*8 - 1 - 4*tz)
(Game_window::get_shape_location). Flats (shapes < 0x96) are painted at
(tx*8, ty*8) as the chunk background (Chunk_terrain::render_flats). Draw
order (tx+ty, then lift) is the same approximation used by
scripts/render_map.py in this repo, which reproduces the in-game look.

The 24576 px full image is rendered in 8 strips of 3072 rows and box-
downsampled on the fly to the requested output sizes.
"""
from __future__ import annotations
import time
from pathlib import Path

import numpy as np
from PIL import Image

from .mapdata import load_map, load_chunks, load_ifix
from .paths import NUM_TILES, NUM_CHUNKS, TILE_PX
from .shapes import ShapesVGA, load_palette, FIRST_OBJ_SHAPE

MAP_PX = NUM_TILES * TILE_PX          # 24576
STRIP_ROWS = 3072                     # 8 strips
NSTRIPS = MAP_PX // STRIP_ROWS


class SpriteCache:
    def __init__(self, vga: ShapesVGA, palette: np.ndarray):
        self.vga = vga
        self.pal = palette
        self._c: dict[tuple[int, int], tuple[np.ndarray, np.ndarray, int, int] | None] = {}

    def get(self, shape: int, frame: int):
        key = (shape, frame)
        if key in self._c:
            return self._c[key]
        fr = self.vga.frame(shape, frame)
        if fr is None and frame >= 32:
            fr = self.vga.frame(shape, frame & 31)
        if fr is None:
            self._c[key] = None
            return None
        rgb = self.pal[fr.pixels]
        val = (rgb, fr.alpha, fr.xleft, fr.yabove)
        self._c[key] = val
        return val


def collect_objects(tmap, chunks, ifix, extra: np.ndarray | None = None) -> np.ndarray:
    """Returns int32 (n, 5): tx, ty, tz, shape, frame for every non-flat object."""
    ch_s, ch_f = chunks
    parts = [ifix[:, :5]]
    rle = ch_s >= FIRST_OBJ_SHAPE
    for t in range(ch_s.shape[0]):
        cells = np.nonzero(rle[t])[0]
        if not len(cells):
            continue
        locs = np.argwhere(tmap == t)
        if not len(locs):
            continue
        for cy, cx in locs:
            tx = cx * 16 + cells % 16
            ty = cy * 16 + cells // 16
            parts.append(np.stack([tx, ty, np.zeros_like(tx), ch_s[t, cells], ch_f[t, cells]], axis=1))
    if extra is not None and len(extra):
        parts.append(extra[:, :5])
    return np.concatenate(parts, axis=0).astype(np.int32)


def render_map(out_sizes: dict[int, Path], tmap=None, chunks=None, ifix=None, extra=None,
               vga: ShapesVGA | None = None, log=print) -> dict[int, np.ndarray]:
    """Render and save one PNG per requested size (must divide 24576). Returns arrays."""
    t0 = time.time()
    tmap = load_map() if tmap is None else tmap
    chunks = load_chunks() if chunks is None else chunks
    ifix = load_ifix() if ifix is None else ifix
    vga = vga or ShapesVGA()
    pal = load_palette()
    cache = SpriteCache(vga, pal)
    ch_s, ch_f = chunks

    # flats per terrain template -> 128x128 RGB
    flat_cache: dict[int, np.ndarray] = {}

    def chunk_flat(t):
        img = flat_cache.get(t)
        if img is None:
            img = np.zeros((128, 128, 3), np.uint8)
            for c in range(256):
                s = int(ch_s[t, c])
                if s >= FIRST_OBJ_SHAPE:
                    continue
                sp = cache.get(s, int(ch_f[t, c]))
                if sp is None:
                    continue
                lx, ly = (c % 16) * 8, (c // 16) * 8
                img[ly:ly + 8, lx:lx + 8] = sp[0][:8, :8]
            flat_cache[t] = img
        return img

    objs = collect_objects(tmap, chunks, ifix, extra)
    log(f"render: {len(objs)} objects, setup {time.time() - t0:.1f}s")
    tx, ty, tz, shp, frm = objs.T
    order = np.argsort((tx + ty) * 64 + tz * 4, kind="stable")
    objs = objs[order]
    tx, ty, tz, shp, frm = objs.T
    n = len(objs)
    px = np.zeros(n, np.int32)
    py = np.zeros(n, np.int32)
    ph = np.zeros(n, np.int32)
    valid = np.zeros(n, bool)
    keys = shp.astype(np.int64) * 64 + frm
    uniq, inv = np.unique(keys, return_inverse=True)
    xl = np.zeros(len(uniq), np.int32)
    ya = np.zeros(len(uniq), np.int32)
    hh = np.zeros(len(uniq), np.int32)
    ok = np.zeros(len(uniq), bool)
    for k, key in enumerate(uniq):
        sp = cache.get(int(key // 64), int(key % 64))
        if sp is None:
            continue
        xl[k], ya[k], hh[k], ok[k] = sp[2], sp[3], sp[0].shape[0], True
    lift = 4 * tz
    px[:] = (tx + 1) * 8 - 1 - lift - xl[inv]
    py[:] = (ty + 1) * 8 - 1 - lift - ya[inv]
    ph[:] = hh[inv]
    valid[:] = ok[inv]
    log(f"render: placed {int(valid.sum())}/{n} ({len(uniq)} distinct sprites) {time.time() - t0:.1f}s")

    outs = {sz: np.zeros((sz, sz, 3), np.uint8) for sz in out_sizes}
    strip = np.zeros((STRIP_ROWS, MAP_PX, 3), np.uint8)
    for s in range(NSTRIPS):
        ts = time.time()
        y0 = s * STRIP_ROWS
        y1 = y0 + STRIP_ROWS
        strip[:] = 0
        for cy in range(y0 // 128, y1 // 128):
            ly = cy * 128 - y0
            for cx in range(NUM_CHUNKS):
                strip[ly:ly + 128, cx * 128:(cx + 1) * 128] = chunk_flat(int(tmap[cy, cx]))
        sel = np.nonzero(valid & (py < y1) & (py + ph > y0))[0]
        for k in sel:
            rgb, al, _, _ = cache.get(int(shp[k]), int(frm[k]))
            x, y = int(px[k]), int(py[k]) - y0
            h, w = al.shape
            xa, ya_ = max(x, 0), max(y, 0)
            xb, yb = min(x + w, MAP_PX), min(y + h, STRIP_ROWS)
            if xa >= xb or ya_ >= yb:
                continue
            src = rgb[ya_ - y:yb - y, xa - x:xb - x]
            m = al[ya_ - y:yb - y, xa - x:xb - x]
            dst = strip[ya_:yb, xa:xb]
            dst[m] = src[m]
        img = Image.fromarray(strip)
        for sz, arr in outs.items():
            f = MAP_PX // sz
            small = img.resize((sz, STRIP_ROWS // f), Image.BOX)
            arr[y0 // f:y1 // f] = np.asarray(small)
        log(f"render: strip {s + 1}/{NSTRIPS} {len(sel)} objects {time.time() - ts:.1f}s (total {time.time() - t0:.0f}s)")
    for sz, path in out_sizes.items():
        Path(path).parent.mkdir(parents=True, exist_ok=True)
        Image.fromarray(outs[sz]).save(path)
        log(f"render: wrote {path}")
    return outs
