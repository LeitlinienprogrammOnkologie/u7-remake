using U7.Data;

namespace U7.Rendering;

/// <summary>
/// The palettes of <c>PALETTES.FLX</c> by Exult's names (palette.h), and
/// Exult's blend between two of them (<c>Palette::create_intermediate</c>).
/// Black Gate has no entry 9; a missing entry is the day palette.
/// </summary>
public sealed class PaletteSet
{
    public const int Day = 0;
    public const int Dusk = 1;
    /// <summary>Exult: "Think this is it." Dawn and dusk share entry 1.</summary>
    public const int Dawn = 1;
    public const int Night = 2;
    /// <summary>While the avatar is invisible.</summary>
    public const int Invisible = 3;
    /// <summary>Raining or overcast by day.</summary>
    public const int Overcast = 4;
    public const int Fog = 5;
    /// <summary>The light spell's.</summary>
    public const int Spell = 6;
    /// <summary>Warmer, for candles.</summary>
    public const int Candle = 7;
    /// <summary>When hit in combat.</summary>
    public const int Red = 8;
    /// <summary>Exult: "9 has lots of black".</summary>
    public const int Nine = 9;
    public const int Lightning = 10;
    public const int SingleLight = 11;
    public const int ManyLights = 12;
    public const int Count = 13;

    static readonly string[] Names =
    [
        "DAY", "DUSK/DAWN", "NIGHT", "INVISIBLE", "OVERCAST", "FOG", "SPELL",
        "CANDLE", "RED", "9", "LIGHTNING", "SINGLE_LIGHT", "MANY_LIGHTS"
    ];

    /// <summary>The 6-bit values of each palette as floats, for blending.</summary>
    readonly float[][] _pals = new float[Count][];
    readonly byte[] _night6;

    public PaletteSet()
    {
        var day = U7Palette.Raw6(Day) ?? new byte[768];
        for (var n = 0; n < Count; n++)
        {
            var raw = U7Palette.Raw6(n) ?? day;
            _pals[n] = new float[768];
            for (var i = 0; i < 768; i++)
            {
                _pals[n][i] = raw[i];
            }
        }

        _night6 = U7Palette.Raw6(Night) ?? day;
    }

    public static string Name(int pal) => (uint)pal < Count ? Names[pal] : pal.ToString();

    /// <summary>
    /// Exult <c>Palette::create_intermediate</c> into 8-bit RGB: per channel
    /// <c>from + (to - from) * t</c> on the 6-bit values, then <c>Get_color8</c>
    /// (Exult steps <paramref name="t"/> in whole sixtieths; any t works here).
    /// </summary>
    public void Blend(int from, int to, float t, Span<byte> rgb)
    {
        var a = _pals[from];
        var b = _pals[to];
        t = Math.Clamp(t, 0f, 1f);
        for (var i = 0; i < 768; i++)
        {
            var v = a[i] + (b[i] - a[i]) * t;
            rgb[i] = (byte)Math.Min(255f, v * 255f / 63f);
        }
    }

    /// <summary>A palette as the window shows it, 8-bit RGB.</summary>
    public void Get(int pal, Span<byte> rgb) => Blend(pal, pal, 0f, rgb);

    /// <summary>
    /// The darkness knob: NIGHT becomes Exult's night palette mixed by
    /// <paramref name="k"/> towards the day palette under the old blue night
    /// tint (0 is Exult's, 1 the old tint).
    /// </summary>
    public void SetNightTint(float k)
    {
        var day = _pals[Day];
        var night = _pals[Night];
        ReadOnlySpan<float> tint = [0.28f, 0.34f, 0.58f];
        for (var i = 0; i < 768; i++)
        {
            night[i] = _night6[i] + (day[i] * tint[i % 3] - _night6[i]) * k;
        }
    }
}
