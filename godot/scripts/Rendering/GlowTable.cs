using Godot;
using U7.Data;

namespace U7.Rendering;

/// <summary>How a light source looks as a pool of light (cosmetic; Exult only switches palettes).</summary>
public readonly record struct Glow(GlowKind Kind, float RadiusTiles, float Intensity, Vector3 Colour, bool Small = false);

public enum GlowKind
{
    /// <summary>An even light: lamp posts, the light pools.</summary>
    Steady,
    /// <summary>A flickering flame: candles, sconces, torches, fires, the burning weapons.</summary>
    Flame,
    /// <summary>A slow pulse: the wisp.</summary>
    Magic,
    /// <summary>Lightning (shape 179), on only in its bright frames.</summary>
    Flash
}

/// <summary>
/// The look of each light source: its kind, radius, intensity and colour
/// from its Exult brightness (<see cref="U7.World.LightSources.Brightness"/>).
/// </summary>
public static class GlowTable
{
    public static readonly Vector3 FlameColour = new(1f, 0.78f, 0.5f);
    public static readonly Vector3 CandleColour = new(1f, 0.82f, 0.58f);
    static readonly Vector3 MagicColour = new(0.72f, 0.86f, 1f);
    static readonly Vector3 FlashColour = new(0.85f, 0.9f, 1f);
    static readonly Vector3 NeutralColour = new(1f, 1f, 1f);
    /// <summary>Pools are this much wider than 1.5 + 1.8·√brightness tiles (the user's pick of 0.8, 1 and 1.3).</summary>
    const float RadiusScale = 1.3f;
    /// <summary>The radius in pixels of the disc the 198 frames cut from.</summary>
    const int PoolDiscRadius = 31;

    /// <summary>The lamp post: the light comes from the lamp at the top of its frame.</summary>
    public const int LampPost = 526;

    /// <summary>
    /// The light pools: translucent discs (440) and discs cut by a wall (198)
    /// that lit the ground in the originals, Exult light sources of a
    /// candle's strength. The world doesn't draw them; their light is the
    /// world shader's.
    /// </summary>
    public static bool IsPool(int shape) => shape is 198 or 440;

    /// <summary>
    /// The pool a lamp post's usecode lays on the street when lit (440). The
    /// lamp's own light covers it, so it gets no light of its own.
    /// </summary>
    public static bool IsLampPool(int shape) => shape == 440;

    public static Glow For(int shape, int brightness)
    {
        var radius = (1.5f + 1.8f * Mathf.Sqrt(brightness)) * RadiusScale;
        var intensity = Mathf.Min(1f, 0.55f + 0.07f * brightness);
        return shape switch
        {
            338 => new Glow(GlowKind.Flame, radius, intensity, CandleColour, Small: true),
            435 or 701 or 825 or 739 or 442 or 551 or 553 or 895 => new Glow(GlowKind.Flame, radius, intensity, FlameColour),
            534 => new Glow(GlowKind.Magic, radius, intensity, MagicColour),
            179 => new Glow(GlowKind.Flash, radius, intensity, FlashColour),
            198 => new Glow(GlowKind.Steady, radius, intensity, NeutralColour),
            _ => new Glow(GlowKind.Steady, radius, intensity, CandleColour)
        };
    }

    /// <summary>
    /// Where a light sits, from the frame's hotspot: the centre of the frame,
    /// a lamp post's lamp near the top left (where its pole rises to), and a
    /// cut pool's disc centre, which the frame's straight edge passes near.
    /// </summary>
    public static Vector2 LightOffset(int shape, int frame, ShapeRecord info)
    {
        var fi = info.GetFrame(frame & 31);
        float ox = fi.Width / 2f;
        float oy = fi.Height / 2f;
        if (shape == LampPost)
        {
            ox = fi.Width * 0.2f;
            oy = fi.Height * 0.2f;
        }
        else if (shape == 198)
        {
            // Frames 0-3 have their flat side at the bottom, right, top and left; 4 is a sliver off a disc's bottom.
            (ox, oy) = (frame & 31) switch
            {
                0 => (ox, PoolDiscRadius),
                1 => (PoolDiscRadius, oy),
                2 or 4 => (ox, fi.Height - PoolDiscRadius),
                3 => (fi.Width - PoolDiscRadius, oy),
                _ => (ox, oy)
            };
        }

        var d = new Vector2(ox - fi.XLeft, oy - fi.YAbove);
        // Exult's reflected frames (bit 5) are transposed about the hotspot.
        return (frame & 32) != 0 ? new Vector2(d.Y, d.X) : d;
    }
}
