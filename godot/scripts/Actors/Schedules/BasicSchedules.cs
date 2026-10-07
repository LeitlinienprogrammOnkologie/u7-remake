using U7.Core;

namespace U7.Actors;

/// <summary>
/// Exult <c>Loiter_schedule</c>: now and then a remark, else amble somewhere
/// within <paramref name="dist"/> tiles, slowly. Exult also uses it, "for
/// now", for tend shop: 3 tiles, remarking one time in 8 instead of 12.
/// </summary>
public sealed class LoiterSchedule(NpcBrain brain, int dist, int odds) : Schedule(brain)
{
    /// <summary>Exult <c>Loiter_schedule</c>'s default distance.</summary>
    public const int DefaultDist = 12;

    public override void NowWhat()
    {
        // (Exult first looks for street lamps and shutters to tend; not ported.)
        if (TryProximityUsecode(odds))
        {
            return;
        }

        var center = Brain.Center;
        var tx = center.Tx - dist + Rng.Next(2 * dist);
        var ty = center.Ty - dist + Rng.Next(2 * dist);
        StartAction(PathWalk.Line(Map, Npc, new TileCoord(tx, ty, center.Tz).Wrapped()),
            2 * U7Constants.StandardDelayMs, Rng.Next(2000));
    }
}

/// <summary>
/// Exult <c>Wander_schedule::now_what</c>: an A* walk to a free spot up to
/// 32 tiles off, staying within 128 of the centre; on failure, try again
/// within 3 s.
/// </summary>
public sealed class WanderSchedule(NpcBrain brain) : Schedule(brain)
{
    const int Dist = 128;
    const int LegDist = 32;

    public override void NowWhat()
    {
        var center = Brain.Center;
        var tx = Npc.Tx - LegDist + Rng.Next(2 * LegDist);
        var ty = Npc.Ty - LegDist + Rng.Next(2 * LegDist);
        tx = center.Tx + Math.Clamp(U7Constants.TileDelta(center.Tx, tx), -Dist, Dist);
        ty = center.Ty + Math.Clamp(U7Constants.TileDelta(center.Ty, ty), -Dist, Dist);
        var walk = Map.FindSpot(tx, ty, Npc.Tz, 4) is { } spot ? PathWalk.Astar(Map, Npc, spot) : null;
        if (walk is null)
        {
            Start(250, Rng.Next(3000));
            return;
        }

        StartAction(walk, U7Constants.StandardDelayMs, Rng.Next(2000));
    }
}

/// <summary>Pacing up and down (or across) within 4 tiles of the spot, turning at the ends or when blocked.</summary>
public sealed class PaceSchedule(NpcBrain brain, bool horiz) : Schedule(brain)
{
    int _dir = 1;

    public override void Begin() => ActorWalker.Stand(Npc, horiz ? 2 : 4);

    public override void NowWhat()
    {
        var pos = horiz ? Npc.Tx : Npc.Ty;
        var origin = horiz ? Brain.Center.Tx : Brain.Center.Ty;
        if (Math.Abs(U7Constants.TileDelta(origin, pos)) >= 4)
        {
            _dir = -Math.Sign(U7Constants.TileDelta(origin, pos));
            if (_dir == 0)
            {
                _dir = 1;
            }
        }

        if (!ActorWalker.TryStep(Map, Npc, horiz ? _dir : 0, horiz ? 0 : _dir))
        {
            _dir = -_dir;
        }
    }
}

/// <summary>(Exult patrols between path eggs; not ported: wander near the spot.)</summary>
public sealed class PatrolSchedule(NpcBrain brain) : Schedule(brain)
{
    public override void NowWhat()
    {
        var center = Brain.Center;
        var tx = center.Tx - 4 + Rng.Next(9);
        var ty = center.Ty - 4 + Rng.Next(9);
        StartAction(PathWalk.Astar(Map, Npc, new TileCoord(tx, ty, center.Tz).Wrapped()),
            U7Constants.StandardDelayMs, Rng.Next(2000));
    }
}
