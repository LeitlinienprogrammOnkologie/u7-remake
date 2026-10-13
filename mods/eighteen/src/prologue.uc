/*
 *	Prologue, After the Gate (months 1-4 begin; Q01 The Quiet After Victory).
 *	The remake runs AfterEndgame after a won endgame, instead of quitting.
 */

// Lord British's throne room: he sits at noon (his schedule's 12h slot).
const int THRONE_X = 935;
const int THRONE_Y = 1142;

// The meeting-goers' evenings at the Fellowship hall end: they go home at
// nine instead (their own beds and homes, from their schedules).
void endMeetings () {
	UI_modify_schedule(NANNA, 7, SLEEP, [964, 1155]);
	UI_modify_schedule(CANDICE, 7, LOITER, [1049, 1094]);
	UI_modify_schedule(PATTERSON, 7, LOITER, [1045, 1091]);
	UI_modify_schedule(FIGG, 7, SLEEP, [772, 1442]);
	UI_modify_schedule(GAYE, 7, SLEEP, [740, 1474]);
	UI_modify_schedule(GRAYSON, 7, SLEEP, [777, 1460]);
	UI_modify_schedule(GORDON, 7, SLEEP, [775, 1498]);
	UI_modify_schedule(SEAN, 7, SLEEP, [1012, 1315]);
}

// The clerk: Millie keeps the hall's records by day, or a stand-in does.
var clerk () {
	return firstAlive([MILLIE, CANDICE, GAYE]);
}

// The Fellowship hall in Britain, four weeks on: Batlin gone, the emblems
// coming down, the clerk among the records, his strongbox in the office.
void hallAfterTheGate () {
	BATLIN->remove_npc();
	UI_remove_from_area(SHAPE_FELLOWSHIP_ICON, FRAME_ANY, [805, 1305], [848, 1342]);
	UI_remove_from_area(SHAPE_FELLOWSHIP_TAPESTRY, FRAME_ANY, [805, 1305], [848, 1342]);
	endMeetings();

	var keeper = clerk();
	if (keeper) {
		UI_set_new_schedules(keeper, [2, 6, 0], [LOITER, WANDER, SLEEP],
			[836, 1326, 860, 1305, 740, 1463]);
		keeper->move_object([836, 1326, 0]);
		UI_run_schedule(keeper);
	}

	var box = UI_create_new_object(SHAPE_LOCKED_CHEST);
	box->set_item_frame(0);
	box->set_item_quality(STRONGBOX_KEY);
	UI_update_last_created([840, 1327, 0]);

	var ledger = UI_create_new_object(SHAPE_BOOK);
	ledger->set_item_frame(3);
	ledger->set_item_quality(LEDGER_QUALITY);
	ledger->set_item_flag(OKAY_TO_TAKE);
	box->give_last_created();

	var passengers = UI_create_new_object(SHAPE_SCROLL);
	passengers->set_item_frame(0);
	passengers->set_item_quality(PASSENGERS_QUALITY);
	passengers->set_item_flag(OKAY_TO_TAKE);
	box->give_last_created();

	// No clerk left alive: the crown's clerks leave the key on the desk.
	if (!keeper) {
		var key = UI_create_new_object(SHAPE_KEY);
		key->set_item_frame(3);
		key->set_item_quality(STRONGBOX_KEY);
		key->set_item_flag(OKAY_TO_TAKE);
		UI_update_last_created([842, 1329, 2]);
	}
}

// Iolo, Shamino and Dupre come to the castle: the friends who go on.
void gatherCompanions () {
	var x = THRONE_X - 2;
	var companions = [IOLO, SHAMINO, DUPRE];
	for (npc in companions) {
		if (!UI_is_dead(npc) && !inParty(npc)) {
			npc->move_object([x, THRONE_Y + 6, 0]);
			UI_add_to_party(npc);
		}
		x = x + 2;
	}
}

// After a won endgame: four weeks pass, and Lord British calls the avatar.
void AfterEndgame () {
	gflags[BRIDGE_STARTED] = true;
	UI_fade_palette(12, 1, 0);

	AVATAR->clear_item_flag(DONT_RENDER);
	AVATAR->clear_item_flag(BG_DONT_MOVE);
	UI_move_object(PARTY, [THRONE_X, THRONE_Y + 5, 0]);
	gatherCompanions();
	passDays(28, 12);
	restParty();
	hallAfterTheGate();
	LORD_BRITISH->move_object([THRONE_X, THRONE_Y, 0]);
	UI_run_schedule(LORD_BRITISH);

	UI_fade_palette(12, 1, 1);
	// Book mode lasts to the end of a usecode run (Exult set_book): the
	// audience runs on its own, after the card.
	var king = UI_get_npc_object(LORD_BRITISH);
	script king after 1 ticks call lordBritishAudience;
	bridgeCard("The Black Gate lies broken on the Isle of the Avatar, and the Guardian's voice is silent.~~"
		+ "Batlin is gone. At the Gate's foot he spoke a spell and vanished, and no one has seen him since.*"
		+ "Four weeks have passed. Britain has had its festival and is sweeping up after it, "
		+ "and Lord British has called thee to his throne.");
}

// The first audience: the decree, the inquiry, Paws.
void lordBritishAudience object#() () {
	LORD_BRITISH->show_npc_face(0);
	say("@Avatar! Come, come. I have waited a month to thank thee without a herald shouting over me.@");
	say("@Thou hast cast down the Gate, yet the shadow it raised remains among my people. "
		+ "I ask not that thou chase every rumor. I ask that those deceived by the Fellowship "
		+ "not be made to suffer again for having believed.@");
	gflags[HEARD_DECREE] = true;

	add(["Batlin", "decree", "inquiry", "Paws", "friends", "bye"]);
	converse (0) {
		case "Batlin" (remove):
			say("@My rangers have searched every port and every road. Batlin is not in Britannia, "
				+ "or he hides better than any man I have known.@");
			say("@He did not flee blindly, I think. A man who planned the Gate planned his escape as well.@");

		case "decree" (remove):
			say("@The Fellowship is no more. Its halls are closed, its banners come down, and its funds "
				+ "belong to the crown until we know whose they were.@");
			say("@But no one shall be punished for having belonged to it. Only for what was done.@");
			if (canSpeak(IOLO)) {
				IOLO.say("@'Tis a merciful decree, my liege. It will not be a popular one in every tavern.@");
				IOLO.hide();
				LORD_BRITISH.say("@Mercy rarely is, Iolo.@");
			}

		case "inquiry" (remove):
			say("@My clerks are taking inventories in the Fellowship's hall here in Britain. "
				+ "They find ledgers, letters, a great deal of paper, and a clerk who will not let them read it.@");
			say("@I would have thee look, Avatar. If Batlin prepared his flight, he paid for it, "
				+ "and someone wrote down what he paid.@");
			var keeper = clerk();
			if (keeper == MILLIE) {
				say("@The hall's clerk is a young woman named Millie. She is... devoted. Be gentle with her.@");
			} else if (keeper) {
				say("@One of their own keeps the records now, " + UI_get_npc_name(keeper)
					+ ". Be gentle; the town has not been.@");
			} else {
				say("@No one of theirs is left to keep the records. My clerks have left the key to Batlin's "
					+ "strongbox on the desk in his office.@");
			}

		case "Paws" (remove):
			say("@The Fellowship ran a shelter for the poor in Paws, the only one in Britannia. "
				+ "Now its coin comes from my treasury and its keepers are Fellowship folk.@");
			say("@My council would close it tomorrow; my heart would not. Go and see it, Avatar, "
				+ "and tell them what thou wouldst do. I shall abide by thy word.@");
			if (canSpeak(DUPRE)) {
				DUPRE.say("@Then I am coming to Paws. I have seen what becomes of the poor when the great forget them.@");
				DUPRE.hide();
			}

		case "friends" (remove):
			say("@Thy friends came to Britain for the festival and stayed. I think they were waiting for thee.@");
			if (canSpeak(SHAMINO)) {
				SHAMINO.say("@I have been reading the Fellowship's books since the Gate fell. Dull stuff, mostly. "
					+ "But Batlin knew more old lore than he ever preached.@");
				SHAMINO.hide();
			}
			if (canSpeak(DUPRE)) {
				DUPRE.say("@And I have drunk thy health in every tavern from Jhelom to here. "
					+ "'Tis thy turn to stand a round.@");
				DUPRE.hide();
			}

		case "bye":
			say("@Go with my blessing. And, Avatar... rest a little, too. Thou hast earned that much.@*");
			break;
	}
}

// Act II's month gate: once Act I is closed and he has heard of the voyage.
void actTwoGate () {
	if (gflags[ACT_ONE_CLOSED] && gflags[REPORTED_LEDGER] && !gflags[ACT_TWO]) {
		var avatar = UI_get_avatar_ref();
		script avatar after 1 ticks call actTwoBegins;
	}
}

// Lord British, while the bridge runs: what the avatar has found, then his
// own conversation (the keyring's, then Black Gate's).
void LordBritish object#(0x417) () {
	if (event != DOUBLECLICK || !gflags[BRIDGE_STARTED]) {
		LordBritish.original();
		return;
	}
	if (lordBritishActThree()) {
		return;
	}
	var news = [];
	if (gflags[LEDGER_RECONCILED] && !gflags[REPORTED_LEDGER]) {
		news << "Batlin's ledger";
	}
	if ((gflags[PAWS_CLOSED] || gflags[PAWS_OVERSIGHT] || gflags[PAWS_STEWARDS]) && !gflags[REPORTED_PAWS]) {
		news << "Paws";
	}
	if (gflags[DEN_DONE] && !gflags[GOT_GWENNOS_MAP]) {
		news << "Gwenno";
	}
	if (!news) {
		if (gflags[ACT_ONE_CLOSED] && gflags[REPORTED_LEDGER] && !gflags[ACT_TWO]) {
			LORD_BRITISH->show_npc_face(0);
			say("@Go, Avatar. Minoc and the Den are waiting, and so, I think, is Iolo.@*");
			actTwoGate();
			return;
		}
		LordBritish.original();
		return;
	}

	LORD_BRITISH->show_npc_face(0);
	say("@Avatar! What news?@");
	add(news);
	add(["other matters", "bye"]);
	converse (0) {
		case "Batlin's ledger" (remove):
			gflags[REPORTED_LEDGER] = true;
			say("You tell Lord British of the stone cut in Minoc, the ship paid for at Buccaneer's Den, "
				+ "and the scholars meant to sail with it.");
			say("@A voyage.@ He is quiet for a long moment. @Then he did not run. He went.@");
			say("@Find me the master of that ship, Avatar, and the men who cut that stone. "
				+ "Minoc and the Den. Take what time thou needest.@");

		case "Paws" (remove):
			gflags[REPORTED_PAWS] = true;
			if (gflags[PAWS_CLOSED]) {
				say("@Closed, then. My council will be pleased.@ He does not look pleased. "
					+ "@I shall see that the poor of Paws are fed some other way. See that I do, Avatar.@");
			} else if (gflags[PAWS_OVERSIGHT]) {
				say("@Kept, and watched. Feridwyn's accounts to my treasurer every month. "
					+ "That is a burden for both of us. I think it a fair one.@");
			} else {
				say("@Given to the town itself! I like that. The crown's coin and the town's hands. "
					+ "We shall see whether Paws thanks thee.@");
			}

		case "Gwenno" (remove):
			say("You tell Lord British of the old pirate's log: the chart, the ring in the western sea, "
				+ "and Gwenno sailing with Batlin's ship.");
			lordBritishGivesMap();

		case "other matters":
			UI_clear_answers();
			UI_remove_npc_face(LORD_BRITISH);
			LordBritish.original();
			actTwoGate();
			return;

		case "bye":
			say("@Fare thee well, Avatar.@*");
			break;
	}
	actTwoGate();
}
