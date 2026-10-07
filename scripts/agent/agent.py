"""Send commands to the game's agent console and print what they produced.

usage: python scripts/agent/agent.py "look" "talk Finnigan" "cont all" ...

Commands go to <io>/cmd.txt; the script waits until the game has written one
"DONE <n>" per command to <io>/out.txt and prints the new output. <io> is
$U7_AGENT, else agent_io/ at the repository root (what restart.ps1 uses).
"""
import os
import re
import sys
import time

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DIR = os.environ.get('U7_AGENT') or os.path.join(ROOT, 'agent_io')
CMD = os.path.join(DIR, 'cmd.txt')
OUT = os.path.join(DIR, 'out.txt')
TIMEOUT = 180


def read_out():
    try:
        with open(OUT, encoding='utf-8', errors='replace') as f:
            return f.read()
    except FileNotFoundError:
        return ''


def done_count(text):
    return len(re.findall(r'^DONE \d+$', text, re.M))


def main():
    cmds = sys.argv[1:]
    before = read_out()
    target = done_count(before) + len(cmds)
    os.makedirs(DIR, exist_ok=True)
    with open(CMD, 'a', encoding='utf-8') as f:
        for c in cmds:
            f.write(c + '\n')
    deadline = time.time() + TIMEOUT
    text = before
    while time.time() < deadline:
        text = read_out()
        if done_count(text) >= target:
            break
        time.sleep(0.1)
    else:
        print('[timeout waiting for the game - is it running? see scripts/agent/restart.ps1]')
    print(text[len(before):].rstrip())


if __name__ == '__main__':
    main()
