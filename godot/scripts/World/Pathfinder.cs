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
public sealed class ActorPathClient(GameMap map, U7Object npc, int dist = 0, bool ignoreNpcs = false) : PathClient
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
    static int Unwrap(int d) => d < -U7Constants.NumTiles / 2 ? d + U7Constants.NumTiles : Math.Abs(d);

    public override bool AtGoal(TileCoord tile, TileCoord goal) =>
        (goal.Tz == -1 ? tile.Distance2d(goal) : tile.Distance(goal)) <= dist;
}

/// <summary>
/// Exult <c>Find_path</c> (pathfinder/path.cc): A* over tiles and lifts. The
/// open set is Exult's: a chain per total cost, newest first. There is no
/// node limit; the search gives up once nothing under the client's cost
/// ceiling is left.
/// </summary>
public static class Pathfinder
{
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
