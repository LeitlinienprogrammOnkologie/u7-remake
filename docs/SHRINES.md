# The Virtues: shrines, runes, the Codex and the Ankh

Design, 2026-10-13, approved by the user in conversation; nothing is built yet.
An addition to Black Gate in the remake's own layer (not Exult's rules: Exult
has no virtue system, and the original's shrines do nothing). Played as a mod
(`mods/virtues`, UCC, function numbers from 0xD00) over the engine hooks it
needs, so the original game stays reachable with the mod off.

## 1. Why

The Forge of Virtue leaves strength, dexterity and intelligence at 30, and hit
points and mana follow them, so a stat reward is worth nothing. What Ultima VII
lacks is standing: the avatar can rob Britain and butcher Paws and only the
guards notice, in a story about a cult replacing the virtues with a
counterfeit. The shrines become the place where the real virtues judge the
avatar, the rewards are abilities and sights, and the Codex and the Ankh make
the virtues what they were in Ultima IV: required to finish.

## 2. What the original data holds

Checked against `u7/STATIC` on 2026-10-13 (IFIX and INITGAME.DAT scans, the
console).

**The eight shrines** are three monoliths (shape 894, frames 2-4) round a
pedestal (718), with a party-bark egg (usecode 0x621, qualities 30-37 say
"The Shrine of X!"). None has usecode.

| Shrine | Bark egg | Pedestal | State in the original |
|---|---|---|---|
| Compassion | 1463,1370 | 1448,1384 | Cove; kept by Nastassia (NPC 75, 0x44B); a gold ring (640) on the pedestal |
| Honesty | 2687,1159 | 2680,1160 | a desk item (675:20) on the pedestal |
| Justice | 734,119 | 728,120 | a desk item (675:0) on the pedestal |
| Spirituality | 2917,2414 | | the Time Lord's: the time barrier (1021), his amulet (955), the Orb's moongate egg (0x6DB) |
| Honor | 847,2557 | 840,2552 | the harpies' nest with the stolen baby (730:2) from Serpent's Hold |
| Valor | 493,2687 | 488,2697 | a sword (599) on the pedestal |
| Sacrifice | 2316,968 | | in ruins: broken columns (544), stone chips (815), blood (912) |
| Humility | 2847,2535 | | bare (a second Humility bark egg sits at 2718,2824) |

**The relics**, all in Britain's Royal Museum (Candice, NPC 41, at 1028,1093 by
day), none marked as owned in the original data:
- the eight **Runes of Virtue**, shape 877 (8 frames: the virtues' symbols on
  stone discs, Ultima VI's look; no usecode), at 883,1329-1341 and
  908,1329-1341 under the plaque "THE RUNES OF THE VIRTUES";
- the two **lenses**, shape 726 (frame 0 and 1), at 887,1313 and 903,1313,
  under "THE BRITANNIAN LENS" and "THE GARGOYLE LENS";
- **The Vortex Cube**, shape 786, at 876,1323, under "THE VORTEX CUBE / THE
  VIRTUES".

A second, owned set of runes stands in the hidden test room at
1834-1846,1146-1158 ("The Throne of the Guardian"); ignore it.

**What the game says:**
- Finnigan (0x40C): "Our Rune of Honor was taken many years ago by one who
  claimed he was the Avatar. I believe it now resides in the Royal Museum in
  Britain, yet the empty pedestal still remains in the center of town."
  (Trinsic's pedestal 718 at 1000,2216, to confirm.)
- Lord British (0x417): "when I created the shrines of the Virtues, I also
  set upon this island three great shrines, dedicated to the Principles".
- The plaque at the Shrine of the Codex (scroll text in 0x31D, the Isle of the
  Avatar's dungeon, where the Black Gate is built): "Between these columns,
  upon this pedestal, once sat the CODEX OF ULTIMATE WISDOM. Now it lies in
  the infinite darkness of the Void... Those who would seek the wisdom
  contained therein must unite the mystic lenses in the same manner as the
  Avatar did more than two hundred years ago. Lord British."
- The thrones' riddle there (0x31D): "Throne of `Change' keeps thee at bay,
  but `Virtue' shall show the way!"
- Arcadion (0x6F6): the lenses "which thou used to place the Codex of
  Infinite Wisdom within the Void, I believe now sit forgotten in the Museum
  of Britannia." The Forge's exile of the Dark Core (0x6F8) finds them by the
  torches and leaves them there: not consumed, so both uses share them.
- Tobias (0x4B2): the Shrine of Sacrifice "I do not think it is there
  anymore". Camille keeps "the old virtues" (0x4A8, 0x4B0, 0x4B2).
- The Book of Forgotten Mantras (0x282) is a joke list; nothing in Black Gate
  teaches the mantras.
- The Forge's Shrines of the Principles (0x356) are the only working shrines:
  each test raises intelligence and magic, dexterity, or strength to 30.
- The Guardian's remarks are 0x614 (his lines, spoken from U7SPEECH.SPC);
  the finale's "So, Avatar! The moment of truth has come!" is there. The
  Black Gate is shape 305, its dais 412; the finale is 0x1FA (BRIDGE.md).
- The Virtue Stones (shape 330, class 11, eight colours, Mark and Recall)
  carry the virtues' colours.

**The engine** already has, for the effects below: CALLO (a mod adds a topic
to an existing NPC and calls its original), the status outlines and glows,
invisibility, heal and cure, moongates, the weather calls, the lightning and
explosions, the SPRITES.VGA effects and fades, light sources, the book gump
(`book_mode`), the sign gump, and the Show machinery (fades, a 320x200
picture, MedievalSharp lettering) of the endgame.

## 3. The design

### 3.1 The tallies: eight virtues, counted from acts

The engine keeps a counter per virtue in the save (with the usecode globals).
Acts and where the engine sees them:

| Virtue | Down | Up |
|---|---|---|
| Honesty | theft (`Guards`, the theft check), lying in named answers, cheating at the House of Games (0x63E's crime) | returning borrowed relics, confessing |
| Compassion | attacking the fleeing, killing animals | giving to beggars (usecode), healing others |
| Valor | fleeing combat (the party out of range of a fight it started) | fighting on at low health, winning against the stronger |
| Justice | killing a non-hostile, attacking the sleeping | handing thieves to the guards, paying the fine |
| Sacrifice | | items or gold left on an altar or shrine, the Sacrifice word |
| Honor | breaking a given word (quests accepted and betrayed, named answers) | the Rune of Honor returned to Trinsic, duels fought fairly |
| Spirituality | | meditating, the Spirituality word |
| Humility | boasting "I am the Avatar" where an answer offers it | the humble answer, coming to Humility alone and unarmoured |

The hooks: the guards' theft check and the arrest, `CombatEngine` (kills,
sleeping targets, fleeing), a hook in the VM on the chosen answer's string
against a list the mod supplies, and three intrinsics for the mod
(`virtue_add(virtue, n)`, `virtue_get(virtue)`, `virtue_rank(virtue)`).

### 3.2 Consequences

- **The party.** Companions comment on vile acts through the proximity remarks
  (new barks by virtue, CALLO over 0x621 or the companions' functions). At the
  bottom of a virtue, Dupre, Shamino or Iolo refuses to go on (leaves the
  party with a word) until the avatar atones at that virtue's shrine.
  Harshness (comments only, or walking out) is the user's call; walking out
  was proposed.
- **The world.** A fallen avatar pays the full fine at the arrest (0x625), is
  refused by healers and the honest shopkeepers (CALLO topics), and gets Lord
  British's rebuke instead of his help. A virtuous one is believed: the
  townsfolk's doubts that the stranger is the Avatar give way (a few greeting
  lines by rank), and the Fellowship's recruiters stop.
- **Atonement** at the shrine costs something: gold or a prized item on the
  pedestal for Sacrifice, a night's vigil for Spirituality (the clock
  advanced, the party waiting), confessing to the wronged for Honesty.

### 3.3 Restoring a shrine (Ultima VI's shape)

1. **The rune**, borrowed from Candice (a CALLO topic: she lends one at a
   time on a promise to return it) or taken. The mod marks the museum's relics
   as owned, so taking one is a theft the engine prosecutes, and the Shrine of
   Honesty refuses a stolen rune. After a shrine is restored Candice lets the
   avatar keep its rune, so the eight end in the pack as the tools of the
   words of power. The Rune of Honor goes back on Trinsic's empty pedestal
   first (an Honor act; Finnigan's and the town's talk change).
2. **The mantra**, from the people who still keep the virtues, as CALLO
   topics: Nastassia MU, Camille and Tobias, Lord British, the Time Lord OM,
   Candice from the museum's plaques, the healers, the gargoyles of Terfin who
   still meditate at the altars (0x4B7). Ultima IV's mantras: Honesty AHM,
   Compassion MU, Valor RA, Justice BEH, Sacrifice CAH, Honor SUMM,
   Spirituality OM, Humility LUM. Once learned, a mantra is offered among the
   answers at the shrine.
3. **The shrine's own obstacle** stands first: Honor only once the harpies
   are cleared and the baby returned (the game's own quest), Sacrifice stays
   ruined until something is sacrificed on its spot, Humility works only for
   an avatar who comes alone and unarmoured, Spirituality needs the Time
   Lord freed (the game's own thread).
4. **Meditation:** double-click the pedestal (new function on 718 by place,
   or an egg the mod places) with the rune in hand; speak the mantra; the
   shrine answers in the Forge's voice (0x356's tone). A wrong mantra costs
   the virtue a point (Ultima IV).
5. **The restoration scene:** the weather breaks (`set_weather`), night lifts
   for a moment (a fade), the monoliths kindle and the ankh beam rises
   (sprite effects, the glow). From then on the shrine's monoliths are light
   sources (`light_classes` / the mod's shape data), a landmark on the night
   horizon, and a **sanctuary**: meditating there heals, cures and restores
   the party once a day and marks the shrine's virtue stone. The ruined
   Sacrifice rebuilds itself column by column in that scene (the broken
   columns removed, monoliths created).
6. **The vision:** a painted picture per shrine (the user's painting
   pipeline, like the map and the faces; `assets/visions/`), shown in the
   Show machinery with the shrine's words in gold, and one letter of
   INFINITY.

### 3.4 The words of power

Each restored shrine gives its mantra as a word of power, spoken by
double-clicking that virtue's rune (a new shape function on 877), once a day
(a day-timer per word in the save). A word answers only while its virtue's
standing is good; a fallen virtue's word dies with a dull chime and a rebuke
until the avatar atones.

| Virtue | Word | Effect | Sight |
|---|---|---|---|
| Honesty | AHM | Truesight for a day: invisible foes, secret doors and traps shown | status outlines and a pale glow on the hidden |
| Compassion | MU | heals and cures the party and every friendly in sight; charmed and animal foes stop fighting | the healing sprite over each |
| Valor | RA | for one fight the party cannot miss and cannot be slept, feared or paralysed | the party glows red |
| Justice | BEH | every evil creature in sight is struck by lightning | bolts from a clear sky, the flash |
| Sacrifice | CAH | raises a dead companion on the spot for one point of the avatar's own strength (the only thing a Forge-perfected avatar has left to give) | a white column of light, the body stands |
| Honor | SUMM | human foes (bandits, pirates, Fellowship guards) yield and flee; experience as if beaten | the kneeling frame, then flight |
| Spirituality | OM | a moongate to any restored shrine; the party's mana restored | the blue gate, the moongate music |
| Humility | LUM | the party passes unseen for an hour | the invisible render, foes walking past |

Engine work: Truesight and Valor's fight as status effects (the combat's
to-hit and the flag timers), the rest is intrinsics the engine already has.

### 3.5 The Codex and the Ankh

1. **The lenses** from the museum, as the runes (the same honesty test).
2. **Summoning the Codex:** at the empty pedestal of the Shrine of the Codex
   (the Isle of the Avatar's dungeon; reachable by ship early) the avatar
   sets the concave and the convex lens either side, as the Forge sets them
   by the Dark Core's torches (0x6F8's manner). The Void opens between the
   columns (a painted vision) and the Codex is there. The Vortex Cube stays a
   museum piece: in the lore it sent the Codex into the Void; the lenses are
   what let both races read it from there.
3. **The first reading** (the book gump over the vision): the Codex tells the
   truth about the Guardian: he cannot be destroyed, only withstood, and no
   wand lifts against his will unless the one who holds it is the Avatar in
   truth. The way is the eight shrines, neglected or ruined, each needing its
   rune and its mantra; the runes under the Fellowship's noses in the museum,
   the mantras with the few who keep the old virtues. The Codex is the
   quest-giver; the Time Lord and Lord British only point to it (the Time
   Lord sits in a shrine; the king wrote the plaque).
4. **The second reading**, with all eight shrines awake and the eight runes
   in hand: the Codex asks Ultima IV's last question (the one thing that
   encompasses all Truth, Love and Courage); the player answers INFINITY from
   the eight letters, and the runes fuse into **the Ankh of the Avatar** on
   the pedestal (shape 1288, the Forge's Ankh art, with a double-click
   function).
5. **The Ankh's power: the Avatar's will against the Guardian's.** At the
   Black Gate the Guardian's presence holds the party: the avatar's arm will
   not lift the wand, the companions stand frozen, his hand reaches through
   (the finale's usecode wrapped with CALLO: a flag check, the hold, his
   lines from 0x614 in his voice, the red edge pulse at full, the party
   outlined and still). Raise the Ankh and his hold breaks: his face is torn
   from the gate, the pulse gone, the colours back, the party moves, the
   wand works, and the original ending plays. Without the Ankh the two ways
   out are the game's own: step through, or retreat before he comes; the
   Codex plaque is in the same dungeon, so the way back is in sight.
6. **Its lesser use:** the Ankh raised anywhere quiets the Guardian: his
   taunts through the game turn to anger and then silence, and the
   enthralled (Elizabeth and Abraham among them) flinch and drop their
   weapons for a moment when it shines.

So the shrines are required, as in Ultima IV, and the path is told, not
hidden.

## 4. What is Exult's and what is not

Nothing here is Exult's rule: the tallies, the consequences, the words, the
Codex and the hold are the remake's additions, taken with the user's
approval (CLAUDE.md, "Ask about taste, decide engineering"). Everything the
scenes use (the effects, the gumps, the book, the moongates, the weather, the
lights) keeps Exult's behaviour. The original ending is unchanged once the
hold is broken; the Guardian's lines are his own.

## 5. Build order

- **V0 Engine:** the eight tallies in the save, the hooks (guards, combat,
  the chosen answer), the three intrinsics; the day-timers; the museum
  relics owned. Verified headless with the console (`virtue` command to
  read and set).
- **V1 Mod, the museum and the shrines:** Candice's lending, the mantras'
  topics, the pedestal functions, the restoration scene, the sanctuary, the
  Rune of Honor to Trinsic. One shrine (Compassion, Cove) end to end first.
- **V2 The words of power:** the rune function, the eight effects, the
  standing check.
- **V3 Consequences:** the party's barks and walking out, atonement, the
  world's reactions (fine, healers, shops, Lord British), belief.
- **V4 The Codex:** the lenses on the pedestal, the vision, both readings,
  the Ankh.
- **V5 The finale:** the hold and its breaking, the lesser use.
- **V6 Paintings:** the eight visions and the Codex scene from the user's
  pipeline; placeholders (the shrine's monoliths over the painted map) until
  then.

## 6. Open (the user's taste)

- The party's harshness: comments only, or walking out (proposed: walking
  out).
- The sanctuary heal: in (proposed) or not.
- The look of the visions and the Codex scene (paintings to come).
- Whether a fallen virtue's word merely fails or scolds (proposed: scolds).
