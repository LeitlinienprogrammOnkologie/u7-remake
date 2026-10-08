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

    /// <summary>Exult <c>SleepUsecode</c>: what a nap in bed runs (BG 0x622: the hours, the fade, the dreams).</summary>
    const int SleepUsecode = 0x622;

    U7Object? _bed;
    TileCoord _floorLoc;
    int _failures;
    /// <summary>Exult <c>Sleep_schedule::set_bed</c> (<c>nap_time</c>): the bed the avatar goes to sleep in.</summary>
    public U7Object? NapBed { get; init; }
    int _napState;

    public override void Begin()
    {
        if (NapBed is null)
        {
            LieInBed();
        }
    }

    public override void NowWhat()
    {
        if (NapBed is { } nap)
        {
            Nap(nap);
            return;
        }

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
    /// Exult <c>Sleep_schedule::now_what</c> after <c>set_bed</c>: walk to
    /// within 3 tiles of the bed (state 0), then lie in it, asleep, and run
    /// the sleep usecode on the bed (state 1, <c>for_nap_time</c>).
    /// </summary>
    void Nap(U7Object bed)
    {
        if (_napState == 2 || bed.Removed)
        {
            return;
        }

        if (_napState == 0)
        {
            _napState = 1;
            bed = TopOf(bed);
            var info = Map.Catalog[bed.Shape];
            var bloc = new TileCoord(bed.Tx - info.DimX / 2, bed.Ty - info.DimY / 2, bed.Tz - bed.Tz % 5);
            StartAction(PathWalk.Astar(Map, Npc, bloc, dist: 3), 200, 0);
            return;
        }

        // (Exult tries the walk again when it ended more than 3 tiles off, but lies down all the same.)
        _napState = 2;
        LieIn(TopOf(bed));
        Npc.SetFlag(ObjFlag.Asleep);
        UsecodeAction.Call?.Invoke(SleepUsecode, _bed!, 1); // double-click
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

        LieIn(TopOf(bed));
    }

    /// <summary>Unmake the bed and lie on top of it (bed lift + bed height), facing west in EW beds, north in NS beds.</summary>
    void LieIn(U7Object bed)
    {
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
