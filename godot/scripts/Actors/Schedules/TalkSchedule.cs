using U7.Core;
using U7.Data;
using U7.World;

namespace U7.Actors;

/// <summary>
/// Exult <c>Talk_schedule</c>: the NPC comes up to the avatar, calling out
/// now and then ("Avatar!" and the like, TEXT.FLX 0x14-0x16), and once within
/// 5 tiles and able to reach it, faces it and starts a conversation (its
/// usecode, as a double-click). Black Gate's usecode sets it for NPCs that
/// want a word.
/// </summary>
public class TalkSchedule(NpcBrain brain, int firstBark, int lastBark) : Schedule(brain)
{
    int _phase;

    public TalkSchedule(NpcBrain brain) : this(brain, TextMessages.FirstTalk, TextMessages.LastTalk)
    {
    }

    U7Object Avatar => Runner.Avatar;

    bool Reachable => FastPathClient.IsGrabable(Map, Npc, Avatar);

    public override void NowWhat()
    {
        // Close enough: talk.
        if (_phase < 3 && ObjectGeometry.Distance(Npc, Avatar) < 6)
        {
            _phase = 3;
            Start(Std, 250);
            return;
        }

        if (Avatar.GetFlag(ObjFlag.Invisible) && !(Runner.Combat?.CanSeeInvisible(Npc) ?? false))
        {
            _phase = 0; // Not to an invisible avatar; try a little later.
            Start(Std, 5000);
            return;
        }

        switch (_phase)
        {
            case 0: // Start by approaching the avatar.
            {
                if (ObjectGeometry.Distance(Npc, Avatar) > 50)
                {
                    Start(Std, 5000); // Too far; try a little later.
                    return;
                }

                // Aim for within 5 tiles.
                if (ApproachAction.Create(Map, Npc, Avatar, 5) is not { } approach)
                {
                    Start(Std, 500); // No path; try again a little later.
                    return;
                }

                CallOut();
                StartAction(approach, Std, 0);
                _phase++;
                return;
            }
            case 1: // Wait a second.
            case 2:
            {
                CallOut();
                // Step towards the avatar.
                var dx = Math.Sign(U7Constants.TileDelta(Npc.Tx, Avatar.Tx));
                var dy = Math.Sign(U7Constants.TileDelta(Npc.Ty, Avatar.Ty));
                var next = new TileCoord(Npc.Tx + dx, Npc.Ty + dy, Npc.Tz).Wrapped();
                StartAction(PathWalk.Line(Map, Npc, next), Std, 500);
                _phase = 3;
                return;
            }
            case 3: // Talk, if close and reachable.
                if (ObjectGeometry.Distance(Npc, Avatar) > 5 || !Reachable)
                {
                    _phase = 0;
                    Start(Std, 500);
                    return;
                }

                Npc.Frame = ActorWalker.DirFrame(ObjectGeometry.Direction(Npc, Avatar), 0); // But first face it.
                _phase++;
                Start(Std, 250);
                return;
            case 4:
                if (Runner.Activate?.Invoke(Npc) ?? false)
                {
                    // Exult stops the NPC and leaves the rest to the usecode.
                    Brain.StopAction();
                    _phase++;
                }
                else
                {
                    Start(Std, 500); // Usecode busy; in a moment.
                }

                return;
        }
    }

    void CallOut()
    {
        if (Reachable && Rng.Next(3) == 0)
        {
            Say(firstBark, lastBark);
        }
    }
}

/// <summary>
/// Exult <c>Arrest_avatar_schedule</c>: a guard called by
/// <see cref="Guards.CallGuards"/> comes up to the avatar like a talker,
/// calling "Surrender!" and the like (TEXT.FLX 0x17-0x1a), and its
/// double-click is Black Gate's arrest, usecode 0x625 (pay, come quietly, or
/// fight). Usecode asking its schedule is told combat. Leaving it for
/// anything but combat calms the guard (neutral).
/// </summary>
public sealed class ArrestAvatarSchedule : TalkSchedule
{
    public ArrestAvatarSchedule(NpcBrain brain) : base(brain, TextMessages.FirstArrest, TextMessages.LastArrest)
    {
        Npc.AssignedUsecode = Guards.ArrestUsecode;
    }

    public override void Ending(int newType)
    {
        if (newType != ScheduleType.Combat)
        {
            Npc.Alignment = Alignment.Neutral;
        }
    }
}
