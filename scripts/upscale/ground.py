"""The ground assembled from painted materials: every chunk template made
from a handful of seamless textures, so the sea is the same sea everywhere.

    python scripts/upscale/ground.py [--compose] [--pack painted]
    python scripts/upscale/ground.py --preview 74 62 [--chunks 3] [--pack painted]

Every run writes assets/hires/ground/: materials/<name>/ (reference.png,
examples.png, prompt.txt), materials.zip with PROMPT.md (the prompt for all
materials at once), orig/ and ground.html, the live page (materials with
their paintings tiled 2 x 2, every chunk by coverage, original beside
assembled, among its neighbours; Reload reads new paintings).

An image AI painting chunk after chunk paints a different sea each time
(brightness and texture drift between runs), so neighbouring chunks don't
meet. Here the AI paints each material once (sea, grass, mud, sand, ...: a
512 x 512 texture, seamless when tiled), and the chunks are put together by
rule from the original:
  * each pixel of the original's ground is a material: the nearest, by
    colour, of the materials its tile shape holds (MATERIALS lists which
    tiles are pure examples of each; a "sandy grass" pixel is sand or grass);
  * the borders between materials are traced at 4x from the original, made
    smooth (the material map blurred a little and blown up, the textures
    mixed in a narrow band);
  * a texture is laid from each chunk's top left corner and repeats every
    512 px, a chunk's size, so it runs on unbroken into the next chunk;
  * built floors (carpets, planks, tiles, cobbles, paving, ruts) keep their
    tile art (the per-set paintings where there are some, else the
    original at 4x), as their design is a grid.
The textures are assets/hires/work/<pack>/materials/<name>.png; a material
without one is shown as a plain stand-in (its colours over soft noise).
--compose writes every chunk to work/<pack>/chunks/TTTT.png for pack.py and
chunks.html; --preview a few chunks of the map, original beside assembled.
"""

from __future__ import annotations

import argparse
from functools import lru_cache
from pathlib import Path

import numpy as np
from PIL import Image

import u7art
from chunks import SIDE, coverage, ground, vga
from generate import SCALE, work_dir

# Each material: the tiles that are pure examples of it (shape: frames, or
# None for all of them). Their colours define the material.
MATERIALS: dict[str, dict[int, list[int] | None]] = {
    "sea": {19: None, 64: None, 30: None, 26: None, 20: None, 65: None, 2: None},
    # The sea's smooth stretches; told from the waves by the pixels round them (calm_sea).
    "calm sea": {20: list(range(0, 9))},
    "grass": {146: None, 147: None, 148: None, 31: None, 32: None},
    "mud": {149: None},
    "sand": {10: list(range(0, 12)), 111: list(range(0, 8))},
    "swamp": {113: [0, 1, 2, 3], 117: [0, 1, 2, 3]},
    "cave floor": {51: list(range(0, 16))},
    "cave rock": {51: list(range(16, 32)), 63: list(range(0, 15))},
}
# What each tile shape is made of, read from the tiles themselves (their
# names often say less: "sand" holds the coasts, shape 4 "grass" flowers and mud).
SHAPES: dict[tuple[str, ...], list[int]] = {
    ("sea",): [2, 19, 20, 26, 30, 64, 65],
    ("sea", "swamp"): [8],
    ("sand", "sea"): [10],
    ("sand", "mud"): [111],
    ("sand", "grass"): list(range(118, 134)),
    ("grass", "mud"): [4, 45, 83, *range(134, 146)],
    ("grass",): [146, 147, 148, *range(31, 45), 66],
    ("grass", "cave rock"): [46],
    ("mud",): [149],
    ("mud", "sea"): [7, 13, 14, 15, 29, 84, *range(85, 110), 112],
    ("swamp", "sea"): list(range(113, 118)),
    ("swamp", "mud"): [22],
    ("swamp", "mud", "sea"): [67],
    ("cave floor", "cave rock"): [5, *range(49, 63)],
    ("cave rock",): [63],
}
# Tile shapes drawn as a grid (floors, carpets, paths, the mines' planked floors), which keep their tile art.
BUILT = {0, 1, 16, 17, 18, 21, 23, 24, 25, 27, 47, 110}
# Shapes of nothing to paint (the darkness of dungeons, the void).
NOTHING = {12, 48}
TEXTURE = SIDE * SCALE


@lru_cache(maxsize=None)
def anchors() -> tuple[list[str], np.ndarray, np.ndarray]:
    """The materials' names, mean colours (CIELAB) and, per palette index,
    how often each material uses it (rows: materials)."""
    from pack import srgb_to_lab

    names = list(MATERIALS)
    use = np.zeros((len(names), 256))
    for m, name in enumerate(names):
        for shape, frames in MATERIALS[name].items():
            for f in frames if frames is not None else range(vga().frame_count(shape)):
                fr = vga().frame(shape, f)
                if fr is not None:
                    use[m] += np.bincount(fr.index.ravel(), minlength=256)
    lab = srgb_to_lab(u7art.day_palette().astype(np.float64))
    means = np.stack([(use[m][:, None] * lab).sum(0) / max(1.0, use[m].sum()) for m in range(len(names))])
    return names, means, use / np.maximum(use.sum(1, keepdims=True), 1)


@lru_cache(maxsize=None)
def shape_materials(shape: int) -> tuple[int, ...]:
    """The materials a tile shape holds: SHAPES's, else those whose colours
    cover a fair share of its pixels."""
    names, _, use = anchors()
    for materials, shapes in SHAPES.items():
        if shape in shapes:
            return tuple(names.index(m) for m in materials)
    idx = np.concatenate([vga().frame(shape, f).index.ravel() for f in range(vga().frame_count(shape))
                          if vga().frame(shape, f) is not None])
    hist = np.bincount(idx, minlength=256) / max(1, len(idx))
    owner = np.where(use.max(0) > 0, use.argmax(0), -1)
    share = [hist[owner == m].sum() for m in range(len(names))]
    found = tuple(m for m in range(len(names)) if share[m] >= 0.12)
    return found or (int(np.argmax(share)),)


def material_map(t: int) -> tuple[np.ndarray, np.ndarray]:
    """The template's material per pixel (128 x 128; -1 built floor or
    nothing to paint, -2 no ground) and which pixels keep their tile art."""
    from pack import srgb_to_lab

    names, means, use = anchors()
    shapes, frames = u7art.chunk_templates()
    index, has = ground(t)
    lab = srgb_to_lab(u7art.day_palette().astype(np.float64))
    out = np.full((SIDE, SIDE), -2, np.int16)
    tile_art = np.zeros((SIDE, SIDE), bool)
    for i in range(256):
        s = int(shapes[t, i])
        y, x = (i // 16) * u7art.TILE, (i % 16) * u7art.TILE
        cell = (slice(y, y + u7art.TILE), slice(x, x + u7art.TILE))
        if not has[cell].any():
            continue
        if s in BUILT or s in NOTHING:
            out[cell] = -1
            tile_art[cell] = True
            continue
        options = np.array(shape_materials(s))
        px = index[cell]
        # The material that uses the index most, among the shape's; else the nearest by colour.
        by_use = use[options][:, px]
        dist = ((lab[px][None] - means[options][:, None, None]) ** 2).sum(-1)
        choice = np.where(by_use.max(0) > 0, by_use.argmax(0), dist.argmin(0))
        out[cell] = options[choice]
    return calm_sea(out, index), tile_art


# Specks smaller than this (pixels at the original size) take the material round them.
SPECK = 24


def calm_sea(mat: np.ndarray, index: np.ndarray) -> np.ndarray:
    """The sea's smooth stretches as "calm sea": wavy and calm water mix
    within the sea's tiles, so it goes by whether the colour varies in the
    5 x 5 pixels round a sea pixel (the colour cycling glints left out)."""
    from scipy import ndimage

    names, _, _ = anchors()
    sea, calm = names.index("sea"), names.index("calm sea")
    rgb = u7art.day_palette()[index].astype(np.float32)
    glint = (index >= u7art.CYCLING_FIRST) & (index <= u7art.CYCLING_LAST)
    mean = np.stack([ndimage.uniform_filter(rgb[..., c], 5, mode="nearest") for c in range(3)], -1)
    sq = np.stack([ndimage.uniform_filter(rgb[..., c] ** 2, 5, mode="nearest") for c in range(3)], -1)
    spread = np.sqrt(np.clip(sq - mean ** 2, 0, None)).mean(-1)
    out = mat.copy()
    out[(mat == sea) & ~glint & (spread < 2.5)] = calm
    return out


def drop_specks(best: np.ndarray, count: int) -> np.ndarray:
    """Material islands smaller than SPECK pixels (a flower, a pebble in the
    grass) taken over by what is round them."""
    from scipy import ndimage

    out = best.copy()
    for m in range(count):
        labels, n = ndimage.label(best == m)
        if not n:
            continue
        sizes = ndimage.sum(np.ones_like(labels), labels, range(1, n + 1))
        small = np.isin(labels, np.flatnonzero(sizes < SPECK) + 1)
        out[small] = -1
    if (out < 0).any() and (out >= 0).any():
        _, (iy, ix) = ndimage.distance_transform_edt(out < 0, return_indices=True)
        out = out[iy, ix]
    return out


def smooth_weights(mat: np.ndarray, count: int) -> np.ndarray:
    """Per material, its weight at 4x: the one-hot map de-speckled (a 3x3
    majority), blurred a little and blown up; mixed only in a narrow band."""
    from scipy import ndimage

    onehot = np.stack([(mat == m).astype(np.float32) for m in range(count)])
    known = (mat >= 0).astype(np.float32)
    # A 3x3 majority among the materials: lone pixels of the other material go.
    votes = np.stack([ndimage.uniform_filter(o, 3, mode="nearest") for o in onehot])
    best = np.where(known > 0, votes.argmax(0), -1)
    # Cells without a material (built floors, no ground) take the nearest material, as do specks.
    best = drop_specks(best, count)
    onehot = np.stack([(best == m).astype(np.float32) for m in range(count)])
    blurred = np.stack([ndimage.gaussian_filter(o, 0.8, mode="nearest") for o in onehot])
    big = np.stack([ndimage.zoom(b, SCALE, order=1, mode="nearest", grid_mode=True) for b in blurred])
    sharp = np.clip(big, 0, None) ** 6
    return sharp / np.maximum(sharp.sum(0, keepdims=True), 1e-6)


@lru_cache(maxsize=None)
def texture(pack: str, m: int) -> np.ndarray:
    """A material's painted texture (512 x 512 RGB float), or a plain
    stand-in: its colours over soft, seamless noise."""
    names, means, use = anchors()
    path = work_dir(pack, "materials") / f"{names[m].replace(' ', '_')}.png"
    if path.exists():
        with Image.open(path) as img:
            img = img.convert("RGB")
            if img.size != (TEXTURE, TEXTURE):
                img = img.resize((TEXTURE, TEXTURE), Image.LANCZOS)
            return np.asarray(img).astype(np.float32)
    pal = u7art.day_palette().astype(np.float32)
    colour = (use[m][:, None] * pal).sum(0)
    rng = np.random.default_rng(m + 1)
    # Seamless noise: random phases under a 1/f falloff, periodic by construction.
    f = np.fft.fftfreq(TEXTURE)
    radius = np.sqrt(f[:, None] ** 2 + f[None, :] ** 2)
    spectrum = np.where(radius > 0, 1 / np.maximum(radius, 1e-6) ** 1.6, 0) * np.exp(
        2j * np.pi * rng.random((TEXTURE, TEXTURE)))
    noise = np.fft.ifft2(spectrum).real
    noise = (noise - noise.mean()) / (noise.std() + 1e-6)
    return np.clip(colour[None, None, :] * (1 + 0.12 * noise[..., None]), 0, 255)


@lru_cache(maxsize=None)
def tile_art_4x(pack: str, shape: int, frame: int) -> np.ndarray:
    """A built floor's tile at 4x: its per-set painting if there is one, else the original blown up."""
    path = work_dir(pack, "shapes") / f"{shape:04d}_{frame:02d}.png"
    if path.exists():
        with Image.open(path) as img:
            if img.size == (u7art.TILE * SCALE, u7art.TILE * SCALE):
                return np.asarray(img.convert("RGB")).astype(np.float32)
    fr = vga().frame(shape, frame)
    rgb = u7art.day_palette()[fr.index].astype(np.float32)
    return np.repeat(np.repeat(rgb, SCALE, 0), SCALE, 1)


def compose(t: int, pack: str) -> np.ndarray:
    """A chunk template's ground at 4x (512 x 512 RGB), assembled."""
    names, _, _ = anchors()
    mat, tile_art = material_map(t)
    weights = smooth_weights(mat, len(names))
    out = np.zeros((TEXTURE, TEXTURE, 3), np.float32)
    for m in range(len(names)):
        if weights[m].max() > 1e-3:
            out += weights[m][..., None] * texture(pack, m)
    shapes, frames = u7art.chunk_templates()
    for i in range(256):
        y, x = (i // 16) * u7art.TILE, (i % 16) * u7art.TILE
        if tile_art[y, x]:
            s, f = int(shapes[t, i]), int(frames[t, i])
            ys, xs = y * SCALE, x * SCALE
            out[ys:ys + u7art.TILE * SCALE, xs:xs + u7art.TILE * SCALE] = tile_art_4x(pack, s, f)
    return np.clip(out + 0.5, 0, 255).astype(np.uint8)


def preview(cx: int, cy: int, n: int, pack: str, out: Path) -> Path:
    """n x n chunks of the map from (cx, cy): the original at 4x beside the assembled ground."""
    tmap = u7art.chunk_map()
    pal = u7art.day_palette()
    side = SIDE * SCALE
    orig = np.zeros((n * side, n * side, 3), np.uint8)
    made = np.zeros_like(orig)
    for dy in range(n):
        for dx in range(n):
            t = int(tmap[(cy + dy) % 192, (cx + dx) % 192])
            index, has = ground(t)
            o = np.where(has[..., None], pal[index], 0)
            orig[dy * side:(dy + 1) * side, dx * side:(dx + 1) * side] = np.repeat(np.repeat(o, SCALE, 0), SCALE, 1)
            made[dy * side:(dy + 1) * side, dx * side:(dx + 1) * side] = compose(t, pack)
    Image.fromarray(np.concatenate([orig, np.full((n * side, 16, 3), 40, np.uint8), made], 1), "RGB").save(out)
    return out


GROUND = u7art.HIRES / "ground"

# What each material is, for the AI (the tiles show the rest).
DESCRIPTIONS = {
    "sea": "the open sea seen from above: deep blue water with small waves. The game adds its white glints "
           "(they sparkle), so paint none.",
    "calm sea": "the same sea where it lies still (deep calm water, sheltered stretches): no waves, a smooth "
                "deep blue that blends into the sea without an edge.",
    "grass": "meadow grass seen from above, short and dense, a few tiny flowers at most.",
    "mud": "dark brown wet earth with ridges and furrows: river banks, fields, the edges of swamps.",
    "sand": "beach and desert sand, fine, with faint ripples.",
    "swamp": "boggy yellow-green marsh ground: wet moss, sedge and puddle-dark patches. The swamp never "
             "lies alone, so its references show water and mud too: paint only the yellow-green ground.",
    "cave floor": "the brown earthen floor of caves and mines, trodden, with grit.",
    "cave rock": "the grey rocky floor of caves: flat, cracked stone.",
}


def file_name(name: str) -> str:
    return name.replace(" ", "_")


def painted(pack: str, m: int) -> bool:
    names, _, _ = anchors()
    return (work_dir(pack, "materials") / f"{file_name(names[m])}.png").exists()


@lru_cache(maxsize=None)
def template_materials(t: int) -> tuple[int, ...]:
    """The materials a template's assembled ground shows."""
    mat, _ = material_map(t)
    return tuple(int(m) for m in np.unique(mat[mat >= 0]))


def material_rows() -> list[dict]:
    """Each material with its share of the map and the templates that are
    most purely it (its references)."""
    names, _, _ = anchors()
    rows = coverage()
    share = np.zeros(len(names))
    purest: list[list[tuple[float, int, int]]] = [[] for _ in names]
    for t, row in rows.items():
        mat, _ = material_map(t)
        known = max(1, int((mat >= 0).sum()))
        for m in template_materials(t):
            part = float((mat == m).sum()) / known
            share[m] += row["share"] * part * float((mat >= 0).mean())
            # The references: the chunks most wholly of it (no darkness or floor round it).
            purest[m].append((round(float((mat == m).mean()), 2), row["n"], t))
    out = []
    for m, name in enumerate(names):
        refs = [t for _, _, t in sorted(purest[m], reverse=True)[:4]]
        out.append({"m": m, "name": name, "file": file_name(name), "desc": DESCRIPTIONS.get(name, ""),
                    "share": float(share[m]), "refs": refs})
    return out


def prompt_text(materials: list[dict], target: Path) -> str:
    from sets import STYLE

    lines = [
        f"# The ground of Ultima VII: {len(materials)} materials to paint",
        "",
        f"**Style:** {STYLE}.",
        "",
        "The ground of the game's world (Ultima VII: The Black Gate, 1992) is assembled from these materials: "
        "the game lays each one wherever the original has it and makes the borders between them itself "
        "(coasts, field edges, river banks). So paint each material on its own, as a texture, never with "
        "borders, coasts or other materials in it.",
        "",
        "## What to paint",
        "",
        "Each material is one seamless texture of exactly 512 x 512 px. It repeats every 512 px across the "
        "whole world (a 2 x 2 tiling must show no edge at all), so nothing in it may stand out: no single "
        "big stone, patch or wave that would be seen again and again.",
        "",
        "Scale: the world is seen from straight above; 4 px of your texture are one pixel of the 1992 game, "
        "a tile (a step) is 32 px, a person stands about 3 tiles tall. Grass blades, pebbles and ripples "
        "at that scale.",
        "",
        "Paint them all in one go, in one style, at one scale and in the same light, so they sit together; "
        "the sea and the calm sea are the same water.",
        "",
        "## Rules",
        "",
        "- Seen straight from above, in flat neutral daylight: no cast shadows, no glow, vignette or fog; the "
        "game lights the world itself (night, torches, spells).",
        "- The hues and overall brightness of the references (reference.png and examples.png in each "
        "folder: the 1992 art blown up 4x), natural and muted. The game turns every colour into a mix of two "
        "of its 224 palette colours, so neon or strongly saturated colours come out muddy; rich texture and "
        "value detail come through well.",
        "- Not an enlargement of the references: paint the material anew, as it would really look, with "
        "the detail the 1992 pixels could only hint at; no visible pixels, dithering or blur.",
        "- Only the material: no objects, plants taller than grass, rocks bigger than a pebble, paths, "
        "borders, text, signature or frame.",
        "",
        f"Save each as a 512 x 512 RGB PNG named as its folder, into **{target}**.",
        "",
        "## The materials",
        "",
    ]
    for r in materials:
        lines.append(f"- `{r['file']}` ({100 * r['share']:.1f}% of the map): {r['desc']}")
    return "\n".join(lines) + "\n"


def write_jobs(pack: str) -> list[dict]:
    """The materials' folders (references and prompt), their zip and PROMPT.md."""
    import zipfile

    materials = material_rows()
    target = work_dir(pack, "materials")
    pal = u7art.day_palette()
    big = lambda a: np.repeat(np.repeat(a, SCALE, 0), SCALE, 1)  # noqa: E731
    for r in materials:
        folder = GROUND / "materials" / r["file"]
        folder.mkdir(parents=True, exist_ok=True)
        crops = []
        for t in r["refs"]:
            index, has = ground(t)
            crops.append(big(np.where(has[..., None], pal[index], 0).astype(np.uint8)))
        if crops:
            Image.fromarray(crops[0], "RGB").save(folder / "reference.png")
            Image.fromarray(np.concatenate(crops, 1), "RGB").save(folder / "examples.png")
        (folder / "prompt.txt").write_text(
            f"{r['name']}: {r['desc']}\n\nreference.png: the material in the 1992 art at 4x (512 x 512).\n"
            f"examples.png: more places where it lies.\nSave the painting as {target / (r['file'] + '.png')}\n",
            encoding="utf-8")
    text = prompt_text(materials, target)
    (GROUND / "PROMPT.md").write_text(text, encoding="utf-8")
    with zipfile.ZipFile(GROUND / "materials.zip", "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("PROMPT.md", text)
        for r in materials:
            for f in sorted((GROUND / "materials" / r["file"]).iterdir()):
                z.write(f, f"{r['file']}/{f.name}")
    return materials


def compose_all(pack: str) -> tuple[int, int]:
    """Every template whose materials are all painted, assembled into
    work/<pack>/chunks/TTTT.png; returns (assembled, waiting for a material)."""
    out = work_dir(pack, "chunks")
    out.mkdir(parents=True, exist_ok=True)
    made = waiting = 0
    for t in sorted(coverage()):
        mats = template_materials(t)
        if not mats:
            continue
        if not all(painted(pack, m) for m in mats):
            waiting += 1
            continue
        Image.fromarray(compose(t, pack), "RGB").save(out / f"{t:04d}.png", compress_level=1)
        made += 1
    return made, waiting


def page(pack: str) -> Path:
    """ground.html: the materials (reference, painting tiled 2 x 2, prompt)
    and every chunk by coverage (original beside assembled, among its
    neighbours); the paintings are read live from the work folder."""
    import json
    import os

    names, _, _ = anchors()
    materials = material_rows()
    rows = coverage()
    (GROUND / "orig").mkdir(parents=True, exist_ok=True)
    pal = u7art.day_palette()
    for t in rows:
        path = GROUND / "orig" / f"{t:04d}.png"
        if not path.exists():
            index, has = ground(t)
            Image.fromarray(np.where(has[..., None], pal[index], 0).astype(np.uint8), "RGB").save(path)
    chunks = sorted(rows.values(), key=lambda r: (-r["n"], r["t"]))
    for r in chunks:
        r["mats"] = [names[m] for m in template_materials(r["t"])]
    data = {
        "pack": pack, "prompt": (GROUND / "PROMPT.md").read_text(encoding="utf-8") if (GROUND / "PROMPT.md").exists() else "",
        "zip": str(GROUND / "materials.zip"), "target": str(work_dir(pack, "materials")),
        "materialsDir": os.path.relpath(work_dir(pack, "materials"), GROUND).replace("\\", "/"),
        "chunksDir": os.path.relpath(work_dir(pack, "chunks"), GROUND).replace("\\", "/"),
        "materials": materials, "chunks": chunks,
    }
    path = GROUND / "ground.html"
    path.write_text(PAGE.replace("/*DATA*/null", json.dumps(data, separators=(",", ":"))), encoding="utf-8")
    return path


PAGE = r"""<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>U7 ground</title>
<style>
:root { --bg: #16181c; --panel: #1e2127; --line: #2c3038; --text: #d9dce1; --dim: #8b919c; --accent: #d4a94f;
        --ok: #6fbf73; --sel: #2a2f38; }
* { box-sizing: border-box; }
html, body { height: 100%; margin: 0; }
body { background: var(--bg); color: var(--text); font: 13px/1.45 system-ui, "Segoe UI", sans-serif;
       display: grid; grid-template-columns: 300px 1fr; }
aside { border-right: 1px solid var(--line); background: var(--panel); display: flex; flex-direction: column; min-height: 0; }
aside header { padding: 12px; border-bottom: 1px solid var(--line); display: grid; gap: 8px; }
aside h1 { font-size: 14px; margin: 0; cursor: pointer; }
.dim { color: var(--dim); font-weight: 400; }
input, button { background: var(--bg); color: var(--text); border: 1px solid var(--line); border-radius: 4px; padding: 5px 8px; font: inherit; }
button { cursor: pointer; } button:hover { border-color: var(--accent); }
#list { overflow-y: auto; flex: 1; }
.head { padding: 8px 12px; font-weight: 600; color: var(--dim); text-transform: uppercase; font-size: 11px; letter-spacing: .06em;
        position: sticky; top: 0; background: var(--panel); border-bottom: 1px solid var(--line); }
.item { display: grid; grid-template-columns: 1fr auto; gap: 6px; padding: 3px 12px; cursor: pointer; align-items: center; }
.item:hover { background: var(--sel); }
.item.on { background: var(--sel); box-shadow: inset 3px 0 var(--accent); }
.num { color: var(--dim); font-variant-numeric: tabular-nums; font-size: 12px; }
.dot { width: 8px; height: 8px; border-radius: 50%; display: inline-block; background: var(--line); margin-left: 6px; }
.dot.yes { background: var(--ok); }
main { overflow-y: auto; min-width: 0; }
.top { position: sticky; top: 0; z-index: 2; background: color-mix(in srgb, var(--bg) 92%, transparent);
       backdrop-filter: blur(4px); border-bottom: 1px solid var(--line); padding: 12px 20px; display: grid; gap: 8px; }
.top h2 { margin: 0; font-size: 18px; } .top h2 span { color: var(--dim); font-weight: 400; font-size: 13px; margin-left: 8px; }
.row { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
.body { padding: 16px 20px 40px; display: grid; grid-template-columns: minmax(0, 1fr); gap: 22px; }
h3 { margin: 0 0 8px; font-size: 12px; color: var(--dim); font-weight: 600; text-transform: uppercase; letter-spacing: .06em; }
.pics { display: flex; gap: 16px; flex-wrap: wrap; align-items: flex-start; }
figure { margin: 0; display: grid; gap: 4px; }
figcaption { font-size: 11px; color: var(--dim); max-width: 520px; }
.px { image-rendering: pixelated; }
.tiled { background-repeat: repeat; border: 1px solid var(--line); }
.empty { display: grid; place-items: center; color: var(--dim); border: 1px dashed var(--line); text-align: center; padding: 8px; }
.ctx { display: grid; grid-template-columns: repeat(3, 1fr); line-height: 0; background: #000; }
.ctx img { width: 100%; display: block; }
.ctx img.fallback { filter: saturate(.3) brightness(.65); }
.ctx .mid { outline: 2px solid var(--accent); outline-offset: -2px; position: relative; z-index: 1; }
.cards { display: grid; grid-template-columns: repeat(auto-fill, minmax(420px, 1fr)); gap: 14px; }
.card { background: var(--panel); border: 1px solid var(--line); border-radius: 6px; padding: 10px; cursor: pointer; }
.card:hover { border-color: var(--accent); }
.card .hd { display: flex; justify-content: space-between; margin-bottom: 6px; }
pre { background: var(--panel); border: 1px solid var(--line); border-radius: 6px; padding: 12px; margin: 0;
      overflow-x: auto; font: 12px/1.45 ui-monospace, Consolas, monospace; white-space: pre-wrap; }
.note { color: var(--dim); }
</style>
</head>
<body>
<aside>
  <header>
    <h1 id="home">The ground <span class="dim">materials and chunks</span></h1>
    <div class="row"><input id="q" type="search" placeholder="Chunk number or material" style="flex:1"><button id="reload">Reload</button></div>
  </header>
  <div id="list"></div>
</aside>
<main id="main"></main>
<script>
const D = /*DATA*/null;
const pad = (n, w = 4) => String(n).padStart(w, "0");
const esc = s => String(s).replace(/[&<>"]/g, c => ({"&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;"}[c]));
let stamp = Date.now();
const done = new Map();
const tex = m => `${D.materialsDir}/${m.file}.png?t=${stamp}`;
const made = t => `${D.chunksDir}/${pad(t)}.png?t=${stamp}`;
const orig = t => `orig/${pad(t)}.png`;
const chunkOf = t => D.chunks.find(c => c.t === t);
let view = {home: true};

function probe() {
  for (const m of D.materials) {
    const img = new Image();
    img.onload = () => { done.set(m.name, true); renderList(); };
    img.onerror = () => { done.set(m.name, false); renderList(); };
    img.src = tex(m);
  }
}

function copy(text, btn) {
  const ok = () => { const old = btn.textContent; btn.textContent = "Copied"; setTimeout(() => btn.textContent = old, 1200); };
  if (navigator.clipboard && window.isSecureContext) { navigator.clipboard.writeText(text).then(ok); return; }
  const ta = document.createElement("textarea"); ta.value = text; document.body.appendChild(ta); ta.select();
  document.execCommand("copy"); ta.remove(); ok();
}

function renderList() {
  const q = document.getElementById("q").value.trim().toLowerCase();
  const mats = D.materials.filter(m => !q || m.name.includes(q));
  const chunks = D.chunks.filter(c => !q || String(c.t).includes(q.replace(/^0+(?=\d)/, "")) || c.mats.some(n => n.includes(q)));
  document.getElementById("list").innerHTML =
    `<div class="head">Materials · ${[...done.values()].filter(Boolean).length} of ${D.materials.length} painted</div>` +
    mats.map(m => `<div class="item ${view.m === m.name ? "on" : ""}" data-m="${esc(m.name)}"><span>${esc(m.name)}</span>
      <span class="num">${(100 * m.share).toFixed(1)}%<span class="dot ${done.get(m.name) ? "yes" : ""}"></span></span></div>`).join("") +
    `<div class="head">Chunks by coverage · ${D.chunks.length}</div>` +
    chunks.slice(0, 400).map(c => `<div class="item ${view.t === c.t ? "on" : ""}" data-t="${c.t}"><span><span class="num">${pad(c.t)}</span> ${esc(c.mats.join(", ") || "tiles only (floors, darkness)")}</span>
      <span class="num">${(100 * c.share).toFixed(2)}%</span></div>`).join("") +
    (chunks.length > 400 ? `<div class="item note">... ${chunks.length - 400} more: search by number</div>` : "");
}

function live(src, w, h, fallback, label) {
  return `<img src="${src}" width="${w}" height="${h}" alt="" data-fallback="${fallback}" data-label="${esc(label)}" class="live">`;
}
function wire() {
  for (const img of document.querySelectorAll("img.live")) img.onerror = () => {
    if (img.dataset.fallback) { img.onerror = null; img.src = img.dataset.fallback; img.classList.add("px", "fallback"); return; }
    img.outerHTML = `<div class="empty" style="width:${img.width}px;height:${img.height}px">${img.dataset.label}</div>`;
  };
}

function tiled(m, size) {
  return `<div class="tiled" style="width:${size}px;height:${size}px;background-image:url('${tex(m)}');background-size:${size / 2}px ${size / 2}px"></div>`;
}

function renderHome() {
  document.getElementById("main").innerHTML = `<div class="top"><h2>The ground<span>${D.materials.length} materials, ${D.chunks.length} chunks assembled from them</span></h2>
      <div class="row"><button id="cp">Copy the prompt</button><button id="cz">Copy the zip's path</button><button id="ct">Copy the target folder</button>
      <span class="note">${esc(D.zip)}</span></div></div>
    <div class="body">
      <section><h3>Materials: reference and painting, the painting tiled 2 × 2 to show its seams</h3><div class="cards">
      ${D.materials.map(m => `<div class="card" data-m="${esc(m.name)}"><div class="hd"><b>${esc(m.name)}</b><span class="num">${(100 * m.share).toFixed(1)}% of the map</span></div>
        <div class="pics"><figure><img class="px" src="materials/${m.file}/reference.png" width="190" height="190" alt=""><figcaption>1992</figcaption></figure>
        <figure>${done.get(m.name) ? tiled(m, 190) : `<div class="empty" style="width:190px;height:190px">not painted yet</div>`}<figcaption>painted, 2 × 2</figcaption></figure></div></div>`).join("")}
      </div></section>
      <section><h3>The prompt (with materials.zip)</h3><pre>${esc(D.prompt)}</pre></section>
    </div>`;
  document.getElementById("cp").onclick = e => copy(D.prompt, e.target);
  document.getElementById("cz").onclick = e => copy(D.zip, e.target);
  document.getElementById("ct").onclick = e => copy(D.target, e.target);
}

function renderMaterial(name) {
  const m = D.materials.find(x => x.name === name);
  const uses = D.chunks.filter(c => c.mats.includes(name)).slice(0, 6);
  document.getElementById("main").innerHTML = `<div class="top"><h2>${esc(m.name)}<span>${(100 * m.share).toFixed(1)}% of the map</span></h2>
      <div class="row"><span class="note">${esc(m.desc)}</span></div></div>
    <div class="body">
      <section><h3>Painting, and tiled 2 × 2 (no edge may show)</h3><div class="pics">
        <figure>${live(tex(m), 512, 512, "", "not painted yet<br>" + D.target + "\\" + m.file + ".png")}<figcaption>${esc(D.target)}\\${esc(m.file)}.png</figcaption></figure>
        <figure>${tiled(m, 512)}<figcaption>2 × 2</figcaption></figure></div></section>
      <section><h3>The 1992 art</h3><div class="pics"><figure><img class="px" src="materials/${m.file}/examples.png" style="max-width:100%" alt=""><figcaption>Where it lies most purely (4×)</figcaption></figure></div></section>
      <section><h3>Chunks with it</h3><div class="cards">${uses.map(c => card(c)).join("")}</div></section>
    </div>`;
  wire(); hookCards();
}

function card(c) {
  return `<div class="card" data-t="${c.t}"><div class="hd"><b>${pad(c.t)}</b><span class="num">${c.n}× · ${(100 * c.share).toFixed(2)}%</span></div><div class="pics">
    <figure><img class="px" src="${orig(c.t)}" width="190" height="190" alt=""><figcaption>1992</figcaption></figure>
    <figure>${live(made(c.t), 190, 190, "", "not assembled yet")}<figcaption>assembled</figcaption></figure></div></div>`;
}

function ctx(c, assembled) {
  return `<div class="ctx" style="width:600px">${c.around.map((n, i) => assembled
    ? `<img class="live ${i === 4 ? "mid" : ""}" src="${made(n)}" data-fallback="${orig(n)}" alt="">`
    : `<img class="px ${i === 4 ? "mid" : ""}" src="${orig(n)}" alt="">`).join("")}</div>`;
}

function renderChunk(t) {
  const c = chunkOf(t);
  document.getElementById("main").innerHTML = `<div class="top"><h2>Chunk ${pad(t)}<span>used ${c.n} times · ${(100 * c.share).toFixed(2)}% of the map · at chunk ${c.at[0]}, ${c.at[1]}</span></h2>
      <div class="row"><span class="note">Materials: ${c.mats.map(n => `<a href="#" data-m="${esc(n)}" style="color:var(--accent)">${esc(n)}</a>`).join(", ") || "none: tiles only (floors, darkness), which stay as they are"}</span></div></div>
    <div class="body">
      <section><h3>The chunk</h3><div class="pics">
        <figure><img class="px" src="${orig(t)}" width="512" height="512" alt=""><figcaption>1992</figcaption></figure>
        <figure>${live(made(t), 512, 512, "", "not assembled yet: its materials aren't all painted (python scripts/upscale/ground.py --compose)")}<figcaption>Assembled from the painted materials</figcaption></figure></div></section>
      <section><h3>Among its neighbours</h3><div class="pics">
        <figure>${ctx(c, false)}<figcaption>1992</figcaption></figure>
        <figure>${ctx(c, true)}<figcaption>Assembled (greyed: not assembled yet)</figcaption></figure></div></section>
    </div>`;
  wire();
  for (const a of document.querySelectorAll("a[data-m]")) a.onclick = e => { e.preventDefault(); select({m: a.dataset.m}); };
}

function hookCards() {
  for (const el of document.querySelectorAll(".card")) el.onclick = () => select(el.dataset.t ? {t: Number(el.dataset.t)} : {m: el.dataset.m});
}

function select(v) {
  view = v;
  history.replaceState(null, "", v.t !== undefined ? `#${pad(v.t)}` : v.m ? `#m-${v.m}` : "#");
  if (v.t !== undefined) renderChunk(v.t); else if (v.m) renderMaterial(v.m); else { renderHome(); hookCards(); }
  renderList();
  document.getElementById("main").scrollTop = 0;
}

document.getElementById("list").onclick = e => { const it = e.target.closest(".item"); if (!it) return;
  if (it.dataset.t) select({t: Number(it.dataset.t)}); else if (it.dataset.m) select({m: it.dataset.m}); };
document.getElementById("home").onclick = () => select({home: true});
document.getElementById("q").oninput = renderList;
document.getElementById("reload").onclick = () => { stamp = Date.now(); done.clear(); probe(); select(view); };
const h = decodeURIComponent(location.hash.slice(1));
probe();
select(h.startsWith("m-") ? {m: h.slice(2)} : h && chunkOf(Number(h)) ? {t: Number(h)} : {home: true});
setTimeout(() => { if (view.home) select(view); }, 400);
</script>
</body>
</html>
"""


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--compose", action="store_true", help="assemble every chunk whose materials are painted")
    ap.add_argument("--preview", nargs=2, type=int, metavar=("CX", "CY"), help="chunks from this chunk of the map")
    ap.add_argument("--chunks", type=int, default=3)
    ap.add_argument("--out", default=str(u7art.HIRES / "ground_preview.png"))
    ap.add_argument("--pack", default="painted")
    args = ap.parse_args()
    if args.preview:
        print(preview(args.preview[0], args.preview[1], args.chunks, args.pack, Path(args.out)), flush=True)
        return
    write_jobs(args.pack)
    if args.compose:
        made, waiting = compose_all(args.pack)
        print(f"{made} chunks assembled, {waiting} waiting for a material", flush=True)
    print(page(args.pack), flush=True)


if __name__ == "__main__":
    main()
