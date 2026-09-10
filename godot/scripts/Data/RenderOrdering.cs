using U7.Core;

namespace U7.Data;

/// <summary>
/// Exult paint-order comparison (<c>Game_object::compare</c> with
/// <c>Ordering_info</c>): compares two objects' 3D tile boxes and sprite
/// rectangles. Returns -1 if <c>a</c> paints before <c>b</c>, 1 if after,
/// 0 if the order does not matter.
/// </summary>
public static class RenderOrdering
{
    public readonly struct Info
    {
        public readonly int XLeft, XRight, YFar, YNear, ZBot, ZTop, Zs;
        public readonly int Ax, Ay, Aw, Ah; // sprite rect in world pixels
        public readonly bool Occludes;

        public Info(U7Object o, ShapeCatalog catalog)
        {
            var rec = catalog[o.Shape];
            var fi = rec.GetFrame(o.Frame);
            // Exult Game_window::get_shape_location: hot spot bottom-right of the tile.
            var lift = 4 * o.Tz;
            var hx = (o.Tx + 1) * U7Constants.TileSize - 1 - lift;
            var hy = (o.Ty + 1) * U7Constants.TileSize - 1 - lift;
            Ax = hx - fi.XLeft;
            Ay = hy - fi.YAbove;
            Aw = fi.Width;
            Ah = fi.Height;
            Zs = o.DimZ;
            XLeft = o.Tx - o.DimX + 1;
            XRight = o.Tx;
            YFar = o.Ty - o.DimY + 1;
            YNear = o.Ty;
            ZTop = o.Tz + Zs - 1;
            ZBot = Zs == 0 ? o.Tz - 1 : o.Tz;
            Occludes = rec.Occludes;
        }

        public bool Intersects(in Info o) =>
            Ax < o.Ax + o.Aw && o.Ax < Ax + Aw && Ay < o.Ay + o.Ah && o.Ay < Ay + Ah;
    }

    static void CompareRanges(int from1, int to1, int from2, int to2, out int cmp, out bool overlap)
    {
        if (to1 < from2)
        {
            overlap = false;
            cmp = -1;
        }
        else if (to2 < from1)
        {
            overlap = false;
            cmp = 1;
        }
        else
        {
            overlap = true;
            cmp = from1 < from2 ? -1 : from1 > from2 ? 1 : to1 < to2 ? 1 : to1 > to2 ? -1 : 0;
        }
    }

    public static int Compare(in Info a, in Info b)
    {
        if (!a.Intersects(b))
        {
            return 0; // no overlap on screen
        }

        CompareRanges(a.XLeft, a.XRight, b.XLeft, b.XRight, out var xcmp, out var xover);
        CompareRanges(a.YFar, a.YNear, b.YFar, b.YNear, out var ycmp, out var yover);
        CompareRanges(a.ZBot, a.ZTop, b.ZBot, b.ZTop, out var zcmp, out var zover);
        if (xcmp == 0 && ycmp == 0 && zcmp == 0)
        {
            // Same space: paint the bigger sprite second.
            return a.Aw < b.Aw && a.Ah < b.Ah ? -1 : a.Aw > b.Aw && a.Ah > b.Ah ? 1 : 0;
        }

        if (xover && yover && zover)
        {
            if (a.Zs == 0)
            {
                return b.Zs == 0 ? 0 : -1; // flat one first
            }

            if (b.Zs == 0)
            {
                return 1;
            }
        }

        if (xcmp >= 0 && ycmp >= 0 && zcmp >= 0)
        {
            return 1;
        }

        if (xcmp <= 0 && ycmp <= 0 && zcmp <= 0)
        {
            return -1;
        }

        if (yover)
        {
            if (xover)
            {
                return zcmp;
            }

            if (zover || zcmp == 0 || xcmp == zcmp)
            {
                return xcmp;
            }

            // Trinsic mayor statue-through-roof fix.
            if (a.ZTop / 5 < b.ZBot / 5 && b.Occludes)
            {
                return -1;
            }

            if (b.ZTop / 5 < a.ZBot / 5 && a.Occludes)
            {
                return 1;
            }

            return 0;
        }

        if (xover)
        {
            if (zover || zcmp == 0)
            {
                return ycmp;
            }

            return ycmp == zcmp ? ycmp : 0;
        }

        if (xcmp == -1)
        {
            if (ycmp == -1)
            {
                return zover || zcmp <= 0 ? -1 : 0;
            }
        }
        else if (ycmp == 1)
        {
            if (zover || zcmp >= 0)
            {
                return 1;
            }

            // Britain museum statue-through-roof fix.
            return a.ZTop / 5 < b.ZBot / 5 ? -1 : 0;
        }

        return 0;
    }
}
