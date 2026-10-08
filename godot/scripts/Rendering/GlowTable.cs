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
    Flash,
    /// <summary>A roofed room's light through a window (<see cref="WindowLights"/>), already flickering with the room's.</summary>
    Window
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

    /// <summary>The lightning missile (807) and the full-screen lightning sprite (17) light up in this colour.</summary>
    public static readonly Vector3 LightningColour = new(0.8f, 0.85f, 1f);
    public const int LightningMissile = 807;
    public const int LightningSprite = 17;

    /// <summary>
    /// How magic glows (cosmetic; Exult has none): <see cref="Glow"/> how far
    /// a glowing pixel shows its day colour at night, <see cref="Light"/> and
    /// <see cref="Radius"/> the light it casts round it, <see cref="Day"/> how
    /// much brighter than its colour a glowing pixel is, day and night, and
    /// <see cref="Haze"/> how much of its glow a see-through pixel gets.
    /// </summary>
    public readonly record struct MagicLook(float Glow, float Light, float Radius, float Day, float Haze = 1f);

    /// <summary>
    /// The user's picks (2026-10-08) from shots at night and noon: "strong"
    /// (of off, subtle, medium and strong), 0.6 brighter by day (of 0, 0.3
    /// and 0.6), see-through parts glowing fully (of 1, 0.4 and 0), and each
    /// thing's light in its own colour (not one cool white).
    /// </summary>
    public static MagicLook Magic { get; } = new(1f, 1.4f, 1.3f, 0.6f, 1f);

    /// <summary>
    /// SPRITES.VGA animations that glow: the teleport (7), the vortex (8),
    /// fireworks (12), bubbles (13), sparkles (16), beads (18), the sword
    /// strike (23) and the blasts; not the clouds (2), smoke (3), the poof
    /// (9) or the notes (24).
    /// </summary>
    public static bool IsGlowingSprite(int sprite) => sprite is 1 or 4 or 5 or 7 or 8 or 12 or 13 or 16 or 18 or 19 or 23;

    /// <summary>The explosion sprites, whose light flashes and dies away (Exult <c>Explosion_effect</c>).</summary>
    public static bool IsBlast(int sprite) => sprite is 1 or 4 or 5 or 19;

    /// <summary>Magic missiles that glow in flight: the fire, death and magic bolts, lightning, the starburst, the mist and the vortex.</summary>
    public static bool IsGlowingMissile(int shape) => shape is 856 or 807 or 527 or 417 or 565 or 399 or 639;

    /// <summary>
    /// Magic things in the world that glow: the moongates, the Orb of the
    /// Moons, the fire, poison, sleep and energy fields, the Virtue Stone,
    /// the Orrery crystal, the crystal ball, the prisms, the beam of light
    /// and the wisp.
    /// </summary>
    public static bool IsEmitter(int shape) =>
        shape is 157 or 776 or 777 or 779 or 785 or 895 or 900 or 902 or 768 or 330 or 746 or 729 or 968 or 981 or 1010 or 168 or 534;

    /// <summary>The glow plane's value for a magic pixel (in its glow bits, <see cref="IndexBuffer8.GlowBits"/>).</summary>
    public static byte GlowByte => (byte)(Mathf.Clamp(Mathf.RoundToInt(63 * Magic.Glow), 0, 63) << 2);

    /// <summary>The glow plane's value for a see-through magic pixel.</summary>
    public static byte HazeByte => (byte)(Mathf.Clamp(Mathf.RoundToInt(63 * Magic.Glow * Magic.Haze), 0, 63) << 2);

    /// <summary>
    /// A blast's light by frame: up to full at a quarter of the way through
    /// (when Exult's explosion strikes), then dying away.
    /// </summary>
    public static float BlastEnvelope(int frame, int frames)
    {
        var peak = Math.Max(1, frames / 4);
        return frame <= peak ? (frame + 1f) / (peak + 1f) : Mathf.Max(0f, 1f - (frame - peak) / (float)Math.Max(1, frames - peak));
    }

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
