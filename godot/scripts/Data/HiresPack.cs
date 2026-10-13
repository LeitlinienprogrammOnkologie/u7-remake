using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace U7.Data;

/// <summary>
/// Frames of one VGA file redrawn at <see cref="Scale"/> times the
/// resolution, as scripts/upscale/pack.py writes them (assets/hires/&lt;art&gt;/,
/// made locally from the player's own files): "U7HR", version, scale and
/// count, a table of (shape, frame, offset, length), and each frame
/// zlib-compressed as its extents, flags (1 RLE, 2 mixed, 4 true colour),
/// scans, indices, mix plane and (from version 2) colour plane: the model's
/// colours as RGBA, A 255 where a pixel shows its colour and 0 where it is
/// its index alone (<see cref="ShapeFrame.Colour"/>). Version 3
/// (docs/RENDERER.md 5.4, R4) puts the frame's own scale (times 16) before
/// the extents, lets the scans overhang the extents, and adds planes after
/// the colour: straight alpha (flag 8), a normal map (16, two bytes), a
/// height map (32) and an emissive map (64), each a byte a pixel in scan
/// order; its kept pixels (cycling, see-through) are quantised within their
/// range at the frame's resolution. Older packs still read. Frames are read when first asked for, the file open only
/// meanwhile, so the generator can replace a pack while the game runs (the
/// game shows the original for it until it loads the pack again).
/// </summary>
public sealed class HiresPack
{
    /// <summary>The pack format written now; <see cref="OldestVersion"/> up to it are read.</summary>
    public const uint Version = 3;
    const uint OldestVersion = 1;

    readonly string _path;
    /// <summary>The file's length when read: a pack replaced since is no longer this one.</summary>
    readonly long _length;
    readonly Dictionary<(int Shape, int Frame), (long Offset, int Length)> _entries = new();

    /// <summary>The pack's scale: every frame's up to version 2, the usual one from version 3 (a frame may have its own).</summary>
    public int Scale { get; }
    /// <summary>Whether the frames carry their own colours (version 2 on).</summary>
    public bool HasColours { get; }
    /// <summary>The format the pack was written in.</summary>
    public uint FormatVersion { get; }
    public int Count => _entries.Count;

    HiresPack(string path, long length, int scale, uint version)
    {
        _path = path;
        _length = length;
        Scale = scale;
        HasColours = version >= 2;
        FormatVersion = version;
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
            BitConverter.ToUInt32(header, 4) is < OldestVersion or > Version)
        {
            return null;
        }

        var pack = new HiresPack(path, RandomAccess.GetLength(file), (int)BitConverter.ToUInt32(header, 8), BitConverter.ToUInt32(header, 4));
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

    /// <summary>
    /// A stored frame; with <paramref name="mix"/>, its colours between two
    /// indices too, and with <paramref name="colour"/>, its own colours
    /// where the pack has them (then instead of the mix).
    /// </summary>
    public ShapeFrame? Get(int shape, int frame, bool mix, bool colour = false)
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

        var scale16 = Scale * 16;
        if (FormatVersion >= 3)
        {
            scale16 = BitConverter.ToUInt16(data);
            data = data[2..];
        }

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
        var mixAt = at + total;
        var colourAt = mixAt + ((flags & 2) != 0 ? total * 2 : 0);
        var colours = colour && (flags & 4) != 0 ? data.Slice(colourAt, total * 4).ToArray() : null;
        // A pixel with its own colour needs no mix; one without has none (kept pixels aren't mixed).
        var planes = mix && colours is null && (flags & 2) != 0 ? data.Slice(mixAt, total * 2).ToArray() : null;
        var next = colourAt + ((flags & 4) != 0 ? total * 4 : 0);
        var extra = new ShapeFrame.Planes();
        if (FormatVersion >= 3)
        {
            // The planes after the colour, in flag order; straight alpha matters only with the colours shown
            // (the classic painters cut it at a half either way).
            byte[]?[] taken = new byte[]?[4];
            ReadOnlySpan<int> bytesPer = [1, 2, 1, 1];
            for (var k = 0; k < 4; k++)
            {
                if ((flags & (8 << k)) == 0)
                {
                    continue;
                }

                taken[k] = data.Slice(next, total * bytesPer[k]).ToArray();
                next += total * bytesPer[k];
            }

            extra = new ShapeFrame.Planes(colours is null ? null : taken[0], taken[1], taken[2], taken[3]);
        }

        return ShapeFrame.FromScans(xleft, yabove, xright, ybelow, (flags & 1) != 0, scans, pixels, planes, colours, scale16, extra);
    }
}
