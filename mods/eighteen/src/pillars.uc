/*
 *	Q06 A Map Kept in Trust (Lord British gives Gwenno's copy of the chart)
 *	and Q07 Two Moons and a Dawn: Rudyom's guess about the stone (Cove), the
 *	Lycaeum's sea tales and a surgeon's dated journal (Nelson), an old
 *	navigator's notes (Brion, optional). The player names the window (the
 *	design's 6.1); a wrong guess is corrected, and Brion dates the next such
 *	dawn: the eighteenth month. A credible hypothesis, never a law.
 */

// Lord British keeps his promise to Gwenno (her letter, Black Gate's scroll
// of quality 36: "until thou hast completed thy explorations").
void lordBritishGivesMap () {
	gflags[GOT_GWENNOS_MAP] = true;
	say("@Then there is something I must give thee. I gave Gwenno my word I would keep it until thine adventures "
		+ "were done. I think they are done, Iolo, or as done as they will ever be.@");
	say("He takes a folded chart from inside his robe, where it has plainly been for some time.");
	giveAvatar(SHAPE_SCROLL, 0, CHART_QUALITY);
	if (canSpeak(IOLO)) {
		IOLO.say("Iolo takes it as if it were made of glass. @Her hand. That is her hand.@");
		say("@She left it with thee, my liege, and not with me. I know why: I would have dropped everything and followed, "
			+ "and she would not take me from a friend who needed me. She was right. She usually is.@");
		say("He folds it again, carefully. @I am grateful. I am also more frightened than I have been since the Gate.@*");
		IOLO.hide();
		LORD_BRITISH.say("@She chose her road, Iolo, as thou hast chosen thine. Go and meet her on it.@");
	}
}

void showChart (var scroll) {
	UI_book_mode(scroll);
	say("A chart, copied in a neat, quick hand from an older one stained with tar.~~"
		+ "The coasts of Britannia, then open sea west and south, past where any map in the castle goes. "
		+ "In the open sea, a ring, and beside it in the old pirate's words: 'where the serpents stand.'*"
		+ "Beneath, in the copyist's own hand:~~"
		+ "Iolo -~The ring is a place, not a promise. Do not wait for a promise.~G.");
}

void showTales (var book) {
	UI_book_mode(book);
	say("Sea Tales of the Western Ocean~gathered at the Lycaeum~*"
		+ "A fisherman of Jhelom: 'The pillars rise only when both moons stand above the sea together. "
		+ "My grandfather saw it, and he was sober that night, near enough.'*"
		+ "A sailor of the Den: 'They come up with the sun. Not full day, mind, just the red line of it on the water. "
		+ "By full light they are gone.'*"
		+ "A shipwright of Minoc, from his father: 'Only in the dead of winter. A summer sailor never sees them, "
		+ "and never believes those who have.'*"
		+ "From the journal of Pellam, ship's surgeon, bound in at the back:~"
		+ "12th month, 2nd day. Dawn. Both moons yet up. Pillars to the west, far off.~"
		+ "1st month, 30th day, some years after. Dawn again, the moons high. The same.~"
		+ "12th month, 19th day, later still. Dawn, moons up. Seen from the masthead only.");
}

void showNotes (var scroll) {
	UI_book_mode(scroll);
	say("An old navigator's notes, in the margin of a star table:~~"
		+ "Trammel and Felucca both above the sea, and the sun just showing. The old ones called it "
		+ "'the serpents' hour', and it comes only in the dark months. Seen it once. Never again.");
}

// Rudyom: what the stone might have been for (the design's 9.3).
var rudyomTalk () {
	RUDYOM->show_npc_face(0);
	say("@Hello again, Avatar!@ Rudyom says, beaming. @Magic works! Did I tell thee? It all works again.@");
	add(["other matters", "bye"]);
	if (gflags[READ_LEDGER]) {
		add("black stone");
	}

	converse (0) {
		case "black stone" (remove):
			gflags[HEARD_RUDYOM] = true;
			say("You tell him of the block of blackrock Batlin bought, dressed, and carried away by ship.");
			say("Rudyom stops beaming. @Blackrock remembers, Avatar. Strike it with a spell and it hums for a week. "
				+ "A great block of it set down on a far shore... a sorcerer might aim for it as a sailor aims for a lighthouse.@");
			say("@A stone need not be a gate to mark the place at which a spell shall end. But if that stone were the mark, "
				+ "and something came to it by such a spell, I should not like to be standing beside it. "
				+ "Even blackrock might not bear it.@");
			say("He wags a finger. @A conjecture, mind thee, not a conclusion.@");
			if (canSpeak(SHAMINO)) {
				SHAMINO.say("@'Kal Ort Por', at the Gate's foot, and he was gone. I have wondered where a man aims such a spell.@");
				SHAMINO.hide();
			}

		case "other matters":
			UI_clear_answers();
			UI_remove_npc_face(RUDYOM);
			return true;

		case "bye":
			say("@Goodbye, Avatar.@*");
			break;
	}
	return false;
}

// Nelson: the Lycaeum's tales, and the window named.
var nelsonTalk () {
	static var seen;
	NELSON->show_npc_face(0);
	if (!seen) {
		seen = true;
		say("Nelson looks up from a heap of manuscripts. @The Avatar! Thou wilt forgive the mess; we are cataloguing "
			+ "everything the Fellowship ever gave this library. It is mostly very bad poetry.@");
	} else {
		say("@Avatar! Back among the poetry?@");
	}
	add(["Serpent Pillars", "other matters", "bye"]);
	if (gflags[READ_TALES] && !gflags[WINDOW_FOUND]) {
		add("the window");
	}

	converse (0) {
		case "Serpent Pillars" (remove):
			say("@Pillars in the sea? There are tales. Sailors' tales, which is to say a great deal of rum and a little truth.@");
			say("@I had them gathered into one book years ago, for my own amusement, and a surgeon's journal bound in at the back. "
				+ "Read it, and tell me what thou makest of it. They do not agree.@");
			if (!gflags[READ_TALES]) {
				giveAvatar(SHAPE_BOOK, 9, TALES_QUALITY);
			}

		case "the window":
			say("@Well? If thou wert to sail for these pillars, when wouldst thou wish to be there?@");
			var answer = askForResponse(["both moons up", "at dawn", "in winter", "a winter dawn, both moons up"]);
			if (answer == "both moons up") {
				say("@The Jhelom man says so. But the surgeon writes 'dawn' every time, and both moons stand over the sea "
					+ "on many a summer night when no one sees anything at all.@");
			} else if (answer == "at dawn") {
				say("@The Den's sailor says so. But there are three hundred dawns in a year, and the surgeon's dates "
					+ "are all in the dark months.@");
			} else if (answer == "in winter") {
				say("@The shipwright says so. But a winter is long, and the surgeon never once writes of an evening.@");
			} else {
				gflags[WINDOW_FOUND] = true;
				say("@A winter dawn, with both moons still above the sea.@ He nods slowly. @Aye. That fits every tale, "
					+ "and every date in the journal. Each of them saw a piece of the same thing.@");
				say("@A hypothesis, mind thee, not a law. But a good one. Brion can tell thee when next the heavens oblige; "
					+ "he lives at the observatory and talks to nothing else.@");
				UI_remove_answer("the window");
			}

		case "other matters":
			UI_clear_answers();
			UI_remove_npc_face(NELSON);
			return true;

		case "bye":
			say("@Mind the poetry on thy way out.@*");
			break;
	}
	return false;
}

// Brion: the navigator's notes (optional), and the next such dawn, given the chart.
var brionTalk () {
	BRION->show_npc_face(0);
	say("@Salutations!@ Brion smiles. @The Alignment came and went, Avatar. The heavens have been very dull since.@");
	add(["Serpent Pillars", "other matters", "bye"]);
	if (gflags[WINDOW_FOUND] && !gflags[DAWN_DATED]) {
		add("the dawn");
	}

	converse (0) {
		case "Serpent Pillars" (remove):
			say("@Pillars? I know nothing of pillars. But I know a navigator's star table when I see one.@");
			if (!gflags[GOT_NAVIGATOR_NOTES]) {
				gflags[GOT_NAVIGATOR_NOTES] = true;
				say("@There is one in my brother's library with notes in the margin about 'the serpents' hour'. "
					+ "I copied them out once. Here, I have no use for them.@");
				giveAvatar(SHAPE_SCROLL, 0, NOTES_QUALITY);
			}

		case "the dawn":
			say("@A winter sunrise with both moons above the sea? A pretty thing to compute.@");
			if (!gflags[GOT_GWENNOS_MAP]) {
				say("@But where? The heavens differ with the sea one stands upon. Bring me a chart of the place, "
					+ "and I shall tell thee when.@");
			} else {
				gflags[DAWN_DATED] = true;
				say("You show him the ring on Gwenno's chart. He measures, mutters, and covers a slate with figures.");
				say("@Not this winter. The moons are wrong. The one after.@ He taps the slate. @By thy reckoning, "
					+ "the eighteenth month since the Gate. And even then, Avatar, only if thy sailors' tales are true.@");
				UI_remove_answer("the dawn");
			}

		case "other matters":
			UI_clear_answers();
			UI_remove_npc_face(BRION);
			return true;

		case "bye":
			say("@Thou mayest use mine observatory as often as thou wishest.@*");
			break;
	}
	return false;
}

void Rudyom object#(0x44A) () {
	if (event == DOUBLECLICK && gflags[ACT_TWO]) {
		var talked = rudyomTalk();
		UI_remove_npc_face(RUDYOM);
		checkActTwo();
		if (!talked) {
			return;
		}
	}
	Rudyom.original();
}

void Brion object#(0x4F8) () {
	if (event == DOUBLECLICK && gflags[ACT_TWO]) {
		var talked = brionTalk();
		UI_remove_npc_face(BRION);
		checkActTwo();
		if (!talked) {
			return;
		}
	}
	Brion.original();
}

void Nelson object#(0x4F9) () {
	if (event == DOUBLECLICK && gflags[ACT_TWO]) {
		if (!nelsonTalk()) {
			return;
		}
	}
	Nelson.original();
}
