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
    /// gives up), then do <paramref name="whenThere"/>.
    /// </summary>
    public static IActorAction WalkThen(GameMap map, U7Object actor, TileCoord dest, IActorAction whenThere)
    {
        if (actor.Tx == dest.Tx && actor.Ty == dest.Ty && actor.Tz == dest.Tz)
        {
            return whenThere;
        }

        IActorAction walk = (IActorAction?)PathWalk.Astar(map, actor, dest) ?? new MoveAction(map, dest);
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
