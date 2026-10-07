using U7.Core;
using U7.Data;

namespace U7.Actors;

/// <summary>
/// Exult <c>Sew_schedule</c>: the weaver's round. Fetch the bale of wool
/// (653) and put it back, sit at the spinning wheel (651) and spin, take the
/// spindle of thread (654), weave at the loom (261) and take the cloth
/// (851), lay it on the work table (971) and cut and sew it with the shears
/// (698) into a top or pants (738, 249), then display them on the wares
/// table (890); with three or more clothes about, one goes again.
/// </summary>
public sealed class SewSchedule(NpcBrain brain) : Schedule(brain)
{
    const int BaleShape = 653;
    const int SpindleShape = 654;
    const int ClothShape = 851;
    const int SpinningWheelShape = 651;
    const int LoomShape = 261;
    const int WorkTableShape = 971;
    const int WaresTableShape = 890;
    const int ShearsShape = 698;
    const int ChairShape = 873;
    static readonly int[] ClothesShapes = [738, 249]; // Black Gate's top and pants.

    enum State
    {
        GetWool,
        SitAtWheel,
        SpinWool,
        GetThread,
        WeaveCloth,
        GetCloth,
        ToWorkTable,
        SetToSew,
        SewClothes,
        GetClothes,
        DisplayClothes,
        Done
    }

    State _state;
    U7Object? _spinwheel;
    U7Object? _spindle;
    U7Object? _loom;
    U7Object? _cloth;
    int _sewClothesCnt;

    U7Object? Closest(int shape) => FindClosest([shape], 24).FirstOrDefault();

    static U7Object? Valid(U7Object? obj) => obj is { Removed: false } ? obj : null;

    int Frames(int shape) => Math.Max(1, Map.Catalog[shape].FrameCount);

    public override void NowWhat()
    {
        // Exult's guess; seems quite rare.
        if (TryProximityUsecode(12))
        {
            return;
        }

        switch (_state)
        {
            case State.GetWool:
            {
                // Clean up any remainders.
                if (Valid(_spindle) is { } spindle)
                {
                    Map.RemoveObject(spindle);
                }

                if (Valid(_cloth) is { } leftover)
                {
                    Map.RemoveObject(leftover);
                }

                _cloth = _spindle = null;
                var quantity = new ItemQuantity(Map.Catalog, Map, Runner.Weapons, null);
                quantity.Remove(Npc, 2, SpindleShape, U7Constants.AnyShape, U7Constants.AnyShape);
                quantity.Remove(Npc, 2, ClothShape, U7Constants.AnyShape, U7Constants.AnyShape);
                _state = State.SitAtWheel;
                if (Closest(BaleShape) is not { } bale)
                {
                    break; // Just skip this step.
                }

                var at = ObjectGeometry.Tile(bale);
                if (PathWalk.Astar(Map, Npc, at, dist: 1) is { } walk)
                {
                    SetAction(new SequenceAction(100, walk, new PickupAction(Map, bale, 250),
                        new PickupAction(Map, bale, at, 250, temporary: false)));
                }

                break;
            }
            case State.SitAtWheel:
                if (Closest(ChairShape) is not { } chair || SitDown(chair, 200) is null)
                {
                    Start(250, 2500); // Uh-oh... try again in a few seconds.
                    return;
                }

                _state = State.SpinWool;
                break;
            case State.SpinWool: // Cycle the spinning wheel 8 times.
                _spinwheel = Closest(SpinningWheelShape);
                if (_spinwheel is null)
                {
                    Start(250, 2500);
                    return;
                }

                SetAction(new ObjectAnimateAction(_spinwheel, Frames(SpinningWheelShape), 8, 200));
                _state = State.GetThread;
                break;
            case State.GetThread:
            {
                if (Valid(_spinwheel) is not { } wheel)
                {
                    _state = State.GetWool;
                    break;
                }

                // Room by the wheel for the thread?
                if (Map.FindSpot(ObjectGeometry.Tile(wheel), 1, SpindleShape, 0) is { } t)
                {
                    var spindle = Map.CreateIregObject(SpindleShape, 0);
                    Map.PlaceInWorld(spindle, t.Tx, t.Ty, t.Tz);
                    _spindle = spindle;
                    SetAction(new PickupAction(Map, spindle, 250));
                }

                _state = State.WeaveCloth;
                break;
            }
            case State.WeaveCloth:
            {
                if (Valid(_spindle) is { } spindle)
                {
                    Map.RemoveObject(spindle); // Should be in hand.
                }

                _spindle = null;
                _loom = Closest(LoomShape);
                if (_loom is null)
                {
                    _state = State.GetWool;
                    break;
                }

                var lpos = new TileCoord(_loom.Tx - 1, _loom.Ty, _loom.Tz).Wrapped();
                if (PathWalk.Astar(Map, Npc, lpos, dist: 1) is { } walk)
                {
                    SetAction(new SequenceAction(100, walk, new FacePosAction(_loom, 250),
                        new ObjectAnimateAction(_loom, Frames(LoomShape), 4, 200)));
                }

                _state = State.GetCloth;
                break;
            }
            case State.GetCloth:
                if (Valid(_loom) is { } loom && Map.FindSpot(ObjectGeometry.Tile(loom), 1, ClothShape, 0) is { } spot)
                {
                    var cloth = Map.CreateIregObject(ClothShape, Rng.Next(2));
                    Map.PlaceInWorld(cloth, spot.Tx, spot.Ty, spot.Tz);
                    _cloth = cloth;
                    SetAction(new PickupAction(Map, cloth, 250));
                }

                _state = State.ToWorkTable;
                break;
            case State.ToWorkTable:
            {
                if (Closest(WorkTableShape) is not { } table || Valid(_cloth) is not { } cloth)
                {
                    _state = State.GetWool;
                    break;
                }

                var tpos = new TileCoord(table.Tx + 1, table.Ty - 2, table.Tz).Wrapped();
                // The cloth goes in the middle of the table.
                var (x, y, w, h) = ObjectGeometry.Footprint(table);
                var cpos = new TileCoord(x + w / 2, y + h / 2, table.Tz + Map.Catalog[table.Shape].DimZ);
                if (PathWalk.Astar(Map, Npc, tpos, dist: 1) is { } walk)
                {
                    SetAction(new SequenceAction(100, walk, new FacePosAction(table, 250),
                        new PickupAction(Map, cloth, cpos, 250, temporary: false)));
                }

                _state = State.SetToSew;
                break;
            }
            case State.SetToSew:
            {
                var shears = Equipment.GetReadied(Npc, ReadySpot.Lhand);
                if (shears is not null && shears.Shape != ShearsShape)
                {
                    Map.RemoveObject(shears); // Something's not right.
                    shears = null;
                }

                if (shears is null)
                {
                    // Shears on the table?
                    if (Map.FindNearby(Here(Npc), ShearsShape, 3) is [var found, ..])
                    {
                        shears = found;
                        Map.TakeFromWorld(shears);
                    }
                    else
                    {
                        shears = Map.CreateIregObject(ShearsShape, 0);
                    }

                    Equipment.AddReadied(Npc, shears, ReadySpot.Lhand, Map.Catalog, Map, forcePos: true);
                }

                _state = State.SewClothes;
                _sewClothesCnt = 0;
                break;
            }
            case State.SewClothes:
            {
                if (Valid(_cloth) is not { } cloth)
                {
                    _state = State.GetWool;
                    break;
                }

                SetAction(new FramesAction(AttackFrames(ShearsShape, ObjectGeometry.Direction(Npc, cloth))));
                _sewClothesCnt++;
                if (_sewClothesCnt is > 1 and < 5)
                {
                    cloth.Frame = Rng.Next(Frames(ClothShape));
                }
                else if (_sewClothesCnt == 5)
                {
                    // A top or pants.
                    var shape = ClothesShapes[Rng.Next(2)];
                    Map.SetShape(cloth, shape);
                    cloth.Frame = Rng.Next(Frames(shape));
                    _state = State.GetClothes;
                }

                break;
            }
            case State.GetClothes:
            {
                var shears = Equipment.GetReadied(Npc, ReadySpot.Lhand);
                if (Valid(_cloth) is not { } cloth)
                {
                    _state = State.GetWool;
                    break;
                }

                SetAction(shears is not null
                    ? new SequenceAction(100, new PickupAction(Map, cloth, 250),
                        new PickupAction(Map, shears, ObjectGeometry.Tile(cloth), 250, temporary: false))
                    : new PickupAction(Map, cloth, 250));
                _state = State.DisplayClothes;
                break;
            }
            case State.DisplayClothes:
            {
                _state = State.Done;
                var cloth = Valid(_cloth);
                if (Closest(WaresTableShape) is not { } wares || cloth is null)
                {
                    if (cloth is not null)
                    {
                        Map.RemoveObject(cloth);
                        _cloth = null;
                    }

                    break;
                }

                var tpos = new TileCoord(wares.Tx + 1, wares.Ty - 2, wares.Tz).Wrapped();
                var (x, y, w, h) = ObjectGeometry.Footprint(wares);
                var cpos = new TileCoord(x + Rng.Next(w), y + Rng.Next(h), wares.Tz + Map.Catalog[wares.Shape].DimZ);
                if (PathWalk.Astar(Map, Npc, tpos, dist: 1) is { } walk)
                {
                    SetAction(new SequenceAction(100, walk, new PickupAction(Map, cloth, cpos, 250, temporary: true)));
                }

                _cloth = null; // Leave it be.
                break;
            }
            case State.Done:
            {
                // Just put down clothing; don't make too many.
                _state = State.GetWool;
                var clothes = ClothesShapes.SelectMany(shape => Map.FindNearby(Here(Npc), shape, 5)).ToList();
                if (clothes.Count >= 3)
                {
                    Map.RemoveObject(clothes[Rng.Next(clothes.Count)]);
                }

                break;
            }
        }

        Start(250, 100);
    }

    /// <summary>Exult: the shears go, and any cloth, not to be left lying around.</summary>
    public override void Ending(int newType)
    {
        if (Equipment.GetReadied(Npc, ReadySpot.Lhand) is { } shears)
        {
            Map.RemoveObject(shears);
        }

        if (Valid(_cloth) is { } cloth)
        {
            Map.RemoveObject(cloth);
        }
    }
}
