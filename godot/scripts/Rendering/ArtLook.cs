namespace U7.Rendering;

/// <summary>
/// The user's look for the world's art at more than its original resolution
/// (<see cref="HiresArt"/>): what Exult shows stays Exult's, only drawn finer.
/// The user's pick (2026-10-10, from the original and five models side by
/// side): NXbrz everywhere, the portraits too, its colours mixed, the sharp fill on.
/// </summary>
public static class ArtLook
{
    /// <summary>
    /// The redrawn art (a pack under assets/hires, made by scripts/upscale),
    /// or null for the original art, which is also what shows while the pack
    /// hasn't been made.
    /// </summary>
    public static string? Pack = "nxbrz";

    /// <summary>Buffer pixels a world pixel is painted as, each way, without a pack (1: the original resolution).</summary>
    public static int Scale = 1;

    /// <summary>A pack's colours between two palette entries, mixed, rather than the nearest one alone.</summary>
    public static bool Mix = true;

    /// <summary>
    /// The texels' edges blended over one screen pixel, so every texel shows
    /// the same size at a fractional scale (a 1440p window at zoom 4 has 7.2
    /// screen pixels a world pixel), instead of nearest, which makes some 7
    /// and some 8 wide.
    /// </summary>
    public static bool Sharp = true;

    /// <summary>
    /// Screen pixels a hi-res texel must cover at least; zoomed further out,
    /// the original art is painted (its pixels would be too small to see).
    /// </summary>
    public const float MinScreenPixels = 0.6f;

    /// <summary>
    /// The most buffer pixels painted at the art's scale: zoomed out further
    /// (below zoom 3 in a 1280x800 view), the original art is painted, as
    /// 4x would take tens of milliseconds a frame.
    /// </summary>
    public const float MaxBufferPixels = 2_500_000;
}
