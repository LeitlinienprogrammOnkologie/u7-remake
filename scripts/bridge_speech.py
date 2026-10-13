#!/usr/bin/env python3
"""The bridge mod's voices: Serpent Isle's intro voices for the Guardian's
scroll scene (docs/BRIDGE.md, D2), made locally from serpent/STATIC/INTRO.DAT
into mods/eighteen/data/u7speech.spc (gitignored, Origin's recordings).

INTRO.DAT is an IFF file (Exult IFF): FORM, size, type, then chunks of a
4-byte type, a big-endian size and data; a chunk's data begins with an
8-byte name, then the VOC (Exult sigame.cc plays buffer + 8). Exult reads a
mod's u7speech.spc over STATIC/U7SPEECH.SPC entry by entry (PATCH_U7SPEECH,
U7multiobject), so the voices go to entries past Black Gate's 34.

    python scripts/bridge_speech.py
"""

from __future__ import annotations

import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
INTRO = ROOT / "serpent" / "STATIC" / "INTRO.DAT"
OUT = ROOT / "mods" / "eighteen" / "data" / "u7speech.spc"

# Serpent Isle intro voice -> entry: "My liege", "All we found...", "Indeed...",
# "Stand back!", the Guardian's scroll, "'Tis my worst fear!" (Exult play_intro).
FIRST = 40
VOICES = [16, 17, 18, 19, 20, 21]


def iff_chunks(data: bytes) -> list[bytes]:
    """Exult IFF::index_file: the chunks' data after FORM, size and type."""
    assert data[:4] == b"FORM", "not an IFF file"
    end = 8 + struct.unpack_from(">I", data, 4)[0]
    pos = 12
    chunks = []
    while pos + 8 <= min(end, len(data)):
        size = struct.unpack_from(">I", data, pos + 4)[0]
        chunks.append(data[pos + 8:pos + 8 + size])
        pos += 8 + size + (size & 1)
    return chunks


def write_flex(path: Path, entries: list[bytes]) -> None:
    """An Exult flex: an 80-byte title, magic, count, then (offset, length) pairs."""
    header = bytearray(0x80)
    header[:len(b"Eighteen Months voices")] = b"Eighteen Months voices"
    struct.pack_into("<II", header, 0x50, 0xFFFF1A00, len(entries))
    table = bytearray()
    body = bytearray()
    offset = 0x80 + 8 * len(entries)
    for entry in entries:
        table += struct.pack("<II", offset + len(body) if entry else 0, len(entry))
        body += entry
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(bytes(header) + bytes(table) + bytes(body))


def main() -> int:
    if not INTRO.exists():
        print(f"no {INTRO}: Serpent Isle is not installed; the scene plays without voices")
        return 1
    chunks = iff_chunks(INTRO.read_bytes())
    entries: list[bytes] = [b""] * (FIRST + len(VOICES))
    for i, voice in enumerate(VOICES):
        voc = chunks[voice][8:]
        assert voc[:19] == b"Creative Voice File", f"chunk {voice} is no VOC"
        entries[FIRST + i] = voc
    write_flex(OUT, entries)
    print(f"wrote {OUT} (entries {FIRST}-{FIRST + len(VOICES) - 1})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
