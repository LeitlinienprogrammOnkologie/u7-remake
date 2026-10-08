"""IREG (movable object) parser and INITGAME.DAT access.

References (Exult 1.12.1):
  * gamedat.cc  Game_window::restore_flex_files() - INITGAME.DAT is a flex whose
                 entries are prefixed with a 13-byte DOS name (npc.dat,
                 u7ireg00..u7ireg8f, flaginit, frames.flg, identity).
  * gamemap.cc  Game_map::read_ireg_objects() - the byte-level grammar below:
      entlen byte: 0/1 = end of container (skipped at top level), 2 = 2-byte
      index id, 255 = special (type byte + u16 len + data), 253/254 = extended
      shape/lift follow (Exult only). Entry lengths 6, 10, 12, 13, 14, 18.
      b0: cx<<4 | tx, b1: cy<<4 | ty; b2,b3: shape = b2 + 256*(b3&3),
      frame = b3>>2. Lift = nibble_swap(b) & 15 == b >> 4.
      hatchable class (eggs):  type=b4+256*b5, prob=b6, data1=b7|b8<<8,
                               lift=b9>>4, data2=b10|b11<<8, data3=b12|b13<<8
      6/10 bytes (simple):     lift=b4>>4, quality=b5
      12/13 bytes (container/body): type=b4|b5<<8 (0 = empty), quality=b7,
                               lift=b9>>4 (b10>>4 for 13-byte bodies), then the
                               contents follow until a 0/1 byte.
      18 bytes: spellbook, lift=b9>>4
  * objs/egg.cc Egg_object::create_egg() / Monster_egg ctor:
      type = itype & 15 (1 monster, 2 jukebox, 3 sfx, 4 voice, 5 usecode,
      6 missile, 7 teleport, 8 weather, 9 path, 10 button, 11 intermap)
      monster: mshape = data2 & 1023, mframe = data2 >> 10, sched = data1 >> 8,
               align = data1 & 3, cnt = (data1 & 0xff) >> 2
"""
from __future__ import annotations
from dataclasses import dataclass, field
from pathlib import Path

from .flex import Flex
from .paths import STATIC, NUM_SCHUNKS, CHUNKS_PER_SCHUNK, TILES_PER_CHUNK
from .shapes import ShapeInfo

IREG_SPECIAL = 255
IREG_EXTENDED = 254
IREG_EXTENDED2 = 253
CLASS_HATCHABLE = 7

EGG_TYPES = {1: "monster", 2: "jukebox", 3: "soundsfx", 4: "voice", 5: "usecode", 6: "missile",
             7: "teleport", 8: "weather", 9: "path", 10: "button", 11: "intermap"}


@dataclass
class IregObject:
    tx: int
    ty: int
    tz: int
    shape: int
    frame: int
    quality: int = 0
    entlen: int = 0
    depth: int = 0          # 0 = in the world, >0 = inside a container / NPC inventory
    is_egg: bool = False
    egg_type: int = 0
    egg_prob: int = 0
    egg_data1: int = 0
    egg_data2: int = 0
    egg_data3: int = 0

    # Monster egg fields (Monster_egg ctor)
    @property
    def monster_shape(self):
        return self.egg_data3 if self.egg_data3 > 0 else self.egg_data2 & 1023

    @property
    def monster_frame(self):
        return (self.egg_data2 & 0xFF) if self.egg_data3 > 0 else self.egg_data2 >> 10

    @property
    def monster_count(self):
        return (self.egg_data1 & 0xFF) >> 2

    @property
    def monster_sched(self):
        return self.egg_data1 >> 8

    @property
    def monster_align(self):
        return self.egg_data1 & 3

    @property
    def egg_type_name(self):
        return EGG_TYPES.get(self.egg_type & 15, f"type{self.egg_type & 15}")

    @property
    def egg_criteria(self):
        # Egg_object::get_criteria(): (type >> 4) & 7
        return (self.egg_type >> 4) & 7


def nibble_swap(v: int) -> int:
    return ((v & 0xF) << 4) | (v >> 4)


class IregParser:
    def __init__(self, info: ShapeInfo):
        self.info = info

    def parse(self, data: bytes, scx: int, scy: int, out: list[IregObject], pos: int = 0,
              container: IregObject | None = None, depth: int = 0) -> int:
        """Port of Game_map::read_ireg_objects. Returns the position after the
        block (for containers: after the terminating 0/1 byte)."""
        n = len(data)
        i = pos
        while i < n:
            entlen = data[i]
            i += 1
            if entlen in (0, 1):
                if container is not None:
                    return i
                continue
            if entlen == 2:
                i += 2
                continue
            if entlen == IREG_SPECIAL:
                if i + 3 <= n:
                    ln = data[i + 1] | (data[i + 2] << 8)
                    i += 3 + ln
                continue
            extended = 0
            extended_lift = False
            if entlen in (IREG_EXTENDED, IREG_EXTENDED2):
                if entlen == IREG_EXTENDED:
                    extended = 1
                extended_lift = True
                entlen = data[i]
                i += 1
            testlen = entlen - extended
            if testlen not in (6, 10, 12, 13, 14, 18):
                i += entlen
                continue
            if i + entlen > n:
                break
            entry = data[i:i + entlen]
            i += entlen
            cx = entry[0] >> 4
            cy = entry[1] >> 4
            if container is not None:
                tilex, tiley = entry[0], entry[1]
            else:
                tilex, tiley = entry[0] & 0xF, entry[1] & 0xF
            if extended:
                shape = entry[2] + 256 * entry[3]
                frame = entry[4]
                entry = entry[1:]
            else:
                shape = entry[2] + 256 * (entry[3] & 3)
                frame = entry[3] >> 2

            def lift_of(b: int) -> int:
                v = nibble_swap(b)
                return v if extended_lift else v & 0xF

            if container is None:
                atx = (scx + cx) * TILES_PER_CHUNK + tilex
                aty = (scy + cy) * TILES_PER_CHUNK + tiley
            else:
                atx, aty = container.tx, container.ty
            cls = self.info.shape_class(shape) if shape < 1024 else 0
            if cls == CLASS_HATCHABLE:
                lift = lift_of(entry[9]) if len(entry) > 9 else 0
                etype = entry[4] + 256 * entry[5]
                obj = IregObject(atx, aty, lift, shape, frame, 0, testlen, depth, True, etype,
                                 entry[6], entry[7] + 256 * entry[8], entry[10] + 256 * entry[11],
                                 (entry[12] + 256 * entry[13]) if entlen >= 14 else 0)
                out.append(obj)
                continue
            if testlen in (6, 10):
                obj = IregObject(atx, aty, lift_of(entry[4]), shape, frame, entry[5], testlen, depth)
                out.append(obj)
                continue
            if testlen in (12, 13):
                extbody = 1 if testlen == 13 else 0
                ctype = entry[4] + 256 * entry[5]
                obj = IregObject(atx, aty, lift_of(entry[9 + extbody]), shape, frame, entry[7], testlen, depth)
                out.append(obj)
                if ctype:
                    i = self.parse(data, scx, scy, out, i, obj, depth + 1)
                continue
            if testlen == 18:
                obj = IregObject(atx, aty, lift_of(entry[9]), shape, frame, 0, testlen, depth)
                out.append(obj)
                continue
            # testlen 14 for non-egg: unknown, skip
        return i


def load_initgame(static: Path = STATIC) -> dict[str, bytes]:
    return Flex.open(static / "INITGAME.DAT").named_entries()


def load_ireg_all(info: ShapeInfo, static: Path = STATIC, files: dict[str, bytes] | None = None):
    """Parse every u7iregNN inside INITGAME.DAT. Returns (objects, files_found)."""
    files = files if files is not None else load_initgame(static)
    parser = IregParser(info)
    out: list[IregObject] = []
    found = 0
    for s in range(NUM_SCHUNKS * NUM_SCHUNKS):
        key = f"u7ireg{s:02x}"
        data = files.get(key)
        if data is None:
            continue
        found += 1
        scy = CHUNKS_PER_SCHUNK * (s // NUM_SCHUNKS)
        scx = CHUNKS_PER_SCHUNK * (s % NUM_SCHUNKS)
        parser.parse(data, scx, scy, out)
    return out, found
