using System.IO;

namespace U7.Data;

/// <summary>
/// Exult's translucency tables (<c>Shape_manager::load</c>, shapeid.cc): for
/// each of the 17 translucent colours 0xEE-0xFE, a 256-entry table giving
/// the index a pixel turns into when seen through that colour. Table
/// <c>i</c> serves index <c>0xEE + i</c> (<c>paint_rle_translucent</c>).
/// </summary>
public sealed class XformTables
{
    public const int Count = 17;
    /// <summary>Exult's <c>0xff - xfcnt</c>: the first index painted through a table.</summary>
    public const int FirstTranslucent = 0xFF - Count;

    /// <summary>
    /// Exult's <c>hard_blends</c> (the values of its blends.dat): colour and
    /// alpha of the translucent colours in table order, for building the
    /// tables when XFORM.TBL is missing.
    /// </summary>
    public static ReadOnlySpan<byte> Blends =>
    [
        208, 216, 224, 192, 136, 44, 148, 198, 248, 252, 80, 211,
        144, 148, 252, 247, 64, 216, 64, 201, 204, 60, 84, 140,
        144, 40, 192, 128, 96, 40, 16, 128, 100, 108, 116, 192,
        68, 132, 28, 128, 255, 208, 48, 64, 28, 52, 255, 128,
        8, 68, 0, 128, 255, 8, 8, 118, 255, 244, 248, 128,
        56, 40, 32, 128, 228, 224, 214, 82
    ];

    readonly byte[] _tables;
    /// <summary>The day palette (8-bit RGB) the tables' colours are fitted in.</summary>
    readonly byte[] _day;
    /// <summary>Per table, the slopes (3x3, row-major) of the affine map that fits it best (<see cref="TurnColour"/>).</summary>
    readonly float[] _slopes = new float[Count * 9];

    XformTables(byte[] tables)
    {
        _tables = tables;
        _day = U7Palette.DayRgb();
        FitSlopes();
    }

    /// <summary>Exult <c>xforms[index]</c>, the table for colour <c>FirstTranslucent + index</c>.</summary>
    public ReadOnlySpan<byte> this[int index] => _tables.AsSpan(index * 256, 256);

    /// <summary>All tables back to back: entry <c>(pix - FirstTranslucent) * 256 + dest</c>.</summary>
    public ReadOnlySpan<byte> All => _tables;

    /// <summary>The day palette (8-bit RGB) the colour slopes are fitted in.</summary>
    public ReadOnlySpan<byte> Day => _day;

    /// <summary>Per table, the 3x3 slopes (row-major) of <see cref="TurnColour"/>, for a backend doing its math elsewhere.</summary>
    public ReadOnlySpan<float> Slopes => _slopes;

    /// <summary>Exult <c>invis_xform</c>, XFORM.TBL's entry 0: invisible actors are painted through it.</summary>
    public ReadOnlySpan<byte> Invisible => this[InvisibleTable];

    /// <summary>The table of <see cref="Invisible"/>.</summary>
    public const int InvisibleTable = Count - 1;

    /// <summary>
    /// A true-colour pixel (the remake's, <c>ArtLook.TrueColour</c>) seen
    /// through table <paramref name="table"/>: its anchor <paramref name="from"/>
    /// turns into <paramref name="to"/> (the table's entry, as an index pixel
    /// does), and its colour <paramref name="rgb"/> into <c>day[to] + M (rgb - day[from])</c>,
    /// M the slopes of the affine map fitting the table. A pixel of its
    /// anchor's own colour becomes exactly the index pixel's colour; the art's
    /// finer shades carry through as the table changes colours near it.
    /// (XFORM.TBL's tables aren't Exult's <c>hard_blends</c>: a blend colour
    /// and alpha miss them by 20 to 80 levels on average.)
    /// </summary>
    public void TurnColour(int table, int from, int to, Span<byte> rgb)
    {
        var m = _slopes.AsSpan(table * 9, 9);
        float dr = rgb[0] - _day[from * 3];
        float dg = rgb[1] - _day[from * 3 + 1];
        float db = rgb[2] - _day[from * 3 + 2];
        for (var c = 0; c < 3; c++)
        {
            var v = _day[to * 3 + c] + m[c * 3] * dr + m[c * 3 + 1] * dg + m[c * 3 + 2] * db;
            rgb[c] = (byte)Math.Clamp(v + 0.5f, 0f, 255f);
        }
    }

    /// <summary>Each table's slopes, by least squares over the colours that don't cycle (true colour's anchors).</summary>
    void FitSlopes()
    {
        const int anchors = 0xE0;
        var to = new byte[anchors * 3];
        var weights = new double[anchors];
        Array.Fill(weights, 1.0);
        for (var t = 0; t < Count; t++)
        {
            for (var i = 0; i < anchors; i++)
            {
                var j = _tables[t * 256 + i];
                to[i * 3] = _day[j * 3];
                to[i * 3 + 1] = _day[j * 3 + 1];
                to[i * 3 + 2] = _day[j * 3 + 2];
            }

            ColourFit.Slopes(_day, to, weights, anchors, _slopes.AsSpan(t * 9, 9));
        }
    }

    /// <summary>XFORM.TBL when the game has it, else the tables built from the blends.</summary>
    public static XformTables Load()
    {
        var path = Path.Combine(U7.Core.U7Paths.StaticDir, "XFORM.TBL");
        if (File.Exists(path))
        {
            return FromFile(new FlexFile(path));
        }

        return FromBlends(U7Palette.Raw6(U7Palette.Day) ?? new byte[768]);
    }

    /// <summary>
    /// XFORM.TBL is a FLEX of 256-byte tables stored in reverse: entry
    /// <c>i</c> is table <c>16 - i</c>. An empty entry is the identity.
    /// </summary>
    public static XformTables FromFile(FlexFile file)
    {
        var tables = new byte[Count * 256];
        for (var i = 0; i < Math.Min(file.Count, Count); i++)
        {
            var table = tables.AsSpan((Count - 1 - i) * 256, 256);
            var entry = file.Get(i);
            if (entry.Length < 256)
            {
                for (var j = 0; j < 256; j++)
                {
                    table[j] = (byte)j;
                }
            }
            else
            {
                entry[..256].CopyTo(table);
            }
        }

        return new XformTables(tables);
    }

    /// <summary>
    /// Exult <c>Palette::create_trans_table</c> for each blend over the 6-bit
    /// day palette: the colour mixed with the blend colour (8-bit / 4) by
    /// the blend's alpha, then the nearest non-rotating index.
    /// </summary>
    public static XformTables FromBlends(ReadOnlySpan<byte> raw6)
    {
        var tables = new byte[Count * 256];
        for (var t = 0; t < Count; t++)
        {
            var br = Blends[4 * t] / 4;
            var bg = Blends[4 * t + 1] / 4;
            var bb = Blends[4 * t + 2] / 4;
            var alpha = (int)Blends[4 * t + 3];
            for (var i = 0; i < 256; i++)
            {
                var r = br * alpha / 255 + raw6[i * 3] * (255 - alpha) / 255;
                var g = bg * alpha / 255 + raw6[i * 3 + 1] * (255 - alpha) / 255;
                var b = bb * alpha / 255 + raw6[i * 3 + 2] * (255 - alpha) / 255;
                tables[t * 256 + i] = (byte)U7Palette.FindColor(raw6, r, g, b);
            }
        }

        return new XformTables(tables);
    }
}
