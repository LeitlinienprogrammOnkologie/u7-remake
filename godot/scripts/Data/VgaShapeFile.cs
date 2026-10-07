using System.IO;
using Godot;

namespace U7.Data;

/// <summary>
/// Reads a SHAPES.VGA-style FLEX (SHAPES.VGA, SPRITES.VGA, GUMPS.VGA,
/// FONTS.VGA, FACES.VGA): frame extents, and frames as palette indices
/// (<see cref="ShapeFrame"/>). Layout is Exult <c>Shape_frame::read</c>: an
/// RLE shape starts with its length and frame offsets, any other is raw 8×8
/// terrain. Titan's batch extract treated GUMPS, FONTS and FACES as 8×8
/// tiles; decode those from the VGA instead.
/// </summary>
public sealed class VgaShapeFile
{
    const int TileBytes = ShapeFrame.TileSize * ShapeFrame.TileSize;

    readonly FlexFile? _flex;
    readonly FrameInfo[][] _frames;

    public VgaShapeFile(string path)
    {
        if (!File.Exists(path))
        {
            _frames = [];
            return;
        }

        try
        {
            _flex = new FlexFile(path);
            _frames = new FrameInfo[_flex.Count][];
            for (var s = 0; s < _flex.Count; s++)
            {
                _frames[s] = ParseExtents(_flex.Get(s));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"VGA load failed ({path}): {ex.Message}");
            _frames = [];
        }
    }

    public int ShapeCount => _frames.Length;

    public int FrameCount(int shape) =>
        (uint)shape < (uint)_frames.Length && _frames[shape] is { } list ? list.Length : 0;

    /// <summary>A frame's extents; the frame number wraps around the count.</summary>
    public FrameInfo Get(int shape, int frame)
    {
        if ((uint)shape >= (uint)_frames.Length || _frames[shape] is not { Length: > 0 } list)
        {
            return default;
        }

        return list[Wrap(frame, list.Length)];
    }

    /// <summary>
    /// Exult <c>Shape::read</c>: a frame as palette indices, or null. Without
    /// <paramref name="wrap"/> (SHAPES.VGA, SPRITES.VGA) a frame past the last
    /// is none, except that one with bit 5 set is frame &amp; 31 reflected,
    /// and terrain ignores the bits above 31. With it the frame number wraps
    /// around the count, as the gump, font and face callers expect.
    /// </summary>
    public ShapeFrame? DecodeFrame(int shape, int frame, bool wrap)
    {
        if (_flex is null || (uint)shape >= (uint)_flex.Count)
        {
            return null;
        }

        var entry = _flex.Get(shape);
        if (IsRle(entry))
        {
            var nframes = RleFrameCount(entry);
            if (wrap)
            {
                frame = Wrap(frame, nframes);
            }
            else if (frame >= nframes && (frame & 32) != 0)
            {
                return DecodeFrame(shape, frame & 0x1f, false)?.Reflect();
            }

            return TryFrameRle(entry, frame, out var data) ? ShapeFrame.FromRle(data) : null;
        }

        var count = entry.Length / TileBytes;
        frame = wrap ? Wrap(frame, count) : frame & 31;
        return (uint)frame < (uint)count ? ShapeFrame.FromTile(entry.Slice(frame * TileBytes, TileBytes)) : null;
    }

    /// <summary>
    /// Decode one frame to RGBA. Palette is 256×RGB (8-bit).
    /// </summary>
    public Image? Decode(int shape, int frame, ReadOnlySpan<byte> paletteRgb) =>
        Decode(shape, frame, paletteRgb, 3);

    /// <summary>
    /// Decode one frame with a 256×RGBA palette, for translucent colours
    /// (Exult paints those through its xform tables).
    /// </summary>
    public Image? DecodeRgba(int shape, int frame, ReadOnlySpan<byte> paletteRgba) =>
        Decode(shape, frame, paletteRgba, 4);

    Image? Decode(int shape, int frame, ReadOnlySpan<byte> pal, int stride)
    {
        if (pal.Length < 256 * stride || DecodeFrame(shape, frame, true) is not { } decoded)
        {
            return null;
        }

        if (decoded.Width > 512 || decoded.Height > 512)
        {
            return null;
        }

        return decoded.ToImage(pal[..(256 * stride)]);
    }

    static int Wrap(int frame, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        var i = frame % count;
        return i < 0 ? i + count : i;
    }

    /// <summary>Exult's test: RLE when the stored length is the entry's, or one less in an even-sized entry.</summary>
    static bool IsRle(ReadOnlySpan<byte> entry)
    {
        if (entry.Length < 8)
        {
            return false;
        }

        var dlen = BitConverter.ToInt32(entry);
        return dlen == entry.Length || ((entry.Length & 1) == 0 && dlen == entry.Length - 1);
    }

    static int RleFrameCount(ReadOnlySpan<byte> entry)
    {
        var hdrlen = BitConverter.ToInt32(entry[4..]);
        return hdrlen < 4 ? 0 : (hdrlen - 4) / 4;
    }

    /// <summary>The bytes of an RLE frame, from its extents to the next frame.</summary>
    static bool TryFrameRle(ReadOnlySpan<byte> entry, int frame, out ReadOnlySpan<byte> data)
    {
        data = default;
        var nframes = RleFrameCount(entry);
        if ((uint)frame >= (uint)nframes)
        {
            return false;
        }

        var dlen = BitConverter.ToInt32(entry);
        var hdrlen = BitConverter.ToInt32(entry[4..]);
        int frameoff;
        int framelen;
        if (frame == 0)
        {
            frameoff = hdrlen;
            framelen = nframes > 1
                ? BitConverter.ToInt32(entry[8..]) - frameoff
                : dlen - frameoff;
        }
        else
        {
            var at = 8 + (frame - 1) * 4;
            if (at + 4 > entry.Length)
            {
                return false;
            }

            frameoff = BitConverter.ToInt32(entry[at..]);
            framelen = frame == nframes - 1
                ? dlen - frameoff
                : BitConverter.ToInt32(entry[(at + 4)..]) - frameoff;
        }

        if (frameoff < 0 || framelen < 8 || frameoff + framelen > entry.Length)
        {
            return false;
        }

        data = entry.Slice(frameoff, framelen);
        return true;
    }

    static FrameInfo[] ParseExtents(ReadOnlySpan<byte> entry)
    {
        if (!IsRle(entry))
        {
            var count = entry.Length / TileBytes;
            var tiles = new FrameInfo[count];
            Array.Fill(tiles, new FrameInfo(ShapeFrame.TileSize, ShapeFrame.TileSize, ShapeFrame.TileSize, ShapeFrame.TileSize, -1, -1, true));
            return tiles;
        }

        var frames = new FrameInfo[RleFrameCount(entry)];
        for (var f = 0; f < frames.Length; f++)
        {
            if (!TryFrameRle(entry, f, out var data))
            {
                continue;
            }

            var xright = BitConverter.ToInt16(data);
            var xleft = BitConverter.ToInt16(data[2..]);
            var yabove = BitConverter.ToInt16(data[4..]);
            var ybelow = BitConverter.ToInt16(data[6..]);
            var w = Math.Max(1, xleft + xright + 1);
            var h = Math.Max(1, yabove + ybelow + 1);
            frames[f] = new FrameInfo(w, h, xleft, yabove, xright, ybelow, false);
        }

        return frames;
    }
}
