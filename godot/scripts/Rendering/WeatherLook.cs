using Godot;

namespace U7.Rendering;

/// <summary>
/// How the weather is drawn (cosmetic; when and how much is Exult's, see
/// <c>World/Weather.cs</c>). Exult's own look is the starting point: rain is
/// pale blue pixels (SPRITES.VGA shape 0, frames 3-7), snow white to grey
/// pixels wobbling in a 5×5 box (13-20), sparkles white pixels fading to grey
/// (21-27), clouds shape 2 in colour 253, which blends the ground halfway
/// towards a dark brown, and the OVERCAST and FOG palettes.
/// </summary>
public static class WeatherLook
{
    /// <summary>
    /// Rain streaks along +x+y: length and width in world pixels (a width of
    /// 0 is one screen pixel), alpha, how many per Exult drop, and the
    /// strength of a splash ring on Exult's splash frame (7), 0 for none.
    /// </summary>
    public readonly record struct RainLook(float Length, float Width, float Alpha, float Density, float Splash);

    /// <summary>Snowflakes: soft dots of this size in world pixels, alpha, sway in pixels, how many per Exult flake.</summary>
    public readonly record struct SnowLook(float Size, float Alpha, float Sway, float Density);

    /// <summary>Sparkles: additive glints of this size in world pixels, at this brightness, how many per Exult sparkle.</summary>
    public readonly record struct SparkleLook(float Size, float Brightness, float Density);

    /// <summary>
    /// Cloud shadows: how dark at most (1 is Exult's colour 253, a third
    /// darker and a little warmer), the size of a cloud in world pixels, how
    /// much of the sky one cloud covers, and how soft the edges are.
    /// </summary>
    public readonly record struct CloudLook(float Shadow, float Scale, float CoverPerCloud, float Softness);

    /// <summary>
    /// Fog: how far towards Exult's FOG palette the day goes (1 is Exult's),
    /// how thick the drifting mist is, its size in world pixels, its drift in
    /// pixels a second.
    /// </summary>
    public readonly record struct FogLook(float Palette, float Mist, float Scale, float Drift);

    /// <summary>Overcast: how far towards Exult's OVERCAST palette (1 is Exult's), and how much greyer still (0 none).</summary>
    public readonly record struct OvercastLook(float Palette, float Grey);

    // The user's picks (2026-10-08) from shots of Trinsic by day and night, each of three:
    /// <summary>Streaks with faint splashes (of a fine 1-pixel drizzle and a 1.6× downpour).</summary>
    public static RainLook Rain { get; } = new(11f, 0.5f, 0.5f, 1f, 0.35f);
    /// <summary>Big flakes, twice Exult's count (of specks at Exult's count and soft flakes at 1.5×).</summary>
    public static SnowLook Snow { get; } = new(3.8f, 0.9f, 3.5f, 2f);
    /// <summary>Large glints at full brightness (of 3 px and 5 px ones).</summary>
    public static SparkleLook Sparkles { get; } = new(8f, 1f, 1f);
    /// <summary>Exult's cloud size and darkness, fairly crisp (of larger and softer at 0.7, and very large at 0.45).</summary>
    public static CloudLook Clouds { get; } = new(1f, 130f, 0.09f, 0.06f);
    /// <summary>70% of Exult's FOG palette with drifting mist (of the full palette with a little mist, and 40% with thick mist).</summary>
    public static FogLook Fog { get; } = new(0.7f, 0.4f, 220f, 6f);
    /// <summary>Exult's OVERCAST palette, 30% greyer (of Exult's alone, and grey alone).</summary>
    public static OvercastLook Overcast { get; } = new(1f, 0.3f);

    /// <summary>Exult colour 253's blend, (56, 40, 32) at half strength, as a multiplier on an average day colour.</summary>
    public static readonly Vector3 CloudTint = new(0.68f, 0.64f, 0.6f);

    /// <summary>The most particles one effect shows, whatever the view's size.</summary>
    public const int MaxParticles = 1600;
}
