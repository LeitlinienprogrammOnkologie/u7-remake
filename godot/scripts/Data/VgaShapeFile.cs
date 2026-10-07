using System.IO;
using Godot;

namespace U7.Data;

/// <summary>
/// Reads a SHAPES.VGA-style FLEX (GUMPS.VGA, FONTS.VGA): RLE extents and pixels.
/// Titan's batch extract treated these as 8×8 tiles; decode from the VGA instead.
/// RLE layout is Exult <c>Shape_frame::read</c> / <c>Image_buffer8::paint_rle</c>.
/// </summary>
public sealed class VgaShapeFile
{
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

    public int FrameCount(int shape) =>
        (uint)shape < (uint)_frames.Length && _frames[shape] is { } list ? list.Length : 0;

    public FrameInfo Get(int shape, int frame)
    {
        if ((uint)shape >= (uint)_frames.Length || _frames[shape] is not { Length: > 0 } list)
        {
            return default;
        }

        var i = frame % list.Length;
        if (i < 0)
        {
            i += list.Length;
        }

        return list[i];
    }

    /// <summary>
    /// Decode one RLE frame to RGBA. Palette is 256×RGB (8-bit).
    /// </summary>
    public Image? Decode(int shape, int frame, ReadOnlySpan<byte> paletteRgb)
    {
        if (_flex is null || (uint)shape >= (uint)_flex.Count || paletteRgb.Length < 768)
        {
            return null;
        }

        var entry = _flex.Get(shape);
        if (!TryFrameRle(entry, frame, out var info, out var rle))
        {
            return null;
        }

        var w = info.Width;
        var h = info.Height;
        if (w <= 0 || h <= 0 || w > 512 || h > 512)
        {
            return null;
        }

        var rgba = new byte[w * h * 4];
        var i = 0;
        while (i + 6 <= rle.Length)
        {
            var scanlen = BitConverter.ToUInt16(rle[i..]);
            i += 2;
            if (scanlen == 0)
            {
                break;
            }

            var encoded = (scanlen & 1) != 0;
            scanlen >>= 1;
            var x = info.XLeft + BitConverter.ToInt16(rle[i..]);
            var y = info.YAbove + BitConverter.ToInt16(rle[(i + 2)..]);
            i += 4;

            if (!encoded)
            {
                PlotRaw(rgba, w, h, x, y, rle, ref i, scanlen, paletteRgb);
                continue;
            }

            while (scanlen > 0 && i < rle.Length)
            {
                var bcnt = rle[i++];
                var repeat = (bcnt & 1) != 0;
                bcnt >>= 1;
                if (bcnt == 0)
                {
                    break;
                }

                if (repeat)
                {
                    if (i >= rle.Length)
                    {
                        break;
                    }

                    var pix = rle[i++];
                    PlotRun(rgba, w, h, x, y, bcnt, pix, paletteRgb);
                }
                else
                {
                    PlotRaw(rgba, w, h, x, y, rle, ref i, bcnt, paletteRgb);
                }

                x += bcnt;
                scanlen -= bcnt;
            }
        }

        return Image.CreateFromData(w, h, false, Image.Format.Rgba8, rgba);
    }

    static void PlotRaw(
        byte[] rgba, int w, int h, int x, int y, ReadOnlySpan<byte> rle, ref int i, int count,
        ReadOnlySpan<byte> pal)
    {
        for (var n = 0; n < count && i < rle.Length; n++, x++)
        {
            Plot(rgba, w, h, x, y, rle[i++], pal);
        }
    }

    static void PlotRun(
        byte[] rgba, int w, int h, int x, int y, int count, byte pix, ReadOnlySpan<byte> pal)
    {
        for (var n = 0; n < count; n++, x++)
        {
            Plot(rgba, w, h, x, y, pix, pal);
        }
    }

    static void Plot(byte[] rgba, int w, int h, int x, int y, byte pix, ReadOnlySpan<byte> pal)
    {
        if ((uint)x >= (uint)w || (uint)y >= (uint)h)
        {
            return;
        }

        var o = (y * w + x) * 4;
        var p = pix * 3;
        rgba[o] = pal[p];
        rgba[o + 1] = pal[p + 1];
        rgba[o + 2] = pal[p + 2];
        rgba[o + 3] = 255;
    }

    static bool TryFrameRle(
        ReadOnlySpan<byte> entry, int frame, out FrameInfo info, out ReadOnlySpan<byte> rle)
    {
        info = default;
        rle = default;
        if (entry.Length < 8)
        {
            return false;
        }

        var dlen = BitConverter.ToInt32(entry);
        var hdrlen = BitConverter.ToInt32(entry[4..]);
        var shapelen = entry.Length;
        var isRle = dlen == shapelen || ((shapelen & 1) == 0 && dlen == shapelen - 1);
        if (!isRle || hdrlen < 4)
        {
            return false;
        }

        var nframes = (hdrlen - 4) / 4;
        if (nframes <= 0)
        {
            return false;
        }

        var fr = frame % nframes;
        if (fr < 0)
        {
            fr += nframes;
        }

        int frameoff;
        int framelen;
        if (fr == 0)
        {
            frameoff = hdrlen;
            framelen = nframes > 1
                ? BitConverter.ToInt32(entry[8..]) - frameoff
                : dlen - frameoff;
        }
        else
        {
            var at = 8 + (fr - 1) * 4;
            if (at + 4 > entry.Length)
            {
                return false;
            }

            frameoff = BitConverter.ToInt32(entry[at..]);
            framelen = fr == nframes - 1
                ? dlen - frameoff
                : BitConverter.ToInt32(entry[(at + 4)..]) - frameoff;
        }

        if (frameoff < 0 || framelen < 8 || frameoff + framelen > entry.Length)
        {
            return false;
        }

        var xright = BitConverter.ToInt16(entry[frameoff..]);
        var xleft = BitConverter.ToInt16(entry[(frameoff + 2)..]);
        var yabove = BitConverter.ToInt16(entry[(frameoff + 4)..]);
        var ybelow = BitConverter.ToInt16(entry[(frameoff + 6)..]);
        var w = Math.Max(1, xleft + xright + 1);
        var h = Math.Max(1, yabove + ybelow + 1);
        info = new FrameInfo(w, h, xleft, yabove, xright, ybelow, false);
        rle = entry.Slice(frameoff + 8, framelen - 8);
        return true;
    }

    static FrameInfo[] ParseExtents(ReadOnlySpan<byte> entry)
    {
        if (entry.Length < 8)
        {
            return [];
        }

        var dlen = BitConverter.ToInt32(entry);
        var hdrlen = BitConverter.ToInt32(entry[4..]);
        var shapelen = entry.Length;
        var rle = dlen == shapelen || ((shapelen & 1) == 0 && dlen == shapelen - 1);
        if (!rle || hdrlen < 4)
        {
            return [];
        }

        var nframes = (hdrlen - 4) / 4;
        if (nframes <= 0)
        {
            return [];
        }

        var frames = new FrameInfo[nframes];
        for (var f = 0; f < nframes; f++)
        {
            if (TryFrameRle(entry, f, out var info, out _))
            {
                frames[f] = info;
            }
        }

        return frames;
    }
}

/// <summary>Day palette from <c>PALETTES.FLX</c> entry 0 (VGA 6-bit RGB).</summary>
public static class U7Palette
{
    public static byte[] DayRgb()
    {
        var rgb = new byte[768];
        var path = Path.Combine(U7.Core.U7Paths.StaticDir, "PALETTES.FLX");
        if (!File.Exists(path))
        {
            return rgb;
        }

        var flex = new FlexFile(path);
        var pal = flex.Get(0);
        var n = Math.Min(256, pal.Length / 3);
        for (var i = 0; i < n; i++)
        {
            rgb[i * 3] = (byte)Math.Min(255, pal[i * 3] << 2);
            rgb[i * 3 + 1] = (byte)Math.Min(255, pal[i * 3 + 1] << 2);
            rgb[i * 3 + 2] = (byte)Math.Min(255, pal[i * 3 + 2] << 2);
        }

        return rgb;
    }
}
