/*
 *	The Eighteen Months: from the Black Gate to the Serpent Pillars.
 *	The bridge between Black Gate's won endgame and Serpent Isle's opening
 *	(docs/BRIDGE.md; the story's source of record is the narrative design
 *	in docs/). Compiled with Exult's UCC:
 *		scripts/ucc/bin/ucc.exe -I mods/eighteen/src -o mods/eighteen/data/usecode mods/eighteen/src/eighteen.uc
 *	Played with restart.ps1 -Mod eighteen (or --mod eighteen).
 *
 *	The headers in headers/ are Exult's BG Keyring mod's (content/bgkeyring,
 *	GPL; Alun Bestor, Marzo Junior and the Exult team).
 */

#game "blackgate"
#autonumber 0xD00

#include "headers/constants.uc"
#include "headers/constants2.uc"
#include "headers/bg/bg_npcs.uc"
#include "headers/bg/bg_shapes.uc"
#include "headers/bg/bg_shapes2.uc"
#include "headers/bg/bg_gflags.uc"
#include "headers/bg/bg_gflags2.uc"
#include "headers/bg/bg_structs.uc"
#include "headers/bg/bg_externals.uc"
#include "headers/bg/bg_externals2.uc"

// The bridge's global flags, past Black Gate's own.
enum bridge_flags {
	BRIDGE_STARTED			= 0x700,	// the prologue has run
	HEARD_DECREE			= 0x701,	// Q01: Lord British's audience
	MET_AFFECTED_CITIZEN	= 0x702,	// Q01: a former member in Britain spoken to
	CLERK_GAVE_KEY			= 0x703,	// Q02: the strongbox's key handed over
	CLERK_HELPED			= 0x704,	// Q02: the donors' names kept sealed with her
	READ_LEDGER				= 0x705,
	READ_PASSENGERS			= 0x706,
	LEDGER_RECONCILED		= 0x707,
	PAWS_HEARD_CAREGIVER	= 0x708,	// Q03: the three perspectives
	PAWS_HEARD_INITIATE		= 0x709,
	PAWS_HEARD_FAMILY		= 0x70A,
	PAWS_CLOSED				= 0x70B,	// Q03: the three outcomes
	PAWS_OVERSIGHT			= 0x70C,
	PAWS_STEWARDS			= 0x70D,
	ACT_ONE_CLOSED			= 0x70E,
	SUPPER_HELD				= 0x70F,
	REPORTED_LEDGER			= 0x710,	// told Lord British of the voyage
	REPORTED_PAWS			= 0x711,
	CLERK_LAST_WORD			= 0x712,
	HEARD_STEWARD			= 0x713,	// the stewards' offer, from Camille or her stand-in
	SEEN_CLERK				= 0x714,
	SEEN_CANDICE			= 0x715,
	SEEN_GAYE				= 0x716,
	SEEN_PATTERSON			= 0x717,
	SEEN_BRITA				= 0x718,
	SEEN_MERRICK			= 0x719,
	SEEN_ALINA				= 0x71A,
	SEEN_FERIDWYN			= 0x71B,
	SEEN_CAMILLE			= 0x71C,
	ACT_TWO					= 0x720,	// months 5-11; the Paws revisit shows from here
	MINOC_TRACE				= 0x721,	// Q04: Gladstone dressed the stone
	MINOC_RECEIPT			= 0x722,	// Q04: the Company's receipt read
	MINOC_WITNESS			= 0x723,	// Q04: Mikos told of the lower tunnel
	MINOC_STAGING			= 0x724,	// Q04, optional: the coaster's tally at the shore
	MINOC_DONE				= 0x725,
	DEN_HEARD_MOLE			= 0x726,	// Q05: Mole sold the chart; his log is with Blacktooth
	DEN_SAW_BILL			= 0x727,	// Q05: Blacktooth's bill of sale read
	DEN_KNOWS_MARK			= 0x728,	// Q05: Mole cannot write; he makes his mark
	DEN_LUCKY_CONFESSED		= 0x729,	// Q05, investigation: the forgery exposed
	DEN_SHARE_SETTLED		= 0x72A,	// Q05, diplomacy: the old share paid
	DEN_FOUGHT				= 0x72B,	// Q05, confrontation
	DEN_LOG_READ			= 0x72C,
	DEN_DONE				= 0x72D,	// Mole has told the rest
	SEEN_GREGOR				= 0x72E,
	SEEN_GLADSTONE			= 0x72F,
	SEEN_MIKOS				= 0x730,
	SEEN_MOLE				= 0x731,
	SEEN_BLACKTOOTH			= 0x732,
	GOT_GWENNOS_MAP			= 0x733,	// Q06
	HEARD_RUDYOM			= 0x734,	// Q07: the stone as an anchor
	READ_TALES				= 0x735,	// Q07: the Lycaeum's sea tales and the journal
	WINDOW_FOUND			= 0x736,	// Q07: a winter dawn with both moons up
	DAWN_DATED				= 0x737,	// Q07: Brion dates it to the eighteenth month
	ACT_TWO_DONE			= 0x738,
	GOT_NAVIGATOR_NOTES		= 0x739,
	DEN_HEARD_SHARE			= 0x73A,	// Blacktooth named his share
	DEN_MOLE_PAYS			= 0x73B,	// Mole agreed to pay it
	INTERLUDE_DONE			= 0x740,	// month 12: the UW2 card and the castle's aftermath
	HEARD_DOME				= 0x741,	// Q08: Lord British on the Guardian's dome
	READ_NELSON_NOTE		= 0x742,	// Q08, optional
	SAW_MEMORIAL			= 0x743,	// Q08, optional
	ACT_THREE				= 0x744,	// month 18
	SCROLL_UNVEILED			= 0x745,	// Q09
	ARMOURY_DONE			= 0x746,	// Q10: Lord British's letter and supplies
	BRIDGE_DONE				= 0x747,	// the ship has sailed
	SEEN_CHUCKLES			= 0x748,
	SEEN_NYSTUL				= 0x749,
	SEEN_GEOFFREY			= 0x74A,
	MOLE_AT_HARBOUR			= 0x74B
};

// The bridge's documents, past the qualities Black Gate's books and scrolls use.
enum bridge_documents {
	LEDGER_QUALITY			= 200,		// a book: Batlin's private accounts
	PASSENGERS_QUALITY		= 200,		// a scroll: the passage list
	RECEIPT_QUALITY			= 201,		// a scroll: the Mining Company's receipt
	TALLY_QUALITY			= 202,		// a scroll: the coaster's tally at Minoc's shore
	CHART_QUALITY			= 203,		// a scroll: Gwenno's copy of the chart
	BILL_QUALITY			= 204,		// a scroll: Blacktooth's bill of sale (forged)
	NOTES_QUALITY			= 205,		// a scroll: an old navigator's notes
	LOG_QUALITY				= 201,		// a book: Mole's log
	TALES_QUALITY			= 202,		// a book: the Lycaeum's sea tales
	NELSON_NOTE_QUALITY		= 206,		// a scroll: Nelson's note, left before the dome
	ENCHANTED_QUALITY		= 207,		// a scroll: Batlin's enchanted scroll, spent
	LETTER_QUALITY			= 208,		// a scroll: Lord British's letter for the quest
	MEMORIAL_QUALITY		= 210,		// a plaque: the castle's memorial
	STRONGBOX_KEY			= 216		// a lock no door or chest uses
};

const int SHAPE_FELLOWSHIP_ICON = 724;
const int SHAPE_FELLOWSHIP_TAPESTRY = 849;
const int SHAPE_CRATE = 804;
const int SHAPE_DESK = 407;
const int SHAPE_SERPENT = 1101;		// shape_additions.txt: Serpent Isle's blackrock serpent
const int SHAPE_PLAQUE = 820;
const int SHAPE_SWAMP_BOOTS = 588;
const int GUMP_PLAQUE = 0x33;

// The bridge's voices: Serpent Isle's intro voices, made locally by
// scripts/bridge_speech.py into data/u7speech.spc (Exult PATCH_U7SPEECH).
enum bridge_voices {
	VOICE_MY_LIEGE			= 40,
	VOICE_ALL_WE_FOUND		= 41,
	VOICE_INDEED			= 42,
	VOICE_STAND_BACK		= 43,
	VOICE_GUARDIAN			= 44,
	VOICE_WORST_FEAR		= 45
};

// Declared ahead: the scenes call each other across files.
extern void checkActOne ();
extern void lordBritishAudience object#() ();
extern void actTwoBegins object#() ();
extern void checkActTwo ();
extern void lordBritishGivesMap ();
extern void underworldInterlude object#() ();
extern void actThreeBegins object#() ();
extern var lordBritishActThree ();
extern var harbourDeparture (var npc);
extern void scrollUnveiling object#() ();
extern void bridgeEnds object#() ();

#include "common.uc"
#include "prologue.uc"
#include "britain.uc"
#include "paws.uc"
#include "actone.uc"
#include "minoc.uc"
#include "den.uc"
#include "pillars.uc"
#include "acttwo.uc"
#include "interlude.uc"
#include "actthree.uc"
#include "documents.uc"
