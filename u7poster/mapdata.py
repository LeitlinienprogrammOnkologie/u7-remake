"""U7MAP, U7CHUNKS and U7IFIXnn readers.

References (Exult 1.12.1 gamemap.cc):
  * Game_map::init()           - U7MAP: 144 superchunks x 512 bytes, each a
                                  16x16 little-endian u16 terrain (chunk) number,
                                  superchunk s covers chunks (16*(s%12), 16*(s/12)).
  * Game_map::init_chunks() / read_terrain() and objs/chunkter.cc
    Chunk_terrain::Chunk_terrain() - U7CHUNKS: 512 bytes per terrain, 256 u16
                                  cells (tiley*16+tilex): shape = lo + 256*(hi&3),
                                  frame = (hi>>2)&0x1f.
  * Game_map::get_ifix_objects() / get_ifix_chunk_objects() - U7IFIXnn is a
                                  flex with 256 entries (chunk cy*16+cx); each
                                  entry is 4 bytes per object: tx=b0>>4, ty=b0&15,
                                  tz=b1&15, shape=b2+256*(b3&3), frame=b3>>2
                                  (5-byte layout only for Exult v2 flexes).
"""
from __future__ import annotations
from pathlib import Path

import numpy as np

from .flex import Flex
from .paths import STATIC, NUM_SCHUNKS, CHUNKS_PER_SCHUNK, NUM_CHUNKS, TILES_PER_CHUNK

TERRAIN_BYTES = TILES_PER_CHUNK * TILES_PER_CHUNK * 2  # 512


def load_map(static: Path = STATIC) -> np.ndarray:
    """Returns tmap[cy, cx] -> terrain number, shape (192, 192)."""
    data = (static / "U7MAP").read_bytes()
    expected = NUM_SCHUNKS * NUM_SCHUNKS * TERRAIN_BYTES
    if len(data) != expected:
        raise ValueError(f"U7MAP is {len(data)} bytes, expected {expected}")
    tmap = np.zeros((NUM_CHUNKS, NUM_CHUNKS), np.int32)
    for s in range(NUM_SCHUNKS * NUM_SCHUNKS):
        scy = CHUNKS_PER_SCHUNK * (s // NUM_SCHUNKS)
        scx = CHUNKS_PER_SCHUNK * (s % NUM_SCHUNKS)
        block = np.frombuffer(data, dtype="<u2", count=256, offset=s * TERRAIN_BYTES).reshape(16, 16)
        tmap[scy:scy + 16, scx:scx + 16] = block  # rows = cy, cols = cx
    return tmap


def load_chunks(static: Path = STATIC):
    """Returns (shapes, frames) each (num_terrains, 256) int32; cell index = tiley*16 + tilex."""
    data = (static / "U7CHUNKS").read_bytes()
    n = len(data) // TERRAIN_BYTES
    raw = np.frombuffer(data, dtype="<u2", count=n * 256).reshape(n, 256)
    shapes = (raw & 0x3FF).astype(np.int32)
    frames = ((raw >> 10) & 0x1F).astype(np.int32)
    return shapes, frames


def load_ifix(static: Path = STATIC) -> np.ndarray:
    """All fixed objects as an int32 array with columns (tx, ty, tz, shape, frame, schunk).

    tx/ty are absolute tile coordinates (0..3071) of the object's hot spot
    (its south-east tile); the footprint extends to the north-west by the
    TFA 3d x/y tile dims (Game_object::get_footprint()).
    """
    rows = []
    for s in range(NUM_SCHUNKS * NUM_SCHUNKS):
        path = static / f"U7IFIX{s:02X}"
        if not path.exists():
            raise FileNotFoundError(path)
        fl = Flex.open(path)
        if fl.count != 256:
            raise ValueError(f"{path.name}: {fl.count} entries, expected 256")
        scy = CHUNKS_PER_SCHUNK * (s // NUM_SCHUNKS)
        scx = CHUNKS_PER_SCHUNK * (s % NUM_SCHUNKS)
        for idx in range(256):
            ent = fl.get(idx)
            if not ent:
                continue
            cx = scx + idx % 16
            cy = scy + idx // 16
            if fl.version == 0:
                a = np.frombuffer(ent[:len(ent) // 4 * 4], dtype=np.uint8).reshape(-1, 4).astype(np.int32)
                tx = (a[:, 0] >> 4) & 15
                ty = a[:, 0] & 15
                tz = a[:, 1] & 15
                shape = a[:, 2] + 256 * (a[:, 3] & 3)
                frame = a[:, 3] >> 2
            else:
                a = np.frombuffer(ent[:len(ent) // 5 * 5], dtype=np.uint8).reshape(-1, 5).astype(np.int32)
                tx = (a[:, 0] >> 4) & 15
                ty = a[:, 0] & 15
                tz = a[:, 1] & 15
                shape = a[:, 2] + 256 * a[:, 3]
                frame = a[:, 4]
            block = np.stack([cx * 16 + tx, cy * 16 + ty, tz, shape, frame, np.full_like(tx, s)], axis=1)
            rows.append(block)
    return np.concatenate(rows, axis=0) if rows else np.zeros((0, 6), np.int32)
