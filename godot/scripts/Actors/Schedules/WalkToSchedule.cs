using U7.Core;

namespace U7.Actors;

/// <summary>
/// Exult <c>Walk_to_schedule</c>: walking to the spot of the next schedule
/// (kept in <see cref="U7Object.PendingSchedule"/>). Within 3 tiles that
/// schedule starts; after 40 legs or 2 failed path searches in a row the NPC
/// is put there; otherwise another A* leg at 200 ms a step, or a straight
/// walk when no path is found. (Exult's off-screen legs are not ported: NPCs
/// far from the avatar are placed at their spot.)
/// </summary>
public sealed class WalkToSchedule(NpcBrain brain, int firstDelay) : Schedule(brain)
{
    int _legs;
    int _retries;
    int _firstDelay = firstDelay;

    public override void Begin()
    {
    }

    public override void NowWhat()
    {
        var next = Npc.PendingSchedule >= 0 ? Npc.PendingSchedule : ScheduleType.Stand;
        if (AtDest(3))
        {
            Runner.BeginType(Brain, next, Brain.Dest, alreadyThere: true);
            return;
        }

        if (_legs >= 40 || _retries >= 2)
        {
            Runner.Teleport(Brain, Brain.Dest);
            Runner.BeginType(Brain, next, Brain.Dest, alreadyThere: true);
            return;
        }

        if (PathWalk.Astar(Map, Npc, Brain.Dest) is { } walk)
        {
            _legs++;
            _retries = 0;
            StartAction(walk, U7Constants.StandardDelayMs, _firstDelay + Rng.Next(1000));
        }
        else
        {
            _retries++;
            StartAction(PathWalk.Line(Map, Npc, Brain.Dest), U7Constants.StandardDelayMs, 1000);
        }

        _firstDelay = 0;
    }
}
