/*
 *	Buccaneer's Den: Q05 The Old Pirate. Mole sold Gwenno the chart, but
 *	will say the rest only with his log, which went with his sea chest aboard
 *	the old Hag, which Blacktooth now claims by a bill of sale Mole never
 *	signed. Three ways to the log (the design's 6.2): expose Lucky's forgery
 *	(investigation), settle the old share (diplomacy), or take it from
 *	Blacktooth (a short fight). Any of them gives the same clue.
 */

const int SHARE_GOLD = 50;

// Without Blacktooth, Mole kept his own log.
void giveLogToMole () {
	var log = UI_create_new_object(SHAPE_BOOK);
	log->set_item_frame(5);
	log->set_item_quality(LOG_QUALITY);
	log->set_item_flag(OKAY_TO_TAKE);
	MOLE->give_last_created();
}

// Mole's log, in Blacktooth's keeping.
void denAtActTwo () {
	if (UI_is_dead(BLACKTOOTH)) {
		// No one left to keep it: it lies in Mole's sea chest on the Den's pier.
		giveLogToMole();
		return;
	}
	var log = UI_create_new_object(SHAPE_BOOK);
	log->set_item_frame(5);
	log->set_item_quality(LOG_QUALITY);
	log->set_item_flag(OKAY_TO_TAKE);
	BLACKTOOTH->give_last_created();
}

// Blacktooth hands the log over (forgery exposed, share settled).
void blacktoothGivesLog () {
	var logs = BLACKTOOTH->get_cont_items(SHAPE_BOOK, LOG_QUALITY, FRAME_ANY);
	for (log in logs) {
		log->remove_item();
	}
	giveAvatar(SHAPE_BOOK, 5, LOG_QUALITY);
}

void showBill (var scroll) {
	UI_book_mode(scroll);
	say("Bill of Sale~~I, Mole, of Buccaneer's Den, do sell to Blacktooth the ship Hag, and all aboard her, "
		+ "for thirty gold, paid.~~Signed in mine own hand,~Mole~~Witnessed,~Lucky~~"
		+ "The 9th day of the 2nd month");
}

void showLog (var book) {
	UI_book_mode(book);
	say("The Log of Mole~kept for him by whoever aboard could write~*"
		+ "Year of the long calm. Took a chart off the drowned Gannet, Blacktooth and me. "
		+ "Open sea west and south of every chart, and a ring drawn in it, and the dead man's words "
		+ "beside: 'where the serpents stand.' Blacktooth says 'tis a sailor's joke.*"
		+ "The Gannet's mate, dying, said they lay a month at the ring and saw nothing but sea. "
		+ "Then one dawn they saw the pillars, far off, and the storm took them before they could steer for it. "
		+ "The chart says where. It never said when.*"
		+ "Sold the chart to the scholar Gwenno of Britain, one hundred gold. She copied it first, "
		+ "by my lamp, the whole night through, and would not say why she wanted it.*"
		+ "4th month, 9th day. The big ship cleared the Den at dawn, heavy by the stern, a stone in her hold. "
		+ "Scholars aboard, and the woman Gwenno, and a fat man in a fine cloak the crew called Brother. "
		+ "West-sou'west. The chart's own course.");
}

// Mole: the chart, and with the log, the rest (the chart gives an area, not a time).
var moleTalk () {
	MOLE->show_npc_face(0);
	if (!gflags[SEEN_MOLE]) {
		gflags[SEEN_MOLE] = true;
		say("Mole is nursing an ale. His Fellowship medallion is gone; a pale mark on his shirt shows where it hung.");
		say("@The Avatar! I suppose thou hast come to tell me what a fool I was. Get in line, mate.@");
	} else {
		say("@What is it?@ Mole asks.");
	}
	add(["Gwenno", "Fellowship", "other matters", "bye"]);
	if (gflags[DEN_HEARD_MOLE]) {
		add(["Blacktooth", "signature"]);
	}
	if (gflags[DEN_LOG_READ] && !gflags[DEN_DONE]) {
		add("the log");
	}
	if (gflags[DEN_HEARD_SHARE] && !gflags[DEN_MOLE_PAYS] && !gflags[DEN_LOG_READ]) {
		add("the share");
	}

	converse (0) {
		case "Gwenno" (remove):
			say("@Aye, I sold a chart to a woman by that name. Brave lass, sharp as a gutting knife.@");
			var kept = MOLE->get_cont_items(SHAPE_BOOK, LOG_QUALITY, FRAME_ANY);
			if (gflags[DEN_LOG_READ]) {
				say("@Thou hast my log, so thou knowest the rest.@");
				add("the log");
			} else if (kept) {
				say("@It is all in my log. Here, read it thyself, and then we shall talk.@");
				for (log in kept) {
					log->remove_item();
				}
				giveAvatar(SHAPE_BOOK, 5, LOG_QUALITY);
			} else {
				gflags[DEN_HEARD_MOLE] = true;
				say("@But I'll tell thee no more without my log. At my age a man's memory is a leaky bucket, "
					+ "and the log is where I kept the truth.@");
				say("@It is in my sea chest, aboard the old Hag, and Blacktooth says the Hag is his now. "
					+ "He has a paper, he says, signed by me.@");
				add(["Blacktooth", "signature"]);
			}

		case "Fellowship" (remove):
			say("@They told me I was worthy. Nobody ever told me that before, not even my mother, and she named me after a cave.@");
			say("@I gave them every coin I had. I would give them a few more to have that feeling back. That is the sad part.@");

		case "Blacktooth" (remove):
			say("@We were the same link on a chain once. Then I joined the Fellowship and preached at him till he could not "
				+ "stand the sight of me. Now he has my ship and my log, and he will not speak to me.@");
			say("@He says I owe him a share of what the woman paid. Mayhap I do. We took that chart together.@");

		case "the share" (remove):
			gflags[DEN_MOLE_PAYS] = true;
			say("@Fifty gold? Half of what she paid.@ Mole scratches his beard. @He is right, curse him. I kept it all "
				+ "and gave most of it to the Fellowship, and that was his share I gave away.@");
			say("@Tell him he shall have it at the Fallen Virgin tonight. And I shall buy the ale.@");

		case "signature" (remove):
			gflags[DEN_KNOWS_MARK] = true;
			say("@Signed by me? I cannot write my own name, mate. Never could. I make my mark, like an honest pirate.@");
			say("He draws a crooked anchor in spilt ale on the table.");

		case "the log" (remove):
			gflags[DEN_DONE] = true;
			say("@My log! Thou hast it.@ He turns the pages with great care, as if they might bite.");
			say("@There. The Gannet's chart. The ring is a place, mate, not a door. The mate swore they lay at it a month "
				+ "and saw nothing, and then one dawn there they were, the pillars, far off. That is all anyone knows.@");
			say("@I told thy Gwenno the same. She said a place was enough to start with. Then she sat up all night "
				+ "copying my chart, and in the morning she sailed with that fat preacher's ship.@");
			if (canSpeak(IOLO)) {
				IOLO.say("@She would. She always said a place was enough to start with.@");
				IOLO.hide();
				MOLE.say("@Thou art her man? Then thou art a lucky one, minstrel.@");
			}
			if (gflags[DEN_FOUGHT] && UI_is_dead(BLACKTOOTH)) {
				say("He closes the log. @Blackie was a fool, but he was my fool. I hope it was worth it.@");
			} else if (gflags[DEN_FOUGHT]) {
				say("He closes the log. @Thou didst not have to break his head for it. But I thank thee.@");
			} else if (gflags[DEN_SHARE_SETTLED]) {
				say("@And Blackie sent it back, of his own will? Mayhap I shall buy him an ale.@");
			} else {
				say("@Lucky made that paper? I should have known. Blackie never could spell 'Hag'.@");
			}
			say("@Keep the log a while. Bring it back when thou hast found her.@");

		case "other matters":
			UI_clear_answers();
			UI_remove_npc_face(MOLE);
			return true;

		case "bye":
			say("@Goodbye, mate.@*");
			break;
	}
	return false;
}

// Blacktooth: the bill of sale, the share, or a fight.
var blacktoothTalk () {
	static var bill_given;
	BLACKTOOTH->show_npc_face(0);
	if (!gflags[SEEN_BLACKTOOTH]) {
		gflags[SEEN_BLACKTOOTH] = true;
		say("Blacktooth has a book under his arm that is clearly not his. He holds it the way a child holds a stolen pie.");
	}
	say("@What dost thou want?@ Blacktooth asks in a threatening voice.");
	add(["the Hag", "other matters", "bye"]);
	var holds = BLACKTOOTH->get_cont_items(SHAPE_BOOK, LOG_QUALITY, FRAME_ANY);
	if (gflags[DEN_HEARD_MOLE] && holds) {
		add(["Mole's log", "share"]);
		if (gflags[DEN_MOLE_PAYS]) {
			add("Mole will pay");
		}
		if (gflags[DEN_LUCKY_CONFESSED]) {
			add("forgery");
		}
	}

	converse (0) {
		case "the Hag" (remove):
			say("@The Hag is mine, and all aboard her. Mole sold her to me fair and square. I have the bill of sale.@");
			say("He thrusts a paper at you. @Read it thyself, if thou canst read.@");
			if (!bill_given) {
				bill_given = true;
				giveAvatar(SHAPE_SCROLL, 0, BILL_QUALITY);
			}

		case "Mole's log" (remove):
			say("@It was in the chest. The chest was aboard. All aboard is mine. 'Tis on the paper.@");
			say("@Thou wantest it? Then come and take it, if thou darest.@");
			add("take it");

		case "take it" (remove):
			say("@Ha! Come on, then!@");
			gflags[DEN_FOUGHT] = true;
			UI_remove_npc_face(BLACKTOOTH);
			BLACKTOOTH->set_alignment(EVIL);
			BLACKTOOTH->set_schedule_type(IN_COMBAT);
			return false;

		case "share" (remove):
			gflags[DEN_HEARD_SHARE] = true;
			say("@We took that chart off the Gannet together, Mole and me. He sold it and kept every coin, "
				+ "then went off singing Triads at me. Half is mine. Fifty gold, by my reckoning.@");
			say("@Pay me my share and he can have his scribbling back. I cannot read it anyway.@");
			add("pay the share");
			if (gflags[DEN_MOLE_PAYS]) {
				add("Mole will pay");
			}

		case "Mole will pay" (remove):
			gflags[DEN_SHARE_SETTLED] = true;
			say("@He said that? Mole? Pay me?@ Blacktooth looks as if he may cry.");
			say("@Then we are square, and I can drink his ale with a clear heart. Here, take his book back to him.@ "
				+ "He hesitates. @Tell him he can have the Hag, too. I never could sail her alone.@");
			blacktoothGivesLog();
			UI_remove_answer(["Mole's log", "take it", "share", "pay the share"]);

		case "pay the share" (remove):
			if (UI_count_objects(PARTY, SHAPE_GOLD, QUALITY_ANY, FRAME_ANY) < SHARE_GOLD) {
				say("@Thou hast not fifty gold! Do not waste my time.@");
				add("pay the share");
			} else {
				UI_remove_party_items(SHARE_GOLD, SHAPE_GOLD, QUALITY_ANY, FRAME_ANY, true);
				gflags[DEN_SHARE_SETTLED] = true;
				say("He counts the coins twice, bites one, and grins his black grin.");
				say("@Square, then. Here, take his book.@ He hesitates. @Tell him... tell him he can have the Hag back, too. "
					+ "I never could sail her alone.@");
				blacktoothGivesLog();
				UI_remove_answer(["Mole's log", "take it"]);
			}

		case "forgery" (remove):
			say("You tell him Mole cannot write, and that Lucky has owned to making the paper.");
			say("Blacktooth's face goes red, then white. @Lucky made it? I paid him ten gold for that! "
				+ "He said Mole had signed it at the House of Games!@");
			say("@...So it is not mine. None of it.@ He holds out the book. @Take it. And do not tell Mole I was fooled. "
				+ "I could not bear the look on him.@");
			blacktoothGivesLog();
			UI_remove_answer(["Mole's log", "take it", "share", "pay the share"]);

		case "other matters":
			UI_clear_answers();
			UI_remove_npc_face(BLACKTOOTH);
			return true;

		case "bye":
			say("@Yeah, goodbye!@*");
			break;
	}
	return false;
}

// Lucky: the bill's forger, once its faults are put to him.
var luckyTalk () {
	LUCKY->show_npc_face(0);
	say("@What dost thou want with Lucky?@ the pirate asks.");
	add(["other matters", "bye"]);
	if (gflags[DEN_SAW_BILL]) {
		add("bill of sale");
	}

	converse (0) {
		case "bill of sale" (remove):
			say("@A fine hand, is it not? Mole is a better penman than anyone knew.@");
			var answer = askForResponse(["Mole cannot write", "thy name is on it", "a fine hand"]);
			if (answer == "Mole cannot write" && gflags[DEN_KNOWS_MARK]) {
				gflags[DEN_LUCKY_CONFESSED] = true;
				say("Lucky's smile stays where it is, but his eyes go somewhere else.");
				say("@Ah. Well. A man must eat. Blacktooth wanted the Hag and a paper to say she was his, "
					+ "so I made him one and told him Mole had signed it over cards. I make papers. "
					+ "I never said it was true. I said it was a paper.@");
				say("@Go and tell him, then. He will want to hit someone, and I would rather it were not me.@");
			} else if (answer == "Mole cannot write") {
				say("@Says who? Hast thou ever seen him try?@");
				add("bill of sale");
			} else if (answer == "thy name is on it") {
				say("@As the witness. I witnessed a great many things that day.@ He winks.");
				add("bill of sale");
			} else {
				say("@I thank thee! I learned it in my youth.@ He laughs.");
				add("bill of sale");
			}

		case "other matters":
			UI_clear_answers();
			UI_remove_npc_face(LUCKY);
			return true;

		case "bye":
			say("@Be careful, my friend.@*");
			break;
	}
	return false;
}

void Blacktooth object#(0x4E2) () {
	if (event == DOUBLECLICK && gflags[ACT_TWO] && !gflags[DEN_FOUGHT]) {
		if (!blacktoothTalk()) {
			return;
		}
	}
	Blacktooth.original();
}

void Mole object#(0x4E3) () {
	if (event == DOUBLECLICK && harbourDeparture(MOLE)) {
		return;
	}
	if (event == DOUBLECLICK && gflags[ACT_TWO]) {
		if (!moleTalk()) {
			return;
		}
	}
	Mole.original();
}

void Lucky object#(0x4E4) () {
	if (event == DOUBLECLICK && gflags[ACT_TWO]) {
		if (!luckyTalk()) {
			return;
		}
	}
	Lucky.original();
}
