"""Extract Ultima 7: The Black Gate assets from the u7 install directory."""

from __future__ import annotations

import json
import os
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
STATIC = ROOT / "u7" / "STATIC"
GAMEDAT = ROOT / "u7" / "GAMEDAT"
ASSETS = ROOT / "assets"
PAL = STATIC / "PALETTES.FLX"


def run(cmd: list[str], cwd: Path | None = None) -> int:
    print(f"\n>>> {' '.join(cmd)}", flush=True)
    result = subprocess.run(cmd, cwd=cwd or ROOT)
    if result.returncode != 0:
        print(f"WARNING: command failed ({result.returncode}): {' '.join(cmd)}", flush=True)
    return result.returncode


def mkdir(path: Path) -> Path:
    path.mkdir(parents=True, exist_ok=True)
    return path


def dump_text_flx(src_dir: Path, dest: Path) -> None:
    records = []
    for path in sorted(src_dir.glob("*")):
        if path.suffix.lower() in {".txt"} and path.name.startswith("_"):
            continue
        if path.is_dir():
            continue
        raw = path.read_bytes()
        text = raw.split(b"\x00", 1)[0].decode("latin-1", errors="replace").strip()
        if not text:
            continue
        stem = path.stem
        try:
            index = int(stem.split("_", 1)[0])
        except ValueError:
            index = stem
        records.append({"index": index, "file": path.name, "text": text})
    dest.write_text(json.dumps(records, indent=2, ensure_ascii=False), encoding="utf-8")
    lines = [f"{r['index']}\t{r['text']}" for r in records]
    dest.with_suffix(".txt").write_text("\n".join(lines) + "\n", encoding="utf-8")


def main() -> int:
    mkdir(ASSETS)
    graphics = mkdir(ASSETS / "graphics")
    audio = mkdir(ASSETS / "audio")
    data = mkdir(ASSETS / "data")
    maps = mkdir(ASSETS / "maps")
    text_dir = mkdir(ASSETS / "text")
    usecode_dir = mkdir(ASSETS / "usecode")
    flex_dir = mkdir(ASSETS / "flex")
    intro = mkdir(ASSETS / "intro_endgame")

    # Palettes
    run(["titan", "u7", "palette-export", str(PAL), "-o", str(graphics / "palettes")])
    run(
        [
            "titan",
            "u7",
            "palette-info",
            str(PAL),
            "-f",
            "json",
            "-o",
            str(graphics / "palettes" / "palette_info.json"),
        ]
    )
    run(
        [
            "titan",
            "u7",
            "palette-export",
            str(STATIC / "INTROPAL.DAT"),
            "-o",
            str(intro / "palettes"),
        ]
    )

    # Graphics archives
    shape_jobs = [
        (STATIC / "SHAPES.VGA", graphics / "shapes"),
        (STATIC / "FACES.VGA", graphics / "faces"),
        (STATIC / "GUMPS.VGA", graphics / "gumps"),
        (STATIC / "SPRITES.VGA", graphics / "sprites"),
        (STATIC / "FONTS.VGA", graphics / "fonts"),
        (STATIC / "MAINSHP.FLX", graphics / "main_menu"),
        (STATIC / "ENDSHAPE.FLX", intro / "end_shapes"),
    ]
    for archive, out in shape_jobs:
        run(
            [
                "titan",
                "u7",
                "shape-batch",
                str(archive),
                "-p",
                str(PAL),
                "-o",
                str(out),
            ]
        )
    run(
        [
            "titan",
            "u7",
            "shape-export",
            str(STATIC / "POINTERS.SHP"),
            "-p",
            str(PAL),
            "-o",
            str(graphics / "pointers"),
        ]
    )
    run(
        [
            "titan",
            "u7",
            "shape-frame-report",
            str(STATIC / "SHAPES.VGA"),
            "-f",
            "csv",
            "-o",
            str(data / "shape_frames.csv"),
        ]
    )
    run(
        [
            "titan",
            "u7",
            "shape-cycle-scan",
            str(STATIC / "SHAPES.VGA"),
            "--static",
            str(STATIC),
            "-p",
            str(PAL),
            "-f",
            "json",
            "-o",
            str(graphics / "shape_cycle_scan"),
        ]
    )

    # Audio
    run(
        [
            "titan",
            "u7",
            "music-export",
            str(STATIC / "ADLIBMUS.DAT"),
            "-o",
            str(audio / "music_adlib"),
        ]
    )
    run(
        [
            "titan",
            "u7",
            "music-export",
            str(STATIC / "MT32MUS.DAT"),
            "-o",
            str(audio / "music_mt32"),
        ]
    )
    run(
        [
            "titan",
            "u7",
            "music-export",
            str(STATIC / "MT32MUS.DAT"),
            "--target",
            "gm",
            "-o",
            str(audio / "music_gm"),
        ]
    )
    run(
        [
            "titan",
            "u7",
            "music-export",
            str(STATIC / "ENDSCORE.XMI"),
            "-o",
            str(audio / "endscore"),
        ]
    )
    run(
        [
            "titan",
            "u7",
            "music-export",
            str(STATIC / "INTROADM.DAT"),
            "-o",
            str(intro / "music_adlib"),
        ]
    )
    run(
        [
            "titan",
            "u7",
            "music-export",
            str(STATIC / "INTRORDM.DAT"),
            "-o",
            str(intro / "music_mt32"),
        ]
    )
    run(
        [
            "titan",
            "u7",
            "speech-export",
            str(STATIC / "U7SPEECH.SPC"),
            "-o",
            str(audio / "speech"),
        ]
    )
    run(
        [
            "titan",
            "u7",
            "voc-export",
            str(STATIC / "INTROSND.DAT"),
            "-o",
            str(intro / "speech"),
        ]
    )

    # SFX flex dumps (AdLib/MT-32 patches, not VOC)
    run(["titan", "flex-extract", str(STATIC / "ADLIBSFX.DAT"), "-o", str(audio / "sfx_adlib_raw")])
    run(["titan", "flex-extract", str(STATIC / "MT32SFX.DAT"), "-o", str(audio / "sfx_mt32_raw")])
    run(["titan", "u7", "music-export", str(STATIC / "ADLIBSFX.DAT"), "-o", str(audio / "sfx_adlib")])
    run(["titan", "u7", "music-export", str(STATIC / "MT32SFX.DAT"), "-o", str(audio / "sfx_mt32")])

    # Flex archives
    run(["titan", "flex-extract", str(STATIC / "TEXT.FLX"), "-o", str(text_dir / "text_flx")])
    dump_text_flx(text_dir / "text_flx", text_dir / "shape_names.json")
    run(["titan", "flex-extract", str(STATIC / "U7VOICE.FLX"), "-o", str(audio / "voice_flx")])
    run(["titan", "flex-extract", str(STATIC / "INITGAME.DAT"), "-o", str(flex_dir / "initgame")])
    run(["titan", "flex-extract", str(STATIC / "MAINSHP.FLX"), "-o", str(flex_dir / "mainshp")])
    run(["titan", "flex-extract", str(STATIC / "ENDSHAPE.FLX"), "-o", str(flex_dir / "endshape")])

    # Static metadata
    run(
        [
            "titan",
            "u7",
            "typeflag-dump",
            str(STATIC),
            "-f",
            "csv",
            "-o",
            str(data / "typeflags.csv"),
        ]
    )
    run(
        [
            "titan",
            "u7",
            "typeflag-dump",
            str(STATIC),
            "-f",
            "detail",
            "-o",
            str(data / "typeflags_detail.txt"),
        ]
    )
    run(
        [
            "titan",
            "u7",
            "wihh-dump",
            str(STATIC),
            "-f",
            "csv",
            "--include-empty",
            "-o",
            str(data / "wihh.csv"),
        ]
    )
    for kind in ("weapons", "ammo", "armor", "container", "xforms", "blends", "usecode"):
        run(
            [
                "titan",
                "u7",
                "static-data-dump",
                kind,
                str(STATIC),
                "-o",
                str(data / f"{kind}.csv"),
            ]
        )
    run(
        [
            "titan",
            "u7",
            "monster-defs",
            str(STATIC / "MONSTERS.DAT"),
            "-f",
            "csv",
            "-o",
            str(data / "monsters.csv"),
        ]
    )
    run(
        [
            "titan",
            "u7",
            "gamedat-info",
            str(GAMEDAT),
            "--static",
            str(STATIC),
            "-f",
            "detail",
            "-o",
            str(data / "gamedat_info.txt"),
        ]
    )
    run(
        [
            "titan",
            "u7",
            "gflag-dump",
            str(GAMEDAT / "FLAGINIT"),
            "-f",
            "csv",
            "-o",
            str(data / "global_flags.csv"),
        ]
    )
    run(
        [
            "titan",
            "u7",
            "schedule-dump",
            str(STATIC / "SCHEDULE.DAT"),
            "-f",
            "csv",
            "-o",
            str(data / "schedules.csv"),
        ]
    )

    # Usecode
    run(
        [
            "titan",
            "u7",
            "usecode-disasm",
            str(STATIC / "USECODE"),
            "--all",
            "-o",
            str(usecode_dir / "usecode_disasm.txt"),
        ]
    )

    # Maps
    run(
        [
            "titan",
            "u7",
            "map-export-json",
            str(STATIC),
            "--pretty",
            "-o",
            str(maps / "u7_map.json"),
        ]
    )
    run(
        [
            "titan",
            "u7",
            "map-sample",
            str(STATIC),
            "-p",
            str(PAL),
            "--scale",
            "1",
            "--grid",
            "-o",
            str(maps / "minimap_full.png"),
        ]
    )
    run(
        [
            "titan",
            "u7",
            "map-sample",
            str(STATIC),
            "-p",
            str(PAL),
            "--scale",
            "4",
            "--grid",
            "-o",
            str(maps / "minimap.png"),
        ]
    )
    run(
        [
            "titan",
            "u7",
            "map-render",
            str(STATIC),
            "-p",
            str(PAL),
            "--full",
            "--view",
            "classic",
            "-o",
            str(maps / "world_classic.png"),
        ]
    )

    run(["python", str(ROOT / "scripts" / "extract_endgame_flics.py")])

    print("\nExtraction complete.", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
