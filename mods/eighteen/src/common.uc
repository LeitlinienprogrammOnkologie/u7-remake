/*
 *	Helpers shared by the bridge's scenes.
 */

// The first of the NPCs who is still alive, or 0: a role's speaker and its
// stand-ins (BRIDGE.md, risk 2: the player may have killed anyone).
var firstAlive (var npcs) {
	for (npc in npcs) {
		if (!UI_is_dead(npc)) {
			return npc;
		}
	}
	return 0;
}

// Names as a speaker lists them: "Brita, Merrick and Alina".
var listNames (var names) {
	var count = UI_get_array_size(names);
	var text = "";
	var n = 1;
	for (name in names) {
		if (n > 1) {
			if (n == count) {
				text = text + " and ";
			} else {
				text = text + ", ";
			}
		}
		text = text + name;
		n = n + 1;
	}
	return text;
}

// Whether the NPC travels with the avatar.
var inParty (var npc) {
	return npc->get_npc_object() in UI_get_party_list();
}

// A card of text on a scroll, between scenes: the time that has passed.
void bridgeCard (var text) {
	var scroll = UI_create_new_object(SHAPE_SCROLL);
	UI_book_mode(scroll);
	say(text);
}

// The party rested: healed, fed and cured, after weeks at home.
void restParty () {
	var party = UI_get_party_list();
	for (npc in party) {
		npc->set_npc_prop(HEALTH, npc->get_npc_prop(STRENGTH) - npc->get_npc_prop(HEALTH));
		npc->set_npc_prop(FOODLEVEL, 24 - npc->get_npc_prop(FOODLEVEL));
		npc->clear_item_flag(POISONED);
		npc->clear_item_flag(PARALYZED);
		npc->clear_item_flag(ASLEEP);
		npc->clear_item_flag(CHARMED);
		npc->clear_item_flag(CURSED);
	}
}

// The clock on to the given hour, that many days later.
void passDays (var days, var hour) {
	var span = days * 24 + hour - UI_game_hour();
	UI_advance_time(span * 1500 - UI_game_minute() * 25);
}

// A new thing for the avatar, or at the avatar's feet if too heavy.
void giveAvatar (var shape, var frame, var quality) {
	var obj = UI_create_new_object(shape);
	obj->set_item_frame(frame);
	obj->set_item_quality(quality);
	obj->set_item_flag(OKAY_TO_TAKE);
	if (!AVATAR->give_last_created()) {
		UI_update_last_created(AVATAR->get_object_position());
	}
}

// Whether a companion travels with the avatar and is near enough to speak up.
var canSpeak (var npc) {
	return inParty(npc) && isNearby(npc);
}
