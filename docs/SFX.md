# Black Gate's sound effects

The list of every sound effect number Black Gate plays, named, with where it
is used, for replacing the MIDI effects with recorded ones (WAV or MP3).

## How the numbers work

- The game asks for effects by **Black Gate's numbers** (0-116). Exult's own
  names for them are the comments of `audio/conv.cc` (`bgconv`), reproduced
  below. Serpent Isle numbers its effects differently; `bgconv` maps each
  BG number onto SI's, and that is also the index into `midisfx.flx`
  (`SoundEffects.BgConv`, `MidiIndex`). A replacement set should be made by
  **BG number**, one file each (`000.wav` ... `116.wav`), so the engine
  needs no table.
- Exult's own digital packs for Black Gate, `sqsfxbg.flx` (Simon Quinn,
  recorded from a Roland MT-32, 117 entries) and `jmsfx.flx` (Joseph Morris,
  from a SoundBlaster, 115 entries: 115 and 116 are out of range), are flex
  files of WAVs indexed by these BG numbers. They are recordings of the
  originals' sounds, so Exult doesn't put them on its download page; its
  all-in-one `exult_audio.zip` on SourceForge carries them, and
  `scripts/get_sfx_packs.py` puts them in `assets/sfx`, where the game plays
  them (Roland first, Exult's order; `sfx pack` in the console).
- Numbers mapped to SI entry 135 (`???` in Exult's table) are silent today.
- Each effect is used from one of four places: **Engine** (Exult's C++,
  ported in `godot/scripts`), **Weapons** (WEAPONS.DAT: a swing sound
  `sfx` played part way through the attack and a hit sound `hitsfx` where
  the blow lands; MONSTERS.DAT gives no monster a sound in BG), **Shapes**
  (Exult's `shape_info.txt`: sounds that animated shapes make, with the
  chance per animation step and the volume, and the explosions' sounds) and
  **Usecode** (`play_sound_effect` / `play_sound_effect2` with a constant;
  calls through a variable or inside usecode scripts are not in this
  list). Black Gate's map has no sound eggs (all 2633 eggs of INITGAME.DAT
  are of other kinds), so there is no fifth place.
- The ambient noise (birds by day, crickets by night, outside) is Exult's
  `Background_noise`; the grandfather clock's hourly chime is the
  `extra` of `shape_sfx`.

## The list

Rows marked "no use found" are in Exult's table but nothing in the data or
code reaches them by a constant; they may still be played through a usecode
variable, so a complete set covers them too.

| BG # | Exult's name | What it is used for |
|---:|---|---|
| 0 | Bow Twang | Swing of bow, crossbow, magic bow, triple crossbow, thrown arrow; a bee's sting |
| 1 | Missile | Hit of every ranged weapon landing (arrow, bolt, boomerang, blowgun dart, sling stone, musket ball); usecode: NPC 2's function |
| 2 | Blade | Swing of every edged weapon: daggers, knives, swords (plain, magic, serpentine, two-handed, custom, decorative, defense, Glass Sword, Black Sword, Magebane, Death Scythe), spears, axes, halberd, main gauche, cleaver, scythe, hoe, rake, pitchfork, shears, throwing axe, starburst; rats' and spiders' bites. Usecode: shutters opening and closing (shapes 290, 291, 322, 372) |
| 3 | Blunt | Swing of hammers, two-handed hammer, club, mace, morning star, pick, shovel, torch and lit torch, Fellowship staff, flaming oil; corpser, hawk, scorpion attacks |
| 4 | Hit | A melee blow landing (Exult `play_hit_sfx`); WEAPONS.DAT hit sound of daggers, swords, hammers, axes, spears, mace, morning star, whip, torch, tools, Hoe of Destruction, juggernaut hammer, fire sword, great dagger; usecode 0x70F |
| 5 | Graze | A blow that does no damage, or that the armour's immunity turns (a metal clang) |
| 6 | Rotating | Swing of blowgun, boomerang, magic boomerang |
| 7 | Explos #1 | Explosion of the burst arrow (shape 554) and starburst (565); swing of the reaper; hit of ether and fireball |
| 8 | Explos #2 | Explosion shape 78; swing of cannon, lightning, lightning wand; usecode: the mage (shape 154), 0x697, Erethian's spell 0x6A0 |
| 9 | Explos #3 | Explosion of the delayed blast (621), cannon (702) and powder keg (704); usecode 0x697, 0x6F7, 0x6F8, 0x6FD, 0x887 |
| 10 | Whip pta | Swing of the whip; usecode 0x810 |
| 11 | Thunder | Swing of the lightning whip; hit of the death vortex and lightning whip; energy field (shape 768, 20% chance, low volume) and shape 1010; usecode 0x824 |
| 12 | Fireball | Swing of dragon breath, explosion, fire bolt, fire sword, fire wand, fireball, firedoom staff; hit of the fire wand |
| 13 | Torches | No use found |
| 14 | Gumps!!!!! | Any gump opening (containers, paperdolls, stats, books, scrolls); usecode: book (642), scroll (797), sealed box (798), 0x638, 0x710 |
| 15 | Gavel | Usecode: desk item (675), NPC 243, the copy-protection judgement 0x63E |
| 16 | Treadle | Hit of the shears |
| 17 | Clock tick | Grandfather clock (shapes 252, 695): tick, alternating with 18 |
| 18 | Clock tock | Grandfather clock: tock |
| 19 | Chime | Grandfather clock's hourly chime, struck hour mod 12 times (twelve at midnight) |
| 20 | Fire 1 | No use found (Exult disabled the campfire and fire field sounds as "awful") |
| 21 | Fire 2 | No use found |
| 22 | Fire 3 | No use found |
| 23 | Bell Ding | Usecode: gold coins (644), NPC 62 |
| 24 | Bell Dong | No use found |
| 25 | Log Saw | No use found |
| 26 | Mill Stone | The mill stone (shape 711, 50% volume) |
| 27 | Key | Usecode: lockpick (627) and key (641): locking and unlocking |
| 28 | Lever | Usecode: the stocks (708), switches (788), levers 0x6AF-0x6B7, 0x70D, 0x816 |
| 29 | Roulette | No use found |
| 30 | Creeeeaack | Doors opening (door shapes 225, 246, 250, 270, 376, 392, 432, 433; 0x81F, 0x820) |
| 31 | Creeeeaack | Doors closing (same functions) |
| 32 | Portcullis | No use found |
| 33 | Portcullis close | No use found |
| 34 | Drawbridge | No use found |
| 35 | Drawbridge | Usecode: the guillotine (734) |
| 36 | Fuse ??? | WEAPONS.DAT hit sound of the Glass Sword (Exult plays 37 instead); silent today |
| 37 | Shadoobie | The Glass Sword's hit (shape 604, `play_hit_sfx`); usecode: gem (760), 0x626, 0x6F9, 0x70C |
| 38 | Splash | No use found |
| 39 | W. Anchor | No use found |
| 40 | D. Anchor | Usecode 0x694, 0x695 |
| 41 | Creeeeaack | No use found |
| 42 | Creeeeaack | No use found |
| 43 | gumpster | A monster casting a fire field in combat (Exult's "weird noise") |
| 44 | gumpster | A monster turning invisible in combat |
| 45 | Forge | A blacksmith's hammer blows (the forge schedule) |
| 46 | Douse | Hit of the lit torch; usecode: lit lamp (526), bucket (810), An Flam 0x642, 0x68E, 0x693, 0x6FC, 0x839 (lights going out, fires lit) |
| 47 | Bellows | No use found |
| 48 | Fountain | Fountains (shape 893, 40% volume) |
| 49 | Surf's up | Surf along the shore (16 shore shapes, 20% volume) |
| 50 | Stream | Water (shape 794, 30% volume) |
| 51 | Waterfall | No use found |
| 52 | Wind ??? | The Well of Souls (748), smokebomb (769), shapes 900 and 902; usecode 0x70A "I summon thee!"; silent today |
| 53 | Rainman ??? | No use found |
| 54 | Swamp 1 | Swamp bubbles (shapes 334, 335) and slimes (529): one of 54-58 at random each step; swing of the sling |
| 55 | Swamp 2 | Bubbles and slimes |
| 56 | Swamp 3 | Bubbles and slimes |
| 57 | Swamp 4 | Bubbles and slimes |
| 58 | Swamp 5 | Bubbles and slimes |
| 59 | Waterwheel | The water wheel (shapes 942, 951, 80% volume) |
| 60 | Eruption ??? | The earthquake, once (the opening's quake) |
| 61 | Crickets | Crickets at night outside (ambient); swing of insects |
| 62 | Thunder | Thunder with each lightning flash (storms); usecode: seat (292), Rudyom's Wand (771), 0x60F, 0x697, 0x698, Erethian 0x69E and more (12 functions) |
| 63 | Whirlpool | Explosion of the death vortex (shape 639) |
| 64 | Heal | Casting heal |
| 65 | Spell | Explosion of the delayed blast (399), swordstrike (287) and fire bolt (856); swing of most attack spells and magic (charm, curse, paralyze, poison, sleep, swordstrike, ether, Hoe of Destruction, Magician's Wand, juggernaut hammer, gazer, ghost, sea serpent attacks); hit of the poison dagger |
| 66 | Spell | Swing of dispel field, enchant missiles, great dagger, gremlin; usecode An Jux 0x650 |
| 67 | Wizard ??? | Swing of death bolt, death vortex, delayed explosion, energy mist, flamestrike; hit of the Death Scythe; usecode: invisibility dust (790), hourglass (839), statue (854), NPC 23, 0x697, 0x698, 0x69F, 0x6F9 |
| 68 | General | Swing of destroy trap, dispel magic, douse, glimmer, ignite, light, reveal, telekinesis, unlock magic; usecode: potions (340), plaque (820), 0x69A, 0x69C, 0x6A1, 0x6A2, 0x6F8 |
| 69 | Fizzle | Usecode 0x606 (a spell fizzling) |
| 70 | New Spell | No use found |
| 71 | MPdrain | Hit of Magebane |
| 72 | MPgain | Usecode: venom (649) |
| 73 | Footstep L | A gump button pushed (not the checkmark); usecode 0x837 |
| 74 | Footstep R | An object dropped (into a gump or on the map); the checkmark closing a gump |
| 75 | Success | Usecode 0x92C |
| 76 | Failure | A drag refused: "Too heavy" and the other cursor flashes; usecode 0x8FD |
| 77 | Moongate | Moongates (shapes 157, 776, 777, 779, 80% volume) |
| 78 | Moongate B | The Black Gate itself (shape 305, 80% volume) |
| 79 | Entity Hum | The ethereal monster (230); the Vortex Cube (786, 70% volume) |
| 80 | Entity Hum | The sphere generator (234-236, 80% volume) |
| 81 | Entity Hum | The tetrahedron generator (242-244, 60%), shapes 968 and 981 |
| 82 | Shreik | Birds by day (ambient, with 85); the cube generator (238-240, 80% volume) |
| 83 | Slap ??? | Usecode: NPC 239, NPC 243, 0x810; silent today |
| 84 | Oooffff ??? | No use found |
| 85 | Bird ??? | Birds by day (ambient, with 82) |
| 86 | Blocked !! | An NPC dying or knocked out (the kneel in Exult's `lay_down` script); swing of the spiked shield |
| 87 | Furl | No use found |
| 88 | Unfurl | No use found |
| 89 | MISSING | No use found |
| 90 | Drink ??? | Usecode: potions (340), pool of water (893), 0x927, 0x928 (drinking) |
| 91 | Eat ??? | No use found |
| 92 | Whip ptb | No use found |
| 93 | Doorslam | No use found |
| 94 | Portcullis | No use found |
| 95 | Drawbridge | No use found |
| 96 | Closed | No use found |
| 97 | SpinnWheel | No use found |
| 98 | Minning ??? | No use found |
| 99 | Minning ??? | No use found |
| 100 | Shutters | No use found (shutters play 2) |
| 101 | 1armbandit ??? | No use found |
| 102 | Loom | No use found |
| 103 | Stalags | Cavern drips (shapes 389, 391, 5% chance) |
| 104 | MagicWeap | Magic weapons lying about (shapes 547, 548, 559, 562, 50% volume) |
| 105 | Poison | No use found |
| 106 | Ignite | Usecode: lamp post (889), 0x600 |
| 107 | Yo yo LA ??? | Swing of the musket |
| 108 | Wind Spell | No use found |
| 109 | Protect | Usecode Vas Uus Sanct 0x65B (Protect All) |
| 110 | PoisonSpel ??? | No use found |
| 111 | IgniteSpel | No use found |
| 112 | CradleRock | No use found |
| 113 | Beeezzzzz | Bees (shape 494, 70% volume) |
| 114 | Machines | Shape 685 (60% volume) and the orrery (767) |
| 115 | Static | Not used (no SI entry) |
| 116 | Tick Tock | No use found |

## The ones heard most

Worth getting first, in roughly the order a player meets them:

1. Interface: 14 (gump open), 74 (drop / close), 73 (button), 76 (too heavy).
2. Doors and locks: 30, 31 (doors), 2 (shutters), 27 (keys), 28 (levers).
3. Combat: 2, 3, 0 (swings), 4, 1, 5 (hits and clangs), 12, 65, 67, 68 (spells and magic weapons), 7, 8, 9 (explosions), 86 (falling).
4. World: 61, 82, 85 (ambient), 49 (surf), 50 (water), 48 (fountains), 54-58 (swamp and slimes), 17, 18, 19 (the clock), 77 (moongates), 62 (thunder), 60 (the earthquake).
5. Dungeons: 79-81 (the generators' hum), 103 (drips), 113 (bees), 104 (magic weapons).

## The unused ones and Serpent Isle

Of the 41 numbers above with no Black Gate use, Serpent Isle (traced the
same way over `serpent/STATIC`: its USECODE, WEAPONS.DAT, MONSTERS.DAT,
`godot/data/si/shape_info.txt` and INITGAME.DAT, by the SI number
`bgconv` maps each to) uses these 14:

| BG # | SI # | SI's use |
|---:|---:|---|
| 13 | 65 | Explosions of shapes 399 and 856, a weapon's swing, 5 usecode functions |
| 22 | 47 | Hit of weapon 701 |
| 24 | 30 | 4 usecode functions |
| 32 | 89 | Swing of weapon 332, 3 usecode functions |
| 33 | 88 | 2 usecode functions |
| 47 | 7 | Swing of weapon 563, 1 usecode function |
| 70 | 84 | Swing of weapon 529 (slime) |
| 99 | 59 | Hit of weapons 564, 594, 598, 624; two swings |
| 102 | 73 | Hit of weapon 179, swing of 474 |
| 105 | 86 | Swing of weapon 424 |
| 108 | 131 | 1 usecode function |
| 110 | 91 | Swing of weapon 706, 2 usecode functions |
| 111 | 66 | Swing of weapon 280 |
| 115 | 255 | Weapons 698 and 994 name it, but 255 is "none": no sound |

Ten are numbering gaps with no sound in either game (`bgconv` sends them to
the empty entry 135): 29, 53, 84, 89, 92, 93, 94, 95, 96, 101.

The other 17 have a MIDI sound but no use found in either game: 20, 21
(Fire 2, 3), 25 (Log Saw), 34 (Drawbridge), 38, 39 (anchors), 41, 42
(Creeeeaack), 51 (Waterfall), 87, 88 (Furl, Unfurl), 91
(Eat), 97 (SpinnWheel), 98 (Minning), 100 (Shutters), 112 (CradleRock), 116
(Tick Tock). They can be skipped unless a usecode variable turns out to
reach one.

Serpent Isle also plays numbers of its own that no Black Gate number maps
to, so a set for an SI game is a separate list in SI's numbering.

## Idea: giving the unused sounds a home

The 17 sounds no game uses could be added to the remake, beyond Exult, as
an addition of its own (behind a console switch like the art options, and
listed in PROGRESS.md). Most of it is data: Exult's shape sound table is
ported (`Animators`, `shape_sfx`), so a sound on an object is one line in
a `shape_sfx` section of a patch `shape_info.txt` in
`godot/data/bg/patch/`, read as a layer over the game's table like the
additions already there:

| BG # | Sound | Where |
|---:|---|---|
| 25 | Log Saw | The log saw and millsaw (shapes 710, 933) |
| 112 | CradleRock | The cradles (987, 992) |
| 20, 21 | Fire 2, 3 | Campfires and fire fields (825, 895); Exult disabled them as awful on MIDI, which a recording fixes |
| 22 | Bellows | The bellows (431) |
| 98 | Minning | The mining machine (410) |

The rest need a small hook where the thing happens:

| BG # | Sound | Where |
|---:|---|---|
| 97, 102 | SpinnWheel, Loom | The sew schedule, while an NPC spins or weaves (`SewSchedule`) |
| 99 | Minning | The miner schedule's pick strokes (`MinerSchedule`) |
| 91 | Eat | The eating frames of the eat schedules |
| 87, 88 | Furl, Unfurl | The ship's sails changing (`Barge`, where the sails are already handled) |
| 38, 39 | W. Anchor, D. Anchor | The ship's anchor raised and dropped |
| 32, 33, 34 | Portcullis, Portcullis close, Drawbridge | Portcullises and drawbridges opening and closing (today whatever the usecode passes) |
| 41, 42 | Creeeeaack | No obvious home: chests or carts |
| 51 | Waterfall | No home: Black Gate has no waterfall shape |

Exult's digital packs now play (2026-10-14), so the additions can be judged
with real sounds. A set of one's own recordings (one file per Black Gate
number, `assets/sfx/bg/NNN.wav`, falling back to the pack where a file is
missing) would need a small loader beside the packs' in `SoundEffects`.

## Sources

- `exult/exult-1.12.1/audio/conv.cc` (the names), `docs/serpentsfx.txt`
  (SI's names), `objs/objs.cc play_hit_sfx`, `actors.cc`, `combat.cc`,
  `drag.cc`, `effects.cc`, `gamewin.cc Background_noise`,
  `gumps/Gump_manager.cc`, `schedule.cc Forge_schedule`.
- `godot/data/bg/shape_info.txt` sections `explosions` and `shape_sfx`.
- `u7/STATIC/WEAPONS.DAT` (`sfx`, `hitsfx`, each minus one as Exult reads
  them), `MONSTERS.DAT` (byte 17: none in BG), `USECODE` (constants pushed
  to intrinsics 0x0F and 0x86), `INITGAME.DAT` (the eggs).
