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
  UI look) go to the user, ideally with rendered options; engineering
  questions with an Exult answer don't need asking. The world keeps
  **Exult's rules and is drawn the modern way**: Exult decides what shows,
  when and for how long (palettes and their blending, light levels, flashes,
  weather and its timing), the look is the user's. Their picks so far:
  - **UI:** MedievalSharp and the "dark wood & gold" conversation panel;
    books and scrolls (for now) in the original GUMPS.VGA art and FONTS.VGA
    font 4, scaled to fit the window.
  - **Signs:** signs, tombstones and plaques in the original GUMPS.VGA art
    and rune fonts, scaled to fit like the books, the runes translated
    beneath in MedievalSharp, gold on dark wood (`GumpView.PaintSign`).
  - **Colour cycling:** every 100 ms (of Exult's 100 and 200).
  - **Darkness:** night (and dungeons) is the day palette under the old blue
    tint, not Exult's teal NIGHT palette (`SceneLighting.NightTint`); the
    hours' blending is Exult's.
  - **Light spell:** an animated glow round the avatar (the user's idea), not
    Exult's whole-screen palette.
  - **Light sources:** flickering pools (radius ×1.3, smoothstep, flicker
    0.25, smooth edges); the light-pool discs (440, 198) aren't drawn; a
    painted roof hides the lights and effects under it (`GameMap.CoverAbove`;
    Exult paints effects over everything).
  - **Window light** (cosmetic, the user's wish, `WindowLights.Look`): a
    roofed room's lights, blasts and missiles show through its windows:
    medium strength, a 4-tile fan on the ground outside, panes lit in the
    glass's own colours (not candlelight), north and west fans reaching past
    the roof that hides their start; hidden effects show through the panes.
  - **Magic glows** (cosmetic, the user's wish): magic sprites, missiles and
    objects glow and light their surroundings, "strong", 60% brighter by day
    too, see-through parts glowing fully, each in its own colour
    (`GlowTable.Magic`).
  - **Crystal ball and Wizard Eye:** Exult's frame (SPRITES.VGA sprite 10)
    at the world's zoom over the original 320x200 screen, black outside it
    (not redrawn smooth, not the view filling the window).
  - **Cursors** (`UI/MouseCursor`): Exult's POINTERS.SHP frames at the
    world's zoom (crisp, the day palette) as the system cursor; the flashed
    words (Too heavy, Out of range, Out of ammo, Won't fit, Blocked) in
    MedievalSharp, gold on dark wood, centred on the cursor.
  - **Endgame** (`Game/Endgame`, `UI/EndgameView`): the movies and the
    credits' picture as they are (320x200 scaled to fit, black around);
    every word in MedievalSharp at the window's size where Exult paints its
    fonts: the Guardian's red with a dark outline, the narration,
    congratulations and credits gold; the credits glide between Exult's
    steps. After the end the game quits (as the original did).
  - **Red flash:** a badly hurt avatar (Exult's rule) gets a faint red pulse
    at the screen's edges, 0.35 strong and 0.12 wide, not the RED palette.
  - **Weather look** (`WeatherLook`): rain as streaks with faint splashes,
    big snowflakes at twice Exult's count, large glints for sparkles, cloud
    shadows of Exult's size and darkness, fog at 70% of the FOG palette with
    drifting mist, overcast as Exult's OVERCAST palette 30% greyer.
  - **Weather rules:** Exult's weather bugs are fixed, not ported (listed in
    PROGRESS.md, "Weather (current)"): clouds alone keep the overcast count,
    the fog count stays true, lightning can't stick or flash past its end,
    rain doesn't flicker back at its end. A storm's parts outliving its egg
    stays Exult's.

## Layout

```
godot/scripts/
  Game/       U7Game (scene root, input, wiring), U7Game.Agent (agent console),
              AvatarController (player walking), SaveGame (Exult GAMEDAT layout),
              Endgame (Exult end_game and the credits)
  Data/       GameMap (chunks, IREG, eggs index, paint order), U7Object,
              ShapeCatalog, FlexFile, VgaShapeFile + ShapeFrame (Exult Shape_frame:
              SHAPES/SPRITES/GUMPS/FONTS/FACES.VGA frames by palette index),
              U7Palette (PALETTES.FLX, Get_color8), XformTables (XFORM.TBL),
              VgaFont (FONTS.VGA metrics, Exult paint_text_box), ChunkBlocking
              (Exult Chunk_cache blocked flags + is_blocked), IffFile and
              FlicFile (ENDGAME.DAT and its movies, Exult playfli)
  Gumps/      GumpManager, GumpView (paints gumps and the open book), container,
              actor and stats gumps, TextGump (Exult Book/Scroll_gump page layout),
              SignGump (Exult Sign_gump: signs, tombstones, plaques)
  Usecode/    UsecodeMachine (VM, waits, scripts), BgIntrinsics (intrinsic table),
              UsecodeScript (execute_usecode_array), Conversation (faces, answers, text)
  Actors/     NpcDat, ScheduleRunner (schedule ticking, slots, proximity),
              Schedules/ (one class per Exult schedule), ActorActions (Exult
              Actor_action kinds), PathWalk (Path_walking_actor_action),
              CombatEngine (also missile eggs), Guards (Exult's theft,
              call_guards, attack_avatar; the arrest), Missile (Exult
              Projectile_effect, painted after the map), HomingMissile
              (Homing_projectile), PartyManager, Equipment, Inventory,
              ItemQuantity, ActorWalker (steps, Actor::is_blocked), tables
  World/      GameClock, EggHatcher, Pathfinder (Exult Find_path + clients),
              LightSources (Exult light rules: brightness, strength, carried
              light, light level), Weather (Exult's weather effects: storm,
              snow, sparkles, fog, clouds, rain, lightning),
              EffectsManager (Exult Sprites_effect, SPRITES.VGA animations;
              the weather's time queue, set_weather)
  Rendering/  WorldView (paints the map into IndexBuffer8, Exult's Image_buffer8:
              XFORM.TBL translucency, invisible actors, status outlines;
              shown through the world shader: ambient/lit palettes and
              lights), WorldPalette (palette texture, colour cycling, Exult's
              special pixels), PaletteSet (PALETTES.FLX by Exult's names,
              blends), SceneLighting (final palette, lights), GlowTable (how
              each light source looks, what magic glows), GlowColours (a
              glow's colour from the pixels), WindowLights (a roofed room's
              light through its windows: fans, panes, effects behind the
              glass), WeatherView (rain, snow and
              sparkles over the world; cloud shadows and mist are in the
              world shader), WeatherLook (the user's weather picks), ScreenFx
              (whole-screen effects: usecode's fades and the red edge pulse,
              over the gumps and the conversation too), ShapeCache (frames by
              index; RGBA shape textures for the gumps; gump, font and face
              frames)
  UI/         ConversationPanel, BarkOverlay, UiTheme, MouseCursor (Exult
              Mouse: POINTERS.SHP cursors and their flashes), EndgameView
              (the endgame's 320x200 picture and words)
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

   Several games can run side by side (e.g. one per town for parallel
   play-testing): `restart.ps1 -Io agent_io/<name> [-Load <slot>]` starts
   one on its own folder and restarts only that one (its `pid.txt`), and
   `U7_AGENT=agent_io/<name> python scripts/agent/agent.py ...` talks to
   it. Plain `restart.ps1` stops every headless game, theirs included.

   - Game time is frozen between commands.
   - Conversation text (`SAY`), barks (`BARK`), fades (`FADE`) and cursor
     flashes (`FLASH`) are logged. Fades hold the usecode for their 20 ms
     steps (`UsecodeWait.Fade`, as Exult's fade loop holds the game), a
     flash holds the game 600 ms (`UsecodeWait.Flash`); a command ends once
     they are over. `die` is F6 (the death flow, waking in Paws). There is
     no mouse headless: `cursor <x> <y>` tells the cursor over a tile, and
     `drag <id>` starts a real drag (Exult's checks) and puts it back.
   - Object ids (`#231108`) are only usable after `look` or `find` has listed them;
     pass them without the `#` (`use 231108`). A load renumbers them.
   - `save <slot>` / `load <slot>` keep progress across restarts; `timer`
     lists or sets usecode timers.
   - The user may have local saves `quest1`–`quest4`, Trinsic checkpoints
     (`quest4` = outside the east gate with Iolo and Spark; it carries stray
     flags and broken spellbooks from old bugs; in `quest1`–`quest4` the
     avatar is invisible, so the screen is grey: `setflag npc:0 invisible 0`),
     `britain0`–`britain3`
     (`quest4` cleaned up, then after Lord British, after Batlin, in the
     castle storeroom), `cove0` (Rudyom's Wand in hand), `minoc0`
     (Batlin's package delivered), `minoc1` (after the murder trail),
     `ship0` (on the Golden Ankh in barge mode) and `den1` (ashore at
     Buccaneer's Den, the Golden Ankh at its pier). Saves from before barges
     were read lack the ships and the carpet: `scripts/repair_barges.py`.
     Don't count on them.
   - After a code change, the running game still has the old assembly:
     rebuild, then `restart.ps1`.
3. For visuals, run windowed (no `--headless`) and use the console's `shot`
   command, or a temporary `GetViewport().GetTexture().GetImage().SavePng()`.
   This opens a window on the user's desktop, so keep it short and prefer
   headless. End a windowed run with the console's `quit`.
4. Remove temporary debug code before finishing. Tag it (e.g. `// TMPTRACE`) so
   `sed -i '/TMPTRACE/d'` can strip it reliably.

**Process safety:** the user often has the Godot editor and an editor-launched
game running. Never stop a Godot process whose command line lacks
`--headless`. `restart.ps1` already stops only headless ones.

## Data and engine facts worth knowing

- **New game data:** a new game reads GAMEDAT files from `STATIC/INITGAME.DAT`
  entries (`U7Paths.ReadGameDat`); saves read `saves/<slot>/`. `u7/GAMEDAT` is
  ignored on purpose: the GOG copy is a game already past the opening.
- **Graphics come from the VGA files:** `ShapeCache` decodes SHAPES.VGA at
  runtime (`GetFrame8` by palette index, `Get` as RGBA textures), and
  GUMPS, FONTS, FACES and SPRITES.VGA through `VgaShapeFile`. The extracted
  shape PNGs in `assets/` are not read; those for gumps, fonts and faces are
  bogus 8×8 tiles anyway.
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
- **Theft:** moving a thing that isn't okay to take (`find owned`; IREG
  flags, contents inherit their container's) into another gump or 2+ tiles
  is a theft if a neutral NPC looking at the avatar sees it: warnings, then
  guards who arrest. The console's `take` and `put` count; `attack <id>`
  hits a non-enemy in combat mode (`use` attacks only enemies).
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
the package in Minoc. Paws, Moonglow, Yew and Buccaneer's Den were explored
from `quick` (all NPCs, their quests; PROGRESS.md "Story exploration").

Also done: the conversation panel with Exult paging, screen-space barks,
party item and object-creation intrinsics, books and scrolls (`book_mode`),
SPRITES.VGA effects (`sprite_effect`, translucent), the spellbook and casting,
explosions and lightning, ships, carts, the flying carpet and the Skara Brae
ferry (Exult's barges, with gangplanks, titles and the ferry's horns),
Exult's walking (blocking, A*, path following with doors, speeds, actors
stepping aside), saves with timers, party order and restored NPC schedules,
NPC proximity remarks and woken sleepers, all of Black Gate's schedules
(street maintenance, patrol markers, crafts, duels), combat as Exult's
`Combat_schedule` for monsters, NPCs, the party and the avatar, thefts, guards
and the arrest (usecode 0x625: pay, prison or fight), Exult's mouse cursors
(speed arrows, the hand, the crosshair, flashes that hold the game, the drag
checks behind them), the endgame (movies, texts, credits; every intrinsic is
now ported), and the agent console.

The visual overhaul is done: the world painted as Exult's 8-bit buffer
(SHAPES.VGA decoded at runtime, XFORM.TBL translucency, invisible actors,
status outlines, colour cycling) through one world shader with Exult's time,
lightning, invisible, overcast and fog palettes; the light spell, light
sources and carried lights as flickering pools, magic glows, Exult's fades,
missiles as effects (homing missiles, missile eggs), the red edge pulse, and
Exult's weather drawn the modern way (rain, snow, sparkles, cloud shadows,
fog). Roofs hide the lights and effects under them, and a roofed room's light
shows through its windows.

Next, in priority order:

1. **Walking follow-ups:** right-button walking (asked for); Exult's
   dormancy and `Actor::follow` if the 32-tile activity range or the
   follow schedule ever show their seams.
2. **Story:** the main thread past Minoc in order (Paws, Moonglow, the Wisps
   via Alagner's notebook, Hook and the Isle of the Avatar), the towns not
   yet explored (Vesper, Jhelom, Skara Brae, New Magincia, Serpent's Hold,
   Terfin), and a name for the avatar at a new game (usecode's "Avatar"
   answers collide with the default name).

Known small gaps:
- Exult shows the avatar's portrait next to the answers.
- Barks last 4 s; Exult's last about 2 s.
- Picked-up items are readied into free slots, as in Exult `Actor::add`.
