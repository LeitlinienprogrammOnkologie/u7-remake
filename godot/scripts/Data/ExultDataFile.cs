using System.IO;
using U7.Core;

namespace U7.Data;

/// <summary>
/// Exult's text data files (<c>Text_msg_file_reader</c>, msgfile.cc):
/// <c>%%section name</c> ... <c>%%endsection</c>, entries <c>:text</c>
/// (numbered on from the last entry, across sections, as Exult does) or
/// <c>n:text</c> (decimal, 0x hex or 0 octal), <c>#</c> comments. Entries
/// outside any section are the global section (<c>get_global_section</c>).
/// </summary>
public sealed class ExultDataFile
{
    readonly Dictionary<string, List<string?>> _sections = new();

    /// <summary>The entries outside any section (Exult <c>get_global_section</c>), for <see cref="Entries"/>.</summary>
    public const string GlobalSection = "";

    /// <summary>Exult's copy shipped in <c>godot/data/bg</c> or <c>si</c> (its <c>config/</c> resources), or null when absent.</summary>
    public static ExultDataFile? Static(string name) =>
        Read(Path.Combine(U7Paths.GameDataDir, name + ".txt"));

    /// <summary>A mod's own (Exult <c>&lt;PATCH&gt;/name.txt</c>), or null.</summary>
    public static ExultDataFile? Patch(string name) =>
        U7Paths.PatchFile(name + ".txt") is { } path ? Read(path) : null;

    /// <summary>The remake's additions' and a mod's, those there in that order, with the folder each is in.</summary>
    public static IEnumerable<(ExultDataFile File, string Dir)> Layers(string name) =>
        U7Paths.PatchLayers(name + ".txt").Select(p => (Read(p)!, Path.GetDirectoryName(p)!));

    public static ExultDataFile? Read(string path) =>
        File.Exists(path) ? Parse(File.ReadAllLines(path)) : null;

    public static ExultDataFile Parse(IEnumerable<string> lines)
    {
        var file = new ExultDataFile();
        var global = new List<string?>();
        file._sections[GlobalSection] = global;
        var current = global;
        var nextIndex = 0;
        foreach (var raw in lines)
        {
            var line = raw.TrimStart(' ', '\t', '\b');
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("%%section", StringComparison.Ordinal))
            {
                var name = line["%%section".Length..].Trim(' ', '\t', '\b');
                current = new List<string?>();
                file._sections[name] = current;
                continue;
            }

            if (line.StartsWith("%%endsection", StringComparison.Ordinal))
            {
                current = global;
                continue;
            }

            int index;
            string value;
            if (line[0] == ':')
            {
                index = nextIndex++;
                value = line[1..];
            }
            else if (line[0] == '#')
            {
                continue;
            }
            else
            {
                var colon = line.IndexOf(':');
                if (colon < 0 || !TryParseIndex(line[..colon], out index))
                {
                    continue;
                }

                value = line[(colon + 1)..];
            }

            while (current.Count <= index)
            {
                current.Add(null);
            }

            current[index] = value;
        }

        return file;
    }

    static bool TryParseIndex(string s, out int index)
    {
        try
        {
            index = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToInt32(s[2..], 16)
                : s.Length > 1 && s[0] == '0' ? Convert.ToInt32(s, 8)
                : int.Parse(s);
            return true;
        }
        catch (FormatException)
        {
            index = 0;
            return false;
        }
    }

    /// <summary>A section's entries in order, with the index Exult hands its parsers; empty when absent.</summary>
    public IEnumerable<(int Index, string Text)> Entries(string section)
    {
        if (!_sections.TryGetValue(section, out var list))
        {
            yield break;
        }

        for (var i = 0; i < list.Count; i++)
        {
            if (!string.IsNullOrEmpty(list[i]))
            {
                yield return (i, list[i]!);
            }
        }
    }

    /// <summary>Exult <c>get_version</c>: the version section's one number, 1 when missing.</summary>
    public int Version =>
        _sections.TryGetValue("version", out var v) && v.Count == 1 && int.TryParse(v[0], out var n) ? n : 1;
}
