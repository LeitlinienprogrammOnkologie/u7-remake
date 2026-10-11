using Godot;
using U7.Core;
using U7.Data;
using U7.World;

namespace U7.Actors;

/// <summary>
/// Exult <c>Follow_avatar_schedule</c> with <c>Actor::follow</c>. A held walk
/// steps the party in formation (<see cref="PartyManager"/>); otherwise, and
/// for a member walking on its own, each one follows here: towards where the
/// avatar is going, or beside it once it has stopped. A member whose step
/// was blocked looks for a path to the avatar, else walks off the screen or
/// on from off it; one left far behind while the avatar isn't walking a path
/// catches up from off the screen or is put beside it.
/// </summary>
public sealed class FollowAvatarSchedule(NpcBrain brain) : Schedule(brain)
{
    /// <summary>Exult <c>next_path_time</c>: after a failed search, none before then (real milliseconds).</summary>
    ulong _nextPathTime;

    /// <summary>Exult <c>Follow_avatar_schedule::now_what</c>.</summary>
    public override void NowWhat()
    {
        var isBlocked = Blocked is not null;
        Blocked = null;
        var avatar = Runner.Avatar;
        if (Npc.GetFlag(ObjFlag.Asleep) || Npc.IsDead || Npc.GetFlag(ObjFlag.Paralyzed) ||
            ObjFlag.DontMoveMode(avatar))
        {
            return; // Under usecode control.
        }

        var leaderPos = Here(avatar);
        var pos = Here(Npc);
        if (avatar.FrameTime == 0 && ObjectGeometry.Distance(avatar, pos) <= 12)
        {
            return; // The avatar stopped, and we're close enough.
        }

        var now = Time.GetTicksMsec(); // Exult wants the real time here.
        if (!isBlocked)
        {
            Follow(avatar);
            return;
        }

        if (now < _nextPathTime)
        {
            // Failed pathfinding recently: wait a bit.
            Start(Std, (int)(_nextPathTime - now));
            return;
        }

        // A free spot within 3 tiles, inside or outside as the avatar is.
        if (Map.FindSpot(leaderPos, 3, Npc, 0, Map.AvatarInside(avatar)) is not { } goal)
        {
            _nextPathTime = now + 1000;
            return;
        }

        var speed = avatar.FrameTime != 0 ? avatar.FrameTime : Std;
        speed -= speed / 4;
        if (pos.Distance(goal) <= 3)
        {
            return; // Already close enough.
        }

        // Succeed if within 3 tiles of the spot.
        if (PathWalk.Astar(Map, Npc, goal, dist: 3, maxBlocked: 1) is { } walk)
        {
            StartAction(walk, speed, 0);
            return;
        }

        bool ok;
        var window = Runner.WindowTiles;
        if (Pathfinder.HasTile(window, pos.Tx - pos.Tz / 2, pos.Ty - pos.Tz / 2))
        {
            // On the screen: try walking off it.
            var off = PathWalk.ToTile(Map, Npc, pos, new TileCoord(-1, -1, -1), window);
            if (off is not null)
            {
                StartAction(off, speed, 0);
            }

            ok = off is not null;
        }
        else
        {
            ok = Runner.ApproachAnother(Npc, avatar);
        }

        if (!ok)
        {
            _nextPathTime = now + 1000; // Don't try again for a bit.
        }
    }

    /// <summary>
    /// Exult <c>Actor::follow</c>: head for where the leader is going (to
    /// within 2 tiles and a third of the party id), or, once it has stopped,
    /// for a spot by it, unless within 6 tiles already (Exult's walk in
    /// formation). A party member more than one and a half window widths
    /// away, while the leader isn't walking an A* path, catches up from off
    /// the screen or is put beside it.
    /// </summary>
    public void Follow(U7Object leader)
    {
        if (Npc.IsDead || !CombatSchedule.CanAct(Npc))
        {
            return; // Not when dead, paralyzed, or asleep.
        }

        var delay = 0;
        int dist;
        var leaderPos = Here(leader);
        var pos = Here(Npc);
        var partyId = Runner.Party?.PartyId(Npc) ?? -1;
        var leaderMoving = leader.FrameTime != 0;
        TileCoord goal;
        if (leaderMoving)
        {
            // Aim for the leader's destination.
            dist = 2 + partyId / 3;
            var dest = Runner.DestOf(leader);
            goal = new TileCoord(Approach(pos.Tx, dest.Tx, dist), Approach(pos.Ty, dest.Ty, dist), dest.Tz).Wrapped();
        }
        else
        {
            if (ObjectGeometry.Distance(Npc, leader) <= 6)
            {
                return; // In formation, and close enough.
            }

            var id = Math.Max(0, partyId) % PartyManager.XOffs.Length;
            goal = new TileCoord(leaderPos.Tx + PartyManager.XOffs[id] + 1 - Rng.Next(3),
                leaderPos.Ty + PartyManager.YOffs[id] + 1 - Rng.Next(3), leaderPos.Tz).Wrapped();
            dist = 1;
        }

        // Already aiming along a path, and the leader moving or going about there?
        if (Npc.FrameTime != 0 && Brain.CurrentAction is PathWalk { FollowingSmartPath: true } own &&
            (leaderMoving || goal.Distance(own.Dest) <= 5))
        {
            return;
        }

        var goaldist = goal.Distance(pos);
        if (goaldist < dist)
        {
            // Already close enough.
            if (!leaderMoving)
            {
                Stop();
            }

            return;
        }

        var leaderPath = Runner.ActionOf(leader) is PathWalk { FollowingSmartPath: true };
        var leaderDist = ObjectGeometry.Distance(leader, goal);
        var speed = leader.FrameTime;
        if (speed == 0)
        {
            speed = 100;
            if (goaldist < leaderDist)
            {
                delay = (1 + leaderDist - goaldist) * 100; // Closer than the leader: wait a bit.
            }
        }

        if (goaldist - leaderDist >= 5)
        {
            speed -= 20; // Speed up if too far.
        }

        var window = Runner.WindowTiles;
        if (ObjectGeometry.Distance(leader, pos) > window.Size.X + window.Size.X / 2 && partyId >= 0 && !leaderPath)
        {
            // Getting kind of far away: approach from off the screen, or teleport.
            if (Runner.ApproachAnother(Npc, leader))
            {
                return;
            }

            if (Map.FindSpot(leaderPos, 2, Npc) is { } spot)
            {
                Runner.Teleport(Brain, spot);
                if (Rng.Next(2) != 0)
                {
                    Say(TextMessages.FirstCatchup, TextMessages.LastCatchup);
                }

                return;
            }
        }

        // Exult: no delay when moving (it jerks), and no retries if blocked.
        WalkTo(goal, speed, delay);
    }

    /// <summary>
    /// Exult <c>Actor::walk_to_tile(goal, speed, delay, 0)</c>: a straight
    /// walk without retries, or more of the same kind of walk if one is under
    /// way (Exult's base action's: straight, with its 3 retries); standing
    /// still if there is none.
    /// </summary>
    void WalkTo(TileCoord goal, int speed, int delay)
    {
        var walk = Brain.CurrentAction switch
        {
            PathWalk current => current.WalkToTile(Npc, goal),
            null => PathWalk.Line(Map, Npc, goal, maxBlocked: 0),
            _ => PathWalk.Line(Map, Npc, goal)
        };
        if (walk is null)
        {
            Brain.StopAction();
            return;
        }

        StartAction(walk, speed, delay);
    }

    /// <summary>Exult <c>Actor::stop</c>: the walk's resting frame, not moving.</summary>
    void Stop()
    {
        if (Brain.CurrentAction is PathWalk walk)
        {
            walk.Stop(Npc);
        }

        Npc.FrameTime = 0;
    }

    /// <summary>Exult <c>Approach</c>: <paramref name="from"/> moved towards <paramref name="to"/> until within <paramref name="dist"/>.</summary>
    static int Approach(int from, int to, int dist)
    {
        if (from <= to)
        {
            return to - from <= dist ? from : to - dist;
        }

        return from - to <= dist ? from : to + dist;
    }
}
