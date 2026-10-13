"""What redrawn art is still missing, and making it.

    python scripts/upscale/missing.py [--model nxbrz]
    scripts/upscale/.venv/Scripts/python scripts/upscale/missing.py --model nxbrz --make [--file paperdolls]

Lists, for each file the game reads (generate.FILES, as u7art.game_file
builds them: Black Gate's, Serpent Isle's imports and paperdolls, Exult's own
art), the frames the model hasn't drawn yet and whether the pack lags behind
what was drawn. With --make it draws the missing frames (generate.run, which
skips what is done) and rebuilds the packs that changed (pack.build), so a
new source of art (Serpent Isle installed, a mod's) is caught up with in one
go. The game picks a rebuilt pack up at its next start; the paperdolls show
redrawn only when switched on (F7, or the console's `art paperdolls on`).
"""

from __future__ import annotations

import argparse
import struct
from pathlib import Path

import u7art
from generate import FILES, work_dir
from pack import VERSION as PACK_VERSION


def pack_count(model: str, file: str) -> tuple[int, int]:
    """Frames in the built pack and its format's version (its header's), (0, 0) when there is none."""
    path = u7art.HIRES / model / f"{file}.hrp"
    if not path.exists():
        return 0, 0
    with path.open("rb") as f:
        head = f.read(16)
    return struct.unpack_from("<II", head, 12)[::-1] if head[:4] == b"U7HR" else (0, 0)


def missing_frames(model: str, file: str) -> tuple[int, int] | None:
    """(frames drawn, frames not drawn yet) for the file, None when it isn't there."""
    vga = u7art.game_file(file)
    if vga is None:
        return None
    out = work_dir(model, file)
    drawn = 0
    missing = 0
    for shape in range(len(vga.records)):
        for f in range(vga.frame_count(shape)):
            if (out / f"{shape:04d}_{f:02d}.png").exists():
                drawn += 1
                continue
            # Only decoded when not drawn: empty frames are never drawn, so don't count them.
            frame = vga.frame(shape, f)
            if frame is not None and frame.opaque.any():
                missing += 1
    return drawn, missing


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", default="nxbrz")
    ap.add_argument("--file", choices=sorted(FILES), help="one file; all by default")
    ap.add_argument("--make", action="store_true", help="draw what is missing and rebuild the packs")
    args = ap.parse_args()
    files = [args.file] if args.file else list(FILES)
    for file in files:
        counts = missing_frames(args.model, file)
        if counts is None:
            print(f"{file:10} not there (Serpent Isle isn't installed)")
            continue
        drawn, missing = counts
        packed, version = pack_count(args.model, file)
        # A pack of an older format (version 1: no true colours) is made again.
        old = 0 < version < PACK_VERSION
        stale = drawn > packed or old
        note = " (old format)" if old else " (behind)" if stale else ""
        print(f"{file:10} {drawn} drawn, {missing} missing, pack {packed}{note}", flush=True)
        if not args.make or (missing == 0 and not stale):
            continue
        if missing:
            import generate
            generate.run(args.model, file, None, None)
        import pack
        pack.build(args.model, file)


if __name__ == "__main__":
    main()
