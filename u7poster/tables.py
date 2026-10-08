"""NPC and Monster rows for the workbook."""
from __future__ import annotations
from dataclasses import dataclass, field

import numpy as np

from .config import CONFIG
from .ireg import IregObject
from .landmarks import Landmark, _label_points
from .npcs import Npc, SCHEDULE_TYPES

# schedule type -> role, in order of specificity (first match wins)
SCHED_ROLES = [
    (13, "blacksmith"), (7, "shopkeeper"), (28, "preacher"), (21, "mage / alchemist (lab)"), (18, "baker"),
    (19, "tailor / seamstress"), (8, "miner"), (6, "farmer"), (23, "waiter / innkeeper"), (30, "clerk / scholar (desk work)"),
    (29, "guard (patrol)"), (22, "thief"), (4, "entertainer (dancer)"), (25, "child"), (17, "animal (grazes)"),
    (27, "fighter / trainer (duels)"), (9, "dog"), (1, "guard (gate)"), (2, "guard (gate)"), (0, "fighter"),
    (26, "patron (eats at inn)"), (3, "talker"), (20, "shy recluse"), (24, "special"),
]
KEY_NPCS = {
    "Avatar": "the Avatar (player)", "Iolo": "companion, bard", "Shamino": "companion, ranger", "Dupre": "companion, knight",
    "Jaana": "companion, healer", "Julia": "companion, tinker", "Katrina": "companion, shepherdess",
    "Tseramed": "companion, ranger", "Spark": "companion, boy", "Sentri": "companion, fighter",
    "Lord_British": "ruler of Britannia", "Batlin": "Fellowship leader (antagonist)", "Hook": "assassin (antagonist)",
    "Abraham": "Fellowship (antagonist)", "Lizbeth": "Fellowship (antagonist)", "Forskis": "gargoyle warrior (antagonist)",
    "Rudyom": "mage of Cove (blackrock)", "Penumbra": "mage of Moonglow", "Nystul": "court mage", "Chuckles": "court jester",
    "Alagner": "sage of New Magincia", "Draxinusom": "gargoyle king", "Elynor": "Fellowship branch leader, Minoc",
    "Finnigan": "mayor of Trinsic", "Nicodemus": "mad mage of Yew", "Horance": "liche of Skara Brae",
    "Cubolt": "gargoyle mage (Vesper)", "Mariah": "mage (Moonglow)", "Zauriel": "mage",
}
ROLE_WORDS = ("healer", "mage", "wright", "trainer", "caretaker", "smith", "farmer", "mayor", "guard", "captain", "sailor",
              "fisher", "innkeep", "bard", "actor", "ranger", "woodsman", "monk", "priest", "teacher", "scholar", "sage",
              "alchemist", "jewel", "baker", "tailor", "weaver", "miner", "shepherd", "hunter", "gypsy", "fortune", "king",
              "lord", "lady", "knight", "paladin", "apprentice", "servant", "cook", "butler", "gardener", "carpenter", "keeper",
              "merchant", "trader", "provision", "thief", "pirate", "beggar", "child", "wizard", "gargoyle", "emp", "nanny",
              "leader", "sovereign", "clown", "jester", "musician", "artist", "painter", "sculptor", "brewer", "vintner",
              "librarian", "astronomer", "ferryman", "owner", "proprietor", "warrior", "fighter", "soldier", "commander",
              "master", "mistress", "wife", "husband", "orphan", "student", "scientist", "inventor", "tinker", "shipwright",
              "blacksmith", "armourer", "armorer", "weaponsmith", "beekeeper", "cobbler", "seamstress", "clothier", "dancer")
ANIMALS = {"deer", "rabbit", "fish", "bird", "sheep", "cow", "cat", "dog", "chicken", "horse", "mouse", "insects",
           "fox", "rat", "wolf", "bee", "snake", "bat", "spider", "scorpion", "alligator", "draft horse"}
BIG_MONSTERS = {"dragon", "liche", "cyclops", "sea serpent", "troll", "gazer", "reaper", "hydra", "daemon",
                "stone harpie", "Golem", "wingless Gargoyle", "mongbat", "drake", "ethereal monster"}


@dataclass
class NpcRow:
    id: str
    num: int
    name: str
    shape: int
    frame: int
    home_x: int
    home_y: int
    home_source: str
    start_x: int
    start_y: int
    role: str
    priority: int
    female: bool
    landmark_id: str = ""
    landmark_name: str = ""


@dataclass
class MonsterRow:
    id: str
    shape: int
    shape_name: str
    kind: str
    count_eggs: int
    spawn_max: int
    tile_x: int
    tile_y: int
    bbox_w: int
    bbox_h: int
    region_id: str
    region_name: str
    priority: int
    align: str = ""
    in_dungeon: bool = False


def npc_home(n: Npc):
    sl = [e for e in n.schedules if e.type == 14]
    if sl:
        return sl[0].tx, sl[0].ty, "schedule: sleep"
    if n.schedules:
        e = n.schedules[0]
        return e.tx, e.ty, f"schedule: {e.type_name} at {e.time * 3:02d}:00"
    return n.tx, n.ty, "start position"


def nearest_landmark(x, y, landmarks: list[Landmark], skip=("cave", "shipwreck", "tower", "harbour", "other", "buildings", "npc_home")):
    best, bd = None, 1e9
    for l in landmarks:
        if l.category in skip:
            continue
        d = max(abs(l.tile_x - x) - l.w / 2, 0) + max(abs(l.tile_y - y) - l.h / 2, 0)
        key = (d, l.w * l.h)
        if best is None or key < bd:
            best, bd = l, key
    return best, (bd[0] if best else 1e9)


def build_npc_rows(npcs: list[Npc], landmarks: list[Landmark], role_phrases: dict[int, str]) -> list[NpcRow]:
    rows = []
    for n in npcs:
        if n.unused or not n.name:
            continue
        hx, hy, src = npc_home(n)
        types = {e.type for e in n.schedules}
        role = ""
        for t, r in SCHED_ROLES:
            if t in types:
                role = r
                break
        if n.name in KEY_NPCS:
            role = KEY_NPCS[n.name] + (f"; {role}" if role else "")
        elif not role:
            phrase = role_phrases.get(n.num, "")
            role = f'"{phrase}" (usecode)' if phrase and any(w in phrase for w in ROLE_WORDS) else ""
        if n.name in KEY_NPCS:
            prio = 1
        elif n.has_usecode and not n.name.startswith("Clone") and n.name not in ("Uuungh!", "Aaargh!", "Batlin_Monster"):
            prio = 2
        else:
            prio = 3
        lm, d = nearest_landmark(hx, hy, landmarks)
        rows.append(NpcRow(f"N{n.num:03d}", n.num, n.name, n.shape, n.frame, hx, hy, src, n.tx, n.ty, role, prio, n.female,
                           lm.id if lm else "", (lm.name if lm and d < 96 else f"{int(d)} tiles from {lm.name}") if lm else ""))
    return rows


def build_monster_rows(objs: list[IregObject], names: list[str], info, landmarks: list[Landmark], cls=None, cfg=CONFIG) -> list[MonsterRow]:
    from .terrain import MOUNTAIN
    eggs = [o for o in objs if o.is_egg and (o.egg_type & 15) == 1 and o.monster_shape < 1024 and info.is_npc(o.monster_shape)]
    by_shape: dict[int, list[IregObject]] = {}
    for o in eggs:
        by_shape.setdefault(o.monster_shape, []).append(o)
    rows = []
    for shape, lst in sorted(by_shape.items()):
        pts = np.array([[o.tx, o.ty] for o in lst], int)
        lab, k = _label_points(pts, cfg["monster_group_radius_tiles"])
        nm = names[shape]
        kind = "animal" if nm in ANIMALS else ("human" if info.shape_class(shape) == 13 else "monster")
        for g in range(1, k + 1):
            idx = np.nonzero(lab == g)[0]
            p = pts[idx]
            cx, cy = p.mean(0)
            rep = idx[np.argmin(np.abs(p - [cx, cy]).sum(1))]
            o = lst[rep]
            spawn = sum(max(lst[i].monster_count, 1) for i in idx)
            lm, d = nearest_landmark(o.tx, o.ty, landmarks)
            if kind == "animal":
                prio = 3
            elif nm in BIG_MONSTERS:
                prio = 1
            else:
                prio = 2
            aligns = {lst[i].monster_align for i in idx}
            under = bool(cls is not None and cls[int(o.ty), int(o.tx)] == MOUNTAIN)
            rows.append(MonsterRow("", shape, nm, kind, len(idx), spawn, int(o.tx), int(o.ty),
                                   int(np.ptp(p[:, 0])) + 1, int(np.ptp(p[:, 1])) + 1,
                                   lm.id if lm else "", lm.name if lm else "", prio,
                                   ",".join({0: "neutral", 1: "good", 2: "evil", 3: "chaotic"}[a] for a in sorted(aligns)), under))
    rows.sort(key=lambda r: (r.priority, r.shape_name, r.tile_y, r.tile_x))
    for i, r in enumerate(rows, 1):
        r.id = f"M{i:03d}"
    return rows
