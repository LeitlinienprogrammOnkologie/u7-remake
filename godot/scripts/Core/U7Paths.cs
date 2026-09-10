using System.IO;
using Godot;

namespace U7.Core;

/// <summary>
/// Resolves the extracted Ultima VII tree relative to the Godot project.
/// Original STATIC/GAMEDAT files stay next to the extracted PNG assets so
/// Godot never imports the 35k shape frames.
/// </summary>
public static class U7Paths
{
    public static string RepoRoot { get; private set; } = "";
    public static string StaticDir => Path.Combine(RepoRoot, "u7", "STATIC");
    /// <summary>When set, GAMEDAT files (IREG, NPC.DAT, FLAGINIT, GWIN.DAT) come from this saved game.</summary>
    public static string? GameDatOverride { get; set; }
    public static string GameDatDir => GameDatOverride ?? Path.Combine(RepoRoot, "u7", "GAMEDAT");
    public static string SavesDir => Path.Combine(RepoRoot, "saves");
    public static string AssetsDir => Path.Combine(RepoRoot, "assets");
    public static string ShapesDir => Path.Combine(AssetsDir, "graphics", "shapes");
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

    public static string ShapePng(int shape, int frame) =>
        Path.Combine(ShapesDir, $"{shape:D4}", $"{shape:D4}_f{frame:D4}.png");

    public static string GumpPng(int shape, int frame) =>
        Path.Combine(GumpsDir, $"{shape:D4}", $"{shape:D4}_f{frame:D4}.png");

    public static string FontPng(int font, int frame) =>
        Path.Combine(FontsDir, $"{font:D4}", $"{font:D4}_f{frame:D4}.png");

    public static string FacePng(int shape, int frame) =>
        Path.Combine(FacesDir, $"{shape:D4}", $"{shape:D4}_f{frame:D4}.png");
}
