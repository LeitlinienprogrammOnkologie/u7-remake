using U7.Core;
using U7.Data;
using U7.World;

namespace U7.Actors;

/// <summary>Exult <c>PathFinder</c>: where a walk goes, one tile at a time.</summary>
public abstract class PathSteps
{
    public TileCoord Src { get; protected init; }
    public TileCoord Dest { get; protected init; }

    /// <summary>The next tile, or false once there are none; <paramref name="done"/> on the last one.</summary>
    public abstract bool NextStep(out TileCoord tile, out bool done);

    /// <summary>Exult <c>get_num_steps</c>: steps left to take.</summary>
    public abstract int StepsLeft { get; }
}

/// <summary>Exult <c>Astar</c>: a path found by <see cref="Pathfinder.FindPath"/>.</summary>
public sealed class AstarSteps : PathSteps
{
    readonly List<TileCoord> _path;
    int _next;

    AstarSteps(TileCoord src, TileCoord dest, List<TileCoord> path)
    {
        Src = src;
        Dest = dest;
        _path = path;
    }

    public int Count => _path.Count;

    public override int StepsLeft => _path.Count - _next;

    /// <summary>Exult <c>Astar::NewPath</c>: null when there is no path.</summary>
    public static AstarSteps? Find(PathClient client, TileCoord src, TileCoord dest) =>
        Pathfinder.FindPath(client, src, dest) is { } path ? new AstarSteps(src, dest, path) : null;

    /// <summary>
    /// Exult <c>Astar::NewPath</c> then <c>set_backwards</c>: found from
    /// <paramref name="dest"/> to wherever the client's goal is, walked the
    /// other way, from its far end to the tile beside <paramref name="dest"/>.
    /// </summary>
    public static AstarSteps? FindBackwards(PathClient client, TileCoord dest, TileCoord goal)
    {
        if (Pathfinder.FindPath(client, dest, goal) is not { Count: > 0 } path)
        {
            return null;
        }

        path.Reverse();
        return new AstarSteps(path[0], dest, path);
    }

    public override bool NextStep(out TileCoord tile, out bool done)
    {
        if (_next >= _path.Count)
        {
            tile = default;
            done = true;
            return false;
        }

        tile = _path[_next++];
        done = _next >= _path.Count;
        return true;
    }
}

/// <summary>Exult <c>Zombie</c>: a straight line to the destination, lift included, blocked or not.</summary>
public sealed class ZombieSteps : PathSteps
{
    readonly int[] _cur = new int[3];
    readonly int[] _dir = new int[3];
    readonly int _major;
    readonly int _minor1;
    readonly int _minor2;
    readonly int _majorDelta;
    readonly int _minorDelta1;
    readonly int _minorDelta2;
    int _majorDistance;
    int _sum1;
    int _sum2;

    ZombieSteps(TileCoord src, TileCoord dest, int[] deltas)
    {
        Src = src;
        Dest = dest;
        _cur[0] = src.Tx;
        _cur[1] = src.Ty;
        _cur[2] = src.Tz;
        var abs = new int[3];
        for (var i = 0; i < 3; i++)
        {
            abs[i] = Math.Abs(deltas[i]);
            _dir[i] = Math.Sign(deltas[i]);
        }

        // Fastest along z, else y, else x (Exult's order of tests).
        (_major, _minor1, _minor2) = abs[2] >= abs[0] && abs[2] >= abs[1] ? (2, 0, 1)
            : abs[1] >= abs[0] && abs[1] >= abs[2] ? (1, 0, 2)
            : (0, 1, 2);
        _majorDelta = abs[_major];
        _minorDelta1 = abs[_minor1];
        _minorDelta2 = abs[_minor2];
        _majorDistance = _majorDelta;
    }

    public override int StepsLeft => Math.Max(0, _majorDistance);

    /// <summary>Exult <c>Zombie::NewPath</c>: null when already there.</summary>
    public static ZombieSteps? Line(TileCoord src, TileCoord dest)
    {
        int[] deltas = [U7Constants.TileDelta(src.Tx, dest.Tx), U7Constants.TileDelta(src.Ty, dest.Ty), dest.Tz - src.Tz];
        return deltas.All(d => d == 0) ? null : new ZombieSteps(src, dest, deltas);
    }

    public override bool NextStep(out TileCoord tile, out bool done)
    {
        tile = default;
        if (_majorDistance <= 0)
        {
            done = true;
            return false;
        }

        _majorDistance--;
        _sum1 += _minorDelta1;
        _sum2 += _minorDelta2;
        var incr1 = _sum1 / _majorDelta;
        var incr2 = _sum2 / _majorDelta;
        _sum1 %= _majorDelta;
        _sum2 %= _majorDelta;
        _cur[_major] += _dir[_major];
        _cur[_minor1] += _dir[_minor1] * incr1;
        _cur[_minor2] += _dir[_minor2] * incr2;
        _cur[0] = U7Constants.WrapTile(_cur[0]);
        _cur[1] = U7Constants.WrapTile(_cur[1]);
        if (_cur[2] < 0)
        {
            _cur[2] = 0;
            _majorDistance = 0;
            done = true;
            return false;
        }

        tile = new TileCoord(_cur[0], _cur[1], _cur[2]);
        done = _majorDistance <= 0;
        return true;
    }
}

/// <summary>
/// Exult <c>Path_walking_actor_action</c>: walks an actor along a path, one
/// step per call at the actor's <see cref="U7Object.FrameTime"/>. A blocked
/// step is retried after 0.1-0.6 s, up to <c>maxBlocked</c> more times; a
/// closed, unlocked door in the way is opened, walked through and closed.
/// The caller calls <see cref="HandleEvent"/> again after the delay it
/// returns, until it returns 0.
/// </summary>
public sealed class PathWalk : IActorAction
{
    /// <summary>Runs a door's usecode as a double-click would; false if usecode cannot run now.</summary>
    public static Func<U7Object, bool>? ActivateDoor { get; set; }
    /// <summary>Exult <c>Actor::is_sentient</c>: monster intelligence 6 or more (opens doors).</summary>
    public static Func<U7Object, bool>? IsSentient { get; set; }

    readonly GameMap _map;
    PathSteps _path;
    readonly int _maxBlocked;
    /// <summary>Exult <c>persistence</c>: how many more times to find a new way round an NPC in the way.</summary>
    int _persistence;
    int _originalDir;
    int _speed;
    int _blocked;
    TileCoord _blockedTile;
    IActorAction? _subseq;
    U7Object? _door;
    bool _handlingDoor;
    bool _doorSequenceComplete;
    /// <summary>Exult <c>from_offscreen</c>: the walker is put on the first tile instead of stepping there.</summary>
    bool _fromOffscreen;

    /// <summary>Not yet put on the first tile of a walk that comes on from off the screen.</summary>
    public bool FromOffscreen => _fromOffscreen;

    /// <summary>Exult <c>reached_end</c>: the last step was taken.</summary>
    public bool ReachedEnd { get; private set; }
    public TileCoord Dest => _path.Dest;
    public int StepsLeft => _path.StepsLeft;
    /// <summary>Called after every step the actor takes (from x, y).</summary>
    public Action<U7Object, int, int>? Stepped { get; set; }

    PathWalk(GameMap map, PathSteps path, int maxBlocked, int persistence = 0)
    {
        _map = map;
        _path = path;
        _maxBlocked = maxBlocked;
        _persistence = persistence;
        _originalDir = OriginalDir(path);
    }

    static int OriginalDir(PathSteps path) =>
        ActorWalker.Direction4(-U7Constants.TileDelta(path.Src.Ty, path.Dest.Ty),
            U7Constants.TileDelta(path.Src.Tx, path.Dest.Tx));

    /// <summary>Exult <c>Actor::walk_to_tile</c>: a straight walk (Zombie); null if already there.</summary>
    public static PathWalk? Line(GameMap map, U7Object actor, TileCoord dest, int maxBlocked = 3) =>
        ZombieSteps.Line(Here(actor), dest) is { } line ? new PathWalk(map, line, maxBlocked) : null;

    /// <summary>
    /// Exult <c>Actor::walk_path_to_tile</c>: an A* walk to within
    /// <paramref name="dist"/> of the destination (at its lift unless that is
    /// -1); null if there is no path. A persistent walk (Exult's for the
    /// avatar and party) plans through NPCs on their feet and, blocked by
    /// one, finds a new way up to 30 times.
    /// </summary>
    public static PathWalk? Astar(GameMap map, U7Object actor, TileCoord dest, int dist = 0, int maxBlocked = 3,
        bool persistent = false) =>
        AstarSteps.Find(new ActorPathClient(map, actor, dist, persistent), Here(actor), dest) is { } path
            ? new PathWalk(map, path, maxBlocked, persistent ? 30 : 0)
            : null;

    /// <summary>
    /// Exult <c>walk_path_to_tile</c> with <c>Path_walking_actor_action::walk_to_tile</c>'s
    /// don't-care coordinates: a -1 in <paramref name="dest"/> walks to any
    /// tile on that line (<see cref="OnecoordPathClient"/>); a source of
    /// -1, -1 comes from off the screen: the path is found backwards from the
    /// destination to the nearest tile off <paramref name="window"/>
    /// (<see cref="OffscreenPathClient"/>, aiming from where the walker is),
    /// and the walker is put on its first tile. Null if there is no path.
    /// </summary>
    public static PathWalk? ToTile(GameMap map, U7Object actor, TileCoord src, TileCoord dest, Godot.Rect2I window,
        int maxBlocked = 3)
    {
        if (dest.Tx == -1 || dest.Ty == -1)
        {
            PathClient client = dest.Tx == dest.Ty
                ? new OffscreenPathClient(map, actor, window)
                : new OnecoordPathClient(map, actor);
            return AstarSteps.Find(client, src, dest) is { } path ? new PathWalk(map, path, maxBlocked) : null;
        }

        if (src.Tx == -1 || src.Ty == -1)
        {
            PathClient client = src.Tx == src.Ty
                ? new OffscreenPathClient(map, actor, window, Here(actor))
                : new OnecoordPathClient(map, actor);
            return AstarSteps.FindBackwards(client, dest, src) is { } back
                ? new PathWalk(map, back, maxBlocked) { _fromOffscreen = true }
                : null;
        }

        return AstarSteps.Find(new ActorPathClient(map, actor), src, dest) is { } direct
            ? new PathWalk(map, direct, maxBlocked)
            : null;
    }

    /// <summary>
    /// Exult <c>Path_walking_actor_action::create_path</c> with an
    /// <c>Approach_object_pathfinder_client</c>: an A* walk to within
    /// <paramref name="dist"/> of the object's footprint; null if there is no path.
    /// </summary>
    public static PathWalk? Approach(GameMap map, U7Object actor, U7Object target, int dist) =>
        CreatePath(map, actor, Here(target), new ApproachPathClient(map, actor, target, dist));

    /// <summary>Exult <c>Path_walking_actor_action::create_path</c>: an A* walk with the given client; null if no path.</summary>
    public static PathWalk? CreatePath(GameMap map, U7Object actor, TileCoord dest, PathClient client,
        int maxBlocked = 3) =>
        CreatePath(map, Here(actor), dest, client, maxBlocked);

    /// <summary>The same from another start, for a walk to follow a first one.</summary>
    public static PathWalk? CreatePath(GameMap map, TileCoord src, TileCoord dest, PathClient client,
        int maxBlocked = 3) =>
        AstarSteps.Find(client, src, dest) is { } path ? new PathWalk(map, path, maxBlocked) : null;

    /// <summary>
    /// Exult <c>walk_to_tile(actor, here, dest, 0, true)</c> on the walk under
    /// way: a new path there, NPCs on their feet not counting.
    /// </summary>
    bool Repath(U7Object actor)
    {
        if (AstarSteps.Find(new ActorPathClient(_map, actor, 0, ignoreNpcs: true), Here(actor), _path.Dest) is not
            { } path)
        {
            return false;
        }

        _path = path;
        _originalDir = OriginalDir(path);
        return true;
    }

    static TileCoord Here(U7Object actor) => new(actor.Tx, actor.Ty, actor.Tz);

    /// <summary>Exult <c>Path_walking_actor_action::handle_event</c>: the delay until the next call, or 0 when done.</summary>
    public int HandleEvent(U7Object actor)
    {
        if (_subseq is not null)
        {
            // Going through a door.
            var delay = _subseq.HandleEvent(actor);
            if (delay != 0)
            {
                return delay;
            }

            _subseq = null;
            actor.FrameTime = _speed;
            if (!_doorSequenceComplete)
            {
                // Close the door behind us.
                if (_door is { Removed: false } door)
                {
                    ActivateWithoutQuality(door);
                }

                _doorSequenceComplete = true;
                _handlingDoor = false;
            }

            return _speed;
        }

        if (_blocked > 0)
        {
            if (Step(actor, _blockedTile))
            {
                _blocked = 0;
                actor.FrameTime = _speed; // He was stopped, so restore speed.
                return _speed;
            }

            if (_blocked++ <= _maxBlocked)
            {
                return 100 + Random.Shared.Next(500); // Wait up to 1.6 secs.
            }

            // Persistent pathfinder?
            if (_persistence == 0)
            {
                return 0;
            }

            _persistence--; // "Tire" a bit from retrying.
            // Blocked by an NPC? Try a new path: the old one may run into an
            // NPC that was not in the way before.
            if (_map.FindBlocking(_blockedTile) is { IsActor: true } && Repath(actor))
            {
                _blocked = 0;
                return _speed;
            }

            return 0;
        }

        var newspeed = actor.FrameTime;
        if (newspeed == 0)
        {
            // Stopped from outside: maybe bumped into an NPC, then pushed
            // aside by another. A persistent walk finds a new way.
            if (_persistence == 0 || !Repath(actor))
            {
                _speed = 0;
                return 0;
            }

            actor.FrameTime = _speed;
            return _speed;
        }

        _speed = newspeed;
        if (!_path.NextStep(out var tile, out var done) ||
            (tile == Here(actor) && !_path.NextStep(out tile, out done)))
        {
            ReachedEnd = true;
            return 0;
        }

        if (done)
        {
            ReachedEnd = true;
        }

        var curSpeed = _speed;
        if (_fromOffscreen)
        {
            // Exult: teleport to the first spot.
            _fromOffscreen = false;
            _map.MoveObject(actor, tile.Tx, tile.Ty, tile.Tz);
            return curSpeed;
        }

        if (Step(actor, tile))
        {
            return done ? 0 : curSpeed;
        }

        ReachedEnd = false;
        if (Here(actor).Distance(tile) <= 2 && (IsSentient?.Invoke(actor) ?? true) &&
            _map.FindDoor(tile) is { } blocking && _map.IsClosedDoor(blocking) && blocking.Frame % 4 < 2 &&
            OpenDoor(actor, blocking))
        {
            return _speed;
        }

        if (_maxBlocked == 0)
        {
            return 0;
        }

        _blocked = 1;
        _blockedTile = tile;
        return 100 + Random.Shared.Next(500);
    }

    /// <summary>
    /// Exult <c>Actor::step</c>: move onto the tile, or, if blocked, stop
    /// (standing, facing the way the walk started) and report failure.
    /// </summary>
    bool Step(U7Object actor, TileCoord tile)
    {
        var fromTx = actor.Tx;
        var fromTy = actor.Ty;
        var dx = Math.Sign(U7Constants.TileDelta(actor.Tx, tile.Tx));
        var dy = Math.Sign(U7Constants.TileDelta(actor.Ty, tile.Ty));
        if (!ActorWalker.CanStep(_map, actor, ref tile))
        {
            Stop(actor);
            return false;
        }

        ActorWalker.MoveTo(_map, actor, tile.Tx, tile.Ty, tile.Tz, ActorWalker.DirIndex(dx, dy));
        Stepped?.Invoke(actor, fromTx, fromTy);
        return true;
    }

    /// <summary>Exult <c>Actor::stop</c> with <c>Path_walking_actor_action::stop</c>: not moving, resting frame.</summary>
    public void Stop(U7Object actor)
    {
        actor.FrameTime = 0;
        ActorWalker.Stand(actor, _originalDir);
    }

    /// <summary>Exult opens and closes doors with quality 0 "to avoid unwanted usecode".</summary>
    static void ActivateWithoutQuality(U7Object door)
    {
        var saved = door.Quality;
        door.Quality = 0;
        ActivateDoor?.Invoke(door);
        door.Quality = saved;
    }

    /// <summary>
    /// Exult <c>Path_walking_actor_action::open_door</c>: open the door, then
    /// walk to the tile past its middle and face onwards; the door is closed
    /// again when that is done. Only one door per walk.
    /// </summary>
    bool OpenDoor(U7Object actor, U7Object door)
    {
        if (_handlingDoor || _doorSequenceComplete || ActivateDoor is null)
        {
            return false;
        }

        _handlingDoor = true;
        _door = door;
        var footX = door.Tx - door.DimX + 1;
        var footY = door.Ty - door.DimY + 1;
        ActivateWithoutQuality(door);
        int px;
        int py;
        int dir;
        if (door.DimX > door.DimY)
        {
            px = footX + door.DimX / 2;
            (py, dir) = U7Constants.TileDelta(footY, actor.Ty) <= 0 ? (footY + door.DimY, 0) : (footY - 1, 4);
        }
        else
        {
            py = footY + door.DimY / 2;
            (px, dir) = U7Constants.TileDelta(footX, actor.Tx) <= 0 ? (footX + door.DimX, 6) : (footX - 1, 2);
        }

        // (Exult's find_spot here has no effect: its result is discarded.)
        var past = new TileCoord(U7Constants.WrapTile(px), U7Constants.WrapTile(py), actor.Tz);
        _doorSequenceComplete = false;
        _subseq = SequenceAction.WalkThen(_map, actor, past,
            new FramesAction([ActorWalker.DirFrame(dir, 0)], 100));
        return true;
    }
}
