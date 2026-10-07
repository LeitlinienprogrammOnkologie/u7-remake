using Godot;
using U7.Core;
using U7.Data;
using U7.World;

namespace U7.Actors;

/// <summary>
/// Runs the NPCs' schedules (Exult <c>Npc_actor::handle_event</c> with
/// <c>Schedule::now_what</c>, <c>schedule_npcs</c> and the proximity
/// handler). Nearby NPCs (~32 tiles) act; far NPCs sit on their current slot
/// destination until the avatar approaches.
/// </summary>
public sealed class ScheduleRunner
{
    public const int ActivityDist = U7Constants.NpcActivityDist;

    readonly ScheduleTable _table;
    readonly GameClock _clock;
    readonly Dictionary<int, NpcBrain> _brains = new();
    readonly HashSet<int> _loggedUnknown = new();
    readonly double _stepInterval = U7Constants.StandardDelayMs / 1000.0;

    public GameMap Map { get; }
    public U7Object Avatar { get; }
    public Random Rng { get; } = new();
    /// <summary>Party ids for follower spacing (Exult <c>Actor::follow</c>).</summary>
    public PartyManager? Party { get; set; }
    /// <summary>True while the avatar is walking; formation stepping drives followers then.</summary>
    public Func<bool>? AvatarMoving { get; set; }
    /// <summary>Exult <c>Actor::in_usecode_control</c>: the schedule waits while a script runs the NPC.</summary>
    public Func<U7Object, bool>? InUsecodeControl { get; set; }
    /// <summary>Queues the NPC's usecode with the npc_proximity event (Exult runs it from a script).</summary>
    public Action<U7Object>? ProximityUsecode { get; set; }
    /// <summary>Exult <c>Usecode_script</c>: runs the opcodes (ints, and strings to say) on the NPC from the next tick.</summary>
    public Action<U7Object, object[]>? Script { get; set; }
    /// <summary>Exult <c>Schedule::seek_foes</c>' end: the NPC fights the foe (combat schedule, target set).</summary>
    public Action<U7Object, U7Object>? Fight { get; set; }
    /// <summary>Exult <c>Actor::ready_best_weapon</c>.</summary>
    public Action<U7Object>? ReadyBestWeapon { get; set; }
    /// <summary>The weapon table, for attack frames.</summary>
    public WeaponTable? Weapons { get; set; }
    /// <summary>Exult <c>Game_object::activate</c>: the object's usecode as a double-click; false if usecode cannot run now.</summary>
    public Func<U7Object, bool>? Activate { get; set; }
    /// <summary>Milliseconds of game play (Exult <c>Game::get_ticks</c>, for schedule timers).</summary>
    public double Ticks { get; private set; }
    public int Hour => _clock.Hour;
    /// <summary>Exult <c>Actor::say</c>: a remark over the NPC's head.</summary>
    public Action<U7Object, string>? Say { get; set; }
    /// <summary>Exult <c>Actor::can_speak</c>.</summary>
    public Func<U7Object, bool>? CanSpeak { get; set; }
    /// <summary>The game window's size in tiles (Exult <c>get_win_tile_rect</c>), centred on the avatar.</summary>
    public (int W, int H) ScreenTiles { get; set; } = (40, 25);

    /// <param name="restore">
    /// A saved game: NPCs keep the schedules and places they were saved with
    /// (Exult <c>restore_schedule</c>); a new game sets everyone to the
    /// schedule of the hour (Exult <c>schedule_npcs</c>).
    /// </param>
    public ScheduleRunner(GameMap map, U7Object avatar, List<U7Object?> npcs,
        ScheduleTable table, GameClock clock, bool restore = false)
    {
        Map = map;
        Avatar = avatar;
        _table = table;
        _clock = clock;
        for (var i = 1; i < npcs.Count; i++)
        {
            if (npcs[i] is { Unused: false, IsDead: false } npc)
            {
                _brains[i] = new NpcBrain(this, npc);
            }
        }

        clock.SlotChanged += _ => ApplySlot(pathIfNearby: true);
        if (restore)
        {
            RestoreSchedules();
        }
        else
        {
            ApplySlot(pathIfNearby: false);
        }
    }

    /// <summary>
    /// Exult <c>Actor::set_schedule_type</c>: the schedule object for a type.
    /// Exult has none for stand; wait does nothing; combat is the combat
    /// engine's.
    /// </summary>
    Schedule Create(NpcBrain b, int type)
    {
        // Exult set_schedule_type: hands emptied for some, the best weapon readied for others.
        switch (type)
        {
            case ScheduleType.Dance:
            case ScheduleType.TendShop:
            case ScheduleType.Eat:
            case ScheduleType.Sit:
            case ScheduleType.Shy:
            case ScheduleType.Thief:
            case ScheduleType.Waiter:
            case ScheduleType.KidGames:
            case ScheduleType.EatAtInn:
            case ScheduleType.DeskWork:
            case ScheduleType.Sleep when Party?.IsInParty(b.Npc) != true:
                Equipment.EmptyHands(b.Npc, Map.Catalog, Map);
                break;
            case ScheduleType.HorizPace:
            case ScheduleType.VertPace:
            case ScheduleType.Hound:
            case ScheduleType.Preach:
            case ScheduleType.Patrol:
                ReadyBestWeapon?.Invoke(b.Npc);
                break;
        }

        switch (type)
        {
            case ScheduleType.Loiter:
                return new LoiterSchedule(b);
            case ScheduleType.TendShop:
                return new LoiterSchedule(b, 3);
            case ScheduleType.KidGames:
                return new KidGamesSchedule(b);
            case ScheduleType.Dance:
                return new DanceSchedule(b);
            case ScheduleType.Graze:
                return new GrazeSchedule(b);
            case ScheduleType.Farm:
                return new FarmerSchedule(b);
            case ScheduleType.Miner:
                return new MinerSchedule(b);
            case ScheduleType.Hound:
                return new HoundSchedule(b);
            case ScheduleType.Preach:
                return new PreachSchedule(b);
            case ScheduleType.Talk:
                return new TalkSchedule(b);
            case ScheduleType.Shy:
                return new ShySchedule(b);
            case ScheduleType.Thief:
                return new ThiefSchedule(b);
            case ScheduleType.Lab:
                return new LabSchedule(b);
            case ScheduleType.Sew:
                return new SewSchedule(b);
            case ScheduleType.Bake:
                return new BakeSchedule(b);
            case ScheduleType.Blacksmith:
                return new ForgeSchedule(b);
            case ScheduleType.Wander:
                return new WanderSchedule(b);
            case ScheduleType.Patrol:
                return new PatrolSchedule(b);
            case ScheduleType.HorizPace:
            case ScheduleType.VertPace:
                return new PaceSchedule(b, type == ScheduleType.HorizPace);
            case ScheduleType.Sleep:
                return new SleepSchedule(b);
            case ScheduleType.Sit:
                return new SitSchedule(b);
            case ScheduleType.EatAtInn:
                return new EatAtInnSchedule(b);
            case ScheduleType.Eat:
                return new EatSchedule(b);
            case ScheduleType.FollowAvatar:
                return new FollowAvatarSchedule(b);
            case ScheduleType.DeskWork:
                return new DeskSchedule(b);
            case ScheduleType.Waiter:
                return new WaiterSchedule(b);
            case ScheduleType.Stand:
            case ScheduleType.Wait:
            case ScheduleType.Combat:
            case ScheduleType.Special:
            case ScheduleType.Duel:
                return new IdleSchedule(b);
            default:
                if (_loggedUnknown.Add(type))
                {
                    GD.Print($"unknown schedule {type} npc {b.Npc.NpcNum}");
                }

                return new IdleSchedule(b);
        }
    }

    /// <summary>
    /// Exult <c>Actor::restore_schedule</c> after a load: an NPC that was
    /// walking to its next schedule's spot sets off again; everyone else stays
    /// where they were with the schedule they had (a loiterer loiters around
    /// where it stands). Party members are left alone. The schedule table
    /// takes over again at the next change of period.
    /// </summary>
    void RestoreSchedules()
    {
        foreach (var b in _brains.Values)
        {
            var npc = b.Npc;
            if (Party?.IsInParty(npc) == true || npc.ScheduleType is ScheduleType.FollowAvatar or ScheduleType.Wait)
            {
                b.Schedule = Create(b, npc.ScheduleType);
                b.WasNearby = true;
                continue;
            }

            var dest = new TileCoord(npc.ScheduleDestTx, npc.ScheduleDestTy, npc.ScheduleDestTz);
            if (dest.Tx == 0 && dest.Ty == 0)
            {
                // Never given a spot (no schedule table entries): where it is.
                dest = new TileCoord(npc.Tx, npc.Ty, npc.Tz);
            }

            var nearby = Dist(npc) <= ActivityDist;
            b.Dest = dest;
            if (npc.ScheduleType == ScheduleType.WalkToSchedule && npc.PendingSchedule >= 0)
            {
                // Exult set_schedule_and_loc: walk there, or be put there when far off.
                if (nearby)
                {
                    BeginWalkTo(b, npc.PendingSchedule, dest);
                }
                else
                {
                    Teleport(b, dest);
                    BeginType(b, npc.PendingSchedule, dest, alreadyThere: true);
                }
            }
            else
            {
                BeginType(b, npc.ScheduleType, dest, alreadyThere: true);
                b.Center = new TileCoord(npc.Tx, npc.Ty, npc.Tz);
            }

            b.WasNearby = nearby;
        }
    }

    public void SetScheduleType(U7Object npc, int type)
    {
        if (npc.NpcNum <= 0 || !_brains.TryGetValue(npc.NpcNum, out var b))
        {
            npc.ScheduleType = type;
            return;
        }

        BeginType(b, type, b.Dest, alreadyThere: true);
    }

    /// <summary>
    /// Restore a schedule after combat. Unlike <see cref="SetScheduleType"/>
    /// the NPC walks back to its slot destination if it wandered off.
    /// </summary>
    public void ResumeSchedule(U7Object npc, int type)
    {
        if (npc.NpcNum <= 0 || !_brains.TryGetValue(npc.NpcNum, out var b))
        {
            npc.ScheduleType = type;
            return;
        }

        BeginType(b, type, b.Dest, alreadyThere: false);
    }

    public int GetActualType(U7Object npc)
    {
        if (npc.ScheduleType == ScheduleType.WalkToSchedule && npc.PendingSchedule >= 0)
        {
            return npc.PendingSchedule;
        }

        if (npc.NpcNum > 0 && _brains.TryGetValue(npc.NpcNum, out var b) && b.Schedule is StreetMaintenanceSchedule s)
        {
            return s.PrevType; // Exult Street_maintenance_schedule::get_actual_type.
        }

        return npc.ScheduleType;
    }

    public void Update(double delta, bool frozen)
    {
        if (frozen)
        {
            return;
        }

        Ticks += delta * 1000;
        foreach (var b in _brains.Values)
        {
            ProximityCheck(b, delta);
            Tick(b, delta);
        }
    }

    /// <summary>
    /// Exult <c>Npc_proximity_handler</c>: while an NPC is on or near the
    /// screen it is looked at every 4-12 s (0-4 s if hostile). A sleeper
    /// within 6 tiles of the avatar, in plain view, wakes one time in three:
    /// it gets up but stays in its sleep schedule, says something, and lies
    /// down again 10 s later.
    /// </summary>
    void ProximityCheck(NpcBrain b, double delta)
    {
        var npc = b.Npc;
        if (!b.OnScreen)
        {
            if (!npc.IsDead && OnScreen(npc))
            {
                // Exult Game_window::add_nearby_npcs: it came into view.
                b.OnScreen = true;
                b.ProximityTimer = ProximityDelay(npc, 0);
            }

            return;
        }

        b.ProximityTimer -= delta;
        if (b.ProximityTimer > 0)
        {
            return;
        }

        if (!OnScreen(npc) || npc.IsDead)
        {
            b.OnScreen = false;
            return;
        }

        var extra = 5;
        if (b.Schedule is SleepSchedule sleep && Party?.IsInParty(npc) != true && !DontWake(npc) &&
            Dist(npc) < 6 && IsStraightPath(npc, Avatar) && Rng.Next(3) == 0)
        {
            sleep.WakeUp();
            if (CanSpeak?.Invoke(npc) ?? true)
            {
                Say?.Invoke(npc, TextMessages.Random(TextMessages.FirstAwakened, TextMessages.LastAwakened));
            }

            extra = 11; // And don't look again while up.
        }

        b.ProximityTimer = ProximityDelay(npc, extra);
    }

    /// <summary>Exult <c>Npc_proximity_handler::add</c>: the wait until the next look, in seconds.</summary>
    double ProximityDelay(U7Object npc, int extraTicks)
    {
        var msecs = npc.Alignment >= Alignment.Evil ? Rng.Next(2000) : 2000 + Rng.Next(4000);
        return (msecs * U7Constants.StandardDelayMs / 100 + extraTicks * U7Constants.StandardDelayMs) / 1000.0;
    }

    /// <summary>
    /// Exult's test in <c>try_street_maintenance</c>: within the game window
    /// (centred on the avatar) enlarged by a quarter of its width all round.
    /// </summary>
    public bool InWindowAndAHalf(U7Object npc)
    {
        var (w, h) = ScreenTiles;
        var dx = U7Constants.TileDelta(Avatar.Tx - w / 2 - w / 4, npc.Tx);
        var dy = U7Constants.TileDelta(Avatar.Ty - h / 2 - w / 4, npc.Ty);
        return dx >= 0 && dx < w + w / 2 && dy >= 0 && dy < h + w / 2;
    }

    /// <summary>Exult: within the game window enlarged by 10 tiles.</summary>
    bool OnScreen(U7Object npc)
    {
        var (w, h) = ScreenTiles;
        var dx = U7Constants.TileDelta(Avatar.Tx - w / 2 - 10, npc.Tx);
        var dy = U7Constants.TileDelta(Avatar.Ty - h / 2 - 10, npc.Ty);
        return dx >= 0 && dx < w + 20 && dy >= 0 && dy < h + 20;
    }

    /// <summary>Exult <c>Bg_dont_wake</c>: ghosts (translucent shapes), Horace and Penumbra sleep on.</summary>
    bool DontWake(U7Object npc) => Map.Catalog[npc.Shape].Translucent || npc.NpcNum is 141 or 150;

    /// <summary>
    /// Exult <c>Fast_pathfinder_client::is_straight_path</c> for two objects:
    /// nothing solid on the straight line between their nearest edges.
    /// </summary>
    public bool IsStraightPath(U7Object from, U7Object to)
    {
        var fromVol = Volume(from);
        var toVol = Volume(to);
        int x1 = from.Tx, y1 = from.Ty, z1 = from.Tz, x2 = to.Tx, y2 = to.Ty, z2 = to.Tz;
        // Exult Get_closest_edge.
        if (x2 < x1)
        {
            x1 = fromVol.X;
        }
        else
        {
            x2 = toVol.X;
        }

        if (y2 < y1)
        {
            y1 = fromVol.Y;
        }
        else
        {
            y2 = toVol.Y;
        }

        if (z2 < z1)
        {
            z2 += toVol.H - 1;
        }

        z1 += fromVol.H - 1;
        if (ZombieSteps.Line(new TileCoord(x1, y1, z1), new TileCoord(x2, y2, z2)) is not { } line)
        {
            return false;
        }

        while (line.NextStep(out var t, out _))
        {
            if (!fromVol.Has(t) && !toVol.Has(t) && Map.Blocking.Test(t.Tx, t.Ty, t.Tz))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Exult <c>Game_object::get_block</c>: the object's footprint and height.</summary>
    Block Volume(U7Object obj)
    {
        var info = Map.Catalog[obj.Shape];
        var reflected = (obj.Frame & 32) != 0;
        var w = Math.Max(1, reflected ? info.DimY : info.DimX);
        var d = Math.Max(1, reflected ? info.DimX : info.DimY);
        return new Block(obj.Tx - w + 1, obj.Ty - d + 1, obj.Tz, w, d, Math.Max(1, info.DimZ));
    }

    readonly record struct Block(int X, int Y, int Z, int W, int D, int H)
    {
        public bool Has(TileCoord t) =>
            t.Tx >= X && t.Tx < X + W && t.Ty >= Y && t.Ty < Y + D && t.Tz >= Z && t.Tz < Z + H;
    }

    /// <summary>
    /// Exult <c>Schedule::try_proximity_usecode</c>: one time in
    /// <paramref name="odds"/>, run the NPC's usecode for being near (mostly
    /// a remark over its head, Black Gate's 0x92E) and look again in 0.5-1.5 s.
    /// </summary>
    public bool TryProximityUsecode(NpcBrain b, int odds)
    {
        if (ProximityUsecode is null || Rng.Next(odds) != 0)
        {
            return false;
        }

        ProximityUsecode(b.Npc);
        b.Npc.FrameTime = U7Constants.StandardDelayMs;
        b.StepTimer = (500 + Rng.Next(1000)) / 1000.0;
        return true;
    }

    void ApplySlot(bool pathIfNearby)
    {
        var slot = _clock.Slot;
        foreach (var b in _brains.Values)
        {
            // Exult Game_window::schedule_npcs skips wait / follow_avatar so
            // companions (Iolo) stay with the avatar instead of Britain.
            if (b.Npc.ScheduleType is ScheduleType.Wait or ScheduleType.FollowAvatar or ScheduleType.Combat)
            {
                b.Schedule ??= Create(b, b.Npc.ScheduleType);
                continue;
            }

            var entry = _table.ForSlot(b.Npc.NpcNum, slot);
            if (entry is null)
            {
                b.Schedule ??= Create(b, b.Npc.ScheduleType);
                continue;
            }

            var dest = new TileCoord(entry.Value.Tx, entry.Value.Ty, entry.Value.Tz);
            var type = entry.Value.Type;
            var nearby = Dist(b.Npc) <= ActivityDist;
            var same = b.Npc.ScheduleType == type &&
                       b.Dest.Tx == dest.Tx && b.Dest.Ty == dest.Ty && b.Dest.Tz == dest.Tz &&
                       b.Npc.ScheduleType != ScheduleType.WalkToSchedule;
            if (same && nearby && b.Schedule is not null)
            {
                continue;
            }

            b.Dest = dest;
            b.Npc.ScheduleDestTx = dest.Tx;
            b.Npc.ScheduleDestTy = dest.Ty;
            b.Npc.ScheduleDestTz = dest.Tz;
            if (!nearby || !pathIfNearby)
            {
                Teleport(b, dest);
                BeginType(b, type, dest, alreadyThere: true);
            }
            else
            {
                BeginWalkTo(b, type, dest);
            }
        }
    }

    /// <summary>Give a resurrected NPC a schedule brain again.</summary>
    public void Revive(U7Object npc)
    {
        if (npc.NpcNum <= 0 || _brains.ContainsKey(npc.NpcNum))
        {
            return;
        }

        var b = new NpcBrain(this, npc) { WasNearby = true };
        b.Schedule = Create(b, npc.ScheduleType);
        _brains[npc.NpcNum] = b;
    }

    /// <summary>Move an NPC in place (teleport), dropping any path.</summary>
    public void TeleportNpc(U7Object npc, TileCoord dest)
    {
        if (npc.NpcNum > 0 && _brains.TryGetValue(npc.NpcNum, out var b))
        {
            Teleport(b, dest);
            return;
        }

        Map.MoveObject(npc, dest.Tx, dest.Ty, dest.Tz);
        ActorWalker.Stand(npc, 4);
    }

    void Tick(NpcBrain b, double delta)
    {
        if (b.Npc.IsDead || b.Npc.Removed)
        {
            return;
        }

        // Followers never fall back to a stale slot destination.
        var nearby = b.Npc.ScheduleType == ScheduleType.FollowAvatar || Dist(b.Npc) <= ActivityDist;
        if (!nearby)
        {
            if (b.WasNearby)
            {
                Teleport(b, b.Dest);
                if (b.Npc.PendingSchedule >= 0)
                {
                    BeginType(b, b.Npc.PendingSchedule, b.Dest, alreadyThere: true);
                }
            }

            b.WasNearby = false;
            return;
        }

        if (!b.WasNearby)
        {
            b.WasNearby = true;
            if (new TileCoord(b.Npc.Tx, b.Npc.Ty, b.Npc.Tz).Distance2d(b.Dest) > 3)
            {
                BeginWalkTo(b, b.Npc.PendingSchedule >= 0 ? b.Npc.PendingSchedule : b.Npc.ScheduleType, b.Dest);
            }
        }

        b.StepTimer -= delta;
        b.Pause -= delta;
        if (b.Pause > 0 || b.StepTimer > 0)
        {
            return;
        }

        if (InUsecodeControl?.Invoke(b.Npc) ?? false)
        {
            // Exult Actor::handle_event: keep trying every standard delay.
            b.StepTimer = _stepInterval;
            return;
        }

        if (b.CurrentAction is not null && b.Npc.ScheduleType == ScheduleType.FollowAvatar &&
            (AvatarMoving?.Invoke() ?? false))
        {
            // The avatar walks again: formation stepping takes over.
            b.StopAction();
        }

        if (b.CurrentAction is { } action)
        {
            // Exult Npc_actor::handle_event: the action's delay, then now_what once it is done.
            var d = action.HandleEvent(b.Npc);
            if (d != 0)
            {
                b.StepTimer = d / 1000.0;
                return;
            }

            b.CurrentAction = null;
            b.Npc.FrameTime = 0;
            b.StepTimer = _stepInterval;
            var done = b.ActionDone;
            b.ActionDone = null;
            done?.Invoke(action);
            return;
        }

        b.StepTimer = _stepInterval;
        (b.Schedule ??= Create(b, b.Npc.ScheduleType)).NowWhat();
    }

    /// <summary>
    /// Exult <c>Actor::set_schedule_and_loc</c>: walk to the next schedule's
    /// spot first (Exult <c>Walk_to_schedule</c>), setting off after up to 5 s.
    /// </summary>
    void BeginWalkTo(NpcBrain b, int pending, TileCoord dest)
    {
        b.Schedule?.Ending(ScheduleType.WalkToSchedule);
        b.StopAction();
        b.Dest = dest;
        b.Npc.PendingSchedule = pending;
        b.Npc.ScheduleType = ScheduleType.WalkToSchedule;
        b.Schedule = new WalkToSchedule(b, Rng.Next(5000));
        if (new TileCoord(b.Npc.Tx, b.Npc.Ty, b.Npc.Tz).Distance2d(dest) <= 3)
        {
            BeginType(b, pending, dest, alreadyThere: true);
        }
    }

    /// <summary>
    /// Exult <c>Actor::set_schedule_type</c>: end the old schedule and start
    /// the new one at <paramref name="dest"/>, walking there first unless
    /// <paramref name="alreadyThere"/>.
    /// </summary>
    public void BeginType(NpcBrain b, int type, TileCoord dest, bool alreadyThere)
    {
        b.Schedule?.Ending(type);
        b.Schedule = null;
        b.Dest = dest;
        b.Center = dest;
        b.Npc.PendingSchedule = -1;
        b.Npc.ScheduleType = type;
        b.Npc.ScheduleDestTx = dest.Tx;
        b.Npc.ScheduleDestTy = dest.Ty;
        b.Npc.ScheduleDestTz = dest.Tz;
        b.StopAction();
        if (!alreadyThere && new TileCoord(b.Npc.Tx, b.Npc.Ty, b.Npc.Tz).Distance2d(dest) > 3)
        {
            BeginWalkTo(b, type, dest);
            return;
        }

        b.Schedule = Create(b, type);
        b.Schedule.Begin();
    }

    /// <summary>
    /// Exult <c>Actor::set_schedule_type(type, newsched)</c>: end the old
    /// schedule, drop the action, and start the given one at once.
    /// </summary>
    public void SetSchedule(NpcBrain b, int type, Schedule schedule)
    {
        b.Schedule?.Ending(type);
        b.StopAction();
        b.Npc.PendingSchedule = -1;
        b.Npc.ScheduleType = type;
        b.Schedule = schedule;
        schedule.NowWhat();
    }

    /// <summary>
    /// Exult <c>Npc_actor::update_schedule(period, 0, pos)</c>: back to the
    /// schedule of the hour, at <paramref name="pos"/> instead of its spot
    /// (walking there, or put there when far off).
    /// </summary>
    public void UpdateSchedule(NpcBrain b, TileCoord? pos)
    {
        if (_table.ForSlot(b.Npc.NpcNum, _clock.Slot) is not { } entry)
        {
            return;
        }

        var dest = pos ?? new TileCoord(entry.Tx, entry.Ty, entry.Tz);
        if (Dist(b.Npc) > ActivityDist)
        {
            Teleport(b, dest);
            BeginType(b, entry.Type, dest, alreadyThere: true);
        }
        else
        {
            BeginWalkTo(b, entry.Type, dest);
        }
    }

    /// <summary>Put the NPC at the spot, standing, with its action dropped.</summary>
    public void Teleport(NpcBrain b, TileCoord dest)
    {
        Map.MoveObject(b.Npc, dest.Tx, dest.Ty, dest.Tz);
        b.StopAction();
        ActorWalker.Stand(b.Npc, 4);
    }

    /// <summary>Tiles between the NPC and the avatar.</summary>
    public int Dist(U7Object npc) =>
        new TileCoord(npc.Tx, npc.Ty, npc.Tz).Distance2d(new TileCoord(Avatar.Tx, Avatar.Ty, Avatar.Tz));
}
