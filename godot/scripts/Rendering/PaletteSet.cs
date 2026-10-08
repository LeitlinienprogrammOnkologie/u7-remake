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

    /// <summary>Exult <c>is_day_palette</c>: dawn (which is also dusk) and day, where overcast and fog show.</summary>
    public static bool IsDayPalette(int pal) => pal is Day or Dawn;

    /// <summary>
    /// Exult <c>Palette::create_intermediate</c> into 8-bit RGB: per channel
    /// <c>from + (to - from) * t</c> on the 6-bit values, then <c>Get_color8</c>
    /// (Exult steps <paramref name="t"/> in whole sixtieths; any t works here).
    /// </summary>
    public void Blend(int from, int to, float t, Span<byte> rgb) => Blend(from, to, t, default, rgb);

    /// <summary>
    /// <see cref="Blend(int, int, float, Span{byte})"/> between the palettes
    /// Exult's <c>get_final_palette</c> shows for each end in this weather:
    /// on the day palettes, OVERCAST, or FOG over it, each eased in by its
    /// weight; then the overcast's extra grey.
    /// </summary>
    public void Blend(int from, int to, float t, Weather weather, Span<byte> rgb)
    {
        var a = _pals[from];
        var b = _pals[to];
        var overcast = _pals[Overcast];
        var fog = _pals[Fog];
        var (oa, fa) = IsDayPalette(from) ? (weather.Overcast, weather.Fog) : (0f, 0f);
        var (ob, fb) = IsDayPalette(to) ? (weather.Overcast, weather.Fog) : (0f, 0f);
        t = Math.Clamp(t, 0f, 1f);
        var grey = weather.Grey * float.Lerp(oa, ob, t);
        Span<float> c = stackalloc float[3];
        for (var i = 0; i < 768; i += 3)
        {
            for (var k = 0; k < 3; k++)
            {
                var j = i + k;
                var va = float.Lerp(float.Lerp(a[j], overcast[j], oa), fog[j], fa);
                var vb = float.Lerp(float.Lerp(b[j], overcast[j], ob), fog[j], fb);
                c[k] = float.Lerp(va, vb, t);
            }

            if (grey > 0)
            {
                var lum = 0.299f * c[0] + 0.587f * c[1] + 0.114f * c[2];
                for (var k = 0; k < 3; k++)
                {
                    c[k] = float.Lerp(c[k], lum, grey);
                }
            }

            for (var k = 0; k < 3; k++)
            {
                rgb[i + k] = (byte)Math.Clamp(c[k] * 255f / 63f, 0f, 255f);
            }
        }
    }

    /// <summary>
    /// How far the day palettes go towards OVERCAST and FOG (0 to 1, eased
    /// in and out), and how much greyer the overcast makes them.
    /// </summary>
    public readonly record struct Weather(float Overcast, float Fog, float Grey);

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
