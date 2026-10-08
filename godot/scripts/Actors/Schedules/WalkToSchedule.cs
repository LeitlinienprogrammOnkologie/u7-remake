using Godot;
using U7.Core;
using U7.World;

namespace U7.Actors;

/// <summary>
/// Exult <c>Walk_to_schedule</c>: walking to the spot of the next schedule
/// (kept in <see cref="U7Object.PendingSchedule"/>). Within 3 tiles that
/// schedule starts; after 40 legs or 2 failed path searches in a row the NPC
/// is put there. A leg ends at the screen's edge (enlarged by 6 tiles) when
/// the spot lies beyond it (unless it is near or the NPC has walked 10 legs:
/// the avatar following it), comes in from off the screen when the NPC is
/// off it and the spot on it, and with both off the screen the NPC is put
/// there on the next tick. When no path is found, a straight walk.
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

        var window = Runner.WindowTiles;
        var screen = window.Grow(6);
        var from = Here(Npc);
        var to = Brain.Dest;
        if (!Pathfinder.HasTile(screen, to.Tx, to.Ty))
        {
            if (!Pathfinder.HasTile(screen, from.Tx, from.Ty))
            {
                // Put there on the next tick.
                _retries = 100;
                Start(200, 100);
                return;
            }

            if (from.Distance(to) > 80 || _legs < 10)
            {
                to = WalkOffScreen(screen, to);
            }
        }
        else if (!Pathfinder.HasTile(screen, from.Tx, from.Ty))
        {
            // (Exult walks NPCs on from off the screen, but no longer off it to walk there.)
            from = new TileCoord(-1, -1, -1);
        }

        if (PathWalk.ToTile(Map, Npc, from, to, window) is { } walk)
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

    /// <summary>Exult <c>Walk_to_schedule::walk_off_screen</c>: the screen's edge on the spot's side, the other coordinate any.</summary>
    static TileCoord WalkOffScreen(Rect2I screen, TileCoord goal)
    {
        var right = screen.Position.X + screen.Size.X;
        var bottom = screen.Position.Y + screen.Size.Y;
        if (U7Constants.TileDelta(screen.Position.X, goal.Tx) >= screen.Size.X)
        {
            return goal with { Tx = right - 1, Ty = -1 };
        }

        if (U7Constants.TileDelta(screen.Position.X, goal.Tx) < 0)
        {
            return goal with { Tx = screen.Position.X, Ty = -1 };
        }

        if (U7Constants.TileDelta(screen.Position.Y, goal.Ty) >= screen.Size.Y)
        {
            return goal with { Tx = -1, Ty = bottom - 1 };
        }

        return goal with { Tx = -1, Ty = screen.Position.Y };
    }
}
