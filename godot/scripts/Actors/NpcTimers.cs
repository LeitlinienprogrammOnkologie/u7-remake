using U7.Core;
using U7.Data;

namespace U7.Actors;

/// <summary>
/// Exult <c>Npc_timer_list</c> (<c>npctime.cc</c>) with the side effects of
/// <c>Actor::set_flag</c> and <c>Actor::clear_flag</c> that start and end
/// them: poison hurts until it wears off, sleep wears off (and mends the
/// knocked out), invisibility, protection, might, curse, charm and paralysis
/// wear off. Times run on Exult's time queue, which stands still while usecode
/// waits and in gump mode. Hunger and bleeding are not ported.
/// </summary>
public sealed class NpcTimers
{
    sealed class Timer
    {
        public required U7Object Npc;
        public required int Flag;
        public double End;
        public double Next;
    }

    /// <summary>Exult <c>Weapon_data::poison_damage</c>.</summary>
    const int PoisonDamage = 3;
    /// <summary>Exult <c>ticks_per_minute * std_delay / 2</c>: half a game minute in ms.</summary>
    const double HalfMinuteMs = 25 * U7Constants.StandardDelayMs / 2.0;
    const int InvisibilityRing = 296, ProtectionRing = 297;

    readonly Dictionary<(U7Object, int), Timer> _timers = new();
    readonly Random _rng = new();
    readonly ScheduleRunner _runner;
    readonly CombatEngine _combat;
    double _now;

    /// <summary>Exult <c>Usecode_script::terminate</c>: the scripts running on an actor end.</summary>
    public Action<U7Object>? TerminateScripts { get; set; }

    public NpcTimers(ScheduleRunner runner, CombatEngine combat)
    {
        _runner = runner;
        _combat = combat;
    }

    U7Object Avatar => _runner.Avatar;

    public void Update(double delta, bool frozen)
    {
        if (frozen || _timers.Count == 0)
        {
            return;
        }

        _now += delta * 1000;
        foreach (var timer in _timers.Values.Where(t => t.Next <= _now).ToList())
        {
            if (_timers.TryGetValue((timer.Npc, timer.Flag), out var live) && live == timer)
            {
                Handle(timer);
            }
        }
    }

    /// <summary>Exult <c>Actor::read</c>: a saved invisible or mighty actor gets its timer again (the others don't).</summary>
    public void Restore(IEnumerable<U7Object?> actors)
    {
        _timers.Clear();
        foreach (var npc in actors)
        {
            if (npc is null)
            {
                continue;
            }

            if (npc.GetFlag(ObjFlag.Invisible))
            {
                Start(npc, ObjFlag.Invisible, 0, 60000 + _rng.Next(20000));
            }

            if (npc.GetFlag(ObjFlag.Might))
            {
                Start(npc, ObjFlag.Might, 0, 60000 + _rng.Next(60000));
            }
        }
    }

    /// <summary>Exult <c>Actor::set_flag</c> for an actor; other objects just take the flag.</summary>
    public void SetFlag(U7Object obj, int flag)
    {
        if (!obj.IsActor)
        {
            obj.SetFlag(flag);
            return;
        }

        var minf = _combat.MonsterInfo(obj.Shape);
        switch (flag)
        {
            case ObjFlag.Asleep:
                if (minf.SleepSafe || minf.PowerSafe)
                {
                    return;
                }

                // Avoid waking Penumbra.
                if (obj.ScheduleType == ScheduleType.Sleep && ScheduleRunner.DontWake(_runner.Map, obj))
                {
                    break;
                }

                if (obj != Avatar && Avatar.CombatTarget == obj)
                {
                    Avatar.CombatTarget = null;
                }

                Start(obj, ObjFlag.Asleep, 0, 5000 + _rng.Next(5000));
                _runner.ClearAction(obj);
                LayDown(obj);
                break;
            case ObjFlag.Poisoned:
                if (minf.PoisonSafe)
                {
                    return;
                }

                Start(obj, ObjFlag.Poisoned, 5000, 60000 + _rng.Next(120000));
                break;
            case ObjFlag.Protection:
                Start(obj, ObjFlag.Protection, 0, 60000 + _rng.Next(20000));
                break;
            case ObjFlag.Might:
                StartFlag(obj, flag);
                break;
            case ObjFlag.Cursed:
                if (minf.CurseSafe || minf.PowerSafe)
                {
                    return;
                }

                StartFlag(obj, flag);
                break;
            case ObjFlag.Charmed:
                if (minf.CharmSafe || minf.PowerSafe)
                {
                    return;
                }

                StartFlag(obj, flag);
                StopAttacking(obj);
                obj.CombatTarget = null;
                break;
            case ObjFlag.Paralyzed:
                if (minf.ParalysisSafe || minf.PowerSafe)
                {
                    return;
                }

                StartFlag(obj, flag);
                break;
            case ObjFlag.Invisible:
                Start(obj, ObjFlag.Invisible, 0, 60000 + _rng.Next(20000));
                StopAttacking(obj, invisible: true);
                break;
        }

        obj.SetFlag(flag);
    }

    /// <summary>Exult <c>Actor::clear_flag</c>'s side effects for an actor.</summary>
    public void ClearFlag(U7Object obj, int flag)
    {
        obj.ClearFlag(flag);
        if (!obj.IsActor)
        {
            return;
        }

        if (flag == ObjFlag.Asleep)
        {
            if (obj.ScheduleType == ScheduleType.Sleep)
            {
                _runner.SetScheduleType(obj, ScheduleType.Stand);
            }
            else if ((obj.Frame & 0xf) == ActorWalker.SleepFrame)
            {
                // Find a spot to stand, at floor level.
                var floor = new TileCoord(obj.Tx, obj.Ty, obj.Tz - obj.Tz % 5);
                if (_runner.Map.FindSpot(floor, 6, obj.Shape, 0) is { } spot)
                {
                    _runner.Map.MoveObject(obj, spot.Tx, spot.Ty, spot.Tz);
                }

                obj.Frame = 0;
                obj.WalkFrameIndex = 0;
            }

            TerminateScripts?.Invoke(obj);
        }
        else if (flag == ObjFlag.Charmed)
        {
            StopAttacking(obj);
            obj.CombatTarget = null;
        }
    }

    /// <summary>Exult <c>Combat_schedule::stop_attacking_npc</c> / <c>stop_attacking_invisible</c>.</summary>
    public void StopAttacking(U7Object npc, bool invisible = false)
    {
        foreach (var actor in _runner.NearbyNpcs())
        {
            if (actor.CombatTarget == npc && (!invisible || !_combat.CanSeeInvisible(actor)))
            {
                actor.CombatTarget = null;
            }
        }
    }

    /// <summary>Exult <c>Actor::lay_down(false)</c>: kneel, the thud, and lie in the sleep frame.</summary>
    void LayDown(U7Object npc)
    {
        if (npc.GetFlag(ObjFlag.Asleep) || (npc.Frame & 0xf) == ActorWalker.SleepFrame)
        {
            return;
        }

        _runner.ClearAction(npc);
        _runner.Script?.Invoke(npc, [ScriptFinish, ScriptStandFrame, ScriptKneelFrame, ScriptSfx, LayDownSfx, ScriptSleepFrame]);
    }

    /// <summary>
    /// Exult <c>Actor::force_sleep</c> (the sleep schedule's): asleep without
    /// the immunities <see cref="SetFlag"/> asks about, the 5-10 s sleep timer
    /// (which leaves a sleep schedule's NPC asleep), the action dropped, lying down.
    /// </summary>
    public void ForceSleep(U7Object npc)
    {
        var lying = (npc.Frame & 0xf) == ActorWalker.SleepFrame;
        npc.SetFlag(ObjFlag.Asleep);
        if (npc.IsActor)
        {
            Start(npc, ObjFlag.Asleep, 0, 5000 + _rng.Next(5000));
        }

        _runner.ClearAction(npc);
        if (!lying)
        {
            _runner.Script?.Invoke(npc, [ScriptFinish, ScriptStandFrame, ScriptKneelFrame, ScriptSfx, LayDownSfx, ScriptSleepFrame]);
        }
    }

    /// <summary>Exult <c>Ucscript</c> opcodes for <c>lay_down</c>, and its sound (game sfx 86).</summary>
    const int ScriptFinish = 0x2c, ScriptSfx = 0x58, ScriptStandFrame = 0x61, ScriptKneelFrame = 0x6d,
        ScriptSleepFrame = 0x6e, LayDownSfx = 86;

    void StartFlag(U7Object npc, int flag) => Start(npc, flag, 0, 60000 + _rng.Next(60000));

    void Start(U7Object npc, int flag, double delay, double lasts) =>
        _timers[(npc, flag)] = new Timer { Npc = npc, Flag = flag, Next = _now + delay, End = _now + lasts };

    void Stop(Timer timer) => _timers.Remove((timer.Npc, timer.Flag));

    void Handle(Timer t)
    {
        var npc = t.Npc;
        if (npc.Removed && !npc.IsDead)
        {
            Stop(t);
            return;
        }

        switch (t.Flag)
        {
            case ObjFlag.Poisoned:
                // Exult Npc_poison_timer: 0-2 points every 10-20 seconds until it wears off.
                if (_now >= t.End || !npc.GetFlag(ObjFlag.Poisoned) || npc.IsDead)
                {
                    npc.ClearFlag(ObjFlag.Poisoned);
                    Stop(t);
                    return;
                }

                _combat.ReduceHealth(npc, _rng.Next(3), null, PoisonDamage);
                t.Next = _now + 10000 + _rng.Next(10000);
                return;
            case ObjFlag.Asleep:
                HandleSleep(t);
                return;
            case ObjFlag.Invisible:
            case ObjFlag.Protection:
                // Exult Npc_invisibility_timer / Npc_protection_timer: the ring keeps it.
                if (WearingRing(npc, t.Flag == ObjFlag.Invisible ? InvisibilityRing : ProtectionRing))
                {
                    Stop(t);
                    return;
                }

                if (_now >= t.End || !npc.GetFlag(t.Flag))
                {
                    npc.ClearFlag(t.Flag);
                    Stop(t);
                    return;
                }

                t.Next = _now + 2000;
                return;
            default:
                // Exult Npc_flag_timer: might, curse, charm, paralysis.
                if (_now >= t.End || !npc.GetFlag(t.Flag))
                {
                    Stop(t);
                    ClearFlag(npc, t.Flag);
                    return;
                }

                t.Next = _now + 10000;
                return;
        }
    }

    /// <summary>Exult <c>Npc_sleep_timer</c>: the knocked out mend, the rest wake when it wears off.</summary>
    void HandleSleep(Timer t)
    {
        var npc = t.Npc;
        if (npc.GetProp(ActorProp.Health) <= 0)
        {
            if (_runner.Party?.IsInParty(npc) == true || Near(npc))
            {
                // 1 in 6 every half minute = approx. 1 HP every 3 min.
                if (_rng.Next(6) == 0)
                {
                    MendWounds(npc);
                }
            }
            else
            {
                npc.SetProp(ActorProp.Health, npc.GetProp(ActorProp.Strength));
                npc.SetProp(ActorProp.Mana, npc.GetProp(ActorProp.Magic));
            }
        }

        // Don't wake up someone beaten into unconsciousness.
        if (npc.GetProp(ActorProp.Health) >= 1 && (_now >= t.End || !npc.GetFlag(ObjFlag.Asleep)))
        {
            // Avoid waking sleeping people; don't wake the dead.
            if (npc.ScheduleType != ScheduleType.Sleep && !npc.IsDead)
            {
                ClearFlag(npc, ObjFlag.Asleep);
                if ((npc.Frame & 0xf) == ActorWalker.SleepFrame)
                {
                    npc.Frame &= 0x30;
                }
            }

            Stop(t);
            return;
        }

        t.Next = _now + HalfMinuteMs;
    }

    /// <summary>Stand-in for Exult's read chunks: within the activity range of the avatar.</summary>
    bool Near(U7Object npc) => _runner.Dist(npc) <= ScheduleRunner.ActivityDist;

    /// <summary>Exult <c>Actor::mend_wounds(false)</c> (hunger and cold are not ported).</summary>
    void MendWounds(U7Object npc)
    {
        if (npc.IsDead || npc.GetFlag(ObjFlag.Poisoned))
        {
            return;
        }

        var hp = npc.GetProp(ActorProp.Health);
        var maxhp = npc.GetProp(ActorProp.Strength);
        if (maxhp > 0 && hp < maxhp)
        {
            hp += maxhp >= 3 && npc.ScheduleType == ScheduleType.Sleep ? 1 + _rng.Next(maxhp / 3) : 1;
            npc.SetProp(ActorProp.Health, Math.Min(hp, maxhp));
        }
    }

    static bool WearingRing(U7Object actor, int shape) =>
        actor.Contents.Any(o => o.ReadySlot is ReadySpot.Lfinger or ReadySpot.Rfinger && o.Shape == shape && o.Frame == 0);
}
