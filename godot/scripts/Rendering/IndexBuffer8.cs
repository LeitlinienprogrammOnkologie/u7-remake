using U7.Data;

namespace U7.Rendering;

/// <summary>
/// Exult <c>Image_buffer8</c> (imagewin/ibuf8.cc): the picture the world is
/// painted into, one palette index a pixel, with a parallel glow plane that
/// marks emissive pixels (all zero for now). Painters clip per scan.
/// </summary>
public sealed class IndexBuffer8
{
    public int Width { get; private set; }
    public int Height { get; private set; }
    /// <summary>Indices row by row, <see cref="Width"/> a row.</summary>
    public byte[] Pixels { get; private set; } = [];
    public byte[] Glow { get; private set; } = [];

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
    }

    /// <summary>Exult <c>fill8(pix)</c>: the whole buffer.</summary>
    public void Fill8(byte color) => Pixels.AsSpan().Fill(color);

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
        }
    }

    /// <summary>Exult <c>copy8</c>: a rectangle of indices, <paramref name="srcw"/> a row, with its top left at x, y.</summary>
    public void Copy8(ReadOnlySpan<byte> src, int srcw, int srch, int x, int y)
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
        }
    }

    /// <summary>
    /// Exult <c>Shape_frame::paint</c>: an RLE frame with its hotspot at x, y
    /// (<c>paint_rle</c>); a raw terrain frame is copied with its top left at
    /// x - 8, y - 8.
    /// </summary>
    public void PaintRle(ShapeFrame frame, int x, int y)
    {
        if (!frame.IsRle)
        {
            Copy8(frame.Pixels, ShapeFrame.TileSize, ShapeFrame.TileSize, x - ShapeFrame.TileSize, y - ShapeFrame.TileSize);
            return;
        }

        foreach (var scan in frame.Scans)
        {
            if (ClipScan(scan, x, y, out var dest, out var src, out var len))
            {
                frame.Pixels.AsSpan(src, len).CopyTo(Pixels.AsSpan(dest, len));
            }
        }
    }

    /// <summary>
    /// Exult <c>paint_rle_translucent</c> (<c>copy_hline_translucent8</c>):
    /// indices 0xEE-0xFE turn the pixel under them through their table,
    /// the others (0xFF too) are copied.
    /// </summary>
    public void PaintRleTranslucent(ShapeFrame frame, int x, int y, XformTables xforms)
    {
        if (!frame.IsRle)
        {
            PaintRle(frame, x, y);
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
            for (var i = 0; i < len; i++)
            {
                var c = from[i];
                to[i] = c is >= XformTables.FirstTranslucent and <= 0xFE
                    ? tables[((c - XformTables.FirstTranslucent) << 8) | to[i]]
                    : c;
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
