/*
 *	Act II, The Lost Expedition (months 5-11): the month gate after Lord
 *	British hears of the voyage, the bridge's documents, and the act's close.
 */

// The friends who never had their supper go home anyway.
void guestsGoHome () {
	if (gflags[SUPPER_HELD]) {
		return;
	}
	var guests = supperGuests();
	for (npc in guests) {
		if (inParty(npc)) {
			UI_remove_from_party(npc);
		}
		UI_revert_schedule(npc);
		UI_run_schedule(npc);
	}
}

// The fifth month: Minoc, the Den and the Lycaeum are ready.
void actTwoBegins object#() () {
	if (gflags[ACT_TWO]) {
		return;
	}
	gflags[ACT_TWO] = true;
	UI_fade_palette(12, 1, 0);
	var supper = gflags[SUPPER_HELD] || !supperGuests();
	guestsGoHome();
	passDays(100, 9);
	restParty();
	minocAtActTwo();
	denAtActTwo();
	UI_fade_palette(12, 1, 1);

	var friends = "";
	if (!supper) {
		friends = "Spark and the others went home without their supper. Spark has written twice to complain.*";
	}
	bridgeCard("Months pass. The Fellowship's hall in Britain becomes a granary, and the medallions go into "
		+ "the melting pot at Minoc and come out as horseshoes.*"
		+ friends
		+ "Lord British's letters go out to every port: a black stone, a ship from the Den, a fat man in a fine cloak. "
		+ "Nothing comes back.*"
		+ "It is the fifth month since the Gate. Iolo has stopped asking when you will go to Minoc. "
		+ "He has started packing.");
}

// Minoc, the Den and the Lycaeum done, and the dawn dated: the act closes.
void checkActTwo () {
	if (gflags[ACT_TWO_DONE] || !gflags[MINOC_DONE] || !gflags[HEARD_RUDYOM] || !gflags[DAWN_DATED]) {
		return;
	}
	gflags[ACT_TWO_DONE] = true;
	if (canSpeak(IOLO)) {
		IOLO.say("@The eighteenth month.@ Iolo looks west, as if the sea were just past the wall. "
			+ "@A stone, a ship, a chart, a dawn. Every road we have walked this year runs out into the Great Ocean.@");
		say("@That is a long while to wait for a sunrise. Gwenno would not have waited.@ He smiles a little. "
			+ "@But then, Gwenno had a ship.@*");
		IOLO.hide();
	} else {
		say("A stone, a ship, a chart, a dawn: every road walked this year runs out into the Great Ocean. "
			+ "The eighteenth month is a long while away.*");
	}
	var avatar = UI_get_avatar_ref();
	script avatar after 1 ticks call underworldInterlude;
}

// After a document is read, in a run of its own (book mode lasts to a run's end).
void passengersRead object#() () {
	if (gflags[READ_PASSENGERS]) {
		return;
	}
	gflags[READ_PASSENGERS] = true;
	ioloSeesGwenno();
	checkActOne();
}

void receiptRead object#() () {
	if (gflags[MINOC_RECEIPT]) {
		return;
	}
	gflags[MINOC_RECEIPT] = true;
	checkMinoc();
	checkActTwo();
}
