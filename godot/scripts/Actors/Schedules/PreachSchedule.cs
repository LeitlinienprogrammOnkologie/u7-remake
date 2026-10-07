using U7.Core;
using U7.Data;

namespace U7.Actors;

/// <summary>
/// Exult <c>Preach_schedule</c>: a Fellowship preacher at the podium (shape
/// 697, within 17 tiles; else it loiters) exhorts the flock with gestures
/// ("Strive for unity!"), and one of those sitting nearby rises to answer
/// ("Yea, verily!"); now and then it goes to one of them ("Art thou with us,
/// brother?"), or kneels in prayer before the icon (shape 724).
/// </summary>
public sealed class PreachSchedule(NpcBrain brain) : Schedule(brain)
{
    const int PodiumShape = 697;
    const int IconShape = 724;
    // Where to stand behind the podium, by its frame.
    static readonly int[,] Deltas = { { -1, 0 }, { 1, 0 }, { 0, -2 }, { 0, 1 } };

    enum State
    {
        FindPodium,
        AtPodium,
        Exhort,
        Visit,
        TalkMember,
        FindIcon,
        Pray
    }

    State _state;

    public override void NowWhat()
    {
        switch (_state)
        {
            case State.FindPodium:
            {
                if (Map.FindNearby(Here(Npc), PodiumShape, 17) is not [var podium, ..])
                {
                    Runner.SetScheduleType(Npc, ScheduleType.Loiter);
                    return;
                }

                var frnum = podium.Frame % 4;
                var pos = new TileCoord(podium.Tx + Deltas[frnum, 0], podium.Ty + Deltas[frnum, 1], podium.Tz).Wrapped();
                if (PathWalk.Astar(Map, Npc, pos) is { } walk)
                {
                    _state = State.AtPodium;
                    StartAction(new SequenceAction(100, walk, new FacePosAction(podium, 200)), Std, 0);
                    return;
                }

                Start(250, 5000 + Rng.Next(5000)); // Try again later.
                return;
            }
            case State.AtPodium:
                if (Rng.Next(2) != 0)
                {
                    Start(Std, Rng.Next(3000)); // Just wait a little.
                }
                else
                {
                    _state = Rng.Next(3) != 0 ? State.Exhort : Rng.Next(3) != 0 ? State.Visit : State.FindIcon;
                    Start(Std, 2000 + Rng.Next(2000));
                }

                return;
            case State.Exhort:
            {
                var dir = ActorWalker.FacingOfFrame(Npc.Frame);
                var frames = new int[1 + Rng.Next(7)];
                int[] choices = [0, 8, 9];
                for (var i = 0; i < frames.Length - 1; i++)
                {
                    frames[i] = ActorWalker.DirFrame(dir, choices[Rng.Next(choices.Length)]);
                }

                frames[^1] = ActorWalker.DirFrame(dir, 0); // End standing.
                StartAction(new FramesAction(frames, 250), Std, 0);
                Say(TextMessages.FirstPreach, TextMessages.LastPreach);
                _state = State.AtPodium;
                if (FindCongregant() is { } member)
                {
                    // Stand up, answer, sit down again.
                    Runner.Script?.Invoke(member, [
                        ScriptDelayTicks, 3, ScriptFaceDir, ActorWalker.FacingOfFrame(member.Frame), ScriptStandFrame,
                        ScriptSay, TextMessages.Random(TextMessages.FirstAmen, TextMessages.LastAmen),
                        ScriptDelayTicks, 2, ScriptSitFrame
                    ]);
                }

                return;
            }
            case State.Visit:
            {
                _state = State.FindPodium;
                Start(Std, 1000 + Rng.Next(2000));
                if (FindCongregant() is not { } member ||
                    PathWalk.Astar(Map, Npc, ObjectGeometry.Tile(member), dist: 1) is not { } walk)
                {
                    return;
                }

                SetAction(new SequenceAction(100, walk, new FacePosAction(member, 200)));
                _state = State.TalkMember;
                return;
            }
            case State.TalkMember:
                _state = State.FindPodium;
                Say(TextMessages.FirstPreach2, TextMessages.LastPreach2);
                Start(250, 2000);
                return;
            case State.FindIcon:
            {
                _state = State.FindPodium; // In case we fail.
                Start(2 * Std, 0);
                if (FindClosest([IconShape], 24) is not [var icon, ..])
                {
                    return;
                }

                var pos = new TileCoord(icon.Tx + 2, icon.Ty - 1, icon.Tz).Wrapped();
                if (PathWalk.Astar(Map, Npc, pos) is { } walk)
                {
                    SetAction(walk);
                    _state = State.Pray;
                }

                return;
            }
            case State.Pray:
                // Facing west; Exult says only the first two lines here.
                RunScript(ScriptFaceDir, 6, ScriptStandFrame, ScriptBowFrame, ScriptDelayTicks, 3, ScriptKneelFrame,
                    ScriptSay, TextMessages.Random(TextMessages.FirstAmen, TextMessages.FirstAmen + 1),
                    ScriptDelayTicks, 5, ScriptBowFrame, ScriptDelayTicks, 3, ScriptStandFrame);
                _state = State.FindPodium;
                Start(2 * Std, 4000);
                return;
        }
    }

    /// <summary>Exult <c>Find_congregant</c>: one of those sitting within 16 tiles, not of the party.</summary>
    U7Object? FindCongregant()
    {
        var flock = Map.FindNearby(Here(Npc), U7Constants.AnyShape, 16, 8)
            .Where(actor => actor.ScheduleType == ScheduleType.Sit && Runner.Party?.IsInParty(actor) != true)
            .ToList();
        return flock.Count > 0 ? flock[Rng.Next(flock.Count)] : null;
    }
}
