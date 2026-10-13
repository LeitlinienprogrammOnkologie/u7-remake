/*
 *	The interlude, Labyrinth of Worlds (month 12), and Q08 A Castle
 *	Remembering. Ultima Underworld II is a card only (decided 2026-10-12): no
 *	scene of it is played. What it cost shows at the castle: Nelson and Lady
 *	Tory gone, Patterson dead (PROVISIONAL, BRIDGE.md risk 3), a subdued
 *	household, a memorial, Nelson's note at the Lycaeum, and the Serpent of
 *	Blackrock in the avatar's pack.
 */

// The memorial plaque, in front of the throne.
const int MEMORIAL_X = 939;
const int MEMORIAL_Y = 1148;

// Those Ultima Underworld II took: no one killed there speaks afterwards.
void underworldTakes () {
	var gone = [NELSON, TORY, PATTERSON];
	for (npc in gone) {
		if (!UI_is_dead(npc)) {
			npc->remove_npc();
		}
	}
}

// Nelson's note, on the desk where he worked (or where the desk would be).
void nelsonLeavesANote () {
	var note = UI_create_new_object(SHAPE_SCROLL);
	note->set_item_frame(0);
	note->set_item_quality(NELSON_NOTE_QUALITY);
	note->set_item_flag(OKAY_TO_TAKE);
	var desks = UI_find_nearby([2710, 1568, 0], SHAPE_DESK, 5, 0);
	for (desk in desks) {
		desk->give_last_created();
		return;
	}
	UI_update_last_created([2710, 1569, 0]);
}

void showNelsonNote (var scroll) {
	UI_book_mode(scroll);
	say("To the Avatar, should I not be here when thou comest back~~"
		+ "Lord British has summoned me to the castle, and I go gladly; it is warmer than the library.*"
		+ "I looked once more for Pellam's ship in the harbour rolls of Jhelom. She sailed for the west the winter after "
		+ "his last sighting and never came home. Make of it what thou wilt. I make nothing of it, which is the honest answer.*"
		+ "Take his journal with thee, and Brion's figures. A hypothesis, I told thee. "
		+ "It is a better one than most of the poetry.~~Nelson");
}

// Month 12: the dome, the worlds beyond it, and home again with the serpent.
void underworldInterlude object#() () {
	if (gflags[INTERLUDE_DONE]) {
		return;
	}
	gflags[INTERLUDE_DONE] = true;
	UI_fade_palette(12, 1, 0);
	var wait = 360 - UI_game_day();
	if (wait < 1) {
		wait = 1;
	}
	passDays(wait, 16);
	UI_move_object(PARTY, [THRONE_X, THRONE_Y + 8, 0]);
	restParty();
	underworldTakes();
	nelsonLeavesANote();
	giveAvatar(SHAPE_SERPENT, 0, 0);

	var plaque = UI_create_new_object(SHAPE_PLAQUE);
	plaque->set_item_frame(0);
	plaque->set_item_quality(MEMORIAL_QUALITY);
	UI_update_last_created([MEMORIAL_X, MEMORIAL_Y, 0]);

	LORD_BRITISH->move_object([THRONE_X, THRONE_Y, 0]);
	UI_run_schedule(LORD_BRITISH);
	UI_fade_palette(12, 1, 1);
	bridgeCard("Labyrinth of Worlds~~"
		+ "A year after the Black Gate fell, the Guardian struck again. In a single night a dome of blackrock "
		+ "closed over Castle Britannia, and everyone within it was trapped: Lord British, his household, his guests, and you.*"
		+ "The only ways out led through other worlds. You walked them, one after another, until the Guardian's "
		+ "prison broke and the sky came back.*"
		+ "You came home carrying a strange thing: a serpent of blackrock, given to you in a goblin camp "
		+ "by one who never told you his name.*"
		+ "Not everyone came home. Nelson of the Lycaeum did not, nor Lady Tory. Patterson, mayor of Britain, "
		+ "is dead, and the castle does not speak of how.");
}

// Lord British after the dome: the Guardian is not finished (Q08).
void lordBritishAfterTheDome () {
	gflags[HEARD_DOME] = true;
	LORD_BRITISH->show_npc_face(0);
	say("Lord British looks as if he has not slept since the dome came down. Perhaps he has not.");
	say("@I had let myself believe it was over, Avatar. A year of mending roads and arguing with my council, "
		+ "and I thought: this is peace. Then the sky went black.@");
	say("@He is not finished with us. He was never finished. The Gate was one door, and he has others.@");
	if (canSpeak(IOLO)) {
		IOLO.say("@And Batlin went through one of them, a year before the dome. With Gwenno on the same ship.@");
		IOLO.hide();
		LORD_BRITISH.say("@Aye. I have not forgotten, old friend.@");
	}
	say("@Rest now. All of you. When the winter comes, we shall see what the winter brings.@*");
}

// The household after the dome: a word each, before their own conversations.
void castleMourns (var npc, var seen, var line) {
	if (gflags[seen] || !gflags[INTERLUDE_DONE] || gflags[BRIDGE_DONE]) {
		return;
	}
	gflags[seen] = true;
	npc->show_npc_face(0);
	say(line);
	UI_remove_npc_face(npc);
}

void Chuckles object#(0x419) () {
	if (event == DOUBLECLICK) {
		castleMourns(CHUCKLES, SEEN_CHUCKLES, "Chuckles does not caper. @I have no jokes this week, Avatar. "
			+ "I had one about a dome, but nobody laughed, and then I did not either.@");
	}
	Chuckles.original();
}

void Nystul object#(0x418) () {
	if (event == DOUBLECLICK) {
		castleMourns(NYSTUL, SEEN_NYSTUL, "Nystul's hands shake a little. @Under the dome I tried every spell I knew. "
			+ "None of them reached the sky. I keep checking that it is still there.@");
	}
	Nystul.original();
}

void Geoffrey object#(0x440) () {
	if (event == DOUBLECLICK) {
		castleMourns(GEOFFREY, SEEN_GEOFFREY, "Geoffrey stands straighter than usual, the way men do when they are tired. "
			+ "@We kept the doors, Avatar. There was nothing on the other side of them to keep out. "
			+ "That was the worst of it.@");
	}
	Geoffrey.original();
}

// The memorial: Britannian runes, translated beneath for the avatar.
void Plaque shape#(0x334) () {
	if (event == DOUBLECLICK && get_item_quality() == MEMORIAL_QUALITY) {
		gflags[SAW_MEMORIAL] = true;
		UI_display_runes(GUMP_PLAQUE, ["in|memory|of", "nelson", "lady|tory", "never|forgotten"]);
		return;
	}
	Plaque.original();
}
