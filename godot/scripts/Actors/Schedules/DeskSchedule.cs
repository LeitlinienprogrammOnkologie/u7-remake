using U7.Core;
using U7.Data;

namespace U7.Actors;

/// <summary>
/// Exult <c>Desk_schedule</c>: desk work. The NPC sits at the desk nearest
/// it (shapes 283, 407) on the chair closest to the desk, and from time to
/// time gets up: to a table to put down or shuffle desk things (quills,
/// documents, inkwells; shape 675, created for it as needed), to fetch one
/// lying about, or just to stand and stretch.
/// </summary>
public sealed class DeskSchedule(NpcBrain brain) : ScheduleWithObjects(brain)
{
    const int DeskItemShape = 675;
    static readonly int[] Desks = [283, 407];
    // Exult: tables of these shapes within 16 tiles, desks too.
    static readonly int[] TableShapes = [890, 633, 1000, 283, 407];
    static readonly int[] DeskFrames = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 12, 13, 14, 15];

    enum State
    {
        Setup,
        SitAtDesk,
        GetDeskItem,
        PickedUpItem,
        WorkAtTable
    }

    State _state;
    readonly List<U7Object> _tables = new();
    U7Object? _chair;
    U7Object? _table;
    /// <summary>Exult's static count of non-document items, so documents do get made.</summary>
    static int _nonDocs;

    protected override List<U7Object> FindItems(int dist)
    {
        var floor = Npc.Tz / 5;
        return Map.FindNearby(Here(Npc), DeskItemShape, dist)
            .Where(item => item.Tz / 5 == floor && DeskFrames.Contains(item.Frame))
            .ToList();
    }

    /// <summary>Exult <c>Desk_item_frame</c>: a likely desk thing to create.</summary>
    int DeskItemFrame()
    {
        var n = _nonDocs >= 2 ? 6 : Rng.Next(10);
        ++_nonDocs;
        switch (n)
        {
            case 1:
                return new[] { 1, 14, 15 }[Rng.Next(3)]; // Quills.
            case 6:
                _nonDocs = 0;
                return new[] { 6, 13 }[Rng.Next(2)]; // Documents.
            case 7:
                return new[] { 7, 12 }[Rng.Next(2)]; // Wax.
            case 0: // Gavel.
            case 2: // Inkwell.
            case 3: // Quill holder.
            case 4: // Book mark.
            case 5: // Letter opener.
            case 8: // Seal.
            case 9: // Blotter.
                return n;
            default:
                return 0;
        }
    }

    void FindTables(int shape)
    {
        var floor = Npc.Tz / 5;
        _tables.AddRange(Map.FindNearby(Here(Npc), shape, 16).Where(t => t.Tz / 5 == floor));
    }

    /// <summary>Exult <c>walk_to_table</c>: to a free spot beside a random table; false if none can be reached.</summary>
    bool WalkToTable()
    {
        while (_tables.Count > 0)
        {
            var index = Rng.Next(_tables.Count);
            _table = _tables[index];
            if (_table is { Removed: false } table &&
                Map.FindSpot(Here(table), 1, Npc.Shape, Npc.Frame) is { } pos &&
                WalkPathTo(pos, U7Constants.StandardDelayMs, 1000 + Rng.Next(1000)))
            {
                return true;
            }

            _tables.RemoveAt(index);
        }

        _table = null;
        return false;
    }

    /// <summary>A base frame turned the way the NPC faces (Exult <c>get_dir_framenum(frnum)</c>).</summary>
    int Facing(int frame) => (frame & 0xf) + (Npc.Frame & 48);

    public override void NowWhat()
    {
        // (Exult first looks for street lamps and shutters to tend; not ported.)
        switch (_state)
        {
            case State.Setup:
                Setup();
                if (_state == State.Setup)
                {
                    return;
                }

                SitAtDesk();
                break;
            case State.SitAtDesk:
                SitAtDesk();
                break;
            case State.GetDeskItem:
                if (CurrentItem is { } item && ObjectGeometry.Distance(Npc, item) <= 3)
                {
                    // Turn to face it.
                    Npc.Frame = ActorWalker.DirFrame(ObjectGeometry.Direction(Npc, item), 0);
                    SetAction(new PickupAction(Map, item, 250));
                    _state = State.PickedUpItem;
                }
                else
                {
                    _state = State.SitAtDesk;
                }

                Start(250, 500 + Rng.Next(1000));
                break;
            case State.PickedUpItem:
                CurrentItem = null;
                if (Rng.Next(2) != 0 && WalkToTable())
                {
                    _state = State.WorkAtTable;
                    break;
                }

                _state = State.SitAtDesk;
                Start(250, 1000 + Rng.Next(500));
                break;
            case State.WorkAtTable:
                WorkAtTable();
                Start(250, 1000 + Rng.Next(500));
                break;
        }
    }

    void Setup()
    {
        if ((Npc.Frame & 0xf) != 0)
        {
            Npc.Frame = 0; // Exult Stand_up.
        }

        if (_tables.Count == 0)
        {
            foreach (var shape in TableShapes)
            {
                FindTables(shape);
            }
        }

        // Create desk items if needed.
        ItemsInHand = Carried(DeskItemShape).Count;
        var nitems = 7 + Rng.Next(5) - ItemsInHand - FindItems(16).Count;
        for (var i = 0; i < nitems; i++)
        {
            var item = Map.CreateIregObject(DeskItemShape, DeskItemFrame());
            GiveToNpc(item);
            AddObject(item);
            ItemsInHand++;
        }

        if (FindClosest(Desks, 24) is [var desk, ..])
        {
            var here = Here(desk);
            _chair = SitSchedule.ChairShapes.SelectMany(shape => Map.FindNearby(here, shape, 24))
                .OrderBy(c => Here(c).Distance(here)).FirstOrDefault();
        }

        if (_chair is null)
        {
            Start(200, 5000); // Failed; try again in a few seconds.
            return;
        }

        _state = State.SitAtDesk;
    }

    void SitAtDesk()
    {
        if ((Npc.Frame & 0xf) != ActorWalker.SitFrame)
        {
            if (_chair is not { Removed: false } chair || SitDown(chair, 0) is null)
            {
                _chair = null; // Look for any nearby chair.
                _state = State.Setup;
                Start(200, 5000);
            }
            else
            {
                Start(250, 0);
            }
        }
        else if (Rng.Next(1 + ItemsInHand) != 0 && WalkToTable())
        {
            _state = State.WorkAtTable;
        }
        else if (Rng.Next(2) != 0 && WalkToRandomItem())
        {
            _state = State.GetDeskItem;
        }
        else
        {
            // Stand up a second.
            SetAction(new FramesAction([
                Facing(0), Facing(ActorWalker.Reach1Frame), Facing(0), Facing(ActorWalker.BowFrame),
                Facing(ActorWalker.SitFrame)
            ]));
            Start(250, 3000 + Rng.Next(2000));
        }
    }

    void WorkAtTable()
    {
        var table = _table is { Removed: false } t ? t : null;
        if (table is not null)
        {
            // Turn to face it.
            Npc.Frame = ActorWalker.DirFrame(ObjectGeometry.FacingDirection(Npc, table), 0);
        }

        if (table is null || Rng.Next(3) == 0)
        {
            _state = State.SitAtDesk; // Back to the desk.
        }
        else if (ItemsInHand > 0 && Rng.Next(6) != 0)
        {
            // Put down an item.
            var items = Carried(DeskItemShape);
            ItemsInHand = items.Count;
            if (items.Count > 0)
            {
                if (DropItem(items[Rng.Next(items.Count)], table) && Rng.Next(2) != 0)
                {
                    _state = State.SitAtDesk;
                }
            }
            else
            {
                _state = State.SitAtDesk;
            }
        }
        else
        {
            var dir = ObjectGeometry.FacingDirection(Npc, table);
            SetAction(new SequenceAction(100, new FacePosAction(table, 200), new FramesAction([
                ActorWalker.DirFrame(dir, 0),
                ActorWalker.DirFrame(dir, Rng.Next(2) == 0 ? ActorWalker.Reach1Frame : ActorWalker.Reach2Frame),
                ActorWalker.DirFrame(dir, 0)
            ])));
        }
    }
}
