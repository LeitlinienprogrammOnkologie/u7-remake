using System.IO;
using Godot;

namespace U7.Core;

/// <summary>
/// Resolves the Ultima VII tree relative to the Godot project. The original
/// STATIC/GAMEDAT files and the extracted assets live outside the project,
/// so Godot never imports them.
/// </summary>
public static class U7Paths
{
    public static string RepoRoot { get; private set; } = "";
    /// <summary>The game's STATIC folder: Black Gate's <c>u7/STATIC</c> or Serpent Isle's (<see cref="GameContext"/>).</summary>
    public static string StaticDir => Path.Combine(RepoRoot, GameContext.InstallFolder, "STATIC");
    /// <summary>
    /// When set, GAMEDAT files (IREG, NPC.DAT, FLAGINIT, GWIN.DAT) come from this saved game;
    /// otherwise a new game starts from <c>STATIC/INITGAME.DAT</c>.
    /// </summary>
    public static string? GameDatOverride { get; set; }
    /// <summary>Every mode's saves and the shared <c>settings.cfg</c>.</summary>
    public static string SavesRoot => Path.Combine(RepoRoot, "saves");
    /// <summary>The mode's saved games: <c>saves/bg</c>, <c>saves/si</c> or <c>saves/saga</c>.</summary>
    public static string SavesDir => Path.Combine(SavesRoot, GameContext.ModeId);
    /// <summary>Exult's data files for the game (its <c>data/bg</c> or <c>data/si</c>): <c>godot/data/bg</c> or <c>godot/data/si</c>.</summary>
    public static string GameDataDir => Path.Combine(RepoRoot, "godot", "data", GameContext.Id);
    public static string AssetsDir => Path.Combine(RepoRoot, "assets");
    /// <summary>The redrawn art's packs (scripts/upscale): Black Gate's in <c>assets/hires</c>, Serpent Isle's in <c>assets/hires/si</c>.</summary>
    public static string HiresDir => GameContext.IsBg ? Path.Combine(AssetsDir, "hires") : Path.Combine(AssetsDir, "hires", "si");
    /// <summary>Serpent Isle's STATIC folder; Exult's <c>&lt;SERPENT_STATIC&gt;</c>, which Black Gate borrows from when it is installed.</summary>
    public static string SerpentStaticDir => Path.Combine(RepoRoot, "serpent", "STATIC");
    /// <summary>The mod being played (Exult <c>--mod</c>, <see cref="GameContext.Mod"/>); null for none.</summary>
    public static string? ModName => GameContext.Mod;
    /// <summary>Exult <c>&lt;PATCH&gt;</c>: the mod's <c>data</c> folder (its <c>.cfg</c>'s <c>__MOD_PATH__/data</c>).</summary>
    public static string? PatchDir => ModName is null ? null : Path.Combine(RepoRoot, "mods", ModName, "data");
    /// <summary>
    /// The remake's own additions to the game (Black Gate's keyring), laid out
    /// as a patch folder and read under any mod's: between the game's files
    /// and a mod's <see cref="PatchDir"/>.
    /// </summary>
    public static string BasePatchDir => Path.Combine(GameDataDir, "patch");

    /// <summary>Once per run: a scene reload (a load, a switch of game) keeps the root and the game chosen.</summary>
    public static void Initialize()
    {
        if (RepoRoot.Length > 0)
        {
            return;
        }

        var res = ProjectSettings.GlobalizePath("res://");
        var dir = new DirectoryInfo(res);
        for (var i = 0; i < 6 && dir is not null; i++)
        {
            var map = Path.Combine(dir.FullName, "u7", "STATIC", "U7MAP");
            if (File.Exists(map))
            {
                RepoRoot = dir.FullName;
                GameContext.Initialize();
                GD.Print($"U7 data root: {RepoRoot}, {GameContext.Describe()}");
                return;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not find u7/STATIC/U7MAP. Open the Godot project from godot/ inside the remake repo.");
    }

    /// <summary>A file in the mod's patch folder (Exult's <c>&lt;PATCH&gt;/</c> names, any case), or null.</summary>
    public static string? PatchFile(string name) => FindFile(PatchDir, name);

    /// <summary>The file in the remake's additions, then in the mod's patch folder, those there in that order.</summary>
    public static IEnumerable<string> PatchLayers(string name)
    {
        if (FindFile(BasePatchDir, name) is { } additions)
        {
            yield return additions;
        }

        if (PatchFile(name) is { } mod)
        {
            yield return mod;
        }
    }

    /// <summary>A file in Serpent Isle's STATIC folder (any case), or null when SI isn't installed.</summary>
    public static string? SerpentStaticFile(string name) => FindFile(SerpentStaticDir, name);

    static string? FindFile(string? dir, string name)
    {
        if (dir is null || !Directory.Exists(dir))
        {
            return null;
        }

        return Directory.EnumerateFiles(dir)
            .FirstOrDefault(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A file in the loaded saved game, or null for a new game or when the save lacks it.</summary>
    public static string? SavedGameFile(string name)
    {
        if (GameDatOverride is null)
        {
            return null;
        }

        var path = Path.Combine(GameDatOverride, name);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// A GAMEDAT file's bytes: from the loaded saved game, or for a new game
    /// from the <c>INITGAME.DAT</c> entry of that name (Exult
    /// <c>restore_gamedat</c> copies those into GAMEDAT). Null when absent.
    /// </summary>
    public static byte[]? ReadGameDat(string name)
    {
        if (GameDatOverride is not null)
        {
            return SavedGameFile(name) is { } path ? File.ReadAllBytes(path) : null;
        }

        return InitGame.TryGetValue(name, out var data) ? data : null;
    }

    static Dictionary<string, byte[]>? _initGame;

    /// <summary>The game was switched: INITGAME.DAT is the other game's.</summary>
    internal static void GameChanged() => _initGame = null;

    /// <summary>The game's <c>IDENTITY</c> as INITGAME.DAT holds it, which a save carries (Exult <c>restore_gamedat</c>).</summary>
    public static byte[]? IdentityFile => InitGame.TryGetValue("identity", out var data) ? data : null;

    /// <summary>
    /// Exult <c>get_game_identity</c>: an <c>IDENTITY</c>'s text up to its
    /// end of line or end of file; from a file's bytes, or from a FLEX's
    /// (INITGAME.DAT's) <c>identity</c> entry. Null when there is none.
    /// </summary>
    public static string? ReadIdentity(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        if (!U7.Data.FlexFile.IsFlex(path))
        {
            return Identity(File.ReadAllBytes(path));
        }

        var flex = new U7.Data.FlexFile(path);
        for (var i = 0; i < flex.Count; i++)
        {
            var entry = flex.Get(i);
            if (entry.Length > 13 && System.Text.Encoding.ASCII.GetString(entry[..13]).Split('\0')[0] == "identity")
            {
                return Identity(entry[13..]);
            }
        }

        return null;
    }

    static string Identity(ReadOnlySpan<byte> data)
    {
        var end = data.IndexOfAny((byte)0x1a, (byte)0x0d, (byte)0x0a);
        return System.Text.Encoding.ASCII.GetString(end < 0 ? data : data[..end]);
    }

    /// <summary>INITGAME.DAT entries by name; each entry is a 13-byte name followed by the file.</summary>
    static Dictionary<string, byte[]> InitGame
    {
        get
        {
            if (_initGame is not null)
            {
                return _initGame;
            }

            _initGame = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var path = Path.Combine(StaticDir, "INITGAME.DAT");
            if (!File.Exists(path))
            {
                return _initGame;
            }

            var flex = new U7.Data.FlexFile(path);
            for (var i = 0; i < flex.Count; i++)
            {
                var entry = flex.Get(i);
                if (entry.Length <= 13)
                {
                    continue;
                }

                var name = System.Text.Encoding.ASCII.GetString(entry[..13]).Split('\0')[0].TrimEnd('.');
                _initGame[name] = entry[13..].ToArray();
            }

            return _initGame;
        }
    }
}
