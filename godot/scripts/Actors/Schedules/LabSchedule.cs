using U7.Core;
using U7.Data;

namespace U7.Actors;

/// <summary>
/// Exult <c>Lab_schedule</c>: a mage at work within 20 tiles of a cauldron
/// (995) and lab tables (1003, 1018): stir the cauldron (it changes frame),
/// sit and read the book on a table, or go round a table taking a potion
/// (340) off it or setting a new one down.
/// </summary>
public sealed class LabSchedule : Schedule
{
    const int CauldronShape = 995;
    const int BookShape = 642;
    const int PotionShape = 340;

    enum State
    {
        Start,
        WalkToCauldron,
        UseCauldron,
        SitDown,
        ReadBook,
        StandUp,
        WalkToTable,
        UsePotion
    }

    State _state;
    U7Object? _cauldron;
    U7Object? _book;
    U7Object? _chair;
    readonly List<U7Object> _tables = new();
    TileCoord _spotOnTable;

    public LabSchedule(NpcBrain brain)
        : base(brain)
    {
        Init();
    }

    /// <summary>Exult <c>Lab_schedule::init</c>: the cauldron, the tables, a book on one and a chair by one.</summary>
    void Init()
    {
        _cauldron = FindClosest([CauldronShape], 20).FirstOrDefault();
        _tables.Clear();
        _tables.AddRange(Map.FindNearby(Here(Npc), 1003, 20));
        _tables.AddRange(Map.FindNearby(Here(Npc), 1018, 20));
        _book = null;
        _chair = null;
        foreach (var table in _tables)
        {
            if (_book is not null && _chair is not null)
            {
                break;
            }

            // A book on the table?
            if (_book is null && ClosestTo(table, [BookShape], 4) is { } book &&
                ObjectGeometry.InFootprint(table, book.Tx, book.Ty))
            {
                _book = book;
            }

            _chair ??= ClosestTo(table, SitSchedule.ChairShapes, 4);
        }
    }

    /// <summary>Exult <c>Game_object::find_closest</c> from another object.</summary>
    U7Object? ClosestTo(U7Object from, int[] shapes, int dist)
    {
        var here = Here(from);
        return shapes.SelectMany(shape => Map.FindNearby(here, shape, dist))
            .OrderBy(o => Here(o).Distance(here)).FirstOrDefault();
    }

    public override void NowWhat()
    {
        var delay = 100;
        var cauldron = _cauldron is { Removed: false } c ? c : null;
        var book = _book is { Removed: false } b ? b : null;
        switch (_state)
        {
            case State.WalkToCauldron:
                _state = State.Start; // In case we fail.
                if (cauldron is not null && PathWalk.Astar(Map, Npc, Here(cauldron), dist: 1) is { } walk)
                {
                    SetAction(new SequenceAction(100, walk, new FacePosAction(cauldron, 200)));
                    _state = State.UseCauldron;
                }

                break;
            case State.UseCauldron:
            {
                if (cauldron is null)
                {
                    _state = State.Start;
                    break;
                }

                // A random frame, but not the last.
                cauldron.Frame = Rng.Next(Math.Max(1, Map.Catalog[CauldronShape].FrameCount - 1));
                Npc.Frame = ActorWalker.DirFrame(ObjectGeometry.Direction(Npc, cauldron), ActorWalker.BowFrame);
                var r = Rng.Next(5);
                _state = r == 0 ? State.UseCauldron : r <= 2 ? State.SitDown : State.WalkToTable;
                break;
            }
            case State.SitDown:
                _state = _chair is { Removed: false } chair && SitDown(chair, 200) is not null
                    ? State.ReadBook
                    : State.Start;
                break;
            case State.ReadBook:
                _state = State.StandUp;
                if (book is null || ObjectGeometry.Distance(Npc, book) > 4)
                {
                    break;
                }

                delay = 1000 + 1000 * Rng.Next(5); // Read a little while.
                book.Frame -= book.Frame % 3; // Open the book.
                break;
            case State.StandUp:
                if (book is not null && ObjectGeometry.Distance(Npc, book) < 4)
                {
                    book.Frame = book.Frame - book.Frame % 3 + 1; // Close it.
                }

                _state = State.Start;
                break;
            case State.WalkToTable:
                WalkToTable();
                break;
            case State.UsePotion:
                UsePotion();
                break;
            default:
            {
                if (cauldron is null)
                {
                    // Try looking again, and again a little later (Exult tests
                    // the cauldron it had before looking, so it always waits).
                    Init();
                    delay = 6000;
                    break;
                }

                var r = Rng.Next(5);
                _state = r == 0 ? State.SitDown : r <= 2 ? State.WalkToCauldron : State.WalkToTable; // Sit less often.
                break;
            }
        }

        Start(Std, delay);
    }

    /// <summary>To a spot beside a random table, facing the table's edge next to it.</summary>
    void WalkToTable()
    {
        _state = State.Start; // In case we fail.
        if (_tables.Count == 0 || _tables[Rng.Next(_tables.Count)] is not { Removed: false } table)
        {
            return;
        }

        var (x, y, w, h) = ObjectGeometry.Footprint(table);
        PerimeterTile(x, y, w, h, Rng.Next(2 * w + 2 * h + 4), out var spot, out _spotOnTable);
        // (Exult's perimeter tiles are at lift 0, so this works on the ground floor.)
        if (PathWalk.Astar(Map, Npc, spot) is not { } walk)
        {
            return; // Failed.
        }

        _spotOnTable = _spotOnTable with { Tz = _spotOnTable.Tz + Map.Catalog[table.Shape].DimZ };
        SetAction(new SequenceAction(100, walk, new FacePosAction(_spotOnTable, 200)));
        _state = State.UsePotion;
    }

    /// <summary>Take the potion off the spot on the table, or set one down there if it is free; reach out.</summary>
    void UsePotion()
    {
        _state = State.Start;
        if (Map.FindNearby(_spotOnTable, PotionShape, 0) is [var potion, ..])
        {
            Map.RemoveObject(potion);
        }
        else if (Map.FindSpot(_spotOnTable, 0, PotionShape, 0) is { } t && t.Tz == _spotOnTable.Tz)
        {
            // A random potion, but not the last frame.
            var created = Map.CreateIregObject(PotionShape, Rng.Next(Math.Max(1, Map.Catalog[PotionShape].FrameCount - 1)));
            Map.PlaceInWorld(created, t.Tx, t.Ty, t.Tz);
        }

        var dir = ObjectGeometry.Direction(Npc, _spotOnTable);
        SetAction(new FramesAction([ActorWalker.DirFrame(dir, 1), ActorWalker.DirFrame(dir, 0)])); // Reach out.
    }

    /// <summary>
    /// Exult <c>Perimeter::get</c>: the i-th tile round a rectangle (from its
    /// top left, clockwise), and the tile of the rectangle next to it.
    /// </summary>
    public static void PerimeterTile(int rx, int ry, int rw, int rh, int i, out TileCoord ptile, out TileCoord atile)
    {
        int x = rx - 1, y = ry - 1, w = rw + 2, h = rh + 2;
        var size = 2 * rw + 2 * rh + 4;
        while (true)
        {
            if (i < w - 1)
            {
                ptile = new TileCoord(x + i, y, 0);
                atile = new TileCoord(ptile.Tx + (i == 0 ? 1 : 0), ptile.Ty + 1, 0);
                return;
            }

            i -= w - 1;
            if (i < h - 1)
            {
                ptile = new TileCoord(x + w - 1, y + i, 0);
                atile = new TileCoord(ptile.Tx - 1, ptile.Ty + (i == 0 ? 1 : 0), 0);
                return;
            }

            i -= h - 1;
            if (i < w - 1)
            {
                ptile = new TileCoord(x + w - 1 - i, y + h - 1, 0);
                atile = new TileCoord(ptile.Tx + (i == 0 ? -1 : 0), ptile.Ty - 1, 0);
                return;
            }

            i -= w - 1;
            if (i < h - 1)
            {
                ptile = new TileCoord(x, y + h - 1 - i, 0);
                atile = new TileCoord(ptile.Tx + 1, ptile.Ty + (i == 0 ? -1 : 0), 0);
                return;
            }

            i %= size; // Bad index.
        }
    }
}
