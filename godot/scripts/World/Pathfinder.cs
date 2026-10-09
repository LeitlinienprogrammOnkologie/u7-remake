using Godot;
using U7.Actors;
using U7.Core;
using U7.Data;

namespace U7.World;

/// <summary>Exult <c>Pathfinder_client</c>: step costs, the estimate, and when the search may stop.</summary>
public abstract class PathClient
{
    /// <summary>Exult <c>Pathfinder_client::get_max_cost</c>: give up at 3× the estimate, at least 74.</summary>
    public virtual int GetMaxCost(int costToGoal) => Math.Max(74, 3 * costToGoal);

    /// <summary>Cost of stepping onto <paramref name="to"/>, or -1 if blocked; may change its lift.</summary>
    public abstract int GetStepCost(TileCoord from, ref TileCoord to);

    public abstract int EstimateCost(TileCoord from, TileCoord to);

    /// <summary>Exult <c>Pathfinder_client::at_goal</c>: the goal's tile, at its lift unless that is -1.</summary>
    public virtual bool AtGoal(TileCoord tile, TileCoord goal) =>
        tile.Tx == goal.Tx && tile.Ty == goal.Ty && (goal.Tz == -1 || tile.Tz == goal.Tz);
}

/// <summary>
/// Exult <c>Actor_pathfinder_client</c>: an actor walking, with its own size
/// and movement flags. Every step costs 3; climbing or dropping a lift adds
/// 3, a closed unlocked door 3 (it gets opened on the way), swamp doubles the
/// cost and Black Gate's cobblestone road takes 1 off. With
/// <paramref name="ignoreNpcs"/> (Exult's persistent walks), NPCs on their
/// feet are no obstacle, though still avoided.
/// </summary>
public class ActorPathClient(GameMap map, U7Object npc, int dist = 0, bool ignoreNpcs = false) : PathClient
{
    /// <summary>Exult: at least three screens' width.</summary>
    public override int GetMaxCost(int costToGoal) => Math.Max(3 * costToGoal, Pathfinder.ScreenTilesWide * 2 * 3);

    /// <summary>
    /// Exult <c>Actor_pathfinder_client::check_blocking</c>: a blocked tile is
    /// still worth trying if a closed, unlocked door blocks it, away from the
    /// door's ends and not from inside the doorway; or, ignoring NPCs, if one
    /// that is up and not fighting stands there.
    /// </summary>
    int CheckBlocking(TileCoord from, TileCoord to)
    {
        if (ignoreNpcs)
        {
            if (map.FindBlocking(to) is not { } block)
            {
                return -1;
            }

            if (block.IsActor)
            {
                // Sitting, kneeling, lying down or fighting, it is an obstacle.
                var frnum = block.Frame & 0xf;
                if (frnum is >= ActorWalker.SitFrame and <= ActorWalker.SleepFrame ||
                    block.ScheduleType == ScheduleType.Combat)
                {
                    return -1;
                }

                return block.FrameTime != 0 ? 0 : 1; // Try to avoid non-moving NPCs.
            }
        }

        if (map.FindDoor(to) is not { } door || !map.IsClosedDoor(door) || door.Frame % 4 >= 2)
        {
            return -1;
        }

        var footX = door.Tx - door.DimX + 1;
        var footY = door.Ty - door.DimY + 1;
        if (door.DimY == 1 && (to.Tx == footX || to.Tx == U7Constants.WrapTile(footX + door.DimX - 1)))
        {
            return -1;
        }

        if (door.DimX == 1 && (to.Ty == footY || to.Ty == U7Constants.WrapTile(footY + door.DimY - 1)))
        {
            return -1;
        }

        var inX = U7Constants.TileDelta(footX, from.Tx) is var dx && dx >= 0 && dx < door.DimX;
        var inY = U7Constants.TileDelta(footY, from.Ty) is var dy && dy >= 0 && dy < door.DimY;
        return inX && inY ? -1 : 1;
    }

    /// <summary>Exult <c>Actor_pathfinder_client::get_step_cost</c>.</summary>
    public override int GetStepCost(TileCoord from, ref TileCoord to)
    {
        var cost = 1;
        var flat = map.GetFlat(to.Tx, to.Ty);
        var poison = map.Catalog[flat.Shape].Poisonous;
        var oldLift = to.Tz;
        if (ActorWalker.IsBlocked(map, npc, ref to, from))
        {
            var ret = CheckBlocking(from, to);
            if (ret < 0)
            {
                return -1;
            }

            cost += ret;
        }

        if (oldLift != to.Tz)
        {
            cost++;
        }

        // Exult means "50% more on the diagonal", but every neighbour differs in x or y.
        cost *= 3;
        if (poison && to.Tz == 0)
        {
            cost *= 2;
        }

        if (flat.Shape == 24 && flat.Frame <= 1)
        {
            cost--; // Cobblestone path.
        }

        return cost;
    }

    /// <summary>Exult <c>Actor_pathfinder_client::estimate_cost</c>: straight 2, diagonal 3.</summary>
    public override int EstimateCost(TileCoord from, TileCoord to)
    {
        var dx = Unwrap(to.Tx - from.Tx);
        var dy = Unwrap(to.Ty - from.Ty);
        return 2 * Math.Max(dx, dy) + Math.Min(dx, dy);
    }

    // Exult only wraps the negative side.
    protected static int Unwrap(int d) => d < -U7Constants.NumTiles / 2 ? d + U7Constants.NumTiles : Math.Abs(d);

    public override bool AtGoal(TileCoord tile, TileCoord goal) =>
        (goal.Tz == -1 ? tile.Distance2d(goal) : tile.Distance(goal)) <= dist;
}

/// <summary>
/// Exult <c>Onecoord_pathfinder_client</c>: an actor walking to a line, the
/// goal's x or its y (the other -1), estimated at 2 a tile.
/// </summary>
public sealed class OnecoordPathClient(GameMap map, U7Object npc, bool ignoreNpcs = false)
    : ActorPathClient(map, npc, 0, ignoreNpcs)
{
    public override int EstimateCost(TileCoord from, TileCoord to) =>
        to.Tx == -1 ? 2 * Unwrap(to.Ty - from.Ty)
        : to.Ty == -1 ? 2 * Unwrap(to.Tx - from.Tx)
        : base.EstimateCost(from, to);

    public override bool AtGoal(TileCoord tile, TileCoord goal) =>
        (goal.Tx == -1 || tile.Tx == goal.Tx) && (goal.Ty == -1 || tile.Ty == goal.Ty) &&
        (goal.Tz == -1 || tile.Tz == goal.Tz);
}

/// <summary>
/// Exult <c>Offscreen_pathfinder_client</c>: an actor's way off the screen
/// (the window in tiles enlarged by 3), towards the best point if one is
/// given and not too far off: penalised for steps away from it.
/// </summary>
public sealed class OffscreenPathClient : ActorPathClient
{
    readonly Rect2I _screen;
    readonly TileCoord? _best;

    public OffscreenPathClient(GameMap map, U7Object npc, Rect2I window, TileCoord? best = null, bool ignoreNpcs = false)
        : base(map, npc, 0, ignoreNpcs)
    {
        _screen = window.Grow(3);
        if (best is not { } b)
        {
            return;
        }

        // Scale (roughly) to the edge of the screen; give up beyond 4 screens or if it doesn't look right.
        var cx = window.Position.X + window.Size.X / 2;
        var cy = window.Position.Y + window.Size.Y / 2;
        var centre = new TileCoord(cx, cy, 0);
        if (b.Distance2d(centre) > 4 * window.Size.X)
        {
            return;
        }

        var tx = b.Tx > cx + window.Size.X ? window.End.X + 1 : b.Tx < cx - window.Size.X ? window.Position.X - 1 : b.Tx;
        var ty = b.Ty > cy + window.Size.Y ? window.End.Y + 1 : b.Ty < cy - window.Size.Y ? window.Position.Y - 1 : b.Ty;
        var scaled = new TileCoord(tx, ty, b.Tz);
        if (scaled.Distance2d(centre) <= window.Size.X)
        {
            _best = scaled;
        }
    }

    public override int GetStepCost(TileCoord from, ref TileCoord to)
    {
        var cost = base.GetStepCost(from, ref to);
        if (cost == -1 || _best is not { } best)
        {
            return cost;
        }

        if ((to.Tx - from.Tx) * (best.Tx - from.Tx) < 0)
        {
            cost++;
        }

        if ((to.Ty - from.Ty) * (best.Ty - from.Ty) < 0)
        {
            cost++;
        }

        return cost;
    }

    public override int EstimateCost(TileCoord from, TileCoord to)
    {
        if (_best is { } best)
        {
            return base.EstimateCost(from, best);
        }

        var dx = Math.Min(from.Tx - _screen.Position.X, _screen.End.X - from.Tx);
        var dy = Math.Min(from.Ty - _screen.Position.Y, _screen.End.Y - from.Ty);
        var cost = Math.Max(0, Math.Min(dx, dy));
        if (to.Tz != -1 && from.Tz != to.Tz)
        {
            cost++;
        }

        return 2 * cost;
    }

    /// <summary>Off the screen (the lift shifts a tile half a tile up-left a lift, as it is drawn).</summary>
    public override bool AtGoal(TileCoord tile, TileCoord goal) =>
        !Pathfinder.HasTile(_screen, tile.Tx - tile.Tz / 2, tile.Ty - tile.Tz / 2);
}

/// <summary>
/// Exult <c>Approach_object_pathfinder_client</c>: an actor walking to
/// within <paramref name="dist"/> of an object, anywhere in its footprint
/// enlarged by that much and within 5 lifts of it.
/// </summary>
public sealed class ApproachPathClient(GameMap map, U7Object npc, U7Object target, int dist)
    : ActorPathClient(map, npc, dist)
{
    readonly (int X, int Y, int W, int H) _box = (target.Tx - target.DimX + 1 - dist, target.Ty - target.DimY + 1 - dist,
        target.DimX + 2 * dist, target.DimY + 2 * dist);

    public override bool AtGoal(TileCoord tile, TileCoord goal)
    {
        var dz = tile.Tz - goal.Tz;
        if (dz is > 5 or < -5)
        {
            return false; // Got to be on the same floor.
        }

        var dx = U7Constants.TileDelta(_box.X, tile.Tx);
        var dy = U7Constants.TileDelta(_box.Y, tile.Ty);
        return dx >= 0 && dx < _box.W && dy >= 0 && dy < _box.H;
    }
}

/// <summary>
/// Exult <c>Fast_pathfinder_client</c>: a quick search that gives up soon
/// (at twice the estimate, 8 to 64), for anything one lift high stepping a
/// lift up or down, done once the walker's footprint touches the target's
/// footprint (or a spot) enlarged by <c>dist</c>, within 5 lifts.
/// </summary>
public class FastPathClient : PathClient
{
    readonly GameMap _map;
    readonly int _moveFlags;
    readonly int _axtiles;
    readonly int _aytiles;
    readonly int _aztiles;
    readonly (int X, int Y, int W, int H) _destBox;

    protected GameMap Map => _map;
    protected int MoveFlags => _moveFlags;
    protected (int X, int Y, int Z) Size => (_axtiles, _aytiles, _aztiles);

    public FastPathClient(GameMap map, U7Object from, U7Object to, int dist)
        : this(map, from, (to.Tx - to.DimX + 1 - dist, to.Ty - to.DimY + 1 - dist, to.DimX + 2 * dist,
            to.DimY + 2 * dist))
    {
    }

    /// <summary>Exult enlarges an empty rectangle at the spot, so the box runs from dist before it to dist - 1 after.</summary>
    public FastPathClient(GameMap map, U7Object from, TileCoord dest, int dist)
        : this(map, from, (dest.Tx - dist, dest.Ty - dist, 2 * dist, 2 * dist))
    {
    }

    FastPathClient(GameMap map, U7Object from, (int, int, int, int) destBox)
    {
        _map = map;
        _moveFlags = from.TypeFlags;
        var info = map.Catalog[from.Shape];
        var reflected = (from.Frame & 32) != 0;
        _axtiles = Math.Max(1, reflected ? info.DimY : info.DimX);
        _aytiles = Math.Max(1, reflected ? info.DimX : info.DimY);
        _aztiles = info.DimZ;
        _destBox = destBox;
    }

    public override int GetMaxCost(int costToGoal) => Math.Clamp(2 * costToGoal, 8, 64);

    public override int GetStepCost(TileCoord from, ref TileCoord to)
    {
        if (_map.Blocking.IsBlocked(1, to.Tz, to.Tx, to.Ty, out var newLift, _moveFlags, 1, 1))
        {
            return -1;
        }

        to = to with { Tz = newLift };
        return 1;
    }

    public override int EstimateCost(TileCoord from, TileCoord to) => from.Distance(to);

    public override bool AtGoal(TileCoord tile, TileCoord goal)
    {
        var dz = tile.Tz - goal.Tz;
        if (dz is > 5 or < -5)
        {
            return false; // Got to be on the same floor.
        }

        int ax = tile.Tx - _axtiles + 1, ay = tile.Ty - _aytiles + 1;
        return ax < _destBox.X + _destBox.W && _destBox.X < ax + _axtiles &&
               ay < _destBox.Y + _destBox.H && _destBox.Y < ay + _aytiles;
    }

    // Exult Block: a footprint with a lift and height.
    readonly record struct Block(int X, int Y, int Z, int W, int D, int H)
    {
        public static Block Of(U7Object obj) => new(obj.Tx - obj.DimX + 1, obj.Ty - obj.DimY + 1, obj.Tz, obj.DimX,
            obj.DimY, Math.Max(1, obj.DimZ));

        public bool Has(TileCoord t) =>
            U7Constants.TileDelta(X, t.Tx) is var dx && dx >= 0 && dx < W &&
            U7Constants.TileDelta(Y, t.Ty) is var dy && dy >= 0 && dy < D && t.Tz >= Z && t.Tz < Z + H;

        public Block Moved(int dx, int dy, int dz) => this with { X = X + dx, Y = Y + dy, Z = Z + dz };
    }

    /// <summary>Exult <c>Get_closest_edge</c>: the facing edges of the two blocks, from the top tile of the first.</summary>
    static void ClosestEdge(Block from, Block to, ref TileCoord pos1, ref TileCoord pos2)
    {
        if (pos2.Tx < pos1.Tx)
        {
            pos1 = pos1 with { Tx = from.X }; // Going left.
        }
        else
        {
            pos2 = pos2 with { Tx = to.X };
        }

        if (pos2.Ty < pos1.Ty)
        {
            pos1 = pos1 with { Ty = from.Y }; // Going north.
        }
        else
        {
            pos2 = pos2 with { Ty = to.Y };
        }

        if (pos2.Tz < pos1.Tz)
        {
            pos2 = pos2 with { Tz = pos2.Tz + to.H - 1 }; // Going down (needed for sails).
        }

        pos1 = pos1 with { Tz = pos1.Tz + from.H - 1 }; // Use the top tile.
    }

    /// <summary>
    /// Exult <c>Fast_pathfinder_client::is_grabable</c>: whether
    /// <paramref name="from"/> could reach <paramref name="to"/>, i.e. is next
    /// to it or can get within 1-5 tiles of it by a short walk and from there
    /// in a straight line past nothing fixed (actors and movable things don't
    /// count).
    /// </summary>
    public static bool IsGrabable(GameMap map, U7Object from, U7Object to)
    {
        if (ObjectGeometry.Distance(from, to) <= 1)
        {
            return true; // Already okay.
        }

        for (var i = 1; i <= 5; i++)
        {
            if (IsGrabable(map, from, to, new FastPathClient(map, from, to, i)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Exult <c>Fast_pathfinder_client::is_grabable</c> to a spot: a 1×1×1 block there.</summary>
    public static bool IsGrabable(GameMap map, U7Object from, TileCoord to) =>
        IsGrabable(map, from, new U7Object { Tx = to.Tx, Ty = to.Ty, Tz = to.Tz, DimX = 1, DimY = 1, DimZ = 1 });

    // Exult is_grabable_internal.
    static bool IsGrabable(GameMap map, U7Object from, U7Object to, FastPathClient client)
    {
        var fromvol = Block.Of(from);
        var tovol = Block.Of(to);
        var here = new TileCoord(from.Tx, from.Ty, from.Tz);
        var src = here;
        var dst = new TileCoord(to.Tx, to.Ty, to.Tz);
        ClosestEdge(fromvol, tovol, ref src, ref dst);
        src = src with { Tz = from.Tz };
        if (AstarSteps.Find(client, src, dst) is not { } path)
        {
            return false;
        }

        var t = here;
        if (path.StepsLeft > 0)
        {
            while (path.NextStep(out var step, out _))
            {
                t = step;
                if (t != here && !client.AtGoal(t, dst) && map.Blocking.Test(t.Tx, t.Ty, t.Tz))
                {
                    return false; // Blocked.
                }
            }
        }

        if (!client.AtGoal(t, dst))
        {
            return false;
        }

        var srcvol = fromvol;
        fromvol = fromvol.Moved(t.Tx - src.Tx, t.Ty - src.Ty, t.Tz - src.Tz);
        dst = new TileCoord(to.Tx, to.Ty, to.Tz);
        ClosestEdge(fromvol, tovol, ref t, ref dst);
        if (ZombieSteps.Line(t, dst) is not { } line)
        {
            return false;
        }

        while (line.NextStep(out var step, out _))
        {
            if (!tovol.Has(step) && !srcvol.Has(step) && !fromvol.Has(step) &&
                map.Blocking.Test(step.Tx, step.Ty, step.Tz) && map.FindBlocking(step) is { } block &&
                // Ignore all blocking actors and movable objects.
                !block.IsActor && !(block.Kind == ObjectKind.Ireg && map.Catalog[block.Shape].Weight > 0))
            {
                return false; // Blocked.
            }
        }

        return true;
    }
}

/// <summary>
/// Exult <c>Monster_pathfinder_client</c>: the quick search for a walker of
/// its own size (any step it could take, all costing 1), trying harder the
/// cleverer it is (twice the estimate plus half its intelligence, 18 to
/// three quarters of the screen's width).
/// </summary>
public sealed class MonsterPathClient : FastPathClient
{
    readonly int _intelligence;

    public MonsterPathClient(GameMap map, U7Object npc, TileCoord dest, int dist)
        : base(map, npc, dest, dist) =>
        _intelligence = npc.GetProp(ActorProp.Intelligence);

    /// <summary>Exult's combat client: to within <paramref name="reach"/> of the opponent's footprint.</summary>
    public MonsterPathClient(GameMap map, U7Object attacker, U7Object opponent, int reach)
        : base(map, attacker, opponent, reach) =>
        _intelligence = attacker.GetProp(ActorProp.Intelligence);

    public override int GetMaxCost(int costToGoal) =>
        Math.Max(18, Math.Min(2 * costToGoal + _intelligence / 2, 3 * Pathfinder.ScreenTilesWide / 4));

    public override int GetStepCost(TileCoord from, ref TileCoord to)
    {
        var (x, y, z) = Size;
        return Map.Blocking.IsBlockedStep(x, y, z, from, ref to, MoveFlags) ? -1 : 1;
    }
}

/// <summary>
/// Exult <c>Find_path</c> (pathfinder/path.cc): A* over tiles and lifts. The
/// open set is Exult's: a chain per total cost, newest first. There is no
/// node limit; the search gives up once nothing under the client's cost
/// ceiling is left.
/// </summary>
public static class Pathfinder
{
    /// <summary>Exult <c>TileRect::has_world_point</c>: the tile is in the rectangle, across the world's wrap.</summary>
    public static bool HasTile(Rect2I rect, int tx, int ty) =>
        U7Constants.TileDelta(rect.Position.X, tx) is var dx && dx >= 0 && dx < rect.Size.X &&
        U7Constants.TileDelta(rect.Position.Y, ty) is var dy && dy >= 0 && dy < rect.Size.Y;

    /// <summary>Exult <c>gwin->get_width() / c_tilesize</c>: the game window's width in tiles.</summary>
    public static int ScreenTilesWide { get; set; } = 40;

    // Exult Neighbor_iterator: NW, N, NE, W, E, SW, S, SE.
    static readonly int[] Coords = [-1, -1, 0, -1, 1, -1, -1, 0, 1, 0, -1, 1, 0, 1, 1, 1];

    sealed class Node(TileCoord tile, int startCost, int goalCost, Node? parent)
    {
        public readonly TileCoord Tile = tile;
        public int StartCost = startCost;
        public int GoalCost = goalCost;
        public int TotalCost = startCost + goalCost;
        public Node? Parent = parent;
        public Node? PriorityNext;

        public bool IsOpen => PriorityNext is not null;

        public void Update(int startCost, int goalCost, Node parent)
        {
            StartCost = startCost;
            GoalCost = goalCost;
            TotalCost = startCost + goalCost;
            Parent = parent;
        }

        /// <summary>Exult <c>Search_node::add_to_chain</c>: insert after 'last', i.e. first in line.</summary>
        public void AddToChain(ref Node? last)
        {
            if (last is not null)
            {
                PriorityNext = last.PriorityNext;
                last.PriorityNext = this;
            }
            else
            {
                last = this;
                PriorityNext = this;
            }
        }

        public void RemoveFromChain(ref Node? last)
        {
            if (PriorityNext == this)
            {
                last = null;
            }
            else
            {
                var prev = last!;
                do
                {
                    var next = prev.PriorityNext!;
                    if (next == this)
                    {
                        break;
                    }

                    prev = next;
                } while (prev != last);

                prev.PriorityNext = PriorityNext;
                if (last == this)
                {
                    last = PriorityNext;
                }
            }

            PriorityNext = null;
        }

        public static Node RemoveFirstFromChain(ref Node? last)
        {
            var first = last!.PriorityNext!;
            if (first == last)
            {
                last = null;
            }
            else
            {
                last.PriorityNext = first.PriorityNext;
            }

            first.PriorityNext = null;
            return first;
        }
    }

    /// <summary>
    /// Exult <c>A_star_queue</c>. It starts with 512 empty cost buckets and
    /// 'best' just past them, so (as in Exult) a start estimate of 512 or more
    /// (some 250 tiles) finds nothing.
    /// </summary>
    sealed class OpenSet
    {
        const int InitialBuckets = 512;
        readonly List<Node?> _open = new(new Node?[InitialBuckets]);
        readonly Dictionary<TileCoord, Node> _lookup = new(1000);
        int _best = InitialBuckets;

        Node? Last(int pri) => pri < _open.Count ? _open[pri] : null;

        void SetLast(int pri, Node? node)
        {
            while (pri >= _open.Count)
            {
                _open.Add(null);
            }

            _open[pri] = node;
        }

        void SkipEmpty()
        {
            for (_best++; _best < _open.Count && _open[_best] is null; _best++)
            {
            }
        }

        public void AddBack(Node nd)
        {
            var last = Last(nd.TotalCost);
            nd.AddToChain(ref last);
            SetLast(nd.TotalCost, last);
            _best = Math.Min(_best, nd.TotalCost);
        }

        public void Add(Node nd)
        {
            _lookup[nd.Tile] = nd;
            AddBack(nd);
        }

        public void RemoveFromOpen(Node nd)
        {
            if (!nd.IsOpen)
            {
                return;
            }

            var last = Last(nd.TotalCost);
            if (last is not null)
            {
                nd.RemoveFromChain(ref last);
                SetLast(nd.TotalCost, last);
            }

            if (last is null && nd.TotalCost == _best)
            {
                SkipEmpty();
            }
        }

        public Node? Pop()
        {
            var last = Last(_best);
            if (last is null)
            {
                return null;
            }

            var node = Node.RemoveFirstFromChain(ref last);
            SetLast(_best, last);
            if (last is null)
            {
                SkipEmpty();
            }

            return node;
        }

        public Node? Find(TileCoord tile) => _lookup.GetValueOrDefault(tile);
    }

    /// <summary>
    /// Exult <c>Find_path</c>: the tiles from just after <paramref name="start"/>
    /// to the goal (empty if already there), or null if there is no way within
    /// the client's cost ceiling.
    /// </summary>
    public static List<TileCoord>? FindPath(PathClient client, TileCoord start, TileCoord goal)
    {
        var nodes = new OpenSet();
        var maxCost = client.EstimateCost(start, goal);
        nodes.Add(new Node(start, 0, maxCost, null));
        maxCost = client.GetMaxCost(maxCost);
        while (nodes.Pop() is { } node)
        {
            var cur = node.Tile;
            if (client.AtGoal(cur, goal))
            {
                return CreatePath(node);
            }

            for (var i = 0; i < Coords.Length; i += 2)
            {
                var ntile = new TileCoord(U7Constants.WrapTile(cur.Tx + Coords[i]), U7Constants.WrapTile(cur.Ty + Coords[i + 1]),
                    cur.Tz);
                var stepCost = client.GetStepCost(cur, ref ntile);
                if (stepCost == -1)
                {
                    continue;
                }

                var newCost = node.StartCost + stepCost;
                var next = nodes.Find(ntile);
                if (next is not null && next.StartCost <= newCost)
                {
                    continue;
                }

                var newGoalCost = client.EstimateCost(ntile, goal);
                if (newCost + newGoalCost >= maxCost)
                {
                    continue;
                }

                if (next is null)
                {
                    nodes.Add(new Node(ntile, newCost, newGoalCost, node));
                }
                else
                {
                    nodes.RemoveFromOpen(next);
                    next.Update(newCost, newGoalCost, node);
                    nodes.AddBack(next);
                }
            }
        }

        return null;
    }

    /// <summary>Exult <c>Search_node::create_path</c>: the tiles after the start, in order.</summary>
    static List<TileCoord> CreatePath(Node goal)
    {
        var path = new List<TileCoord>();
        for (var n = goal; n.Parent is not null; n = n.Parent)
        {
            path.Add(n.Tile);
        }

        path.Reverse();
        return path;
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
}
