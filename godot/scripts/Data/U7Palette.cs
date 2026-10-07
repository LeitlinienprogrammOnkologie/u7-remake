using System.IO;

namespace U7.Data;

/// <summary>
/// The VGA palettes of <c>PALETTES.FLX</c> (Exult <c>Palette</c>, palette.cc):
/// 256 colours of three 6-bit values (0-63), entry 0 the day palette.
/// </summary>
public static class U7Palette
{
    public const int Day = 0;

    static FlexFile? _flex;

    /// <summary>
    /// <c>PALETTES.FLX</c> entry <paramref name="n"/> as stored, 6-bit, or
    /// null when the entry is missing (Exult <c>set_loaded</c> keeps the old
    /// palette then). Black Gate's are all 768-byte single palettes.
    /// </summary>
    public static byte[]? Raw6(int n)
    {
        if (_flex is null)
        {
            var path = Path.Combine(U7.Core.U7Paths.StaticDir, "PALETTES.FLX");
            if (!File.Exists(path))
            {
                return null;
            }

            _flex = new FlexFile(path);
        }

        var pal = _flex.Get(n);
        return pal.Length >= 768 ? pal[..768].ToArray() : null;
    }

    /// <summary>
    /// Exult <c>Image_window8::set_palette</c> at normal brightness: each
    /// value through <c>Get_color8</c> (imagewin/iwin8.cc), v·255/63, capped
    /// at 255 (the day palette's index 255 holds values above 63).
    /// </summary>
    public static byte[] ToRgb(ReadOnlySpan<byte> raw6)
    {
        var rgb = new byte[768];
        for (var i = 0; i < rgb.Length && i < raw6.Length; i++)
        {
            rgb[i] = (byte)Math.Min(255, raw6[i] * 255 / 63);
        }

        return rgb;
    }

    /// <summary>The day palette as the window shows it, 8-bit RGB (black when PALETTES.FLX is missing).</summary>
    public static byte[] DayRgb() => Raw6(Day) is { } raw ? ToRgb(raw) : new byte[768];

    /// <summary>
    /// Exult <c>Palette::find_color</c>: the index below <paramref name="last"/>
    /// (by default not the rotating colours from 0xE0) nearest to the 6-bit
    /// colour, by squared distance, the first on a tie; -1 if none.
    /// </summary>
    public static int FindColor(ReadOnlySpan<byte> raw6, int r, int g, int b, int last = 0xE0)
    {
        var best = -1;
        var bestDistance = 0xfffffffL;
        for (var i = 0; i < last; i++)
        {
            long dr = r - raw6[3 * i];
            long dg = g - raw6[3 * i + 1];
            long db = b - raw6[3 * i + 2];
            var distance = dr * dr + dg * dg + db * db;
            if (distance < bestDistance)
            {
                best = i;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>
    /// The day palette as RGBA with the translucent colours as their blend
    /// colour and alpha, an approximation of Exult's <c>paint_rle_translucent</c>
    /// for painting into RGBA textures.
    /// </summary>
    public static byte[] DayRgbaTranslucent()
    {
        var rgb = DayRgb();
        var rgba = new byte[1024];
        for (var i = 0; i < 256; i++)
        {
            if (i is >= XformTables.FirstTranslucent and <= 0xFE)
            {
                XformTables.Blends.Slice((i - XformTables.FirstTranslucent) * 4, 4).CopyTo(rgba.AsSpan(i * 4));
                continue;
            }

            rgba[i * 4] = rgb[i * 3];
            rgba[i * 4 + 1] = rgb[i * 3 + 1];
            rgba[i * 4 + 2] = rgb[i * 3 + 2];
            rgba[i * 4 + 3] = 255;
        }

        return rgba;
    }
}
