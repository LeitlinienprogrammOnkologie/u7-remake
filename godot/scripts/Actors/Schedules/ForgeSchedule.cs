using U7.Core;
using U7.Data;
using U7.World;

namespace U7.Actors;

/// <summary>
/// Exult <c>Forge_schedule</c>: the blacksmith's round. Lay the sword blank
/// (668) on the firepit (739), work the bellows (431) as the fire and the
/// blank glow hotter, carry the blank with tongs (994) to the anvil (991) and
/// hammer it (623) while it cools, fill the trough (719) if it is empty, and
/// quench the blank in it. Exult made its own tools, which go when the
/// schedule does (the original kept them on a nearby table).
/// </summary>
public sealed class ForgeSchedule(NpcBrain brain) : Schedule(brain)
{
    const int BlankShape = 668;
    const int FirepitShape = 739;
    const int BellowsShape = 431;
    const int TongsShape = 994;
    const int AnvilShape = 991;
    const int HammerShape = 623;
    const int TroughShape = 719;
    // Exult's raw frames for working the bellows: bowing and standing, facing west.
    const int BowWest = 0x20 | ActorWalker.BowFrame;
    const int StandWest = 0x20;

    enum State
    {
        PutSwordOnFirepit,
        UseBellows,
        GetTongs,
        SwordOnAnvil,
        GetHammer,
        UseHammer,
        WalkToTrough,
        FillTrough,
        GetTongs2,
        UseTrough,
        Done
    }

    State _state;
    U7Object? _blank;
    U7Object? _tongs;
    U7Object? _hammer;

    static U7Object? Valid(U7Object? obj) => obj is { Removed: false } ? obj : null;

    U7Object? Closest(int shape) => FindClosest([shape], 24).FirstOrDefault();

    /// <summary>Exult's paths here get within 1 tile (<c>Actor_pathfinder_client(npc, 1)</c>).</summary>
    PathWalk? PathTo(TileCoord dest) => PathTo(Here(Npc), dest);

    PathWalk? PathTo(TileCoord src, TileCoord dest) =>
        PathWalk.CreatePath(Map, src, dest.Wrapped(), new ActorPathClient(Map, Npc, 1));

    /// <summary>Exult <c>Frames_actor_action(frame, 0, obj)</c>: set one frame, no wait.</summary>
    static FramesAction Frame(int frame, U7Object? obj = null) => new([frame], 0, obj);

    /// <summary>Exult: <c>empty_hands</c>, then the tool in the left hand.</summary>
    U7Object Wield(U7Object? tool, int shape)
    {
        tool = Valid(tool) ?? Map.CreateIregObject(shape, 0);
        Equipment.EmptyHands(Npc, Map.Catalog, Map);
        Map.TakeFromWorld(tool);
        Equipment.AddReadied(Npc, tool, ReadySpot.Lhand, Map.Catalog, Map, forcePos: true);
        return tool;
    }

    void Retry()
    {
        Start(250, 2500); // Uh-oh... try again in a few seconds.
        _state = State.PutSwordOnFirepit;
    }

    public override void NowWhat()
    {
        switch (_state)
        {
            case State.PutSwordOnFirepit:
            {
                // (Exult: "TODO: go and get it...")
                var blank = Valid(_blank) ?? Closest(BlankShape);
                if (blank is null)
                {
                    // A new one, carried to the fire (Exult's is put there from nowhere).
                    blank = Map.CreateIregObject(BlankShape, 0);
                    Map.PlaceInContainer(blank, Npc, 255, 255);
                }

                _blank = blank;
                if (Closest(FirepitShape) is not { } firepit)
                {
                    Start(250, 2500);
                    return;
                }

                var (x, y, w, h) = ObjectGeometry.Footprint(firepit);
                var bpos = new TileCoord(x + w / 2 + 1, y + h / 2, firepit.Tz + Map.Catalog[firepit.Shape].DimZ);
                var put = new PickupAction(Map, blank, bpos, 250, temporary: false);
                SetAction(PathTo(ObjectGeometry.Tile(firepit)) is { } walk ? new SequenceAction(100, walk, put) : put);
                _state = State.UseBellows;
                break;
            }
            case State.UseBellows:
            {
                var bellows = Closest(BellowsShape);
                var firepit = Closest(FirepitShape);
                var blank = Valid(_blank);
                if (bellows is null || firepit is null || blank is null)
                {
                    Retry();
                    return;
                }

                // Pump the bellows; the fire and the blank glow hotter. (Without a
                // path Exult's sequence ends before it starts.)
                if (PathTo(new TileCoord(bellows.Tx + 3, bellows.Ty, bellows.Tz)) is { } walk)
                {
                    var actions = new List<IActorAction> { walk, new FacePosAction(bellows, 250) };
                    void Pump() => actions.AddRange([
                        Frame(BowWest), new ObjectAnimateAction(bellows, 3, 1, 300), Frame(StandWest)
                    ]);
                    Pump();
                    actions.AddRange([Frame(1, firepit), Frame(1, blank)]);
                    Pump();
                    actions.Add(Frame(2, blank));
                    Pump();
                    actions.AddRange([Frame(2, firepit), Frame(3, blank)]);
                    Pump();
                    actions.AddRange([Frame(3, firepit), Frame(4, blank)]);
                    for (var i = 0; i < 4; i++)
                    {
                        Pump();
                    }

                    actions.Add(Frame(0, bellows));
                    SetAction(new SequenceAction(100, [.. actions]));
                }

                _state = State.GetTongs;
                break;
            }
            case State.GetTongs:
                _tongs = Wield(_tongs, TongsShape);
                _state = State.SwordOnAnvil;
                break;
            case State.SwordOnAnvil:
            {
                var anvil = Closest(AnvilShape);
                var firepit = Closest(FirepitShape);
                var blank = Valid(_blank);
                if (anvil is null || firepit is null || blank is null)
                {
                    Retry();
                    return;
                }

                // Take the blank from the fire to the anvil.
                var tpos = ObjectGeometry.Tile(firepit);
                var tpos2 = new TileCoord(anvil.Tx, anvil.Ty + 1, anvil.Tz);
                var (x, y, _, _) = ObjectGeometry.Footprint(anvil);
                var bpos = new TileCoord(x + 2, y, anvil.Tz + Map.Catalog[anvil.Shape].DimZ);
                SetAction(PathTo(tpos) is { } walk && PathTo(tpos, tpos2) is { } walk2
                    ? new SequenceAction(100, walk, new PickupAction(Map, blank, 250), walk2,
                        new PickupAction(Map, blank, bpos, 250, temporary: false))
                    : new SequenceAction(100, new PickupAction(Map, blank, 250),
                        new PickupAction(Map, blank, bpos, 250, temporary: false)));
                _state = State.GetHammer;
                break;
            }
            case State.GetHammer:
                if (Valid(_tongs) is { } tongs)
                {
                    Map.RemoveObject(tongs);
                    _tongs = null;
                }

                _hammer = Wield(_hammer, HammerShape);
                _state = State.UseHammer;
                break;
            case State.UseHammer:
            {
                var anvil = Closest(AnvilShape);
                var firepit = Closest(FirepitShape);
                var blank = Valid(_blank);
                if (anvil is null || firepit is null || blank is null)
                {
                    Retry();
                    return;
                }

                // Three blows, the blank and the fire cooling down.
                var frames = AttackFrames(HammerShape, 0);
                SetAction(new SequenceAction(100,
                    new FramesAction(frames), Frame(3, blank), Frame(2, firepit),
                    new FramesAction(frames), Frame(2, blank), Frame(1, firepit),
                    new FramesAction(frames), Frame(1, blank), Frame(0, firepit)));
                _state = State.WalkToTrough;
                break;
            }
            case State.WalkToTrough:
            {
                if (Valid(_hammer) is { } hammer)
                {
                    Map.RemoveObject(hammer);
                }

                if (Closest(TroughShape) is not { } trough)
                {
                    Retry();
                    return;
                }

                if (trough.Frame == 0)
                {
                    // Empty: go and fill it.
                    SetAction(PathTo(new TileCoord(trough.Tx, trough.Ty + 2, trough.Tz)));
                    _state = State.FillTrough;
                    break;
                }

                _state = State.GetTongs2;
                break;
            }
            case State.FillTrough:
            {
                if (Closest(TroughShape) is not { } trough)
                {
                    Retry();
                    return;
                }

                trough.Frame = 3;
                Npc.Frame = ActorWalker.DirFrame(ObjectGeometry.Direction(Npc, trough), ActorWalker.BowFrame);
                _state = State.GetTongs2;
                break;
            }
            case State.GetTongs2:
                _tongs = Wield(_tongs, TongsShape);
                _state = State.UseTrough;
                break;
            case State.UseTrough:
            {
                var trough = Closest(TroughShape);
                var anvil = Closest(AnvilShape);
                var blank = Valid(_blank);
                if (trough is null || anvil is null || blank is null)
                {
                    Retry();
                    return;
                }

                // From the anvil to the trough, and quench the blank.
                var tpos = new TileCoord(anvil.Tx, anvil.Ty + 1, anvil.Tz);
                var tpos2 = new TileCoord(trough.Tx, trough.Ty + 2, trough.Tz);
                if (PathTo(tpos) is { } walk && PathTo(tpos, tpos2) is { } walk2)
                {
                    var troughFrame = Math.Max(0, trough.Frame - 1);
                    var npcFrame = ActorWalker.DirFrame(ObjectGeometry.Direction(Npc, trough), ActorWalker.BowFrame);
                    SetAction(new SequenceAction(100, walk, new PickupAction(Map, blank, 250), walk2,
                        new FramesAction([npcFrame], 250), Frame(troughFrame, trough), Frame(0, blank)));
                }
                else
                {
                    // No path: just pick up the blank.
                    SetAction(new SequenceAction(100, new PickupAction(Map, blank, 250), Frame(0, blank)));
                }

                _state = State.Done;
                break;
            }
            case State.Done:
                if (Valid(_tongs) is { } done)
                {
                    Map.RemoveObject(done);
                }

                _state = State.PutSwordOnFirepit;
                break;
        }

        Start(250, 100);
    }

    /// <summary>Exult: the tools go, and the fire, bellows and blank are put back to their first frames.</summary>
    public override void Ending(int newType)
    {
        if (Valid(_tongs) is { } tongs)
        {
            Map.RemoveObject(tongs);
        }

        if (Valid(_hammer) is { } hammer)
        {
            Map.RemoveObject(hammer);
        }

        foreach (var obj in new[] { Closest(FirepitShape), Closest(BellowsShape), Valid(_blank) })
        {
            if (obj is not null && obj.Frame != 0)
            {
                obj.Frame = 0;
            }
        }
    }
}
