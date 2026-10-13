/*
 *	Paws: Q03 The Shelter. Three perspectives (the caregiver Brita, the
 *	former initiate Merrick, the family: Alina and her baby), the choice
 *	(close it, keep it under the crown's eye, give it to the town's stewards:
 *	Camille), and the revisit from Act II on, which shows the choice.
 */

// The shelter's people and their stand-ins (BRIDGE.md, risk 2).
var shelterManager () {
	return firstAlive([FERIDWYN, BRITA]);
}

var steward () {
	return firstAlive([CAMILLE, THURSTON, ANDREW]);
}

var stewardName () {
	return UI_get_npc_name(steward());
}

var pawsDecided () {
	return gflags[PAWS_CLOSED] || gflags[PAWS_OVERSIGHT] || gflags[PAWS_STEWARDS];
}

// The perspectives not yet heard, by name; a role whose people are all dead
// counts as heard.
var pawsUnheard () {
	var names = [];
	if (!gflags[PAWS_HEARD_CAREGIVER] && !UI_is_dead(BRITA)) {
		names << "Brita";
	}
	if (!gflags[PAWS_HEARD_INITIATE] && !UI_is_dead(MERRICK)) {
		names << "Merrick";
	}
	if (!gflags[PAWS_HEARD_FAMILY] && !UI_is_dead(ALINA)) {
		names << "Alina";
	}
	return names;
}

// The revisit's line for one of the shelter's people, once months have passed.
var pawsRevisit (var closed, var watched, var town) {
	if (!gflags[ACT_TWO] || !pawsDecided()) {
		return false;
	}
	if (gflags[PAWS_CLOSED]) {
		say(closed);
	} else if (gflags[PAWS_OVERSIGHT]) {
		say(watched);
	} else {
		say(town);
	}
	return true;
}

// The choice's immediate, visible effect on the shelter's days.
void pawsDecide (var outcome) {
	gflags[outcome] = true;
	var keeper = steward();
	if (outcome == PAWS_CLOSED) {
		UI_modify_schedule(FERIDWYN, 0, LOITER, [845, 1717]);
		UI_modify_schedule(BRITA, 2, WANDER, [835, 1700]);
		UI_modify_schedule(ALINA, 4, LOITER, [988, 1812]);
		UI_modify_schedule(MERRICK, 4, LOITER, [988, 1812]);
	} else if (outcome == PAWS_STEWARDS) {
		UI_modify_schedule(FERIDWYN, 0, LOITER, [835, 1722]);
		if (keeper) {
			UI_modify_schedule(keeper, 2, TEND_SHOP, [839, 1729]);
		}
	}
	var shelter = [FERIDWYN, BRITA, ALINA, MERRICK, keeper];
	for (npc in shelter) {
		if (npc && !UI_is_dead(npc)) {
			UI_run_schedule(npc);
		}
	}
}

// The decision, given to the shelter's manager.
void pawsDecision (var manager) {
	var who = UI_get_npc_name(manager);
	var unheard = pawsUnheard();
	if (unheard) {
		say("@Ere thou decidest anything, speak with " + listNames(unheard)
			+ ". They will tell thee more truth than I.@");
		return;
	}
	say("@Then tell me, Avatar. What becomes of this place?@");
	var keeper = steward();
	var choices = ["close it", "keep it, watched"];
	if (keeper) {
		choices << "give it to the town";
	}
	var answer = askForResponse(choices);
	if (answer == "close it") {
		say("@Then it is closed.@ " + who + " sets the account book down very carefully. "
			+ "@I shall tell them at supper. It will be the last one.@");
		pawsDecide(PAWS_CLOSED);
		if (canSpeak(DUPRE)) {
			DUPRE.say("@Then the crown must feed them some other way. I shall hold Lord British to it.@");
			DUPRE.hide();
		}
	} else if (answer == "keep it, watched") {
		say("@Under watch, then. Accounts to the castle every month, and a crown's clerk counting the bowls.@");
		say(who + " nods slowly. @I would rather be watched than useless.@");
		pawsDecide(PAWS_OVERSIGHT);
	} else {
		var name = UI_get_npc_name(keeper);
		if (keeper == CAMILLE) {
			say("@To Camille?@ For a moment " + who + " looks as if a lemon had been bitten. Then the look passes. "
				+ "@She will do well. Better than we, mayhap. She never needed a Triad to be kind.@");
		} else {
			say("@To the town? To " + name + " and the rest?@ A long breath. @They will do well. Better than we, mayhap.@");
		}
		pawsDecide(PAWS_STEWARDS);
	}
	if (canSpeak(IOLO)) {
		IOLO.say("@'Tis decided, then. Lord British will want to hear of it.@");
		IOLO.hide();
	}
}

// Feridwyn, who ran the shelter by the Triad (or Brita, if he is dead).
void managerTalk (var npc) {
	npc->show_npc_face(0);
	if (pawsRevisit("@I stand here out of habit. The door is locked; the crown has the key.@",
			"@The crown's clerk counted forty-two bowls yesterday. He was surprised. I was not.@",
			"@I carry the flour now, and someone else gives the orders. It is restful, not to give orders.@")) {
		add(["bye"]);
	} else {
		if (!gflags[SEEN_FERIDWYN]) {
			gflags[SEEN_FERIDWYN] = true;
			say(UI_get_npc_name(npc) + " stands in the doorway with an account book, as if expecting to be asked for it.");
			say("@Avatar. I have heard. The crown pays our bills now, for the present. "
				+ "I suppose thou art here to tell me for how long.@");
		} else {
			say("@Avatar.@");
		}
		add(["job", "shelter", "bye"]);
		if (!pawsDecided()) {
			add("decision");
		}
	}

	converse (0) {
		case "job" (remove):
			say("@I ran this shelter by the Triad. I believed in it. I was hard on people. "
				+ "I thought hardness was a kind of help.@");
			say("@Now I do not know what I thought.@");

		case "shelter" (remove):
			say("@Forty bowls at noon, twelve beds, one roof that leaks. The coin came from Britain every month, "
				+ "by Batlin's own hand. The last of it came a week before the end.@");

		case "decision":
			pawsDecision(npc);
			if (pawsDecided()) {
				UI_remove_answer("decision");
			}

		case "bye":
			say("@Good day, Avatar.@*");
			break;
	}
}

// Brita, the caregiver: the bread was theirs (the design's 9.4).
void caregiverTalk () {
	BRITA->show_npc_face(0);
	if (pawsRevisit("@They queue at Polly's door now, for what she can spare. Some of them spit when I pass. "
				+ "I do not blame them.@",
			"@They come back, slowly. Some of them still will not look at me, but they eat.@",
			"@" + stewardName() + " has me teaching the farm girls to bake. They are terrible at it. I am enjoying myself.@")) {
		add(["bye"]);
	} else {
		if (!gflags[SEEN_BRITA]) {
			gflags[SEEN_BRITA] = true;
			say("Brita is kneading dough, her sleeves rolled to the elbow and flour on her cheek.");
			say("@Avatar. Come to close us, I suppose.@");
		} else {
			say("@Back again? Then make thyself useful and hand me that bowl.@");
		}
		add(["name", "job", "shelter", "Fellowship", "bye"]);
		if (shelterManager() == BRITA && !pawsDecided()) {
			add("decision");
		}
	}

	converse (0) {
		case "name" (remove):
			say("@Brita. Feridwyn's wife, the cook, the one who said such clever things about the poor of this town.@");
			say("She does not smile.");

		case "job" (remove):
			say("@I run the kitchen. Feridwyn keeps the accounts; I keep the people fed. "
				+ "Forty bowls at noon, more in a hard month.@");

		case "shelter" (remove):
			gflags[PAWS_HEARD_CAREGIVER] = true;
			say("@The sign upon the door was theirs, aye. But the bread was baked by our own hands. "
				+ "If thou tear down the sign, who shall bake tomorrow?@");

		case "Fellowship" (remove):
			gflags[PAWS_HEARD_CAREGIVER] = true;
			say("@I said terrible things about the poor of this town. I said them because Batlin's books said "
				+ "them first, and it was comfortable to believe.@");
			say("@I am not proud of that. I am proud of the bread.@");

		case "decision":
			pawsDecision(BRITA);
			if (pawsDecided()) {
				UI_remove_answer("decision");
			}

		case "bye":
			say("@Mind the step, it is loose. Everything here is loose.@*");
			break;
	}
}

// Merrick, the former initiate: the Fellowship gave him a bed.
void initiateTalk () {
	MERRICK->show_npc_face(0);
	if (pawsRevisit("@I sleep in Thurston's barn now. He lets me, for the work. It is not the road. Not quite.@",
			"@The crown's man asked me what the Fellowship meant to me. I told him, and he wrote it all down. "
				+ "No one ever wrote down anything I said before.@",
			"@" + stewardName() + " set me to the vegetable rows. I was a farmer once. I had forgotten.@")) {
		add(["bye"]);
	} else {
		if (!gflags[SEEN_MERRICK]) {
			gflags[SEEN_MERRICK] = true;
			say("Merrick sits on his bunk, turning a Fellowship medallion over and over in his fingers.");
			say("@Avatar. Hast thou come to tell me I was a fool? Komor already did. Twice.@");
		} else {
			say("@Thou again.@");
		}
		add(["name", "job", "Fellowship", "shelter", "bye"]);
	}

	converse (0) {
		case "name" (remove):
			say("@Merrick. Farmer, pauper, initiate. Now I do not know which.@");

		case "job" (remove):
			say("@I had one. I spread the word. Now there is no word.@");

		case "Fellowship" (remove):
			gflags[PAWS_HEARD_INITIATE] = true;
			say("@Thou wilt say they lied to me. Mayhap they did. But I slept by the road for seven years, "
				+ "and the Fellowship gave me a bed, and the roadside never did.@");
			say("@Batlin never asked me for anything but my belief. It was all I had, so I gave it.@");

		case "shelter" (remove):
			gflags[PAWS_HEARD_INITIATE] = true;
			say("@If they close it, I go back to the road. Komor and Fenn will laugh. "
				+ "'Tis the only thing they ever had from me.@");
			if (canSpeak(DUPRE)) {
				DUPRE.say("@No one is going back to the road, Merrick. Not while I have a say in it.@");
				DUPRE.hide();
			}

		case "bye":
			say("@Good day.@*");
			break;
	}
}

// Alina and her baby: the family that relies on the kitchen.
void familyTalk () {
	ALINA->show_npc_face(0);
	if (pawsRevisit("@We eat at whatever table will have us, and at none when none will.@",
			"@No one asks me to recite a Triad before the soup now. The soup is the same. "
				+ "I did not think that would matter so much.@",
			"@" + stewardName() + " says Cassie may help in the garden when she can walk. Imagine. "
				+ "A place that expects her to grow up.@")) {
		add(["bye"]);
	} else {
		if (!gflags[SEEN_ALINA]) {
			gflags[SEEN_ALINA] = true;
			say("Alina is feeding her baby porridge from a wooden spoon.");
		}
		say("@Good day, Avatar.@");
		add(["name", "Weston", "shelter", "bye"]);
	}

	converse (0) {
		case "name" (remove):
			say("@Alina. I am Cassie's mother, and that is work enough.@");

		case "Weston" (remove):
			if (gflags[WESTON_FREED]) {
				say("@Weston is free, thanks to thee, and works the fields where he can. "
					+ "But a field pays at harvest, and Cassie eats every day.@");
			} else {
				say("@Weston is still in the castle's prison, for the fruit he meant to pay for. "
					+ "They say the king pardons those who are spoken for.@");
			}

		case "shelter" (remove):
			gflags[PAWS_HEARD_FAMILY] = true;
			say("@They made me feel a beggar for every bowl, and I hated them for it. "
				+ "And still my daughter never went hungry under this roof.@");
			say("@I do not know how to hold both of those things, Avatar. Mayhap thou dost.@");

		case "bye":
			say("@Say goodbye, Cassie.@ The baby blows a bubble.*");
			break;
	}
}

// Camille (or the town's stand-in steward): the town could keep it.
// Returns true when the player asks for her own conversation.
var stewardTalk (var npc) {
	npc->show_npc_face(0);
	if (pawsRevisit("@I feed whom I can. The farm is not a shelter, Avatar, and I am not the crown.@",
			"@The Fellowship's man, under the crown's eye. Well. I watch, too.@",
			"@It is harder than farming and less tidy. I would not give it back.@")) {
		add(["other matters", "bye"]);
	} else {
		say("@Avatar! Hast thou been to the shelter?@");
		add(["shelter", "other matters", "bye"]);
	}

	converse (0) {
		case "shelter" (remove):
			gflags[HEARD_STEWARD] = true;
			say("@The Fellowship had the coin, so they had the say. If the coin came from the crown, "
				+ "the town could keep the shelter itself.@");
			say("@I would help. Thurston has flour to spare, and he would give it, "
				+ "if no one asked him to recite a Triad first.@");

		case "other matters":
			UI_clear_answers();
			UI_remove_npc_face(npc);
			return true;

		case "bye":
			say("@Pleasant journey, Avatar.@*");
			break;
	}
	return false;
}

void Feridwyn object#(0x4A7) () {
	if (event == DOUBLECLICK && gflags[BRIDGE_STARTED]) {
		managerTalk(FERIDWYN);
		UI_remove_npc_face(FERIDWYN);
		checkActOne();
		return;
	}
	Feridwyn.original();
}

void Brita object#(0x4A8) () {
	if (event == DOUBLECLICK && gflags[BRIDGE_STARTED]) {
		caregiverTalk();
		UI_remove_npc_face(BRITA);
		checkActOne();
		return;
	}
	Brita.original();
}

void Alina object#(0x4A9) () {
	if (event == DOUBLECLICK && gflags[BRIDGE_STARTED]) {
		familyTalk();
		return;
	}
	Alina.original();
}

void Merrick object#(0x4AA) () {
	if (event == DOUBLECLICK && gflags[BRIDGE_STARTED]) {
		initiateTalk();
		return;
	}
	Merrick.original();
}

void Camille object#(0x4B1) () {
	if (event == DOUBLECLICK && gflags[BRIDGE_STARTED] && steward() == CAMILLE) {
		if (!stewardTalk(CAMILLE)) {
			return;
		}
	}
	Camille.original();
}
