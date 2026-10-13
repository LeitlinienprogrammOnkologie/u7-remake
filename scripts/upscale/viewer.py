"""A page to compare the original art with its new paintings, set by set.

    python scripts/upscale/viewer.py [--file shapes] [--pack painted]

Writes assets/hires/sets/<file>/viewer.html (open it in a browser): the kinds
(terrain, built pieces, characters, objects, kept) with their sets in a list
to search and filter by progress; a set shows each frame's original (FF.png,
at 4x) beside its painting (assets/hires/work/<pack>/<file>/SSSS_FF.png),
flags paintings of the wrong size (pack.py skips them), and shows the set's
world views full size: the original beside the same view drawn with every
painting there is (render_new). Terrain can be shown tiled 3x3, to see the seams.
Run it again after new paintings come in (sets.py --cut does it itself).
"""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path

from PIL import Image

from generate import work_dir
from sets import FILE_KINDS, KINDS, SETS


def gather(file: str, pack: str) -> dict:
    """Every set's frames and how far their paintings are."""
    root = SETS / file
    work = work_dir(pack, file)
    kinds = KINDS if file == "shapes" else {file: FILE_KINDS[file]}
    out = []
    for kind in kinds:
        for path in sorted((root / kind).glob("*/set.json")):
            info = json.loads(path.read_text(encoding="utf-8"))
            frames = []
            for e in info["frames"]:
                target = work / f"{e['shape']:04d}_{e['frame']:02d}.png"
                state, size, stamp = "missing", None, 0
                if target.exists():
                    with Image.open(target) as img:
                        size = list(img.size)
                    stamp = int(target.stat().st_mtime)
                    state = "painted" if size == [e["target_width"], e["target_height"]] else "wrong"
                frames.append({"f": e["frame"], "w": e["width"], "h": e["height"], "tw": e["target_width"],
                               "th": e["target_height"], "s": state, "size": size, "t": stamp,
                               "v": e.get("world_view", "")})
            views = []
            for v in (info["in_world"] or {}).get("views", []):
                new = path.parent / v["file"].replace(".png", "_new.png")
                views.append({"f": v["file"], "new": new.name if new.exists() else "",
                              "t": int(new.stat().st_mtime) if new.exists() else 0, "size": v["size"]})
            out.append({"kind": kind, "dir": path.parent.name, "shape": info["shape"], "name": info["name"],
                        "views": views, "frames": frames})
    return {"file": file, "pack": pack, "work": os.path.relpath(work, root).replace("\\", "/"),
            "kinds": [{"key": k, "title": t, "desc": d} for k, (t, d) in kinds.items()], "sets": out}


def render_new(file: str, pack: str) -> int:
    """Each world view of the sets with paintings drawn again with every
    painting there is (world_view.PaintedArt), as <view>_new.png beside it;
    a set's views are drawn again only when the paintings have changed.
    Returns how many were drawn."""
    work = work_dir(pack, file)
    pngs = list(work.glob("*.png")) if file == "shapes" and work.exists() else []
    if not pngs:
        return 0
    key = f"{pack}:{len(pngs)}:{max(p.stat().st_mtime_ns for p in pngs)}"
    todo = []
    for path in (SETS / file).glob("*/*/set.json"):
        info = json.loads(path.read_text(encoding="utf-8"))
        views = (info["in_world"] or {}).get("views", [])
        if not views or not any((work / f"{e['shape']:04d}_{e['frame']:02d}.png").exists() for e in info["frames"]):
            continue
        stamp = path.parent / ".new_key"
        done = stamp.exists() and stamp.read_text(encoding="utf-8") == key
        if not (done and all((path.parent / v["file"].replace(".png", "_new.png")).exists() for v in views)):
            todo.append((path.parent, info, views))
    if not todo:
        return 0
    import u7art
    from world_view import PaintedArt, WorldView

    view = WorldView(u7art.game_file(file))
    art = PaintedArt(view, work)
    drawn = 0
    for folder, info, views in todo:
        for v in views:
            rect = view.view_rect(v | {"shape": info["shape"]})
            if rect is None:
                continue
            x0, y0, side, objects, skip = rect
            rgb = view.paint_art(art, x0, y0, side, side, objects=objects, skip_lift=skip)
            Image.fromarray(rgb, "RGB").save(folder / v["file"].replace(".png", "_new.png"), compress_level=1)
            drawn += 1
        (folder / ".new_key").write_text(key, encoding="utf-8")
    return drawn


def build(file: str, pack: str) -> Path:
    drawn = render_new(file, pack)
    if drawn:
        print(f"{drawn} world views drawn with the paintings", flush=True)
    data = gather(file, pack)
    page = PAGE.replace("/*DATA*/null", json.dumps(data, separators=(",", ":")))
    path = SETS / file / "viewer.html"
    path.write_text(page, encoding="utf-8")
    return path


PAGE = r"""<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>U7 art: originals and paintings</title>
<style>
:root {
  --bg: #16181c; --panel: #1e2127; --line: #2c3038; --text: #d9dce1; --dim: #8b919c;
  --accent: #d4a94f; --ok: #6fbf73; --warn: #e0a040; --bad: #e06060; --sel: #2a2f38;
}
* { box-sizing: border-box; }
html, body { height: 100%; margin: 0; }
body { background: var(--bg); color: var(--text); font: 13px/1.4 system-ui, "Segoe UI", sans-serif;
       display: grid; grid-template-columns: 300px 1fr; }
aside { border-right: 1px solid var(--line); display: flex; flex-direction: column; min-height: 0; background: var(--panel); }
aside header { padding: 12px; border-bottom: 1px solid var(--line); display: grid; gap: 8px; }
aside h1 { font-size: 14px; margin: 0; font-weight: 600; }
aside h1 small { color: var(--dim); font-weight: 400; }
input, select, button { background: var(--bg); color: var(--text); border: 1px solid var(--line); border-radius: 4px;
       padding: 5px 7px; font: inherit; }
button { cursor: pointer; }
button:hover, select:hover { border-color: var(--accent); }
.row { display: flex; gap: 6px; align-items: center; flex-wrap: wrap; }
.row > input { flex: 1; min-width: 0; }
#list { overflow-y: auto; flex: 1; padding-bottom: 20px; }
details { border-bottom: 1px solid var(--line); }
summary { padding: 8px 12px; cursor: pointer; display: flex; justify-content: space-between; align-items: center;
       gap: 8px; font-weight: 600; list-style: none; position: sticky; top: 0; background: var(--panel); z-index: 1; }
summary::-webkit-details-marker { display: none; }
summary::before { content: "▸"; color: var(--dim); margin-right: 4px; }
details[open] > summary::before { content: "▾"; }
summary .title { flex: 1; }
.count { color: var(--dim); font-weight: 400; font-variant-numeric: tabular-nums; font-size: 12px; }
.set { display: grid; grid-template-columns: 40px 1fr auto; gap: 6px; padding: 3px 12px 3px 22px; cursor: pointer;
       align-items: center; }
.set:hover { background: var(--sel); }
.set.on { background: var(--sel); box-shadow: inset 3px 0 var(--accent); }
.set .num { color: var(--dim); font-variant-numeric: tabular-nums; font-size: 12px; }
.set .nm { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.bar { height: 3px; background: var(--line); border-radius: 2px; overflow: hidden; grid-column: 2 / 4; margin-top: -2px; }
.bar i { display: block; height: 100%; background: var(--ok); }
.bar i.part { background: var(--accent); }
.dot { width: 7px; height: 7px; border-radius: 50%; display: inline-block; background: var(--line); }
.dot.some { background: var(--accent); } .dot.all { background: var(--ok); } .dot.bad { background: var(--bad); }
main { overflow-y: auto; min-width: 0; }
.top { position: sticky; top: 0; z-index: 2; background: color-mix(in srgb, var(--bg) 92%, transparent);
       backdrop-filter: blur(4px); border-bottom: 1px solid var(--line); padding: 12px 20px; display: grid; gap: 8px; }
.top h2 { margin: 0; font-size: 18px; font-weight: 600; }
.top h2 span { color: var(--dim); font-weight: 400; font-size: 13px; margin-left: 8px; }
.top .row label { color: var(--dim); }
.refs { display: flex; gap: 10px; padding: 14px 20px 0; flex-wrap: wrap; }
.refs a { color: var(--dim); text-decoration: none; display: grid; gap: 4px; font-size: 11px; }
.refs a:hover { color: var(--accent); }
.refs img { height: 84px; border: 1px solid var(--line); border-radius: 3px; image-rendering: pixelated; background: #000; }
.worlds { display: grid; grid-template-columns: minmax(0, 1fr); gap: 18px; padding: 16px 20px 0; }
.world h3 { margin: 0 0 6px; font-size: 12px; font-weight: 600; color: var(--dim); }
.world .pair { flex-wrap: wrap; gap: 14px; }
.world img, .world .empty { max-width: min(640px, calc(50vw - 200px)); height: auto; }
.world .empty { aspect-ratio: 1; }
.frames { display: flex; flex-wrap: wrap; gap: 14px; padding: 16px 20px 40px; align-items: flex-start; }
.card { background: var(--panel); border: 1px solid var(--line); border-radius: 6px; padding: 8px; }
.card.missing { opacity: .75; }
.card.wrong { border-color: var(--bad); }
.card .hd { display: flex; gap: 8px; align-items: baseline; margin-bottom: 6px; font-size: 12px; }
.card .hd b { font-variant-numeric: tabular-nums; }
.card .hd .meta { color: var(--dim); }
.card .hd .flag { margin-left: auto; font-size: 11px; padding: 1px 6px; border-radius: 9px; }
.flag.painted { background: color-mix(in srgb, var(--ok) 20%, transparent); color: var(--ok); }
.flag.missing { background: var(--line); color: var(--dim); }
.flag.wrong { background: color-mix(in srgb, var(--bad) 22%, transparent); color: var(--bad); }
.pair { display: flex; gap: 8px; }
.pair figure { margin: 0; display: grid; gap: 3px; justify-items: center; }
.pair figcaption { font-size: 10px; color: var(--dim); text-transform: uppercase; letter-spacing: .06em; }
.stage { display: grid; place-items: center; border-radius: 3px; overflow: hidden; }
.stage img, .stage .tiled { display: block; }
.px { image-rendering: pixelated; }
.empty { display: grid; place-items: center; color: var(--dim); font-size: 11px; border: 1px dashed var(--line);
       border-radius: 3px; text-align: center; padding: 4px; }
body.bg-checker .stage { background: repeating-conic-gradient(#3a3d44 0 25%, #2b2e34 0 50%) 0 0 / 16px 16px; }
body.bg-dark .stage { background: #000; }
body.bg-grey .stage { background: #404040; }
body.bg-light .stage { background: #e8e8e8; }
.note { color: var(--dim); padding: 40px 20px; }
kbd { border: 1px solid var(--line); border-radius: 3px; padding: 0 4px; font-size: 11px; color: var(--dim); }
</style>
</head>
<body class="bg-checker">
<aside>
  <header>
    <h1>Originals and paintings <small id="totals"></small></h1>
    <div class="row"><input id="q" type="search" placeholder="Search name or shape number"></div>
    <div class="row">
      <select id="filter" title="Which sets to list">
        <option value="all">All sets</option>
        <option value="some">With paintings</option>
        <option value="part">Partly painted</option>
        <option value="done">Complete</option>
        <option value="none">Not painted yet</option>
        <option value="bad">Wrong sizes</option>
      </select>
    </div>
  </header>
  <div id="list"></div>
</aside>
<main id="main"></main>
<script>
const DATA = /*DATA*/null;
const pad = (n, w) => String(n).padStart(w, "0");
const esc = s => String(s).replace(/[&<>"]/g, c => ({"&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;"}[c]));
const stats = set => {
  const n = set.frames.length, p = set.frames.filter(f => f.s === "painted").length;
  return {n, p, bad: set.frames.some(f => f.s === "wrong")};
};
const store = (k, v) => { try { v === undefined ? null : localStorage.setItem("u7viewer." + k, v); return localStorage.getItem("u7viewer." + k); } catch { return null; } };
let current = null;
let zoom = store("zoom") || "auto", bg = store("bg") || "checker", mode = store("mode") || "pair";
let marked = store("marked") === "1";
document.body.className = "bg-" + bg;

function matches(set, q, filter) {
  const {n, p, bad} = stats(set);
  if (filter === "some" && !p) return false;
  if (filter === "part" && !(p && p < n)) return false;
  if (filter === "done" && p < n) return false;
  if (filter === "none" && p) return false;
  if (filter === "bad" && !bad) return false;
  if (!q) return true;
  q = q.toLowerCase();
  return set.name.toLowerCase().includes(q) || String(set.shape) === q.replace(/^0+(?=\d)/, "") || set.dir.includes(q);
}

function renderList() {
  const q = document.getElementById("q").value.trim(), filter = document.getElementById("filter").value;
  const all = DATA.sets.reduce((a, s) => { const t = stats(s); a.n += t.n; a.p += t.p; return a; }, {n: 0, p: 0});
  document.getElementById("totals").textContent = `${all.p} of ${all.n} frames painted`;
  const open = new Set([...document.querySelectorAll("#list details[open]")].map(d => d.dataset.kind));
  const html = DATA.kinds.map(k => {
    const sets = DATA.sets.filter(s => s.kind === k.key);
    if (!sets.length) return "";
    const shown = sets.filter(s => matches(s, q, filter));
    const done = sets.filter(s => { const t = stats(s); return t.p === t.n; }).length;
    const frames = sets.reduce((a, s) => { const t = stats(s); a.n += t.n; a.p += t.p; return a; }, {n: 0, p: 0});
    const isOpen = open.has(k.key) || (current && current.kind === k.key) || q || filter !== "all";
    return `<details data-kind="${k.key}" ${isOpen ? "open" : ""}><summary title="${esc(k.desc)}">
      <span class="title">${esc(k.title)}</span><span class="count">${shown.length} sets · ${frames.p}/${frames.n} frames${done ? ` · ${done} done` : ""}</span></summary>
      ${shown.map(s => {
        const t = stats(s), pct = Math.round(100 * t.p / t.n);
        const dot = t.bad ? "bad" : t.p === t.n ? "all" : t.p ? "some" : "";
        return `<div class="set ${current === s ? "on" : ""}" data-key="${s.kind}/${s.dir}">
          <span class="num">${pad(s.shape, 4)}</span><span class="nm">${esc(s.name)}</span>
          <span class="count"><span class="dot ${dot}"></span> ${t.p}/${t.n}</span>
          ${t.p ? `<div class="bar"><i class="${t.p < t.n ? "part" : ""}" style="width:${pct}%"></i></div>` : ""}</div>`;
      }).join("")}</details>`;
  }).join("");
  document.getElementById("list").innerHTML = html;
}

function stage(src, w, h, z, pixelated, tiled) {
  const cls = pixelated ? "px" : "";
  if (tiled) {
    return `<div class="stage"><div class="tiled ${cls}" style="width:${3 * w * z}px;height:${3 * h * z}px;
      background-image:url('${src}');background-size:${w * z}px ${h * z}px;background-repeat:repeat"></div></div>`;
  }
  return `<div class="stage"><img class="${cls}" src="${src}" width="${w * z}" height="${h * z}" loading="lazy" alt=""></div>`;
}

function renderSet() {
  const main = document.getElementById("main");
  if (!current) {
    main.innerHTML = `<p class="note">Pick a set on the left. <kbd>j</kbd> / <kbd>k</kbd> step through the listed sets.</p>`;
    return;
  }
  const s = current, t = stats(s), base = `${s.kind}/${s.dir}/`;
  const big = Math.max(...s.frames.map(f => Math.max(f.tw, f.th)));
  const z = zoom === "auto" ? Math.max(1, Math.min(4, Math.floor(256 / big))) : Number(zoom);
  const terrain = s.kind === "terrain";
  const tiled = terrain && mode === "tiled";
  const refs = [["sheet.png", "sheet"]];
  const worlds = s.views.map(v => {
    const stem = v.f.replace(".png", "");
    const orig = `${base}${marked ? stem + "_marked.png" : v.f}`;
    const [w, h] = v.size;
    const news = v.new
      ? `<img src="${base}${v.new}?v=${v.t}" width="${w}" height="${h}" alt="">`
      : `<div class="empty" style="width:${w}px;height:${h}px">no paintings in this view yet</div>`;
    return `<section class="world"><h3>${esc(stem)}</h3><div class="pair">
      <figure><div class="stage"><img src="${orig}" width="${w}" height="${h}" alt=""></div><figcaption>Original</figcaption></figure>
      <figure><div class="stage">${news}</div><figcaption>With the paintings</figcaption></figure></div></section>`;
  }).join("");
  const cards = s.frames.map(f => {
    const ff = pad(f.f, 2);
    const painted = `${DATA.work}/${pad(s.shape, 4)}_${ff}.png?v=${f.t}`;
    const flag = f.s === "painted" ? "painted" : f.s === "wrong" ? `${f.size[0]}×${f.size[1]}, wants ${f.tw}×${f.th}` : "not painted";
    const orig = stage(`${base}${ff}.png`, f.tw, f.th, z, true, tiled);
    const w = (tiled ? 3 : 1) * f.tw * z, h = (tiled ? 3 : 1) * f.th * z;
    const news = f.s === "missing"
      ? `<div class="empty" style="width:${w}px;height:${h}px">not painted</div>`
      : stage(painted, f.tw, f.th, z, z > 1 && mode === "pixels", tiled);
    const panes = mode === "original" ? [["Original", orig]] : mode === "new" ? [["New", news]] : [["Original", orig], ["New", news]];
    return `<div class="card ${f.s}"><div class="hd"><b>${ff}</b>
      <span class="meta">${f.w}×${f.h} → ${f.tw}×${f.th}${f.v ? ` · ${esc(f.v)}.png` : ""}</span>
      <span class="flag ${f.s}">${esc(flag)}</span></div>
      <div class="pair">${panes.map(([c, x]) => `<figure>${x}<figcaption>${c}</figcaption></figure>`).join("")}</div></div>`;
  }).join("");
  const kind = DATA.kinds.find(k => k.key === s.kind);
  main.innerHTML = `<div class="top">
      <h2>${esc(s.name)}<span>${esc(kind.title)} · shape ${s.shape} · ${t.p} of ${t.n} frames painted${t.bad ? " · wrong sizes" : ""}</span></h2>
      <div class="row">
        <label>Zoom</label><select id="zoom">${["auto", "1", "2", "3", "4"].map(v => `<option ${v === zoom ? "selected" : ""}>${v}</option>`).join("")}</select>
        <label>Show</label><select id="mode">
          <option value="pair" ${mode === "pair" ? "selected" : ""}>Side by side</option>
          <option value="pixels" ${mode === "pixels" ? "selected" : ""}>Side by side, sharp pixels</option>
          ${terrain ? `<option value="tiled" ${mode === "tiled" ? "selected" : ""}>Tiled 3×3 (seams)</option>` : ""}
          <option value="original" ${mode === "original" ? "selected" : ""}>Originals only</option>
          <option value="new" ${mode === "new" ? "selected" : ""}>Paintings only</option></select>
        <label>Ground</label><select id="bg">${["checker", "dark", "grey", "light"].map(v => `<option ${v === bg ? "selected" : ""}>${v}</option>`).join("")}</select>
        ${s.views.length ? `<label><input type="checkbox" id="marked" ${marked ? "checked" : ""}> outline the set in the world</label>` : ""}
        <span class="count">${esc(DATA.pack)} pack · <kbd>j</kbd>/<kbd>k</kbd> next/previous set</span>
      </div></div>
    <div class="refs">${refs.map(([f, label]) => `<a href="${base}${f}" target="_blank"><img src="${base}${f}" alt="" loading="lazy">${label}</a>`).join("")}</div>
    ${worlds ? `<div class="worlds">${worlds}</div>` : ""}
    <div class="frames">${cards}</div>`;
  const mk = document.getElementById("marked");
  if (mk) mk.onchange = e => { marked = e.target.checked; store("marked", marked ? "1" : "0"); renderSet(); };
  for (const [id, set] of [["zoom", v => zoom = v], ["mode", v => mode = v], ["bg", v => { bg = v; document.body.className = "bg-" + v; }]]) {
    document.getElementById(id).onchange = e => { set(e.target.value); store(id, e.target.value); renderSet(); };
  }
  main.scrollTop = 0;
}

function select(key) {
  current = DATA.sets.find(s => `${s.kind}/${s.dir}` === key) || null;
  if (current && location.hash.slice(1) !== key) history.replaceState(null, "", "#" + key);
  renderList();
  renderSet();
  document.querySelector("#list .set.on")?.scrollIntoView({block: "nearest"});
}

document.getElementById("list").onclick = e => { const el = e.target.closest(".set"); if (el) select(el.dataset.key); };
document.getElementById("q").oninput = renderList;
document.getElementById("filter").onchange = e => { store("filter", e.target.value); renderList(); };
document.getElementById("filter").value = store("filter") || "all";
document.addEventListener("keydown", e => {
  if (e.target.matches("input, select") || !["j", "k"].includes(e.key)) return;
  const keys = [...document.querySelectorAll("#list .set")].map(el => el.dataset.key);
  if (!keys.length) return;
  const at = current ? keys.indexOf(`${current.kind}/${current.dir}`) : -1;
  select(keys[Math.max(0, Math.min(keys.length - 1, at + (e.key === "j" ? 1 : -1)))]);
});
window.onhashchange = () => select(decodeURIComponent(location.hash.slice(1)));
select(decodeURIComponent(location.hash.slice(1)));
</script>
</body>
</html>
"""


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--file", default="shapes", choices=["shapes", "sprites", "gumps", "paperdolls"])
    ap.add_argument("--pack", default="painted")
    args = ap.parse_args()
    print(build(args.file, args.pack), flush=True)


if __name__ == "__main__":
    main()
