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
needs (paper-doll layout, bodies) are copied into `godot/data/bg/` and
`godot/scripts/Actors/Bodies.cs`, under Exult's GPL terms.

## 3. Extracted assets: `assets/`

Run the extraction script from the repository root:

```bash
python scripts/extract_assets.py
```

It reads `u7/` and writes about 500 MB into `assets/`: shape, gump,
font and face frames as PNG, music and speech, text tables, usecode
disassembly and the CSV tables the game loads at startup
(`assets/data/*.csv`, `assets/text/shape_names.txt`). The game does not
need the PNG frames: it decodes SHAPES.VGA and the other VGA files from
`u7/STATIC/` at runtime. Two optional
scripts, `scripts/extract_endgame_flics.py` and `scripts/render_map.py`,
produce the endgame frames and a full-map render and are not needed to
play.

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
