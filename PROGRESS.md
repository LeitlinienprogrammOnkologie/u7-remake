# u7-remake progress

Black Gate in Godot 4 C#, driven by original `u7/STATIC` data and Exult 1.12.1 as source of truth. Not a full game yet: walk Britannia, open gumps, talk to NPCs, watch the clock, hatch eggs, fight.

Last update: 2026-10-07.

## Status

| Area | State |
|---|---|
| Map (U7MAP / CHUNKS / IFIX / IREG) | Done |
| Walking (Exult blocking, A*, path following, speeds) | Done (actors block and step aside) |
| Usecode VM (BG bytecode) | Done (many intrinsics still stub) |
| Gumps (inventory, containers, stats, spellbook) | Done (BG paper doll, containers, weight/volume/stacks, spellbook; no SI PAPERDOL.VGA / save) |
| Magic | Done for the spellbook and casting (mana, level, reagents, spell usecode); 11 intrinsics of higher spells stubbed, no carried-light palettes |
| NPCs from `INITGAME.DAT` `npc.dat` | Done (291 used of 356; unused skipped) |
| Game clock + dusk/night modulate | Done (RGBA grade, not 8-bit palettes) |
| Schedules (`assets/data/schedules.csv`) | Done (all Black Gate's, duel included) |
| Eggs | Done (teleport, usecode, jukebox, button, monster) |
| Combat | Done (Exult's Combat_schedule for everyone, explosions; no arrest / spellbooks in combat) |
| Save / load | Done (quick slot, Exult GAMEDAT layout with timers, party order, restored schedules; no zip saves) |
| Music | Done (jukebox eggs → GM MIDI via Windows synth; no SFX/speech yet) |
| Party | Done (join/leave, formation, follow, teleport, combat) |
| Opening scene (moongate, Iolo, earthquake) | Done |
| Conversation panel | Done (portraits, paging, click to continue, answers) |
| Books and scrolls (`book_mode`) | Done (original gumps and font, Exult page layout) |
| Story (played through the agent console) | Trinsic chapter; Britain's first visit (Lord British, Batlin, the storeroom, the Orb) |
| Sprite effects (SPRITES.VGA) | Done (`sprite_effect`, `obj_sprite_effect`, notes, spell sparkles, explosions, lightning flashes; weather not drawn) |
| Barges (ships, carts, flying carpet) | Done (Exult's Barge_object: boarding by usecode, seats, sailing, flying, turning, landing, saved) |
| Intro movie | Not started |
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

With the environment variable `U7_AGENT=<dir>` set, `Game/U7Game.Agent.cs` reads one command per line from `<dir>/cmd.txt` and appends results to `<dir>/out.txt`, each ending in `DONE <n>`. Game time is frozen between commands, music is off, and conversation text and barks are logged. Commands: `look [r]`, `find <text>`, `npc <num|name>`, `state`, `inv [npc|id]`, `flags`, `timer [n] [hours-ago]`, `stubs`, `tile <x> <y> [z]`, `walk <x> <y>`, `walkto <id|npc:num>`, `steer <dir> <sec> [ms]`, `tp <x> <y> [z]`, `talk`/`use <id|npc>`, `take <id>`, `sail <x> <y>` (in barge mode: the barge's centre heads there, as with the button held, until it stops), `put <id> <container-id>` (a drag into a container, also off a ready spot), `book [page]` (the open spellbook's page, spells and bookmark; turns to a page) and `cast <spell>` (a double-click on a spell in it), `close`, `cont [n|all]`, `choose <answer|#n>`, `num <n>`, `click <id>` or `click <x> <y> [z]` (a tile, for `click_on_item`), `wait <sec>`, `hour <h>`, `save`/`load <slot>`, `shot <name>` (windowed only). Object ids come from `look`/`find` output. Helpers: `pwsh scripts/agent/restart.ps1 [-Load <slot>]` starts it headless (I/O in `agent_io/`), `python scripts/agent/agent.py "<cmd>" ...` sends commands and prints the results. The Trinsic murder chapter (opening, stables, Finnigan, Spark, the chest, Gilberto, Gargan, the report and map quiz, Johnson's gate) and Britain's first visit were played through this way.

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
- Combat: **C** (or a party member being hit) switches every member and the avatar between the combat and follow-avatar schedules like Exult `toggle_combat`; members fight by their attack mode (see Combat) and drop back to following after five failures to find a foe. A member that dies leaves the party (no dead-party list or bodies yet).
- Not yet: party items intrinsics, dead-party handling and resurrection, sleeping/paralysed members.

### Usecode scripts (current)

- `Usecode/UsecodeScript` ports Exult `Usecode_script` (ucsched.cc): the arrays handed to `execute_usecode_array` / `delayed_execute_usecode_array` (intrinsics 0x01/0x02) run against their object over time on 200 ms ticks: cont, reset, repeat/repeat2, delays (ticks, minutes, hours), wait_while_near/far, remove, rise/descend, frame and the NPC frame opcodes 0x61-0x70, next/prev frame, say, step (forced), face_dir, music, usecode/usecode2 calls, egg, set_egg, hit, resurrect. Speech, sfx, weather and attack are accepted but do nothing yet. Scripts pause while usecode runs or a conversation waits. They are saved with their object as Exult's IREG_SPECIAL/IREG_UCSCRIPT entries (`Usecode_script::save` layout) and restored on load.
- Avatar death runs the game's death usecode 0x60E (event 4) after combat is switched off and gumps close; with global flag 0x57 set it revives the party in the Fellowship shelter in Paws, otherwise it restarts the game; the intrinsics it needs are in: play_music, fade_palette (world fades to black), get_dead_party, resurrect (Exult `Actor::resurrect`: items back, corpse gone, full health, follow or loiter), remove_npc, halt_scheduled, restart_game (reloads the initial game).

### Save / load (current)

- `Game/SaveGame` writes Exult's GAMEDAT layout into `saves/<slot>/`: `U7IREG00..8F` (Exult `write_ireg` / `write_ireg_objects`: plain 10-byte, container 12-byte and egg 12/14-byte entries, extended entries for shape ≥ 1024 or frame ≥ 64, contents terminated by 01, chunks by 00 00), `NPC.DAT` (Exult `Actor::write` layout with the extended magic/mana bytes, readied items marked `02 <spot>`, and `next_schedule` for an NPC walking to its spot), `FLAGINIT` (usecode global flags), `GAMEWIN.DAT` (Exult `gamewin.dat`: day/hour/minute, music track, combat flag; older saves named it `GWIN.DAT` and still load), and `USECODE.DAT` / `USECODE.VAR` (`Usecode/UsecodeDat`, Exult `Usecode_internal::write`: the party in join order, the usecode timers, Exult's saved position; the statics file is empty, as Black Gate's usecode has no statics).
- Loading sets `U7Paths.GameDatOverride` to the slot and reloads the scene, so the normal loaders pick up the saved files; `NpcDat` reads a raw `NPC.DAT` when present (both the original and Exult's magic/mana layouts) and keeps schedule destinations, type flags and the object flags. The party is linked from `USECODE.DAT` in its saved order (Exult `link_party`), or from the in_party flags for saves without that file; the timers go back to the usecode machine.
- NPC schedules on load follow Exult `Actor::restore_schedule`: everyone keeps the place and schedule they were saved with (a loiterer loiters where it stands), an NPC that was walking to its next schedule's spot sets off again (or is put there when far off), and the schedule table takes over at the next period. A new game applies the table at once (Exult `schedule_npcs` after character creation).
- Spawned monsters are saved to `MONSNPCS.DAT` (count + `Actor::write` records) and restored on load. No `schedule.dat`: Exult falls back to the static `SCHEDULE.DAT`, and Black Gate's usecode cannot change schedules (the intrinsics that do, 0xa9 and up, are Exult's own). Spellbooks keep their spells and bookmark in Exult's 18-byte entry (`Spellbook_object::write_ireg`). Not saved: virtue-stone extra bytes. Exult saves are zip files; a `saves/<slot>/` directory with the same files loads, the zip itself does not yet.

### Music (current)


- `Audio/MusicPlayer` sequences the MT-32 export in `assets/audio/music_mt32/NNNN_MT32MUS.MID` (track = jukebox egg `data1 & 0xff`), converting it at load exactly like Exult's `XMIDIFILE_CONVERT_MT32_TO_GM` (`mt32asgm` patch table, no patch changes on channel 10, bank selects dropped, volume curve, default CC7 90), on a background thread and sends it to the Windows MIDI mapper (built-in GS wavetable synth) with `winmm`, like Exult's Windows MIDI driver. Non-Windows is silent for now.
- Jukebox semantics follow Exult `Jukebox_egg` / `MyMidiPlayer`: continuous eggs hold a repeat count; leaving the last one lets the track finish and stops the repeat. **M** mutes.
- No SFX (the originals are AdLib/MT-32 data, Exult uses its own digital pack) and no speech playback yet, although speech is extracted as WAV.

### Combat (current)

- Monster eggs hatch via Exult `Monster_egg` packing (`sched`/`align`/`cnt`, shape from d2/d3). NPC-class or `monsters.csv` shapes spawn; others drop a takeable temporary item.
- Spawned stats from `assets/data/monsters.csv` (`Randomize_initial_stat`); health = strength; alignment from the egg unless the egg is neutral.
- Hostility: good↔evil/chaotic, evil↔good/chaotic, chaotic↔evil/good; **neutral never initiates**. Avatar is good.
- Fighting is Exult's `Combat_schedule` (`Actors/Schedules/CombatSchedule`), run like any schedule for monsters (each spawned or saved monster has a schedule brain; an egg's schedule 0 is combat, others loiter, wander, ...), for NPCs pulled into a fight, for party members in combat mode, and for the avatar: Exult runs the avatar's schedule whenever the player isn't walking it, so in combat mode it goes for foes by itself, and a walk of the player's replaces what the schedule was doing.
- A fighter picks a foe among those near the screen (Exult `get_nearby_npcs`, the avatar included) by its attack mode: nearest (preferring foes nobody else attacks, not fleeing), weakest, strongest, random, berserk, protect (attackers of the party member under protection, "On my way!"), defend, flank; manual leaves it to the player. Party members also take on whoever attacks the party or is attacked by it. Monsters get their mode from their kind of fighter at birth (Exult `monster_modes`). It walks within reach with Exult's monster pathfinder (`Approach_actor_action`, stopping when blocked), strikes once 30 dexterity points have built up (Exult `dex_to_attack`), playing the weapon's attack frames (reach, raise, fast or slow swing, one- or two-handed), then the blow lands; with a ranged weapon in range it shoots; blocked from striking, it tries to get adjacent and steps aside. Battle cries and taunts ("To Battle!", "Take this!"). Out of ammunition it swaps to the weapon on its belt or back (Exult `Swap_weapons`), else fights bare-handed.
- It flees when its attack mode says so or below 3 health (unless berserk or it can't die), screaming the first time. After five failures to find or reach a foe it gives up: party members follow again, good NPCs go back to the schedule of the hour (or the one they had), others wander a little. Mages, liches, ghosts and dragons (Exult's `actor_flags`) teleport near their target, summon (the spell's usecode) and turn invisible, with Exult's sprites (stars where they land, sparkles as they vanish); the sounds are not played.
- Getting hit (Exult `fight_back`): a party member brings the party into combat mode; an NPC without a target takes on its attacker. Exult `set_target` keeps each fighter's target and the numbered NPC attacking it (`oppressor`). The hit actor whose weapon reaches farther steps back a tile (`back_off`).
- Double-click in combat mode attacks anything but party members and bodies (unlocked doors and containers open): the avatar targets it and everyone goes into combat (Exult `double_clicked`). Outside combat mode a double-click uses the thing as usual. The agent console's `use` on an enemy still attacks straight away, and `arena` / `combat [off]` spawn the F3 rats and toggle combat.
- Duel (Exult `Duel_schedule`): the combat schedule play-fighting without damage or music, at an archery target with a bow and a couple of arrows (arrows stick in it, frame by frame), at a fencing dummy or against another duelist with a two-handed sword, breaking off every eighth blow. Verified in Jhelom's fighting school.
- Battle music (Exult `start_battle` / `monster_died`): when the avatar's fight starts and none played in the last 30 s, track 11 or 12; when no hostile is left nearby, victory (15) or, after a long hard fight, battle-over (9); leaving combat with foes close by, running away (16).
- Ranged and thrown weapons (Exult `Combat_schedule::attack_target` + `Projectile_effect`): a bow, crossbow, musket, wand or thrown axe fires a projectile when the target is beyond melee reach (or always, for `uses = ranged`). Ammunition comes from the quiver or any bag (`find_weapon_ammo`), charges from the wand's quality, thrown weapons consume themselves; the missile sprite flies along a straight line at the weapon's missile speed with Exult's 16-direction frames and rotation, then rolls to hit with the ranged bonus (+6, minus distance for thrown), applies weapon plus ammo damage with the ammo's damage type, drops the ammo by its drop rule, and returning weapons come straight back into the thrower's hands. Archers do not keep their distance (Exult has no such behaviour outside flee mode): they shoot from where they stand and only walk when the target is out of range. `assets/data/ammo.csv` is loaded as `Actors/AmmoTable`. Homing missiles and attacks on tiles are not ported.
- Explosions (Exult `Explosion_effect`, `CombatEngine.Explode`): the explosion sprite of the weapon or missile (Exult's BG list: the powder keg's 4, the burst arrow's 19, ...; else 5) plays where it goes off; a quarter of the way through, the exploding object is removed and everything within half the sprite's width (party included, as in Exult) is attacked with the weapon, the attacker answering for it (the avatar if none). They come from a powder keg used as a weapon (it blows up the target instead of hitting it, e.g. Rudyom's Wand on blackrock), exploding missiles where they land, and exploding weapons (`figure_hit_points`). Their sound, powder kegs set off by other explosions (breakable objects) and homing missiles are not ported.
- Unarmed damage is 1 plus the strength roll; the MONSTERS.DAT weapon byte is not used.
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
- Homing missiles, attacks on tiles, blood, arrest
- Party: dead-party list, party items intrinsics, sleeping/paralysed members, attack modes other than nearest
- Exult's arrest schedule and scripted (0x80+) schedules, `im_dormant`; in combat: spellbooks, powder kegs set off by explosions (breakable objects), guards called by witnesses (`fight_back`'s bully code), invisible actors not drawn, combat difficulty and pausing
- Hunger, poison, weather, dungeon lights
- True 8-bit palette cycling (world PNGs are day-baked RGBA)
- Intro / endgame, SFX, speech playback, music on non-Windows
- SI paperdolls (`PAPERDOL.VGA`), save/load gumps
- Magic: the spellbook's keys (Home/End, PgUp/PgDn, arrows), the casting frames in the caster's hand (shape 859), casting from a readied spellbook in combat, the palettes of carried and lit light sources (`set_light`, `set_time_palette`: candle, single and many lights), drawing the weather (rain, snow, fog, clouds)
- Save: virtue-stone bytes, Exult zip saves
- Many BG intrinsics still log `stub UI_*` and return 0
- Signs (`display_runes`) show in the conversation panel, not Exult's `Sign_gump`
- Walking: the follow schedule's blocked check, Exult's speed cursor arrows, walking with the right button, `Walk_to_schedule`'s off-screen legs and dormant NPCs, the Onecoord and Offscreen pathfinder clients

## Next milestone

Remaining BG intrinsics and full schedule classes. Intro, barges and arrest can wait.

`python scripts/usecode_stub_report.py` ranks the stubbed intrinsics by static reachability (Trinsic NPCs by default, `--npcs` for others, plus all of USECODE). In game, every stub hit is counted and written to `stub_report.txt` (repo root, untracked) on exit. Nothing reachable from the Trinsic NPCs or the Britain NPCs (Lord British's castle and town, NPCs 23-73, `--npcs 23,24,...`) is stubbed any more; 32 intrinsics remain game-wide (top: `flash_mouse`, `is_readied`, `set_attack_mode`, `kill_npc`, `set_oppressor`); from the 72 spells' usecode 11 are left (`summon`, `wizard_eye`, `display_map`, `clone`, `kill_npc`, `armageddon`, `stop_time`, ...). `flash_mouse` waits for Exult's mouse cursors (POINTERS.SHP), which the game does not draw yet.

### Opening scene (current)

- A new game reads IREG, FLAGINIT and the rest from `INITGAME.DAT` entries (`U7Paths.ReadGameDat`); saves read `saves/<slot>/`. `u7/GAMEDAT` is not used: the GOG copy is a game already past the opening (flags 0x14, 0x3b, 0x4c, 0x5a, 0x5c set, the Trinsic superchunks 64/65/70/71 changed).
- Exult `Game_window::read`: while global flag 0x3b (`did_first_scene`) is clear, the avatar gets object flag 16 (BG `dont_move` + `bg_dont_render`): not drawn, no walking, double-clicks, drags or game keys (debug, save, music and zoom still work), followers wait (Exult `main_actor_dont_move`).
- The scene itself is the game's usecode: the cached-in egg (0x06C2) calls Iolo's 0x0401 with the egg event (music 35, barks), 0x06AA creates and animates the red moongate, 0x0618 clears flag 16, Iolo greets the Avatar, Petre interrupts, 0x08DD makes the NPCs react and shakes the screen (Forge of Virtue earthquake), Iolo joins, 0x06FA remarks on the tremor. Verified headless end to end.
- Usecode control (Exult `Actor::in_usecode_control`: flag 16/22 or a started, halting script): the player cannot walk the avatar and NPC schedules wait, so script steps and frames are not overwritten. The avatar returns to its standing frame once when a walk or swing ends (Exult `Actor::stop`), facing the way it already faces, instead of every idle frame.
- Barks (`item_say`, script `say`) follow Exult `Text_effect` (one per speaker, '@' shown as '"', drawn after the map) but in screen space: `UI/BarkOverlay` draws MedievalSharp in yellow with a dark outline, sized with the zoom, centred above the speaker's sprite and kept on screen. `WorldView.Barks` supplies the anchors.

### Barges (current)

- `World/Barge` ports Exult's `Barge_object`: an invisible footprint (shape 961, hot spot at the lower right, size and facing from its IREG entry) whose parts (hull, decks, sails, seats, the carpet's pieces) and passengers are gathered from the footprint (its lift up to five above, its own parts from one below; no eggs) and moved together (`GameMap.MoveGroup`). Under way it makes two steps per move after the first, straight at its target (Exult's Zombie path), turning first to face that way: round its centre, each part on its rotated frame (seats by facing, barge parts by Exult's rule, others reflected), a quarter turn only if the new footprint is clear. A ship on water swims, a cart walks, a carpet in the air levitates; nothing rises or drops. Cart wheels turn and draft horses trot. Eggs fire for the avatar as it goes.
- Barge mode (Exult `moving_barge`, `World/Barges`): usecode starts and stops it by setting or clearing flag 10 or 26 on a part, asks it with those flags, asks flag 21 whether the carpet may land (`okay_to_land`: nothing below, no water), and flag 20 marks the sailor. `get_barge`, `on_barge` (the whole party in the footprint) and `sit_down` (the sit schedule on that seat) are ported; once the whole party sits on a barge's seats, the sit schedule calls the barge usecode 0x634 (Exult). In barge mode the player's walking steers the barge instead: holding the button or a direction key heads its centre for that spot at twice walking speed, letting go stops it. Leaving barge mode furls a ship's sails (Exult `done`); combat ends it. Moving a barge object by usecode (`set_lift`, `move_object`) takes its parts along. Saved in Exult's 12-byte entry with the barge-mode bit, restored on load.
- IREG: the entries after a barge in the originals are its "contents", which Exult puts in the world; they used to be dropped, and with them the flying carpet, most ships' hulls, holds and cargo, the carts and the Skara Brae ferry. Saves made before lack them: `python scripts/repair_barges.py <slot>` adds them from INITGAME.DAT (done for `britain0`-`britain3`, `cove0`, `minoc0`, `minoc1`).
- Exult `Main_actor::get_followers`: when the player sets the avatar walking, party members not fighting, waiting or loitering follow again, so those seated on a barge or chair get up.
- Verified: the flying carpet at 630,1052 (double-click a piece: the party sits, it rises to lift 9, flies east and south, lands, everyone gets up and follows); the Golden Ankh at Vesper (2192,1316) with Lord British's deed: the sails go up, the party sits, it sails south, turns west and east (blocked by sea rocks where they are), the barge mode survives a save and load (`ship0`), and the sails come down again.

### Spellbook and magic (current)

- `Gumps/SpellbookGump` ports Exult's `Spellbook_gump` in the original GUMPS.VGA art: a page per circle (the linear spells first), each spell's rune button (shapes 33-40, frame = circle) where the book has it, how many casts the reagents of the book's holder allow (font 5; none on the linear page), the circle's name from TEXT.FLX ("First" ... "Circle"), the page corners, the red bookmark ribbon beside the marked spell (or at the edge towards its page), and the turning-page animation (painted over the new page; Exult swaps the page halfway and blocks the game meanwhile). It opens on the bookmarked page when the book is double-clicked, in the world or in a gump (Exult `Spellbook_object::activate`). A click marks a spell, a double-click casts it, the checkmark closes it.
- `Actors/Spellbook` ports `Spellbook_object`: Black Gate's reagent table, `can_do_spell` (the spell in the book, mana and level at least the circle, one of each reagent, shape 842 by frame) and `do_spell` (mana and reagents paid, then the spell's usecode 0x640 + spell as a double-click on the caster, all gumps closed so its animation plays). Casting is the avatar's, whoever carries the book. The casting frames in the hand are not shown.
- Spell intrinsics: `cause_light` (Exult `add_special_light`: a light spell until a game minute, saved in GAMEWIN.DAT; where Exult switches the whole screen to PALETTE_SPELL at dusk and night, `Rendering/LightSpellOverlay` draws a glow round the avatar instead, on the user's wish: a soft circle of 7 tiles that breathes, ripples at its rim and has a faint blue band there, while outside it stays night; it fades in and out over about a second and follows the avatar), `lightning` (Exult `Lightning_effect`, a weather effect: a 25-50 ms flash, then another every 4-7 s, now and then sooner; Exult passes 1000 for a usecode flash, which the weather effect counts in game minutes, so it keeps flashing now and then; the flash is the world at full light with a blue-white wash added, PALETTES.FLX entry 10 against day; no thunder), `get_weather` / `set_weather` and script opcode 0x5A (Exult `Egg_object::set_weather`: which weather until when; the weather is not drawn), `set_to_attack` with script opcode 0x7A (`usecode_attack`: the actor attacks the target usecode set, an object; tiles are not ported) and `telekenesis` (a script's next call of that function is a double-click). Weapons now run their usecode on what they hit (Exult `figure_hit_points`, e.g. the douse, ignite and telekinesis spell weapons).
- Verified from `britain3`: the castle book opens on the bookmark's page (linear spells, then "First Circle" with Cure and Light); Bet Lor lights the night; An Flam sends its missile at a lit candle (338), which goes out (336), and In Flam lights it again. Circle spells need reagents, which the avatar has none of yet.

### Minoc (current)

- From `cove0`: Elynor (`npc 81`, at the sawmill) speaks of the murders of Frederico and Tania, the gypsies camped southeast of town, and sends you to Owen about the Crown Jewel. Asked about her job a second time (the first answer sets flag 0x11F) she remembers Batlin's message; "deliver", "No" (unopened) and she takes the sealed box (`find_object` in the party, quality 1) and pays 50 gold. `minoc0` is saved after that.
- The sawmill murders (`minoc1`): the victims are scenery (shape 867, no usecode) among blood; the door egg (0x6AB) sets flag 0x122 and releases Owen, Rutherford, Xanthia and Zorn from their npc.dat "wait" schedule to loiter (Exult's `schedule_npcs` leaves waiting NPCs alone, so a new game finds Owen at 1355,455 until then). William found the bodies; Burnside and Gregor on the monument; Jergi, Sasha and Margareta at the gypsy camp southeast of town (her paid fortune names Moonglow's observatory, the Time Lord, the Wisps of Yew and Empath Abbey); Owen: the Crown Jewel left for Paws, a man with a hook was in town; Rutherford: Hook is a pirate of Buccaneer's Den. No stubs were hit.

### Cove (current)

- Played from `britain3`: Rudyom (`npc 74`, in his lab) on blackrock, his experiments, the notebook and the transmuter; the notebook is the book of quality 3 on the table at 1618,1256 (usecode 0x282); Rudyom's Wand (shape 771) lies at 1623,1245. `cove0` is saved with the wand in hand.
- The wand (usecode 0x303) on a blackrock: the rock is set to vanish, the avatar attacks it with a powder keg (it explodes, sprite 4) and lightning flashes. From three tiles away the blast killed Iolo and Spark and hurt the avatar, as Rudyom warns; from seven it does no harm.

### Britain (current)

- Played from `britain0`: `quest4` with the stray npc.dat flags cleared (the avatar's invisible, might, immunities and cant_die; Spark's temporary, bg_dont_move, tournament and others) and the spellbooks restored from INITGAME, saved as a new slot. `britain1` is after Lord British, `britain2` after Batlin, `britain3` in the storeroom with the spellbook.
- Lord British: every topic, the murder thread (Trinsic, a murder, killer, Hook, Crown Jewel; flags 0x40/0x43 from Trinsic), healing (0x8B4/0x91D), the Orb of the Moons, the ship's deed and the crystal. Clint (the Crown Jewel has not been to Britain) and Patterson (Finster's murder).
- Batlin: the Triad, the Fellowship examination (eight questions) and the sealed box for Elynor in Minoc (`find_object` in his inventory). Wearing Christopher's medallion makes him refuse to talk, as in the original; `put` it into the backpack first.
- The storeroom: key 236 from the nightstand at 897,1170 unlocks and opens the door at 890,1132 (lift 5); the spellbook there has its spells (see Spellbook and magic).
- The Orb of the Moons (0x0311): a moongate on a free spot by the click (`is_not_blocked`), the avatar walks in (`path_run_usecode`), and one time in three the gate hurts and throws the avatar back (0x0824); otherwise the party arrives at the moongate in the gate's direction (Trinsic's for one to the north).

### Conversation panel (current)

- `UI/ConversationPanel` replaces the bare label/buttons: dark wood with gold trim along the bottom, the speaker's portrait (from `FACES.VGA`; the extracted face PNGs are 8x8 tiles), their name once met before this conversation, MedievalSharp text, a pulsing continue marker, answers as numbered buttons (1–9 keys work), the number prompt, and a smaller portrait for a second speaker. The Guardian's large face gets red text like Exult's font 7.
- Paging follows Exult `say_string` / `show_npc_message`: the VM splits a say at '~' ("~~" counts once) and waits for a click after every piece (`UsecodeWait.ClickToContinue`, `ContinueText`); a leading '*' is an extra click, a '*' inside a piece starts a new page, '^' capitalises, and text too long for the box is paged in the panel. Clicks anywhere, Space or Enter continue. `display_runes` waits for a click like Exult's modal sign.
- Fonts: MedievalSharp (`godot/fonts/`, SIL OFL) for the panel and barks; gumps still use the original `FONTS.VGA`.

### Usecode items (current)

- `Actors/ItemQuantity` ports Exult's quantity container code (`add_quantity`, `create_quantity`, `remove_quantity`, `modify_quantity`, `count_objects`): stacks fill to 100 before new objects are made, carry weight limits what is added, quantity shapes (coins, arrows, bolts, lockpicks) switch to their pile frames, locked containers (522, 798) refuse.
- `count_objects` sums stack quantities (it counted objects before, so 50 gold read as 1) and counts the whole party for -357. `remove_party_items` / `add_party_items` work across the party like Exult; BG returns the receiving members and drops nothing on the ground.
- `create_new_object` / `set_last_created` / `update_last_created` / `give_last_created` keep Exult's last_created stack. Monster shapes become neutral wait-schedule monsters that join the monster AI once placed.
- `find_object` (Exult `UI_find_object`): the first match inside an object (`Container_game_object::find_item`, depth first), in the party (-357), on the screen (-359) or within a tile of a position (`find_nearby`, which now also filters by quality and frame). Batlin looks for the sealed box with it; the spell sellers for the party's spellbook.
- `add_spell` (Exult `Spellbook_object::add_spell`): sets the spell's bit in the book's nine circles, 0 if it was there. Spellbooks (shape class 8) read their circles, lift and bookmark from the 18-byte IREG entry; before, the first spell byte was taken for the lift (the castle's book at 885,1127 stood at lift 15), and saves made then lost the spells. Verified: Nystul sells Light for 35 gold and refuses to sell it twice.
- `is_not_blocked` (Exult `UI_is_not_blocked`): `Map_chunk::is_blocked` over the shape's footprint for walkers and swimmers; true only if it would stand at that very lift.
- `path_run_usecode` / `set_path_failure` (Exult `Usecode_internal::path_run_usecode`, Black Gate's form): the avatar gets an `If_else_path_actor_action` (`Actors/ActorActions`: an A* walk giving up after 6 blocked tries, then a `Usecode_actor_action` running the function on the item with the event, or the failure one) on its own brain, with the party following its steps; without an item it just walks. `set_path_failure` gives the last walker its failure usecode, which also runs when no path was found. Usecode control no longer zeroes the avatar's frame time when the player is not walking it (it made such walks give up as "not moving" once the avatar's script ended).
- `input_numeric_value` waits on a number box + OK in the answer column; `earthquake` jolts the camera ±4 px every 100 ms; `wearing_fellowship` checks the medallion (955 frame 1) on the neck; `get_timer`/`set_timer` count game hours (saved in `USECODE.DAT`); `reset_conv_face`; both sound-effect intrinsics are silent no-ops.

### Sprite effects (current)

- `World/EffectsManager` ports Exult's `Sprites_effect`: an animation from SPRITES.VGA at a tile or following an object, a frame every standard delay (200 ms), through its frames once or for a number of repetitions, moved by its per-frame offsets. Like Exult's time queue it stands still while usecode waits for a click and in gump mode.
- `WorldView` paints them after the map (Exult `Game_render::paint`): the frame's hotspot at the tile's corner, raised by half the lift in whole tiles. SPRITES.VGA is decoded from the original file with translucency: palette colours 0xEE-0xFE use Exult's blend colours and alpha (its `hard_blends`, which approximate XFORM.TBL; checked against the tables), as Exult always paints sprites translucent.
- Uses: `sprite_effect` (0x53) and `obj_sprite_effect` (0x7B); `play_music` with a player shows the rising notes (sprite 24) over it, as for the instruments in Lord British's castle; combat's teleport (stars, 7) and invisibility (12). Lord British's own stars (0x0417, the reward after the Forge of Virtue) use the same call. Not ported: explosions, weather, the touch-screen walk marker.
- Agent console: the status line lists active sprites (`sprites 24 frame 3/8 at x,y,z`). Verified with the harpsichord at 1043,1154 and, temporarily, sprites 3, 7 and 15 over the avatar in a screenshot.

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
- Actors block like everything else (Exult counts every solid shape with height in its chunk cache; NPC and monster shapes are solid). A step onto a tile an actor stands in follows Exult `Actor::is_really_blocked`: the actor in the way steps aside for the walker (`move_aside`: to one side, else diagonally, else they swap places, `swap_positions`), unless it is walking itself, fighting, sitting, kneeling or asleep. The avatar's steering does the same when the way ahead and both sidesteps are blocked (`start_actor_alt`). Persistent walks (Exult's for the avatar and party, e.g. walking to a chair through `set_action_sequence`) plan through NPCs on their feet (`ignore_npcs`) and, blocked by one, plan again up to 30 times. A walker's own tile counts as blocked, as in Exult, so `find_spot` around an NPC finds the tiles beside it.
- Agent console: `walk` uses the A* walk, `steer <dir> <sec> [ms]` holds a direction, `tile` lists the blocked lifts and where the avatar would stand.

### NPC proximity (current)

- Exult runs an NPC's "near" usecode (event 0, `npc_proximity`) from its schedule (`Schedule::try_proximity_usecode`): queued as a no-halt script (`usecode2 <npc function> 0`), after which the NPC looks again in 0.5-1.5 s. Black Gate's NPC functions mostly call 0x92E, which picks a remark for the schedule ("Looks like rain...", "Try the wine.", Fellowship slogans, "Tag! Thou art it!") and barks it. Loitering NPCs do this one time in 12 (verified: Ellen in Trinsic); tend shop (1 in 8), dance, hound, sew and bake will once those schedules are ported.
- Exult `Npc_proximity_handler` (`ScheduleRunner.ProximityCheck`): NPCs on or near the screen are looked at every 4-12 s (0-4 s if hostile). A sleeper within 6 tiles of the avatar with a clear straight line between them (`Fast_pathfinder_client::is_straight_path`) wakes one time in three: it gets out of bed but stays in its sleep schedule, says one of the game's lines (TEXT.FLX messages 0x95-0x9a, "Who goes there?", "I am trying to sleep!", via `Data/TextMessages`, Exult `get_text_msg`), and lies down again 10 s later. Ghosts, Horace and Penumbra sleep on (Exult `Bg_dont_wake`); animals that cannot speak (`cant_yell`) wake silently.

### Schedules (current)

- Each scheduled NPC has an `Actors/NpcBrain` holding an Exult-style `Schedule` object (`Actors/Schedules/`, one class per Exult schedule, created as in Exult `Actor::set_schedule_type`) and its current action (`IActorAction`, Exult `Actor_action`). `ScheduleRunner` ticks the action by the delays it returns and calls the schedule's `NowWhat` when the NPC is idle (Exult `Npc_actor::handle_event`), `Ending` when it changes schedule, applies the schedule table at each period, and restores saved schedules.
- Actions (`Actors/ActorActions`, `Actors/PathWalk`): walks (A* and straight), frames, move/teleport, sequences, and Exult's `create_action_sequence` (walk there, then do something), `Sit_actor_action` (bow, sit; complains if the chair was moved, Exult's own "Who moved my chair??").
- Ported schedules: sleep (beds), loiter (and tend shop: a 3-tile loiter that remarks one time in 8), wander, pace, walk-to-schedule, follow avatar, sit (nearest free reachable chair, shapes 873/292), eat at inn (sit, finish food within reach, "Mmmm, tasty!", "Barkeeper!" from the game's TEXT.FLX), eat (sit, put a random food on the plate in front, eat it), desk work, waiter, and all the others below except duel. Stand, wait and combat have no schedule of their own.
- On a change of schedule the hands are emptied (into the belt or backpack: dance, tend shop, eat, sit, shy, thief, waiter, kid games, eat at inn, desk work, sleep outside the party) or the best weapon readied (pacing, hound, preach, patrol), as in Exult `set_schedule_type`.
- Street maintenance (Exult `try_street_maintenance`, `Street_maintenance_schedule`): from loiter (1 in 3), wander (1 in 2), desk work (1 in 3), a waiter at the counter (1 in 4) and patrol markers, at most every 30 s for an NPC near the screen: by day (9-18) shutters are opened and lamps put out, at night (18-6) shutters closed and lamps lit, and spent candles replaced, through the object's usecode, with a remark ("This need not be on.", "Who left these open?"); then back to the schedule of the hour from where it left. Verified: Ellen in Trinsic puts out a lamp at 16:00.
- Patrol (Exult `Patrol_schedule`): from path marker to path marker (shape 607, frame = number, within 25 tiles; out to the last and back), doing what each marker's quality says: wrap, pause, sit or read a book, kneel, loiter, turn about, pace, reverse or skip at random, hammer (a hammer fetched or made), tend lamps and shutters, run the NPC's usecode, bow, ready or put away the weapon, swing it; flag 32 fights any foe in sight (`seek_foes`), 64 repeats the marker. Without markers the NPC loiters and fights. Verified: Klog in Trinsic walks his markers, sits, loiters.
- Pace (Exult `Pace_schedule`): back and forth east-west or north-south from its spot, turning about at an obstacle; an actor in the way is told "Step aside!" and asked to move. Verified: Johnson at Trinsic's gate.
- Talk (Exult `Talk_schedule`): Black Gate's usecode (and Iriale's timetable) sets it for an NPC who wants a word: it comes up to the avatar (`Approach_actor_action`), calling "I would have words with thee." if it could reach it (`Fast_pathfinder_client::is_grabable`), and within 5 tiles faces it and starts its conversation. Verified with Iriale.
- Preach (Fellowship podium 697: exhorting with gestures, the seated flock standing to answer "Yea, verily!", a word with one of them, praying at the icon 724; verified with Klog at 21:00), kid games (chasing other children, a quick search; verified in Britain), dance (spins, arm flaps, punches; Chuckles), graze, hound (dogging the avatar), shy (keeping away, the Monster pathfinder client), thief (lifting gold next to the avatar every 8-16 s).
- Crafts: farming (scythe, cutting crops 423 and growing the far ones; Brownie in Britain), mining (pick, ore 915/916 breaking down, gold nuggets and gems turning up), the lab (cauldron, reading, potions on the tables; Rudyom), sewing (bale, spinning wheel, loom, cloth cut and sewn into tops and pants, displayed; Gaye), baking (flour, dough kneaded on the table, oven, bread on the display table; Willy), the forge (blank on the firepit, bellows, anvil and hammer, trough; Menion). Tools come from Exult `Actor::get_attack_frames`, now ported whole (reach, raise, fast or slow swing, one- or two-handed, from the weapon's `actor_frames`); combat strikes use it too.
- Objects in IREG start okay-to-take and container contents inherit their container's flags (Exult `read_ireg_objects`); the temporary flag of 10-byte entries is read back.
- Exult `Schedule_with_objects` (`ScheduleWithObjects`): items made for the NPC's work are removed with the schedule if it still carries them; it walks to a random item of its kind, and puts items down on a table at the edge nearest it (`drop_item`, with the shape `find_spot` on the table top). `Pickup_actor_action` (`PickupAction`) faces the item, bows or reaches up, and takes it, deletes it, or puts it down (optionally temporary); `Face_pos_actor_action` turns to a spot.
- Desk work (Exult `Desk_schedule`): sits at the nearest desk (283, 407) on the chair closest to it; now and then gets up to put desk things (shape 675: quills, documents, inkwells, wax, seals, made for it as needed) on a table, to fetch one lying about, or to stand a moment.
- Waiter (Exult `Waiter_schedule`): sorts the tables within 24 tiles into eating tables (chairs within 3), counters (847) and prep tables (tables, stoves, the cauldron; within 26, 36 or 50); takes the orders of NPCs eating at the inn within 32 tiles, up to four ("What wilt thou have?", "Try the ale.", TEXT.FLX 0x1b-0x1f; to those already eating, Exult's own banter), setting a small temporary plate on the table in front of those without one; cooks at the prep tables (drops a cup or pot there, or works at it with ready/raise frames from a usecode script, stirring the cauldron or stoking the stove); brings each their food in the left hand and sets it on their plate ("Enjoy!", "Specialty of the house!"); waits at the counter, clears plates it set once nobody sits by them, and moves cups, bottles and pots about. Its hands are emptied when the schedule ends. Street maintenance (lamps, shutters) is not ported.
- Schedules run Exult `Usecode_script`s on their NPC through `ScheduleRunner.Script` (face, ready, raise and standing frames with tick delays).
- npc.dat flags follow Exult `Actor::read` bit for bit: the first flag word (asleep, charmed, cursed, in party, paralyzed, poisoned, protection, on a moving barge; dead and temporary only from saves), read/tournament/polymorph in the intelligence byte, Petra in the combat byte, met/no spell casting/zombie, and dead for an NPC below -1/3 of its strength. Exult's two 4-byte flag words are read from saves only: in INITGAME they are garbage. (Before, the garbage was read as flags, so saves made from a new game before this fix carry stray flags, e.g. in `quest4` Spark has bg_dont_move and never walks on its own, and the avatar counts as invisible, so NPCs coming to talk wait; start a new game for clean ones.)
- Verified headless in Trinsic: Finnigan sits at his desk, Dell calls "Buy something!" in her shop, Ellen sits down to eat at noon with food on her plate, the inn fills at 18:00 with diners calling for food and ale. Gargan works at his desk (sits, gets up, puts desk things down). Apollonia waits at the Honorable Hound: from 6:00 she cooks at the table and stove and waits at the counter; at 18:00 she takes four orders standing beside each diner, sets a plate, cooks, and serves all four, the food landing on their plates.
