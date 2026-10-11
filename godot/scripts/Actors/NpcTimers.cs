using U7.Core;
using U7.Data;

namespace U7.Actors;

/// <summary>
/// Exult <c>Npc_timer_list</c> (<c>npctime.cc</c>) with the side effects of
/// <c>Actor::set_flag</c> and <c>Actor::clear_flag</c> that start and end
/// them: poison hurts until it wears off, sleep wears off (and mends the
/// knocked out), invisibility, protection, might, curse, charm and paralysis
/// wear off, hunger complains and starves. Times run on Exult's time queue,
/// which stands still while usecode waits and in gump mode. Also the hourly
/// mending and food (Exult <c>mend_npcs</c>, <c>check_hunger</c>), the
/// swamps' poison, and bleeding (blood pools at the wounded's feet). Cold is
/// not ported.
/// </summary>
public sealed class NpcTimers
{
    sealed class Timer
    {
        public required U7Object Npc;
        public required int Flag;
        public double End;
        public double Next;
        /// <summary>Exult <c>Npc_hunger_timer::last_time</c>: the game minute it last acted.</summary>
        public int LastMinute;
        /// <summary>The bleeder's hit points when it last bled: more is a healing, which stops it (Exult <c>set_property(health)</c>).</summary>
        public int LastHealth;
    }

    /// <summary>Exult <c>Weapon_data::poison_damage</c> and <c>starvation_damage</c> (both 3).</summary>
    const int PoisonDamage = 3, StarvationDamage = 3;
    /// <summary>The hunger and bleeding timers' keys (Exult <c>Npc_hunger_timer</c>, <c>Npc_bleed_timer</c>), beside the flags'.</summary>
    const int Hunger = -1, Bleed = -2;
    /// <summary>Exult <c>Actor::tf_bleeding</c>: the type flag, kept in saves.</summary>
    const int TypeFlagBleeding = 10;
    /// <summary>Exult's blood (shape 912): frames 0-3 the pool's size, 32 on the other variant.</summary>
    public const int BloodShape = 912;
    /// <summary>Exult <c>items.h</c>: TEXT.FLX's hunger lines: a little hungry, must have food, starving.</summary>
    const int FirstHunger = 0x77, LastHunger = 0x79, FirstNeedFood = 0x7a, LastNeedFood = 0x7c, FirstStarving = 0x7d, LastStarving = 0x7f;
    /// <summary>Exult <c>set_property(food_level)</c>: "Never seems to get above this."</summary>
    const int MaxFood = 31;
    /// <summary>The swamp boots: Exult's <c>frame_powers</c> make them <c>swamp_safe</c>.</summary>
    const int SwampBoots = 588;
    /// <summary>Exult <c>ticks_per_minute * std_delay / 2</c>: half a game minute in ms.</summary>
    const double HalfMinuteMs = 25 * U7Constants.StandardDelayMs / 2.0;
    const int InvisibilityRing = 296, ProtectionRing = 297;

    readonly Dictionary<(U7Object, int), Timer> _timers = new();
    readonly Random _rng = new();
    readonly ScheduleRunner _runner;
    readonly CombatEngine _combat;
    readonly U7.World.GameClock _clock;
    double _now;

    /// <summary>Exult <c>Usecode_script::terminate</c>: the scripts running on an actor end.</summary>
    public Action<U7Object>? TerminateScripts { get; set; }

    public NpcTimers(ScheduleRunner runner, CombatEngine combat, U7.World.GameClock clock)
    {
        _runner = runner;
        _combat = combat;
        _clock = clock;
    }

    U7Object Avatar => _runner.Avatar;

    public void Update(double delta, bool frozen)
    {
        if (frozen || _timers.Count == 0)
        {
            return;
        }

        _now += delta * 1000;
        // The due ones first: handling a timer starts and ends others.
        _due.Clear();
        foreach (var timer in _timers.Values)
        {
            if (timer.Next <= _now)
            {
                _due.Add(timer);
            }
        }

        foreach (var timer in _due)
        {
            if (_timers.TryGetValue((timer.Npc, timer.Flag), out var live) && live == timer)
            {
                Handle(timer);
            }
        }
    }

    /// <summary>Scratch for <see cref="Update"/>, reused every frame.</summary>
    readonly List<Timer> _due = new();

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

            // Exult Actor::set_type_flags: a saved bleeder bleeds on.
            if (Bleeding(npc))
            {
                StartBleeding(npc);
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

                FallDown(obj);
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
                if (_runner.Map.FindSpot(floor, 6, obj.Shape, 0, moverFlags: obj.TypeFlags & MoveFlags.All) is { } spot)
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
            // Exult reset_effective_alignment: back on its own side.
            obj.CharmAlignment = obj.Alignment;
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
        if (npc.GetFlag(ObjFlag.Asleep))
        {
            return;
        }

        FallDown(npc);
        if ((npc.Frame & 0xf) == ActorWalker.SleepFrame)
        {
            return;
        }

        _runner.ClearAction(npc);
        _runner.Script?.Invoke(npc, [ScriptFinish, ScriptStandFrame, ScriptKneelFrame, ScriptSfx, LayDownSfx, ScriptSleepFrame]);
    }

    /// <summary>
    /// Exult <c>Actor::fall_down</c>: a flyer (its type flag) above what is
    /// below it drops onto it by walking's rules, however far, and takes 1-5
    /// normal damage.
    /// </summary>
    public void FallDown(U7Object npc)
    {
        if ((npc.TypeFlags & MoveFlags.Fly) == 0)
        {
            return;
        }

        var map = _runner.Map;
        if (!map.Blocking.IsBlocked(1, npc.Tz, npc.Tx, npc.Ty, out var lift, MoveFlags.Walk, 100) && lift < npc.Tz)
        {
            map.MoveObject(npc, npc.Tx, npc.Ty, lift);
            _combat.ReduceHealth(npc, 1 + _rng.Next(5), null);
        }
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
            case Hunger:
                HandleHunger(t);
                return;
            case Bleed:
                HandleBleeding(t);
                return;
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

    /// <summary>
    /// Exult <c>Actor::mend_wounds</c>: a hit point (more asleep) unless dead,
    /// poisoned or, in the party, hungry (food 9 or less) and not knocked out;
    /// <paramref name="mendMana"/> (the hourly mending) also some mana, and
    /// spells may be cast again. (Cold is not ported: Black Gate has none.)
    /// </summary>
    void MendWounds(U7Object npc, bool mendMana = false)
    {
        // Exult: mending stops the bleeding first of all.
        StopBleeding(npc);
        var hp = npc.GetProp(ActorProp.Health);
        var starving = npc.GetProp(ActorProp.FoodLevel) <= 9 && InParty(npc);
        if (npc.IsDead || npc.GetFlag(ObjFlag.Poisoned) || (starving && hp > 0))
        {
            return;
        }

        var maxhp = npc.GetProp(ActorProp.Strength);
        if (maxhp > 0 && hp < maxhp)
        {
            hp += maxhp >= 3 && !starving && npc.ScheduleType == ScheduleType.Sleep ? 1 + _rng.Next(maxhp / 3) : 1;
            ActorProp.Set(npc, ActorProp.Health, Math.Min(hp, maxhp));
        }

        if (!mendMana)
        {
            return;
        }

        npc.ClearFlag(ObjFlag.NoSpellCasting);
        var maxmana = npc.GetProp(ActorProp.Magic);
        var mana = npc.GetProp(ActorProp.Mana);
        if (maxmana > 0 && mana < maxmana)
        {
            mana += maxmana >= 3 ? 1 + _rng.Next(maxmana / 3) : 1;
            npc.SetProp(ActorProp.Mana, Math.Min(mana, maxmana));
        }
    }

    /// <summary>Exult <c>is_in_party</c>: the flag, which the avatar carries too.</summary>
    bool InParty(U7Object npc) => npc == Avatar || npc.GetFlag(ObjFlag.InParty) || _runner.Party?.IsInParty(npc) == true;

    /// <summary>
    /// Exult <c>Game_clock::handle_event</c> for each hour its tick passes:
    /// everyone mends (<c>Game_window::mend_npcs</c>), then the party uses
    /// food (<c>check_hunger</c>). Not when usecode moves the clock on.
    /// </summary>
    public void HourPassed(IEnumerable<U7Object?> npcs)
    {
        foreach (var npc in npcs)
        {
            if (npc is not null)
            {
                MendWounds(npc, mendMana: true);
            }
        }

        foreach (var member in (_runner.Party?.Members ?? []).Prepend(Avatar).Distinct().ToList())
        {
            UseFood(member);
        }
    }

    /// <summary>
    /// Exult <c>Actor::use_food</c>: 0-3 food an hour (1.5 on average); at 9
    /// or less the complaints and the hunger timer begin. (No Black Gate
    /// shape or gear is exempt: Exult's <c>dont_eat</c> and <c>doesnt_eat</c> are unset.)
    /// </summary>
    void UseFood(U7Object npc)
    {
        var food = Math.Clamp(npc.GetProp(ActorProp.FoodLevel) - _rng.Next(4), 0, MaxFood);
        ActorProp.Set(npc, ActorProp.FoodLevel, food);
        if (food > 9)
        {
            return;
        }

        SayHunger(npc);
        if (!_timers.ContainsKey((npc, Hunger)))
        {
            Start(npc, Hunger, 5000, double.MaxValue);
            _timers[(npc, Hunger)].LastMinute = _clock.TotalMinutes;
        }
    }

    /// <summary>Exult <c>Actor::say_hunger_message</c>: starving 3 times in 4, needing food 2 in 3, a little hungry 1 in 2.</summary>
    void SayHunger(U7Object npc)
    {
        var food = npc.GetProp(ActorProp.FoodLevel);
        var (first, last, chance) = food <= 0 ? (FirstStarving, LastStarving, 4) : food <= 4 ? (FirstNeedFood, LastNeedFood, 3) : (FirstHunger, LastHunger, 2);
        if (_rng.Next(chance) != 0)
        {
            _runner.Say?.Invoke(npc, TextMessages.Random(first, last));
        }
    }

    /// <summary>
    /// Exult <c>Npc_hunger_timer</c>: every 5 s, once a game minute, a
    /// complaint one time in three and, starving, a point of starvation damage
    /// two times in five (not while knocked out); it ends once the NPC has
    /// eaten (food over 9), left the party or died.
    /// </summary>
    void HandleHunger(Timer t)
    {
        var npc = t.Npc;
        var food = npc.GetProp(ActorProp.FoodLevel);
        if (!InParty(npc) || food > 9 || npc.IsDead)
        {
            Stop(t);
            return;
        }

        var minute = _clock.TotalMinutes;
        if (minute != t.LastMinute)
        {
            if (npc.GetProp(ActorProp.Health) > 0)
            {
                if (_rng.Next(3) == 0)
                {
                    SayHunger(npc);
                }

                if (food <= 0 && _rng.Next(5) < 2)
                {
                    _combat.ReduceHealth(npc, 1, null, StarvationDamage);
                }
            }

            t.LastMinute = minute;
        }

        t.Next = _now + 5000;
    }

    static bool Bleeding(U7Object npc) => (npc.TypeFlags & (1 << TypeFlagBleeding)) != 0;

    /// <summary>
    /// Exult <c>Actor::set_property(health)</c>: a rise in hit points stops
    /// the bleeding (its timer ends at its next step). Sets the hit points.
    /// </summary>
    public static void SetHealth(U7Object npc, int hp)
    {
        if (hp > npc.GetProp(ActorProp.Health))
        {
            npc.TypeFlags &= ~(1 << TypeFlagBleeding);
        }

        npc.SetProp(ActorProp.Health, hp);
    }

    /// <summary>
    /// Exult <c>set_type_flag(tf_bleeding)</c> and <c>Npc_bleed_timer</c>:
    /// the wound bleeds at once, then every half to one second for 1-3
    /// minutes (5 s before the second pool).
    /// </summary>
    public void StartBleeding(U7Object npc)
    {
        npc.TypeFlags |= 1 << TypeFlagBleeding;
        if (_timers.ContainsKey((npc, Bleed)))
        {
            return;
        }

        BleedOnce(npc);
        Start(npc, Bleed, 5000, 60000 + _rng.Next(120000));
        _timers[(npc, Bleed)].LastHealth = npc.GetProp(ActorProp.Health);
    }

    void StopBleeding(U7Object npc)
    {
        npc.TypeFlags &= ~(1 << TypeFlagBleeding);
        if (_timers.TryGetValue((npc, Bleed), out var t))
        {
            Stop(t);
        }
    }

    /// <summary>
    /// Exult <c>Npc_bleed_timer::handle_event</c>: it ends when its time is
    /// up, the wound was healed, the bleeder died or is out of the activity
    /// range (Exult: dormant); else another drop.
    /// </summary>
    void HandleBleeding(Timer t)
    {
        var npc = t.Npc;
        var hp = npc.GetProp(ActorProp.Health);
        if (_now >= t.End || !Bleeding(npc) || hp > t.LastHealth || npc.IsDead || !Near(npc))
        {
            StopBleeding(npc);
            return;
        }

        t.LastHealth = hp;
        BleedOnce(npc);
        t.Next = _now + 500 + _rng.Next(500);
    }

    /// <summary>
    /// Exult <c>Actor::bleed</c>: a pool of blood on the bleeder's tile grows
    /// by a frame (the variant by chance) up to frame 3; with none there, a
    /// small new one (frame 0 or 1), temporary. Creatures that can't bleed
    /// leave none.
    /// </summary>
    void BleedOnce(U7Object npc)
    {
        if (_combat.MonsterInfo(npc.Shape).CantBleed)
        {
            return;
        }

        const int firstFrame = 0, lastFrame = 3;
        var here = new TileCoord(npc.Tx, npc.Ty, npc.Tz);
        foreach (var blood in _runner.Map.FindNearby(here, BloodShape, 0, 0x80))
        {
            var frame = blood.Frame % 32;
            if (frame is < firstFrame or > lastFrame)
            {
                continue;
            }

            if (frame < lastFrame)
            {
                _runner.Map.SetFrame(blood, (frame + 1) | (_rng.Next(2) != 0 ? 32 : 0));
            }

            return;
        }

        var pool = _runner.Map.CreateIregObject(BloodShape, (firstFrame + _rng.Next(2)) | (_rng.Next(2) != 0 ? 32 : 0));
        pool.SetFlag(ObjFlag.Temporary);
        _runner.Map.PlaceInWorld(pool, npc.Tx, npc.Ty, npc.Tz);
    }

    /// <summary>
    /// Exult <c>Main_actor::step</c> / <c>Npc_actor::step</c>: a step onto
    /// poisonous ground (the swamps) at lift 0 poisons, unless swamp boots are
    /// readied (Exult's <c>swamp_safe</c> gear) or the creature can't be
    /// poisoned (<see cref="SetFlag"/>).
    /// </summary>
    public void SteppedOn(U7Object actor)
    {
        if (actor.Tz != 0 || actor.GetFlag(ObjFlag.Poisoned))
        {
            return;
        }

        var flat = _runner.Map.GetFlat(actor.Tx, actor.Ty);
        if (flat.Shape < 0 || !_runner.Map.Catalog[flat.Shape].Poisonous ||
            actor.Contents.Any(o => o.ReadySlot >= 0 && o.Shape == SwampBoots))
        {
            return;
        }

        SetFlag(actor, ObjFlag.Poisoned);
    }

    static bool WearingRing(U7Object actor, int shape) =>
        actor.Contents.Any(o => o.ReadySlot is ReadySpot.Lfinger or ReadySpot.Rfinger && o.Shape == shape && o.Frame == 0);
}
