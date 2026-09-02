using Godot;
using U7.Core;
using U7.Data;

namespace U7.World;

/// <summary>
/// A* on the 3072×3072 wrap-around tile grid. Cost 1 per step; diagonals
/// only when both cardinals are free. NPCs are not solid.
/// </summary>
public static class Pathfinder
{
    static readonly (int Dx, int Dy)[] Dirs =
    [
        (0, -1), (1, 0), (0, 1), (-1, 0),
        (1, -1), (1, 1), (-1, 1), (-1, -1)
    ];

    public static List<Vector2I>? Find(GameMap map, int sx, int sy, int gx, int gy, int lift,
        int maxNodes = U7Constants.PathMaxNodes)
    {
        sx = U7Constants.WrapTile(sx);
        sy = U7Constants.WrapTile(sy);
        gx = U7Constants.WrapTile(gx);
        gy = U7Constants.WrapTile(gy);
        if (sx == gx && sy == gy)
        {
            return new List<Vector2I>();
        }

        if (map.IsBlocked(gx, gy, lift))
        {
            var alt = FindNearbyOpen(map, gx, gy, lift);
            if (alt is { } a)
            {
                gx = a.X;
                gy = a.Y;
            }
        }

        var start = Pack(sx, sy);
        var goal = Pack(gx, gy);
        var open = new PriorityQueue<int, int>();
        var gScore = new Dictionary<int, int> { [start] = 0 };
        var came = new Dictionary<int, int>();
        var closed = new HashSet<int>();
        open.Enqueue(start, Heuristic(sx, sy, gx, gy));
        var expanded = 0;
        while (open.Count > 0 && expanded < maxNodes)
        {
            var cur = open.Dequeue();
            if (!closed.Add(cur))
            {
                continue;
            }

            expanded++;
            if (cur == goal)
            {
                return Reconstruct(came, cur, start);
            }

            Unpack(cur, out var x, out var y);
            var g = gScore[cur];
            for (var i = 0; i < Dirs.Length; i++)
            {
                var nx = U7Constants.WrapTile(x + Dirs[i].Dx);
                var ny = U7Constants.WrapTile(y + Dirs[i].Dy);
                if (i >= 4)
                {
                    var cx = U7Constants.WrapTile(x + Dirs[i].Dx);
                    var cy = y;
                    var rx = x;
                    var ry = U7Constants.WrapTile(y + Dirs[i].Dy);
                    if (map.IsBlocked(cx, cy, lift) || map.IsBlocked(rx, ry, lift))
                    {
                        continue;
                    }
                }

                if (map.IsBlocked(nx, ny, lift) && !(nx == gx && ny == gy))
                {
                    continue;
                }

                var np = Pack(nx, ny);
                var ng = g + 1;
                if (gScore.TryGetValue(np, out var old) && ng >= old)
                {
                    continue;
                }

                gScore[np] = ng;
                came[np] = cur;
                open.Enqueue(np, ng + Heuristic(nx, ny, gx, gy));
            }
        }

        return null;
    }

    public static Vector2I GreedyStep(GameMap map, int sx, int sy, int gx, int gy, int lift)
    {
        var dx = Math.Sign(U7Constants.TileDelta(sx, gx));
        var dy = Math.Sign(U7Constants.TileDelta(sy, gy));
        if (dx == 0 && dy == 0)
        {
            return new Vector2I(sx, sy);
        }

        var nx = U7Constants.WrapTile(sx + dx);
        var ny = U7Constants.WrapTile(sy + dy);
        if (!map.IsBlocked(nx, ny, lift))
        {
            return new Vector2I(nx, ny);
        }

        if (dx != 0)
        {
            nx = U7Constants.WrapTile(sx + dx);
            if (!map.IsBlocked(nx, sy, lift))
            {
                return new Vector2I(nx, sy);
            }
        }

        if (dy != 0)
        {
            ny = U7Constants.WrapTile(sy + dy);
            if (!map.IsBlocked(sx, ny, lift))
            {
                return new Vector2I(sx, ny);
            }
        }

        return new Vector2I(sx, sy);
    }

    static Vector2I? FindNearbyOpen(GameMap map, int gx, int gy, int lift)
    {
        for (var r = 1; r <= 3; r++)
        {
            for (var dy = -r; dy <= r; dy++)
            {
                for (var dx = -r; dx <= r; dx++)
                {
                    if (Math.Abs(dx) != r && Math.Abs(dy) != r)
                    {
                        continue;
                    }

                    var x = U7Constants.WrapTile(gx + dx);
                    var y = U7Constants.WrapTile(gy + dy);
                    if (!map.IsBlocked(x, y, lift))
                    {
                        return new Vector2I(x, y);
                    }
                }
            }
        }

        return null;
    }

    static List<Vector2I> Reconstruct(Dictionary<int, int> came, int cur, int start)
    {
        var rev = new List<Vector2I>();
        while (cur != start)
        {
            Unpack(cur, out var x, out var y);
            rev.Add(new Vector2I(x, y));
            if (!came.TryGetValue(cur, out cur))
            {
                break;
            }
        }

        rev.Reverse();
        return rev;
    }

    static int Heuristic(int x, int y, int gx, int gy)
    {
        var dx = Math.Abs(U7Constants.TileDelta(x, gx));
        var dy = Math.Abs(U7Constants.TileDelta(y, gy));
        return Math.Max(dx, dy);
    }

    static int Pack(int x, int y) => (y << 12) | x;

    static void Unpack(int p, out int x, out int y)
    {
        x = p & 0xfff;
        y = p >> 12;
    }
}
