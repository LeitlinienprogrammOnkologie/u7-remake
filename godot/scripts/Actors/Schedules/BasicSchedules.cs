using U7.Core;
using U7.Data;
using U7.World;

namespace U7.Actors;

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
        if (Rng.Next(2) != 0 && TryStreetMaintenance())
        {
            return; // Off to tend a lamp or shutter.
        }

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

/// <summary>
/// Exult <c>Pace_schedule</c>: from where the schedule began, back and forth
/// east-west (<paramref name="horiz"/>) or north-south until something is in
/// the way; then turn about, asking any actor in the way to move aside.
/// </summary>
public sealed class PaceSchedule(NpcBrain brain, bool horiz) : Schedule(brain)
{
    int _phase;

    public override void NowWhat()
    {
        if (_phase == 0)
        {
            _phase++;
            if (Here(Npc) != StartPos)
            {
                StartAction(PathWalk.Line(Map, Npc, StartPos), Std, Std);
            }
            else
            {
                Start(Std, Std);
            }

            return;
        }

        Pace(horiz, ref _phase, Std);
    }
}

/// <summary>
/// Exult <c>Hound_schedule</c>: dog the avatar's steps, keeping 1-2 tiles off
/// (a quick search to near it), facing it when close; now and then a remark.
/// Within 20 tiles only.
/// </summary>
public sealed class HoundSchedule(NpcBrain brain) : Schedule(brain)
{
    public override void NowWhat()
    {
        var av = Runner.Avatar;
        var dist = ObjectGeometry.Distance(Npc, av);
        // Exult's guess; seems quite rare.
        if (TryProximityUsecode(12))
        {
            return;
        }

        if (dist < 3)
        {
            // Close enough: face the avatar, and look again soon.
            Npc.Frame = ActorWalker.DirFrame(ObjectGeometry.Direction(Npc, av), 0);
            Start(Std, 500 + Rng.Next(1000));
            return;
        }

        if (dist > 20)
        {
            Start(Std, 500 + Rng.Next(1000)); // Too far; again in a moment.
            return;
        }

        var newdist = 1 + Rng.Next(2);
        var avpos = new TileCoord(av.Tx + Rng.Next(3) - 1, av.Ty + Rng.Next(3) - 1, av.Tz).Wrapped();
        if (PathWalk.CreatePath(Map, Npc, avpos, new FastPathClient(Map, Npc, avpos, newdist)) is { } walk)
        {
            StartAction(walk, Std, 50);
        }
        else
        {
            Start(Std, 2000 + Rng.Next(3000)); // Try again.
        }
    }
}

/// <summary>
/// Exult <c>Shy_schedule</c>: keep away from the avatar; within 10 tiles,
/// walk off away from it (the nearer, the farther), else wait or amble.
/// </summary>
public sealed class ShySchedule(NpcBrain brain) : Schedule(brain)
{
    public override void NowWhat()
    {
        var av = Runner.Avatar;
        if (ObjectGeometry.Distance(Npc, av) > 10)
        {
            // Far enough: look again in a moment, sometimes wandering a little.
            if (Rng.Next(3) != 0)
            {
                Start(250, 1000 + Rng.Next(1000));
            }
            else
            {
                var near = new TileCoord(Npc.Tx + Rng.Next(6) - 3, Npc.Ty + Rng.Next(6) - 3, Npc.Tz).Wrapped();
                StartAction(PathWalk.Line(Map, Npc, near), 250, 0);
            }

            return;
        }

        var dx = U7Constants.TileDelta(av.Tx, Npc.Tx);
        var dy = U7Constants.TileDelta(av.Ty, Npc.Ty);
        var farthest = Math.Max(Math.Abs(dx), Math.Abs(dy));
        var factor = farthest < 2 ? 9 : farthest < 4 ? 4 : farthest < 7 ? 2 : 1;
        // Walk away.
        var dest = new TileCoord(Npc.Tx + dx * factor + Rng.Next(3), Npc.Ty + dy * factor + Rng.Next(3), Npc.Tz)
            .Wrapped();
        if (PathWalk.CreatePath(Map, Npc, dest, new MonsterPathClient(Map, Npc, dest, 4)) is { } walk)
        {
            StartAction(walk, 200, 100 + Rng.Next(200));
        }
        else
        {
            Start(250, 500 + Rng.Next(1000)); // Try again in a couple of seconds.
        }
    }
}

/// <summary>
/// Exult <c>Thief_schedule</c>: hang about the avatar and, next to it, with
/// small talk ("Nice weather today."), now and then lift its gold (coins, a
/// nugget or a bar, the whole stack), at most every 8-16 s.
/// </summary>
public sealed class ThiefSchedule(NpcBrain brain) : Schedule(brain)
{
    double _nextStealTime;

    public override void NowWhat()
    {
        var av = Runner.Avatar;
        if (Runner.Ticks < _nextStealTime)
        {
            // Not time: wander about the avatar.
            const int dist = 6;
            var dest = new TileCoord(av.Tx - dist + Rng.Next(2 * dist), av.Ty - dist + Rng.Next(2 * dist), av.Tz)
                .Wrapped();
            StartAction(PathWalk.Line(Map, Npc, dest), 2 * Std, Rng.Next(4000));
            return;
        }

        if (ObjectGeometry.Distance(Npc, av) <= 1)
        {
            // Next to the avatar.
            if (Rng.Next(3) != 0)
            {
                Steal(av);
            }

            _nextStealTime = Runner.Ticks + 8000 + Rng.Next(8000);
            Start(250, 1000 + Rng.Next(2000));
            return;
        }

        // Get within 1 tile of it.
        var to = ObjectGeometry.Tile(av);
        if (PathWalk.CreatePath(Map, Npc, to, new MonsterPathClient(Map, Npc, to, 1)) is { } walk)
        {
            StartAction(walk, Std, 1000 + Rng.Next(1000));
        }
        else
        {
            Start(250, 2000 + Rng.Next(2000)); // Try again in a couple of seconds.
        }
    }

    /// <summary>Exult <c>Thief_schedule::steal</c>.</summary>
    void Steal(U7Object from)
    {
        if (CanSpeak)
        {
            Say(TextMessages.FirstThief, TextMessages.LastThief);
        }

        // A gold coin, nugget or bar, starting with a random one.
        var all = new List<U7Object>();
        from.CollectContents(all);
        var shnum = Rng.Next(3);
        U7Object? obj = null;
        for (var i = 0; obj is null && i < 3; i++)
        {
            obj = all.FirstOrDefault(o => !o.Removed && o.Shape == 644 + shnum);
            shnum = (shnum + 1) % 3;
        }

        if (obj is not null)
        {
            Map.TakeFromWorld(obj);
            Equipment.AddToActor(Npc, obj, Map.Catalog, Map);
        }
    }
}
