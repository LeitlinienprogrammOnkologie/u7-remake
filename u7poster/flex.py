"""Flex archive reader.

Mirrors exult/files/Flex.h (struct Flex_header: 80-byte title, magic1
0xffff1a00, count, magic2, 9 padding dwords = 128 bytes) and
Flex::index_file() (8-byte offset/size table starting at byte 128).
INITGAME.DAT entries carry a 13-byte DOS filename before the payload, as
read by Game_window::restore_flex_files() in gamedat.cc.
"""
from __future__ import annotations
import struct
from pathlib import Path

FLEX_MAGIC1 = 0xFFFF1A00
EXULT_FLEX_MAGIC2 = 0x0000CC00
HEADER_LEN = 128
TITLE_LEN = 80


class Flex:
    def __init__(self, data: bytes, name: str = "?"):
        self.data = data
        self.name = name
        if len(data) < HEADER_LEN:
            raise ValueError(f"{name}: too short for a flex header")
        self.title = data[:TITLE_LEN].split(b"\0")[0].decode("latin1")
        self.magic1, self.count, self.magic2 = struct.unpack_from("<III", data, TITLE_LEN)
        if self.magic1 != FLEX_MAGIC1:
            raise ValueError(f"{name}: bad flex magic1 {self.magic1:#x}")
        # Flex_header::get_vers(): exult_v2 iff magic2 & ~0xff == 0xcc00
        self.version = (self.magic2 & 0xFF) if (self.magic2 & ~0xFF) == EXULT_FLEX_MAGIC2 else 0
        self.entries = [struct.unpack_from("<II", data, HEADER_LEN + 8 * i) for i in range(self.count)]

    @classmethod
    def open(cls, path: Path) -> "Flex":
        return cls(Path(path).read_bytes(), Path(path).name)

    def __len__(self):
        return self.count

    def get(self, i: int) -> bytes:
        off, size = self.entries[i]
        if size == 0:
            return b""
        return self.data[off:off + size]

    def __iter__(self):
        for i in range(self.count):
            yield self.get(i)

    def named_entries(self) -> dict[str, bytes]:
        """restore_flex_files(): entries longer than 13 bytes start with a
        13-byte DOS filename; a trailing dot is stripped."""
        out = {}
        for i in range(self.count):
            off, size = self.entries[i]
            if size <= 13:
                continue
            raw = self.data[off:off + 13]
            fname = raw.split(b"\0")[0].decode("latin1")
            if fname.endswith("."):
                fname = fname[:-1]
            out[fname.lower()] = self.data[off + 13:off + size]
        return out
