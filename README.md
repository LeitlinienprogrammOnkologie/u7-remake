# Ultima VII: The Black Gate remake

A Godot 4.7 / .NET 8 (C#) remake of Ultima VII: The Black Gate, ported
function by function from [Exult](https://exult.info) 1.12.1. Progress,
controls and per-system notes live in [PROGRESS.md](PROGRESS.md).

The repository holds only source. The original game files, the Exult
source tree and the extracted assets are **not tracked** (see
`.gitignore`) and have to be provided locally as described below.

## Prerequisites

- Godot 4.7 with C# support and the .NET 8 SDK
- Python 3.12 or newer
- The `titan` CLI from the `titan-ultima` package: `pip install titan-ultima`
- Ultima VII: The Black Gate from GOG (sold as *Ultima 7 Complete*)
- The Exult 1.12.1 source release (optional, reference only)

## 1. Game data: `u7/`

Copy (or junction) the GOG installation into `u7/` at the repository
root. The GOG default install path on Windows is one of

```text
C:\GOG Games\Ultima 7 Complete
C:\Program Files (x86)\GOG Galaxy\Games\Ultima 7 Complete
```

The game reads only `u7/STATIC/` (a new game starts from
`STATIC/INITGAME.DAT`, like Exult; `u7/GAMEDAT/` is ignored), but the extraction
script and `titan` expect the whole install. The data root is located at
runtime by walking up from the Godot project until `u7/STATIC/U7MAP`
exists, so the folder name and position are fixed.

`titan.toml` at the repository root carries an absolute `base` path for
the `titan` CLI. Point it at your own `u7/` directory.

## 2. Exult source: `exult/exult-1.12.1/` (optional)

Unpack the Exult 1.12.1 source tarball here if you want the port's
reference at hand. Nothing reads it at runtime: the data tables the game
needs (paper-doll layout, bodies, the avatar's skins, the shape imports) and
Exult's own Black Gate paperdoll and face art (`godot/data/bg/flx/`) are
copied into `godot/data/bg/` and `godot/scripts/Actors/Bodies.cs`, under
Exult's GPL terms.

## 2b. Serpent Isle: `serpent/` (optional)

With the GOG Serpent Isle (with Silver Seed) copied to `serpent/` (its
`STATIC` folder is what is read), Black Gate shows Exult's paperdolls and can
offer Serpent Isle's skin colours, as Exult's Black Gate does when SI is
installed. Without it, Black Gate's own paper doll shows.

Serpent Isle itself is being brought up (docs/SERPENT_ISLE.md): run with
`--game si` (or `U7_GAME=si`; `--game saga` for Black Gate, the bridge and
then Serpent Isle). Saves go to `saves/bg`, `saves/si` and `saves/saga`.

The bridge mod (`mods/eighteen`) borrows Serpent Isle's blackrock serpent
(read from its SHAPES.VGA at load) and, for the Guardian's scroll, the voices
of Serpent Isle's intro. Make those locally (they stay out of the repository):

```bash
python scripts/bridge_speech.py      # serpent/STATIC/INTRO.DAT -> mods/eighteen/data/u7speech.spc
```

## 3. Extracted assets: `assets/`

Run the extraction script from the repository root:

```bash
python scripts/extract_assets.py
```

It reads `u7/` and writes about 500 MB into `assets/`: shape, gump,
font and face frames as PNG, music and speech, text tables, usecode
disassembly and CSV tables. Of these the game reads only the music
(`assets/audio`, `assets/intro_endgame`): it decodes SHAPES.VGA and the
other VGA files and reads the shapes' data (TFA.DAT, WGTVOL.DAT,
WEAPONS.DAT, MONSTERS.DAT, TEXT.FLX, ...) from the game's `STATIC/` at
runtime. The CSVs and disassembly are references for working on it. Two optional
scripts, `scripts/extract_endgame_flics.py` and `scripts/render_map.py`,
produce the endgame frames and a full-map render and are not needed to
play.

### Redrawn art (optional): `assets/hires/`

The world, gumps and faces can be shown in art redrawn at four times the
resolution by an AI upscaler, made locally from your own game files
(`scripts/upscale/`). It needs a DirectX 12 graphics card (AMD or NVIDIA,
through DirectML) and about 1 GB of disk:

```bash
python -m venv scripts/upscale/.venv
scripts/upscale/.venv/Scripts/python -m pip install torch --index-url https://download.pytorch.org/whl/cpu
scripts/upscale/.venv/Scripts/python -m pip install onnxruntime-directml numpy pillow spandrel onnx
for f in sprites gumps faces shapes; do
  scripts/upscale/.venv/Scripts/python scripts/upscale/generate.py --model nxbrz --file $f
  scripts/upscale/.venv/Scripts/python scripts/upscale/pack.py --model nxbrz --file $f   # version 3 packs: a scale per frame, soft alpha, cycling at 4x
done
```

`generate.py` downloads the model into `assets/hires/models/` on first use
(the models and their licences are listed in `scripts/upscale/models.py`;
most are free for non-commercial use only) and takes about ten minutes per
model on a recent card. Without `assets/hires/` the game shows the
original art.

When new art turns up (Serpent Isle installed, or a mod), draw only what is
missing and rebuild those packs, the paperdolls included:

```bash
python scripts/upscale/missing.py --model nxbrz          # what is missing (no venv needed)
scripts/upscale/.venv/Scripts/python scripts/upscale/missing.py --model nxbrz --make
```

The paperdolls show in their original art unless switched to the redrawn
ones with F7 in the game (remembered in `saves/settings.cfg`).

To have the art painted anew (by hand or by another AI: new paintings of the
same subjects, not enlargements), export it as sets: one shape with all its
frames, as a sheet to paint, its outline masks, the world round it (Britannia
painted from the original art, the piece outlined) and a prompt
(`PROMPT.md` for the whole folder and its structure, `prompt.txt` per set):

```bash
python scripts/upscale/sets.py --file shapes                         # assets/hires/sets/shapes/PROMPT.md
python scripts/upscale/sets.py --cut <set folder> <painted sheet.png> # the painting into its frames
python scripts/upscale/sets.py --file shapes --check                 # what is drawn, missing or the wrong size
scripts/upscale/.venv/Scripts/python scripts/upscale/pack.py --model painted --file shapes
```

The frames go to `assets/hires/work/painted/shapes/SSSS_FF.png` at exactly
4x the frame; a painting with a transparent ground keeps its own outline
(objects and characters; terrain, walls, floors and roofs keep the
original's so they join). The console's `art pack painted` shows them, and
frames not painted yet show in the original art. The style the prompts ask
for is `STYLE` in `sets.py`.

`assets/hires/sets/shapes/viewer.html` (`scripts/upscale/viewer.py`, rebuilt by
every export and cut) shows each set's originals beside their paintings in
a browser, by kind and set, with the progress and wrong sizes flagged, and
each world view full size beside the same view drawn with every painting so
far (a close preview: the colours aren't reduced to the palette).

### Painted ground (optional): materials

The ground is assembled from a few painted materials (sea, calm sea, grass,
mud, sand, swamp, cave floor, cave rock), each one seamless 512 x 512
texture, laid by the original's map of where each material lies, so the sea
is the same sea everywhere and chunks join without a seam:

```bash
python scripts/upscale/ground.py                 # materials.zip, PROMPT.md and assets/hires/ground/ground.html
python scripts/upscale/ground.py --compose       # every chunk whose materials are painted
scripts/upscale/.venv/Scripts/python scripts/upscale/pack.py --model painted --file chunks
```

Give the AI `assets/hires/ground/materials.zip` with its `PROMPT.md` (also
on the page, with copy buttons); it saves each material as
`assets/hires/work/painted/materials/<name>.png`. Open `ground.html` and
press Reload to see the paintings tiled 2 x 2; `--compose` assembles the
chunks (shown on the page among their neighbours), `pack.py --file chunks`
packs them and the console's `art pack painted` shows them in the game.
`ground.py --preview <cx> <cy>` shows a few chunks, original beside assembled.

### Painted faces (optional): `assets/faces/`

Painted portraits in place of the faces' art: put them in `u7-new-faces/portraits/`
(Serpent Isle's in `si-new-faces/portraits/`), one PNG per FACES.VGA shape
and frame named `face_SSS_frame_FFF.png`, and make them ready (numbers burned
in under them taken out, a black background round the frame made
see-through, trimmed, at most 512 pixels) with

```bash
python scripts/faces/prepare.py          # needs numpy, pillow, scipy
```

Without `assets/faces/` the game shows the faces' own art. The console's
`art faces off` switches back to it (remembered in `saves/settings.cfg`).

### Sound effect packs (optional): `assets/sfx/`

Exult's digital sound effects, recordings of the originals' own (Simon
Quinn's from a Roland MT-32, Joseph Morris's from a SoundBlaster), come in
Exult's all-in-one audio pack. Fetch it (checked against the SHA-256
Exult's installer uses) and put the four packs in place with

```bash
python scripts/get_sfx_packs.py          # or --zip <exult_audio.zip> for a copy at hand
```

The game picks them as Exult does: Roland first, then SoundBlaster. Without
them the sound effects are Exult's MIDI ones (`godot/data/midisfx.flx`,
Exult's data, GPL), played through the music's synth. The console's
`sfx <n>` tries one, `sfx pack auto|roland|blaster|midi` chooses
(remembered in `saves/settings.cfg`).

Music plays through the Windows built-in MIDI synth from the MT-32 export
in `assets/audio/music_mt32/`, converted to General MIDI at load with
Exult's patch table; no soundfont or extra software is needed. On other platforms music is silent for now.

## 4. Run

The conversation panel and barks use MedievalSharp (`godot/fonts/`,
SIL Open Font License, see `godot/fonts/OFL.txt`).

Open the `godot/` project in Godot 4.7 and press Play, or build from the
command line:

```bash
dotnet build godot/U7.csproj
```

## Why these folders are ignored

The GOG files are copyrighted and must not be redistributed. The Exult
tree is available upstream. The assets are derived from the GOG files
by the script above and can be regenerated at any time. Together they
are roughly 550 MB and 54,000 files, which no source repository should
carry.
