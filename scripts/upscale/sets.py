"""The art as sets to redraw by hand or with an outside AI, and taking the drawings back.

    python scripts/upscale/sets.py [--file shapes|sprites|gumps|paperdolls] [--pack painted] [--shapes 150-160]
    python scripts/upscale/sets.py --cut <set folder> <painted sheet.png or in_world*.png> [--pack painted]
    python scripts/upscale/sets.py --check [--file shapes] [--pack painted]

A set is one shape with all its frames (its animation, directions or
variants). Single frames are often tiny (a terrain tile is 32x32 at 4x) and
make sense only together, so a set is painted as a whole: its sheet.png (every
frame in a grid, the layout in set.json) or, for terrain, its world views.
--cut takes the painted image back apart into the files pack.py reads,
assets/hires/work/<pack>/<file>/SSSS_FF.png at exactly 4x each frame; then
`pack.py --model <pack> --file <file>` and, in the game, `art pack <pack>`.
Frames the pack lacks show in the original art.

Everything goes to assets/hires/sets/<file>/: PROMPT.md (the prompt for the
whole folder, its structure), index.md (the sets by kind), manifest.csv (every
frame), and one folder per set with its own prompt.txt (see prompt()).
--check lists what is drawn, missing or the wrong size.
"""

from __future__ import annotations

import argparse
import csv
import json
import re
from pathlib import Path

import numpy as np
from PIL import Image

import u7art
from generate import BUILT, PAD, SCALE, built_context, fills_frame, is_translucent, parse_range, work_dir
from pack import upscale_mask

SETS = u7art.HIRES / "sets"
# Frames whose drawable share is below this keep the original anyway (water, effects).
DRAWABLE = 0.1
# Names of the pieces buildings and landscape are built of, whatever their TFA class.
BUILT_NAMES = re.compile(r"\b(wall|roof|floor|crenellations|chimney|bridge|window|mountain|pillar)\b")
GAP = 8
SHEET_WIDTH = 2048
GROUND = (64, 64, 64)
TERRAIN_WINDOW = 20
KINDS = {
    "terrain": ("Terrain tiles", "32x32 ground squares (grass, sand, roads, dungeon floors) that tile with "
                "each other; a shape's frames are its variants and its edges towards other ground."),
    "built": ("Built pieces", "walls, floors, roofs, windows, bridges, mountains: each piece is part of "
              "something larger and meets copies of itself and its neighbours."),
    "characters": ("Characters", "people and monsters: every frame is one pose of the same character "
                   "(walking, standing, sitting, striking, lying) facing north or south."),
    "objects": ("Objects", "furniture, items, plants, scenery: a shape's frames are its variants, states "
                "(open, closed, lit) or animation."),
    "kept": ("Kept", "nearly every pixel stays the original (light beams, mists, ghosts, effects): "
             "nothing to paint."),
}
FILE_KINDS = {
    "sprites": ("Sprites", "effects over the world: explosions, spells, the moongate's light; animations."),
    "gumps": ("Gumps", "the interface's panels: backpacks, chests, the paperdoll, books, buttons."),
    "paperdolls": ("Paperdolls", "clothes, armour and weapons as worn on the paperdoll, in front view."),
}


def shape_classes() -> dict[int, str]:
    out = {}
    with (u7art.DATA / "typeflags.csv").open(newline="", encoding="utf-8") as f:
        for row in csv.DictReader(f):
            out[int(row["shape"])] = row["shape_class_name"]
    return out


def shape_names(file: str) -> dict[int, str]:
    """TEXT.FLX's shape names without Exult's article and plural pattern ("/gold coin//s")."""
    if file != "shapes":
        return {}
    out = {}
    for i, raw in enumerate(u7art.read_flex(u7art.STATIC / "TEXT.FLX")[:1024]):
        text = raw.split(b"\0")[0].decode("latin-1")
        parts = [p for p in text.split("/") if p]
        if parts:
            out[i] = max(parts, key=len).strip()
    return out


def slug(text: str) -> str:
    return re.sub(r"[^a-z0-9]+", "_", text.lower()).strip("_")[:30] or "unnamed"


def four_x(a: np.ndarray) -> np.ndarray:
    return np.repeat(np.repeat(a, SCALE, 0), SCALE, 1)


def rgba(frame: u7art.Frame) -> Image.Image:
    rgb = u7art.day_palette()[frame.index]
    alpha = np.where(frame.opaque, 255, 0).astype(np.uint8)
    return Image.fromarray(four_x(np.dstack([rgb, alpha])), "RGBA")


def mask(frame: u7art.Frame, translucent: bool) -> tuple[Image.Image, float]:
    """pack.py's outline and kept pixels at 4x; the share of the outline a drawing shows."""
    shown = upscale_mask(frame)
    kept = four_x(u7art.special_mask(frame, translucent))
    out = np.where(kept, 128, np.where(shown, 255, 0)).astype(np.uint8)
    share = float((shown & ~kept).sum()) / max(1, int(shown.sum()))
    return Image.fromarray(out, "L"), share


def tile_context(vga: u7art.VgaFile, shape: int, frame: int) -> Image.Image:
    """The tile among its most frequent neighbours, 3x3 tiles, as generate.tile_input sees it."""
    neighbours = u7art.tile_neighbours().get(shape << 5 | frame, {})
    grid = np.zeros((3 * u7art.TILE, 3 * u7art.TILE), np.uint8)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            n = neighbours.get((dx, dy), shape << 5 | frame)
            y0, x0 = (dy + 1) * u7art.TILE, (dx + 1) * u7art.TILE
            grid[y0:y0 + u7art.TILE, x0:x0 + u7art.TILE] = vga.frame(n >> 5, n & 31).index
    return Image.fromarray(four_x(u7art.day_palette()[grid]), "RGB")


def kind_of(file: str, vga: u7art.VgaFile, shape: int, frames: list[u7art.Frame], shares: list[float],
            classes: dict[int, str], name: str) -> str:
    if file != "shapes":
        return file
    if max(shares) < DRAWABLE:
        return "kept"
    if vga.is_tile(shape):
        return "terrain"
    if classes.get(shape) in ("human", "monster"):
        return "characters"
    if classes.get(shape) == "building" or BUILT_NAMES.search(name.lower()):
        return "built"
    built = sum(1 for f in frames if f.rle and fills_frame(f, BUILT))
    return "built" if built * 2 > len(frames) else "objects"


def layout(frames: list[u7art.Frame]) -> tuple[int, int, list[tuple[int, int]]]:
    """The sheet's size and each frame's top-left in it: a grid of cells as big
    as the largest frame, GAP apart, no wider than SHEET_WIDTH where it can."""
    cw = max(f.width for f in frames) * SCALE + GAP
    ch = max(f.height for f in frames) * SCALE + GAP
    cols = max(1, min(8, len(frames), SHEET_WIDTH // cw))
    rows = (len(frames) + cols - 1) // cols
    at = []
    for i, f in enumerate(frames):
        x = (i % cols) * cw + GAP // 2 + (cw - GAP - f.width * SCALE) // 2
        y = (i // cols) * ch + GAP // 2 + (ch - GAP - f.height * SCALE) // 2
        at.append((x, y))
    return cols * cw, rows * ch, at


def world_name(n: int) -> str:
    """The n-th world view's name: in_world, in_world_2, ..."""
    return "in_world" if n == 0 else f"in_world_{n + 1}"


def save_view(folder: Path, name: str, rgb: np.ndarray, sil: np.ndarray) -> list[int]:
    """The view at 4x and a copy with `sil` (at 4x) outlined; returns its size."""
    from world_view import MARK, outline

    big = four_x(rgb)
    Image.fromarray(big, "RGB").save(folder / f"{name}.png", compress_level=1)
    marked = big.copy()
    marked[outline(sil, 3)] = MARK
    Image.fromarray(marked, "RGB").save(folder / f"{name}_marked.png", compress_level=1)
    return [big.shape[1], big.shape[0]]


def vga_tile(view, shape: int, frame: int) -> np.ndarray | None:
    """A terrain tile's palette indices; None for a frame the shape lacks
    (the map uses a few, which paint nothing)."""
    fr = view.vga.frame(shape, frame)
    return None if fr is None else fr.index


def world_views(view, kind: str, shape: int, folder: Path) -> tuple[dict | None, dict[int, tuple[str, int, int]]]:
    """The set in the world, the piece (or the terrain's tiles) outlined in a
    _marked copy: for terrain as many views as it takes (up to 4) to show every
    frame the world has, else one. Returns them and, for terrain, where each
    frame's tile is (the view's name, x, y): in the first view that has it,
    nearest its middle."""
    from world_view import crop_round, terrain_windows

    tiles_at: dict[int, tuple[str, int, int]] = {}
    views = []
    if kind == "terrain":
        side = TERRAIN_WINDOW * u7art.TILE
        centre = (TERRAIN_WINDOW - 1) / 2
        pal = u7art.day_palette()
        for n, (tx0, ty0) in enumerate(terrain_windows(shape, TERRAIN_WINDOW, view.covered)):
            name = world_name(n)
            rgb = view.paint(tx0 * u7art.TILE, ty0 * u7art.TILE, side, side, objects=False)
            ids = u7art.world_tiles()[ty0:ty0 + TERRAIN_WINDOW, tx0:tx0 + TERRAIN_WINDOW]
            mine = (ids >= 0) & ((ids >> 5) == shape)
            visible = np.zeros_like(mine)
            shown = set()
            for ty, tx in sorted(zip(*np.nonzero(mine)), key=lambda p: (p[0] - centre) ** 2 + (p[1] - centre) ** 2):
                f = int(ids[ty, tx] & 31)
                # Only where the tile shows whole: an object of the chunk may lie over it.
                y, x = int(ty) * u7art.TILE, int(tx) * u7art.TILE
                index = vga_tile(view, shape, f)
                if index is None or not np.array_equal(rgb[y:y + u7art.TILE, x:x + u7art.TILE], pal[index]):
                    continue
                visible[ty, tx] = True
                shown.add(f)
                tiles_at.setdefault(f, (name, x * SCALE, y * SCALE))
            sil = four_x(np.repeat(np.repeat(visible, u7art.TILE, 0), u7art.TILE, 1))
            views.append({"file": f"{name}.png", "tile_x": tx0, "tile_y": ty0, "tiles": TERRAIN_WINDOW,
                          "frames": sorted(shown), "size": save_view(folder, name, rgb, sil)})
    else:
        k = view.instance(shape)
        rect = view.bounds(k) if k is not None else None
        if rect is not None:
            x0, y0, side = crop_round(*rect, least=160, most=288)
            # Under a roof the view is the inside, as the game shows it with the avatar there.
            roof = view.roof_over(k)
            rgb = view.paint(x0, y0, side, side, skip_lift=roof)
            sil = four_x(view.silhouette(k, x0, y0, side, side))
            where = {"file": "in_world.png", "tile_x": int(view.objs[k, 0]), "tile_y": int(view.objs[k, 1]),
                     "lift": int(view.objs[k, 2]), "frame": int(view.objs[k, 4])}
            if roof < 255:
                where["hidden_from_lift"] = roof
            where["size"] = save_view(folder, "in_world", rgb, sil)
            views.append(where)
    if not views:
        return None, tiles_at
    # The first view's details at the top, the views (terrain may have more) listed.
    return views[0] | {"views": views}, tiles_at


def export(file: str, pack: str, only: range | None = None) -> None:
    """Every set of the file, or with `only` just those shapes' sets (the
    folder's PROMPT.md, index and manifest left as they are)."""
    vga = u7art.game_file(file)
    if vga is None:
        print(f"{file}: not there (Serpent Isle isn't installed)", flush=True)
        return
    root = SETS / file
    target_dir = work_dir(pack, file)
    classes = shape_classes()
    names = shape_names(file)
    translucent = u7art.translucent_shapes()
    view = None
    if file == "shapes":
        from world_view import WorldView
        view = WorldView(vga)
    rows: list[dict] = []
    sets: dict[str, list[tuple[int, str, int, str, str]]] = {}
    for shape in only or range(len(vga.records)):
        if shape >= len(vga.records):
            break
        numbered = [(n, vga.frame(shape, n)) for n in range(vga.frame_count(shape))]
        numbered = [(n, f) for n, f in numbered if f is not None and f.opaque.any()]
        if not numbered:
            continue
        see_through = is_translucent(file, shape, translucent)
        masks = [mask(f, see_through) for _, f in numbered]
        frames = [f for _, f in numbered]
        name = names.get(shape, f"{file} {shape}")
        kind = kind_of(file, vga, shape, frames, [s for _, s in masks], classes, name)
        folder = root / kind / f"{shape:04d}_{slug(name)}"
        folder.mkdir(parents=True, exist_ok=True)

        sw, sh, at = layout(frames)
        sheet = Image.new("RGBA", (sw, sh), GROUND + (255,))
        sheet_mask = Image.new("L", (sw, sh), 0)
        world, tiles_at = world_views(view, kind, shape, folder) if view is not None and kind != "kept" else (None, {})
        entries = []
        for (n, frame), (m, _), (x, y) in zip(numbered, masks, at):
            ref = rgba(frame)
            ref.save(folder / f"{n:02d}.png", compress_level=1)
            m.save(folder / f"{n:02d}_mask.png", compress_level=1)
            sheet.alpha_composite(ref, (x, y))
            sheet_mask.paste(m, (x, y))
            context, cx, cy = None, 0, 0
            if kind == "terrain":
                context, cx, cy = tile_context(vga, shape, n), u7art.TILE * SCALE, u7art.TILE * SCALE
            elif kind != "kept" and frame.rle:
                built = built_context(frame, shape, n, see_through)
                if built is not None:
                    rgb = (np.clip(built[0], 0, 1) * 255 + 0.5).astype(np.uint8)
                    context, cx, cy = Image.fromarray(four_x(rgb), "RGB"), PAD * SCALE, PAD * SCALE
            if context is not None:
                context.save(folder / f"{n:02d}_context.png", compress_level=1)
            wv, wx, wy = tiles_at.get(n, ("", -1, -1))
            entries.append({
                "file": file, "kind": kind, "shape": shape, "frame": n, "name": name,
                "width": frame.width, "height": frame.height,
                "target_width": frame.width * SCALE, "target_height": frame.height * SCALE,
                "sheet_x": x, "sheet_y": y, "world_view": wv, "world_x": wx, "world_y": wy,
                "context": f"{n:02d}_context.png" if context is not None else "", "crop_x": cx, "crop_y": cy,
                "folder": str(folder), "target": str(target_dir / f"{shape:04d}_{n:02d}.png"),
            })
        sheet.convert("RGB").save(folder / "sheet.png", compress_level=1)
        sheet_mask.save(folder / "sheet_mask.png", compress_level=1)
        info = {"file": file, "kind": kind, "shape": shape, "name": name, "sheet": [sw, sh],
                "in_world": world, "frames": entries}
        (folder / "set.json").write_text(json.dumps(info, indent=1), encoding="utf-8")
        (folder / "prompt.txt").write_text(prompt(info, folder, target_dir), encoding="utf-8")
        rows += entries
        sizes = sorted({(e["target_width"], e["target_height"]) for e in entries})
        size = f"{sizes[0][0]}x{sizes[0][1]}" + (f" .. {sizes[-1][0]}x{sizes[-1][1]}" if len(sizes) > 1 else "")
        sets.setdefault(kind, []).append((shape, name, len(entries), size, f"{kind}/{folder.name}"))
        if shape % 200 == 0:
            print(f"{file}: shape {shape}", flush=True)

    if only is not None:
        return
    with (root / "manifest.csv").open("w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0]))
        w.writeheader()
        w.writerows(rows)
    kinds = KINDS if file == "shapes" else {file: FILE_KINDS[file]}
    (root / "PROMPT.md").write_text(overview(file, kinds, sets, root, target_dir, pack), encoding="utf-8")
    index = [f"# {file}: the sets", ""]
    for kind, (title, desc) in kinds.items():
        if kind not in sets:
            continue
        found = sets[kind]
        index += [f"## {title} (`{kind}/`): {len(found)} sets, {sum(n for _, _, n, _, _ in found)} frames", "",
                  f"{desc[0].upper()}{desc[1:]}", "", "| shape | name | frames | output size | folder |",
                  "|---|---|---|---|---|"]
        index += [f"| {s} | {n} | {c} | {z} | `{d}` |" for s, n, c, z, d in found]
        index.append("")
    (root / "index.md").write_text("\n".join(index), encoding="utf-8")
    for kind, found in sets.items():
        print(f"{file} {kind}: {len(found)} sets, {sum(n for _, _, n, _, _ in found)} frames", flush=True)
    print(root / "PROMPT.md", flush=True)
    from viewer import build
    print(build(file, pack), flush=True)


# The look asked for: the user's to choose; every prompt carries it.
STYLE = ("hand-painted, richly detailed, naturalistic fantasy illustration, as if the game had been painted "
         "at this resolution by a skilled artist: a modern remaster of a 1990s role-playing game")

GOAL = ("The aim is a new painting of the same subject, not an enlargement. The originals are 1992 pixel art at a quarter "
        "of this size: use them as a guide to WHAT is shown (the subject, its materials, colours, proportions, "
        "pose, and where it sits in its cell), never as pixels to trace, upscale, smooth or sharpen. Recreate it "
        "from scratch as an artist would, inventing the fine detail the pixels could only hint at (single stones "
        "and their mortar, wood grain and nails, folds and seams in cloth, faces with real features, leaves and "
        "bark), and read dithering and pixel patterns as the materials they stand for (a red-and-black checker "
        "on grass is a field of flowers).")

RULES = [
    "Each frame keeps its own material, pattern and colours as its original shows them, even where a world "
    "view shows something else round it: never fill a frame with the ground, floor or wall beside it (a woven "
    "mat stays a woven mat next to a stone floor).",
    "Nothing that looks upscaled: no visible pixels, staircase edges, blur or smoothing-filter look, and none "
    "of the original's dithering patterns or banded shading copied.",
    "Keep what the game relies on: each frame's size and place in its cell, the size and position of the "
    "thing in it (where it stands on the ground, how tall it is), its footprint, and each frame's pose or "
    "state. Where it touches the ground stays where it is, so it stands on the same tiles in the game.",
    "The original's projection: the ground seen straight from above, height drawn up and to the left at "
    "45 degrees (oblique, not isometric diamonds, not perspective); south and east faces show.",
    "Flat, neutral daylight in the original's light direction; no cast shadows on the ground, no glow, rim "
    "light, vignette or fog: the game lights the world itself (night, torches, spells).",
    "The original's hues and overall brightness, natural and muted. The game turns every colour into a mix of "
    "two of its 224 palette colours, so neon, strongly saturated colours and subtle hue gradients come out "
    "muddy; rich texture and value detail come through well.",
    "Where the mask is grey the game keeps the original pixels (animated water, fire and lava colours, "
    "see-through glass and mist): your paint there is replaced, so let the painting meet them.",
    "The outline: for objects and characters, deliver on a transparent background (PNG with alpha) and your "
    "own outline is used, kept inside the cell and close to the original's (white in the mask): refined, not "
    "a different shape. Without transparency the original's outline cuts the painting, so cover the whole "
    "white area and a little beyond. Terrain and built pieces always keep the original outline exactly, so "
    "they still join.",
    "One consistent style, detail level, brush and texture scale for the whole set, and across sets: stone "
    "courses, planks, shingles and grass blades at the same scale everywhere.",
    "No text, signature, border or frame lines; nothing painted between the cells (transparent or the grey).",
]

KIND_RULES = {
    "terrain": [
        "Terrain tiles meet each other on every edge: grass runs into grass, a road's edge into the grass "
        "beside it. Paint each world view (in_world.png, and in_world_2.png ... where there are more: the "
        "terrain as it lies in Britannia) as one picture of real ground (grass with blades and clover, earth "
        "with pebbles, worn paving), with no tile grid showing, all views alike; each tile of this set is cut "
        "from the view set.json names (world_view, world_x, world_y), the frames shown in none from sheet.png. "
        "Keep it seamless, without any single feature that would repeat visibly across a field.",
        "Only this set's tiles (outlined in the _marked views) are cut out; the ground round them is there to "
        "show how they meet it, and belongs to other sets.",
    ],
    "built": [
        "These pieces build walls, floors, roofs, bridges or mountains together with copies of themselves; "
        "in_world.png shows the whole they make. Keep courses, planks, shingles and rock aligned so that "
        "edges continue straight out of the cell into the next piece; where a frame has FF_context.png it is "
        "shown among its copies, to see how its edges join.",
        "Each frame may be a different material or variant (stone, brick, wood, a corner, an end); keep each "
        "as it is. Frames that are solid black are the darkness behind walls: leave them black.",
    ],
    "characters": [
        "Every frame is the same character, designed once: you may refine the design (the face, the cut of the "
        "clothes, the armour's details) as long as it stays recognisably the original's character in its "
        "colours, and identical in every frame; only the pose changes. Frames 0-15 face north and 16-31 south; "
        "the game mirrors them for west and east, so keep them free of anything that must not be mirrored "
        "(writing, a sword always in one hand).",
    ],
    "objects": [
        "A shape's frames are variants or states of one kind of thing (colours, open and closed, lit and "
        "unlit, animation steps): keep what changes between them and keep the rest identical.",
    ],
}

FILES_TREE = """\
  sheet.png            {sheet}  every original frame of the set in a grid on a grey ground: the layout
                                  to paint in (your painting replaces it, cell for cell)
  sheet_mask.png       {sheet}  the same layout: white = the original's outline (where a painting
                                  without transparency is cut), grey = original pixels the game keeps
                                  (animated colours, see-through), black = outside
{world}  FF.png               each original frame alone, blown up 4x only to show it at the final size:
                                  a guide, not a base (FF = the frame number)
  FF_mask.png          each frame's mask, as in sheet_mask.png
{context}  set.json             where each frame sits in sheet.png and in_world.png, and the file it becomes
  prompt.txt           these instructions"""


def prompt(info: dict, folder: Path, target_dir: Path) -> str:
    """The set's instructions: what it is, its folder, what to paint and deliver."""
    kind, frames = info["kind"], info["frames"]
    title, desc = (KINDS | FILE_KINDS)[kind]
    sw, sh = info["sheet"]
    world = info["in_world"]
    world_lines = ""
    views = world["views"] if world is not None else []
    for v in views:
        stem = v["file"][:-4]
        size = f"{v['size'][0]}x{v['size'][1]}".ljust(9)
        if kind == "terrain":
            what = (f"this terrain as it lies in Britannia, as the original game shows it; this set's frames "
                    f"in it: {', '.join(f'{f:02d}' for f in v['frames'])}")
        else:
            what = "the set where it stands in Britannia, among what it belongs to, as the original game shows it"
            if "hidden_from_lift" in v:
                what += ("; it stands under a roof, so the roof and what is above it are left out, as the game "
                         "shows a building's inside")
        world_lines += (f"  {v['file']:<21}{size}  {what}\n"
                        f"  {stem + '_marked.png':<21}{size}  the same, this set's "
                        f"{'tiles' if kind == 'terrain' else 'piece'} outlined in magenta\n")
    contexts = [e for e in frames if e["context"]]
    context_lines = ""
    if contexts:
        context_lines = ("  FF_context.png       (frames " + ", ".join(f"{e['frame']:02d}" for e in contexts[:12])
                         + (", ..." if len(contexts) > 12 else "")
                         + ") the frame among its neighbours, to see how its edges join\n")
    tree = FILES_TREE.format(sheet=f"{sw}x{sh}".ljust(9), world=world_lines, context=context_lines)
    lines = [
        "Paint new, high-resolution art for one set of Ultima VII: The Black Gate (1992), at 4 times the "
        "original size.",
        "",
        f"Style: {STYLE}.",
        "",
        GOAL,
        "",
        f"The set: \"{info['name']}\" (shape {info['shape']}), {title.lower()}: {desc}",
        f"{len(frames)} frame(s), originally {min(e['width'] for e in frames)}-{max(e['width'] for e in frames)} "
        "px wide; many are tiny on their own and only make sense together, so the set is painted as a whole.",
        "",
        f"The folder {folder.name}/ (every image is the original blown up to the final 4x size, hard-edged: "
        "references for the subject and the layout, not a base to upscale):",
        tree,
        "",
        "Look first at in_world.png (what the set builds or where it stands) and sheet.png (all of it); the "
        "single frames are for detail." if world is not None else
        "Look first at sheet.png (all of it); the single frames are for detail.",
        "",
        "Deliver:",
    ]
    if kind == "terrain" and world is not None:
        names = ", ".join(v["file"] for v in views)
        lines += [f"  1. a new painting of each world view's ground ({names}), each the same size as its",
                  "     original and under the same name, and",
                  f"  2. a new painting in sheet.png's layout, the same size ({sw}x{sh}), each frame in its cell,",
                  "     matching (1). The tiles are cut from (1) where they appear there, the others from (2)."]
    else:
        lines += [f"  a new painting in sheet.png's layout, the same size ({sw}x{sh}), each frame painted in its",
                  "  cell, on a transparent background if you can (PNG with alpha). If you must work smaller,",
                  "  keep the exact proportions; it is scaled back before cutting."]
    lines += ["", "Rules:"]
    lines += [f"- {r}" for r in KIND_RULES.get(kind, []) + RULES]
    lines += ["", "Frames (number: original size -> size in the sheet, its cell's top-left in sheet.png"
              + (", where it is cut from a world view" if kind == "terrain" else "") + "):"]
    for e in frames:
        w_at = ""
        if kind == "terrain":
            w_at = (f", {e['world_view']}.png ({e['world_x']}, {e['world_y']})" if e["world_x"] >= 0
                    else ", in no world view: from sheet.png")
        lines.append(f"  {e['frame']:02d}: {e['width']}x{e['height']} -> {e['target_width']}x{e['target_height']}"
                     f" at ({e['sheet_x']}, {e['sheet_y']}){w_at}")
    lines += [
        "",
        "--- For the person running this (not for the AI) ---",
        f"Set folder:   {folder}",
    ]
    if world is not None:
        where = f"tile {world['tile_x']}, {world['tile_y']}" + (f", lift {world['lift']}" if "lift" in world else "")
        lines.append(f"In the world: {where} (the console's `tp` takes the avatar there)")
    lines += [
        "Cut a painted sheet (or a painted world view, named as the view) into the frames:",
        f"  python scripts/upscale/sets.py --cut \"{folder}\" <painted.png>",
        f"They go to {target_dir}\\SSSS_FF.png; then pack.py --model {target_dir.parent.name} "
        f"--file {info['file']} and `art pack {target_dir.parent.name}` in the game.",
    ]
    return "\n".join(lines) + "\n"


def overview(file: str, kinds: dict, sets: dict, root: Path, target_dir: Path, pack: str) -> str:
    """PROMPT.md: the folder's structure and the rules every set shares."""
    count = sum(len(v) for v in sets.values())
    frames = sum(n for v in sets.values() for _, _, n, _, _ in v)
    tree = [f"{root.name}/",
            "  PROMPT.md                      this file",
            "  index.md                       every set: shape, name, frames, sizes, folder",
            "  manifest.csv                   every frame: its set, sizes, place in the sheet, the file it becomes"]
    for kind, (title, _) in kinds.items():
        if kind not in sets:
            continue
        found = sets[kind]
        tree.append(f"  {kind + '/':<30} {title}: {len(found)} sets, {sum(n for _, _, n, _, _ in found)} frames")
    example = next(iter(sets.values()))[0][4]
    tree += [f"  {example}/   one set (one shape, all its frames), e.g.:",
             "      prompt.txt                 that set's instructions (start here)",
             "      sheet.png, sheet_mask.png  all its frames in a grid, the image to paint, and its mask"]
    if file == "shapes":
        tree.append("      in_world.png, in_world_marked.png  the set where it stands in Britannia, outlined")
    tree += ["      FF.png, FF_mask.png        each frame alone and its mask (FF = frame number)",
             "      FF_context.png             (terrain, built pieces) a frame among its neighbours",
             "      set.json                   the layout: where each frame is, the file it becomes"]
    lines = [
        f"# New high-resolution art for Ultima VII's {file}",
        "",
        f"This folder holds the art of Ultima VII: The Black Gate (1992), {file}, as {count} sets "
        f"({frames} frames), each to be painted anew at 4 times the original size, one set at a time. A set is "
        "one shape with all its frames; single frames are often tiny (a ground tile is 32x32 px here) and only "
        "make sense together, so each set comes as one sheet to paint"
        + (", with the world round it." if file == "shapes" else "."),
        "",
        f"**Style:** {STYLE}.",
        "",
        GOAL,
        "",
        "## The folder",
        "",
        "```",
        *tree,
        "```",
        "",
        "Every image is the original blown up to the final 4x size, hard-edged: references for the subject and "
        "the layout, not a base to upscale. Paint at that size.",
        "",
        "## Kinds",
        "",
    ]
    for kind, (title, desc) in kinds.items():
        if kind in sets:
            lines.append(f"- **{title}** (`{kind}/`): {desc}")
            lines += [f"  {r}" for r in KIND_RULES.get(kind, [])]
    lines += ["", "## Rules for every set", ""]
    lines += [f"- {r}" for r in RULES]
    lines += [
        "",
        "## Working through a set",
        "",
        "1. Read its `prompt.txt`; look at `in_world.png` and `sheet.png` together.",
        "2. Paint a new picture in `sheet.png`'s layout at its size (terrain: of every world view's ground too), "
        "keeping to `sheet_mask.png`, on a transparent background where you can.",
        "3. Deliver the painting(s) under the same name(s).",
        "",
        "## For the person running this",
        "",
        f"- `python scripts/upscale/sets.py --cut <set folder> <painted.png>` cuts a painted sheet (or a "
        f"terrain's painted world view, named as the view: `in_world.png`, `in_world_2.png`, ...) into "
        f"`{target_dir}\\SSSS_FF.png`. For terrain cut the world views first: the sheet then fills in only "
        "the tiles no view has.",
        f"- `python scripts/upscale/sets.py --check --file {file}` counts what is drawn and lists wrong sizes.",
        f"- `scripts/upscale/.venv/Scripts/python scripts/upscale/pack.py --model {pack} --file {file}` makes "
        f"the pack; in the game, the console's `art pack {pack}`. Frames not drawn yet show the original art.",
        "",
    ]
    return "\n".join(lines)


def cut(folder: Path, painted: Path, pack: str) -> None:
    """A painted sheet or world view back into the frames' files, keeping its
    transparency (pack.py takes a painting's own outline from it). Which one
    it is goes by its name (sheet, in_world, in_world_2, ...), else by its
    proportions; it is scaled back if need be. A terrain's tiles come from
    the world views: a sheet gives only those no view has, and those whose
    file isn't there yet."""
    info = json.loads((folder / "set.json").read_text(encoding="utf-8"))
    with Image.open(painted) as raw:
        img = raw.convert("RGBA" if "A" in raw.getbands() or "transparency" in raw.info else "RGB")
    sizes = {"sheet": tuple(info["sheet"])}
    for v in (info["in_world"] or {}).get("views", []):
        sizes[v["file"][:-4]] = tuple(v["size"])
    stem = painted.stem.lower()
    source = next((name for name in sorted(sizes, key=len, reverse=True) if stem.startswith(name)), None)
    if source is None:
        ratio = img.width / img.height
        source = min(sizes, key=lambda k: abs(sizes[k][0] / sizes[k][1] - ratio))
        if abs(sizes[source][0] / sizes[source][1] - ratio) > 0.02:
            print(f"{painted}: {img.width}x{img.height} matches none of {sizes}", flush=True)
            return
    if img.size != sizes[source]:
        img = img.resize(sizes[source], Image.LANCZOS)
    out = work_dir(pack, info["file"])
    out.mkdir(parents=True, exist_ok=True)
    made, skipped = 0, []
    for e in info["frames"]:
        target = out / f"{e['shape']:04d}_{e['frame']:02d}.png"
        if source == "sheet":
            if e["world_view"] and target.exists():
                skipped.append(f"{e['frame']:02d}")
                continue
            x, y = e["sheet_x"], e["sheet_y"]
        elif e["world_view"] == source:
            x, y = e["world_x"], e["world_y"]
        else:
            continue
        img.crop((x, y, x + e["target_width"], y + e["target_height"])).save(target, compress_level=1)
        made += 1
    print(f"{info['name']} ({info['shape']}): {made} frames from the painted {source} into {out}", flush=True)
    if skipped:
        print(f"  kept as cut from the world views: {', '.join(skipped)}", flush=True)
    from viewer import build
    print(f"  {build(info['file'], pack)}", flush=True)


def check(file: str, pack: str) -> None:
    path = SETS / file / "manifest.csv"
    if not path.exists():
        print(f"{path}: not there; run sets.py --file {file} first", flush=True)
        return
    done: dict[str, int] = {}
    total: dict[str, int] = {}
    wrong = []
    with path.open(newline="", encoding="utf-8") as f:
        for row in csv.DictReader(f):
            kind = row["kind"]
            total[kind] = total.get(kind, 0) + 1
            target = work_dir(pack, file) / Path(row["target"]).name
            if not target.exists():
                continue
            with Image.open(target) as img:
                size = img.size
            want = (int(row["target_width"]), int(row["target_height"]))
            if size != want:
                wrong.append(f"  {target}: {size[0]}x{size[1]}, wants {want[0]}x{want[1]}")
                continue
            done[kind] = done.get(kind, 0) + 1
    for kind, n in total.items():
        print(f"{file} {kind}: {done.get(kind, 0)} of {n} drawn", flush=True)
    if wrong:
        print(f"{len(wrong)} the wrong size (pack.py skips them):", flush=True)
        print("\n".join(wrong), flush=True)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--file", default="shapes", choices=["shapes", "sprites", "gumps", "paperdolls"])
    ap.add_argument("--pack", default="painted", help="the pack the drawings become (assets/hires/<pack>)")
    ap.add_argument("--check", action="store_true", help="what is drawn, missing or the wrong size")
    ap.add_argument("--cut", nargs=2, metavar=("SET", "PAINTED"), help="a painted sheet or in_world.png into frames")
    ap.add_argument("--shapes", help="only these shapes' sets again, a range such as 150-160")
    args = ap.parse_args()
    if args.cut:
        cut(Path(args.cut[0]), Path(args.cut[1]), args.pack)
    elif args.check:
        check(args.file, args.pack)
    else:
        export(args.file, args.pack, parse_range(args.shapes) if args.shapes else None)


if __name__ == "__main__":
    main()
