# Serpent Isle: the engine plan

Plan of 2026-10-12, first drafted before the bridge ([BRIDGE.md](BRIDGE.md)) and brought up to date after it. The remake plays Serpent Isle the way Exult does: one engine, the game chosen when a world loads, each per-game branch where Exult puts its `GAME_SI` / `GAME_BG` one, per-game data read from files rather than C# constants. Black Gate keeps working at every step.

## 1. What is in `serpent/`

- **GOG's Serpent Isle with Silver Seed merged in:** `serpent/GAMEDAT/IDENTITY` reads `SILVER SEED`. Exult treats that as SI with an expansion flag (`GAME_SS`), not a separate game. (Black Gate's `u7/GAMEDAT/IDENTITY` reads `FORGE`, BG with the Forge of Virtue.)
- **The formats are Black Gate's:** `USECODE` parses with the remake's reader (1237 functions against BG's 1012); `U7CHUNKS`, the 144 `U7IFIX` files, `INITGAME.DAT`, the VGA files and `PALETTES.FLX` are all there.
- **New files:** `PAPERDOL.VGA`, `INTRO.DAT` (the intro's and endgame's movies), `SISPEECH.SPC` and extra `.PAL` files.
- `/serpent/` is gitignored (Origin's data).

## 2. What assumes Black Gate today

| Area | Where | What SI needs |
|---|---|---|
| Paths | `U7Paths.StaticDir` is `u7/STATIC`; `SaveGame` copies `u7/GAMEDAT/IDENTITY` | the game's STATIC folder; its own saves |
| Intrinsics | `BgIntrinsics` looks them up by number | SI numbers them differently: look up by name with a BG and an SI table, as Exult's `intrinsics_bg` / `intrinsics_si` |
| Shape and gump numbers | `U7Constants` (gumps from `bggame.cc`), ~100 `const int …Shape`, `MaxShapes = 1024` | SI's gumps differ (book 27 not 32, stats 42 not 47); `sigame.cc` has 53 `add_shape` entries; SI has 1036 shapes (`gamemap.cc:446`) |
| Exult's per-game data | `shape_info.txt` and the like copied into C# tables (`ShapeCatalog`, `ObjectNames`, `MonsterInfo`, `ItemQuantity`, ...) | Exult's `data/{bg,si}/*.txt` read at runtime from `godot/data/{bg,si}` |
| Data extracted by titan | `assets/data/*.csv` (weapons, armour, monsters, schedules, typeflags), `shape_names.txt`, MIDIs | `WEAPONS.DAT`, `MONSTERS.DAT`, `TFA.DAT`, `WGTVOL.DAT`, `TEXT.FLX` read directly, as Exult's `shapevga.cc` does |
| BG story hooks | opening flags 0x3B/0x3C, `Guards` and usecode 0x625, the Trinsic start, the map quiz | BG only; SI has its own arrival and arrest (`stop_arresting`) |
| Shows | `MainMenuView`, `Intro`, `NewGameView`, `Endgame` | SI's from `sigame.cc`: its menu, the `INTRO.DAT` movies, its new game, its endgame |

Exult's SI code to port is about 130 branches in about 30 files (most in `schedule.cc`, `ucinternal.cc`, `gamewin.cc`, `actors.cc`, `intrinsics.cc`, `egg.cc`), plus `sigame.cc` (1655 lines) and `Paperdoll_gump.cc` (1097 lines).

## 3. Three ways to play

| Mode | Launch | At Black Gate's end |
|---|---|---|
| Black Gate | `--game bg` | quits after the credits, as the original did |
| Serpent Isle | `--game si` | SI's own menu, new game, intro and endgame |
| The saga | `--game saga` | the bridge mod (*The Eighteen Months*, `AfterEndgame`), then Serpent Isle |

With no argument, a small picker (M7) shows the installed games; until it exists the default is Black Gate. `restart.ps1 -Game bg|si|saga` does the same headless.

## 4. Core design: a per-load `GameContext`

- **`GameContext`** holds the game (`IsBg`, `IsSi`, `IsSs`, as Exult's `game.h`), the mode (bg, si, saga), its STATIC folder (`u7/` or `serpent/`), its Exult data folder (`godot/data/bg` or `si`), its saves folder and its mod. Later milestones add the intrinsic names table, the gump map (`bggame.cc` / `sigame.cc`) and its shows.
- **Switching games** sets the context and the GAMEDAT source and reloads the scene (`ReloadCurrentScene()`, as loading a save already does). Loads, the bridge's handoff and loading an SI save while playing BG all take this one path.
- **Static state** (caches and tables, about 86 fields) must survive a switch: shape-number tables move into the per-game data; the file caches (`U7Palette`, `VgaFont`, `TextMessages`, `XformTables`, `PaletteSet`, `Intro`'s and the like) are cleared on a switch. A headless test (BG → SI → BG, then compare) catches leaks.
- **Saves:** `saves/bg/`, `saves/si/`, `saves/saga/`, each slot in Exult's GAMEDAT layout with its game's `IDENTITY`, so loading any save picks its game. A saga save also carries a saga marker and, once in SI, the carry-over record. The save/load screen lists one mode's saves. `saves/settings.cfg` stays shared.

## 5. The saga's crossing

Exult has no carry-over, so these are the remake's own rules. Settled by the bridge's decisions (BRIDGE.md):

- **The way across** is the bridge mod: after Black Gate's won endgame, `AfterEndgame` plays *The Eighteen Months*; its closing card is where the handoff (B5) begins.
- **What carries over:** who the avatar is: name, sex, portrait and skin (the skin is chosen in Black Gate's new game, as Exult's Black Gate offers it with SI installed). SI's own starting stats keep its balance. Companions are matched by name, not NPC number. Story flags don't carry (SI's usecode never reads BG's); items needn't, as SI's opening storm takes them in its own usecode (to check once it runs).
- **The crossing's look:** SI's intro from its voyage on (flics 6–8; D2), then SI's new game with the carry-over and its name/sex screen skipped, then an automatic first saga save. A standalone SI game plays the whole intro.

Still open: whether a finished Black Gate game outside the saga offers "Continue to Serpent Isle".

## 6. Milestones

- **M0, plumbing** (done 2026-10-12, PROGRESS.md "Serpent Isle (current)"): `GameContext`, switching by scene reload, the caches of game files dropped on a switch; `--game bg|si|saga`; `saves/{bg,si,saga}` (existing saves moved to `saves/bg/`), each save with Exult's IDENTITY and loaded in its game; `restart.ps1 -Game` and the console's `game`. SI's usecode is held back until M3. Tested: BG → SI → BG and SI by switch match fresh processes. Left for later: the saga marker and carry-over record (M4), the picker (M7).
- **M1, the SI world loads** (done 2026-10-12, PROGRESS.md "Serpent Isle (current)"): SHAPES.VGA's 1124 shapes; names, TFA.DAT, WGTVOL.DAT, OCCLUDE.DAT, WEAPONS.DAT, AMMO.DAT, ARMOR.DAT and MONSTERS.DAT read from the game's files for both games (titan's CSVs no longer read); Exult's SI load and paint branches (the Silver Seed lever, SI's dead bodies, dungeon roofs at lift 5, the Y depression); path markers hidden as Exult's egg-likes. Hi-res art from `assets/hires/si` (none yet). `tp` round SI headless and windowed shots done.
- **M2, per-game data** (done 2026-10-13, PROGRESS.md "Serpent Isle (current)"): Exult's `data/{bg,si}` files in `godot/data/{bg,si}` read as Exult reads them (`shape_info.txt`, `bodies.txt`, `container.dat`, `gump_area_info.txt`, `avatar_data`, `paperdol_info`, `shape_files`), replacing the tables copied from Black Gate; gumps by Exult's names (`GameShapes`); the 75 shape numbers left in the code audited against Exult's SI branches, the per-game ones ported (guards by place, street maintenance, baker, tailor, beds, spellbook gumps, gear powers, serpentine books), the BG-only ones gated. The glow and window lists keep in SI only the shapes that mean the same there; SI's own are a look question.
  - Exult's SI branches with shape numbers left for later (M3, M6): the frost serpent's lying frames (actors.cc:3186-3203, 4432), the Nightmare Smith's face (ucinternal.cc:660-670), the Temple of Discipline's trap eggs (ucinternal.cc:860-872), wells' scripts (ucinternal.cc:1527-1535), `get_speech_face`'s serpent ring (intrinsics.cc:2373-2390), the patrol without markers (schedule.cc:1061-1075), the loiterers' ghost (schedule.cc:1622), the Silver Seed maze's NPC 31 (schedule.cc:1279), Talk's event (schedule.cc:1468), SI's reagents and mana (spellbook.cc), the paperdoll's combat-stats button (Paperdoll_gump.cc:231-243). M3 ported the Nightmare Smith's face, the trap eggs, the wells' scripts and Talk's event.
- **M3, SI usecode** (done 2026-10-13, PROGRESS.md "Serpent Isle (current)"): intrinsics by name in both tables; the SI-only ones (temperature, `set_polymorph`, `resurrect_npc`, `stop_arresting`, `npc_nearby2`, `can_avatar_reach_pos`, ...); SI's branches in `ucinternal.cc` (Petra's face, the Nightmare Smith, ...). Goal: SI's opening plays through the agent console. Reached: the ship, Iolo's talk, the landing and the storm that scatters the party, to the avatar alone 176 tiles east. Left for M7: `get_speech_face` (the serpents' faces with SISPEECH.SPC).
- **M4, the bridge's handoff (B5):** needs only SI's new game and opening. Agent commands `saga` (end the BG game and cross over) and `carry` (show the record), so it can be tested from any BG save.
- **M5, SI's gumps:** the jawbone, the SI spellbook (its own reagents, each spell 1 more mana), SI's books, scrolls and signs.
- **M6, SI's engine rules:** cold and freezing (`Actor::check_temperature`), the frost serpent, the goblin village rule, SI's egg rules (Silver Seed's teleport eggs), the rest of the `actors.cc` and `schedule.cc` branches.
- **M7, shows and sound:** SI's menu, intro, new game and endgame (`sigame.cc`); SI's music mapping (`Audio.cc`) and `SISPEECH.SPC`; the game picker.
- **M8, play-through:** SI from its own new game (Monitor, Fawn, Moonshade, ... the end), Silver Seed, then one whole saga crossing.

M0–M3 come first and in order; the bridge's B5 needs them.

## 7. Already done by the bridge's work (B1)

Built for Black Gate, but SI's engine reuses them:
- **Reading SI's files** when installed (`U7Paths.SerpentStaticDir`, Exult's `<SERPENT_STATIC>`): shape imports (`Shape_file::import_shapes`), the skin bodies (2048-2059) and faces, the paperdolls.
- **Paperdolls:** Exult's `Paperdoll_gump` with SI's `PAPERDOL.VGA` (`Gumps/PaperdollGump`, `Data/PaperdollInfo`), Exult's `avatar_data.txt` skins (`Actors/AvatarLook`).
- **The keyring** (Exult's `keyring.cc` idea, after BG Keyring).
- **Intrinsics SI shares,** in Exult's BG table past 0x96: the schedule changes (`set_new_schedules`, `run_schedule`, `modify/revert_schedule`, saved as `schedule.dat`), two conversation faces (`show_npc_face0/1`, `remove_npc_face0/1`, `change_npc_face0/1`, `set_conversation_slot`), `set_npc_name`, `set/get_usecode_fun`, `save_pos` / `teleport_to_saved_pos`, `game_day`, `fade_palette_sleep`, `set_intercept_item`. M3 gives them SI's numbers.
- **Usecode:** `CALLO`, indirect calls, try, statics; Exult's whole NPC record in saves.
- **SI's painted faces** are ready in `assets/faces/si/` for when SI's `FACES.VGA` is read; a painted Serpent Isle map is in `godot/data/map/serpent_isle.png` (its warp still to fit).
