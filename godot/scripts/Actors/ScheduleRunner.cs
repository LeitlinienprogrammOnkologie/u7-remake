using Godot;
using U7.Core;
using U7.Data;
using U7.World;

namespace U7.Actors;

/// <summary>
/// Per-NPC schedule brain. Nearby NPCs (~32 tiles) walk; far NPCs sit on
/// their current slot destination until the avatar approaches.
/// </summary>
public sealed class ScheduleRunner
{
    public const int ActivityDist = U7Constants.NpcActivityDist;

    readonly GameMap _map;
    readonly U7Object _avatar;
    readonly List<U7Object?> _npcs;
    readonly ScheduleTable _table;
    readonly GameClock _clock;
    readonly Dictionary<int, Brain> _brains = new();
    readonly HashSet<int> _loggedUnknown = new();
    readonly Random _rng = new();
    readonly double _stepInterval = U7Constants.StandardDelayMs / 1000.0;

    public ScheduleRunner(GameMap map, U7Object avatar, List<U7Object?> npcs,
        ScheduleTable table, GameClock clock)
    {
        _map = map;
        _avatar = avatar;
        _npcs = npcs;
        _table = table;
        _clock = clock;
        for (var i = 1; i < npcs.Count; i++)
        {
            if (npcs[i] is not { Unused: false } npc)
            {
                continue;
            }

            _brains[i] = new Brain
            {
                Npc = npc,
                Dest = new TileCoord(npc.Tx, npc.Ty, npc.Tz),
                Center = new TileCoord(npc.Tx, npc.Ty, npc.Tz)
            };
        }

        clock.SlotChanged += _ => ApplySlot(pathIfNearby: true);
        ApplySlot(pathIfNearby: false);
    }

    public void SetScheduleType(U7Object npc, int type)
    {
        if (npc.NpcNum <= 0 || !_brains.TryGetValue(npc.NpcNum, out var b))
        {
            npc.ScheduleType = type;
            return;
        }

        BeginType(b, type, b.Dest, alreadyThere: true);
    }

    /// <summary>
    /// Restore a schedule after combat. Unlike <see cref="SetScheduleType"/>
    /// the NPC walks back to its slot destination if it wandered off.
    /// </summary>
    public void ResumeSchedule(U7Object npc, int type)
    {
        if (npc.NpcNum <= 0 || !_brains.TryGetValue(npc.NpcNum, out var b))
        {
            npc.ScheduleType = type;
            return;
        }

        BeginType(b, type, b.Dest, alreadyThere: false);
    }

    public int GetActualType(U7Object npc)
    {
        if (npc.ScheduleType == ScheduleType.WalkToSchedule && npc.PendingSchedule >= 0)
        {
            return npc.PendingSchedule;
        }

        return npc.ScheduleType;
    }

    public void Update(double delta, bool frozen)
    {
        if (frozen)
        {
            return;
        }

        foreach (var b in _brains.Values)
        {
            Tick(b, delta);
        }
    }

    void ApplySlot(bool pathIfNearby)
    {
        var slot = _clock.Slot;
        foreach (var b in _brains.Values)
        {
            // Exult Game_window::schedule_npcs skips wait / follow_avatar so
            // companions (Iolo) stay with the avatar instead of Britain.
            if (b.Npc.ScheduleType is ScheduleType.Wait or ScheduleType.FollowAvatar
                or ScheduleType.Combat)
            {
                continue;
            }

            var entry = _table.ForSlot(b.Npc.NpcNum, slot);
            if (entry is null)
            {
                continue;
            }

            var dest = new TileCoord(entry.Value.Tx, entry.Value.Ty, entry.Value.Tz);
            var type = entry.Value.Type;
            var nearby = Dist(b.Npc) <= ActivityDist;
            var same = b.Npc.ScheduleType == type &&
                       b.Dest.Tx == dest.Tx && b.Dest.Ty == dest.Ty && b.Dest.Tz == dest.Tz &&
                       b.Npc.ScheduleType != ScheduleType.WalkToSchedule;
            if (same && nearby)
            {
                continue;
            }

            b.Dest = dest;
            b.Npc.ScheduleDestTx = dest.Tx;
            b.Npc.ScheduleDestTy = dest.Ty;
            b.Npc.ScheduleDestTz = dest.Tz;
            if (!nearby || !pathIfNearby)
            {
                Teleport(b, dest);
                BeginType(b, type, dest, alreadyThere: true);
            }
            else
            {
                BeginWalkTo(b, type, dest);
            }
        }
    }

    void Tick(Brain b, double delta)
    {
        var nearby = Dist(b.Npc) <= ActivityDist;
        if (!nearby)
        {
            if (b.WasNearby)
            {
                Teleport(b, b.Dest);
                if (b.Npc.PendingSchedule >= 0)
                {
                    BeginType(b, b.Npc.PendingSchedule, b.Dest, alreadyThere: true);
                }
            }

            b.WasNearby = false;
            return;
        }

        if (!b.WasNearby)
        {
            b.WasNearby = true;
            var here = new TileCoord(b.Npc.Tx, b.Npc.Ty, b.Npc.Tz);
            if (here.Distance2d(b.Dest) > 3)
            {
                BeginWalkTo(b, b.Npc.PendingSchedule >= 0 ? b.Npc.PendingSchedule : b.Npc.ScheduleType, b.Dest);
            }
        }

        b.StepTimer -= delta;
        b.Pause -= delta;
        if (b.Pause > 0 || b.StepTimer > 0)
        {
            return;
        }

        b.StepTimer = _stepInterval;
        NowWhat(b);
    }

    void NowWhat(Brain b)
    {
        var type = b.Npc.ScheduleType;
        switch (type)
        {
            case ScheduleType.WalkToSchedule:
                StepAlongPath(b, arrive: () =>
                {
                    var next = b.Npc.PendingSchedule >= 0 ? b.Npc.PendingSchedule : ScheduleType.Stand;
                    BeginType(b, next, b.Dest, alreadyThere: true);
                });
                break;
            case ScheduleType.Loiter:
            case ScheduleType.Wander:
            case ScheduleType.Patrol:
            {
                if (b.Path is { Count: > 0 } && b.PathI < b.Path.Count)
                {
                    StepAlongPath(b, arrive: () => b.Pause = 0.4 + _rng.NextDouble() * 1.6);
                    break;
                }

                var radius = type == ScheduleType.Wander ? 16 : type == ScheduleType.Patrol ? 4 : 8;
                var tx = b.Center.Tx - radius + _rng.Next(radius * 2 + 1);
                var ty = b.Center.Ty - radius + _rng.Next(radius * 2 + 1);
                SetPath(b, tx, ty);
                if (b.Path is null || b.Path.Count == 0)
                {
                    b.Pause = 0.8;
                }

                break;
            }
            case ScheduleType.HorizPace:
                Pace(b, horiz: true);
                break;
            case ScheduleType.VertPace:
                Pace(b, horiz: false);
                break;
            case ScheduleType.Sleep:
                if (AtDest(b, 2))
                {
                    ActorWalker.Sleep(b.Npc, _map.Catalog);
                    b.Pause = 2;
                }
                else
                {
                    StepAlongPath(b, arrive: () => ActorWalker.Sleep(b.Npc, _map.Catalog));
                }

                break;
            case ScheduleType.Sit:
            case ScheduleType.EatAtInn:
            case ScheduleType.Stand:
            case ScheduleType.Wait:
            case ScheduleType.TendShop:
            case ScheduleType.Eat:
            case ScheduleType.DeskWork:
            case ScheduleType.Waiter:
            case ScheduleType.Talk:
            case ScheduleType.Dance:
            case ScheduleType.Farm:
            case ScheduleType.Miner:
            case ScheduleType.Hound:
            case ScheduleType.Blacksmith:
            case ScheduleType.Graze:
            case ScheduleType.Bake:
            case ScheduleType.Sew:
            case ScheduleType.Shy:
            case ScheduleType.Lab:
            case ScheduleType.Thief:
            case ScheduleType.Special:
            case ScheduleType.KidGames:
            case ScheduleType.Duel:
            case ScheduleType.Preach:
            case ScheduleType.Combat:
                break;
            case ScheduleType.FollowAvatar:
                if (!AtDest(b, 2) && b.Path is { Count: > 0 })
                {
                    StepAlongPath(b, arrive: () => ActorWalker.Stand(b.Npc, 4));
                }
                else if (!AtDest(b, 2))
                {
                    SetPath(b, b.Dest.Tx, b.Dest.Ty);
                }
                else
                {
                    ActorWalker.Stand(b.Npc, FrameFacing(b.Npc.Frame));
                    b.Pause = 1.5;
                }

                if (!_loggedUnknown.Contains(type) && type is not (
                    ScheduleType.Stand or ScheduleType.Wait or ScheduleType.Sit or
                    ScheduleType.EatAtInn or ScheduleType.TendShop or ScheduleType.Eat))
                {
                    _loggedUnknown.Add(type);
                    GD.Print($"schedule stub {ScheduleType.Name(type)} ({type}) npc {b.Npc.NpcNum}");
                }

                break;
            default:
                if (_loggedUnknown.Add(type))
                {
                    GD.Print($"unknown schedule {type} npc {b.Npc.NpcNum}");
                }

                ActorWalker.Stand(b.Npc, 4);
                b.Pause = 2;
                break;
        }
    }

    void BeginWalkTo(Brain b, int pending, TileCoord dest)
    {
        b.Dest = dest;
        b.Npc.PendingSchedule = pending;
        b.Npc.ScheduleType = ScheduleType.WalkToSchedule;
        b.Failures = 0;
        if (AtDest(b, 3))
        {
            BeginType(b, pending, dest, alreadyThere: true);
            return;
        }

        SetPath(b, dest.Tx, dest.Ty);
        if (b.Path is null)
        {
            b.Failures++;
            if (b.Failures >= 2)
            {
                Teleport(b, dest);
                BeginType(b, pending, dest, alreadyThere: true);
            }
        }
    }

    void BeginType(Brain b, int type, TileCoord dest, bool alreadyThere)
    {
        b.Dest = dest;
        b.Center = dest;
        b.Npc.PendingSchedule = -1;
        b.Npc.ScheduleType = type;
        b.Npc.ScheduleDestTx = dest.Tx;
        b.Npc.ScheduleDestTy = dest.Ty;
        b.Npc.ScheduleDestTz = dest.Tz;
        b.Path = null;
        b.PathI = 0;
        b.Failures = 0;
        b.PaceDir = 1;
        if (!alreadyThere && DistFrom(b.Npc, dest) > 3)
        {
            BeginWalkTo(b, type, dest);
            return;
        }

        switch (type)
        {
            case ScheduleType.Sleep:
                ActorWalker.Sleep(b.Npc, _map.Catalog);
                break;
            case ScheduleType.HorizPace:
            case ScheduleType.VertPace:
                ActorWalker.Stand(b.Npc, type == ScheduleType.HorizPace ? 2 : 4);
                break;
            default:
                ActorWalker.Stand(b.Npc, FrameFacing(b.Npc.Frame));
                break;
        }
    }

    void Pace(Brain b, bool horiz)
    {
        var origin = b.Center;
        var pos = horiz ? b.Npc.Tx : b.Npc.Ty;
        var originV = horiz ? origin.Tx : origin.Ty;
        if (Math.Abs(U7Constants.TileDelta(originV, pos)) >= 4)
        {
            b.PaceDir = -Math.Sign(U7Constants.TileDelta(originV, pos));
            if (b.PaceDir == 0)
            {
                b.PaceDir = 1;
            }
        }

        var dx = horiz ? b.PaceDir : 0;
        var dy = horiz ? 0 : b.PaceDir;
        if (!ActorWalker.TryStep(_map, b.Npc, dx, dy))
        {
            b.PaceDir = -b.PaceDir;
        }
    }

    void StepAlongPath(Brain b, Action arrive)
    {
        if (b.Path is null || b.PathI >= b.Path.Count)
        {
            if (AtDest(b, 3) || b.Failures >= 2)
            {
                if (!AtDest(b, 3) && b.Failures >= 2)
                {
                    Teleport(b, b.Dest);
                }

                arrive();
                return;
            }

            SetPath(b, b.Dest.Tx, b.Dest.Ty);
            if (b.Path is null || b.Path.Count == 0)
            {
                b.Failures++;
                var step = Pathfinder.GreedyStep(_map, b.Npc.Tx, b.Npc.Ty, b.Dest.Tx, b.Dest.Ty, b.Npc.Tz);
                var dx = Math.Sign(U7Constants.TileDelta(b.Npc.Tx, step.X));
                var dy = Math.Sign(U7Constants.TileDelta(b.Npc.Ty, step.Y));
                ActorWalker.TryStep(_map, b.Npc, dx, dy);
            }

            return;
        }

        var next = b.Path[b.PathI];
        var sdx = Math.Sign(U7Constants.TileDelta(b.Npc.Tx, next.X));
        var sdy = Math.Sign(U7Constants.TileDelta(b.Npc.Ty, next.Y));
        if (!ActorWalker.TryStep(_map, b.Npc, sdx, sdy))
        {
            b.Failures++;
            SetPath(b, b.Dest.Tx, b.Dest.Ty);
            return;
        }

        b.PathI++;
        b.Failures = 0;
        if (b.PathI >= b.Path.Count)
        {
            arrive();
        }
    }

    void SetPath(Brain b, int tx, int ty)
    {
        b.Path = Pathfinder.Find(_map, b.Npc.Tx, b.Npc.Ty, tx, ty, b.Npc.Tz);
        b.PathI = 0;
    }

    void Teleport(Brain b, TileCoord dest)
    {
        _map.MoveObject(b.Npc, dest.Tx, dest.Ty, dest.Tz);
        b.Path = null;
        b.PathI = 0;
        ActorWalker.Stand(b.Npc, 4);
    }

    bool AtDest(Brain b, int dist) =>
        new TileCoord(b.Npc.Tx, b.Npc.Ty, b.Npc.Tz).Distance2d(b.Dest) <= dist;

    int Dist(U7Object npc) =>
        new TileCoord(npc.Tx, npc.Ty, npc.Tz).Distance2d(new TileCoord(_avatar.Tx, _avatar.Ty, _avatar.Tz));

    static int DistFrom(U7Object npc, TileCoord dest) =>
        new TileCoord(npc.Tx, npc.Ty, npc.Tz).Distance2d(dest);

    static int FrameFacing(int frame) => ((frame >> 4) & 3) switch
    {
        0 => 0,
        1 => 4,
        2 => 6,
        _ => 2
    };

    sealed class Brain
    {
        public U7Object Npc = null!;
        public TileCoord Dest;
        public TileCoord Center;
        public List<Vector2I>? Path;
        public int PathI;
        public int Failures;
        public int PaceDir = 1;
        public double StepTimer;
        public double Pause;
        public bool WasNearby;
    }
}
