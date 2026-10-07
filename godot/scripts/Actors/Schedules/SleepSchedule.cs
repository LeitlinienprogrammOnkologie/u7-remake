using U7.Core;
using U7.Data;

namespace U7.Actors;

/// <summary>
/// Exult <c>Sleep_schedule</c>: walk to the bed spot, lie down in the nearest
/// free bed (unmaking it), and on leaving make the bed and step back onto
/// the floor.
/// </summary>
public sealed class SleepSchedule(NpcBrain brain) : Schedule(brain)
{
    // Exult Sleep_schedule: BG bedspread frames 3..16; even = spread out.
    const int Spread0 = 3;
    const int Spread1 = 16;
    static readonly int[] BedShapes = [696, 1011];

    U7Object? _bed;
    TileCoord _floorLoc;
    int _failures;

    public override void Begin() => LieInBed();

    public override void NowWhat()
    {
        if (AtDest(3) || _failures >= 2)
        {
            if (!AtDest(3))
            {
                Runner.Teleport(Brain, Brain.Dest);
            }

            LieInBed();
            Brain.Pause = 2;
            return;
        }

        // Exult Sleep_schedule walks to the bed at 200 ms a step.
        var walk = PathWalk.Astar(Map, Npc, Brain.Dest, dist: 1);
        _failures = walk is null ? _failures + 1 : 0;
        StartAction(walk, 200, 0);
    }

    public override void Ending(int newType)
    {
        if (newType is not (ScheduleType.Sleep or ScheduleType.Wait))
        {
            GetUp(newType);
        }
    }

    /// <summary>
    /// Exult <c>Npc_proximity_handler</c>'s trick for a woken sleeper: stand
    /// (but stay in the sleep schedule) and lie down again 10 s later.
    /// </summary>
    public void WakeUp()
    {
        GetUp(ScheduleType.Stand);
        Brain.StopAction();
        Brain.StepTimer = 10;
    }

    /// <summary>
    /// Exult <c>Sleep_schedule::now_what</c> state 1: pick the nearest free bed,
    /// unmake it, and put the NPC on top of it (bed lift + bed height) in the
    /// sleep frame facing west for EW beds, north for NS beds. With no bed
    /// nearby the NPC just lies down where it is.
    /// </summary>
    void LieInBed()
    {
        if (_bed is { Removed: false } && (Npc.Frame & 0xf) == ActorWalker.SleepFrame)
        {
            return; // Already in bed.
        }

        var here = Here(Npc);
        U7Object? bed = null;
        var best = int.MaxValue;
        foreach (var shape in BedShapes)
        {
            foreach (var cand in Map.FindNearby(here, shape, 24))
            {
                var d = here.Distance2d(Here(cand));
                if (d < best && !IsBedOccupied(cand))
                {
                    best = d;
                    bed = cand;
                }
            }
        }

        if (bed is null)
        {
            ActorWalker.Sleep(Npc, Map.Catalog);
            return;
        }

        // Prefer the sheet object on the same floor if the bed is a stack.
        var floor = bed.Tz / 5;
        foreach (var top in Map.FindNearby(Here(bed), bed.Shape, 1))
        {
            if (top.Frame >= Spread0 && top.Frame <= Spread1 && top.Tz / 5 == floor)
            {
                bed = top;
                break;
            }
        }

        _bed = bed;
        _floorLoc = new TileCoord(Npc.Tx, Npc.Ty, Npc.Tz - Npc.Tz % 5);
        var bedframe = bed.Frame;
        if (bedframe >= Spread0 && bedframe < Spread1 && bedframe % 2 == 1)
        {
            bed.Frame = ++bedframe; // Unmake the bed.
        }

        var bedspread = bedframe >= Spread0 && bedframe % 2 == 0;
        var height = Map.Catalog[Npc.Shape].DimZ;
        var delta = height < 4 ? height - 4 : 0;
        var bedHeight = Map.Catalog[bed.Shape].DimZ;
        Map.MoveObject(Npc, bed.Tx + delta, bed.Ty + delta, bed.Tz + (bedspread ? 0 : bedHeight));
        var band = bed.Shape == 696 ? 32 : 0; // West for EW beds, north for NS.
        var count = Map.Catalog[Npc.Shape].FrameCount;
        Npc.Frame = ActorWalker.SleepFrame < count ? ActorWalker.SleepFrame + band : band;
        Npc.WalkFrameIndex = 0;
    }

    /// <summary>Exult <c>Sleep_schedule::ending</c>: make the bed and step back onto the floor.</summary>
    void GetUp(int newType)
    {
        if (_bed is not { } bed)
        {
            return;
        }

        _bed = null;
        if (bed.Removed || (Npc.Frame & 0xf) != ActorWalker.SleepFrame || Here(Npc).Distance2d(Here(bed)) >= 8)
        {
            return;
        }

        if (newType != ScheduleType.Combat && bed.Frame >= Spread0 && bed.Frame <= Spread1 &&
            bed.Frame % 2 == 0 && !IsBedOccupied(bed))
        {
            bed.Frame--; // Make the bed.
        }

        var pos = Map.FindSpot(_floorLoc.Tx, _floorLoc.Ty, _floorLoc.Tz, 6) ?? _floorLoc;
        Map.MoveObject(Npc, pos.Tx, pos.Ty, pos.Tz);
        ActorWalker.Stand(Npc, 4);
    }

    /// <summary>Exult <c>Sleep_schedule::is_bed_occupied</c>.</summary>
    bool IsBedOccupied(U7Object bed)
    {
        var floor = bed.Tz / 5;
        foreach (var other in Map.FindNearby(Here(bed), U7Constants.AnyShape, 2, 8))
        {
            if (other != Npc && other.IsActor && bed.Occupies(other.Tx, other.Ty) && other.Tz / 5 == floor)
            {
                return true;
            }
        }

        return false;
    }
}
