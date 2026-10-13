"""The renderer's regression suite (docs/RENDERER.md, section 7): scenes saved
as slots, their planes dumped (`art dump`) and compared bit for bit with
golden ones, and shots of them for approving looks.

usage: python scripts/agent/render_suite.py build|golden|check|parity|shots [--pack NAME] [--truecolour] [--modern] [scene...]

  build   makes each scene's slot (saves/bg/rs_<scene>) from its base save
  golden  dumps every scene's planes as the golden ones
  check   dumps them again and compares them with the golden ones
  parity  dumps every scene on the CPU backend and on the GPU's two resolves
          and compares them (a windowed game only: docs/RENDERER.md, R3)
  shots   takes a shot of each scene at zoom 4 (a windowed game only)

The game must be running with the agent console (scripts/agent/restart.ps1,
-Windowed for parity and shots); U7_AGENT says which one, as for agent.py.
Goldens are made headless and checked headless: the headless view is square,
so its planes are not a window's. They and the shots live in
saves/render_suite/ (local, as saves are). Animation is held (`art still on`)
so that a slot loads to the same planes in every run. Parity compares the
backends within one windowed run (there is no GPU headless): each dump on
the CPU, then on the GPU's reference resolve, then on its skipping resolve,
every plane bit for bit; Trinsic's street once more after the window is
resized mid-run. With --pack, every scene loads with that pack's art
(`art pack`), and with --truecolour its own colours shown; goldens then live
in golden_<pack>[_tc]/ (docs/RENDERER.md, R4: the test art of
scripts/upscale/testart.py); --modern turns modern colour on (R5), whose
shots go to shots_<pack>[_tc]_modern/ for shot_diff.py against classic's.
"""
import os
import re
import shutil
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import agent  # noqa: E402

SUITE = os.path.join(agent.ROOT, 'saves', 'render_suite')
GOLDEN = os.path.join(SUITE, 'golden')
# The art every scene loads with: the user's pack (ArtLook.Pack's default, which the goldens were made with) or one
# given (--pack; "original" for the original art blown up), its colours shown or not (--truecolour).
DEFAULT_PACK = 'nxbrz'
PACK = DEFAULT_PACK
TRUECOLOUR = False
MODERN = False
SHOTS = os.path.join(SUITE, 'shots')
PLANES = os.path.join(agent.DIR, 'planes')
# The window size parity resizes Trinsic's street to, mid-run (the planes' textures are remade), and back.
RESIZE = (1000, 700)
WINDOW = (1280, 800)
# Iolo was saved bleeding in sweep_1, and a saved bleeder bleeds on at a load, its pool's frame by chance
# (Exult Actor::set_type_flags, Actor::bleed). Health rising stops it: lowered, seen by the bleed timer (first
# after 5 s), raised, seen again.
STAUNCH = ['prop npc:1 health 17', 'wait 0.7', 'prop npc:1 health 18', 'wait 0.2']
# The towns' scenes start from ng_a (a new game in Trinsic after the opening, Iolo in the party), not quick:
# quick and the saves made from it carry 11 of Trinsic's windows and 29 of its roof pieces a second time in
# their IREG (an old bug), so their panes would turn the light twice.
NEW = 'ng_a'


class Scene:
    def __init__(self, name, base, setup=(), view=(), zooms=(4,), covers=''):
        self.name = name
        self.base = base
        self.setup = list(setup)  # run on the base save, then saved as the slot
        self.view = list(view)  # run after loading the slot (what a save doesn't keep)
        self.zooms = zooms
        self.covers = covers

    @property
    def slot(self):
        return 'rs_' + self.name


SCENES = [
    Scene('trinsic', NEW, [], zooms=(1, 2.5, 4, 4.5, 8),
          covers='camera: zoom steps, integer and fractional; Trinsic\'s street'),
    Scene('glass', NEW,
          ['tp 1062 2228 0', 'create 895 0 0 at 1062 2223 0', 'create 317 0 0 at 1063 2223 0',
           'setflag npc:1 poisoned 1', 'setflag npc:0 invisible 1'],
          ['sprite 7'],
          covers='stacked XFORM: a window over a ghost over a fire field; the teleport sprite over the invisible '
                 'avatar; a poison outline; inside a roofed house'),
    Scene('house_out', NEW, ['tp 1062 2219 0'], covers='roof transitions: the same house from outside'),
    Scene('panes', NEW, ['tp 1046 2204 0'], ['sprite 7 8 1038 2197 0'],
          covers='the through-panes op: the teleport sprite hidden under a roof at 1038,2197, showing through the '
                 'east-facing glass at 1039,2198 (frame 8 overlaps the panes; frame 0 is too small)'),
    Scene('sea', NEW, ['tp 1118 2212 0'], zooms=(2, 4), covers='ground continuity: the sea off Trinsic and its coast (no glints in view)'),
    Scene('water', NEW, ['tp 1128 2088 0'],
          covers="cycling: the water's glints off Trinsic's coast (chunk 70,130 holds about a hundred cycling pixels; "
                 "scripts/agent/cycling_check.py tells whether they cycle at the art's resolution)"),
    Scene('lamps', NEW, ['hour 22', 'create 526 0 at 1083 2213 0', 'create 435 0 at 1083 2217 0'],
          covers="light occlusion: Trinsic's street at night with a lit lamp and a lit sconce set by the house wall east of "
                 "the avatar (the lamp posts' own light is usecode's pool when the lamplighter comes), so their pools stop "
                 "at the wall (docs/RENDERER.md R6)"),
    Scene('castle_night', NEW, ['tp 936 1138 0', 'hour 22'],
          covers='lighting: the throne room at night, its light sources; under a roof'),
    Scene('castle_roof', NEW, ['tp 935 1127 5'], covers='lifts: on the castle\'s walls at lift 5'),
    Scene('yew', NEW, ['tp 331 1660 0'], covers='ground continuity: Yew\'s graveyard'),
    Scene('destard', 'sweep_1', STAUNCH, zooms=(2, 4), covers='occlusion: the dungeon blackness in Destard'),
]


def say(cmds):
    out = agent.run(cmds)
    for line in out.splitlines():
        if line.startswith('ERROR') or 'timeout waiting' in line:
            sys.exit(f'{cmds}: {line}')
    return out


def dumps(scene):
    """Each dump's name and the commands making it: every zoom at the original scale, and at the art's from zoom 2."""
    for zoom in scene.zooms:
        for scale in (1, 4):
            if scale == 1 or zoom >= 2:
                name = f'{scene.name}_z{zoom}_s{scale}'
                yield name, [f'zoom {zoom}', f'art dump {name} {scale}']


def art():
    """The commands choosing the art (the pack and its colours) before a scene loads."""
    return [f'art pack {PACK}', f'art truecolour {"on" if TRUECOLOUR else "off"}', f'art modern {"on" if MODERN else "off"}']


def load(scene):
    say(['art still on'] + art() + [f'load {scene.slot}'] + scene.view)


def build(scene):
    say(['art still on', f'load {scene.base}'] + scene.setup + [f'save {scene.slot}'])
    print(f'{scene.slot}: {scene.covers}')


def golden(scene):
    load(scene)
    os.makedirs(GOLDEN, exist_ok=True)
    for name, cmds in dumps(scene):
        print(re.search(r'^planes .*$', say(cmds), re.M).group(0))
        target = os.path.join(GOLDEN, name)
        shutil.rmtree(target, ignore_errors=True)
        shutil.copytree(os.path.join(PLANES, name), target)


def check(scene):
    load(scene)
    failed = 0
    for name, cmds in dumps(scene):
        say(cmds)
        with open(os.path.join(PLANES, name, 'planes.txt')) as f:
            now = f.read().strip()
        with open(os.path.join(GOLDEN, name, 'planes.txt')) as f:
            was = f.read().strip()
        if now == was:
            print(f'{name}: same')
            continue

        failed += 1
        diff = say([f'art diff {os.path.join(GOLDEN, name)} {name}'])
        print(f'{name}: DIFFERS: ' + next(l for l in diff.splitlines() if l.startswith('index')))
    return failed


def dump(name, cmds, backend):
    """One dump on a backend ('cpu', 'reference' or 'skip'): its planes.txt summary, the backend's tag checked."""
    switch = ['art renderer cpu'] if backend == 'cpu' else ['art renderer gpu', f'art resolve {backend}']
    out = say(switch + cmds[:-1] + [f'art dump {name} {cmds[-1].split()[-1]}'])
    line = re.search(rf'^planes {re.escape(name)}: .*$', out, re.M).group(0)
    tag = {'cpu': '', 'reference': ', gpu reference', 'skip': ', gpu skipping'}[backend]
    if backend == 'cpu' and ', gpu' in line or backend != 'cpu' and tag not in line:
        sys.exit(f'{name}: not painted on the {backend} backend: {line}')
    return line.split(': ', 1)[1].replace(tag, '')


def parity(scene):
    load(scene)
    failed = 0
    sets = list(dumps(scene))
    if scene.name == 'trinsic':
        # The window's new size reaches the viewport a frame later: a throwaway dump passes the frames, game time frozen.
        sets.append((f'{scene.name}_resized_z4_s4', [f'window {RESIZE[0]} {RESIZE[1]}', 'art dump settle 4', 'zoom 4', 'art dump x 4']))
    for name, cmds in sets:
        cpu = dump(name + '_cpu', cmds, 'cpu')
        for backend in ('reference', 'skip'):
            gpu = dump(f'{name}_{backend}', cmds, backend)
            if gpu == cpu:
                print(f'{name} {backend}: same')
                continue

            failed += 1
            diff = say([f'art diff {name}_cpu {name}_{backend}'])
            print(f'{name} {backend}: DIFFERS: ' + next(l for l in diff.splitlines() if l.startswith('index')))
    say([f'window {WINDOW[0]} {WINDOW[1]}', 'art renderer cpu'])
    return failed


def shot(scene):
    load(scene)
    os.makedirs(SHOTS, exist_ok=True)
    say(['zoom 4', f'shot {scene.slot}'])
    shutil.copy(os.path.join(agent.DIR, f'{scene.slot}.png'), os.path.join(SHOTS, f'{scene.name}.png'))
    print(f'{scene.name}: shot')


def main():
    global PACK, TRUECOLOUR, MODERN, GOLDEN, SHOTS
    args = sys.argv[1:]
    if '--pack' in args:
        i = args.index('--pack')
        PACK = args[i + 1]
        del args[i:i + 2]
    if '--truecolour' in args:
        TRUECOLOUR = True
        args.remove('--truecolour')
    if '--modern' in args:
        MODERN = True
        args.remove('--modern')
    if PACK != DEFAULT_PACK or TRUECOLOUR or MODERN:
        suffix = f'_{PACK}{"_tc" if TRUECOLOUR else ""}{"_modern" if MODERN else ""}'
        GOLDEN = os.path.join(SUITE, 'golden' + suffix)
        SHOTS = os.path.join(SUITE, 'shots' + suffix)
    if len(args) < 1 or args[0] not in ('build', 'golden', 'check', 'parity', 'shots'):
        sys.exit(__doc__)
    sys.argv = [sys.argv[0]] + args
    names = sys.argv[2:]
    scenes = [s for s in SCENES if not names or s.name in names]
    action = {'build': build, 'golden': golden, 'check': check, 'parity': parity, 'shots': shot}[sys.argv[1]]
    failed = sum(action(s) or 0 for s in scenes)
    if sys.argv[1] in ('check', 'parity'):
        print('all the same' if failed == 0 else f'{failed} dumps differ')
        sys.exit(1 if failed else 0)


if __name__ == '__main__':
    main()
