"""End-to-end pipeline: parse -> render -> landmarks/NPCs/monsters -> register
coarse map -> defects -> crops/overlays -> assets.xlsx.

    python -m u7poster.run [--out poster] [--coarse PATH] [--skip-render] [--no-crops]
"""
from __future__ import annotations
import argparse
import json
import sys
import time
from collections import Counter
from pathlib import Path

import numpy as np
from PIL import Image

from . import paths
from .config import CONFIG
from .defects import find_defects
from .ireg import load_initgame, load_ireg_all
from .landmarks import find_landmarks
from .mapdata import load_map, load_chunks, load_ifix
from .npcs import load_npcs
from .overlays import overview_image, diff_overlay, vignette_overlay, write_crops
from .registration import load_coarse, register, registration_check_image
from .render import render_map
from .shapes import load_shape_names, ShapeInfo
from .tables import build_npc_rows, build_monster_rows, npc_home
from .terrain import build_terrain_raster, CLASS_NAMES
from .usecode import load_usecode, sign_texts, npc_role_phrase
from .xlsx import build_workbook

Image.MAX_IMAGE_PIXELS = None


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--out", type=Path, default=paths.OUT)
    ap.add_argument("--coarse", type=Path, default=paths.COARSE_MAP)
    ap.add_argument("--skip-render", action="store_true", help="reuse basemap_clean.png / _terrain_3072.png")
    ap.add_argument("--no-crops", action="store_true")
    ap.add_argument("--set", action="append", default=[], metavar="KEY=VALUE", help="override a Config value")
    args = ap.parse_args(argv)
    cfg = dict(CONFIG)
    for kv in args.set:
        k, v = kv.split("=", 1)
        cfg[k] = type(CONFIG[k])(v) if k in CONFIG else float(v)
    out = args.out
    out.mkdir(parents=True, exist_ok=True)
    lines = []

    def log(*a):
        s = " ".join(str(x) for x in a)
        print(s, flush=True)
        lines.append(s)

    t0 = time.time()
    # ---------- parse ----------
    names = load_shape_names()
    info = ShapeInfo.load()
    tmap = load_map()
    chunks = load_chunks()
    ifix = load_ifix()
    files = load_initgame()
    objs, nfiles = load_ireg_all(info, files=files)
    npcs, leftover, nsched = load_npcs(info, files=files)
    funcs = load_usecode()
    stx = sign_texts(funcs)
    used = [n for n in npcs if not n.unused and n.name]
    log(f"parsed: map {tmap.shape[1]}x{tmap.shape[0]} chunks, {chunks[0].shape[0]} terrains, {len(ifix)} IFIX objects "
        f"in 144 files, {nfiles} IREG files with {sum(o.depth == 0 for o in objs)} top-level objects and "
        f"{sum(o.is_egg for o in objs)} eggs; npc.dat {len(npcs)} records ({len(used)} used+named, leftover {leftover} bytes); "
        f"SCHEDULE.DAT {nsched} slots; {len(funcs)} usecode functions, {len(stx)} sign texts")

    # ---------- terrain ----------
    tr = build_terrain_raster(names, info, tmap, chunks, ifix)
    cls = tr.cls
    counts = dict(zip(*np.unique(cls, return_counts=True)))
    log("terrain classes: " + ", ".join(f"{CLASS_NAMES[int(k)]}={int(v)}" for k, v in counts.items()))

    # ---------- render ----------
    base_path = out / "basemap_clean.png"
    r3072_path = out / "_terrain_3072.png"
    if args.skip_render and base_path.exists() and r3072_path.exists():
        render_3072 = np.asarray(Image.open(r3072_path).convert("RGB"))
        log("render: reused existing basemap_clean.png / _terrain_3072.png")
    else:
        outs = render_map({2048: base_path, 3072: r3072_path}, tmap, chunks, ifix, log=log)
        render_3072 = outs[3072]

    # ---------- landmarks / NPCs / monsters ----------
    signs = [(o.tx, o.ty, stx.get(o.quality, "")) for o in objs if o.shape == 379 and o.depth == 0]
    homes = {n.num: npc_home(n)[:2] for n in used if n.num > 0}
    landmarks = find_landmarks(cls, ifix, names, info, npcs, homes, signs, objs, cfg, log=log)
    phrases = {n.num: npc_role_phrase(funcs, n.num) for n in used}
    npc_rows = build_npc_rows(npcs, landmarks, phrases)
    monster_rows = build_monster_rows(objs, names, info, landmarks, cls, cfg)
    log(f"landmarks: {len(landmarks)} (" + ", ".join(f"{k}={v}" for k, v in sorted(Counter(l.category for l in landmarks).items())) + ")")
    log(f"  named from signs: {sum(l.name_source.startswith('sign') for l in landmarks)}, from NPC residents: "
        f"{sum(l.name_source.startswith('NPC') for l in landmarks)}, curated: {sum(l.name_source.startswith('curated') for l in landmarks)}, "
        f"unnamed: {sum(l.name.startswith('unnamed') for l in landmarks)}")
    log(f"NPCs: {len(npc_rows)} rows (priority " + ", ".join(f"p{k}={v}" for k, v in sorted(Counter(r.priority for r in npc_rows).items())) +
        f"; role filled {sum(bool(r.role) for r in npc_rows)}; home from sleep schedule {sum(r.home_source == 'schedule: sleep' for r in npc_rows)})")
    log(f"monsters: {len(monster_rows)} spawn groups from {sum(r.count_eggs for r in monster_rows)} monster eggs (" +
        ", ".join(f"{k}={v}" for k, v in sorted(Counter(r.kind for r in monster_rows).items())) +
        f"; under mountains {sum(r.in_dungeon for r in monster_rows)})")
    overview_image(render_3072, landmarks, out / "overview.png")
    log(f"wrote {out / 'overview.png'}")

    # ---------- coarse map ----------
    defects, masks, crop_paths, cm = [], {}, {}, None
    if args.coarse.exists():
        rgb = load_coarse(args.coarse)
        cfg["coarse_px"] = rgb.shape[1]
        reg, land_c = register(cls, rgb, log=log)
        cfg.update(coarse_offset_x=round(reg.offset_x, 2), coarse_offset_y=round(reg.offset_y, 2),
                   coarse_scale_x=round(reg.scale_x, 5), coarse_scale_y=round(reg.scale_y, 5),
                   coarse_residual_px=round(reg.residual_px, 1))
        registration_check_image(cls, rgb, reg, land_c).save(out / "registration_check.png")
        log(f"registration: residual {reg.residual_px:.1f} px RMS; wrote registration_check.png (red = data coast, blue = image coast)")
        defects, masks, aux = find_defects(cls, rgb, reg, cfg, log=log)
        if not args.no_crops:
            crop_paths = write_crops(rgb, render_3072, reg, defects, out / "before", cfg)
            log(f"wrote {len(crop_paths)} before/<id>.png + _ref.png crops")
        diff_overlay(rgb, masks, defects, out / "diff_overlay.png")
        log(f"wrote {out / 'diff_overlay.png'}")
    else:
        log(f"coarse map not found at {args.coarse}: skipping registration / defects / overlays")
        reg = None

    # ---------- workbook ----------
    xlsx_path = out / "assets.xlsx"
    cm = build_workbook(xlsx_path, cfg, landmarks, npc_rows, monster_rows, defects, crop_paths)
    log(f"wrote {xlsx_path}")
    if reg is not None:
        vignette_overlay(rgb, reg, landmarks, cm["landmarks"], npc_rows, cm["npcs"], monster_rows, cm["monsters"], cfg,
                         out / "vignette_overlay.png")
        log(f"wrote {out / 'vignette_overlay.png'}")

    # ---------- summary ----------
    log("")
    log("SUMMARY")
    log(f"  landmarks {len(landmarks)}, NPCs {len(npc_rows)}, monster groups {len(monster_rows)}, defects {len(defects)}")
    if defects:
        log("  defects per class: " + ", ".join(f"{k}={v}" for k, v in sorted(Counter(d.dclass for d in defects).items())))
    unresolved = [l for l in landmarks if l.name.startswith("unnamed") or l.confidence in ("low", "n/a")]
    log(f"  unresolved / low-confidence landmark names: {len(unresolved)} (see Landmarks.name_source / confidence)")
    log(f"  NPCs without a role: {sum(not r.role for r in npc_rows)}")
    log(f"  total {time.time() - t0:.0f}s")
    (out / "summary.txt").write_text("\n".join(lines), encoding="utf8")
    (out / "config_used.json").write_text(json.dumps(cfg, indent=2), encoding="utf8")
    return 0


if __name__ == "__main__":
    sys.exit(main())
