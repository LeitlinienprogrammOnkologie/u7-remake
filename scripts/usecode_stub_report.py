"""Rank the Black Gate intrinsics the remake still stubs by how much usecode needs them.

Builds a static call graph from assets/usecode/usecode_disasm.txt (call targets
resolved through the extern lists in assets/data/usecode.csv, calle by id),
walks it from root functions and counts calli/callis sites per intrinsic.
Implemented intrinsics are read from the dispatch switch in BgIntrinsics.cs.

Usage: python scripts/usecode_stub_report.py [--npcs 1,2,11] [--out report.txt]
Default roots are the Trinsic NPCs (0x400 + npc number); a second section
covers every function in USECODE.
"""

import argparse
import csv
import re
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DISASM = ROOT / "assets" / "usecode" / "usecode_disasm.txt"
USECODE_CSV = ROOT / "assets" / "data" / "usecode.csv"
SCHEDULES = ROOT / "assets" / "data" / "schedules.csv"
INTRINSICS_CS = ROOT / "godot" / "scripts" / "Usecode" / "BgIntrinsics.cs"

# Trinsic: town, gate, Spark's house, Fellowship branch. Iolo (1) waits
# there at game start although his schedule slots are elsewhere.
TRINSIC_BOX = (940, 2080, 1180, 2320)
TRINSIC_EXTRA = [1]

FUNC_RE = re.compile(r"^Function 0x([0-9A-F]{4})")
INS_RE = re.compile(r"^[0-9A-F]{4}: (?:[0-9A-F]{2} )+\s+(\w+)\s*(.*)$")
INTRINSIC_ARG_RE = re.compile(r"0x([0-9A-F]+)@")


def load_names_and_implemented():
    src = INTRINSICS_CS.read_text(encoding="utf-8")
    block = re.search(r"Names =\s*\[(.*?)\];", src, re.S).group(1)
    names = re.findall(r'"([^"]+)"', block)
    implemented = {int(h, 16) for h in re.findall(r"^\s+0x([0-9a-f]+) =>", src, re.M)}
    return names, implemented


def load_externs():
    externs = {}
    with USECODE_CSV.open(newline="") as f:
        for row in csv.DictReader(f):
            ids = [int(x, 16) for x in row["externs"].split("|") if x]
            externs[int(row["func_id"])] = ids
    return externs


def parse_disasm(externs):
    calls = defaultdict(set)  # function -> called functions
    intrinsic_sites = defaultdict(lambda: defaultdict(int))  # function -> intrinsic -> sites
    cur = None
    with DISASM.open(encoding="utf-8", errors="replace") as f:
        for line in f:
            m = FUNC_RE.match(line)
            if m:
                cur = int(m.group(1), 16)
                continue
            m = INS_RE.match(line)
            if not m or cur is None:
                continue
            op, arg = m.group(1), m.group(2)
            if op in ("calli", "callis"):
                intrinsic_sites[cur][int(INTRINSIC_ARG_RE.search(arg).group(1), 16)] += 1
            elif op == "call":
                idx = int(arg.split()[0], 16)
                ext = externs.get(cur, [])
                if idx < len(ext):
                    calls[cur].add(ext[idx])
            elif op == "calle":
                calls[cur].add(int(arg.split()[0], 16))
    return calls, intrinsic_sites


def trinsic_npcs():
    npcs = set(TRINSIC_EXTRA)
    x0, y0, x1, y1 = TRINSIC_BOX
    with SCHEDULES.open(newline="") as f:
        for row in csv.DictReader(f):
            if x0 <= int(row["tx"]) <= x1 and y0 <= int(row["ty"]) <= y1:
                npcs.add(int(row["npc"]))
    return sorted(npcs)


def reachable(roots, calls):
    seen, stack = set(), list(roots)
    while stack:
        fn = stack.pop()
        if fn in seen:
            continue
        seen.add(fn)
        stack.extend(calls.get(fn, ()))
    return seen


def rank(funcs, intrinsic_sites, implemented):
    sites = defaultdict(int)
    users = defaultdict(set)
    for fn in funcs:
        for intr, n in intrinsic_sites.get(fn, {}).items():
            if intr not in implemented:
                sites[intr] += n
                users[intr].add(fn)
    return sorted(sites, key=lambda i: (-len(users[i]), -sites[i])), sites, users


def section(title, funcs, intrinsic_sites, implemented, names):
    order, sites, users = rank(funcs, intrinsic_sites, implemented)
    out = [f"== {title}: {len(funcs)} functions, {len(order)} stubbed intrinsics ==",
           f"{'id':>4}  {'name':<26}{'funcs':>6}{'sites':>7}  example callers"]
    for i in order:
        name = names[i] if i < len(names) else f"unknown_{i:02X}"
        ex = " ".join(f"0x{f:04X}" for f in sorted(users[i])[:6])
        out.append(f"0x{i:02X}  {name:<26}{len(users[i]):>6}{sites[i]:>7}  {ex}")
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--npcs", help="comma-separated NPC numbers to use as roots instead of Trinsic")
    ap.add_argument("--out", help="write the report here as well as stdout")
    args = ap.parse_args()

    names, implemented = load_names_and_implemented()
    externs = load_externs()
    calls, intrinsic_sites = parse_disasm(externs)

    npcs = [int(n) for n in args.npcs.split(",")] if args.npcs else trinsic_npcs()
    roots = [0x400 + n for n in npcs if 0x400 + n in externs]
    lines = [f"implemented intrinsics: {len(implemented)}",
             f"root NPCs: {', '.join(map(str, npcs))}", ""]
    lines += section("Reachable from root NPCs", reachable(roots, calls),
                     intrinsic_sites, implemented, names)
    lines.append("")
    lines += section("All of USECODE", set(externs), intrinsic_sites, implemented, names)

    text = "\n".join(lines)
    print(text)
    if args.out:
        Path(args.out).write_text(text + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
