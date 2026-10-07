using U7.Core;
using U7.Data;

namespace U7.Actors;

/// <summary>
/// Exult <c>Street_maintenance_schedule</c>: an NPC gone off to tend a lamp
/// or shutter (see <see cref="Schedule.TryStreetMaintenance"/>) walks next
/// to it, reaches out and works it through its usecode (a spent candle is
/// replaced with a fresh one, lit at night), with a remark; then it goes
/// back to the schedule of the hour from where it left.
/// </summary>
public sealed class StreetMaintenanceSchedule(
    NpcBrain brain, PathWalk walk, U7Object obj, int prevType, TileCoord oldLoc) : Schedule(brain)
{
    const int SpentLight = 997;

    PathWalk? _walk = walk;
    /// <summary>What it was going to tend, as it was; 0 once done.</summary>
    int _shape = obj.Shape;
    readonly int _frame = obj.Frame;

    /// <summary>Exult <c>prev_type</c>, which usecode sees as the NPC's schedule meanwhile.</summary>
    public int PrevType => prevType;

    public override void NowWhat()
    {
        if (_walk is { } path)
        {
            // First time: follow the path.
            _walk = null;
            StartAction(path, Std, 0);
            return;
        }

        if (!obj.Removed && ObjectGeometry.Distance(Npc, obj) <= 2 && obj.Shape == _shape && obj.Frame == _frame)
        {
            Tend();
            return;
        }

        // Back to the schedule of the hour, from where it left.
        Runner.UpdateSchedule(Brain, oldLoc);
        if (Npc.ScheduleType == ScheduleType.StreetMaintenance)
        {
            Runner.SetScheduleType(Npc,
                prevType != ScheduleType.StreetMaintenance ? prevType : ScheduleType.Loiter);
        }
    }

    void Tend()
    {
        var dir = ObjectGeometry.Direction(Npc, obj);
        var standFrame = ActorWalker.DirFrame(dir, 0);
        IActorAction act;
        if (_shape == SpentLight)
        {
            // A fresh light source, lit at night; it lasts some 5-9 hours.
            var hour = Runner.Hour;
            var newShape = hour is >= 18 or < 6 ? 338 : 336;
            act = new ChangeAction(Map, obj, newShape, _frame, 30 + Rng.Next(27));
        }
        else
        {
            act = new ActivateAction(obj, Runner.Activate);
        }

        SetAction(new SequenceAction(100, new FramesAction([standFrame, ActorWalker.DirFrame(dir, 3)]),
            new FacePosAction(obj, Std), act, new FramesAction([standFrame])));
        Start(Std, 0);
        if (CanSpeak)
        {
            switch (_shape)
            {
                case 322: // Closing shutters.
                case 372:
                    Say(TextMessages.FirstCloseShutters, TextMessages.LastCloseShutters);
                    break;
                case 290: // Opening shutters.
                case 291:
                    Say(TextMessages.FirstOpenShutters, TextMessages.LastOpenShutters);
                    break;
                case 336: // Lighting a light source or lamp.
                case 889:
                    Say(TextMessages.FirstLampOn, TextMessages.LastLampOn);
                    break;
                case 338: // Putting one out.
                case 526:
                    Say(TextMessages.LampOff, TextMessages.LampOff);
                    break;
                case SpentLight:
                    Say(TextMessages.NewCandle, TextMessages.NewCandle);
                    break;
            }
        }

        _shape = 0; // Don't want to repeat.
    }
}
