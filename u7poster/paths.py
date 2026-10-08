from __future__ import annotations
import os
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
STATIC = Path(os.environ.get("U7_STATIC", ROOT / "u7" / "STATIC"))
OUT = Path(os.environ.get("U7_POSTER_OUT", ROOT / "poster"))
COARSE_MAP = Path(os.environ.get("U7_COARSE_MAP", r"C:\Users\wenzel\Sync\Privates\maps\ultima 7-wonder-3-5.png"))

# exult_constants.h
TILES_PER_CHUNK = 16
CHUNKS_PER_SCHUNK = 16
NUM_SCHUNKS = 12
NUM_CHUNKS = NUM_SCHUNKS * CHUNKS_PER_SCHUNK      # 192
NUM_TILES = NUM_CHUNKS * TILES_PER_CHUNK          # 3072
TILES_PER_SCHUNK = TILES_PER_CHUNK * CHUNKS_PER_SCHUNK  # 256
TILE_PX = 8
