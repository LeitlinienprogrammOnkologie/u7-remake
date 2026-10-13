using System.IO;
using Godot;

namespace U7.Core;

/// <summary>Exult <c>Exult_Game</c>: the game a world is loaded from.</summary>
public enum GameType
{
    BlackGate,
    SerpentIsle
}

/// <summary>How the remake is played: one game, or the saga (Black Gate, the bridge, then Serpent Isle).</summary>
public enum PlayMode
{
    BlackGate,
    SerpentIsle,
    Saga
}

/// <summary>
/// Exult <c>Game</c> (game.h): which game the world is loaded from, chosen
/// each time a world loads rather than once per run, so a switch is a scene
/// reload with the other game's files (docs/SERPENT_ISLE.md). Statics survive
/// the reload, so <see cref="Switch"/> drops the caches of game files.
/// </summary>
public static class GameContext
{
    /// <summary>The saga's Black Gate half plays the bridge unless another mod is asked for.</summary>
    const string SagaMod = "eighteen";

    public static PlayMode Mode { get; private set; }
    public static GameType Type { get; private set; }
    public static bool IsBg => Type == GameType.BlackGate;
    public static bool IsSi => Type == GameType.SerpentIsle;
    /// <summary>Exult <c>has_expansion</c>: the Forge of Virtue for Black Gate, the Silver Seed for Serpent Isle.</summary>
    public static bool HasExpansion { get; private set; }
    /// <summary>Exult <c>GAME_SS</c>.</summary>
    public static bool IsSs => IsSi && HasExpansion;
    /// <summary>The game's <c>INITGAME.DAT</c> identity (Exult <c>get_game_identity</c>): ULTIMA7, FORGE, SERPENT ISLE or SILVER SEED.</summary>
    public static string Identity { get; private set; } = "";
    /// <summary>The game's short name for folders (<c>godot/data/bg</c>, <c>assets/faces/si</c>).</summary>
    public static string Id => IsSi ? "si" : "bg";
    /// <summary>The mode's saves folder under <c>saves/</c>.</summary>
    public static string ModeId => Mode switch
    {
        PlayMode.SerpentIsle => "si",
        PlayMode.Saga => "saga",
        _ => "bg"
    };

    /// <summary>The install folder under the repo: <c>u7</c> or <c>serpent</c>.</summary>
    public static string InstallFolder => IsSi ? "serpent" : "u7";
    /// <summary>The mod being played (Exult <c>--mod</c>); it belongs to the game it was given for.</summary>
    public static string? Mod => _modGame == Type ? _mod : null;

    static string? _mod;
    static GameType _modGame;

    /// <summary>From <c>--game bg|si|saga</c> (or <c>U7_GAME</c>) and <c>--mod &lt;name&gt;</c> (or <c>U7_MOD</c>); Black Gate by default.</summary>
    public static void Initialize()
    {
        var mode = Arg("--game", "U7_GAME") switch
        {
            "si" => PlayMode.SerpentIsle,
            "saga" => PlayMode.Saga,
            _ => PlayMode.BlackGate
        };
        Mode = mode;
        SetType(mode == PlayMode.SerpentIsle ? GameType.SerpentIsle : GameType.BlackGate);
        _mod = Arg("--mod", "U7_MOD");
        _modGame = Type;
        if (_mod is null && mode == PlayMode.Saga && Directory.Exists(Path.Combine(U7Paths.RepoRoot, "mods", SagaMod)))
        {
            _mod = SagaMod;
        }
    }

    /// <summary>
    /// Another mode or game for the next scene load: the caches of the old
    /// game's files are dropped. A new mode starts in its first game (the
    /// saga in Black Gate); <paramref name="type"/> picks the game, as a
    /// saga save's identity does.
    /// </summary>
    public static void Switch(PlayMode mode, GameType? type = null)
    {
        var newType = type ?? (mode == PlayMode.SerpentIsle ? GameType.SerpentIsle : GameType.BlackGate);
        if (mode == Mode && newType == Type)
        {
            return;
        }

        Mode = mode;
        if (newType != Type)
        {
            SetType(newType);
            U7Paths.GameChanged();
            U7.Data.U7Palette.Reset();
            U7.Data.TextMessages.Reset();
            U7.Data.VgaFont.Reset();
        }

        GD.Print($"game: {Describe()}");
    }

    /// <summary>The game an <c>IDENTITY</c> names (Exult <c>BaseGameInfo</c>'s identities), or null for none known.</summary>
    public static GameType? TypeOf(string identity) => identity switch
    {
        "ULTIMA7" or "FORGE" => GameType.BlackGate,
        "SERPENT ISLE" or "SILVER SEED" => GameType.SerpentIsle,
        _ => null
    };

    /// <summary>The installed games.</summary>
    public static bool Installed(GameType type) =>
        File.Exists(Path.Combine(U7Paths.RepoRoot, type == GameType.SerpentIsle ? "serpent" : "u7", "STATIC", "INITGAME.DAT"));

    public static string Describe() =>
        $"{(IsSi ? "Serpent Isle" : "Black Gate")} ({Identity}), mode {ModeId}" + (Mod is null ? "" : $", mod {Mod}");

    static void SetType(GameType type)
    {
        Type = type;
        Identity = U7Paths.ReadIdentity(Path.Combine(U7Paths.RepoRoot, InstallFolder, "STATIC", "INITGAME.DAT")) ?? "";
        HasExpansion = Identity is "FORGE" or "SILVER SEED";
    }

    static string? Arg(string flag, string env)
    {
        var args = OS.GetCmdlineUserArgs();
        for (var i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == flag)
            {
                return args[i + 1];
            }
        }

        var value = System.Environment.GetEnvironmentVariable(env);
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
