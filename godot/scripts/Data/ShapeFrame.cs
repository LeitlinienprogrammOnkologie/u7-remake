using Godot;

namespace U7.Data;

/// <summary>
/// One frame of a SHAPES.VGA-style file as palette indices: Exult
/// <c>Shape_frame</c> (shapes/vgafile.cc). The RLE data is unpacked into
/// scans, runs of opaque pixels placed relative to the hotspot, whose indices
/// sit back to back in <see cref="Pixels"/>, so a painter copies or
/// transforms each scan as Exult's <c>paint_rle</c> family does.
/// </summary>
public sealed class ShapeFrame
{
    /// <summary>Exult <c>c_tilesize</c>: a flat terrain frame is 8×8.</summary>
    public const int TileSize = 8;
    const int TileBytes = TileSize * TileSize;
    /// <summary>The pixel Exult's <c>reflect</c> and <c>encode_rle</c> treat as transparent.</summary>
    const byte Transparent = 255;

    /// <summary>
    /// A run of <see cref="Length"/> pixels starting <see cref="X"/>,
    /// <see cref="Y"/> from the hotspot, its indices at
    /// <see cref="Offset"/> in <see cref="Pixels"/>.
    /// </summary>
    public readonly record struct Scan(short X, short Y, short Length, int Offset);

    public int XLeft { get; }
    public int YAbove { get; }
    public int XRight { get; }
    public int YBelow { get; }
    /// <summary>False for a raw 8×8 terrain frame, which Exult paints with <c>copy8</c> at -8,-8.</summary>
    public bool IsRle { get; }
    public Scan[] Scans { get; }
    public byte[] Pixels { get; }
    /// <summary>
    /// Hi-res art's colours between two palette entries (<see cref="U7.Rendering.HiresArt"/>):
    /// beside each of <see cref="Pixels"/> two bytes, a second index and how far
    /// (0-255) the colour lies from the first towards it. Null when every pixel
    /// is its index alone, as in the original files.
    /// </summary>
    public byte[]? Mix { get; }
    /// <summary>
    /// Hi-res art's own colours (the remake's true colour, <see cref="U7.Rendering.ArtLook.TrueColour"/>):
    /// beside each of <see cref="Pixels"/> four bytes, its colour in the day
    /// palette's light (RGB) and 255 where the pixel shows it, 0 where it is
    /// its index alone (colours that cycle, see-through ones). The index is
    /// then the colour's anchor, the nearest palette entry, through which the
    /// palettes and tables change it. Null when every pixel is its index.
    /// </summary>
    public byte[]? Colour { get; }
    /// <summary>
    /// Hi-res art's straight alpha (pack version 3, docs/RENDERER.md 5.4):
    /// beside each of <see cref="Pixels"/> how opaque it is, 1-255. Null
    /// where every pixel is opaque, as in the original files. The classic
    /// painters count a pixel from 128 (binary coverage, as Exult); the
    /// true-colour plane blends any (soft edges).
    /// </summary>
    public byte[]? Alpha { get; }
    /// <summary>Optional maps a pack may carry per pixel (RENDERER.md 5.4), unused until the lighting milestones: a normal (two bytes), a height, an emissive strength.</summary>
    public byte[]? Normal { get; }
    public byte[]? Height8 { get; }
    public byte[]? Emissive { get; }
    /// <summary>
    /// The frame's resolution against the original's, times 16 (a scale per
    /// frame, pack version 3): 16 for the original, 64 for 4x; a painting
    /// fitted by height may have any. The extents and scans are at this scale.
    /// </summary>
    public int Scale16 { get; }
    /// <summary>The opacity below which the classic painters leave a pixel (Exult's coverage is binary).</summary>
    public const byte Covered = 128;

    public int Width => XLeft + XRight + 1;
    /// <summary>Exult <c>Shape_frame::is_empty</c>: an RLE frame with no pixels.</summary>
    public bool IsEmpty => IsRle && Scans.Length == 0;
    public int Height => YAbove + YBelow + 1;

    ShapeFrame(int xleft, int yabove, int xright, int ybelow, bool rle, Scan[] scans, byte[] pixels, byte[]? mix = null, byte[]? colour = null,
        byte[]? alpha = null, int scale16 = 16, byte[]? normal = null, byte[]? height = null, byte[]? emissive = null)
    {
        XLeft = xleft;
        YAbove = yabove;
        XRight = xright;
        YBelow = ybelow;
        IsRle = rle;
        Scans = scans;
        Pixels = pixels;
        Mix = mix;
        Colour = colour;
        Alpha = alpha;
        Scale16 = scale16;
        Normal = normal;
        Height8 = height;
        Emissive = emissive;
    }

    /// <summary>The optional planes of a pack's frame (<see cref="FromScans"/>): straight alpha and the maps, each null if the pack has none.</summary>
    public readonly record struct Planes(byte[]? Alpha = null, byte[]? Normal = null, byte[]? Height = null, byte[]? Emissive = null);

    /// <summary>
    /// A hi-res frame from its parts: the extents and scans at its own
    /// resolution (<paramref name="scale16"/>), the indices back to back and,
    /// if any, their mix and colour planes and the optional ones.
    /// </summary>
    public static ShapeFrame FromScans(int xleft, int yabove, int xright, int ybelow, bool rle, Scan[] scans, byte[] pixels, byte[]? mix,
        byte[]? colour = null, int scale16 = 16, Planes planes = default) =>
        new(xleft, yabove, xright, ybelow, rle, scans, pixels, mix, colour, planes.Alpha, scale16, planes.Normal, planes.Height, planes.Emissive);

    /// <summary>
    /// The visual bounds: the rectangle the scans cover, relative to the
    /// hotspot (left, above, right, below as the extents are). Pack version 3
    /// frames may overhang their logical extents (a halo, a painted shadow),
    /// which stay the game's for ordering and picking (RENDERER.md 5.4).
    /// </summary>
    public (int Left, int Above, int Right, int Below) VisualBounds()
    {
        if (!IsRle || Scans.Length == 0)
        {
            return (XLeft, YAbove, XRight, YBelow);
        }

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var s in Scans)
        {
            minX = Math.Min(minX, s.X);
            maxX = Math.Max(maxX, s.X + s.Length - 1);
            minY = Math.Min(minY, s.Y);
            maxY = Math.Max(maxY, s.Y);
        }

        return (Math.Max(XLeft, -minX), Math.Max(YAbove, -minY), Math.Max(XRight, maxX), Math.Max(YBelow, maxY));
    }

    /// <summary>
    /// The frame resampled from its own scale to <paramref name="toScale16"/>
    /// by nearest pixel (every plane), for a pack frame at another scale than
    /// the buffer's (RENDERER.md 5.4: each frame's pixels at their own density
    /// is the GPU store's later; here placement is exact and a 2x frame in a
    /// 4x buffer is its pixels doubled). Integer blow-ups give
    /// <see cref="Scaled"/>'s result.
    /// </summary>
    public ShapeFrame Rescaled(int toScale16)
    {
        if (toScale16 == Scale16)
        {
            return this;
        }

        if (toScale16 % Scale16 == 0)
        {
            return Scaled(toScale16 / Scale16);
        }

        var (left, above, right, below) = VisualBounds();
        var w = left + right + 1;
        var h = above + below + 1;
        var has = new bool[w * h];
        var at = new int[w * h];
        foreach (var scan in Scans)
        {
            for (var k = 0; k < scan.Length; k++)
            {
                var i = (above + scan.Y) * w + left + scan.X + k;
                has[i] = true;
                at[i] = scan.Offset + k;
            }
        }

        // The hotspot's pixel starts at 0 at both scales: a source pixel x covers x * to / from to the next.
        int Up(int v) => (int)Math.Floor((double)v * toScale16 / Scale16);
        int Down(int v) => (int)Math.Floor((double)v * Scale16 / toScale16);
        var nLeft = -Up(-left);
        var nAbove = -Up(-above);
        var nRight = Up(right + 1) - 1;
        var nBelow = Up(below + 1) - 1;
        var nw = nLeft + nRight + 1;
        var nh = nAbove + nBelow + 1;
        var scans = new List<Scan>();
        var pixels = new List<byte>();
        var mix = Mix is null ? null : new List<byte>();
        var colour = Colour is null ? null : new List<byte>();
        var alpha = Alpha is null ? null : new List<byte>();
        var normal = Normal is null ? null : new List<byte>();
        var height = Height8 is null ? null : new List<byte>();
        var emissive = Emissive is null ? null : new List<byte>();
        for (var y = 0; y < nh; y++)
        {
            var sy = Down(y - nAbove) + above;
            if ((uint)sy >= (uint)h)
            {
                continue;
            }

            for (var x = 0; x < nw;)
            {
                var sx = Down(x - nLeft) + left;
                if ((uint)sx >= (uint)w || !has[sy * w + sx])
                {
                    x++;
                    continue;
                }

                var start = x;
                var offset = pixels.Count;
                for (; x < nw; x++)
                {
                    sx = Down(x - nLeft) + left;
                    if ((uint)sx >= (uint)w || !has[sy * w + sx])
                    {
                        break;
                    }

                    var src = at[sy * w + sx];
                    pixels.Add(Pixels[src]);
                    mix?.AddRange(Mix.AsSpan(src * 2, 2));
                    colour?.AddRange(Colour.AsSpan(src * 4, 4));
                    alpha?.Add(Alpha![src]);
                    normal?.AddRange(Normal.AsSpan(src * 2, 2));
                    height?.Add(Height8![src]);
                    emissive?.Add(Emissive![src]);
                }

                scans.Add(new Scan((short)(start - nLeft), (short)(y - nAbove), (short)(x - start), offset));
            }
        }

        var planes = new Planes(alpha?.ToArray(), normal?.ToArray(), height?.ToArray(), emissive?.ToArray());
        return new ShapeFrame(-Up(-XLeft), -Up(-YAbove), Up(XRight + 1) - 1, Up(YBelow + 1) - 1, IsRle, scans.ToArray(), pixels.ToArray(),
            mix?.ToArray(), colour?.ToArray(), planes.Alpha, toScale16, planes.Normal, planes.Height, planes.Emissive);
    }

    /// <summary>
    /// The frame at <paramref name="n"/> times the resolution, each pixel an
    /// n×n block: what hi-res art stands in for where it has no frame. A
    /// pixel x of the original covers x·n to x·n + n - 1, so the hotspot's
    /// block starts at the new hotspot.
    /// </summary>
    public ShapeFrame Scaled(int n)
    {
        if (n == 1)
        {
            return this;
        }

        var scans = new Scan[Scans.Length * n];
        var pixels = new byte[Pixels.Length * n * n];
        var mix = Mix is null ? null : new byte[pixels.Length * 2];
        var colour = Colour is null ? null : new byte[pixels.Length * 4];
        var alpha = Alpha is null ? null : new byte[pixels.Length];
        var normal = Normal is null ? null : new byte[pixels.Length * 2];
        var height = Height8 is null ? null : new byte[pixels.Length];
        var emissive = Emissive is null ? null : new byte[pixels.Length];
        var at = 0;
        var k = 0;
        foreach (var scan in Scans)
        {
            var len = scan.Length * n;
            for (var row = 0; row < n; row++)
            {
                scans[k++] = new Scan((short)(scan.X * n), (short)(scan.Y * n + row), (short)len, at);
                for (var i = 0; i < scan.Length; i++)
                {
                    var src = scan.Offset + i;
                    var dest = at + i * n;
                    pixels.AsSpan(dest, n).Fill(Pixels[src]);
                    alpha?.AsSpan(dest, n).Fill(Alpha![src]);
                    height?.AsSpan(dest, n).Fill(Height8![src]);
                    emissive?.AsSpan(dest, n).Fill(Emissive![src]);
                    for (var j = 0; j < n; j++)
                    {
                        if (mix is not null)
                        {
                            mix[(dest + j) * 2] = Mix![src * 2];
                            mix[(dest + j) * 2 + 1] = Mix[src * 2 + 1];
                        }

                        if (normal is not null)
                        {
                            normal[(dest + j) * 2] = Normal![src * 2];
                            normal[(dest + j) * 2 + 1] = Normal[src * 2 + 1];
                        }

                        if (colour is not null)
                        {
                            Colour!.AsSpan(src * 4, 4).CopyTo(colour.AsSpan((dest + j) * 4, 4));
                        }
                    }
                }

                at += len;
            }
        }

        return new ShapeFrame(XLeft * n, YAbove * n, XRight * n + n - 1, YBelow * n + n - 1, IsRle, scans, pixels, mix, colour, alpha, Scale16 * n,
            normal, height, emissive);
    }

    /// <summary>
    /// Exult <c>Shape_frame::get_rle_shape</c>: a frame's extents (xright,
    /// xleft, yabove, ybelow), then scans until a zero length. A scan is a
    /// length whose bit 0 marks it encoded, x and y, then the raw indices or
    /// runs (a count whose bit 0 means one index repeated, else that many).
    /// </summary>
    public static ShapeFrame? FromRle(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 8)
        {
            return null;
        }

        var xright = BitConverter.ToInt16(frame);
        var xleft = BitConverter.ToInt16(frame[2..]);
        var yabove = BitConverter.ToInt16(frame[4..]);
        var ybelow = BitConverter.ToInt16(frame[6..]);
        var rle = frame[8..];
        var (scanCount, pixelCount) = Unpack(rle, null, null);
        var scans = new Scan[scanCount];
        var pixels = new byte[pixelCount];
        Unpack(rle, scans, pixels);
        return new ShapeFrame(xleft, yabove, xright, ybelow, true, scans, pixels);
    }

    /// <summary>
    /// Exult <c>Shape_frame::read</c> for a shape that isn't RLE (the terrain,
    /// shapes 0-149): 64 indices, extents 8, 8, -1, -1, painted at -8,-8.
    /// </summary>
    public static ShapeFrame FromTile(ReadOnlySpan<byte> tile)
    {
        var pixels = tile[..TileBytes].ToArray();
        var scans = new Scan[TileSize];
        for (var row = 0; row < TileSize; row++)
        {
            scans[row] = new Scan(-TileSize, (short)(row - TileSize), TileSize, row * TileSize);
        }

        return new ShapeFrame(TileSize, TileSize, -1, -1, false, scans, pixels);
    }

    /// <summary>Walks the RLE data; counts the scans and pixels, and fills the arrays when given.</summary>
    static (int Scans, int Pixels) Unpack(ReadOnlySpan<byte> rle, Scan[]? scans, byte[]? pixels)
    {
        var i = 0;
        var nscans = 0;
        var npixels = 0;
        while (i + 2 <= rle.Length)
        {
            int scanlen = BitConverter.ToUInt16(rle[i..]);
            i += 2;
            if (scanlen == 0 || i + 4 > rle.Length)
            {
                break;
            }

            var encoded = (scanlen & 1) != 0;
            scanlen >>= 1;
            var x = BitConverter.ToInt16(rle[i..]);
            var y = BitConverter.ToInt16(rle[(i + 2)..]);
            i += 4;
            var start = npixels;
            if (!encoded)
            {
                var n = Math.Min(scanlen, rle.Length - i);
                if (pixels is not null)
                {
                    rle.Slice(i, n).CopyTo(pixels.AsSpan(npixels));
                }

                npixels += n;
                i += n;
            }
            else
            {
                for (var b = 0; b < scanlen && i < rle.Length;)
                {
                    int bcnt = rle[i++];
                    var repeat = (bcnt & 1) != 0;
                    bcnt >>= 1;
                    if (bcnt == 0)
                    {
                        break; // Exult would loop forever
                    }

                    if (repeat)
                    {
                        if (i >= rle.Length)
                        {
                            break;
                        }

                        if (pixels is not null)
                        {
                            pixels.AsSpan(npixels, bcnt).Fill(rle[i]);
                        }

                        i++;
                    }
                    else
                    {
                        bcnt = Math.Min(bcnt, rle.Length - i);
                        if (pixels is not null)
                        {
                            rle.Slice(i, bcnt).CopyTo(pixels.AsSpan(npixels));
                        }

                        i += bcnt;
                    }

                    npixels += bcnt;
                    b += bcnt;
                }
            }

            if (npixels > start)
            {
                if (scans is not null)
                {
                    scans[nscans] = new Scan(x, y, (short)(npixels - start), start);
                }

                nscans++;
            }
        }

        return (nscans, npixels);
    }

    /// <summary>
    /// Exult <c>Shape_frame::reflect</c>, which makes the frames with bit 5
    /// set: the frame transposed across the NW-SE line into a square of the
    /// larger dimension filled with 255, then scanned again with 255 as
    /// transparent (so index 255 drops out), with the extents swapped.
    /// </summary>
    public ShapeFrame Reflect()
    {
        var xleft = YAbove;
        var yabove = XLeft;
        // The square holds the visual bounds (a version 3 frame may overhang its extents).
        var (vLeft, vAbove, vRight, vBelow) = VisualBounds();
        var side = Math.Max(Math.Max(Width, Height), Math.Max(vLeft + vRight + 1, vAbove + vBelow + 1));
        if (side <= 0)
        {
            return new ShapeFrame(xleft, yabove, YBelow, XRight, true, [], [], scale16: Scale16);
        }

        // The hotspot's row and column in the square: the overhang's room above and left of it.
        var hx = Math.Max(xleft, vAbove);
        var hy = Math.Max(yabove, vLeft);
        side = Math.Max(side, Math.Max(hx + vBelow + 1, hy + vRight + 1));
        var buf = new byte[side * side];
        Array.Fill(buf, Transparent);
        var at = new int[side * side];
        foreach (var scan in Scans)
        {
            var col = hx + scan.Y;
            if ((uint)col >= (uint)side)
            {
                continue;
            }

            for (var k = 0; k < scan.Length; k++)
            {
                var row = hy + scan.X + k;
                if ((uint)row < (uint)side)
                {
                    buf[row * side + col] = Pixels[scan.Offset + k];
                    at[row * side + col] = scan.Offset + k;
                }
            }
        }

        var scans = new List<Scan>();
        var pixels = new List<byte>();
        var mix = Mix is null ? null : new List<byte>();
        var colour = Colour is null ? null : new List<byte>();
        var alpha = Alpha is null ? null : new List<byte>();
        var normal = Normal is null ? null : new List<byte>();
        var height = Height8 is null ? null : new List<byte>();
        var emissive = Emissive is null ? null : new List<byte>();
        for (var row = 0; row < side; row++)
        {
            var line = buf.AsSpan(row * side, side);
            for (var x = 0; x < side;)
            {
                if (line[x] == Transparent)
                {
                    x++;
                    continue;
                }

                var end = x;
                while (end < side && line[end] != Transparent)
                {
                    end++;
                }

                scans.Add(new Scan((short)(x - hx), (short)(row - hy), (short)(end - x), pixels.Count));
                for (var c = x; c < end; c++)
                {
                    var src = at[row * side + c];
                    pixels.Add(Pixels[src]);
                    mix?.AddRange(Mix.AsSpan(src * 2, 2));
                    colour?.AddRange(Colour.AsSpan(src * 4, 4));
                    alpha?.Add(Alpha![src]);
                    normal?.AddRange(Normal.AsSpan(src * 2, 2));
                    height?.Add(Height8![src]);
                    emissive?.Add(Emissive![src]);
                }

                x = end;
            }
        }

        return new ShapeFrame(xleft, yabove, YBelow, XRight, true, scans.ToArray(), pixels.ToArray(), mix?.ToArray(), colour?.ToArray(),
            alpha?.ToArray(), Scale16, normal?.ToArray(), height?.ToArray(), emissive?.ToArray());
    }

    /// <summary>
    /// The band <paramref name="width"/> pixels deep along the inside of the
    /// frame's edge, as scans (their pixels unused): where hi-res art draws
    /// Exult's outline (<c>paint_rle_outline</c>, on a frame's edge pixels),
    /// following its smooth silhouette instead of the original's stairs.
    /// </summary>
    public ShapeFrame Outline(int width)
    {
        var (left, above, right, below) = VisualBounds();
        var w = Math.Max(1, left + right + 1);
        var h = Math.Max(1, above + below + 1);
        var mask = new bool[w * h];
        foreach (var scan in Scans)
        {
            var y = above + scan.Y;
            for (var k = 0; k < scan.Length && (uint)y < (uint)h; k++)
            {
                var x = left + scan.X + k;
                // The silhouette the classic painters show: pixels at least half opaque.
                if ((uint)x < (uint)w && (Alpha is null || Alpha[scan.Offset + k] >= Covered))
                {
                    mask[y * w + x] = true;
                }
            }
        }

        // Eroded by a (2 width + 1) square, a row pass then a column pass; outside the frame is empty.
        var across = new bool[w * h];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var all = true;
                for (var dx = -width; dx <= width && all; dx++)
                {
                    var xx = x + dx;
                    all = (uint)xx < (uint)w && mask[y * w + xx];
                }

                across[y * w + x] = all;
            }
        }

        var scans = new List<Scan>();
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w;)
            {
                if (!Ring(x, y))
                {
                    x++;
                    continue;
                }

                var end = x;
                while (end < w && Ring(end, y))
                {
                    end++;
                }

                scans.Add(new Scan((short)(x - left), (short)(y - above), (short)(end - x), 0));
                x = end;
            }
        }

        return new ShapeFrame(XLeft, YAbove, XRight, YBelow, true, scans.ToArray(), [], scale16: Scale16);

        bool Inside(int x, int y)
        {
            for (var dy = -width; dy <= width; dy++)
            {
                var yy = y + dy;
                if ((uint)yy >= (uint)h || !across[yy * w + x])
                {
                    return false;
                }
            }

            return true;
        }

        bool Ring(int x, int y) => mask[y * w + x] && !Inside(x, y);
    }

    /// <summary>
    /// Exult <c>Shape_frame::has_point</c>, relative to the hotspot: on a
    /// scan's row and within a pixel of it. A flat is tested against its
    /// extents as Exult does.
    /// </summary>
    public bool Covers(int x, int y)
    {
        if (!IsRle)
        {
            return x >= -XLeft && x < XRight && y >= -YAbove && y < YBelow;
        }

        foreach (var scan in Scans)
        {
            if (y == scan.Y && x >= scan.X - 1 && x <= scan.X + scan.Length)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The frame as an RGBA image of its visual bounds, with the hotspot at
    /// <see cref="ImageHotspot"/> (the extents' corner unless the frame
    /// overhangs them). The palette is 256 RGB entries (opaque) or 256 RGBA
    /// entries; mixed pixels blend their two, and true-colour ones
    /// (<see cref="Colour"/>) show their colour; a frame with straight alpha
    /// (<see cref="Alpha"/>) is as opaque as it says.
    /// </summary>
    public Image ToImage(ReadOnlySpan<byte> palette)
    {
        var stride = palette.Length switch
        {
            768 => 3,
            1024 => 4,
            _ => throw new ArgumentException("palette must be 256 RGB or RGBA entries", nameof(palette))
        };
        var (left, above, right, below) = VisualBounds();
        var w = Math.Max(1, left + right + 1);
        var h = Math.Max(1, above + below + 1);
        var rgba = new byte[w * h * 4];
        foreach (var scan in Scans)
        {
            var y = above + scan.Y;
            if ((uint)y >= (uint)h)
            {
                continue;
            }

            for (var k = 0; k < scan.Length; k++)
            {
                var x = left + scan.X + k;
                if ((uint)x >= (uint)w)
                {
                    continue;
                }

                var o = (y * w + x) * 4;
                var at = scan.Offset + k;
                var opacity = Alpha is null ? (byte)255 : Alpha[at];
                if (Colour is not null && Colour[at * 4 + 3] != 0)
                {
                    Colour.AsSpan(at * 4, 3).CopyTo(rgba.AsSpan(o, 3));
                    rgba[o + 3] = opacity;
                    continue;
                }

                var p = Pixels[at] * stride;
                var t = Mix is null ? 0 : Mix[at * 2 + 1];
                var q = Mix is null ? p : Mix[at * 2] * stride;
                for (var c = 0; c < 4; c++)
                {
                    int a = c < 3 || stride == 4 ? palette[p + c] : 255;
                    int b = c < 3 || stride == 4 ? palette[q + c] : 255;
                    rgba[o + c] = (byte)(a + ((b - a) * t + 127) / 255);
                }

                rgba[o + 3] = (byte)(rgba[o + 3] * opacity / 255);
            }
        }

        return Image.CreateFromData(w, h, false, Image.Format.Rgba8, rgba);
    }

    /// <summary>Where the hotspot lies in <see cref="ToImage"/>'s picture: the visual bounds' left and above.</summary>
    public (int X, int Y) ImageHotspot()
    {
        var (left, above, _, _) = VisualBounds();
        return (left, above);
    }
}
