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

    /// <summary>Party ids for follower spacing (Exult <c>Actor::follow</c>).</summary>
    public PartyManager? Party { get; set; }
    /// <summary>True while the avatar is walking; formation stepping drives followers then.</summary>
    public Func<bool>? AvatarMoving { get; set; }
    /// <summary>Exult <c>Actor::in_usecode_control</c>: the schedule waits while a script runs the NPC.</summary>
    public Func<U7Object, bool>? InUsecodeControl { get; set; }

    /// <param name="restore">
    /// A saved game: NPCs keep the schedules and places they were saved with
    /// (Exult <c>restore_schedule</c>); a new game sets everyone to the
    /// schedule of the hour (Exult <c>schedule_npcs</c>).
    /// </param>
    public ScheduleRunner(GameMap map, U7Object avatar, List<U7Object?> npcs,
        ScheduleTable table, GameClock clock, bool restore = false)
    {
        _map = map;
        _avatar = avatar;
        _npcs = npcs;
        _table = table;
        _clock = clock;
        for (var i = 1; i < npcs.Count; i++)
        {
            if (npcs[i] is not { Unused: false } npc || npc.IsDead)
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
        if (restore)
        {
            RestoreSchedules();
        }
        else
        {
            ApplySlot(pathIfNearby: false);
        }
    }

    /// <summary>
    /// Exult <c>Actor::restore_schedule</c> after a load: an NPC that was
    /// walking to its next schedule's spot sets off again; everyone else stays
    /// where they were with the schedule they had (a loiterer loiters around
    /// where it stands). Party members are left alone. The schedule table
    /// takes over again at the next change of period.
    /// </summary>
    void RestoreSchedules()
    {
        foreach (var b in _brains.Values)
        {
            var npc = b.Npc;
            if (Party?.IsInParty(npc) == true || npc.ScheduleType is ScheduleType.FollowAvatar or ScheduleType.Wait)
            {
                b.WasNearby = true;
                continue;
            }

            var dest = new TileCoord(npc.ScheduleDestTx, npc.ScheduleDestTy, npc.ScheduleDestTz);
            if (dest.Tx == 0 && dest.Ty == 0)
            {
                // Never given a spot (no schedule table entries): where it is.
                dest = new TileCoord(npc.Tx, npc.Ty, npc.Tz);
            }

            var nearby = Dist(npc) <= ActivityDist;
            b.Dest = dest;
            if (npc.ScheduleType == ScheduleType.WalkToSchedule && npc.PendingSchedule >= 0)
            {
                // Exult set_schedule_and_loc: walk there, or be put there when far off.
                if (nearby)
                {
                    BeginWalkTo(b, npc.PendingSchedule, dest);
                }
                else
                {
                    Teleport(b, dest);
                    BeginType(b, npc.PendingSchedule, dest, alreadyThere: true);
                }
            }
            else
            {
                BeginType(b, npc.ScheduleType, dest, alreadyThere: true);
                b.Center = new TileCoord(npc.Tx, npc.Ty, npc.Tz);
            }

            b.WasNearby = nearby;
        }
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

    /// <summary>Give a resurrected NPC a schedule brain again.</summary>
    public void Revive(U7Object npc)
    {
        if (npc.NpcNum <= 0 || _brains.ContainsKey(npc.NpcNum))
        {
            return;
        }

        _brains[npc.NpcNum] = new Brain
        {
            Npc = npc,
            Dest = new TileCoord(npc.Tx, npc.Ty, npc.Tz),
            Center = new TileCoord(npc.Tx, npc.Ty, npc.Tz),
            WasNearby = true
        };
    }

    /// <summary>Move an NPC in place (teleport), dropping any path.</summary>
    public void TeleportNpc(U7Object npc, TileCoord dest)
    {
        if (npc.NpcNum > 0 && _brains.TryGetValue(npc.NpcNum, out var b))
        {
            Teleport(b, dest);
            return;
        }

        _map.MoveObject(npc, dest.Tx, dest.Ty, dest.Tz);
        ActorWalker.Stand(npc, 4);
    }

    void Tick(Brain b, double delta)
    {
        if (b.Npc.IsDead || b.Npc.Removed)
        {
            return;
        }

        // Followers never fall back to a stale slot destination.
        var nearby = b.Npc.ScheduleType == ScheduleType.FollowAvatar || Dist(b.Npc) <= ActivityDist;
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

        if (InUsecodeControl?.Invoke(b.Npc) ?? false)
        {
            // Exult Actor::handle_event: keep trying every standard delay.
            b.StepTimer = U7Constants.StandardDelayMs / 1000.0;
            return;
        }

        if (b.Walk is not null && b.Npc.ScheduleType == ScheduleType.FollowAvatar && (AvatarMoving?.Invoke() ?? false))
        {
            // The avatar walks again: formation stepping takes over.
            StopWalk(b);
        }

        if (b.Walk is { } walk)
        {
            // Exult Npc_actor::handle_event: the action's delay, then now_what once it is done.
            var d = walk.HandleEvent(b.Npc);
            if (d != 0)
            {
                b.StepTimer = d / 1000.0;
                return;
            }

            b.Walk = null;
            b.Npc.FrameTime = 0;
            b.StepTimer = _stepInterval;
            var done = b.WalkDone;
            b.WalkDone = null;
            done?.Invoke(walk);
            return;
        }

        b.StepTimer = _stepInterval;
        NowWhat(b);
    }

    /// <summary>
    /// Exult <c>Actor::start(speed, delay)</c> with a walk as the action: it
    /// steps every <paramref name="speedMs"/> once <paramref name="delayMs"/>
    /// has passed; <paramref name="done"/> runs when it ends.
    /// </summary>
    void StartWalk(Brain b, PathWalk? walk, int speedMs, int delayMs, Action<PathWalk>? done = null)
    {
        b.Walk = walk;
        b.WalkDone = done;
        b.Npc.FrameTime = walk is null ? 0 : speedMs;
        b.StepTimer = delayMs / 1000.0;
    }

    void NowWhat(Brain b)
    {
        var type = b.Npc.ScheduleType;
        switch (type)
        {
            case ScheduleType.WalkToSchedule:
                WalkToSchedule(b);
                break;
            case ScheduleType.Loiter:
            {
                // Exult Loiter_schedule::now_what: amble somewhere within 12 tiles, slowly.
                var tx = b.Center.Tx - LoiterDist + _rng.Next(2 * LoiterDist);
                var ty = b.Center.Ty - LoiterDist + _rng.Next(2 * LoiterDist);
                StartWalk(b, PathWalk.Line(_map, b.Npc, new TileCoord(tx, ty, b.Center.Tz).Wrapped()),
                    2 * U7Constants.StandardDelayMs, _rng.Next(2000));
                break;
            }
            case ScheduleType.Wander:
                Wander(b);
                break;
            case ScheduleType.Patrol:
            {
                // (Exult patrols between path eggs; not ported: wander near the spot.)
                var tx = b.Center.Tx - 4 + _rng.Next(9);
                var ty = b.Center.Ty - 4 + _rng.Next(9);
                StartWalk(b, PathWalk.Astar(_map, b.Npc, new TileCoord(tx, ty, b.Center.Tz).Wrapped()),
                    U7Constants.StandardDelayMs, _rng.Next(2000));
                break;
            }
            case ScheduleType.HorizPace:
                Pace(b, horiz: true);
                break;
            case ScheduleType.VertPace:
                Pace(b, horiz: false);
                break;
            case ScheduleType.Sleep:
                if (AtDest(b, 3) || b.Failures >= 2)
                {
                    if (!AtDest(b, 3))
                    {
                        Teleport(b, b.Dest);
                    }

                    LieInBed(b);
                    b.Pause = 2;
                }
                else
                {
                    // Exult Sleep_schedule walks to the bed at 200 ms a step.
                    var walk = PathWalk.Astar(_map, b.Npc, b.Dest, dist: 1);
                    b.Failures = walk is null ? b.Failures + 1 : 0;
                    StartWalk(b, walk, 200, 0);
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
                FollowAvatar(b);
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

    /// <summary>
    /// Exult <c>Follow_avatar_schedule::now_what</c> + <c>Actor::follow</c>.
    /// While the avatar walks, <see cref="PartyManager"/> steps followers in
    /// formation and this does nothing. Once the avatar stops, a member more
    /// than a few tiles away paths to a spot beside the avatar; one far off
    /// screen is brought over (Exult <c>approach_another</c>).
    /// </summary>
    void FollowAvatar(Brain b)
    {
        var npc = b.Npc;
        if (npc.IsDead || npc.GetFlag(ObjFlag.Asleep) || npc.GetFlag(ObjFlag.Paralyzed) ||
            ObjFlag.DontMoveMode(_avatar))
        {
            return;
        }

        if (AvatarMoving?.Invoke() ?? false)
        {
            return;
        }

        var dist = Dist(npc);
        if (dist <= 6)
        {
            if (npc.WalkFrameIndex != 0)
            {
                ActorWalker.Stand(npc, FrameFacing(npc.Frame));
            }

            return;
        }

        if (dist > 40)
        {
            var spot = _map.FindSpot(_avatar.Tx, _avatar.Ty, _avatar.Tz, 8);
            if (spot is { } t)
            {
                Teleport(b, t);
            }

            return;
        }

        var id = Math.Max(0, Party?.PartyId(npc) ?? 0);
        var goal = new TileCoord(
            U7Constants.WrapTile(_avatar.Tx + PartyManager.XOffs[id % PartyManager.XOffs.Length] + 1 - _rng.Next(3)),
            U7Constants.WrapTile(_avatar.Ty + PartyManager.YOffs[id % PartyManager.YOffs.Length] + 1 - _rng.Next(3)),
            _avatar.Tz);
        b.Dest = goal;
        // Exult Actor::follow: with the leader standing, catch up at 100 ms a step.
        StartWalk(b, PathWalk.Astar(_map, npc, goal, dist: 1) ?? PathWalk.Line(_map, npc, goal), FollowSpeed, 0,
            _ => ActorWalker.Stand(npc, FrameFacing(npc.Frame)));
    }

    /// <summary>Exult <c>Actor::follow</c>: a stopped leader is caught up with at 100 ms a step.</summary>
    const int FollowSpeed = 100;
    /// <summary>Exult <c>Loiter_schedule</c>'s default distance.</summary>
    const int LoiterDist = 12;
    /// <summary>Exult <c>Wander_schedule</c>: how far from the centre, and how far one leg goes.</summary>
    const int WanderDist = 128;
    const int WanderLeg = 32;

    void BeginWalkTo(Brain b, int pending, TileCoord dest)
    {
        b.Dest = dest;
        b.Npc.PendingSchedule = pending;
        b.Npc.ScheduleType = ScheduleType.WalkToSchedule;
        b.Failures = 0;
        b.Legs = 0;
        // Exult Walk_to_schedule: a schedule change sets off after up to 5 s.
        b.FirstDelay = _rng.Next(5000);
        if (AtDest(b, 3))
        {
            BeginType(b, pending, dest, alreadyThere: true);
        }
    }

    /// <summary>
    /// Exult <c>Walk_to_schedule::now_what</c>: within 3 tiles the new
    /// schedule starts; after 40 legs or 2 failed path searches in a row the
    /// NPC is put there; otherwise another A* leg at 200 ms a step, or a
    /// straight walk when no path is found. (Exult's off-screen legs are not
    /// ported: NPCs far from the avatar are placed at their spot.)
    /// </summary>
    void WalkToSchedule(Brain b)
    {
        var next = b.Npc.PendingSchedule >= 0 ? b.Npc.PendingSchedule : ScheduleType.Stand;
        if (AtDest(b, 3))
        {
            BeginType(b, next, b.Dest, alreadyThere: true);
            return;
        }

        if (b.Legs >= 40 || b.Failures >= 2)
        {
            Teleport(b, b.Dest);
            BeginType(b, next, b.Dest, alreadyThere: true);
            return;
        }

        if (PathWalk.Astar(_map, b.Npc, b.Dest) is { } walk)
        {
            b.Legs++;
            b.Failures = 0;
            StartWalk(b, walk, U7Constants.StandardDelayMs, b.FirstDelay + _rng.Next(1000));
        }
        else
        {
            b.Failures++;
            StartWalk(b, PathWalk.Line(_map, b.Npc, b.Dest), U7Constants.StandardDelayMs, 1000);
        }

        b.FirstDelay = 0;
    }

    /// <summary>
    /// Exult <c>Wander_schedule::now_what</c>: an A* walk to a free spot up to
    /// 32 tiles off, staying within 128 of the centre; on failure, try again
    /// within 3 s.
    /// </summary>
    void Wander(Brain b)
    {
        var npc = b.Npc;
        var tx = npc.Tx - WanderLeg + _rng.Next(2 * WanderLeg);
        var ty = npc.Ty - WanderLeg + _rng.Next(2 * WanderLeg);
        tx = b.Center.Tx + Math.Clamp(U7Constants.TileDelta(b.Center.Tx, tx), -WanderDist, WanderDist);
        ty = b.Center.Ty + Math.Clamp(U7Constants.TileDelta(b.Center.Ty, ty), -WanderDist, WanderDist);
        var walk = _map.FindSpot(tx, ty, npc.Tz, 4) is { } spot ? PathWalk.Astar(_map, npc, spot) : null;
        if (walk is null)
        {
            b.StepTimer = _rng.Next(3000) / 1000.0;
            return;
        }

        StartWalk(b, walk, U7Constants.StandardDelayMs, _rng.Next(2000));
    }

    void BeginType(Brain b, int type, TileCoord dest, bool alreadyThere)
    {
        if (b.Bed is not null && type is not (ScheduleType.Sleep or ScheduleType.Wait))
        {
            EndSleep(b, type);
        }

        b.Dest = dest;
        b.Center = dest;
        b.Npc.PendingSchedule = -1;
        b.Npc.ScheduleType = type;
        b.Npc.ScheduleDestTx = dest.Tx;
        b.Npc.ScheduleDestTy = dest.Ty;
        b.Npc.ScheduleDestTz = dest.Tz;
        StopWalk(b);
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
                LieInBed(b);
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

    /// <summary>Drop the NPC's walk (Exult <c>set_action(nullptr)</c>): not moving any more.</summary>
    static void StopWalk(Brain b)
    {
        b.Walk = null;
        b.WalkDone = null;
        b.Npc.FrameTime = 0;
    }

    // Exult Sleep_schedule: BG bedspread frames 3..16; even = spread out.
    const int Spread0 = 3;
    const int Spread1 = 16;
    static readonly int[] BedShapes = [696, 1011];

    /// <summary>
    /// Exult <c>Sleep_schedule::now_what</c> state 1: pick the nearest free bed,
    /// unmake it, and put the NPC on top of it (bed lift + bed height) in the
    /// sleep frame facing west for EW beds, north for NS beds. With no bed
    /// nearby the NPC just lies down where it is.
    /// </summary>
    void LieInBed(Brain b)
    {
        var npc = b.Npc;
        if (b.Bed is { Removed: false } && (npc.Frame & 0xf) == 13)
        {
            return; // already in bed
        }

        var here = new TileCoord(npc.Tx, npc.Ty, npc.Tz);
        U7Object? bed = null;
        var best = int.MaxValue;
        foreach (var shape in BedShapes)
        {
            foreach (var cand in _map.FindNearby(here, shape, 24))
            {
                var d = here.Distance2d(new TileCoord(cand.Tx, cand.Ty, cand.Tz));
                if (d < best && !IsBedOccupied(cand, npc))
                {
                    best = d;
                    bed = cand;
                }
            }
        }

        if (bed is null)
        {
            ActorWalker.Sleep(npc, _map.Catalog);
            return;
        }

        // Prefer the sheet object on the same floor if the bed is a stack.
        var floor = bed.Tz / 5;
        foreach (var top in _map.FindNearby(new TileCoord(bed.Tx, bed.Ty, bed.Tz), bed.Shape, 1))
        {
            if (top.Frame >= Spread0 && top.Frame <= Spread1 && top.Tz / 5 == floor)
            {
                bed = top;
                break;
            }
        }

        b.Bed = bed;
        b.FloorLoc = new TileCoord(npc.Tx, npc.Ty, npc.Tz - npc.Tz % 5);
        var bedframe = bed.Frame;
        if (bedframe >= Spread0 && bedframe < Spread1 && bedframe % 2 == 1)
        {
            bed.Frame = ++bedframe; // unmake the bed
        }

        var bedspread = bedframe >= Spread0 && bedframe % 2 == 0;
        var height = _map.Catalog[npc.Shape].DimZ;
        var delta = height < 4 ? height - 4 : 0;
        var bedHeight = _map.Catalog[bed.Shape].DimZ;
        _map.MoveObject(npc, bed.Tx + delta, bed.Ty + delta, bed.Tz + (bedspread ? 0 : bedHeight));
        var band = bed.Shape == 696 ? 32 : 0; // west for EW beds, north for NS
        var count = _map.Catalog[npc.Shape].FrameCount;
        npc.Frame = 13 < count ? 13 + band : band;
        npc.WalkFrameIndex = 0;
    }

    /// <summary>Exult <c>Sleep_schedule::ending</c>: make the bed and step back onto the floor.</summary>
    void EndSleep(Brain b, int newType)
    {
        var npc = b.Npc;
        var bed = b.Bed!;
        b.Bed = null;
        if (bed.Removed || (npc.Frame & 0xf) != 13 ||
            new TileCoord(npc.Tx, npc.Ty, npc.Tz).Distance2d(new TileCoord(bed.Tx, bed.Ty, bed.Tz)) >= 8)
        {
            return;
        }

        if (newType != ScheduleType.Combat && bed.Frame >= Spread0 && bed.Frame <= Spread1 &&
            bed.Frame % 2 == 0 && !IsBedOccupied(bed, npc))
        {
            bed.Frame--; // make the bed
        }

        var spot = _map.FindSpot(b.FloorLoc.Tx, b.FloorLoc.Ty, b.FloorLoc.Tz, 6);
        var pos = spot ?? b.FloorLoc;
        _map.MoveObject(npc, pos.Tx, pos.Ty, pos.Tz);
        ActorWalker.Stand(npc, 4);
    }

    /// <summary>Exult <c>Sleep_schedule::is_bed_occupied</c>.</summary>
    bool IsBedOccupied(U7Object bed, U7Object npc)
    {
        var floor = bed.Tz / 5;
        foreach (var other in _map.FindNearby(new TileCoord(bed.Tx, bed.Ty, bed.Tz), U7Constants.AnyShape, 2, 8))
        {
            if (other == npc || !other.IsActor)
            {
                continue;
            }

            if (bed.Occupies(other.Tx, other.Ty) && other.Tz / 5 == floor)
            {
                return true;
            }
        }

        return false;
    }

    void Teleport(Brain b, TileCoord dest)
    {
        _map.MoveObject(b.Npc, dest.Tx, dest.Ty, dest.Tz);
        StopWalk(b);
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
        /// <summary>Exult <c>Actor::action</c>: the walk under way.</summary>
        public PathWalk? Walk;
        public Action<PathWalk>? WalkDone;
        public int Failures;
        /// <summary>Exult <c>Walk_to_schedule</c>'s legs walked and its delay before the first.</summary>
        public int Legs;
        public int FirstDelay;
        public int PaceDir = 1;
        public double StepTimer;
        public double Pause;
        public bool WasNearby;
        public U7Object? Bed;
        public TileCoord FloorLoc;
    }
}
