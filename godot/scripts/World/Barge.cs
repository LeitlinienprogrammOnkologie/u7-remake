using U7.Actors;
using U7.Core;
using U7.Data;

namespace U7.World;

/// <summary>
/// Exult <c>Barge_object</c>: a ship, cart or flying carpet. The barge itself
/// is an invisible footprint (shape 961) with its hot spot at the lower right;
/// its hull, sails, seats and whatever stands on it are objects of their own,
/// gathered up and moved together, turned as it turns.
/// </summary>
public sealed class Barge
{
    public const int Shape = 961;
    const int Height = 4; // Exult assumes a barge is 4 lifts high.

    readonly GameMap _map;
    readonly List<U7Object> _objects = new();
    bool _gathered;
    /// <summary>Exult <c>boat</c>: 1 if on water, 0 if not, -1 untested.</summary>
    int _boat = -1;
    bool _firstStep = true;
    bool _takingSecondStep;
    PathSteps? _path;
    bool _queued;
    double _wait;
    static int _doneRecursion;

    public Barge(GameMap map, U7Object obj)
    {
        _map = map;
        Obj = obj;
        SetCenter();
    }

    public U7Object Obj { get; }
    public TileCoord Center { get; private set; }
    /// <summary>Exult <c>frame_time</c>: milliseconds between moves, 0 when not moving.</summary>
    public int FrameTime { get; private set; }
    public bool IsMoving => FrameTime > 0;
    /// <summary>Exult <c>activate_eggs</c> after a step: the barge stepped from this tile.</summary>
    public Action<TileCoord>? Stepped { get; set; }
    /// <summary>Exult <c>finish_move</c>'s <c>scroll_if_needed(center)</c>: the barge and all on it moved.</summary>
    public Action? Moved { get; set; }
    /// <summary>Runs an object's usecode as a double-click (the sails, in <see cref="Done"/>).</summary>
    public static Action<U7Object>? Activate { get; set; }

    TileCoord Here => new(Obj.Tx, Obj.Ty, Obj.Tz);
    int XTiles => Obj.BargeXTiles;
    int YTiles => Obj.BargeYTiles;

    /// <summary>Exult <c>get_tile_footprint</c>: x, y of the upper left, width, height.</summary>
    public (int X, int Y, int W, int H) Footprint =>
        (U7Constants.WrapTile(Obj.Tx - XTiles + 1), U7Constants.WrapTile(Obj.Ty - YTiles + 1), XTiles, YTiles);

    public bool InFootprint(int tx, int ty)
    {
        var f = Footprint;
        return U7Constants.WrapTile(tx - f.X) < f.W && U7Constants.WrapTile(ty - f.Y) < f.H;
    }

    public bool Contains(U7Object obj) => _objects.Contains(obj);

    /// <summary>Exult <c>set_to_gather</c>: gather again before the next move.</summary>
    public void SetToGather() => _gathered = false;

    void SetCenter() =>
        Center = new TileCoord(U7Constants.WrapTile(Obj.Tx - XTiles / 2), U7Constants.WrapTile(Obj.Ty - YTiles / 2), Obj.Tz);

    /// <summary>
    /// Exult <c>Barge_object::gather</c>: everything in the footprint from
    /// the barge's lift up to five above (its own parts from one below), but
    /// no eggs; and whether it floats on water.
    /// </summary>
    public void Gather()
    {
        _objects.Clear();
        var f = Footprint;
        var lift = Obj.Tz;
        for (var cy = f.Y / U7Constants.TilesPerChunk; cy <= (f.Y + f.H - 1) / U7Constants.TilesPerChunk; cy++)
        {
            for (var cx = f.X / U7Constants.TilesPerChunk; cx <= (f.X + f.W - 1) / U7Constants.TilesPerChunk; cx++)
            {
                foreach (var obj in _map.ObjectsInChunk(cx, cy))
                {
                    if (obj == Obj || obj.IsEgg || obj.Removed || !InFootprint(obj.Tx, obj.Ty))
                    {
                        continue;
                    }

                    var info = _map.Catalog[obj.Shape];
                    if (obj.Tz + info.DimZ > lift &&
                        ((info.BargePart && obj.Tz >= lift - 1) || (obj.Tz < lift + 5 && obj.Tz >= lift)))
                    {
                        _objects.Add(obj);
                    }
                }
            }
        }

        SetCenter();
        if (_boat == -1)
        {
            _boat = _map.Catalog[_map.GetFlat(Center.Tx, Center.Ty).Shape].Water ? 1 : 0;
        }

        _gathered = true;
    }

    /// <summary>Exult <c>travel_to_tile</c>: head straight there (turning to face that way), <paramref name="speedMs"/> between moves.</summary>
    public void TravelTo(TileCoord dest, int speedMs)
    {
        var cur = Here;
        _path = ZombieSteps.Line(cur, dest);
        if (_path is null)
        {
            FrameTime = 0;
            return;
        }

        FrameTime = speedMs;
        FaceDirection(ActorWalker.Direction4(-U7Constants.TileDelta(cur.Ty, dest.Ty), U7Constants.TileDelta(cur.Tx, dest.Tx)));
        if (!_queued)
        {
            _queued = true;
            _wait = 0;
        }
    }

    /// <summary>Exult <c>stop</c>.</summary>
    public void Stop()
    {
        FrameTime = 0;
        _firstStep = true;
    }

    /// <summary>
    /// Exult <c>Barge_object::handle_event</c>: two steps per move once
    /// under way (one at first), until blocked or there.
    /// </summary>
    public void Update(double delta)
    {
        if (!_queued)
        {
            return;
        }

        _wait -= delta * 1000;
        while (_queued && _wait <= 0)
        {
            if (_path is null || FrameTime == 0)
            {
                _queued = false;
                break;
            }

            if (!_path.NextStep(out var tile, out _) || !Step(tile))
            {
                FrameTime = 0;
            }
            else if (!_firstStep)
            {
                _takingSecondStep = true;
                if (!_path.NextStep(out tile, out _) || !Step(tile))
                {
                    FrameTime = 0;
                }

                _takingSecondStep = false;
            }

            _firstStep = false;
            if (FrameTime > 0)
            {
                _wait += FrameTime;
            }
            else
            {
                _queued = false;
            }
        }
    }

    /// <summary>
    /// Exult <c>Barge_object::step</c>: onto an adjacent tile, neither rising
    /// nor dropping; a carpet in the air levitates, a boat swims, a cart
    /// walks. False if blocked.
    /// </summary>
    public bool Step(TileCoord t, bool force = false)
    {
        if (!_gathered)
        {
            Gather();
        }

        var cur = Here;
        var moveType = cur.Tz > 0 ? MoveFlags.Levitate : force ? MoveFlags.All : _boat != 0 ? MoveFlags.Swim : MoveFlags.Walk;
        if (_map.Blocking.IsBlockedStep(XTiles, YTiles, Height, cur, ref t, moveType, maxDrop: 0, maxRise: 0))
        {
            return false;
        }

        Move(t.Tx, t.Ty, t.Tz);
        Stepped?.Invoke(cur);
        return true;
    }

    /// <summary>Exult <c>Barge_object::move</c>: the barge and all on it by the same amount; cart wheels turn and horses trot.</summary>
    public void Move(int newTx, int newTy, int newTz)
    {
        if (!_gathered)
        {
            Gather();
        }

        var dx = newTx - Obj.Tx;
        var dy = newTy - Obj.Ty;
        var dz = newTz - Obj.Tz;
        var objs = new List<U7Object>(_objects.Count + 1) { Obj };
        var positions = new List<TileCoord>(objs.Capacity) { new(newTx, newTy, newTz) };
        var frames = new List<int>(objs.Capacity) { Obj.Frame };
        foreach (var obj in _objects)
        {
            objs.Add(obj);
            positions.Add(new TileCoord(obj.Tx + dx, obj.Ty + dy, obj.Tz + dz));
            var frame = obj.Frame;
            if (!_takingSecondStep)
            {
                frame = _map.Catalog[obj.Shape].BargeType switch
                {
                    4 => ((frame + 1) & 3) | (frame & 32), // Cart wheel.
                    5 => ((frame + 4) & 15) | (frame & 32), // Draft horse.
                    _ => frame
                };
            }

            frames.Add(frame);
        }

        _map.MoveGroup(objs, positions, frames);
        SetCenter();
        Moved?.Invoke();
    }

    /// <summary>Exult <c>face_direction</c>: 0-7, north first.</summary>
    void FaceDirection(int dir)
    {
        switch ((4 + dir / 2 - Obj.BargeDir) % 4)
        {
            case 1:
                Turn(1);
                break;
            case 2:
                Turn(2);
                break;
            case 3:
                Turn(3);
                break;
        }
    }

    /// <summary>
    /// Exult <c>turn_right</c> / <c>turn_around</c> / <c>turn_left</c>
    /// (<paramref name="quads"/> 1, 2, 3): round the centre, each part on its
    /// rotated frame; a quarter turn only if the new footprint is clear.
    /// </summary>
    void Turn(int quads)
    {
        var rot = RotateHotspot(Here, XTiles, YTiles, Center, quads);
        if (quads != 2 && !OkayToRotate(rot))
        {
            return;
        }

        var objs = new List<U7Object>(_objects.Count + 1) { Obj };
        var positions = new List<TileCoord>(objs.Capacity) { rot };
        var frames = new List<int>(objs.Capacity) { Obj.Frame };
        foreach (var obj in _objects)
        {
            objs.Add(obj);
            positions.Add(RotateHotspot(new TileCoord(obj.Tx, obj.Ty, obj.Tz), obj.DimX, obj.DimY, Center, quads));
            frames.Add(RotatedFrame(obj, quads));
        }

        if (quads != 2)
        {
            (Obj.BargeXTiles, Obj.BargeYTiles) = (Obj.BargeYTiles, Obj.BargeXTiles);
        }

        Obj.BargeDir = (Obj.BargeDir + quads) % 4;
        _map.MoveGroup(objs, positions, frames);
        SetCenter();
        Moved?.Invoke();
    }

    /// <summary>
    /// Exult <c>Rotate90r</c> / <c>Rotate180</c> / <c>Rotate90l</c> for an
    /// object of the given size with its hot spot at the lower right.
    /// </summary>
    static TileCoord RotateHotspot(TileCoord t, int xtiles, int ytiles, TileCoord c, int quads)
    {
        var rx = t.Tx - c.Tx;
        var ry = c.Ty - t.Ty;
        return quads switch
        {
            1 => new TileCoord(U7Constants.WrapTile(c.Tx + ry + ytiles), U7Constants.WrapTile(c.Ty + rx), t.Tz),
            2 => new TileCoord(U7Constants.WrapTile(c.Tx - rx + xtiles), U7Constants.WrapTile(c.Ty + ry + ytiles), t.Tz),
            _ => new TileCoord(U7Constants.WrapTile(c.Tx - ry), U7Constants.WrapTile(c.Ty - rx + xtiles), t.Tz)
        };
    }

    /// <summary>Exult <c>Shape_info::get_rotated_frame</c>: seats turn by facing, barge parts by their own rule, others reflect.</summary>
    int RotatedFrame(U7Object obj, int quads)
    {
        var info = _map.Catalog[obj.Shape];
        var frame = obj.Frame;
        if (info.BargeType == 2)
        {
            var dir = frame % 4;
            return frame - dir + (dir + quads) % 4;
        }

        if (info.BargePart)
        {
            return quads switch
            {
                1 => (frame ^ 32) ^ ((frame & 32) != 0 ? 3 : 1),
                2 => frame ^ 2,
                3 => (frame ^ 32) ^ ((frame & 32) != 0 ? 1 : 3),
                _ => frame
            };
        }

        return frame ^ ((quads % 2) << 5);
    }

    /// <summary>Exult <c>okay_to_rotate</c>: the parts of the turned footprint outside the old one must be clear at the barge's lift.</summary>
    bool OkayToRotate(TileCoord pos)
    {
        var lift = Obj.Tz;
        var moveType = lift > 0 ? MoveFlags.Levitate : MoveFlags.Walk | MoveFlags.Swim;
        var foot = Footprint;
        var newX = pos.Tx - YTiles + 1;
        var newY = pos.Ty - XTiles + 1;
        var newW = YTiles;
        var newH = XTiles;
        bool Blocked(int x, int y, int w, int h) =>
            w > 0 && h > 0 &&
            (_map.Blocking.IsBlockedArea(Height, lift, x, y, w, h, out var newLift, moveType, maxDrop: 0) || newLift != lift);

        return !(newY < foot.Y && Blocked(newX, newY, newW, foot.Y - newY)) &&
               !(foot.Y + foot.H < newY + newH && Blocked(newX, foot.Y + foot.H, newW, newY + newH - (foot.Y + foot.H))) &&
               !(newX < foot.X && Blocked(newX, newY, foot.X - newX, newH)) &&
               !(foot.X + foot.W < newX + newW && Blocked(foot.X + foot.W, newY, newX + newW - (foot.X + foot.W), newH));
    }

    /// <summary>Exult <c>Barge_object::done</c>: out of barge mode a boat furls its sails (as if they were clicked).</summary>
    public void Done()
    {
        _gathered = false;
        if (_doneRecursion > 0)
        {
            return;
        }

        _doneRecursion++;
        if (_boat == 1)
        {
            foreach (var obj in _objects)
            {
                if (_map.Catalog[obj.Shape].BargeType == 3 && (obj.Frame & 7) < 4)
                {
                    Activate?.Invoke(obj);
                    break;
                }
            }
        }

        _doneRecursion--;
    }

    /// <summary>Exult <c>okay_to_land</c>: nothing below the barge in its footprint and no water.</summary>
    public bool OkayToLand()
    {
        var f = Footprint;
        for (var y = 0; y < f.H; y++)
        {
            for (var x = 0; x < f.W; x++)
            {
                var tx = U7Constants.WrapTile(f.X + x);
                var ty = U7Constants.WrapTile(f.Y + y);
                if (ChunkBlocking.HighestBlocked(_map.Blocking.Column(tx, ty), Obj.Tz) != -1 ||
                    _map.Catalog[_map.GetFlat(tx, ty).Shape].Water)
                {
                    return false;
                }
            }
        }

        return true;
    }
}

/// <summary>
/// Exult's barge handling in <c>Game_window</c> and usecode: the barge in
/// barge mode (<c>moving_barge</c>) and <c>Get_barge</c>.
/// </summary>
public sealed class Barges(GameMap map)
{
    readonly Dictionary<U7Object, Barge> _all = new();

    /// <summary>Exult <c>moving_barge</c>: the barge the player steers, if any.</summary>
    public Barge? Moving { get; private set; }

    /// <summary>A barge stepped from a tile: the eggs where it went.</summary>
    public Action<Barge, TileCoord>? Stepped { get; set; }
    /// <summary>A barge and all on it moved (or turned).</summary>
    public Action<Barge>? Moved { get; set; }

    public Barge Of(U7Object bargeObj)
    {
        if (!_all.TryGetValue(bargeObj, out var barge))
        {
            var b = barge = new Barge(map, bargeObj);
            b.Stepped = from => Stepped?.Invoke(b, from);
            b.Moved = () => Moved?.Invoke(b);
            _all[bargeObj] = barge;
        }

        return barge;
    }

    /// <summary>
    /// Exult <c>Get_barge</c>: the object itself if it is a barge, else the
    /// barge within 20 tiles whose footprint it is in (the highest one
    /// beneath it).
    /// </summary>
    public Barge? GetBarge(U7Object obj)
    {
        if (obj.IsBarge)
        {
            return Of(obj);
        }

        U7Object? best = null;
        foreach (var b in map.FindNearby(new TileCoord(obj.Tx, obj.Ty, obj.Tz), Barge.Shape, 20))
        {
            if (!b.IsBarge || !Of(b).InFootprint(obj.Tx, obj.Ty))
            {
                continue;
            }

            if (best is null || (best.Tz > obj.Tz && b.Tz <= obj.Tz) || (b.Tz <= obj.Tz && b.Tz > best.Tz))
            {
                best = b;
            }
        }

        return best is null ? null : Of(best);
    }

    /// <summary>Exult <c>Game_window::set_moving_barge</c>: into barge mode (gathering what is aboard) or out of it.</summary>
    public void SetMoving(Barge? barge, U7Object avatar)
    {
        if (barge is not null && barge != Moving)
        {
            barge.Gather();
            if (!barge.Contains(avatar))
            {
                barge.SetToGather();
            }
        }
        else if (barge is null && Moving is not null)
        {
            Moving.Done();
        }

        Moving = barge;
    }

    /// <summary>Exult <c>teleport_party</c>: out of barge mode without the sails.</summary>
    public void Drop() => Moving = null;

    public void Update(double delta) => Moving?.Update(delta);
}
