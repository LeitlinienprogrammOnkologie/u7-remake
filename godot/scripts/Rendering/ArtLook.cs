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

    /// <summary>
    /// The paperdolls (Serpent Isle's art and Exult's) redrawn from the pack
    /// too, rather than in their original art (the user's pick for now,
    /// 2026-10-12); switched with F7 and kept in <c>saves/settings.cfg</c>.
    /// The pack's paperdolls are made by <c>scripts/upscale/missing.py --make</c>.
    /// </summary>
    public static bool RedrawnPaperdolls;

    /// <summary>
    /// The user's painted portraits (<see cref="PaintedFaces"/>) in place of
    /// the faces' art, original or redrawn (the user's wish, 2026-10-12);
    /// switched with the console's <c>art faces on|off</c> and kept in
    /// <c>saves/settings.cfg</c>.
    /// </summary>
    public static bool PaintedFaces = true;

    /// <summary>A pack's colours between two palette entries, mixed, rather than the nearest one alone.</summary>
    public static bool Mix = true;

    /// <summary>
    /// A pack's own colours, more than the palette's 256 (from version 2
    /// packs), rather than palette entries (mixed or not): each still changes
    /// with the palettes and translucency tables as its nearest entry does
    /// (<see cref="PaletteSlopes"/>, <c>XformTables.TurnColour</c>); what
    /// cycles or is see-through stays its index. Switched with the console's
    /// <c>art truecolour on|off</c> and kept in <c>saves/settings.cfg</c>.
    /// </summary>
    public static bool TrueColour;

    /// <summary>
    /// The texels' edges blended over one screen pixel, so every texel shows
    /// the same size at a fractional scale (a 1440p window at zoom 4 has 7.2
    /// screen pixels a world pixel), instead of nearest, which makes some 7
    /// and some 8 wide.
    /// </summary>
    public static bool Sharp = true;

    /// <summary>
    /// Which backend replays the command stream (docs/RENDERER.md, 5.3):
    /// <c>cpu</c> (<see cref="IndexBuffer8"/>, the reference), <c>gpu</c>
    /// (<see cref="GpuCompositor"/>, where a rendering device exists) or
    /// <c>auto</c> (gpu where it exists). The console's <c>art renderer</c>,
    /// kept in <c>saves/settings.cfg</c>. The GPU is the default since the
    /// user's word of 2026-10-13, its planes being bit-exact to the CPU's in
    /// classic mode (R3); headless and under Compatibility the CPU paints.
    /// </summary>
    public static string Renderer = "auto";

    /// <summary>
    /// Modern colour (docs/RENDERER.md 5.5, R5): a true-colour pixel takes
    /// the palettes and the translucency tables through 3D lookup tables
    /// (<see cref="ColourLut"/>) rather than its anchor's rule, and the
    /// remake's own lights, glow, cloud shade and mist are mixed in linear
    /// light. Off: classic, today's math. The console's <c>art modern on|off</c>,
    /// kept in <c>saves/settings.cfg</c>. On by default: the user found no
    /// discernible difference and took either look (2026-10-13).
    /// </summary>
    public static bool Modern = true;

    /// <summary>
    /// Light occlusion (docs/RENDERER.md 5.6, R6): a pool's and the light
    /// spell's light is stopped by walls, closed doors and other opaque
    /// things on the way (<see cref="U7.World.LightOcclusion"/>) and doesn't
    /// reach a roof or an upper floor from below. The console's
    /// <c>art occlusion on|off</c>, kept in <c>saves/settings.cfg</c>.
    /// </summary>
    public static bool Occlusion = true;

    /// <summary>
    /// Bloom (docs/RENDERER.md 5.7 and 5.8, R7): in modern mode the glow of
    /// magic, blasts and lit things spills over what is round them, this
    /// strong (0 off) and this far in world pixels. The console's
    /// <c>art bloom &lt;strength&gt; [radius]</c>, kept in <c>saves/settings.cfg</c>.
    /// </summary>
    public static float Bloom = 0.35f;
    public static float BloomRadius = 10f;

    /// <summary>
    /// The gumps under the world's palette (docs/RENDERER.md 5.9, R8), as
    /// Exult's one palette shows them: dark at night, through the fades and
    /// the override palettes, their water and flames cycling. Off, the day
    /// palette as before. The console's <c>art gumps on|off</c>, kept in
    /// <c>saves/settings.cfg</c>.
    /// </summary>
    public static bool GumpPalette = true;

    /// <summary>The lookup tables' cells along each axis: 32, or 64 where a seam between original and hi-res pixels shows (<c>art lut 64</c>).</summary>
    public static int LutSize = 32;

    /// <summary>
    /// The GPU backend's art store in megabytes (RENDERER.md 5.4's budget;
    /// 512 to start there, 256 here for this PC's integrated GPU), split
    /// into pages freed one at a time. The console's <c>art store</c>, kept
    /// in <c>saves/settings.cfg</c>; takes effect when the backend is made again.
    /// </summary>
    public static int StoreMb = 256;

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
