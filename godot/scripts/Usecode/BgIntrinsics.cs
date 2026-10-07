using Godot;
using U7.Core;
using U7.Data;

namespace U7.Usecode;

/// <summary>
/// Black Gate intrinsic dispatch (Exult <c>bgintrinsics.h</c> + <c>intrinsics.cc</c>).
/// Phase A/B are implemented; everything else logs and returns 0.
/// </summary>
public sealed class BgIntrinsics
{
    static readonly string[] Names =
    [
        "get_random", "execute_usecode_array", "delayed_execute_usecode_array",
        "show_npc_face", "remove_npc_face", "add_answer", "remove_answer",
        "push_answers", "pop_answers", "clear_answers", "select_from_menu",
        "select_from_menu2", "input_numeric_value", "set_item_shape", "find_nearest",
        "play_sound_effect", "die_roll", "get_item_shape", "get_item_frame",
        "set_item_frame", "get_item_quality", "set_item_quality", "get_item_quantity",
        "set_item_quantity", "get_object_position", "get_distance", "find_direction",
        "get_npc_object", "get_schedule_type", "set_schedule_type", "add_to_party",
        "remove_from_party", "get_npc_prop", "set_npc_prop", "get_avatar_ref",
        "get_party_list", "create_new_object", "set_last_created", "update_last_created",
        "get_npc_name", "count_objects", "find_object", "get_cont_items",
        "remove_party_items", "add_party_items", "get_music_track", "play_music",
        "npc_nearby", "find_nearby_avatar", "is_npc", "display_runes", "click_on_item",
        "error_message", "find_nearby", "give_last_created", "is_dead", "game_hour",
        "game_minute", "get_npc_number", "part_of_day", "get_alignment", "set_alignment",
        "move_object", "remove_npc", "item_say", "set_to_attack", "get_lift", "set_lift",
        "get_weather", "set_weather", "sit_down", "summon", "display_map", "kill_npc",
        "roll_to_win", "set_attack_mode", "set_oppressor", "clone", "UNKNOWN",
        "display_area", "wizard_eye", "resurrect", "add_spell", "sprite_effect",
        "attack_object", "book_mode", "stop_time", "cause_light", "get_barge",
        "earthquake", "is_pc_female", "armageddon", "halt_scheduled", "lightning",
        "get_array_size", "mark_virtue_stone", "recall_virtue_stone", "apply_damage",
        "is_pc_inside", "set_orrery", "UNKNOWN", "get_timer", "set_timer",
        "wearing_fellowship", "mouse_exists", "get_speech_track", "flash_mouse",
        "get_item_frame_rot", "set_item_frame_rot", "on_barge", "get_container",
        "remove_item", "UNKNOWN", "reduce_health", "is_readied", "restart_game",
        "start_speech", "run_endgame", "fire_projectile", "nap_time", "advance_time",
        "in_usecode", "call_guards", "obj_sprite_effect", "attack_avatar",
        "path_run_usecode", "close_gumps", "item_say", "close_gump", "in_gump_mode",
        "set_light", "UNKNOWN", "set_time_palette", "is_not_blocked", "play_sound_effect2",
        "direction_from", "get_item_flag", "set_item_flag", "clear_item_flag",
        "set_path_failure", "fade_palette", "get_party_list2", "in_combat",
        "start_blocking_speech", "is_water", "reset_conv_face", "set_camera",
        "get_dead_party", "view_tile", "telekenesis", "a_or_an"
    ];

    readonly UsecodeMachine _vm;

    public BgIntrinsics(UsecodeMachine vm) => _vm = vm;

    public static string Name(int id) =>
        (uint)id < (uint)Names.Length ? Names[id] : $"unknown_{id:X2}";

    public UsecodeValue Call(int id, UsecodeValue[] p, int n) =>
        id switch
        {
            0x00 => GetRandom(p),
            0x03 => ShowNpcFace(p),
            0x04 => RemoveNpcFace(p),
            0x05 => AddAnswer(p),
            0x06 => RemoveAnswer(p),
            0x07 => PushAnswers(),
            0x08 => PopAnswers(),
            0x09 => ClearAnswers(),
            0x0a => SelectFromMenu(),
            0x0b => SelectFromMenu2(),
            0x0d => SetItemShape(p),
            0x0e => FindNearest(p),
            0x10 => DieRoll(p),
            0x11 => GetItemShape(p),
            0x12 => GetItemFrame(p),
            0x13 => SetItemFrame(p),
            0x14 => GetItemQuality(p),
            0x15 => SetItemQuality(p),
            0x16 => GetItemQuantity(p),
            0x17 => SetItemQuantity(p),
            0x18 => GetObjectPosition(p),
            0x19 => GetDistance(p),
            0x1a => FindDirection(p),
            0x1b => GetNpcObject(p),
            0x1c => GetScheduleType(p),
            0x1d => SetScheduleType(p),
            0x20 => GetNpcProp(p),
            0x21 => SetNpcProp(p),
            0x22 => GetAvatarRef(),
            0x01 => ExecuteUsecodeArray(p, delayed: false),
            0x02 => ExecuteUsecodeArray(p, delayed: true),
            0x1e => AddToParty(p),
            0x1f => RemoveFromParty(p),
            0x23 => GetPartyList(),
            0x2e => PlayMusic(p),
            0x3f => RemoveNpc(p),
            0x51 => ResurrectIntrinsic(p),
            0x5c => HaltScheduled(p),
            0x73 => RestartGame(),
            0x83 => Zero(),
            0x8c => FadePalette(p),
            0x93 => GetDeadParty(p),
            0x27 => GetNpcName(p),
            0x28 => CountObjects(p),
            0x2a => GetContItems(p),
            0x2f => NpcNearby(p),
            0x30 => FindNearbyAvatar(p),
            0x31 => IsNpc(p),
            0x32 => DisplayRunes(p),
            0x33 => ClickOnItem(),
            0x35 => FindNearby(p),
            0x37 => IsDead(p),
            0x38 => UsecodeValue.FromInt(_vm.Clock?.Hour ?? 6),
            0x39 => UsecodeValue.FromInt(_vm.Clock?.Minute ?? 0),
            0x3a => GetNpcNumber(p),
            0x3b => UsecodeValue.FromInt((_vm.Clock?.Hour ?? 6) / 3),
            0x3c => GetAlignment(p),
            0x3d => SetAlignment(p),
            0x3e => MoveObject(p),
            0x40 => ItemSay(p),
            0x42 => GetLift(p),
            0x43 => SetLift(p),
            0x4a => RollToWin(p),
            0x5a => UsecodeValue.FromInt(0), // is_pc_female: male default
            0x5e => GetArraySize(p),
            0x61 => ApplyDamage(p),
            0x68 => UsecodeValue.FromInt(1), // mouse_exists
            0x6b => GetItemFrameRot(p),
            0x6c => SetItemFrameRot(p),
            0x6e => GetContainer(p),
            0x6f => RemoveItem(p),
            0x71 => ReduceHealth(p),
            0x79 => UsecodeValue.FromInt(_vm.InUsecode ? 1 : 0),
            0x7e => CloseGumps(),
            0x7f => ItemSay(p),
            0x80 => CloseGump(p),
            0x81 => UsecodeValue.FromInt(_vm.Gumps is { GumpMode: true } ? 1 : 0),
            0x87 => FindDirection(p),
            0x88 => GetItemFlag(p),
            0x89 => SetItemFlag(p),
            0x8a => ClearItemFlag(p),
            0x8d => GetPartyList(),
            0x8e => UsecodeValue.FromInt(_vm.Combat is { InCombat: true } ? 1 : 0),
            0x96 => AOrAn(p),
            0x0c => InputNumericValue(p),
            0x0f => Zero(), // play_sound_effect: no SFX playback yet
            0x86 => Zero(), // play_sound_effect2
            0x24 => CreateNewObject(p),
            0x25 => SetLastCreated(p),
            0x26 => UpdateLastCreated(p),
            0x2b => RemovePartyItems(p),
            0x2c => AddPartyItems(p),
            0x36 => GiveLastCreated(p),
            0x59 => Earthquake(p),
            0x65 => GetTimer(p),
            0x66 => SetTimer(p),
            0x67 => WearingFellowship(),
            0x91 => ResetConvFace(),
            _ => Stub(id, p, n)
        };

    static UsecodeValue Zero() => UsecodeValue.FromInt(0);

    UsecodeValue Stub(int id, UsecodeValue[] p, int n)
    {
        var args = n <= 0 ? "" : string.Join(", ", p.Take(n).Select(a => a.ToString()));
        _vm.Log($"stub UI_{Name(id)}({args})");
        RecordStub(id, _vm.CurrentFrame?.Function.Id ?? -1);
        return Zero();
    }

    /// <summary>Stub hits for the whole run (static: survives the scene reload on load).</summary>
    static readonly Dictionary<int, (int Hits, SortedSet<int> Callers)> StubHits = new();

    static void RecordStub(int id, int caller)
    {
        if (!StubHits.TryGetValue(id, out var e))
        {
            e = (0, new SortedSet<int>());
        }

        if (caller >= 0)
        {
            e.Callers.Add(caller);
        }

        StubHits[id] = (e.Hits + 1, e.Callers);
    }

    /// <summary>Stub hits so far, most-hit first, one per line.</summary>
    public static string StubSummary() =>
        StubHits.Count == 0
            ? "no stub hits"
            : string.Join("\n", StubHits.OrderByDescending(kv => kv.Value.Hits).Select(kv =>
                $"0x{kv.Key:X2} {Name(kv.Key)} x{kv.Value.Hits} in {string.Join(" ", kv.Value.Callers.Select(c => $"0x{c:X4}"))}"));

    /// <summary>Write the stub hits, most-hit first, to <c>stub_report.txt</c> in the repo root.</summary>
    public static void WriteStubReport()
    {
        if (StubHits.Count == 0 || U7Paths.RepoRoot.Length == 0)
        {
            return;
        }

        var lines = StubHits
            .OrderByDescending(kv => kv.Value.Hits)
            .Select(kv => $"0x{kv.Key:X2}  {Name(kv.Key),-26}{kv.Value.Hits,6}  " +
                          string.Join(" ", kv.Value.Callers.Select(c => $"0x{c:X4}")));
        System.IO.File.WriteAllLines(System.IO.Path.Combine(U7Paths.RepoRoot, "stub_report.txt"),
            lines.Prepend($"{"id",4}  {"name",-26}{"hits",6}  callers"));
    }

    UsecodeValue GetRandom(UsecodeValue[] p)
    {
        var range = (int)p[0].IntValue;
        return UsecodeValue.FromInt(_vm.Random(range));
    }

    UsecodeValue DieRoll(UsecodeValue[] p)
    {
        var low = (int)p[0].IntValue;
        var high = (int)p[1].IntValue;
        if (low > high)
        {
            (low, high) = (high, low);
        }

        return UsecodeValue.FromInt(low + _vm.Random(high - low + 1) - 1);
    }

    UsecodeValue ShowNpcFace(UsecodeValue[] p)
    {
        var frame = p.Length > 1 ? (int)p[1].IntValue : 0;
        var item = _vm.GetItem(p[0]);
        var shape = FaceShape(p[0], item);
        if (shape < 0)
        {
            return Zero();
        }

        // The panel names a speaker the player already knows (Exult sets met here,
        // so a first meeting stays nameless until the next conversation).
        var name = item is { NpcNum: >= 0 } && (item.NpcNum == 0 || item.GetFlag(U7.Actors.ObjFlag.Met))
            ? item.NpcName
            : "";
        if (item is { NpcNum: >= 0 })
        {
            item.SetFlag(U7.Actors.ObjFlag.Met);
        }

        _vm.Conv.ShowFace(shape, frame, name);
        _vm.NotifyFaces();
        return Zero();
    }

    UsecodeValue RemoveNpcFace(UsecodeValue[] p)
    {
        var item = _vm.GetItem(p[0]);
        var shape = FaceShape(p[0], item);
        if (shape >= 0)
        {
            _vm.Conv.RemoveFace(shape);
            _vm.NotifyFaces();
        }

        return Zero();
    }

    int FaceShape(UsecodeValue arg, U7Object? item)
    {
        if (arg.IsPtr)
        {
            if (item is { NpcNum: >= 0 })
            {
                return item.NpcNum == 0 ? 0 : item.FaceNum;
            }

            return -1;
        }

        if (arg.IsInt || arg.IsArray)
        {
            var n = Math.Abs((int)arg.NeedIntValue());
            return n == 356 ? 0 : n;
        }

        if (item is { NpcNum: >= 0 })
        {
            return item.NpcNum == 0 ? 0 : item.FaceNum;
        }

        return -1;
    }

    UsecodeValue AddAnswer(UsecodeValue[] p)
    {
        _vm.Conv.AddAnswer(p[0]);
        _vm.NotifyAnswers();
        return Zero();
    }

    UsecodeValue RemoveAnswer(UsecodeValue[] p)
    {
        _vm.Conv.RemoveAnswer(p[0]);
        _vm.NotifyAnswers();
        return Zero();
    }

    UsecodeValue PushAnswers()
    {
        _vm.Conv.PushAnswers();
        _vm.NotifyAnswers();
        return Zero();
    }

    UsecodeValue PopAnswers()
    {
        _vm.Conv.PopAnswers();
        _vm.NotifyAnswers();
        return Zero();
    }

    UsecodeValue ClearAnswers()
    {
        _vm.Conv.ClearAnswers();
        _vm.NotifyAnswers();
        return Zero();
    }

    UsecodeValue SelectFromMenu()
    {
        if (_vm.Conv.Answers.Count == 0)
        {
            return UsecodeValue.FromString("");
        }

        _vm.RequestWait(UsecodeWait.SelectMenu);
        return Zero();
    }

    UsecodeValue SelectFromMenu2()
    {
        if (_vm.Conv.Answers.Count == 0)
        {
            return UsecodeValue.FromInt(0);
        }

        _vm.RequestWait(UsecodeWait.SelectMenuIndex);
        return Zero();
    }

    UsecodeValue SetItemShape(UsecodeValue[] p)
    {
        var item = _vm.GetItem(p[0]);
        if (item is null)
        {
            return Zero();
        }

        item.Shape = (int)p[1].IntValue;
        var info = _vm.Catalog[item.Shape];
        var reflected = (item.Frame & 32) != 0;
        item.DimX = reflected ? info.DimY : info.DimX;
        item.DimY = reflected ? info.DimX : info.DimY;
        item.DimZ = info.DimZ;
        item.Solid = info.Solid;
        return Zero();
    }

    UsecodeValue GetItemShape(UsecodeValue[] p)
    {
        var item = _vm.GetItem(p[0]);
        return UsecodeValue.FromInt(item?.Shape ?? 0);
    }

    UsecodeValue GetItemFrame(UsecodeValue[] p)
    {
        var item = _vm.GetItem(p[0]);
        return UsecodeValue.FromInt(item is null ? 0 : item.Frame & 31);
    }

    UsecodeValue SetItemFrame(UsecodeValue[] p)
    {
        var item = _vm.GetItem(p[0]);
        if (item is null)
        {
            return Zero();
        }

        var frame = (int)p[1].IntValue;
        item.Frame = (item.Frame & 32) | (frame & 31);
        return Zero();
    }

    UsecodeValue GetItemFrameRot(UsecodeValue[] p)
    {
        var item = _vm.GetItem(p[0]);
        return UsecodeValue.FromInt(item?.Frame ?? 0);
    }

    UsecodeValue SetItemFrameRot(UsecodeValue[] p)
    {
        var item = _vm.GetItem(p[0]);
        if (item is not null)
        {
            item.Frame = (int)p[1].IntValue;
        }

        return Zero();
    }

    UsecodeValue GetItemQuality(UsecodeValue[] p)
    {
        var item = _vm.GetItem(p[0]);
        if (item is null)
        {
            return Zero();
        }

        return UsecodeValue.FromInt(_vm.Catalog[item.Shape].HasQuality ? item.Quality : 0);
    }

    UsecodeValue SetItemQuality(UsecodeValue[] p)
    {
        var item = _vm.GetItem(p[0]);
        if (item is not null)
        {
            item.Quality = (int)p[1].IntValue;
        }

        return Zero();
    }

    UsecodeValue GetItemQuantity(UsecodeValue[] p)
    {
        var item = _vm.GetItem(p[0]);
        if (item is null)
        {
            return Zero();
        }

        return UsecodeValue.FromInt(_vm.Catalog[item.Shape].HasQuantity ? Math.Max(1, item.Quality) : 1);
    }

    UsecodeValue SetItemQuantity(UsecodeValue[] p)
    {
        var item = _vm.GetItem(p[0]);
        if (item is not null && _vm.Catalog[item.Shape].HasQuantity)
        {
            item.Quality = (int)p[1].IntValue;
        }

        return UsecodeValue.FromInt(item is not null && _vm.Catalog[item.Shape].HasQuantity ? 1 : 0);
    }

    UsecodeValue GetObjectPosition(UsecodeValue[] p)
    {
        var obj = _vm.GetItem(p[0]);
        var arr = UsecodeValue.FromArray(3);
        if (obj is not null)
        {
            arr.PutElem(0, UsecodeValue.FromInt(obj.Tx));
            arr.PutElem(1, UsecodeValue.FromInt(obj.Ty));
            arr.PutElem(2, UsecodeValue.FromInt(obj.Tz));
        }

        return arr;
    }

    UsecodeValue GetDistance(UsecodeValue[] p)
    {
        var a = _vm.GetItem(p[0]);
        var b = _vm.GetItem(p[1]);
        if (a is null || b is null)
        {
            return Zero();
        }

        return UsecodeValue.FromInt(new TileCoord(a.Tx, a.Ty, a.Tz).Distance2d(new TileCoord(b.Tx, b.Ty, b.Tz)));
    }

    UsecodeValue FindDirection(UsecodeValue[] p)
    {
        var from = PositionOf(p[0]);
        var to = PositionOf(p[1]);
        var dy = from.Ty - to.Ty;
        var dx = to.Tx - from.Tx;
        // Exult Get_direction(deltay, deltax): 0 N … clockwise? Actually
        // "Treat as cartesian" Get_direction(t1.ty - t2.ty, t2.tx - t1.tx).
        var angle = Math.Atan2(dx, -dy); // 0 = north
        var dir = (int)Math.Round(angle / (Math.PI / 4));
        if (dir < 0)
        {
            dir += 8;
        }

        return UsecodeValue.FromInt(dir & 7);
    }

    TileCoord PositionOf(UsecodeValue v)
    {
        var obj = _vm.GetItem(v);
        if (obj is not null)
        {
            return new TileCoord(obj.Tx, obj.Ty, obj.Tz);
        }

        if (v.ArraySize >= 3)
        {
            return new TileCoord((int)v.GetElem(0).IntValue, (int)v.GetElem(1).IntValue, (int)v.GetElem(2).IntValue);
        }

        var c = _vm.CurrentFrame?.Caller;
        return c is null ? default : new TileCoord(c.Tx, c.Ty, c.Tz);
    }

    UsecodeValue GetAvatarRef() => UsecodeValue.FromObject(_vm.Avatar);

    /// <summary>Exult <c>UI_execute_usecode_array</c> / <c>UI_delayed_execute_usecode_array</c>.</summary>
    UsecodeValue ExecuteUsecodeArray(UsecodeValue[] p, bool delayed)
    {
        var obj = _vm.GetItem(p[0]);
        var code = p[1];
        var ticks = 1;
        if (delayed)
        {
            // Exult: BG infinite-loop guard for internal_exec + [.., .., 0x6f7].
            if (_vm.LastEvent == (int)UsecodeEvent.InternalExec && code.ArraySize == 3 &&
                code.GetElem(2).IntValue == 0x6f7)
            {
                return Zero();
            }

            ticks = (int)p[2].IntValue;
        }

        _vm.StartScript(obj, code, ticks * UsecodeMachine.StdDelaySeconds);
        return UsecodeValue.FromInt(1);
    }

    /// <summary>Exult <c>UI_play_music(track, item)</c>: 0xff stops.</summary>
    UsecodeValue PlayMusic(UsecodeValue[] p)
    {
        var val = (int)p[0].IntValue;
        var track = val & 0xff;
        if (track == 0xff)
        {
            _vm.Music?.Stop();
        }
        else
        {
            _vm.Music?.Start(track, ((val >> 8) & 1) != 0);
        }

        return Zero();
    }

    /// <summary>Exult <c>UI_remove_npc</c>: off the map, record kept.</summary>
    UsecodeValue RemoveNpc(UsecodeValue[] p)
    {
        var npc = _vm.GetItem(p[0]);
        if (npc is { IsActor: true })
        {
            if (_vm.Schedules is { } s)
            {
                s.SetScheduleType(npc, U7.Actors.ScheduleType.Wait);
            }

            _vm.Map.RemoveObject(npc);
        }

        return Zero();
    }

    /// <summary>Exult <c>UI_resurrect(body)</c>: schedules the resurrect script on the corpse.</summary>
    UsecodeValue ResurrectIntrinsic(UsecodeValue[] p)
    {
        var body = _vm.GetItem(p[0]);
        var num = body?.LiveNpcNum ?? -1;
        if (body is null || num <= 0 || num >= _vm.Npcs.Count || _vm.Npcs[num] is not { } npc)
        {
            return UsecodeValue.FromObject(null);
        }

        _vm.StartScript(body, UsecodeValue.FromArray(1, UsecodeValue.FromInt(0x81)), UsecodeMachine.StdDelaySeconds);
        return UsecodeValue.FromObject(npc);
    }

    UsecodeValue HaltScheduled(UsecodeValue[] p)
    {
        var obj = _vm.GetItem(p[0]);
        if (obj is not null)
        {
            _vm.TerminateScripts(obj);
        }

        return Zero();
    }

    UsecodeValue RestartGame()
    {
        _vm.Music?.Stop();
        _vm.RestartRequested = true;
        return Zero();
    }

    /// <summary>Exult <c>UI_fade_palette(cycles, 1, inout)</c>: 0 = to black, 1 = back.</summary>
    UsecodeValue FadePalette(UsecodeValue[] p)
    {
        var inout = p.Length > 2 ? (int)p[2].IntValue : 1;
        _vm.FadedOut = inout == 0;
        return Zero();
    }

    /// <summary>Exult <c>UI_get_dead_party(obj)</c>: corpses of party members within 50 tiles.</summary>
    UsecodeValue GetDeadParty(UsecodeValue[] p)
    {
        var obj = _vm.GetItem(p[0]) ?? _vm.Avatar;
        var bodies = new List<U7Object>();
        foreach (var o in _vm.Map.FindNearby(new TileCoord(obj.Tx, obj.Ty, obj.Tz), U7Constants.AnyShape, 50))
        {
            if (o.LiveNpcNum > 0 && o.LiveNpcNum < _vm.Npcs.Count &&
                _vm.Npcs[o.LiveNpcNum] is { } npc && npc.IsDead && npc.GetFlag(U7.Actors.ObjFlag.InParty))
            {
                bodies.Add(o);
            }
        }

        var arr = UsecodeValue.FromArray(bodies.Count);
        for (var i = 0; i < bodies.Count; i++)
        {
            arr.PutElem(i, UsecodeValue.FromObject(bodies[i]));
        }

        return arr;
    }

    /// <summary>Exult <c>Usecode_internal::get_party</c>: avatar first, then the members.</summary>
    UsecodeValue GetPartyList()
    {
        var members = _vm.Party?.Members;
        var count = members?.Count ?? 0;
        var arr = UsecodeValue.FromArray(1 + count, UsecodeValue.FromObject(_vm.Avatar));
        for (var i = 0; i < count; i++)
        {
            arr.PutElem(1 + i, UsecodeValue.FromObject(members![i]));
        }

        return arr;
    }

    /// <summary>Exult <c>UI_add_to_party</c> (BG intrinsic 0x1e): join, follow the avatar, become good.</summary>
    UsecodeValue AddToParty(UsecodeValue[] p)
    {
        var npc = _vm.GetItem(p[0]);
        if (npc is null || _vm.Party is not { } party || !party.AddToParty(npc))
        {
            return Zero();
        }

        if (_vm.Schedules is { } schedules)
        {
            schedules.SetScheduleType(npc, U7.Actors.ScheduleType.FollowAvatar);
        }
        else
        {
            npc.ScheduleType = U7.Actors.ScheduleType.FollowAvatar;
        }

        npc.Alignment = U7.Actors.Alignment.Good;
        GD.Print($"party: {npc.NpcName} joins");
        return Zero();
    }

    /// <summary>Exult <c>UI_remove_from_party</c>.</summary>
    UsecodeValue RemoveFromParty(UsecodeValue[] p)
    {
        var npc = _vm.GetItem(p[0]);
        if (npc is not null && _vm.Party is { } party && party.RemoveFromParty(npc))
        {
            npc.Alignment = U7.Actors.Alignment.Neutral;
            GD.Print($"party: {npc.NpcName} leaves");
        }

        return Zero();
    }

    UsecodeValue GetNpcObject(UsecodeValue[] p)
    {
        if (p[0].IsArray)
        {
            var arr = UsecodeValue.FromArray(p[0].ArraySize);
            for (var i = 0; i < p[0].ArraySize; i++)
            {
                arr.PutElem(i, UsecodeValue.FromObject(_vm.GetItem(p[0].GetElem(i))));
            }

            return arr;
        }

        return UsecodeValue.FromObject(_vm.GetItem(p[0]));
    }

    UsecodeValue GetScheduleType(UsecodeValue[] p)
    {
        var npc = _vm.GetItem(p[0]);
        if (npc is null || npc.NpcNum < 0)
        {
            return Zero();
        }

        return UsecodeValue.FromInt(_vm.Schedules?.GetActualType(npc) ?? npc.ScheduleType);
    }

    UsecodeValue SetScheduleType(UsecodeValue[] p)
    {
        var npc = _vm.GetItem(p[0]);
        if (npc is not null)
        {
            var type = (int)p[1].IntValue;
            if (_vm.Schedules is { } schedules)
            {
                schedules.SetScheduleType(npc, type);
            }
            else
            {
                npc.ScheduleType = type;
            }
        }

        return Zero();
    }

    UsecodeValue GetNpcProp(UsecodeValue[] p)
    {
        var item = _vm.GetItem(p[0]);
        if (item is null)
        {
            return Zero();
        }

        if (p[1].StrValue is { Length: > 0 })
        {
            return Zero();
        }

        var prop = (int)p[1].IntValue;
        if (item.NpcNum < 0 && prop == U7.Actors.ActorProp.Health)
        {
            return UsecodeValue.FromInt(item.Quality);
        }

        return UsecodeValue.FromInt(item.GetProp(prop));
    }

    UsecodeValue SetNpcProp(UsecodeValue[] p)
    {
        var item = _vm.GetItem(p[0]);
        if (item is null || item.NpcNum < 0)
        {
            return Zero();
        }

        var prop = (int)p[1].IntValue;
        var delta = (int)p[2].IntValue;
        if (prop == U7.Actors.ActorProp.Exp)
        {
            if (delta < 0)
            {
                if (delta >= short.MinValue)
                {
                    delta = (ushort)delta;
                }
                else
                {
                    return UsecodeValue.FromInt(1);
                }
            }

            delta /= 2;
        }

        if (prop != U7.Actors.ActorProp.SexFlag)
        {
            delta += item.GetProp(prop);
        }

        item.SetProp(prop, delta);
        return UsecodeValue.FromInt(1);
    }

    UsecodeValue GetNpcName(UsecodeValue[] p)
    {
        if (p[0].IsArray)
        {
            var names = new List<UsecodeValue>();
            for (var i = 0; i < p[0].ArraySize; i++)
            {
                var n = NpcNameOf(_vm.GetItem(p[0].GetElem(i)));
                if (n.Length > 0)
                {
                    names.Add(UsecodeValue.FromString(n));
                }
            }

            var arr = UsecodeValue.FromArray(names.Count);
            for (var i = 0; i < names.Count; i++)
            {
                arr.PutElem(i, names[i]);
            }

            return arr;
        }

        return UsecodeValue.FromString(NpcNameOf(_vm.GetItem(p[0])));
    }

    string NpcNameOf(U7Object? item)
    {
        if (item is null)
        {
            return "";
        }

        if (!string.IsNullOrEmpty(item.NpcName))
        {
            return item.NpcName;
        }

        if (item.NpcNum == 0 || item == _vm.Avatar)
        {
            return "Avatar";
        }

        var name = _vm.Catalog[item.Shape].Name;
        return string.IsNullOrEmpty(name) ? $"shape {item.Shape}" : name;
    }

    UsecodeValue IsNpc(UsecodeValue[] p)
    {
        var item = _vm.GetItem(p[0]);
        return UsecodeValue.FromInt(item is { NpcNum: >= 0 } ? 1 : 0);
    }

    UsecodeValue IsDead(UsecodeValue[] p)
    {
        var npc = _vm.GetItem(p[0]);
        return UsecodeValue.FromInt(npc is { IsDead: true } ? 1 : 0);
    }

    static UsecodeValue RollToWin(UsecodeValue[] p) =>
        UsecodeValue.FromInt(U7.Actors.CombatEngine.RollToWin((int)p[0].IntValue, (int)p[1].IntValue) ? 1 : 0);

    UsecodeValue ApplyDamage(UsecodeValue[] p)
    {
        var obj = _vm.GetItem(p[3]);
        if (obj is null)
        {
            return Zero();
        }

        _vm.Combat?.ApplyDamage(null, obj, (int)p[0].IntValue, (int)p[1].IntValue, (int)p[2].IntValue);
        return UsecodeValue.FromInt(1);
    }

    UsecodeValue ReduceHealth(UsecodeValue[] p)
    {
        var obj = _vm.GetItem(p[0]);
        if (obj is not null)
        {
            _vm.Combat?.ReduceHealth(obj, (int)p[1].IntValue, null, (int)p[2].IntValue);
        }

        return Zero();
    }

    UsecodeValue GetAlignment(UsecodeValue[] p)
    {
        var npc = _vm.GetItem(p[0]);
        return UsecodeValue.FromInt(npc?.Alignment ?? 0);
    }

    UsecodeValue SetAlignment(UsecodeValue[] p)
    {
        var npc = _vm.GetItem(p[0]);
        if (npc is not null)
        {
            npc.Alignment = (int)p[1].IntValue;
        }

        return Zero();
    }

    UsecodeValue DisplayRunes(UsecodeValue[] p)
    {
        var sb = new System.Text.StringBuilder();
        var text = p[1];
        var cnt = text.IsArray ? text.ArraySize : 1;
        for (var i = 0; i < cnt; i++)
        {
            var line = (text.IsArray ? text.GetElem(i) : text).StrValue ?? "";
            if (i > 0)
            {
                sb.Append('\n');
            }

            sb.Append(line);
        }

        var shown = sb.ToString();
        _vm.ShowText(shown);
        _vm.Conv.TextFace = -1;
        _vm.RequestWait(UsecodeWait.ClickToContinue);
        return Zero();
    }

    UsecodeValue ClickOnItem()
    {
        _vm.RequestWait(UsecodeWait.ClickOnItem);
        return Zero();
    }

    UsecodeValue NpcNearby(UsecodeValue[] p)
    {
        var obj = _vm.GetItem(p[0]);
        if (obj is null)
        {
            return Zero();
        }

        var dist = new TileCoord(obj.Tx, obj.Ty, obj.Tz)
            .Distance2d(new TileCoord(_vm.Avatar.Tx, _vm.Avatar.Ty, _vm.Avatar.Tz));
        return UsecodeValue.FromInt(dist <= U7Constants.NpcActivityDist ? 1 : 0);
    }

    UsecodeValue FindNearbyAvatar(UsecodeValue[] p)
    {
        var shape = (int)(p[0].IsArray ? p[0].GetElem0().IntValue : p[0].IntValue);
        var origin = new TileCoord(_vm.Avatar.Tx, _vm.Avatar.Ty, _vm.Avatar.Tz);
        return NearbyArray(_vm.Map.FindNearby(origin, shape, 192, 0));
    }

    UsecodeValue FindNearby(UsecodeValue[] p)
    {
        var shape = (int)(p[1].IsArray ? p[1].GetElem0().IntValue : p[1].IntValue);
        var dist = (int)p[2].IntValue;
        var mask = p.Length > 3 ? (int)p[3].IntValue : 0;
        var origin = PositionOf(p[0]);
        return NearbyArray(_vm.Map.FindNearby(origin, shape, dist, mask));
    }

    static UsecodeValue NearbyArray(List<U7Object> found)
    {
        if (found.Count == 0)
        {
            return UsecodeValue.FromArray(0);
        }

        var arr = UsecodeValue.FromArray(found.Count);
        for (var i = 0; i < found.Count; i++)
        {
            arr.PutElem(i, UsecodeValue.FromObject(found[i]));
        }

        return arr;
    }

    UsecodeValue FindNearest(UsecodeValue[] p)
    {
        var obj = _vm.GetItem(p[0]);
        if (obj is null)
        {
            return UsecodeValue.FromObject(null);
        }

        var shape = (int)p[1].IntValue;
        var dist = (int)p[2].IntValue;
        var found = _vm.Map.FindNearby(new TileCoord(obj.Tx, obj.Ty, obj.Tz), shape, dist);
        U7Object? closest = null;
        var best = int.MaxValue;
        foreach (var each in found)
        {
            var dx = obj.Tx - each.Tx;
            var dy = obj.Ty - each.Ty;
            var dz = obj.Tz - each.Tz;
            var d = dx * dx + dy * dy + dz * dz;
            if (d < best)
            {
                best = d;
                closest = each;
            }
        }

        return UsecodeValue.FromObject(closest);
    }

    UsecodeValue GetNpcNumber(UsecodeValue[] p)
    {
        var item = _vm.GetItem(p[0]);
        if (item is null)
        {
            return Zero();
        }

        if (item == _vm.Avatar || item.NpcNum == 0)
        {
            return UsecodeValue.FromInt(-356);
        }

        return UsecodeValue.FromInt(item.NpcNum > 0 ? -item.NpcNum : 0);
    }

    UsecodeValue MoveObject(UsecodeValue[] p)
    {
        var loc = p[1];
        var tx = loc.IsArray ? (int)loc.GetElem(0).IntValue : 0;
        var ty = loc.IsArray && loc.ArraySize > 1 ? (int)loc.GetElem(1).IntValue : 0;
        var tz = loc.IsArray && loc.ArraySize > 2 ? (int)loc.GetElem(2).IntValue : 0;
        if (p[0].IntValue == -357)
        {
            _vm.Map.MoveObject(_vm.Avatar, tx, ty, tz);
            return Zero();
        }

        var obj = _vm.GetItem(p[0]);
        if (obj is not null)
        {
            _vm.Map.MoveObject(obj, tx, ty, tz);
        }

        return Zero();
    }

    UsecodeValue ItemSay(UsecodeValue[] p)
    {
        var obj = _vm.GetItem(p[0]);
        var str = p[1].StrValue;
        _vm.Bark(obj, str);
        return Zero();
    }

    UsecodeValue GetLift(UsecodeValue[] p)
    {
        var obj = _vm.GetItem(p[0]);
        return UsecodeValue.FromInt(obj?.Tz ?? 0);
    }

    UsecodeValue SetLift(UsecodeValue[] p)
    {
        var obj = _vm.GetItem(p[0]);
        if (obj is not null)
        {
            var lift = (int)p[1].IntValue;
            if (lift is >= 0 and < 20)
            {
                _vm.Map.MoveObject(obj, obj.Tx, obj.Ty, lift);
            }
        }

        return Zero();
    }

    UsecodeValue GetArraySize(UsecodeValue[] p) =>
        UsecodeValue.FromInt(p[0].IsArray ? p[0].ArraySize : 1);

    UsecodeValue GetContainer(UsecodeValue[] p)
    {
        var obj = _vm.GetItem(p[0]);
        return UsecodeValue.FromObject(obj?.Container);
    }

    UsecodeValue CloseGumps()
    {
        _vm.Gumps?.CloseAll();
        return Zero();
    }

    UsecodeValue CloseGump(UsecodeValue[] p)
    {
        var obj = _vm.GetItem(p[0]);
        if (obj is not null)
        {
            _vm.Gumps?.CloseFor(obj);
        }

        return Zero();
    }

    /// <summary>Exult <c>Usecode_internal::count_objects</c>: -357 counts the whole party; stacks count their quantity.</summary>
    UsecodeValue CountObjects(UsecodeValue[] p)
    {
        var shape = (int)p[1].IntValue;
        var qual = p.Length > 2 ? (int)p[2].IntValue : U7Constants.AnyShape;
        var frame = p.Length > 3 ? (int)p[3].IntValue : U7Constants.AnyShape;
        var oval = p[0].IsPtr ? 0 : (int)p[0].IntValue;
        if (oval == -357)
        {
            return UsecodeValue.FromInt(PartyObjects().Sum(m => Quantities.Count(m, shape, qual, frame)));
        }

        var obj = _vm.GetItem(p[0]);
        return UsecodeValue.FromInt(obj is null ? 0 : Quantities.Count(obj, shape, qual, frame));
    }

    U7.Actors.ItemQuantity? _quantities;

    U7.Actors.ItemQuantity Quantities => _quantities ??=
        new U7.Actors.ItemQuantity(_vm.Catalog, _vm.Map, _vm.Combat?.Weapons, _vm.Combat?.Ammo);

    /// <summary>Exult <c>get_party</c> as objects: avatar first, then the members.</summary>
    List<U7Object> PartyObjects()
    {
        var list = new List<U7Object> { _vm.Avatar };
        if (_vm.Party?.Members is { } members)
        {
            list.AddRange(members);
        }

        return list;
    }

    /// <summary>Exult <c>Usecode_internal::remove_party_items(quantity, shape, quality, frame, flag)</c>.</summary>
    UsecodeValue RemovePartyItems(UsecodeValue[] p)
    {
        var quantity = (int)p[0].NeedIntValue();
        var shape = (int)p[1].IntValue;
        var qual = (int)p[2].IntValue;
        var frame = (int)p[3].IntValue;
        var party = PartyObjects();
        var avail = party.Sum(m => Quantities.Count(m, shape, qual, frame));
        if (quantity == U7Constants.AnyShape)
        {
            quantity = avail;
        }
        else if (avail < quantity)
        {
            return Zero();
        }

        var orig = quantity;
        foreach (var member in party)
        {
            if (quantity <= 0)
            {
                break;
            }

            quantity = Quantities.Remove(member, quantity, shape, qual, frame);
        }

        return UsecodeValue.FromInt(quantity != orig ? 1 : 0);
    }

    /// <summary>
    /// Exult <c>Usecode_internal::add_party_items(quantity, shape, quality, frame, temporary)</c>:
    /// returns the party members who received items (BG does not drop the rest).
    /// </summary>
    UsecodeValue AddPartyItems(UsecodeValue[] p)
    {
        var quantity = (int)p[0].IntValue;
        var shape = (int)p[1].IntValue;
        var qual = (int)p[2].IntValue;
        var frame = (int)p[3].IntValue;
        var temp = p[4].IntValue != 0;
        var got = new List<U7Object>();
        foreach (var member in PartyObjects())
        {
            if (quantity <= 0)
            {
                break;
            }

            var prev = quantity;
            quantity = Quantities.Add(member, quantity, shape, qual, frame, dontCreate: false, temp);
            if (quantity < prev)
            {
                got.Add(member);
            }
        }

        var arr = UsecodeValue.FromArray(got.Count);
        for (var i = 0; i < got.Count; i++)
        {
            arr.PutElem(i, UsecodeValue.FromObject(got[i]));
        }

        return arr;
    }

    /// <summary>
    /// Exult <c>Usecode_internal::create_object</c>: a new object outside the
    /// world, pushed on last_created. Monster shapes become neutral monsters in
    /// the wait schedule.
    /// </summary>
    UsecodeValue CreateNewObject(UsecodeValue[] p)
    {
        var shape = (int)p[0].IntValue;
        U7Object obj;
        if (_vm.Combat is { } combat && (combat.IsMonsterShape(shape) || _vm.Catalog[shape].IsNpcClass))
        {
            obj = combat.CreateMonster(shape, 0, U7.Actors.ScheduleType.Wait, U7.Actors.Alignment.Neutral);
            obj.Alignment = U7.Actors.Alignment.Neutral;
            obj.Removed = true;
        }
        else
        {
            obj = Quantities.NewItem(shape, 0);
            obj.SetFlag(U7.Actors.ObjFlag.OkayToTake);
        }

        _vm.LastCreated.Add(obj);
        return UsecodeValue.FromObject(obj);
    }

    /// <summary>Exult <c>UI_set_last_created(item)</c>: take it off the map (or out of its container) onto last_created.</summary>
    UsecodeValue SetLastCreated(UsecodeValue[] p)
    {
        var obj = _vm.GetItem(p[0]);
        if (obj is not null && _vm.LastCreated.Contains(obj))
        {
            return Zero();
        }

        if (obj is not null)
        {
            _vm.LastCreated.Add(obj);
            _vm.Map.TakeFromWorld(obj);
            obj.Removed = true;
        }

        return UsecodeValue.FromObject(obj);
    }

    /// <summary>
    /// Exult <c>UI_update_last_created(pos)</c>: pop last_created and place it
    /// at (x, y[, z]); a one-element array just drops it.
    /// </summary>
    UsecodeValue UpdateLastCreated(UsecodeValue[] p)
    {
        if (_vm.LastCreated.Count == 0)
        {
            return Zero();
        }

        var obj = _vm.LastCreated[^1];
        _vm.LastCreated.RemoveAt(_vm.LastCreated.Count - 1);
        var arr = p[0];
        var sz = arr.IsArray ? arr.ArraySize : 1;
        if (sz >= 2)
        {
            var tz = sz >= 3 ? (int)arr.GetElem(2).IntValue : 0;
            _vm.Map.PlaceInWorld(obj, (int)arr.GetElem(0).IntValue, (int)arr.GetElem(1).IntValue, tz);
            _vm.Combat?.AdoptMonster(obj);
        }
        else
        {
            _vm.Map.TakeFromWorld(obj);
            obj.Removed = true;
        }

        return UsecodeValue.FromInt(1);
    }

    /// <summary>Exult <c>UI_give_last_created(container)</c>: pops only when the add succeeds.</summary>
    UsecodeValue GiveLastCreated(UsecodeValue[] p)
    {
        var cont = _vm.GetItem(p[0]);
        if (cont is null || _vm.LastCreated.Count == 0)
        {
            return Zero();
        }

        var obj = _vm.LastCreated[^1];
        var ok = obj.Container is null && obj.Removed && Quantities.AddTo(cont, obj);
        if (ok)
        {
            _vm.LastCreated.RemoveAt(_vm.LastCreated.Count - 1);
        }

        return UsecodeValue.FromInt(ok ? 1 : 0);
    }

    /// <summary>Exult <c>UI_input_numeric_value(min, max, step, default)</c>: waits for the slider.</summary>
    UsecodeValue InputNumericValue(UsecodeValue[] p)
    {
        var min = (int)p[0].IntValue;
        var max = (int)p[1].IntValue;
        var step = Math.Max(1, (int)p[2].IntValue);
        var def = Math.Clamp((int)p[3].IntValue, Math.Min(min, max), Math.Max(min, max));
        _vm.NumericPrompt = (min, max, step, def);
        _vm.RequestWait(UsecodeWait.NumericInput);
        return UsecodeValue.FromInt(def);
    }

    /// <summary>Exult <c>UI_earthquake(len)</c>: shake the view len times, 100 ms apart.</summary>
    UsecodeValue Earthquake(UsecodeValue[] p)
    {
        _vm.QuakeSteps = Math.Max(_vm.QuakeSteps, (int)p[0].IntValue);
        return Zero();
    }

    /// <summary>Exult <c>UI_get_timer(n)</c>: hours since set_timer, or a random 0–12 if never set.</summary>
    UsecodeValue GetTimer(UsecodeValue[] p)
    {
        var tnum = (int)p[0].IntValue;
        if (_vm.Timers.TryGetValue(tnum, out var set) && set > 0)
        {
            return UsecodeValue.FromInt((_vm.Clock?.TotalHours ?? 0) - set);
        }

        return UsecodeValue.FromInt(_vm.Random(13) - 1);
    }

    UsecodeValue SetTimer(UsecodeValue[] p)
    {
        _vm.Timers[(int)p[0].IntValue] = _vm.Clock?.TotalHours ?? 0;
        return Zero();
    }

    /// <summary>Exult <c>UI_wearing_fellowship</c>: Fellowship medallion (shape 955 frame 1) on the avatar's neck.</summary>
    UsecodeValue WearingFellowship()
    {
        var obj = U7.Actors.Equipment.GetReadied(_vm.Avatar, U7.Actors.ReadySpot.Neck);
        return UsecodeValue.FromInt(obj is { Shape: 955 } && (obj.Frame & 31) == 1 ? 1 : 0);
    }

    /// <summary>Exult <c>UI_reset_conv_face</c>: first face back to frame 0.</summary>
    UsecodeValue ResetConvFace()
    {
        _vm.Conv.ChangeFaceFrame(0, 0);
        _vm.NotifyFaces();
        return Zero();
    }

    UsecodeValue GetContItems(UsecodeValue[] p)
    {
        var obj = _vm.GetItem(p[0]);
        if (obj is null)
        {
            return UsecodeValue.FromArray(0);
        }

        var shape = (int)p[1].IntValue;
        var qual = p.Length > 2 ? (int)p[2].IntValue : U7Constants.AnyShape;
        var frame = p.Length > 3 ? (int)p[3].IntValue : U7Constants.AnyShape;
        var found = new List<U7Object>();
        CollectMatching(obj, shape, qual, frame, found);
        var arr = UsecodeValue.FromArray(found.Count);
        for (var i = 0; i < found.Count; i++)
        {
            arr.PutElem(i, UsecodeValue.FromObject(found[i]));
        }

        return arr;
    }

    static void CollectMatching(U7Object container, int shape, int qual, int frame, List<U7Object> dest)
    {
        foreach (var child in container.Contents)
        {
            if ((shape == U7Constants.AnyShape || child.Shape == shape) &&
                (frame == U7Constants.AnyShape || (child.Frame & 31) == frame) &&
                (qual == U7Constants.AnyShape || child.Quality == qual))
            {
                dest.Add(child);
            }

            CollectMatching(child, shape, qual, frame, dest);
        }
    }

    UsecodeValue RemoveItem(UsecodeValue[] p)
    {
        var obj = _vm.GetItem(p[0]);
        if (obj is not null)
        {
            _vm.Map.RemoveObject(obj);
        }

        return Zero();
    }

    UsecodeValue GetItemFlag(UsecodeValue[] p)
    {
        var obj = _vm.GetItem(p[0]);
        if (obj is null)
        {
            return Zero();
        }

        var fnum = (int)p[1].IntValue;
        if (fnum == 24) // is_solid
        {
            return UsecodeValue.FromInt(obj.Solid ? 1 : 0);
        }

        return UsecodeValue.FromInt(obj.GetFlag(fnum) ? 1 : 0);
    }

    UsecodeValue SetItemFlag(UsecodeValue[] p)
    {
        var obj = _vm.GetItem(p[0]);
        obj?.SetFlag((int)p[1].IntValue);
        return Zero();
    }

    UsecodeValue ClearItemFlag(UsecodeValue[] p)
    {
        var obj = _vm.GetItem(p[0]);
        obj?.ClearFlag((int)p[1].IntValue);
        return Zero();
    }

    UsecodeValue AOrAn(UsecodeValue[] p)
    {
        var s = p[0].StrValue ?? "";
        if (s.Length == 0)
        {
            return UsecodeValue.FromString("a");
        }

        var c = char.ToLowerInvariant(s[0]);
        return UsecodeValue.FromString("aeiou".Contains(c) ? "an" : "a");
    }
}
