namespace U7.Usecode;

/// <summary>
/// Exult <c>siintrinsics.h</c>: Serpent Isle's intrinsic numbers by name.
/// Exult runs both games' intrinsics through the same functions, so a name
/// here dispatches to Black Gate's handler of that name
/// (<see cref="BgIntrinsics"/>); the few Black Gate's table lacks have their
/// own numbers from <see cref="FirstSiOnly"/>.
/// </summary>
public static class SiIntrinsics
{
    static readonly string[] Names =
    [
        "get_random", "execute_usecode_array", "delayed_execute_usecode_array", "show_npc_face", "remove_npc_face", "show_npc_face0",
        "show_npc_face1", "remove_npc_face0", "remove_npc_face1", "set_conversation_slot", "change_npc_face0", "change_npc_face1",
        "add_answer", "remove_answer", "push_answers", "pop_answers", "clear_answers", "select_from_menu",
        "select_from_menu2", "input_numeric_value", "set_item_shape", "find_nearest", "play_sound_effect", "die_roll",
        "get_item_shape", "get_item_weight", "get_item_frame", "set_item_frame", "get_item_quality", "set_item_quality",
        "get_item_quantity", "set_item_quantity", "get_object_position", "get_distance", "find_direction", "get_npc_object",
        "get_schedule_type", "set_schedule_type", "add_to_party", "remove_from_party", "get_npc_prop", "set_npc_prop",
        "get_avatar_ref", "get_party_list", "create_new_object", "create_new_object2", "set_last_created", "update_last_created",
        "get_npc_name", "count_objects", "find_object", "get_cont_items", "remove_party_items", "add_party_items",
        "add_cont_items", "remove_cont_items", "get_music_track", "play_music", "npc_nearby", "npc_nearby2",
        "find_nearby_avatar", "is_npc", "display_runes", "click_on_item", "error_message", "find_nearby",
        "give_last_created", "give_last_created", "is_dead", "game_hour", "game_minute", "get_npc_number",
        "part_of_day", "get_alignment", "set_alignment", "move_object", "remove_npc", "item_say",
        "clear_item_say", "set_to_attack", "get_lift", "set_lift", "get_weather", "set_weather",
        "sit_down", "summon", "si_display_map", "kill_npc", "roll_to_win", "set_attack_mode",
        "get_attack_mode", "set_oppressor", "get_oppressor", "get_weapon", "set_opponent", "clone",
        "UNKNOWN", "display_area", "wizard_eye", "resurrect", "resurrect_npc", "get_body_npc",
        "add_spell", "remove_all_spells", "sprite_effect", "attack_object", "book_mode", "stop_time",
        "cause_light", "get_barge", "earthquake", "is_pc_female", "armageddon", "halt_scheduled",
        "lightning", "get_array_size", "save_pos", "teleport_to_saved_pos", "apply_damage", "is_pc_inside",
        "UNKNOWN", "UNKNOWN", "get_timer", "set_timer", "wearing_fellowship", "mouse_exists",
        "get_speech_track", "flash_mouse", "get_item_frame_rot", "set_item_frame_rot", "on_barge", "get_container",
        "remove_item", "init_conversation", "end_conversation", "reduce_health", "is_readied", "restart_game",
        "start_speech", "run_endgame", "fire_projectile", "nap_time", "advance_time", "in_usecode",
        "call_guards", "obj_sprite_effect", "attack_avatar", "stop_arresting", "path_run_usecode", "UNKNOWN",
        "can_avatar_reach_pos", "UNKNOWN", "close_gumps", "item_say", "close_gump", "in_gump_mode",
        "set_light", "UNKNOWN", "set_time_palette", "ambient_light", "is_not_blocked", "play_sound_effect2",
        "direction_from", "get_item_flag", "set_item_flag", "clear_item_flag", "get_skin_colour", "set_path_failure",
        "fade_palette", "fade_palette_sleep", "get_party_list2", "in_combat", "is_water", "reset_conv_face",
        "set_camera", "get_dead_party", "view_tile", "telekenesis", "a_or_an", "set_polymorph",
        "revert_schedule", "modify_schedule", "set_new_schedules", "run_schedule", "get_temperature", "get_temperature_zone",
        "set_temperature", "get_npc_warmth", "get_npc_id", "set_npc_id", "get_readied", "approach_avatar",
        "set_barge_dir", "si_path_run_usecode", "is_on_keyring", "add_to_keyring", "remove_from_area", "infravision",
        "set_intercept_item", "printf", "remove_from_keyring", "begin_casting_mode", "get_usecode_fun", "get_map_num",
        "display_map_ex", "book_mode_ex", "UNKNOWN", "center_view", "UNKNOWN", "is_dest_reachable",
        "set_npc_name", "set_usecode_fun", "has_spell", "remove_spell", "create_barge_object", "in_usecode_path",
        "start_blocking_speech", "close_gumps2", "close_gump2", "game_day", "UNKNOWN", "UNKNOWN",
        "UNKNOWN", "UNKNOWN", "UNKNOWN", "UNKNOWN", "UNKNOWN", "UNKNOWN",
        "UNKNOWN", "UNKNOWN", "UNKNOWN", "UNKNOWN", "UNKNOWN", "UNKNOWN",
        "UNKNOWN", "UNKNOWN", "UNKNOWN", "UNKNOWN", "UNKNOWN", "UNKNOWN",
        "UNKNOWN", "UNKNOWN", "UNKNOWN", "UNKNOWN", "UNKNOWN", "UNKNOWN",
        "UNKNOWN", "UNKNOWN", "UNKNOWN", "UNKNOWN", "UNKNOWN", "UNKNOWN",
        "UNKNOWN", "UNKNOWN", "UNKNOWN", "UNKNOWN"
    ];

    /// <summary>The handlers' numbers for the intrinsics only Serpent Isle's table has (past Black Gate's 256).</summary>
    public const int FirstSiOnly = 0x100;

    public static readonly string[] SiOnly =
    [
        "npc_nearby2", "si_display_map", "resurrect_npc", "init_conversation", "end_conversation",
        "stop_arresting", "can_avatar_reach_pos", "is_on_keyring", "add_to_keyring", "remove_from_keyring"
    ];

    static int[]? _dispatch;

    /// <summary>Serpent Isle's intrinsic number as the number of the handler of its name (unknown names stay unknown).</summary>
    public static int ToDispatch(int id)
    {
        _dispatch ??= Names.Select(Resolve).ToArray();
        return (uint)id < (uint)_dispatch.Length ? _dispatch[id] : id;
    }

    static int Resolve(string name)
    {
        var siOnly = Array.IndexOf(SiOnly, name);
        if (siOnly >= 0)
        {
            return FirstSiOnly + siOnly;
        }

        return name == "UNKNOWN" ? 0xff : BgIntrinsics.IdOf(name);
    }
}
