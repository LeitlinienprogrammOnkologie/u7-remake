using U7.Data;

namespace U7.Rendering;

/// <summary>
/// Exult <c>Image_buffer8</c> (imagewin/ibuf8.cc): the picture the world is
/// painted into, one palette index a pixel, with a parallel glow plane: its
/// top six bits how emissive a pixel is (magic, cosmetic: Exult has none),
/// its low two bits marks for the world shader (<see cref="RoofMark"/>,
/// <see cref="PaneMark"/>). Every painter writes its glow byte (0 unless
/// told) where it paints, so what covers a glowing thing or a pane doesn't
/// glow or count as one. Painters clip per scan. Hi-res art adds a mix
/// plane (<see cref="Mix"/>), a pixel's colour between two indices, or a
/// colour plane (<see cref="Colour"/>), its own colour anchored to its index.
/// A frame with straight alpha (<see cref="ShapeFrame.Alpha"/>, pack
/// version 3) is painted by Exult's binary coverage, a pixel from half
/// opaque, and its fainter pixels blend into the colour plane alone (soft
/// edges, in true colour; RENDERER.md 5.4).
/// </summary>
public sealed class IndexBuffer8
{
    /// <summary>The day palette (8-bit RGB), in which true colours are anchored: what a soft pixel blends over where the pixel under it has no colour of its own.</summary>
    public byte[] DayPalette { get; set; } = new byte[768];

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
    /// The lift of what each pixel shows (RENDERER.md 5.6's height, the
    /// ground proxy: an object's lift over its whole picture, the flats 0),
    /// written where a painter replaces the pixel and by a see-through
    /// pixel (the glass is what shows); a transform leaves it. The world
    /// shader finds a pixel's world point from it for light occlusion.
    /// </summary>
    public byte[] Lift { get; private set; } = [];
    /// <summary>The box proxy (RENDERER.md 5.6) of the frames painted next: a building piece's height in lifts (0: the ground proxy, its lift over the whole picture) and buffer pixels per lift (4 at scale 1).</summary>
    int _boxDimZ;
    int _liftPx = 4;

    /// <summary>What <see cref="LiftAt"/> adds for the frames painted next: a piece <paramref name="dimZ"/> lifts tall rises from its hotspot's row, <paramref name="liftPx"/> pixels a lift (0: flat).</summary>
    public void LiftBox(int dimZ, int liftPx)
    {
        _boxDimZ = dimZ;
        _liftPx = Math.Max(1, liftPx);
    }

    /// <summary>
    /// The lift plane's value for a pixel <paramref name="scanX"/>, <paramref name="scanY"/>
    /// from the hotspot (the box's south-east corner at the ground): the base
    /// lift, plus the box's height there. Exult's projection shifts a point
    /// up and left by 4 pixels a lift, so a pixel on the south face is as
    /// high as its row says, one on the east face as high as its column
    /// says, and one on the top is at the full height, which the smaller of
    /// the two gives on all three (RENDERER.md 5.6, the box proxy).
    /// </summary>
    byte LiftAt(byte lift, int scanX, int scanY) =>
        _boxDimZ == 0 ? lift : (byte)Math.Min(255, lift + Math.Clamp(Math.Min(-scanY, -scanX) / _liftPx, 0, _boxDimZ));

    /// <summary>The lift plane over a scan's pixels: one value for a flat frame, per pixel for a box.</summary>
    void FillLift(int dest, int len, byte lift, int scanX, int scanY)
    {
        if (_boxDimZ == 0)
        {
            Lift.AsSpan(dest, len).Fill(lift);
            return;
        }

        for (var i = 0; i < len; i++)
        {
            Lift[dest + i] = LiftAt(lift, scanX + i, scanY);
        }
    }
    /// <summary>
    /// For a pixel marked <see cref="PaneMark"/>: its window's number (low
    /// byte) and the translucency table the pane turned it through (high
    /// byte). Meaningless where the glow plane has no pane mark.
    /// </summary>
    public ushort[] Panes { get; private set; } = [];
    /// <summary>
    /// Two bytes a pixel, as a frame's <see cref="ShapeFrame.Mix"/>: a second
    /// index and how far the colour lies towards it. Only meaningful while
    /// <see cref="MixUsed"/>; until then every pixel is its index alone.
    /// </summary>
    public byte[] Mix { get; private set; } = [];
    /// <summary>Whether this frame painted a mixed pixel (then <see cref="Mix"/> is kept for every pixel painted after).</summary>
    public bool MixUsed { get; private set; }
    /// <summary>
    /// Four bytes a pixel, as a frame's <see cref="ShapeFrame.Colour"/>: the
    /// pixel's own colour (RGB, in the day palette's light) and 255 where it
    /// shows it, its index then the colour's anchor; 0 where the pixel is its
    /// index alone. Only meaningful while <see cref="ColourUsed"/>.
    /// </summary>
    public byte[] Colour { get; private set; } = [];
    /// <summary>Whether this frame painted a true-colour pixel (then <see cref="Colour"/> is kept for every pixel painted after).</summary>
    public bool ColourUsed { get; private set; }

    /// <summary>Exult <c>rotatecolours</c>' first index: a true-colour pixel's anchor is below it, as it must not cycle.</summary>
    const int FirstCycling = 0xE0;

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
        Lift = new byte[width * height];
        Panes = new ushort[width * height];
        Mix = new byte[width * height * 2];
        MixUsed = false;
        Colour = new byte[width * height * 4];
        ColourUsed = false;
    }

    /// <summary>Starts a frame: no pixel is mixed or of its own colour yet.</summary>
    public void BeginFrame()
    {
        if (MixUsed)
        {
            Array.Clear(Mix);
            MixUsed = false;
        }

        if (ColourUsed)
        {
            Array.Clear(Colour);
            ColourUsed = false;
        }
    }

    /// <summary>Exult <c>fill8(pix, w, h, x, y)</c>: a rectangle, at <paramref name="lift"/>.</summary>
    public void Fill8(byte color, int w, int h, int x, int y, byte lift = 0)
    {
        if (!Clip(ref x, ref y, ref w, ref h, out _, out _))
        {
            return;
        }

        for (var row = 0; row < h; row++)
        {
            Pixels.AsSpan((y + row) * Width + x, w).Fill(color);
            Glow.AsSpan((y + row) * Width + x, w).Clear();
            Lift.AsSpan((y + row) * Width + x, w).Fill(lift);
            Unmix((y + row) * Width + x, w);
            Uncolour((y + row) * Width + x, w);
        }
    }

    /// <summary>
    /// Exult <c>copy8</c>: a rectangle of indices, <paramref name="srcw"/> a
    /// row, with its top left at x, y; <paramref name="mix"/> and
    /// <paramref name="colour"/> its mix and colour planes, if any.
    /// </summary>
    public void Copy8(ReadOnlySpan<byte> src, int srcw, int srch, int x, int y, byte glow = 0, ReadOnlySpan<byte> mix = default,
        ReadOnlySpan<byte> colour = default, ReadOnlySpan<byte> alpha = default, byte lift = 0)
    {
        var w = srcw;
        var h = srch;
        if (!Clip(ref x, ref y, ref w, ref h, out var sx, out var sy))
        {
            return;
        }

        for (var row = 0; row < h; row++)
        {
            var from = (sy + row) * srcw + sx;
            var dest = (y + row) * Width + x;
            if (!alpha.IsEmpty)
            {
                PaintSoft(src, mix, colour, alpha, from, dest, w, glow, lift);
                continue;
            }

            src.Slice(from, w).CopyTo(Pixels.AsSpan(dest, w));
            Glow.AsSpan(dest, w).Fill(glow);
            Lift.AsSpan(dest, w).Fill(lift);
            CopyMix(mix, from, dest, w);
            CopyColour(colour, from, dest, w);
        }
    }

    /// <summary>
    /// A run of a frame with straight alpha: a pixel at least
    /// <see cref="ShapeFrame.Covered"/> opaque is painted as any other, a
    /// fainter one leaves the index, glow and mix planes alone; with the
    /// frame's own colours, every pixel blends into the colour plane by its
    /// alpha over what is there (the pixel's own colour, else its index's day
    /// colour). The mix and colour planes count from the run, as
    /// <see cref="CopyMix"/> and <see cref="CopyColour"/> count a run.
    /// </summary>
    void PaintSoft(ReadOnlySpan<byte> src, ReadOnlySpan<byte> mix, ReadOnlySpan<byte> colour, ReadOnlySpan<byte> alpha, int from, int dest, int len,
        byte glow, byte lift, int scanX = 0, int scanY = 0)
    {
        if (!mix.IsEmpty)
        {
            MixUsed = true;
        }

        if (!colour.IsEmpty)
        {
            ColourUsed = true;
        }

        for (var i = 0; i < len; i++)
        {
            var a = alpha[from + i];
            var at = dest + i;
            BlendColour(colour, from + i, at, a);
            if (a < ShapeFrame.Covered)
            {
                continue;
            }

            Pixels[at] = src[from + i];
            Glow[at] = glow;
            Lift[at] = LiftAt(lift, scanX + i, scanY);
            CopyMix(mix, from + i, at, 1);
        }
    }

    /// <summary>
    /// A soft pixel's colour over the colour plane: the frame's colour
    /// (<paramref name="colour"/> at <paramref name="src"/>) blended by
    /// <paramref name="a"/> over the pixel's own colour, or over its index's
    /// day colour where it has none; the result is the pixel's own. A kept
    /// pixel (no colour of its own) covering the pixel leaves it its index alone.
    /// </summary>
    void BlendColour(ReadOnlySpan<byte> colour, int src, int at, byte a)
    {
        if (colour.IsEmpty || colour[src * 4 + 3] == 0)
        {
            if (a >= ShapeFrame.Covered)
            {
                Uncolour(at, 1);
            }

            return;
        }

        var own = Colour.AsSpan(at * 4, 4);
        var under = own[3] != 0 ? own[..3] : DayPalette.AsSpan(Pixels[at] * 3, 3);
        for (var c = 0; c < 3; c++)
        {
            own[c] = (byte)((colour[src * 4 + c] * a + under[c] * (255 - a) + 127) / 255);
        }

        own[3] = 255;
    }

    /// <summary>
    /// Exult <c>Shape_frame::paint</c>: an RLE frame with its hotspot at x, y
    /// (<c>paint_rle</c>); a raw terrain frame is copied with its top left at
    /// x - 8, y - 8 (its extents, at its resolution).
    /// </summary>
    public void PaintRle(ShapeFrame frame, int x, int y, byte glow = 0, byte lift = 0)
    {
        if (!frame.IsRle)
        {
            Copy8(frame.Pixels, frame.Width, frame.Height, x - frame.XLeft, y - frame.YAbove, glow, frame.Mix, frame.Colour, frame.Alpha, lift);
            return;
        }

        foreach (var scan in frame.Scans)
        {
            if (!ClipScan(scan, x, y, out var dest, out var src, out var len))
            {
                continue;
            }

            // A clipped scan starts src - scan.Offset pixels into the scan.
            var scanX = scan.X + (src - scan.Offset);
            if (frame.Alpha is not null)
            {
                PaintSoft(frame.Pixels, frame.Mix, frame.Colour, frame.Alpha, src, dest, len, glow, lift, scanX, scan.Y);
                continue;
            }

            frame.Pixels.AsSpan(src, len).CopyTo(Pixels.AsSpan(dest, len));
            Glow.AsSpan(dest, len).Fill(glow);
            FillLift(dest, len, lift, scanX, scan.Y);
            CopyMix(frame.Mix, src, dest, len);
            CopyColour(frame.Colour, src, dest, len);
        }
    }

    /// <summary>
    /// Exult <c>paint_rle_translucent</c> (<c>copy_hline_translucent8</c>):
    /// indices 0xEE-0xFE turn the pixel under them through their table,
    /// the others (0xFF too) are copied. The see-through pixels glow
    /// <paramref name="haze"/>, the others <paramref name="glow"/>; if
    /// <paramref name="haze"/> marks panes, they note <paramref name="window"/>
    /// and their table in <see cref="Panes"/>. A mixed pixel under glass
    /// keeps its mix, both its indices turned; a true-colour one keeps its
    /// colour, turned as its anchor is (<see cref="XformTables.TurnColour"/>).
    /// </summary>
    public void PaintRleTranslucent(ShapeFrame frame, int x, int y, XformTables xforms, byte glow = 0, byte haze = 0, byte window = 0, byte lift = 0)
    {
        if (!frame.IsRle)
        {
            PaintRle(frame, x, y, glow, lift);
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
            // Most of a run has no see-through colour: copy up to the first one at once (a soft frame goes pixel by pixel).
            var first = frame.Alpha is null ? from.IndexOfAnyInRange((byte)XformTables.FirstTranslucent, (byte)0xFE) : 0;
            var plain = first < 0 ? len : first;
            from[..plain].CopyTo(to);
            lit[..plain].Fill(glow);
            var scanX = scan.X + (src - scan.Offset);
            if (frame.Alpha is null)
            {
                FillLift(dest, len, lift, scanX, scan.Y);
            }

            CopyMix(frame.Mix, src, dest, plain);
            CopyColour(frame.Colour, src, dest, plain);
            if (frame.Alpha is not null)
            {
                MixUsed |= frame.Mix is not null;
                ColourUsed |= frame.Colour is not null;
            }

            var panes = (haze & PaneMark) != 0 ? Panes.AsSpan(dest, len) : default;
            for (var i = plain; i < len; i++)
            {
                var c = from[i];
                var a = frame.Alpha is null ? (byte)255 : frame.Alpha[src + i];
                if (c is >= XformTables.FirstTranslucent and <= 0xFE)
                {
                    if (a < ShapeFrame.Covered)
                    {
                        continue;
                    }

                    Lift[dest + i] = LiftAt(lift, scanX + i, scan.Y);

                    var table = (c - XformTables.FirstTranslucent) << 8;
                    var under = to[i];
                    to[i] = tables[table | under];
                    TurnColour(xforms, c - XformTables.FirstTranslucent, dest + i, under, to[i]);
                    if (MixUsed)
                    {
                        Mix[(dest + i) * 2] = tables[table | Mix[(dest + i) * 2]];
                    }

                    lit[i] = haze;
                    if (!panes.IsEmpty)
                    {
                        panes[i] = (ushort)(((c - XformTables.FirstTranslucent) << 8) | window);
                    }
                }
                else if (frame.Alpha is not null)
                {
                    BlendColour(frame.Colour, src + i, dest + i, a);
                    if (a >= ShapeFrame.Covered)
                    {
                        to[i] = c;
                        lit[i] = glow;
                        Lift[dest + i] = LiftAt(lift, scanX + i, scan.Y);
                        CopyMix(frame.Mix, src + i, dest + i, 1);
                    }
                }
                else
                {
                    to[i] = c;
                    lit[i] = glow;
                    CopyMix(frame.Mix, src + i, dest + i, 1);
                    CopyColour(frame.Colour, src + i, dest + i, 1);
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
    /// <paramref name="glow"/>. True colours are turned as their anchors are.
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
                if ((Glow[at] & PaneMark) == 0 || !windows[pane & 0xFF] || (frame.Alpha is not null && frame.Alpha[src + i] < ShapeFrame.Covered))
                {
                    continue;
                }

                var c = frame.Pixels[src + i];
                if (translucent && c is >= XformTables.FirstTranslucent and <= 0xFE)
                {
                    var table = (c - XformTables.FirstTranslucent) << 8;
                    var under = Pixels[at];
                    Pixels[at] = tables[table | under];
                    TurnColour(xforms, c - XformTables.FirstTranslucent, at, under, Pixels[at]);
                    if (MixUsed)
                    {
                        Mix[at * 2] = tables[table | Mix[at * 2]];
                    }
                }
                else
                {
                    Pixels[at] = tables[(pane & 0xFF00) | c];
                    CopyColour(frame.Colour, src + i, at, 1);
                    TurnColour(xforms, pane >> 8, at, c, Pixels[at]);
                    CopyMix(frame.Mix, src + i, at, 1);
                    if (frame.Mix is not null)
                    {
                        Mix[at * 2] = tables[(pane & 0xFF00) | Mix[at * 2]];
                    }
                }

                Glow[at] = (byte)((glow & GlowBits) | (Glow[at] & ~GlowBits));
            }
        }
    }

    /// <summary>
    /// Exult <c>paint_rle_transformed</c>: every pixel the frame covers turns
    /// the pixel under it through one table (invisible actors), a true colour
    /// as its anchor.
    /// </summary>
    public void PaintRleTransformed(ShapeFrame frame, int x, int y, XformTables xforms, int table)
    {
        var xform = xforms[table];
        if (!frame.IsRle)
        {
            return;
        }

        foreach (var scan in frame.Scans)
        {
            if (!ClipScan(scan, x, y, out var dest, out var src, out var len))
            {
                continue;
            }

            var to = Pixels.AsSpan(dest, len);
            for (var i = 0; i < len; i++)
            {
                if (frame.Alpha is not null && frame.Alpha[src + i] < ShapeFrame.Covered)
                {
                    continue;
                }

                var under = to[i];
                to[i] = xform[under];
                TurnColour(xforms, table, dest + i, under, to[i]);
                if (MixUsed)
                {
                    Mix[(dest + i) * 2] = xform[Mix[(dest + i) * 2]];
                }

                Glow[dest + i] = 0;
            }
        }
    }

    /// <summary>
    /// Exult <c>Shape_frame::paint_rle_outline</c>: a pixel of the colour at
    /// both ends of every scan, and the whole scans on the first scan's row
    /// and the row a frame's height below it. The frame is the original
    /// one; each of its pixels is a <paramref name="scale"/>-square block
    /// here, the hotspot's at x, y.
    /// </summary>
    public void PaintRleOutline(ShapeFrame frame, int x, int y, byte color, int scale = 1, byte lift = 0)
    {
        if (!frame.IsRle || frame.Scans.Length == 0)
        {
            return;
        }

        var firsty = frame.Scans[0].Y;
        var lasty = firsty + frame.Height - 1;
        foreach (var scan in frame.Scans)
        {
            var sx = x + scan.X * scale;
            var sy = y + scan.Y * scale;
            Fill8(color, scale, scale, sx, sy, lift);
            Fill8(color, scale, scale, sx + (scan.Length - 1) * scale, sy, lift);
            if (scan.Y == firsty || scan.Y == lasty)
            {
                Fill8(color, scan.Length * scale, scale, sx, sy, lift);
            }
        }
    }

    /// <summary>A frame's scans filled with one colour (a hi-res outline, <see cref="ShapeFrame.Outline"/>).</summary>
    public void PaintRleSolid(ShapeFrame frame, int x, int y, byte color, byte lift = 0)
    {
        foreach (var scan in frame.Scans)
        {
            if (ClipScan(scan, x, y, out var dest, out _, out var len))
            {
                Pixels.AsSpan(dest, len).Fill(color);
                Glow.AsSpan(dest, len).Clear();
                Lift.AsSpan(dest, len).Fill(lift);
                Unmix(dest, len);
                Uncolour(dest, len);
            }
        }
    }

    /// <summary>
    /// <paramref name="len"/> pixels' mix from a frame's plane (at pixel
    /// <paramref name="src"/>) to the buffer's (at <paramref name="dest"/>);
    /// a frame without one leaves them unmixed.
    /// </summary>
    void CopyMix(ReadOnlySpan<byte> mix, int src, int dest, int len)
    {
        if (mix.IsEmpty)
        {
            Unmix(dest, len);
            return;
        }

        mix.Slice(src * 2, len * 2).CopyTo(Mix.AsSpan(dest * 2, len * 2));
        MixUsed = true;
    }

    void Unmix(int dest, int len)
    {
        if (MixUsed)
        {
            Mix.AsSpan(dest * 2, len * 2).Clear();
        }
    }

    /// <summary>
    /// <paramref name="len"/> pixels' colours from a frame's plane (at pixel
    /// <paramref name="src"/>) to the buffer's (at <paramref name="dest"/>);
    /// a frame without one leaves them their indices alone.
    /// </summary>
    void CopyColour(ReadOnlySpan<byte> colour, int src, int dest, int len)
    {
        if (colour.IsEmpty)
        {
            Uncolour(dest, len);
            return;
        }

        colour.Slice(src * 4, len * 4).CopyTo(Colour.AsSpan(dest * 4, len * 4));
        ColourUsed = true;
    }

    void Uncolour(int dest, int len)
    {
        if (ColourUsed)
        {
            Colour.AsSpan(dest * 4, len * 4).Clear();
        }
    }

    /// <summary>
    /// A true-colour pixel at <paramref name="at"/> seen through table
    /// <paramref name="table"/>, its anchor turned from <paramref name="from"/>
    /// to <paramref name="to"/>. An anchor turned into a cycling colour (no
    /// table of Black Gate's does) leaves the pixel that index alone.
    /// </summary>
    void TurnColour(XformTables xforms, int table, int at, byte from, byte to)
    {
        if (!ColourUsed || Colour[at * 4 + 3] == 0)
        {
            return;
        }

        if (from >= FirstCycling || to >= FirstCycling)
        {
            Colour[at * 4 + 3] = 0;
            return;
        }

        xforms.TurnColour(table, from, to, Colour.AsSpan(at * 4, 3));
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
