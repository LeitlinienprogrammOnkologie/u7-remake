using Godot;
using U7.Actors;
using U7.Core;
using U7.Data;
using U7.World;

namespace U7.Game;

/// <summary>
/// Keyboard / click walking. Click-to-walk uses A*; keyboard is one tile
/// at a time. Avatar frames: N 0–2, S 16–18, W 32–34, E 48–50.
/// </summary>
public sealed class AvatarController
{
    public U7Object Avatar { get; }
    readonly GameMap _map;
    double _stepTimer;
    readonly double _stepInterval = U7Constants.StandardDelayMs / 1000.0;
    int _facing = 4;
    Vector2I? _clickTarget;
    List<Vector2I>? _path;
    int _pathI;

    public Action<U7Object, int, int>? Moved;

    public bool IsPlayerMoving =>
        KeyboardDir() is not null ||
        (_path is { Count: > 0 } && _pathI < _path.Count) ||
        _clickTarget is not null;

    public AvatarController(U7Object avatar, GameMap map)
    {
        Avatar = avatar;
        _map = map;
        Avatar.IsActor = true;
        Avatar.Solid = false;
        Avatar.NpcNum = 0;
        if (Avatar.Frame == 0)
        {
            Avatar.Frame = 16;
        }
    }

    public void Update(double delta, Vector2I? clickTile, bool holdFrame = false)
    {
        if (clickTile is { } t)
        {
            _clickTarget = t;
            _path = Pathfinder.Find(_map, Avatar.Tx, Avatar.Ty, t.X, t.Y, Avatar.Tz);
            _pathI = 0;
            if (_path is null)
            {
                _path = new List<Vector2I>();
            }
        }

        _stepTimer -= delta;
        if (_stepTimer > 0)
        {
            return;
        }

        var dir = KeyboardDir();
        if (dir is { } kb)
        {
            _path = null;
            _clickTarget = null;
            TryStep(kb.X, kb.Y);
            _stepTimer = _stepInterval;
            return;
        }

        if (_path is { Count: > 0 } && _pathI < _path.Count)
        {
            var next = _path[_pathI];
            var dx = Math.Sign(U7Constants.TileDelta(Avatar.Tx, next.X));
            var dy = Math.Sign(U7Constants.TileDelta(Avatar.Ty, next.Y));
            if (dx == 0 && dy == 0)
            {
                _pathI++;
                return;
            }

            if (!TryStep(dx, dy))
            {
                _path = Pathfinder.Find(_map, Avatar.Tx, Avatar.Ty,
                    _clickTarget?.X ?? next.X, _clickTarget?.Y ?? next.Y, Avatar.Tz);
                _pathI = 0;
            }
            else
            {
                _pathI++;
            }

            _stepTimer = _stepInterval;
            return;
        }

        if (_clickTarget is { } target)
        {
            var dx = Math.Sign(U7Constants.TileDelta(Avatar.Tx, target.X));
            var dy = Math.Sign(U7Constants.TileDelta(Avatar.Ty, target.Y));
            if (dx == 0 && dy == 0)
            {
                _clickTarget = null;
                ActorWalker.Stand(Avatar, _facing);
                return;
            }

            var step = Pathfinder.GreedyStep(_map, Avatar.Tx, Avatar.Ty, target.X, target.Y, Avatar.Tz);
            dx = Math.Sign(U7Constants.TileDelta(Avatar.Tx, step.X));
            dy = Math.Sign(U7Constants.TileDelta(Avatar.Ty, step.Y));
            TryStep(dx, dy);
            _stepTimer = _stepInterval;
            return;
        }

        if (!holdFrame)
        {
            ActorWalker.Stand(Avatar, _facing);
        }
    }

    public void ClearPath()
    {
        _path = null;
        _clickTarget = null;
    }

    Vector2I? KeyboardDir()
    {
        var x = 0;
        var y = 0;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left))
        {
            x -= 1;
        }

        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right))
        {
            x += 1;
        }

        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up))
        {
            y -= 1;
        }

        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down))
        {
            y += 1;
        }

        if (x == 0 && y == 0)
        {
            return null;
        }

        return new Vector2I(x, y);
    }

    bool TryStep(int dx, int dy)
    {
        _facing = ActorWalker.DirIndex(dx, dy);
        var fromTx = Avatar.Tx;
        var fromTy = Avatar.Ty;
        if (!ActorWalker.TryStep(_map, Avatar, dx, dy))
        {
            return false;
        }

        if (Avatar.Tx != fromTx || Avatar.Ty != fromTy)
        {
            Moved?.Invoke(Avatar, fromTx, fromTy);
        }

        return true;
    }
}
