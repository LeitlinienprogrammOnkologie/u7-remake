using Godot;
using U7.Data;

namespace U7.Rendering;

/// <summary>
/// Modern colour (docs/RENDERER.md 5.5, R5): how the palettes and the
/// translucency tables change a true-colour pixel, as 3D lookup tables over
/// RGB rather than a rule on the pixel's anchor. Each cell of the
/// <see cref="Size"/>-cubed grid takes the nearest palette entry below
/// Exult's cycling ranges as its anchor and evaluates today's rule there
/// (<see cref="PaletteSlopes"/>: <c>palette[anchor] + M (rgb - day[anchor])</c>;
/// <see cref="XformTables.TurnColour"/> for a table), so the table is the
/// anchor rule made continuous: a soft pixel blended over the ground no
/// longer carries an anchor, and a palette's own colours map to within the
/// grid's interpolation error (<see cref="Error"/>), which is the seam
/// between a hi-res frame and an original pixel beside it. The palettes'
/// tables are rebuilt when the palettes change (every minute of a
/// transition, about a millisecond); the translucency tables' once.
/// </summary>
public sealed class ColourLut
{
    /// <summary>The entries a colour can be anchored to: those below Exult's cycling ranges.</summary>
    const int Anchors = PaletteSlopes.Anchors;

    readonly byte[] _day;
    readonly byte[] _anchor;
    readonly byte[] _ambient;
    readonly byte[] _lit;
    readonly Godot.Collections.Array<Image> _ambientSlices = [];
    readonly Godot.Collections.Array<Image> _litSlices = [];

    /// <summary>Cells along each axis (32, or 64 as the fallback for a seam that shows).</summary>
    public int Size { get; }

    /// <summary>The ambient and lit palettes' tables, RGBA8 3D textures of <see cref="Size"/> cubed, sampled trilinearly.</summary>
    public ImageTexture3D AmbientTexture { get; }
    public ImageTexture3D LitTexture { get; }

    /// <summary>Milliseconds the last <see cref="Build"/> took.</summary>
    public double LastBuildMs { get; private set; }

    public ColourLut(ReadOnlySpan<byte> dayRgb, int size)
    {
        Size = size;
        _day = dayRgb[..768].ToArray();
        _anchor = Anchors3D(_day, size);
        _ambient = new byte[size * size * size * 4];
        _lit = new byte[size * size * size * 4];
        for (var z = 0; z < size; z++)
        {
            _ambientSlices.Add(Image.CreateFromData(size, size, false, Image.Format.Rgba8, _ambient.AsSpan(z * size * size * 4, size * size * 4).ToArray()));
            _litSlices.Add(Image.CreateFromData(size, size, false, Image.Format.Rgba8, _lit.AsSpan(z * size * size * 4, size * size * 4).ToArray()));
        }

        AmbientTexture = new ImageTexture3D();
        AmbientTexture.Create(Image.Format.Rgba8, size, size, size, false, _ambientSlices);
        LitTexture = new ImageTexture3D();
        LitTexture.Create(Image.Format.Rgba8, size, size, size, false, _litSlices);
    }

    /// <summary>The grid's colour along an axis: cell i of <paramref name="size"/> is <c>i * 255 / (size - 1)</c>, so the corners are 0 and 255.</summary>
    static int Level(int i, int size) => (i * 255 + (size - 1) / 2) / (size - 1);

    /// <summary>Each cell's anchor: the palette entry below the cycling ranges nearest its colour (RGB distance).</summary>
    static byte[] Anchors3D(ReadOnlySpan<byte> day, int size)
    {
        var anchors = new byte[size * size * size];
        for (var z = 0; z < size; z++)
        {
            var b = Level(z, size);
            for (var y = 0; y < size; y++)
            {
                var g = Level(y, size);
                for (var x = 0; x < size; x++)
                {
                    var r = Level(x, size);
                    var best = 0;
                    var bestD = int.MaxValue;
                    for (var i = 0; i < Anchors; i++)
                    {
                        var dr = r - day[i * 3];
                        var dg = g - day[i * 3 + 1];
                        var db = b - day[i * 3 + 2];
                        var d = dr * dr + dg * dg + db * db;
                        if (d < bestD)
                        {
                            bestD = d;
                            best = i;
                        }
                    }

                    anchors[(z * size + y) * size + x] = (byte)best;
                }
            }
        }

        return anchors;
    }

    /// <summary>
    /// The tables for an ambient and a lit palette (8-bit RGB, before
    /// rotation) from the slopes fitted for them (<see cref="PaletteSlopes.Build"/>
    /// first): row 1 of the slopes is the ambient palette's, row 4 the lit one's.
    /// </summary>
    public void Build(PaletteSlopes slopes, ReadOnlySpan<byte> ambientRgb, ReadOnlySpan<byte> litRgb)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        Fill(_ambient, slopes, 1, ambientRgb);
        Fill(_lit, slopes, 4, litRgb);
        var sliceBytes = Size * Size * 4;
        for (var z = 0; z < Size; z++)
        {
            _ambientSlices[z].SetData(Size, Size, false, Image.Format.Rgba8, _ambient.AsSpan(z * sliceBytes, sliceBytes).ToArray());
            _litSlices[z].SetData(Size, Size, false, Image.Format.Rgba8, _lit.AsSpan(z * sliceBytes, sliceBytes).ToArray());
        }

        AmbientTexture.Update(_ambientSlices);
        LitTexture.Update(_litSlices);
        LastBuildMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }

    void Fill(byte[] lut, PaletteSlopes slopes, int row, ReadOnlySpan<byte> palette)
    {
        var size = Size;
        for (var z = 0; z < size; z++)
        {
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var cell = (z * size + y) * size + x;
                    var a = _anchor[cell];
                    float dr = Level(x, size) - _day[a * 3];
                    float dg = Level(y, size) - _day[a * 3 + 1];
                    float db = Level(z, size) - _day[a * 3 + 2];
                    for (var c = 0; c < 3; c++)
                    {
                        var v = palette[a * 3 + c] + slopes.Slope(row, a, c, 0) * dr + slopes.Slope(row, a, c, 1) * dg + slopes.Slope(row, a, c, 2) * db;
                        lut[cell * 4 + c] = (byte)Math.Clamp(v + 0.5f, 0f, 255f);
                    }

                    lut[cell * 4 + 3] = 255;
                }
            }
        }
    }

    /// <summary>
    /// The translucency tables' lookup tables for a compositor (RGBA8, a
    /// uint a cell, <see cref="XformTables.Count"/> tables of
    /// <paramref name="size"/> cubed back to back): a cell's colour seen
    /// through a table, as <see cref="XformTables.TurnColour"/> turns it
    /// from the cell's anchor.
    /// </summary>
    public static uint[] XformLuts(XformTables xforms, int size)
    {
        var day = xforms.Day;
        var anchors = Anchors3D(day, size);
        var cells = size * size * size;
        var luts = new uint[XformTables.Count * cells];
        Span<byte> rgb = stackalloc byte[3];
        for (var t = 0; t < XformTables.Count; t++)
        {
            var table = xforms[t];
            for (var z = 0; z < size; z++)
            {
                for (var y = 0; y < size; y++)
                {
                    for (var x = 0; x < size; x++)
                    {
                        var cell = (z * size + y) * size + x;
                        var a = anchors[cell];
                        rgb[0] = (byte)Level(x, size);
                        rgb[1] = (byte)Level(y, size);
                        rgb[2] = (byte)Level(z, size);
                        xforms.TurnColour(t, a, table[a], rgb);
                        luts[t * cells + cell] = rgb[0] | (uint)rgb[1] << 8 | (uint)rgb[2] << 16 | 0xFF000000u;
                    }
                }
            }
        }

        return luts;
    }

    /// <summary>
    /// The translucency tables' lookup tables as one 3D texture for a canvas
    /// shader (the gumps', R8): <see cref="XformTables.Count"/> tables of
    /// <paramref name="size"/> cubed stacked along z, table t's cells at
    /// depths t * size to t * size + size - 1, sampled trilinearly within a table.
    /// </summary>
    public static ImageTexture3D XformTexture(XformTables xforms, int size)
    {
        var luts = XformLuts(xforms, size);
        var slices = new Godot.Collections.Array<Image>();
        var cells = size * size;
        var bytes = new byte[cells * 4];
        for (var z = 0; z < XformTables.Count * size; z++)
        {
            for (var i = 0; i < cells; i++)
            {
                var v = luts[z * cells + i];
                bytes[i * 4] = (byte)v;
                bytes[i * 4 + 1] = (byte)(v >> 8);
                bytes[i * 4 + 2] = (byte)(v >> 16);
                bytes[i * 4 + 3] = 255;
            }

            slices.Add(Image.CreateFromData(size, size, false, Image.Format.Rgba8, bytes));
        }

        var texture = new ImageTexture3D();
        texture.Create(Image.Format.Rgba8, size, size, XformTables.Count * size, false, slices);
        return texture;
    }

    /// <summary>
    /// How far the table strays from the palette on the palette's own
    /// colours: the ambient (or lit) table sampled trilinearly at each
    /// anchorable entry's day colour against the entry's palette colour, in
    /// 8-bit levels (mean and largest over the channels and entries). The
    /// seam between an original pixel and a hi-res one of the same colour.
    /// </summary>
    public (double Mean, int Max) Error(ReadOnlySpan<byte> palette, bool lit)
    {
        var lut = lit ? _lit : _ambient;
        double sum = 0;
        var max = 0;
        var n = 0;
        for (var i = 0; i < Anchors; i++)
        {
            var c = Sample(lut, _day[i * 3], _day[i * 3 + 1], _day[i * 3 + 2]);
            for (var k = 0; k < 3; k++)
            {
                var e = Math.Abs((int)Math.Round(c[k]) - palette[i * 3 + k]);
                sum += e;
                max = Math.Max(max, e);
                n++;
            }
        }

        return (sum / n, max);
    }

    /// <summary>The table sampled trilinearly at an 8-bit colour, as the shader samples the texture.</summary>
    float[] Sample(byte[] lut, int r, int g, int b)
    {
        var size = Size;
        var scale = (size - 1) / 255f;
        var (fx, fy, fz) = (r * scale, g * scale, b * scale);
        var (x0, y0, z0) = ((int)fx, (int)fy, (int)fz);
        var (x1, y1, z1) = (Math.Min(x0 + 1, size - 1), Math.Min(y0 + 1, size - 1), Math.Min(z0 + 1, size - 1));
        var (tx, ty, tz) = (fx - x0, fy - y0, fz - z0);
        var result = new float[3];
        for (var c = 0; c < 3; c++)
        {
            float At(int x, int y, int z) => lut[((z * size + y) * size + x) * 4 + c];
            var c00 = At(x0, y0, z0) * (1 - tx) + At(x1, y0, z0) * tx;
            var c10 = At(x0, y1, z0) * (1 - tx) + At(x1, y1, z0) * tx;
            var c01 = At(x0, y0, z1) * (1 - tx) + At(x1, y0, z1) * tx;
            var c11 = At(x0, y1, z1) * (1 - tx) + At(x1, y1, z1) * tx;
            result[c] = (c00 * (1 - ty) + c10 * ty) * (1 - tz) + (c01 * (1 - ty) + c11 * ty) * tz;
        }

        return result;
    }
}
