/*
 *	The keyring: Serpent Isle's (Exult keyring.cc: the keys' qualities, kept
 *	on the ring) in Black Gate, after Exult's BG Keyring mod (content/bgkeyring,
 *	GPL; Marzo Junior and the Exult team): a key used on the keyring goes onto
 *	it, the keyring used on the avatar takes all the party's keys, and used on
 *	a lock whose key it holds it works as that key. Inn keys and Alagner's
 *	are refused, as they must go back. Black Gate's own key code does the
 *	locking: a key's target is handed on to it (set_intercept_item), the
 *	keyring lends it a key of the quality it holds.
 *
 *	Lord British gives it with the castle keys: the first time he is spoken
 *	to, the storeroom key in his study's nightstand (he says "Thou wilt find
 *	the key in my study") is a keyring holding it; taken already, he hands
 *	the keyring over.
 */

const int DOUBLECLICK = 1;
const int SHAPE_KEY = 641;
const int SHAPE_KEYRING = 1100;		// shape_additions.txt
const int SHAPE_NIGHTSTAND = 406;
const int KEY_INN = 255;
const int KEY_ALAGNER = 254;
const int KEY_STOREROOM = 236;		// Lord British's storeroom door (890, 1132, 5)
const int QUALITY_ANY = -359;
const int FRAME_ANY = -359;

extern void randomPartyBark 0x08FE (var line);
extern void flashBlocked 0x08FD (var cursor);

// The qualities of the keys on the keyring (there is one).
static var keyring_keys;

var keyringHas (var quality) {
	if (!keyring_keys) {
		return false;
	}
	return quality in keyring_keys;
}

// A key's quality onto the keyring, which shows how many it holds (frames 0-4).
void keyringAdd (var keyring, var quality) {
	if (!keyringHas(quality)) {
		if (!keyring_keys) {
			keyring_keys = [quality];
		} else {
			keyring_keys << quality;
		}
	}
	var count = UI_get_array_size(keyring_keys);
	if (count > 4) {
		count = 4;
	}
	keyring->set_item_frame(count);
}

// Whether a key may go onto the keyring: not the ones to be given back.
var keyringTakes (var quality) {
	if (quality == KEY_INN) {
		UI_get_avatar_ref()->item_say("@I don't think the innkeeper would like that...@");
		return false;
	}
	if (quality == KEY_ALAGNER) {
		UI_get_avatar_ref()->item_say("@I am supposed to return this key to Alagner.@");
		return false;
	}
	return true;
}

// The keyring used on the avatar: all the party's keys go onto it.
void keyringAddPartyKeys (var keyring) {
	var added = 0;
	var party = UI_get_party_list();
	for (npc in party) {
		var keys = npc->get_cont_items(SHAPE_KEY, QUALITY_ANY, FRAME_ANY);
		for (key in keys) {
			var quality = key->get_item_quality();
			if (quality != KEY_INN && quality != KEY_ALAGNER) {
				keyringAdd(keyring, quality);
				key->remove_item();
				added = added + 1;
			}
		}
	}
	randomPartyBark("@Added " + added + " keys.@");
}

// Black Gate's key: used on the keyring it goes onto it; on anything else
// its own code runs, on the same target (no second click).
void Key shape#(0x281) () {
	if (event == DOUBLECLICK) {
		var target = UI_click_on_item();
		if (!target) {
			return;
		}
		if (target->get_item_shape() == SHAPE_KEYRING) {
			var quality = get_item_quality();
			if (keyringTakes(quality)) {
				keyringAdd(target, quality);
				remove_item();
			}
			return;
		}
		UI_set_intercept_item(target);
	}
	Key.original();
}

void Keyring shape#(1100) () {
	if (event != DOUBLECLICK) {
		return;
	}
	var target = UI_click_on_item();
	if (!target) {
		return;
	}
	var obj = target[1];
	if (obj->is_npc()) {
		if (obj->get_npc_object() == UI_get_avatar_ref()) {
			keyringAddPartyKeys(item);
		}
		return;
	}
	var quality = obj->get_item_quality();
	if (!keyringHas(quality)) {
		flashBlocked(0);
		randomPartyBark("@It's not on the keyring.@");
		return;
	}
	// A key of that quality, lent for Black Gate's own key code.
	var key = UI_create_new_object(SHAPE_KEY);
	key->set_item_quality(quality);
	key->set_item_frame(quality % 32);
	if (!UI_get_avatar_ref()->give_last_created()) {
		UI_update_last_created(UI_get_avatar_ref()->get_object_position());
	}
	UI_set_intercept_item(target);
	key->Key();
	key->remove_item();
}

// The storeroom key in Lord British's nightstand becomes a keyring holding
// it; already taken, he gives the keyring.
void keyringToStudy () {
	var stands = UI_find_nearby([897, 1170, 0], SHAPE_NIGHTSTAND, 2, 0);
	for (stand in stands) {
		var keys = stand->get_cont_items(SHAPE_KEY, KEY_STOREROOM, FRAME_ANY);
		for (key in keys) {
			key->remove_item();
			var ring = UI_create_new_object(SHAPE_KEYRING);
			stand->give_last_created();
			keyringAdd(ring, KEY_STOREROOM);
			return;
		}
	}
	var given = UI_create_new_object(SHAPE_KEYRING);
	given->set_item_frame(0);
	if (!UI_get_avatar_ref()->give_last_created()) {
		UI_update_last_created(UI_get_avatar_ref()->get_object_position());
	}
	item->item_say("@Take this ring for thy keys.@");
}

void LordBritish object#(0x417) () {
	static var keyring_given;
	if (event == DOUBLECLICK && !keyring_given) {
		keyring_given = true;
		keyringToStudy();
	}
	LordBritish.original();
}
