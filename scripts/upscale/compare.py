"""Shots of the same scenes in each art, side by side, for the user's pick.

    U7_AGENT=<io of a windowed game> scripts/upscale/.venv/Scripts/python scripts/upscale/compare.py [art ...]

A windowed game must be running with the agent console on that folder (a
headless one takes no shots). For each scene the script loads its save and
sets it up, then shoots it in every art (the sharp fill on); the sheet
agent_io/compare/<scene>.png lays the same crop of each shot side by side
at the screen's own pixels.
"""

from __future__ import annotations

import os
import re
import subprocess
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
AGENT = ROOT / "scripts" / "agent" / "agent.py"
OUT = ROOT / "agent_io" / "compare"
FONT = ROOT / "godot" / "fonts" / "MedievalSharp.ttf"

# Scene: save, console commands to set it up, and the crop: world pixels
# round the screen's centre (w, h), or a box in fractions of the screen.
SCENES = {
    "trinsic": ("quest4", ["setflag npc:0 invisible 0", "tp 1068 2229", "hour 11"], (100, 64)),
    "throne": ("britain1", ["hour 22"], (100, 64)),
    "cove": ("cove0", ["hour 13"], (100, 64)),
    "yew": ("quest4", ["setflag npc:0 invisible 0", "tp 340 1650", "hour 15"], (100, 64)),
    "talk": ("britain1", ["hour 12", "talk 1"], (0.03, 0.66, 0.36, 0.98)),
    "backpack": ("britain1", ["hour 12", "use npc:0", "@backpack"], (0.2, 0.15, 0.8, 0.85)),
}


def agent(*cmds: str) -> str:
    return subprocess.run([sys.executable, str(AGENT), *cmds], capture_output=True, text=True).stdout


def setup(save: str, cmds: list[str]) -> None:
    agent(f"load {save}", "art sharp on")
    for cmd in cmds:
        if cmd == "@backpack":
            # Open the avatar's backpack: its id from the inventory listing.
            found = re.search(r"#(\d+) backpack", agent("inv"))
            if found:
                agent(f"use {found.group(1)}")
        else:
            agent(cmd)
    agent("wait 1")


def shoot(scene: str, arts: list[str]) -> list[Path]:
    save, cmds, _ = SCENES[scene]
    setup(save, cmds)
    io = Path(os.environ["U7_AGENT"])
    shots = []
    for art in arts:
        name = f"cmp_{scene}_{art}"
        agent(f"art pack {art}", "art", f"shot {name}")
        shots.append(io / f"{name}.png")
    return shots


def crop(im: Image.Image, spec: tuple) -> Image.Image:
    if isinstance(spec[0], float):
        x0, y0, x1, y1 = spec
        return im.crop((int(x0 * im.width), int(y0 * im.height), int(x1 * im.width), int(y1 * im.height)))
    # Screen pixels a world pixel covers: zoom 4 at the 1280x800 base the window stretches.
    scale = 4 * min(im.width / 1280, im.height / 800)
    w, h = int(spec[0] * scale), int(spec[1] * scale)
    cx, cy = im.width // 2, im.height // 2
    return im.crop((cx - w // 2, cy - h // 2, cx + w // 2, cy + h // 2))


def sheet(scene: str, arts: list[str], shots: list[Path]) -> Path:
    font = ImageFont.truetype(str(FONT), 30)
    crops = [crop(Image.open(s).convert("RGB"), SCENES[scene][2]) for s in shots]
    cols = 3 if len(crops) > 4 else 2
    w, h = crops[0].size
    rows = (len(crops) + cols - 1) // cols
    out = Image.new("RGB", (cols * (w + 8), rows * (h + 44)), (24, 18, 12))
    draw = ImageDraw.Draw(out)
    for i, (art, c) in enumerate(zip(arts, crops)):
        x, y = (i % cols) * (w + 8), (i // cols) * (h + 44)
        draw.text((x + 6, y + 4), art, fill=(222, 184, 92), font=font)
        out.paste(c, (x, y + 40))
    OUT.mkdir(parents=True, exist_ok=True)
    path = OUT / f"{scene}.png"
    out.save(path)
    return path


def main() -> None:
    arts = sys.argv[1:] or ["original", "nxbrz", "realesrgan-anime", "rebout", "lollypop", "realesrgan"]
    scenes = [a[1:] for a in arts if a.startswith("+")]
    arts = [a for a in arts if not a.startswith("+")]
    for scene in scenes or SCENES:
        print(sheet(scene, arts, shoot(scene, arts)), flush=True)


if __name__ == "__main__":
    main()
