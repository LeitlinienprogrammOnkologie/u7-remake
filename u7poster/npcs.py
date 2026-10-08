"""npc.dat (inside INITGAME.DAT) and SCHEDULE.DAT readers.

References (Exult 1.12.1):
  * readnpcs.cc Game_window::read_npcs()  - npc.dat starts with two u16 counts
                 (num_npcs1 = NPCs with usecode, num_npcs = num_npcs1 + second
                 count); records follow, NPC 0 = Avatar.
  * actorio.cc  Actor::read()              - record layout for a new game
                 (fix_first == true). Offsets used here, in read order:
       0  locx (cx<<4|tx)        1  locy (cy<<4|ty)
       2  u16 shape|frame<<10    4  u16 iflag1 (has_contents if != 0)
       6  schunk                 7  map
       8  u16 usefun (lift = >>12)
      10  health                11..13 skip
      14  u16 iflag2 (0 => 'unused', not in game)
      16  u16 rflags (bit 11 in_party; alignment = (rflags>>3)&3)
      18  strength&0x3f (BG)    19 dexterity   20 intelligence&0x1f  21 combat&0x7f
      22  schedule_type          23 attack mode 24 charmalign(skipped)
      25  unk0  26 unk1  27 magic/ident  28 mana/flags3
      29  u16 face  31 skip  32 u32 exp  36 training  37..40 attackers  41 u16 oppressor
      43..46 skip  47 u16 sched tx  49 u16 sched ty  51 u16 tflags (bit 3 = female in BG,
                 fix_first inverts it)  53..57 skip  58 next_schedule  59..63 skip
      64 u16 shape16 (ignored when fix_first)  66 u16 polymorph (skipped)
      68..78 flags/siflags/flags2/skin (11 bytes skipped when fix_first)
      79..92 skip 14   93 food   94..100 skip 7   101..116 name (16 bytes)
     117 = record size; then inventory as a container IREG block if has_contents
     (read_ireg_objects with container = the NPC, ends at a 0/1 byte).
  * readnpcs.cc Set_to_read_schedules() / Read_a_schedule() and
    schedule.cc Schedule_change::set4() - SCHEDULE.DAT: u32 count, count x s16
    offsets, then 4-byte entries: b0 = time(0..7 = 3-hour block) | type<<3,
    b1 = x, b2 = y, b3 = superchunk; tile = (schunk%12)*256 + x, (schunk/12)*256 + y.
    NPC i (i >= 1) owns entries offsets[i-1] .. offsets[i].
"""
from __future__ import annotations
import struct
from dataclasses import dataclass, field
from pathlib import Path

from .ireg import IregParser, IregObject, load_initgame
from .paths import STATIC, NUM_SCHUNKS, CHUNKS_PER_SCHUNK, TILES_PER_CHUNK, TILES_PER_SCHUNK
from .shapes import ShapeInfo

NPC_RECORD_SIZE = 117

SCHEDULE_TYPES = {0: "combat", 1: "horiz_pace", 2: "vert_pace", 3: "talk", 4: "dance", 5: "eat",
                  6: "farm", 7: "tend_shop", 8: "miner", 9: "hound", 10: "stand", 11: "loiter",
                  12: "wander", 13: "blacksmith", 14: "sleep", 15: "wait", 16: "sit", 17: "graze",
                  18: "bake", 19: "sew", 20: "shy", 21: "lab", 22: "thief", 23: "waiter",
                  24: "special", 25: "kid_games", 26: "eat_at_inn", 27: "duel", 28: "preach",
                  29: "patrol", 30: "desk_work", 31: "follow_avatar"}

ALIGNMENTS = {0: "neutral", 1: "good", 2: "evil", 3: "chaotic"}


@dataclass
class ScheduleEntry:
    time: int      # 0..7 -> hour = time*3
    type: int
    tx: int
    ty: int

    @property
    def type_name(self):
        return SCHEDULE_TYPES.get(self.type, f"type{self.type}")


@dataclass
class Npc:
    num: int
    name: str
    shape: int
    frame: int
    tx: int
    ty: int
    tz: int
    unused: bool
    has_usecode: bool
    in_party: bool
    alignment: int
    schedule_type: int
    sched_tx: int
    sched_ty: int
    female: bool
    strength: int
    dexterity: int
    intelligence: int
    combat: int
    health: int
    iflag1: int
    iflag2: int
    inventory: list[IregObject] = field(default_factory=list)
    schedules: list[ScheduleEntry] = field(default_factory=list)


def parse_npc_dat(data: bytes, info: ShapeInfo) -> tuple[list[Npc], int]:
    """Returns (npcs, leftover_bytes)."""
    num1, num2 = struct.unpack_from("<HH", data, 0)
    total = num1 + num2
    parser = IregParser(info)
    pos = 4
    npcs = []
    for num in range(total):
        r = data[pos:pos + NPC_RECORD_SIZE]
        if len(r) < NPC_RECORD_SIZE:
            raise ValueError(f"npc.dat truncated at NPC {num}")
        locx, locy = r[0], r[1]
        shnum = struct.unpack_from("<H", r, 2)[0]
        shape = shnum & 0x3FF
        frame = shnum >> 10
        iflag1 = struct.unpack_from("<H", r, 4)[0]
        schunk = r[6]
        usefun = struct.unpack_from("<H", r, 8)[0]
        lift = usefun >> 12
        health = struct.unpack_from("<b", r, 10)[0]
        iflag2 = struct.unpack_from("<H", r, 14)[0]
        rflags = struct.unpack_from("<H", r, 16)[0]
        unused = iflag2 == 0 and num > 0
        strength = r[18] & 0x3F
        dex = r[19]
        intel = r[20] & 0x1F
        combat = r[21] & 0x7F
        sched_type = r[22]
        sched_tx, sched_ty = struct.unpack_from("<HH", r, 47)
        tflags = struct.unpack_from("<H", r, 51)[0]
        # fix_first: tf_sex is inverted relative to the stored bit (bit 3)
        female = not (tflags & (1 << 3))
        name = r[101:117].split(b"\0")[0].decode("latin1")
        pos += NPC_RECORD_SIZE
        has_contents = bool(iflag1) and not unused
        inv: list[IregObject] = []
        scy = CHUNKS_PER_SCHUNK * (schunk // NUM_SCHUNKS)
        scx = CHUNKS_PER_SCHUNK * (schunk % NUM_SCHUNKS)
        cx, cy = locx >> 4, locy >> 4
        tx = (scx + cx) * TILES_PER_CHUNK + (locx & 0xF)
        ty = (scy + cy) * TILES_PER_CHUNK + (locy & 0xF)
        npc = Npc(num, name, shape, frame, tx, ty, lift, unused, num < num1,
                  bool((rflags >> 0xB) & 1), (rflags >> 3) & 3, sched_type, sched_tx, sched_ty,
                  female, strength, dex, intel, combat, health, iflag1, iflag2)
        if has_contents:
            pos = parser.parse(data, scx, scy, inv, pos, container=IregObject(tx, ty, lift, shape, frame), depth=1)
        npc.inventory = inv
        npcs.append(npc)
    return npcs, len(data) - pos


def parse_schedule_dat(data: bytes) -> list[list[ScheduleEntry]]:
    num = struct.unpack_from("<i", data, 0)[0]
    if num in (-1, -2):
        raise ValueError("Exult-format SCHEDULE.DAT not supported")
    offsets = list(struct.unpack_from(f"<{num}h", data, 4))
    pos = 4 + 2 * num
    scheds: list[list[ScheduleEntry]] = [[] for _ in range(num)]
    for i in range(1, num):
        cnt = offsets[i] - offsets[i - 1]
        entries = []
        for _ in range(cnt):
            b0, x, y, sc = data[pos:pos + 4]
            pos += 4
            sx, sy = sc % NUM_SCHUNKS, sc // NUM_SCHUNKS
            entries.append(ScheduleEntry(b0 & 7, b0 >> 3, sx * TILES_PER_SCHUNK + x, sy * TILES_PER_SCHUNK + y))
        scheds[i] = entries
    return scheds


def load_npcs(info: ShapeInfo, static: Path = STATIC, files: dict[str, bytes] | None = None):
    files = files if files is not None else load_initgame(static)
    npcs, leftover = parse_npc_dat(files["npc.dat"], info)
    scheds = parse_schedule_dat((static / "SCHEDULE.DAT").read_bytes())
    for npc in npcs:
        if npc.num < len(scheds):
            npc.schedules = scheds[npc.num]
    return npcs, leftover, len(scheds)
