using U7.Core;
using U7.Data;

namespace U7.Actors;

/// <summary>
/// Exult <c>Waiter_schedule</c>: serving at an inn. The waiter takes the
/// orders of the diners nearby (NPCs eating at the inn), up to four at a
/// time, setting a plate for those without one; cooks at a prep table, stove
/// or cauldron; brings each their food; and in between waits at the counter,
/// clears plates the diners have left, and moves cups, bottles and pots about.
/// </summary>
public sealed class WaiterSchedule(NpcBrain brain) : ScheduleWithObjects(brain)
{
    // Exult waiter_shapes: bottle, cup, pot.
    static readonly int[] WaiterShapes = [616, 628, 944];
    const int PotShape = 944;
    // Exult: tables of these shapes within 24 tiles; those with chairs by them are eaten at.
    static readonly int[] TableShapes = [971, 633, 847, 890, 964];
    const int CounterShape = 847;
    // Exult find_prep_tables: tables, stoves, the cauldron.
    static readonly int[] PrepShapes = [333, 1018, 1003, 664, 872, 995];
    const int CauldronShape = 995;
    static readonly int[] StoveShapes = [664, 872];

    enum State
    {
        Setup,
        GetCustomer,
        GetOrder,
        TookOrder,
        GivePlate,
        PrepFood,
        BringFood,
        ServeFood,
        ServedFood,
        WaitAtCounter,
        GetWaiterItem,
        PickedUpItem,
        WalkToCleanupFood,
        CleanupFood
    }

    State _state;
    readonly TileCoord _startPos = Here(brain.Npc);
    U7Object? _customer;
    /// <summary>The table worked at: a prep table, or the counter waited at.</summary>
    U7Object? _prepTable;
    bool _cooking;
    readonly List<U7Object> _customers = new();
    /// <summary>Those whose orders were taken.</summary>
    readonly List<U7Object> _customersOrdered = new();
    readonly List<U7Object> _prepTables = new();
    /// <summary>Places to hang out.</summary>
    readonly List<U7Object> _counters = new();
    /// <summary>Tables with chairs around them.</summary>
    readonly List<U7Object> _eatingTables = new();
    readonly List<U7Object> _unattendedPlates = new();

    const int Std = U7Constants.StandardDelayMs;

    protected override List<U7Object> FindItems(int dist)
    {
        var here = Here(Npc);
        var items = new List<U7Object>();
        if (_cooking) // Find food, pot.
        {
            items.AddRange(Map.FindNearby(here, FoodShape, dist));
            items.AddRange(Map.FindNearby(here, PotShape, dist));
        }

        if (!_cooking || items.Count == 0)
        {
            foreach (var shape in WaiterShapes)
            {
                items.AddRange(Map.FindNearby(here, shape, dist));
            }
        }

        var floor = Npc.Tz / 5; // Make sure it's on the same floor.
        items.RemoveAll(item => item.Tz / 5 != floor);
        return items;
    }

    /// <summary>Exult <c>Get_waiter_objects</c>: the bottles, cups and pots it carries; cooking, its food and pots first.</summary>
    List<U7Object> WaiterObjects(bool cooking = false)
    {
        var items = new List<U7Object>();
        if (cooking)
        {
            items.AddRange(Carried(FoodShape));
            items.AddRange(Carried(PotShape));
        }

        if (!cooking || items.Count == 0)
        {
            foreach (var shape in WaiterShapes)
            {
                items.AddRange(Carried(shape));
            }
        }

        return items;
    }

    /// <summary>Exult <c>find_unattended_plate</c>: plates it set (temporary) with nobody by them, closest first.</summary>
    bool FindUnattendedPlate()
    {
        _unattendedPlates.RemoveAll(plate => plate.Removed);
        if (_unattendedPlates.Count == 0)
        {
            var floor = Npc.Tz / 5;
            foreach (var plate in FindClosest([PlateShape], 32))
            {
                if (plate.Tz / 5 == floor && plate.GetFlag(ObjFlag.Temporary) &&
                    Map.FindNearby(Here(plate), U7Constants.AnyShape, 2, 8).Count == 0)
                {
                    _unattendedPlates.Add(plate);
                }
            }
        }

        return _unattendedPlates.Count > 0;
    }

    /// <summary>Exult <c>find_customer</c>: the next of those eating at the inn within 32 tiles, looked for again when all are served.</summary>
    bool FindCustomer()
    {
        if (_customers.Count == 0) // Got to search?
        {
            _customers.AddRange(Map.FindNearby(Here(Npc), U7Constants.AnyShape, 32, 8)
                .Where(actor => actor.ScheduleType == ScheduleType.EatAtInn));
        }

        _customer = null;
        if (_customers.Count > 0)
        {
            _customer = _customers[^1];
            _customers.RemoveAt(_customers.Count - 1);
        }

        return _customer is not null;
    }

    /// <summary>
    /// Exult <c>walk_to_work_spot</c>: to a free spot by a random prep table
    /// or counter; with none to be reached, a short walk about where the
    /// schedule began, and false.
    /// </summary>
    bool WalkToWorkSpot(bool counter)
    {
        var tables = counter ? _counters : _prepTables;
        while (tables.Count > 0)
        {
            var index = Rng.Next(tables.Count);
            _prepTable = tables[index];
            if (!_prepTable.Removed && Map.FindSpot(Here(_prepTable), 1, Npc) is { } pos &&
                WalkPathTo(pos, Std, 1000 + Rng.Next(1000)))
            {
                return true;
            }

            tables.RemoveAt(index); // Failed, so remove this table from the list.
        }

        _prepTable = null;
        const int dist = 8; // Bad luck? Walk randomly.
        var dest = new TileCoord(_startPos.Tx - dist + Rng.Next(2 * dist), _startPos.Ty - dist + Rng.Next(2 * dist),
            _startPos.Tz).Wrapped();
        StartAction(PathWalk.Line(Map, Npc, dest), 2 * Std, Rng.Next(2000));
        return false;
    }

    bool WalkToPrep() => WalkToWorkSpot(false);

    bool WalkToCounter() => WalkToWorkSpot(true);

    /// <summary>Exult <c>walk_to_customer</c>: to a free spot within 3 of the customer; if not, look again in 2-6 s.</summary>
    bool WalkToCustomer(int minDelay = 0)
    {
        if (_customer is { } customer)
        {
            if (customer.ScheduleType != ScheduleType.EatAtInn)
            {
                // The customer's schedule changed: get a new list.
                _customers.Clear();
            }
            else if (Map.FindSpot(Here(customer), 3, Npc) is { } dest &&
                     WalkPathTo(dest, Std, minDelay + Rng.Next(1000)))
            {
                return true; // Walking there.
            }
        }

        Start(200, 2000 + Rng.Next(4000)); // Failed so try again later.
        return false;
    }

    /// <summary>Exult <c>find_tables</c>: sort the tables of a shape on this floor into eating tables (chairs within 3), counters and prep tables.</summary>
    void FindTables(int shape, int dist, bool isPrep = false)
    {
        var floor = Npc.Tz / 5;
        foreach (var table in Map.FindNearby(Here(Npc), shape, dist))
        {
            if (table.Tz / 5 != floor)
            {
                continue;
            }

            var here = Here(table);
            // No chairs by it? (Exult: "TODO: check for nearby stove.")
            if (SitSchedule.ChairShapes.All(chair => Map.FindNearby(here, chair, 3).Count == 0))
            {
                if (isPrep)
                {
                    _prepTables.Add(table);
                }
                else if (shape == CounterShape)
                {
                    _counters.Add(table);
                }
            }
            else
            {
                _eatingTables.Add(table);
            }
        }
    }

    /// <summary>Exult <c>find_prep_tables</c>: within 26 tiles, else 36, else 50.</summary>
    void FindPrepTables()
    {
        foreach (var dist in new[] { 26, 36, 50 })
        {
            foreach (var shape in PrepShapes)
            {
                FindTables(shape, dist, isPrep: true);
            }

            if (_prepTables.Count > 0)
            {
                break;
            }
        }
    }

    /// <summary>Exult <c>Find_customer_table</c>: an eating table within 2 of the customer.</summary>
    U7Object? FindCustomerTable(U7Object customer) =>
        _eatingTables.FirstOrDefault(table => !table.Removed && ObjectGeometry.Distance(customer, table) < 3);

    /// <summary>Exult <c>Find_customer_plate</c>: a plate within a tile of the customer, on a table on its floor.</summary>
    U7Object? FindCustomerPlate(U7Object customer)
    {
        var floor = customer.Tz / 5;
        return Map.FindNearby(Here(customer), PlateShape, 1)
            .FirstOrDefault(plate => plate.Tz / 5 == floor && plate.Tz % 5 != 0);
    }

    /// <summary>
    /// Exult <c>create_customer_plate</c>: a new small plate (frame 4 or 5,
    /// temporary) on the edge of the customer's table in front of them.
    /// </summary>
    U7Object? CreateCustomerPlate()
    {
        if (_customer is not { } customer)
        {
            return null;
        }

        int cx = customer.Tx, cy = customer.Ty;
        _eatingTables.RemoveAll(table => table.Removed);
        foreach (var table in _eatingTables)
        {
            if (ObjectGeometry.FootprintDistance(table, cx, cy) > 2)
            {
                continue;
            }

            // Found it.
            var (fx, fy, fw, fh) = ObjectGeometry.Footprint(table);
            int sx = cx, sy = cy;
            if (cy >= fy && cy < fy + fh)
            {
                sx = cx <= fx ? fx : fx + fw - 1; // East or west of the table.
            }
            else
            {
                sy = cy <= fy ? fy : fy + fh - 1; // North or south.
            }

            if (ObjectGeometry.InFootprint(table, sx, sy))
            {
                // Small plates: frames 4, 5. Seems random.
                var plate = Map.CreateIregObject(PlateShape, 4 + Rng.Next(2));
                plate.SetFlag(ObjFlag.Temporary);
                Map.PlaceInWorld(plate, sx, sy, table.Tz + Map.Catalog[table.Shape].DimZ);
                return plate;
            }
        }

        return null; // Failed.
    }

    /// <summary>
    /// Exult <c>Ready_food</c>: food in the left hand: its own, else some
    /// made up (temporary, not to be taken). What was in the hand goes back
    /// in its inventory.
    /// </summary>
    void ReadyFood()
    {
        var held = Equipment.GetReadied(Npc, ReadySpot.Lhand);
        if (held is { Shape: FoodShape })
        {
            return; // Food, so done.
        }

        if (held is not null)
        {
            Map.TakeFromWorld(held); // Make space.
        }

        U7Object food;
        if (Carried(FoodShape) is [var own, ..])
        {
            food = own; // Already have one, so just move it.
            Map.TakeFromWorld(food);
        }
        else
        {
            // Acquire some food.
            food = Map.CreateIregObject(FoodShape, Rng.Next(Math.Max(1, Map.Catalog[FoodShape].FrameCount)));
            food.SetFlag(ObjFlag.Temporary);
            food.ClearFlag(ObjFlag.OkayToTake);
        }

        Equipment.AddReadied(Npc, food, ReadySpot.Lhand, Map.Catalog, Map, forcePos: true);
        if (held is not null)
        {
            GiveToNpc(held); // Add back what was there before.
        }
    }

    /// <summary>Exult <c>find_serving_spot</c>: just above the customer's plate, setting a plate if there is none.</summary>
    TileCoord? FindServingSpot(U7Object customer)
    {
        var plate = FindCustomerPlate(customer) ?? CreateCustomerPlate();
        return plate is null ? null : new TileCoord(plate.Tx, plate.Ty, plate.Tz + 1);
    }

    /// <summary>Exult's script for setting something down: hand out, then stand.</summary>
    void HandOver() =>
        RunScript(ScriptFaceDir, ActorWalker.FacingOfFrame(Npc.Frame), ScriptReadyFrame, ScriptDelayTicks, 2,
            ScriptStandFrame);

    /// <summary>
    /// Exult <c>Prep_animation</c>: face the table and work at it a few
    /// strokes; a cauldron bubbles (frames 0 and 2), a stove flares (a
    /// random frame; Exult skips empty ones, the stoves have none).
    /// </summary>
    void PrepAnimation(U7Object table)
    {
        Npc.Frame = ActorWalker.DirFrame(ObjectGeometry.FacingDirection(Npc, table), 0);
        var script = new List<int> { ScriptFaceDir, ActorWalker.FacingOfFrame(Npc.Frame) };
        for (var cnt = 1 + Rng.Next(3); cnt > 0; --cnt)
        {
            script.AddRange([ScriptReadyFrame, ScriptDelayTicks, 1, ScriptRaise1Frame, ScriptDelayTicks, 1]);
        }

        script.Add(ScriptStandFrame);
        RunScript([.. script]);
        if (table.Shape == CauldronShape)
        {
            table.Frame = table.Frame == 0 ? 2 : 0;
        }
        else if (StoveShapes.Contains(table.Shape))
        {
            table.Frame = Rng.Next(Math.Max(1, Map.Catalog[table.Shape].FrameCount));
        }
    }

    public override void NowWhat()
    {
        // (Exult first, one time in 4 while waiting at the counter, looks for
        // street lamps and shutters to tend; not ported.)
        if (_state is State.GetOrder or State.ServeFood)
        {
            var dist = _customer is { } c ? ObjectGeometry.Distance(Npc, c) : 5000;
            if (dist > 32) // Need a new customer?
            {
                _state = State.GetCustomer;
                Start(200, 1000 + Rng.Next(1000));
                return;
            }

            if (dist >= 3 && !WalkToCustomer()) // Not close enough, so try again.
            {
                _state = State.GetCustomer;
                return;
            }
        }

        switch (_state)
        {
            case State.Setup:
                ItemsInHand = WaiterObjects().Count;
                foreach (var shape in TableShapes)
                {
                    FindTables(shape, 24);
                }

                FindPrepTables();
                _state = State.GetCustomer;
                goto case State.GetCustomer;
            case State.GetCustomer:
                if (!FindCustomer())
                {
                    WalkToPrep();
                    _state = State.PrepFood;
                }
                else if (WalkToCustomer())
                {
                    _state = State.GetOrder;
                }

                break;
            case State.GetOrder:
            {
                if (_customer is not { } customer)
                {
                    _state = State.GetCustomer;
                    Start(200, 1000);
                    break;
                }

                if (Map.FindNearby(Here(customer), FoodShape, 1).Count > 0)
                {
                    if (Rng.Next(4) != 0)
                    {
                        Say(TextMessages.FirstWaiterBanter, TextMessages.LastWaiterBanter);
                    }
                }
                else
                {
                    // Ask for the order.
                    Say(TextMessages.FirstWaiterAsk, TextMessages.LastWaiterAsk);
                    if (FindCustomerPlate(customer) is null)
                    {
                        _state = State.GivePlate;
                        Start(Std, 500 + Rng.Next(1000));
                        break;
                    }

                    _customersOrdered.Add(customer);
                }

                _state = State.TookOrder;
                goto case State.TookOrder;
            }
            case State.TookOrder:
                // Get up to 4 orders before serving them.
                if (_customersOrdered.Count >= 4 || _customers.Count == 0)
                {
                    WalkToPrep();
                    _state = State.PrepFood;
                }
                else
                {
                    _state = State.GetCustomer;
                    Start(Std, 500 + Rng.Next(1000));
                }

                break;
            case State.GivePlate:
                CreateCustomerPlate();
                HandOver();
                _state = State.TookOrder;
                if (_customer is { } ordered)
                {
                    _customersOrdered.Add(ordered);
                }

                Start(Std, 500 + Rng.Next(1000));
                break;
            case State.PrepFood:
            {
                if (_cooking && ItemsInHand < 4 && Rng.Next(2) == 0 && WalkToRandomItem(4))
                {
                    _state = State.GetWaiterItem;
                    break;
                }

                var prepTable = _prepTable is { Removed: false } t ? t : null;
                ReadyFood(); // So we can drop it.
                if ((!_cooking || Rng.Next(3) != 0) && prepTable is not null &&
                    ObjectGeometry.Distance(Npc, prepTable) <= 3)
                {
                    U7Object? toDrop = null;
                    if (Rng.Next(3) != 0)
                    {
                        var items = WaiterObjects(cooking: true);
                        ItemsInHand = items.Count;
                        toDrop = items.Count > 0 ? items[Rng.Next(items.Count)] : null;
                    }

                    _cooking = true;
                    if (toDrop is not null)
                    {
                        DropItem(toDrop, prepTable);
                    }
                    else
                    {
                        PrepAnimation(prepTable);
                    }

                    Start(Std, 500 + Rng.Next(500));
                    break;
                }

                if (Rng.Next(3) == 1 && _prepTables.Count > 1)
                {
                    WalkToPrep(); // A little more cooking.
                    break;
                }

                ReadyFood();
                _cooking = false;
                _state = State.BringFood;
                goto case State.BringFood;
            }
            case State.BringFood:
                if (_customersOrdered.Count == 0) // All done serving them?
                {
                    if (Rng.Next(3) == 0)
                    {
                        // Exult calls the NPC's npc_proximity usecode straight away; here it is queued.
                        Runner.ProximityUsecode?.Invoke(Npc);
                    }

                    _state = WalkToCounter() ? State.WaitAtCounter : State.GetCustomer;
                }
                else
                {
                    _customer = _customersOrdered[^1];
                    _customersOrdered.RemoveAt(_customersOrdered.Count - 1);
                    ReadyFood();
                    _state = WalkToCustomer(3000) ? State.ServeFood : State.GetCustomer;
                }

                break;
            case State.WaitAtCounter:
            {
                var counter = _prepTable is { Removed: false } t ? t : null;
                // Check for customers who have left.
                if (Rng.Next(2) != 0 && FindUnattendedPlate())
                {
                    _state = State.WalkToCleanupFood;
                }
                else if (ItemsInHand < 3 && Rng.Next(2) != 0 && WalkToRandomItem(12))
                {
                    _state = State.GetWaiterItem;
                    break;
                }
                else if (counter is not null)
                {
                    Npc.Frame = ActorWalker.DirFrame(ObjectGeometry.FacingDirection(Npc, counter), 0);
                    if (Rng.Next(2) != 0)
                    {
                        PrepAnimation(counter);
                    }

                    if (Rng.Next(3) == 1)
                    {
                        _state = State.GetCustomer;
                    }
                }
                else
                {
                    _state = State.GetCustomer;
                }

                Start(Std, 2000 + Rng.Next(2000));
                break;
            }
            case State.GetWaiterItem:
                if (CurrentItem is { } item && ObjectGeometry.Distance(Npc, item) <= 3)
                {
                    SetAction(new PickupAction(Map, item, 250));
                    _state = State.PickedUpItem;
                    Start(250, 500 + Rng.Next(1000));
                    ItemsInHand++;
                    break;
                }

                goto case State.PickedUpItem;
            case State.PickedUpItem:
                CurrentItem = null;
                if (_cooking)
                {
                    WalkToPrep();
                    _state = State.PrepFood;
                }
                else
                {
                    _state = WalkToCounter() ? State.WaitAtCounter : State.GetCustomer;
                }

                break;
            case State.ServeFood:
                if (Equipment.GetReadied(Npc, ReadySpot.Lhand) is { Shape: FoodShape } food &&
                    _customer is { } diner && FindServingSpot(diner) is { } spot)
                {
                    Npc.Frame = ActorWalker.DirFrame(ObjectGeometry.Direction(Npc, diner), 0);
                    Map.PlaceInWorld(food, spot.Tx, spot.Ty, spot.Tz);
                    if (Rng.Next(3) != 0)
                    {
                        Say(TextMessages.FirstWaiterServe, TextMessages.LastWaiterServe);
                    }

                    HandOver();
                }

                _state = State.ServedFood;
                Start(250, 1000 + Rng.Next(1000));
                break;
            case State.ServedFood:
                // Randomly drop or pick up an item.
                if (Rng.Next(2) != 0)
                {
                    if (_customer is { } served && ItemsInHand > 0)
                    {
                        var items = WaiterObjects();
                        ItemsInHand = items.Count;
                        if (items.Count > 0)
                        {
                            var toDrop = items[Rng.Next(items.Count)];
                            if (FindCustomerTable(served) is { } table)
                            {
                                DropItem(toDrop, table);
                            }
                        }
                    }
                }
                else if (ItemsInHand < 4)
                {
                    var items = FindItems(4); // Look near by to pick up an item.
                    if (items.Count > 0)
                    {
                        SetAction(new PickupAction(Map, items[Rng.Next(items.Count)], 250));
                        ItemsInHand++;
                    }
                }

                _customer = null; // Done with this one.
                _state = State.BringFood; // On to the next.
                Start(Std, 1000 + Rng.Next(1000));
                break;
            case State.WalkToCleanupFood:
                if (_unattendedPlates.Count > 0)
                {
                    var plate = _unattendedPlates[0]; // Closest.
                    if (!plate.Removed && WalkPathTo(Here(plate), Std, 500 + Rng.Next(1000), dist: 2))
                    {
                        _state = State.CleanupFood;
                        break;
                    }

                    _unattendedPlates.RemoveAt(0);
                    Start(Std, 0);
                }
                else
                {
                    _state = WalkToCounter() ? State.WaitAtCounter : State.GetCustomer;
                }

                break;
            case State.CleanupFood:
                if (_unattendedPlates.Count > 0)
                {
                    var plate = _unattendedPlates[0];
                    _unattendedPlates.RemoveAt(0);
                    if (plate.Removed)
                    {
                        _state = State.WalkToCleanupFood;
                        Start(Std, 500 + Rng.Next(1000));
                        break;
                    }

                    // Delete after picking up, the food on it too.
                    IActorAction act = new PickupAction(Map, plate, 250, delete: true);
                    var leftovers = Map.FindNearby(Here(plate), FoodShape, 2)
                        .OrderBy(f => Here(f).Distance(Here(plate))).FirstOrDefault();
                    if (leftovers is not null)
                    {
                        act = new SequenceAction(100, act, new PickupAction(Map, leftovers, 250, delete: true));
                    }

                    SetAction(act);
                    _state = State.WalkToCleanupFood;
                    Start(Std, 500 + Rng.Next(1000));
                }
                else
                {
                    _state = WalkToCounter() ? State.WaitAtCounter : State.GetCustomer;
                }

                break;
        }
    }

    /// <summary>Exult <c>Waiter_schedule::ending</c>: what it holds in its hands goes, then the items made for it.</summary>
    public override void Ending(int newType)
    {
        if (Equipment.GetReadied(Npc, ReadySpot.Lhand) is { } left)
        {
            Map.RemoveObject(left);
        }

        if (Equipment.GetReadied(Npc, ReadySpot.Rhand) is { } right)
        {
            Map.RemoveObject(right);
        }

        base.Ending(newType);
    }
}
