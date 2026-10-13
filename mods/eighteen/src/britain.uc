/*
 *	Britain: Q01's former members (Candice, Gaye, Patterson) and Q02 The
 *	Ledger (the clerk, Batlin's strongbox, his accounts and the passage list).
 */

// The strongbox's key, from the clerk.
void clerkGivesKey () {
	gflags[CLERK_GAVE_KEY] = true;
	giveAvatar(SHAPE_KEY, 3, STRONGBOX_KEY);
}

// The clerk's last word, for one who helped her keep the donors' names.
void clerkLastWord () {
	if (gflags[CLERK_HELPED] && gflags[LEDGER_RECONCILED] && !gflags[CLERK_LAST_WORD]) {
		gflags[CLERK_LAST_WORD] = true;
		say("@One more thing, since thou wast kind to me. Batlin asked me once whether I could sail. "
			+ "I laughed. He did not.@");
	}
}

// Where did the stone go? The two entries reconciled; a wrong answer is
// corrected, never punished.
void clerkReconciles () {
	say("@Thou hast read it? Then tell me, for I cannot make sense of it. Where did his stone go?@");
	var answer = askForResponse(["to the Retreat", "to sea", "nowhere"]);
	if (answer == "to the Retreat") {
		say("@Then why pay a ship's master at the Den to carry a stone of the same weight, from Minoc, "
			+ "a fortnight after it was set up? And no one at the Retreat signed for it. Look again.@");
	} else if (answer == "nowhere") {
		say("@But the Mining Company was paid for the cutting, and the Den was paid half and promised "
			+ "the rest on landing. Nobody promises money for a stone that does not exist.@");
	} else {
		gflags[LEDGER_RECONCILED] = true;
		say("@...Yes.@ She sits down slowly. @The Retreat entry is the lie. It names no ship, "
			+ "and no one signed for it. He wrote it for the treasurer to read.@");
		if (canSpeak(IOLO)) {
			IOLO.say("@A stone cut in Minoc and carried to sea from Buccaneer's Den. Wherever Batlin went, "
				+ "Avatar, he went by ship, and he planned it months before the Gate fell.@");
			IOLO.hide();
		}
		clerkLastWord();
	}
}

// The clerk among the records: Millie, or a stand-in if she is dead.
void clerkTalk (var keeper) {
	var millie = keeper == MILLIE;
	keeper->show_npc_face(0);
	if (!gflags[SEEN_CLERK]) {
		gflags[SEEN_CLERK] = true;
		if (millie) {
			say("Millie sits among crates of papers with a quill behind each ear. She does not giggle.");
		} else {
			say(UI_get_npc_name(keeper) + " sits among crates of papers, sorting them into piles.");
		}
		say("@Oh. The Avatar. I suppose thou hast come to take the rest of it.@");
	} else {
		say("@Avatar.@ The piles of paper have grown since thy last visit.");
	}
	gflags[MET_AFFECTED_CITIZEN] = true;

	add(["name", "job", "records"]);
	if (gflags[CLERK_GAVE_KEY]) {
		add("strongbox");
	}
	if (gflags[READ_LEDGER]) {
		add("the Retreat");
		if (!gflags[LEDGER_RECONCILED]) {
			add("the ledger");
		}
	}
	if (millie) {
		add("Thad");
	}
	add("bye");

	converse (0) {
		case "name" (remove):
			if (millie) {
				say("@Millie. Thou knowest that. Everyone in Britain knows it now. "
					+ "'Millie, who talked to people about the Fellowship all day long.'@");
			} else {
				say("@Thou knowest my name, Avatar. Everyone in Britain knows it now.@");
			}

		case "job" (remove):
			say("@I keep the records. Someone must. The crown's clerks would throw them all in a sack "
				+ "and burn whatever they could not read.@");
			add("records");

		case "records" (remove):
			say("@These are the names of everyone who ever gave a coin to the Fellowship. Farmers. Widows. "
				+ "Children who gave a copper because their mothers did.@");
			say("@They believed, Avatar, as I did. Are they to be shamed in the square for a copper?@");
			if (!gflags[CLERK_HELPED]) {
				add("donors");
			}
			add("strongbox");

		case "donors" (remove):
			say("@The crown's clerks want every name. Wilt thou help me keep them sealed?@");
			if (askYesNo()) {
				say("@Then help me. The donors' rolls go in this crate, and I seal it, and thou dost witness it. "
					+ "Batlin's own accounts are another thing. Those were never ours.@");
				say("You spend an hour sorting rolls of names into the crate. She seals it with candle wax "
					+ "and presses her thumb into the seal.");
				say("@There. Thank thee, Avatar. Truly.@");
				gflags[CLERK_HELPED] = true;
				if (!gflags[CLERK_GAVE_KEY]) {
					say("@And here. The key to his strongbox. He gave it to me the week before... before. "
						+ "'Keep this, thou art the only one I trust.' I suppose he said that to everyone.@");
					clerkGivesKey();
					UI_remove_answer("strongbox");
				}
			} else {
				say("@Then thou art the crown's, like the rest of them.@ She folds her arms over the crate.");
			}

		case "strongbox":
			if (gflags[CLERK_GAVE_KEY]) {
				say("@I gave thee the key. His strongbox is in the office, behind the hall. "
					+ "What is in it was his, not ours.@");
			} else {
				say("@Batlin kept his own accounts in a strongbox in his office. I have the key, "
					+ "and I am not giving it to the crown's clerks. Why should I give it to thee?@");
				var answer = askForResponse(["his, not theirs", "the king's orders", "he used thee"]);
				if (answer == "his, not theirs") {
					say("@...That is true. There is nothing of the donors in his strongbox. Nothing of mine, either.@");
					say("She hands you a small iron key. @Take it before I think better of it.@");
					clerkGivesKey();
				} else if (answer == "the king's orders") {
					say("@I obeyed someone for three years, Avatar. I am resting from it.@");
				} else {
					say("Her eyes fill. @I joined of mine own free will. No one used me.@");
				}
			}

		case "the ledger":
			clerkReconciles();
			if (gflags[LEDGER_RECONCILED]) {
				UI_remove_answer("the ledger");
			}

		case "the Retreat" (remove):
			if (millie) {
				say("@A monument at the Meditation Retreat? I was there that very month, Avatar, "
					+ "to learn to hear the voice. There was no new stone. There was not even a new bench.@");
			} else {
				say("@A monument at the Meditation Retreat? I have heard of none, and I heard of everything "
					+ "the Fellowship built.@");
			}

		case "Thad" (remove):
			say("@My brother came the day the Gate fell. He did not say 'I told thee so.' He did not have to.@");
			say("She looks at her hands. @I have not answered his letter.@");

		case "bye":
			say("@Mind the crates.@*");
			break;
	}
	UI_remove_npc_face(keeper);
	checkActOne();
}

// A former member in Britain, before his or her own conversation (Q01):
// true when the player asks for that conversation ("other matters").
var formerMemberTalk (var npc, var met) {
	npc->show_npc_face(0);
	gflags[MET_AFFECTED_CITIZEN] = true;
	var first = !gflags[met];
	gflags[met] = true;

	if (npc == CANDICE) {
		if (first) {
			say("Candice does not curtsy this time.");
			say("@Avatar. They took the Fellowship's books from the museum's reading room yesterday. "
				+ "I suppose thou art pleased.@");
		} else {
			say("@Yes, Avatar?@ Candice says, a little coolly.");
		}
		add(["Fellowship", "Batlin", "decree"]);
	} else if (npc == GAYE) {
		if (first) {
			say("Gaye is unpicking the Triad from a stack of blue robes.");
			say("@Waste not, Avatar. Good cloth is good cloth, whatever was stitched on it.@");
		} else {
			say("Gaye looks up from her unpicking.");
		}
		add(["robes", "Fellowship"]);
	} else {
		if (first) {
			say("Patterson straightens his collar and does not quite meet thine eye.");
			say("@Avatar! A great victory, a great victory. I said so to the council.@");
		} else {
			say("@Avatar! Always a pleasure.@");
		}
		add(["Fellowship"]);
	}
	add(["other matters", "bye"]);

	converse (0) {
		case "Fellowship" (remove):
			if (npc == CANDICE) {
				say("@Thou thinkest we were fools. Perhaps we were. But I found friends there, and a purpose, "
					+ "and I never once heard a voice telling me to harm anyone.@");
				say("@Batlin was kind to me. I cannot make the Batlin I knew into the one they curse in the squares.@");
			} else if (npc == GAYE) {
				say("@I went every night at nine. I liked the singing. I liked being someone who went somewhere at nine.@");
				say("@I do not know what I shall do with my evenings now.@");
			} else {
				say("@I was never really one of them, thou knowest. A mayor must attend things.@");
				say("@Every guild in Britain had members among them. The bakers, the bards, the... the mayor. "
					+ "One cannot govern a city by refusing to go where the city goes.@");
				if (canSpeak(IOLO)) {
					IOLO.say("Iolo leans close to you. @He hath found his conscience remarkably quickly.@");
					IOLO.hide();
				}
			}

		case "Batlin" (remove):
			say("@He spoke once of the Fellowship going beyond Britannia. 'There are lands that have never "
				+ "heard the Triad,' he said. I thought he meant Jhelom.@");

		case "decree" (remove):
			say("@Lord British says no one shall be punished for having belonged. Then why do the children "
				+ "follow me down the street, chanting 'Strive for Unity' in that voice?@");

		case "robes" (remove):
			say("@Fellowship robes. Half of Britain wore them to the meetings. Now nobody will be seen in them, "
				+ "and they bring them to me to make into something else.@");
			say("@Shirts, mostly. I made a christening gown last week.@");

		case "other matters":
			UI_clear_answers();
			UI_remove_npc_face(npc);
			return true;

		case "bye":
			say("@Good day, Avatar.@*");
			break;
	}
	return false;
}

void Millie object#(0x43F) () {
	if (event == DOUBLECLICK && gflags[BRIDGE_STARTED] && clerk() == MILLIE) {
		clerkTalk(MILLIE);
		return;
	}
	Millie.original();
}

void Candice object#(0x429) () {
	if (event == DOUBLECLICK && gflags[BRIDGE_STARTED]) {
		if (clerk() == CANDICE) {
			clerkTalk(CANDICE);
			return;
		}
		if (!formerMemberTalk(CANDICE, SEEN_CANDICE)) {
			return;
		}
	}
	Candice.original();
}

void Gaye object#(0x435) () {
	if (event == DOUBLECLICK && gflags[BRIDGE_STARTED]) {
		if (clerk() == GAYE) {
			clerkTalk(GAYE);
			return;
		}
		if (!formerMemberTalk(GAYE, SEEN_GAYE)) {
			return;
		}
	}
	Gaye.original();
}

void Patterson object#(0x42B) () {
	if (event == DOUBLECLICK && gflags[BRIDGE_STARTED]) {
		if (!formerMemberTalk(PATTERSON, SEEN_PATTERSON)) {
			return;
		}
	}
	Patterson.original();
}

// Batlin's private accounts: the stone in two places at once.
void showLedger (var book) {
	UI_book_mode(book);
	say("Accounts of the Britain hall~kept by mine own hand~~B.*"
		+ "3rd month, 9th day~To the Britannian Mining Company at Minoc, for one black stone, "
		+ "eleven hundredweight, cut and dressed as instructed. 2,000 gold.~~"
		+ "3rd month, 14th day~To the Paws shelter, the month's bread. 150 gold.*"
		+ "3rd month, 20th day~The same stone set up at the Meditation Retreat, a monument to the Triad. "
		+ "Carriage by sea from Minoc, 300 gold.~~"
		+ "3rd month, 26th day~Candles and oil for the hall. 12 gold.*"
		+ "4th month, 2nd day~To the master of a ship at Buccaneer's Den: the carriage of one heavy stone, "
		+ "eleven hundredweight, from Minoc, and passage for the scholars. Half now, 900 gold. "
		+ "The rest on landing.~~"
		+ "4th month, 5th day~To Elizabeth and Abraham, for the road. 500 gold.");
}

// The passage list, with Gwenno's name on it.
void showPassengers (var scroll) {
	UI_book_mode(scroll);
	say("Passage for the scholars~~To sail from Buccaneer's Den with the stone, when the master gives the word.~~"
		+ "Brother Leon, to preach~Palos~Brunt~Deadeye~~"
		+ "Gwenno of Britain, scholar. Paid her own passage, in full, and would take no Fellowship coin.~~B.");
}

// Iolo finds Gwenno's name (the design's 9.2).
void ioloSeesGwenno () {
	if (!canSpeak(IOLO)) {
		return;
	}
	IOLO.say("Iolo reads the list over your shoulder. At the last name he stops.");
	say("@Gwenno has sailed farther than I ever dared, Avatar. I would not begrudge her the journey.@");
	say("@Yet to find her name in Batlin's accounts... there is a difference between trusting her "
		+ "and trusting him.@*");
	IOLO.hide();
}
