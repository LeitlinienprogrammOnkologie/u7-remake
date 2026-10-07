using U7.Data;

namespace U7.Actors;

/// <summary>
/// Exult <c>Sit_schedule</c>: sit on a chair (the one usecode's
/// <c>sit_down</c> names, or the nearest) and stay there; if made to get up,
/// sit down again a second or two later. Once the whole party sits on a
/// barge's seats, the barge usecode (0x634) sets it going.
/// </summary>
public sealed class SitSchedule(NpcBrain brain, U7Object? chair = null) : Schedule(brain)
{
    public static readonly int[] ChairShapes = [873, 292];
    const int BargeUsecode = 0x634;

    U7Object? _chair = chair;
    bool _sat;
    bool _didBargeUsecode;

    public override void NowWhat()
    {
        var chair = _chair is { Removed: false } c ? c : null;
        if (chair is not null && (Npc.Frame & 0xf) == ActorWalker.SitFrame && Here(Npc).Distance(Here(chair)) <= 1)
        {
            // Already sitting. A barge seat starts the barge, but not more than once.
            if (Map.Catalog[chair.Shape].BargeType != 2 || _didBargeUsecode)
            {
                return;
            }

            _didBargeUsecode = true;
            if (Runner.Barges?.Moving is not null || !Npc.GetFlag(ObjFlag.InParty) ||
                Runner.Party is not { } party ||
                party.Members.Prepend(Runner.Avatar).Any(m => (m.Frame & 0xf) != ActorWalker.SitFrame) ||
                Map.FindNearby(Here(chair), U7.World.Barge.Shape, 24).Count == 0)
            {
                return;
            }

            // (With the avatar as item, so that nearby barges are left alone.)
            Runner.CallUsecode?.Invoke(BargeUsecode, Runner.Avatar);
            return;
        }

        // Wait a while if we got up.
        _chair = SitDown(chair, _sat ? 1000 + Rng.Next(1000) : 0);
        if (_chair is null)
        {
            Start(200, 1000); // Failed? Try again later.
        }
        else
        {
            _sat = true;
        }
    }
}

/// <summary>
/// Exult <c>Eat_at_inn_schedule</c>: sit down, then every 5-17 s now and
/// then finish a plate of food within reach, and remark on it ("Mmmm,
/// tasty!") or call for more ("Barkeeper!"). Leaving, the NPC eats up.
/// </summary>
public sealed class EatAtInnSchedule(NpcBrain brain) : Schedule(brain)
{
    bool _sittingAtChair;

    public override void NowWhat()
    {
        var frnum = Npc.Frame & 0xf;
        if (!_sittingAtChair || frnum != ActorWalker.SitFrame)
        {
            if (frnum == ActorWalker.SitFrame && FindClosest(SitSchedule.ChairShapes, 1).Count > 0)
            {
                _sittingAtChair = true;
            }
            else
            {
                if (SitDown(null, 0) is null)
                {
                    Start(250, 5000); // Try again in a while.
                }

                return;
            }
        }

        if (FindClosest([FoodShape], 2) is [var food, ..])
        {
            if (Rng.Next(5) == 0)
            {
                Map.RemoveObject(food);
            }

            if (CanSpeak && Rng.Next(4) != 0)
            {
                Say(TextMessages.FirstMunch, TextMessages.LastMunch);
            }
        }
        else if (CanSpeak && Rng.Next(4) != 0)
        {
            Say(TextMessages.FirstMoreFood, TextMessages.LastMoreFood);
        }

        Start(250, 5000 + Rng.Next(12000)); // Wake up in a little while.
    }

    /// <summary>Exult eats it all at once (its TODO: one at a time).</summary>
    public override void Ending(int newType)
    {
        foreach (var food in Map.FindNearby(Here(Npc), FoodShape, 2))
        {
            Map.RemoveObject(food);
            if (CanSpeak)
            {
                Say(TextMessages.FirstMunch, TextMessages.LastMunch);
            }
        }
    }
}

/// <summary>
/// Exult <c>Eat_schedule</c>: sit down, find a plate within a tile on the
/// same floor, put a random food on it, then now and then eat food within
/// reach; no remarks while eating. Leaving, the food goes.
/// </summary>
public sealed class EatSchedule(NpcBrain brain) : Schedule(brain)
{
    enum State
    {
        FindPlate,
        ServeFood,
        Eat
    }

    State _state;
    U7Object? _plate;

    public override void NowWhat()
    {
        var delay = 5000 + Rng.Next(12000);
        if ((Npc.Frame & 0xf) != ActorWalker.SitFrame)
        {
            if (SitDown(null, 0) is null)
            {
                Start(250, 5000); // First have to sit down; try again in a while.
            }

            return;
        }

        switch (_state)
        {
            case State.Eat:
                // Loops back to itself, since the NPC can be pushed out of the chair.
                if (FindClosest([FoodShape], 2) is [var food, ..] && Rng.Next(5) == 0)
                {
                    Map.RemoveObject(food);
                }

                break;
            case State.FindPlate:
                _state = State.ServeFood;
                delay = 0;
                _plate = Map.FindNearby(Here(Npc), PlateShape, 1).FirstOrDefault(p => p.Tz / 5 == Npc.Tz / 5);
                break;
            case State.ServeFood:
                _state = State.Eat;
                if (_plate is not { Removed: false, Container: null } plate)
                {
                    _state = State.FindPlate;
                    break;
                }

                var dish = Map.CreateIregObject(FoodShape, Rng.Next(31));
                Map.PlaceInWorld(dish, plate.Tx, plate.Ty, plate.Tz + 1);
                break;
        }

        Start(250, delay);
    }

    public override void Ending(int newType)
    {
        foreach (var food in Map.FindNearby(Here(Npc), FoodShape, 2))
        {
            Map.RemoveObject(food);
        }
    }
}
