"""Per-tile terrain class raster (3072 x 3072) from U7CHUNKS + U7MAP + U7IFIX.

Object footprints follow Game_object::get_footprint() in objs/objs.h: an
object at hot-spot tile (tx, ty) with TFA dims (xt, yt) covers
tiles [tx - xt + 1 .. tx] x [ty - yt + 1 .. ty].
"""
from __future__ import annotations
from dataclasses import dataclass

import numpy as np

from .mapdata import load_map, load_chunks, load_ifix
from .paths import NUM_TILES, NUM_CHUNKS, TILES_PER_CHUNK
from .shapes import ShapeInfo, load_shape_names, FIRST_OBJ_SHAPE, NUM_SHAPES

# class ids (also the painting priority: higher wins)
VOID, LAND, SAND, SWAMP, WATER, CAVE, ROAD, FOREST, DOCK, STRUCTURE, MOUNTAIN = range(11)
CLASS_NAMES = ["void", "land", "sand", "swamp", "water", "cave", "road", "forest", "dock", "structure", "mountain"]
CLASS_RGB = {
    VOID: (0, 0, 0), LAND: (96, 140, 60), SAND: (214, 196, 140), SWAMP: (70, 100, 60),
    WATER: (40, 70, 170), CAVE: (90, 80, 70), ROAD: (170, 140, 100), FOREST: (40, 90, 40),
    DOCK: (150, 110, 60), STRUCTURE: (200, 60, 50), MOUNTAIN: (215, 215, 225),
}

# name keyword -> class. Checked in order; first hit wins.
NAME_RULES = [
    ("swamp", SWAMP), ("reeds", SWAMP), ("cattails", SWAMP),
    ("void", VOID),
    ("cavefloor", CAVE),
    ("sandy grass", LAND), ("sandy ground", SAND), ("sand", SAND),
    ("cobblestone", ROAD), ("sidewalk", ROAD), ("rut", ROAD), ("dirt", ROAD), ("stone floor", ROAD), ("path", ROAD),
    ("planking", DOCK), ("bridge", DOCK), ("drawbridge", DOCK),
    ("mountain", MOUNTAIN), ("cavern", MOUNTAIN), ("delta", MOUNTAIN),
    ("evergreen", FOREST), ("tree", FOREST), ("cypress", FOREST), ("baobab", FOREST), ("cactus", FOREST),
    ("tropical plant", FOREST), ("stump", LAND),
    ("wall", STRUCTURE), ("roof", STRUCTURE), ("floor", STRUCTURE), ("carpet", STRUCTURE), ("rug", STRUCTURE),
    ("tile", STRUCTURE), ("window", STRUCTURE), ("door", STRUCTURE), ("pillar", STRUCTURE), ("column", STRUCTURE),
    ("crenellations", STRUCTURE), ("monolith", STRUCTURE), ("standing stone", STRUCTURE), ("pedestal", STRUCTURE),
    ("altar", STRUCTURE), ("statue", STRUCTURE), ("chimney", STRUCTURE), ("bars", STRUCTURE), ("fortress", STRUCTURE),
    ("portcullis", STRUCTURE), ("gate", STRUCTURE), ("stairs", STRUCTURE), ("well", STRUCTURE), ("fence", STRUCTURE),
    ("water wheel", STRUCTURE), ("mast", STRUCTURE), ("ship", STRUCTURE), ("sails", STRUCTURE),
    ("grass", LAND), ("mud", LAND), ("ford", LAND), ("muck", LAND), ("rock", LAND), ("boulder", LAND),
    ("weeds", LAND), ("bush", LAND), ("brambles", LAND), ("fern", LAND), ("plant", LAND), ("crops", LAND),
    ("lily", WATER), ("surf", WATER), ("waves", WATER), ("bubbles", WATER), ("water", WATER), ("spring", WATER),
]


def shape_classes(names: list[str], info: ShapeInfo) -> np.ndarray:
    """Class per shape number, -1 = no class (transparent for the raster)."""
    cls = np.full(NUM_SHAPES, -1, np.int32)
    tfa_cls = info.classes
    water = info.water_mask
    for s in range(NUM_SHAPES):
        n = names[s].lower()
        c = -1
        for kw, k in NAME_RULES:
            if kw in n:
                c = k
                break
        if c == -1:
            if water[s]:
                c = WATER
            elif s < FIRST_OBJ_SHAPE:
                c = LAND
            elif tfa_cls[s] == 14:  # building
                c = STRUCTURE
        if c == SWAMP or c == WATER or c == LAND:
            pass
        elif water[s] and c not in (SWAMP, DOCK):
            c = WATER
        cls[s] = c
    return cls


@dataclass
class TerrainRaster:
    cls: np.ndarray           # (3072, 3072) uint8 class ids
    tree_count: np.ndarray    # (192, 192) trees per chunk
    struct_count: np.ndarray  # (192, 192) structure objects per chunk

    @property
    def water(self):
        return self.cls == WATER

    @property
    def land(self):
        return (self.cls != WATER) & (self.cls != VOID)


def build_terrain_raster(names=None, info: ShapeInfo | None = None, tmap=None, chunks=None, ifix=None,
                         extra_objects: np.ndarray | None = None) -> TerrainRaster:
    names = names or load_shape_names()
    info = info or ShapeInfo.load()
    tmap = load_map() if tmap is None else tmap
    ch_s, ch_f = load_chunks() if chunks is None else chunks
    ifix = load_ifix() if ifix is None else ifix
    scls = shape_classes(names, info)
    xt, yt = info.xtiles, info.ytiles
    is_flat = np.zeros(NUM_SHAPES, bool)
    is_flat[:FIRST_OBJ_SHAPE] = True

    # 1. flats: per terrain template 16x16 class, then tile through the map.
    flat_cls = np.where(is_flat[ch_s], scls[ch_s], -1)          # (nterr, 256)
    flat_cls = np.where(flat_cls < 0, LAND, flat_cls).astype(np.uint8)
    tmpl = flat_cls.reshape(-1, 16, 16)
    grid = tmpl[tmap]                                            # (192cy, 192cx, 16ty, 16tx)
    prio = grid.transpose(0, 2, 1, 3).reshape(NUM_TILES, NUM_TILES).astype(np.uint8)

    # 2. RLE cells embedded in chunk templates -> instances
    inst = []
    rle_mask = ~is_flat[ch_s]
    for t in range(ch_s.shape[0]):
        cells = np.nonzero(rle_mask[t])[0]
        if not len(cells):
            continue
        locs = np.argwhere(tmap == t)   # (n, 2) [cy, cx]
        if not len(locs):
            continue
        shp = ch_s[t, cells]
        c = scls[shp]
        keep = c >= 0
        if not keep.any():
            continue
        cells, shp, c = cells[keep], shp[keep], c[keep]
        for cy, cx in locs:
            tx = cx * 16 + (cells % 16)
            ty = cy * 16 + (cells // 16)
            inst.append(np.stack([tx, ty, shp, c], axis=1))
    # 3. IFIX
    c = scls[ifix[:, 3]]
    keep = c >= 0
    inst.append(np.stack([ifix[keep, 0], ifix[keep, 1], ifix[keep, 3], c[keep]], axis=1))
    if extra_objects is not None and len(extra_objects):
        c = scls[extra_objects[:, 3]]
        keep = c >= 0
        inst.append(np.stack([extra_objects[keep, 0], extra_objects[keep, 1], extra_objects[keep, 3], c[keep]], axis=1))
    allobj = np.concatenate(inst, axis=0)
    # paint by ascending priority so higher classes overwrite
    order = np.argsort(allobj[:, 3], kind="stable")
    allobj = allobj[order]
    N = NUM_TILES
    allobj = allobj.astype(np.int64)
    for tx, ty, s, cl in allobj.tolist():
        w, h = xt[s], yt[s]
        x0 = max(tx - w + 1, 0)
        y0 = max(ty - h + 1, 0)
        x1 = min(tx + 1, N)
        y1 = min(ty + 1, N)
        if x1 > x0 and y1 > y0:
            blk = prio[y0:y1, x0:x1]
            np.maximum(blk, np.uint8(cl), out=blk)

    # docks: planking / bridges count as DOCK only near water, else structure
    dock = prio == DOCK
    if dock.any():
        wat = prio == WATER
        near = np.zeros_like(wat)
        for dy in range(-2, 3):
            for dx in range(-2, 3):
                near |= np.roll(np.roll(wat, dy, 0), dx, 1)
        prio[dock & ~near] = STRUCTURE

    tree_count = np.zeros((NUM_CHUNKS, NUM_CHUNKS), np.int32)
    struct_count = np.zeros((NUM_CHUNKS, NUM_CHUNKS), np.int32)
    trees = allobj[allobj[:, 3] == FOREST]
    np.add.at(tree_count, (trees[:, 1] // 16, trees[:, 0] // 16), 1)
    st = allobj[allobj[:, 3] == STRUCTURE]
    np.add.at(struct_count, (st[:, 1] // 16, st[:, 0] // 16), 1)
    return TerrainRaster(prio, tree_count, struct_count)


def class_rgb_image(cls: np.ndarray) -> np.ndarray:
    lut = np.zeros((256, 3), np.uint8)
    for k, rgb in CLASS_RGB.items():
        lut[k] = rgb
    return lut[cls]
