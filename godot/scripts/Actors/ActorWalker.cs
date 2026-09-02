using U7.Core;
using U7.Data;

namespace U7.Actors;

/// <summary>
/// Shared one-tile step + walk frames. Avatar uses a 3-frame cycle;
/// NPCs use Exult's 5-frame <c>{0,1,0,2,0}</c> per cardinal.
/// </summary>
public static class ActorWalker
{
    static readonly int[][] AvatarFrames =
    [
        [0, 1, 2],
        [0, 1, 2],
        [48, 49, 50],
        [16, 17, 18],
        [16, 17, 18],
        [16, 17, 18],
        [32, 33, 34],
        [0, 1, 2]
    ];

    static readonly int[][] NpcFrames =
    [
        [0, 1, 0, 2, 0],
        [0, 1, 0, 2, 0],
        [48, 49, 48, 50, 48],
        [16, 17, 16, 18, 16],
        [16, 17, 16, 18, 16],
        [16, 17, 16, 18, 16],
        [32, 33, 32, 34, 32],
        [0, 1, 0, 2, 0]
    ];

    public static bool TryStep(GameMap map, U7Object actor, int dx, int dy)
    {
        if (dx == 0 && dy == 0)
        {
            Stand(actor, actor.WalkFrameIndex == 0 ? 4 : DirIndex(0, 0));
            return false;
        }

        var facing = DirIndex(dx, dy);
        var nx = U7Constants.WrapTile(actor.Tx + dx);
        var ny = U7Constants.WrapTile(actor.Ty + dy);
        var nz = actor.Tz;
        if (map.IsBlocked(nx, ny, nz))
        {
            if (!map.IsBlocked(nx, ny, nz + 1))
            {
                nz += 1;
            }
            else
            {
                Stand(actor, facing);
                return false;
            }
        }
        else if (nz > 0 && !map.IsBlocked(nx, ny, nz - 1) && !HasFloor(map, nx, ny, nz))
        {
            nz -= 1;
        }

        map.MoveObject(actor, nx, ny, nz);
        AdvanceWalkFrame(actor, facing);
        return true;
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
        actor.WalkFrameIndex = (actor.WalkFrameIndex + 1) % cycle.Length;
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

    static bool HasFloor(GameMap map, int tx, int ty, int lift)
    {
        foreach (var obj in map.ObjectsInChunk(tx / 16, ty / 16))
        {
            if (obj.IsActor || obj.Removed)
            {
                continue;
            }

            if (obj.Occupies(tx, ty) && obj.Tz + obj.DimZ == lift)
            {
                return true;
            }
        }

        return lift <= 0;
    }
}
