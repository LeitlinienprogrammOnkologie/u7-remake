/*
 *	Act III, The Guardian's Message (month 18), and the epilogue.
 *	Q09: the enchanted scroll found among Batlin's things speaks with the
 *	Guardian's voice, in Serpent Isle's intro's own words and voices (D2:
 *	the saga then shows only the intro's voyage). Q10: Lord British's letter
 *	and supplies (the substance of Serpent Isle's opening letter, the
 *	Serpent of Blackrock already in the pack), Iolo, Shamino and Dupre
 *	gathered, the harbour, the old pirate's advice, and the ship.
 */

// The harbour: Clint's shipyard, Britain.
const int HARBOUR_X = 944;
const int HARBOUR_Y = 1484;

// A line spoken with its recorded voice (when made by scripts/bridge_speech.py).
// Showing the face first shows the line before (Exult show_pending_text),
// so each voice starts with its own words.
void voiced (var npc, var voice, var text) {
	if (npc) {
		npc->show_npc_face(0);
	}
	UI_start_speech(voice);
	if (npc) {
		npc->show_npc_face(0);
	}
	say(text);
}

// The eighteenth month: winter, and one box of Batlin's that no one could open.
void actThreeBegins object#() () {
	if (gflags[ACT_THREE]) {
		return;
	}
	gflags[ACT_THREE] = true;
	UI_fade_palette(12, 1, 0);
	var wait = 525 - UI_game_day();
	if (wait < 1) {
		wait = 1;
	}
	passDays(wait, 10);
	restParty();
	UI_set_weather(SNOWING);
	UI_move_object(PARTY, [THRONE_X, THRONE_Y + 5, 0]);
	LORD_BRITISH->move_object([THRONE_X, THRONE_Y, 0]);
	UI_run_schedule(LORD_BRITISH);
	if (!UI_is_dead(MOLE)) {
		gflags[MOLE_AT_HARBOUR] = true;
		UI_set_new_schedules(MOLE, [0], [LOITER], [HARBOUR_X, HARBOUR_Y]);
		MOLE->move_object([HARBOUR_X, HARBOUR_Y, 0]);
		UI_run_schedule(MOLE);
	}
	UI_fade_palette(12, 1, 1);

	var king = UI_get_npc_object(LORD_BRITISH);
	script king after 1 ticks call scrollUnveiling;
	bridgeCard("Months pass. Britain mends its roofs a second time. Nystul checks the sky every morning.*"
		+ "Winter comes early, and hard. It is the eighteenth month since the Gate.*"
		+ "The inventory of Batlin's belongings is finished at last, all but one sealed box that no key, "
		+ "no spell and no crowbar could open. This morning, it opened by itself.");
}

// Q09: the scroll speaks (the design's 9.6; Serpent Isle's intro, exultmsg 0x550-0x569).
void scrollUnveiling object#() () {
	if (gflags[SCROLL_UNVEILED]) {
		return;
	}
	gflags[SCROLL_UNVEILED] = true;
	var herald = firstAlive([GEOFFREY, NYSTUL]);
	if (herald) {
		voiced(herald, VOICE_MY_LIEGE, "@My liege.@");
		voiced(herald, VOICE_ALL_WE_FOUND, "@All we found among Batlin's belongings was this enchanted scroll... "
			+ "and a map showing the way to a place called the Serpent Isle.@");
	} else {
		say("A servant brings in a sealed box, now open: an enchanted scroll, and a map showing the way "
			+ "to a place called the Serpent Isle.");
	}
	voiced(LORD_BRITISH, VOICE_INDEED, "@Indeed. Put it on the table.@");

	var scroll = UI_create_new_object(SHAPE_SCROLL);
	scroll->set_item_frame(0);
	scroll->set_item_quality(ENCHANTED_QUALITY);
	scroll->set_item_flag(OKAY_TO_TAKE);
	UI_update_last_created([THRONE_X + 2, THRONE_Y + 3, 0]);
	voiced(LORD_BRITISH, VOICE_STAND_BACK, "The scroll begins to smoulder. @Stand back!@");
	UI_earthquake(6);

	UI_remove_npc_face(LORD_BRITISH);
	UI_start_speech(VOICE_GUARDIAN);
	say("A voice fills the room, deep and amused, a voice you know too well:~~"
		+ "@Batlin! In the event that the Avatar destroys the Black Gate you shall follow the unwitting human "
		+ "Gwenno to the Serpent Isle. There I shall outline my plan to destroy Britannia!@");
	voiced(LORD_BRITISH, VOICE_WORST_FEAR, "@'Tis my worst fear! I must send the Avatar through the pillars to the Serpent Isle!@");

	say("The scroll is ash. Lord British stares at it a long moment.");
	say("@That voice. I heard it every night under the dome.@ He straightens. @No more waiting, Avatar.@");
	if (canSpeak(IOLO)) {
		IOLO.say("@Unwitting.@ Iolo's voice is very quiet. @He calls her unwitting.@");
		say("@She went to find a place, of her own will, with her own coin. He means to use her road. "
			+ "Very well. I mean to walk it faster than he does.@");
		IOLO.hide();
	}
	LORD_BRITISH.say("@Thy winter dawn is near, if Brion's figures are right. Take Iolo, Shamino and Dupre; "
		+ "no more. Come to me when thou art ready, and I shall give thee what the armoury holds.@*");
	scroll->remove_item();
}

// Q10: the letter for the quest and the armoury's supplies.
void lordBritishArms () {
	gflags[ARMOURY_DONE] = true;
	say("@I have written down what I would have thee carry, so that thou canst not say I forgot anything.@");
	giveAvatar(SHAPE_SCROLL, 0, LETTER_QUALITY);
	var n = 0;
	while (n < 4) {
		giveAvatar(SHAPE_TORCH, 0, 0);
		n = n + 1;
	}
	var food = UI_create_new_object(SHAPE_FOOD);
	food->set_item_frame(16);
	food->set_item_quantity(12);
	food->set_item_flag(OKAY_TO_TAKE);
	if (!AVATAR->give_last_created()) {
		UI_update_last_created(AVATAR->get_object_position());
	}
	giveAvatar(SHAPE_SWAMP_BOOTS, 0, 0);
	say("@Torches, bread, swamp boots, the rest as the letter says. And thou hast the serpent already.@ "
		+ "He nods at your pack. @Keep it close. I do not know what it is, and I do not like what I do not know.@");
	say("@The ship waits in the harbour, by Clint's yard. She sails when thou dost.@");
}

void showLetter (var scroll) {
	UI_book_mode(scroll);
	say("To the Avatar, for the Quest to Find Batlin~~"
		+ "Carry the Glass Sword and Magebane if thou hast them, and the Black Sword of the Forge, "
		+ "and Rudyom's Wand. Carry the serpent of blackrock thou didst bring back from below.*"
		+ "Thy spellbook. The best armour thou canst find: breastplate, helm and gauntlets. "
		+ "Iolo his crossbow, Dupre his shield, Shamino his bow and burst arrows.*"
		+ "Torches. Swamp boots. A good dagger. Food enough.~~"
		+ "Find Batlin, and stop whatever he intends. And find Iolo's wife, the goodwoman Gwenno, "
		+ "who went first.~~Lord British");
}

// Lord British in Act III and after the dome; true when he has spoken.
var lordBritishActThree () {
	if (gflags[INTERLUDE_DONE] && !gflags[HEARD_DOME]) {
		lordBritishAfterTheDome();
		var avatar = UI_get_avatar_ref();
		script avatar after 1 ticks call actThreeBegins;
		return true;
	}
	if (gflags[SCROLL_UNVEILED] && !gflags[ARMOURY_DONE]) {
		LORD_BRITISH->show_npc_face(0);
		say("@Thou art ready, then.@");
		lordBritishArms();
		return true;
	}
	if (gflags[ARMOURY_DONE] && !gflags[BRIDGE_DONE]) {
		LORD_BRITISH->show_npc_face(0);
		say("@The ship waits by Clint's yard, Avatar. Go with my blessing, all four of you.@*");
		return true;
	}
	return false;
}

// The friends who go on, and only they (no extra Serpent Isle companions).
void onlyTheThree () {
	var party = UI_get_party_list();
	for (member in party) {
		var num = member->get_npc_number();
		if (num != AVATAR && num != IOLO && num != SHAMINO && num != DUPRE
				&& member != UI_get_avatar_ref()) {
			UI_remove_from_party(num);
			UI_revert_schedule(num);
			UI_run_schedule(num);
		}
	}
	var three = [IOLO, SHAMINO, DUPRE];
	for (npc in three) {
		if (!UI_is_dead(npc) && !inParty(npc)) {
			npc->move_object(AVATAR->get_object_position());
			UI_add_to_party(npc);
		}
	}
}

// The epilogue: the harbour, last words, and the ship (the design's 9.5).
void theLastVoyage () {
	gflags[BRIDGE_DONE] = true;
	onlyTheThree();
	if (canSpeak(SHAMINO)) {
		SHAMINO.say("@Look at Britain's chimneys. A year ago half of them were cold.@");
		SHAMINO.hide();
	}
	if (canSpeak(DUPRE)) {
		DUPRE.say("@Paws will manage. So will Minoc. We leave the place better than we found it, and that is all "
			+ "a knight can ask of a year.@");
		DUPRE.hide();
	}
	if (canSpeak(IOLO)) {
		IOLO.say("Iolo is looking west, past the harbour mouth, at nothing at all.");
		say("@The map promises a place, not safe passage. Still, Gwenno would not turn back for fear of an "
			+ "uncertain horizon. Nor shall I.@*");
		IOLO.hide();
	}
	var avatar = UI_get_avatar_ref();
	script avatar after 1 ticks call bridgeEnds;
}

void bridgeEnds object#() () {
	UI_fade_palette(12, 1, 0);
	passDays(1, 6);
	UI_fade_palette(12, 1, 1);
	bridgeCard("At the next dawn the ship slips out of Britain's harbour, four travellers aboard and an old pirate's "
		+ "warnings in their ears. Somewhere to the west, if the tales are true, the serpents stand.*"
		+ "The Eighteen Months~ends here.~~Serpent Isle begins.");
}

// Mole at the harbour: advisor, never a fifth companion.
var moleAtHarbour () {
	MOLE->show_npc_face(0);
	say("Mole is sitting on a bollard with his collar up against the snow.");
	say("@So it is true. Thou art going to the ring. I came to see thee off, and to give thee advice thou wilt not take.@");
	add(["advice", "pilot", "set sail", "bye"]);
	converse (0) {
		case "advice" (remove):
			say("@Keep both moons on thy larboard bow till thou lose the coast. When the sea goes flat and the birds stop, "
				+ "thou art close. When the sea goes strange, do not argue with it.@");
			say("@And if thou seest pillars, mate, do not stop to admire them.@");

		case "pilot" (remove):
			say("@Me? I am too old and too fond of dry land. I would only be one more mouth for the sea to argue with.@");
			say("@Besides, the chart is in better hands than mine. It always was.@");

		case "set sail":
			say("@Then go. Fair winds, Avatar. Bring the minstrel's lady home, and my log with her.@*");
			UI_remove_npc_face(MOLE);
			theLastVoyage();
			return false;

		case "bye":
			say("@I shall be here. The snow is no colder than the Den.@*");
			break;
	}
	return false;
}

// The last voyage starts from Mole, or, with him gone, from the harbour's shipwright.
var harbourDeparture (var npc) {
	if (!gflags[ARMOURY_DONE] || gflags[BRIDGE_DONE]) {
		return false;
	}
	if (npc == MOLE && gflags[MOLE_AT_HARBOUR]) {
		moleAtHarbour();
		return true;
	}
	if (npc == CLINT && !gflags[MOLE_AT_HARBOUR]) {
		CLINT->show_npc_face(0);
		say("@Thy ship is ready, Avatar. Shall I cast off?@");
		if (askYesNo()) {
			UI_remove_npc_face(CLINT);
			theLastVoyage();
		}
		return true;
	}
	return false;
}

void Clint object#(0x439) () {
	if (event == DOUBLECLICK && harbourDeparture(CLINT)) {
		return;
	}
	Clint.original();
}
