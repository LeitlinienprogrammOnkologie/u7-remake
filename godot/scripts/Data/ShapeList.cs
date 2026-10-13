using System.IO;

namespace U7.Data;

/// <summary>
/// One of Exult's own shape files as its sources list it: an <c>expack</c>
/// index (<c>.in</c>: the archive's name, then <c>:n:file.shp</c> or
/// <c>file.shp</c>, numbered on from the line before), read straight from
/// the shape files instead of the FLEX <c>expack</c> would build.
/// </summary>
public sealed class ShapeList : IShapeSource
{
    readonly byte[]?[] _entries;

    ShapeList(byte[]?[] entries) => _entries = entries;

    public int Count => _entries.Length;

    /// <summary>One shape file's bytes as shape 0.</summary>
    public static ShapeList Single(byte[] shape) => new([shape]);

    public ReadOnlySpan<byte> Get(int index) =>
        (uint)index < (uint)_entries.Length && _entries[index] is { } e ? e : ReadOnlySpan<byte>.Empty;

    /// <summary>Exult <c>expack -i</c>'s numbering of an index file; null when it is missing.</summary>
    public static ShapeList? Read(string indexPath)
    {
        if (!File.Exists(indexPath))
        {
            return null;
        }

        var dir = Path.GetDirectoryName(indexPath)!;
        var entries = new List<byte[]?>();
        var shnum = 0;
        foreach (var raw in File.ReadLines(indexPath).Skip(1))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line[0] == ':')
            {
                var colon = line.IndexOf(':', 1);
                if (colon < 0 || !int.TryParse(line[1..colon], out shnum))
                {
                    continue;
                }

                line = line[(colon + 1)..];
            }

            var path = Path.Combine(dir, line);
            while (entries.Count <= shnum)
            {
                entries.Add(null);
            }

            entries[shnum] = File.Exists(path) ? File.ReadAllBytes(path) : null;
            shnum++;
        }

        return new ShapeList(entries.ToArray());
    }
}
