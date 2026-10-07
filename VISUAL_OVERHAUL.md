# Visual overhaul: Exult's rules, drawn the modern way, in 15 steps

## Context

The light spell is drawn as an animated glow around the avatar instead of
Exult's whole-screen palette, and Exult's rules still decide when and how
long. This plan gives everything else Exult shows through palettes, or that we
don't show yet, the same treatment:
- light sources and carried torches;
- dark dungeons;
- weather (317 eggs);
- palette cycling for water and flames;
- translucency (52 shapes, actors);
- spell sprites, missiles, explosions and magic objects;
- status outlines and invisibility;
- fades.

**User decisions (2026-10-07):**
- **Scope:** all four areas: lights, weather, magic & combat, palette cycling.
- **Magic glows:** moongates, fields, crystals, missiles and explosions also
  glow. This is cosmetic.
- **Weather:** a modern look (streaks, drifting snow, shader fog, cloud
  shadows, overcast grade) with Exult's triggers and durations.

**How to use it:** this file lives at the repo root as `VISUAL_OVERHAUL.md`,
next to PROGRESS.md. Run the steps in order, one session each. Paste the
step's prompt into a fresh Claude Code session in the repo. Steps marked
**TASTE** stop and show you rendered options before settling. Commit after
each step (or each phase) once you're happy; committing this file keeps the
plan with the code.

**On approval of this plan:** write it, unchanged, to
`c:\Users\Gregor\source\repos\u7-remake\VISUAL_OVERHAUL.md`. Change nothing
else and start no step.

## Architecture (every prompt refers to this)

1. **8-bit frame.** `WorldView` paints the world as Exult does, into a CPU
   byte buffer (an `Image_buffer8` port: `IndexBuffer8`).
   - Black Gate's `XFORM.TBL` makes translucency, invisible silhouettes and
     outlines exact table lookups.
   - A second byte plane, **glow**, marks emissive pixels.
   - Both planes are uploaded once per frame as an RG8 texture and drawn with
     one `DrawTexture`.
2. **One world shader** turns indices into colour and does all the lighting.
   - Palette rows: *ambient* is Exult's final palette from PALETTES.FLX,
     blended as Exult blends it (time of day, invisible, lightning, overcast,
     fog). *Lit* is the day palette. Both are rotated by Exult's colour
     cycling.
   - Pixel colour = `mix(ambient[i], lit[i], clamp(L + glow))`. `L` is the
     sum of up to 64 light pools passed as uniform arrays.
   - The light spell's current look moves in as one kind of light.
   - Cloud shadows and fog are noise terms in the same shader.
3. **Above the world:** `WeatherView` (rain, snow, sparkles), then `ScreenFx`
   (fade to black, red edge pulse). `LightSpellOverlay` and `LightningFlash`
   go.
4. **Rules in plain C#** under `World/` (light measure, palette choice,
   weather, missiles), checked headless with new console commands (`light`,
   `weather`, `damage`, `sprite`, `eggs`). The renderer only reads that state.

## Standing rules (every prompt says "follow the standing rules")

- Read `CLAUDE.md` and `PROGRESS.md` first. Exult 1.12.1
  (`exult/exult-1.12.1/`) is the source of truth for rules. Port it and name
  the origin in doc comments.
- `dotnet build godot/U7.csproj` ends with 0 warnings and 0 errors.
- Check rules headless with the agent console (`restart.ps1`, `agent.py`).
  Check looks with short windowed runs and `shot <name>`. Compare shots with a
  small Python script in the scratchpad. Never stop a Godot process without
  `--headless`.
- Tag temporary code `// TMPTRACE` and strip it before finishing.
- Update PROGRESS.md: status table, the system's "(current)" section, and
  "Not done".
- **TASTE** points: render the options, show the user the shots, apply their
  choice, and remove temporary preset keys afterwards.
- Don't commit. Finish with a summary of what changed, what was verified, and
  what is left.

---

## Phase 1: 8-bit world renderer

### Step 1: Decode SHAPES.VGA by palette index (data only)

```text
Step 1 of the visual overhaul (plan: VISUAL_OVERHAUL.md at the repo root; read "Architecture" and follow the "Standing rules").

Goal: a data layer that decodes SHAPES.VGA frames as palette indices, plus Exult's translucency tables. No rendering change in this step.

1. New godot/scripts/Data/ShapeFrame.cs, Exult Shape_frame (shapes/vgafile.cc):
   - extents XLeft/YAbove/XRight/YBelow;
   - RLE scans relative to the hotspot (X, Y, Length, Offset) plus packed index bytes;
   - FromRle;
   - FromTile for raw 8x8 terrain frames (shapes 0-149: shapelen/64 frames, framenum &= 31, extents 8/8/-1/-1, painted at -8,-8 like Exult copy8);
   - Reflect() (Exult Shape_frame::reflect: transpose into a buffer filled with 255, re-scan skipping 255, swap extents);
   - Covers(x, y);
   - ToImage(palette).
2. Data/VgaShapeFile.cs:
   - accept non-RLE entries in ParseExtents/FrameCount/Get;
   - add DecodeFrame(shape, frame, wrap). SHAPES.VGA and SPRITES.VGA must not wrap frame numbers (Exult returns no frame);
   - rebuild Decode/DecodeRgba on it so GUMPS/FONTS/FACES are unchanged.
3. U7Palette (consider its own file):
   - DayRgb uses Exult Get_color8, v*255/63 (imagewin/iwin8.cc:83). The old titan PNGs used it, the <<2 path did not;
   - add Raw6(n) (PALETTES.FLX entry n, 6-bit) and FindColor(pal, r, g, b, last = 0xE0) (palette.cc:401).
4. New Data/XformTables.cs: the 17 256-byte tables from u7/STATIC/XFORM.TBL (a FLEX).
   - Store them reversed as Exult does (shapeid.cc:296-316: xforms[16-i] = entry i).
   - Fallback: Exult Palette::create_trans_table from blends (palette.cc:465-483).
   - Invisible = file entry 0; FirstTranslucent = 0xEE.

Verify with temporary TMPTRACE startup code:
- every SHAPES.VGA frame decoded with the day palette equals its PNG in assets/graphics/shapes pixel for pixel;
- extents equal ShapeCatalog's FrameInfo;
- the number of shapes using indices 0xE0-0xFE is close to 242 (assets/graphics/shape_cycle_scan/descriptor.json);
- reflected extents equal those of frame|32.
Report any mismatch, then strip the TMPTRACE code. Take one windowed shot with a gump, a book and a conversation open: they must look as before.
Also delete the stray untracked scripts/__pycache__/ folder.
```

### Step 2: ShapeCache reads SHAPES.VGA instead of PNGs

```text
Step 2 of the visual overhaul (plan: VISUAL_OVERHAUL.md at the repo root; read "Architecture" and follow the "Standing rules"). Step 1 added ShapeFrame, VgaShapeFile.DecodeFrame, XformTables and the Get_color8 palette.

Goal: drop the extracted shape PNGs. ShapeCache reads u7/STATIC/SHAPES.VGA through the Step 1 decoder. WorldView still draws RGBA textures, so the game must look identical.

1. Rendering/ShapeCache.cs:
   - ShapeFrame? GetFrame8(shape, frame), with a jagged [1024][64] cache; slots 32-63 are reflected on demand;
   - ShapeFrame? GetSprite8(sprite, frame);
   - an XformTables Xforms property;
   - keep Get (RGBA Texture2D), now built from GetFrame8(...).ToImage with the opaque day palette (translucency comes in Step 5);
   - remove the PNG path, GetImage, ReflectNwSe, the unused WarmTiles, and U7Paths.ShapePng.
2. Callers:
   - WorldView's alpha pick ("Top-most object whose sprite contains") and GumpView.WorldSpriteContains use ShapeFrame.Covers;
   - GumpView.DrawWorldShape (container and paperdoll items, drag ghost) keeps using Get.
3. CLAUDE.md "Bad PNGs" note and README: shape PNGs are no longer needed; SHAPES.VGA is decoded at runtime.

Verify:
- before/after windowed shots (take the "before" set first) are pixel-identical: quest4 start, britain1 castle, a fireplace;
- container and paperdoll gumps, dragging an item, double-click picking;
- report the startup time before and after.
```

### Step 3: Paint the world into an 8-bit buffer

```text
Step 3 of the visual overhaul (plan: VISUAL_OVERHAUL.md at the repo root; read "Architecture" and follow the "Standing rules"). ShapeCache now has GetFrame8/GetSprite8 and Xforms (Steps 1-2).

Goal: WorldView paints like Exult's Image_buffer8 into a CPU byte buffer and shows it through one palette shader. The palette is static for now. The world looks the same except:
- SPRITES.VGA effects use exact XFORM translucency;
- hits show Exult's red outline, because a per-object tint can't survive one big draw.

1. New Rendering/IndexBuffer8.cs:
   - Width, Height, Pixels, plus a parallel Glow byte plane (zero for now);
   - Resize, Fill8(c), Fill8(c, w, h, x, y), Copy8;
   - PaintRle: non-RLE frames go through Copy8 at x-8, y-8;
   - PaintRleTranslucent: 0xEE..0xFE become xforms[p-0xEE][dest], 0xFF is copied (Exult ibuf8.cc:423-471);
   - PaintRleTransformed(xform);
   - PaintRleOutline(color): Exult vgafile.cc:658, both ends of every scan plus the first and last rows;
   - clip with spans; no per-pixel bounds checks, no per-frame allocations.
2. New Rendering/WorldPalette.cs:
   - a 256x1 RGBA8 ImageTexture from the day palette;
   - Exult Pixel_colors (HIT_PIXEL etc., shapeid.cc:130-142) found with U7Palette.FindColor.
3. WorldView._Draw:
   - origin = floor(camera centre - half view), size = ceil(view/zoom) + 2; Fill8(0);
   - flats from 128x128 index caches per terrain number (Exult Chunk_terrain::render_flats, LRU of about 256), copied with Copy8. This replaces GetChunkFlat/BuildChunkFlat;
   - flat objects, then dependency-ordered objects, with PaintRle;
   - an actor hit within 200 ms gets PaintRleOutline(HIT_PIXEL) (Exult actors.cc:2101), replacing the red tint;
   - dungeon blackness with Fill8(0) (Exult paint_blackness);
   - sprites with PaintRleTranslucent (every SPRITES.VGA shape is translucent, shapeid.cc:491);
   - upload as one RG8 ImageTexture (R index, G glow) with Image.SetData + Update (recreate on size change);
   - one DrawTexture at the origin;
   - keep PaintStamp and the bark bookkeeping.
4. Shader: an inline const string like LightSpellOverlay's; canvas_item, unshaded.
   - `varying vec4 tint; void vertex() { tint = COLOR; }`. Fragment COLOR is pre-multiplied by the texture, which breaks an index texture.
   - Fragment: texelFetch the index at ivec2(UV * size), texelFetch the palette, COLOR = vec4(rgb, 1) * tint.
   - WorldView.Modulate (night tint, fades), LightSpellOverlay and LightningFlash must still work on top.

Verify:
- windowed shots equal Step 2's except sprites (play an instrument for the sprite 24 notes; cast a spell from britain3 for sprite 7);
- the arena hit outline;
- TMPTRACE timing of paint plus upload at zoom 1 and zoom 4 (report the numbers);
- headless walking, picking and combat still work.
```

### Step 4: Palette cycling — TASTE

```text
Step 4 of the visual overhaul (plan: VISUAL_OVERHAUL.md at the repo root; read "Architecture" and follow the "Standing rules"). The world is painted into an 8-bit buffer through WorldPalette (Step 3).

Goal: Exult's palette colour cycling, so water, flames and the ether animate.

WorldPalette.Advance(ticksMsec), Exult Game_window::rotatecolours (gamewin.cc:1034-1059) and Image_window8::rotate_colors (iwin8.cc:122-155):
- rotate ranges 0xFC/3, 0xF8/4, 0xF4/4, 0xF0/4, 0xE8/8, 0xE0/8. In each, the last colour moves to the first slot (std::rotate(start, finish-1, finish)). There is no reversed range;
- step every RotateMs of real time with `while (ticks > last + speed) last += speed`. Keep rotating while gumps are open (Gump_manager.cc:847);
- update the texture only when a step happens.
Translucent shapes' 0xEE-0xFE pixels are already turned into other indices by the xforms, so only non-translucent pixels cycle, as in Exult.

Verify: two windowed shots about 300 ms apart at Trinsic's water, a fireplace and the ether differ only in pixels whose index is 0xE0-0xFE.

TASTE: Exult rotates every 100 ms in a paletted or unscaled window and every 200 ms in its default scaled setup (imagewin.h:357). Let the user watch both (a temporary key toggle in a windowed run) and keep their choice as a constant.
```

### Step 5: Translucency, invisibility and status outlines

```text
Step 5 of the visual overhaul (plan: VISUAL_OVERHAUL.md at the repo root; read "Architecture" and follow the "Standing rules"). IndexBuffer8 has PaintRleTranslucent/Transformed/Outline and the palette cycles (Steps 3-4).

Goal: Exult translucency for TFA-translucent shapes and all actors, and Exult Actor::paint (actors.cc:2081-2117).

1. WorldView: translucent = obj.IsActor || Catalog[shape].Translucent, painted with PaintRleTranslucent.
2. PaintActor:
   - skip DontMove/dont_render as now;
   - an invisible actor that is neither the avatar nor in the party is not drawn;
   - the invisible avatar and party members are painted with PaintRleTransformed(Xforms.Invisible) (vgafile.cc:611-652);
   - otherwise translucent;
   - then one outline in Exult's priority: hit (200 ms) > charmed > paralysed > protection > cursed > poisoned, with the Pixel_colors from shapeid.cc:130-142.
3. Gumps: ShapeCache.Get uses U7Palette.DayRgbaTranslucent for TFA-translucent shapes. This approximates Exult, which xforms over the gump.
4. Agent console: add `setflag <npc|id> <flag> [0|1]` if nothing like it exists, to test the outlines and invisibility.

Verify with windowed shots:
- Britain castle windows and stained glass, lamp posts 889, a fire field, a ghost;
- Iolo and the avatar change only in their blend pixels;
- each status outline;
- an invisible party member and an invisible NPC.

Docs (end of Phase 1):
- PROGRESS.md: status rows (the map renders 8-bit with palette cycling, XFORM.TBL translucency, outlines and invisible actors); a new "Rendering (current)" section; in "Not done", drop "True 8-bit palette cycling" and "invisible actors not drawn", and add "gumps are not palette-cycled or xformed";
- CLAUDE.md: layout lines for Data/ and Rendering/.
```

---

## Phase 2: Night and dungeon light

### Step 6: Exult's time palettes and the light spell in the world shader — TASTE

```text
Step 6 of the visual overhaul (plan: VISUAL_OVERHAUL.md at the repo root; read "Architecture" and follow the "Standing rules"). Phase 1 is done: WorldView paints an 8-bit buffer (index plus glow plane) through a palette shader with Exult's cycling.

Goal:
- the night tint (GameClock.WorldModulate) is replaced by Exult's real palettes, blended as Exult blends them;
- the world shader gets the light-pool machinery, with the light spell as its first light;
- LightSpellOverlay and LightningFlash are deleted.

1. New Rendering/PaletteSet.cs:
   - the 13 PALETTES.FLX entries named as in palette.h:32-45 (DAY, DUSK/DAWN, NIGHT, INVISIBLE, OVERCAST, FOG, SPELL, CANDLE, RED, 9, LIGHTNING, SINGLE_LIGHT, MANY_LIGHTS);
   - per-channel linear Blend (palette.cc:436-458).
2. World/GameClock.cs:
   - PaletteForHour(h, dungeon): dungeon or h < 5 NIGHT, 5 DAWN, 6-19 DAY, 20 DUSK, 21-23 NIGHT (gameclk.cc:42-54);
   - PaletteBlend(dungeon): from PaletteForHour(Hour) to PaletteForHour(Hour+1) across the hour (gameclk.cc:102-104, 166-169; Exult steps per game minute, smooth interpolation is fine);
   - LightSpellShows(dungeon) treats dungeons as dark (get_final_palette, gameclk.cc:56-84);
   - remove WorldModulate.
3. New Rendering/SceneLighting.cs, plain C#, updated in U7Game._Process before WorldView draws:
   - in dungeon = Map.DungeonHeight(avatar) != 0 (Exult set_in_dungeon);
   - the ambient palette per get_final_palette: an invisible avatar gives INVISIBLE, a lightning flash gives LIGHTNING (EffectsManager.LightningFlash), otherwise the time blend. Overcast and fog come in Step 14;
   - a light list holding just the spell for now: centred on the avatar's figure as U7Game computes it today, strength easing at 1.5/s;
   - Describe() for the console.
4. World shader:
   - the palette texture becomes 256x2 (row 0 ambient, row 1 lit = day; WorldPalette rotates both);
   - uniform arrays MAX_LIGHTS = 64: light_a (xy world px, z radius, w kind) and light_b (rgb colour*intensity, a phase), plus light_count. Pass preallocated full-length Vector4[] arrays;
   - per light a smoothstep falloff summed into L;
   - final colour = mix(ambient[i], lit[i], clamp(L + glow));
   - kind Spell ports LightSpellOverlay's look exactly: radius 7 tiles breathing sin(TIME*1.9), rim ripples with 5 and 11 lobes, inner blue pulse, rim band (0.62, 0.74, 1.0);
   - WorldView.Modulate stays white; the fade keeps using it until Step 8.
5. Delete LightSpellOverlay.cs, LightningFlash.cs and their .uid files. Remove _worldTint and the spell/flash block in U7Game.
6. Agent console:
   - `light`: palette from -> to, t, dungeon, final palette, spell minutes and strength;
   - `hour <h> [m]` takes minutes.

Verify:
- headless `light` at 18:00, 19:30, 20:30, 21:00, 4:30, 5:30 and in a dungeon;
- windowed: the light spell from britain3 at 23:00 against a shot taken before the change;
- a lightning flash.

TASTE: night darkness. Shoot outdoors at 23:00, at 20:30 and in a dungeon with:
(a) Exult's NIGHT palette as is;
(b) the old blue tint (0.28, 0.34, 0.58) applied to the day palette;
(c) between the two.
Apply the user's pick as a knob on the ambient row.
```

### Step 7: Light sources cast pools — TASTE

```text
Step 7 of the visual overhaul (plan: VISUAL_OVERHAUL.md at the repo root; read "Architecture" and follow the "Standing rules"). The world shader has ambient/lit palette rows and 64 light slots; SceneLighting feeds it the spell (Step 6).

Goal: Exult's light sources and carried lights cast flickering pools.

1. New World/LightSources.cs (Exult rules only):
   - Brightness(shape, frame): Exult get_object_light (shapeinf.cc:456), 0 unless the TFA light flag is set. Table from data/bg/shape_info.txt light_data (505-576):
     - 179: frame 0 -> 0, others 5;
     - 338: frames 2, 7, 8, 9, 12 -> 2; frames 3, 4, 5, 10, 11 -> 5; others 1;
     - 435, 551, 553, 701 -> 5;
     - 526, 825 -> 7;
     - 739: frames 0-7 -> 0, 2, 4, 6, 0, 2, 4, 6;
     - other flagged shapes (198, 440, 442, 534, 895) -> 1. Check how Exult's reader treats the malformed "198:/-1/3" line;
   - Strength: Exult Get_light_strength, max(0, 75 - 2|dx| - 3|dy|) * b, from centre tiles (gamerend.cc:326, objs.cc:149);
   - CarriedLight: Exult refigure_gear (actors.cc:973-998), with its slot rules (a belt item counts only if it isn't a hand item);
   - Level: the paint_map chunk window (gamerend.cc:215-234) over a fixed 320x200 around the camera, the dungeon / non-dungeon lists (chunks.cc:850), plus the party (gamerend.cc:590-602);
   - Classify: below 224 candle, below 640 single, otherwise many.
2. New Rendering/GlowTable.cs (look only):
   - kinds Flame (flicker; 338 small, 435, 701, 825, 739, 442, 551, 553, 895), Steady (526 lamp post, biased towards the top of its frame; light pools 198/440 with radius at least half their footprint), Magic (534 wisp), Flash (179);
   - radius about 1.5 + 1.8*sqrt(b) tiles, intensity min(1, 0.55 + 0.07b);
   - colours: flame (1, 0.78, 0.5), candle (1, 0.82, 0.58).
3. SceneLighting collects every frame from the visible chunks plus a 1-chunk margin:
   - use WorldView's paint filters (not removed, not contained, not an invisible egg, not a barge, not a DontMove actor, not an invisible non-party actor, Tz below the roof skip) and the dungeon filter;
   - get the frame from a shared WorldView.DisplayFrame(obj, ticks);
   - lights carried by painted actors sit at the figure's centre; other lights at the centre of the drawn frame;
   - seeded flicker (hash of tile and shape);
   - rank by intensity*radius/(1 + dist/200) and keep 64;
   - Exult's level and class are reported but don't change the look (an AmbientLift knob, default 0).
4. Intrinsics:
   - set_light 0x82 becomes a documented no-op: the per-frame recount equals Exult's refigure_gear (intrinsics.cc:3564-3583);
   - set_time_palette 0x84 calls SceneLighting.ResetPalette, which snaps the eases.
5. `light` also lists the top lights (shape, frame, brightness, tile, kind, radius) and the level and class.

Verify:
- headless: Britain lamp posts at 22:00 (britain1);
- use an unlit torch (595), ready it: carried 5, class single;
- firepit frames; inside a dungeon;
- frame time at zoom 1 with 64 lights.

TASTE: pool radius, falloff, flicker, flame colour, and pixel-snapped vs smooth. Use a temporary F7 preset switch in a windowed run and send the user shots of Britain at night and a torch-lit dungeon. Keep their choice and remove the key.
```

### Step 8: Fades and ScreenFx

```text
Step 8 of the visual overhaul (plan: VISUAL_OVERHAUL.md at the repo root; read "Architecture" and follow the "Standing rules"). Lighting runs through SceneLighting and the world shader (Steps 6-7).

Goal: Exult's fades with their real timing, drawn by a new ScreenFx layer.

1. fade_palette 0x8C (intrinsics.cc:2919-2934, palette.cc:304-397):
   - (cycles+1) steps 20 ms apart, to or from black;
   - a fade-in calls ResetPalette first;
   - UsecodeMachine gets FadeMs/FadeSerial next to FadedOut (keep FadedOut's use in NextText).
2. New Rendering/ScreenFx.cs: a Node2D at the top of the world stack. It draws the black fade (the red pulse comes in Step 12). WorldView.Modulate is no longer used for fades.
3. Exult's fades block the game. While a fade-out runs or holds, WorldView redraw and the camera freeze, so a teleport after the fade-out isn't seen.

Verify:
- the F6 death flow (0x60E fades out over 12 cycles, 0x610 fades in over 36): no jump;
- the Orb of the Moons from britain3 (0x824 fade, teleport, 0x636 fade-in and sprite 7);
- sleeping in a bed (0x622), if reachable.

Docs (end of Phase 2):
- PROGRESS.md: Clock and Magic rows (the carried-light palettes are now pools); a new "Lighting (current)" section; prune "Not done";
- CLAUDE.md: the taste note now says light sources are pools, and records the user's darkness and pool choices.
```

---

## Phase 3: Magic and combat

### Step 9: Missiles become effects that fly smoothly

```text
Step 9 of the visual overhaul (plan: VISUAL_OVERHAUL.md at the repo root; read "Architecture" and follow the "Standing rules"). WorldView paints an 8-bit buffer with a glow plane; SceneLighting feeds lights (Phases 1-2).

Goal: missiles stop being temporary world items and become effects, as Exult's Projectile_effect is (effects.cc:502-847), painted after the map and flying pixel-smoothly.

1. Actors/CombatEngine.cs projectiles (Projectile, LaunchProjectile, AdvanceProjectile):
   - no CreateItem/MoveObject/RemoveObject;
   - keep Shape, Frame, Pos, PrevPos and the step fraction;
   - expose a read-only Missiles list;
   - frames per Exult: 8 + dir for shapes with 24+ frames, frame 0 for 1-frame explosive shapes, otherwise skip_render;
   - rotation (rotation_speed) and returning weapons keep working.
2. WorldView paints missiles after the map with Exult's formula (tx*8 - 4tz, ty*8 - 4tz), interpolated between the 50 ms steps (visual only).

Verify:
- arena with a bow, crossbow bolts, a thrown weapon and a returning weapon if available;
- spells Fire Blast (0x652) and Lightning (0x661) cast from britain3;
- explosions still happen;
- `look` no longer lists missile objects.
```

### Step 10: Homing missiles and missile eggs

```text
Step 10 of the visual overhaul (plan: VISUAL_OVERHAUL.md at the repo root; read "Architecture" and follow the "Standing rules"). Missiles are effects with a Missiles list (Step 9).

Goal: Exult's Homing_projectile and missile eggs.

1. Homing_projectile (effects.cc:853-998):
   - Actors/AmmoTable.cs loads the homing column of ammo.csv (399 energy mist and 639 death vortex are 1);
   - created when a homing missile arrives, instead of Explode;
   - drawn as the explosion sprite (399 -> 13, 639 -> 8) through the buffer's sprite path;
   - lives 20 s and steps one tile per axis every 100 ms towards the target's tile plus half its height;
   - every 1000 ms it hurts non-party actors under it (HitWith, explosion);
   - when its target dies it retargets the nearest non-party living actor with alignment >= evil within 30 tiles.
2. Missile eggs (egg.cc:70-145, 500-565; 52 in the world). EggHatcher type 6 calls CombatEngine.HatchMissileEgg:
   - weapon = d1; projectile = the weapon's projectile, or 856;
   - d2 & 0xff = direction 0-7, or 8 = a random party member reachable in a straight line;
   - repeat delay = (d2 >> 8) * std_delay; range = the weapon's range;
   - active only within screen + 10 tiles.
3. Agent console: `eggs [type] [radius]` lists nearby eggs with type and data.

Verify:
- Energy Mist (0x67C) and Death Vortex (0x681) on arena monsters (give reagents or use a TMPTRACE shortcut);
- a missile egg: find one with `eggs 6`, `tp` near it, watch it fire.
```

### Step 11: Magic glows — TASTE

```text
Step 11 of the visual overhaul (plan: VISUAL_OVERHAUL.md at the repo root; read "Architecture" and follow the "Standing rules"). The buffer has a glow plane; SceneLighting has 64 light slots and a GlowTable (Steps 6-7); missiles and homing missiles are effects (Steps 9-10).

Goal: magic glows. The user wants these even where Exult has no light; they are cosmetic.

1. Glow plane, painted emissive:
   - SPRITES.VGA 7 teleport, 8 vortex, 12 fireworks, 13 bubbles, 16 sparkles, 18 beads, 23 sword strike, and the blasts 1, 4, 5, 19. Not 2 clouds, 3 smoke, 9 poof or 24 notes;
   - magic missiles 856, 807, 527, 417, 565, 399, 639;
   - cosmetic emitters: moongates 157/776/777/779, the Orb 785, fields 895/900/902/768, Virtue Stone 330, crystals and prisms 746/729/968/981/1010, beam of light 168, wisp 534.
2. Transient lights in SceneLighting for those sprites, missiles and emitters:
   - blasts flash with an envelope that peaks at frames/4 (Exult Explosion_effect's blast frame);
   - colour auto-derived from the brightest 20% of the sprite's opaque pixels, cached; lightning overridden to (0.8, 0.85, 1);
   - the full-screen lightning sprite 17 gives a brief ambient flash.
3. Agent console: `sprite <n> [frame]` plays a sprite over the avatar.

Verify at night with windowed shots:
- sprites 7, 12, 13 and 17;
- the cove0 blackrock explosion;
- a moongate from the Orb (britain3);
- a fire field.

TASTE: glow strength and colours, and whether magic objects glow by day too. Show the shots and apply the user's choice.
```

### Step 12: Red edge pulse on heavy hits — TASTE

```text
Step 12 of the visual overhaul (plan: VISUAL_OVERHAUL.md at the repo root; read "Architecture" and follow the "Standing rules"). ScreenFx exists (Step 8); hits show Exult's 200 ms red outline (Steps 3, 5).

Goal: Exult's flash_red rule drawn as a red pulse at the screen edge instead of the red palette.

1. CombatEngine.ReduceHealth: if the victim is the avatar and (delta >= maxhp/3 || oldhp < maxhp/4 || the damage is lightning), raise AvatarFlashRed. Otherwise the outline as now. (Exult actors.cc:2994-3005, palette.cc:105-112.)
2. ScreenFx red edge vignette: rises in 30 ms, holds about 70 ms, decays over 300 ms.
3. Agent console: `damage <n> [type]` on the avatar logs pulse vs outline.

Verify: the four `damage` cases headless; windowed shots of the pulse.

TASTE: pulse strength and width.

Docs (end of Phase 3):
- PROGRESS.md: the Combat section (missiles as effects, homing missiles, missile eggs, the pulse); the Sprite effects section (glows; also fix the stale "explosions not ported" line); prune "Not done".
```

---

## Phase 4: Weather

### Step 13: Weather rules and weather eggs (headless)

```text
Step 13 of the visual overhaul (plan: VISUAL_OVERHAUL.md at the repo root; read "Architecture" and follow the "Standing rules"). EffectsManager has bookkeeping-only weather and lightning today.

Goal: Exult's weather rules, without drawing.

1. New World/Weather.cs, Exult's Weather_effect classes (effects.cc:1149-1777), with an OnRemoved hook standing in for Exult's destructors:
   - Storm (2) = Clouds(len+1, overcast) + Rain(len+2, delay 20 + r%1000, gradual) + Lightning(len-2, the rain's delay + r%500);
   - Snowstorm (1) = Clouds + Snow;
   - Sparkle (3) = 33 sparkles, shown indoors;
   - Fog (4) = clock.SetFog while it lasts, plus 100 sparkles;
   - Clouds(n): overcast unless n == 6; 2 + r%5 clouds (+ r%2 when overcast); wind dx, dy = r%5 - 2 px per 100 ms;
   - Rain: 100 ms steps; a gradual start adds r%5 drops up to 200; the last 2.5 s removes r%15;
   - duration = len * 25 * std_delay.
2. EffectsManager.SetWeather (egg.cc:1452-1491):
   - len 0 means 6000;
   - unless the new weather is fog, all weather is removed first if the type is 3 or differs from the current one;
   - RemoveWeather(avatar, 120) on chunk change (gamewin.cc:2982) removes only effects with an egg location. A storm's own parts survive, an Exult quirk to keep;
   - storm lightning never flashes in a dungeon, usecode lightning always does (effects.cc:1432).
3. GameClock overcast and fog counters (gameclk.cc:206-221); fog is forced off when hour < 6 or > 20.
4. Wiring:
   - EggHatcher: weather eggs with type = d1 & 0xff and minutes = d1 >> 8 (egg.cc:619-634);
   - UsecodeScript opcode 0x5A gets Exult's check (ucsched.cc:666-672).
5. Agent console: `weather [n [min]]` shows or sets the weather; `weather eggs` lists nearby type-8 eggs.

Verify headless:
- tp to a storm egg: storm, clouds, rain and lightning are listed;
- walk 120+ tiles away: the egg's weather goes, the storm's parts stay;
- fog by day vs at 22:00;
- no storm flashes in a dungeon.
```

### Step 14: Drawing the weather — TASTE

```text
Step 14 of the visual overhaul (plan: VISUAL_OVERHAUL.md at the repo root; read "Architecture" and follow the "Standing rules"). Weather rules run in World/Weather.cs and EffectsManager (Step 13); SceneLighting builds the ambient palette row (Step 6).

Goal: draw the weather the modern way, keeping Exult's timing.

1. SceneLighting ambient row:
   - overcast blends towards the OVERCAST palette on day, dawn and dusk palettes (is_day_palette);
   - fog blends towards the FOG palette by day;
   - both ease over 20 clock ticks, about 4 s (gameclk.cc:146-158).
2. New Rendering/WeatherView.cs, a Node2D above WorldView and below ScreenFx:
   - per Rain effect, a particle set with a seeded Random, stepped every 100 ms of effect time and interpolated;
   - rain: diagonal streaks along +x+y (Exult 6,6 px per 100 ms);
   - snow: soft flakes with sway (+1,+1);
   - sparkles: additive glints timed like frames 21-27;
   - world-space positions, respawning at random view positions (Exult Basicdrop);
   - count scaled from Exult's 200 per 320x200 to the view, with a cap;
   - hidden under a roof (sparkles excepted); paused in gump mode;
   - tinted by the ambient luminance and brightened by lightning.
3. World shader:
   - cloud shadows: fbm noise drifting with the Clouds_effect wind, strength from the number of clouds, none indoors;
   - fog: drifting noise mixing towards the FOG colour.

Verify with windowed shots of storm, snow, fog, overcast, clouds and sparkles (an Ambrosia or generator egg), outdoors and indoors, day and night.

TASTE: each kind of weather. Show options and apply the user's picks.

Docs (end of Phase 4):
- PROGRESS.md: the Eggs row (weather and missile eggs), the Sprite effects row ("weather drawn"), a "Lighting and weather (current)" section, prune "Not done".
```

### Step 15: Wrap-up and regression

```text
Step 15 of the visual overhaul (plan: VISUAL_OVERHAUL.md at the repo root; follow the "Standing rules"). Steps 1-14 are done.

Goal: docs and a full regression pass.

1. CLAUDE.md:
   - Layout: the new Data/, Rendering/ and World/ classes (ShapeFrame, XformTables, IndexBuffer8, WorldPalette, PaletteSet, SceneLighting, GlowTable, ScreenFx, WeatherView, LightSources, Weather);
   - "Bad PNGs": SHAPES.VGA is decoded at runtime;
   - the taste note: Exult's rules, modern rendering, and the user's picks for darkness, pools, glows, the pulse and weather;
   - "Where things stand": move these items out of "Next".
2. PROGRESS.md: a final consistency pass over the status table, the "(current)" sections and "Not done".
3. Regression, headless and with short windowed shots:
   - walk Trinsic (quest4) and Britain (britain1);
   - open containers and the paperdoll, drag items, double-click NPCs;
   - fight in the arena; the F6 death flow;
   - load each save; cast a few spells from britain3;
   - Cove explosion (cove0).
4. grep for leftover TMPTRACE; the build ends with 0 warnings.
5. In VISUAL_OVERHAUL.md, mark every step done, with a line on what changed against the plan, or delete the file if PROGRESS.md now covers everything. Ask the user which.
```
