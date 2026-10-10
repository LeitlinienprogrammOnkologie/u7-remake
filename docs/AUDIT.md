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
7. ~~The rest of sections 2 and 3 as the code around them is touched.~~ Done 2026-10-10 (R20's button look chosen by the user), but for the small leftovers each entry names.

---

## 1. Divergences from Exult

### ~~D1. A load forgets the jukebox egg count~~ (fixed 2026-10-09)

- `GwinState.EggCount` reads `repeat >> 16`; U7Game sets it after `MusicPlayer.Start`, which clears it. Checked headless: a save holding count 1 loads as 1 (the console's own `_music.Stop()` after a load zeroes it again, so a byte-for-byte round trip needs a windowed run).
- **Left:** the background-track condition of `read_gwin` (the port plays every track, as Exult does with MT-32 music), and the track lost by a save made with music off (headless or muted). R10's one `GwinState` (done) keeps the writer and reader from drifting apart again.

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

### ~~D23. Stacks combine without Exult's rules~~ (found and fixed 2026-10-09)

- `Inventory.TryCombine` and `TryCombineInto` are gone. Exult has three combining paths, now each ported:
  - a drop on a stack, Exult `Game_object::drop`: `ItemQuantity.Drop` (U7Game's world `DropOn` already had it and now calls it): same shape, any frame for the quantity-frame shapes, all or nothing up to 100;
  - a drop on a container, Exult `Container_game_object::add(obj, false, true)`: `ItemQuantity.Combine`, i.e. `add_quantity` without creating, filling the stacks of the kind at any depth, partly if need be, the rest then added (before: only the container's own stacks, and frames had to match);
  - Exult `Actor::add(..., combine)` for a drop on a party member or a paperdoll's open area: `Equipment.AddToActor(..., combine:)` fills the bags' stacks first, or drops onto what a hand holds (before: a second stack).
- Counts change through `Modify` (Exult `modify_quantity`), so the frame follows: 60 bolts show frame 31 (before: 30). `Equipment.AddReadied` no longer combines into an occupied spot (Exult `add_readied` doesn't). A drop on a container that refuses it now falls through to the gump (its container or the paperdoll's spots), as Exult's `Gump::add` and `Actor_gump::add` do; the port refused it.
- With it, R15's duplicated drop block is one `Gump.DropOnto`.
- Checked headless on `britain1`: 10 coins (frame 6) dropped on Iolo join the 16 in his backpack (26, frame 7); two bolt stacks dropped on one tile become 60 in frame 31; through a temporary console command calling `Combine`: 100 coins into Iolo fill his backpack's 16 to 100 and keep 16, a further 10 are refused by his full stack, and his 100 into the avatar fill its 16 to 100 and its 10 (frame 6) to 26 (frame 7), the party's gold unchanged. Mouse drops in a window not tried; the gumps only call the two functions.

### ~~R1. Direction tables and helpers in several copies~~ (done 2026-10-09)

- One `Core/Directions` (Exult dir.cc): `Dx` / `Dy`, `FrameRotation` (the old `Rotate`), `Of` (`Get_direction`), `NoWrap`, `Of4`, `WrapDelta`, moved verbatim. The copies in ActorWalker, CombatEngine, UsecodeScript, PartyManager, TileCoord and ObjectGeometry are gone; `FrameToDir` was only the frame's rotation band and went too. `ActorWalker.FacingOfFrame`, `DirFrame` and `DirIndex` stay (frame and step helpers). Checked: walking and the party's following unchanged on `quick`.

### R2. Walking an actor's possessions, five ways (mostly done 2026-10-09)

- `U7Object.AllInside()` (one iterator and an explicit stack, depth first, removed things skipped) replaces the two `AllPossessions` copies and `CombatEngine.HasInside` (Is_draco; it counted removed things, which never stay in a container). `U7Object.SetFlagWithContents` replaces `CombatEngine.SetOkayToTake` and `PartyManager.SetFlagRecursively`. Checked: `AllInside` gave the old walk's exact sequence for every NPC on three saves.
- **Left:** `U7Object.CollectContents` (a list, removed things included) stays for its callers. The locked-container rule (522, 798; Exult `inside_locked()`) is still only in `ItemQuantity.CanBeAdded`: applying it to the walks changes which weapon, shield or ammo is found, so it belongs with D9 and D11.

### ~~R3. Container membership changed outside GameMap~~ (done 2026-10-09)

- `LeaveBody` moves the items with `PlaceInContainer` / `PlaceInWorld`, `GumpManager.LiftUp` with `TakeFromWorld`, and `UsecodeMachine.Resurrect` with `Equipment.AddToActor(..., dontCheck: true)`. `Inventory.TryCombine` took the map and removed a used-up stack with `RemoveObject`, so it left its container's list wherever it was (since D23, `ItemQuantity.Drop` / `Combine` do); the console's `put` doesn't detach first, so "every caller detaches it first" was not quite so.
- Two changes towards Exult came with it: a resurrected NPC's items are readied again (Exult `Actor::resurrect`'s `add(item, true)`; before, they all came back loose, the armour not counting), and a body-less death's items that find no spot stay with the dead actor (Exult's `tooheavy`; before, they were lost).
- Checked headless on `britain1`: Iolo killed, his body holds his things at 255,255 unreadied; the Resurrect spell (0x0684) on the body brings him back with crossbow, backpack, armour, leggings and boots in their old spots; two bolt stacks merged in his backpack (a temporary console command calling `TryCombine`) leave one stack of 60 and the backpack at 7 items.

### ~~R4. Objects built by hand~~ (done 2026-10-09)

- `GameMap.CreateActor` (Exult's `Actor` constructors) is `CreateIregObject` for actors: footprint by the frame's reflection, solidity from the shape, out of the world until placed. `CreateMonster` and the avatar in U7Game use it (a monster hatched in a reflected frame now has its turned footprint), `Place` and the avatar go in with `PlaceInWorld`; `LeaveBody`'s body and the missiles that land or come back use `CreateIregObject`, and `CombatEngine.CreateItem` is gone. `HatchMonsterEgg` already did (D17).
- Towards Exult: a landed missile has quality 0 (Exult's; the quantity reads as 1 either way) and the shape's solidity, no longer forced off; of the shapes that can land, only the hawk (555) is solid.
- Checked headless: the arena's rats spawn, fight and leave bodies; a new game's avatar starts at the gate with its dagger readied and the opening runs to Iolo joining. Not reached: a missile landing (Iolo hit with every bolt in two fights) and a returning hawk.
- **Left:** NpcDat still builds the actors it reads (`ApplyShape`, the same footprint rule), as loading may.

### ~~R5. Equipment is a static class fed its tables by every caller~~ (done 2026-10-09, differently)

- The weapon and shield choice moved into CombatEngine, which owns the tables and the ammunition rules, instead of making Equipment an instance service; Equipment keeps the slot helpers (`AddToActor`, `AddReadied`, `FindBestSpot`, ...). Its `Equip` and `UnequipSlot` (two of R3's hand edits of `Contents`) went with the old choice.

### ~~R6. A schedule change happens in many places~~ (done 2026-10-09)

- `ScheduleRunner.ChangeSchedule` is Exult's `set_schedule_type`: an unfinished `IfElsePathAction` (`path_run_usecode`) is kept, any other action dropped; `Ending(new type)`; the new schedule (made and begun unless given); type and pending set; `NowWhat` at once within the activity range (Exult: unless dormant). `BeginType` and `SetSchedule` go through it; the avatar's `SetAvatarSchedule` keeps the walk too (it dropped it on every combat toggle) but not `StopAction`'s frame-time reset. Every schedule that changes the schedule inside its own `NowWhat` returns right after, so the immediate `NowWhat` can't be overwritten.
- **Left:** `RestoreSchedules`, `AddMonster` and `Revive` still assign a fresh brain's schedule directly (there is no old schedule to end, and a restored NPC keeps its pose).

### ~~R7. Helpers copied between schedule classes~~ (mostly done 2026-10-09)

- In `Schedule`: one `Valid` (the three copies and eight inline ones gone), `TopLift` (Exult `get_lift() + get_3d_height()`, ten sites), and Exult's two `find_closest`s with Exult's mask 0xb0: `FindClosest` (the sorted list, kept where Exult goes through the list: chairs, crops, ores, plates) and `ClosestTo` / `Closest` (the single result: a scan keeping the first closest, no sort), now used wherever Exult takes one (practice targets, desk and its chair, cauldron, the lab's book and chair, the patrol's book, the icon, the sitter's chair, the bake, forge and sew shapes, the plate's leftovers). Ties now go as in Exult, where `List.Sort` was unstable; Bake, Forge and Sew lose their `Closest`, Lab its `ClosestTo`.
- `ObjectGeometry.PerimeterTile` (from LabSchedule) and `ObjectGeometry.EdgeNearest` (the table edge `DropItem` and the waiter's plate both worked out); `WaiterSchedule` uses `StartPos`; Bake's `Nearby`, `PatrolSchedule.Markers` and the procure search pass quality and frame to `FindNearby` (Bake keeps its okay-to-take filter).
- Checked headless on `britain1`: a day of Britain's schedules without an exception; Patterson at his desk handling desk items by the chair found, Gaye sitting at her work, Jeanette serving the Blue Boar's diners with a small plate (717:5) made on a table top.
- **Left (on purpose):** the "path, then face, then act" helper (the schedules differ in the details it would have to cover) and the bare-int state machines (Exult's are ints too).

### ~~R8. Three usecode resume paths~~ (done 2026-10-09)

- One private `UsecodeMachine.Resume(result, abort)`: the waiting intrinsic's result pushed if it waits for one, then `Run` (or the abort that waited for the text), then `FinishIfDone`. `Choose` works out the menu's result (the answer, or its number for `select_from_menu2`; none for a converse), `ResumeWait` passes its own, `ResumeAfterText` 0 after running `_afterText`.

### ~~R9. Small usecode helpers repeated~~ (done 2026-10-09)

- `UsecodeValue.FromObjects` builds the six object arrays; `get_party_list` and `on_barge` use `PartyObjects`; `CollectMatching` uses `ItemQuantity.Matches` (now public). The `p.Length` checks are plain `p[i]`, which is what ran (12 slots) and what Exult does: `show_npc_face`, `fade_palette`, `find_nearby`, `count_objects` and `get_cont_items` all read `parms[i]` without looking at `num_parms`. The class comment says so instead of "Phase A/B". `Add` and `Operate` build their result instead of deep-copying the left value first.
- Checked headless with R8 from `quest4`: the map quiz (three numeric answers), Iolo's "leave" (`select_from_menu` Yes / No: "Whew. Thou didst frighten me!"), a book's page, the cloth map, a death (the fades, Paws, Iolo's and Spark's words), and Polly's bread in Paws (`select_from_menu2`, Yes, 3: 6 gold paid, three loaves).

### ~~R10. Saves~~ (done 2026-10-09)

- `Game/GwinState` is GAMEWIN.DAT's one writer and reader (Exult `write_gwin` / `read_gwin`), the reader now Exult's field by field: a short file keeps the defaults of what it lacks (before, a file with the special light but no music lost the light, and one cut inside the last bytes lost armageddon and combat both), combat `== 1`.
- `SaveGame.Write` writes into `slot.tmp`, copies over the old slot's files it didn't write (all but `GWIN.DAT`), renames the old slot to `slot.old`, moves the new one in (putting the old one back if that fails) and deletes the old. `SaveGame.Exists` puts back a `slot.old` left without its slot by a crash between the renames. `U7Game.SaveTo` / `LoadFrom` serve F5 / F9 and the console.
- Checked headless: GAMEWIN.DAT byte for byte as the old writer's; `save b` twice, `load b`, `save c`: b and c identical; GWIN.DAT replaced and a stray file kept on a `quest4` copy; a slot left as `.old` loads; a save over a slot with a file held open fails with the old slot unchanged, and the next save succeeds and clears the `.tmp`; GAMEWIN.DAT cut to 22, 14 and 10 bytes loads, cut to 6 is refused (Exult throws there too).
- **Not byte for byte across a load:** the reload runs a few frames before the console's next command, so NPC.DAT can drift (a frame, an item picked up), already so with the old writer. Animated objects (D7) also move between two commands, so `save a` then `save b` can differ in a frame byte.

### ~~R11. `Check_weight` and `get_outermost` ported twice~~ (done 2026-10-09)

- `Inventory.CheckWeight(obj, onto, catalog)` is Exult's `Check_weight`; `GumpManager.RefuseDrop` and `U7Game.CheckWeight` (which keeps the cursor flash) call it. GumpManager's and the console's private `Outermost` copies are gone (`Inventory.Outermost`). Not play-tested: the console's `put` skips the drop checks, and only a mouse drop reaches them; the expression is unchanged.

### ~~R12. Repeated guards and calls in U7Game~~ (done 2026-10-09)

- `UsecodeRunning` replaces the inline checks (U7Game and the console's `talk`, `call`, `save`). `TryCallUsecode(fun, item, ev)` serves `PathWalk.ActivateDoor` (which gains the panel refresh), `UsecodeAction.Call` and the schedules' `CallUsecode`; the spell cast and the death callbacks stay unguarded, as they were on purpose. One `ToggleCombat()` serves the combat button and the C key.
- `CanWalk` is one predicate for the held left button, the keys, the left quick click's path and both right-button handlers: no usecode, no `dont_move`, and `CombatSchedule.CanAct` (Exult's `avatar_can_act`; Exult's `start_actor` also refuses a sleeping or paralysed avatar). **Behaviour change towards Exult:** a sleeping or paralysed avatar no longer walks with the left button or the keys.
- Checked headless on `britain1`: `rmouse` walks, then refuses while the avatar is asleep, then walks again; a walk through the castle's east door both ways opens and closes it; a minute of the schedules at 11:00 ran without an exception. The left button and the keys can't be driven headless.

### ~~R13. The conversation panel is refreshed by 20 manual calls~~ (done 2026-10-09)

- `ConversationPanel` keeps a snapshot of what it was last built from (the wait, whether usecode runs, the text, the speaking and last face, both faces, the answers as a hash) and `_Process` refreshes when it changes, before its `!Visible` return (refreshing is what shows it). A path that changes the VM without refreshing no longer leaves the panel stale. The manual calls stay: they make the panel right in the same frame, and the console's `cont` reads the pages a synchronous `Refresh` sets up; `Refresh` is idempotent, so the snapshot only refreshes again after a real change.
- Checked headless from `quest4`: Iolo's talk with the leave menu and the map quiz give the same transcript as before, line for line.

### ~~R14. Left-button state in several flags~~ (done 2026-10-10)

- One `LeftPress` (None, Walk, Consumed, Book) replaces `_suppressWalk`, `_walkPress` and `_bookPress`: every press says where it went, the held walk and the cheat teleport need a press on open ground (`Walk`), a press made while reading keeps `Book` until it is let go, and the state goes back to `None` once the button is up. **Changes, towards Exult:** a press that never reached the game (a GUI control, the new game screen, a cursor flash holding it) no longer lets Shift teleport, and no longer blocks the right button's walk while the left is held (Exult's main loop steers with the right button whatever the left does).
- The input moved to a partial `U7Game.Input.cs`: the key and mouse handler, both buttons' walking, the cursor, books and pictures waiting for a click, the Wizard Eye, and the drops on the map (Exult drag.cc). The drops stay methods of U7Game rather than a class of their own: they use the camera, the cursor, the world view, the map, the eggs and the combat quantities, which a class would have to be handed one by one. U7Game.cs is half its size (1,031 lines).
- Checked headless with real mouse and key events fed to the game (`Input.ParseInputEvent`, a temporary console command), before and after: a held left walk on open grass (9 tiles east in 1.5 s), Shift and the held left teleporting (repeatedly while held, as before), a quick left click walking a path, a right double-click walking a path, a click turning a book page, Shift and a press on a rock starting a drag instead, and the C key toggling combat: the same results.

### ~~R15. Gump contents: paint order and drops duplicated~~ (done 2026-10-09)

- `Gump.PaintOrder` (the loose things, then the readied ones over them, not the one dragged) serves `PaintContents` and `FindObject` (the last one under the mouse is the top one); `GumpView.Texts` doesn't depend on the order and keeps its loop. The drop block is one `Gump.DropOnto` since D23. Layout during paint stays, a literal `Gump::paint`.
- Checked headless: naming things in an open backpack and on the paperdoll (the Orb of the Moons, a stack of 100 gold, the armour) and the gump itself.

### R16. (withdrawn) Blocking counts capped at 3

- [ChunkBlocking.cs](../godot/scripts/Data/ChunkBlocking.cs) (~129) `Math.Min(3, count + 1)` copies Exult's 2-bit `Set_blocked_tile` / `Clear_blocked_tile` (chunks.cc ~70-108), which saturates the same way. Dropping the cap would move *away* from Exult, so it stays. Nothing rebuilds a chunk after `Need` builds it (~45-68), so the live counts can't drift from a rebuild in practice.

### ~~R17. Frame extents read from two sources~~ (done 2026-10-09)

- `ShapeCatalog` opens SHAPES.VGA once (`ShapesVga`) and fills `ShapeRecord.Frames`, `FrameCount` and `TileShape` from it, a terrain frame as the catalog's (0, 0, 7, 7); ShapeCache paints from the same `VgaShapeFile` (it used to parse the file a second time). The game no longer reads `shape_frames.csv` (the Python tools still do).
- Checked: one run loading both and comparing every shape: all 14,171 frames (10,286 RLE, 3,885 terrain) equal, and every shape's frame count and tile flag.

### ~~R18. Fallbacks to the bogus extracted PNGs~~ (done 2026-10-09)

- The PNG fallbacks in `ShapeCache` (gumps, fonts, faces, a face's frame 0) and their fake sizes in `GetGumpFrame` / `GetFontFrame` are gone, and with them `U7Paths.GumpPng`, `FontPng`, `FacePng`, `GumpsDir`, `FontsDir`, `FacesDir`. The VGA decode wraps frame numbers, so only a shape missing or broken in the VGA file reached a fallback.
- Checked: a temporary console command decoding the VGA frame of every extracted PNG (4,194 gump, 1,727 font, 5,455 face): none failed, so the fallbacks could never load anything; the conversation's faces, the paperdoll and a book work as before.

### ~~R19. Effects on two clocks~~ (done 2026-10-09)

- `EffectsManager.Update` runs one time queue for the weather and the SPRITES.VGA animations: each entry has an absolute `DueMs` and a sequence number taken whenever it is (re)scheduled, and the one due first runs next, equal times first in, first out (Exult `Time_queue::add`'s `upper_bound`); a long frame still catches up step by step. Before, all weather events of a frame ran before any animation step (an explosion's quarter-way blast among them), and equal-time weather events went newest first.
- Checked headless: a storm (`weather 2 3`) with the teleport and another sprite running: the sprites step every 200 ms from their first frame at once, the storm's lightning flashes, its rain ends gradually, a usecode lightning keeps flashing after it, no error.

### ~~R20. Small UI duplicates and leftovers~~ (done 2026-10-10 but for the barge list, cosmetic)

- ~~`SignGump.Translation` rebuilds its strings with LINQ on every `_Draw`~~ (done 2026-10-09: worked out once in the constructor; Trinsic's ",rand" still reads STRAND).
- ~~The text size `Clamp(RoundToInt(6.5f * zoom), 14, 40)` is in both BarkOverlay and MouseCursor~~ (done 2026-10-09: `UiTheme.WorldTextSize`).
- ~~The gold-on-wood button styling is in both `ConversationPanel.MakeButton` and `NewGameView._Ready`~~ (done 2026-10-10: `UiTheme.StyleButton` with two `ButtonLook`s, `AnswerLook` and `PanelLook`, each exactly as it was; the answers gain hover-pressed and disabled boxes they never show). The user then chose one look (2026-10-10): the new game screen's, for the answers too (`AnswerLook` is gone).
- `Barges._all` ([Barge.cs](../godot/scripts/World/Barge.cs) ~411) is never pruned within a session (it is rebuilt on each scene load, U7Game ~362); barges are few, so this is cosmetic.

---

## 3. Performance (same output)

### ~~P1. The palette cache almost never hits, and the blend jitters~~ (fixed 2026-10-09)

- `PaletteSet.Blend` uses Exult's `from + (to - from) * t` (also for the overcast's grey), exact when both ends agree, so the one-step jitter is gone. `SceneLighting.BuildPalettes` keys `T` only when `From != To`, and copies the day palette (made once) into `Lit` instead of re-blending it. Checked: no rebuild over 20 s at noon, one at night, one per clock tick through the dawn blend.

### ~~P2. `FindNearby` reads too many chunks and sorts twice~~ (done 2026-10-09)

- Done with D6: `FindNearby` reads only the chunks the box touches and doesn't sort. The single-result lookups no longer sort a list (R7's `ClosestTo`). Multi-shape lookups still do one pass per shape, as Exult's `find_closest` does.

### ~~P3. The same chunks are scanned three times a frame~~ (mostly done 2026-10-09)

- `GameMap.LightCandidates(cx, cy)`: each chunk's objects the lighting looks at, every actor (carried lights) and the shapes SceneLighting hands over as `LightShapes` (light sources, windows, glowing magic), in the chunk list's order, built on first use and kept by `InsertIntoChunk` / `RemoveFromChunk`, dropped by `SetShape` when a shape change makes or unmakes one (Exult keeps its chunks' light counts the same way). `LightSources.Level` and `SceneLighting.FindLights` walk those instead of every object.
- Checked headless with a check beside it (every chunk `FindLights` visited, the kept list against a fresh filter of the chunk): 98,315 checks over Britain from 18:00 to 22:00 with NPCs about and a lamp post lit and put out by hand (889 to 526 and back: the level 0, 552, 0), no difference.
- **Left:** WorldView's paint pass still walks every object (it paints them), `WorldView.IsPainted` is still a copy of the painter's checks, and the roof lift and dungeon test are still worked out in both WorldView and SceneLighting.

### ~~P4. Extra passes in the world upload~~ (done 2026-10-09)

- The two clears are gone (the terrain copy covers every pixel and its glow; `GetFlats` is always a whole chunk), and with them the then unused whole-buffer `Fill8` and `ClearGlow`. `Upload` hands the buffer's index and glow arrays to two R8 textures, the glow one as the shader's `glow_tex` (both read with `texelFetch` at the same texel, as the RG8's two channels were), so the per-pixel interleave loop is gone. The six look constants are set once in `_Ready`; the nine per-frame parameters use cached `StringName`s.
- Checked windowed (1280x720, zoom 4): `britain1` at 22:00 in the throne room, the torch lit and the teleport sprite (7) glowing over the avatar, shot with the old and the new `WorldView`: no pixel differs but the bark and the actors that moved between the runs; the sprite's 567 glowing pixels match.

### ~~P5. Every actor pixel takes the translucent path~~ (done 2026-10-09)

- `IndexBuffer8.PaintRleTranslucent` finds a run's first see-through colour with the vectorised `IndexOfAnyInRange(0xEE, 0xFE)` and copies everything before it at once (indices and glow), going pixel by pixel only from there; most actor runs have none. Missiles and sprites share the path. (No flag at decode was needed.)
- Checked windowed with the old loop run beside it into scratch copies: 122,830 painted runs (Britain by night and day, the party, a guard, the teleport sprite), no byte different in pixels, glow or panes.

### ~~P6. A* scans whole chunks for doors~~ (done 2026-10-09)

- `GameMap.FindDoor` walks a per-chunk door list (Exult `Chunk_cache::find_door`'s door set), built from the chunk's objects on first use (loading fills the lists directly) and then kept by `InsertIntoChunk` / `RemoveFromChunk`, which append and remove as the chunk list does, so the doors keep its order and the first match is the scan's; `SetShape` drops a chunk's list when a shape change makes or unmakes a door. `FindBlocking` stays a full scan, as Exult's `find_blocking`.
- Checked headless on `britain1` with both lookups run side by side: 30,000 calls from the schedules (08:00, 18:00, 22:00) and walks through the castle's doors, no disagreement; the lookups took 2.6 ms in all against 18.9 ms for the scan.

### ~~P7. Usecode VM overhead~~ (done 2026-10-09)

- `CallIntrinsic` keeps only the id and argument count (`LastIntrinsic` is formatted when the debug text asks) and makes zeros only for the parameters not passed; a shared zero stays out, as the VM changes values in place. The write-only `IntrinsicLog` is gone; `FlagLog` is a ring of (offset, value) formatted on read. The run loop keeps a reference to the frame it runs (`LastFunctionId`, `LastIp`, `LastEvent` read from it) instead of three writes per instruction.
- `UsecodeFunction.GetString` decodes each string once, by offset.
- `InUsecodeControl`, `HasScript` and `TerminateScripts` first ask a set of the objects that have scripts (kept where scripts are added and pruned), so most NPCs are answered at once and nothing is allocated; the ordered list stays for the rest.
- Checked headless: a new game's opening to Iolo joining (the avatar under usecode control throughout), a conversation, a death and the wake in Paws, and Britain's wake-up hour, all as before and without an error.

### ~~P8. Per-frame allocations in actors~~ (done 2026-10-09)

- `NpcTimers.Update` collects the due timers into a reused list. `IsHostileNearby` walks `ScheduleRunner.NpcBrains`, `MonsterBrains` and `AvatarBrain` as they are (no `Concat` / `Append`); the visible rectangle stays its filter, which is what Exult's `get_nearby_npcs` amounts to. Equipment's hands, bags and last spots and `SetHandToHand`'s spots are static arrays (`FindWeaponAmmo` already had one; `UnequipSlot` went with R5); `MoveAside`'s directions, which depend on the facing, a stack span.
- Checked headless on `britain1`: poison and sleep wear off on their timers; with combat off the cursor turns from fast (100 ms) to medium (200 ms) while the arena's rats fight and back once they are dead.
- **Left:** Exult's god-mode skip and `get_effective_alignment` (D13).

### ~~P9. Map work~~ (done 2026-10-09)

- `EnsureChunkOrdered` works out every object's `RenderOrdering.Info` in the chunk and its eight neighbours once and hands the arrays to `AddDependencies`, which used to build the other object's info for every pair (each chunk's first ordering: n x N builds, now N). A single object entering a chunk still builds them as it goes.
- `FastPathClient.IsGrabable(TileCoord)` is Exult's spot version: a 1x1x1 `Block` with the spot's `distance` and client box (`FastPathClient(map, from, TileCoord, dist)`), no longer a made-up `U7Object` that used up an object id each call and took the object versions of both (a slightly different box and height, so also a small step towards Exult for drops on the map).
- Checked windowed: three scenes (the throne room by night with the torch and the teleport sprite, rooms with rugs, a bed and racks, a fenced field) shot before and after: identical but for a bark and the actors that moved between the runs.

### ~~P10. Debug work in the game loop~~ (done 2026-10-09)

- Removed: `U7Game._Ready`'s load-time prints (the walk over all 36,864 chunks counting filled containers, the nearby NPCs and eggs; the console's `look` and `eggs` tell the same), `GameMap.LogEggSummary` (the same kind, on every map load), and the prints in `EffectsManager.Add` and `CombatEngine.Place` (the spawn stays in `LastMessage`). Combat's prints per hit, missile and death stay: they are events, and handy in the headless logs.
- `MusicPlayer` keeps every MIDI file it has read, by path and sequence, in a static cache (a load reloads the scene); the playing thread only reads the events. Not exercised: the console runs with music off.
- **Left, as advised:** the HUD string built every frame.

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
