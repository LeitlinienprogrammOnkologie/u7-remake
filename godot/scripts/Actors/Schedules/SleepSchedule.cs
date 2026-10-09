using U7.Core;
using U7.Data;

namespace U7.Actors;

/// <summary>
/// Exult <c>Sleep_schedule</c>: find the closest free bed in sight, walk to
/// within 3 tiles of it, unmake it and lie on it asleep (with no bed, lie down
/// where it stands); on leaving, step back onto the floor and make the bed.
/// </summary>
public sealed class SleepSchedule(NpcBrain brain) : Schedule(brain)
{
    // Exult Sleep_schedule: BG bedspread frames 3..16; even = spread out.
    const int Spread0 = 3;
    const int Spread1 = 16;

    /// <summary>Exult's BG beds: east-west (696), north-south (1011), and the gargoyles' futons (363, 312).</summary>
    static readonly int[] BedShapes = [696, 1011, 363, 312];

    /// <summary>Exult <c>SleepUsecode</c>: what a nap in bed runs (BG 0x622: the hours, the fade, the dreams).</summary>
    const int SleepUsecode = 0x622;

    /// <summary>Exult <c>Ucscript</c>'s <c>finish</c> and <c>frame</c>, for making the bed.</summary>
    const int ScriptFinish = 0x2c, ScriptFrame = 0x46;

    U7Object? _bed;
    TileCoord? _floorLoc;
    int _state;
    /// <summary>Exult <c>sleep_interrupted</c>: woken by the avatar, so the bed isn't made.</summary>
    bool _interrupted;
    readonly U7Object? _napBed;

    /// <summary>Exult <c>Sleep_schedule::set_bed</c> (<c>nap_time</c>): the bed the avatar goes to sleep in.</summary>
    public U7Object? NapBed
    {
        get => _napBed;
        init
        {
            _napBed = value;
            _bed = value;
        }
    }

    /// <summary>Exult's <c>set_schedule_type</c> changes no frame; <see cref="NowWhat"/> lies down.</summary>
    public override void Begin()
    {
    }

    public override void NowWhat()
    {
        if (_bed is { } held && _state <= 1 && (held.Removed || IsBedOccupied(held)))
        {
            _bed = null;
            _state = 0;
            Brain.StopAction();
        }

        if (_bed is null && _state == 0 && !Npc.GetFlag(ObjFlag.Asleep))
        {
            _bed = ClosestFreeBed();
        }

        if (_bed is null && Npc == Runner.Avatar)
        {
            Brain.StepTimer = U7Constants.StandardDelayMs / 1000.0;
            return;
        }

        if ((Npc.Frame & 0xf) == ActorWalker.SleepFrame)
        {
            return; // Already sleeping.
        }

        switch (_state)
        {
            case 0:
                if (_bed is null)
                {
                    LieDownHere();
                    return;
                }

                // Walk to within 3 tiles of the middle of the bed, on its floor.
                _state = 1;
                _bed = TopOf(_bed);
                var bloc = new TileCoord(_bed.Tx - _bed.DimX / 2, _bed.Ty - _bed.DimY / 2, _bed.Tz - _bed.Tz % 5);
                StartAction(PathWalk.Astar(Map, Npc, bloc, dist: 3), 200, 0);
                return;
            case 1:
                // (Exult tries the walk again when it ended more than 3 tiles off, but lies down all the same.)
                LieIn(_bed!);
                _state = 2;
                if (_napBed is not null)
                {
                    UsecodeAction.Call?.Invoke(SleepUsecode, _bed!, 1); // double-click
                }

                return;
        }
    }

    /// <summary>Exult: the closest bed of <see cref="BedShapes"/> within 24 tiles that is free and in a straight line.</summary>
    U7Object? ClosestFreeBed()
    {
        U7Object? best = null;
        var bestDist = 100;
        foreach (var shape in BedShapes)
        {
            foreach (var bed in Map.FindNearby(Here(Npc), shape, 24))
            {
                var dist = ObjectGeometry.Distance(Npc, bed);
                if (dist < bestDist && !IsBedOccupied(bed) && Runner.IsStraightPath(Npc, bed))
                {
                    bestDist = dist;
                    best = bed;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Exult, with no bed: lie down where it stands, facing west if there is
    /// room for the body that way, else north, else west all the same.
    /// </summary>
    void LieDownHere()
    {
        var here = Here(Npc);
        var height = Map.Catalog[Npc.Shape].DimZ;
        var dir = 6;
        foreach (var direction in (int[])[6, 0])
        {
            var (t1, t2) = direction == 6
                ? (here with { Tx = here.Tx + 1 }, here with { Tx = here.Tx - (height - 1) })
                : (here with { Ty = here.Ty + 1 }, here with { Ty = here.Ty - height });
            if (!ActorWalker.IsBlocked(Map, Npc, ref t1) && !ActorWalker.IsBlocked(Map, Npc, ref t2))
            {
                dir = direction;
                break;
            }
        }

        SleepFacing(dir);
        Runner.ForceSleep?.Invoke(Npc);
    }

    /// <summary>The sleep frame facing the direction, or the direction's standing frame when the shape has none.</summary>
    void SleepFacing(int dir)
    {
        var frame = ActorWalker.DirFrame(dir, ActorWalker.SleepFrame);
        Npc.Frame = frame < Map.Catalog[Npc.Shape].FrameCount ? frame : frame & 0x30;
        Npc.WalkFrameIndex = 0;
    }

    /// <summary>The sheet object on the bed's floor, when the bed is a stack (Exult's top of bed).</summary>
    U7Object TopOf(U7Object bed)
    {
        var floor = bed.Tz / 5;
        foreach (var top in Map.FindNearby(Here(bed), bed.Shape, 1))
        {
            if (top.Frame >= Spread0 && top.Frame <= Spread1 && top.Tz / 5 == floor)
            {
                return top;
            }
        }

        return bed;
    }

    /// <summary>
    /// Exult <c>Sleep_schedule::now_what</c> state 1: unmake the bed and lie on
    /// top of it (bed lift + bed height), facing west in EW beds and futons,
    /// north in NS ones, asleep.
    /// </summary>
    void LieIn(U7Object bed)
    {
        Brain.StopAction();
        SleepFacing(bed.Shape is 696 or 363 ? 6 : 0);
        _floorLoc = Here(Npc);
        Runner.TerminateScripts?.Invoke(bed);
        var bedframe = bed.Frame;
        if (bedframe >= Spread0 && bedframe < Spread1 && bedframe % 2 == 1)
        {
            bed.Frame = ++bedframe; // Unmake the bed.
        }

        var bedspread = bedframe >= Spread0 && bedframe % 2 == 0;
        // Children are not covered completely by the sheets.
        var height = Map.Catalog[Npc.Shape].DimZ;
        var delta = height < 4 ? height - 4 : 0;
        var bedHeight = Map.Catalog[bed.Shape].DimZ;
        Map.MoveObject(Npc, bed.Tx + delta, bed.Ty + delta, bed.Tz + (bedspread ? 0 : bedHeight));
        Runner.ForceSleep?.Invoke(Npc);
    }

    /// <summary>
    /// Exult <c>Sleep_schedule::ending</c>: still in bed, back to a free spot
    /// on the floor and (unless woken, or going into combat) the bed is made a
    /// moment later, the NPC turning to make it. For sleep or wait (Skara
    /// Brae's ghosts, Penumbra) it stays lying.
    /// </summary>
    public override void Ending(int newType)
    {
        if (newType is ScheduleType.Sleep or ScheduleType.Wait)
        {
            return;
        }

        var makeBed = false;
        var dir = 0;
        var lying = (Npc.Frame & 0xf) == ActorWalker.SleepFrame;
        if (_bed is { Removed: false } bed && lying && ObjectGeometry.Distance(Npc, bed) < 8)
        {
            _floorLoc = FloorSpot(_floorLoc ?? FloorBelow());
            var frnum = bed.Frame;
            if (!_interrupted && newType != ScheduleType.Combat && frnum is >= Spread0 and <= Spread1 && frnum % 2 == 0 &&
                !IsBedOccupied(bed))
            {
                makeBed = true;
                Runner.Script?.Invoke(bed, [ScriptFinish, ScriptDelayTicks, 3, ScriptFrame, frnum - 1]);
                if (_floorLoc is { } at)
                {
                    var bloc = ObjectGeometry.CenterTile(bed);
                    dir = Directions.Of(at.Ty - bloc.Ty, bloc.Tx - at.Tx);
                }
            }
        }
        else if (_bed is null && lying)
        {
            // The port's: a sleeper loaded asleep has no bed recorded; Exult would stand it up on the bed.
            _floorLoc = FloorSpot(FloorBelow());
        }

        if (_floorLoc is { } floor)
        {
            Map.MoveObject(Npc, floor.Tx, floor.Ty, floor.Tz);
        }

        Npc.ClearFlag(ObjFlag.Asleep); // Exult clear_sleep: the flag alone.
        ActorWalker.Stand(Npc, ActorWalker.FacingOfFrame(Npc.Frame));
        if (makeBed && !_interrupted)
        {
            RunScript(ScriptDontHalt, ScriptFaceDir, dir, ScriptReadyFrame, ScriptDelayTicks, 1, ScriptRaise1Frame,
                ScriptDelayTicks, 1, ScriptReadyFrame, ScriptDelayTicks, 1, ScriptStandFrame);
        }

        _interrupted = false;
        _state = 0; // In case it goes back to sleep.
    }

    TileCoord FloorBelow() => new(Npc.Tx, Npc.Ty, Npc.Tz - Npc.Tz % 5);

    /// <summary>Exult: a free spot within 6 tiles for the standing NPC, a lift change allowed if need be; null if none.</summary>
    TileCoord? FloorSpot(TileCoord near) =>
        Map.FindSpot(near, 6, Npc.Shape, 0) ?? Map.FindSpot(near, 6, Npc.Shape, 0, maxDrop: 1);

    /// <summary>
    /// Exult <c>Npc_proximity_handler</c>'s trick for a woken sleeper: stand
    /// (but stay in the sleep schedule, the bed left unmade) and lie down again 10 s later.
    /// </summary>
    public void WakeUp()
    {
        _interrupted = true;
        Ending(ScheduleType.Stand);
        Brain.StopAction();
        Brain.StepTimer = 10;
    }

    bool IsBedOccupied(U7Object bed) => IsBedOccupied(Map, bed, Npc);

    /// <summary>Exult <c>Sleep_schedule::is_bed_occupied</c>: someone other than <paramref name="npc"/> lies on it.</summary>
    public static bool IsBedOccupied(GameMap map, U7Object bed, U7Object npc)
    {
        var floor = bed.Tz / 5;
        foreach (var other in map.FindNearby(Here(bed), U7Constants.AnyShape, 2, 8))
        {
            if (other != npc && other.IsActor && bed.Occupies(other.Tx, other.Ty) && other.Tz / 5 == floor)
            {
                return true;
            }
        }

        return false;
    }
}
