# u7-remake progress

Black Gate in Godot 4 C#, driven by original `u7/STATIC` data and Exult 1.12.1 as source of truth. Not a full game yet: walk Britannia, open gumps, talk to NPCs, watch the clock, hatch eggs, fight.

Last update: 2026-10-07.

## Status

| Area | State |
|---|---|
| Map (U7MAP / CHUNKS / IFIX / IREG) | Done |
| Walking (Exult blocking, A*, path following, speeds) | Done (actors don't block each other yet) |
| Usecode VM (BG bytecode) | Done (many intrinsics still stub) |
| Gumps (inventory, containers, stats) | Done (BG paper doll, containers, weight/volume/stacks; no SI PAPERDOL.VGA / spellbook / save) |
| NPCs from `INITGAME.DAT` `npc.dat` | Done (291 used of 356; unused skipped) |
| Game clock + dusk/night modulate | Done (RGBA grade, not 8-bit palettes) |
| Schedules (`assets/data/schedules.csv`) | Done (core types; rest stand-at-dest) |
| Eggs | Done (teleport, usecode, jukebox, button, monster) |
| Combat | Done (melee, ranged, bodies, avatar death; no explosions / arrest) |
| Save / load | Done (quick slot, Exult GAMEDAT layout) |
| Music | Done (jukebox eggs → GM MIDI via Windows synth; no SFX/speech yet) |
| Party | Done (join/leave, formation, follow, teleport, combat) |
| Opening scene (moongate, Iolo, earthquake) | Done |
| Conversation panel | Done (portraits, paging, click to continue, answers) |
| Books and scrolls (`book_mode`) | Done (original gumps and font, Exult page layout) |
| Intro movie, barges | Not started |
| Serpent Isle | Out of scope |

`dotnet build` of `godot/U7.csproj` is clean.

## How to run

Open the **`godot/`** project in Godot 4.7 (C# / .NET 8). Data root is the repo (`u7/STATIC/U7MAP` must exist). `u7/`, `exult/` and `assets/` are not tracked; [README.md](README.md) explains how to provide them. A new game starts from `STATIC/INITGAME.DAT` (not `u7/GAMEDAT`, which holds a game already past the opening): Trinsic **1079, 2214**, hour **6:00**, with the opening scene.

| Key / input | Action |
|---|---|
| WASD / arrows | Walk while held (fast; Shift: medium) |
| Left button | Hold on open ground to walk toward the cursor (speed by distance); a quick click walks a path there; drag items |
| Shift+click | Teleport avatar to tile |
| Double-click / E | Use object or run NPC usecode `0x400+id` |
| I | Avatar inventory |
| C | Toggle combat (walk to nearest foe and auto-strike) |
| F4 | Toggle avatar invincibility (hits still flash / bark) |
| [ ] | Skip −1 / +1 hour |
| Home | Return to Trinsic start |
| PgUp / PgDn | Debug: avatar lift +1 / -1 in place (re-checks eggs at the new lift) |
| F5 / F9 | Save / load the quick slot (`saves/quick/`) |
| F6 | Debug: lethal damage to the avatar with global flag 0x57 set, so usecode 0x60E puts the party in the Fellowship shelter in Paws (836,1733); Shift+F6 leaves the flag alone, in which case the original restarts the game |
| M | Toggle music |
| F2 | Usecode / gump debug |
| F3 | Combat arena (heal, spawn 3 rats, combat on) |
| Mouse wheel | Zoom 1–8× |

### Agent console (automated play-testing)

With the environment variable `U7_AGENT=<dir>` set, `Game/U7Game.Agent.cs` reads one command per line from `<dir>/cmd.txt` and appends results to `<dir>/out.txt`, each ending in `DONE <n>`. Game time is frozen between commands, music is off, and conversation text and barks are logged. Commands: `look [r]`, `find <text>`, `npc <num|name>`, `state`, `inv [npc]`, `flags`, `stubs`, `tile <x> <y> [z]`, `walk <x> <y>`, `walkto <id|npc:num>`, `steer <dir> <sec> [ms]`, `tp <x> <y> [z]`, `talk`/`use <id|npc>`, `take <id>`, `close`, `cont [n|all]`, `choose <answer|#n>`, `num <n>`, `click <id>`, `wait <sec>`, `hour <h>`, `save`/`load <slot>`, `shot <name>` (windowed only). Object ids come from `look`/`find` output. Helpers: `pwsh scripts/agent/restart.ps1 [-Load <slot>]` starts it headless (I/O in `agent_io/`), `python scripts/agent/agent.py "<cmd>" ...` sends commands and prints the results. The Trinsic murder chapter (opening, stables, Finnigan, Spark, the chest, Gilberto, Gargan, the report and map quiz, Johnson's gate) was played through this way.

## Done milestones

1. **Walker** — isometric map, extracted SHAPES.VGA PNGs, avatar at Trinsic, pick object under cursor.
2. **Usecode VM** — `STATIC/USECODE` (1012 functions). Double-click runs shape-fun or `0x400+npc_num`. SAY, answers, faces, `click_on_item`. Interpreter matches Exult `ucinternal.cc` (BG 16-bit opcodes).
3. **Gumps** — GUMPS.VGA windows, drag between containers/world, stats sheet from NPC props, checkmark close.
4. **NPCs, clock, schedules**
5. **Eggs** — teleport, usecode, jukebox, button, monster spawn.
6. **Combat** — melee v1.
7. **Inventory** — BG paper doll + real container gumps.
8. **Combat v2** — ranged/thrown weapons with projectiles and ammo, corpses, battle music, avatar death via usecode 0x60E.
9. **Party** — Exult `Party_manager` port: join/leave, formation, follow, teleport, party combat.
10. **Save / load** — Exult GAMEDAT layout, quick slot.
11. **Usecode scripts** — Exult `Usecode_script` engine, saved with their objects.
12. **Music** — jukebox eggs → GM MIDI on Windows.

### NPCs / clock / schedules (current)

- `npc.dat` parsed byte-for-byte from Exult `Actor::read` (`fix_first`). Leftover **0** after 356 records.
- NPC 0 fills the existing avatar (shape 721). Inventory nested IREG goes into `Contents`.
- Double-click NPC → usecode **`0x400+npc_num`**, not shape-fun. Spark is **0x402**.
- Clock: 25 ticks/minute, HUD `Day N  HH:MM`. WorldView modulate: day white, dusk warm, night dark blue. Gumps unmodulated.
- Nearby NPCs (~32 tiles) walk; far ones sit on slot dest until approached. `wait` / `follow_avatar` are **not** overwritten from SCHEDULE.DAT (Iolo stays in Trinsic).
- Walking and pathfinding: see "Walking (current)".

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

### Temporary objects (current)

- Exult `Game_map::cache_out`: whenever the avatar enters a new superchunk, spawned monsters, their corpses and items flagged temporary outside the surrounding 3×3 superchunks are deleted (`GameMap.CacheOut`). Auto-reset monster eggs therefore respawn on a later visit, as in Exult.

### Rendering order (current)

- Mouse picking chooses the object painted last in the most recent frame at the cursor (`PaintStamp`), so clicks agree with what is drawn on top.
- Paint order is Exult's: each chunk computes paint dependencies between its non-flat objects (and its neighbours') with a port of `Game_object::compare` the first time it is drawn, refreshed whenever an object moves. Flat objects paint first, then each chunk row-major with every object painted after its dependencies (`Game_render::paint_object`). The old single sort key survives only in `FindNearby`'s ordering.
- Sleeping NPCs lie on top of the nearest free bed at bed lift + bed height in the sleep frame (Exult `Sleep_schedule`), unmake the bed on lying down and make it again on getting up.

### Eggs (current)

- Hatchable IREG (shapes 200, 275, 305, 776, 777) parsed as eggs, not containers. 1×1 eggs (200/275) are hidden from paint/pick; moongates (305) still draw.
- Eggs are indexed per chunk with 16 bits per tile (Exult `Chunk_cache::eggs`): solid-area criteria (cached-in, something-on, teleports) mark the whole area, everything else only its perimeter. A step looks up the bits of the tile entered instead of scanning objects within 40 tiles.
- Each step first runs Exult `test_unhatch`/`unhatch` on the tile left: jukebox and sfx eggs clear their hatched flag (auto-reset) or are removed (once), so music no longer re-triggers on every tile inside the area. Once-only eggs of every other type, usecode included, are removed after hatching.
- Teleport-in and map load use Exult `try_all_eggs`: every active egg within 32 tiles except jukebox and teleport, with the dice roll, guarded against recursion so chained teleports stop.
- Implemented: **teleport** (coords or path-egg quality), **usecode** (`event 3`), **jukebox** (plays the track), **button**, **monster** (spawn from IREG data). Missile / weather / sfx / voice log a stub once.
- Teleport eggs move the party along (`teleport_party`).

### Party (current)

- `Actors/PartyManager` ports Exult `Party_manager`: up to 8 members, `add_to_party` (good alignment, in_party and okay_to_take flags) and `remove_from_party` via the usecode intrinsics 0x24/0x25; `get_party_list` returns avatar plus members. `link_party` rebuilds the party from the npc.dat in_party flags at load (NpcDat now keeps the object flags).
- Each avatar step calls `get_followers`/`move_followers`: members walk in Exult's formation (two followers per member, behind-left and behind-right by the 4-way direction) with `Is_step_okay`, `Clear_to_leader`, `Get_cost` and `Take_best_step`. Members in combat, wait or loiter schedules stay put.
- The follow-avatar schedule (`Follow_avatar_schedule` + `Actor::follow`) only acts once the avatar stops: members farther than 6 tiles path to an offset spot beside the avatar; farther than 40 tiles they are brought over like `approach_another`. Teleports (eggs, Home, Shift-click) move the party along (`teleport_party`, free spot within 8 tiles).
- Combat: **C** (or getting hit, or attacking) switches every member between the combat and follow-avatar schedules like Exult `toggle_combat`; members chase and strike the nearest foe in sight, drop back to following when nothing is left, and monsters target the nearest party member instead of always the avatar. A member that dies leaves the party (no dead-party list or bodies yet).
- Not yet: party items intrinsics, dead-party handling and resurrection, sleeping/paralysed members, attack modes other than nearest.

### Usecode scripts (current)

- `Usecode/UsecodeScript` ports Exult `Usecode_script` (ucsched.cc): the arrays handed to `execute_usecode_array` / `delayed_execute_usecode_array` (intrinsics 0x01/0x02) run against their object over time on 200 ms ticks: cont, reset, repeat/repeat2, delays (ticks, minutes, hours), wait_while_near/far, remove, rise/descend, frame and the NPC frame opcodes 0x61-0x70, next/prev frame, say, step (forced), face_dir, music, usecode/usecode2 calls, egg, set_egg, hit, resurrect. Speech, sfx, weather and attack are accepted but do nothing yet. Scripts pause while usecode runs or a conversation waits. They are saved with their object as Exult's IREG_SPECIAL/IREG_UCSCRIPT entries (`Usecode_script::save` layout) and restored on load.
- Avatar death runs the game's death usecode 0x60E (event 4) after combat is switched off and gumps close; with global flag 0x57 set it revives the party in the Fellowship shelter in Paws, otherwise it restarts the game; the intrinsics it needs are in: play_music, fade_palette (world fades to black), get_dead_party, resurrect (Exult `Actor::resurrect`: items back, corpse gone, full health, follow or loiter), remove_npc, halt_scheduled, restart_game (reloads the initial game).

### Save / load (current)

- `Game/SaveGame` writes Exult's GAMEDAT layout into `saves/<slot>/`: `U7IREG00..8F` (Exult `write_ireg` / `write_ireg_objects`: plain 10-byte, container 12-byte and egg 12/14-byte entries, extended entries for shape ≥ 1024 or frame ≥ 64, contents terminated by 01, chunks by 00 00), `NPC.DAT` (Exult `Actor::write` layout with the extended magic/mana bytes, readied items marked `02 <spot>`), `FLAGINIT` (usecode global flags) and `GWIN.DAT` (day/hour/minute, music track, combat flag).
- Loading sets `U7Paths.GameDatOverride` to the slot and reloads the scene, so the normal loaders pick up the saved files; `NpcDat` reads a raw `NPC.DAT` when present (both the original and Exult's magic/mana layouts) and keeps schedule destinations, type flags and the object flags. The party is rebuilt from the in_party flags.
- Spawned monsters are saved to `MONSNPCS.DAT` (count + `Actor::write` records) and restored on load. Not saved: usecode timers/statics (usecode.dat, usevars), schedule changes made by usecode (schedule.dat), spellbook/virtue-stone extra bytes. Exult saves are zip files; a `saves/<slot>/` directory with the same files loads, the zip itself does not yet.

### Music (current)


- `Audio/MusicPlayer` sequences the MT-32 export in `assets/audio/music_mt32/NNNN_MT32MUS.MID` (track = jukebox egg `data1 & 0xff`), converting it at load exactly like Exult's `XMIDIFILE_CONVERT_MT32_TO_GM` (`mt32asgm` patch table, no patch changes on channel 10, bank selects dropped, volume curve, default CC7 90), on a background thread and sends it to the Windows MIDI mapper (built-in GS wavetable synth) with `winmm`, like Exult's Windows MIDI driver. Non-Windows is silent for now.
- Jukebox semantics follow Exult `Jukebox_egg` / `MyMidiPlayer`: continuous eggs hold a repeat count; leaving the last one lets the track finish and stops the repeat. **M** mutes.
- No SFX (the originals are AdLib/MT-32 data, Exult uses its own digital pack) and no speech playback yet, although speech is extracted as WAV.

### Combat (current)

- Monster eggs hatch via Exult `Monster_egg` packing (`sched`/`align`/`cnt`, shape from d2/d3). NPC-class or `monsters.csv` shapes spawn; others drop a takeable temporary item.
- Spawned stats from `assets/data/monsters.csv` (`Randomize_initial_stat`); health = strength; alignment from the egg unless the egg is neutral.
- Hostility: good↔evil/chaotic, evil↔good/chaotic, chaotic↔evil/good; **neutral never initiates**. Avatar is good.
- Attacking a permanent NPC (or hitting one via usecode damage) engages it: it enters the combat schedule, chases and strikes like a spawned monster, and returns to its previous schedule when its target dies or moves out of sight (Exult `prev_schedule`).
- A strike faces the target and plays Exult's fast-swing frames (ready, reach, strike, back to ready) one per 200 ms tick; frames the shape lacks fall back to standing. The avatar stands again once the swing ends instead of freezing mid-stride.
- Battle music (Exult `start_battle` / `monster_died`): when the avatar gains a foe and none played in the last 30 s, track 11 or 12 plays once; when the last hostile dies, victory (15) or, after a long hard fight, battle-over (9).
- Ranged and thrown weapons (Exult `Combat_schedule::attack_target` + `Projectile_effect`): a bow, crossbow, musket, wand or thrown axe fires a projectile when the target is beyond melee reach (or always, for `uses = ranged`). Ammunition comes from the quiver or any bag (`find_weapon_ammo`), charges from the wand's quality, thrown weapons consume themselves; the missile sprite flies along a straight line at the weapon's missile speed with Exult's 16-direction frames and rotation, then rolls to hit with the ranged bonus (+6, minus distance for thrown), applies weapon plus ammo damage with the ammo's damage type, drops the ammo by its drop rule, and returning weapons come straight back into the thrower's hands. Archers do not keep their distance (Exult has no such behaviour outside flee mode): they shoot from where they stand and only walk when the target is out of range. `assets/data/ammo.csv` is loaded as `Actors/AmmoTable`. Explosions, homing missiles and attacks on tiles are not ported.
- Strike timing follows Exult `dex_to_attack`: each 200 ms tick in reach banks the actor's dexterity, and a swing costs 30 points (a dex-6 rat strikes about once a second). Unarmed damage is 1 plus the strength roll; the MONSTERS.DAT weapon byte is not used.
- Nearby spawned hostiles (sight 24) chase and melee. **C** or the paperdoll combat button toggles avatar combat: the avatar walks to the nearest foe and auto-strikes at weapon reach (WASD/click still override). Double-click a hostile attacks. Getting hit turns combat on.
- Hit/damage: Exult `roll_to_win` (30-sided) and `apply_damage` (str/3 + weapon − **worn + monster** armor). Readied `weapons.csv` item, else innate weapon, else monster weapon points. Worn `armor.csv` protection and immunities apply.
- Starting kit: IREG ready-slot index (`entlen==2`) is kept; then `ready_best_weapon` / best shield. Open inventory (**I**) for the paper doll.
- Death (Exult `Actor::die`): shapes in Exult's `bodies.txt` (copied into `Actors/Bodies`) leave a corpse container (shape 400/414/762/778/892, frame per NPC, reflected like the NPC) holding the whole inventory, all okay to take; other shapes drop their items nearby. Dead permanent NPCs keep their record with the dead flag, are never placed or scheduled again, and the corpse is saved as Exult's 13-byte `Dead_body` entry with the NPC number. Avatar death runs usecode 0x60E (see Usecode scripts). No blood or arrest.
- Trinsic start: cached-in monster egg at **1084, 2236** (dog, shape 496, **neutral** — spawn test, will not attack).
- **F3** heals the avatar, spawns three chaotic rats (shape 523) six tiles out, and turns combat on. Press again to reset the wave. **F4** toggles invincibility (hits still flash red and bark the blocked damage). Shift-click to open ground first so they are not in the Trinsic street.

### Inventory (current)

- Black Gate paper doll is Exult `Actor_gump`: GUMPS.VGA silhouette (shape 65) with **world item sprites** on the 12 ready spots. SI `Paperdoll_gump` / `PAPERDOL.VGA` is not in vanilla BG STATIC, so it is not used.
- Containers (`Container_gump`) use GUMPS.VGA art and the same SHAPES.VGA sprites, packed into Exult’s object-area rects (chest, bag, crate, barrel, …). Double-click a bag in a gump opens it.
- Drop: nest/combine on the item under the mouse, else the closest empty ready spot if `fits_in_spot` (READY.DAT + `paperdol_info.txt`), else auto-equip / bags (`Actor::add`). Chests refuse over-volume and over-weight (`2 × strength` stones).
- Quantity shapes stack up to 100; dropping onto a full stack fills it and keeps the remainder (Exult `add_quantity`). A displaced readied item goes into a readied bag with room or loose into the inventory with a fresh gump position (Exult `add(obj, true)`). Weapons count only in the left hand (`Actor::get_weapon`); monsters fall back to their own shape's weapon entry (`Monster_actor::get_weapon`). Lightning, ethereal and sonic damage ignore armour. Two-handed weapons paint GUMPS.VGA 48 over the right hand; weight `current/max` is font 2 at the feet. Halo and combat-mode buttons match Exult (save disk is still a no-op).

## Not done (on purpose)

- Missile / weather / sfx / voice eggs
- Explosions, homing missiles, attacks on tiles, blood, arrest
- Party: dead-party list, party items intrinsics, sleeping/paralysed members, attack modes other than nearest
- Full schedule classes (waiter, smith, farm, …) — stubbed as stand
- Hunger, poison, barges, weather, dungeon lights
- True 8-bit palette cycling (world PNGs are day-baked RGBA)
- Intro / endgame, SFX, speech playback, music on non-Windows
- SI paperdolls (`PAPERDOL.VGA`), spellbook, save/load gumps
- Save: usecode timers (`get_timer`/`set_timer`) and statics, usecode schedule changes, Exult zip saves
- Many BG intrinsics still log `stub UI_*` and return 0
- Proximity usecode (`npc_proximity`) not on a timer
- Signs (`display_runes`) show in the conversation panel, not Exult's `Sign_gump`
- Walking: actors don't block one another (Exult's `move_aside` / `swap_positions`), Exult's speed cursor arrows, walking with the right button, `Walk_to_schedule`'s off-screen legs and dormant NPCs, the Onecoord / Offscreen / Fast / Monster pathfinder clients (combat still approaches with a greedy step), `Approach_actor_action` and `If_else_path_actor_action` (`path_run_usecode`)

## Next milestone

Remaining BG intrinsics, then `npc_proximity` timer and full schedule classes. Intro, barges and arrest can wait.

`python scripts/usecode_stub_report.py` ranks the stubbed intrinsics by static reachability (Trinsic NPCs by default, `--npcs` for others, plus all of USECODE). In game, every stub hit is counted and written to `stub_report.txt` (repo root, untracked) on exit. Nothing reachable from the Trinsic NPCs is stubbed any more; 48 intrinsics remain game-wide (top: `sprite_effect`, `flash_mouse`, `set_to_attack`, `is_not_blocked`, `is_readied`).

### Opening scene (current)

- A new game reads IREG, FLAGINIT and the rest from `INITGAME.DAT` entries (`U7Paths.ReadGameDat`); saves read `saves/<slot>/`. `u7/GAMEDAT` is not used: the GOG copy is a game already past the opening (flags 0x14, 0x3b, 0x4c, 0x5a, 0x5c set, the Trinsic superchunks 64/65/70/71 changed).
- Exult `Game_window::read`: while global flag 0x3b (`did_first_scene`) is clear, the avatar gets object flag 16 (BG `dont_move` + `bg_dont_render`): not drawn, no walking, double-clicks, drags or game keys (debug, save, music and zoom still work), followers wait (Exult `main_actor_dont_move`).
- The scene itself is the game's usecode: the cached-in egg (0x06C2) calls Iolo's 0x0401 with the egg event (music 35, barks), 0x06AA creates and animates the red moongate, 0x0618 clears flag 16, Iolo greets the Avatar, Petre interrupts, 0x08DD makes the NPCs react and shakes the screen (Forge of Virtue earthquake), Iolo joins, 0x06FA remarks on the tremor. Verified headless end to end.
- Usecode control (Exult `Actor::in_usecode_control`: flag 16/22 or a started, halting script): the player cannot walk the avatar and NPC schedules wait, so script steps and frames are not overwritten. The avatar returns to its standing frame once when a walk or swing ends (Exult `Actor::stop`), facing the way it already faces, instead of every idle frame.
- Barks (`item_say`, script `say`) follow Exult `Text_effect` (one per speaker, '@' shown as '"', drawn after the map) but in screen space: `UI/BarkOverlay` draws MedievalSharp in yellow with a dark outline, sized with the zoom, centred above the speaker's sprite and kept on screen. `WorldView.Barks` supplies the anchors.

### Conversation panel (current)

- `UI/ConversationPanel` replaces the bare label/buttons: dark wood with gold trim along the bottom, the speaker's portrait (from `FACES.VGA`; the extracted face PNGs are 8x8 tiles), their name once met before this conversation, MedievalSharp text, a pulsing continue marker, answers as numbered buttons (1–9 keys work), the number prompt, and a smaller portrait for a second speaker. The Guardian's large face gets red text like Exult's font 7.
- Paging follows Exult `say_string` / `show_npc_message`: the VM splits a say at '~' ("~~" counts once) and waits for a click after every piece (`UsecodeWait.ClickToContinue`, `ContinueText`); a leading '*' is an extra click, a '*' inside a piece starts a new page, '^' capitalises, and text too long for the box is paged in the panel. Clicks anywhere, Space or Enter continue. `display_runes` waits for a click like Exult's modal sign.
- Fonts: MedievalSharp (`godot/fonts/`, SIL OFL) for the panel and barks; gumps still use the original `FONTS.VGA`.

### Usecode items (current)

- `Actors/ItemQuantity` ports Exult's quantity container code (`add_quantity`, `create_quantity`, `remove_quantity`, `modify_quantity`, `count_objects`): stacks fill to 100 before new objects are made, carry weight limits what is added, quantity shapes (coins, arrows, bolts, lockpicks) switch to their pile frames, locked containers (522, 798) refuse.
- `count_objects` sums stack quantities (it counted objects before, so 50 gold read as 1) and counts the whole party for -357. `remove_party_items` / `add_party_items` work across the party like Exult; BG returns the receiving members and drops nothing on the ground.
- `create_new_object` / `set_last_created` / `update_last_created` / `give_last_created` keep Exult's last_created stack. Monster shapes become neutral wait-schedule monsters that join the monster AI once placed.
- `input_numeric_value` waits on a number box + OK in the answer column; `earthquake` jolts the camera ±4 px every 100 ms; `wearing_fellowship` checks the medallion (955 frame 1) on the neck; `get_timer`/`set_timer` count game hours (timers are **not saved** yet); `reset_conv_face`; both sound-effect intrinsics are silent no-ops.

### Books and scrolls (current)

- `book_mode` (0x55) ports Exult's: the says that follow go into a `Gumps/ScrollGump` (shape 797, GUMPS.VGA 55) or `Gumps/BookGump` (GUMPS.VGA 32) instead of the conversation (Exult `show_book` / `Text_gump::add_text`, which joins says with '~'). When usecode next shows pending text (RET, ABRT), the gump shows a page, or a pair of facing pages, and waits for a click per page (`UsecodeWait.BookPage`, Exult `show_pending_text`'s book loop); the book is dropped when the call ends (`call_usecode`'s `set_book(nullptr)`).
- Layout is Exult's `Text_gump::paint_page` and `Font::paint_text_box` on FONTS.VGA font 4 (`Data/VgaFont`: glyph widths, `hor_lead`, line height from `calc_highlow`): '~' ends a line, '*' ends a page (a book fills the rest with blank lines), and Black Gate scrolls also break at " ~~" but keep going on the same page when the next block fits with two lines to spare. Pages are laid out once per page turn and painted from the cached lines.
- The book is painted over everything at the largest whole scale that fits the window, centred (Exult `Gump::set_pos`), with the original art and font. A left-button release turns the page (not the release of the double-click that opened it); Esc stops reading (Exult `Get_click` returning false); Space and Enter also turn the page, as in conversations. The conversation panel hides meanwhile.
- Exult's frame/quality usecode for books (`data/bg/shape_info.txt`, frame_usecode): books of quality 100-179 run 0x638 instead of 0x282 (`UsecodeMachine.GetItemFun`), e.g. the inn registers and THOU ART WHAT THEE EATS.
- An ABRT/THROW with pending text now waits for it to be clicked away before aborting, as Exult's `show_pending_text` does (it used to drop the text).
- Agent console: pages are logged as `BOOK book|scroll: line / line / ...`; `cont` turns pages, `close` stops reading. Verified on Christopher's note, ship deeds, the Honorable Hound register and three-spread books in Trinsic.

### Walking (current)

- Blocking is Exult's `Chunk_cache`: per chunk, tile and lift a count of solid objects with height (`Data/ChunkBlocking`), built per chunk on first use and updated as objects enter, leave or change shape. A wall counts in every chunk its footprint covers (before, walls reaching in from the next chunk east or south were missed). `is_blocked` is ported whole: a walker of its own height (humans 4 lifts) climbs at most one lift, drops at most one, needs headroom, and at lift 0 the terrain keeps walkers out of water and swimmers in it; the multi-tile-step variant serves big monsters. Actors use their type flags (walk, fly, swim, ethereal): npc.dat gets walk on a new game like Exult's `fix_first`, spawned monsters their monster info; saves from before these flags existed are given the same on load.
- Pathfinding is Exult's `Find_path` (`World/Pathfinder`) with `Actor_pathfinder_client`: 3D tiles, every step costs 3, lift changes and closed doors add, swamp doubles, BG cobblestone is cheaper; the open set is Exult's per-cost chains, newest first. There is no node limit: it gives up past a cost ceiling of 3× the estimate, at least three screens wide, so a walk of 200+ tiles is one search (e.g. Trinsic's east gate to 1100,2000). As in Exult, a start estimate of 512 or more (about 250 tiles) finds nothing, and the destination must be at the walker's lift.
- Walks follow Exult `Path_walking_actor_action` (`Actors/PathWalk`): one step per call at the walker's frame time; a blocked step stops the walker and is retried after 0.1-0.6 s up to 3 more times; a closed, unlocked door in the way is opened through its usecode (quality set to 0, as Exult does), walked past and closed behind (Exult's walker is stopped by then, so it hops the last tile past the door). Straight walks (`Zombie`) and A* walks share it. Walking frames follow Exult: the avatar cycles 1,2,1,2 and NPCs 1,0,2,0, diagonal steps use the east/west frames.
- Speeds are Exult's: milliseconds per step, with no run animation. The avatar: by the cursor's distance in a square around it (`Mouse::set_speed_cursor`) slow 400, medium 200, fast 100; in combat 266, with a hostile nearby no fast; keys fast, Shift medium (`Game/WalkSpeed`). Holding the left button on open ground or a key steers a straight walk 8 tiles ahead in that direction, sidestepping a blocked direction (`start_actor_alt`); a quick click walks an A* path to the tile (`start_actor_along_path`, Exult's double right-click); letting go stops. Idle for 2 s the avatar stands. NPCs: walking to the next schedule spot 200 ms after up to 5 s (A* legs, a straight walk when no path, placed there after 2 failures or 40 legs), loiter 400 ms straight walks within 12 tiles, wander 200 ms A* legs of up to 32 tiles, catching up with the party 100 ms. Formation followers step with the avatar.
- Agent console: `walk` uses the A* walk, `steer <dir> <sec> [ms]` holds a direction, `tile` lists the blocked lifts and where the avatar would stand.
