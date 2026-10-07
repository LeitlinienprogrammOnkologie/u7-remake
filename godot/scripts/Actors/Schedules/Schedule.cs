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
    protected const int Std = U7Constants.StandardDelayMs;

    /// <summary>Exult <c>start_pos</c>: where the NPC was when the schedule began.</summary>
    public TileCoord StartPos { get; } = Here(brain.Npc);

    /// <summary>Exult <c>blocked</c>: the tile a step of the schedule's was blocked at, if any.</summary>
    protected TileCoord? Blocked;

    int _streetMaintenanceFailures;
    double _streetMaintenanceTime;

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

    /// <summary>Exult <c>Actor::set_action</c>: the action runs once the NPC is started (<see cref="Start"/>).</summary>
    protected void SetAction(IActorAction? action)
    {
        Brain.CurrentAction = action;
        Brain.ActionDone = null;
    }

    /// <summary>
    /// Exult <c>Actor::walk_path_to_tile</c>: an A* walk at
    /// <paramref name="speedMs"/> to within <paramref name="dist"/> of the
    /// tile; false if there is no path.
    /// </summary>
    protected bool WalkPathTo(TileCoord dest, int speedMs, int delayMs, int dist = 0)
    {
        if (PathWalk.Astar(Map, Npc, dest, dist) is not { } walk)
        {
            return false;
        }

        StartAction(walk, speedMs, delayMs);
        return true;
    }

    /// <summary>Exult <c>Actor::start(speed, delay)</c> without a new action: look again after the delay.</summary>
    protected void Start(int speedMs, int delayMs)
    {
        Npc.FrameTime = speedMs;
        Brain.StepTimer = delayMs / 1000.0;
    }

    protected bool CanSpeak => Runner.CanSpeak?.Invoke(Npc) ?? true;

    /// <summary>Exult <c>Actor::say(from, to)</c>: one of the messages, over the NPC's head.</summary>
    protected void Say(int first, int last) => Runner.Say?.Invoke(Npc, TextMessages.Random(first, last));

    /// <summary>Exult <c>Ucscript</c> opcodes, for the scripts schedules run on their NPC.</summary>
    protected const int ScriptRepeat = 0x0b, ScriptDontHalt = 0x23, ScriptDelayTicks = 0x27, ScriptSay = 0x52,
        ScriptSfx = 0x58, ScriptFaceDir = 0x59, ScriptStandFrame = 0x61, ScriptReadyFrame = 0x64,
        ScriptRaise1Frame = 0x65, ScriptStrike1Frame = 0x67, ScriptSitFrame = 0x6b, ScriptBowFrame = 0x6c,
        ScriptKneelFrame = 0x6d,
        ScriptOutFrame = 0x70;

    /// <summary>Exult <c>new Usecode_script(npc) ... start()</c>: run the opcodes on the NPC from the next tick.</summary>
    protected void RunScript(params object[] code) => Runner.Script?.Invoke(Npc, code);

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

    /// <summary>Exult <c>Actor::get_attack_frames</c> for a weapon or tool (not shooting), facing <paramref name="dir"/>.</summary>
    protected int[] AttackFrames(int weaponShape, int dir) =>
        Runner.Weapons is { } weapons
            ? CombatEngine.AttackFrames(Map.Catalog, weapons, Npc, weaponShape, false, dir)
            : [ActorWalker.DirFrame(dir, 3)];

    /// <summary>The NPC's carried items of a shape, nested ones too (Exult <c>get_objects</c>).</summary>
    protected List<U7Object> Carried(int shape)
    {
        var all = new List<U7Object>();
        Npc.CollectContents(all);
        return all.Where(o => o.Shape == shape && !o.Removed).ToList();
    }

    /// <summary>Exult <c>get_outermost</c> for a contained item: the container holding it all.</summary>
    protected static U7Object? Owner(U7Object item)
    {
        var owner = item.Container;
        while (owner?.Container is { } outer)
        {
            owner = outer;
        }

        return owner;
    }

    /// <summary>Exult <c>Schedule::find_nearest</c>: the object closest to the NPC.</summary>
    protected U7Object? FindNearest(IEnumerable<U7Object> nearby)
    {
        U7Object? nearest = null;
        var bestDist = 1000;
        foreach (var obj in nearby)
        {
            var dist = ObjectGeometry.Distance(obj, Npc);
            if (dist < bestDist)
            {
                nearest = obj;
                bestDist = dist;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Exult <c>Schedule::seek_foes</c>: an enemy within 20 tiles, alive, awake
    /// and visible, in a straight line, and the NPC fights it (its schedule
    /// becomes combat). False if there is none.
    /// </summary>
    protected bool SeekFoes()
    {
        if (Runner.Fight is null)
        {
            return false;
        }

        foreach (var actor in Map.FindNearby(Here(Npc), U7Constants.AnyShape, 20, 8))
        {
            if (actor == Npc || actor.IsDead || actor.GetFlag(ObjFlag.Asleep) || actor.GetFlag(ObjFlag.Invisible) ||
                !Runner.IsStraightPath(Npc, actor))
            {
                continue;
            }

            if (CombatEngine.IsEnemy(Npc.Alignment, actor.Alignment))
            {
                Runner.Fight(Npc, actor);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Exult <c>Schedule::set_procure_item_action</c>: the item if the NPC
    /// carries it, walking to pick it up if it lies about; else one of the
    /// shape it carries, or the nearest within <paramref name="dist"/>
    /// (walking to pick it up), or a new one in its inventory.
    /// </summary>
    protected U7Object SetProcureItemAction(U7Object? obj, int dist, int shape, int frame)
    {
        if (obj is { Removed: false })
        {
            if (obj.Container is not null && (Owner(obj) == Npc))
            {
                return obj;
            }

            if (obj.Container is null && SetPickupItemAction(obj, Std))
            {
                return obj;
            }
        }

        if (Carried(shape).FirstOrDefault(o => o.Frame == frame) is { } carried)
        {
            return carried;
        }

        var found = FindNearest(Map.FindNearby(Here(Npc), shape, dist).Where(o => o.Frame == frame));
        if (found is not null && !SetPickupItemAction(found, Std))
        {
            found = null;
        }

        if (found is null)
        {
            found = Map.CreateIregObject(shape, frame);
            Equipment.AddToActor(Npc, found, Map.Catalog, Map);
        }

        return found;
    }

    /// <summary>Exult <c>Schedule::set_pickup_item_action</c>: walk next to the item and pick it up; false if it cannot be reached.</summary>
    protected bool SetPickupItemAction(U7Object obj, int delayMs)
    {
        if (PathWalk.Approach(Map, Npc, obj, 1) is not { } walk)
        {
            return false;
        }

        SetAction(new SequenceAction(100, walk, new PickupAction(Map, obj, delayMs)));
        return true;
    }

    /// <summary>Exult <c>street_maintenance_time = 0</c>: look for lamps and shutters at the next chance.</summary>
    protected void ForceStreetMaintenance() => _streetMaintenanceTime = 0;

    // Exult try_street_maintenance: shutters to close and lamps to light at
    // night; shutters to open, lamps to put out and spent candles by day.
    static readonly int[] NightShapes = [322, 372, 336, 997, 889];
    static readonly int[] DayShapes = [290, 291, 338, 997, 526];

    /// <summary>
    /// Exult <c>Schedule::try_street_maintenance</c>: at most every 30 s (5 s
    /// more for every path that could not be found), an NPC near the screen
    /// looks within 20 tiles for a lamp or shutter to tend and, if it can get
    /// next to one, goes off to do it (the street maintenance schedule).
    /// True if it did; this schedule is finished then.
    /// </summary>
    protected bool TryStreetMaintenance()
    {
        var now = Runner.Ticks;
        if (now < _streetMaintenanceTime)
        {
            return false; // Not time yet.
        }

        if (!(PathWalk.IsSentient?.Invoke(Npc) ?? true))
        {
            return false; // Only want normal NPCs.
        }

        _streetMaintenanceTime = now + 30000 + _streetMaintenanceFailures * 5000;
        var hour = Runner.Hour;
        int[] shapes;
        if (hour is >= 9 and < 18)
        {
            shapes = DayShapes;
        }
        else if (hour is >= 18 or < 6)
        {
            shapes = NightShapes;
        }
        else
        {
            return false; // Dusk or dawn.
        }

        if (!Runner.InWindowAndAHalf(Npc))
        {
            return false;
        }

        foreach (var shape in shapes)
        {
            foreach (var obj in Map.FindNearby(Here(Npc), shape, 20))
            {
                if (PathWalk.Approach(Map, Npc, obj, 1) is { } walk)
                {
                    Runner.SetSchedule(Brain, ScheduleType.StreetMaintenance,
                        new StreetMaintenanceSchedule(Brain, walk, obj, Npc.ScheduleType, StartPos));
                    return true;
                }

                _streetMaintenanceFailures++;
            }
        }

        return false;
    }

    /// <summary>
    /// Exult <c>Pace_schedule::pace</c>: step on the way the NPC faces, along
    /// its line (east-west if <paramref name="horiz"/>, else north-south);
    /// blocked, ask an actor in the way to move aside, or else stop, then
    /// turn left twice to walk back. <paramref name="phase"/> 1 walks, 2-4
    /// stand and turn.
    /// </summary>
    protected void Pace(bool horiz, ref int phase, int delay)
    {
        var dir = ActorWalker.FacingOfFrame(Npc.Frame);
        switch (phase)
        {
            case 1:
            {
                var changedir = (dir is 0 or 4) == horiz;
                if (changedir)
                {
                    phase = 4;
                    Start(delay, delay);
                    return;
                }

                if (Blocked is not null)
                {
                    if (ActorWalker.FindBlocking(Map, Npc, dir) is { IsActor: true } obj && CanSpeak)
                    {
                        Say(TextMessages.FirstMoveAside, TextMessages.LastMoveAside);
                        // Ask it to move aside, and wait longer.
                        var moved = ActorWalker.MoveAside(Map, obj, Npc, dir);
                        Start(moved ? 3 * delay : delay, moved ? 3 * delay : delay);
                        return;
                    }

                    Blocked = null;
                    changedir = true;
                }

                if (changedir)
                {
                    phase++;
                }
                else
                {
                    // One step at a time (Exult Npc_actor::step, which notes where it was blocked).
                    var to = Here(Npc).Neighbor(dir);
                    var p0 = to;
                    if (ActorWalker.CanStep(Map, Npc, ref to))
                    {
                        ActorWalker.MoveTo(Map, Npc, to.Tx, to.Ty, to.Tz, dir);
                    }
                    else
                    {
                        Blocked = p0;
                    }
                }

                Start(delay, delay);
                break;
            }
            case 2:
                phase++;
                Npc.Frame = ActorWalker.DirFrame(dir, 0);
                Start(2 * delay, 2 * delay);
                break;
            case 3:
            case 4:
                phase++;
                Npc.Frame = ActorWalker.DirFrame((dir + 6) % 8, 0);
                Start(2 * delay, 2 * delay);
                break;
            default:
                phase = 1;
                Npc.WalkFrameIndex = 0;
                Start(2 * delay, 2 * delay);
                break;
        }
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
        // Exult Schedule::set_action_sequence: walk there, then sit; the
        // avatar and party walk persistently.
        var persistent = Npc.NpcNum == 0 || (Runner.Party?.IsInParty(Npc) ?? false);
        StartAction(SequenceAction.WalkThen(Map, Npc, sit.SitLoc, sit, persistent), 250, delayMs);
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
