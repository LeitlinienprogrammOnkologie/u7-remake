/*
 *	The remake's own additions to Black Gate, as an Exult patch:
 *		scripts/ucc/bin/ucc.exe -I godot/data/bg/usecode -o godot/data/bg/patch/usecode godot/data/bg/usecode/additions.uc
 *	Read under any mod's patch (U7Paths.BasePatchDir). Function numbers from
 *	0xC00 are the additions'; a mod's start at 0xD00.
 */

#game "blackgate"
#autonumber 0xC00

#include "keyring.uc"
