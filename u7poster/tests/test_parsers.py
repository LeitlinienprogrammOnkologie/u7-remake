"""Tests against known facts of the Black Gate data. Run: python -m pytest u7poster/tests -q"""
from __future__ import annotations
import struct

import numpy as np
import pytest

from u7poster import paths
from u7poster.flex import Flex
from u7poster.ireg import load_initgame, load_ireg_all, IregParser, IregObject
from u7poster.mapdata import load_map, load_chunks, load_ifix
from u7poster.npcs import load_npcs, parse_npc_dat
from u7poster.shapes import load_shape_names, ShapeInfo, ShapesVGA, load_palette, NUM_SHAPES
from u7poster.terrain import build_terrain_raster, WATER, MOUNTAIN, STRUCTURE, LAND, CLASS_NAMES
from u7poster.usecode import load_usecode, sign_texts, decode_sign

pytestmark = pytest.mark.skipif(not (paths.STATIC / "U7MAP").exists(), reason="game data not present")


@pytest.fixture(scope="module")
def info():
    return ShapeInfo.load()


@pytest.fixture(scope="module")
def names():
    return load_shape_names()


@pytest.fixture(scope="module")
def initgame():
    return load_initgame()


def test_world_dimensions():
    tmap = load_map()
    assert tmap.shape == (192, 192)                      # 12 x 12 superchunks of 16 x 16 chunks
    assert paths.NUM_TILES == 3072
    shapes, frames = load_chunks()
    assert shapes.shape == (3072, 256)                    # 3072 terrain templates of 16 x 16 tiles
    assert tmap.max() < shapes.shape[0]


def test_ifix_files_and_layout():
    ifix = load_ifix()
    assert len(ifix) > 30000
    assert ifix[:, 0].min() >= 0 and ifix[:, 0].max() < 3072
    assert ifix[:, 1].min() >= 0 and ifix[:, 1].max() < 3072
    assert set(ifix[:, 5].tolist()) <= set(range(144)) and len(set(ifix[:, 5].tolist())) > 100   # most superchunks have objects
    for s in (0, 0x4F, 0x8F):
        fl = Flex.open(paths.STATIC / f"U7IFIX{s:02X}")
        assert fl.count == 256 and fl.version == 0


def test_initgame_contains_144_ireg_and_npc_dat(initgame):
    assert "npc.dat" in initgame
    assert sum(1 for k in initgame if k.startswith("u7ireg")) == 144
    assert {f"u7ireg{s:02x}" for s in range(144)} <= set(initgame)


def test_npc_dat_roster(info, initgame):
    npcs, leftover = parse_npc_dat(initgame["npc.dat"], info)
    assert leftover == 0                                   # records consumed byte-for-byte
    assert len(npcs) == 356
    used = [n for n in npcs if not n.unused and n.name]
    assert len(used) == 288
    by_name = {n.name: n for n in npcs}
    assert npcs[0].name == "Avatar" and (npcs[0].tx, npcs[0].ty) == (1079, 2214)   # Trinsic start tile
    assert by_name["Iolo"].num == 1 and by_name["Lord_British"].num == 23
    assert by_name["Iolo"].shape == 465 and by_name["Lord_British"].shape == 466
    # Lord British lives in the castle north of Britain
    lb = by_name["Lord_British"]
    assert 850 < lb.tx < 1000 and 1100 < lb.ty < 1250


def test_schedules(info, initgame):
    npcs, _, nsched = load_npcs(info, files=initgame)
    assert nsched == 256
    iolo = npcs[1]
    types = {e.type for e in iolo.schedules}
    assert 14 in types                                     # Iolo has a sleep entry
    for n in npcs:
        for e in n.schedules:
            assert 0 <= e.tx < 3072 and 0 <= e.ty < 3072 and 0 <= e.time < 8


def test_ireg_objects_and_eggs(info, initgame):
    objs, found = load_ireg_all(info, files=initgame)
    assert found == 144
    eggs = [o for o in objs if o.is_egg]
    assert 2500 < len(eggs) < 3000
    monster = [o for o in eggs if (o.egg_type & 15) == 1]
    assert 1300 < len(monster) < 1500
    shapes = {o.monster_shape for o in monster}
    assert 504 in shapes and 528 in shapes                 # dragon, skeleton eggs exist
    assert all(0 <= o.tx < 3072 and 0 <= o.ty < 3072 for o in objs if o.depth == 0)
    moongates = [o for o in eggs if o.shape in (776, 777)]
    assert 25 <= len(moongates) <= 40


def test_ireg_container_grammar(info):
    # hand-built: a 12-byte container with one 6-byte item then the 0 terminator
    cont = bytes([12, 0x12, 0x34, 0x20, 0x03, 0x01, 0x00, 0, 0, 0, 0x50, 0, 0])   # shape 800 chest, lift 5
    item = bytes([6, 0x00, 0x00, 0x84, 0x02, 0x00, 0x00])                        # shape 644 gold, frame 0
    data = cont + item + b"\0"
    out: list[IregObject] = []
    IregParser(info).parse(data, 0, 0, out)
    assert [o.shape for o in out] == [800, 644]
    assert out[0].depth == 0 and out[1].depth == 1
    assert (out[0].tx, out[0].ty, out[0].tz) == (1 * 16 + 2, 3 * 16 + 4, 5)


def test_shape_names_and_info(names, info):
    assert names[8] == "water" and names[180] == "mountain" and names[721] == "Avatar"
    assert names[379] == "sign" and names[894] == "monolith"
    assert info.is_water(8) and not info.is_water(4)
    assert info.dims(180) == (8, 8, 1)
    assert info.shape_class(275) == 7                      # Egg is hatchable
    assert info.is_npc(721) and info.is_npc(504)


def test_shapes_vga_decoding():
    vga = ShapesVGA()
    assert vga.count == 1024
    fr = vga.frame(8, 0)                                   # flat water tile
    assert fr is not None and fr.pixels.shape == (8, 8) and not fr.is_rle
    fr = vga.frame(180, 0)                                 # 8x8-tile mountain: 64 x 64 px sprite
    assert fr.is_rle and (fr.w, fr.h) == (64, 64) and fr.alpha.all()
    fr = vga.frame(721, 0)
    assert fr.is_rle and fr.alpha.sum() > 100
    pal = load_palette()
    assert pal.shape == (256, 3) and pal.max() == 255


def test_terrain_raster_known_places(names, info):
    tr = build_terrain_raster(names, info)
    cls = tr.cls
    assert cls.shape == (3072, 3072)
    # the north-west corner of the world is open sea
    assert (cls[0:64, 0:64] == WATER).all()
    # Trinsic: walled town around the start tile -> structures nearby
    win = cls[2150:2280, 960:1090]
    assert (win == STRUCTURE).mean() > 0.15
    # Castle Britannia: structure at Lord British's throne room
    assert (cls[1120:1240, 880:1000] == STRUCTURE).mean() > 0.2
    # Britain is mostly land/structure, not water
    assert (cls[1300:1500, 750:1000] == WATER).mean() < 0.1
    # the mountain range north of Britain (Serpent's Spine)
    assert (cls[850:950, 700:800] == MOUNTAIN).mean() > 0.3
    # roughly 45 % of the world is land
    land = (cls != WATER) & (cls != 0)
    assert 0.40 < land.mean() < 0.50


def test_sign_texts():
    funcs = load_usecode()
    assert len(funcs) > 1000
    st = sign_texts(funcs)
    assert st[2] == "trinsic" and st[5] == "britain" and st[71] == "jhelom"
    assert st[69] == "serpents hold" and st[32] == "buccaneers den"
    assert decode_sign("moo*low") == "moonglow" and decode_sign("nor(") == "north"


def test_flex_header():
    fl = Flex.open(paths.STATIC / "TEXT.FLX")
    assert fl.count == 2560 and fl.version == 0
    assert struct.unpack_from("<I", fl.data, 80)[0] == 0xFFFF1A00
