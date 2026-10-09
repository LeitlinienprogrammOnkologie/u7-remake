using U7.Core;
using U7.Data;

namespace U7.Actors;

/// <summary>
/// Shared one-tile step + walk frames (Exult <c>Frames_sequence</c>): the
/// avatar's 3-frame <c>{0,1,2}</c> and NPCs' 5-frame <c>{0,1,0,2,0}</c> per
/// cardinal; frame 0 is the resting frame and the cycle skips it.
/// </summary>
public static class ActorWalker
{
    /// <summary>Exult <c>Actor::reach1_frame</c>, <c>reach2_frame</c>, <c>sit_frame</c>, <c>bow_frame</c> and <c>sleep_frame</c>.</summary>
    public const int Reach1Frame = 5;
    public const int Reach2Frame = 8;
    public const int SitFrame = 10;
    public const int BowFrame = 11;
    public const int SleepFrame = 13;
    static readonly int[] Rotate = [0, 0, 48, 48, 16, 16, 32, 32];

    /// <summary>Exult <c>Game_object::get_dir_framenum</c>: a base frame (0-15) turned to face a direction (0-7).</summary>
    public static int DirFrame(int dir, int frame) => (frame & 0xf) + Rotate[dir & 7];

    // By 8-way direction. Exult picks walking frames by Get_direction4, which
    // turns a diagonal step into east or west.
    static readonly int[][] AvatarFrames =
    [
        [0, 1, 2],
        [48, 49, 50],
        [48, 49, 50],
        [48, 49, 50],
        [16, 17, 18],
        [32, 33, 34],
        [32, 33, 34],
        [32, 33, 34]
    ];

    static readonly int[][] NpcFrames =
    [
        [0, 1, 0, 2, 0],
        [48, 49, 48, 50, 48],
        [48, 49, 48, 50, 48],
        [48, 49, 48, 50, 48],
        [16, 17, 16, 18, 16],
        [32, 33, 32, 34, 32],
        [32, 33, 32, 34, 32],
        [32, 33, 32, 34, 32]
    ];

    public static bool TryStep(GameMap map, U7Object actor, int dx, int dy)
    {
        if (dx == 0 && dy == 0)
        {
            Stand(actor, actor.WalkFrameIndex == 0 ? 4 : DirIndex(0, 0));
            return false;
        }

        var facing = DirIndex(dx, dy);
        var to = new TileCoord(U7Constants.WrapTile(actor.Tx + dx), U7Constants.WrapTile(actor.Ty + dy), actor.Tz);
        if (!CanStep(map, actor, ref to))
        {
            Stand(actor, facing);
            return false;
        }

        MoveTo(map, actor, to.Tx, to.Ty, to.Tz, facing);
        return true;
    }

    /// <summary>
    /// Exult <c>Actor::step</c>'s test: the tile is free for the actor
    /// (<see cref="IsBlocked"/>), or the actor in the way steps aside or
    /// swaps places (<see cref="IsReallyBlocked"/>); and it is at most one
    /// lift up or down. <paramref name="to"/>.Tz becomes the lift it lands on.
    /// </summary>
    public static bool CanStep(GameMap map, U7Object actor, ref TileCoord to)
    {
        if (IsBlocked(map, actor, ref to) && IsReallyBlocked(map, actor, ref to))
        {
            return false;
        }

        return Math.Abs(to.Tz - actor.Tz) <= 1;
    }

    static readonly int[] DirDx = [0, 1, 1, 1, 0, -1, -1, -1];
    static readonly int[] DirDy = [-1, -1, 0, 1, 1, 1, 0, -1];

    /// <summary>
    /// Exult <c>Actor::is_really_blocked</c>, for a step onto a blocked tile:
    /// it is, if more than a lift up or down or if nothing found is in the
    /// way (water, say); it is not, if the actor in the way steps aside or
    /// swaps places with this one.
    /// </summary>
    public static bool IsReallyBlocked(GameMap map, U7Object actor, ref TileCoord t, bool force = false)
    {
        if (Math.Abs(t.Tz - actor.Tz) > 1)
        {
            return true;
        }

        var block = FindBlocking(map, actor, ObjectGeometry.Direction(actor, t));
        if (block is null)
        {
            return true; // IE, water.
        }

        if (block == actor)
        {
            return false;
        }

        // Try to get the blocker to move aside.
        if (block.IsActor && MoveAside(map, block, actor, ObjectGeometry.Direction(actor, block)))
        {
            return false;
        }

        // (May have swapped places.) If okay, try one last time.
        return (t.Tx != actor.Tx || t.Ty != actor.Ty || t.Tz != actor.Tz) &&
               IsBlocked(map, actor, ref t, moveFlags: force ? MoveFlags.All : 0);
    }

    /// <summary>
    /// Exult <c>Actor::find_blocking</c>: what is in the way of the tiles the
    /// actor's footprint moves onto, a step in <paramref name="dir"/>, at its lift.
    /// </summary>
    public static U7Object? FindBlocking(GameMap map, U7Object actor, int dir)
    {
        var (x, y, w, h) = ObjectGeometry.Footprint(actor);
        for (var i = x + DirDx[dir]; i < x + DirDx[dir] + w; i++)
        {
            for (var j = y + DirDy[dir]; j < y + DirDy[dir] + h; j++)
            {
                if (!ObjectGeometry.InFootprint(actor, i, j) &&
                    map.FindBlocking(new TileCoord(i, j, actor.Tz)) is { } block)
                {
                    return block;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Exult <c>Actor::move_aside</c>: step out of the way of
    /// <paramref name="forActor"/>, coming from direction
    /// <paramref name="dir"/>: to one side, else diagonally, else swap places
    /// with it. Not in combat, while moving, or sitting, bending over,
    /// kneeling or asleep.
    /// </summary>
    public static bool MoveAside(GameMap map, U7Object npc, U7Object forActor, int dir)
    {
        // (Exult: not stepping aside in combat also prevents some double
        // moves through walls or doors; and not while moving, as that may
        // break pathfinding.)
        if (npc.ScheduleType == ScheduleType.Combat || npc.FrameTime != 0)
        {
            return false;
        }

        var frnum = npc.Frame & 0xf;
        if (frnum is >= SitFrame and <= SleepFrame)
        {
            return false;
        }

        // Try orthogonal directions first, then diagonals.
        foreach (var d in new[] { (dir + 2) % 8, (dir + 6) % 8, 1, 3, 5, 7 })
        {
            var to = new TileCoord(npc.Tx + DirDx[d], npc.Ty + DirDy[d], npc.Tz).Wrapped();
            if (IsBlocked(map, npc, ref to, moveFlags: npc.TypeFlags))
            {
                continue;
            }

            // Step, and face the direction.
            if (CanStep(map, npc, ref to))
            {
                map.MoveObject(npc, to.Tx, to.Ty, to.Tz);
                npc.WalkFrameIndex = 0;
                npc.Frame = DirFrame(d, 0);
            }

            return npc.Tx == to.Tx && npc.Ty == to.Ty;
        }

        return SwapPositions(map, npc, forActor);
    }

    /// <summary>Exult <c>Game_object::swap_positions</c>: trade places with an object of the same footprint.</summary>
    public static bool SwapPositions(GameMap map, U7Object a, U7Object b)
    {
        if (a.DimX != b.DimX || a.DimY != b.DimY)
        {
            return false; // Not the same size.
        }

        int ax = a.Tx, ay = a.Ty, az = a.Tz;
        map.MoveObject(a, b.Tx, b.Ty, b.Tz);
        map.MoveObject(b, ax, ay, az);
        return true;
    }

    /// <summary>
    /// Exult <c>Actor::is_blocked</c>: whether the actor, with its own 3D size
    /// for its frame and its type flags (walk, swim, fly), cannot stand on
    /// <paramref name="t"/> stepping from <paramref name="from"/> (default:
    /// where it is). <paramref name="t"/>.Tz becomes the lift it would be on.
    /// </summary>
    public static bool IsBlocked(GameMap map, U7Object actor, ref TileCoord t, TileCoord? from = null,
        int moveFlags = 0)
    {
        var info = map.Catalog[actor.Shape];
        var reflected = (actor.Frame & 32) != 0;
        var xtiles = Math.Max(1, reflected ? info.DimY : info.DimX);
        var ytiles = Math.Max(1, reflected ? info.DimX : info.DimY);
        var ztiles = info.DimZ;
        var flags = moveFlags | actor.TypeFlags;
        t = t.Wrapped();
        if (xtiles == 1 && ytiles == 1)
        {
            var blocked = map.Blocking.IsBlocked(ztiles, t.Tz, t.Tx, t.Ty, out var newLift, flags);
            t = t with { Tz = newLift };
            return blocked;
        }

        return map.Blocking.IsBlockedStep(xtiles, ytiles, ztiles, from ?? new TileCoord(actor.Tx, actor.Ty, actor.Tz),
            ref t, flags);
    }

    /// <summary>
    /// Where the actor would land stepping onto (tx, ty) from lift
    /// <paramref name="fromZ"/> (Exult <c>npc->is_blocked(to)</c> in the party
    /// code), or false if it cannot.
    /// </summary>
    public static bool ResolveStep(GameMap map, U7Object actor, int tx, int ty, int fromZ, out int nz)
    {
        var to = new TileCoord(tx, ty, fromZ);
        var blocked = IsBlocked(map, actor, ref to);
        nz = to.Tz;
        return !blocked;
    }

    /// <summary>Move one tile and advance the walk cycle in the given facing.</summary>
    public static void MoveTo(GameMap map, U7Object actor, int tx, int ty, int tz, int facing)
    {
        map.MoveObject(actor, tx, ty, tz);
        AdvanceWalkFrame(actor, facing);
    }

    public static void Stand(U7Object actor, int facing)
    {
        actor.WalkFrameIndex = 0;
        var frames = actor.NpcNum > 0 ? NpcFrames : AvatarFrames;
        actor.Frame = frames[facing][0];
    }

    public static void Sleep(U7Object actor, ShapeCatalog catalog)
    {
        var dir = FrameToDir(actor.Frame);
        var sleep = 13 + dir * 16;
        var count = catalog[actor.Shape].FrameCount;
        actor.Frame = sleep < count ? sleep : (dir * 16);
        actor.WalkFrameIndex = 0;
    }

    /// <summary>Facing (0 N, 2 E, 4 S, 6 W) from a frame's rotation bits.</summary>
    public static int FacingOfFrame(int frame) => ((frame >> 4) & 3) switch
    {
        0 => 0,
        1 => 4,
        2 => 6,
        _ => 2
    };

    /// <summary>Exult <c>Get_direction</c>: <see cref="DirectionNoWrap"/> with the deltas wrapped round the world.</summary>
    public static int Direction(int dy, int dx) => DirectionNoWrap(WrapDelta(dy), WrapDelta(dx));

    /// <summary>Exult <c>Wrap_Delta</c> (dir.cc), as it is.</summary>
    static int WrapDelta(int delta) =>
        delta >= U7Constants.NumTiles / 2 ? U7Constants.NumTiles / 2 - delta
        : delta <= -U7Constants.NumTiles / 2 ? -U7Constants.NumTiles / 2 - delta
        : delta;

    /// <summary>
    /// Exult <c>Get_direction_NoWrap</c>: one of 8 directions (0 north,
    /// clockwise) for a slope; <paramref name="dy"/> grows northwards.
    /// </summary>
    public static int DirectionNoWrap(int dy, int dx)
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

    /// <summary>Exult <c>Get_direction4</c>: north, east, south or west (0, 2, 4, 6); <paramref name="dy"/> grows northwards.</summary>
    public static int Direction4(int dy, int dx)
    {
        if (dx >= 0)
        {
            return dy > dx ? 0 : dy < -dx ? 4 : 2;
        }

        return dy > -dx ? 0 : dy < dx ? 4 : 6;
    }

    public static int DirIndex(int dx, int dy) => (dx, dy) switch
    {
        (0, -1) => 0,
        (1, -1) => 1,
        (1, 0) => 2,
        (1, 1) => 3,
        (0, 1) => 4,
        (-1, 1) => 5,
        (-1, 0) => 6,
        (-1, -1) => 7,
        _ => 4
    };

    static void AdvanceWalkFrame(U7Object actor, int facing)
    {
        var frames = actor.NpcNum > 0 ? NpcFrames : AvatarFrames;
        var cycle = frames[facing];
        // Exult Frames_sequence::get_next: wrap to 1, past the resting frame.
        actor.WalkFrameIndex = actor.WalkFrameIndex + 1 >= cycle.Length ? 1 : actor.WalkFrameIndex + 1;
        actor.Frame = cycle[actor.WalkFrameIndex];
    }

    static int FrameToDir(int frame)
    {
        var band = (frame >> 4) & 3;
        return band switch
        {
            0 => 0,
            1 => 1,
            2 => 2,
            _ => 3
        };
    }
}
