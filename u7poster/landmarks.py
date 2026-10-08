"""Landmark candidates: clusters of fixed structures, shrines, moongates,
cave mouths and shipwrecks, with names cross-referenced from sign texts
(usecode function 0x17b), NPC homes and a small curated table.
"""
from __future__ import annotations
from dataclasses import dataclass, field

import numpy as np
import scipy.ndimage as ndi

from .config import CONFIG
from .terrain import STRUCTURE, DOCK, CAVE, LAND, WATER, MOUNTAIN, FOREST, ROAD, SAND, SWAMP
from .paths import NUM_TILES

# decoded sign text (lower case) -> canonical place name
SIGN_PLACES = {
    "trinsic": "Trinsic", "britain": "Britain", "jhelom": "Jhelom", "minoc": "Minoc", "vesper": "Vesper",
    "moonglow": "Moonglow", "cove": "Cove", "paws": "Paws", "terfin": "Terfin",
    "serpents hold": "Serpent's Hold", "buccaneers den": "Buccaneer's Den",
    "skara brae population 47": "Skara Brae", "empath abbey": "Empath Abbey",
    "lycaeum": "The Lycaeum", "selwyns tower": "Selwyn's Tower",
    "high court of britannia": "Yew (High Court of Britannia)",
    "britannian mining company": "Britannian Mining Company",
    "the throne of the guardian": "Throne of the Guardian (Isle of the Avatar)",
    "meditation retreat": "Fellowship Meditation Retreat",
}
# NPC name -> place, only for residents whose home is certain. Names that
# also have a sign are cross-checked against the sign position at run time.
NPC_PLACES = {
    "Lord_British": "Castle Britannia", "Nystul": "Castle Britannia", "Chuckles": "Castle Britannia",
    "Geoffrey": "Castle Britannia",
    "Katrina": "New Magincia", "Alagner": "New Magincia", "Russell": "New Magincia", "Boris": "New Magincia",
    "Magenta": "New Magincia", "Henry": "New Magincia", "Constance": "New Magincia", "Sam": "New Magincia",
    "Robin": "New Magincia", "Battles": "New Magincia", "Leavell": "New Magincia",
    "Horance": "Skara Brae", "Rowena": "Skara Brae", "Mordra": "Skara Brae", "Paulette": "Skara Brae",
    "Quenton": "Skara Brae", "Forsythe": "Skara Brae", "Trent": "Skara Brae", "Caine": "Skara Brae", "Markham": "Skara Brae",
    "Sir_Jeff": "Yew", "Tiery": "Yew", "Reyna": "Yew", "Gharl": "Yew", "Aimi": "Yew", "Goth": "Yew", "Perrin": "Yew",
    "Taylor": "Yew", "Kreg": "Yew", "D_Rel": "Yew",
    "Nicodemus": "Nicodemus' hut", "Iolo": "Iolo's cabin", "Smith": "Iolo's cabin", "Tseramed": "Tseramed's camp",
    "Trellek": "Emp village", "Saralek": "Emp village", "Tavenor": "Emp village", "Salamon": "Emp village",
    "Papa": "Emp home (Papa & Mama)", "Mama": "Emp home (Papa & Mama)",
    "Penumbra": "Moonglow", "Mariah": "Moonglow", "Zelda": "Moonglow", "Rankin": "Moonglow", "Balayna": "Moonglow",
    "Brion": "Moonglow", "Nelson": "Moonglow", "Frank": "Moonglow", "Addom": "Moonglow", "Elad": "Moonglow",
    "Phearcy": "Moonglow", "Jillian": "Moonglow", "Effrem": "Moonglow", "Carlyn": "Moonglow",
    "Cador": "Vesper", "Mara": "Vesper", "Zaksam": "Vesper", "Eldroth": "Vesper", "Yongi": "Vesper", "Blorn": "Vesper",
    "Auston": "Vesper", "Liana": "Vesper", "Laplem": "Vesper", "Yvella": "Vesper", "Catherine": "Vesper",
    "Ansikart": "Vesper", "Wis_Sur": "Vesper", "Anmanivas": "Vesper", "Foranamo": "Vesper", "Aurvidlem": "Vesper",
    "Forlem": "Vesper",
    "Draxinusom": "Terfin", "Inmanilem": "Terfin", "Teregus": "Terfin", "Quan": "Terfin", "Quaeven": "Terfin",
    "Inforlem": "Terfin", "Sarpling": "Terfin", "Forbrak": "Terfin", "Betra": "Terfin",
    "Horffe": "Serpent's Hold", "Jehanne": "Serpent's Hold", "Denton": "Serpent's Hold", "Menion": "Serpent's Hold",
    "Leonardo": "Serpent's Hold", "Pendaran": "Serpent's Hold", "Richter": "Serpent's Hold",
    "John_Paul": "Serpent's Hold", "Jordan": "Serpent's Hold", "Tory": "Serpent's Hold", "Leigh": "Serpent's Hold",
    "Feridwyn": "Paws", "Camille": "Paws", "Tobias": "Paws", "Morfin": "Paws", "Andrew": "Paws", "Fenn": "Paws",
    "Thurston": "Paws", "Polly": "Paws", "Brita": "Paws", "Alina": "Paws", "Merrick": "Paws", "Garritt": "Paws",
    "Komor": "Paws", "Beverlea": "Paws",
    "Margareta": "Gypsy camp (near Minoc)", "Sasha": "Gypsy camp (near Minoc)", "Jergi": "Gypsy camp (near Minoc)",
    "Elynor": "Minoc", "Owen": "Minoc", "Julia": "Minoc", "Zorn": "Minoc", "Gladstone": "Minoc", "Gregor": "Minoc",
    "Rudyom": "Cove", "Nastassia": "Cove", "Lord_Heather": "Cove", "Jaana": "Cove", "Rayburt": "Cove",
    "Finnigan": "Trinsic", "Petre": "Trinsic", "Gargan": "Trinsic", "Spark": "Trinsic", "Klog": "Trinsic",
    "De_Snel": "Jhelom", "Joseph": "Jhelom", "Kliftin": "Jhelom", "Sprellic": "Jhelom", "Ophelia": "Jhelom",
    "Batlin": "Britain", "Csil": "Britain", "Raymundo": "Britain", "Gaye": "Britain", "Sean": "Britain",
    "Danag": "Buccaneer's Den", "Wench": "Buccaneer's Den", "Martine": "Buccaneer's Den", "Budo": "Buccaneer's Den",
    "Mole": "Buccaneer's Den", "Sintag": "Buccaneer's Den", "Roberto": "Buccaneer's Den", "Blacktooth": "Buccaneer's Den",
    "Mandy": "Buccaneer's Den", "Gordy": "Buccaneer's Den", "Lucky": "Buccaneer's Den", "Glenno": "Buccaneer's Den",
    "Smithy": "Buccaneer's Den",
    "Hook": "Isle of the Avatar (Black Gate)", "Abraham": "Isle of the Avatar (Black Gate)",
    "Lizbeth": "Isle of the Avatar (Black Gate)", "Forskis": "Isle of the Avatar (Black Gate)",
    "Clone_Alagner": "Sutek's castle (clones)", "Hook_Clone": "Sutek's castle (clones)",
    "Clone_Forskis": "Sutek's castle (clones)", "Clone_Elizabeth": "Sutek's castle (clones)",
}
NO_SIGN_NAMES = {"Castle Britannia", "Skara Brae", "Gypsy camp (near Minoc)", "New Magincia", "Yew", "Nicodemus' hut", "Iolo's cabin", "Tseramed's camp",
                 "Emp village", "Emp home (Papa & Mama)", "Isle of the Avatar (Black Gate)", "Sutek's castle (clones)"}
# curated point features (name, category, tile_x, tile_y, radius, confidence, why)
CURATED = [
    ("Shrine of Compassion", "shrine", 1449, 1385, 40, "high", "8 monoliths; east of Britain next to Cove"),
    ("Shrine of Honor", "shrine", 840, 2552, 40, "high", "8 monoliths; south of Trinsic"),
    ("Shrine of Valor", "shrine", 488, 2696, 40, "high", "8 monoliths; east of Jhelom"),
    ("Shrine of Honesty", "shrine", 2680, 1160, 40, "medium", "8 monoliths; north of Moonglow (Verity Isle)"),
    ("Shrine of Sacrifice", "shrine", 2328, 968, 40, "medium", "8 monoliths; mountains east of Minoc"),
    ("Shrine of Justice", "shrine", 728, 120, 40, "low", "8 monoliths; north-east of Yew"),
    ("Shrine of Humility", "shrine", 2840, 2536, 40, "low", "8 monoliths; island north of the Isle of the Avatar"),
    ("Shrine of Spirituality", "shrine", 2882, 380, 40, "medium", "8 standing stones on the north-east island"),
    ("Moongate (Yew)", "moongate", 521, 488, 24, "high", "8 standing stones + moongate egg"),
    ("Moongate (Minoc)", "moongate", 1857, 399, 24, "high", "8 standing stones + moongate egg"),
    ("Moongate (Moonglow)", "moongate", 2881, 1807, 24, "high", "8 standing stones + moongate egg"),
    ("Moongate (Britain)", "moongate", 1233, 1311, 24, "high", "8 standing stones + moongate egg"),
    ("Moongate (Skara Brae)", "moongate", 569, 1607, 24, "high", "8 standing stones + moongate egg"),
    ("Moongate (Trinsic)", "moongate", 1026, 2432, 24, "high", "8 standing stones + moongate egg"),
    ("Moongate (Jhelom)", "moongate", 263, 2823, 24, "high", "8 standing stones + moongate egg"),
    ("Moongate (New Magincia)", "moongate", 2177, 2335, 24, "high", "8 standing stones + moongate egg"),
    ("Sutek's castle (Spektran)", "castle", 2920, 2416, 60, "medium", "large roofed hall on the SE island, clones nearby"),
]

CATEGORY_PRIORITY = {"city": 1, "castle": 1, "town": 2, "shrine": 2, "moongate": 2, "keep": 2, "abbey": 2,
                     "tower": 3, "npc_home": 3, "village": 3, "hamlet": 3, "farm": 3, "cemetery": 3, "harbour": 3, "cave": 3,
                     "shipwreck": 3, "ruin": 3, "buildings": 3, "other": 3}
CITY_NAMES = {"Britain", "Trinsic", "Minoc", "Yew", "Jhelom", "Moonglow"}


@dataclass
class Landmark:
    id: str
    name: str
    category: str
    tile_x: int
    tile_y: int
    w: int
    h: int
    priority: int
    prompt_hint: str
    name_source: str = ""
    confidence: str = ""
    npcs: list[str] = field(default_factory=list)
    signs: list[str] = field(default_factory=list)
    feats: dict = field(default_factory=dict)


def _label_points(pts: np.ndarray, gap: int):
    n = len(pts)
    lab = np.zeros(n, int)
    k = 0
    for i in range(n):
        if lab[i]:
            continue
        k += 1
        stack = [i]
        lab[i] = k
        while stack:
            j = stack.pop()
            d = np.abs(pts - pts[j]).max(1)
            for m in np.nonzero((d <= gap) & (lab == 0))[0]:
                lab[m] = k
                stack.append(m)
    return lab, k


def _direction(dx, dy):
    ns = "S" if dy > 0 else "N"
    ew = "E" if dx > 0 else "W"
    if abs(dx) > 2.4 * abs(dy):
        return ew
    if abs(dy) > 2.4 * abs(dx):
        return ns
    return ns + ew


def find_landmarks(cls: np.ndarray, ifix: np.ndarray, names: list[str], info, npcs, npc_homes: dict[int, tuple[int, int]],
                   signs: list[tuple[int, int, str]], ireg_objs, cfg=CONFIG, log=print) -> list[Landmark]:
    gap = cfg["landmark_merge_gap_tiles"]
    min_tiles = cfg["landmark_min_tiles"]
    margin = cfg["landmark_npc_margin_tiles"]
    lname = [n.lower() for n in names]
    tfa_cls = info.classes
    xt, yt = info.xtiles, info.ytiles

    def shapes_named(*kws):
        return np.array([s for s in range(len(names)) if any(k in lname[s] for k in kws)], int)

    roof_shapes = np.array(sorted(set(shapes_named("roof").tolist()) |
                                  {s for s in range(len(names)) if not lname[s] and tfa_cls[s] == 14 and xt[s] == 4 and yt[s] == 4}), int)
    cren_shapes = shapes_named("crenellations")
    wall_shapes = shapes_named("wall")
    broken_shapes = shapes_named("broken")
    gate_shapes = shapes_named("drawbridge", "portcullis", "gateway")
    fortress_shapes = shapes_named("fortress")
    monolith = np.nonzero(np.array(lname) == "monolith")[0]
    sstone = shapes_named("standing stone")
    altar = shapes_named("altar")

    # roof raster -> connected roof blobs ~ buildings
    roof = np.zeros((NUM_TILES, NUM_TILES), bool)
    for tx, ty, s in ifix[np.isin(ifix[:, 3], roof_shapes)][:, [0, 1, 3]].tolist():
        roof[max(ty - yt[s] + 1, 0):ty + 1, max(tx - xt[s] + 1, 0):tx + 1] = True
    rlab, rn = ndi.label(roof)
    rcent = np.array(ndi.center_of_mass(roof, rlab, range(1, rn + 1))) if rn else np.zeros((0, 2))
    rcent = rcent[:, ::-1] if len(rcent) else rcent   # (x, y)

    st = np.isin(cls, [STRUCTURE, DOCK])
    lab, n = ndi.label(ndi.binary_dilation(st, iterations=max(1, gap // 2)))
    slices = ndi.find_objects(lab)
    sizes = ndi.sum(st, lab, range(1, n + 1))

    npc_pts = np.array([[x, y] for x, y in npc_homes.values()], int).reshape(-1, 2)
    npc_ids = list(npc_homes.keys())
    npc_by_num = {p.num: p for p in npcs}
    sign_pts = np.array([[x, y] for x, y, _ in signs], int).reshape(-1, 2)
    tomb = np.array([[o.tx, o.ty] for o in ireg_objs if o.depth == 0 and lname[o.shape] == "tombstone"], int).reshape(-1, 2)
    eggs_mg = np.array([[o.tx, o.ty] for o in ireg_objs if o.is_egg and lname[o.shape] == "moongate"], int).reshape(-1, 2)
    ships = np.array([[o.tx, o.ty] for o in ireg_objs if o.depth == 0 and lname[o.shape] in ("ship", "ship rails", "mast", "sails")], int).reshape(-1, 2)
    lights = np.array([[o.tx, o.ty, o.tz] for o in ireg_objs if o.depth == 0 and "light source" in lname[o.shape]], int).reshape(-1, 3)
    cannons = np.array([[o.tx, o.ty] for o in ireg_objs if o.depth == 0 and lname[o.shape] == "cannon"], int).reshape(-1, 2)

    def inside(pts, x0, y0, x1, y1, m=0):
        if not len(pts):
            return np.zeros(0, bool)
        return (pts[:, 0] >= x0 - m) & (pts[:, 0] < x1 + m) & (pts[:, 1] >= y0 - m) & (pts[:, 1] < y1 + m)

    raw = []
    for g in range(1, n + 1):
        if sizes[g - 1] < min_tiles:
            continue
        sl = slices[g - 1]
        y0, x0 = sl[0].start, sl[1].start
        m = (lab[sl] == g) & st[sl]
        ys, xs = np.nonzero(m)
        raw.append(dict(x0=x0 + xs.min(), x1=x0 + xs.max() + 1, y0=y0 + ys.min(), y1=y0 + ys.max() + 1,
                        tiles=int(sizes[g - 1]), cx=int(round(xs.mean())) + x0, cy=int(round(ys.mean())) + y0, g=g))

    # sign-named reference positions, for validating NPC-based names
    sign_ref: dict[str, list] = {}
    for x, y, t in signs:
        key = t.lower()
        if key in SIGN_PLACES and SIGN_PLACES[key]:
            sign_ref.setdefault(SIGN_PLACES[key], []).append((x, y))

    def npc_place(nm, x, y):
        place = NPC_PLACES.get(nm)
        if not place:
            return None
        if place in NO_SIGN_NAMES or place not in sign_ref:
            return place
        if min(abs(x - sx) + abs(y - sy) for sx, sy in sign_ref[place]) <= 480:
            return place
        log(f"  warn: NPC {nm} home ({x},{y}) is far from any '{place}' sign; ignoring anchor")
        return None

    def in_region(pts, g):
        if not len(pts):
            return np.zeros(0, bool)
        xs = np.clip(pts[:, 0], 0, NUM_TILES - 1)
        ys = np.clip(pts[:, 1], 0, NUM_TILES - 1)
        return lab[ys, xs] == g

    def anchors_in(c):
        out = {}
        for i in np.nonzero(in_region(sign_pts, c["g"]))[0]:
            key = signs[i][2].lower()
            if key in SIGN_PLACES and SIGN_PLACES[key]:
                out.setdefault(SIGN_PLACES[key], []).append(tuple(sign_pts[i]))
        for i in np.nonzero(in_region(npc_pts, c["g"]))[0]:
            p = npc_place(npc_by_num[npc_ids[i]].name, *npc_pts[i])
            if p:
                out.setdefault(p, []).append(tuple(npc_pts[i]))
        return out

    clusters = []
    for c in raw:
        anc = {k: v for k, v in anchors_in(c).items() if len(v) >= 3}
        if len(anc) >= 2 and (c["x1"] - c["x0"]) * (c["y1"] - c["y0"]) > 60 * 60:
            keys = list(anc)
            cen = np.array([np.median(np.array(anc[k]), axis=0) for k in keys])
            sub = st[c["y0"]:c["y1"], c["x0"]:c["x1"]] & (lab[c["y0"]:c["y1"], c["x0"]:c["x1"]] == c["g"])
            ys, xs = np.nonzero(sub)
            pts = np.stack([xs + c["x0"], ys + c["y0"]], 1)
            asg = ((pts[:, None, :] - cen[None, :, :]) ** 2).sum(2).argmin(1)
            for ki, k in enumerate(keys):
                p = pts[asg == ki]
                if len(p) < min_tiles:
                    continue
                clusters.append(dict(x0=int(p[:, 0].min()), x1=int(p[:, 0].max()) + 1, y0=int(p[:, 1].min()),
                                     y1=int(p[:, 1].max()) + 1, tiles=len(p), cx=int(round(p[:, 0].mean())),
                                     cy=int(round(p[:, 1].mean())), forced_name=k, g=c["g"]))
            log(f"  split cluster at ({c['cx']},{c['cy']}) into {keys}")
        else:
            clusters.append(c)

    out: list[Landmark] = []
    used_curated = set()
    for c in clusters:
        x0, y0, x1, y1 = c["x0"], c["y0"], c["x1"], c["y1"]
        box = inside(ifix[:, :2], x0, y0, x1, y1, 2)
        shp = ifix[box, 3]
        bld = int(inside(rcent, x0, y0, x1, y1, 2).sum()) if len(rcent) else 0
        f = dict(
            tiles=c["tiles"], buildings=bld, roofs=int(np.isin(shp, roof_shapes).sum()),
            walls=int(np.isin(shp, wall_shapes).sum()),
            cren=int(np.isin(shp, cren_shapes).sum()), broken=int(np.isin(shp, broken_shapes).sum()),
            gates=int(np.isin(shp, gate_shapes).sum()), fortress=int(np.isin(shp, fortress_shapes).sum()),
            monoliths=int(np.isin(shp, monolith).sum()), stones=int(np.isin(shp, sstone).sum()),
            altars=int(np.isin(shp, altar).sum()),
            tombs=int(inside(tomb, x0, y0, x1, y1, 4).sum()), moongate_eggs=int(inside(eggs_mg, x0, y0, x1, y1, 6).sum()),
            docks=int((cls[y0:y1, x0:x1] == DOCK).sum()), ships=int(inside(ships, x0, y0, x1, y1, 12).sum()),
            beacon=int((inside(lights[:, :2], x0, y0, x1, y1, 2) & (lights[:, 2] >= 8)).sum()) if len(lights) else 0,
            cannons=int(inside(cannons, x0, y0, x1, y1, 2).sum()),
        )
        top = [names[s] for s, _ in sorted(((int(s), int(k)) for s, k in zip(*np.unique(shp, return_counts=True))),
                                           key=lambda t: -t[1])[:3] if names[s]]
        f["top_shapes"] = ", ".join(top)
        yy0, yy1, xx0, xx1 = max(y0 - 12, 0), min(y1 + 12, NUM_TILES), max(x0 - 12, 0), min(x1 + 12, NUM_TILES)
        env = cls[yy0:yy1, xx0:xx1]
        for k, v in (("water_near", WATER), ("mountain_near", MOUNTAIN), ("forest_near", FOREST), ("sand_near", SAND), ("swamp_near", SWAMP)):
            f[k] = float((env == v).mean())
        npc_in = np.nonzero(inside(npc_pts, x0, y0, x1, y1, margin))[0]
        npc_names = [npc_by_num[npc_ids[i]].name for i in npc_in]
        sign_in = [signs[i][2] for i in np.nonzero(inside(sign_pts, x0, y0, x1, y1, 8))[0]]

        name, src, conf = "", "", ""
        if c.get("forced_name"):
            name, src, conf = c["forced_name"], "sign/NPC anchors (split cluster)", "high"
        if not name:
            for s in sign_in:
                key = s.lower()
                if key in SIGN_PLACES and SIGN_PLACES[key]:
                    name, src, conf = SIGN_PLACES[key], f"sign '{s}'", "high"
                    break
        if not name:
            votes = {}
            for i in np.nonzero(in_region(npc_pts, c["g"]))[0] if not c.get("forced_name") else []:
                p = npc_place(npc_by_num[npc_ids[i]].name, *npc_pts[i])
                if p:
                    votes[p] = votes.get(p, 0) + 1
            if votes:
                name = max(votes, key=votes.get)
                src, conf = "NPC residents: " + ", ".join(nm for nm in npc_names if NPC_PLACES.get(nm) == name)[:80], "medium"
        if not name:
            for ci, (cn, ccat, cxp, cyp, rad, cconf, why) in enumerate(CURATED):
                if abs(cxp - c["cx"]) <= rad and abs(cyp - c["cy"]) <= rad and ci not in used_curated:
                    name, src, conf = cn, f"curated ({why})", cconf
                    used_curated.add(ci)
                    f["curated_cat"] = ccat
                    break

        n_npc = len(npc_names)
        w, h = x1 - x0, y1 - y0
        if 6 <= f["monoliths"] <= 12 and bld <= 1 and f["tiles"] < 600:
            cat = "shrine"
        elif f["stones"] >= 6 and f["moongate_eggs"] >= 1 and bld <= 1:
            cat = "moongate"
        elif f.get("curated_cat"):
            cat = f["curated_cat"]
        elif f["cren"] >= 8 and bld == 0 and w <= 24 and h <= 24:
            cat = "tower"
        elif (f["docks"] >= 6 or f["ships"] >= 4) and bld < 1 and f["cren"] < 8:
            cat = "harbour"
        elif (f["cren"] >= 20 or f["gates"] >= 1 or f["fortress"] >= 10) and n_npc >= 3 and bld < 4:
            cat = "castle"
        elif bld >= 15 and n_npc >= 12:
            cat = "city"
        elif bld >= 5 and n_npc >= 4:
            cat = "town"
        elif f["tombs"] >= 5 and bld < 2:
            cat = "cemetery"
        elif bld >= 1 and n_npc >= 1:
            cat = "village" if n_npc >= 3 else "hamlet"
        elif f["broken"] >= 6 and bld < 2:
            cat = "ruin"
        elif f["cren"] >= 8 and bld < 3:
            cat = "keep"
        elif bld >= 1:
            cat = "buildings"
        else:
            cat = "other"
        if "abbey" in name.lower():
            cat = "abbey"
        if name in ("The Lycaeum", "Yew (High Court of Britannia)"):
            cat = "keep"
        if "castle" in name.lower() and cat not in ("tower",):
            cat = "castle"
        if name in CITY_NAMES:
            cat = "city"

        bits = []
        if cat in ("city", "town", "village", "hamlet"):
            bits.append(("walled " if f["cren"] >= 20 else "") + f"{cat} of about {bld} buildings")
        elif cat == "castle":
            bits.append("stone castle with crenellated walls" + (" and drawbridge" if f["gates"] else ""))
        elif cat == "shrine":
            bits.append("virtue shrine: ring of eight monoliths around a stone dais")
        elif cat == "moongate":
            bits.append("blue moongate in a circle of eight standing stones")
        elif cat == "tower":
            bits.append("small square stone tower with battlements" + (" and a lit beacon on top" if f["beacon"] else ""))
        elif cat == "cemetery":
            bits.append(f"graveyard with {f['tombs']} tombstones")
        elif cat == "ruin":
            bits.append("ruined walls and broken roofs")
        elif cat == "harbour":
            bits.append("wooden pier" + (" with a moored ship" if f["ships"] >= 4 else ""))
        elif cat == "abbey":
            bits.append("monastery with cloister and vineyard")
        elif cat == "keep":
            bits.append("fortified stone building with battlements")
        else:
            bits.append((f"{bld} building(s)" if bld else "stonework") + f" (mostly {f['top_shapes']})")
        if (f["docks"] >= 6 or f["ships"] >= 4) and cat not in ("shrine", "moongate", "harbour"):
            bits.append("harbour with docks" + (" and a moored ship" if f["ships"] >= 4 else ""))
        if f["altars"]:
            bits.append("Fellowship hall")
        if f["cannons"]:
            bits.append("cannons")
        if f["tombs"] >= 3 and cat != "cemetery":
            bits.append("small graveyard")
        if f["water_near"] > 0.25:
            bits.append("on the coast")
        if f["mountain_near"] > 0.25:
            bits.append("in the mountains")
        if f["forest_near"] > 0.2:
            bits.append("in dense forest")
        if f["sand_near"] > 0.3:
            bits.append("in desert sand")
        if f["swamp_near"] > 0.15:
            bits.append("by a swamp")
        hint = ", ".join(bits)
        prio = CATEGORY_PRIORITY.get(cat, 3)
        if not name and n_npc:
            name, src, conf = "Home of " + ", ".join(npc_names[:3]), "NPC residents", "n/a"
        out.append(Landmark("", name, cat, c["cx"], c["cy"], w, h, prio, hint, src, conf, npc_names, sign_in, f))

    for ci, (cn, ccat, cxp, cyp, rad, cconf, why) in enumerate(CURATED):
        if ci in used_curated:
            continue
        near = [l for l in out if abs(l.tile_x - cxp) <= rad and abs(l.tile_y - cyp) <= rad]
        if near:
            l = near[0]
            if not l.name:
                l.name, l.name_source, l.confidence = cn, f"curated ({why})", cconf
            continue
        out.append(Landmark("", cn, ccat, cxp, cyp, 16, 16, CATEGORY_PRIORITY.get(ccat, 3),
                            "virtue shrine" if ccat == "shrine" else ccat, f"curated, no structure cluster ({why})", cconf))

    # NPCs whose home lies outside every structure cluster -> camp / hut landmarks
    covered = np.zeros(len(npc_pts), bool)
    for l in out:
        if not l.feats.get("tiles"):
            continue
        covered |= inside(npc_pts, l.tile_x - l.w // 2, l.tile_y - l.h // 2, l.tile_x + l.w // 2 + 1, l.tile_y + l.h // 2 + 1, margin)
    free = np.nonzero(~covered)[0]
    if len(free):
        flab, fk = _label_points(npc_pts[free], 64)
        for g in range(1, fk + 1):
            idx = free[flab == g]
            p = npc_pts[idx]
            nms = [npc_by_num[npc_ids[i]].name for i in idx]
            places = [npc_place(nm, *npc_pts[i]) for nm, i in zip(nms, idx)]
            places = [q for q in places if q]
            name = max(set(places), key=places.count) if places else "Home of " + ", ".join(nms[:3])
            cx, cy = int(p[:, 0].mean()), int(p[:, 1].mean())
            env = cls[max(cy - 8, 0):cy + 9, max(cx - 8, 0):cx + 9]
            where = "under the mountains (dungeon interior)" if (env == MOUNTAIN).mean() > 0.6 else                     ("in dense forest" if (env == FOREST).mean() > 0.2 else "in open country")
            out.append(Landmark("", name, "npc_home", cx, cy, int(np.ptp(p[:, 0])) + 8, int(np.ptp(p[:, 1])) + 8, 3,
                                f"home of {', '.join(nms[:4])} ({len(nms)} NPC{'s' if len(nms) > 1 else ''}) {where}, no fixed buildings here",
                                "NPC residents (no structure cluster)", "medium" if places else "n/a", nms, [], {}))

    # cave mouths
    cave = cls == CAVE
    clab, cn_ = ndi.label(ndi.binary_dilation(cave, iterations=1))
    csz = ndi.sum(cave, clab, range(1, cn_ + 1))
    cslices = ndi.find_objects(clab)
    for g in range(1, cn_ + 1):
        if not (cfg["cave_mouth_min_tiles"] <= csz[g - 1] <= cfg["cave_mouth_max_tiles"]):
            continue
        sl = cslices[g - 1]
        m = (clab[sl] == g) & cave[sl]
        ys, xs = np.nonzero(m)
        cx, cy = int(xs.mean()) + sl[1].start, int(ys.mean()) + sl[0].start
        env = cls[max(cy - 6, 0):cy + 7, max(cx - 6, 0):cx + 7]
        if not np.isin(env, [LAND, ROAD, FOREST, SAND]).any():
            continue
        if any(abs(l.tile_x - cx) < 24 and abs(l.tile_y - cy) < 24 for l in out):
            continue
        out.append(Landmark("", "", "cave", cx, cy, int(np.ptp(xs)) + 1, int(np.ptp(ys)) + 1, 3,
                            "dark cave mouth in a mountainside", "", "", [], [], dict(tiles=int(csz[g - 1]))))

    # shipwrecks: ship parts with no dock / structure within 12 tiles
    if len(ships):
        slab, sk = _label_points(ships, 16)
        for g in range(1, sk + 1):
            p = ships[slab == g]
            cx, cy = int(p[:, 0].mean()), int(p[:, 1].mean())
            x0, x1 = max(p[:, 0].min() - 12, 0), min(p[:, 0].max() + 13, NUM_TILES)
            y0, y1 = max(p[:, 1].min() - 12, 0), min(p[:, 1].max() + 13, NUM_TILES)
            if st[y0:y1, x0:x1].any():
                continue
            env = cls[max(cy - 8, 0):cy + 9, max(cx - 8, 0):cx + 9]
            out.append(Landmark("", "", "shipwreck", cx, cy, int(np.ptp(p[:, 0])) + 4, int(np.ptp(p[:, 1])) + 4, 3,
                                "wrecked ship" + (" on the shore" if (env == WATER).mean() < 0.9 else " at sea"),
                                "", "", [], [], dict(parts=len(p))))

    # dedupe names, relative hints for unnamed features, ids
    out.sort(key=lambda l: (l.priority, 0 if l.name_source.startswith("sign") else 1, -l.feats.get("tiles", 0)))
    named = [l for l in out if l.name and not l.name.startswith("Home of")]
    seen: dict[str, int] = {}
    for l in out:
        if not l.name:
            continue
        seen[l.name] = seen.get(l.name, 0) + 1
        if seen[l.name] > 1:
            l.name = f"{l.name} (outskirts {seen[l.name] - 1})"
            l.priority = max(l.priority, 3)
    for l in out:
        if l.name and not l.name.startswith("Home of"):
            continue
        cands = [(abs(m.tile_x - l.tile_x) + abs(m.tile_y - l.tile_y), m) for m in named if m is not l and m.category not in ("cave", "shipwreck", "tower")]
        if cands:
            d, m = min(cands, key=lambda t: t[0])
            rel = f"{d} tiles {_direction(l.tile_x - m.tile_x, l.tile_y - m.tile_y)} of {m.name}"
            l.prompt_hint += f"; {rel}"
            if not l.name:
                l.name_source, l.confidence = "none", "n/a"
    counters: dict[str, int] = {}
    for l in out:
        if not l.name:
            counters[l.category] = counters.get(l.category, 0) + 1
            l.name = f"unnamed {l.category} {counters[l.category]}"
    for i, l in enumerate(out, 1):
        l.id = f"L{i:03d}"
    return out
