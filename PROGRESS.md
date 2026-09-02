# u7-remake progress

Black Gate in Godot 4 C#, driven by original `u7/STATIC` data and Exult 1.12.1 as source of truth. Not a full game yet: walk Britannia, open gumps, talk to NPCs, watch the clock, hatch eggs, fight.

Last update: 2026-09-04.

## Status

| Area | State |
|---|---|
| Map (U7MAP / CHUNKS / IFIX / IREG) | Done |
| Walker + click-to-walk A* | Done |
| Usecode VM (BG bytecode) | Done (many intrinsics still stub) |
| Gumps (inventory, containers, stats) | Done (BG paper doll, containers, weight/volume/stacks; no SI PAPERDOL.VGA / spellbook / save) |
| NPCs from `INITGAME.DAT` `npc.dat` | Done (291 used of 356; unused skipped) |
| Game clock + dusk/night modulate | Done (RGBA grade, not 8-bit palettes) |
| Schedules (`assets/data/schedules.csv`) | Done (core types; rest stand-at-dest) |
| Eggs | Done (teleport, usecode, jukebox, button, monster) |
| Combat | Done (melee v1; no missiles / arrest / bodies) |
| Audio, intro, barges, party-follow | Not started |
| Serpent Isle | Out of scope |

`dotnet build` of `godot/U7.csproj` is clean.

## How to run

Open the **`godot/`** project in Godot 4.6 (C# / .NET 8). Data root is the repo (`u7/STATIC/U7MAP` must exist). `u7/`, `exult/` and `assets/` are not tracked; [README.md](README.md) explains how to provide them. Start tile is Trinsic **1079, 2214**, hour **6:00**.

| Key / input | Action |
|---|---|
| WASD / arrows | Walk |
| Left click | Walk (A*) / drag items |
| Shift+click | Teleport avatar to tile |
| Double-click / E | Use object or run NPC usecode `0x400+id` |
| I | Avatar inventory |
| C | Toggle combat (walk to nearest foe and auto-strike) |
| F4 | Toggle avatar invincibility (hits still flash / bark) |
| [ ] | Skip −1 / +1 hour |
| Home | Return to Trinsic start |
| F2 | Usecode / gump debug |
| F3 | Combat arena (heal, spawn 3 rats, combat on) |
| Mouse wheel | Zoom 1–8× |

## Done milestones

1. **Walker** — isometric map, extracted SHAPES.VGA PNGs, avatar at Trinsic, pick object under cursor.
2. **Usecode VM** — `STATIC/USECODE` (1012 functions). Double-click runs shape-fun or `0x400+npc_num`. SAY, answers, faces, `click_on_item`. Interpreter matches Exult `ucinternal.cc` (BG 16-bit opcodes).
3. **Gumps** — GUMPS.VGA windows, drag between containers/world, stats sheet from NPC props, checkmark close.
4. **NPCs, clock, schedules**
5. **Eggs** — teleport, usecode, jukebox, button, monster spawn.
6. **Combat** — melee v1.
7. **Inventory** — BG paper doll + real container gumps.

### NPCs / clock / schedules (current)

- `npc.dat` parsed byte-for-byte from Exult `Actor::read` (`fix_first`). Leftover **0** after 356 records.
- NPC 0 fills the existing avatar (shape 721). Inventory nested IREG goes into `Contents`.
- Double-click NPC → usecode **`0x400+npc_num`**, not shape-fun. Spark is **0x402**.
- Clock: 25 ticks/minute, HUD `Day N  HH:MM`. WorldView modulate: day white, dusk warm, night dark blue. Gumps unmodulated.
- Nearby NPCs (~32 tiles) walk; far ones sit on slot dest until approached. `wait` / `follow_avatar` are **not** overwritten from SCHEDULE.DAT (Iolo stays in Trinsic).
- Pathfinding: wrap-around A*, cap 1024 nodes; used by NPCs and avatar click-to-walk.

**Where people are at hour 6**

| NPC | Name | Notes |
|---|---|---|
| 1 | Iolo | `wait` at 1069, 2212 (next to avatar) |
| 11 | Petre | `wait` near start |
| 12 | Finnigan | `wait` near start |
| 14 | Johnson | vert-pace at the gate (~1094, 2215) |
| 2 | Spark | loiter at his house (~970, 2135), not the gate. Walk west, or `]` to noon (inn ~1062, 2231) |

Verified offline: Spark/Johnson/Petre tiles match `schedules.csv` Trinsic coords. Not yet play-tested in Godot from the last agent session.

## Layout

```
godot/scripts/
  Game/          U7Game, AvatarController
  Data/          GameMap, U7Object, FlexFile, shapes
  Usecode/       VM, values, BG intrinsics, conversation
  Gumps/         windows, drag, stats
  Actors/        NpcDat, schedules, combat, equipment, inventory, walk frames
  World/         GameClock, Pathfinder, EggHatcher
  Rendering/     WorldView, ShapeCache
  Core/          U7Paths, constants, TileCoord
u7/STATIC/       original Black Gate files (untracked; GOG install, not imported into Godot)
assets/          extracted PNGs, schedules.csv, usecode_disasm.txt (untracked; scripts/extract_assets.py)
exult/exult-1.12.1/   source of truth (untracked; Exult 1.12.1 source release)
```

### Eggs (current)

- Hatchable IREG (shapes 200, 275, 305, 776, 777) parsed as eggs, not containers. 1×1 eggs (200/275) are hidden from paint/pick; moongates (305) still draw.
- Avatar step, Shift-click, Home, and dropping an item hatch nearby eggs. After map load, eggs hatch with `from = -1` like Exult `activate_eggs`.
- Implemented: **teleport** (coords or path-egg quality), **usecode** (`event 3`), **jukebox** (HUD track only), **button**, **monster** (spawn from IREG data). Missile / weather / sfx / voice log a stub once.
- Party teleport moves the avatar only (no follow). Jukebox is not MIDI.

### Combat (current)

- Monster eggs hatch via Exult `Monster_egg` packing (`sched`/`align`/`cnt`, shape from d2/d3). NPC-class or `monsters.csv` shapes spawn; others drop a takeable temporary item.
- Spawned stats from `assets/data/monsters.csv` (`Randomize_initial_stat`); health = strength; alignment from the egg unless the egg is neutral.
- Hostility: good↔evil/chaotic, evil↔good/chaotic, chaotic↔evil/good; **neutral never initiates**. Avatar is good.
- Attacking a permanent NPC (or hitting one via usecode damage) engages it: it enters the combat schedule, chases and strikes like a spawned monster, and returns to its previous schedule when its target dies or moves out of sight (Exult `prev_schedule`).
- Nearby spawned hostiles (sight 24) chase and melee. **C** or the paperdoll combat button toggles avatar combat: the avatar walks to the nearest foe and auto-strikes at weapon reach (WASD/click still override). Double-click a hostile attacks. Getting hit turns combat on.
- Hit/damage: Exult `roll_to_win` (30-sided) and `apply_damage` (str/3 + weapon − **worn + monster** armor). Readied `weapons.csv` item, else innate weapon, else monster weapon points. Worn `armor.csv` protection and immunities apply. Ranged weapons only if adjacent (no missiles).
- Starting kit: IREG ready-slot index (`entlen==2`) is kept; then `ready_best_weapon` / best shield. Open inventory (**I**) for the paper doll.
- Death: `Obj_flags::dead`; spawned monsters are removed. Avatar barks and cannot walk. No bodies, blood, arrest, or combat music.
- Trinsic start: cached-in monster egg at **1084, 2236** (dog, shape 496, **neutral** — spawn test, will not attack).
- **F3** heals the avatar, spawns three chaotic rats (shape 523) six tiles out, and turns combat on. Press again to reset the wave. **F4** toggles invincibility (hits still flash red and bark the blocked damage). Shift-click to open ground first so they are not in the Trinsic street.

### Inventory (current)

- Black Gate paper doll is Exult `Actor_gump`: GUMPS.VGA silhouette (shape 65) with **world item sprites** on the 12 ready spots. SI `Paperdoll_gump` / `PAPERDOL.VGA` is not in vanilla BG STATIC, so it is not used.
- Containers (`Container_gump`) use GUMPS.VGA art and the same SHAPES.VGA sprites, packed into Exult’s object-area rects (chest, bag, crate, barrel, …). Double-click a bag in a gump opens it.
- Drop: nest/combine on the item under the mouse, else the closest empty ready spot if `fits_in_spot` (READY.DAT + `paperdol_info.txt`), else auto-equip / bags (`Actor::add`). Chests refuse over-volume and over-weight (`2 × strength` stones).
- Quantity shapes stack up to 100. Two-handed weapons paint GUMPS.VGA 48 over the right hand; weight `current/max` is font 2 at the feet. Halo and combat-mode buttons match Exult (save disk is still a no-op).

## Not done (on purpose)

- Missile / weather eggs
- Projectiles, bodies, blood, arrest, combat music
- Party: `add_to_party`, follow-avatar (Iolo stands still)
- Full schedule classes (waiter, smith, farm, …) — stubbed as stand
- Hunger, poison, barges, weather, dungeon lights
- True 8-bit palette cycling (world PNGs are day-baked RGBA)
- Intro / audio / endgame
- SI paperdolls (`PAPERDOL.VGA`), spellbook, save/load gumps
- Many BG intrinsics still log `stub UI_*` and return 0
- Proximity usecode (`npc_proximity`) not on a timer

## Next milestone

Audio mixer, intro, barges, and party-follow stay out. Arrest can wait.
