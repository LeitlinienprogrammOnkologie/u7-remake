# Ultima VII remake: modernization design

Status: proposal, 2026-09-10. Companion to [PROGRESS.md](../PROGRESS.md), which records what is built today.

## 1. Goal

Ship *The Black Gate* as a game a 2026 player can install, run and finish on any desktop, with the feel of the original intact, while keeping the Exult-derived simulation that already exists in `godot/scripts` as the engine. "Modern" means the parts of the experience that have aged: setup, resolution, input, audio, saving, feedback and polish. It does not mean new rules, new art or a different game.

The guiding rule for every decision below:

> **The simulation is Exult. The presentation is ours.**

Anything that decides *what happens* (usecode, schedules, eggs, combat rolls, inventory rules, paint order, the 200 ms tick) stays a function-by-function port and is treated as read-only from a design standpoint. Anything that decides *how it looks, sounds and is controlled* may be rebuilt freely, as long as it consumes the simulation through a narrow interface.

## 2. Where the project stands

### 2.1 What exists

| Layer | Files | State | Verdict |
|---|---|---|---|
| Data formats | `Data/FlexFile`, `VgaShapeFile`, `ShapeCatalog`, `GameMap`, `U7Object`, `RenderOrdering` | U7MAP / CHUNKS / IFIX / IREG / eggs / dungeon levels / Exult paint dependencies | **Keep** |
| Usecode VM | `Usecode/*` | BG opcode loop ported from `ucinternal.cc`; about 75 of 151 intrinsics implemented, the rest log a stub | **Keep, complete** |
| NPCs | `Actors/NpcDat`, `ScheduleRunner`, `ScheduleTable`, `PartyManager` | npc.dat byte-exact; 8 schedule types real, ~25 stubbed as stand; party formation and follow ported | **Keep, complete** |
| Combat | `Actors/CombatEngine`, `WeaponTable`, `ArmorTable`, `MonsterInfo` | Melee v1 with Exult hit / damage / dex timing | **Keep, complete** |
| Inventory | `Actors/Equipment`, `Inventory`, `Gumps/*` | Paper doll, containers, weight / volume, drag and drop, all Exult semantics | **Keep logic, re-skin presentation** |
| World | `World/EggHatcher`, `GameClock`, `Pathfinder` | Eggs indexed per chunk as in `Chunk_cache`; clock; wrap-around A* | **Keep** |
| Rendering | `Rendering/WorldView`, `ShapeCache` | Immediate-mode `_Draw` every frame, one PNG per shape frame loaded from disk, chunk flats cached as textures | **Replace** |
| Game shell | `Game/U7Game`, `AvatarController` | 740-line bootstrap node that also owns input, HUD labels, debug keys and conversation widgets | **Replace** |
| Audio | `Audio/MusicPlayer` | MT-32 to GM conversion ported from Exult, played through `winmm`; Windows only; no SFX, no speech | **Replace transport, keep conversion** |
| Assets | `scripts/extract_assets.py` via the `titan` CLI | ~54,000 PNG / CSV / MIDI files, ~550 MB, day palette baked into RGBA | **Retire for graphics** |
| Persistence | none | Loads the GOG `GAMEDAT` snapshot only; no save, no load, no options | **Add** |
| Tests | none | | **Add** |

### 2.2 How coupled the simulation is to Godot

A grep of the non-rendering scripts shows the engine dependency is thin:

| File | Godot use |
|---|---|
| `CombatEngine`, `UsecodeMachine` | `Time.GetTicksMsec()` for strike animation and timers |
| `Pathfinder`, `ScheduleRunner` | `Vector2I` as a tile pair |
| `VgaShapeFile` | `Image` as the decode target |
| everything | `GD.Print` |

That is the whole list. The simulation can become a Godot-free assembly in a day of mechanical edits, which is what makes the rest of this document cheap: the modern layer can be written against a stable, testable core instead of against a scene tree.

### 2.3 Things that will fight modernization if left alone

- **Day-baked graphics.** The PNG pipeline flattens palette 0 into RGBA. Night, dusk, dawn, dungeon darkness, translucency (`XFORM.TBL`), palette cycling (water, fire, lava), fades and the palette-driven weather are all impossible on top of it. The current night is a `Modulate` tint over day art, which is the one visible place the remake looks like a remake.
- **Wall-clock time in the simulation.** Two subsystems read the OS clock. Any fixed-step, pause, fast-forward, replay or headless test breaks on that.
- **Unseeded randomness.** `new Random()` in three places. Saves and tests need a seeded stream.
- **Per-frame immediate paint with one texture per frame.** Every visible object is a separate `DrawTexture` on a separate texture, so Godot cannot batch. It is fine at Trinsic and will not be fine in Britain at 1440p.
- **The 320×200 assumption inside gumps** is a *virtual* screen scaled by zoom, which is actually the right design; it only needs anchoring rules for wide screens.
- **No license file.** The code is a function-by-function port of Exult, which is GPL-2.0-or-later. The repository must carry that license before anything is distributed. This is a blocker, not a nicety.

## 3. Principles

1. **Read the originals.** Exult reads `STATIC` directly; so should we. The extraction step becomes a developer tool, not an install step. A player points the game at a GOG folder and plays.
2. **Sim in tiles, screen in pixels.** Actors live on integer tiles and move once per 200 ms tick, exactly as today. Smoothness is a renderer trick, never a simulation change.
3. **Palette is data, not paint.** Store shapes as 8-bit indices and apply the palette on the GPU. Everything Exult does with palettes then falls out of one shader.
4. **One narrow seam.** Presentation talks to the simulation through a small set of interfaces (`IWorldQuery`, `ISimEvents`, `IGumpHost`, `IAudioSink`, `ILog`, `IClock`, `IRng`). Nothing in `U7.Core` references `Godot`.
5. **Classic by default, modern by option.** Every modern touch that changes the look (smooth movement, lights, filters, health bars, modern font) ships behind a setting whose default is the original behaviour, except where the original is objectively a usability bug (no save slots, no key rebinding, 4:3 letterbox).
6. **Finish what is ported before porting more.** Stubbed intrinsics and schedules are the real gap between "walkable demo" and "finishable game"; they are simulation work and come first.

## 4. Target architecture

```
┌──────────────────────────────────────────────────────────────────┐
│ U7.Godot  (Godot 4.x, C#)                                        │
│  Shell: Main menu · New game · Options · Save/Load · Death       │
│  Presentation: WorldRenderer · GumpRenderer · Hud · Conversation │
│  Platform: AudioMixer (MIDI synth, SFX, speech) · Input map      │
│            · Settings · SaveStore · FileLocator                  │
└───────────────▲───────────────────────────────▲──────────────────┘
                │ IWorldQuery / ISimEvents       │ IAudioSink / IClock / IRng / ILog
┌───────────────┴───────────────────────────────┴──────────────────┐
│ U7.Core  (netstandard2.1 / net8, no Godot reference)             │
│  Data: Flex · Vga · Palettes · Xform · Catalog · GameMap · Object │
│  Sim:  Usecode VM + intrinsics · Schedules · Party · Combat       │
│        · Eggs · Clock · Pathfinder · Inventory · Barges · Weather │
│  Persist: GameState (dirty IREG, npc, flags, timers, clock, rng)  │
└──────────────────────────────────────────────────────────────────┘
┌──────────────────────────────────────────────────────────────────┐
│ U7.Tests  (xunit)  · U7.Tools (headless sim runner, extractors)   │
└──────────────────────────────────────────────────────────────────┘
```

### 4.1 The simulation tick

`U7.Core` exposes one entry point:

```csharp
void Tick();                 // advance exactly one 200 ms step
IEnumerable<SimEvent> Drain(); // what happened: moved, frame changed, bark, sfx, music, hit, died, gump opened…
```

Godot calls `Tick()` from a fixed accumulator in `_PhysicsProcess` (or its own accumulator at 5 Hz) and renders at display rate. Fast-forward, pause, time-skip and headless tests are all "call `Tick()` N times". The two OS-clock reads move behind `IClock.TickCount`.

### 4.2 Events out, commands in

The renderer never polls the object tree for "what changed". The sim emits `SimEvent`s; the renderer keeps per-object *visual* state (previous tile, step start tick, hit flash, bark) keyed by `U7Object.Id`. Input turns into commands (`WalkTowards`, `Use(obj)`, `Drag(obj, to)`, `ToggleCombat`, `Answer(i)`), which is also the shape a gamepad, a touch layer or a scripted test needs.

## 5. Modernization by area

### 5.1 Graphics: indexed rendering

This is the highest-value single change and it is *more* faithful, not less.

**Storage.** At load, decode `SHAPES.VGA`, `GUMPS.VGA`, `FONTS.VGA`, `FACES.VGA`, `SPRITES.VGA` with the RLE decoder that already exists in `VgaShapeFile` into 8-bit index buffers, and pack them into a small number of `R8` atlases (a `ShapeRegistry` maps shape/frame to atlas rect plus hotspot). 35,000 files become ~8 textures and one lookup table. Startup stays fast by decoding lazily per shape file and caching the packed atlases in `user://cache` keyed by the `STATIC` file hashes.

**Palette.** A `256×N` `RGB8` texture holds the game palettes from `PALETTES.FLX` (day, dusk, night, dawn, ambient, the lightning and fade ramps) plus the live palette. A canvas shader samples the index, then the live palette. Per tick the `PaletteController` does what Exult's `Palette` class does:

- selects the time-of-day palette (`Game_clock::set_time_palette`), with the Exult 6-step dusk / dawn ramp rather than the current two-colour lerp;
- rotates the cycling ranges (the water / fire / lava index bands) at Exult's rates;
- applies dungeon darkness and light sources by lift, as `Game_window::paint` does today with `PaintDungeonBlackness`;
- runs fades (`fade_palette` intrinsic, currently a stub) and the lightning flash.

**Translucency.** `XFORM.TBL` is a set of 256-entry remap tables. Translucent shapes (`ShapeRecord.Translucent`) draw with a second shader path that reads the destination index through a screen-space `R8` copy and remaps through the xform texture, which is the exact operation `Image_buffer8::paint_rle_translucent` performs. This restores glass, water surfaces, ghosts and magic effects.

**Smooth motion (option, default on).** The sim keeps tile positions. The renderer records `(fromTile, toTile, startTick)` on each `Moved` event and draws the sprite at the interpolated pixel position for the 200 ms of the step. Paint dependencies still use the logical tile, so ordering never flickers. The walking animation frame still advances per tick, so it looks like the original with the hitching removed rather than like a different game.

**Widescreen and zoom.** The world view already draws whatever the camera sees. Add: integer zoom presets (2×, 3×, 4×), a "fit" zoom that picks the largest integer scale that keeps at least 320×200 virtual pixels visible, and pixel-perfect camera rounding (already done). Gumps get anchors (centre, bottom-left, stagger from centre) inside the virtual screen instead of absolute 320×200 coordinates.

**Batching.** With atlases, the existing immediate-mode paint in dependency order becomes cheap because consecutive draws share a texture. Keep the algorithm; drop the per-object `ImageTexture`.

**Optional modern layer.** All off by default: Godot `PointLight2D` on `LightSource` shapes at night, a subtle CRT / scanline filter, and a soft-scale filter for non-integer zoom. Explicitly *not* offered: HD sprite packs, AI upscaling, a 3D camera.

### 5.2 Game shell

- **First run.** A locator dialog finds the GOG install (registry, default paths, drag a folder) and validates the file set by name and size, then remembers it in `user://settings.json`. `U7Paths.Initialize` stops walking up from `res://`.
- **Main menu** using `MAINSHP.FLX` art: New game, Continue, Load, Options, Intro, Credits, Quit.
- **New game.** Name and gender (`is_pc_female`, currently a stub), then the intro, skippable. The intro flics are already extracted by `extract_endgame_flics.py`; render them from the `.flc` decoder or from frames.
- **Options.** Video (zoom, filter, fullscreen, smooth motion, lights), Audio (music / SFX / speech volume, synth choice), Controls (rebind, gamepad), Gameplay (classic / modern toggles, text speed), Accessibility (font scale, high-contrast text box, colour-blind palette swap for the cursor and health bars).
- **Death.** The Exult resurrection sequence at Lord British's, with a load-game offer. Today the avatar simply cannot walk.
- **Endgame.** `ENDGAME.DAT` / `ENDSHAPE.FLX` playback; assets are already extracted.

### 5.3 Input

- Move every hard-coded `Key.*` into Godot `InputMap` actions: `walk_*`, `use`, `inventory`, `combat`, `pause`, `menu`, `map`, `journal`, `highlight_items`, `quick_save`, `quick_load`. Debug keys (`F2`–`F4`, `[ ]`, `PgUp`, `Home`) move into a developer console gated by a setting, off in release.
- **Mouse.** Keep click-to-walk (A\*) and double-click to use. Add Exult's hold-to-walk: while the button is held, the avatar keeps walking towards the cursor with speed by distance from centre. Right-drag pans the camera when zoomed; hover tooltips show the object name (the name is already computed for the HUD every frame).
- **Gamepad.** Left stick walks in 8 directions, right stick moves a virtual cursor; `A` use, `B` cancel, `X` inventory, `Y` combat, bumpers cycle party portraits, `Start` menu. Gumps become cursor-navigable with a snap-to-slot mode. This is what Steam Deck needs and it costs little because input already funnels into commands.
- **Keyboard-only.** Tab cycles nearby usable objects with an outline, Enter uses, number keys answer conversations.

### 5.4 UI

The gump system is the one part of the original UI worth keeping as-is: the drag-and-drop containers and paper doll are the game. Keep `Gump`, `GumpManager`, `DragState` logic unchanged; keep the GUMPS.VGA art; render through the new indexed renderer.

Replace the ad-hoc `Label` HUD with:

- **Conversation panel.** Portrait (`FACES.VGA`) left, text in a box at the bottom, answers as a vertical list navigable by keyboard / gamepad / mouse. Default font is `FONTS.VGA` scaled to the zoom, with an option for a modern TTF with the same colour and box. Text speed setting; click or `A` to advance.
- **Status strip.** Clock and date, party portraits with health bars (Exult ships `hp_bar.shp`), current schedule hint for the avatar (combat / peace), quick-slot for the currently readied weapon.
- **Bark bubbles.** Draw with the same bitmap font, positioned like `Game_window::add_text`, replacing the `ThemeDB` fallback font.
- **Map.** `MAPCOORD.DAT` plus the already-rendered `assets/maps/minimap.png` for the `display_map` intrinsic, with the avatar marker and, in modern mode, discovered-city labels. No fast travel.
- **Journal.** Exult's `autonotes.txt` behaviour: certain conversations write a note. Fully optional and cheap to add once conversation events exist.
- **Highlight items** (`Alt` in Exult): outline every takeable object on screen via a one-pixel dilate in the shader.

### 5.5 Audio

**Music.** Keep the MT-32-to-GM conversion (already ported) but drop `winmm`. Render MIDI in-process with a pure-C# General MIDI synthesizer (MeltySynth, MIT) driven from an `AudioStreamGenerator`, with a bundled small GM soundfont (a redistributable one such as GeneralUser GS or a trimmed FluidR3). This gives Windows, Linux, macOS and Steam Deck the same music with no system dependency. Add a 1.5 s cross-fade between tracks and the jukebox-egg repeat semantics that already exist.

**Sound effects.** Two faithful options, both from data already extracted:

1. Emulate AdLib: play `ADLIBSFX.DAT` through an OPL2 emulator (a C# port of Nuked OPL or DOSBox's DBOPL, ~2,000 lines). This is the sound most players remember from DOS.
2. Digital packs: support Exult's `sqsfx.flx` / `jmsfx.flx` format so a player who downloads the community SFX pack from exult.info gets sampled effects. Never bundled.

Positional mixing: attenuate by tile distance from the avatar and pan by dx, as Exult's `Audio::play_sound_effect` does with its distance parameter.

**Speech.** `U7SPEECH.SPC` is already extracted to WAV; wire `start_speech` / `start_blocking_speech` intrinsics to an `AudioStreamPlayer` and gate conversation advance on blocking speech, exactly like the Guardian's lines in the original.

### 5.6 Saving and loading

Design the save around what Exult writes, so the writer mirrors the reader that already exists:

| Part | Source of truth | Note |
|---|---|---|
| Moveable objects | dirty `U7IREGxx` per superchunk | IREG writer is the inverse of `GameMap.LoadAllIreg`; IFIX is never written |
| NPCs | `npc.dat` record per actor | inverse of `NpcDat.Load`, including schedules and inventories |
| Flags | `GFlags[2048]`, item flags | |
| Time and weather | clock, day, palette state, weather | |
| Party | member list, formation, dead party | |
| Usecode timers, delayed usecode arrays, scheduled paths | `PATH.DAT`-equivalent | |
| Gump positions, hatched eggs, RNG seed | | |

Container format: one zip per slot in `user://saves/slotNN.u7save` with the files above plus `meta.json` (name, location name, in-game time, play time, timestamp, screenshot PNG, engine version). Slots are unlimited, plus quicksave and a rolling autosave every in-game hour and at every sleep. Because the file set is the GAMEDAT set, importing an Exult save (a flex of the same files) is a small stretch goal rather than a rewrite.

Determinism requirement: the sim RNG is a seeded `IRng` saved with the state, and no simulation code reads the OS clock.

### 5.7 Completing the simulation

Modern polish is worthless if the game cannot be finished. These are simulation ports, in order of how often the player will hit them:

1. **Intrinsics** still returning 0 that block quests: `execute_usecode_array` / `delayed_execute_usecode_array` (cutscenes and timed scripts), `set_timer` / `get_timer`, `sprite_effect` / `obj_sprite_effect`, `play_sound_effect`, `start_speech`, `fade_palette`, `set_time_palette`, `path_run_usecode`, `fire_projectile`, `summon`, `resurrect`, `display_map`, `book_mode`, `nap_time`, `advance_time`, `call_guards`, `set_orrery`, `earthquake`, `lightning`, `armageddon`, `mark_virtue_stone` / `recall_virtue_stone`, `run_endgame`, `restart_game`, barge intrinsics.
2. **Schedules**: waiter, tend-shop, eat, desk-work, farm, blacksmith, bake, sew, lab, preach, dance, kid-games, thief, graze, hound, duel, sleep polish, walk-to-schedule with doors. Each is a class in Exult's `schedule.cc`.
3. **Combat**: missiles and projectiles, ranged AI, party member AI (attack modes already exist on the paper doll), bodies and blood, arrest and guards, combat music transitions, poison, hunger, healing over time.
4. **Barges** (ships, carts, the magic carpet) and the moongate / orrery / Time Lord sequences.
5. **Weather, missile and sfx eggs**; dungeon lights; rain and storms.
6. **Usecode scripts** (`Usecode_script`), the delayed action queue every cutscene depends on.

None of this changes the presentation contract, which is why the two tracks can run in parallel.

### 5.8 Engineering

- **Assemblies.** `U7.Core` (no Godot), `U7.Godot` (the game), `U7.Tests`, `U7.Tools`. Enforced by the build: `U7.Core` simply has no Godot package reference.
- **Tests.** Golden tests for the VM: run selected usecode functions in a headless world at known coordinates and compare the intrinsic log and flag log against a recorded trace (the `IntrinsicLog` / `FlagLog` already exist). Format tests for every reader against the GOG files (present on dev machines, skipped in CI). A headless "day in Trinsic" run that ticks 24 hours and asserts every NPC reached each schedule slot's destination.
- **Performance budget.** 60 fps at 4× zoom on a 1440p screen in Britain with ~1,500 visible objects and 3 open gumps on integrated graphics. Atlases plus batching get there; profile before adding more.
- **Cache.** Atlases and the decoded catalog are cached in `user://cache` and invalidated by `STATIC` hashes and engine version, so second launch is instant.
- **Logging.** `ILog` with categories (usecode, schedule, combat, egg) written to `user://logs`, surfaced in the developer console. Replaces 36 scattered `GD.Print` calls.
- **Exports.** Windows, Linux, macOS (universal), with Steam Deck verified via the Linux build and the gamepad layer. No data is ever bundled; the export contains only the engine and the soundfont.

## 6. Roadmap

Each milestone is playable on its own and does not depend on the milestone after it.

| # | Milestone | Scope | Exit criterion |
|---|---|---|---|
| M0 | Housekeeping | `LICENSE` (GPL-2.0-or-later), `U7.Core` split, `IClock` / `IRng` / `ILog`, `Tick()` + event drain, xunit skeleton, golden VM traces | Sim builds without Godot; headless day-in-Trinsic test passes |
| M1 | Indexed renderer | RLE-to-atlas decode from `STATIC`, palette shader, time-of-day ramps, cycling, xform translucency, dungeon darkness, batching, smooth motion option, widescreen anchors | PNG extraction not needed to run; night in Trinsic matches Exult side by side |
| M2 | Shell and persistence | File locator, main menu, new game, options, save / load / quicksave / autosave, death sequence, settings, input map, gamepad | A player can install from GOG, start, save, quit, continue |
| M3 | Audio | In-process GM synth, OPL SFX, speech, positional mixing, cross-fades | Music on Linux and macOS; Guardian speaks in the intro |
| M4 | Simulation completion I | Usecode scripts and delayed arrays, timers, remaining intrinsics, all schedule classes | Trinsic murder investigation completable start to end |
| M5 | Simulation completion II | Missiles, party combat, bodies, arrest, barges, weather, hunger / poison, moongates | Britain, Cove, Minoc, Yew, Vesper reachable and their quests completable |
| M6 | UI and modern layer | Conversation panel, status strip, map, journal, highlight items, accessibility, optional lights / filters | Full playthrough with gamepad only |
| M7 | Endgame and release | Intro / endgame playback, Exult save import, export builds, performance budget verified | Black Gate finishable on three platforms |

M0 and M1 are prerequisites for everything else and should not be skipped or reordered. M4 and M5 can interleave with M2, M3 and M6 because they touch only `U7.Core`.

## 7. Non-goals

- Serpent Isle, the Forge of Virtue and Silver Seed add-ons, and any SI-only feature (`PAPERDOL.VGA`, spellbook gump variants). The SI paths in Exult are ignored during porting.
- New content, rebalanced combat, new quests, remixed music.
- HD or AI-upscaled art. The palette renderer is the "remaster".
- Multiplayer, mobile, web export.
- Redistributing any Origin, GOG or Exult data. The engine ships empty.

## 8. Risks

| Risk | Impact | Mitigation |
|---|---|---|
| License exposure from distributing a GPL derivative without the license | Blocks any release | M0, first task |
| Palette shader diverges from Exult in dusk / dawn or xform | Visible regressions that are hard to describe | Side-by-side screenshot tests against Exult at fixed tiles and hours, checked into `U7.Tests/golden` |
| Save format drift while the simulation is still being completed | Old saves stop loading | Version every part, write migrations from day one, and keep the parts as separate files inside the slot |
| In-process synth quality below the Windows GS synth players are used to | Music complaints | Ship a good soundfont and keep a "system MIDI" option on Windows |
| Scope creep into "modern RPG" features | Loses the point of the project | Section 7 is the contract; new features enter only as options whose default is classic |
| Porting stubs faithfully takes longer than the presentation work | Presentation looks finished while the game is not | Order M4 before M6; track the intrinsic and schedule counts in `PROGRESS.md` |

## 9. Decisions to make now

1. Adopt GPL-2.0-or-later and add the license file. (Not optional.)
2. Approve the `U7.Core` / `U7.Godot` split and the no-Godot rule for the core.
3. Approve indexed rendering from `STATIC` as the replacement for the PNG pipeline, which retires `extract_assets.py` for graphics and keeps it for audio and tables until those readers are ported too.
4. Pick MeltySynth plus a bundled soundfont as the default music path, with OPL emulation for SFX.
5. Adopt the zip-of-GAMEDAT-files save format.
