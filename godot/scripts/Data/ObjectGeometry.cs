using U7.Core;

namespace U7.Data;

/// <summary>
/// Exult <c>Game_object</c>'s geometry: footprints, the centre tile, and
/// distances and directions that take an object's size into account.
/// Directions are 0 north, clockwise to 7 northwest.
/// </summary>
public static class ObjectGeometry
{
    public static TileCoord Tile(U7Object obj) => new(obj.Tx, obj.Ty, obj.Tz);

    /// <summary>Exult <c>Game_object::get_footprint</c>: the tiles it covers (x, y, w, h); its tile is the lower right.</summary>
    public static (int X, int Y, int W, int H) Footprint(U7Object obj) =>
        (obj.Tx - obj.DimX + 1, obj.Ty - obj.DimY + 1, obj.DimX, obj.DimY);

    public static bool InFootprint(U7Object obj, int tx, int ty)
    {
        var (x, y, w, h) = Footprint(obj);
        var dx = U7Constants.TileDelta(x, tx);
        var dy = U7Constants.TileDelta(y, ty);
        return dx >= 0 && dx < w && dy >= 0 && dy < h;
    }

    /// <summary>Exult <c>TileRect::distance</c> of the footprint: tiles from it along x or y, whichever is more; 0 inside.</summary>
    public static int FootprintDistance(U7Object obj, int px, int py)
    {
        var (x, y, w, h) = Footprint(obj);
        var xdist = px <= x ? x - px : px - x - w + 1;
        var ydist = py <= y ? y - py : py - y - h + 1;
        return Math.Max(0, Math.Max(xdist, ydist));
    }

    /// <summary>Exult <c>Game_object::get_center_tile</c>.</summary>
    public static TileCoord CenterTile(U7Object obj) =>
        new(obj.Tx - (obj.DimX - 1) / 2, obj.Ty - (obj.DimY - 1) / 2, obj.Tz + obj.DimZ * 3 / 4);

    /// <summary>Exult <c>Game_object::distance</c>: between the nearest edges of the two objects.</summary>
    public static int Distance(U7Object a, U7Object b)
    {
        int ax = a.Tx, ay = a.Ty, az = a.Tz, bx = b.Tx, by = b.Ty, bz = b.Tz;
        WrapCheck(U7Constants.TileDelta(ax, bx), a.DimX - 1, b.DimX - 1, ref ax, ref bx);
        WrapCheck(U7Constants.TileDelta(ay, by), a.DimY - 1, b.DimY - 1, ref ay, ref by);
        HeightCheck(az - bz, a.DimZ, b.DimZ, ref az, ref bz);
        return new TileCoord(ax, ay, az).Distance(new TileCoord(bx, by, bz));
    }

    /// <summary>Exult <c>Game_object::distance(Tile_coord)</c>: from the object's nearest edge.</summary>
    public static int Distance(U7Object a, TileCoord t)
    {
        int ax = a.Tx, ay = a.Ty, az = a.Tz, bx = t.Tx, by = t.Ty, bz = t.Tz;
        WrapCheck(U7Constants.TileDelta(ax, bx), a.DimX - 1, 0, ref ax, ref bx);
        WrapCheck(U7Constants.TileDelta(ay, by), a.DimY - 1, 0, ref ay, ref by);
        HeightCheck(az - bz, a.DimZ, 0, ref az, ref bz);
        return new TileCoord(ax, ay, az).Distance(new TileCoord(bx, by, bz));
    }

    // Exult delta_wrap_check: an object's tile is its lower right corner.
    static void WrapCheck(int dir, int size1, int size2, ref int coord1, ref int coord2)
    {
        if (dir > 0)
        {
            coord2 = U7Constants.WrapTile(coord2 - size2);
        }
        else if (dir < 0)
        {
            coord1 = U7Constants.WrapTile(coord1 - size1);
        }
    }

    // Exult delta_check.
    static void HeightCheck(int delta, int size1, int size2, ref int coord1, ref int coord2)
    {
        if (delta < 0)
        {
            coord1 = coord1 + size1 > coord2 ? coord2 : coord1 + size1;
        }
        else if (delta > 0)
        {
            coord2 = coord2 + size2 > coord1 ? coord1 : coord2 + size2;
        }
    }

    /// <summary>Exult <c>Game_object::get_direction(Tile_coord)</c>: from the object's centre tile.</summary>
    public static int Direction(U7Object from, TileCoord to)
    {
        var t1 = CenterTile(from);
        return Directions.Of(t1.Ty - to.Ty, to.Tx - t1.Tx);
    }

    /// <summary>Exult <c>Game_object::get_direction(Game_object*)</c>: centre tile to centre tile.</summary>
    public static int Direction(U7Object from, U7Object to) => Direction(from, CenterTile(to));

    /// <summary>
    /// Exult <c>Game_object::get_facing_direction</c>: straight east or west
    /// when level with the object's footprint, north or south when in line
    /// with it, else towards its centre. (Exult's north/south test compares
    /// with <c>w + h</c> where <c>x + w</c> is meant; kept.)
    /// </summary>
    public static int FacingDirection(U7Object from, U7Object to)
    {
        var (x, y, w, h) = Footprint(to);
        int tx = from.Tx, ty = from.Ty;
        if (x + w <= tx && ty >= y && ty < y + h)
        {
            return 6;
        }

        if (tx < x && ty >= y && ty < y + h)
        {
            return 2;
        }

        if (y + h <= ty && tx >= x && tx < w + h)
        {
            return 4;
        }

        if (ty < y && tx >= x && tx < w + h)
        {
            return 0;
        }

        return Direction(from, to);
    }
}
