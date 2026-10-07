using U7.Core;
using U7.Data;

namespace U7.Actors;

/// <summary>
/// Exult <c>Schedule</c>: what an NPC does under one schedule type. The
/// runner calls <see cref="NowWhat"/> whenever the NPC has nothing to do, and
/// <see cref="Ending"/> when it changes to another schedule.
/// </summary>
public abstract class Schedule(NpcBrain brain)
{
    protected NpcBrain Brain { get; } = brain;
    protected U7Object Npc => Brain.Npc;
    protected ScheduleRunner Runner => Brain.Runner;
    protected GameMap Map => Runner.Map;
    protected Random Rng => Runner.Rng;

    /// <summary>Exult <c>Schedule::now_what</c>: the NPC is idle; set it doing something.</summary>
    public abstract void NowWhat();

    /// <summary>Exult <c>Schedule::ending</c>: the NPC is leaving for <paramref name="newType"/>.</summary>
    public virtual void Ending(int newType)
    {
    }

    /// <summary>Set up when the schedule starts with the NPC in place: stand, facing the way it faces.</summary>
    public virtual void Begin() => ActorWalker.Stand(Npc, ActorWalker.FacingOfFrame(Npc.Frame));

    /// <summary>
    /// Exult <c>set_action</c> with <c>Actor::start(speed, delay)</c>: the
    /// action (a walk steps every <paramref name="speedMs"/>) starts once
    /// <paramref name="delayMs"/> has passed; <paramref name="done"/> runs
    /// when it ends.
    /// </summary>
    protected void StartAction(IActorAction? action, int speedMs, int delayMs, Action<IActorAction>? done = null) =>
        Brain.StartAction(action, speedMs, delayMs, done);

    /// <summary>Exult <c>Actor::start(speed, delay)</c> without a new action: look again after the delay.</summary>
    protected void Start(int speedMs, int delayMs)
    {
        Npc.FrameTime = speedMs;
        Brain.StepTimer = delayMs / 1000.0;
    }

    protected bool CanSpeak => Runner.CanSpeak?.Invoke(Npc) ?? true;

    /// <summary>Exult <c>Actor::say(from, to)</c>: one of the messages, over the NPC's head.</summary>
    protected void Say(int first, int last) => Runner.Say?.Invoke(Npc, TextMessages.Random(first, last));

    /// <summary>Exult <c>Schedule::try_proximity_usecode</c>.</summary>
    protected bool TryProximityUsecode(int odds) => Runner.TryProximityUsecode(Brain, odds);

    protected static TileCoord Here(U7Object obj) => new(obj.Tx, obj.Ty, obj.Tz);

    /// <summary>Exult <c>Game_object::find_closest</c>: objects of the shapes within <paramref name="dist"/>, closest first.</summary>
    protected List<U7Object> FindClosest(int[] shapes, int dist)
    {
        var here = Here(Npc);
        var found = shapes.SelectMany(shape => Map.FindNearby(here, shape, dist)).ToList();
        found.Sort((a, c) => Here(a).Distance(here).CompareTo(Here(c).Distance(here)));
        return found;
    }

    /// <summary>Within <paramref name="dist"/> tiles of the schedule's spot.</summary>
    protected bool AtDest(int dist) => Here(Npc).Distance2d(Brain.Dest) <= dist;

    /// <summary>Exult: what diners eat, and the plates it goes on.</summary>
    protected const int FoodShape = 377;
    protected const int PlateShape = 717;

    /// <summary>
    /// Exult <c>Sit_schedule::set_action</c>: walk to a chair and sit on it;
    /// with none given, the closest free one that can be reached. Returns
    /// the chair, or null if there is none.
    /// </summary>
    protected U7Object? SitDown(U7Object? chair, int delayMs)
    {
        if (chair is null)
        {
            foreach (var candidate in FindClosest(SitSchedule.ChairShapes, 24))
            {
                if (!SitAction.IsOccupied(Map, candidate, Npc) &&
                    PathWalk.Astar(Map, Npc, Here(candidate), dist: 1, maxBlocked: 6) is not null)
                {
                    chair = candidate;
                    break;
                }
            }

            if (chair is null)
            {
                return null;
            }
        }
        else if (SitAction.IsOccupied(Map, chair, Npc))
        {
            return null;
        }

        var sit = new SitAction(Map, chair);
        // Exult Schedule::set_action_sequence: walk there, then sit.
        StartAction(SequenceAction.WalkThen(Map, Npc, sit.SitLoc, sit), 250, delayMs);
        return chair;
    }
}

/// <summary>
/// Exult's schedule-less states: stand (no schedule at all), wait, combat
/// (run by the combat engine), and the schedules not ported yet.
/// </summary>
public sealed class IdleSchedule(NpcBrain brain) : Schedule(brain)
{
    public override void NowWhat()
    {
    }
}

/// <summary>One scheduled NPC: its schedule, its action under way, and the timers that drive them.</summary>
public sealed class NpcBrain(ScheduleRunner runner, U7Object npc)
{
    public ScheduleRunner Runner { get; } = runner;
    public U7Object Npc { get; } = npc;
    public Schedule? Schedule;
    /// <summary>Where the schedule takes place (Exult <c>schedule_loc</c>).</summary>
    public TileCoord Dest = new(npc.Tx, npc.Ty, npc.Tz);
    /// <summary>Exult <c>Loiter_schedule::center</c> and the like.</summary>
    public TileCoord Center = new(npc.Tx, npc.Ty, npc.Tz);
    /// <summary>Exult <c>Actor::action</c>.</summary>
    public IActorAction? CurrentAction;
    public Action<IActorAction>? ActionDone;
    /// <summary>Seconds until the action's next step, or until <see cref="Schedule.NowWhat"/>.</summary>
    public double StepTimer;
    public double Pause;
    public bool WasNearby;
    /// <summary>Exult <c>Npc_actor::nearby</c>: in the proximity handler, with its next look.</summary>
    public bool OnScreen;
    public double ProximityTimer;

    public void StartAction(IActorAction? action, int speedMs, int delayMs, Action<IActorAction>? done = null)
    {
        CurrentAction = action;
        ActionDone = done;
        Npc.FrameTime = action is null ? 0 : speedMs;
        StepTimer = delayMs / 1000.0;
    }

    /// <summary>Drop the action (Exult <c>set_action(nullptr)</c>): not moving any more.</summary>
    public void StopAction()
    {
        CurrentAction = null;
        ActionDone = null;
        Npc.FrameTime = 0;
    }
}
