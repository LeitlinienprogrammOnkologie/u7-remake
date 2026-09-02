"""Extract Autodesk FLIC animations from U7 ENDGAME.DAT (IFF FORM ENDG)."""

from __future__ import annotations

import struct
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "u7" / "STATIC" / "ENDGAME.DAT"
OUT = ROOT / "assets" / "intro_endgame" / "flics"

FLI_COLOR256 = 4
FLI_SS2 = 7
FLI_COLOR = 11
FLI_LC = 12
FLI_BLACK = 13
FLI_BRUN = 15
FLI_COPY = 16
FLI_PSTAMP = 18


def parse_iff(data: bytes) -> list[tuple[bytes, bytes]]:
    if data[:4] != b"FORM":
        raise ValueError("Not an IFF FORM file")
    chunks: list[tuple[bytes, bytes]] = []
    pos = 12  # skip FORM + size + type
    while pos + 8 <= len(data):
        cid = data[pos : pos + 4]
        size = int.from_bytes(data[pos + 4 : pos + 8], "big")
        start = pos + 8
        end = min(start + size, len(data))
        chunks.append((cid, data[start:end]))
        pos = start + size
        if size % 2:
            pos += 1
    return chunks


def scale6(rgb: bytes) -> list[int]:
    return [min(255, v * 4) for v in rgb]


def read_palette(buf: memoryview, offset: int, six_bit: bool) -> tuple[list[int], int]:
    packets = struct.unpack_from("<H", buf, offset)[0]
    offset += 2
    colors = [0, 0, 0] * 256
    index = 0
    for _ in range(packets):
        skip = buf[offset]
        change = buf[offset + 1]
        offset += 2
        index += skip
        if change == 0:
            change = 256
        nbytes = change * 3
        raw = bytes(buf[offset : offset + nbytes])
        offset += nbytes
        vals = scale6(raw) if six_bit else list(raw)
        for i, v in enumerate(vals):
            slot = index * 3 + i
            if slot < len(colors):
                colors[slot] = v
        index += change
    return colors, offset


def decode_flic(data: bytes, dest: Path) -> dict:
    mv = memoryview(data)
    offset = 0
    name = ""
    magic = struct.unpack_from("<H", mv, 4)[0]
    if magic not in (0xAF11, 0xAF12):
        name = bytes(mv[:8]).split(b"\x00", 1)[0].decode("latin-1", errors="replace")
        offset = 8
        magic = struct.unpack_from("<H", mv, offset + 4)[0]
    size, magic, frames, width, height, depth, flags, speed = struct.unpack_from(
        "<IHHHHHHH", mv, offset
    )
    offset += 128
    if magic not in (0xAF11, 0xAF12):
        raise ValueError(f"Not a FLIC (magic={magic:#x})")

    dest.mkdir(parents=True, exist_ok=True)
    pixels = bytearray(width * height)
    palette = [0, 0, 0] * 256
    images: list[Image.Image] = []
    delay_ms = speed * 10 if magic == 0xAF11 else speed
    if delay_ms <= 0:
        delay_ms = 70

    for frame_index in range(frames):
        if offset + 16 > len(mv):
            break
        frame_size, _frame_magic, nchunks = struct.unpack_from("<IHH", mv, offset)
        chunk_pos = offset + 16
        frame_end = offset + frame_size
        for _ in range(nchunks):
            if chunk_pos + 6 > len(mv):
                break
            _chunk_size, ctype = struct.unpack_from("<IH", mv, chunk_pos)
            pos = chunk_pos + 6
            if ctype == FLI_COLOR256:
                palette, pos = read_palette(mv, pos, six_bit=False)
            elif ctype == FLI_COLOR:
                palette, pos = read_palette(mv, pos, six_bit=True)
            elif ctype == FLI_BLACK:
                pixels[:] = b"\x00" * len(pixels)
            elif ctype == FLI_COPY:
                need = width * height
                pixels[:need] = mv[pos : pos + need]
            elif ctype == FLI_BRUN:
                pos = decode_brun(mv, pos, pixels, width, height)
            elif ctype == FLI_LC:
                pos = decode_lc(mv, pos, pixels, width)
            elif ctype == FLI_SS2:
                pos = decode_ss2(mv, pos, pixels, width)
            elif ctype == FLI_PSTAMP:
                pass
            chunk_pos += _chunk_size
        offset = frame_end
        img = Image.frombytes("P", (width, height), bytes(pixels))
        img.putpalette(palette)
        rgba = img.convert("RGBA")
        rgba.save(dest / f"frame_{frame_index:04d}.png")
        images.append(rgba)

    if images:
        images[0].save(
            dest / "animation.gif",
            save_all=True,
            append_images=images[1:],
            duration=delay_ms,
            loop=0,
            disposal=1,
        )
    return {
        "name": name,
        "magic": hex(magic),
        "frames": len(images),
        "width": width,
        "height": height,
        "speed": speed,
        "delay_ms": delay_ms,
    }


def decode_brun(mv: memoryview, pos: int, pixels: bytearray, width: int, height: int) -> int:
    for y in range(height):
        packets = mv[pos]
        pos += 1
        x = 0
        for _ in range(packets):
            count = struct.unpack_from("b", mv, pos)[0]
            pos += 1
            if count > 0:
                val = mv[pos]
                pos += 1
                for _i in range(count):
                    if x < width:
                        pixels[y * width + x] = val
                    x += 1
            else:
                n = -count
                for i in range(n):
                    if x < width:
                        pixels[y * width + x] = mv[pos + i]
                    x += 1
                pos += n
    return pos


def decode_lc(mv: memoryview, pos: int, pixels: bytearray, width: int) -> int:
    skip_lines, change_lines = struct.unpack_from("<HH", mv, pos)
    pos += 4
    for line in range(change_lines):
        packets = mv[pos]
        pos += 1
        x = 0
        y = skip_lines + line
        for _ in range(packets):
            x += mv[pos]
            count = struct.unpack_from("b", mv, pos + 1)[0]
            pos += 2
            if count < 0:
                val = mv[pos]
                pos += 1
                n = -count
                for i in range(n):
                    pixels[y * width + x + i] = val
                x += n
            else:
                for i in range(count):
                    pixels[y * width + x + i] = mv[pos + i]
                x += count
                pos += count
    return pos


def decode_ss2(mv: memoryview, pos: int, pixels: bytearray, width: int) -> int:
    change_lines = struct.unpack_from("<H", mv, pos)[0]
    pos += 2
    line = 0
    for _ in range(change_lines):
        packets = struct.unpack_from("<h", mv, pos)[0]
        pos += 2
        while packets & 0x8000:
            if packets & 0x4000:
                line += abs(packets)
            else:
                pixels[line * width + width - 1] = packets & 0xFF
            packets = struct.unpack_from("<h", mv, pos)[0]
            pos += 2
        x = 0
        for _p in range(packets):
            x += mv[pos]
            count = struct.unpack_from("b", mv, pos + 1)[0]
            pos += 2
            if count < 0:
                word = struct.unpack_from("<H", mv, pos)[0]
                pos += 2
                n = -count
                lo, hi = word & 0xFF, (word >> 8) & 0xFF
                for i in range(n):
                    pixels[line * width + x] = lo
                    pixels[line * width + x + 1] = hi
                    x += 2
            else:
                nbytes = 2 * count
                pixels[line * width + x : line * width + x + nbytes] = mv[pos : pos + nbytes]
                x += nbytes
                pos += nbytes
        line += 1
    return pos


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    data = SRC.read_bytes()
    chunks = parse_iff(data)
    summary = []
    flic_index = 0
    for cid, payload in chunks:
        if cid not in {b"FLIC", b"flic"} and not payload[:4].lower().startswith(b"flic"):
            (OUT / f"{cid.decode('latin-1', errors='replace')}_{flic_index}.bin").write_bytes(payload)
            continue
        name_guess = payload[:8].split(b"\x00", 1)[0].decode("latin-1", errors="replace") or f"flic{flic_index}"
        dest = OUT / f"{flic_index:02d}_{name_guess}"
        info = decode_flic(payload, dest)
        (dest / "flic.bin").write_bytes(payload)
        summary.append(info)
        print(f"Extracted {dest.name}: {info['frames']} frames {info['width']}x{info['height']}")
        flic_index += 1
    (OUT / "summary.json").write_text(
        __import__("json").dumps(summary, indent=2), encoding="utf-8"
    )


if __name__ == "__main__":
    main()
