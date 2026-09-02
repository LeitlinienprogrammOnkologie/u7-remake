# Ultima VII: The Black Gate remake

A Godot 4.6 / .NET 8 (C#) remake of Ultima VII: The Black Gate, ported
function by function from [Exult](https://exult.info) 1.12.1. Progress,
controls and per-system notes live in [PROGRESS.md](PROGRESS.md).

The repository holds only source. The original game files, the Exult
source tree and the extracted assets are **not tracked** (see
`.gitignore`) and have to be provided locally as described below.

## Prerequisites

- Godot 4.6 with C# support and the .NET 8 SDK
- Python 3.12 or newer
- The `titan` CLI from the `titan-ultima` package: `pip install titan-ultima`
- Ultima VII: The Black Gate from GOG (sold as *Ultima 7 Complete*)
- The Exult 1.12.1 source release

## 1. Game data: `u7/`

Copy (or junction) the GOG installation into `u7/` at the repository
root. The GOG default install path on Windows is one of

```text
C:\GOG Games\Ultima 7 Complete
C:\Program Files (x86)\GOG Galaxy\Games\Ultima 7 Complete
```

The game reads only `u7/STATIC/` and `u7/GAMEDAT/`, but the extraction
script and `titan` expect the whole install. The data root is located at
runtime by walking up from the Godot project until `u7/STATIC/U7MAP`
exists, so the folder name and position are fixed.

`titan.toml` at the repository root carries an absolute `base` path for
the `titan` CLI. Point it at your own `u7/` directory.

## 2. Exult source: `exult/exult-1.12.1/`

Unpack the Exult 1.12.1 source tarball so that
`exult/exult-1.12.1/data/bg/paperdol_info.txt` exists. The tree is the
port's reference, and the paper-doll layout is currently read from that
file at runtime. Without it items still equip, but through a cruder
fallback, so a checkout without the tree behaves differently in the
inventory gump.

## 3. Extracted assets: `assets/`

Run the extraction script from the repository root:

```bash
python scripts/extract_assets.py
```

It reads `u7/` and writes about 500 MB into `assets/`: shape, gump,
font and face frames as PNG, music and speech, text tables, usecode
disassembly and the CSV tables the game loads at startup
(`assets/data/*.csv`, `assets/text/shape_names.txt`). Two optional
scripts, `scripts/extract_endgame_flics.py` and `scripts/render_map.py`,
produce the endgame frames and a full-map render and are not needed to
play.

## 4. Run

Open the `godot/` project in Godot 4.6 and press Play, or build from the
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
