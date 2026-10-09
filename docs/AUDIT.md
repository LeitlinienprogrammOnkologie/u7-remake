# Exult port audit

Status: reference, 2026-10-09. A read-only review of how `godot/scripts` ports Exult 1.12.1, looking for logic that could be done better without breaking the game. Companion to [PROGRESS.md](../PROGRESS.md) (what is built) and [ENGINEERING.md](ENGINEERING.md) (where the engine is going).

Snapshot: `main` at `80e9097` plus the uncommitted work in the tree on that day (see `git status` of that day: among others CombatEngine, ItemQuantity, MonsterInfo, GameMap, U7Game, U7Game.Agent, BgIntrinsics, UsecodeMachine, ScheduleRunner, CombatSchedule, ActorWalker, ActorTypes, EggHatcher, ConversationPanel, and the new NpcTimers and Fields). Line numbers drift; the function names are the stable anchor.

## How to read this

Every finding points at code that exists today. Nothing here is a new feature.

- **Section 1, divergences:** the port does something Exult does not. Fixing one changes behaviour, towards Exult. Exult stays the source of truth (CLAUDE.md), so these come first.
- **Section 2, refactors:** the same behaviour, written more simply or in one place instead of several. Several divergences in section 1 exist *because* of a duplicate listed here. Where a refactor would also change behaviour, it says so.
- **Section 3, performance:** the same output for less work.

Each finding has a **risk** (how likely a fix is to break something) and a **check** (how to prove it didn't). Every finding was checked twice against the code and the Exult source on the day of writing; the second pass corrected several claims, line numbers and fixes, and found the extra points listed as **Also**.

When a finding is fixed, strike it through or delete it here, and update PROGRESS.md as usual.

## Suggested order

1. ~~D1, D3, D4, D5: a lost save field, a crash, and wrong data handed to usecode.~~ Fixed 2026-10-09, with D2's empty-stack gap; D5's chunk-order half waits for D6.
2. ~~P1: a two-line fix in `PaletteSet.Blend` plus the cache key.~~ Fixed 2026-10-09.
3. ~~R1, R2, R11: cheap de-duplication that later fixes build on.~~ Done 2026-10-09 (R2's locked-container rule is left for D9/D11).
4. ~~R5 with D9, then D10 and D11 in one pass over the combat code.~~ Done 2026-10-09.
5. ~~R6 with D12, then the rest of section 1 by subsystem.~~ Done 2026-10-09: every D finding is fixed, a few with a small part left (each says which).
6. ~~D18 needs the user's decision first, and so does D12's sleep-to-sleep case.~~ Decided and done 2026-10-09 (the user left both to the best choice): D18 Exult's rule, D12's sleeper gets up (Exult's bug fixed).
7. The rest of sections 2 and 3 as the code around them is touched.

---

## 1. Divergences from Exult

### ~~D1. A load forgets the jukebox egg count~~ (fixed 2026-10-09)

- `GwinState.EggCount` reads `repeat >> 16`; U7Game sets it after `MusicPlayer.Start`, which clears it. Checked headless: a save holding count 1 loads as 1 (the console's own `_music.Stop()` after a load zeroes it again, so a byte-for-byte round trip needs a windowed run).
- **Left:** the background-track condition of `read_gwin` (the port plays every track, as Exult does with MT-32 music), and the track lost by a save made with music off (headless or muted). R10 is still the structural fix.

### D2. Usecode started from inside usecode (fixed but for the order)

- **Where:** [UsecodeMachine.cs](../godot/scripts/Usecode/UsecodeMachine.cs) `Call` (~517-527), `WhenDone`, `FinishIfDone` (~564).
- **Exult:** `Usecode_internal::call_usecode` (ucinternal.cc ~3059-3088) runs the function at once, nested; afterwards it clears the answers, `set_book(nullptr)` and `init_faces`.
- **Status:** the hang described in the first draft (a nested `Call` wiping `UserChoice`, `_foundAnswer`, `Wait` and the pending CALLIS push, stranding the outer frames) is fixed in the tree: `Call` now defers through `WhenDone` whenever `InUsecode`. `AvatarDied`, `WeaponUsecode`, `CastSpell` and the `must` egg path go through it.
- **Remaining:**
  - ~~The guard tests only `InUsecode`.~~ Fixed 2026-10-09: `Call` defers on `InUsecode || WaitingForChoice`, like `WhenDone`, so text left showing after the final RET is no longer cleared.
  - Deferring is not Exult's order. `AvatarDied` closes the gumps at once (U7Game ~356) but the death usecode 0x60E runs only after the outer usecode ends. Fine for now; note it if a scene depends on the order.
- **Risk:** low.
- **Check:** bring the avatar to 1 HP and trigger 0x0824's damage (or `kill_npc` on the avatar) in the console; the game must not hang.

### ~~D3. Brain dictionaries can change while being iterated~~ (fixed 2026-10-09)

- `ScheduleRunner.Update` ticks a snapshot (`_ticking`, a reused list) of the NPC brains, then of the monsters; a monster or revived NPC added by a tick's usecode acts from the next frame. Checked: the arena's rats fought and died, a monster egg hatched, no exception. `ApplySlot` and `RestoreSchedules` still loop over the live dictionary; nothing there runs usecode synchronously today.

### ~~D4. Usecode positions and distances ignore the outermost container~~ (fixed 2026-10-09)

- `GetObjectPosition` and `GetDistance` take `Inventory.Outermost`; `GetDistance` is `ObjectGeometry.Distance` (footprints, 3D). `PositionOf` is Exult's `get_position`: an object only for a size 0 or 1 value (its outermost container), 3 elements as a tile, 4 (a `click_on_item` result) as elements 1-3, else the caller. No `get_original_tile_coord`: the port has no animation wiggle (see D7).
- Checked: Trinsic conversations and the bucket give the same distances as before for things on the map. The carried-item branch wasn't reached from the console (no usecode found that calls it on a carried item through `use`).

### ~~D5. `find_nearby` returns objects in a different order~~ (fixed 2026-10-09)

- Usecode's `find_nearby` (`FindNearbyExult`) sorts with `GameMap.ReverseSorter` (Exult `Object_reverse_sorter`); the engine's callers get Exult's unsorted order (D6), within a chunk Exult's list order. No difference in element 0 showed in Trinsic's conversations.
- **Left:** an actor's polymorph shape in the shape match (the port has no polymorph); `Barges.GetBarge` doesn't sort (ties between overlapping barges only).

### ~~D6. `find_nearby` is ported three times with different filters~~ (fixed 2026-10-09)

- One `GameMap.FindNearby`, Exult's `Game_object::find_nearby`: Exult's mask (`FindMaskAllows`: mask 0 takes actors too and leaves out invisible things and transparent shapes; 8 living NPCs; 0x28 with the invisible ones), only the chunks the box touches (P2), each chunk in Exult's list order (`ForEachInExultOrder`: flats as they came, then the rest newest first, as `Map_chunk::add` keeps them), unsorted. `FindNearbyExult` is it plus usecode's sort. Callers with port-only masks now pass Exult's: `SeekFoes` 0x28 (then D13's rule), explosions 0 (Exult; so an invisible actor isn't caught by a blast).
- `EggsInChunksNear` is Exult's `try_all_eggs` search: every egg in the chunks the 64-tile square touches, no distance test; `EggsNear` (button eggs, the console) keeps its distance. The rectangle is wrapped round the world (Exult's isn't, near x or y 0, where there is only sea).
- Checked: a temporary trace over a Vesper day, evening and fight found no search losing its wanted shape to the new filter; the town's schedules and eggs run as before.

### ~~D7. Object animation is not Exult's~~ (fixed 2026-10-09)

- `World/Animators` ports `Frame_animator`: Exult's beat of 100 ms, per object, for the animated objects painted since the last beat (off-screen ones stop where they are), `Initialize` (the frame group, a time-synched object's phase from its own frame), `get_next_frame` for every type of `Animation_info::create_from_tfa` (time-synched with the frame delay, hourly, non-looping, looping with the chance to leave frame 0 and the recycle, random), the mirror bit kept; the frame is written to the object (`GameMap.SetFrame`), so usecode and saves see it. `ShapeRecord.AnimType` reads `typeflags.csv`'s `anim_type`. `WorldView.DisplayFrame` is gone: the painter and SceneLighting both read `obj.Frame`, so a light comes from the frame painted.
- No BG shape is a single-frame animated one, so Exult's `Wiggle_animator` isn't needed; animated shapes' sound effects aren't played (no sound yet).
- Checked headless: Trinsic's red flag steps through its frames on the beat. Not compared side by side with Exult in a window.

### ~~D8. Missile eggs miss Exult's lift tolerance~~ (fixed 2026-10-09)

- `IsActive`'s party-near case takes Exult's `type == missile && deltaz / 5 == 0`; the avatar-far inner rectangle is one `InInner` for both tests.

**Left (decided against for now), outside EggHatcher:** eggs are only hatched for the avatar (U7Game ~153), but Exult's `Npc_actor::step` (actors.cc ~5483) hatches them for NPCs too. `IsActive` also hard-codes `inParty = isAvatar` (~203), so even with NPC steps hatching, party members would never trigger `party_near` / `party_footpad` eggs (Exult checks `in_party`, egg.cc ~983, ~1019). This is a separate, medium-risk change: party members would then set off teleport and monster eggs. The avatar leads, so party-only triggers rarely matter, while a companion trailing through a teleport pad would be sent off alone.

### ~~D9. Best weapon and shield choice~~ (fixed 2026-10-09)

- `CombatEngine.ReadyBestWeapon` / `ReadyBestShield` are Exult's `Actor::ready_best_weapon` / `ready_best_shield`: a weapon in hand that can shoot (`ready_ammo`), or a spellbook that can cast its bookmarked spell, is kept; otherwise the usable weapon (`Is_weapon_usable`) with the best `Weapon_info::get_base_strength` (`WeaponRecord.BaseStrength`, the `powers` column now read) plus effective range, not in a locked container, its best ammunition swapped into the quiver. Shields: armour in the shield hand stays, a party member keeps whatever is there, else the best by `Armor_info::get_base_strength` from the lhand or back types.
- Only a new game readies weapons at load, for the avatar and Iolo (Exult `read_npcs`); everyone else keeps NPC.DAT's or the save's readied things.
- Checked: a new game readies the avatar's dagger (thrown, so its effective range beats the torch) and Iolo's crossbow with a bolt stack moved into the quiver; Trinsic's fight and the arena run as before.

### ~~D10. Missile flight path and aim~~ (fixed 2026-10-09)

- `LinePath` walks `ZombieSteps` (Exult's `Zombie`, z can lead). Missiles start at `MissileTile` (the egg's too), aim at `ObjectGeometry.CenterTile` (party members for missile eggs, the thrower for a returning weapon), hit on a 3D distance under 3, and step out of the shooter's own volume first (`InVolume`, Exult `Block::has_world_point`). `Centre` remains for explosions and homing missiles, as Exult. Checked: Iolo's bolts fly and hit in the arena; no large shooter was at hand.

### ~~D11. Ammunition~~ (fixed 2026-10-09)

- Shots go through `Quantities.Modify` (the pile's frame follows, 30 bolts to 24 went from frame 30 to 31 as Exult's rule says); the triple crossbow needs 3 bolts a shot (its projectile's READY.DAT type 15); `FindWeaponAmmo` takes a `recursive` flag and uses `FindBestAmmo` (Exult `find_best_ammo`: `Ammo_info::get_base_strength`, fewer shots counting less, no locked containers; `AmmoRecord.Powers` now read) and Exult's search of the pack for a weapon that is its own ammunition; `ReadyAmmo` checks the readied spots first and then swaps the best ammunition into the quiver (`SwapAmmo`). `ItemQuantity.InsideLocked` is Exult's `inside_locked`.
- **Left:** Exult's `Can_be_added` refuses a weapon "on fire" in that pack search; the catalog has no on-fire flag.

### ~~D12. Schedule changes~~ (fixed 2026-10-09)

- `BeginWalkTo` tells the old schedule the final type (Exult), and a jump (`ApplySlot`, `UpdateSchedule`) ends the old schedule before it moves the NPC, so a sleeper still finds and makes its bed (`EndSchedule`). Checked in Vesper: at 6 the sleepers get up beside their beds and Auston's bedspread goes from frame 10 to 9.
- **The sleep-to-sleep case (decided):** Exult leaves a sleeper sent to sleep or wait elsewhere lying, to lie down on the floor later ("Does this leave NPC's stuck?"). The port gets it up instead, a fixed Exult bug like the weather ones.
- The action rule is R6's `ChangeSchedule`.

### ~~D13. Invisible targets are always skipped~~ (fixed 2026-10-09)

- `SeekFoes`, `FindOpponents` and `NeedNewOpponent` skip or drop an invisible actor only for a fighter that can't see invisible (`CombatEngine.CanSeeInvisible`: the dragons 504 and 505 and five other shapes can).
- **Left:** Exult's `get_effective_alignment` (charmed actors switching sides); the port has no charmed alignment.

### ~~D14. Sleeping: beds and facing~~ (fixed 2026-10-09)

- `SleepSchedule` is Exult's state machine: the closest free bed of 696, 1011 and the gargoyle futons 363 and 312 by `ObjectGeometry.Distance` within 24 tiles in a straight line; a walk to within 3 tiles of it; lying on it facing west for 696 and 363, north otherwise; `force_sleep` (`NpcTimers.ForceSleep`: the asleep flag, the 5-10 s sleep timer). With no bed, west or north by Exult's room test. Getting up: Exult's floor spot search, the bed made by a script 3 ticks later and the making animation; not when woken or going into combat. `Begin` no longer jumps into a bed; the nap (`NapBed`) is the same machine with Exult's `for_nap_time`. The bed's scripts end when it is lain in (`Runner.TerminateScripts`).
- The port's one addition: a sleeper loaded asleep has no bed recorded; on waking it gets a floor spot near the bed (Exult would stand it up on top of the bed).
- Checked in Vesper: Auston walks to his bed, lies down asleep, gets up at 6 and the bed is made after the delay; standing beside him wakes him ("Who goes there?") with the bed left unmade, and he lies down again; the gargoyle Aurvidlem sleeps on his futon.

### ~~D15. Smaller schedule differences~~ (fixed 2026-10-09)

- Diners pick food with `find_nearby` and the object distance (`FindNearest`). The walk-to arrival (`AtDest`) and `BeginWalkTo` / `BeginType`'s `<= 3` are `ObjectGeometry.Distance` (3D, footprints). `ScheduleRunner.Teleport` keeps the facing.

### ~~D16. Egg trigger areas can be left behind~~ (fixed 2026-10-09)

- `InsertIntoChunk` / `RemoveFromChunk` keep each egg's trigger area and the path-egg index (`AddEgg` / `RemoveEgg`, Exult `Map_chunk::add_egg` / `remove_egg`), the removal only when the chunk removal happened; `AddObject`, `MoveObject`, `MoveGroup`, `RemoveObject` and `ReadIregEgg` no longer do it themselves. `Eggs` is a `HashSet`. Checked: Trinsic's once-only usecode egg and its jukebox egg fire as before.

### ~~D17. Egg monsters are placed with the wrong spot search~~ (fixed 2026-10-09)

- `SpawnMonster` uses `FindSpot(tile, 5, shape, 0, maxDrop: 1)` (Exult's footprint, height, drop and random start), the monster then taking its frame; only NPC-class shapes spawn (every shape in the monster table is one, so nothing changes there); the item branch uses `CreateIregObject` (a reflected frame's footprint) and `PlaceInWorld`. `Pathfinder.GreedyStep` is gone.

### ~~D18. Roof detection leaves out actors~~ (fixed 2026-10-09, decided: Exult's rule)

- `GameMap.RoofHeight` is Exult's `is_roof` on the chunk cache (`ChunkBlocking.LowestBlocked` of the tile's column, actors included), constant time. `RoofOverAvatar` is Exult's `skip_above_actor`: worked out when the avatar's tile or lift changes, so nothing passing overhead toggles the roof while it stands still; WorldView, SceneLighting, combat's spot search and `is_pc_inside` read it (`AvatarInside`, Exult's `< 31`, as `FindSpot`'s inside test now is). The old object scan remains only as `CoverAbove`'s building-class `LowestBuilding`.
- Why Exult's rule: anything that stands above the avatar stands on a floor that already counts, so counting actors hardly ever shows. Over all 48,400 tiles of Trinsic at ground level the new answer equalled the old one.

### ~~D19. Gump hit-tests use rectangles, Exult uses pixels~~ (fixed 2026-10-09)

- `Gump.HasPoint` and `GumpButton.Contains` test the frame's pixels (`ShapeFrame.Covers`, Exult `Shape_frame::has_point` with its pixel of slack) from `ShapeCache.GetGump8`, a frame table over GUMPS.VGA that wraps frame numbers as the painting does. The gump is tested in frame 0, the frame it is painted in. Checked: the backpack's gump covers 12,388 of its 15,288 box pixels (the corners are off it), and naming and right-click-closing it work.
- **Left:** `GumpManager.FindGump` still also counts a click on a button outside the gump's pixels (Exult doesn't), kept for buttons drawn beyond their gump.

### ~~D20. Chunks are painted row by row~~ (fixed 2026-10-09)

- WorldView paints the non-flat objects chunk by chunk along Exult's diagonals (by x + y, each from its south-west end), checked by hand against Exult's two loops on a 4x3 view. Flats keep their row-by-row pass, as Exult's.

### ~~D21. Setting an object's frame: five copies, none with Exult's rules~~ (fixed 2026-10-09)

- One `UsecodeMachine.SetItemFrame(item, frame, checkEmpty, setRotated)`, Exult's `set_item_frame`: the reflection kept unless set, the frame already shown ignored; actors through `ChangeActorFrame` (Exult `Actor::change_frame`: an empty frame becomes its `visible_frames` stand-in, else standing); other objects ignore a frame they lack, or an empty one when checked (`ShapeFrame.IsEmpty`, through the VM's `Frame8`). `set_item_frame`, `set_item_frame_rot` and the script's frame opcodes use it, `face_dir` and 0x61-0x70 checking for empty frames; only `face_dir` restarts the walk. `GameMap.SetFrame` moves an object out of its chunk and back when a frame turns its footprint. Checked: doors open and close, the bucket's animation runs and leaves the avatar standing.
- **Left:** `rest_time` and `set_usecode_dir`, which the port doesn't keep.

### D22. Small usecode and actor differences (fixed but for one row, 2026-10-09)

| Where | Was | Now (Exult) |
|---|---|---|
| `UsecodeValue.Operate` | string `-` `*` `/` `%` int gave 0 | the string unchanged |
| `Conversation.LocateAnswer` | case-insensitive | exact (both callers pass the exact answer and index) |
| `GetContItems` on a null container | empty array | a null object (`get_array_size` 1) |
| `ShowNpcFace` | skipped `show_pending_text` | shows the pending text first, the face after it (the said text no longer appears under the next speaker's face) |
| `NpcTimers.Near` | a plain distance | the activity range by the wrap-aware `Dist`, the port's stand-in for Exult's read chunks |
| `ExecuteUsecodeArray` | loop guard read `_vm.LastEvent` | `CurrentFrame.EventId` |

- **Left:** `ShowPendingText` still says leftover text when a function returns (Exult doesn't); no case was found, and changing it risks the conversations.

### ~~R1. Direction tables and helpers in several copies~~ (done 2026-10-09)

- One `Core/Directions` (Exult dir.cc): `Dx` / `Dy`, `FrameRotation` (the old `Rotate`), `Of` (`Get_direction`), `NoWrap`, `Of4`, `WrapDelta`, moved verbatim. The copies in ActorWalker, CombatEngine, UsecodeScript, PartyManager, TileCoord and ObjectGeometry are gone; `FrameToDir` was only the frame's rotation band and went too. `ActorWalker.FacingOfFrame`, `DirFrame` and `DirIndex` stay (frame and step helpers). Checked: walking and the party's following unchanged on `quick`.

### R2. Walking an actor's possessions, five ways (mostly done 2026-10-09)

- `U7Object.AllInside()` (one iterator and an explicit stack, depth first, removed things skipped) replaces the two `AllPossessions` copies and `CombatEngine.HasInside` (Is_draco; it counted removed things, which never stay in a container). `U7Object.SetFlagWithContents` replaces `CombatEngine.SetOkayToTake` and `PartyManager.SetFlagRecursively`. Checked: `AllInside` gave the old walk's exact sequence for every NPC on three saves.
- **Left:** `U7Object.CollectContents` (a list, removed things included) stays for its callers. The locked-container rule (522, 798; Exult `inside_locked()`) is still only in `ItemQuantity.CanBeAdded`: applying it to the walks changes which weapon, shield or ammo is found, so it belongs with D9 and D11.

### R3. Container membership changed outside GameMap

- ~~`Equipment.Equip`, `Equipment.UnequipSlot`~~ (gone with R5), `CombatEngine.LeaveBody` (~2276-2296), `GumpManager.LiftUp` (~469-486) and UsecodeMachine (~419-427) edit `Contents`, `Container` and `ReadySlot` by hand, repeating `GameMap.PlaceInContainer` / `TakeFromWorld`.
- `Inventory.TryCombine` (~158-191) marks the source `Removed` and clears `Container` (~187-189) but leaves it in the parent's `Contents`; safe today only because every caller detaches it first.
- **Exult:** `remove_this`, `Container_game_object::add`.
- **Fix:** route through GameMap. **Risk:** medium-low. **Check:** drag, ready, kill, resurrect; counts and weights unchanged.

### R4. Objects built by hand

- `GameMap.CreateIregObject` (~2218, Exult `create_ireg_object`) is public, wraps the private `MakeObject`, handles reflection, and the schedules already use it. Yet `CombatEngine.CreateItem` (~2034, forces `Solid = false`), `HatchMonsterEgg` (~226-243), `LeaveBody`, `CreateMonster` (~331) and the avatar in U7Game (~97) build `U7Object`s themselves.
- **Fix:** use `CreateIregObject`, then place the object; also fixes the reflected footprint in D17. **Risk:** low.

### ~~R5. Equipment is a static class fed its tables by every caller~~ (done 2026-10-09, differently)

- The weapon and shield choice moved into CombatEngine, which owns the tables and the ammunition rules, instead of making Equipment an instance service; Equipment keeps the slot helpers (`AddToActor`, `AddReadied`, `FindBestSpot`, ...). Its `Equip` and `UnequipSlot` (two of R3's hand edits of `Contents`) went with the old choice.

### ~~R6. A schedule change happens in many places~~ (done 2026-10-09)

- `ScheduleRunner.ChangeSchedule` is Exult's `set_schedule_type`: an unfinished `IfElsePathAction` (`path_run_usecode`) is kept, any other action dropped; `Ending(new type)`; the new schedule (made and begun unless given); type and pending set; `NowWhat` at once within the activity range (Exult: unless dormant). `BeginType` and `SetSchedule` go through it; the avatar's `SetAvatarSchedule` keeps the walk too (it dropped it on every combat toggle) but not `StopAction`'s frame-time reset. Every schedule that changes the schedule inside its own `NowWhat` returns right after, so the immediate `NowWhat` can't be overwritten.
- **Left:** `RestoreSchedules`, `AddMonster` and `Revive` still assign a fresh brain's schedule directly (there is no old schedule to end, and a restored NPC keeps its pose).

### R7. Helpers copied between schedule classes

- `Valid()` defined 3 times (Bake ~44, Forge ~48, Sew ~52) and written inline 8 more (`x is { Removed: false } t ? t : null`).
- `PathTo` wrappers in Bake (~58) and Forge (~53-56).
- "Top of a table" (`obj.Tz + Catalog[obj.Shape].DimZ`) at about 10 sites.
- The table-edge spot logic in `ScheduleWithObjects.DropItem` (~78-87) and `WaiterSchedule.CreateCustomerPlate` (~278-287).
- `LabSchedule.PerimeterTile` is general geometry also used by Bake; it belongs in `ObjectGeometry`.
- `WaiterSchedule._startPos` (~45) duplicates `Schedule.StartPos`.
- "Path, then face, then act" repeated in Sew, Lab, Farmer, Miner, Preach, Bake, Forge: a `WalkThenFace(dest, dist, target, ms, ...)` returning null would cover them.
- Nearest object found by sorting a whole list: `FindClosest(...).FirstOrDefault()` and `is [var x, ..]` in Bake (~46), Forge (~50), Sew (~50), Lab (~46), Patrol (~242), Preach (~118), Desk (~182), Combat (~923, ~936), Sit (~72); and with `OrderBy` in `LabSchedule.ClosestTo` (~71-76), Desk (~185-186), Waiter (~691-692). Exult's single-result `find_closest` (objs.cc ~747-772) is a plain scan keeping the first closest; only the vector overload sorts (`std::sort`, also unstable). Add one `ClosestTo(TileCoord, int[] shapes, int dist = 24)` scan to `Schedule`. Ties at the `List.Sort` sites then resolve as in Exult; the `OrderBy` sites are already stable.
- `PatrolSchedule._state` (bare int from -1 to 6, used through ~383) and `_phase` (~29), `GrazeSchedule._phase`, `TalkSchedule._phase`, `PaceSchedule._phase` and `SleepSchedule._napState` are bare ints with magic numbers; most other schedules use a `State` enum. (Exult uses ints in several of these too.)
- Quality and frame filters applied in LINQ after `FindNearby` (Bake `Nearby` ~49-55, `PatrolSchedule.Markers` ~39-40, `SetProcureItemAction` in Schedule ~208), though `FindNearby` takes `qual` and `frame`. Bake's `OkayToTake` filter has to stay.
- **Risk:** low (pure extraction).

### R8. Three usecode resume paths

- [UsecodeMachine.cs](../godot/scripts/Usecode/UsecodeMachine.cs) has three cores that each "clear the wait, maybe push a result, `Run`, `FinishIfDone`": `Choose` (~604), `ResumeWait` (~633) and `ResumeAfterText` (~1427). `ClosePicture`, `EndWizardEye`, `EndFlash` and `UpdateFade` already go through `ResumeWait`; `ContinueText` and `TurnBookPage` through `ResumeAfterText`. `_afterText` runs only in `ResumeAfterText` (as does `_abortAfterText`, correctly: it is only set while a text wait is pending).
- **Fix:** one private `Resume(UsecodeValue? result)`; each wait kind only works out its result.
- **Risk:** low. **Check:** replay the opening, books, the map, fades and numeric input.

### R9. Small usecode helpers repeated

- "Objects to usecode array" written 6 times in BgIntrinsics (~711, ~725, ~774, ~1505, ~1895, ~2206), each `PutElem` copying a fresh value. Add `UsecodeValue.FromObjects`.
- "Avatar plus party" built three ways (`GetPartyList`, `PartyObjects` ~1818, `OnBarge` ~2372).
- `CollectMatching` (~2208, Exult `Container_game_object::get_objects`) repeats `ItemQuantity.Matches`.
- `p.Length > 1/2/3` checks (~285, ~683, ~1459, ~1666-1667, ~2199-2200) are always true because `p` has 12 slots, so their "missing argument" fallbacks never run. Read `p[i]` (0 when not passed; Exult's is a default `Usecode_value`, 0 but undefined, useval.h ~71), and fix the stale "Phase A/B" class comment. (`n` is used by `Stub` to log arguments and stays.)
- `UsecodeValue.Operate` and `Add` deep-copy the left value before checking types; copy only when the result is the left value.
- **Risk:** low.

### R10. Saves

- [SaveGame.cs](../godot/scripts/Game/SaveGame.cs): GAMEWIN.DAT has a separate writer and reader, which is how D1 happened. One `GwinState` with symmetric `Write(BinaryWriter)` / `Read(BinaryReader)`.
- `Write` deletes the slot's `U7IREG*` files (~31-34) before writing new ones; an exception part-way leaves a broken slot. Write into `slot.tmp` and swap; the format is unchanged. Two caveats: loading a slot has no fallback for files `Write` skips (FLAGINIT and USECODE.* when there is no usecode, IDENTITY when `u7/GAMEDAT/IDENTITY` is missing; U7Paths ~68-71), so copy those over from the old slot; and a directory swap on Windows isn't atomic, so rename the old slot aside first.
- The 9-argument `SaveGame.Write(...)` call appears in U7Game (~1082) and U7Game.Agent (~842), and so does the load sequence (U7Game ~1093-1096, Agent ~846-853). One `SaveTo(slot)` / `LoadFrom(slot)`.
- **Exult:** `Game_window::write_gwin` / `read_gwin` (gamewin.cc ~1424-1522).
- **Risk:** low. **Check:** `save a`, `load a`, `save b`, diff the slots byte for byte.

### ~~R11. `Check_weight` and `get_outermost` ported twice~~ (done 2026-10-09)

- `Inventory.CheckWeight(obj, onto, catalog)` is Exult's `Check_weight`; `GumpManager.RefuseDrop` and `U7Game.CheckWeight` (which keeps the cursor flash) call it. GumpManager's and the console's private `Outermost` copies are gone (`Inventory.Outermost`). Not play-tested: the console's `put` skips the drop checks, and only a mouse drop reaches them; the expression is unchanged.

### R12. Repeated guards and calls in U7Game

- `UsecodeRunning` exists (~1452) but its check is written inline at ~380, ~406, ~431, ~463, ~473, ~661, ~1848.
- "Can walk" is computed three ways: the left button in `_Process` (~670) and `EndWalkPress` (~1430) tests only `IsDead`; `RightButtonDown` (~1509-1522) uses `CombatSchedule.CanAct` (also paralysed, asleep, health ≤ 0) and `Drag is null`. Exult uses `avatar_can_act` for every walk (exult.cc ~1275, ~1576, ~1710), so a single predicate is a **behaviour change** towards Exult (a paralysed or sleeping avatar could no longer walk with the left button), not a pure refactor.
- `_usecode.Call(...)` plus `_conversation.Refresh()` is copied into the spell-cast, `UsecodeAction`, `CallUsecode` and death callbacks (~416-436, ~468-478) and `RunUsecode`; `PathWalk.ActivateDoor` (~404-412) is a fifth `Call`, without the refresh. The spell cast and the death callback are unguarded on purpose (the death one defers through `WhenDone`), so a guarded helper fits only the others.
- The combat toggle is the same in the gump button callback (~225-234) and the C key (~1048-1055).
- **Fix:** `TryCallUsecode(fun, obj, ev)` for the guarded calls, one `CanWalk` predicate (with the change above), one `ToggleCombat()`. **Risk:** low, medium for `CanWalk`.

### R13. The conversation panel is refreshed by 20 manual calls

- U7Game ~420, ~426, ~434, ~479, ~631, ~997, ~1006, ~1017, ~1769, ~1808, ~1837, ~1998 (12), U7Game.Agent ~369, ~466, ~471, ~476, ~498, ~508, ~519, ~829 (8), plus 3 VM events (U7Game ~500-502). `ConversationPanel.Refresh` relies on every path that changes `Wait` remembering to call it.
- **Fix:** in the panel's `_Process` (~277), compare a cheap snapshot (`Wait`, `InUsecode`, face, text, answer count) and refresh on change. The comparison must come before its `!Visible` early return, since `Refresh` is what shows the panel. The agent's `cont` and its page note read the pagination a synchronous `Refresh` sets up, so keep the agent's calls or refresh before reading.
- **Risk:** medium. **Check:** `talk` / `cont` / `choose` transcripts on `quest4` and `britain1` identical.

### R14. Left-button state in several flags

- `_suppressWalk`, `_walkPress`, `_walkPressMsec`, `_bookPress` (U7Game ~65-74; `_rightOnGump` is the right button's, Exult `right_on_gump`), mixing events and polling (`Input.IsMouseButtonPressed` ~675, ~717). After a left press that reaches the press branch (~899-951) `_suppressWalk` is `!_walkPress`; a press swallowed earlier (~855: holding a drag, the new game screen, a GUI control) leaves both false, and only the Shift-teleport cheat reads `_suppressWalk` alone. An enum changes that case (held left + Shift would no longer teleport), arguably a fix.
- **Fix:** one `enum LeftPress { None, Walk, Consumed, Book }`; move input (~849-1120, ~1425-1838) to a partial `U7Game.Input.cs`; the drop code (`DropOnMap` / `DropAtLift` / `DropOn`, ~1179-1298, Exult drag.cc) into its own class next to GumpManager.
- **Risk:** medium. **Check:** windowed: quick click, held walk, book page turn, right double-click, Shift + held left.

### R15. Gump contents: paint order and drops duplicated

- `Gump.FindObject` (~101-138) and `PaintContents` (~309-337) each loop twice with the same filter (loose items, then readied). `ActorGump.Add` (~44-68) copies the combine / put-into-container block of `Gump.Add` (~146-170).
- **Fix:** a protected `PaintOrder()` used by paint, hit-test and `GumpView.Texts`; a protected `TryDropOnto`. Layout during paint (`LayoutContents`, `SetToSpot` in `ActorGump.Paint`) is a literal port of `Gump::paint` and should stay.
- **Risk:** low.

### R16. (withdrawn) Blocking counts capped at 3

- [ChunkBlocking.cs](../godot/scripts/Data/ChunkBlocking.cs) (~129) `Math.Min(3, count + 1)` copies Exult's 2-bit `Set_blocked_tile` / `Clear_blocked_tile` (chunks.cc ~70-108), which saturates the same way. Dropping the cap would move *away* from Exult, so it stays. Nothing rebuilds a chunk after `Need` builds it (~45-68), so the live counts can't drift from a rebuild in practice.

### R17. Frame extents read from two sources

- `ShapeCatalog.LoadFrames` (~255) reads `assets/data/shape_frames.csv`; `VgaShapeFile.ParseExtents` (~214) reads the same from SHAPES.VGA. All 10,286 RLE frames match today; the CSV's 3,885 tile frames use a different convention (`0, 0, 7, 7` against `ParseExtents`' `8, 8, -1, -1`).
- **Fix:** fill `ShapeRecord.Frames` from the SHAPES.VGA that ShapeCache already loads, mapping tile frames to the catalog's convention and keeping `ShapeRecord.FrameCount` (~306, used by `DisplayFrame`), so paint order and painting can't drift apart. **Risk:** low.

### R18. Fallbacks to the bogus extracted PNGs

- [U7Paths.cs](../godot/scripts/Core/U7Paths.cs) (~22-24, ~112-119: `GumpPng`, `FontPng`, `FacePng`), used by [ShapeCache.cs](../godot/scripts/Rendering/ShapeCache.cs) (~124, ~166, ~211-214). The PNGs are bogus 8x8 tiles (CLAUDE.md), so when a VGA frame fails to decode the port draws that tile where Exult draws nothing, and reports a fake 8x8 size to the hit-tests (through `GetGumpFrame` ~143-147 and `GetFontFrame` ~191-195).
- **Fix:** delete the fallbacks and helpers, and the then-unused `GumpsDir`, `FontsDir` and `FacesDir`. A missing frame then falls to `GumpButton.Contains`' 8 px box (D19). **Risk:** low. **Check:** a script decoding every gump and face shape the catalog references.

### R19. Effects on two clocks

- [EffectsManager.cs](../godot/scripts/World/EffectsManager.cs) (~219-245): weather uses absolute due times, sprite effects count down their own `Due` and step newest first (~231). All weather events in a frame run before any sprite step, including an explosion's quarter-way blast, whatever their due times. Exult runs one time queue in time order. Weather and sprite events hardly interact, so the gain is small.
- **Fix:** absolute due times for sprites, one ordered queue. **Risk:** medium (edge-case ordering, towards Exult). **Check:** log event order with `sprite` and `weather 2`.

### R20. Small UI duplicates and leftovers

- `SignGump.Translation` (~74-86) rebuilds its strings with LINQ on every `_Draw`; `Lines` is set once in the constructor, so compute once.
- The text size `Clamp(RoundToInt(6.5f * zoom), 14, 40)` is in both [BarkOverlay.cs](../godot/scripts/UI/BarkOverlay.cs) (~36) and [MouseCursor.cs](../godot/scripts/UI/MouseCursor.cs) (~141).
- The gold-on-wood button styling is in both `ConversationPanel.MakeButton` and `NewGameView._Ready`, but not identically (border 1 with alpha against border 2 with hover-pressed and disabled styles). A shared `UiTheme.StyleButton` needs parameters to keep both looks; unifying the look is the user's call.
- `Barges._all` ([Barge.cs](../godot/scripts/World/Barge.cs) ~411) is never pruned within a session (it is rebuilt on each scene load, U7Game ~362); barges are few, so this is cosmetic.

---

## 3. Performance (same output)

### ~~P1. The palette cache almost never hits, and the blend jitters~~ (fixed 2026-10-09)

- `PaletteSet.Blend` uses Exult's `from + (to - from) * t` (also for the overcast's grey), exact when both ends agree, so the one-step jitter is gone. `SceneLighting.BuildPalettes` keys `T` only when `From != To`, and copies the day palette (made once) into `Lit` instead of re-blending it. Checked: no rebuild over 20 s at noon, one at night, one per clock tick through the dawn blend.

### P2. `FindNearby` reads too many chunks and sorts twice (mostly done 2026-10-09)

- Done with D6: `FindNearby` reads only the chunks the box touches and doesn't sort. **Left:** `FindClosest` still sorts a whole list (R7), and multi-shape lookups do one pass per shape.

### P3. The same chunks are scanned three times a frame

- `LightSources.Level` ([LightSources.cs](../godot/scripts/World/LightSources.cs) ~122-159), `SceneLighting.FindLights` (~248-325) and WorldView's paint (~331-362) each walk every object. `Level` calls `DungeonHeight` and `DisplayFrame` only for light sources; `FindLights` calls `IsPainted` and `DungeonHeight` per object. `WorldView.IsPainted` (~634-658) is a hand copy of the painter's checks and can drift. The roof lift and the dungeon test are worked out separately in WorldView (~290-291) and SceneLighting (~178, ~190).
- **Exult:** `Map_chunk::add` / `remove` keep per-chunk `dungeon_lights` / `non_dungeon_lights` counts (objs/chunks.cc ~852, ~917, ~1435) for the light level (gamerend.cc ~588-597); they don't feed drawing.
- **Fix:** per-chunk light-source lists maintained in `InsertIntoChunk`, `RemoveFromChunk`, `SetShape`. They cover `Level`; `FindLights` also collects actors (carried lights), windows and magic emitters that aren't light sources, which need their own lists or the paint pass. Optionally collect light candidates during WorldView's own paint pass (before its `IsPool` skip).
- **Risk:** low for the lists, medium for the merge. **Check:** keep the old path in a debug build and compare `Level`, `Class` and the light list every frame; the console's `light` across saves and hours.

### P4. Extra passes in the world upload

- [WorldView.cs](../godot/scripts/Rendering/WorldView.cs): `Fill8(0)` and `ClearGlow()` (~302-303) are redundant: the terrain `Copy8` (~319-326) writes both pixels and glow over the whole buffer, margin included. `Upload` (~421-448) interleaves index and glow into a two-channel texture with a per-pixel C# loop. 15 `SetShaderParameter("...")` calls (~391-408) allocate a `StringName` each frame; six set constants (`glow_boost`, `window_spread`, `cloud_tint`, `cloud_scale`, `cloud_soft`, `mist_scale`).
- **Fix:** drop the clears; two single-channel textures; static cached `StringName`s; constants once in `_Ready`. **Risk:** low. **Check:** pixel-diff `shot` captures.

### P5. Every actor pixel takes the translucent path

- [IndexBuffer8.cs](../godot/scripts/Rendering/IndexBuffer8.cs) (~119-158), from WorldView (~727). Exult's `Actor::paint` always paints translucent (actors.cc ~2095), so every pixel is tested one by one, even in runs with no see-through colours (0xEE-0xFE). Missiles and sprites (WorldView ~796, ~828, ~872) take the same path.
- **Fix:** when `ShapeFrame` decodes, flag each RLE run containing a see-through pixel; other runs get a span copy plus glow fill. **Risk:** low. **Check:** hash the buffer on both paths.

### P6. A* scans whole chunks for doors

- `FindDoor` / `FindBlocking` ([GameMap.cs](../godot/scripts/Data/GameMap.cs) ~837-864) scan every object in the chunk, called by `ActorPathClient.CheckBlocking` ([Pathfinder.cs](../godot/scripts/World/Pathfinder.cs) ~47, ~66) for every blocked neighbour.
- **Exult:** `Chunk_cache::find_door` (chunks.cc ~677-684) walks the chunk's door set (a `std::set` ordered by pointer); `find_blocking` (objs.cc ~837-850) is a full scan, as here.
- **Fix:** a per-chunk door list for `FindDoor` only, kept in `InsertIntoChunk` / `RemoveFromChunk` (not `Blocking.Update`, which skips non-solid objects, marks every chunk a footprint covers, and builds lazily), in chunk order so the first match is unchanged. **Risk:** low. **Check:** assert old and new agree; time a long walk in Britain.

### P7. Usecode VM overhead

- `CallIntrinsic` (~1572) allocates a 12-slot array plus 12 zero values, formats the `LastIntrinsic` string, and does `IntrinsicLog.RemoveAt(0)` per call; `FlagLog` likewise per flag write (~1114-1117). The run loop (~676-678) writes `LastFunctionId`, `LastIp`, `LastEvent` per instruction (fix D22's `ExecuteUsecodeArray` row before dropping `LastEvent`).
- `UsecodeFunction.GetString` re-decodes on every PUSHS / ADDSI (~872, ~879; 14,676 PUSHS and 9,732 ADDSI sites; converse loops push all answers each pass).
- `InUsecodeControl`, `HasScript`, `TerminateScripts` scan every script (~193-212); ScheduleRunner (~881) calls `InUsecodeControl` per NPC per tick. (Exult's `Usecode_script::find` scans too.)
- **Fix:** store id and argc, format only when the log is read; ring buffers; a shared zero value (only if no intrinsic changes its parameters in place: `UsecodeValue` is mutable); cache strings by offset; a by-object lookup kept beside the ordered script list. **Risk:** low.

### P8. Per-frame allocations in actors

- `NpcTimers.Update` (~55): `Where(...).ToList()` every frame while any timer exists; a reusable buffer or a `PriorityQueue` keyed on `Next`.
- `CombatEngine.IsHostileNearby` (~907) runs every frame from the cursor (U7Game ~1635) and while a key is held from the speed check (~1710), and walks every brain through `Schedules.Brains`, whose `Concat` / `Append` (ScheduleRunner ~366-369) allocates. Exult's `is_hostile_nearby` (gamewin.cc ~3168) looks only at `get_nearby_npcs`; the port already filters by the visible rectangle, so iterating the on-screen brains gives the same answer. (Exult also uses `get_effective_alignment` and skips the check in god mode; see D13.)
- Small arrays allocated per call in `MoveAside` (~170), `FindWeaponAmmo` (~1401), `FindBestSpot` (Equipment ~359), `UnequipSlot` (~557), and collection expressions `(int[])[...]` at Equipment ~381, ~498 and CombatSchedule ~634: make them static readonly.
- **Risk:** low.

### P9. Map work

- `AddDependencies` (GameMap ~335-370) recomputes `RenderOrdering.Info` (a readonly struct, so no allocation) for every pair within the 24-tile `Reach`; compute once per object per call.
- `FastPathClient.IsGrabable(TileCoord)` (Pathfinder ~403) creates a whole `U7Object` to describe a box, consuming a global object id each time (U7Object ~19). It only runs on a click (U7Game ~1247), so it isn't hot; pass a `Block` to stop burning ids.
- **Risk:** low.

### P10. Debug work in the game loop

- `U7Game._Ready` (~504-555) walks all 192x192 chunks counting filled containers and lists nearby NPCs and eggs, only to print them, outside `_debugOn`; it runs on every load (loading reloads the scene, U7Game ~749, ~1097, Agent ~853). Remove or put behind the debug flag.
- `_Process` (~816-842) does a `PickObject` and builds the HUD string every frame. The HUD also shows hp, combat mode, party, invincible, dead, the picked object's frame and flags, the jukebox track and the egg, combat and status messages, so a rebuild-on-change needs all of those in its key; otherwise leave it.
- `EffectsManager.Add` (~118) and `CombatEngine.Place` print on every sprite effect and spawn.
- `MusicPlayer.Start` (~93) re-reads and re-parses the MIDI on every egg restart; cache parsed events by (path, sequence).
- **Risk:** low.

---

## Checked and fine

- **A\*** ([Pathfinder.cs](../godot/scripts/World/Pathfinder.cs) `OpenSet` ~597-674, `FindPath` ~676-734) is a faithful `A_star_queue` / `Find_path` (pathfinder/path.cc ~164-470): the circular bucket chain with `last`, newest first within a bucket, 512 buckets to start (growing with `pri + 2`, as Exult), the same neighbour order. Nit: its comment says an estimate "of 512 or more finds nothing"; exactly 512 still works.
- **Decoders:** FLEX, palettes, XFORM.TBL and RLE are clean.
- **Positions:** only written outside GameMap for contained items, before placement, or while loading NPC.DAT.
- **The 8-bit CPU world buffer** is the right design: Exult's see-through shapes recolour the pixel underneath through a lookup table, which ordinary GPU blending can't reproduce. Section 3 works inside it. Skipping the repaint when nothing changed would save the most, but object fields (`Frame`, flags, `HitUntilMsec`) are written from many places, so without a central change counter it would be fragile.
- **Endgame** runs as an iterator of waits (`IEnumerable<double>`), a clean translation of Exult's blocking loops.
- **AvatarController, TextGump, SpellbookGump, MouseCursor** follow Exult closely without needless complexity.
- **Gump layout during paint** is a literal `Gump::paint` and should stay.
- **Blocking counts capped at 3** match Exult (see R16).
