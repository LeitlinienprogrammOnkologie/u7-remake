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

    public int Width => XLeft + XRight + 1;
    /// <summary>Exult <c>Shape_frame::is_empty</c>: an RLE frame with no pixels.</summary>
    public bool IsEmpty => IsRle && Scans.Length == 0;
    public int Height => YAbove + YBelow + 1;

    ShapeFrame(int xleft, int yabove, int xright, int ybelow, bool rle, Scan[] scans, byte[] pixels)
    {
        XLeft = xleft;
        YAbove = yabove;
        XRight = xright;
        YBelow = ybelow;
        IsRle = rle;
        Scans = scans;
        Pixels = pixels;
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
        var side = Math.Max(Width, Height);
        if (side <= 0)
        {
            return new ShapeFrame(xleft, yabove, YBelow, XRight, true, [], []);
        }

        var buf = new byte[side * side];
        Array.Fill(buf, Transparent);
        foreach (var scan in Scans)
        {
            var col = xleft + scan.Y;
            if ((uint)col >= (uint)side)
            {
                continue;
            }

            for (var k = 0; k < scan.Length; k++)
            {
                var row = yabove + scan.X + k;
                if ((uint)row < (uint)side)
                {
                    buf[row * side + col] = Pixels[scan.Offset + k];
                }
            }
        }

        var scans = new List<Scan>();
        var pixels = new List<byte>();
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

                scans.Add(new Scan((short)(x - xleft), (short)(row - yabove), (short)(end - x), pixels.Count));
                foreach (var p in line[x..end])
                {
                    pixels.Add(p);
                }

                x = end;
            }
        }

        return new ShapeFrame(xleft, yabove, YBelow, XRight, true, scans.ToArray(), pixels.ToArray());
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
    /// The frame as an RGBA image with the hotspot at
    /// (<see cref="XLeft"/>, <see cref="YAbove"/>). The palette is 256 RGB
    /// entries (opaque) or 256 RGBA entries.
    /// </summary>
    public Image ToImage(ReadOnlySpan<byte> palette)
    {
        var stride = palette.Length switch
        {
            768 => 3,
            1024 => 4,
            _ => throw new ArgumentException("palette must be 256 RGB or RGBA entries", nameof(palette))
        };
        var w = Math.Max(1, Width);
        var h = Math.Max(1, Height);
        var rgba = new byte[w * h * 4];
        foreach (var scan in Scans)
        {
            var y = YAbove + scan.Y;
            if ((uint)y >= (uint)h)
            {
                continue;
            }

            for (var k = 0; k < scan.Length; k++)
            {
                var x = XLeft + scan.X + k;
                if ((uint)x >= (uint)w)
                {
                    continue;
                }

                var o = (y * w + x) * 4;
                var p = Pixels[scan.Offset + k] * stride;
                rgba[o] = palette[p];
                rgba[o + 1] = palette[p + 1];
                rgba[o + 2] = palette[p + 2];
                rgba[o + 3] = stride == 4 ? palette[p + 3] : (byte)255;
            }
        }

        return Image.CreateFromData(w, h, false, Image.Format.Rgba8, rgba);
    }
}
