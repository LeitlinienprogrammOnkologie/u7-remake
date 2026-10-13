using Godot;
using U7.Actors;
using U7.Core;
using U7.Data;
using U7.UI;

namespace U7.Usecode;

/// <summary>
/// Black Gate intrinsic dispatch (Exult <c>bgintrinsics.h</c> + <c>intrinsics.cc</c>).
/// Every intrinsic gets 12 parameters, those not passed 0 (Exult's default
/// <c>Usecode_value</c>); one without a port logs and returns 0.
/// </summary>
public sealed partial class BgIntrinsics
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
        "get_dead_party", "view_tile", "telekenesis", "a_or_an",
        // Exult bgintrinsics.h past the originals: its own and Serpent Isle's, for mods.
        "UNKNOWN", "UNKNOWN", "UNKNOWN", "UNKNOWN", "UNKNOWN", "UNKNOWN", "UNKNOWN", "UNKNOWN",
        "UNKNOWN", "approach_avatar", "get_attack_mode", "center_view", "get_body_npc",
        "get_npc_id", "set_npc_id", "get_oppressor", "get_readied", "get_weapon", "modify_schedule",
        "printf", "remove_cont_items", "remove_from_area", "revert_schedule", "run_schedule",
        "set_barge_dir", "set_intercept_item", "set_new_schedules", "set_opponent",
        "si_path_run_usecode", "begin_casting_mode", "get_usecode_fun", "get_map_num",
        "create_new_object2", "add_cont_items", "clear_item_say", "save_pos",
        "teleport_to_saved_pos", "set_camera", "set_polymorph", "infravision", "display_map_ex",
        "book_mode_ex", "show_npc_face0", "show_npc_face1", "remove_npc_face0", "remove_npc_face1",
        "set_conversation_slot", "is_dest_reachable", "set_npc_name", "set_usecode_fun",
        "get_item_weight", "remove_all_spells", "get_skin_colour", "change_npc_face0",
        "change_npc_face1", "get_temperature", "set_temperature", "get_temperature_zone",
        "get_npc_warmth", "has_spell", "remove_spell", "ambient_light", "create_barge_object",
        "in_usecode_path", "fade_palette_sleep", "close_gumps2", "close_gump2", "game_day"
    ];

    readonly UsecodeMachine _vm;
    /// <summary>Exult <c>speech_track</c>: the last speech asked for.</summary>

    public BgIntrinsics(UsecodeMachine vm) => _vm = vm;

    /// <summary>The handler's name: Black Gate's table, then Serpent Isle's own (<see cref="SiIntrinsics.SiOnly"/>).</summary>
    public static string Name(int id) =>
        (uint)id < (uint)Names.Length ? Names[id]
        : id - SiIntrinsics.FirstSiOnly is var si && (uint)si < (uint)SiIntrinsics.SiOnly.Length ? SiIntrinsics.SiOnly[si]
        : $"unknown_{id:X2}";

    /// <summary>The handler of a name (its first number in Black Gate's table), 0xff (none) for a name it lacks.</summary>
    public static int IdOf(string name) => Array.IndexOf(Names, name) is var i and >= 0 ? i : 0xff;

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
            0x47 => Summon(p),
            0x48 => DisplayMap(),
            0x4f => DisplayArea(p),
            0x50 => WizardEye(p),
            0x94 => ViewTile(p),
            0x49 => KillNpc(p),
            0x5b => Armageddon(),
            0x56 => StopTime(p),
            0x77 => NapTime(p),
            0x5f => MarkVirtueStone(p),
            0x60 => RecallVirtueStone(p),
            0x54 => AttackObject(p),
            0x63 => SetOrrery(p),
            0x76 => FireProjectile(p),
            0x4a => RollToWin(p),
            0x4b => SetAttackMode(p),
            0x4c => SetOppressor(p),
            0x4d => Clone(p),
            0x5a => UsecodeValue.FromInt(AvatarLook.IsFemale(_vm.Avatar) ? 1 : 0), // is_pc_female
            0x5e => GetArraySize(p),
            0x61 => ApplyDamage(p),
            0x68 => UsecodeValue.FromInt(1), // mouse_exists
            0x6b => GetItemFrameRot(p),
            0x6c => SetItemFrameRot(p),
            0x6e => GetContainer(p),
            0x6f => RemoveItem(p),
            0x62 => IsPcInside(),
            0x69 => UsecodeValue.FromInt(_vm.SpeechTrack), // get_speech_track
            0x70 => Zero(), // UNKNOWN: Exult's does nothing either
            0x71 => ReduceHealth(p),
            0x72 => IsReadied(p),
            0x74 => StartSpeech(p),
            0x78 => AdvanceTime(p),
            0x8f => StartSpeech(p), // start_blocking_speech
            0x90 => IsWater(p),
            0x79 => InUsecode(p),
            0x92 => SetCamera(p),
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
            0x0f => PlaySoundEffect(p),
            0x86 => PlaySoundEffect2(p),
            0x24 => CreateNewObject(p),
            0x25 => SetLastCreated(p),
            0x26 => UpdateLastCreated(p),
            0x2b => RemovePartyItems(p),
            0x2c => AddPartyItems(p),
            0x36 => GiveLastCreated(p),
            0x59 => Earthquake(p),
            0x53 => SpriteEffect(p),
            0x57 => CauseLight(p),
            0x82 => SetLight(),
            0x84 => SetTimePalette(),
            0x5d => Lightning(),
            0x44 => UsecodeValue.FromInt(_vm.Effects?.GetWeather() ?? 0),
            0x45 => SetWeather(p),
            0x41 => SetToAttack(p),
            0x95 => Telekenesis(p),
            0x29 => FindObject(p),
            0x58 => UsecodeValue.FromObject(_vm.GetItem(p[0]) is { } bargeOf ? _vm.Barges?.GetBarge(bargeOf)?.Obj : null),
            0x6d => OnBarge(),
            0x46 => SitDown(p),
            0x85 => IsNotBlocked(p),
            0x7d => PathRunUsecode(p, n),
            0x8b => SetPathFailure(p),
            0x52 => AddSpell(p),
            0x7b => ObjSpriteEffect(p),
            0x65 => GetTimer(p),
            0x66 => SetTimer(p),
            0x67 => WearingFellowship(),
            0x91 => ResetConvFace(),
            0x55 => BookMode(p),
            0x6a => FlashMouse(p),
            0x75 => RunEndgame(p),
            0x7a => CallGuards(),
            0x7c => AttackAvatar(),
            0xa0 => ApproachAvatar(p),
            0xa2 => CenterView(p),
            0xa4 => GetNpcId(p),
            0xa5 => SetNpcId(p),
            0xa9 => ModifySchedule(p),
            0xac => RemoveFromArea(p),
            0xad => RevertSchedule(p),
            0xae => RunSchedule(p),
            0xb0 => SetInterceptItem(p),
            0xb1 => SetNewSchedules(p),
            0xb5 => GetUsecodeFun(p),
            0xba => SavePos(p),
            0xbb => TeleportToSavedPos(p),
            0xbc => SetCamera(p),
            0xc1 => ShowNpcFace(p, 0),
            0xc2 => ShowNpcFace(p, 1),
            0xc3 => RemoveSlotFace(0),
            0xc4 => RemoveSlotFace(1),
            0xc5 => SetConversationSlot(p),
            0xc7 => SetNpcName(p),
            0xc8 => SetUsecodeFun(p),
            0xcc => ChangeNpcFace(p, 0),
            0xcd => ChangeNpcFace(p, 1),
            0xd7 => FadePaletteSleep(p),
            0xda => UsecodeValue.FromInt(_vm.Clock?.Day ?? 0),
            0x2d => GetMusicTrack(),
            0x34 or 0xaa => ErrorMessage(p, n),
            0xa1 => GetAttackMode(p),
            0xa3 => GetBodyNpc(p),
            0xa6 => GetOppressor(p),
            0xa7 => GetReadied(p),
            0xa8 => GetWeapon(p),
            0xab => RemoveContItems(p),
            0xaf => SetBargeDir(p),
            0xb2 => SetOpponent(p),
            0xb3 => SiPathRunUsecode(p),
            0xb4 => BeginCastingMode(p, n),
            0xb6 => GetMapNum(p),
            0xb7 => CreateNewObject2(p),
            0xb8 => AddContItems(p),
            0xb9 => ClearItemSay(p),
            0xbd => SetPolymorph(p),
            0xbe => Infravision(p),
            0xbf => DisplayMapEx(p),
            0xc0 => BookModeEx(p, n),
            0xc6 => IsDestReachableIntrinsic(p),
            0xc9 => GetItemWeight(p),
            0xca => RemoveAllSpells(p),
            0xcb => GetSkinColour(),
            0xce => GetTemperature(p),
            0xcf => SetTemperature(p),
            0xd0 => GetTemperatureZone(p),
            0xd1 => GetNpcWarmth(p),
            0xd2 => HasSpell(p),
            0xd3 => RemoveSpell(p),
            0xd4 => AmbientLight(p),
            0xd5 => CreateBargeObject(p, n),
            0xd6 => InUsecodePath(p),
            0xd8 => CloseGumps2(),
            0xd9 => CloseGump2(p),
            >= SiIntrinsics.FirstSiOnly => CallSiOnly(id, p, n),
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

    /// <summary>Exult <c>show_npc_face</c>; <paramref name="slot"/> 0 or 1 for SI's <c>show_npc_face0/1</c>, -1 the free one.</summary>
    UsecodeValue ShowNpcFace(UsecodeValue[] p, int slot = -1)
    {
        // Exult: the text said so far shows first, under the face that said it.
        _vm.ShowPendingText();
        var frame = (int)p[1].IntValue;
        var item = _vm.GetItem(p[0]);
        var shape = FaceShape(p[0], item);
        if (shape < 0)
        {
            return Zero();
        }

        (shape, frame) = AvatarFace(shape, frame, item);

        // The panel names a speaker the player already knows (Exult sets met here,
        // so a first meeting stays nameless until the next conversation).
        var name = item is { NpcNum: >= 0 } && (item.NpcNum == 0 || item.GetFlag(U7.Actors.ObjFlag.Met))
            ? item.NpcName
            : "";
        // Exult: in Black Gate only (Serpent Isle's usecode sets it itself).
        if (GameContext.IsBg && item is { NpcNum: >= 0 })
        {
            item.SetFlag(U7.Actors.ObjFlag.Met);
        }

        _vm.AfterText(() =>
        {
            _vm.Conv.ShowFace(shape, frame, name, slot);
            _vm.NotifyFaces();
        });
        return Zero();
    }

    /// <summary>Exult <c>remove_npc_face</c>: the pending text is shown (with the face) first.</summary>
    UsecodeValue RemoveNpcFace(UsecodeValue[] p)
    {
        _vm.ShowPendingText();
        var item = _vm.GetItem(p[0]);
        var (shape, _) = AvatarFace(FaceShape(p[0], item), 0, item);
        if (shape >= 0)
        {
            _vm.AfterText(() =>
            {
                _vm.Conv.RemoveFace(shape);
                _vm.NotifyFaces();
            });
        }

        return Zero();
    }

    /// <summary>
    /// Exult <c>get_face_shape</c>'s special case: face 0 is the avatar's,
    /// its skin's face (by the sex of the NPC given, else the avatar's).
    /// </summary>
    (int Shape, int Frame) AvatarFace(int shape, int frame, U7Object? item) =>
        shape == AvatarLook.FaceShape
            ? AvatarLook.Face(_vm.Avatar, AvatarLook.IsFemale(item is { IsActor: true } ? item : _vm.Avatar))
            : (shape, frame);

    /// <summary>
    /// Exult <c>Usecode_internal::get_face_shape</c>: the speaker's face shape
    /// (0 the avatar's, -1 none), Petra's swap applied; in Serpent Isle the
    /// Nightmare Smith (NPC 277 calling with the Guardian's face, 296) has
    /// face 277, which is never drawn ("but don't mess up Guardian").
    /// </summary>
    int FaceShape(UsecodeValue arg, U7Object? item)
    {
        var shape = SpeakerFace(arg, item);
        if (shape < 0)
        {
            return shape;
        }

        shape = AvatarLook.FaceReplacement(_vm.Avatar, shape);
        return GameContext.IsSi && shape == 296 && _vm.CurrentFrame?.Caller is { IsActor: true, NpcNum: 277 } ? 277 : shape;
    }

    int SpeakerFace(UsecodeValue arg, U7Object? item)
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

        _vm.Map.SetShape(item, (int)p[1].IntValue);
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

    /// <summary>Exult <c>UI_set_item_frame</c>: the frame, keeping the reflection.</summary>
    UsecodeValue SetItemFrame(UsecodeValue[] p)
    {
        _vm.SetItemFrame(_vm.GetItem(p[0]), (int)p[1].IntValue);
        return Zero();
    }

    UsecodeValue GetItemFrameRot(UsecodeValue[] p)
    {
        var item = _vm.GetItem(p[0]);
        return UsecodeValue.FromInt(item?.Frame ?? 0);
    }

    /// <summary>Exult <c>UI_set_item_frame_rot</c>: the whole frame, reflection included.</summary>
    UsecodeValue SetItemFrameRot(UsecodeValue[] p)
    {
        _vm.SetItemFrame(_vm.GetItem(p[0]), (int)p[1].IntValue, setRotated: true);
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

    /// <summary>
    /// Exult <c>UI_get_object_position</c>: the tile of the outermost object, so a
    /// carried item reports where its carrier stands, not its spot in the gump.
    /// </summary>
    UsecodeValue GetObjectPosition(UsecodeValue[] p)
    {
        var obj = _vm.GetItem(p[0]);
        var arr = UsecodeValue.FromArray(3);
        if (obj is not null)
        {
            var top = Inventory.Outermost(obj);
            arr.PutElem(0, UsecodeValue.FromInt(top.Tx));
            arr.PutElem(1, UsecodeValue.FromInt(top.Ty));
            arr.PutElem(2, UsecodeValue.FromInt(top.Tz));
        }

        return arr;
    }

    /// <summary>Exult <c>UI_get_distance</c>: <c>Game_object::distance</c> between the outermost objects.</summary>
    UsecodeValue GetDistance(UsecodeValue[] p)
    {
        var a = _vm.GetItem(p[0]);
        var b = _vm.GetItem(p[1]);
        if (a is null || b is null)
        {
            return Zero();
        }

        return UsecodeValue.FromInt(ObjectGeometry.Distance(Inventory.Outermost(a), Inventory.Outermost(b)));
    }

    /// <summary>
    /// Exult <c>Usecode_internal::find_direction</c> (<c>find_direction</c>,
    /// <c>direction_from</c>): 0 north, clockwise, from one place to the other,
    /// "treated as cartesian": <c>Get_direction(t1.ty - t2.ty, t2.tx - t1.tx)</c>.
    /// </summary>
    UsecodeValue FindDirection(UsecodeValue[] p)
    {
        var from = PositionOf(p[0]);
        var to = PositionOf(p[1]);
        return UsecodeValue.FromInt(Directions.Of(from.Ty - to.Ty, to.Tx - from.Tx));
    }

    /// <summary>
    /// Exult <c>Usecode_internal::get_position</c>: an object (its outermost
    /// container's tile), three coordinates, or <c>click_on_item</c>'s
    /// (object, x, y, z) read as the clicked tile; else the caller item.
    /// </summary>
    TileCoord PositionOf(UsecodeValue v)
    {
        var size = v.ArraySize;
        if (size <= 1 && _vm.GetItem(v) is { } obj)
        {
            var top = Inventory.Outermost(obj);
            return new TileCoord(top.Tx, top.Ty, top.Tz);
        }

        if (size == 3)
        {
            return new TileCoord((int)v.GetElem(0).IntValue, (int)v.GetElem(1).IntValue, (int)v.GetElem(2).IntValue);
        }

        if (size == 4)
        {
            return new TileCoord((int)v.GetElem(1).IntValue, (int)v.GetElem(2).IntValue, (int)v.GetElem(3).IntValue);
        }

        var c = _vm.CurrentFrame?.Caller;
        return c is null ? default : new TileCoord(c.Tx, c.Ty, c.Tz);
    }

    UsecodeValue GetAvatarRef() => UsecodeValue.FromObject(_vm.Avatar);

    /// <summary>Exult <c>UI_execute_usecode_array</c> / <c>UI_delayed_execute_usecode_array</c>.</summary>
    UsecodeValue ExecuteUsecodeArray(UsecodeValue[] p, bool delayed)
    {
        var obj = _vm.GetItem(p[0]);
        // Exult create_script's "pure kludge for SI wells": the script goes to the well's top.
        if (GameContext.IsSi && p[0] is { IsArray: true, ArraySize: 2 } wells && obj is { Shape: 470, Tz: 0 } &&
            _vm.GetItem(wells.GetElem(1)) is { Shape: 470, Tz: 2 } top)
        {
            obj = top;
        }
        var code = p[1];
        var ticks = 1;
        if (delayed)
        {
            // Exult: BG infinite-loop guard for internal_exec + [.., .., 0x6f7].
            if (GameContext.IsBg && _vm.CurrentFrame?.EventId == (int)UsecodeEvent.InternalExec && code.ArraySize == 3 &&
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

            // A number that is no NPC: no notes.
            var who = p[1];
            if (who.IsInt && (who.IntValue >= 0 || (who.IntValue != -356 && who.IntValue < -_vm.Npcs.Count)))
            {
                return Zero();
            }

            // The notes rising from the item (the instrument played).
            if (_vm.GetItem(p[1]) is { Removed: false, Container: null } obj)
            {
                _vm.Effects?.AddSprite(24, obj, 0, 0, -2, -2);
            }
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

    /// <summary>
    /// Exult <c>UI_fade_palette(cycles, 1, inout)</c>: 0 fades to black, 1
    /// back from it, in cycles + 1 steps 20 ms apart (<c>Palette::fade</c>),
    /// while the usecode waits. A fade-in first resets the palette to the
    /// hour's (<c>reset_palette</c>). Exult's <c>show_pending_text</c> before a
    /// fade-out finds nothing here (said text is clicked through as it is
    /// said, and no fade of Black Gate's follows book text), and its
    /// <c>toggle_ambient_light(false)</c> clears what only Exult's own
    /// <c>ambient_light</c> intrinsic sets.
    /// </summary>
    UsecodeValue FadePalette(UsecodeValue[] p)
    {
        var cycles = (int)p[0].IntValue;
        var fadeIn = p[2].IntValue != 0;
        if (fadeIn)
        {
            _vm.Lighting?.ResetPalette();
        }

        _vm.StartFade(cycles, fadeIn);
        return Zero();
    }

    /// <summary>
    /// Exult <c>UI_get_dead_party(obj)</c>: an element per member on the
    /// dead-party list, in its order, its body if that lies within 50 tiles
    /// of the object (Exult's guess), else 0.
    /// </summary>
    UsecodeValue GetDeadParty(UsecodeValue[] p)
    {
        if (_vm.GetItem(p[0]) is not { } obj || _vm.Party is not { } party)
        {
            return Zero();
        }

        var dead = party.DeadMembers;
        var near = _vm.Map.FindNearby(new TileCoord(obj.Tx, obj.Ty, obj.Tz), U7Constants.AnyShape, 49);
        var ret = UsecodeValue.FromArray(dead.Count, Zero());
        for (var i = 0; i < dead.Count; i++)
        {
            if (near.FirstOrDefault(o => o.LiveNpcNum == dead[i].NpcNum && o.LiveNpcNum > 0) is { } body)
            {
                ret.PutElem(i, UsecodeValue.FromObject(body));
            }
        }

        return ret;
    }

    /// <summary>Exult <c>Usecode_internal::get_party</c>: avatar first, then the members.</summary>
    UsecodeValue GetPartyList() => UsecodeValue.FromObjects(PartyObjects());

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
            return UsecodeValue.FromObjects(Enumerable.Range(0, p[0].ArraySize).Select(i => _vm.GetItem(p[0].GetElem(i))));
        }

        return UsecodeValue.FromObject(_vm.GetItem(p[0]));
    }

    UsecodeValue GetScheduleType(UsecodeValue[] p)
    {
        var npc = _vm.GetItem(p[0]);
        if (npc is not { IsActor: true })
        {
            return Zero();
        }

        // Exult: in Serpent Isle an NPC on a usecode path gives the walk-to schedule ("a bug in the Fawn Trial").
        if (GameContext.IsSi && _vm.Schedules?.BrainOf(npc)?.CurrentAction is IfElsePathAction)
        {
            return UsecodeValue.FromInt(U7.Actors.ScheduleType.WalkToSchedule);
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

            // Exult: taking the avatar out of combat ends combat mode (a bribed guard's 0x625 does).
            if (npc == _vm.Avatar && _vm.Combat is { InCombat: true } combat && type != ScheduleType.Combat)
            {
                _vm.Music?.Stop();
                combat.SetInCombat(false);
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

        U7.Actors.ActorProp.Set(item, prop, delta);
        if (prop == U7.Actors.ActorProp.SexFlag)
        {
            // Exult set_type_flag / clear_type_flag: set_actor_shape.
            AvatarLook.SetActorShape(_vm.Map, item);
        }

        return UsecodeValue.FromInt(1);
    }

    /// <summary>Exult <c>UI_get_npc_name</c>: names of an object or an array of them (the missing left out).</summary>
    UsecodeValue GetNpcName(UsecodeValue[] p)
    {
        if (p[0].IsArray)
        {
            var names = new List<UsecodeValue>();
            for (var i = 0; i < p[0].ArraySize; i++)
            {
                if (_vm.GetItem(p[0].GetElem(i)) is { } item)
                {
                    names.Add(UsecodeValue.FromString(NpcNameOf(item)));
                }
            }

            var arr = UsecodeValue.FromArray(names.Count);
            for (var i = 0; i < names.Count; i++)
            {
                arr.PutElem(i, names[i]);
            }

            return arr;
        }

        return UsecodeValue.FromString(_vm.GetItem(p[0]) is { } one ? NpcNameOf(one) : UnknownName);
    }

    const string UnknownName = "??name??";

    /// <summary>
    /// Exult <c>get_npc_name</c>'s name: an actor's own (Exult
    /// <c>get_npc_name</c>; the avatar, who is given no name at a new game,
    /// is "Avatar"), or its shape's; a thing's as a click shows it
    /// (<c>get_name</c>).
    /// </summary>
    string NpcNameOf(U7Object item)
    {
        if (!item.IsActor)
        {
            return ObjectNames.OfThing(item, _vm.Catalog);
        }

        return (item.NpcNum == 0 || item == _vm.Avatar) && item.NpcName.Length == 0
            ? "Avatar"
            : ObjectNames.NpcName(item, _vm.Catalog);
    }

    /// <summary>Exult <c>UI_is_npc</c>: any actor, monsters included.</summary>
    UsecodeValue IsNpc(UsecodeValue[] p)
    {
        var item = _vm.GetItem(p[0]);
        return UsecodeValue.FromInt(item is { IsActor: true } ? 1 : 0);
    }

    UsecodeValue IsDead(UsecodeValue[] p)
    {
        var npc = _vm.GetItem(p[0]);
        return UsecodeValue.FromInt(npc is { IsDead: true } ? 1 : 0);
    }

    static UsecodeValue RollToWin(UsecodeValue[] p) =>
        UsecodeValue.FromInt(U7.Actors.CombatEngine.RollToWin((int)p[0].IntValue, (int)p[1].IntValue) ? 1 : 0);

    /// <summary>
    /// Exult <c>UI_display_area(pos)</c> (the crystal ball, the orrery
    /// viewer): the world round the tile, seen through SPRITES.VGA sprite 10,
    /// until a click. A fourth element would be Exult's map number; Black Gate
    /// has the one map.
    /// </summary>
    UsecodeValue DisplayArea(UsecodeValue[] p)
    {
        if (!p[0].IsArray || p[0].ArraySize < 3)
        {
            return Zero();
        }

        var area = new TileCoord((int)p[0].GetElem(0).IntValue, (int)p[0].GetElem(1).IntValue, 0);
        _vm.ShowPicture(new UsecodePicture(EyeSprite, 0, null, area));
        return Zero();
    }

    /// <summary>SPRITES.VGA's eye: the frame Exult paints over a view elsewhere (the original 320x200 screen).</summary>
    const int EyeSprite = 10;

    /// <summary>
    /// Exult <c>UI_wizard_eye(ticks, ?)</c>: the player looks about from the
    /// avatar's tile while the game runs, for half again the ticks (Exult's
    /// 3 x std_delay / 2 ms each: the spell's 45 are 13.5 s, the telescope's
    /// 10000 50 minutes), or until Esc.
    /// </summary>
    UsecodeValue WizardEye(UsecodeValue[] p)
    {
        if (_vm.Avatar is { } av)
        {
            _vm.StartWizardEye((int)p[0].IntValue * 3 * U7Constants.StandardDelayMs / 2, new TileCoord(av.Tx, av.Ty, av.Tz), EyeSprite);
        }

        return Zero();
    }

    /// <summary>Exult <c>UI_view_tile(pos)</c>: the view centres on the tile (until the avatar moves or a view elsewhere ends).</summary>
    UsecodeValue ViewTile(UsecodeValue[] p)
    {
        if (p[0].IsArray && p[0].ArraySize >= 2)
        {
            _vm.ViewTile?.Invoke(new TileCoord((int)p[0].GetElem(0).IntValue, (int)p[0].GetElem(1).IntValue, 0));
        }

        return Zero();
    }

    /// <summary>Exult <c>UI_summon(shape, ?)</c>: the monster summoned for the caller, or 0.</summary>
    UsecodeValue Summon(UsecodeValue[] p) =>
        _vm.Combat?.Summon((int)p[0].IntValue, _vm.CurrentFrame?.Caller) is { } monster ? UsecodeValue.FromObject(monster) : Zero();

    /// <summary>Exult <c>UI_clone(npc)</c>: the NPC's fighting double, or 0.</summary>
    UsecodeValue Clone(UsecodeValue[] p) =>
        _vm.GetItem(p[0]) is { IsActor: true } npc && _vm.Combat?.Clone(npc) is { } clone ? UsecodeValue.FromObject(clone) : Zero();

    /// <summary>Exult <c>UI_attack_object(attacker, target, weapon)</c>: <c>Combat_schedule::attack_target</c> outside combat (a powder keg set off).</summary>
    UsecodeValue AttackObject(UsecodeValue[] p) =>
        _vm.GetItem(p[0]) is { } att && _vm.GetItem(p[1]) is { } trg && _vm.Combat is { } combat
            ? UsecodeValue.FromInt(combat.AttackTarget(att, trg, (int)p[2].IntValue, combat: false) ? 1 : 0)
            : Zero();

    /// <summary>Exult <c>UI_fire_projectile(attacker, dir, sprite, attval, weapon, ammo)</c>: the cannon's shot.</summary>
    UsecodeValue FireProjectile(UsecodeValue[] p)
    {
        if (_vm.GetItem(p[0]) is { } attacker)
        {
            _vm.Combat?.FireProjectile(attacker, (int)p[1].IntValue, (int)p[2].IntValue, (int)p[3].IntValue,
                (int)p[4].IntValue, (int)p[5].IntValue);
        }

        return Zero();
    }

    const int PlanetBritannia = 765, Planets = 988;

    /// <summary>
    /// Exult <c>UI_set_orrery</c>'s table (after Marzo Sette Torres Junior's
    /// Planets.txt): for each of the ten states, where the eight planets
    /// (frames 0-7) stand from the orrery's centre.
    /// </summary>
    static readonly (int Dx, int Dy)[][] OrreryOffsets =
    [
        [(2, -3), (3, -3), (1, -6), (6, -2), (7, -1), (8, 1), (-4, 8), (9, -2)],
        [(3, -1), (4, -1), (-5, -3), (3, 6), (7, 2), (4, 7), (-8, 4), (8, 5)],
        [(3, 1), (3, 2), (-3, 4), (-5, 4), (2, 7), (-2, 8), (-9, 1), (2, 9)],
        [(1, 3), (1, 4), (4, 3), (-5, -3), (-4, 6), (-7, 4), (-9, -1), (-4, 9)],
        [(-2, 3), (-2, 4), (5, -2), (5, -4), (-7, 2), (-8, 1), (-8, -4), (-8, 6)],
        [(-4, 1), (-5, 1), (-5, -3), (6, 3), (-7, -2), (-7, -4), (-7, -6), (-10, 1)],
        [(-4, 9), (-5, -1), (-3, 4), (-3, 6), (-6, -4), (-5, -6), (-7, -6), (-10, -2)],
        [(-4, 2), (-4, -3), (4, 3), (-6, 1), (-5, -5), (-3, -7), (-4, -8), (-8, -6)],
        [(-3, -3), (-3, -4), (5, -2), (-3, -5), (-1, -7), (0, -8), (-1, -9), (-5, -9)],
        [(0, -4), (0, -5), (1, -6), (1, -6), (1, -7), (1, -8), (1, -9), (-1, -10)]
    ];

    /// <summary>
    /// Exult <c>UI_set_orrery(pos, state)</c>: with planet Britannia (765)
    /// within 24 tiles of the centre, the planets round it (988 frames 0-7,
    /// the sun stays) are taken away and set down at the state's places.
    /// </summary>
    UsecodeValue SetOrrery(UsecodeValue[] p)
    {
        var pos = new TileCoord((int)p[0].GetElem(0).IntValue, (int)p[0].GetElem(1).IntValue, (int)p[0].GetElem(2).IntValue);
        var state = (int)p[1].IntValue;
        var brit = _vm.Map.FindNearbyExult(pos, PlanetBritannia, 24, 0xb0)
            .OrderBy(o => Math.Max(Math.Abs(o.Tx - pos.Tx), Math.Abs(o.Ty - pos.Ty)))
            .FirstOrDefault();
        if (brit is null || state is < 0 or > 9)
        {
            return Zero();
        }

        foreach (var planet in _vm.Map.FindNearbyExult(new TileCoord(brit.Tx, brit.Ty, brit.Tz), Planets, 24, 0))
        {
            if (planet.Frame <= 7)
            {
                _vm.Map.RemoveObject(planet);
            }
        }

        for (var frame = 0; frame <= 7; frame++)
        {
            var (dx, dy) = OrreryOffsets[state][frame];
            _vm.Map.PlaceInWorld(Quantities.NewItem(Planets, frame), pos.Tx + dx, pos.Ty + dy, pos.Tz);
        }

        return Zero();
    }

    /// <summary>Exult <c>UI_mark_virtue_stone(stone)</c>: the stone remembers where it is (its outermost container's tile).</summary>
    UsecodeValue MarkVirtueStone(UsecodeValue[] p)
    {
        if (_vm.GetItem(p[0]) is { } stone && _vm.Catalog[stone.Shape].IsVirtueStoneClass)
        {
            var outer = Inventory.Outermost(stone);
            stone.VirtueTarget = new TileCoord(outer.Tx, outer.Ty, outer.Tz);
            stone.VirtueMap = 0;
        }

        return Zero();
    }

    /// <summary>
    /// Exult <c>UI_recall_virtue_stone(stone)</c>: the gumps close, a stone
    /// lying in the world goes to the first of the party with room (else onto
    /// the avatar regardless), and a marked stone takes the party to its place
    /// (<c>Game_window::teleport_party</c>).
    /// </summary>
    UsecodeValue RecallVirtueStone(UsecodeValue[] p)
    {
        if (_vm.GetItem(p[0]) is not { } stone || !_vm.Catalog[stone.Shape].IsVirtueStoneClass || _vm.Avatar is not { } av)
        {
            return Zero();
        }

        _vm.Gumps?.CloseAll();
        if (stone.Container is null && !PartyObjects().Any(m => Equipment.AddToActor(m, stone, _vm.Catalog, _vm.Map)))
        {
            Equipment.TryPlace(_vm.Map, stone, av, 255, 255, _vm.Catalog, checkLimits: false);
        }

        var t = stone.VirtueTarget;
        if (t.Tx > 0 || t.Ty > 0)
        {
            _vm.TeleportParty?.Invoke(t);
        }

        return Zero();
    }

    /// <summary>
    /// Exult <c>UI_nap_time(bed)</c>: if someone else lies in the bed, a
    /// party member (or the avatar, alone) says so and the avatar follows
    /// again; otherwise the avatar goes to lie in it (<c>set_bed</c>), and
    /// the bed's sleep usecode runs once it does.
    /// </summary>
    UsecodeValue NapTime(UsecodeValue[] p)
    {
        if (_vm.GetItem(p[0]) is not { } bed || _vm.Avatar is not { } av || _vm.Schedules is not { } schedules)
        {
            return Zero();
        }

        if (SleepSchedule.IsBedOccupied(_vm.Map, bed, av))
        {
            // Exult shows the face and the message without waiting; here the panel waits for a click.
            var members = _vm.Party?.Members ?? [];
            var npcnum = members.Count > 0 ? members[_vm.Random(members.Count) - 1].NpcNum : 356;
            ShowNpcFace([UsecodeValue.FromInt(-npcnum), UsecodeValue.FromInt(0)]);
            _vm.ShowText(TextMessages.Random(TextMessages.FirstBedOccupied, TextMessages.LastBedOccupied));
            _vm.RequestWait(UsecodeWait.ClickToContinue);
            schedules.SetScheduleType(av, ScheduleType.FollowAvatar);
            return Zero();
        }

        schedules.NapIn(bed);
        return Zero();
    }

    /// <summary>Exult <c>UI_stop_time(n)</c>: time stands still for n quarter seconds.</summary>
    UsecodeValue StopTime(UsecodeValue[] p)
    {
        _vm.Clock?.StopTime((int)p[0].IntValue * 250);
        return Zero();
    }

    /// <summary>Exult <c>Armageddon_death</c>'s cries.</summary>
    static readonly string[] ArmageddonCries = ["Aiiiieee!", "Noooo!", "#!?*#%!"];

    /// <summary>
    /// Exult <c>UI_armageddon</c>: every NPC but the avatar, and the monsters
    /// within 40 tiles of it, lie down dead where they stand, all but Lord
    /// British and Batlin; the NPCs on the screen cry out. From then on no
    /// monster egg hatches (Exult's <c>armageddon</c> flag).
    /// </summary>
    UsecodeValue Armageddon()
    {
        var view = _vm.Combat?.ViewTiles ?? default;
        for (var i = 1; i < _vm.Npcs.Count; i++)
        {
            ArmageddonDeath(_vm.Npcs[i], view, barks: true);
        }

        if (_vm.Avatar is { } av)
        {
            foreach (var act in _vm.Map.FindNearbyExult(new TileCoord(av.Tx, av.Ty, av.Tz), U7Constants.AnyShape, 40, 0x28))
            {
                if (act is { IsActor: true, IsMonster: true })
                {
                    ArmageddonDeath(act, view, barks: false);
                }
            }
        }

        if (_vm.Combat is { } combat)
        {
            combat.Armageddon = true;
        }

        return Zero();
    }

    /// <summary>
    /// Exult <c>Armageddon_death</c>: no body and no <c>Actor::die</c>; the
    /// actor lies down (<c>lay_down</c>'s kneel and sleep frames) with the dead
    /// flag, unresponsive, its health at -health/3 - 1.
    /// </summary>
    void ArmageddonDeath(U7Object? npc, Godot.Rect2I view, bool barks)
    {
        if (npc is not { Unused: false } || npc.IsDead || U7.Actors.ActorFlags.SurvivesArmageddon(npc.Shape))
        {
            return;
        }

        if (barks && view.HasPoint(new Godot.Vector2I(npc.Tx, npc.Ty)))
        {
            _vm.Bark(npc, ArmageddonCries[_vm.Random(ArmageddonCries.Length) - 1]);
        }

        var layDown = UsecodeValue.FromArray(6);
        int[] ops = [ScriptFinish, ScriptStandFrame, ScriptKneelFrame, ScriptSfx, LayDownSfx, ScriptSleepFrame];
        for (var i = 0; i < ops.Length; i++)
        {
            layDown.PutElem(i, UsecodeValue.FromInt(ops[i]));
        }

        _vm.StartScript(npc, layDown, 0);
        npc.SetProp(ActorProp.Health, -npc.GetProp(ActorProp.Health) / 3 - 1);
        npc.SetFlag(ObjFlag.Dead);
    }

    /// <summary>Exult <c>Ucscript</c> opcodes for <c>Actor::lay_down</c>, and its sound (game sfx 86).</summary>
    const int ScriptFinish = 0x2c, ScriptSfx = 0x58, ScriptStandFrame = 0x61, ScriptKneelFrame = 0x6d,
        ScriptSleepFrame = 0x6e, LayDownSfx = 86;

    /// <summary>Exult <c>UI_kill_npc(npc)</c>: <c>Actor::die</c> with no attacker.</summary>
    UsecodeValue KillNpc(UsecodeValue[] p)
    {
        if (_vm.GetItem(p[0]) is { IsActor: true } npc)
        {
            _vm.Combat?.Kill(npc);
        }

        return Zero();
    }

    /// <summary>
    /// Exult <c>UI_set_attack_mode(npc, mode)</c>. Not the player's choice, so
    /// starting combat turns a flee set here back to nearest.
    /// </summary>
    UsecodeValue SetAttackMode(UsecodeValue[] p)
    {
        if (_vm.GetItem(p[0]) is { IsActor: true } npc)
        {
            npc.AttackMode = (int)p[1].IntValue;
            npc.UserSetAttack = false;
        }

        return Zero();
    }

    /// <summary>
    /// Exult <c>UI_set_oppressor(npc, opp)</c>. Exult keeps the oppressor's NPC
    /// number, so a monster without one (-1) leaves none.
    /// </summary>
    UsecodeValue SetOppressor(UsecodeValue[] p)
    {
        if (_vm.GetItem(p[0]) is { IsActor: true } npc && _vm.GetItem(p[1]) is { IsActor: true } opp && npc != opp)
        {
            npc.Oppressor = opp.NpcNum >= 0 ? opp : null;
        }

        return Zero();
    }

    /// <summary>Exult <c>UI_is_pc_inside</c>.</summary>
    UsecodeValue IsPcInside() => UsecodeValue.FromInt(AvatarInside() ? 1 : 0);

    /// <summary>Exult <c>is_main_actor_inside</c>: a roof over the avatar.</summary>
    bool AvatarInside() =>
        _vm.Avatar is { } av && _vm.Map.AvatarInside(av);

    /// <summary>Exult <c>sprites/map</c> in Black Gate (bggame.cc).</summary>
    public const int MapSprite = 22;
    /// <summary>The sextant's shape.</summary>
    const int SextantShape = 650;

    /// <summary>
    /// Exult <c>UI_display_map</c>: the map sprite until a click, marking where
    /// the avatar is when the party has a sextant and the avatar is outdoors
    /// (<c>Paint_map</c>; the game stands still while it shows, so the tile is
    /// the one Exult reads when it paints).
    /// </summary>
    UsecodeValue DisplayMap()
    {
        TileCoord? mark = null;
        if (_vm.Avatar is { } av && !AvatarInside() &&
            PartyObjects().Sum(m => Quantities.Count(m, SextantShape, U7Constants.AnyShape, U7Constants.AnyShape)) > 0)
        {
            mark = new TileCoord(av.Tx, av.Ty, av.Tz);
        }

        _vm.ShowPicture(new UsecodePicture(MapSprite, 0, mark));
        return Zero();
    }

    /// <summary>
    /// Exult <c>UI_start_speech(n)</c> and <c>UI_start_blocking_speech(n)</c>:
    /// the track kept for <c>get_speech_track</c> and its voice played; 1 when
    /// it plays, else the faces go and 0 tells the usecode (0x940) to show the
    /// text. With the user's subtitles the answer is 0 while the voice plays,
    /// so the text shows too (Exult's <c>speech_with_subs</c> reaches only
    /// <c>do_speech</c>).
    /// </summary>
    UsecodeValue StartSpeech(UsecodeValue[] p)
    {
        _vm.SpeechTrack = (int)p[0].IntValue;
        var played = _vm.SpeechTrack >= 0 && _vm.Speech?.PlaySpeech(_vm.SpeechTrack) == true;
        if (played && _vm.Speech is { WithSubtitles: false })
        {
            return UsecodeValue.FromInt(1);
        }

        _vm.Conv.InitFaces();
        _vm.NotifyFaces();
        return Zero();
    }

    /// <summary>
    /// Exult <c>UI_advance_time(ticks)</c>: <c>Game_clock::increment</c> by the
    /// whole minutes (25 ticks each), then <c>set_time_palette</c>.
    /// </summary>
    UsecodeValue AdvanceTime(UsecodeValue[] p)
    {
        _vm.Clock?.Increment((int)p[0].IntValue / U7Constants.TicksPerMinute);
        _vm.Lighting?.ResetPalette();
        return Zero();
    }

    /// <summary>
    /// Exult <c>UI_is_water(pos)</c>: whether the ground at the tile is water;
    /// the lift is ignored, as in the original. Exult's own [obj, x, y, z]
    /// form asks the object's shape.
    /// </summary>
    UsecodeValue IsWater(UsecodeValue[] p)
    {
        var size = p[0].IsArray ? p[0].ArraySize : 0;
        if (size is < 2 or > 4)
        {
            return Zero();
        }

        if (size == 4 && _vm.GetItem(p[0].GetElem(0)) is { } obj)
        {
            return UsecodeValue.FromInt(_vm.Catalog[obj.Shape].Water ? 1 : 0);
        }

        var off = size == 4 ? 1 : 0;
        var flat = _vm.Map.GetFlat((int)p[0].GetElem(off).IntValue, (int)p[0].GetElem(off + 1).IntValue);
        return UsecodeValue.FromInt(_vm.Catalog[flat.Shape].Water ? 1 : 0);
    }

    /// <summary>
    /// Exult <c>UI_is_readied(npc, where, shape, frame)</c>: <c>where</c> is a
    /// Black Gate spot (<c>Ready_spot_from_BG</c>, 1 the weapon hand); frame
    /// -359 is any. Spots past the usecode container (both hands, neck) never
    /// match, as in Exult.
    /// </summary>
    UsecodeValue IsReadied(UsecodeValue[] p)
    {
        if (_vm.GetItem(p[0]) is not { IsActor: true } npc)
        {
            return Zero();
        }

        var spot = UsecodeSpot((int)p[1].IntValue);
        var shape = (int)p[2].IntValue;
        var frame = (int)p[3].IntValue;
        var obj = spot <= ReadySpot.Ucont ? Equipment.GetReadied(npc, spot) : null;
        return UsecodeValue.FromInt(obj is not null && obj.Shape == shape &&
                                    (frame == U7Constants.AnyShape || obj.Frame == frame) ? 1 : 0);
    }

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

    /// <summary>Exult <c>UI_get_alignment</c>: the effective one (a charmed actor's charm side).</summary>
    UsecodeValue GetAlignment(UsecodeValue[] p)
    {
        var npc = _vm.GetItem(p[0]);
        return UsecodeValue.FromInt(npc is { IsActor: true } ? npc.EffectiveAlignment : 0);
    }

    /// <summary>
    /// Exult <c>UI_set_alignment</c>: <c>set_effective_alignment</c>; a change
    /// makes those fighting it look again and it seek a new opponent; one
    /// fleeing fights the nearest instead (Exult's fix for fleeing in fields).
    /// </summary>
    UsecodeValue SetAlignment(UsecodeValue[] p)
    {
        if (_vm.GetItem(p[0]) is not { IsActor: true } npc)
        {
            return Zero();
        }

        var align = (int)p[1].IntValue;
        var old = npc.EffectiveAlignment;
        npc.SetEffectiveAlignment(align);
        if (old != align)
        {
            _vm.Combat?.Timers?.StopAttacking(npc);
            npc.CombatTarget = null;
        }

        if (npc.AttackMode == U7.Actors.AttackMode.Flee)
        {
            npc.AttackMode = U7.Actors.AttackMode.Nearest;
        }

        return Zero();
    }

    /// <summary>
    /// Exult <c>UI_display_runes(gump, lines)</c>: a sign, tombstone or plaque
    /// (<see cref="Gumps.SignGump"/>) until a click; an avatar who can read
    /// runes (the read flag) sees the lines in letters.
    /// </summary>
    UsecodeValue DisplayRunes(UsecodeValue[] p)
    {
        var text = p[1];
        var cnt = text.IsArray ? Math.Max(1, text.ArraySize) : 1;
        var lines = new string[cnt];
        for (var i = 0; i < cnt; i++)
        {
            lines[i] = (text.IsArray && text.ArraySize > 0 ? text.GetElem(i) : text).StrValue ?? "";
        }

        _vm.ShowSign(new Gumps.SignGump((int)p[0].IntValue, lines, _vm.Avatar?.GetFlag(ObjFlag.Read) == true));
        return Zero();
    }

    /// <summary>
    /// Exult <c>book_mode(item)</c>: the following says fill a scroll gump
    /// (shape 797) or a book gump instead of the conversation, shown when the
    /// usecode next shows pending text. Exult's serpentine-script shapes 705
    /// and 707 are Serpent Isle's; no Black Gate usecode opens them.
    /// </summary>
    UsecodeValue BookMode(UsecodeValue[] p)
    {
        var obj = _vm.GetItem(p[0]);
        if (obj is null)
        {
            return Zero();
        }

        // Exult: the serpentine scroll (707) and book (705) in serpent runes (font 8) unless the avatar can read them.
        var font = _vm.Avatar?.GetFlag(ObjFlag.Read) == true ? U7Constants.BookFont : 8;
        _vm.SetBook(obj.Shape switch
        {
            707 => new U7.Gumps.ScrollGump(font),
            705 => new U7.Gumps.BookGump(font),
            797 => new U7.Gumps.ScrollGump(),
            _ => new U7.Gumps.BookGump()
        });
        return Zero();
    }

    /// <summary>
    /// Exult <c>UI_click_on_item</c>: an intercepted click answers at once
    /// (the object and its tile, Exult's <c>intercept_item</c>: a spell cast
    /// from a readied spellbook in combat aims at the caster's target); else
    /// it waits for the player's click.
    /// </summary>
    UsecodeValue ClickOnItem()
    {
        if (_vm.InterceptItem is { } obj)
        {
            _vm.InterceptItem = null;
            _vm.InterceptTile = null;
            var arr = UsecodeValue.FromArray(4, UsecodeValue.FromObject(obj));
            arr.PutElem(1, UsecodeValue.FromInt(obj.Tx));
            arr.PutElem(2, UsecodeValue.FromInt(obj.Ty));
            arr.PutElem(3, UsecodeValue.FromInt(obj.Tz));
            return arr;
        }

        if (_vm.InterceptTile is { } t)
        {
            _vm.InterceptTile = null;
            var arr = UsecodeValue.FromArray(4, UsecodeValue.FromObject(null));
            arr.PutElem(1, UsecodeValue.FromInt(t.Tx));
            arr.PutElem(2, UsecodeValue.FromInt(t.Ty));
            arr.PutElem(3, UsecodeValue.FromInt(t.Tz));
            return arr;
        }

        _vm.RequestWait(UsecodeWait.ClickOnItem);
        return Zero();
    }

    /// <summary>Exult <c>UI_npc_nearby(item)</c>: on the screen (the view in tiles) and, an actor, able to act.</summary>
    UsecodeValue NpcNearby(UsecodeValue[] p)
    {
        if (_vm.GetItem(p[0]) is not { } obj)
        {
            return Zero();
        }

        var near = U7.World.Pathfinder.HasTile(_vm.Combat?.ViewTiles ?? default, obj.Tx, obj.Ty) &&
                   (!obj.IsActor || CombatSchedule.CanAct(obj));
        return UsecodeValue.FromInt(near ? 1 : 0);
    }

    /// <summary>Exult <c>UI_find_nearby_avatar(shape)</c>: <c>find_nearby</c> round the avatar, 192 tiles (for the Test of Love's tree), mask 0.</summary>
    UsecodeValue FindNearbyAvatar(UsecodeValue[] p) =>
        NearbyArray(FindNearbyFor(UsecodeValue.FromObject(_vm.Avatar), p[0], 192, 0));

    /// <summary>Exult <c>UI_find_nearby(where, shape, dist, mask)</c>.</summary>
    UsecodeValue FindNearby(UsecodeValue[] p) =>
        NearbyArray(FindNearbyFor(p[0], p[1], (int)p[2].IntValue, (int)p[3].IntValue));

    /// <summary>
    /// Exult <c>Usecode_internal::find_nearby</c>: round a click's result
    /// (object, x, y, z), a position (x, y, z, with quality and frame as a
    /// 4th and 5th), or an object (its outermost container), with Exult's
    /// mask (<see cref="GameMap.FindNearbyExult"/>).
    /// </summary>
    List<U7Object> FindNearbyFor(UsecodeValue where, UsecodeValue shapeval, int dist, int mask)
    {
        var shape = (int)(shapeval.IsArray ? shapeval.GetElem0().IntValue : shapeval.IntValue);
        var size = where.IsArray ? where.ArraySize : 0;
        if (size == 4)
        {
            return _vm.Map.FindNearbyExult(new TileCoord((int)where.GetElem(1).IntValue, (int)where.GetElem(2).IntValue,
                (int)where.GetElem(3).IntValue), shape, dist, mask);
        }

        if (size is 3 or 5)
        {
            return _vm.Map.FindNearbyExult(
                new TileCoord((int)where.GetElem(0).IntValue, (int)where.GetElem(1).IntValue, (int)where.GetElem(2).IntValue),
                shape, dist, mask,
                size == 5 ? (int)where.GetElem(3).IntValue : U7Constants.AnyShape,
                size == 5 ? (int)where.GetElem(4).IntValue : U7Constants.AnyShape);
        }

        if (_vm.GetItem(where) is not { } obj)
        {
            return [];
        }

        var outer = Inventory.Outermost(obj);
        return _vm.Map.FindNearbyExult(new TileCoord(outer.Tx, outer.Ty, outer.Tz), shape, dist, mask);
    }

    static UsecodeValue NearbyArray(List<U7Object> found) => UsecodeValue.FromObjects(found);

    /// <summary>
    /// Exult <c>Usecode_internal::find_nearest</c>: of the shape round the
    /// object's outermost container (Exult's mask 0, NPCs included), the
    /// closest by straight distance; the Test of Courage looks 16 tiles for its mage.
    /// </summary>
    UsecodeValue FindNearest(UsecodeValue[] p)
    {
        var obj = _vm.GetItem(p[0]);
        if (obj is null)
        {
            return UsecodeValue.FromObject(null);
        }

        obj = Inventory.Outermost(obj);
        var shape = (int)p[1].IntValue;
        var dist = (int)p[2].IntValue;
        if (_vm.CurrentFrame?.Function.Id == 0x70a && shape == 0x9a && dist == 0)
        {
            dist = 16; // Exult: the mage may have wandered.
        }

        var found = _vm.Map.FindNearbyExult(new TileCoord(obj.Tx, obj.Ty, obj.Tz), shape, dist, 0);
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
            // Exult move_object: the whole party (teleport_party).
            if (_vm.TeleportParty is { } teleport)
            {
                teleport(new TileCoord(tx, ty, tz));
            }
            else
            {
                _vm.Map.MoveObject(_vm.Avatar, tx, ty, tz);
            }

            return Zero();
        }

        if (_vm.GetItem(p[0]) is not { } obj)
        {
            return Zero();
        }

        var (fromTx, fromTy) = (obj.Tx, obj.Ty);
        _vm.Map.MoveObject(obj, tx, ty, tz);
        if (obj == _vm.Avatar)
        {
            _vm.AvatarTeleported?.Invoke(fromTx, fromTy);
        }
        else if (obj.IsActor)
        {
            _vm.Schedules?.ClearAction(obj);
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
        var qual = (int)p[2].IntValue;
        var frame = (int)p[3].IntValue;
        var oval = p[0].IsPtr ? 0 : (int)p[0].IntValue;
        if (oval == -357)
        {
            return UsecodeValue.FromInt(PartyObjects().Sum(m => Quantities.Count(m, shape, qual, frame)));
        }

        var obj = _vm.GetItem(p[0]);
        return UsecodeValue.FromInt(obj is null ? 0 : Quantities.Count(obj, shape, qual, frame));
    }

    /// <summary>
    /// Exult <c>UI_find_object(where, shape, qual, frame)</c>: the first match
    /// within a tile of a position, on the screen (-359), in the party (-357)
    /// or inside an object; null if none.
    /// </summary>
    UsecodeValue FindObject(UsecodeValue[] p)
    {
        var shape = (int)p[1].IntValue;
        var qual = (int)p[2].IntValue;
        var frame = (int)p[3].IntValue;
        if (p[0].ArraySize == 3)
        {
            var tile = new TileCoord((int)p[0].GetElem(0).IntValue, (int)p[0].GetElem(1).IntValue, (int)p[0].GetElem(2).IntValue);
            return FirstOrNull(_vm.Map.FindNearby(tile, shape, 1, 0, qual, frame));
        }

        var oval = (int)p[0].IntValue;
        if (oval == U7Constants.AnyShape)
        {
            // The game window, centred on the avatar.
            var h = _vm.Schedules?.ScreenTiles.H ?? 25;
            var av = _vm.Avatar;
            return FirstOrNull(_vm.Map.FindNearby(new TileCoord(av.Tx, av.Ty, 0), shape, h / 2, 0, qual, frame));
        }

        if (oval != -357)
        {
            var owner = _vm.GetItem(p[0]);
            return UsecodeValue.FromObject(owner is null ? null : U7.Actors.ItemQuantity.FindItem(owner, shape, qual, frame));
        }

        foreach (var member in PartyObjects())
        {
            if (U7.Actors.ItemQuantity.FindItem(member, shape, qual, frame) is { } found)
            {
                return UsecodeValue.FromObject(found);
            }
        }

        return UsecodeValue.FromObject(null);
    }

    /// <summary>
    /// Exult <c>UI_is_not_blocked(tile, shape, frame)</c>: whether the shape
    /// would stand at the tile, neither blocked nor rising or falling
    /// (<c>Map_chunk::is_blocked</c> over its footprint, for walkers and swimmers).
    /// </summary>
    UsecodeValue IsNotBlocked(UsecodeValue[] p)
    {
        if (p[0].ArraySize < 3)
        {
            return Zero();
        }

        var tile = new TileCoord((int)p[0].GetElem(0).IntValue, (int)p[0].GetElem(1).IntValue, (int)p[0].GetElem(2).IntValue);
        var info = _vm.Catalog[(int)p[1].IntValue];
        var reflected = ((int)p[2].IntValue & 32) != 0;
        var xtiles = reflected ? info.DimY : info.DimX;
        var ytiles = reflected ? info.DimX : info.DimY;
        var blocked = _vm.Map.Blocking.IsBlockedArea(
            info.DimZ, tile.Tz, tile.Tx - xtiles + 1, tile.Ty - ytiles + 1, xtiles, ytiles,
            out var newLift, MoveFlags.Walk | MoveFlags.Swim);
        return UsecodeValue.FromInt(!blocked && newLift == tile.Tz ? 1 : 0);
    }

    /// <summary>Exult <c>path_npc</c>: the walker of the last <c>path_run_usecode</c>, for <c>set_path_failure</c>.</summary>
    U7Object? _pathNpc;

    /// <summary>
    /// Exult <c>UI_path_run_usecode(loc, fun, item, event[, simode])</c>: the
    /// avatar walks there with the party and then runs the function on the
    /// item; in Serpent Isle (or asked) to a free spot near it; 0 if there is
    /// no way there.
    /// </summary>
    UsecodeValue PathRunUsecode(UsecodeValue[] p, int n)
    {
        var simode = n > 4 ? p[4].IntValue != 0 : GameContext.IsSi;
        return UsecodeValue.FromInt(PathRunUsecode(_vm.Avatar, p[0], (int)p[1].GetElem0().IntValue, _vm.GetItem(p[2]),
            (int)p[3].IntValue, simode, always: false, companions: true) ? 1 : 0);
    }

    /// <summary>
    /// Exult <c>Usecode_internal::path_run_usecode</c>: the NPC walks to the
    /// place (with <paramref name="findFree"/> a free spot near it, rising up
    /// to 3, else at its own lift) and runs the function on the item there,
    /// or only walks without an item; the companions come along if asked; with
    /// <paramref name="always"/> a failed walk runs it too, with event 14.
    /// False if there is no way there.
    /// </summary>
    bool PathRunUsecode(U7Object npc, UsecodeValue loc, int fun, U7Object? obj, int eventId, bool findFree, bool always,
        bool companions)
    {
        _pathNpc = npc;
        var size = loc.ArraySize;
        if (size < 2 || _vm.Schedules is not { } runner)
        {
            return false;
        }

        var src = new TileCoord(npc.Tx, npc.Ty, npc.Tz);
        var dest = new TileCoord((int)loc.GetElem(0).IntValue, (int)loc.GetElem(1).IntValue,
            size == 3 ? Math.Max(0, (int)loc.GetElem(2).IntValue) : 0);
        if (findFree)
        {
            // "Now works with SI lightning platform": a rise of 3 allowed.
            if ((_vm.Map.FindSpot(dest, 3, npc, 3) ?? _vm.Map.FindSpot(dest with { Tz = src.Tz }, 3, npc)) is { } free)
            {
                dest = free;
            }

            // Exult's kludge for the Iron Maiden loop in Serpent Isle.
            if (fun == 0x60a && ObjectGeometry.Distance(npc, dest) <= 1)
            {
                return true;
            }
        }

        IActorAction? action = obj is null
            ? PathWalk.Astar(_vm.Map, npc, dest)
            : new IfElsePathAction(_vm.Map, npc, dest, new UsecodeAction(fun, obj, eventId),
                always ? new UsecodeAction(fun, obj, SiPathFailEvent) : null);
        // Exult set_get_party: the companions come along, and are called after the last step.
        switch (action)
        {
            case null:
                return false;
            case PathWalk walk when companions:
                walk.GetParty = true;
                walk.LastStep = runner.AvatarWalkEnded;
                break;
            case IfElsePathAction ifElse when companions:
                ifElse.GetParty = true;
                ifElse.LastStep = runner.AvatarWalkEnded;
                break;
        }

        runner.StartAction(npc, action, U7Constants.StandardDelayMs, 0);
        return action is not IfElsePathAction { DoneAndFailed: true };
    }

    /// <summary>Exult <c>UI_set_path_failure(fun, item, event)</c>: what the last <c>path_run_usecode</c> walker does if it cannot get there.</summary>
    UsecodeValue SetPathFailure(UsecodeValue[] p)
    {
        var item = _vm.GetItem(p[1]);
        if (_pathNpc is { } npc && item is not null && _vm.Schedules?.BrainOf(npc)?.CurrentAction is IfElsePathAction action)
        {
            action.SetFailure(new UsecodeAction((int)p[0].IntValue, item, (int)p[2].IntValue));
        }

        return Zero();
    }

    static UsecodeValue FirstOrNull(List<U7Object> found) => UsecodeValue.FromObject(found.Count > 0 ? found[0] : null);

    /// <summary>
    /// Exult <c>UI_add_spell(spell, ?, spellbook)</c> (<c>Spellbook_object::add_spell</c>):
    /// 1 if the spell (circle * 8 + number) was added, 0 if the book had it or is no spellbook.
    /// </summary>
    UsecodeValue AddSpell(UsecodeValue[] p)
    {
        var book = _vm.GetItem(p[2]);
        var spell = (int)p[0].IntValue;
        if (book is null || !_vm.Catalog[book.Shape].IsSpellbookClass || spell is < 0 or >= 72)
        {
            return Zero();
        }

        book.SpellCircles ??= new byte[9];
        var bit = (byte)(1 << (spell % 8));
        if ((book.SpellCircles[spell / 8] & bit) != 0)
        {
            return Zero();
        }

        book.SpellCircles[spell / 8] |= bit;
        return UsecodeValue.FromInt(1);
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
            quantity = Quantities.Add(member, quantity, shape, qual, frame, dontCreate: false, GameContext.IsBg && temp);
            if (quantity < prev)
            {
                got.Add(member);
            }
        }

        var result = UsecodeValue.FromObjects(got);
        if (GameContext.IsBg)
        {
            return result;
        }

        // Exult for Serpent Isle: the rest is put on the ground by the avatar, and the count appended.
        var todo = quantity;
        frame = frame == U7Constants.AnyShape ? 0 : frame;
        while (todo > 0 && _vm.Map.FindSpot(new TileCoord(_vm.Avatar.Tx, _vm.Avatar.Ty, _vm.Avatar.Tz), 3, shape, frame, 2) is { } pos)
        {
            var obj = Quantities.NewItem(shape, frame);
            if (qual != U7Constants.AnyShape)
            {
                obj.Quality = qual;
            }

            obj.SetFlag(U7.Actors.ObjFlag.OkayToTake);
            if (temp)
            {
                obj.SetFlag(U7.Actors.ObjFlag.Temporary);
            }

            _vm.Map.PlaceInWorld(obj, pos.Tx, pos.Ty, pos.Tz);
            todo--;
            if (todo > 0)
            {
                todo = Quantities.Modify(obj, todo, out _);
            }
        }

        result.Concat(UsecodeValue.FromInt(quantity - todo));
        return result;
    }

    /// <summary>
    /// Exult <c>Usecode_internal::create_object</c>: a new object outside the
    /// world, pushed on last_created. Monster shapes become neutral monsters in
    /// the wait schedule.
    /// </summary>
    UsecodeValue CreateNewObject(UsecodeValue[] p) => UsecodeValue.FromObject(CreateObject((int)p[0].IntValue, equip: false));

    /// <summary>Exult <c>Usecode_internal::create_object</c>: <paramref name="equip"/> gives a monster its equipment.</summary>
    U7Object CreateObject(int shape, bool equip)
    {
        U7Object obj;
        if (_vm.Combat is { } combat && (combat.IsMonsterShape(shape) || _vm.Catalog[shape].IsNpcClass))
        {
            obj = combat.CreateMonster(shape, 0, U7.Actors.ScheduleType.Wait, U7.Actors.Alignment.Neutral, equip);
            obj.Alignment = U7.Actors.Alignment.Neutral;
        }
        else
        {
            obj = Quantities.NewItem(shape, 0);
            obj.SetFlag(U7.Actors.ObjFlag.OkayToTake);
        }

        _vm.LastCreated.Add(obj);
        return obj;
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
        // Exult adds without checking the volume ("Causes failures"): the marker scrolls
        // egg usecode 0x6A3 gives its monsters (Dracothraxus) find no free spot otherwise.
        var ok = obj.Container is null && obj.Removed && Quantities.AddTo(cont, obj, dontCheck: true);
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

    /// <summary>Exult <c>UI_cause_light(units)</c>: a light spell (<c>add_special_light</c>).</summary>
    UsecodeValue CauseLight(UsecodeValue[] p)
    {
        _vm.Clock?.AddSpecialLight((int)p[0].IntValue);
        return Zero();
    }

    /// <summary>
    /// Exult <c>UI_set_light(light, onoff)</c>: <c>refigure_gear</c> for the
    /// actor holding the light, less the light if it is going out (a torch's
    /// usecode calls it before changing the torch's shape). Nothing to do here:
    /// <see cref="U7.Rendering.SceneLighting"/> recounts every actor's readied
    /// lights each frame (<see cref="U7.World.LightSources.CarriedLight(U7Object, ShapeCatalog)"/>).
    /// </summary>
    static UsecodeValue SetLight() => Zero();

    /// <summary>Exult <c>UI_set_time_palette</c>: <c>Game_clock::reset_palette</c>, the palette set at once.</summary>
    UsecodeValue SetTimePalette()
    {
        _vm.Lighting?.ResetPalette();
        return Zero();
    }

    /// <summary>Exult <c>UI_lightning</c>: a flash of lightning, and more now and then.</summary>
    UsecodeValue Lightning()
    {
        _vm.Effects?.AddUsecodeLightning();
        return Zero();
    }

    /// <summary>Exult <c>UI_set_weather(n)</c>: <c>Egg_object::set_weather</c> for the standard 15 minutes.</summary>
    UsecodeValue SetWeather(UsecodeValue[] p)
    {
        _vm.Effects?.SetWeather((int)p[0].IntValue);
        return Zero();
    }

    /// <summary>
    /// Exult <c>UI_set_to_attack(from, to, weapon)</c>: what the actor's next
    /// script 'attack' hits, an object or the tile of a <c>click_on_item</c>;
    /// 0 if the shape is no weapon.
    /// </summary>
    UsecodeValue SetToAttack(UsecodeValue[] p)
    {
        var from = _vm.GetItem(p[0]);
        var shape = (int)p[2].IntValue;
        if (from is not { IsActor: true } || shape < 0 || _vm.Combat?.Weapons[shape] is null)
        {
            return Zero();
        }

        from.AttackWeapon = shape;
        if (_vm.GetItem(p[1]) is { } to)
        {
            from.AttackTargetObj = to;
            from.AttackTargetTile = null;
            return UsecodeValue.FromInt(1);
        }

        var size = p[1].IsArray ? p[1].ArraySize : 0;
        if (size >= 3)
        {
            from.AttackTargetObj = null;
            // Exult Tile_coord::fixme: wrapped round the world.
            from.AttackTargetTile = new TileCoord(U7Constants.WrapTile((int)p[1].GetElem(1).IntValue),
                U7Constants.WrapTile((int)p[1].GetElem(2).IntValue), size >= 4 ? (int)p[1].GetElem(3).IntValue : 0);
            return UsecodeValue.FromInt(1);
        }

        return Zero();
    }

    /// <summary>Exult <c>UI_telekenesis(fun)</c>: a script's next call of the function runs as a double-click.</summary>
    UsecodeValue Telekenesis(UsecodeValue[] p)
    {
        _vm.TelekenesisFun = (int)p[0].IntValue;
        return Zero();
    }

    /// <summary>Exult <c>UI_sprite_effect(sprite, tx, ty, dx, dy, frame, reps)</c>: a SPRITES.VGA animation at a tile.</summary>
    UsecodeValue SpriteEffect(UsecodeValue[] p)
    {
        _vm.Effects?.AddSprite(
            (int)p[0].IntValue, new TileCoord((int)p[1].IntValue, (int)p[2].IntValue, 0),
            (int)p[3].IntValue, (int)p[4].IntValue, 0, (int)p[5].IntValue, (int)p[6].IntValue);
        return Zero();
    }

    /// <summary>Exult <c>UI_obj_sprite_effect(obj, sprite, -xoff, -yoff, dx, dy, frame, reps)</c>: one following the object.</summary>
    UsecodeValue ObjSpriteEffect(UsecodeValue[] p)
    {
        if (_vm.GetItem(p[0]) is { } obj)
        {
            _vm.Effects?.AddSprite(
                (int)p[1].IntValue, obj, -(int)p[2].IntValue, -(int)p[3].IntValue,
                (int)p[4].IntValue, (int)p[5].IntValue, (int)p[6].IntValue, (int)p[7].IntValue);
        }

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

    /// <summary>
    /// Exult <c>UI_flash_mouse(code)</c>: 2 out of range, 3 out of ammo, 4 too
    /// heavy, 5 won't fit, 7 blocked, anything else the red X.
    /// </summary>
    UsecodeValue FlashMouse(UsecodeValue[] p)
    {
        _vm.Flash((int)p[0].IntValue switch
        {
            2 => MouseShape.OutOfRange,
            3 => MouseShape.OutOfAmmo,
            4 => MouseShape.TooHeavy,
            5 => MouseShape.WontFit,
            7 => MouseShape.Blocked,
            _ => MouseShape.RedX
        });
        return Zero();
    }

    /// <summary>
    /// Exult <c>UI_run_endgame(success)</c>: the endgame (the Black Gate
    /// destroyed, or the avatar through it), then the game is over.
    /// </summary>
    UsecodeValue RunEndgame(UsecodeValue[] p)
    {
        _vm.RunEndgame?.Invoke(p[0].IntValue != 0);
        return Zero();
    }

    /// <summary>Exult <c>UI_call_guards</c>: guards come to arrest the avatar (<c>Game_window::call_guards</c>).</summary>
    UsecodeValue CallGuards()
    {
        _vm.Combat?.Guards?.CallGuards();
        return Zero();
    }

    /// <summary>Exult <c>UI_attack_avatar</c>: nearby guards and neutral residents attack (<c>Game_window::attack_avatar</c>).</summary>
    UsecodeValue AttackAvatar()
    {
        _vm.Combat?.Guards?.AttackAvatar();
        return Zero();
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
            return UsecodeValue.FromObject(null); // Exult: a null object, not an empty array (get_array_size says 1).
        }

        var shape = (int)p[1].IntValue;
        var found = new List<U7Object>();
        CollectMatching(obj, shape, (int)p[2].IntValue, (int)p[3].IntValue, found);
        return UsecodeValue.FromObjects(found);
    }

    /// <summary>Exult <c>Container_game_object::get_objects</c>: the matches, depth first.</summary>
    static void CollectMatching(U7Object container, int shape, int qual, int frame, List<U7Object> dest)
    {
        foreach (var child in container.Contents)
        {
            if (ItemQuantity.Matches(child, shape, qual, frame))
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
            // Exult's fix for the broken trap switch in Serpent Isle's Temple of Discipline: the
            // trap's eggs (shape 0xc8 within 15 tiles) go too, so it stays disarmed.
            if (GameContext.IsSi && _vm.CurrentFrame?.Function.Id == 0x70e && obj is { Shape: 0x113, Quality: 0xd })
            {
                foreach (var egg in _vm.Map.EggsNear(obj.Tx, obj.Ty, 15).Where(e => e.Shape == 0xc8).ToList())
                {
                    _vm.Map.RemoveObject(egg);
                }
            }

            _vm.Map.RemoveObject(obj);
        }

        return Zero();
    }

    /// <summary>
    /// Exult <c>set_camera(obj)</c>: the view follows an actor (the avatar
    /// again, after the Ferryman's crossing), or centres on another object.
    /// </summary>
    UsecodeValue SetCamera(UsecodeValue[] p)
    {
        if (_vm.GetItem(p[0]) is { } obj)
        {
            _vm.SetCamera?.Invoke(obj);
        }

        return Zero();
    }

    /// <summary>
    /// Exult <c>approach_avatar(npc)</c> (<c>Actor::approach_another</c>): an
    /// NPC within 10 tiles is there already; else it walks up fast from where
    /// it is, or from off the screen. Exult also holds the game up to 2/5 s
    /// for its arrival (<c>Wait_for_arrival</c>); not here.
    /// </summary>
    UsecodeValue ApproachAvatar(UsecodeValue[] p)
    {
        if (_vm.GetItem(p[0]) is not { IsActor: true } actor || actor.IsDead)
        {
            return Zero();
        }

        if (ObjectGeometry.Distance(actor, _vm.Avatar) < 10)
        {
            return UsecodeValue.FromInt(1);
        }

        return UsecodeValue.FromInt(_vm.Schedules?.ApproachAnother(actor, _vm.Avatar) == true ? 1 : 0);
    }

    /// <summary>Exult <c>center_view(item)</c>: the view centred on the item's tile (an actor's too, without following it).</summary>
    UsecodeValue CenterView(UsecodeValue[] p)
    {
        if (_vm.GetItem(p[0]) is { } obj)
        {
            _vm.CenterView?.Invoke(new TileCoord(obj.Tx, obj.Ty, obj.Tz));
        }

        return Zero();
    }

    UsecodeValue GetNpcId(UsecodeValue[] p) =>
        UsecodeValue.FromInt(_vm.GetItem(p[0]) is { IsActor: true } actor ? actor.Ident : 0);

    /// <summary>Exult <c>set_npc_id(npc, id)</c>: a byte usecode keeps on the actor (saved as 5 bits).</summary>
    UsecodeValue SetNpcId(UsecodeValue[] p)
    {
        if (_vm.GetItem(p[0]) is { IsActor: true } actor)
        {
            actor.Ident = (byte)p[1].IntValue;
        }

        return Zero();
    }

    /// <summary>The NPC whose schedules usecode changes: Exult's <c>Npc_actor</c>s (the avatar keeps none).</summary>
    U7Object? ScheduledNpc(UsecodeValue arg) =>
        _vm.GetItem(arg) is { IsActor: true, NpcNum: > 0 } npc ? npc : null;

    /// <summary>Exult <c>modify_schedule(npc, time, schedule, [x, y])</c>: one time slot's schedule and spot.</summary>
    UsecodeValue ModifySchedule(UsecodeValue[] p)
    {
        if (ScheduledNpc(p[0]) is { } npc && _vm.Schedules is { } runner)
        {
            var time = (int)p[1].IntValue;
            runner.Table.SetTimeType(npc.NpcNum, time, (int)p[2].IntValue);
            runner.Table.SetTimeLocation(npc.NpcNum, time, (int)p[3].GetElem(0).IntValue, (int)p[3].GetElem(1).IntValue);
        }

        return Zero();
    }

    /// <summary>
    /// Exult <c>set_new_schedules(npc, time, schedule, [x, y])</c>, or with
    /// arrays of times and schedules and a flat array of spots: the NPC's
    /// whole list replaced.
    /// </summary>
    UsecodeValue SetNewSchedules(UsecodeValue[] p)
    {
        if (ScheduledNpc(p[0]) is not { } npc || _vm.Schedules is not { } runner)
        {
            return Zero();
        }

        var list = new List<(int, int, int, int)>();
        if (!p[1].IsArray)
        {
            list.Add(((int)p[1].IntValue, (int)p[2].IntValue, (int)p[3].GetElem(0).IntValue, (int)p[3].GetElem(1).IntValue));
        }
        else
        {
            for (var i = 0; i < p[1].ArraySize; i++)
            {
                list.Add(((int)p[1].GetElem(i).IntValue, (int)p[2].GetElem(i).IntValue,
                    (int)p[3].GetElem(i * 2).IntValue, (int)p[3].GetElem(i * 2 + 1).IntValue));
            }
        }

        runner.Table.SetSchedules(npc.NpcNum, list);
        return Zero();
    }

    /// <summary>Exult <c>revert_schedule(npc)</c>: the NPC's schedules from <c>STATIC/SCHEDULE.DAT</c> again.</summary>
    UsecodeValue RevertSchedule(UsecodeValue[] p)
    {
        if (ScheduledNpc(p[0]) is { } npc)
        {
            _vm.Schedules?.Table.Revert(npc.NpcNum);
        }

        return Zero();
    }

    /// <summary>Exult <c>run_schedule(npc)</c>: back to the schedule of the hour.</summary>
    UsecodeValue RunSchedule(UsecodeValue[] p)
    {
        if (_vm.GetItem(p[0]) is { IsActor: true } npc)
        {
            _vm.Schedules?.RunSchedule(npc);
        }

        return Zero();
    }

    /// <summary>Exult <c>remove_from_area(shape, frame, [x, y], [x2, y2])</c>: every such thing standing in the rectangle goes.</summary>
    UsecodeValue RemoveFromArea(UsecodeValue[] p)
    {
        var shape = (int)p[0].IntValue;
        var frame = (int)p[1].IntValue;
        var x0 = (int)p[2].GetElem(0).IntValue;
        var y0 = (int)p[2].GetElem(1).IntValue;
        var x1 = (int)p[3].GetElem(0).IntValue;
        var y1 = (int)p[3].GetElem(1).IntValue;
        if (x1 < x0 || y1 < y0)
        {
            return Zero();
        }

        var found = new List<U7Object>();
        for (var cx = x0 / U7Constants.TilesPerChunk; cx <= x1 / U7Constants.TilesPerChunk; cx++)
        {
            for (var cy = y0 / U7Constants.TilesPerChunk; cy <= y1 / U7Constants.TilesPerChunk; cy++)
            {
                found.AddRange(_vm.Map.ObjectsInChunk(cx, cy).Where(o =>
                    o.Shape == shape && o.Frame == frame && o.Tx >= x0 && o.Tx <= x1 && o.Ty >= y0 && o.Ty <= y1));
            }
        }

        foreach (var obj in found)
        {
            _vm.Map.RemoveObject(obj);
        }

        return Zero();
    }

    /// <summary>Exult <c>get_usecode_fun(item)</c>: the function it runs (an actor's assigned or own, else its shape's).</summary>
    UsecodeValue GetUsecodeFun(UsecodeValue[] p)
    {
        if (_vm.GetItem(p[0]) is not { } obj)
        {
            return Zero();
        }

        var fun = obj.IsActor || obj.IsEgg ? obj.GetUsecode() : -1;
        return UsecodeValue.FromInt(fun >= 0 ? fun : _vm.GetItemFun(obj));
    }

    /// <summary>
    /// Exult <c>set_usecode_fun(item, fun)</c>: an actor (or a usecode egg)
    /// runs that function; an actor keeps its name from UCC's table, which a
    /// save writes instead of the number; a negative one clears it.
    /// </summary>
    UsecodeValue SetUsecodeFun(UsecodeValue[] p)
    {
        if (_vm.GetItem(p[0]) is not { } obj)
        {
            return Zero();
        }

        var fun = (int)p[1].IntValue;
        if (obj.IsActor)
        {
            obj.AssignedUsecode = fun < 0 ? -1 : fun;
            obj.UsecodeName = fun < 0 ? null : _vm.File.Symbols?.Find(fun)?.Name;
        }
        else if (obj.IsEgg && obj.EggType == U7.World.EggType.Usecode)
        {
            obj.EggData2 = fun;
        }

        return Zero();
    }

    /// <summary>
    /// Exult <c>set_intercept_item(item or [x, y(, z)] or [obj, x, y, z])</c>:
    /// the next <c>click_on_item</c> answers with it at once; anything else,
    /// the avatar's tile.
    /// </summary>
    UsecodeValue SetInterceptItem(UsecodeValue[] p)
    {
        if (_vm.GetItem(p[0]) is { } obj)
        {
            _vm.InterceptItem = obj;
            _vm.InterceptTile = null;
            return Zero();
        }

        var size = p[0].ArraySize;
        var off = size == 4 ? 1 : 0;
        _vm.InterceptTile = size is >= 2 and <= 4
            ? new TileCoord((int)p[0].GetElem(off).IntValue, (int)p[0].GetElem(1 + off).IntValue,
                size >= 3 ? (int)p[0].GetElem(2 + off).IntValue : 0)
            : new TileCoord(_vm.Avatar.Tx, _vm.Avatar.Ty, _vm.Avatar.Tz);
        return Zero();
    }

    /// <summary>Exult <c>save_pos(item)</c>: where <c>teleport_to_saved_pos</c> goes back to, kept in <c>usecode.dat</c>.</summary>
    UsecodeValue SavePos(UsecodeValue[] p)
    {
        if (_vm.GetItem(p[0]) is { } obj)
        {
            _vm.SavedPos = new TileCoord(obj.Tx, obj.Ty, obj.Tz);
        }

        return Zero();
    }

    /// <summary>Exult <c>teleport_to_saved_pos(avatar)</c>: the party back to the saved spot (a bad one, Exult's Monitor fix for old games).</summary>
    UsecodeValue TeleportToSavedPos(UsecodeValue[] p)
    {
        if (_vm.GetItem(p[0]) == _vm.Avatar)
        {
            if (_vm.SavedPos.Tx is < 0 or >= U7Constants.NumTiles)
            {
                _vm.SavedPos = new TileCoord(719, 2608, 1);
            }

            _vm.TeleportParty?.Invoke(_vm.SavedPos);
        }

        return Zero();
    }

    /// <summary>Exult <c>set_npc_name(npc, name)</c>.</summary>
    UsecodeValue SetNpcName(UsecodeValue[] p)
    {
        if (_vm.GetItem(p[0]) is { IsActor: true } npc)
        {
            npc.NpcName = p[1].StrValue ?? "";
        }

        return Zero();
    }

    /// <summary>Exult <c>remove_npc_face0/1</c>: the pending text first, then that slot's face goes.</summary>
    UsecodeValue RemoveSlotFace(int slot)
    {
        _vm.ShowPendingText();
        _vm.AfterText(() =>
        {
            _vm.Conv.RemoveSlotFace(slot);
            _vm.NotifyFaces();
        });
        return Zero();
    }

    /// <summary>Exult <c>change_npc_face0/1(frame)</c>: the pending text first, then that slot's face in another frame, speaking next.</summary>
    UsecodeValue ChangeNpcFace(UsecodeValue[] p, int slot)
    {
        _vm.ShowPendingText();
        var frame = (int)p[0].IntValue;
        _vm.AfterText(() =>
        {
            _vm.Conv.ChangeFaceFrame(frame, slot);
            _vm.NotifyFaces();
        });
        return Zero();
    }

    /// <summary>Exult <c>set_conversation_slot(0 or 1)</c>: which face speaks next.</summary>
    UsecodeValue SetConversationSlot(UsecodeValue[] p)
    {
        var slot = (int)p[0].IntValue;
        _vm.AfterText(() => _vm.Conv.SetSlot(slot));
        return Zero();
    }

    /// <summary>
    /// Exult <c>fade_palette_sleep(cycles, 1, in_out)</c>: a fade with the
    /// sleep music (track 33 going dark, after the text said so far; 31
    /// waking, the hour's palette again).
    /// </summary>
    UsecodeValue FadePaletteSleep(UsecodeValue[] p)
    {
        var cycles = (int)p[0].IntValue;
        var fadeIn = p[2].IntValue != 0;
        if (!fadeIn)
        {
            _vm.ShowPendingText();
            _vm.Music?.Start(33, false);
        }
        else
        {
            _vm.Music?.Start(31, false);
            _vm.Lighting?.ResetPalette();
        }

        _vm.StartFade(cycles, fadeIn);
        return Zero();
    }

    /// <summary>Exult <c>in_usecode(item)</c>: whether a script is running on the item.</summary>
    UsecodeValue InUsecode(UsecodeValue[] p) =>
        UsecodeValue.FromInt(_vm.GetItem(p[0]) is { } obj && _vm.HasScript(obj) ? 1 : 0);

    /// <summary>Exult <c>UI_play_sound_effect</c>: the effect, wherever the player is.</summary>
    UsecodeValue PlaySoundEffect(UsecodeValue[] p)
    {
        if (p.Length > 0)
        {
            _vm.Sfx?.Play((int)p[0].IntValue);
        }

        return Zero();
    }

    /// <summary>Exult <c>UI_play_sound_effect2</c>: the effect at an object (<c>Object_sfx::Play</c>).</summary>
    UsecodeValue PlaySoundEffect2(UsecodeValue[] p)
    {
        if (p.Length > 1 && _vm.GetItem(p[1]) is { } obj)
        {
            _vm.Sfx?.PlayAt((int)p[0].IntValue, obj);
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
        if (IsMovingBargeFlag(fnum))
        {
            // Exult: whether the barge it is on (or is) is the one in barge mode.
            var moving = _vm.Barges?.Moving;
            return UsecodeValue.FromInt(moving is not null && _vm.Barges!.GetBarge(obj) == moving ? 1 : 0);
        }

        if (fnum == U7.Actors.ObjFlag.OkayToLand)
        {
            return UsecodeValue.FromInt(_vm.Barges?.GetBarge(obj)?.OkayToLand() == true ? 1 : 0);
        }

        if (fnum == 24) // is_solid
        {
            return UsecodeValue.FromInt(obj.Solid ? 1 : 0);
        }

        if (fnum == U7.Actors.ObjFlag.ActiveSailor)
        {
            // Exult: the sailor itself, as the Ferryman's usecode checks.
            return UsecodeValue.FromObject(_sailor);
        }

        return UsecodeValue.FromInt(obj.GetFlag(fnum) ? 1 : 0);
    }

    /// <summary>Exult <c>sailor</c>: the barge's current captain (the Ferryman or the sails).</summary>
    U7Object? _sailor;

    /// <summary>Exult <c>Is_moving_barge_flag</c>: in Serpent Isle also its own flag 17 (the ice raft needs 10 too).</summary>
    static bool IsMovingBargeFlag(int fnum) =>
        fnum is U7.Actors.ObjFlag.OnMovingBarge or U7.Actors.ObjFlag.ActiveBarge || (GameContext.IsSi && fnum == SiOnMovingBarge);

    /// <summary>Exult <c>si_on_moving_barge</c>.</summary>
    const int SiOnMovingBarge = 17;

    UsecodeValue SetItemFlag(UsecodeValue[] p)
    {
        var obj = _vm.GetItem(p[0]);
        var flag = (int)p[1].IntValue;
        if (obj is null)
        {
            return Zero();
        }

        if (flag == U7.Actors.ObjFlag.ActiveSailor)
        {
            _sailor = obj;
            return Zero();
        }

        if (_vm.ActorFlags is { } timers)
        {
            timers.SetFlag(obj, flag);
        }
        else
        {
            obj.SetFlag(flag);
        }

        if (IsMovingBargeFlag(flag) && _vm.Barges is { } barges && barges.GetBarge(obj) is { } barge)
        {
            // Set the barge in motion.
            barges.SetMoving(barge, _vm.Avatar);
        }

        return Zero();
    }

    UsecodeValue ClearItemFlag(UsecodeValue[] p)
    {
        var obj = _vm.GetItem(p[0]);
        var flag = (int)p[1].IntValue;
        if (obj is null)
        {
            return Zero();
        }

        // Exult: the knocked out don't wake for usecode.
        if (flag != U7.Actors.ObjFlag.Asleep || !obj.IsActor || obj.GetProp(ActorProp.Health) > 0)
        {
            if (_vm.ActorFlags is { } timers)
            {
                timers.ClearFlag(obj, flag);
            }
            else
            {
                obj.ClearFlag(flag);
            }
        }

        if (IsMovingBargeFlag(flag) && _vm.Barges is { } barges && barges.GetBarge(obj) is { } barge && barge == barges.Moving)
        {
            // Stop the barge it is on or part of.
            barges.SetMoving(null, _vm.Avatar);
        }
        else if (flag == U7.Actors.ObjFlag.ActiveSailor)
        {
            _sailor = null;
        }

        return Zero();
    }

    /// <summary>
    /// Exult <c>UI_on_barge</c> (BG: the flying carpet's usecode): whether
    /// the avatar is on a barge with the whole party in its footprint.
    /// </summary>
    UsecodeValue OnBarge()
    {
        if (_vm.Barges?.GetBarge(_vm.Avatar) is not { } barge)
        {
            return Zero();
        }

        return UsecodeValue.FromInt(PartyObjects().All(m => barge.InFootprint(m.Tx, m.Ty)) ? 1 : 0);
    }

    /// <summary>Exult <c>UI_sit_down(npc, chair)</c>: the NPC's schedule becomes sitting on that chair.</summary>
    UsecodeValue SitDown(UsecodeValue[] p)
    {
        if (_vm.GetItem(p[0]) is { IsActor: true } npc && _vm.GetItem(p[1]) is { } chair)
        {
            _vm.Schedules?.SitOn(npc, chair);
        }

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
