"""The world round a shape, painted from the original art: the reference for
what a piece builds (a wall among its walls, a tile in its terrain).

Objects are placed as Exult's get_shape_location places them (the hotspot at
((tx + 1) * 8 - 1 - 4 * lift, (ty + 1) * 8 - 1 - 4 * lift)) and painted in
u7poster's order (tx + ty, then lift), close enough to the game's look for a
reference; the terrain is U7MAP's chunks (u7art.world_tiles). The objects
are the map's (U7CHUNKS, U7IFIX), INITGAME.DAT's IREG outside containers
(no eggs) and npc.dat's NPCs, as u7poster reads them.
"""

from __future__ import annotations

import sys
from functools import lru_cache

import numpy as np

import u7art

sys.path.insert(0, str(u7art.ROOT))
from u7poster.ireg import load_initgame, load_ireg_all  # noqa: E402
from u7poster.mapdata import load_chunks, load_ifix, load_map  # noqa: E402
from u7poster.npcs import load_npcs  # noqa: E402
from u7poster.render import collect_objects  # noqa: E402
from u7poster.shapes import ShapeInfo  # noqa: E402

WORLD_PX = 3072 * u7art.TILE
MARK = np.array([255, 0, 255], np.uint8)


class WorldView:
    def __init__(self, vga: u7art.VgaFile):
        self.vga = vga
        info = ShapeInfo.load()
        files = load_initgame()
        ireg, _ = load_ireg_all(info, files=files)
        npcs, _, _ = load_npcs(info, files=files)
        extra = [(o.tx, o.ty, o.tz, o.shape, o.frame) for o in ireg if o.depth == 0 and not o.is_egg]
        extra += [(n.tx, n.ty, n.tz, n.shape, n.frame) for n in npcs if not n.unused and (n.tx or n.ty)]
        # The chunk templates' own objects (U7CHUNKS) belong to the ground: the terrain views paint them too.
        template = collect_objects(load_map(), load_chunks(), np.zeros((0, 5), np.int32))
        placed = np.concatenate([load_ifix()[:, :5], np.array(extra, np.int32)]).astype(np.int32)
        objs = np.concatenate([template, placed])
        tx, ty, tz = objs[:, 0], objs[:, 1], objs[:, 2]
        order = np.argsort((tx + ty) * 64 + tz * 4, kind="stable")
        self.objs = objs[order]
        self.template = (np.arange(len(objs)) < len(template))[order]
        tx, ty, tz = self.objs[:, 0], self.objs[:, 1], self.objs[:, 2]
        self.hx = (tx + 1) * u7art.TILE - 1 - 4 * tz
        self.hy = (ty + 1) * u7art.TILE - 1 - 4 * tz
        # Exult Chunk_cache::update_object: solid objects with a height block
        # their footprint (x and y swapped in a reflected frame) at their lifts.
        shape = np.minimum(self.objs[:, 3], len(info.tfa) - 1)
        swap = (self.objs[:, 4] & 32) != 0
        self.xtiles = np.where(swap, info.ytiles[shape], info.xtiles[shape])
        self.ytiles = np.where(swap, info.xtiles[shape], info.ytiles[shape])
        known = self.objs[:, 3] < len(info.tfa)
        self.ztiles = np.where(known & ((info.tfa[shape, 0] & 0x08) != 0), info.ztiles[shape], 0)

    @property
    def covered(self) -> np.ndarray:
        """Tiles a chunk template's object paints over, even partly (cavern
        rock over a cave floor, drawn far beyond its footprint): the terrain
        views paint those objects, so the tile under them doesn't show whole."""
        if not hasattr(self, "_covered"):
            out = np.zeros((3072, 3072), bool)
            ks = np.flatnonzero(self.template)
            keys = self.objs[ks, 3].astype(np.int64) * 4096 + self.objs[ks, 4] * 64 + self.objs[ks, 2]
            for key in np.unique(keys):
                shape, frame, lift = int(key // 4096), int(key // 64 % 64), int(key % 64)
                offsets = self._touched(shape, frame, lift)
                if not len(offsets):
                    continue
                at = ks[keys == key]
                ty = self.objs[at, 1][:, None] + offsets[None, :, 0]
                tx = self.objs[at, 0][:, None] + offsets[None, :, 1]
                ok = (tx >= 0) & (tx < 3072) & (ty >= 0) & (ty < 3072)
                out[ty[ok], tx[ok]] = True
            self._covered = out
        return self._covered

    def _touched(self, shape: int, frame: int, lift: int) -> np.ndarray:
        """The tiles (dy, dx from the object's tile) a frame's opaque pixels
        touch, placed as get_shape_location places it."""
        fr = self.frame(shape, frame)
        if fr is None:
            return np.zeros((0, 2), np.int64)
        # The frame's top-left in pixels from the object's tile's top-left.
        ox = u7art.TILE - 1 - 4 * lift - fr.xleft
        oy = u7art.TILE - 1 - 4 * lift - fr.yabove
        ys, xs = np.nonzero(fr.opaque)
        tiles = np.unique(np.stack([(ys + oy) // u7art.TILE, (xs + ox) // u7art.TILE], 1), axis=0)
        return tiles.astype(np.int64)

    def roof_over(self, k: int) -> int:
        """Exult Map_chunk::is_roof at the object's tile: the lowest blocked
        lift from its lift + 4 up, 255 where nothing is above it. The game
        doesn't paint that lift and up while the avatar stands there, so a
        roof hides nothing under it in the view, and a roof piece keeps the
        roofs round it."""
        tx, ty, lift = (int(v) for v in self.objs[k, :3])
        above = lift + 4
        tops = self.objs[:, 2] + self.ztiles - 1
        over = np.flatnonzero((self.ztiles > 0) & (tops >= above)
                              & (self.objs[:, 0] >= tx) & (self.objs[:, 0] - self.xtiles < tx)
                              & (self.objs[:, 1] >= ty) & (self.objs[:, 1] - self.ytiles < ty))
        if not len(over):
            return 255
        return int(np.maximum(self.objs[over, 2], above).min())

    @lru_cache(maxsize=None)
    def frame(self, shape: int, frame: int) -> u7art.Frame | None:
        """The frame, or for a missing frame 32 and up the lower one reflected (Exult's reflect)."""
        fr = self.vga.frame(shape, frame)
        if fr is None and frame & 32:
            b = self.vga.frame(shape, frame & 31)
            if b is not None:
                fr = u7art.Frame(b.yabove, b.xleft, b.ybelow, b.xright, b.rle, b.index.T.copy(), b.opaque.T.copy())
        return fr

    def instance(self, shape: int) -> int | None:
        """The shape's instance in the 8x8-tile cell showing the most of its
        frames, then the most of it: a wall among its walls."""
        found = np.flatnonzero(self.objs[:, 3] == shape)
        if not len(found):
            return None
        cell = (self.objs[found, 0] // 8) * 4096 + self.objs[found, 1] // 8
        cells, counts = np.unique(cell, return_counts=True)
        kinds = np.unique(cell * 64 + self.objs[found, 4])
        distinct = np.searchsorted(kinds // 64, cells, side="right") - np.searchsorted(kinds // 64, cells)
        inside = found[cell == cells[np.argmax(distinct * 100000 + counts)]]
        cx, cy = self.objs[inside, 0].mean(), self.objs[inside, 1].mean()
        return int(inside[np.argmin((self.objs[inside, 0] - cx) ** 2 + (self.objs[inside, 1] - cy) ** 2)])

    def bounds(self, k: int) -> tuple[int, int, int, int] | None:
        """The instance's painted rectangle (x, y, w, h) in world pixels."""
        fr = self.frame(int(self.objs[k, 3]), int(self.objs[k, 4]))
        if fr is None:
            return None
        return int(self.hx[k]) - fr.xleft, int(self.hy[k]) - fr.yabove, fr.width, fr.height

    def paint(self, x0: int, y0: int, w: int, h: int, objects: bool = True, skip_lift: int = 255) -> np.ndarray:
        """The world's pixels in the rectangle: terrain, the chunk templates'
        objects, and (unless not) the placed objects below `skip_lift`
        (Exult's render skip lift: objects there and up aren't painted)."""
        pal = u7art.day_palette()
        out = np.zeros((h, w, 3), np.uint8)
        tiles = u7art.world_tiles()
        for ty in range(y0 // 8, (y0 + h - 1) // 8 + 1):
            for tx in range(x0 // 8, (x0 + w - 1) // 8 + 1):
                if not (0 <= tx < 3072 and 0 <= ty < 3072) or tiles[ty, tx] < 0:
                    continue
                t = int(tiles[ty, tx])
                fr = self.vga.frame(t >> 5, t & 31)
                if fr is not None:
                    self._blit(out, x0, y0, tx * 8, ty * 8, pal[fr.index], fr.opaque)
        near = np.flatnonzero((self.hx >= x0 - 80) & (self.hx < x0 + w + 80)
                              & (self.hy >= y0 - 80) & (self.hy < y0 + h + 80)
                              & (self.objs[:, 2] < skip_lift) & (self.template | objects))
        if len(near):
            for k in near:
                fr = self.frame(int(self.objs[k, 3]), int(self.objs[k, 4]))
                if fr is not None:
                    self._blit(out, x0, y0, int(self.hx[k]) - fr.xleft, int(self.hy[k]) - fr.yabove,
                               pal[fr.index], fr.opaque)
        return out

    def paint_art(self, art: "PaintedArt", x0: int, y0: int, w: int, h: int, objects: bool = True,
                  skip_lift: int = 255) -> np.ndarray:
        """As paint, at the art's scale, each frame from `art` (a painting where there is one)."""
        s = art.scale
        out = np.zeros((h * s, w * s, 3), np.uint8)
        tiles = u7art.world_tiles()
        for ty in range(y0 // 8, (y0 + h - 1) // 8 + 1):
            for tx in range(x0 // 8, (x0 + w - 1) // 8 + 1):
                if not (0 <= tx < 3072 and 0 <= ty < 3072) or tiles[ty, tx] < 0:
                    continue
                t = int(tiles[ty, tx])
                got = art.frame(t >> 5, t & 31)
                if got is not None:
                    self._blit(out, x0 * s, y0 * s, tx * 8 * s, ty * 8 * s, *got)
        near = np.flatnonzero((self.hx >= x0 - 80) & (self.hx < x0 + w + 80)
                              & (self.hy >= y0 - 80) & (self.hy < y0 + h + 80)
                              & (self.objs[:, 2] < skip_lift) & (self.template | objects))
        for k in near:
            shape, frame = int(self.objs[k, 3]), int(self.objs[k, 4])
            fr, got = self.frame(shape, frame), art.frame(shape, frame)
            if fr is not None and got is not None:
                self._blit(out, x0 * s, y0 * s, (int(self.hx[k]) - fr.xleft) * s, (int(self.hy[k]) - fr.yabove) * s, *got)
        return out

    def view_rect(self, view: dict) -> tuple[int, int, int, bool, int] | None:
        """A world view's square (x, y, side), whether it shows the placed
        objects and its skip lift, from what set.json keeps of it, as sets.py
        chose it: terrain by its tiles, else round the instance."""
        from sets import TERRAIN_WINDOW

        if "lift" not in view:
            return view["tile_x"] * u7art.TILE, view["tile_y"] * u7art.TILE, view.get("tiles", TERRAIN_WINDOW) * u7art.TILE, False, 255
        hit = np.flatnonzero((self.objs[:, 0] == view["tile_x"]) & (self.objs[:, 1] == view["tile_y"])
                             & (self.objs[:, 2] == view["lift"]) & (self.objs[:, 4] == view["frame"])
                             & (self.objs[:, 3] == view["shape"]))
        if not len(hit):
            return None
        k = int(hit[0])
        x0, y0, side = crop_round(*self.bounds(k), least=160, most=288)
        return x0, y0, side, True, self.roof_over(k)

    def silhouette(self, k: int, x0: int, y0: int, w: int, h: int) -> np.ndarray:
        fr = self.frame(int(self.objs[k, 3]), int(self.objs[k, 4]))
        out = np.zeros((h, w), bool)
        if fr is not None:
            self._blit(out, x0, y0, int(self.hx[k]) - fr.xleft, int(self.hy[k]) - fr.yabove, fr.opaque, fr.opaque)
        return out

    @staticmethod
    def _blit(dst: np.ndarray, x0: int, y0: int, x: int, y: int, src: np.ndarray, mask: np.ndarray) -> None:
        h, w = mask.shape
        ax, ay = max(x, x0), max(y, y0)
        bx, by = min(x + w, x0 + dst.shape[1]), min(y + h, y0 + dst.shape[0])
        if ax >= bx or ay >= by:
            return
        m = mask[ay - y:by - y, ax - x:bx - x]
        view = dst[ay - y0:by - y0, ax - x0:bx - x0]
        view[m] = src[ay - y:by - y, ax - x:bx - x][m]


def crop_round(x: int, y: int, w: int, h: int, least: int, most: int) -> tuple[int, int, int]:
    """A square (x, y, side) round the rectangle with room about it, inside the world."""
    side = int(min(most, max(least, max(w, h) + 96)))
    cx, cy = x + w // 2, y + h // 2
    sx = min(max(0, cx - side // 2), WORLD_PX - side)
    sy = min(max(0, cy - side // 2), WORLD_PX - side)
    return sx, sy, side


def terrain_windows(shape: int, side_tiles: int, hidden: np.ndarray, most: int = 4,
                    step: int = 4) -> list[tuple[int, int]]:
    """Top-left tiles of squares (on a `step`-tile grid) that between them show
    every frame of the terrain shape the world shows, up to `most`: each the
    square showing the most frames not shown yet, then the most of their
    tiles; tiles `hidden` (under an object) don't count."""
    tiles = u7art.world_tiles()
    ys, xs = np.nonzero((tiles >= 0) & ((tiles >> 5) == shape) & ~hidden)
    if not len(ys):
        return []
    blocks = 3072 // step
    counts = np.zeros((32, blocks, blocks), np.int32)
    np.add.at(counts, (tiles[ys, xs] & 31, ys // step, xs // step), 1)
    n = side_tiles // step
    s = np.pad(counts.cumsum(1).cumsum(2), ((0, 0), (1, 0), (1, 0)))
    box = s[:, n:, n:] - s[:, :-n, n:] - s[:, n:, :-n] + s[:, :-n, :-n]
    left = counts.reshape(32, -1).any(1)
    out = []
    while left.any() and len(out) < most:
        mine = box[left]
        score = (mine > 0).sum(0).astype(np.int64) * 1000000 + mine.sum(0)
        by, bx = np.unravel_index(int(np.argmax(score)), score.shape)
        out.append((int(bx) * step, int(by) * step))
        left &= box[:, by, bx] == 0
    return out


def outline(mask: np.ndarray, width: int) -> np.ndarray:
    """The ring just outside the mask, `width` pixels wide."""
    grown = mask.copy()
    for _ in range(width):
        g = grown.copy()
        g[1:] |= grown[:-1]
        g[:-1] |= grown[1:]
        g[:, 1:] |= grown[:, :-1]
        g[:, :-1] |= grown[:, 1:]
        grown = g
    return grown & ~mask


class PaintedArt:
    """Frames at 4x as a pack would make them from the paintings (pack.py):
    the painting's own outline where it has a transparent ground and may
    keep it (own_outline), else the original's (upscale_mask); the colour
    cycling and see-through pixels the original's; frames not painted (or of
    the wrong size) the original blown up. Colours aren't turned into the
    palette's, so it is a close preview, not the game's exact look."""

    def __init__(self, view: WorldView, work):
        from generate import SCALE, is_translucent

        self.view, self.work, self.scale = view, work, SCALE
        self.translucent = u7art.translucent_shapes()
        self.is_translucent = is_translucent
        self.painted: set[tuple[int, int]] = set()
        self._cache: dict[tuple[int, int], tuple[np.ndarray, np.ndarray] | None] = {}

    def frame(self, shape: int, frame: int) -> tuple[np.ndarray, np.ndarray] | None:
        """(rgb, mask) at 4x, or None where the frame doesn't exist."""
        key = (shape, frame)
        if key not in self._cache:
            self._cache[key] = self._make(shape, frame)
        return self._cache[key]

    def _make(self, shape: int, frame: int) -> tuple[np.ndarray, np.ndarray] | None:
        from PIL import Image
        from pack import own_outline, upscale_mask

        fr = self.view.vga.frame(shape, frame)
        if fr is None:
            # Exult's reflection of the lower frame, made from its painting too.
            base = self.frame(shape, frame & 31) if frame & 32 else None
            return None if base is None else (base[0].transpose(1, 0, 2).copy(), base[1].T.copy())
        s = self.scale
        big = lambda a: np.repeat(np.repeat(a, s, 0), s, 1)  # noqa: E731
        rgb = big(u7art.day_palette()[fr.index])
        mask = big(fr.opaque)
        path = self.work / f"{shape:04d}_{frame:02d}.png"
        if path.exists():
            with Image.open(path) as img:
                if img.size == (fr.width * s, fr.height * s):
                    alpha = np.asarray(img.getchannel("A")) if "A" in img.getbands() else None
                    painted = np.asarray(img.convert("RGB"))
                    own = own_outline(fr, alpha)
                    mask = upscale_mask(fr) if own is None else own
                    keep = big(u7art.special_mask(fr, self.is_translucent("shapes", shape, self.translucent)))
                    rgb = np.where(keep[..., None], rgb, painted)
                    self.painted.add((shape, frame))
        return rgb, mask
