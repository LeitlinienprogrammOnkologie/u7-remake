using U7.Core;
using U7.Data;
using U7.World;

namespace U7.Actors;

/// <summary>
/// Exult <c>Loiter_schedule</c>: now and then off to tend a lamp or shutter,
/// or a remark (loitering one time in 12; Exult also uses it, "for now", for
/// tend shop: 3 tiles, one time in 8), else amble somewhere within
/// <paramref name="dist"/> tiles of the centre, slowly. Kid games, dance,
/// graze, farming and mining build on it.
/// </summary>
public class LoiterSchedule(NpcBrain brain, int dist = 12) : Schedule(brain)
{
    protected TileCoord Center => Brain.Center;
    protected int Dist => dist;

    public override void NowWhat()
    {
        if (Rng.Next(3) == 0 && TryStreetMaintenance())
        {
            return; // Off to tend a lamp or shutter.
        }

        // Exult's guess: only some schedules run the proximity usecode.
        if ((Npc.ScheduleType == ScheduleType.Loiter && TryProximityUsecode(12)) ||
            (Npc.ScheduleType == ScheduleType.TendShop && TryProximityUsecode(8)))
        {
            return;
        }

        var tx = Center.Tx - dist + Rng.Next(2 * dist);
        var ty = Center.Ty - dist + Rng.Next(2 * dist);
        StartAction(PathWalk.Line(Map, Npc, new TileCoord(tx, ty, Center.Tz).Wrapped()), 2 * Std, Rng.Next(2000));
    }
}

/// <summary>
/// Exult <c>Kid_games_schedule</c>: run after another child playing within
/// 16 tiles (a short, quick search), else loiter within 10 tiles.
/// </summary>
public sealed class KidGamesSchedule(NpcBrain brain) : LoiterSchedule(brain, 10)
{
    /// <summary>Other kids playing, chased one after another.</summary>
    readonly List<U7Object> _kids = new();

    public override void NowWhat()
    {
        U7Object? kid = null;
        while (_kids.Count > 0)
        {
            kid = _kids[^1];
            _kids.RemoveAt(_kids.Count - 1);
            if (!kid.Removed && ObjectGeometry.Distance(Npc, kid) < 16)
            {
                break; // But don't run too far.
            }

            kid = null;
        }

        if (kid is not null)
        {
            var dest = ObjectGeometry.Tile(kid);
            if (PathWalk.CreatePath(Map, Npc, dest, new FastPathClient(Map, Npc, dest, 1)) is { } run)
            {
                StartAction(run, 100, 250); // Run.
                return;
            }
        }
        else
        {
            // No more kids? Search.
            _kids.AddRange(Map.FindNearby(Here(Npc), U7Constants.AnyShape, 16, 8)
                .Where(actor => actor.ScheduleType == ScheduleType.KidGames));
        }

        base.NowWhat(); // Wander around the start.
    }
}

/// <summary>
/// Exult <c>Dance_schedule</c>: walk a few steps within 4 tiles, then spin
/// round (standing, hands up or out), flap the arms, or punch the air; now
/// and then a remark.
/// </summary>
public sealed class DanceSchedule(NpcBrain brain) : LoiterSchedule(brain, 4)
{
    const int UpFrame = 14;
    const int OutFrame = 15;

    public override void NowWhat()
    {
        var dest = new TileCoord(Center.Tx - Dist + Rng.Next(2 * Dist), Center.Ty - Dist + Rng.Next(2 * Dist),
            Center.Tz).Wrapped();
        var dir = ActorWalker.Direction4(-U7Constants.TileDelta(Npc.Ty, dest.Ty), U7Constants.TileDelta(Npc.Tx, dest.Tx));
        // Exult's guess; seems quite rare.
        if (TryProximityUsecode(8))
        {
            return;
        }

        int[] frames;
        var routine = Rng.Next(5);
        switch (routine)
        {
            case 3: // Flap the arms, a random number of times.
            {
                frames = new int[5 + 4 * Rng.Next(4)];
                for (var i = 0; i < frames.Length - 1; i++)
                {
                    frames[i] = ActorWalker.DirFrame(dir, i % 2 == 0 ? UpFrame : OutFrame);
                }

                frames[^1] = ActorWalker.DirFrame(dir, 0);
                break;
            }
            case 4: // Punch: ready, raise, reach, strike, ready.
                frames = [.. new[] { 3, 4, 5, 6, 3 }.Select(f => ActorWalker.DirFrame(dir, f))];
                break;
            default:
            {
                // Spin in place in one of several frames.
                var basefr = new[] { 0, UpFrame, OutFrame }[routine];
                frames = new int[6];
                for (var i = 0; i < 5; i++)
                {
                    frames[i] = ActorWalker.DirFrame((dir + 2 * i) % 8, basefr);
                }

                frames[5] = ActorWalker.DirFrame(dir, 0);
                break;
            }
        }

        // Walk, then dance.
        var spin = new FramesAction(frames, 2 * Std);
        SetAction(PathWalk.Line(Map, Npc, dest) is { } walk ? new SequenceAction(100, walk, spin) : spin);
        Start(Std, 500);
    }
}

/// <summary>
/// Exult <c>Graze_schedule</c>: amble within 12 tiles, stand a while, then
/// graze (head down, frames 14 and 15) a few times; a fish (shape 0x1fd)
/// only now and then flips.
/// </summary>
public sealed class GrazeSchedule(NpcBrain brain) : LoiterSchedule(brain)
{
    const int FishShape = 0x1fd;
    int _phase;

    public override void NowWhat()
    {
        var delay = 2 * Std;
        var dir = ActorWalker.FacingOfFrame(Npc.Frame);
        switch (_phase)
        {
            case 0:
            {
                var dest = new TileCoord(Center.Tx - Dist + Rng.Next(2 * Dist), Center.Ty - Dist + Rng.Next(2 * Dist),
                    Center.Tz).Wrapped();
                StartAction(PathWalk.Line(Map, Npc, dest), 2 * Std, Rng.Next(2000));
                _phase++;
                break;
            }
            case 1:
            case 2:
                if (Npc.Shape == FishShape)
                {
                    if (Rng.Next(12) == 0)
                    {
                        SetAction(new FramesAction([
                            ActorWalker.DirFrame(dir, 14), ActorWalker.DirFrame(dir, 15), ActorWalker.DirFrame(dir, 0)
                        ]));
                    }
                }
                else
                {
                    Npc.Frame = ActorWalker.DirFrame(dir, 0);
                }

                _phase++;
                break;
            case 3:
                _phase = Npc.Shape == FishShape ? 0 : _phase + 1;
                break;
            default:
            {
                // Non-fish only.
                var frames = new List<int>();
                var max = 2 + Rng.Next(4);
                frames.AddRange(Enumerable.Repeat(ActorWalker.DirFrame(dir, 14), max));
                frames.AddRange(Enumerable.Repeat(ActorWalker.DirFrame(dir, 15), 2 + Rng.Next(4)));
                SetAction(new FramesAction([.. frames]));
                _phase = 4 + Rng.Next(4) < _phase ? 0 : _phase + 1;
                break;
            }
        }

        Start(delay, delay);
    }
}
