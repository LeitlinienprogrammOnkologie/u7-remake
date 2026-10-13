namespace U7.Core;

/// <summary>
/// Exult <c>Game::add_shape</c> / <c>get_shape</c>: shapes the engine names,
/// numbered per game in <c>BG_Game</c> (bggame.cc) and <c>SI_Game</c>
/// (sigame.cc); 0xfff where the game has none.
/// </summary>
public static class GameShapes
{
    static readonly Dictionary<string, int> Bg = new()
    {
        ["gumps/check"] = 2, ["gumps/fileio"] = 3, ["gumps/fntext"] = 4, ["gumps/loadbtn"] = 5,
        ["gumps/savebtn"] = 6, ["gumps/halo"] = 7, ["gumps/disk"] = 24, ["gumps/heart"] = 25,
        ["gumps/statatts"] = 28, ["gumps/musicbtn"] = 29, ["gumps/speechbtn"] = 30, ["gumps/soundbtn"] = 31,
        ["gumps/spellbook"] = 43, ["gumps/statsdisplay"] = 47, ["gumps/combat"] = 46, ["gumps/quitbtn"] = 56,
        ["gumps/yesnobox"] = 69, ["gumps/yesbtn"] = 70, ["gumps/nobtn"] = 71, ["gumps/book"] = 32,
        ["gumps/scroll"] = 55, ["gumps/combatmode"] = 12, ["gumps/slider"] = 14, ["gumps/slider_diamond"] = 15,
        ["gumps/slider_right"] = 16, ["gumps/slider_left"] = 17, ["gumps/box"] = 0, ["gumps/crate"] = 1,
        ["gumps/barrel"] = 8, ["gumps/bag"] = 9, ["gumps/backpack"] = 10, ["gumps/basket"] = 11,
        ["gumps/chest"] = 22, ["gumps/shipshold"] = 26, ["gumps/drawer"] = 27, ["gumps/woodsign"] = 49,
        ["gumps/tombstone"] = 50, ["gumps/goldsign"] = 51, ["gumps/body"] = 53, ["gumps/scroll_spells"] = 0xfff,
        ["gumps/spell_scroll"] = 0xfff, ["gumps/jawbone"] = 0xfff, ["gumps/tooth"] = 0xfff, ["sprites/map"] = 22
    };

    static readonly Dictionary<string, int> Si = new()
    {
        ["gumps/check"] = 2, ["gumps/fileio"] = 3, ["gumps/fntext"] = 4, ["gumps/loadbtn"] = 5,
        ["gumps/savebtn"] = 6, ["gumps/halo"] = 7, ["gumps/disk"] = 19, ["gumps/heart"] = 20,
        ["gumps/statatts"] = 23, ["gumps/musicbtn"] = 24, ["gumps/speechbtn"] = 25, ["gumps/soundbtn"] = 26,
        ["gumps/spellbook"] = 38, ["gumps/combat"] = 41, ["gumps/statsdisplay"] = 42, ["gumps/quitbtn"] = 50,
        ["gumps/yesnobox"] = 51, ["gumps/yesbtn"] = 52, ["gumps/nobtn"] = 53, ["gumps/book"] = 27,
        ["gumps/scroll"] = 49, ["gumps/combat_stats"] = 91, ["gumps/combatmode"] = 12, ["gumps/slider"] = 14,
        ["gumps/slider_diamond"] = 15, ["gumps/slider_right"] = 16, ["gumps/slider_left"] = 17, ["gumps/box"] = 0,
        ["gumps/crate"] = 1, ["gumps/barrel"] = 8, ["gumps/bag"] = 9, ["gumps/backpack"] = 10,
        ["gumps/basket"] = 11, ["gumps/chest"] = 18, ["gumps/shipshold"] = 21, ["gumps/drawer"] = 22,
        ["gumps/woodsign"] = 44, ["gumps/tombstone"] = 45, ["gumps/goldsign"] = 46, ["gumps/body"] = 48,
        ["gumps/tree"] = 64, ["gumps/cstats/1"] = 58, ["gumps/cstats/2"] = 59, ["gumps/cstats/3"] = 60,
        ["gumps/cstats/4"] = 61, ["gumps/cstats/5"] = 62, ["gumps/cstats/6"] = 63, ["sprites/map"] = 22,
        ["gumps/scroll_spells"] = 66, ["gumps/spell_scroll"] = 65, ["gumps/jawbone"] = 56, ["gumps/tooth"] = 57
    };

    /// <summary>Exult <c>Game::get_shape</c>: the game's number for the name (0 for a name it doesn't register).</summary>
    public static int Get(string name) => (GameContext.IsSi ? Si : Bg).GetValueOrDefault(name);
}
