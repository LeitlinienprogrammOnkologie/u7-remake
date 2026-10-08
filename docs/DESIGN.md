# The Black Gate, expanded

New gameplay options for the remake. Proposal, 2026-09-10.

This is a catalogue of *new* systems: things Ultima VII never had, offered as options on top of the game it was. Turn-based combat, a magic system with real effects, character builds, stealth, virtue, crafting, a home, a living economy, new ways to play. Each entry says what the player gets, how it works in rules, what part of the existing Exult-derived engine it rides on, and how big it is.

Everything here is optional. The game ships with two presets, **Classic** (nothing in this document switched on) and **Modern** (the recommended bundle in section 10), and every system can be toggled on its own at the start of a journey. Quality-of-life work that does not add mechanics is in [QUALITY_OF_LIFE.md](QUALITY_OF_LIFE.md); engine work is in [ENGINEERING.md](ENGINEERING.md).

## 0. The one design rule

New systems **wrap** the engine's rules, they do not replace them. Hit chance is still `roll_to_win`, damage is still `apply_damage`, swing timing is still dexterity banked per tick, movement is still one tile per step on the same grid, objects are still the same shapes with the same flags, dialogue is still the original usecode. A new system may decide *when* those functions run, *with what inputs*, and *what happens around them*. It may add objects, flags, text and effects. It may never change what an unmodified function returns. This is what keeps Classic honest and keeps the engine shared between every mode.

Where a system needs a number the original does not have (cover, flanking, a perk bonus), the number is applied as a modifier to the input of the original roll, and the modifier is listed in this document.

---

## 1. Combat

### 1.1 Turn-based tactical combat

**What the player gets.** When a fight starts, time stops. The screen shows an initiative bar, each combatant's action pips, reachable tiles and threatened tiles on the grid. The player moves and acts with each party member in turn, enemies take their turns, and when no hostile is left in sight the world resumes in real time. It plays like a small tactics game on the Ultima VII map, with the map's furniture, walls, doors, roofs and lifts all mattering.

**How it works.**

- *Rounds and time units.* One round is five engine ticks, one second of original time. Each combatant gets **5 time units (TU)** per round. Everything costs what it costs the engine:

  | Action | Cost | Where the number comes from |
  |---|---|---|
  | Move one tile | 1 TU | one step per tick |
  | Melee or ranged attack | `max(1, round(30 / dexterity))` TU | Exult banks dexterity per tick and a swing costs 30 |
  | Cast a spell | `1 + circle / 2` TU | new; circle 1 = 1 TU, circle 8 = 5 TU |
  | Use, drink, throw, ready a weapon | 1 TU | new |
  | Open or close a door | 1 TU | new |
  | Wait | remaining TU, carried as +1 to next initiative | new |

  A dexterity-18 fighter gets two swings and a step per round; a dexterity-6 rat gets one bite and four steps. This is the engine's own attack rate quantised, so the damage output of a fight is the same as in real time, on average.
- *Initiative.* Each round: dexterity + a 10-sided roll, highest first, re-rolled every round so the order shifts.
- *Attacks* call the unchanged engine strike with modifiers added to the attacker's combat value before `roll_to_win`:

  | Situation | Modifier | Default |
  |---|---|---|
  | Attacker one lift level higher than target | +2 | on |
  | Target has a solid object of height 2+ between it and a ranged attacker | −3 (cover) | on |
  | Target is adjacent to two or more hostiles | +2 (flanked) | on |
  | Target is asleep, paralysed or unaware | +5 | on |
  | Attacker moved 3+ tiles this turn before a ranged shot | −2 | on |

- *Reach and line of sight* use the engine's weapon reach table and its existing blocking test, tile by tile, so a bow shot down a corridor behaves as the world says it should.
- *Opportunity attacks.* Leaving a tile adjacent to a hostile that still has TU gives it one free swing at the normal cost. Off by default; on in the Hard preset.
- *Enemies* use the engine's existing combat AI to choose targets and paths, executed in turn order instead of per tick.
- *Companions* are player-controlled by default; any member can be set to "act on their own", in which case their existing attack mode drives them.
- *Non-combatants* freeze during rounds. Between rounds the scheduler advances one tick so the town keeps living at one fifth speed while you fight.
- *Escape.* Combat ends when no hostile has sight of the party for a full round, exactly the engine's disengage rule.

**Engine reuse.** `CombatEngine` strike, damage, reach, hostility, AI target choice; `Pathfinder` for reachable tiles; `GameMap` blocking and lift for cover and height; schedules for the bystanders. **New:** turn scheduler, TU accounting, overlay UI, modifiers.

**Size:** L. **Depends on:** nothing.

### 1.2 Real time with pause and orders

The lighter option. Combat runs as the original, but space pauses it and, while paused, the player can queue one order per party member (move here, attack this, cast that, drink this, hold). Orders execute on resume through the same commands the turn-based mode uses, which is why the two modes share most of their code. A slow-motion setting (half speed while any hostile is in sight) sits between the two.

**Size:** M. **Depends on:** 1.1's command layer.

### 1.3 Stances and combat manoeuvres

A small set of things a fighter can *do* besides swing, chosen from a ring around the character. Each is a cost and a modifier, nothing more:

| Manoeuvre | Cost | Effect |
|---|---|---|
| Power attack | attack + 1 TU | +4 damage, −3 to hit |
| Defensive stance | 1 TU | +3 armour until next turn, no attacks |
| Shield bash (shield readied) | 1 TU | target loses 1 TU next round on a hit |
| Aimed shot (ranged) | attack + 1 TU | +3 to hit, ignore cover |
| Disarm | attack | on a hit, target's weapon drops at its feet; only against humanoids |
| Throw | 1 TU | throw any small object as a weak missile |

Available in both combat modes; in real time the cost is a delay in ticks.

**Size:** S. **Depends on:** 1.1 or 1.2.

### 1.4 Wounds and recovery

Optional replacement for "health is a number": at 25% health a combatant gets a wound (bleeding, limping, weak arm) that persists until treated by a healer, a bandage or a night's sleep. Wounds are modifiers again (−1 TU, −2 to hit, −2 damage). Companions comment on them. Off by default, on in Hard.

**Size:** S.

---

## 2. Magic

### 2.1 Spell effects that touch the world

**What the player gets.** Spells that look like something and do something to the world around the target. Fire lights things, cold puts them out, lightning follows water, wind moves things. The eight circles, the reagents, the spellbook and the mana costs are unchanged; what a spell *is* gets a body.

**Effects.** Every spell keeps its original effect and gains, in Modern mode, a physical footprint on the map:

| Spell family | New world effect |
|---|---|
| Fire (Ignite, Fire Blast, Fire Field, Fire Ring, Flame Strike, Explosion) | Ignites flammable objects (wood furniture, hay, cloth, books, doors) on struck tiles. Fire spreads to adjacent flammables each tick with a chance, burns for a duration, leaves ash. Burning tiles damage anyone standing in them. |
| Water and cold (Douse, Great Douse; the hidden Words spell *In Frio*, section 2.2) | Douse extinguishes fire in an area. *In Frio* freezes shallow water for a time, making it walkable, and puts out fires. |
| Lightning (Lightning, Magic Storm, Delayed Blast) | Jumps to every combatant standing in water or on wet tiles (raining), chaining once. |
| Force (Telekinesis, Tremor, Explosion) | Pushes light objects and small creatures one tile away from the centre; Tremor knocks everyone in range down for a turn. |
| Light (Glimmer, Light, Great Light) | Real palette light radius that reveals dungeon tiles and counts as light for stealth (section 4). |
| Weather (Magic Storm) | Changes the weather state for an hour, and with it schedules, stealth light and lightning chaining. |
| Poison, Curse, Charm, Paralyze, Sleep | Status effects with a visible icon and a duration bar instead of a hidden flag. |

**Visuals.** Each spell gets a particle layer and a palette flash drawn over the sprite art: a fireball is a thrown light with a trail and an explosion that briefly lights the palette; lightning uses the game's own lightning palette ramp; sleep is drifting motes. Screen shake for explosions and earthquakes. The original SPRITES.VGA animations still play underneath and are all that plays in Classic.

**Targeting.** Radius, cone and line spells show their area on the grid before the cast, with everyone inside highlighted, friend or foe. Friendly fire is real, as in the original.

**Engine reuse.** Shape catalog flags (solid, height, class) and shape names classify flammables; `GameMap` object placement adds fire objects; the palette controller does the lighting; `apply_damage` does the burning; existing status flags are the status effects. **New:** fire propagation, freeze/wet tile state, particle layer, targeting overlay.

**Size:** L. **Depends on:** the indexed palette renderer.

### 2.2 Words of Power

**What the player gets.** A second way to cast. Every Ultima spell has a name in the language of magic (Vas Flam, In Mani, Kal Vas Flam). In Words mode the player can cast any spell they know by choosing its syllables from a ring of the runes they have learned, without opening the book. More importantly, combining syllables in orders that are not in the book **discovers spells**: a set of hidden spells that exist as syllable combinations and are found only by experiment or by clues in the game's books.

**Rules.** Twenty-two syllables. A known spell's syllables are learned when the spell is learned. A combination that matches a book spell casts it for its normal cost. A combination that matches a hidden spell casts it, and writes it into the book. A combination that matches nothing costs the reagents of its highest-circle syllable and fizzles with a visible puff. Twelve hidden spells, written to fit the lore (e.g. *In Ex Por* opens locks without a key at a mana cost; *Vas Rel Por* teleports to a marked stone; *An Nox* cures the party; *Kal Xen Corp* summons a skeleton).

**Engine reuse.** The spell table, reagent checks and mana come from the usecode spell functions, called unchanged. **New:** syllable input, hidden spell table with C#-side effects, book entries.

**Size:** M. **Depends on:** nothing.

### 2.3 Enchanting

At a mage's workbench (the ones that exist in Moonglow and the Britain mage's house), combine a weapon or armour with reagents and mana to add one property: fire damage, frost, lightning, keen (+to hit), light (glows), returning (thrown weapons come back), or a virtue mark (bonus against the unvirtuous, section 5). One property per item; enchanting sets the item's quality field, which the engine already stores and saves. Enchanted items show their property in the hover text.

**Size:** S. **Depends on:** 2.1 for the elemental damage types.

---

## 3. Character

### 3.1 Backgrounds

At creation, after name and gender, choose a background from the eight Ultima professions: Fighter, Mage, Bard, Druid, Tinker, Paladin, Ranger, Shepherd. A background sets the starting stats inside the original's range (the total is the same, the distribution differs), grants one starting perk, a small kit change (a mage starts with a spellbook and two circle-1 spells; a ranger with a bow) and a handful of extra dialogue lines where it makes sense (a Tinker gets a line with Tinker-Bobby in Britain; a Bard with Iolo). The Avatar stays the Avatar.

**Size:** S.

### 3.2 Perks

**What the player gets.** A build. The original grants training points that a trainer converts to stats. In Modern mode, every second training session also grants a perk choice from a small tree per stat. Companions have their own trees, smaller, themed to who they are.

**Trees** (three tiers each, tier 2 needs one tier-1 perk, tier 3 needs two):

| Strength | Dexterity | Intelligence |
|---|---|---|
| Pack Mule: +50% carry weight | Quick Draw: readying costs 0 TU | Bookworm: reading a book once teaches its recipe / spell hint |
| Cleave: killing blow grants a free attack on an adjacent foe | Light Step: sneaking at full speed | Reagent Sense: 25% chance a cast keeps its reagents |
| Iron Stomach: hunger halved | Riposte: one free counter per round when missed in melee | Silver Tongue: unlocks persuasion lines (section 6) |
| Bulwark: shields give +2 more armour | Steady Aim: no ranged penalty after moving | Battle Mage: casting costs 1 TU less |
| Juggernaut: cannot be knocked back or held | Evasion: +2 armour vs ranged | Lore: identifies enchantments and quality on sight |
| Titan: +4 damage with two-handed weapons | Blade Dance: attacks cost 1 TU less at dexterity 24+ | Archmage: circle-8 spells cost half mana |

Every perk is a modifier on an engine input or a new small rule; none changes an original formula.

**Companion perks** (examples): Iolo, *Crossbow Master*; Shamino, *Wilderness Guide* (party moves faster outdoors and finds more reagents); Dupre, *Tavern Brawler*; Jaana, *Field Medic*; Sentri, *Veteran*; Julia, *Tinkerer*; Spark, *Street Kid* (pickpocket bonus).

**Size:** M. **Depends on:** 1.x for the TU perks to mean anything.

---

## 4. Stealth and crime

**What the player gets.** A thief's game inside the world that already has locks, keys, guards, a night, shops that close and homes that empty by schedule. Sneak, pick locks, pick pockets, fence goods, get caught, run.

**Rules.**

- *Sneaking* is a toggle: half speed, no barks, and every NPC gets a **detection check** each tick the sneaker is within their sight range: they see the player if the player is inside their facing cone (the sprite's direction is known), the tile's light level (day, dusk, night, dungeon, plus light sources and spells) exceeds a threshold, and a dexterity roll fails. Shadows matter for the first time.
- *Lockpicking* with the lockpick item that already exists: a short timing minigame (three tumblers, one press each, tempo set by the lock's quality; the door's quality already encodes the key). A failed pick makes noise (alerts NPCs within 8 tiles). Magic locks need Words or the spell.
- *Pickpocketing*: sneak up behind an NPC and use them to open a limited view of their inventory; each take is a dexterity roll against their dexterity; a fail turns them hostile or makes them call the guards (both existing engine behaviours).
- *Stolen goods* carry a hidden "stolen in <town>" mark. Shopkeepers in that town refuse them; a fence in each major town (new NPCs, four in total) buys them at 40%. Wearing a stolen unique item in its town is a crime if a guard sees it.
- *Crime and witnesses* extend the engine's existing guard-calling: a crime needs a witness with line of sight; a witness who is not a guard goes to find one (a real walk, with a schedule change); killing the witness before they reach a guard prevents the report. This is grim and very Ultima VII.
- *Reputation* per town is a number moved by crimes reported and good deeds done; it sets guard tolerance (a bad reputation means guards search you at the gate), prices, and a few greeting lines.

**Engine reuse.** Schedules, sight ranges, `call_guards`, arrest, lock quality, `okay_to_take`, the palette light level, facing frames. **New:** detection check, minigame, stolen mark, fences, witness walk, reputation.

**Size:** M. **Depends on:** the indexed palette renderer for light levels.

---

## 5. Virtue

**What the player gets.** The thing that makes an Ultima an Ultima and that VII, alone in the series, dropped: the eight virtues as a living measure of how the Avatar behaves, with the Fellowship's philosophy as the counterweight.

**Rules.**

- Eight meters, −100 to +100, moved by acts the engine already knows about: killing a non-hostile (Compassion, Justice), stealing (Honesty, Honour), lying in the original dialogues that offer a lie (Honesty), giving gold to beggars (Compassion), fleeing a fight (Valour), resting when companions are wounded (Sacrifice), completing quests for no reward when offered the choice (Sacrifice, Humility), meditating at shrines (each shrine's virtue).
- *Shrines* become usable: meditate with the right mantra (the original mantras from IV, printed in the in-game books) for a temporary bonus tied to the virtue: Valour gives +2 to hit for a day, Compassion heals the party, Honesty reveals lies in dialogue with an icon for a day, and so on.
- *The Fellowship* notices. Joining the Fellowship (an original option) and rising in it now trades virtue for access; some Fellowship-only lines and one Fellowship-only shortcut through the main quest exist. Batlin comments on the Avatar's virtue in his original conversations, through a handful of new lines.
- *Lord British* and the companions comment when a virtue crosses ±50. Dupre leaves the party at Honour −75; Iolo argues; Shamino stays. They come back if asked and the virtue recovers.
- *Avatarhood* at +75 on all eight grants the Codex line and a mark that unlocks a single new dungeon room in Deceit with a unique item. This is the only new content behind the system.

**Engine reuse.** Global flags for every act the original tracks; gold transfers; combat kills; `add_to_party` / `remove_from_party`. **New:** meters, shrine interactions, about sixty new lines of dialogue.

**Size:** M. **Depends on:** section 6 for the dialogue hooks.

---

## 6. Dialogue

### 6.1 Skill-gated lines

**What the player gets.** In conversations where the original offers "pay" or "fight" or "leave", Modern mode adds one or two lines gated by a stat or perk, marked with a small icon: *[Persuade]*, *[Intimidate]*, *[Lore]*, *[Tinker]*. They resolve to the same flags the original sets, so the story proceeds as written; they add a way to get there. Around one hundred and twenty new lines across the game, written in the original's register. Success rolls are intelligence or strength against a difficulty set per line; failure closes that route for the conversation and sometimes costs reputation.

**How it attaches.** Dialogue is bytecode the engine runs unchanged. A hook layer lets C# code observe a usecode function and its answers as they are offered, insert answers of its own, and run C# when they are chosen (setting the same flags the original branch would). Exult has the same idea in its usecode patches; this does it in the engine's language. The original text is never edited.

**Size:** M (mostly writing).

### 6.2 Companion interjections

Companions speak up in conversations where they have a stake: Iolo about Trinsic, Spark about his father, Dupre in every tavern, Shamino about the Forest of Yew. One or two lines each, triggered by conversation and flag, appearing as a bark inside the panel. Sixty lines.

**Size:** S.

### 6.3 Conversation memory and rumours

NPCs remember: a shopkeeper the Avatar stole from says so; a person the Avatar helped greets them by name. Taverns carry rumours that reflect the state of the main quest flags ("They say the Fellowship's ship sailed from Vesper last night"), which doubles as a gentle hint system. Forty lines plus the rumour table.

**Size:** S.

---

## 7. Crafting and a home

### 7.1 Crafting

**What the player gets.** The original already lets you bake bread, forge a sword at a smithy, sew, and mix in a lab, as one-off object interactions. Modern mode makes them systems with recipes, discovery and value.

| Station (exists in world) | Craft | Input | Output |
|---|---|---|---|
| Oven, fire, cauldron | Cooking | raw food, flour, water | meals that feed more and grant a small day-long bonus (+1 strength from meat, +1 intelligence from fish, mana regen from mushroom stew) |
| Forge and anvil | Smithing | ore or an existing weapon, coal, hammer | repair (quality restored), upgrade (+1 damage, once), or forge a plain weapon |
| Loom, spinning wheel | Tailoring | cloth, wool, thread | cloth armour, cloaks (cold resistance), bags |
| Lab (Moonglow, Britain, Cove) | Alchemy | reagents | potions the game already has, plus antidotes, oil (throwable fire), and a sleeping draught |
| Mage's workbench | Enchanting | item + reagents + mana | section 2.3 |

Recipes are learned from books already in the world (a recipe icon appears when a book teaches one), from NPCs who do the trade (asking the baker in Britain), and by experiment. Item quality, which the engine stores on every object, becomes the durability and upgrade counter, so weapons dull in use and can be repaired.

**Engine reuse.** Every station is an existing shape; ingredients are existing objects; quality, weight and volume are engine fields; the schedule classes that show NPCs crafting are the same animations. **New:** recipe table, crafting gump, durability loss on use.

**Size:** M.

### 7.2 A home

Buy a house. Three are for sale (the empty house in Britain by the docks, a cottage in Cove, the old farmhouse near Paws), from a named seller with a price and a deed. Once owned: containers inside are safe from the engine's ownership rules and never reset; furniture can be bought and placed with the ordinary drag-and-drop; companions not in the party go there and follow schedules of their own inside it; a crafting station can be installed; a bed there gives the best rest bonus in the game. A mailbox receives letters (section 8.3).

**Size:** S. **Depends on:** nothing, though 7.1 makes it worth having.

---

## 8. A living world

### 8.1 Economy

Shops track stock. Buying empties the shelf, and caravans (existing carts on existing roads) and ships (existing barges) restock towns on a schedule, so a town cut off by a storm or a bandit raid runs short and prices rise. Prices differ by town and by what the town produces (cheap fish in Vesper, cheap ore in Minoc, cheap reagents in Moonglow); the player can carry goods and make a margin. Bandits attack caravans the party is near, which is an encounter.

**Size:** M. **Depends on:** barges working.

### 8.2 Weather and seasons

Weather is already a state. Modern mode makes it drive schedules (rain sends most NPCs indoors and closes the docks; storms ground ships; fog halves sight ranges, which matters for stealth and combat) and adds four seasons on the calendar the game already keeps: winter palettes with snow tiles overlaid on grass, frozen lakes, shorter days; summer with long days and more fires. Seasons are purely the palette and a tile overlay, never a change to the map.

**Size:** M. **Depends on:** the indexed palette renderer.

### 8.3 Letters and consequences

Things the Avatar does come back. A letter arrives at the home or at an inn's message board: thanks from someone helped, a bounty notice for a crime, an invitation from the Fellowship, a plea from a town the player has not visited (a hook). A small table of forty letters, triggered by flags and time.

**Size:** S.

### 8.4 Dynamic encounters

The original spawns monsters from eggs at fixed spots. Modern mode adds a roaming layer: bandit camps that move, a wandering merchant, a lost child (a rescue, with the parent in the nearest town), a Fellowship patrol, a hungry wolf pack in winter. Each is a small scripted event with an existing monster or NPC type, spawned by a director that watches the party's position, the time and the season and keeps encounters rare (one every two in-game days on average, none in towns).

**Size:** M.

---

## 9. Travel

### 9.1 Sailing

Ships already sail. Modern mode gives them wind (direction from the weather state, speed by angle to the wind), sails to trim, an anchor, and sea encounters: a pirate ship that boards (a fight on two decks), a sea serpent, a drifting wreck with cargo. Naval combat is the same combat on the deck tiles, with the ship's cannons (existing objects) as a manoeuvre.

**Size:** M. **Depends on:** barges, 8.4.

### 9.2 Riding and the carriage

Horses and carts exist. Riding a horse doubles the walk pace on roads; the carriage carries the whole party and its cargo and can be hired between major towns for gold, which is the only "fast travel" and it happens in real game time, with encounters on the way.

**Size:** S.

---

## 10. Ways to play

| Mode | What it is | Size |
|---|---|---|
| **Classic** | Nothing in this document. Exult's Black Gate with the quality-of-life layer. | — |
| **Modern** | Turn-based combat (1.1) with manoeuvres (1.3), world-touching magic (2.1), Words of Power (2.2), backgrounds and perks (3), stealth (4), virtue (5), skill lines and interjections (6.1, 6.2), crafting and home (7), letters (8.3). Economy, seasons, encounters and sailing off until they are proven. | the bundle |
| **Hard** | Modern plus opportunity attacks, wounds (1.4), permadeath for companions, hunger and thirst, no autosave. | S |
| **Story** | Modern with no death (the party wakes at the last inn) and easy combat. | S |
| **Ironman** | Any preset with a single save that is deleted on death. | S |
| **Randomised journey** | Item, key and reagent locations shuffled with a seed; the main quest's required items are guaranteed reachable. For the second playthrough and for the speedrun community. | M |
| **New Journey+** | Start over with the Avatar's stats, perks, virtue and the home; the world resets. | S |
| **Photo mode** | Free camera, hide UI, time of day slider, palette picker, export at any zoom. | S |
| **Mod support** | Load Exult Studio maps and usecode patches; load C# rule modules through the same hook layer sections 5 and 6 use; a workshop-style folder. | M |

---

## 11. How it all bolts on

Every system above attaches at one of five points, and none touches the ported functions:

1. **Command layer.** Everything a character can do is a command (move, attack, cast, use, drop). Turn-based mode, pause-and-orders, and manoeuvres are schedulers and modifiers on commands.
2. **Modifier hooks.** `roll_to_win` and `apply_damage` are called with inputs that a modifier pipeline may adjust (cover, flanking, perks, enchantments, wounds). The functions do not change; the numbers going in do.
3. **World hooks.** Objects can be added, removed and flagged through the map's existing placement API. Fire, ice, stolen marks, crafted items, seasonal overlays, letters and encounters are all objects with the ordinary shapes and flags.
4. **Dialogue hooks.** A usecode function's answer list can be observed and extended, and a chosen extension runs C# and sets the flags the original branch would have. Skill lines, interjections, rumours, virtue comments and Words discovery text all use this.
5. **Event stream.** The simulation already reports what happened (moved, hit, died, took, gave, flag set). Virtue, reputation, letters, the encounter director and the economy are listeners on that stream.

Because of this, Classic is not a separate build. It is the same engine with every listener and modifier switched off.

## 12. Priority

Ordered by how much a player would notice, against cost:

1. Turn-based combat and pause-and-orders (1.1, 1.2). The single change that makes the game feel new.
2. Magic effects and targeting (2.1). The single change that makes it *look* new.
3. Backgrounds and perks (3). Gives the player a reason to care about the numbers.
4. Skill lines, interjections and virtue (6.1, 6.2, 5). Makes the writing, the game's best asset, bigger.
5. Stealth and crime (4). Turns the existing simulation into a second game.
6. Crafting, home, letters (7, 8.3). Gives the world a place to belong to.
7. Words of Power, enchanting, manoeuvres (2.2, 2.3, 1.3). Depth for the second twenty hours.
8. Economy, seasons, encounters, sailing, riding (8.1, 8.2, 8.4, 9). The living world, once the engine work that they rest on (barges, palette) exists.
9. Modes and mod support (10).

Items 1 to 4 make the Modern preset. Everything after ships as it is finished.
