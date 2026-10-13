namespace U7.Data;

/// <summary>
/// Exult <c>Shapeinfo_lookup::setup_shape_files</c> (<c>shape_files.txt</c>,
/// then a mod's own): where Black Gate's paperdolls come from, and which
/// Serpent Isle gumps and shapes it imports. An import takes the next number
/// from 2048 (Exult <c>c_max_shapes</c>), named <c>%0</c>, <c>%1</c>... for
/// the other tables (<c>avatar_data.txt</c>, <c>blue_shapes</c>).
/// </summary>
public sealed class ShapeFiles
{
    /// <summary>Exult <c>c_max_shapes</c>: the first number an imported shape takes.</summary>
    public const int FirstImport = 2048;

    /// <summary>Exult <c>paperdoll_source</c>: <c>si</c> (SI's PAPERDOL.VGA), <c>flx</c> (Exult's own Black Gate art), <c>static</c>.</summary>
    public List<string> PaperdollSources { get; } = new();
    /// <summary>Exult <c>gump_imports</c>: (the number here, SI's gump).</summary>
    public List<(int Dest, int Src)> GumpImports { get; } = new();
    /// <summary>Exult <c>multiracial_imports</c>: (the number here, SI's shape).</summary>
    public List<(int Dest, int Src)> SkinImports { get; } = new();
    /// <summary>Exult <c>blue_shapes</c>: a paperdoll spot's gump.</summary>
    public List<(int Spot, int Gump)> BlueShapes { get; } = new();
    public Dictionary<string, int> GumpVars { get; } = new();
    public Dictionary<string, int> SkinVars { get; } = new();

    int _nextGump = FirstImport;
    int _nextSkin = FirstImport;

    public static ShapeFiles Load()
    {
        var files = new ShapeFiles();
        if (ExultDataFile.Static("shape_files") is { } bundled)
        {
            files.Read(bundled, false);
        }

        if (ExultDataFile.Patch("shape_files") is { } patch)
        {
            files.Read(patch, true);
        }

        return files;
    }

    void Read(ExultDataFile file, bool patch)
    {
        var sources = file.Entries("paperdoll_source").Select(e => e.Text.Trim()).ToList();
        // Exult clears the list for a patch's entries (and, never setting its erased_for_patch, on each one,
        // keeping only the last); here a patch's list replaces the bundled one.
        if (patch && sources.Count > 0)
        {
            PaperdollSources.Clear();
        }

        PaperdollSources.AddRange(sources);
        foreach (var (_, text) in file.Entries("gump_imports"))
        {
            Import(text, GumpImports, GumpVars, ref _nextGump);
        }

        foreach (var (_, text) in file.Entries("blue_shapes"))
        {
            var parts = text.Split('/');
            if (parts.Length == 2 && int.TryParse(parts[0], out var spot) && Ref(parts[1], GumpVars) is { } gump)
            {
                BlueShapes.Add((spot, gump));
            }
        }

        foreach (var (_, text) in file.Entries("multiracial_imports"))
        {
            Import(text, SkinImports, SkinVars, ref _nextSkin);
        }
    }

    /// <summary>Exult <c>Shape_imports_parser</c>: <c>src/%name</c> takes the next free number, <c>src/n</c> that one; a repeated source is skipped.</summary>
    static void Import(string text, List<(int Dest, int Src)> table, Dictionary<string, int> vars, ref int next)
    {
        var parts = text.Split('/');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var src) || table.Any(t => t.Src == src))
        {
            return;
        }

        var dest = parts[1].Trim();
        if (dest.StartsWith('%'))
        {
            vars[dest] = next;
            table.Add((next++, src));
        }
        else if (int.TryParse(dest, out var n))
        {
            table.Add((n, src));
        }
    }

    /// <summary>Exult <c>Shaperef_parser</c>'s value: a number, or a <c>%name</c> defined earlier (else null).</summary>
    public static int? Ref(string text, Dictionary<string, int> vars)
    {
        text = text.Trim();
        if (text.StartsWith('%'))
        {
            return vars.TryGetValue(text, out var v) ? v : null;
        }

        return int.TryParse(text, out var n) ? n : null;
    }
}
