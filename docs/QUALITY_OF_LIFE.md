# The Black Gate, played today

A modernization design from the player's chair. Proposal, 2026-09-10.

This document describes what a person sitting down with the remake experiences: what they install, what they see, how they move, talk, fight, get lost, save and come back. The engine underneath is the Exult-derived simulation already in this repository, and nothing here changes what the game *is*; the mechanics, the world, the writing, the schedules and the art are the 1992 game. How that is delivered is what changes. Engine-side consequences live in [ENGINEERING.md](ENGINEERING.md) and are not repeated here.

## 1. Four promises

1. **You are playing in two minutes.** Point the game at your GOG folder and it runs. No DOSBox, no extraction step, no memory managers, no reading a README.
2. **The interface never fights you.** Everything the original made you fight (tiny text, drag-and-drop with no undo, a party that wanders off in combat, no map with your position) is fixed, and every fix is a convenience, not a new rule.
3. **You never lose progress.** Unlimited saves, quicksave, autosave, and death is a setback rather than a lost evening.
4. **It is still Ultima VII.** Same world, same words, same items, same schedules, same tile-by-tile walk, same 256-colour art. The remake removes the 1992 friction and keeps the 1992 game.

Every feature below says whether it is on for everyone, on by default and switchable, or off by default. A player who wants the original can switch to a **Classic** preset in one click and get exactly the Exult behaviour.

## 2. The first ten minutes

### Installing

The player owns *Ultima 7 Complete* on GOG. They download the remake, run it, and see one screen: "Where is Ultima VII?" with the GOG install already found on Windows, a folder picker otherwise, and a green check once the files verify. That is the whole setup. Nothing is copied or converted; the game reads the originals in place.

Today: install Godot, Python, a pip package, run an extraction script, unpack the Exult source, open the editor. The remake is only runnable by its developer.

### The main menu

The original title art and menu music. Four choices: **Continue** (straight into the latest save, present only when one exists), **New Journey**, **Load**, **Options**. Credits and the intro are under a small "Watch" link so that the second time through nobody sits through the Guardian again.

### A new journey

Name and gender, then the intro: the Guardian's face on the monitor, his voice, the moongate. It can be skipped with any key after the first viewing. The player arrives in Trinsic at 6:00 with Iolo beside them, exactly as in the original. The first thing they see is a brief, dismissable overlay of the four controls that matter: walk, use, inventory, talk. It appears once.

## 3. Looking at Britannia

### The screen

The world fills the whole window at whatever size or aspect the monitor has. No 4:3 letterbox, no stretched pixels. Art is scaled by whole numbers so every 8×8 tile is crisp, and the zoom (mouse wheel, or a button on the controller) goes from "the original 320×200 blown up" to "see the whole town". A 27-inch 1440p screen at 4× shows a bit more than twice the original view, which is the sweet spot: more Britannia, no loss of readability.

### Walking looks like walking

In the original, everyone jumps one tile every fifth of a second. The remake slides sprites between tiles over that same fifth of a second, so walking is smooth, while the walk cycle frames still tick as they always did. Positions, speeds and collisions are unchanged; a player timing a guard's patrol gets the same answer. *On by default, switchable to the original hop.*

### Night falls the way it did on a VGA card

The current remake tints the daytime art blue at night. The modernized game brings back the real palettes: dusk goes amber in steps, night is the deep blue with lit windows, dawn comes up warm, dungeons are black outside the torchlight, water and fire ripple through their palette cycles, magic and glass are translucent. Weather darkens the palette and rain falls. This is the single biggest visual improvement and it is *more* original, not less. *Always on.*

### Optional touches

Off by default, each a single checkbox: soft glow from torches, fireplaces and lamps at night; a gentle CRT scanline filter; a smoothing filter for the in-between zoom levels. Nothing redraws the art. There are no HD sprites and no AI upscale, and there will not be.

## 4. Moving around

### Mouse

Click a spot and the Avatar walks there around obstacles. Hold the button and the Avatar keeps walking toward the cursor, faster the farther the cursor is from the centre, which is how the original mouse felt. Double-click anything to use it. Right-click is "look": the object's name and a one-line description as a tooltip, without walking.

Hovering over anything shows its name in a small label after a short delay, so a player no longer clicks every barrel to find the one with the key.

### Keyboard

WASD or arrows to walk in eight directions. **E** uses whatever the cursor is over, **Tab** cycles through nearby usable things with a highlight (doors, chests, people) so the game is playable without touching the mouse. Every key is rebindable in Options.

### Controller

Left stick walks. Right stick moves a cursor that snaps to objects and to inventory slots. **A** uses, **B** cancels or closes, **X** opens inventory, **Y** toggles combat, **Start** opens the menu, bumpers switch between party members. The whole game, including dragging items between bags, is playable on a Steam Deck.

### Doors, and the things that made walking annoying

Walking into a closed, unlocked door opens it. Walking into a locked one says "Locked" once, not on every step. The party no longer blocks doorways; a companion standing in the way steps aside. Clicking a destination across the town walks the whole way without re-clicking every screen. The camera follows without jitter.

## 5. Talking

The conversation is the same text, faces and answers, presented like this:

- The speaker's portrait on the left of a text panel at the bottom of the screen, the original bitmap font scaled up (or a clean modern typeface, a setting). The player's own portrait appears on the right while choosing an answer.
- Answers are a vertical list. Click, press a number, or use the stick. The list remembers which answers were already chosen and dims them, which is how you know you have exhausted a topic. Nothing is hidden; every keyword the original offered is still there.
- Text appears at a chosen speed, instant by default, and a click advances. A player never misses a line because a paragraph was replaced before they finished reading.
- A **conversation log** (one key) shows everything said in this session, so the name of the innkeeper's cousin's ship is one keypress away instead of a notebook away.
- When a line has recorded speech (the Guardian, Lord British), it plays and the text waits for it. *Speech on by default; subtitles always.*

Barks over people's heads, "Thou art in my way!" and shop greetings, are drawn in the same bitmap font, positioned as in the original.

## 6. Things: inventory and containers

The paper doll and the container windows are the heart of Ultima VII and they stay, with the original art, the original drag-and-drop and the original rules for weight, volume and what fits where. What changes is the friction around them.

| Pain in 1992 | Now |
|---|---|
| Windows open on top of each other in a pile | Windows open beside the last one and remember where you put them; a key closes all |
| No way to know what an item is without picking it up | Hover shows name, quantity, and for weapons and armour their numbers |
| Finding the key in a bag in a backpack in a chest | **Highlight** (Alt, or a controller button) outlines every takeable item on screen and in open windows; the search box in a container filters by name |
| Counting gold by opening the bag | Gold, food and weight are always shown in a small strip under the party portraits |
| Dropping something in the wrong place, or on a roof | A drop that would fail shows why before you let go ("too heavy", "no room", "can't reach"), and a dropped item is highlighted for a moment so it is not lost in the grass |
| Splitting a stack means a typed number | Drag with a modifier for one, or a slider for a count |
| Equipping means finding the right slot | Double-click any wearable to put it on in its proper slot, or drag as before |
| Every companion's inventory is a separate hunt | The party portraits open each member's paper doll; items drag between members directly |

Nothing about *what* can be carried, worn or stacked changes. The 2 × strength stone limit is the limit; the remake only tells you about it before rather than after.

## 7. Fighting

Ultima VII's combat is famous for being a scrum where the player cannot tell what is happening. The rules stay: same hit chance, same damage, same swing timing by dexterity, same weapons and armour. What changes is that the player can *see* and *direct* it.

- **Health bars** above the Avatar, companions and any enemy that has been hit. The original's own bar art is used. *On by default, switchable off.*
- **Hit feedback.** A struck creature flashes, as in the original, and a short number floats up with the damage. *Numbers off by default.*
- **Targeting.** Double-click a creature to attack it; the target gets a small marker so the player knows who the Avatar is walking toward. Clicking elsewhere cancels.
- **Orders that stick.** Each companion's attack mode (the original's attack / defend / flee / berserk and friends) is set from a small panel beside their portrait, not by opening the paper doll mid-fight. A "hold" order keeps a companion where they stand, which is the single thing players asked for in 1992.
- **Combat music** starts when a hostile notices the party and ends when the last one is down or gone, as in the original.
- **Optional tactical pause.** A setting lets the player pause with the space bar, give orders, and resume. Nothing about the outcome changes; only when the orders are typed. *Off by default.*
- **Bodies, blood and loot.** The fallen leave bodies that can be searched, with a highlight so the loot is not missed.
- **Being arrested** by guards for theft or violence works as in the original, including the trip to the cells.

## 8. Your companions

Party members follow in the original formation and step aside in doorways. Their portraits sit at the top of the screen with health, and a colour on the portrait warns when one is hungry, poisoned or asleep, replacing the original's habit of complaining in text you may have missed.

- Click a portrait to open their paper doll; drag food onto the portrait to feed them.
- "Wait here" and "Follow" are on the same panel as the attack mode.
- A companion who dies is carried as in the original and can be resurrected at a healer or by the spell. The remake adds nothing to death and takes nothing away.

## 9. Time, sleep and hunger

- The **clock** and the day are always visible in the corner, with a small sun or moon so a glance says whether the shop is still open.
- **Shops keep their hours.** When the player tries a locked shop door, the message says when it opens ("The Blue Boar opens at 9") instead of just "Locked". The hours come from the same schedules that drive the shopkeepers; the remake only reads them out.
- **Sleeping.** Use a bed and choose "until dawn", "until noon" or a number of hours, with the original fade. Waiting outdoors works the same way, with the original chance of being disturbed.
- **Hunger** warns once at "hungry" and again at "starving", with a portrait colour, and the strength penalty is the original one. The nagging at every meal-time is gone; the mechanic is not.

## 10. Finding your way

Britannia is enormous and the original shipped a cloth map for a reason.

- **The map** (M) shows the whole continent from the cloth map art, with the party's position, places the player has visited, and the moongate the player is standing near. No fast travel: the map tells you where to go, the moongates and ships take you there.
- **The journal** fills itself in. When a conversation gives a task, a name or a place ("Find Christopher's gold in the well behind the house"), the line is recorded under the town it was heard in. Exult's own auto-notes list is the source. The player can also type notes.
- **Keys** are named by what they open once they have opened it.
- **Signposts and books** are readable at any zoom; the book gump shows the page with the original font and turns pages with click or stick.
- **Moongate and Orrery** rules are unchanged; the journal simply records the phase table the moment the player reads it in the game.

## 11. Saving, dying and coming back

- **Unlimited save slots** with a screenshot, the place name, the in-game day and time, and how long the player has been at it.
- **Quicksave** and **quickload** on two keys.
- **Autosave** every in-game hour and whenever the party sleeps, kept as a rolling set of three, so a crash or a mistake never costs more than an hour of play.
- **Death** is the original resurrection at Lord British's castle, with the option to load a save instead. No permadeath, no penalty beyond the original's.
- Saves from Exult can be imported, so a player midway through Exult's version can continue here.

## 12. Sound

- **Music** plays on every platform, the MT-32 arrangements that the original's best hardware produced, with a smooth cross-fade when walking from one area's theme to another instead of a hard cut. A setting switches to the AdLib versions for players who grew up with those.
- **Sound effects.** The original AdLib effects, emulated exactly, are the default. Players who have downloaded one of the community digital packs for Exult can point the game at it and get sampled effects.
- **Speech** for the recorded lines, at its own volume slider.
- Effects are positioned: a fire crackles from where the fire is, a door creaks from the door.
- Separate sliders for music, effects, speech and ambient, and a mute key.

## 13. Comfort and accessibility

- Text scale from 1× to 3× of the original font, independent of world zoom.
- A high-contrast option for the conversation panel and tooltips.
- Colour-blind-safe alternatives for the cursor, health bars and portrait warnings.
- Everything reachable by keyboard alone and by controller alone.
- Reduced motion turns off smooth walking, palette cycling and the CRT filter in one switch.
- Pause on focus loss.
- Text speed and click-to-continue settings.

## 14. What does not change

Written down so that it is a contract, not a hope:

- The map, every building, every object placement, every NPC and every schedule.
- Every conversation, keyword and line of text.
- Movement on a tile grid at the original pace. The remake slides between tiles; it does not move faster.
- Combat rules, weapon and armour numbers, experience, training and levelling.
- Weight and volume limits, what fits in what, what can be worn where.
- Hunger, poison, sleep, resurrection and the Fellowship's plots.
- The palette art at its original resolution.
- The music and the sound effects, from the original data.
- Reagents, spell casting, the spellbook's pages, the moongates and the Orrery.
- The ending.

## 15. Settings at a glance

| Setting | Classic preset | Default | Notes |
|---|---|---|---|
| Smooth walking | off | on | sim pace unchanged |
| Real palettes, day and night | on | on | always on |
| Torch glow | off | off | |
| CRT filter | off | off | |
| Font | original bitmap | original bitmap | modern face available |
| Text speed | original | instant | |
| Hover names | off | on | |
| Item highlight key | on | on | Exult has it |
| Health bars | off | on | Exult art |
| Damage numbers | off | off | |
| Tactical pause | off | off | outcome unchanged |
| Companion order panel | off | on | |
| Shop hours in door message | off | on | read from schedules |
| Hunger nag | original | reduced | penalty unchanged |
| Map with position | on | on | Exult has it |
| Auto-journal | off | on | Exult auto-notes |
| Autosave | off | on, hourly | |
| Speech | on | on | subtitles always |
| Music | MT-32 arrangement | MT-32 arrangement | AdLib available |
| Sound effects | AdLib emulated | AdLib emulated | digital pack if present |

"Classic" is the Exult experience. "Default" is what a first-time 2026 player gets. Either is one click.

## 16. A note on what is underneath

Every item above is presentation or convenience on top of the simulation that already exists in this repository, which is a function-by-function port of Exult. Nothing in this document requires a rule to change, and several items (real palettes, the map, item highlight, auto-notes, the attack modes) are Exult features that the port has not surfaced yet. The order of work, the technical choices and the risks are in [ENGINEERING.md](ENGINEERING.md).
