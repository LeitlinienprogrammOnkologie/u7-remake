using U7.Actors;
using U7.Core;
using U7.Data;

namespace U7.Usecode;

/// <summary>
/// Exult's intrinsics past the originals' (its own, in Black Gate's table for
/// mods, and Serpent Isle's under its own numbers) and the ten only Serpent
/// Isle's table has (<see cref="SiIntrinsics.SiOnly"/>), from intrinsics.cc.
/// </summary>
public sealed partial class BgIntrinsics
{
    /// <summary>Exult <c>Usecode_machine::si_path_fail</c>: the event a failed <c>si_path_run_usecode</c> runs.</summary>
    const int SiPathFailEvent = 14;

    /// <summary>The SI-only handlers, by their numbers from <see cref="SiIntrinsics.FirstSiOnly"/>.</summary>
    UsecodeValue CallSiOnly(int id, UsecodeValue[] p, int n) =>
        (id - SiIntrinsics.FirstSiOnly) switch
        {
            0 => NpcNearby2(p),
            1 => SiDisplayMap(p),
            2 => ResurrectNpc(p),
            3 => InitConversation(),
            4 => EndConversation(),
            5 => StopArresting(),
            6 => CanAvatarReachPos(p),
            7 => UsecodeValue.FromInt(_vm.Keyring.Contains((int)p[0].IntValue) ? 1 : 0),
            8 => AddToKeyring(p),
            9 => UsecodeValue.FromInt(_vm.Keyring.Remove((int)p[0].IntValue) ? 1 : 0),
            _ => Stub(id, p, n)
        };

    /// <summary>Exult <c>UI_get_music_track</c>: the track playing, -1 for none ("we do it the SI way").</summary>
    UsecodeValue GetMusicTrack() => UsecodeValue.FromInt(_vm.Music?.CurrentTrack ?? -1);

    /// <summary>Exult <c>UI_error_message</c> and <c>UI_printf</c>: the values, to the log.</summary>
    UsecodeValue ErrorMessage(UsecodeValue[] p, int n)
    {
        _vm.Log("usecode: " + string.Join(" ", p.Take(n).Select(v => v.ToString())));
        return Zero();
    }

    /// <summary>Exult <c>UI_get_attack_mode(npc)</c>.</summary>
    UsecodeValue GetAttackMode(UsecodeValue[] p) =>
        UsecodeValue.FromInt(_vm.GetItem(p[0]) is { IsActor: true } npc ? npc.AttackMode : 0);

    /// <summary>Exult <c>UI_get_body_npc(body)</c>: the NPC the corpse was, as a negative number; 0 for none.</summary>
    UsecodeValue GetBodyNpc(UsecodeValue[] p) =>
        UsecodeValue.FromInt(_vm.GetItem(p[0]) is { LiveNpcNum: > 0 and var num } ? -num : 0);

    /// <summary>Exult <c>UI_get_oppressor(npc)</c>: its oppressor's NPC number (0 the avatar), -1 for none.</summary>
    UsecodeValue GetOppressor(UsecodeValue[] p) =>
        UsecodeValue.FromInt(_vm.GetItem(p[0]) is { IsActor: true } npc ? npc.Oppressor?.NpcNum ?? -1 : 0);

    /// <summary>Exult's ready spot from usecode's number, in the game's own numbering.</summary>
    static int UsecodeSpot(int where) => GameContext.IsSi ? ReadySpot.FromSi(where) : ReadySpot.FromBg(where);

    /// <summary>Exult <c>UI_get_readied(npc, where)</c>: what is in the spot (the game's numbering), 0 for none.</summary>
    UsecodeValue GetReadied(UsecodeValue[] p)
    {
        if (_vm.GetItem(p[0]) is not { IsActor: true } npc)
        {
            return Zero();
        }

        var spot = UsecodeSpot((int)p[1].IntValue);
        return spot <= ReadySpot.Ucont ? UsecodeValue.FromObject(Equipment.GetReadied(npc, spot)) : Zero();
    }

    /// <summary>Exult <c>UI_get_weapon(npc)</c>: the shape it fights with, 0 for none.</summary>
    UsecodeValue GetWeapon(UsecodeValue[] p)
    {
        if (_vm.GetItem(p[0]) is { IsActor: true } npc && _vm.Combat?.GetWeapon(npc, out _, out var shape) is not null)
        {
            return UsecodeValue.FromInt(shape);
        }

        return Zero();
    }

    /// <summary>Exult <c>Usecode_internal::remove_cont_items(container, quantity, shape, quality, frame, flag)</c>: how many it took; -357 is the party.</summary>
    UsecodeValue RemoveContItems(UsecodeValue[] p)
    {
        if (_vm.GetItem(p[0]) is not { } cont)
        {
            return p[0].IntValue == -357 ? RemovePartyItems(p[1..]) : Zero();
        }

        var quantity = (int)p[1].IntValue;
        var (shape, qual, frame) = ((int)p[2].IntValue, (int)p[3].IntValue, (int)p[4].IntValue);
        if (quantity == U7Constants.AnyShape)
        {
            quantity = Quantities.Count(cont, shape, qual, frame);
        }

        return UsecodeValue.FromInt(quantity - Quantities.Remove(cont, quantity, shape, qual, frame));
    }

    /// <summary>Exult <c>Usecode_internal::add_cont_items(container, quantity, shape, quality, frame, temporary)</c>: how many found no room.</summary>
    UsecodeValue AddContItems(UsecodeValue[] p)
    {
        if (_vm.GetItem(p[0]) is not { } cont)
        {
            return Zero();
        }

        var quality = (int)p[3].IntValue;
        // e.g. the Knight's Test wolf meat.
        quality = quality == U7Constants.AnyShape ? 0 : quality;
        return UsecodeValue.FromInt(Quantities.Add(cont, (int)p[1].IntValue, (int)p[2].IntValue, quality, (int)p[4].IntValue,
            dontCreate: false, p[5].IntValue != 0));
    }

    /// <summary>Exult <c>UI_set_barge_dir(barge, dir)</c>.</summary>
    UsecodeValue SetBargeDir(UsecodeValue[] p)
    {
        if (_vm.GetItem(p[0]) is { IsBarge: true } obj && _vm.Barges is { } barges)
        {
            barges.Of(obj).FaceDirection((int)p[1].IntValue);
        }

        return Zero();
    }

    /// <summary>Exult <c>UI_set_opponent(npc, opponent)</c>: its target, without starting a fight.</summary>
    UsecodeValue SetOpponent(UsecodeValue[] p)
    {
        if (_vm.GetItem(p[0]) is { IsActor: true } npc && _vm.GetItem(p[1]) is { } opponent)
        {
            _vm.Combat?.SetTarget(npc, opponent, false);
        }

        return Zero();
    }

    /// <summary>
    /// Exult <c>UI_si_path_run_usecode(npc, loc, event, item, fun, always)</c>:
    /// the NPC walks to a free spot near the place and runs the function on
    /// the item; with <c>always</c>, a failed walk runs it too, with event 14.
    /// </summary>
    UsecodeValue SiPathRunUsecode(UsecodeValue[] p)
    {
        if (_vm.GetItem(p[0]) is { IsActor: true } npc)
        {
            PathRunUsecode(npc, p[1], (int)p[4].GetElem0().IntValue, _vm.GetItem(p[3]), (int)p[2].IntValue,
                findFree: true, always: p[5].IntValue != 0, companions: false);
        }

        return Zero();
    }

    /// <summary>Exult <c>UI_begin_casting_mode(npc[, frames])</c>: its next script shows the casting frames (Exult's 859 by default).</summary>
    UsecodeValue BeginCastingMode(UsecodeValue[] p, int n)
    {
        if (_vm.GetItem(p[0]) is { IsActor: true } npc)
        {
            npc.Casting = CastingMode.Init;
            npc.CastingShape = n > 1 ? (int)p[1].IntValue : Spellbook.CastingFrames;
        }

        return Zero();
    }

    /// <summary>Exult <c>UI_get_map_num(item)</c>: the remake has the one map, 0; -1 for none.</summary>
    UsecodeValue GetMapNum(UsecodeValue[] p) => UsecodeValue.FromInt(_vm.GetItem(p[0]) is null ? -1 : 0);

    /// <summary>Exult <c>UI_create_new_object2(shape, loc)</c>: a new object, a monster with its equipment, put at the place.</summary>
    UsecodeValue CreateNewObject2(UsecodeValue[] p)
    {
        var obj = CreateObject((int)p[0].IntValue, equip: true);
        UpdateLastCreated(p[1..]);
        return UsecodeValue.FromObject(obj);
    }

    /// <summary>
    /// Exult <c>UI_clear_item_say(item)</c>: its bark goes, and its scripts'
    /// barks are not said ("seems to be right": the avatar's barks after the
    /// teleport storm).
    /// </summary>
    UsecodeValue ClearItemSay(UsecodeValue[] p)
    {
        if (_vm.GetItem(p[0]) is { } item)
        {
            item.BarkUntilMsec = 0;
            foreach (var script in _vm.Scripts.Where(s => s.Obj == item))
            {
                script.KilledBarks = true;
            }
        }

        return Zero();
    }

    /// <summary>Exult <c>UI_set_polymorph(npc, shape)</c>.</summary>
    UsecodeValue SetPolymorph(UsecodeValue[] p)
    {
        if (_vm.GetItem(p[0]) is { IsActor: true } npc)
        {
            Polymorph.Set(_vm.Map, npc, _vm.Avatar, (int)p[1].IntValue);
        }

        return Zero();
    }

    /// <summary>Exult <c>UI_infravision(npc, on)</c>: for one of the party, the day palette whatever the hour.</summary>
    UsecodeValue Infravision(UsecodeValue[] p)
    {
        if (_vm.GetItem(p[0]) is { IsActor: true } npc && (npc == _vm.Avatar || _vm.Party?.IsInParty(npc) == true) &&
            _vm.Clock is { } clock)
        {
            clock.Infravision = p[1].IntValue != 0;
            _vm.Lighting?.ResetPalette();
        }

        return Zero();
    }

    /// <summary>Exult <c>UI_ambient_light(on)</c>: usecode lights the scene as the light spell does (the cutscene with Batlin and Cantra).</summary>
    UsecodeValue AmbientLight(UsecodeValue[] p)
    {
        if (_vm.Clock is { } clock)
        {
            // Exult toggle_ambient_light(parms[0] == 0), as it reads.
            clock.AmbientLight = p[0].IntValue == 0;
            _vm.Lighting?.ResetPalette();
        }

        return Zero();
    }

    /// <summary>Exult <c>UI_display_map_ex(sprite, loc)</c>: a map sprite until a click, with the location mark if asked.</summary>
    UsecodeValue DisplayMapEx(UsecodeValue[] p)
    {
        var mark = p[1].IntValue != 0 ? new TileCoord(_vm.Avatar.Tx, _vm.Avatar.Ty, _vm.Avatar.Tz) : (TileCoord?)null;
        _vm.ShowPicture(new UsecodePicture((int)p[0].IntValue, 0, mark));
        return Zero();
    }

    /// <summary>Exult <c>UI_book_mode_ex(scroll, font[, gump])</c>: a book or scroll in that font and gump.</summary>
    UsecodeValue BookModeEx(UsecodeValue[] p, int n)
    {
        var font = (int)p[1].IntValue;
        var gump = n >= 3 ? (int)p[2].IntValue : -1;
        _vm.SetBook(p[0].IntValue != 0 ? new U7.Gumps.ScrollGump(font, gump) : new U7.Gumps.BookGump(font, gump));
        return Zero();
    }

    /// <summary>
    /// Exult <c>Usecode_internal::is_dest_reachable</c>: a place at a lift of
    /// 0 or more the NPC is next to, or can find a path to within a tile.
    /// </summary>
    bool IsDestReachable(U7Object npc, UsecodeValue loc)
    {
        if (!loc.IsArray || loc.ArraySize < 2)
        {
            return false;
        }

        var dest = new TileCoord((int)loc.GetElem(0).IntValue, (int)loc.GetElem(1).IntValue,
            loc.ArraySize == 2 ? 0 : (int)loc.GetElem(2).IntValue);
        if (dest.Tz < 0)
        {
            return false;
        }

        return ObjectGeometry.Distance(npc, dest) <= 1 || PathWalk.Astar(_vm.Map, npc, dest, 1, maxBlocked: 6) is not null;
    }

    /// <summary>Exult <c>UI_is_dest_reachable(npc, loc)</c>.</summary>
    UsecodeValue IsDestReachableIntrinsic(UsecodeValue[] p) =>
        UsecodeValue.FromInt(_vm.GetItem(p[0]) is { IsActor: true } npc && IsDestReachable(npc, p[1]) ? 1 : 0);

    /// <summary>Exult <c>UI_get_item_weight(item)</c>.</summary>
    UsecodeValue GetItemWeight(UsecodeValue[] p) =>
        UsecodeValue.FromInt(_vm.GetItem(p[0]) is { } obj ? Inventory.GetWeight(obj, _vm.Catalog) : 0);

    /// <summary>Exult <c>UI_remove_all_spells(book)</c> (<c>clear_spells</c>).</summary>
    UsecodeValue RemoveAllSpells(UsecodeValue[] p)
    {
        if (_vm.GetItem(p[0]) is { } book && _vm.Catalog[book.Shape].IsSpellbookClass)
        {
            book.SpellCircles = new byte[9];
        }

        return Zero();
    }

    /// <summary>Exult <c>UI_has_spell(book, spell)</c>.</summary>
    UsecodeValue HasSpell(UsecodeValue[] p) =>
        UsecodeValue.FromInt(SpellBit(p) is var (book, circle, bit) && book is not null && (book.SpellCircles![circle] & bit) != 0 ? 1 : 0);

    /// <summary>Exult <c>UI_remove_spell(book, spell)</c>: 1 if the book had it.</summary>
    UsecodeValue RemoveSpell(UsecodeValue[] p)
    {
        if (SpellBit(p) is not ({ } book, var circle, var bit) || (book.SpellCircles![circle] & bit) == 0)
        {
            return Zero();
        }

        book.SpellCircles[circle] &= (byte)~bit;
        return UsecodeValue.FromInt(1);
    }

    /// <summary>The spellbook of parameter 0 and spell of parameter 1 (0-71) as a circle and bit; no book when it is none.</summary>
    (U7Object? Book, int Circle, int Bit) SpellBit(UsecodeValue[] p)
    {
        var spell = (int)p[1].IntValue;
        if (_vm.GetItem(p[0]) is not { } book || !_vm.Catalog[book.Shape].IsSpellbookClass || spell is < 0 or >= 72)
        {
            return (null, 0, 0);
        }

        book.SpellCircles ??= new byte[9];
        return (book, spell / 8, 1 << (spell % 8));
    }

    /// <summary>Exult <c>UI_get_skin_colour</c>: the avatar's.</summary>
    UsecodeValue GetSkinColour() => UsecodeValue.FromInt(_vm.Avatar.SkinColor);

    /// <summary>Exult <c>UI_get_temperature(npc)</c>: how cold it is, 0-63.</summary>
    UsecodeValue GetTemperature(UsecodeValue[] p) =>
        UsecodeValue.FromInt(_vm.GetItem(p[0]) is { IsActor: true } npc ? npc.Temperature : 0);

    /// <summary>Exult <c>UI_set_temperature(npc, value)</c> (<c>Actor::set_temperature</c>: kept to 0-63).</summary>
    UsecodeValue SetTemperature(UsecodeValue[] p)
    {
        if (_vm.GetItem(p[0]) is { IsActor: true } npc)
        {
            npc.Temperature = Math.Clamp((int)p[1].IntValue, 0, 63);
        }

        return Zero();
    }

    /// <summary>Exult <c>Actor::get_temperature_zone</c> ("SI-verified"): 1 below 15, 2 below 25, 3 below 40, 4 below 50, else 5.</summary>
    UsecodeValue GetTemperatureZone(UsecodeValue[] p) =>
        UsecodeValue.FromInt(_vm.GetItem(p[0]) is { IsActor: true } npc
            ? npc.Temperature switch { < 15 => 1, < 25 => 2, < 40 => 3, < 50 => 4, _ => 5 }
            : 0);

    /// <summary>The spots <c>Actor::figure_warmth</c> counts.</summary>
    static readonly int[] WarmthSpots = [ReadySpot.Head, ReadySpot.Cloak, ReadySpot.Feet, ReadySpot.Torso, ReadySpot.Gloves, ReadySpot.Legs];

    /// <summary>Exult <c>Actor::figure_warmth</c>: -75, and each worn thing's warmth (<c>shape_info.txt</c>'s warmth_data).</summary>
    UsecodeValue GetNpcWarmth(UsecodeValue[] p)
    {
        if (_vm.GetItem(p[0]) is not { IsActor: true } npc)
        {
            return UsecodeValue.FromInt(-75);
        }

        var warmth = -75;
        foreach (var spot in WarmthSpots)
        {
            if (Equipment.GetReadied(npc, spot) is { } worn)
            {
                warmth += ShapeInfoText.Find(_vm.Catalog[worn.Shape].Warmth, worn.Frame & 31, -1, e => (e.Frame, e.Quality))?.Value ?? 0;
            }
        }

        return UsecodeValue.FromInt(warmth);
    }

    /// <summary>Exult <c>UI_create_barge_object(width, height[, dir])</c>: a barge outside the world, on last_created.</summary>
    UsecodeValue CreateBargeObject(UsecodeValue[] p, int n)
    {
        if (n < 2)
        {
            return Zero();
        }

        var barge = _vm.Map.CreateIregObject(961, 0);
        barge.IsBarge = true;
        barge.BargeXTiles = (int)p[0].IntValue;
        barge.BargeYTiles = (int)p[1].IntValue;
        barge.BargeDir = n >= 3 ? ((int)p[2].IntValue >> 1) & 3 : 0;
        barge.SetFlag(ObjFlag.OkayToTake);
        _vm.LastCreated.Add(barge);
        return UsecodeValue.FromObject(barge);
    }

    /// <summary>Exult <c>UI_in_usecode_path(npc)</c>: walking a <c>path_run_usecode</c>.</summary>
    UsecodeValue InUsecodePath(UsecodeValue[] p) =>
        UsecodeValue.FromInt(_vm.GetItem(p[0]) is { IsActor: true } npc &&
                             _vm.Schedules?.BrainOf(npc)?.CurrentAction is IfElsePathAction ? 1 : 0);

    /// <summary>Exult <c>UI_close_gumps2</c> and <c>UI_close_gump2</c>: as the others, while dragging too.</summary>
    UsecodeValue CloseGumps2() => CloseGumps();

    UsecodeValue CloseGump2(UsecodeValue[] p) => CloseGump(p);

    /// <summary>Exult <c>UI_npc_nearby2(npc)</c> ("guessing wildly", Serpent Isle): within 40 tiles of the avatar, whatever its state.</summary>
    UsecodeValue NpcNearby2(UsecodeValue[] p) =>
        UsecodeValue.FromInt(_vm.GetItem(p[0]) is { } npc && ObjectGeometry.Distance(npc, _vm.Avatar) < 40 ? 1 : 0);

    /// <summary>Exult <c>UI_si_display_map(map)</c>: 0 the world map (<c>display_map</c>), 1-5 SPRITES.VGA's maps 57-60 and 52.</summary>
    UsecodeValue SiDisplayMap(UsecodeValue[] p)
    {
        int? sprite = p[0].IntValue switch
        {
            0 => null,
            1 => 57,
            2 => 58,
            3 => 59,
            4 => 60,
            5 => 52,
            _ => -1
        };
        if (sprite is null)
        {
            return DisplayMap();
        }

        if (sprite >= 0)
        {
            _vm.ShowPicture(new UsecodePicture(sprite.Value, 0, null));
        }

        return Zero();
    }

    /// <summary>
    /// Exult <c>UI_resurrect_npc(npc)</c> (<c>Actor::resurrect</c> without a
    /// body, "behaves like the original does"): alive with full health and
    /// its afflictions gone, out of the world (Exult moves it to -1, -1)
    /// until usecode puts it somewhere; back in the party if it was.
    /// </summary>
    UsecodeValue ResurrectNpc(UsecodeValue[] p)
    {
        if (_vm.GetItem(p[0]) is not { IsActor: true } npc)
        {
            return Zero();
        }

        _vm.Map.TakeFromWorld(npc);
        npc.SetProp(ActorProp.Health, npc.GetProp(ActorProp.Strength));
        foreach (var flag in new[] { ObjFlag.Dead, ObjFlag.Poisoned, ObjFlag.Paralyzed, ObjFlag.Asleep, ObjFlag.Protection, ObjFlag.Cursed, ObjFlag.Charmed })
        {
            npc.ClearFlag(flag);
        }

        _vm.Party?.UpdatePartyStatus(npc);
        var sched = _vm.Party?.IsInParty(npc) == true ? ScheduleType.FollowAvatar : ScheduleType.Loiter;
        if (_vm.Schedules is { } s)
        {
            s.Revive(npc);
            s.SetScheduleType(npc, sched);
        }
        else
        {
            npc.ScheduleType = sched;
        }

        return Zero();
    }

    /// <summary>Exult <c>UI_init_conversation</c>: the faces go.</summary>
    UsecodeValue InitConversation()
    {
        _vm.Conv.InitFaces();
        _vm.NotifyFaces();
        return Zero();
    }

    /// <summary>Exult <c>UI_end_conversation</c>: the pending text is shown (a click if needed), then the faces go.</summary>
    UsecodeValue EndConversation()
    {
        _vm.ShowPendingText();
        _vm.AfterText(() =>
        {
            _vm.Conv.InitFaces();
            _vm.NotifyFaces();
        });
        return Zero();
    }

    /// <summary>Exult <c>UI_stop_arresting</c> (<c>Game_window::stop_arresting</c>).</summary>
    UsecodeValue StopArresting()
    {
        _vm.Combat?.Guards?.StopArresting();
        return Zero();
    }

    /// <summary>Exult <c>UI_can_avatar_reach_pos(loc)</c>.</summary>
    UsecodeValue CanAvatarReachPos(UsecodeValue[] p) => UsecodeValue.FromInt(IsDestReachable(_vm.Avatar, p[0]) ? 1 : 0);

    /// <summary>Exult <c>UI_add_to_keyring(quality)</c>.</summary>
    UsecodeValue AddToKeyring(UsecodeValue[] p)
    {
        _vm.Keyring.Add((int)p[0].IntValue);
        return Zero();
    }
}
