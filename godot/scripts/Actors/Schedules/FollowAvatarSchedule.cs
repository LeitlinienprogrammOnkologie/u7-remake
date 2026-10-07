using U7.Core;

namespace U7.Actors;

/// <summary>
/// Exult <c>Follow_avatar_schedule::now_what</c> + <c>Actor::follow</c>.
/// While the avatar walks, <see cref="PartyManager"/> steps followers in
/// formation and this does nothing. Once the avatar stops, a member more
/// than a few tiles away paths to a spot beside the avatar; one far off
/// screen is brought over (Exult <c>approach_another</c>).
/// </summary>
public sealed class FollowAvatarSchedule(NpcBrain brain) : Schedule(brain)
{
    /// <summary>Exult <c>Actor::follow</c>: a stopped leader is caught up with at 100 ms a step.</summary>
    const int FollowSpeed = 100;

    public override void NowWhat()
    {
        var avatar = Runner.Avatar;
        if (Npc.IsDead || Npc.GetFlag(ObjFlag.Asleep) || Npc.GetFlag(ObjFlag.Paralyzed) ||
            ObjFlag.DontMoveMode(avatar) || (Runner.AvatarMoving?.Invoke() ?? false))
        {
            return;
        }

        var dist = Runner.Dist(Npc);
        if (dist <= 6)
        {
            if (Npc.WalkFrameIndex != 0)
            {
                ActorWalker.Stand(Npc, ActorWalker.FacingOfFrame(Npc.Frame));
            }

            return;
        }

        if (dist > 40)
        {
            if (Map.FindSpot(avatar.Tx, avatar.Ty, avatar.Tz, 8) is { } spot)
            {
                Runner.Teleport(Brain, spot);
            }

            return;
        }

        var id = Math.Max(0, Runner.Party?.PartyId(Npc) ?? 0);
        var goal = new TileCoord(
            U7Constants.WrapTile(avatar.Tx + PartyManager.XOffs[id % PartyManager.XOffs.Length] + 1 - Rng.Next(3)),
            U7Constants.WrapTile(avatar.Ty + PartyManager.YOffs[id % PartyManager.YOffs.Length] + 1 - Rng.Next(3)),
            avatar.Tz);
        Brain.Dest = goal;
        StartAction(PathWalk.Astar(Map, Npc, goal, dist: 1) ?? PathWalk.Line(Map, Npc, goal), FollowSpeed, 0,
            _ => ActorWalker.Stand(Npc, ActorWalker.FacingOfFrame(Npc.Frame)));
    }
}
