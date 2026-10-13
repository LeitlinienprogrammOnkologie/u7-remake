# The Eighteen Months: building the bridge

Implementation design, 2026-10-12. A short new adventure between Black Gate's won endgame and Serpent Isle's opening, played in the saga mode (Black Gate, then Serpent Isle, in one game).

**The story's source of record** is [the narrative design](Ultima_VII_The_Eighteen_Months_Narrative_Design.docx) (v1.0, 12 October 2026). It holds the premise, the cast and their arcs, the walkthrough, quests Q01–Q10, the choices and their consequences, the dialogue tone, the look, and the acceptance tests. It leaves out the technical side on purpose.

This file holds the rest:
- the canon, checked against the original files;
- the answers to the document's open risks (its §11.1), where the files can answer them;
- the decisions taken and still open;
- what each quest is made of, and how it is built.

## 1. Canon checked against the files

| Fact | Source |
|---|---|
| Batlin escapes the Black Gate: after Hook dies, Batlin's speech, "Kal Ort Por", and he is gone. | BG usecode 0x1FA (PROGRESS.md, "the finale") |
| The Fellowship shipped blackrock by sea: "store the blackrock in the hold of The Crown Jewel". | BG usecode, scroll 0x31D |
| Gwenno bought a map to "the legendary Serpent Isle" from an old pirate in Buccaneer's Den, began her search, and left a copy with Lord British, not to be given to Iolo until the Avatar's adventures were over. | BG usecode, scroll 0x31D (her letter to Iolo); whether the original places it in the world is still to check |
| Batlin hired a ship "to transport scholars to explore a new land and spread word of The Fellowship"; Gwenno sailed on it. It carried a heavy blackrock obelisk; once it was ashore and Gwenno had gone inland, "there was a huge explosion... there stood Batlin -- and the obelisk was shattered". Batlin left with Palos, Brunt and Deadeye; Leon stayed in Fawn. | SI usecode 0x43B, 0x43C, 0x434, 0x375 |
| **Serpent Isle's intro**, eighteen months after the Gate and "the dismantling of The Fellowship": at Lord British's castle, "All we found among Batlin's belongings was this enchanted scroll... and a map showing the way to a place called the Serpent Isle." The scroll speaks with the Guardian's voice: "Batlin! In the event that the Avatar destroys the Black Gate you shall follow the unwitting human Gwenno to the Serpent Isle. There I shall outline my plan to destroy Britannia!" Lord British: "'Tis my worst fear! I must send the Avatar through the pillars to the Serpent Isle!" Then the ship, the Pillars and the title. | Exult `SI_Game::play_intro` (INTRO.DAT flics 1–8) and its subtitles, `data/exultmsg.txt` 0x54B–0x569. The "Dick British" and muppet lines there are Exult's joke mode, not canon. |
| The Serpent Pillars, in Erstam's words: "They were rumored to exist, so the tales of drunken sailors indicated, beneath the surface of the Great Ocean. Other stories mentioned that the pillars would rise only when both moons were above the horizon; some said that the sun itself must also be visible. Yet other tales suggested that the pillars would only appear in the depths of winter." The crossing: a storm, the pillars rising, "a brilliant flash of white". | SI manual, Foreword and "Lessons of the Past" |
| Erstam, a mage of Moon, led the emigrants from Moon, Fawn and the Montors through the Pillars. | SI manual; SI usecode 0x445 |
| Lord British's equipment letter for the "Quest to Find Batlin": Glass Sword, Magebane, the Daemon Sword (Forge of Virtue), Rudyom's Wand, the Serpent of Blackrock "which thou didst obtain from netherworlds during thy most recent adventure", a spellbook, enchanted breastplate, helm and gauntlets; Iolo's crossbow, Dupre's magic shield, Shamino's magic bow with burst arrows; torches, swamp boots, a good dagger and food. It closes with finding Batlin and "Iolo's lost wife, the goodwoman Gwenno". | SI usecode, scroll 0x31D |
| The Serpent of Blackrock (of Order) comes from **Ultima Underworld II**, a year after the Gate, given by a nameless goblin of a Britannian goblin camp. In SI the teleport storm swaps it for Columna's moonsilk stockings; Stefano locks it in his vault; the Avatar gets the key by rescuing him from a Death Knight. | UW2 (not in the local data); SI usecode 0x4A8, 0x40E, 0x421 |
| SI's party is the Avatar, Iolo, Shamino and Dupre; the storm replaces Lord British's equipment with strange items. | SI usecode 0x402, 0x403 |

## 2. The document's open risks (§11.1)

1. **The Guardian's scroll and the map in SI's intro:** answered (table above). The intro **stages the unveiling itself**, and the map in it is Batlin's, found with the scroll. This produces decision D2 below.
2. **Whether the Fellowship leaders survive BG:** the people Act I needs (the headquarters' clerk, the Paws shelter's Feridwyn and Brita) are ordinary NPCs a player may have killed. Rule: the prologue checks the save and gives each required speaker a stand-in. Still open: the exact cast.
3. **Nelson before UW2:** plausible. BG's Nelson is the Lycaeum's head in Moonglow, the twin of Brion (BG 0x498). What becomes of him in UW2 isn't in the local data, so it stays PROVISIONAL. The same goes for Lady Tory (BG 0x4C8, Serpent's Hold) and Patterson (Britain's mayor, BG 0x42B), whom the document's aftermath removes.
4. **Lord British's letter:** answered: the exact text is SI 0x31D (table above).
5. **Erstam's wording and certainty:** answered: quoted above. It is hearsay throughout ("rumored", "tales", "some said"). That matches the document's three testimonies (A two moons, B the sun, C winter) and its rule that the answer is a credible hypothesis, not a law.

## 3. Decisions

Taken:
- **UW2 is a text card only** (you, 2026-10-12). What changed afterwards shows in the world: Nelson, Lady Tory and Patterson gone (provisional, see risk 3), the castle subdued, the optional Nelson note.
- **From the document:**
  - **Title:** "The Eighteen Months".
  - **Length:** about 3 hours on the main path, 4 with optional content.
  - **The two maps and the scroll:** Gwenno's copy from Lord British (Q06) and the Guardian's enchanted scroll held back as the last revelation (Q09).
  - **The Pillars:** shown only by SI's own intro, with no crossing in play.
  - **The four required centres:** the Britain headquarters and Paws (Act I), Minoc and Buccaneer's Den (Act II).

- **D1, the castle after UW2 (Q08)** (agreed 2026-10-12): UW2 itself is only the text card. Q08 is the changed castle, walkable: no UW2 scene is retold or staged.
- **D2, where the scroll is unveiled** (agreed 2026-10-12): SI's intro stages the unveiling, so the bridge takes it over and the saga doesn't show it twice. The bridge stages Q09 in the castle with the intro's own words (and its recorded voice, if it can be taken from INTRO.DAT). Then Q10 plays, and the saga shows only the intro's voyage (the ship, the Pillars, the title; flics 6–8). A standalone SI game keeps the whole intro.
- **From Serpent Isle into Black Gate** (agreed 2026-10-12), as Exult's Black Gate takes them when SI is installed:
  - SI's paperdolls (its `PAPERDOL.VGA` and gump shapes);
  - skin colour in Black Gate's new game (SI's skin shapes), so the saga's Avatar arrives in SI already chosen;
  - the keyring.

  These come with the engineering B1 needs (below). Their look goes to you before it's built. The keyring: Lord British gives it with the castle keys early on (agreed 2026-10-12).

- **Casting** (agreed 2026-10-12; all have their portraits in FACES.VGA), all existing Black Gate characters (their own conversations kept, the bridge's topics added; numbers are NPC numbers):
  - **The clerk** at the Britain headquarters: Millie (63), the Fellowship's most devoted talker, who wondered whether Batlin wanted *her* to run the meeting.
  - **Paws:** the caregiver Brita (168), who runs the shelter with Feridwyn (167); the former initiate Merrick (170), a farmer who lived off rubbish with Komor and Fenn until the Fellowship took him in; the family Alina (169) and her baby, living in the shelter, her husband Weston (pardoned in Black Gate). The local stewards, should the player hand the shelter over: Camille (177), a widow who keeps the old virtues and doesn't trust the Fellowship.
  - **Minoc:** the reluctant witness Mikos (97), the mine's foreman, a Fellowship member who hides from the town's quarrels underground; the payment receipt in the office of Gregor (82), head of the Britannian Mining Company's branch; the workshop trace at Gladstone's (85), the sculptor who works in stone and glass.
  - **Buccaneer's Den:** the old pirate Mole (227), past fifty, who roamed the seas for years and retired to the Den (a Fellowship member, which gives him a debt to settle); the forger Lucky (228), who trains Fellowship initiates for pocket money; the rival crew under Blacktooth (226).


## 4. What each part is made of

Month gates follow the document: Prologue and Act I in months 1–4, Act II in months 5–11, UW2 at month 12, Act III and the departure at month 18. Season words are avoided except for winter at the end.

| Quest | Where | Who | New things |
|---|---|---|---|
| Q01 The Quiet After Victory | Castle British, Britain | Lord British, citizens, a guard | the decree; Fellowship emblems removed in Britain |
| Q02 The Ledger | Fellowship headquarters, Britain | the clerk (casting open), Iolo | Batlin's ledger (two contradicting entries), the passenger document naming Gwenno |
| Q03 The Shelter | Paws | Feridwyn, Brita or stand-ins; caregiver, former initiate, family | the three outcomes as flags; the revisit's staffing and mood |
| Farewell supper (optional) | an inn in Britain | Spark, Sentri, Jaana, Julia | their leaving the party; under ten minutes |
| Q04 Blackrock Freight | Minoc | a reluctant witness | workshop trace, payment receipt, optional staging area |
| Q05 The Old Pirate | Buccaneer's Den | the old pirate, a forger, a rival crew | his logbook, a forged manifest; three ways through, one nonviolent |
| Q06 A Map Kept in Trust | Castle British | Lord British, Iolo | Gwenno's letter and her map copy |
| Q07 Two Moons and a Dawn | Cove, Moonglow | Rudyom, Nelson | the Lycaeum's accounts, a dated journal, optional navigator's notes |
| UW2 interlude, Q08 | Castle British | the castle's people | the text card; Nelson, Lady Tory and Patterson removed; the Serpent of Order in the pack; the memorial; Nelson's note |
| Q09 The Enchanted Command | Castle British | Lord British, the party | the Guardian's scroll, its words (and voice) from SI's intro (D2) |
| Q10 The Last Voyage | the castle's armoury, Britain's harbour | Lord British, Iolo, Shamino, Dupre, the pirate (advisor or pilot only) | the letter's items and the letter itself; then the handoff into SI |

## 5. How it is built

- **An Exult-format mod**, the remake's first. Usecode is written in Exult's UCC (`exult/exult-1.12.1/tools/compiler`), on the model of Exult's BG mod `content/bgkeyring`. Map and object changes go in patch files.
  - The remake learns Exult's patch loading (`<PATCH>`: patch usecode replaces functions by number), as [DESIGN.md](DESIGN.md) section 10 plans. The VM already runs Exult's extended opcodes (`UsecodeOpcodes.cs`).
  - The same mod runs in Exult itself, which gives a reference to check against.
- **New talk for existing NPCs:** the patch function replaces the NPC's function by number and calls the replaced one with UCC's `original` keyword (Exult's opcode `UC_CALLO` 0x52, `Usecode_function::orig`). Exult's own `sifixes` cutscenes and `bgkeyring`'s inn keys do this. The bridge adds its topics and passes everything else to the original, so no conversation is copied. The remake's VM lacks `CALLO`.
- **Serpent Isle's intrinsics, as Black Gate mods get them:** Exult's BG table continues past the original 0x96 with SI's intrinsics and Exult's own (0xA0–0xDA, `bgintrinsics.h`). The remake stops at 0x96. The bridge needs:
  - **NPCs' days:** `set_new_schedules`, `modify_schedule`, `run_schedule`, `revert_schedule` (the month gates, the Paws staffing, stand-ins at their posts).
  - **Casting:** `set_usecode_fun`, `get_usecode_fun`, `set_npc_name` (stand-ins for speakers the player killed, risk 2).
  - **Taking things out of the world:** `remove_from_area` (the Fellowship's emblems, the absences after UW2).
  - **Two faces at once:** `show_npc_face0/1`, `remove_npc_face0/1`, `change_npc_face0/1`, `set_conversation_slot` (Lord British with Iolo, the three voices in Paws, Q09).
  - **Time and scenes:** `game_day` (the month gates), `fade_palette_sleep` (the time-skip cards), `center_view`, `save_pos`, `teleport_to_saved_pos`, `approach_avatar`.

  All of these are SI's own intrinsics too, so the SI work needs the same code; the BG table only gives them other numbers.
- **Function numbers and shapes:** the remake's own additions (the keyring) use 0xC00 and up and shape 1100, under every game; the bridge mod's usecode starts at 0xD00 and its new shapes (the Serpent of Blackrock, `si:262`) go in its own `shape_additions.txt`.
- **UCC headers:** `bgkeyring`'s `headers/bg/` (BG's NPCs, shapes, global flags and functions by name) save writing the bridge against raw numbers.
- **Month gates:** `advance_time` and flags. World changes (emblems, the Paws outcome, the UW2 absences) are flag-gated usecode run at each gate.
- **Shapes BG lacks:** the Serpent of Blackrock comes from `serpent/STATIC/SHAPES.VGA` the way Exult's BG imports SI shapes at load (`Shape_file::import_shapes`, `shapeid.cc`). It is read from the local SI files, never copied into the repo.
- **Both ends:**
  - The start is the won endgame's follow-up (`U7Game.StartEndgame`, today `null`, which quits).
  - The end is the switch to SI (the saga plan's `GameContext` switch), then SI's intro from its voyage on (flics 6–8, D2; a standalone SI game plays it all), then SI's new game with the carry-over (name, sex, portrait).
- **Order of work:** the bridge needs SI running only for its last step, so it can be built alongside the SI engine work. Until then it ends on a "to be continued" card.
  1. B1: patch loading, `CALLO`, the BG intrinsics above, SI shape import, UCC built, and the endgame hook. Test: a hello-world mod function that adds a topic to Lord British and calls his original, in both Exult and the remake.
     - Done 2026-10-12: patch loading, UCC's symbol table, statics (saved in `usecode.var`), `CALLO`, indirect calls, try; tested with a hand-built patch (PROGRESS.md, "Mods (current)"). Also done: UCC built (`scripts/ucc`), Exult's BG intrinsics the bridge needs (schedules saved as Exult's `schedule.dat`, two faces, names, usecode assignment by name, the saved position, fades, the day, `set_intercept_item`), Exult's whole NPC record in saves; SI's imports and the remake's `shape_additions.txt` (the Serpent of Blackrock: `si:262` in the mod's own); the remake's additions layer under the mod (`original()` chaining through both); the endgame hook (`AfterEndgame`). B1 is complete (2026-10-12).
  2. B2: the Prologue and Act I.
     - Done 2026-10-12 (`mods/eighteen`, PROGRESS.md "The Eighteen Months"): the prologue from the won endgame (Batlin removed, the hall's emblems down, the meetings ended, 28 days on, Lord British's audience with the decree and both mandates), Q01 (Candice, Gaye, Patterson), Q02 (Millie, Batlin's strongbox, the ledger's two entries reconciled, the passage list and Iolo's 9.2), Q03 (Brita, Merrick, Alina, Feridwyn's decision, Camille as steward, the revisit lines from Act II), Act I's close and the farewell supper, the reports to Lord British, and stand-ins for a dead cast. The new dialogue is the bridge's own (NEW), written to the design's samples; the user reviews it.
  3. B3: Act II.
     - Done 2026-10-12 (PROGRESS.md "The Eighteen Months"): the fifth month's gate; Q04 Minoc (Gladstone, Gregor's receipt, Mikos, the optional tally); Q05 the Den (Mole, Blacktooth's forged bill, Lucky; the share settled by the avatar or by Mole; or a fight); Q06 Gwenno's chart from Lord British; Q07 Rudyom, Nelson's tales and the window, Brion's notes and the eighteenth month.
  4. B4: the UW2 interlude, Act III and the epilogue.
     - Done 2026-10-12 (PROGRESS.md "The Eighteen Months"): the UW2 card at month 12 (Nelson, Lady Tory and Patterson removed, the Serpent of Blackrock given, a memorial plaque, Nelson's note, the household's words, Lord British after the dome); the eighteenth month's gate in snow; Q09 the scroll in the intro's words with its voices (`scripts/bridge_speech.py`, Exult's `PATCH_U7SPEECH`); Q10 Lord British's letter and supplies, only Iolo, Shamino and Dupre kept; the harbour, Mole's advice (no fifth companion), the last words (9.5) and the closing card. The game goes on afterwards until B5's handoff.
  5. B5: the handoff into SI (needs SI M0–M3).
- **Testing:** the agent console, the same mod in Exult, and the document's §11 acceptance tests as the play-test checklist.
