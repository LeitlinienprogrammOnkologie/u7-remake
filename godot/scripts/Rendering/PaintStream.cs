using System.Collections.Generic;
using System.Runtime.InteropServices;
using U7.Data;

namespace U7.Rendering;

/// <summary>The painting ops: <see cref="IndexBuffer8"/>'s methods, Exult's <c>Image_buffer8</c> (docs/RENDERER.md, 5.1).</summary>
public enum PaintOp : byte
{
    /// <summary>Exult <c>copy8</c>: a terrain's flats, the frame's top left at X, Y.</summary>
    Copy8,
    /// <summary>Exult <c>fill8</c>: the dungeon blackness, W by H at X, Y.</summary>
    Fill8,
    /// <summary>Exult <c>paint_rle</c>: an opaque frame, its hotspot at X, Y.</summary>
    Rle,
    /// <summary>Exult <c>paint_rle_translucent</c>: see-through colours turn what is under them.</summary>
    RleTranslucent,
    /// <summary>The remake's: a hidden effect on the panes of its room's windows.</summary>
    RleThroughPanes,
    /// <summary>Exult <c>paint_rle_transformed</c>: an invisible actor, one table over its silhouette.</summary>
    RleTransformed,
    /// <summary>Exult <c>paint_rle_outline</c>: a status outline round the original frame, blown up.</summary>
    RleOutline,
    /// <summary>A hi-res outline: the frame's scans in one colour.</summary>
    RleSolid
}

/// <summary>Which file a command's frame comes from (its identity for a backend with an art store of its own).</summary>
public enum ArtFile : byte
{
    None,
    Shapes,
    Sprites,
    /// <summary>A chunk terrain's flats (<c>Shape</c> is the terrain number).</summary>
    Terrain
}

/// <summary>A frame's identity: the file, shape and frame it was taken from.</summary>
public readonly record struct ArtRef(ArtFile File, int Shape, int Frame);

/// <summary>
/// One painting command (docs/RENDERER.md, 5.1): the op, the frame and
/// where it came from, its hotspot in buffer pixels, and what the op takes
/// (glow and haze bytes, the window slot, the table, the colour, the scale,
/// the pane mask) and the object painted, if any (for its lift and box).
/// </summary>
public struct PaintCommand
{
    public PaintOp Op;
    public ArtRef Source;
    /// <summary>The frame to paint, at the scale painted; null for <see cref="PaintOp.Fill8"/>.</summary>
    public ShapeFrame? Art;
    public U7Object? Object;
    /// <summary>The hotspot (RLE ops) or the top left (<see cref="PaintOp.Copy8"/>, <see cref="PaintOp.Fill8"/>), in buffer pixels.</summary>
    public int X;
    public int Y;
    /// <summary><see cref="PaintOp.Fill8"/>'s size.</summary>
    public int W;
    public int H;
    public byte Glow;
    public byte Haze;
    public byte Window;
    /// <summary>The outline's or the fill's index.</summary>
    public byte Colour;
    /// <summary><see cref="PaintOp.RleTransformed"/>'s table.</summary>
    public int Table;
    /// <summary><see cref="PaintOp.RleOutline"/>'s block size.</summary>
    public int Scale;
    /// <summary><see cref="PaintOp.RleThroughPanes"/>'s mask in <see cref="PaintStream.PaneMask"/>.</summary>
    public int Mask;
    /// <summary><see cref="PaintOp.RleThroughPanes"/>: the frame's own see-through colours turn what the pane shows.</summary>
    public bool Translucent;
    /// <summary>The lift of what is painted (the object's, a sprite's tile's; the flats 0): the lift plane's value where the op covers (RENDERER.md 5.6).</summary>
    public byte Lift;
    /// <summary>A building piece's height in lifts for the box proxy (RENDERER.md 5.6); 0 for the ground proxy.</summary>
    public byte DimZ;
}

/// <summary>
/// The command stream (docs/RENDERER.md, R1): what <see cref="WorldView"/>
/// decides to paint, in order, recorded for a backend to replay. The CPU
/// backend is <see cref="Replay"/>: <see cref="IndexBuffer8"/>'s methods,
/// called with the same arguments in the same order as before the stream,
/// so its planes are the reference. The recording methods mirror the
/// buffer's, plus the frame's identity and the object. Cleared and reused
/// each frame.
/// </summary>
public sealed class PaintStream
{
    readonly List<PaintCommand> _commands = new(4096);
    /// <summary>The pane masks the stream's <see cref="PaintOp.RleThroughPanes"/> commands index, reused across frames.</summary>
    readonly List<bool[]> _masks = [];
    int _masksUsed;

    public int Count => _commands.Count;
    public ReadOnlySpan<PaintCommand> Commands => CollectionsMarshal.AsSpan(_commands);
    /// <summary>The pane masks the frame's commands refer to (<see cref="PaintCommand.Mask"/>).</summary>
    public int MaskCount => _masksUsed;
    public bool[] Mask(int index) => _masks[index];

    public void Clear()
    {
        _commands.Clear();
        _masksUsed = 0;
    }

    /// <summary>For the console: how many commands of each op the frame holds (the ops with any).</summary>
    public string OpCounts()
    {
        var counts = new int[8];
        foreach (ref readonly var c in Commands)
        {
            counts[(int)c.Op]++;
        }

        var parts = new List<string>();
        for (var op = 0; op < counts.Length; op++)
        {
            if (counts[op] > 0)
            {
                parts.Add($"{((PaintOp)op).ToString().ToLowerInvariant()} {counts[op]}");
            }
        }

        return string.Join(", ", parts);
    }

    /// <summary>A terrain's flats, the frame's top left at x, y.</summary>
    public void Copy8(ShapeFrame flats, int terrain, int x, int y) =>
        _commands.Add(new PaintCommand { Op = PaintOp.Copy8, Source = new ArtRef(ArtFile.Terrain, terrain, 0), Art = flats, X = x, Y = y });

    public void Fill8(byte colour, int w, int h, int x, int y) =>
        _commands.Add(new PaintCommand { Op = PaintOp.Fill8, Colour = colour, W = w, H = h, X = x, Y = y });

    /// <summary>The lift plane's value for a command: the lift given, else its object's, else 0.</summary>
    static byte LiftOf(U7Object? obj, int lift) => (byte)Math.Clamp(lift >= 0 ? lift : obj?.Tz ?? 0, 0, 255);

    public void PaintRle(ShapeFrame frame, ArtRef source, int x, int y, byte glow, U7Object? obj = null, int lift = -1, int box = 0) =>
        _commands.Add(new PaintCommand
        {
            Op = PaintOp.Rle, Source = source, Art = frame, Object = obj, X = x, Y = y, Glow = glow, Lift = LiftOf(obj, lift), DimZ = (byte)box
        });

    public void PaintRleTranslucent(ShapeFrame frame, ArtRef source, int x, int y, byte glow, byte haze, byte window = 0, U7Object? obj = null,
        int lift = -1, int box = 0) =>
        _commands.Add(new PaintCommand
        {
            Op = PaintOp.RleTranslucent, Source = source, Art = frame, Object = obj, X = x, Y = y, Glow = glow, Haze = haze, Window = window,
            Lift = LiftOf(obj, lift), DimZ = (byte)box
        });

    /// <summary>
    /// The windows a hidden effect shows through: the mask the next
    /// <see cref="PaintRleThroughPanes"/> records (cleared), to be filled first.
    /// </summary>
    public bool[] PaneMask()
    {
        if (_masksUsed == _masks.Count)
        {
            _masks.Add(new bool[256]);
        }

        var mask = _masks[_masksUsed];
        Array.Clear(mask);
        return mask;
    }

    /// <summary>A hidden effect on the panes of the windows marked in the mask from <see cref="PaneMask"/>.</summary>
    public void PaintRleThroughPanes(ShapeFrame frame, ArtRef source, int x, int y, byte glow, bool translucent, int lift = 0) =>
        _commands.Add(new PaintCommand
        {
            Op = PaintOp.RleThroughPanes, Source = source, Art = frame, X = x, Y = y, Glow = glow, Translucent = translucent, Mask = _masksUsed++,
            Lift = LiftOf(null, lift)
        });

    public void PaintRleTransformed(ShapeFrame frame, ArtRef source, int x, int y, int table, U7Object? obj = null) =>
        _commands.Add(new PaintCommand
        {
            Op = PaintOp.RleTransformed, Source = source, Art = frame, Object = obj, X = x, Y = y, Table = table, Lift = LiftOf(obj, -1)
        });

    public void PaintRleOutline(ShapeFrame original, ArtRef source, int x, int y, byte colour, int scale, U7Object? obj = null) =>
        _commands.Add(new PaintCommand
        {
            Op = PaintOp.RleOutline, Source = source, Art = original, Object = obj, X = x, Y = y, Colour = colour, Scale = scale,
            Lift = LiftOf(obj, -1)
        });

    public void PaintRleSolid(ShapeFrame outline, ArtRef source, int x, int y, byte colour, U7Object? obj = null) =>
        _commands.Add(new PaintCommand
        {
            Op = PaintOp.RleSolid, Source = source, Art = outline, Object = obj, X = x, Y = y, Colour = colour, Lift = LiftOf(obj, -1)
        });

    /// <summary>The CPU backend: every command in order into the buffer, painted at <paramref name="scale"/> (the box proxy's pixels a lift).</summary>
    public void Replay(IndexBuffer8 buffer, XformTables xforms, int scale = 1)
    {
        foreach (ref readonly var c in Commands)
        {
            buffer.LiftBox(c.DimZ, 4 * scale);
            switch (c.Op)
            {
                case PaintOp.Copy8:
                case PaintOp.Rle:
                    buffer.PaintRle(c.Art!, c.X, c.Y, c.Glow, c.Lift);
                    break;
                case PaintOp.Fill8:
                    buffer.Fill8(c.Colour, c.W, c.H, c.X, c.Y);
                    break;
                case PaintOp.RleTranslucent:
                    buffer.PaintRleTranslucent(c.Art!, c.X, c.Y, xforms, c.Glow, c.Haze, c.Window, c.Lift);
                    break;
                case PaintOp.RleThroughPanes:
                    buffer.PaintRleThroughPanes(c.Art!, c.X, c.Y, xforms, _masks[c.Mask], c.Glow, c.Translucent);
                    break;
                case PaintOp.RleTransformed:
                    buffer.PaintRleTransformed(c.Art!, c.X, c.Y, xforms, c.Table);
                    break;
                case PaintOp.RleOutline:
                    buffer.PaintRleOutline(c.Art!, c.X, c.Y, c.Colour, c.Scale, c.Lift);
                    break;
                case PaintOp.RleSolid:
                    buffer.PaintRleSolid(c.Art!, c.X, c.Y, c.Colour, c.Lift);
                    break;
            }
        }
    }
}
