"""The original art, as the game reads it: palettes, VGA shape frames, TFA
translucency and the world's terrain (for tiles in context).

Mirrors the C# readers (godot/scripts/Data: U7Palette, ShapeFrame,
VgaShapeFile, GameMap.LoadChunks/LoadMap), which follow Exult.
"""

from __future__ import annotations

import csv
import struct
from dataclasses import dataclass
from functools import lru_cache
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
STATIC = ROOT / "u7" / "STATIC"
# Serpent Isle's files, which Black Gate borrows from when they are there (as Exult's does).
SERPENT = ROOT / "serpent" / "STATIC"
# Exult's own Black Gate data and art the game reads (godot/data/bg).
EXULT_BG = ROOT / "godot" / "data" / "bg"
FIRST_IMPORT = 2048
DATA = ROOT / "assets" / "data"
HIRES = ROOT / "assets" / "hires"

# Exult rotatecolours: the colour-cycling ranges (water, flames, the void).
CYCLING_FIRST = 0xE0
CYCLING_LAST = 0xFE
# XformTables.FirstTranslucent: in a translucent shape these indices turn
# the pixel under them through a table.
FIRST_TRANSLUCENT = 0xFF - 17
TILE_SHAPES = 150
TILE = 8


def read_flex(path: Path) -> list[bytes]:
    data = path.read_bytes()
    count = struct.unpack_from("<I", data, 0x54)[0]
    out = []
    for i in range(count):
        off, size = struct.unpack_from("<II", data, 0x80 + 8 * i)
        out.append(data[off:off + size] if size else b"")
    return out


@lru_cache(maxsize=None)
def day_palette() -> np.ndarray:
    """U7Palette.DayRgb: 256 x 3 uint8, 6-bit values times 255 / 63."""
    raw = np.frombuffer(read_flex(STATIC / "PALETTES.FLX")[0][:768], dtype=np.uint8).astype(np.int32)
    return np.minimum(255, raw * 255 // 63).astype(np.uint8).reshape(256, 3)


@dataclass
class Frame:
    """A frame as arrays with the hotspot at (xleft, yabove): indices and opacity."""
    xleft: int
    yabove: int
    xright: int
    ybelow: int
    rle: bool
    index: np.ndarray  # (h, w) uint8
    opaque: np.ndarray  # (h, w) bool

    @property
    def width(self) -> int:
        return self.xleft + self.xright + 1

    @property
    def height(self) -> int:
        return self.yabove + self.ybelow + 1


def _decode_rle(rec: bytes, off: int) -> Frame:
    xright, xleft, yabove, ybelow = struct.unpack_from("<hhhh", rec, off)
    w, h = xleft + xright + 1, yabove + ybelow + 1
    index = np.zeros((max(h, 1), max(w, 1)), np.uint8)
    opaque = np.zeros_like(index, dtype=bool)
    i = off + 8
    while i + 2 <= len(rec):
        scanlen = struct.unpack_from("<H", rec, i)[0]
        i += 2
        if scanlen == 0 or i + 4 > len(rec):
            break
        encoded = scanlen & 1
        scanlen >>= 1
        x, y = struct.unpack_from("<hh", rec, i)
        i += 4
        px, py = x + xleft, y + yabove
        run = bytearray()
        if not encoded:
            run += rec[i:i + scanlen]
            i += scanlen
        else:
            b = 0
            while b < scanlen and i < len(rec):
                bcnt = rec[i]
                i += 1
                repeat = bcnt & 1
                bcnt >>= 1
                if bcnt == 0:
                    break
                if repeat:
                    run += bytes([rec[i]]) * bcnt
                    i += 1
                else:
                    run += rec[i:i + bcnt]
                    i += bcnt
                b += bcnt
        if 0 <= py < h and run:
            lo, hi = max(px, 0), min(px + len(run), w)
            if hi > lo:
                index[py, lo:hi] = np.frombuffer(bytes(run[lo - px:hi - px]), np.uint8)
                opaque[py, lo:hi] = True
    return Frame(xleft, yabove, xright, ybelow, True, index, opaque)


def _find(folder: Path, name: str) -> Path | None:
    """A file in the folder by name, any case; None when it (or the folder) isn't there."""
    if not folder.is_dir():
        return None
    for p in folder.iterdir():
        if p.name.lower() == name.lower():
            return p
    return None


def read_shape_list(index: Path) -> list[bytes]:
    """Exult's expack index (.in): the archive name, then ':n:file.shp' or
    'file.shp' numbered on from the line before (Data/ShapeList.cs)."""
    records: list[bytes] = []
    n = 0
    for line in index.read_text(encoding="latin-1").splitlines()[1:]:
        line = line.strip()
        if not line:
            continue
        if line.startswith(":"):
            num, _, line = line[1:].partition(":")
            n = int(num)
        while len(records) <= n:
            records.append(b"")
        shp = index.parent / line
        records[n] = shp.read_bytes() if shp.exists() else b""
        n += 1
    return records


def shape_imports(section: str) -> list[tuple[int, int]]:
    """Exult's shape_files.txt imports (Data/ShapeFiles.cs): (number here,
    Serpent Isle's), a '%name' taking the next number from 2048."""
    out: list[tuple[int, int]] = []
    path = EXULT_BG / "shape_files.txt"
    if not path.exists():
        return out
    inside = False
    nxt = FIRST_IMPORT
    for line in path.read_text(encoding="latin-1").splitlines():
        line = line.strip()
        if line.startswith("%%section"):
            inside = line.split()[-1] == section
            continue
        if line.startswith("%%endsection"):
            inside = False
            continue
        if not inside or not line.startswith(":"):
            continue
        src, _, dest = line[1:].partition("/")
        if any(s == int(src) for _, s in out):
            continue
        if dest.strip().startswith("%"):
            out.append((nxt, int(src)))
            nxt += 1
        else:
            out.append((int(dest), int(src)))
    return out


def shape_additions() -> list[tuple[int, bytes]]:
    """The remake's new shapes (godot/data/bg/patch/shape_additions.txt,
    Data/ShapeAdditions.cs): (number, the shape's bytes), the art an Exult
    shape file beside it or Serpent Isle's (si:<n>)."""
    out: list[tuple[int, bytes]] = []
    path = EXULT_BG / "patch" / "shape_additions.txt"
    if not path.exists():
        return out
    inside = False
    serpent = None
    for line in path.read_text(encoding="latin-1").splitlines():
        line = line.strip()
        if line.startswith("%%section"):
            inside = line.split()[-1] == "shapes"
            continue
        if line.startswith("%%endsection"):
            inside = False
            continue
        if not inside or not line.startswith(":"):
            continue
        fields = line[1:].split("/")
        if len(fields) < 4:
            continue
        dest, art = int(fields[0]), fields[1].strip()
        if art.lower().startswith("si:"):
            si = _find(SERPENT, "SHAPES.VGA")
            if si is None:
                continue
            serpent = serpent or read_flex(si)
            out.append((dest, serpent[int(art[3:])]))
        elif (path.parent / art).exists():
            out.append((dest, (path.parent / art).read_bytes()))
    return out


def _layer(records: list[bytes], over: list[bytes]) -> list[bytes]:
    """Exult Vga_file: a later source's shape wins where it has data."""
    records = list(records) + [b""] * max(0, len(over) - len(records))
    for i, rec in enumerate(over):
        if rec:
            records[i] = rec
    return records


def _import(records: list[bytes], source: list[bytes], imports: list[tuple[int, int]]) -> list[bytes]:
    """Exult Vga_file::import_shapes: another game's shape at a number here."""
    for dest, src in imports:
        if src < len(source) and source[src]:
            records = records + [b""] * max(0, dest + 1 - len(records))
            records[dest] = source[src]
    return records


class VgaFile:
    """A SHAPES.VGA-style file: RLE shapes, or raw 8x8 tiles (the terrain)."""

    def __init__(self, name: str, records: list[bytes] | None = None):
        self.name = name
        self.records = records if records is not None else read_flex(STATIC / name)

    def frame_count(self, shape: int) -> int:
        rec = self.records[shape] if shape < len(self.records) else b""
        if len(rec) < 8:
            return 0
        if struct.unpack_from("<I", rec, 0)[0] != len(rec):
            return len(rec) // (TILE * TILE)
        return (struct.unpack_from("<I", rec, 4)[0] - 4) // 4

    def is_tile(self, shape: int) -> bool:
        rec = self.records[shape] if shape < len(self.records) else b""
        return len(rec) >= 8 and struct.unpack_from("<I", rec, 0)[0] != len(rec)

    def frame(self, shape: int, frame: int) -> Frame | None:
        if frame >= self.frame_count(shape):
            return None
        rec = self.records[shape]
        if self.is_tile(shape):
            tile = np.frombuffer(rec[frame * 64:frame * 64 + 64], np.uint8).reshape(TILE, TILE).copy()
            return Frame(TILE, TILE, -1, -1, False, tile, np.ones_like(tile, dtype=bool))
        off = struct.unpack_from("<I", rec, 4 + 4 * frame)[0]
        return _decode_rle(rec, off)


@lru_cache(maxsize=None)
def translucent_shapes() -> frozenset[int]:
    """TFA translucency (the extracted typeflags.csv, as ShapeCatalog reads it)."""
    path = DATA / "typeflags.csv"
    out = set()
    with path.open(newline="", encoding="utf-8") as f:
        for row in csv.DictReader(f):
            if row.get("translucency") == "1":
                out.add(int(row["shape"]))
    return frozenset(out)


@lru_cache(maxsize=None)
def shape_dims() -> dict[int, tuple[int, int]]:
    """Each shape's footprint in tiles (TFA dims x, y), from the extracted typeflags.csv."""
    out = {}
    with (DATA / "typeflags.csv").open(newline="", encoding="utf-8") as f:
        for row in csv.DictReader(f):
            out[int(row["shape"])] = (int(row["dims_x"] or 1), int(row["dims_y"] or 1))
    return out


def special_mask(frame: Frame, translucent: bool) -> np.ndarray:
    """Pixels whose index must survive as it is: colour-cycling ones, and in a
    translucent shape the ones that turn what is under them."""
    idx = frame.index
    keep = (idx >= CYCLING_FIRST) & (idx <= CYCLING_LAST)
    if translucent:
        keep |= (idx >= FIRST_TRANSLUCENT) & (idx <= 0xFE)
    return keep & frame.opaque


@lru_cache(maxsize=None)
def chunk_templates() -> tuple[np.ndarray, np.ndarray]:
    """U7CHUNKS, Exult's Chunk_terrain: each terrain's 256 cells (16 x 16
    tiles, row by row) as shape and frame arrays, (count, 256) each."""
    chunks = (STATIC / "U7CHUNKS").read_bytes()
    count = len(chunks) // 512
    cells = np.frombuffer(chunks[:count * 512], np.uint8).reshape(count, 256, 2).astype(np.int32)
    return cells[:, :, 0] + 256 * (cells[:, :, 1] & 3), (cells[:, :, 1] >> 2) & 0x1F


@lru_cache(maxsize=None)
def chunk_map() -> np.ndarray:
    """U7MAP: the terrain of each of the world's 192 x 192 chunks, [cy, cx]."""
    data = (STATIC / "U7MAP").read_bytes()
    terrain = np.zeros((192, 192), np.int32)
    off = 0
    for schunk in range(144):
        scy, scx = 16 * (schunk // 12), 16 * (schunk % 12)
        block = np.frombuffer(data[off:off + 512], "<u2").reshape(16, 16)
        terrain[scy:scy + 16, scx:scx + 16] = block
        off += 512
    return terrain


@lru_cache(maxsize=None)
def world_tiles() -> np.ndarray:
    """The world's flat terrain as tile ids (shape << 5 | frame), -1 where a
    cell is an RLE object: 3072 x 3072 (U7MAP chunks of U7CHUNKS terrains)."""
    shapes = VgaFile("SHAPES.VGA")
    shape, frame = chunk_templates()
    raw = np.array([shapes.is_tile(s) for s in range(1024)])
    ids = np.where(raw[shape], (shape << 5) | frame, -1).reshape(len(shape), 16, 16)
    world = ids[chunk_map()]  # (192, 192, 16, 16)
    return world.transpose(0, 2, 1, 3).reshape(192 * 16, 192 * 16)


@lru_cache(maxsize=None)
def tile_neighbours() -> dict[int, dict[tuple[int, int], int]]:
    """For each tile id, its most frequent neighbour in each of the 8
    directions across the world (absent where it never has a flat one)."""
    world = world_tiles()
    out: dict[int, dict[tuple[int, int], int]] = {}
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            if dx == 0 and dy == 0:
                continue
            centre = world
            neigh = np.roll(world, (-dy, -dx), axis=(0, 1))
            ok = (centre >= 0) & (neigh >= 0)
            key = centre[ok].astype(np.int64) * 65536 + neigh[ok]
            keys, counts = np.unique(key, return_counts=True)
            order = np.lexsort((-counts, keys // 65536))
            seen = set()
            for k in keys[order]:
                c = int(k // 65536)
                if c in seen:
                    continue
                seen.add(c)
                out.setdefault(c, {})[(dx, dy)] = int(k % 65536)
    return out


def game_file(file: str) -> VgaFile | None:
    """A shape file as the remake builds it (Rendering/ShapeCache,
    Data/ShapeCatalog): Black Gate's, with the remake's new shapes (the keyring) and Serpent Isle's skin bodies
    (shapes) and paperdoll markers (gumps) imported at 2048 and up when SI
    is installed, Exult's skin faces at face 5000, and the paperdolls (SI's
    PAPERDOL.VGA with Exult's Black Gate art over it). None for the
    paperdolls without Serpent Isle."""
    si_shapes, si_gumps, si_dolls = (_find(SERPENT, n) for n in ("SHAPES.VGA", "GUMPS.VGA", "PAPERDOL.VGA"))
    if file == "paperdolls":
        if si_dolls is None or si_gumps is None:
            return None
        records = read_flex(si_dolls)
        extra = EXULT_BG / "flx" / "bg_paperdol.in"
        if extra.exists():
            records = _layer(records, read_shape_list(extra))
        return VgaFile("PAPERDOL.VGA", records)
    name = {"shapes": "SHAPES.VGA", "sprites": "SPRITES.VGA", "gumps": "GUMPS.VGA", "faces": "FACES.VGA"}[file]
    records = read_flex(STATIC / name)
    if file == "shapes" and si_shapes is not None:
        records = _import(records, read_flex(si_shapes), shape_imports("multiracial_imports"))
    if file == "shapes":
        for dest, rec in shape_additions():
            records = _import(records, [rec], [(dest, 0)])
    elif file == "gumps" and si_gumps is not None and si_dolls is not None:
        records = _import(records, read_flex(si_gumps), shape_imports("gump_imports"))
    elif file == "faces":
        faces = EXULT_BG / "flx" / "bg_mr_faces.in"
        if faces.exists():
            records = _layer(records, read_shape_list(faces))
    return VgaFile(name, records)
