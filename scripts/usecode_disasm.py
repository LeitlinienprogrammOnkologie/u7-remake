"""Disassemble functions of a game's original USECODE (Black Gate or Serpent Isle).

    python scripts/usecode_disasm.py [--game si] 0x403 [0x6cb ...]

Reads u7/STATIC/USECODE (or serpent/STATIC/USECODE with --game si) and prints
each function's code with its strings, intrinsic names (the game's table, from
godot/scripts/Usecode) and called functions resolved. The opcode sizes are
Exult's (tools/uctools.h, opcode_table) for the originals' opcodes.
"""
import argparse
import os
import re
import struct
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# opcode: (mnemonic, operand bytes, kind) per Exult's opcode_table.
OPS = {
    0x02: ("loop", 10, "loop"), 0x04: ("startconv", 2, "rel"), 0x05: ("jne", 2, "rel"), 0x06: ("jmp", 2, "rel"),
    0x07: ("cmps", 4, "argrel"), 0x09: ("add", 0, ""), 0x0a: ("sub", 0, ""), 0x0b: ("div", 0, ""),
    0x0c: ("mul", 0, ""), 0x0d: ("mod", 0, ""), 0x0e: ("and", 0, ""), 0x0f: ("or", 0, ""), 0x10: ("not", 0, ""),
    0x12: ("pop", 2, "var"), 0x13: ("push true", 0, ""), 0x14: ("push false", 0, ""), 0x16: ("cmpgt", 0, ""),
    0x17: ("cmplt", 0, ""), 0x18: ("cmpge", 0, ""), 0x19: ("cmple", 0, ""), 0x1a: ("cmpne", 0, ""),
    0x1c: ("addsi", 2, "str"), 0x1d: ("pushs", 2, "str"), 0x1e: ("arrc", 2, "imm"), 0x1f: ("pushi", 2, "imm"),
    0x21: ("push", 2, "var"), 0x22: ("cmpeq", 0, ""), 0x24: ("call", 2, "extern"), 0x25: ("ret", 0, ""),
    0x26: ("aidx", 2, "var"), 0x2c: ("ret2", 0, ""), 0x2d: ("retv", 0, ""), 0x2e: ("initloop", 0, ""),
    0x2f: ("addsv", 2, "var"), 0x30: ("in", 0, ""), 0x31: ("conv_something", 4, "immrel"), 0x32: ("retz", 0, ""),
    0x33: ("say", 0, ""), 0x38: ("callis", 3, "intr"), 0x39: ("calli", 3, "intr"), 0x3e: ("push itemref", 0, ""),
    0x3f: ("abrt", 0, ""), 0x40: ("endconv", 0, ""), 0x42: ("pushf", 2, "imm"), 0x43: ("popf", 2, "imm"),
    0x44: ("pushb", 1, "byte"), 0x46: ("setarrayelem", 2, "imm"), 0x47: ("calle", 2, "imm"),
    0x48: ("push eventid", 0, ""), 0x4a: ("arra", 0, ""), 0x4b: ("pop eventid", 0, ""), 0x4c: ("dbgline", 2, "imm"),
    0x4d: ("dbgfunc", 4, "imm"),
}


def intrinsic_names(game):
    path = os.path.join(ROOT, "godot", "scripts", "Usecode", "SiIntrinsics.cs" if game == "si" else "BgIntrinsics.cs")
    text = open(path, encoding="utf-8").read()
    start = text.index("static readonly string[] Names")
    return re.findall(r'"(\w+)"', text[start:text.index("];", start)])


def functions(game):
    path = os.path.join(ROOT, "serpent" if game == "si" else "u7", "STATIC", "USECODE")
    data = open(path, "rb").read()
    i = 0
    while i < len(data):
        fid, size = struct.unpack_from("<HH", data, i)
        yield fid, data[i + 4:i + 4 + size]
        i += 4 + size


def disasm(body, names):
    dsize = struct.unpack_from("<H", body, 0)[0]
    strings = body[2:2 + dsize]
    c = 2 + dsize
    nargs, nlocals, nextern = struct.unpack_from("<HHH", body, c)
    c += 6
    externs = list(struct.unpack_from(f"<{nextern}H", body, c))
    c += 2 * nextern
    code = body[c:]
    out = [f"; args {nargs}, locals {nlocals}, externs {[hex(e) for e in externs]}"]

    def string(off):
        end = strings.find(b"\0", off)
        return strings[off:end].decode("latin1")

    ip = 0
    while ip < len(code):
        op = code[ip]
        name, n, kind = OPS.get(op, (f"?{op:02x}", 0, ""))
        arg = code[ip + 1:ip + 1 + n]
        text = name
        if kind in ("imm", "var"):
            v = struct.unpack_from("<h", arg)[0]
            text += f" {v}" if kind == "var" else f" 0x{v & 0xffff:04x}"
        elif kind == "byte":
            text += f" 0x{arg[0]:02x}"
        elif kind == "str":
            text += f' "{string(struct.unpack_from("<H", arg)[0])}"'
        elif kind == "rel":
            text += f" -> {ip + 1 + n + struct.unpack_from('<h', arg)[0]:04x}"
        elif kind == "argrel":
            cnt, rel = struct.unpack_from("<hh", arg)
            text += f" {cnt} -> {ip + 1 + n + rel:04x}"
        elif kind == "immrel":
            v, rel = struct.unpack_from("<hh", arg)
            text += f" 0x{v & 0xffff:04x} -> {ip + 1 + n + rel:04x}"
        elif kind == "extern":
            idx = struct.unpack_from("<H", arg)[0]
            text += f" [{idx}] 0x{externs[idx]:04x}" if idx < len(externs) else f" [{idx}]"
        elif kind == "intr":
            num, argc = struct.unpack_from("<HB", arg)
            text += f" {names[num] if num < len(names) else '?'}(0x{num:02x})@{argc}"
        elif kind == "loop":
            text += " " + " ".join(str(v) for v in struct.unpack_from("<hhhhh", arg))
        out.append(f"{ip:04x}: {text}")
        ip += 1 + n
    return "\n".join(out)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--game", choices=["bg", "si"], default="bg")
    p.add_argument("funcs", nargs="+")
    args = p.parse_args()
    wanted = {int(f, 0) for f in args.funcs}
    names = intrinsic_names(args.game)
    for fid, body in functions(args.game):
        if fid in wanted:
            print(f"Function 0x{fid:04x}")
            print(disasm(body, names))
            print()
    sys.stdout.flush()


if __name__ == "__main__":
    main()
