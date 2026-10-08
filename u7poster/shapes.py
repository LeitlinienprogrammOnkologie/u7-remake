"""SHAPES.VGA / TFA.DAT / SHPDIMS.DAT / TEXT.FLX / PALETTES.FLX readers.

References (Exult 1.12.1):
  * shapes/vgafile.cc  Shape::read()          - entry table at 0x80 + 8*shapenum
                        Shape_frame::read()    - RLE detection, frame offsets
                        Shape_frame::get_rle_shape() - xright,xleft,yabove,ybelow
  * imagewin/ibuf8.cc  Image_buffer8::paint_rle() - RLE scanline decoding
  * shapes/shapevga.cc Shapes_vga_file::read_info() - TFA.DAT is 3 bytes/shape
                        (1024 shapes), SHPDIMS.DAT 2 bytes/shape from shape 0x96
  * shapes/shapeinf.h  tfa bit fields (is_water = tfa[0]&0x10, is_animated
                        tfa[0]&4, has_sfx tfa[0]&1, shape class tfa[1]&15,
                        3d dims: x=1+(tfa[2]&7), y=1+((tfa[2]>>3)&7), z=tfa[0]>>5)
  * shapes/items.cc    Setup_item_names() - TEXT.FLX entries 0..0x3ff are shape
                        names, 0x400..0x4ff text msgs, 0x500..0x5ff misc names
  * imagewin/iwin8.cc  Get_color8() - palette values are 0..63 (max_val 63)
"""
from __future__ import annotations
import struct
from dataclasses import dataclass
from pathlib import Path

import numpy as np

from .flex import Flex
from .paths import STATIC, TILE_PX

FIRST_OBJ_SHAPE = 0x96  # shapevga.h c_first_obj_shape: 0..0x95 are 8x8 flats
NUM_SHAPES = 1024
TILE_BYTES = TILE_PX * TILE_PX

SHAPE_CLASS = {0: "unusable", 2: "quality", 3: "quantity", 4: "has_hp", 5: "quality_flags",
               6: "container", 7: "hatchable", 8: "spellbook", 9: "barge", 11: "virtue_stone",
               12: "monster", 13: "human", 14: "building"}


def load_shape_names(static: Path = STATIC) -> list[str]:
    fl = Flex.open(static / "TEXT.FLX")
    names = []
    for i in range(min(fl.count, NUM_SHAPES)):
        b = fl.get(i)
        names.append(b.split(b"\0")[0].decode("latin1") if b else "")
    while len(names) < NUM_SHAPES:
        names.append("")
    return names


@dataclass
class ShapeInfo:
    tfa: np.ndarray  # (1024, 3) uint8

    @classmethod
    def load(cls, static: Path = STATIC) -> "ShapeInfo":
        raw = (static / "TFA.DAT").read_bytes()
        tfa = np.frombuffer(raw[:3 * NUM_SHAPES], dtype=np.uint8).reshape(NUM_SHAPES, 3).copy()
        return cls(tfa)

    def is_water(self, s): return bool(self.tfa[s, 0] & 0x10)
    def is_animated(self, s): return bool(self.tfa[s, 0] & 0x04)
    def has_sfx(self, s): return bool(self.tfa[s, 0] & 0x01)
    def is_solid(self, s): return bool(self.tfa[s, 0] & 0x08)
    def shape_class(self, s): return int(self.tfa[s, 1] & 15)
    def is_npc(self, s): return self.shape_class(s) in (12, 13)

    def dims(self, s):
        """(xtiles, ytiles, ztiles) - Shape_info::get_3d_xtiles etc."""
        return 1 + int(self.tfa[s, 2] & 7), 1 + int((self.tfa[s, 2] >> 3) & 7), int(self.tfa[s, 0] >> 5)

    @property
    def water_mask(self) -> np.ndarray:
        return (self.tfa[:, 0] & 0x10) != 0

    @property
    def classes(self) -> np.ndarray:
        return (self.tfa[:, 1] & 15).astype(np.int32)

    @property
    def xtiles(self) -> np.ndarray:
        return 1 + (self.tfa[:, 2] & 7).astype(np.int32)

    @property
    def ytiles(self) -> np.ndarray:
        return 1 + ((self.tfa[:, 2] >> 3) & 7).astype(np.int32)

    @property
    def ztiles(self) -> np.ndarray:
        return (self.tfa[:, 0] >> 5).astype(np.int32)


def load_shpdims(static: Path = STATIC) -> np.ndarray:
    """SHPDIMS.DAT: 2 bytes per shape starting at shape 0x96 (read_info())."""
    raw = (static / "SHPDIMS.DAT").read_bytes()
    n = len(raw) // 2
    out = np.zeros((NUM_SHAPES, 2), np.uint8)
    arr = np.frombuffer(raw[:2 * n], dtype=np.uint8).reshape(n, 2)
    out[FIRST_OBJ_SHAPE:FIRST_OBJ_SHAPE + n] = arr[:NUM_SHAPES - FIRST_OBJ_SHAPE]
    return out


def load_palette(static: Path = STATIC, num: int = 0) -> np.ndarray:
    """Returns (256, 3) uint8 RGB. Palette entries are 6-bit (Get_color8 with max_val 63)."""
    fl = Flex.open(static / "PALETTES.FLX")
    raw = np.frombuffer(fl.get(num)[:768], dtype=np.uint8).reshape(256, 3).astype(np.int32)
    return np.clip(raw * 255 // 63, 0, 255).astype(np.uint8)


@dataclass
class Frame:
    pixels: np.ndarray   # (h, w) uint8 palette indices
    alpha: np.ndarray    # (h, w) bool
    xleft: int           # hot spot offset from left edge  (Shape_frame::xleft)
    yabove: int          # hot spot offset from top edge   (Shape_frame::yabove)
    is_rle: bool

    @property
    def w(self): return self.pixels.shape[1]

    @property
    def h(self): return self.pixels.shape[0]


class ShapesVGA:
    """Lazy decoder for SHAPES.VGA (a flex with one shape per entry)."""

    def __init__(self, static: Path = STATIC):
        self.data = (static / "SHAPES.VGA").read_bytes()
        self.count = struct.unpack_from("<I", self.data, 0x54)[0]
        self._cache: dict[tuple[int, int], Frame | None] = {}
        self._nframes: dict[int, int] = {}

    def _entry(self, shapenum: int):
        return struct.unpack_from("<II", self.data, 0x80 + shapenum * 8)

    def is_rle(self, shapenum: int) -> bool:
        off, ln = self._entry(shapenum)
        if not off or not ln:
            return False
        dlen, hdrlen = struct.unpack_from("<II", self.data, off)
        return dlen == ln or ((ln & 1) == 0 and dlen == ln - 1)

    def num_frames(self, shapenum: int) -> int:
        if shapenum in self._nframes:
            return self._nframes[shapenum]
        off, ln = self._entry(shapenum)
        if not off or not ln:
            n = 0
        else:
            dlen, hdrlen = struct.unpack_from("<II", self.data, off)
            if dlen == ln or ((ln & 1) == 0 and dlen == ln - 1):
                n = (hdrlen - 4) // 4
            else:
                n = ln // TILE_BYTES
        self._nframes[shapenum] = n
        return n

    def frame(self, shapenum: int, framenum: int) -> Frame | None:
        key = (shapenum, framenum)
        if key in self._cache:
            return self._cache[key]
        fr = self._read(shapenum, framenum)
        self._cache[key] = fr
        return fr

    def _read(self, shapenum: int, framenum: int) -> Frame | None:
        off, ln = self._entry(shapenum)
        if not off or not ln:
            return None
        dlen, hdrlen = struct.unpack_from("<II", self.data, off)
        rle = dlen == ln or ((ln & 1) == 0 and dlen == ln - 1)
        if not rle:
            # flat 8x8 tile: framenum &= 31 (Shape_frame::read)
            fn = framenum & 31
            nframes = ln // TILE_BYTES
            if fn >= nframes:
                return None
            start = off + fn * TILE_BYTES
            px = np.frombuffer(self.data[start:start + TILE_BYTES], dtype=np.uint8).reshape(TILE_PX, TILE_PX).copy()
            # Shape_frame::read: xleft = yabove = c_tilesize for flats
            return Frame(px, np.ones_like(px, dtype=bool), TILE_PX, TILE_PX, False)
        nframes = (hdrlen - 4) // 4
        if framenum >= nframes:
            if framenum & 32 and (framenum & 0x1F) < nframes:
                base = self.frame(shapenum, framenum & 0x1F)
                return self._reflect(base) if base else None
            return None
        offs = [struct.unpack_from("<I", self.data, off + 8 + 4 * i)[0] for i in range(nframes - 1)]
        frameoff = hdrlen if framenum == 0 else offs[framenum - 1]
        if framenum == nframes - 1:
            framelen = dlen - frameoff
        else:
            framelen = offs[framenum] - frameoff
        return self._decode_rle(off + frameoff, framelen)

    def _decode_rle(self, pos: int, length: int) -> Frame:
        d = self.data
        xright, xleft, yabove, ybelow = struct.unpack_from("<hhhh", d, pos)
        w = xright + xleft + 1
        h = yabove + ybelow + 1
        px = np.zeros((h, w), np.uint8)
        al = np.zeros((h, w), bool)
        i = pos + 8
        end = pos + length
        # Image_buffer8::paint_rle
        while i + 2 <= end:
            scanlen = struct.unpack_from("<H", d, i)[0]
            i += 2
            if scanlen == 0:
                break
            encoded = scanlen & 1
            scanlen >>= 1
            sx, sy = struct.unpack_from("<hh", d, i)
            i += 4
            x = sx + xleft
            y = sy + yabove
            if not encoded:
                seg = np.frombuffer(d[i:i + scanlen], dtype=np.uint8)
                i += scanlen
                if 0 <= y < h:
                    x0 = max(x, 0)
                    x1 = min(x + scanlen, w)
                    if x1 > x0:
                        px[y, x0:x1] = seg[x0 - x:x1 - x]
                        al[y, x0:x1] = True
                continue
            while scanlen > 0:
                bcnt = d[i]
                i += 1
                repeat = bcnt & 1
                bcnt >>= 1
                if repeat:
                    col = d[i]
                    i += 1
                    if 0 <= y < h:
                        x0 = max(x, 0)
                        x1 = min(x + bcnt, w)
                        if x1 > x0:
                            px[y, x0:x1] = col
                            al[y, x0:x1] = True
                else:
                    seg = np.frombuffer(d[i:i + bcnt], dtype=np.uint8)
                    i += bcnt
                    if 0 <= y < h:
                        x0 = max(x, 0)
                        x1 = min(x + bcnt, w)
                        if x1 > x0:
                            px[y, x0:x1] = seg[x0 - x:x1 - x]
                            al[y, x0:x1] = True
                x += bcnt
                scanlen -= bcnt
        return Frame(px, al, xleft, yabove, True)

    @staticmethod
    def _reflect(f: Frame) -> Frame:
        """Shape::reflect(): swap x/y (mirror across the NW-SE diagonal)."""
        return Frame(np.ascontiguousarray(f.pixels.T), np.ascontiguousarray(f.alpha.T), f.yabove, f.xleft, f.is_rle)
