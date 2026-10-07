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
    public static string StaticDir => Path.Combine(RepoRoot, "u7", "STATIC");
    /// <summary>
    /// When set, GAMEDAT files (IREG, NPC.DAT, FLAGINIT, GWIN.DAT) come from this saved game;
    /// otherwise a new game starts from <c>STATIC/INITGAME.DAT</c>.
    /// </summary>
    public static string? GameDatOverride { get; set; }
    public static string SavesDir => Path.Combine(RepoRoot, "saves");
    public static string AssetsDir => Path.Combine(RepoRoot, "assets");
    public static string GumpsDir => Path.Combine(AssetsDir, "graphics", "gumps");
    public static string FontsDir => Path.Combine(AssetsDir, "graphics", "fonts");
    public static string FacesDir => Path.Combine(AssetsDir, "graphics", "faces");
    public static string DataDir => Path.Combine(AssetsDir, "data");
    public static string TextDir => Path.Combine(AssetsDir, "text");

    public static void Initialize()
    {
        var res = ProjectSettings.GlobalizePath("res://");
        var dir = new DirectoryInfo(res);
        for (var i = 0; i < 6 && dir is not null; i++)
        {
            var map = Path.Combine(dir.FullName, "u7", "STATIC", "U7MAP");
            if (File.Exists(map))
            {
                RepoRoot = dir.FullName;
                GD.Print($"U7 data root: {RepoRoot}");
                return;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not find u7/STATIC/U7MAP. Open the Godot project from godot/ inside the remake repo.");
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

    public static string GumpPng(int shape, int frame) =>
        Path.Combine(GumpsDir, $"{shape:D4}", $"{shape:D4}_f{frame:D4}.png");

    public static string FontPng(int font, int frame) =>
        Path.Combine(FontsDir, $"{font:D4}", $"{font:D4}_f{frame:D4}.png");

    public static string FacePng(int shape, int frame) =>
        Path.Combine(FacesDir, $"{shape:D4}", $"{shape:D4}_f{frame:D4}.png");
}
