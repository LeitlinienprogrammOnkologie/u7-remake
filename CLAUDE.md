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
  `u7-new-faces/`, `si-new-faces/`,
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
    font 4, scaled to fit the window. Buttons everywhere in the new game
    screen's look (dark wood, a firm gold border, `UiTheme.PanelLook`;
    2026-10-10, over the answers' see-through, thin-bordered one). The
    avatar's face (Exult shows it by the answers) in the second speaker's
    place at the panel's right while answers are offered (2026-10-10).
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
  - **The map** (`UI/MapView`, 2026-10-10): the user's painting of
    Britannia (`godot/data/map/britannia.png`) instead of sprite 22, smooth
    to fit the window; the sextant's mark (Exult's rules) a pulsing gold
    ring, placed through a warp fitted to the painting's towns and coasts
    (`scripts/painted_map_warp.py`; rerun it for a new painting).
  - **New game** (`UI/NewGameView`): the original title from MAINSHP.FLX
    over a dark wood & gold panel (name, Male / Female with the portrait,
    Journey Onward); with Serpent Isle installed, a "Skin" row of the skins'
    faces (2026-10-12, look A of three).
  - **Main menu** (`UI/MainMenuView`, Exult's `Game::show_menu`): look B
    of the mock-ups (2026-10-10): the same title with the new game
    screen's dark wood & gold panel of MedievalSharp buttons (Journey
    Onward, Start New Game, View Credits, View Quotes / View Endgame once
    earned, Quit), the chosen one lit.
  - **Save/load screen** (`UI/SaveLoadView`, Exult's `Newfile_gump`):
    layout A of the mock-ups (2026-10-10): the list of names on the left,
    the chosen game's picture, details and party faces on the right, Save /
    Load / Delete / Cancel and Quit below, dark wood & gold. Opened by Esc
    when no gumps are open (Exult's Esc opens its menu) and by the
    paperdoll's disk; F5/F9 stay the quick save and load.
  - **Original pixel art** (`ArtLook.Pack`, 2026-10-13): the world, gumps
    and items in the original art; the AI-upscaled packs (4x-NXbrz was the
    pick of 2026-10-10, made locally into `assets/hires/`, README) look bad
    and are off by default (`art pack nxbrz` for a look, kept in
    `saves/settings.cfg`). The painted faces stay: they are very good.
  - **Painted ground** (2026-10-12): an outside AI paints each ground
    material once (sea, calm sea, grass, mud, sand, swamp, cave floor and
    rock: seamless 512x512 textures), and `scripts/upscale/ground.py`
    assembles every chunk template from them by the original's material
    map (borders traced smooth at 4x, textures repeating every chunk so
    chunks join); built floors keep their tile art. Painting chunks or
    tiles one by one drifted between runs and left seams. The game draws
    an assembled template as the chunk's ground in one piece
    (`HiresArt.Terrain`, the pack's `chunks.hrp`); live page `ground.html`.
  - **Faces** (2026-10-12): the user's painted portraits
    (`u7-new-faces`, ready in `assets/faces/bg/` by
    `scripts/faces/prepare.py`) in place of FACES.VGA's art everywhere,
    smooth, at the original's height (`Rendering/PaintedFaces`; `art faces
    off` for the original). Serpent Isle's (`si-new-faces`, their black
    made see-through) are ready in `assets/faces/si/` for an SI game.
  - **Paperdolls** (2026-10-12, Exult's SI paperdolls in Black Gate when
    Serpent Isle is installed): SI's original art for now; redrawn on
    demand (`scripts/upscale/missing.py --make` makes what is missing, F7
    or `art paperdolls on` switches, kept in `saves/settings.cfg`).
  - **Mouse:** Exult's right-button walking, and the left button walks on
    open ground too (hold to steer, a quick click for a path).
  - **Cursors** (`UI/MouseCursor`): Exult's POINTERS.SHP frames at the
    world's zoom (crisp, the day palette) as the system cursor; the flashed
    words (Too heavy, Out of range, Out of ammo, Won't fit, Blocked) in
    MedievalSharp, gold on dark wood, centred on the cursor.
  - **Endgame** (`Game/Endgame`, `UI/EndgameView`; the intro, `Game/Intro`,
    in the same look): the movies and the
    credits' picture as they are (320x200 scaled to fit, black around);
    every word in MedievalSharp at the window's size where Exult paints its
    fonts: the Guardian's red with a dark outline, the narration,
    congratulations and credits gold; the credits glide between Exult's
    steps. After the end the game quits (as the original did).
  - **Quantity dialog** (`UI/QuantityView`, Exult's `Slider_gump` for
    part of a stack): look B (2026-10-10): a small dark wood & gold panel,
    "How many?", the count in words, a gold slider, OK and Cancel.
  - **Speech:** the Guardian's voice (U7SPEECH.SPC, INTROSND.DAT,
    ENDGAME.DAT) with subtitles: Exult's `speech_with_subs` on
    (2026-10-10), so his words still show while he speaks.
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
  - **Gumps under the palette** (RENDERER.md 5.9, 2026-10-13): Exult's
    rule, so gumps are dark at night and their flames cycle (`art gumps
    off` for day colours).
  - **Bloom and lit weather** (RENDERER.md 5.8, 2026-10-13, the user
    taking either look): the glow's spill at 0.35 over 10 world pixels in
    modern mode (`art bloom`); rain and snow brightened in the lights'
    colours where they fall (`WeatherLook.LightCatch`).
  - **GPU renderer the default** (RENDERER.md 5.3, 2026-10-13): `art
    renderer auto`, the compute compositor where a rendering device exists
    (bit-exact to the CPU in classic mode), the CPU headless.
  - **Modern colour** (RENDERER.md 5.5, 2026-10-13): true colour through
    the lookup tables and the remake's lights in linear light, on by
    default; the user saw no discernible difference and took either look.
  - **Left to the best choice** (2026-10-09, `docs/AUDIT.md` D12, D18):
    roofs are Exult's `is_roof` on the chunk cache, actors counting, set
    when the avatar moves; a sleeper sent to sleep elsewhere gets up (Exult
    leaves it lying, its own "stuck?" bug, fixed not ported).

## Layout

```
godot/scripts/
  Game/       U7Game (scene root, wiring), U7Game.Input (keys, mouse buttons,
              cursor, drops on the map), U7Game.Agent (agent console),
              AvatarController (player walking), SaveGame (Exult GAMEDAT layout;
              GwinState: gamewin.dat; SaveInfo: saveinfo.dat), U7Game.SaveLoad
              (quick save, the save/load screen, the world's picture),
              U7Game.Menu (the main menu and the shows it starts),
              Show (the shows on the endgame's screen: clock, fades,
              palettes, lettering), Endgame (Exult end_game and the
              credits), Intro (Exult play_intro)
  Data/       GameMap (chunks, IREG, eggs index, paint order), U7Object,
              ShapeCatalog (Exult read_info: TEXT.FLX names, TFA, WGTVOL,
              OCCLUDE, READY, WIHH), ShapeDataFile (WEAPONS/AMMO/ARMOR/
              MONSTERS.DAT), ShapeInfoText (shape_info.txt, bodies.txt,
              container.dat per game) and EntryReader (Exult ReadInt), FlexFile, VgaShapeFile + ShapeFrame (Exult Shape_frame:
              SHAPES/SPRITES/GUMPS/FONTS/FACES.VGA frames by palette index),
              U7Palette (PALETTES.FLX, Get_color8), XformTables (XFORM.TBL),
              VgaFont (FONTS.VGA metrics, Exult paint_text_box), ObjectNames
              (Exult get_name: what a click shows), ChunkBlocking
              (Exult Chunk_cache blocked flags + is_blocked), IffFile and
              FlicFile (ENDGAME.DAT and its movies, Exult playfli),
              HiresPack (the redrawn art's packs, scripts/upscale),
              ObjectSet (Exult Game_object_set: render dependencies in
              creation order)
  Gumps/      GumpManager, GumpView (paints gumps and the open book), container,
              actor and stats gumps, TextGump (Exult Book/Scroll_gump page layout),
              SignGump (Exult Sign_gump: signs, tombstones, plaques)
  Usecode/    UsecodeMachine (VM, waits, scripts), BgIntrinsics (intrinsic table;
              BgIntrinsics.Extended: Exult's own and Serpent Isle's), SiIntrinsics
              (SI's numbers mapped by name onto the same handlers),
              UsecodeScript (execute_usecode_array), Conversation (faces, answers, text)
  Actors/     NpcDat, ScheduleRunner (schedule ticking, slots, proximity),
              Schedules/ (one class per Exult schedule), ActorActions (Exult
              Actor_action kinds), PathWalk (Path_walking_actor_action),
              CombatEngine (also missile eggs), Guards (Exult's theft,
              call_guards, attack_avatar; the arrest), Missile (Exult
              Projectile_effect, painted after the map), HomingMissile
              (Homing_projectile), PartyManager, Equipment, Inventory, AvatarLook
              (the avatar's shape and face by sex), Polymorph (Exult shape_save),
              ItemQuantity, ActorWalker (steps, Actor::is_blocked), tables
  World/      GameClock, EggHatcher, Pathfinder (Exult Find_path + clients),
              LightSources (Exult light rules: brightness, strength, carried
              light, light level), Weather (Exult's weather effects: storm,
              snow, sparkles, fog, clouds, rain, lightning),
              EffectsManager (Exult Sprites_effect, SPRITES.VGA animations;
              the weather's time queue, set_weather), Animators (Exult
              Frame_animator: animated shapes' frames on screen),
              LightOcclusion (what stops light per tile and lift, RENDERER.md 5.6)
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
              frames), HiresArt (the art at 4x: a pack's frames or the
              original blown up, for the world and the gumps' textures),
              ArtLook (the user's picks for it), PaintedFaces (the user's
              painted portraits for FACES.VGA), PaintStream (the command
              stream WorldView records and a backend replays, RENDERER.md
              R1), GpuCompositor (the stream resolved by a compute shader
              into the same planes, `art renderer gpu`, RENDERER.md R2-R4),
              ColourLut (modern colour's lookup tables, R5),
              PlaneDump and RenderBench (the console's `art dump` and
              `art bench`, docs/RENDERER.md R0)
  UI/         ConversationPanel, BarkOverlay, UiTheme, MouseCursor (Exult
              Mouse: POINTERS.SHP cursors and their flashes), EndgameView
              (the endgame's 320x200 picture and words), NewGameView (Exult
              BG_Game::new_game: the avatar's name and sex), MainMenuView
              (Exult Game::show_menu), QuantityView (Exult Slider_gump: how
              many of a stack), SaveLoadView
              (Exult Newfile_gump: the save/load screen), MapView and
              PaintedMap (display_map's painted map, the tile -> pixel warp)
  Audio/      MusicPlayer (MIDI through the Windows synth), SoundEffects
              (Exult Audio's effects: its digital packs in assets/sfx,
              scripts/get_sfx_packs.py, else its MIDI effects,
              godot/data/midisfx.flx, through the same synth; Object_sfx),
              SfxMixer (Exult AudioMixer: 32 channels, distance and
              balance), WavFile (Exult WavAudioSample), SpeechPlayer
              (Exult's speech) and VocFile (Exult VocAudioSample)
  Core/       GameContext (Exult Game: the game and mode a world loads in,
              docs/SERPENT_ISLE.md), GameShapes (Exult get_shape: gumps by
              name per game), U7Paths (data paths by game, ReadGameDat,
              the mod's patch folder, Serpent Isle's files), U7Constants, TileCoord, Settings (saves/settings.cfg),
              Directions (Exult dir.cc: the 8 directions, steps, frame rotation)
scripts/      extract_assets.py, usecode_stub_report.py, get_sfx_packs.py
              (Exult's sound effect packs into assets/sfx), agent/ (console helpers;
              render_suite.py: the renderer's regression suite, RENDERER.md 7;
              cycling_check.py: does a dump's water cycle at the art's resolution;
              shot_diff.py: two shots compared, a heat map of the difference),
              usecode_disasm.py (a function of either game's USECODE, names resolved),
              painted_map_warp.py (fits godot/data/map/britannia_warp.txt),
              bridge_speech.py (the bridge's voices from SI's INTRO.DAT),
              upscale/ (the art redrawn by AI models into assets/hires, in its
              own venv: generate.py, pack.py, missing.py, compare.py; sets.py
              and world_view.py export the art as sets to paint anew, viewer.py
              compares them with the paintings, ground.py the ground
              assembled from painted materials (chunks.py: the templates),
              testart.py the synthetic test packs of RENDERER.md R4;
              see README),
              faces/prepare.py (the painted faces into assets/faces),
              ucc/ (Exult's usecode compiler built from exult/: build.ps1)
mods/<name>/  Exult-format mods: src/ (UCC), data/ (Exult's patch folder,
              --mod <name> or restart.ps1 -Mod <name>); function numbers
              from 0xD00
godot/data/bg/ Exult's Black Gate data (GPL: shape_info.txt, bodies.txt,
              container.dat, gump_area_info.txt, avatar_data, paperdol_info,
              shape_files, flx/) and patch/ (the remake's own additions, read
              under any mod: shape_additions.txt, the keyring; light_classes.txt,
              what stops light; its UCC in usecode/, numbers from 0xC00)
godot/data/si/ Exult's Serpent Isle data (GPL), the same files
docs/         BRIDGE.md (the adventure from Black Gate to Serpent Isle),
              RENDERER.md (the renderer's audit and the GPU plan) and the
              design notes
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

   A mod (`mods/<name>/data`, Exult's patch folder) is played with
   `restart.ps1 -Mod <name>`; its usecode is compiled with
   `scripts/ucc/bin/ucc.exe -o mods/<name>/data/usecode <main>.uc` (build
   UCC once with `pwsh scripts/ucc/build.ps1`).

   Serpent Isle (`serpent/`, docs/SERPENT_ISLE.md) and the saga are played
   with `restart.ps1 -Game si|saga` (Black Gate by default); the console's
   `game [bg|si|saga]` tells or switches the mode. Saves are per mode:
   `saves/bg/<slot>`, `saves/si/<slot>`, `saves/saga/<slot>`; `-Load` and
   `load` take a slot of the current mode.

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
     no mouse headless: `cursor <x> <y>` tells the cursor over a tile,
     `drag <id>` starts a real drag (Exult's checks) and puts it back,
     `name <id>` is a left click naming a thing, `rmouse <x> <y> <sec>|double`
     holds or double-clicks the right button over a tile, `rclose`
     right-clicks the top gump, `target [id|esc]` is the T key (target mode,
     the click on a thing, or Esc).
   - Object ids (`#231108`) are only usable after `look` or `find` has listed them;
     pass them without the `#` (`use 231108`). A load renumbers them.
   - `save <slot>` / `load <slot>` keep progress across restarts; `timer`
     lists or sets usecode timers.
   - The user may have local saves (in `saves/bg/`) `quest1`–`quest4`, Trinsic checkpoints
     (`quest4` = outside the east gate with Iolo and Spark; it carries stray
     flags and broken spellbooks from old bugs; in `quest1`–`quest4` the
     avatar is invisible, so the screen is grey: `setflag npc:0 invisible 0`),
     `britain0`–`britain3`
     (`quest4` cleaned up, then after Lord British, after Batlin, in the
     castle storeroom), `cove0` (Rudyom's Wand in hand), `minoc0`
     (Batlin's package delivered), `minoc1` (after the murder trail),
     `ship0` (on the Golden Ankh in barge mode) and `den1` (ashore at
     Buccaneer's Den, the Golden Ankh at its pier); `bridge0`–`bridge3`
     (the bridge mod, `-Mod eighteen`: after the audience, after Britain,
     before the Paws decision, after closing the shelter; `bridge4`-`bridge8`:
     Act II begun, Minoc done, Mole met, the log back, Act II done;
     `bridge9`/`bridge10`: after the UW2 card, after the Guardian's scroll). Saves from before barges
     were read lack the ships and the carpet: `scripts/repair_barges.py`.
     Don't count on them.
   - After a code change, the running game still has the old assembly:
     rebuild, then `restart.ps1`.
3. For visuals, run windowed (`restart.ps1 -Windowed`) and use the console's
   `shot` command, or a temporary `GetViewport().GetTexture().GetImage().SavePng()`.
   This opens a window on the user's desktop, so keep it short and prefer
   headless. End a windowed run with the console's `quit`. A change to the
   world's painting is checked with `python scripts/agent/render_suite.py
   check` (headless; the golden planes are local, `golden` remakes them).
4. Remove temporary debug code before finishing. Tag it (e.g. `// TMPTRACE`) so
   `sed -i '/TMPTRACE/d'` can strip it reliably.

**Process safety:** the user often has the Godot editor and an editor-launched
game running. Never stop a Godot process whose command line lacks
`--headless`. `restart.ps1` already stops only headless ones.

## Data and engine facts worth knowing

- **New game data:** a new game reads GAMEDAT files from `STATIC/INITGAME.DAT`
  entries (`U7Paths.ReadGameDat`); saves read `saves/<mode>/<slot>/`. `u7/GAMEDAT` is
  ignored on purpose: the GOG copy is a game already past the opening.
- **Graphics come from the VGA files:** `ShapeCache` decodes SHAPES.VGA at
  runtime (`GetFrame8` by palette index, `Get` as RGBA textures), and
  GUMPS, FONTS, FACES and SPRITES.VGA through `VgaShapeFile`. The extracted
  shape PNGs in `assets/` are not read; those for gumps, fonts and faces are
  bogus 8×8 tiles anyway.
- **Usecode reference:** `assets/usecode/usecode_disasm.txt` is the
  disassembly; `python scripts/usecode_disasm.py [--game si] 0x913` prints
  any function of either game with its strings and intrinsic names. `assets/data/usecode.csv` has each function's externs (the
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
the package in Minoc. Every other town was explored from `quick` (all NPCs,
their quests; PROGRESS.md "Story exploration"): Paws, Moonglow, Yew,
Buccaneer's Den, Vesper, Terfin, Jhelom, Skara Brae (its whole quest),
Serpent's Hold, Spektran and New Magincia, the main thread through Xorinia,
Alagner and the Time Lord's mission, and the Black Gate confrontation.
`quick` carries a stray invisible flag on the avatar (NPCs won't come to talk:
`setflag npc:0 invisible 0`) and has no barges (`scripts/repair_barges.py`).

Also done: Exult's NPC timers (poison, sleep, invisibility and the other
flags wear off), hunger and the hourly mending, the swamps' poison, bleeding
and its blood, weapon powers (poison, sleep, curse, charm with its change of
sides, paralysis, magebane), might and curse on the stats, experience for
kills, slimes splitting, breakable objects and powder kegs' chains, attacks
on tiles, knockouts (dead only below -strength/3), fields (fire,
sleep, poison, caltrops, campfires) and voice eggs; Hook's and
Dracothraxus's death usecode. Saves from the dungeon play-tests are local
(`sphere_*`, `tetra_*`, `cube_*`, `gate_*`, `sweep_*`, `fire_*`, `isle_*`,
`wrong_*`, `covetous_e`, listed in
PROGRESS.md).

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
checks behind them) and its buttons (right-button walking, a left click
naming things, a right-click closing a gump), the failed copy protection
("Oink!"), the endgame (movies, texts, credits; every intrinsic is
now ported), and the agent console.

The visual overhaul is done: the world painted as Exult's 8-bit buffer
(SHAPES.VGA decoded at runtime, XFORM.TBL translucency, invisible actors,
status outlines, weapons in hand and casting frames from WIHH.DAT, colour
cycling) through one world shader with Exult's time,
lightning, invisible, overcast and fog palettes; the light spell, light
sources and carried lights as flickering pools, magic glows, Exult's fades,
missiles as effects (homing missiles, missile eggs), the red edge pulse, and
Exult's weather drawn the modern way (rain, snow, sparkles, cloud shadows,
fog). Roofs hide the lights and effects under them, and a roofed room's light
shows through its windows.

Next, in priority order:

1. **Story:** every town, the main thread and every dungeon have been
   played (PROGRESS.md, "Story exploration" and "Dungeons and the main
   thread": the Sphere, Tetrahedron and Cube, the finale to both endgames,
   the whole Isle of Fire to Exodus's exile, the Isle of the Avatar's
   thrones to the Black Gate, Destard, Despise, Shame, Wrong, Covetous
   and the mines, Hythloth's edge, Alagner's notebook by the user's
   route). Nothing known is left; much of it was reached with shortcuts
   (`tp`, `create`, flags), so a new game played through would be the
   next test.
2. **Serpent Isle's engine** (docs/SERPENT_ISLE.md, M0-M8): M0 (the
   game per world load, saves per mode), M1 (SI's world loads and
   renders; the shapes' data read from the original files, not titan's
   CSVs) and M2 (Exult's per-game data files, gumps by name, the shape
   numbers made per game) and M3 (SI's usecode: intrinsics by name, the
   SI-only ones, its opening played to the storm) are done; M4 (the
   bridge's handoff, B5) is next.
3. **Walking follow-ups:** Exult's dormancy if the 32-tile activity range
   ever shows its seams (`Actor::follow` and the follow schedule are
   ported, 2026-10-11).

Known small gaps:
- Picked-up items are readied into free slots, as in Exult `Actor::add`.
