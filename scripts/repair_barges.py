"""Add the barges of INITGAME.DAT, with the objects that follow them, to a save made before barges were read.

usage: python scripts/repair_barges.py <slot> [<slot> ...]   (saves/<slot>/)
Each barge entry and the entries after it up to the end of their list are appended to the
save's U7IREGxx, ended by 01 (read back as a barge whose parts go into the world).
A superchunk that already has a barge is left alone.
"""
import os, struct, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def barge_blocks():
    d = open(os.path.join(ROOT, "u7", "STATIC", "INITGAME.DAT"), "rb").read()
    n = struct.unpack_from("<I", d, 84)[0]
    blocks = {}
    for k in range(n):
        off, ln = struct.unpack_from("<II", d, 128 + 8 * k)
        e = d[off:off + ln]
        name = e[:13].split(b"\0")[0].decode("latin-1").upper()
        if "IREG" not in name:
            continue
        data = e[13:]
        for i in range(len(data) - 13):
            if data[i] == 12 and data[i + 3] == 0xC1 and (data[i + 4] & 3) == 3:
                j = i + 13
                depth = 0
                while j < len(data):
                    L = data[j]
                    if L in (0, 1):
                        if depth == 0:
                            break
                        depth -= 1
                        j += 1
                        continue
                    j += 1
                    if L == 2:
                        j += 2
                        continue
                    ext = L in (253, 254)
                    if ext:
                        L = data[j]
                        j += 1
                    if L == 255:
                        kind = data[j]
                        j += 1
                        if kind == 1:
                            j += 2 + struct.unpack_from("<H", data, j)[0]
                        continue
                    ent = data[j:j + L]
                    j += L
                    b = 1 if L == 13 and ext else 0
                    if (L - b) in (12, 13) and (ent[4 + b] or ent[5 + b]):
                        depth += 1
                blocks.setdefault(name[-2:], []).append(data[i:j] + b"\x01")
    return blocks


def has_barge(data):
    return any(data[i] == 12 and data[i + 3] == 0xC1 and (data[i + 4] & 3) == 3 for i in range(len(data) - 5))


blocks = barge_blocks()
for slot in sys.argv[1:]:
    for sc, items in blocks.items():
        path = os.path.join(ROOT, "saves", slot, "U7IREG" + sc)
        data = open(path, "rb").read()
        if has_barge(data):
            print(slot, sc, "already has a barge")
            continue
        open(path, "wb").write(data + b"".join(items))
        print(slot, sc, "added", len(items), "barge(s),", sum(len(x) for x in items), "bytes")
