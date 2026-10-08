using U7.Data;

namespace U7.Rendering;

/// <summary>
/// Exult <c>Image_buffer8</c> (imagewin/ibuf8.cc): the picture the world is
/// painted into, one palette index a pixel, with a parallel glow plane: its
/// top six bits how emissive a pixel is (magic, cosmetic: Exult has none),
/// its low two bits marks for the world shader (<see cref="RoofMark"/>,
/// <see cref="PaneMark"/>). Every painter writes its glow byte (0 unless
/// told) where it paints, so what covers a glowing thing or a pane doesn't
/// glow or count as one. Painters clip per scan.
/// </summary>
public sealed class IndexBuffer8
{
    /// <summary>The glow plane's bits for how emissive a pixel is.</summary>
    public const byte GlowBits = 0xFC;
    /// <summary>A roof's (or upper floor's) pixel: window light doesn't fall on it.</summary>
    public const byte RoofMark = 1;
    /// <summary>A pane of glass: a window's light makes it glow, and what is behind it can show (<see cref="Panes"/>).</summary>
    public const byte PaneMark = 2;

    public int Width { get; private set; }
    public int Height { get; private set; }
    /// <summary>Indices row by row, <see cref="Width"/> a row.</summary>
    public byte[] Pixels { get; private set; } = [];
    public byte[] Glow { get; private set; } = [];
    /// <summary>
    /// For a pixel marked <see cref="PaneMark"/>: its window's number (low
    /// byte) and the translucency table the pane turned it through (high
    /// byte). Meaningless where the glow plane has no pane mark.
    /// </summary>
    public ushort[] Panes { get; private set; } = [];

    /// <summary>Takes a new size, allocating only when it changes; the pixels are then undefined.</summary>
    public void Resize(int width, int height)
    {
        if (width == Width && height == Height)
        {
            return;
        }

        Width = width;
        Height = height;
        Pixels = new byte[width * height];
        Glow = new byte[width * height];
        Panes = new ushort[width * height];
    }

    /// <summary>Exult <c>fill8(pix)</c>: the whole buffer.</summary>
    public void Fill8(byte color) => Pixels.AsSpan().Fill(color);

    /// <summary>Nothing glows: the start of a frame.</summary>
    public void ClearGlow() => Glow.AsSpan().Clear();

    /// <summary>Exult <c>fill8(pix, w, h, x, y)</c>: a rectangle.</summary>
    public void Fill8(byte color, int w, int h, int x, int y)
    {
        if (!Clip(ref x, ref y, ref w, ref h, out _, out _))
        {
            return;
        }

        for (var row = 0; row < h; row++)
        {
            Pixels.AsSpan((y + row) * Width + x, w).Fill(color);
            Glow.AsSpan((y + row) * Width + x, w).Clear();
        }
    }

    /// <summary>Exult <c>copy8</c>: a rectangle of indices, <paramref name="srcw"/> a row, with its top left at x, y.</summary>
    public void Copy8(ReadOnlySpan<byte> src, int srcw, int srch, int x, int y, byte glow = 0)
    {
        var w = srcw;
        var h = srch;
        if (!Clip(ref x, ref y, ref w, ref h, out var sx, out var sy))
        {
            return;
        }

        for (var row = 0; row < h; row++)
        {
            src.Slice((sy + row) * srcw + sx, w).CopyTo(Pixels.AsSpan((y + row) * Width + x, w));
            Glow.AsSpan((y + row) * Width + x, w).Fill(glow);
        }
    }

    /// <summary>
    /// Exult <c>Shape_frame::paint</c>: an RLE frame with its hotspot at x, y
    /// (<c>paint_rle</c>); a raw terrain frame is copied with its top left at
    /// x - 8, y - 8.
    /// </summary>
    public void PaintRle(ShapeFrame frame, int x, int y, byte glow = 0)
    {
        if (!frame.IsRle)
        {
            Copy8(frame.Pixels, ShapeFrame.TileSize, ShapeFrame.TileSize, x - ShapeFrame.TileSize, y - ShapeFrame.TileSize, glow);
            return;
        }

        foreach (var scan in frame.Scans)
        {
            if (ClipScan(scan, x, y, out var dest, out var src, out var len))
            {
                frame.Pixels.AsSpan(src, len).CopyTo(Pixels.AsSpan(dest, len));
                Glow.AsSpan(dest, len).Fill(glow);
            }
        }
    }

    /// <summary>
    /// Exult <c>paint_rle_translucent</c> (<c>copy_hline_translucent8</c>):
    /// indices 0xEE-0xFE turn the pixel under them through their table,
    /// the others (0xFF too) are copied. The see-through pixels glow
    /// <paramref name="haze"/>, the others <paramref name="glow"/>; if
    /// <paramref name="haze"/> marks panes, they note <paramref name="window"/>
    /// and their table in <see cref="Panes"/>.
    /// </summary>
    public void PaintRleTranslucent(ShapeFrame frame, int x, int y, XformTables xforms, byte glow = 0, byte haze = 0, byte window = 0)
    {
        if (!frame.IsRle)
        {
            PaintRle(frame, x, y, glow);
            return;
        }

        var tables = xforms.All;
        foreach (var scan in frame.Scans)
        {
            if (!ClipScan(scan, x, y, out var dest, out var src, out var len))
            {
                continue;
            }

            var from = frame.Pixels.AsSpan(src, len);
            var to = Pixels.AsSpan(dest, len);
            var lit = Glow.AsSpan(dest, len);
            var panes = (haze & PaneMark) != 0 ? Panes.AsSpan(dest, len) : default;
            for (var i = 0; i < len; i++)
            {
                var c = from[i];
                if (c is >= XformTables.FirstTranslucent and <= 0xFE)
                {
                    to[i] = tables[((c - XformTables.FirstTranslucent) << 8) | to[i]];
                    lit[i] = haze;
                    if (!panes.IsEmpty)
                    {
                        panes[i] = (ushort)(((c - XformTables.FirstTranslucent) << 8) | window);
                    }
                }
                else
                {
                    to[i] = c;
                    lit[i] = glow;
                }
            }
        }
    }

    /// <summary>
    /// A frame behind window glass (cosmetic: what a roof hides shows through
    /// its room's windows): painted only on pane pixels of the windows marked
    /// in <paramref name="windows"/>, each seen through its pane's table; with
    /// <paramref name="translucent"/>, its own see-through pixels turn what
    /// the pane shows instead. The panes keep their marks and take
    /// <paramref name="glow"/>.
    /// </summary>
    public void PaintRleThroughPanes(ShapeFrame frame, int x, int y, XformTables xforms, ReadOnlySpan<bool> windows, byte glow, bool translucent)
    {
        if (!frame.IsRle)
        {
            return;
        }

        var tables = xforms.All;
        foreach (var scan in frame.Scans)
        {
            if (!ClipScan(scan, x, y, out var dest, out var src, out var len))
            {
                continue;
            }

            for (var i = 0; i < len; i++)
            {
                var at = dest + i;
                var pane = Panes[at];
                if ((Glow[at] & PaneMark) == 0 || !windows[pane & 0xFF])
                {
                    continue;
                }

                var c = frame.Pixels[src + i];
                Pixels[at] = translucent && c is >= XformTables.FirstTranslucent and <= 0xFE
                    ? tables[((c - XformTables.FirstTranslucent) << 8) | Pixels[at]]
                    : tables[(pane & 0xFF00) | c];
                Glow[at] = (byte)((glow & GlowBits) | (Glow[at] & ~GlowBits));
            }
        }
    }

    /// <summary>
    /// Exult <c>paint_rle_transformed</c>: every pixel the frame covers turns
    /// the pixel under it through one table (invisible actors).
    /// </summary>
    public void PaintRleTransformed(ShapeFrame frame, int x, int y, ReadOnlySpan<byte> xform)
    {
        if (!frame.IsRle)
        {
            return;
        }

        foreach (var scan in frame.Scans)
        {
            if (!ClipScan(scan, x, y, out var dest, out _, out var len))
            {
                continue;
            }

            var to = Pixels.AsSpan(dest, len);
            for (var i = 0; i < len; i++)
            {
                to[i] = xform[to[i]];
            }

            Glow.AsSpan(dest, len).Clear();
        }
    }

    /// <summary>
    /// Exult <c>Shape_frame::paint_rle_outline</c>: a pixel of the colour at
    /// both ends of every scan, and the whole scans on the first scan's row
    /// and the row a frame's height below it.
    /// </summary>
    public void PaintRleOutline(ShapeFrame frame, int x, int y, byte color)
    {
        if (!frame.IsRle || frame.Scans.Length == 0)
        {
            return;
        }

        var firsty = y + frame.Scans[0].Y;
        var lasty = firsty + frame.Height - 1;
        foreach (var scan in frame.Scans)
        {
            var sx = x + scan.X;
            var sy = y + scan.Y;
            PutPixel8(color, sx, sy);
            PutPixel8(color, sx + scan.Length - 1, sy);
            if (sy == firsty || sy == lasty)
            {
                Fill8(color, scan.Length, 1, sx, sy);
            }
        }
    }

    void PutPixel8(byte color, int x, int y)
    {
        if ((uint)x < (uint)Width && (uint)y < (uint)Height)
        {
            Pixels[y * Width + x] = color;
            Glow[y * Width + x] = 0;
        }
    }

    /// <summary>A rectangle cut to the buffer; <paramref name="sx"/>, <paramref name="sy"/> are what was cut off its top left.</summary>
    bool Clip(ref int x, ref int y, ref int w, ref int h, out int sx, out int sy)
    {
        sx = 0;
        sy = 0;
        if (x < 0)
        {
            sx = -x;
            w += x;
            x = 0;
        }

        if (y < 0)
        {
            sy = -y;
            h += y;
            y = 0;
        }

        w = Math.Min(w, Width - x);
        h = Math.Min(h, Height - y);
        return w > 0 && h > 0;
    }

    /// <summary>A scan cut to its row: where it starts in the buffer and in the frame's pixels, and how long it is.</summary>
    bool ClipScan(ShapeFrame.Scan scan, int x, int y, out int dest, out int src, out int len)
    {
        dest = 0;
        src = scan.Offset;
        len = scan.Length;
        var py = y + scan.Y;
        if ((uint)py >= (uint)Height)
        {
            return false;
        }

        var px = x + scan.X;
        if (px < 0)
        {
            src -= px;
            len += px;
            px = 0;
        }

        len = Math.Min(len, Width - px);
        if (len <= 0)
        {
            return false;
        }

        dest = py * Width + px;
        return true;
    }
}
