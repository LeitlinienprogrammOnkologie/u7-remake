using U7.Core;
using U7.Data;

namespace U7.Actors;

/// <summary>
/// Exult <c>Actor_action</c>: something an actor does over time.
/// <see cref="HandleEvent"/> returns the milliseconds until it wants to be
/// called again, or 0 when it is done.
/// </summary>
public interface IActorAction
{
    int HandleEvent(U7Object actor);
}

/// <summary>
/// Exult <c>Frames_actor_action</c>: show frames one after another,
/// <c>speed</c> ms apart (-1 leaves the frame as it is), on the actor or on
/// another object.
/// </summary>
public class FramesAction(int[] frames, int speed = 200, U7Object? obj = null) : IActorAction
{
    public int Index { get; private set; }

    public virtual int HandleEvent(U7Object actor)
    {
        if (Index == frames.Length || obj is { Removed: true })
        {
            return 0;
        }

        var frame = frames[Index++];
        if (frame >= 0)
        {
            (obj ?? actor).Frame = frame;
        }

        return speed;
    }
}

/// <summary>Exult <c>Move_actor_action</c>: put the actor at the spot unless it is there already.</summary>
public sealed class MoveAction(GameMap map, TileCoord dest) : IActorAction
{
    bool _done;

    public int HandleEvent(U7Object actor)
    {
        if (_done || (actor.Tx == dest.Tx && actor.Ty == dest.Ty && actor.Tz == dest.Tz))
        {
            return 0;
        }

        map.MoveObject(actor, dest.Tx, dest.Ty, dest.Tz);
        _done = true;
        return 100;
    }
}

/// <summary>
/// Exult <c>Sequence_actor_action</c>: one action after another,
/// <paramref name="speed"/> ms apart (0: the next starts at once).
/// </summary>
public sealed class SequenceAction(int speed, params IActorAction[] actions) : IActorAction
{
    int _index;

    public int HandleEvent(U7Object actor)
    {
        while (_index < actions.Length)
        {
            var delay = actions[_index].HandleEvent(actor);
            if (delay != 0)
            {
                return delay;
            }

            _index++;
            if (speed != 0)
            {
                return speed;
            }
        }

        return 0;
    }

    /// <summary>
    /// Exult <c>Actor_action::create_action_sequence</c>: walk to the spot
    /// (A*, or straight there if no path is found, and put there if the walk
    /// gives up), then do <paramref name="whenThere"/>. A persistent walk
    /// keeps finding its way round NPCs (<see cref="PathWalk.Astar"/>).
    /// </summary>
    public static IActorAction WalkThen(GameMap map, U7Object actor, TileCoord dest, IActorAction whenThere,
        bool persistent = false)
    {
        if (actor.Tx == dest.Tx && actor.Ty == dest.Ty && actor.Tz == dest.Tz)
        {
            return whenThere;
        }

        IActorAction walk = (IActorAction?)PathWalk.Astar(map, actor, dest, persistent: persistent) ??
                            new MoveAction(map, dest);
        return new SequenceAction(0, walk, new MoveAction(map, dest), whenThere);
    }
}

/// <summary>
/// Exult <c>Sit_actor_action</c>: bow, then sit, in front of a chair facing
/// the way the chair faces (frame 0 north, 1 east, ...). Gives up if someone
/// else sits there, and complains if the chair has been moved.
/// </summary>
public sealed class SitAction : FramesAction
{
    // Where to sit, by the way the chair faces.
    static readonly int[] Offsets = [0, -1, 1, 0, 0, 1, -1, 0];

    /// <summary>Exult <c>Actor::say</c>, for the chair thief.</summary>
    public static Action<U7Object, string>? Say { get; set; }

    readonly GameMap _map;
    readonly U7Object _chair;
    readonly TileCoord _chairLoc;

    public TileCoord SitLoc { get; }

    public SitAction(GameMap map, U7Object chair)
        : base(Frames(chair))
    {
        _map = map;
        _chair = chair;
        _chairLoc = new TileCoord(chair.Tx, chair.Ty, chair.Tz);
        SitLoc = SitLocOf(chair);
    }

    static int[] Frames(U7Object chair)
    {
        var dir = 2 * (chair.Frame % 4);
        return [ActorWalker.DirFrame(dir, ActorWalker.BowFrame), ActorWalker.DirFrame(dir, ActorWalker.SitFrame)];
    }

    static TileCoord SitLocOf(U7Object chair)
    {
        var nsew = chair.Frame % 4;
        return new TileCoord(U7Constants.WrapTile(chair.Tx + Offsets[2 * nsew]),
            U7Constants.WrapTile(chair.Ty + Offsets[2 * nsew + 1]), chair.Tz);
    }

    /// <summary>Exult <c>Sit_actor_action::is_occupied</c> for a chair.</summary>
    public static bool IsOccupied(GameMap map, U7Object chair, U7Object actor) =>
        IsOccupied(map, SitLocOf(chair), actor);

    /// <summary>
    /// Someone else sitting or bowing there, or (unless the actor stands
    /// there) no room to stand there.
    /// </summary>
    static bool IsOccupied(GameMap map, TileCoord sitloc, U7Object actor)
    {
        foreach (var other in map.FindNearby(sitloc, U7Constants.AnyShape, 0, 8))
        {
            var frame = other.Frame & 15;
            if (other != actor && frame is ActorWalker.SitFrame or ActorWalker.BowFrame)
            {
                return true;
            }
        }

        if (actor.Tx == sitloc.Tx && actor.Ty == sitloc.Ty && actor.Tz == sitloc.Tz)
        {
            return false;
        }

        return map.Blocking.IsBlocked(map.Catalog[actor.Shape].DimZ, sitloc.Tz, sitloc.Tx, sitloc.Ty, out _,
            MoveFlags.Walk, 0);
    }

    public override int HandleEvent(U7Object actor)
    {
        if (Index == 0)
        {
            if (IsOccupied(_map, SitLoc, actor))
            {
                return 0;
            }

            if (_chair.Removed || _chair.Container is not null || _chair.Tx != _chairLoc.Tx || _chair.Ty != _chairLoc.Ty ||
                _chair.Tz != _chairLoc.Tz)
            {
                Say?.Invoke(actor, TextMessages.Random(TextMessages.FirstChairThief, TextMessages.LastChairThief));
                return 0;
            }
        }

        return base.HandleEvent(actor);
    }
}

/// <summary>
/// Exult <c>Approach_actor_action</c>: an A* walk towards an object that may
/// move, stopping when blocked, once within <c>goalDist</c> of it, or, at a
/// check part way along, if it has moved more than 2 tiles.
/// </summary>
public sealed class ApproachAction : IActorAction
{
    readonly PathWalk _walk;
    readonly U7Object _dest;
    readonly int _goalDist;
    readonly TileCoord _origDestPos;
    int _curStep;
    int _checkStep;

    ApproachAction(PathWalk walk, U7Object dest, int goalDist)
    {
        _walk = walk;
        _dest = dest;
        _goalDist = goalDist;
        _origDestPos = ObjectGeometry.Tile(dest);
        var nsteps = walk.StepsLeft;
        _checkStep = nsteps >= 6 ? (nsteps > 18 ? 9 : nsteps / 2) : 10000;
    }

    /// <summary>Exult <c>Approach_actor_action::create_path</c>: to within <paramref name="dist"/> of it (the A* goal too); null if no path.</summary>
    public static ApproachAction? Create(GameMap map, U7Object actor, U7Object dest, int dist) =>
        PathWalk.Astar(map, actor, ObjectGeometry.Tile(dest), dist, maxBlocked: 0) is { } walk
            ? new ApproachAction(walk, dest, dist)
            : null;

    /// <summary>Exult <c>new Approach_actor_action(path, dest, gdist)</c> on a path already found (it should stop when blocked).</summary>
    public static ApproachAction FromPath(PathWalk walk, U7Object dest, int goalDist) => new(walk, dest, goalDist);

    /// <summary>Called after every step (from x, y).</summary>
    public Action<U7Object, int, int>? Stepped
    {
        get => _walk.Stepped;
        set => _walk.Stepped = value;
    }

    public int HandleEvent(U7Object actor)
    {
        var delay = _walk.HandleEvent(actor);
        if (_dest.Removed || delay == 0)
        {
            return 0; // Done or blocked.
        }

        if (_goalDist >= 0 && ObjectGeometry.Distance(actor, _dest) <= _goalDist)
        {
            return 0; // Close enough.
        }

        if (++_curStep == _checkStep)
        {
            if (ObjectGeometry.Distance(_dest, _origDestPos) > 2)
            {
                return 0; // Moved too much, so stop.
            }

            if (_walk.StepsLeft >= 6)
            {
                _checkStep += 3; // Try checking more often.
            }
        }

        return delay;
    }
}

/// <summary>Exult <c>Object_animate_actor_action</c>: run an object through its frames for some cycles.</summary>
public sealed class ObjectAnimateAction(U7Object obj, int nframes, int cycles, int speed) : IActorAction
{
    int _cycles = cycles;

    public int HandleEvent(U7Object actor)
    {
        if (obj.Removed || _cycles == 0 || nframes <= 0)
        {
            return 0;
        }

        var frnum = (obj.Frame + 1) % nframes;
        if (frnum == 0)
        {
            --_cycles; // A new cycle.
        }

        obj.Frame = frnum;
        return _cycles != 0 ? speed : 0;
    }
}

/// <summary>Exult <c>Change_actor_action</c>: change an object's shape, frame and quality.</summary>
public sealed class ChangeAction(GameMap map, U7Object obj, int shape, int frame, int quality) : IActorAction
{
    public int HandleEvent(U7Object actor)
    {
        if (!obj.Removed)
        {
            map.SetShape(obj, shape);
            obj.Frame = frame;
            obj.Quality = quality;
        }

        return 0;
    }
}

/// <summary>Exult <c>Activate_actor_action</c>: run the object's usecode as a double-click would.</summary>
public sealed class ActivateAction(U7Object obj, Func<U7Object, bool>? activate) : IActorAction
{
    public int HandleEvent(U7Object actor)
    {
        if (!obj.Removed)
        {
            activate?.Invoke(obj);
        }

        return 0;
    }
}

/// <summary>Exult <c>Face_pos_actor_action</c>: turn to face a tile (standing).</summary>
public sealed class FacePosAction(TileCoord pos, int speed) : IActorAction
{
    public FacePosAction(U7Object obj, int speed)
        : this(ObjectGeometry.Tile(obj), speed)
    {
    }

    public int HandleEvent(U7Object actor)
    {
        var frame = ActorWalker.DirFrame(ObjectGeometry.Direction(actor, pos), 0);
        if (actor.Frame == frame)
        {
            return 0;
        }

        actor.Frame = frame;
        return speed;
    }
}

/// <summary>
/// Exult <c>Pickup_actor_action</c>: face the item, bend down (or reach up,
/// if it is two lifts or more above the feet) and take it into the
/// inventory, or put it down at a spot; then stand again.
/// </summary>
public sealed class PickupAction : IActorAction
{
    readonly GameMap _map;
    readonly U7Object _obj;
    readonly bool _pickup;
    readonly TileCoord _objPos;
    readonly bool _temporary;
    readonly bool _delete;
    int _count;
    int _dir;
    readonly int _speed;

    /// <summary>Pick the item up (or, with <paramref name="delete"/>, make it vanish).</summary>
    public PickupAction(GameMap map, U7Object obj, int speed, bool delete = false)
    {
        _map = map;
        _obj = obj;
        _pickup = true;
        _objPos = ObjectGeometry.Tile(obj);
        _speed = speed;
        _delete = delete;
    }

    /// <summary>Put the item down at <paramref name="pos"/>, flagged temporary if asked.</summary>
    public PickupAction(GameMap map, U7Object obj, TileCoord pos, int speed, bool temporary)
    {
        _map = map;
        _obj = obj;
        _objPos = pos;
        _speed = speed;
        _temporary = temporary;
    }

    public int HandleEvent(U7Object actor)
    {
        if (_obj.Removed)
        {
            return 0; // It's gone.
        }

        int frame;
        switch (_count)
        {
            case 0:
                _dir = ObjectGeometry.Direction(actor, _objPos);
                frame = ActorWalker.DirFrame(_dir, 0);
                break;
            case 1:
            {
                var tz = _pickup ? _obj.Tz : _objPos.Tz;
                frame = ActorWalker.DirFrame(_dir, tz >= actor.Tz + 2
                    ? Random.Shared.Next(2) == 0 ? ActorWalker.Reach1Frame : ActorWalker.Reach2Frame
                    : ActorWalker.BowFrame);
                if (!_pickup)
                {
                    _map.PlaceInWorld(_obj, _objPos.Tx, _objPos.Ty, _objPos.Tz);
                    if (_temporary)
                    {
                        _obj.SetFlag(ObjFlag.Temporary);
                    }
                }
                else if (ObjectGeometry.Distance(actor, _obj) <= 8)
                {
                    if (_delete)
                    {
                        _map.RemoveObject(_obj);
                    }
                    else
                    {
                        _map.PlaceInContainer(_obj, actor, 255, 255);
                    }
                }

                break;
            }
            case 2:
                frame = ActorWalker.DirFrame(_dir, 0);
                break;
            default:
                return 0;
        }

        _count++;
        actor.Frame = frame;
        return _speed;
    }
}
