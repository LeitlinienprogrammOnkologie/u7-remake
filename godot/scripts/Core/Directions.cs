namespace U7.Core;

/// <summary>
/// Exult's eight directions (dir.cc, <c>Tile_coord::get_neighbor</c>,
/// <c>Game_object::get_dir_framenum</c>): 0 north, clockwise to 7 northwest.
/// In the slope functions <c>dy</c> grows northwards.
/// </summary>
public static class Directions
{
    /// <summary>A step's x change by direction.</summary>
    public static ReadOnlySpan<int> Dx => [0, 1, 1, 1, 0, -1, -1, -1];

    /// <summary>A step's y change by direction (y grows southwards on the map).</summary>
    public static ReadOnlySpan<int> Dy => [-1, -1, 0, 1, 1, 1, 0, -1];

    /// <summary>
    /// Exult <c>Game_object::rotate</c> as a frame's rotation bits by direction:
    /// north 0, south 16, west 32, east 48 (a diagonal turns clockwise to the next of those).
    /// </summary>
    public static ReadOnlySpan<int> FrameRotation => [0, 0, 48, 48, 16, 16, 32, 32];

    /// <summary>Exult <c>Get_direction</c>: <see cref="NoWrap"/> with the deltas wrapped round the world.</summary>
    public static int Of(int dy, int dx) => NoWrap(WrapDelta(dy), WrapDelta(dx));

    /// <summary>Exult <c>Wrap_Delta</c>, as it is.</summary>
    public static int WrapDelta(int delta) =>
        delta >= U7Constants.NumTiles / 2 ? U7Constants.NumTiles / 2 - delta
        : delta <= -U7Constants.NumTiles / 2 ? -U7Constants.NumTiles / 2 - delta
        : delta;

    /// <summary>Exult <c>Get_direction_NoWrap</c>: one of 8 directions for a slope.</summary>
    public static int NoWrap(int dy, int dx)
    {
        if (dx == 0)
        {
            return dy > 0 ? 0 : 4;
        }

        var dydx = 1024 * dy / dx;
        if (dydx >= 0)
        {
            return dx >= 0
                ? dydx <= 424 ? 2 : dydx <= 2472 ? 1 : 0
                : dydx <= 424 ? 6 : dydx <= 2472 ? 5 : 4;
        }

        return dx >= 0
            ? dydx >= -424 ? 2 : dydx >= -2472 ? 3 : 4
            : dydx >= -424 ? 6 : dydx >= -2472 ? 7 : 0;
    }

    /// <summary>Exult <c>Get_direction4</c>: north, east, south or west (0, 2, 4, 6).</summary>
    public static int Of4(int dy, int dx)
    {
        if (dx >= 0)
        {
            return dy > dx ? 0 : dy < -dx ? 4 : 2;
        }

        return dy > -dx ? 0 : dy < dx ? 4 : 6;
    }
}
