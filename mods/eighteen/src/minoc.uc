/*
 *	Minoc: Q04 Blackrock Freight. Three clues in any order (Gladstone dressed
 *	the stone, the Mining Company's receipt, Mikos's testimony) and an
 *	optional fourth (the coaster's tally at the shore) point to Buccaneer's
 *	Den. What the stone was for stays unknown.
 */

// The Company's receipt, in Gregor's desk, and the coaster's tally in a
// crate on the shore by the warehouses; the chips on Gladstone's floor.
void minocAtActTwo () {
	var desks = UI_find_nearby([1753, 515, 0], SHAPE_DESK, 2, 0);
	for (desk in desks) {
		var receipt = UI_create_new_object(SHAPE_SCROLL);
		receipt->set_item_frame(0);
		receipt->set_item_quality(RECEIPT_QUALITY);
		desk->give_last_created();
	}

	var crate = UI_create_new_object(SHAPE_CRATE);
	crate->set_item_frame(0);
	UI_update_last_created([1434, 528, 0]);
	var tally = UI_create_new_object(SHAPE_SCROLL);
	tally->set_item_frame(0);
	tally->set_item_quality(TALLY_QUALITY);
	tally->set_item_flag(OKAY_TO_TAKE);
	crate->give_last_created();

	var chips = UI_create_new_object(SHAPE_STONE_CHIPS);
	chips->set_item_frame(0);
	chips->set_item_flag(OKAY_TO_TAKE);
	UI_update_last_created([1593, 371, 0]);
}

// All three clues: the stone went from Minoc to Buccaneer's Den.
void checkMinoc () {
	if (gflags[MINOC_DONE] || !gflags[MINOC_TRACE] || !gflags[MINOC_RECEIPT] || !gflags[MINOC_WITNESS]) {
		return;
	}
	gflags[MINOC_DONE] = true;
	if (canSpeak(IOLO)) {
		IOLO.say("@Cut in the lower tunnel, dressed at the Guild, paid for by Batlin, carted to the shore "
			+ "and off by coaster to the Den. What it was for, no one here knows.@");
		say("@But the ship that took it is at Buccaneer's Den, or was. Someone there remembers her.@*");
		IOLO.hide();
	} else {
		say("Cut in the lower tunnel, dressed at the Guild, paid for by Batlin, carted to the shore and off "
			+ "by coaster to Buccaneer's Den. What it was for, no one in Minoc knows.*");
	}
	checkActTwo();
}

void showReceipt (var scroll) {
	UI_book_mode(scroll);
	say("Britannian Mining Company~Minoc branch~~"
		+ "Received of Batlin of Britain, two thousand gold, for one block of black stone from the lower tunnel, "
		+ "eleven hundredweight, to be dressed at the Artists' Guild, crated by the 25th, and carted to the "
		+ "shore road for the coaster to Buccaneer's Den.~~"
		+ "3rd month, 9th day~~Gregor, for the Company");
}

void showTally (var scroll) {
	UI_book_mode(scroll);
	say("Coaster Gull, out of Minoc~~"
		+ "1 crate, stone, very heavy. Took six men and the yard tackle.~"
		+ "Nails, 40. Rope, 12 fathom.~"
		+ "For the Den, the big ship's master to sign.~~"
		+ "Sailed 3rd month, 27th day, on the morning tide. The fat brother came down himself "
		+ "to see it aboard, and paid in old crowns.");
}

// Gladstone: the stone was dressed in the Guild's workshop.
var gladstoneTalk () {
	GLADSTONE->show_npc_face(0);
	if (!gflags[SEEN_GLADSTONE]) {
		gflags[SEEN_GLADSTONE] = true;
		say("Gladstone is sweeping black grit from under his bench. He sees you and stops.");
	} else {
		say("@Avatar.@");
	}
	add(["black stone", "other matters", "bye"]);
	converse (0) {
		case "black stone" (remove):
			gflags[MINOC_TRACE] = true;
			say("@Ah. That.@ He leans on his broom. @Last year Gregor brought a block of black stone to the Guild "
				+ "and paid me to dress it. Square, polished, a socket cut in the base. It ate three of my best chisels.@");
			say("@I did not ask what it was for. Gregor paid well, and the Guild needed the money. "
				+ "I despised the Fellowship, and I took their gold anyway. Make of that what thou wilt.@");
			say("@He wanted it crated by the 25th of the third month and carted to the shore road. "
				+ "I am still sweeping up the chips. They make my teeth ache.@");
			checkMinoc();

		case "other matters":
			UI_clear_answers();
			UI_remove_npc_face(GLADSTONE);
			return true;

		case "bye":
			say("@Good journey, my friend.@*");
			break;
	}
	return false;
}

// Gregor: the Company sold the stone, and he has its receipt.
var gregorTalk () {
	GREGOR->show_npc_face(0);
	if (!gflags[SEEN_GREGOR]) {
		gflags[SEEN_GREGOR] = true;
		say("Gregor does not get up from his desk. @The Avatar. The Company is open for business, "
			+ "whatever thou mayest have heard.@");
	} else {
		say("@Art thou addressing me again?@ Gregor scowls.");
	}
	add(["black stone", "other matters", "bye"]);
	converse (0) {
		case "black stone" (remove):
			say("@The Company sells what it mines. To whom is the Company's business.@");
			if (gflags[READ_LEDGER]) {
				say("You tell him what Batlin's ledger says: two thousand gold, to the Company, for one black stone.");
				say("He is silent a while. @...Very well. One block from the lower tunnel, paid in full. "
					+ "I have nothing to hide. Here is my copy of the receipt.@");
				var copy = UI_create_new_object(SHAPE_SCROLL);
				copy->set_item_frame(0);
				copy->set_item_quality(RECEIPT_QUALITY);
				copy->set_item_flag(OKAY_TO_TAKE);
				if (!AVATAR->give_last_created()) {
					UI_update_last_created(AVATAR->get_object_position());
				}
			}

		case "other matters":
			UI_clear_answers();
			UI_remove_npc_face(GREGOR);
			return true;

		case "bye":
			say("@Be on thy way then.@*");
			break;
	}
	return false;
}

// Mikos, the reluctant witness: who cut it, and where it went.
var mikosTalk () {
	MIKOS->show_npc_face(0);
	if (!gflags[SEEN_MIKOS]) {
		gflags[SEEN_MIKOS] = true;
		say("Mikos heaves his shoulders and sighs. @What dost thou want this time?@");
	} else {
		say("@Thou again.@");
	}
	add(["lower tunnel", "other matters", "bye"]);
	converse (0) {
		case "lower tunnel":
			if (gflags[MINOC_WITNESS]) {
				say("@I told thee all I know, and more than I should.@");
			} else {
				say("@I do not talk about the lower tunnel.@ He spits on the ground.");
				var answer = askForResponse(["the king's decree", "the receipt", "Owings and Malloy"]);
				if (answer == "the king's decree") {
					say("@No one punished for having belonged. Hm. That is what they say before they punish thee.@");
					say("He looks at you a long time, then sighs. @Very well.@");
					gflags[MINOC_WITNESS] = true;
				} else if (answer == "the receipt" && gflags[MINOC_RECEIPT]) {
					say("@Gregor wrote it down? Then there is no use my keeping quiet, is there.@");
					gflags[MINOC_WITNESS] = true;
				} else if (answer == "the receipt") {
					say("@What receipt? I know of no receipt.@");
				} else {
					say("@Leave them out of it. They cut what they were told to cut.@");
				}
				if (gflags[MINOC_WITNESS]) {
					say("@Owings and Malloy cut it, in the branch tunnel. Batlin came himself, in the dark, "
						+ "and stood there all night watching them. I never saw a man look at a stone like that, "
						+ "as if it owed him money.@");
					say("@It went out on a cart the night of the 25th, down the shore road to the warehouses. "
						+ "A coaster took it off two days later, for the Den. That is all I know.@");
					checkMinoc();
				}
			}

		case "other matters":
			UI_clear_answers();
			UI_remove_npc_face(MIKOS);
			return true;

		case "bye":
			say("@Do not wander around down here.@*");
			break;
	}
	return false;
}

void Gladstone object#(0x455) () {
	if (event == DOUBLECLICK && gflags[ACT_TWO]) {
		if (!gladstoneTalk()) {
			return;
		}
	}
	Gladstone.original();
}

void Gregor object#(0x452) () {
	if (event == DOUBLECLICK && gflags[ACT_TWO]) {
		if (!gregorTalk()) {
			return;
		}
	}
	Gregor.original();
}

void Mikos object#(0x461) () {
	if (event == DOUBLECLICK && gflags[ACT_TWO]) {
		if (!mikosTalk()) {
			return;
		}
	}
	Mikos.original();
}
