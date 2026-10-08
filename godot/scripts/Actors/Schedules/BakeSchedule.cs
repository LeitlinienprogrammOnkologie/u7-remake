using U7.Core;
using U7.Data;

namespace U7.Actors;

/// <summary>
/// Exult <c>Bake_schedule</c>: the baker's round. Take flour from a sack
/// (863), knead dough on the work table (658: flour, flat, ball; quality 50),
/// put it in the oven (831, or on the stove 664; quality 51), take it out
/// baked (food 377) and set it out on the display table, clearing the table
/// of its own bread when it is full. Leftover dough and food are found again
/// after a break; the dough goes when the schedule does.
/// </summary>
public sealed class BakeSchedule(NpcBrain brain) : Schedule(brain)
{
    const int DoughShape = 658;
    const int FlourShape = 863;
    const int OvenShape = 831;
    const int StoveShape = 664;
    const int DoughQuality = 50;
    const int InOvenQuality = 51;

    enum State
    {
        FindLeftovers,
        ToFlour,
        GetFlour,
        ToTable,
        MakeDough,
        RemoveFromOven,
        DisplayWares,
        ClearDisplay,
        RemoveFood,
        GetDough,
        PutInOven
    }

    State _state;
    U7Object? _dough;
    U7Object? _doughInOven;
    U7Object? _flourbag;
    bool _clearing;

    static U7Object? Valid(U7Object? obj) => obj is { Removed: false } ? obj : null;

    U7Object? Closest(int shape) => FindClosest([shape], 24).FirstOrDefault();

    /// <summary>Exult <c>Actor::find_nearby</c> with a quality, frame and okay-to-take filter.</summary>
    List<U7Object> Nearby(TileCoord pos, int shape, int dist, int qual = U7Constants.AnyShape,
        int frame = U7Constants.AnyShape, bool excludeOkayToTake = false) =>
        Map.FindNearby(pos, shape, dist)
            .Where(o => (qual == U7Constants.AnyShape || o.Quality == qual) &&
                        (frame == U7Constants.AnyShape || o.Frame == frame) &&
                        !(excludeOkayToTake && o.GetFlag(ObjFlag.OkayToTake)))
            .ToList();

    /// <summary>Exult <c>Actor_pathfinder_client(npc, dist)</c> with <c>create_path</c>.</summary>
    PathWalk? PathTo(TileCoord dest, int dist) => PathWalk.Astar(Map, Npc, dest, dist);

    public override void NowWhat()
    {
        var npcpos = Here(Npc);
        var delay = 100;
        var stove = Closest(StoveShape);
        // Exult's guess; seems quite common.
        if (TryProximityUsecode(8))
        {
            return;
        }

        switch (_state)
        {
            case State.FindLeftovers:
                FindLeftovers(npcpos, stove, ref delay);
                break;
            case State.ToFlour:
            {
                // A flour sack, and walk to it.
                var sacks = new[] { 0, 13, 14 }.SelectMany(frame => Nearby(npcpos, FlourShape, 24, frame: frame)).ToList();
                if (sacks.Count == 0)
                {
                    _state = State.ToTable;
                    break;
                }

                var sack = sacks[Rng.Next(sacks.Count)];
                _flourbag = sack;
                if (PathTo(ObjectGeometry.Tile(sack), 1) is not { } walk)
                {
                    _state = State.ToTable; // Just ignore it.
                    break;
                }

                SetAction(walk);
                _state = State.GetFlour;
                break;
            }
            case State.GetFlour:
            {
                // Bend over the sack, and open it if it isn't.
                if (Valid(_flourbag) is not { } sack)
                {
                    _state = State.ToFlour;
                    break;
                }

                Npc.Frame = ActorWalker.DirFrame(ObjectGeometry.Direction(Npc, sack), ActorWalker.BowFrame);
                if (sack.Frame != 0)
                {
                    sack.Frame = 0;
                }

                delay = 750;
                _state = State.ToTable;
                break;
            }
            case State.ToTable:
                ToTable(npcpos, stove, ref delay);
                break;
            case State.MakeDough:
            {
                // Flour to flat dough, then a dough ball.
                if (Valid(_dough) is not { } dough)
                {
                    delay = 2500; // Better try again.
                    _state = State.ToTable;
                    break;
                }

                dough.ClearFlag(ObjFlag.OkayToTake);
                var dir = ObjectGeometry.Direction(Npc, dough);
                int[] knead = [ActorWalker.DirFrame(dir, 3), ActorWalker.DirFrame(dir, 0)];
                if (dough.Frame == 0)
                {
                    SetAction(new SequenceAction(100, new FramesAction(knead, 500), new FramesAction([1], 250, dough),
                        new FramesAction(knead, 500), new FramesAction([2], 250, dough)));
                }
                else if (dough.Frame == 1)
                {
                    SetAction(new SequenceAction(100, new FramesAction(knead, 500), new FramesAction([2], 250, dough)));
                }

                _state = State.RemoveFromOven;
                break;
            }
            case State.RemoveFromOven:
            {
                // The baked goods: food, taken out of the oven.
                if (Valid(_doughInOven) is not { } baked)
                {
                    _state = State.GetDough; // Nothing in the oven yet.
                    break;
                }

                if ((stove ?? Closest(OvenShape)) is not { } oven)
                {
                    Map.RemoveObject(baked); // This really shouldn't happen.
                    delay = 2500;
                    _state = State.ToTable;
                    break;
                }

                if (baked.Shape != 377)
                {
                    Map.SetShape(baked, 377);
                    baked.Frame = Rng.Next(7);
                    baked.ClearFlag(ObjFlag.OkayToTake);
                }

                var tpos = new TileCoord(oven.Tx + 1, oven.Ty + 1, oven.Tz).Wrapped();
                SetAction(PathTo(tpos, 1) is { } walk
                    ? new SequenceAction(100, walk, new PickupAction(Map, baked, 250))
                    : new PickupAction(Map, baked, 250)); // Else just pick it up.
                _state = State.DisplayWares;
                break;
            }
            case State.DisplayWares:
                DisplayWares(npcpos, stove, ref delay);
                break;
            case State.ClearDisplay:
            {
                // Mark food for removal.
                var food = Nearby(npcpos, 377, 4, excludeOkayToTake: true);
                if (food.Count == 0 && !_clearing)
                {
                    // None of our food on the table, so we can't clear it.
                    if (Valid(_doughInOven) is { } baked)
                    {
                        Map.RemoveObject(baked);
                    }

                    _state = State.GetDough;
                    break;
                }

                _clearing = true;
                if (food.Count > 0)
                {
                    delay = 500;
                    _state = State.RemoveFood;
                }
                else
                {
                    _state = State.DisplayWares;
                }

                break;
            }
            case State.RemoveFood:
            {
                // One by one, with a slight delay.
                var food = Nearby(npcpos, 377, 4, excludeOkayToTake: true);
                if (food.Count > 0)
                {
                    delay = 500;
                    _state = State.ClearDisplay;
                    Map.RemoveObject(food[0]);
                }

                break;
            }
            case State.GetDough:
            {
                // Walk to the work table and pick up the dough.
                if (Valid(_dough) is not { } dough || (stove ?? Closest(OvenShape)) is null)
                {
                    delay = 2500; // Try again; wait a while.
                    _state = State.FindLeftovers;
                    break;
                }

                SetAction(PathTo(ObjectGeometry.Tile(dough), 2) is { } walk
                    ? new SequenceAction(100, walk, new PickupAction(Map, dough, 250))
                    : new PickupAction(Map, dough, 250));
                _state = State.PutInOven;
                break;
            }
            case State.PutInOven:
                PutInOven(stove, ref delay);
                break;
        }

        Start(250, delay);
    }

    /// <summary>Exult's find_leftovers: dough baking or bread left in the oven, or dough left on a table.</summary>
    void FindLeftovers(TileCoord npcpos, U7Object? stove, ref int delay)
    {
        _state = State.ToFlour;
        var dough = Valid(_dough);
        var inOven = Valid(_doughInOven);
        if (inOven is null)
        {
            // Baking dough?
            if (Nearby(npcpos, DoughShape, 20, InOvenQuality, 2) is [var baking, ..] && baking != dough)
            {
                baking.ClearFlag(ObjFlag.OkayToTake);
                _doughInOven = baking;
                _state = State.RemoveFromOven;
                return;
            }

            // Cooked food left in the oven?
            if ((Closest(OvenShape) ?? stove) is { } oven &&
                Nearby(ObjectGeometry.Tile(oven), 377, 2, excludeOkayToTake: true) is [var food, ..])
            {
                food.ClearFlag(ObjFlag.OkayToTake);
                _doughInOven = food;
                _state = State.RemoveFromOven;
                return;
            }
        }

        if (dough is null && Nearby(npcpos, DoughShape, 20, DoughQuality) is [var leftover, ..] &&
            leftover != Valid(_doughInOven))
        {
            // Unused dough on a table: walk to it if we can.
            leftover.ClearFlag(ObjFlag.OkayToTake);
            _dough = leftover;
            _state = State.MakeDough;
            delay = 0;
            if (PathTo(ObjectGeometry.Tile(leftover), 2) is { } walk)
            {
                SetAction(walk);
            }
        }
    }

    /// <summary>Exult's to_table: walk to a work table and put new flour (dough frame 0) on it.</summary>
    void ToTable(TileCoord npcpos, U7Object? stove, ref int delay)
    {
        var table1 = Closest(1003);
        var table2 = Closest(1018);
        U7Object? worktable;
        if (stove is not null)
        {
            var tables = Nearby(npcpos, 890, 24, frame: 5);
            worktable = tables.Count switch
            {
                0 => null,
                1 => tables[0],
                _ => tables[Rng.Next(2) != 0 ? 0 : 1]
            };
        }
        else if (table1 is null)
        {
            worktable = table2;
        }
        else if (table2 is null)
        {
            worktable = table1;
        }
        else
        {
            worktable = ObjectGeometry.Distance(table1, Npc) < ObjectGeometry.Distance(table2, Npc) ? table1 : table2;
        }

        worktable ??= Closest(1018);
        if (worktable is null)
        {
            delay = 2500; // Problem... try again in a few seconds.
            _state = State.ToFlour;
            return;
        }

        // Where to put the dough; Exult walks to that tile at lift 0.
        var (x, y, w, h) = ObjectGeometry.Footprint(worktable);
        var tablepos = new TileCoord(x + Rng.Next(w), y + Rng.Next(h),
            worktable.Tz + Map.Catalog[worktable.Shape].DimZ);
        if (PathTo(tablepos with { Tz = 0 }, 1) is not { } walk)
        {
            delay = 2500; // Not good... try again.
            _state = State.ToFlour;
            return;
        }

        if (Valid(_dough) is { } old)
        {
            Map.RemoveObject(old);
        }

        var dough = Map.CreateIregObject(DoughShape, 0);
        Equipment.AddToActor(Npc, dough, Map.Catalog, Map, dontCheck: true);
        dough.Quality = DoughQuality;
        _dough = dough;
        SetAction(new SequenceAction(100, walk, new PickupAction(Map, dough, tablepos, 250, temporary: false)));
        _state = State.MakeDough;
    }

    /// <summary>
    /// Exult's display_wares: to a spot round the display table, setting the
    /// bread down on it if that spot is free, else off to clear the table.
    /// </summary>
    void DisplayWares(TileCoord npcpos, U7Object? stove, ref int delay)
    {
        if (Valid(_doughInOven) is not { } baked)
        {
            delay = 2500; // Try again.
            _state = State.FindLeftovers;
            return;
        }

        var display = Closest(633); // Britain.
        if (display is null)
        {
            // Moonshade's table, or by a stove, a lab table.
            var tables = stove is not null ? Nearby(npcpos, 1003, 24, frame: 2) : Nearby(npcpos, 890, 24, frame: 1);
            display = tables.Count switch
            {
                0 => null,
                1 => tables[0],
                _ => tables[Rng.Next(2) != 0 ? 0 : 1]
            };
        }

        if (display is null)
        {
            Map.RemoveObject(baked); // Uh-oh...
            delay = 2500;
            _state = State.FindLeftovers;
            return;
        }

        var (x, y, w, h) = ObjectGeometry.Footprint(display);
        LabSchedule.PerimeterTile(x, y, w, h, Rng.Next(2 * w + 2 * h + 4), out var spot, out var spotOnTable);
        var walk = PathTo(spot, 2);
        // (Exult's perimeter tiles are at lift 0.)
        spotOnTable = spotOnTable with { Tz = spotOnTable.Tz + Map.Catalog[display.Shape].DimZ };
        // Set the bread down if the spot is free.
        if (Map.FindSpot(spotOnTable, 0, 377, 0) is { } t && t.Tz == spotOnTable.Tz)
        {
            // (Exult's sequence does nothing without a path.)
            if (walk is not null)
            {
                SetAction(new SequenceAction(100, walk, new PickupAction(Map, baked, spotOnTable, 250, temporary: false)));
            }

            _doughInOven = null;
            _state = State.GetDough;
        }
        else
        {
            if (PathTo(ObjectGeometry.Tile(display), 1) is { } toTable)
            {
                SetAction(new SequenceAction(100, toTable, new FacePosAction(ObjectGeometry.Tile(display), 250)));
            }

            delay = 250;
            _state = State.ClearDisplay;
        }

        _clearing = false;
    }

    /// <summary>Exult's put_in_oven: walk to the oven and put the dough in (on a stove, hidden behind it).</summary>
    void PutInOven(U7Object? stove, ref int delay)
    {
        _state = State.FindLeftovers;
        if (Valid(_dough) is not { } dough)
        {
            delay = 2500; // Try again.
            return;
        }

        if ((stove ?? Closest(OvenShape)) is not { } oven)
        {
            Map.RemoveObject(dough); // Oops... retry.
            delay = 2500;
            _state = State.ToTable;
            return;
        }

        var tpos = new TileCoord(oven.Tx + 1, oven.Ty + 1, oven.Tz).Wrapped();
        var (offX, offY, offZ) = stove is not null ? (-3, 0, -2) : (1, 0, 0);
        var (x, y, _, _) = ObjectGeometry.Footprint(oven);
        var cpos = new TileCoord(x + offX, y + offY, oven.Tz + Map.Catalog[oven.Shape].DimZ + offZ);
        if (PathTo(tpos, 1) is { } walk)
        {
            SetAction(new SequenceAction(100, walk, new PickupAction(Map, dough, cpos, 250, temporary: false)));
            dough.Quality = InOvenQuality;
            _doughInOven = dough;
            _dough = null;
        }
        else
        {
            Map.RemoveObject(dough);
        }
    }

    /// <summary>Exult: the dough goes, and what is in the oven.</summary>
    public override void Ending(int newType)
    {
        if (Valid(_dough) is { } dough)
        {
            Map.RemoveObject(dough);
        }

        if (Valid(_doughInOven) is { } baked)
        {
            Map.RemoveObject(baked);
        }
    }
}
