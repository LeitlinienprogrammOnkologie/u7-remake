/*
 *	Act I's close: the ledger and the passage list read together (Iolo), and
 *	the optional farewell supper at the Blue Boar with the friends who go
 *	home (Spark, Jaana, Sentri, Julia).
 */

// The Blue Boar's corner table (Lucy's inn, Britain).
const int BOAR_X = 903;
const int BOAR_Y = 1367;

var actOneDone () {
	return gflags[LEDGER_RECONCILED] && gflags[READ_PASSENGERS] && pawsDecided();
}

// The friends who go home after Act I, those still alive.
var supperGuests () {
	var guests = [];
	var friends = [SPARK, JAANA, SENTRI, JULIA];
	for (npc in friends) {
		if (!UI_is_dead(npc)) {
			guests << npc;
		}
	}
	return guests;
}

// The friends not travelling with the avatar wait at the Blue Boar.
void guestsToTheInn () {
	var guests = supperGuests();
	var x = BOAR_X - 2;
	for (npc in guests) {
		if (!inParty(npc)) {
			UI_set_new_schedules(npc, [0, 6], [LOITER, EAT], [BOAR_X, BOAR_Y - 2, BOAR_X, BOAR_Y]);
			npc->move_object([x, BOAR_Y - 3, 0]);
			UI_run_schedule(npc);
			x = x + 1;
		}
	}
}

// Once the ledger is reconciled, the passage list read and the shelter
// decided: Iolo puts it together.
void checkActOne () {
	if (gflags[ACT_ONE_CLOSED] || !actOneDone()) {
		return;
	}
	gflags[ACT_ONE_CLOSED] = true;
	if (canSpeak(IOLO)) {
		IOLO.say("@Avatar, a word.@ Iolo has the ledger and the passage list spread across his knees.");
		say("@A stone cut in Minoc. A ship paid for at Buccaneer's Den. Scholars to sail with it, and Batlin's "
			+ "own hand on every entry. That is no flight, my friend. That is a voyage, planned months "
			+ "before the Gate fell.@");
		say("@And Gwenno's name among them.@ He folds the list very carefully. @I would know where that ship went.@");
		if (supperGuests()) {
			say("@But first, Spark and the others mean to sup with us at the Blue Boar ere they go home. "
				+ "Any evening, they said. I would not miss it.@");
		}
		say("@And Lord British should hear of this.@*");
		IOLO.hide();
	} else {
		say("The ledger and the passage list together tell a plain story: a stone cut in Minoc, a ship paid "
			+ "for at Buccaneer's Den, scholars to sail with it. Batlin did not flee. He went somewhere by sea, "
			+ "and planned it months before the Gate fell. Lord British should hear of it.*");
	}
	guestsToTheInn();
}

// Whether the avatar is at the Blue Boar of an evening.
var supperTime () {
	var pos = AVATAR->get_object_position();
	var hour = UI_game_hour();
	return pos[1] >= 895 && pos[1] <= 916 && pos[2] >= 1357 && pos[2] <= 1379 && hour >= 18;
}

// The farewell supper: under ten minutes, affectionate, no knowledge of
// what is to come (the design's 4.2).
void farewellSupper () {
	gflags[SUPPER_HELD] = true;
	say("The Blue Boar's corner table is crowded with old friends, a roast, and too many mugs.");
	var guests = supperGuests();
	for (npc in guests) {
		if (npc == SPARK) {
			SPARK.say("@I am taller now. Didst thou notice? Iolo says I am not. Iolo is old.@");
			say("@I'm going back to Trinsic. Someone has to look after the house. I'm the man of it now.@");
			SPARK.hide();
			if (canSpeak(IOLO)) {
				IOLO.say("@Thou art the man of it, lad. Thy father would have said so.@");
				IOLO.hide();
			}
		} else if (npc == SENTRI) {
			SENTRI.say("@A word of advice, Avatar, and then I shall drink. Thou art going after a man who "
				+ "planned the Gate. Such a man plans his road back, too. Watch thy back, and sleep with thy boots on.@");
			SENTRI.hide();
		} else if (npc == JAANA) {
			JAANA.say("@In Cove they ask me whether they may still trust the neighbours who wore the medallion. "
				+ "I tell them trust is built again one plank at a time, like a boat.@");
			JAANA.hide();
		} else if (npc == JULIA) {
			JULIA.say("@I keep wondering what will last of all this. Not the Gate, I hope.@ She lifts her mug. "
				+ "@Mayhap the friends.@");
			JULIA.hide();
		}
	}
	if (canSpeak(DUPRE)) {
		DUPRE.say("@To friends who go home, and friends who go on!@");
		DUPRE.hide();
	}
	say("The evening runs late. When at last the fire burns low, your friends go their ways.*");
	if (UI_game_hour() < 23) {
		passDays(0, 23);
	}
	for (npc in guests) {
		if (inParty(npc)) {
			UI_remove_from_party(npc);
		}
		UI_revert_schedule(npc);
		UI_run_schedule(npc);
	}
}

// A friend at the Blue Boar of an evening starts the supper; otherwise the
// friend's own conversation.
var supperStarts () {
	if (!gflags[ACT_ONE_CLOSED] || gflags[SUPPER_HELD] || !supperTime()) {
		return false;
	}
	farewellSupper();
	return true;
}

void Spark object#(0x402) () {
	if (event != DOUBLECLICK || !supperStarts()) {
		Spark.original();
	}
}

void Jaana object#(0x405) () {
	if (event != DOUBLECLICK || !supperStarts()) {
		Jaana.original();
	}
}

void Sentri object#(0x407) () {
	if (event != DOUBLECLICK || !supperStarts()) {
		Sentri.original();
	}
}

void Julia object#(0x408) () {
	if (event != DOUBLECLICK || !supperStarts()) {
		Julia.original();
	}
}
