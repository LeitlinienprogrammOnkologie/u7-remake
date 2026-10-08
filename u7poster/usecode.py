"""USECODE string extraction (Black Gate 16-bit format).

References (Exult 1.12.1 usecode/ucfunction.cc Usecode_function ctor):
  each function: u16 id, u16 len (0xffff => u16 id + u32 len), then `len`
  bytes: u16 data_size, data (NUL-terminated strings), code.
  Opcodes used below (usecode/opcodes.h / ucinternal.cc): 0x1D pushs (u16
  data offset), 0x1E arrc (u16 count), 0x1F pushi (s16), 0x21 push local
  (u16), 0x22 cmpeq, 0x05 jne (s16).

Shape usecode function number == shape number (Usecode_internal::call_usecode
with the object's shape) and NPC conversations live at 0x400 + npc_num.
Sign texts (shape 379 -> function 0x17b) are selected by the sign's quality
and are written in the sign font's ligature shorthand.
"""
from __future__ import annotations
import re
import struct
from pathlib import Path

from .paths import STATIC

SIGN_SHAPE = 379
SIGN_FUNC = 0x17B
# Ligatures of the sign font (derived from readable strings such as
# "nor(" -> north, "we," -> west, "h+ler" -> healer, ",r)t" -> street,
# "moo*low" -> moonglow).
SIGN_LIGATURES = {"(": "th", ",": "st", "+": "ea", ")": "ee", "*": "ng", "|": " ", "~": " "}


def load_usecode(static: Path = STATIC) -> dict[int, tuple[list[str], bytes, dict[int, str]]]:
    """Returns {func_id: (strings, code, {data_offset: string})}."""
    u = (static / "USECODE").read_bytes()
    i = 0
    funcs = {}
    while i + 4 <= len(u):
        fid, ln = struct.unpack_from("<HH", u, i)
        i += 4
        if fid == 0xFFFF:
            fid, ln = struct.unpack_from("<HI", u, i)
            i += 6
        body = u[i:i + ln]
        i += ln
        if len(body) < 2:
            continue
        dl = struct.unpack_from("<H", body, 0)[0]
        data = body[2:2 + dl]
        by_off = {}
        pos = 0
        for chunk in data.split(b"\0"):
            by_off[pos] = chunk.decode("latin1")
            pos += len(chunk) + 1
        strs = [s for s in by_off.values() if s]
        funcs[fid] = (strs, body[2 + dl:], by_off)
    return funcs


def decode_sign(s: str) -> str:
    out = "".join(SIGN_LIGATURES.get(c, c) for c in s)
    return re.sub(r"\s+", " ", out).strip()


def sign_texts(funcs) -> dict[int, str]:
    """Parse function 0x17b: `push local1; pushi Q; cmpeq; jne; pushs..;arrc`
    blocks give the text lines for sign quality Q."""
    if SIGN_FUNC not in funcs:
        return {}
    strs, code, by_off = funcs[SIGN_FUNC]
    out = {}
    pat = re.compile(rb"\x21\x01\x00\x1F(..)\x22\x05..((?:\x1D..)+)\x1E", re.S)
    for m in pat.finditer(code):
        q = struct.unpack("<h", m.group(1))[0]
        offs = [struct.unpack_from("<H", m.group(2), k + 1)[0] for k in range(0, len(m.group(2)), 3)]
        # strings are pushed last-line-first; reverse to read naturally
        lines = [decode_sign(by_off.get(o, "")) for o in reversed(offs)]
        out[q] = " ".join(l for l in lines if l)
    return out


ROLE_PAT = re.compile(r"\bI am (?:the |a |an )?([A-Za-z][A-Za-z' -]{2,28}?)(?:[.,;!\"]| of | here| and | who )")
ROLE_STOP = {"avatar", "sorry", "sure", "not", "afraid", "glad", "here", "certain", "well", "fine", "busy",
             "pleased", "honored", "honoured", "called", "known", "no", "sorry to", "happy", "very", "so",
             "too", "in", "on", "the", "a", "an", "just", "only", "still", "now", "most", "quite"}


def npc_role_phrase(funcs, npc_num: int) -> str:
    """Crude: first 'I am the X' phrase in the NPC's conversation function."""
    f = funcs.get(0x400 + npc_num)
    if not f:
        return ""
    for s in f[0]:
        for m in ROLE_PAT.finditer(s):
            phrase = m.group(1).strip().lower()
            first = phrase.split()[0]
            if first in ROLE_STOP or phrase in ROLE_STOP or len(phrase) < 4:
                continue
            if any(w in phrase for w in ("thee", "thou", "thy", "thine", "very", "sorry", "sure")):
                continue
            return phrase
    return ""
