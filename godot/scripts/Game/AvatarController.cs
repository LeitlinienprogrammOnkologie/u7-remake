using Godot;
using U7.Actors;
using U7.Core;
using U7.Data;

namespace U7.Game;

/// <summary>
/// The player walking the avatar, as Exult's <c>Game_window</c> does it:
/// holding the mouse button or a key steers a straight walk a few tiles ahead
/// in that direction (<c>start_actor</c>), a click walks an A* path to the
/// tile (<c>start_actor_along_path</c>), and letting go stops
/// (<c>stop_actor</c>). The walk takes a step whenever its delay runs out,
/// at the speed given (<see cref="WalkSpeed"/>).
/// </summary>
public sealed class AvatarController
{
    /// <summary>Exult <c>step_tile_delta</c>: how far ahead a steered walk aims.</summary>
    const int StepTileDelta = 8;

    public U7Object Avatar { get; }
    readonly GameMap _map;
    PathWalk? _walk;
    /// <summary>Milliseconds until the walk's next step.</summary>
    double _waitMs;
    /// <summary>Exult <c>rest_time</c>: idle milliseconds since the last step.</summary>
    double _restMs;
    int _steerDir = -1;
    int _steerSpeed;

    /// <summary>The avatar stepped (from x, y): eggs and followers react.</summary>
    public Action<U7Object, int, int>? Moved;

    public bool IsPlayerMoving => _walk is not null;

    public AvatarController(U7Object avatar, GameMap map)
    {
        Avatar = avatar;
        _map = map;
        Avatar.IsActor = true;
        Avatar.NpcNum = 0;
        if (Avatar.Frame == 0)
        {
            Avatar.Frame = 16;
        }
    }

    /// <summary>
    /// Exult <c>Game_window::start_actor_alt</c>: walk straight toward
    /// <paramref name="target"/> (world pixels), sidestepping one direction
    /// if that way is blocked, aiming <see cref="StepTileDelta"/> tiles ahead.
    /// Only re-aims when the direction or speed changes.
    /// </summary>
    public void Steer(Vector2 target, int speed)
    {
        U7.Rendering.WorldView.ShapeLocation(Avatar.Tx, Avatar.Ty, Avatar.Tz, out var ax, out var ay);
        var start = new TileCoord(Avatar.Tx, Avatar.Ty, Avatar.Tz);
        var levitating = (Avatar.TypeFlags & MoveFlags.Levitate) != 0;
        var blocked = new bool[8];
        for (var d = 0; d < 8; d++)
        {
            var next = start.Neighbor(d);
            blocked[d] = ActorWalker.IsBlocked(_map, Avatar, ref next, start, Avatar.TypeFlags) ||
                         (!levitating && Math.Abs(start.Tz - next.Tz) > 1);
        }

        var dir = ActorWalker.DirectionNoWrap(ay - Mathf.RoundToInt(target.Y), Mathf.RoundToInt(target.X) - ax);
        if (blocked[dir])
        {
            if (!blocked[(dir + 1) % 8])
            {
                dir = (dir + 1) % 8;
            }
            else if (!blocked[(dir + 7) % 8])
            {
                dir = (dir + 7) % 8;
            }
            else
            {
                // An NPC in the way may step aside (not while walking: Exult's is_moving).
                var block = Avatar.FrameTime != 0 ? null : ActorWalker.FindBlocking(_map, Avatar, dir);
                if (block is not { IsActor: true } || !ActorWalker.MoveAside(_map, block, Avatar, dir))
                {
                    Stop();
                    UnstickFromAir(start);
                    return;
                }
            }
        }

        if (_walk is not null && dir == _steerDir && speed == _steerSpeed)
        {
            return;
        }

        var aim = start.Neighbor(dir);
        var dest = new TileCoord(start.Tx + StepTileDelta * U7Constants.TileDelta(start.Tx, aim.Tx),
            start.Ty + StepTileDelta * U7Constants.TileDelta(start.Ty, aim.Ty), start.Tz).Wrapped();
        Start(PathWalk.Line(_map, Avatar, dest), speed);
        _steerDir = dir;
        _steerSpeed = speed;
    }

    /// <summary>Exult: if stuck up on something with nothing below to stand on, drop down.</summary>
    void UnstickFromAir(TileCoord start)
    {
        if (Avatar.Tz % 5 == 0)
        {
            return;
        }

        if (!_map.Blocking.IsBlocked(1, start.Tz, start.Tx, start.Ty, out var lift, MoveFlags.Walk, 100) &&
            lift < start.Tz)
        {
            _map.MoveObject(Avatar, start.Tx, start.Ty, lift);
        }
    }

    /// <summary>
    /// Exult <c>Game_window::start_actor_along_path</c>: an A* walk to the
    /// tile at the avatar's lift; false if there is no way there.
    /// </summary>
    public bool PathTo(TileCoord dest, int speed)
    {
        var walk = PathWalk.Astar(_map, Avatar, dest with { Tz = Avatar.Tz });
        if (walk is null)
        {
            GD.Print("Couldn't find path for Avatar.");
            Stop();
            return false;
        }

        Start(walk, speed);
        _steerDir = -1;
        return true;
    }

    /// <summary>Exult <c>Actor::start(speed, 0)</c>: the walk takes over; a running step timer is kept.</summary>
    void Start(PathWalk? walk, int speed)
    {
        var moving = _walk is not null;
        _walk = walk;
        if (walk is null)
        {
            return;
        }

        walk.Stepped = (a, fx, fy) =>
        {
            _restMs = 0;
            Moved?.Invoke(a, fx, fy);
        };
        Avatar.FrameTime = speed;
        if (!moving)
        {
            _waitMs = 0;
        }
    }

    /// <summary>Exult <c>Game_window::stop_actor</c>: stop and stand.</summary>
    public void Stop()
    {
        if (_walk is { } walk)
        {
            walk.Stop(Avatar);
        }

        _walk = null;
        _steerDir = -1;
        Avatar.FrameTime = 0;
    }

    /// <summary>Drop the walk without changing the avatar's frame (teleports, combat).</summary>
    public void ClearPath()
    {
        _walk = null;
        _steerDir = -1;
        Avatar.FrameTime = 0;
    }

    /// <summary>
    /// Exult <c>Main_actor::handle_event</c>: take the walk's next step when
    /// its delay is up. Idle for 2 s, the avatar stands (Exult <c>resting</c>),
    /// unless a swing holds the frame.
    /// </summary>
    public void Update(double delta, bool holdFrame = false, bool inUsecodeControl = false)
    {
        if (inUsecodeControl)
        {
            // Exult: no walking while a script moves the avatar, and its frames are the script's.
            ClearPath();
            return;
        }

        if (_walk is null)
        {
            _restMs += delta * 1000;
            if (_restMs > 2000 && !holdFrame)
            {
                StandAtRest();
            }

            return;
        }

        _waitMs -= delta * 1000;
        while (_walk is { } walk && _waitMs <= 0)
        {
            var d = walk.HandleEvent(Avatar);
            if (d == 0)
            {
                // Finished: the next walk waits one step (Exult keeps frame_time).
                _walk = null;
                _steerDir = -1;
                _waitMs += Avatar.FrameTime > 0 ? Avatar.FrameTime : U7Constants.StandardDelayMs;
                Avatar.FrameTime = 0;
                break;
            }

            _waitMs += d;
        }
    }

    /// <summary>Exult <c>Actor::stand_at_rest</c>.</summary>
    void StandAtRest()
    {
        _restMs = 0;
        var frame = Avatar.Frame & 0xf;
        if (frame is 0 or ActorWalker.SitFrame or ActorWalker.SleepFrame || Avatar.IsDead || Avatar.GetFlag(ObjFlag.Asleep))
        {
            return;
        }

        ActorWalker.Stand(Avatar, ActorWalker.FacingOfFrame(Avatar.Frame));
    }
}
