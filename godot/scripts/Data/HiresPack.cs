using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace U7.Data;

/// <summary>
/// Frames of one VGA file redrawn at <see cref="Scale"/> times the
/// resolution, as scripts/upscale/pack.py writes them (assets/hires/&lt;art&gt;/,
/// made locally from the player's own files): "U7HR", version, scale and
/// count, a table of (shape, frame, offset, length), and each frame
/// zlib-compressed as its extents, flags (1 RLE, 2 mixed), scans, indices
/// and mix plane. Frames are read when first asked for, the file open only
/// meanwhile, so the generator can replace a pack while the game runs (the
/// game shows the original for it until it loads the pack again).
/// </summary>
public sealed class HiresPack
{
    const uint Version = 1;

    readonly string _path;
    /// <summary>The file's length when read: a pack replaced since is no longer this one.</summary>
    readonly long _length;
    readonly Dictionary<(int Shape, int Frame), (long Offset, int Length)> _entries = new();

    public int Scale { get; }
    public int Count => _entries.Count;

    HiresPack(string path, long length, int scale)
    {
        _path = path;
        _length = length;
        Scale = scale;
    }

    /// <summary>The pack at <paramref name="path"/>, or null if there is none (or it isn't one).</summary>
    public static HiresPack? Open(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var header = new byte[16];
        if (RandomAccess.Read(file, header, 0) < 16 || header[0] != 'U' || header[1] != '7' || header[2] != 'H' || header[3] != 'R' ||
            BitConverter.ToUInt32(header, 4) != Version)
        {
            return null;
        }

        var pack = new HiresPack(path, RandomAccess.GetLength(file), (int)BitConverter.ToUInt32(header, 8));
        var count = (int)BitConverter.ToUInt32(header, 12);
        var table = new byte[count * 12];
        RandomAccess.Read(file, table, 16);
        for (var i = 0; i < count; i++)
        {
            var e = table.AsSpan(i * 12);
            pack._entries[(BitConverter.ToUInt16(e), BitConverter.ToUInt16(e[2..]))] =
                (BitConverter.ToUInt32(e[4..]), (int)BitConverter.ToUInt32(e[8..]));
        }

        return pack;
    }

    public bool Has(int shape, int frame) => _entries.ContainsKey((shape, frame));

    /// <summary>A stored frame; with <paramref name="mix"/>, its colours between two indices too.</summary>
    public ShapeFrame? Get(int shape, int frame, bool mix)
    {
        if (!_entries.TryGetValue((shape, frame), out var entry))
        {
            return null;
        }

        var packed = new byte[entry.Length];
        try
        {
            using var file = File.OpenHandle(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (RandomAccess.GetLength(file) != _length)
            {
                return null;
            }

            RandomAccess.Read(file, packed, entry.Offset);
        }
        catch (IOException)
        {
            return null; // being replaced
        }

        using var unzip = new ZLibStream(new MemoryStream(packed), CompressionMode.Decompress);
        using var body = new MemoryStream();
        unzip.CopyTo(body);
        var data = body.GetBuffer().AsSpan(0, (int)body.Length);

        var xleft = BitConverter.ToInt16(data);
        var yabove = BitConverter.ToInt16(data[2..]);
        var xright = BitConverter.ToInt16(data[4..]);
        var ybelow = BitConverter.ToInt16(data[6..]);
        var flags = data[8];
        var count = (int)BitConverter.ToUInt32(data[9..]);
        var scans = new ShapeFrame.Scan[count];
        var at = 13;
        var total = 0;
        for (var i = 0; i < count; i++, at += 6)
        {
            var length = BitConverter.ToInt16(data[(at + 4)..]);
            scans[i] = new ShapeFrame.Scan(BitConverter.ToInt16(data[at..]), BitConverter.ToInt16(data[(at + 2)..]), length, total);
            total += length;
        }

        var pixels = data.Slice(at, total).ToArray();
        var planes = mix && (flags & 2) != 0 ? data.Slice(at + total, total * 2).ToArray() : null;
        return ShapeFrame.FromScans(xleft, yabove, xright, ybelow, (flags & 1) != 0, scans, pixels, planes);
    }
}
