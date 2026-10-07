using U7.Data;

namespace U7.Actors;

/// <summary>
/// Exult <c>Tool_schedule</c>: work with a tool (a scythe, a pick) made for
/// the NPC and put in its hand, which empties both hands of whatever was in
/// them; the tool goes when the schedule does. In between it loiters.
/// </summary>
public abstract class ToolSchedule(NpcBrain brain, int toolShape) : LoiterSchedule(brain)
{
    protected U7Object? Tool;
    protected int ToolShape => toolShape;

    /// <summary>Exult <c>Tool_schedule::get_tool</c>.</summary>
    protected void GetTool()
    {
        var tool = Map.CreateIregObject(toolShape, 0);
        Tool = tool;
        // Free up both hands.
        if (Equipment.GetReadied(Npc, ReadySpot.Rhand) is { } right)
        {
            Map.RemoveObject(right);
        }

        if (Equipment.GetReadied(Npc, ReadySpot.Lhand) is { } left)
        {
            Map.RemoveObject(left);
        }

        Equipment.AddReadied(Npc, tool, ReadySpot.Lhand, Map.Catalog, Map, forcePos: true);
    }

    /// <summary>Strike at it: the tool's attack frames, then standing.</summary>
    protected IActorAction UseTool(U7Object target, int dir) =>
        new FramesAction([.. AttackFrames(toolShape, dir), ActorWalker.DirFrame(dir, 0)]);

    public override void Ending(int newType)
    {
        if (Tool is { Removed: false } tool)
        {
            Map.RemoveObject(tool);
        }
    }
}

/// <summary>
/// Exult <c>Farmer_schedule</c>: walk up to a crop (shape 423) not yet cut,
/// swing the scythe at it until it is cut (frame 3 of its group of 4),
/// grumbling now and then; sometimes the farther crops of that kind grow a
/// stage meanwhile. Now and then it wanders.
/// </summary>
public sealed class FarmerSchedule(NpcBrain brain) : ToolSchedule(brain, 618)
{
    const int CropShape = 423;

    enum State
    {
        Start,
        FindCrop,
        AttackCrop,
        CropAttacked,
        Wander
    }

    State _state;
    U7Object? _crop;
    int _growCnt;
    /// <summary>The first frame of the group of 4 being cut.</summary>
    int _frameGroup0 = -1;

    public override void NowWhat()
    {
        var delay = 0;
        if (Tool is not { Removed: false })
        {
            GetTool(); // First time.
        }

        switch (_state)
        {
            case State.Start:
                _state = State.FindCrop;
                goto case State.FindCrop;
            case State.FindCrop:
            {
                _crop = null;
                if (CanSpeak && Rng.Next(5) == 0)
                {
                    Say(TextMessages.FirstFarmer2, TextMessages.LastFarmer2);
                }

                U7Object? crop = null;
                foreach (var each in FindClosest([CropShape], 24))
                {
                    if (each.Frame % 4 != 3) // Not cut already.
                    {
                        crop = each;
                        if (Rng.Next(3) == 0)
                        {
                            break; // A little randomness.
                        }
                    }
                }

                if (crop is not null && PathWalk.Astar(Map, Npc, ObjectGeometry.Tile(crop), dist: 2) is { } walk)
                {
                    _crop = crop;
                    _state = State.AttackCrop;
                    SetAction(new SequenceAction(100, walk, new FacePosAction(crop, 200)));
                    break;
                }

                _state = State.Wander;
                delay = 1000 + Rng.Next(1000); // Try again later.
                break;
            }
            case State.AttackCrop:
            {
                if (_crop is not { Removed: false, Container: null } crop || ObjectGeometry.Distance(Npc, crop) > 2)
                {
                    _state = State.FindCrop;
                    break;
                }

                _frameGroup0 = crop.Frame & ~3;
                SetAction(UseTool(crop, ObjectGeometry.Direction(Npc, crop)));
                _state = State.CropAttacked;
                break;
            }
            case State.CropAttacked:
            {
                if (_crop is not { Removed: false, Container: null } crop)
                {
                    _state = State.FindCrop;
                    break;
                }

                if (CanSpeak && Rng.Next(8) == 0)
                {
                    Say(TextMessages.FirstFarmer, TextMessages.LastFarmer);
                }

                if (Rng.Next(3) == 0)
                {
                    crop.Frame |= 3; // Cut down: frame 3 of each group of 4.
                    _crop = null;
                    _state = Rng.Next(4) == 0 ? State.Wander : State.FindCrop; // Wander now and then.
                    if (_frameGroup0 >= 0 && _growCnt < 4 && Rng.Next(2) != 0)
                    {
                        GrowCrops();
                        ++_growCnt;
                    }

                    delay = 500 + Rng.Next(2000);
                }
                else
                {
                    _state = State.AttackCrop;
                    delay = 250;
                }

                break;
            }
            case State.Wander:
                base.NowWhat();
                if (Rng.Next(2) == 0)
                {
                    _state = State.FindCrop;
                }

                return;
        }

        Start(Std, delay);
    }

    /// <summary>Exult <c>Grow_crops</c>: the farther half of the crops of the kind being cut grow a stage, one in four.</summary>
    void GrowCrops()
    {
        var crops = FindClosest([CropShape], 24);
        for (var i = crops.Count / 2; i < crops.Count; i++)
        {
            if (Rng.Next(4) != 0)
            {
                continue;
            }

            var crop = crops[i];
            var growth = ((crop.Frame & 3) + 1) & 3;
            if ((crop.Frame & ~3) == _frameGroup0 && growth != 3)
            {
                crop.Frame = (crop.Frame & ~3) | growth;
            }
        }
    }
}

/// <summary>
/// Exult <c>Miner_schedule</c>: walk up to a piece of ore (shapes 915, 916;
/// not yet dust, frame 3) and work it with the pick, each blow sometimes
/// breaking it down a stage and sometimes turning up a gold nugget or a gem
/// ("Eureka!", left lying there); muttering now and then. Now and then it
/// wanders.
/// </summary>
public sealed class MinerSchedule(NpcBrain brain) : ToolSchedule(brain, 624)
{
    static readonly int[] OreShapes = [915, 916];
    const int GoldNugget = 645;
    const int Gem = 760;

    enum State
    {
        FindOre,
        AttackOre,
        OreAttacked,
        Wander
    }

    State _state;
    U7Object? _ore;

    public override void NowWhat()
    {
        var delay = 0;
        if (Tool is not { Removed: false })
        {
            GetTool(); // First time.
        }

        switch (_state)
        {
            case State.FindOre:
            {
                var ores = FindClosest(OreShapes, 24).Where(o => o.Frame < 3).ToList(); // Not dust.
                if (ores.Count > 0)
                {
                    var ore = ores[Rng.Next(ores.Count)];
                    if (PathWalk.Astar(Map, Npc, ObjectGeometry.Tile(ore), dist: 2) is { } walk)
                    {
                        _ore = ore;
                        _state = State.AttackOre;
                        SetAction(new SequenceAction(100, walk, new FacePosAction(ore, 200)));
                        break;
                    }
                }

                _ore = null;
                _state = State.Wander;
                delay = 1000 + Rng.Next(1000); // Try again later.
                break;
            }
            case State.AttackOre:
            {
                if (_ore is not { Removed: false, Container: null } ore || ObjectGeometry.Distance(Npc, ore) > 2)
                {
                    _state = State.FindOre;
                    break;
                }

                SetAction(UseTool(ore, ObjectGeometry.Direction(Npc, ore)));
                _state = State.OreAttacked;
                break;
            }
            case State.OreAttacked:
            {
                if (_ore is not { Removed: false, Container: null } ore)
                {
                    _state = State.FindOre;
                    break;
                }

                _state = State.AttackOre;
                if (Rng.Next(6) == 0)
                {
                    // Break up a piece.
                    var frnum = ore.Frame;
                    if (frnum == 3)
                    {
                        _state = State.FindOre; // Dust.
                    }
                    else if (Rng.Next(4 + 2 * frnum) == 0)
                    {
                        if (CanSpeak)
                        {
                            Say(TextMessages.FirstMinerGold, TextMessages.LastMinerGold);
                        }

                        var pos = ObjectGeometry.Tile(ore);
                        Map.RemoveObject(ore);
                        var find = frnum == 0
                            ? Map.CreateIregObject(GoldNugget, Rng.Next(2))
                            : Map.CreateIregObject(Gem, Rng.Next(10));
                        Map.PlaceInWorld(find, pos.Tx, pos.Ty, pos.Tz);
                        find.SetFlag(ObjFlag.Temporary);
                        _state = State.FindOre;
                        break;
                    }
                    else
                    {
                        ore.Frame = frnum + 1;
                        if (ore.Frame == 3)
                        {
                            _state = State.FindOre; // Dust.
                        }
                    }
                }

                if (CanSpeak && Rng.Next(4) == 0)
                {
                    Say(TextMessages.FirstMiner, TextMessages.LastMiner);
                }

                delay = 500 + Rng.Next(2000);
                break;
            }
            case State.Wander:
                if (Rng.Next(2) == 0)
                {
                    base.NowWhat();
                    return;
                }

                _state = State.FindOre;
                break;
        }

        Start(Std, delay);
    }
}
