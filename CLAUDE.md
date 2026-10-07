# Instructions for AI agents working on u7-remake

Ultima VII: The Black Gate remade in Godot 4.7 / C# (.NET 8), driven by the
original game data and ported from Exult 1.12.1. Read this file first, then
[PROGRESS.md](PROGRESS.md) (the status of record: what works, what is missing,
how each system maps to Exult) and [README.md](README.md) (data setup).

## Ground rules

- **Exult is the source of truth.** The Exult 1.12.1 source lives in
  `exult/exult-1.12.1/` (untracked). Before implementing any behaviour, find
  the Exult code for it (`usecode/intrinsics.cc`, `usecode/ucinternal.cc`,
  `usecode/ucsched.cc`, `actors.cc`, `schedule.cc`, `objs/*.cc`, `gamewin.cc`,
  `effects.cc`, `data/bg/*.txt`) and port it. Do not invent behaviour or guess
  at original mechanics; when Exult and memory disagree, Exult wins.
- **Name the Exult origin** in doc comments, the way the code already does:
  `/// Exult <c>Usecode_internal::add_party_items</c>: ...`.
- **The original files decide data questions.** `u7/STATIC/` holds the GOG
  game files (untracked). Check facts against them (USECODE, INITGAME.DAT,
  FACES.VGA, ...) with small Python scripts rather than assuming.
- **Never commit** `u7/`, `exult/`, `assets/`, `saves/`, `modern fonts/`,
  `agent_io/` or `stub_report.txt` (copyright or local state; all gitignored).
  Fonts in `godot/fonts/` must be SIL OFL with their licence file.
- **Keep the build clean:** `dotnet build godot/U7.csproj` must end with
  0 warnings and 0 errors.
- **Match the surrounding code:** file-scoped namespaces, braces on their own
  lines, `var`, small focused classes, and sparse comments that explain *why*
  or the Exult origin, not what the next line does.
- **Update PROGRESS.md** with every feature: the status table, the
  "(current)" section of the system, and "Not done".
- **Commits:** only when the user asks. The user commits straight to `main`.
  End commit messages with
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` (or your own
  model's attribution line).
- **Ask about taste, decide engineering.** Visual or aesthetic choices (fonts,
  UI look) go to the user, ideally with rendered options. The user picked
  MedievalSharp and the "dark wood & gold" conversation panel, and accepted
  (for now) books and scrolls in the original GUMPS.VGA art and FONTS.VGA font 4,
  scaled to fit the window. The light spell is drawn as an animated glow round
  the avatar (the user's idea), not Exult's whole-screen palette; its rules
  stay Exult's. Engineering
  questions with an Exult answer don't need asking.

## Layout

```
godot/scripts/
  Game/       U7Game (scene root, input, wiring), U7Game.Agent (agent console),
              AvatarController (player walking), SaveGame (Exult GAMEDAT layout)
  Data/       GameMap (chunks, IREG, eggs index, paint order), U7Object,
              ShapeCatalog, FlexFile, VgaShapeFile (RLE decode for GUMPS/FONTS/FACES.VGA),
              VgaFont (FONTS.VGA metrics, Exult paint_text_box), ChunkBlocking
              (Exult Chunk_cache blocked flags + is_blocked)
  Gumps/      GumpManager, GumpView (paints gumps and the open book), container,
              actor and stats gumps, TextGump (Exult Book/Scroll_gump page layout)
  Usecode/    UsecodeMachine (VM, waits, scripts), BgIntrinsics (intrinsic table),
              UsecodeScript (execute_usecode_array), Conversation (faces, answers, text)
  Actors/     NpcDat, ScheduleRunner (schedule ticking, slots, proximity),
              Schedules/ (one class per Exult schedule), ActorActions (Exult
              Actor_action kinds), PathWalk (Path_walking_actor_action),
              CombatEngine, PartyManager, Equipment, Inventory, ItemQuantity,
              ActorWalker (steps, Actor::is_blocked), tables
  World/      GameClock, EggHatcher, Pathfinder (Exult Find_path + clients),
              EffectsManager (Exult Sprites_effect, SPRITES.VGA animations)
  Rendering/  WorldView (map painting, sprites, bark anchors), ShapeCache
  UI/         ConversationPanel, BarkOverlay, UiTheme
  Core/       U7Paths (data paths, ReadGameDat), U7Constants, TileCoord
scripts/      extract_assets.py, usecode_stub_report.py, agent/ (console helpers)
```

## Building and testing

1. `dotnet build godot/U7.csproj`
2. Play-test headless through the **agent console** (see PROGRESS.md,
   "Agent console"). This is how features are verified here:

   ```
   pwsh scripts/agent/restart.ps1 [-Load quest4]     # (re)start headless, wait for "agent ready"
   python scripts/agent/agent.py "state" "look 12"    # send commands, print results
   python scripts/agent/agent.py "walkto npc:12" "talk 12" "cont all" "choose murder"
   ```

   Don't pipe `restart.ps1`'s output (`| Out-Null`, `| Select-Object`): the
   game inherits the pipe and the shell waits until it exits.

   - Game time is frozen between commands.
   - Conversation text (`SAY`) and barks (`BARK`) are logged.
   - Object ids (`#231108`) are only usable after `look` or `find` has listed them.
   - `save <slot>` / `load <slot>` keep progress across restarts; `timer`
     lists or sets usecode timers.
   - The user may have local saves `quest1`–`quest4`, Trinsic checkpoints
     (`quest4` = outside the east gate with Iolo and Spark; it carries stray
     flags and broken spellbooks from old bugs), `britain0`–`britain3`
     (`quest4` cleaned up, then after Lord British, after Batlin, in the
     castle storeroom), `cove0` (Rudyom's Wand in hand), `minoc0`
     (Batlin's package delivered), `minoc1` (after the murder trail) and
     `ship0` (on the Golden Ankh in barge mode). Saves from before barges
     were read lack the ships and the carpet: `scripts/repair_barges.py`.
     Don't count on them.
   - After a code change, the running game still has the old assembly:
     rebuild, then `restart.ps1`.
3. For visuals, run windowed (no `--headless`) and use the console's `shot`
   command, or a temporary `GetViewport().GetTexture().GetImage().SavePng()`.
   This opens a window on the user's desktop, so keep it short and prefer
   headless.
4. Remove temporary debug code before finishing. Tag it (e.g. `// TMPTRACE`) so
   `sed -i '/TMPTRACE/d'` can strip it reliably.

**Process safety:** the user often has the Godot editor and an editor-launched
game running. Never stop a Godot process whose command line lacks
`--headless`. `restart.ps1` already stops only headless ones.

## Data and engine facts worth knowing

- **New game data:** a new game reads GAMEDAT files from `STATIC/INITGAME.DAT`
  entries (`U7Paths.ReadGameDat`); saves read `saves/<slot>/`. `u7/GAMEDAT` is
  ignored on purpose: the GOG copy is a game already past the opening.
- **Bad PNGs:** the extracted PNGs for gumps, fonts and faces in `assets/` are
  bogus 8×8 tiles. Decode those from the VGA files via `VgaShapeFile`.
  `ShapeCache.GetFace` does this for FACES.VGA. Shape PNGs are fine.
- **Usecode reference:** `assets/usecode/usecode_disasm.txt` is the
  disassembly. `assets/data/usecode.csv` has each function's externs (the
  `call n` targets). To read a function's strings, parse `u7/STATIC/USECODE`
  (u16 id, u16 length, then u16 data size and the data block).
- **Event ids:** 0 proximity, 1 double-click, 2 internal exec (from scripts),
  3 egg, 4 weapon, ... `0x400 + npc` is an NPC's function; shape functions use
  `UsecodeMachine.GetShapeFun`.
- **ARRC:** the first value popped becomes element 0.
- **Books:** `book_mode` sends says into a book/scroll gump, shown at the next
  pending-text point (RET, ABRT). Book text uses '~' for a line and '*' for a
  page. Books (642) of quality 100-179 run 0x638, not 0x282
  (`UsecodeMachine.GetItemFun`, Exult's frame_usecode). Trinsic has plenty to
  test with: `find book` / `find scroll` from `quest4`.
- **Text codes:** `~` splits a say into click-to-continue pieces (`~~` counts
  once); a `*` at the start of a piece is an extra click, inside a piece a page
  break; `^` capitalises the next letter; `@` is the originals' quote mark
  (show `"`).
- **Opening flags:** global flag 0x3B is "did first scene" and 0x3C is "saw
  the stables". While 0x3B is clear the avatar carries object flag 16 (BG
  `dont_move` + `bg_dont_render`).
- **Usecode control:** actors under usecode control (flag 16/22, or a started
  halting script) are not walked by the player or their schedule
  (`UsecodeMachine.InUsecodeControl`).
- **Gump mode:** an open container gump blocks walking (Exult default). The
  console's walk commands close gumps first.
- **Walking:** blocking is Exult's chunk cache (`GameMap.Blocking`, tile x
  lift), actors included (solid shapes, `Solid` from the shape info); use
  `ActorWalker.IsBlocked`/`CanStep` for actors, not the single-lift
  `GameMap.IsBlocked`. `CanStep` asks an actor in the way to step aside
  (Exult `is_really_blocked`). An actor's own tile is blocked, so search
  spots around NPCs with `FindSpot`, and never change an in-world object's
  position, size or `Solid` except through `GameMap` (the counts would drift). A* (`Pathfinder.FindPath` + `ActorPathClient`) has no
  node cap, only Exult's cost ceiling: walks of 200+ tiles work, but the goal
  must be at the walker's lift and reachable within ~3x the straight cost.
  Walls make some routes too long (e.g. out of walled Trinsic except via the
  east gate). Walks are `PathWalk`s ticked by their delays (ms per step).
- **Range:** the console can `talk` at any range; a player cannot. Some
  scripts search near the avatar (Johnson's gate, `0x0834`), so stand next to
  the NPC (`walkto` / `tp`) when it matters.
- **Shape names** carry Exult's plural pattern (`/gold coin//s`).
- **Finding stubs:** `python scripts/usecode_stub_report.py` ranks stubbed
  intrinsics by static reachability. The console's `stubs` command and
  `stub_report.txt` (written on exit) list stubs actually hit.
- **Map quiz:** the copy-protection answers (Finnigan, function `0x886`) are
  Spektran 120, Terfin longitude 120, Buccaneer's Den longitude 60 and
  latitude 60, Dagger Isle 0, Skara Brae latitude 30 and longitude 60, Deep
  Forest 60.

## Tooling pitfalls in this environment (Windows, Git Bash + PowerShell)

- Bash heredocs mangle backslashes: a `'\\n'` inside a heredoc'd Python
  script reached the file as a real newline. For C# edits use the Edit tool,
  or write the patch script to a file with the Write tool and run it.
- The sandbox refuses `Remove-Item` in any command that also mentions a
  `C:\Program Files` path. Split such commands.
- Foreground `sleep` is blocked. Long-running games go through
  `restart.ps1` (detached) or `run_in_background`.
- Godot's `SetAnchorsPreset` under a `CanvasLayer` leaves the size at 0.
  Use `SetAnchorsAndOffsetsPreset`.

## Where things stand (October 2026)

Done and play-tested: the whole Trinsic murder chapter.
- **Opening:** moongate, Iolo, earthquake.
- **Investigation:** the stables, Finnigan and his report quiz, Spark joins,
  the key opens Christopher's chest, Gilberto, Gargan.
- **Leaving town:** the map quiz, the password "Blackbird", Johnson raises
  the portcullis.

Also played: Britain's first visit (Lord British's leads, Clint, Patterson,
Batlin's examination and package, the storeroom key, the Orb of the Moons),
Cove (Rudyom's notebook and wand, the blackrock explosion) and Elynor taking
the package in Minoc.

Also done: the conversation panel with Exult paging, screen-space barks,
party item and object-creation intrinsics, books and scrolls (`book_mode`),
SPRITES.VGA effects (`sprite_effect`, translucent), the spellbook and casting,
explosions and lightning, ships, carts and the flying carpet (Exult's barges),
Exult's walking (blocking, A*, path following with doors, speeds, actors
stepping aside), saves with timers, party order and restored NPC schedules,
NPC proximity remarks and woken sleepers, all of Black Gate's schedules
(street maintenance, patrol markers, crafts, duels), combat as Exult's
`Combat_schedule` for monsters, NPCs, the party and the avatar, and the agent
console.

Next, in priority order:

1. **Remaining intrinsics:** 32 stubbed, none reachable from Trinsic's or
   Britain's NPCs; the most-used are `flash_mouse` (needs Exult's cursors),
   `is_readied`, `set_attack_mode`, `kill_npc` and `set_oppressor`. Spells still
   reach 11 (summon, wizard eye, clone, armageddon, ...), and the light
   sources' palettes (`set_light`, `set_time_palette`) are not ported.
2. **Signs:** `display_runes` still uses the conversation panel; port
   Exult's `Sign_gump` (runic signs, plaques, gravestones).
3. **Walking follow-ups:** `Walk_to_schedule`'s off-screen legs, dormant
   NPCs, speed cursors.
4. **Story:** the Minoc leads point to Paws (the Crown Jewel), Moonglow's
   observatory, the Wisps of Yew and Hook in Buccaneer's Den; the Golden
   Ankh (Lord British's deed) and the flying carpet now travel. From `minoc1`
   or `ship0`.

Known small gaps:
- Exult shows the avatar's portrait next to the answers.
- Barks last 4 s; Exult's last about 2 s.
- Picked-up items are readied into free slots, as in Exult `Actor::add`.
