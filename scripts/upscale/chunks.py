"""The world's chunk templates: their ground, how much of the map each covers.

The ground is built of chunk templates (U7CHUNKS, Exult's Chunk_terrain:
16 x 16 tiles), which the map (U7MAP) uses again and again: 2,105 of them.
ground.py assembles each from painted materials into
assets/hires/work/<pack>/chunks/TTTT.png (512 x 512); pack.py --file chunks
makes them the pack's chunks.hrp, and the game draws a template so painted as
the chunk's ground in one piece (WorldView.GetFlats), its tiles where it isn't.
"""

from __future__ import annotations

import collections
from functools import lru_cache

import numpy as np

import u7art

SIDE = 16 * u7art.TILE


@lru_cache(maxsize=None)
def shape_names() -> dict[int, str]:
    """TEXT.FLX's shape names without Exult's article and plural pattern."""
    out = {}
    for i, raw in enumerate(u7art.read_flex(u7art.STATIC / "TEXT.FLX")[:1024]):
        parts = [p for p in raw.split(b"\0")[0].decode("latin-1").split("/") if p]
        out[i] = max(parts, key=len).strip() if parts else ""
    return out


@lru_cache(maxsize=None)
def vga() -> u7art.VgaFile:
    return u7art.game_file("shapes")


@lru_cache(maxsize=None)
def ground(t: int) -> tuple[np.ndarray, np.ndarray]:
    """A template's ground as Exult's render_flats paints it: palette indices
    (128 x 128) and which pixels have a tile (the others lie under one of the
    template's own objects)."""
    shapes, frames = u7art.chunk_templates()
    index = np.zeros((SIDE, SIDE), np.uint8)
    has = np.zeros((SIDE, SIDE), bool)
    for i in range(256):
        s, f = int(shapes[t, i]), int(frames[t, i])
        fr = vga().frame(s, f) if vga().is_tile(s) else None
        if fr is None:
            continue
        y, x = (i // 16) * u7art.TILE, (i % 16) * u7art.TILE
        index[y:y + u7art.TILE, x:x + u7art.TILE] = fr.index
        has[y:y + u7art.TILE, x:x + u7art.TILE] = True
    return index, has


def template_frame(t: int) -> u7art.Frame:
    """A template's ground as a 128 x 128 terrain frame, for pack.py."""
    index, _ = ground(t)
    return u7art.Frame(SIDE, SIDE, -1, -1, False, index, np.ones_like(index, dtype=bool))


@lru_cache(maxsize=None)
def coverage() -> dict[int, dict]:
    """The used templates: how often the map uses each, its share of the map,
    and the 3 x 3 templates round the occurrence whose neighbours are the
    most usual ones (for showing it among them)."""
    tmap = u7art.chunk_map()
    counts = collections.Counter(tmap.ravel().tolist())
    rows = {}
    for t, n in counts.items():
        where = np.argwhere(tmap == t)
        common = {(dy, dx): collections.Counter(int(tmap[(cy + dy) % 192, (cx + dx) % 192]) for cy, cx in where)
                  .most_common(1)[0][0] for dy in (-1, 0, 1) for dx in (-1, 0, 1)}
        best, score = None, -1
        for cy, cx in where:
            s = sum(int(tmap[(cy + dy) % 192, (cx + dx) % 192]) == want for (dy, dx), want in common.items())
            if s > score:
                score, best = s, (int(cy), int(cx))
        cy, cx = best
        rows[t] = {"t": t, "n": n, "share": n / tmap.size, "at": [cx, cy],
                   "around": [int(tmap[(cy + dy) % 192, (cx + dx) % 192]) for dy in (-1, 0, 1) for dx in (-1, 0, 1)]}
    return rows
