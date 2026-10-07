using Godot;
using U7.Core;
using U7.Data;
using U7.World;

namespace U7.Actors;

/// <summary>
/// BG v1 combat: hatch monster eggs, melee approach/strike, HP and death.
/// Ports Exult <c>Monster_actor::create</c>, <c>roll_to_win</c>,
/// <c>apply_damage</c>, and a stripped <c>Combat_schedule</c>.
/// </summary>
public sealed class CombatEngine
{
    public const int SightRange = 24;
    /// <summary>Exult <c>combat.cc dex_to_attack</c>: dexterity points needed per strike.</summary>
    public const int DexToAttack = 30;

    readonly GameMap _map;
    readonly U7Object _avatar;
    readonly ShapeCatalog _catalog;
    readonly MonsterTable _monsters;
    readonly WeaponTable _weapons;
    readonly ArmorTable _armor;
    readonly AmmoTable _ammo = AmmoTable.Load();
    readonly List<Projectile> _projectiles = new();
    readonly List<U7Object> _spawned = new();
    readonly List<U7Object> _arena = new();
    /// <summary>Permanent NPCs pulled into combat (Exult <c>Combat_schedule</c> on a non-monster).</summary>
    readonly List<U7Object> _engaged = new();
    /// <summary>Exult <c>Combat_schedule::prev_schedule</c>, keyed by object id.</summary>
    readonly Dictionary<int, int> _prevSchedule = new();
    readonly Dictionary<int, double> _cooldown = new();
    /// <summary>Exult <c>Combat_schedule::dex_points</c>, keyed by object id.</summary>
    readonly Dictionary<int, int> _dexPoints = new();
    /// <summary>Running strike animations, keyed by object id (Exult <c>Frames_actor_action</c>).</summary>
    readonly Dictionary<int, StrikeAnim> _anims = new();
    /// <summary>Exult <c>Game_object::rotate</c>: frame band per direction 0-7 (N, NE, E, ...).</summary>
    static readonly int[] Rotate = [0, 0, 48, 48, 16, 16, 32, 32];
    /// <summary>Exult <c>fast_swing_attack_frames1</c>: ready, reach, strike.</summary>
    static readonly int[] FastSwing1 = [3, 5, 6];
    /// <summary>Exult <c>visible_frames</c>: substitute when a frame is empty (1-handed ↔ 2-handed).</summary>
    static readonly int[] VisibleFrames = [0, 0, 0, 0, 7, 8, 9, 4, 5, 6, 0, 12, 11, 0, 9, 3];
    readonly Random _rng = new();
    readonly double _stepInterval = U7Constants.StandardDelayMs / 1000.0;
    double _avatarCooldown;

    public Action<U7Object, int, int>? AvatarMoved { get; set; }
    /// <summary>Exult <c>Main_actor::die</c>: combat off, gumps closed, death usecode.</summary>
    public Action? AvatarDied { get; set; }
    public U7Object Avatar => _avatar;
    public U7.Audio.MusicPlayer? Music { get; set; }
    // Exult Combat_schedule::battle_time / battle_end_time (ms).
    ulong _battleTime = unchecked((ulong)-30000L);
    ulong _battleEndTime;
    /// <summary>Schedule runner that engaged NPCs are returned to when combat ends.</summary>
    public ScheduleRunner? Schedules { get; set; }
    /// <summary>Party members fight alongside the avatar and are targets for monsters.</summary>
    public PartyManager? Party { get; set; }
    public bool InCombat { get; private set; }
    public bool AvatarInvincible { get; private set; }
    public string LastMessage { get; private set; } = "";
    public IReadOnlyList<U7Object> Spawned => _spawned;
    public IReadOnlyList<U7Object> Engaged => _engaged;
    public WeaponTable Weapons => _weapons;
    public AmmoTable Ammo => _ammo;
    public bool IsMonsterShape(int shape) => _monsters.Contains(shape);

    public CombatEngine(GameMap map, U7Object avatar, ShapeCatalog catalog)
    {
        _map = map;
        _avatar = avatar;
        _catalog = catalog;
        _monsters = MonsterTable.Load();
        _weapons = WeaponTable.Load();
        _armor = ArmorTable.Load();
        if (_avatar.Alignment == Alignment.Neutral)
        {
            _avatar.Alignment = Alignment.Good;
        }
    }

    public void ToggleCombat() => SetInCombat(!InCombat);

    /// <summary>
    /// Exult <c>Game_window::toggle_combat</c>: party members switch between the
    /// combat and follow-avatar schedules (wait and loiter are left alone), and
    /// nobody keeps a party member as target.
    /// </summary>
    public void SetInCombat(bool on)
    {
        if (InCombat == on)
        {
            return;
        }

        InCombat = on;
        LastMessage = on ? "combat" : "peace";
        GD.Print(LastMessage);
        if (Party is not { } party)
        {
            return;
        }

        var newsched = on ? ScheduleType.Combat : ScheduleType.FollowAvatar;
        foreach (var member in party.Members)
        {
            if (member.IsDead)
            {
                continue;
            }

            var sched = member.ScheduleType;
            if (sched != newsched && sched is not (ScheduleType.Wait or ScheduleType.Loiter))
            {
                SetSchedule(member, newsched);
            }

            if (!on)
            {
                member.CombatTarget = null;
                Forget(member.Id);
            }
            else if (member.CombatTarget is { } t && party.IsInParty(t))
            {
                member.CombatTarget = null;
            }
        }

        if (!on)
        {
            _avatar.CombatTarget = null;
        }
    }

    void SetSchedule(U7Object npc, int type)
    {
        if (Schedules is { } s)
        {
            s.SetScheduleType(npc, type);
        }
        else
        {
            npc.ScheduleType = type;
        }
    }

    public void ToggleInvincible()
    {
        AvatarInvincible = !AvatarInvincible;
        LastMessage = AvatarInvincible ? "invincible" : "vulnerable";
        GD.Print(LastMessage);
    }

    public void HatchMonsterEgg(U7Object egg)
    {
        var d1 = egg.EggData1;
        var d2 = egg.EggData2;
        var d3 = egg.EggData3;
        var sched = d1 >> 8;
        var align = d1 & 3;
        var cnt = (d1 & 0xff) >> 2;
        int mshape;
        int mframe;
        if (d3 > 0)
        {
            mshape = d3;
            mframe = d2 & 0xff;
        }
        else
        {
            mshape = d2 & 1023;
            mframe = d2 >> 10;
        }

        var info = _catalog[mshape];
        if (info.IsNpcClass || _monsters.Contains(mshape))
        {
            var num = cnt;
            if (num > 1)
            {
                num = 1 + _rng.Next(num);
            }

            for (var i = 0; i < num; i++)
            {
                SpawnMonster(mshape, mframe, egg.Tx, egg.Ty, egg.Tz, sched, align);
            }
        }
        else
        {
            var obj = new U7Object
            {
                Tx = egg.Tx,
                Ty = egg.Ty,
                Tz = egg.Tz,
                Shape = mshape,
                Frame = mframe,
                Kind = ObjectKind.Ireg,
                DimX = info.DimX,
                DimY = info.DimY,
                DimZ = info.DimZ,
                Solid = info.Solid
            };
            obj.SetFlag(ObjFlag.OkayToTake);
            obj.SetFlag(ObjFlag.Temporary);
            _map.AddObject(obj);
        }
    }

    public U7Object? SpawnMonster(int shape, int frame, int tx, int ty, int tz, int sched, int align)
    {
        var spot = _map.FindSpot(tx, ty, tz, 5);
        if (spot is null)
        {
            return null;
        }

        var npc = CreateMonster(shape, frame, sched < 0 ? ScheduleType.Loiter : sched, align);
        npc.Tx = spot.Value.Tx;
        npc.Ty = spot.Value.Ty;
        npc.Tz = spot.Value.Tz;
        npc.SetFlag(ObjFlag.Temporary);
        _map.AddObject(npc);
        _spawned.Add(npc);
        StartBattle();
        LastMessage = $"spawn {npc.NpcName} at {npc.Tx},{npc.Ty} align {npc.Alignment}";
        GD.Print(LastMessage);
        return npc;
    }

    /// <summary>
    /// Exult <c>Monster_actor::create</c> without placing it: stats from
    /// monsters.csv, alignment from <paramref name="align"/> unless neutral.
    /// </summary>
    public U7Object CreateMonster(int shape, int frame, int sched, int align)
    {
        var inf = _monsters[shape];
        var rec = _catalog[shape];
        var npc = new U7Object
        {
            Shape = shape,
            Frame = frame,
            Kind = ObjectKind.Actor,
            IsActor = true,
            IsMonster = true,
            NpcNum = -1,
            Solid = false,
            DimX = rec.DimX,
            DimY = rec.DimY,
            DimZ = rec.DimZ,
            Alignment = align == Alignment.Neutral ? inf.Alignment : align,
            ScheduleType = sched,
            NpcName = string.IsNullOrEmpty(rec.Name) ? $"shape {shape}" : rec.Name
        };
        var str = RandomizeStat(inf.Strength);
        npc.SetProp(ActorProp.Strength, str);
        npc.SetProp(ActorProp.Health, str);
        npc.SetProp(ActorProp.Dexterity, RandomizeStat(inf.Dexterity));
        npc.SetProp(ActorProp.Intelligence, RandomizeStat(inf.Intelligence));
        npc.SetProp(ActorProp.Combat, RandomizeStat(inf.Combat));
        return npc;
    }

    /// <summary>Let the monster AI drive a monster that usecode placed in the world.</summary>
    public void AdoptMonster(U7Object npc)
    {
        if (npc.IsMonster && !_spawned.Contains(npc))
        {
            _spawned.Add(npc);
        }
    }

    /// <summary>Debug F3: heal avatar, spawn three chaotic rats, enter combat.</summary>
    public void SpawnArena()
    {
        ClearArena();
        _avatar.ClearFlag(ObjFlag.Dead);
        _avatar.CombatTarget = null;
        var maxHp = Math.Max(1, _avatar.GetProp(ActorProp.Strength));
        _avatar.SetProp(ActorProp.Health, maxHp);
        SetInCombat(true);

        ReadOnlySpan<(int Dx, int Dy)> spots = [(6, 0), (0, 6), (-6, 0)];
        var n = 0;
        foreach (var (dx, dy) in spots)
        {
            var rat = SpawnMonster(
                523, 0,
                _avatar.Tx + dx, _avatar.Ty + dy, _avatar.Tz,
                ScheduleType.Combat, Alignment.Chaotic);
            if (rat is null)
            {
                continue;
            }

            _arena.Add(rat);
            n++;
        }

        LastMessage = $"arena: {n} rats  hp {maxHp}";
        GD.Print(LastMessage);
    }

    public void Update(double delta, bool frozen, bool playerSteering = false)
    {
        if (frozen)
        {
            return;
        }

        PurgeDead();
        AdvanceAnims(delta);
        TickProjectiles(delta);
        foreach (var npc in _spawned)
        {
            TickFighter(npc, delta);
        }

        for (var i = _engaged.Count - 1; i >= 0; i--)
        {
            TickEngaged(_engaged[i], delta);
        }

        TickAvatar(delta, playerSteering);
        if (Party is { } party)
        {
            for (var i = party.Members.Count - 1; i >= 0; i--)
            {
                TickPartyMember(party.Members[i], delta);
            }
        }
    }

    public bool Attack(U7Object attacker, U7Object defender)
    {
        if (!attacker.IsActor || !defender.IsActor || attacker.IsDead || defender.IsDead)
        {
            return false;
        }

        SetInCombat(true);
        attacker.CombatTarget = defender;
        Engage(defender, attacker);
        StartBattle();

        var dist = new TileCoord(attacker.Tx, attacker.Ty, 0)
            .Distance2d(new TileCoord(defender.Tx, defender.Ty, 0));
        var reach = GetReach(attacker);
        if (dist > reach)
        {
            LastMessage = "out of range";
            return false;
        }

        return TryStrike(attacker, defender);
    }

    public static bool IsEnemy(int align, int other) =>
        align switch
        {
            Alignment.Good => other is Alignment.Evil or Alignment.Chaotic,
            Alignment.Evil => other is Alignment.Good or Alignment.Chaotic,
            Alignment.Chaotic => other is Alignment.Evil or Alignment.Good,
            _ => false
        };

    public static bool RollToWin(int attacker, int defender)
    {
        const int sides = 30;
        var roll = Random.Shared.Next(sides);
        if (roll == 0)
        {
            return false;
        }

        if (roll == sides - 1)
        {
            return true;
        }

        return roll + attacker - defender >= sides / 2 - 1;
    }

    public int ApplyDamage(U7Object? attacker, U7Object victim, int str, int wpoints, int type)
    {
        if (!victim.IsActor || victim.IsDead)
        {
            return 0;
        }

        var inf = _monsters[victim.Shape];
        if (inf.CantDie || (inf.Immune & (1 << type)) != 0)
        {
            return 0;
        }

        var damage = 0;
        str /= 3;
        if (wpoints >= 127)
        {
            damage = 127;
        }
        else
        {
            if (type != 3 && str > 0)
            {
                damage += 1 + _rng.Next(str);
            }

            if (wpoints > 0)
            {
                damage += 1 + _rng.Next(wpoints);
            }
        }

        var armor = inf.Armor + Equipment.WornArmor(victim, _armor, out var gearImm);
        if ((gearImm & (1 << type)) != 0)
        {
            FlashHit(victim);
            return 0;
        }

        // Exult: lightning (3), ethereal (4) and sonic (5) damage ignore armour.
        if (wpoints >= 127 || type is 3 or 4 or 5 || armor < 0)
        {
            armor = 0;
        }

        if (armor > 0)
        {
            damage -= 1 + _rng.Next(armor);
        }

        if (damage <= 0)
        {
            FlashHit(victim);
            return 0;
        }

        return ReduceHealth(victim, damage, attacker, type);
    }

    public int ReduceHealth(U7Object victim, int delta, U7Object? attacker, int type = 0)
    {
        var inf = _monsters[victim.Shape];
        if (!victim.IsActor || victim.IsDead || inf.CantDie ||
            (inf.Immune & (1 << type)) != 0)
        {
            return 0;
        }

        if ((inf.Vulnerable & (1 << type)) != 0)
        {
            delta *= 2;
        }

        if (victim.NpcNum == 0 && AvatarInvincible)
        {
            FlashHit(victim);
            victim.Bark($"{delta}");
            LastMessage = $"blocked {delta} (invincible)";
            return 0;
        }

        var hp = victim.GetProp(ActorProp.Health) - delta;
        if (hp < -50)
        {
            hp = -50;
        }

        victim.SetProp(ActorProp.Health, hp);
        FlashHit(victim);
        if (hp <= 0)
        {
            Die(victim, attacker);
        }
        else if (attacker is not null)
        {
            FightBack(victim, attacker);
        }
        else if (victim.NpcNum == 0 && delta >= Math.Max(1, victim.GetProp(ActorProp.Strength) / 3))
        {
            LastMessage = $"you take {delta} hits ({hp} hp)";
        }

        return delta;
    }

    void TickAvatar(double delta, bool playerSteering)
    {
        if (!InCombat || _avatar.IsDead)
        {
            return;
        }

        _avatarCooldown -= delta;
        if (_avatarCooldown > 0)
        {
            return;
        }

        var foe = NearestFoe(_avatar, SightRange);
        if (foe is null)
        {
            return;
        }

        _avatar.CombatTarget = foe;
        _avatarCooldown = _stepInterval;
        var dist = new TileCoord(_avatar.Tx, _avatar.Ty, 0)
            .Distance2d(new TileCoord(foe.Tx, foe.Ty, 0));
        if (dist <= GetReach(_avatar))
        {
            if (ReadyToStrike(_avatar))
            {
                TryStrike(_avatar, foe);
            }

            return;
        }

        if (playerSteering)
        {
            return;
        }

        StepToward(_avatar, foe.Tx, foe.Ty);
    }

    void TickFighter(U7Object npc, double delta)
    {
        if (npc.Removed || npc.IsDead)
        {
            return;
        }

        var target = NearestPartyTarget(npc, SightRange);
        if (target is null)
        {
            return;
        }

        var dist = new TileCoord(npc.Tx, npc.Ty, 0)
            .Distance2d(new TileCoord(target.Tx, target.Ty, 0));
        npc.CombatTarget = target;
        npc.ScheduleType = ScheduleType.Combat;
        SetInCombat(true);
        if (!_cooldown.TryGetValue(npc.Id, out var cd))
        {
            cd = 0;
        }

        cd -= delta;
        if (cd > 0)
        {
            _cooldown[npc.Id] = cd;
            return;
        }

        _cooldown[npc.Id] = _stepInterval;
        if (dist <= GetReach(npc))
        {
            if (ReadyToStrike(npc))
            {
                TryStrike(npc, target);
            }

            return;
        }

        StepToward(npc, target.Tx, target.Ty);
    }

    /// <summary>
    /// Put a permanent NPC into the combat schedule and remember what it was
    /// doing, like Exult <c>Actor::set_target(…, start_combat)</c>.
    /// Spawned monsters are already driven by <see cref="TickFighter"/>.
    /// </summary>
    void Engage(U7Object npc, U7Object target)
    {
        npc.CombatTarget = target;
        if (npc.NpcNum == 0 || _spawned.Contains(npc) || _engaged.Contains(npc))
        {
            return;
        }

        _prevSchedule[npc.Id] = npc.ScheduleType;
        if (npc.ScheduleType != ScheduleType.Combat)
        {
            if (Schedules is { } sched)
            {
                sched.SetScheduleType(npc, ScheduleType.Combat);
            }
            else
            {
                npc.ScheduleType = ScheduleType.Combat;
            }
        }

        _engaged.Add(npc);
    }

    /// <summary>
    /// Exult <c>Combat_schedule::now_what</c> with no opponents: return the
    /// NPC to its previous schedule (unless it was combat to begin with).
    /// </summary>
    void Disengage(U7Object npc)
    {
        _engaged.Remove(npc);
        Forget(npc.Id);
        npc.CombatTarget = null;
        if (!_prevSchedule.Remove(npc.Id, out var prev) || npc.IsDead || npc.Removed)
        {
            return;
        }

        if (prev == ScheduleType.Combat)
        {
            return;
        }

        if (Schedules is { } sched)
        {
            sched.ResumeSchedule(npc, prev);
        }
        else
        {
            npc.ScheduleType = prev;
        }
    }

    /// <summary>Exult <c>Actor::fight_back</c>: a hit NPC targets its attacker.</summary>
    void FightBack(U7Object victim, U7Object attacker)
    {
        if (!attacker.IsActor || !victim.IsActor ||
            !IsEnemy(victim.Alignment, attacker.Alignment))
        {
            return;
        }

        if (victim.NpcNum == 0 || Party?.IsInParty(victim) == true)
        {
            SetInCombat(true);
            return;
        }

        if (victim.CombatTarget is not null)
        {
            return;
        }

        SetInCombat(true);
        Engage(victim, attacker);
    }

    /// <summary>
    /// Exult <c>Combat_schedule</c> for a party member: chase and strike the
    /// nearest foe in sight. With nothing to fight a member drops back to the
    /// follow-avatar schedule (combat.cc now_what), and while combat mode is on a
    /// follower that sees a foe joins the fight.
    /// </summary>
    void TickPartyMember(U7Object npc, double delta)
    {
        if (npc.Removed || npc.IsDead || npc.GetFlag(ObjFlag.Asleep) || npc.GetFlag(ObjFlag.Paralyzed))
        {
            return;
        }

        var sched = npc.ScheduleType;
        if (sched is not (ScheduleType.Combat or ScheduleType.FollowAvatar))
        {
            return;
        }

        var foe = NearestFoe(npc, SightRange);
        if (foe is null)
        {
            if (sched == ScheduleType.Combat)
            {
                npc.CombatTarget = null;
                SetSchedule(npc, ScheduleType.FollowAvatar);
            }

            return;
        }

        if (sched == ScheduleType.FollowAvatar)
        {
            if (!InCombat)
            {
                return;
            }

            SetSchedule(npc, ScheduleType.Combat);
        }

        npc.CombatTarget = foe;
        if (!_cooldown.TryGetValue(npc.Id, out var cd))
        {
            cd = 0;
        }

        cd -= delta;
        if (cd > 0)
        {
            _cooldown[npc.Id] = cd;
            return;
        }

        _cooldown[npc.Id] = _stepInterval;
        var dist = new TileCoord(npc.Tx, npc.Ty, 0)
            .Distance2d(new TileCoord(foe.Tx, foe.Ty, 0));
        if (dist <= GetReach(npc))
        {
            if (ReadyToStrike(npc))
            {
                TryStrike(npc, foe);
            }

            return;
        }

        StepToward(npc, foe.Tx, foe.Ty);
    }

    /// <summary>Nearest living party member (avatar included) that is an enemy of <paramref name="npc"/>.</summary>
    U7Object? NearestPartyTarget(U7Object npc, int maxDist)
    {
        U7Object? best = null;
        var bestD = int.MaxValue;
        Consider(_avatar);
        if (Party is { } party)
        {
            foreach (var m in party.Members)
            {
                Consider(m);
            }
        }

        return best;

        void Consider(U7Object cand)
        {
            if (cand.IsDead || cand.Removed || !IsEnemy(npc.Alignment, cand.Alignment))
            {
                return;
            }

            var d = new TileCoord(npc.Tx, npc.Ty, 0).Distance2d(new TileCoord(cand.Tx, cand.Ty, 0));
            if (d <= maxDist && d < bestD)
            {
                bestD = d;
                best = cand;
            }
        }
    }

    void TickEngaged(U7Object npc, double delta)
    {
        if (npc.Removed || npc.IsDead)
        {
            Disengage(npc);
            return;
        }

        var target = npc.CombatTarget is { IsDead: false, Removed: false } t ? t : NearestPartyTarget(npc, SightRange) ?? _avatar;
        var dist = new TileCoord(npc.Tx, npc.Ty, 0)
            .Distance2d(new TileCoord(target.Tx, target.Ty, 0));
        if (target.IsDead || target.Removed || dist > SightRange ||
            !IsEnemy(npc.Alignment, target.Alignment))
        {
            Disengage(npc);
            return;
        }

        if (!_cooldown.TryGetValue(npc.Id, out var cd))
        {
            cd = 0;
        }

        cd -= delta;
        if (cd > 0)
        {
            _cooldown[npc.Id] = cd;
            return;
        }

        _cooldown[npc.Id] = _stepInterval;
        if (dist <= GetReach(npc))
        {
            if (ReadyToStrike(npc))
            {
                TryStrike(npc, target);
            }

            return;
        }

        StepToward(npc, target.Tx, target.Ty);
    }

    /// <summary>
    /// Exult <c>Combat_schedule::now_what</c> approach state: each standard
    /// tick in reach either strikes (spending <see cref="DexToAttack"/>
    /// points) or banks the actor's dexterity and waits.
    /// </summary>
    bool ReadyToStrike(U7Object actor)
    {
        _dexPoints.TryGetValue(actor.Id, out var pts);
        if (pts >= DexToAttack)
        {
            _dexPoints[actor.Id] = pts - DexToAttack;
            return true;
        }

        _dexPoints[actor.Id] = pts + Math.Max(1, actor.GetProp(ActorProp.Dexterity));
        return false;
    }

    void Forget(int id)
    {
        _cooldown.Remove(id);
        _dexPoints.Remove(id);
        _anims.Remove(id);
    }

    void StepToward(U7Object actor, int tx, int ty)
    {
        var fromTx = actor.Tx;
        var fromTy = actor.Ty;
        var step = Pathfinder.GreedyStep(_map, actor.Tx, actor.Ty, tx, ty, actor.Tz);
        var dx = Math.Sign(U7Constants.TileDelta(actor.Tx, step.X));
        var dy = Math.Sign(U7Constants.TileDelta(actor.Ty, step.Y));
        if (dx == 0 && dy == 0)
        {
            return;
        }

        ActorWalker.TryStep(_map, actor, dx, dy);
        if (actor.NpcNum == 0 && (actor.Tx != fromTx || actor.Ty != fromTy))
        {
            AvatarMoved?.Invoke(actor, fromTx, fromTy);
        }
    }

    /// <summary>Restore spawned monsters from a saved game (MONSNPCS.DAT).</summary>
    public void AdoptMonsters(IEnumerable<U7Object> monsters)
    {
        foreach (var m in monsters)
        {
            if (!m.Removed && !m.IsDead)
            {
                _spawned.Add(m);
            }
        }
    }

    /// <summary>
    /// Exult <c>Combat_schedule::start_battle</c>: when the avatar has a foe and
    /// no battle music played in the last 30 s, play one of the two attack tracks.
    /// </summary>
    void StartBattle()
    {
        if (Music is null)
        {
            return;
        }

        var now = Time.GetTicksMsec();
        if (now - _battleTime < 30000 || NearestFoe(_avatar, SightRange) is null)
        {
            return;
        }

        Music.Start(_rng.Next(2) == 0 ? 11 : 12, repeat: false);
        _battleTime = now;
        _battleEndTime = now - 1;
    }

    /// <summary>Exult <c>Combat_schedule::monster_died</c>: victory music once no hostiles remain.</summary>
    void MonsterDied()
    {
        if (Music is null || _battleEndTime >= _battleTime)
        {
            return;
        }

        foreach (var npc in Fighters())
        {
            if (!npc.IsDead && !npc.Removed && npc.Alignment >= Alignment.Evil)
            {
                return;
            }
        }

        _battleEndTime = Time.GetTicksMsec();
        var len = (_battleEndTime - _battleTime) / 1000;
        var hard = len > 15 && (ulong)_rng.Next(60) < len;
        Music.Start(hard ? 9 : 15, repeat: false);
    }

    /// <summary>True while a strike animation owns the actor's frame.</summary>
    public bool IsAnimating(U7Object actor) => _anims.ContainsKey(actor.Id);

    /// <summary>
    /// Exult <c>Combat_schedule::start_strike</c>: face the target and play the
    /// swing frames one per standard tick, ending on the ready frame. Frames the
    /// shape lacks fall back to standing, as <c>Actor::get_attack_frames</c> does.
    /// </summary>
    void PlayStrike(U7Object attacker, U7Object defender)
    {
        var dir = ActorWalker.DirIndex(
            Math.Sign(U7Constants.TileDelta(attacker.Tx, defender.Tx)),
            Math.Sign(U7Constants.TileDelta(attacker.Ty, defender.Ty)));
        var band = Rotate[dir];
        var rec = _catalog[attacker.Shape];
        var frames = new int[FastSwing1.Length + 1];
        for (var i = 0; i < FastSwing1.Length; i++)
        {
            var fr = FastSwing1[i] + band;
            if (!HasFrame(rec, fr))
            {
                fr = VisibleFrames[fr & 15] + band;
                if (!HasFrame(rec, fr))
                {
                    fr = band;
                }
            }

            frames[i] = fr;
        }

        frames[^1] = frames[0]; // back to ready (or standing if ready is missing)
        attacker.WalkFrameIndex = 0;
        attacker.Frame = frames[0];
        _anims[attacker.Id] = new StrikeAnim(attacker, frames, 1, _stepInterval);
    }

    /// <summary>Exult <c>Shape_frame::is_empty</c>; the export writes empty frames as 1×1.</summary>
    static bool HasFrame(ShapeRecord rec, int frame)
    {
        if ((frame & 0x1f) >= rec.FrameCount)
        {
            return false;
        }

        var fi = rec.GetFrame(frame);
        return fi.Width > 1 || fi.Height > 1;
    }

    void AdvanceAnims(double delta)
    {
        if (_anims.Count == 0)
        {
            return;
        }

        List<int>? done = null;
        foreach (var (id, anim) in _anims)
        {
            anim.Timer -= delta;
            if (anim.Timer > 0)
            {
                continue;
            }

            if (anim.Index >= anim.Frames.Length || anim.Actor.IsDead || anim.Actor.Removed)
            {
                (done ??= new List<int>()).Add(id);
                continue;
            }

            anim.Actor.Frame = anim.Frames[anim.Index++];
            anim.Timer = _stepInterval;
        }

        if (done is not null)
        {
            foreach (var id in done)
            {
                _anims.Remove(id);
            }
        }
    }

    sealed class StrikeAnim(U7Object actor, int[] frames, int index, double timer)
    {
        public U7Object Actor { get; } = actor;
        public int[] Frames { get; } = frames;
        public int Index { get; set; } = index;
        public double Timer { get; set; } = timer;
    }

    /// <summary>
    /// Exult <c>Combat_schedule::attack_target</c>: melee strike, or a projectile
    /// for ranged and thrown weapons, consuming ammunition or charges.
    /// </summary>
    bool TryStrike(U7Object attacker, U7Object defender)
    {
        PlayStrike(attacker, defender);
        var wpn = GetWeapon(attacker, out var points, out var weaponShape);
        var dist = new TileCoord(attacker.Tx, attacker.Ty, 0)
            .Distance2d(new TileCoord(defender.Tx, defender.Ty, 0));
        int reach;
        var family = -1;
        var proj = -1;
        if (wpn is null)
        {
            reach = Math.Max(1, _monsters[attacker.Shape].Reach);
        }
        else
        {
            reach = wpn.Range;
            proj = wpn.Projectile;
            family = wpn.Ammo;
        }

        var ranged = wpn is { Uses: WeaponRecord.UsesRanged } || dist > reach;
        if (EffectiveRange(attacker, wpn, reach) < dist)
        {
            LastMessage = "out of range";
            return false;
        }

        var needAmmo = GetWeaponAmmo(attacker, wpn, weaponShape, family, proj, ranged, out var ammoObj);
        if (needAmmo > 0 && ammoObj is null)
        {
            LastMessage = $"{NameOf(attacker)}: out of ammo";
            if (attacker.NpcNum >= 0 && wpn is not null)
            {
                Equipment.ReadyBestWeapon(attacker, _catalog, _weapons, _armor);
            }

            return false;
        }

        if (proj == -3)
        {
            proj = weaponShape;
        }

        AmmoRecord? ainf;
        int basesprite;
        if (needAmmo > 0 && family >= 0 && ammoObj is not null)
        {
            ainf = _ammo[ammoObj.Shape];
            basesprite = ammoObj.Shape;
        }
        else
        {
            ainf = weaponShape >= 0 ? _ammo[weaponShape] : null;
            basesprite = weaponShape;
        }

        if (ainf is not null)
        {
            if (ainf.Sprite == -3)
            {
                proj = basesprite;
            }
            else if (ainf.Sprite != -1 && ainf.Sprite != ainf.Family)
            {
                proj = ainf.Sprite;
            }
        }

        var ammoShape = ammoObj?.Shape ?? proj;
        var returns = wpn is { Returns: true } || ainf is { Returns: true };
        if (needAmmo > 0 && ammoObj is not null && wpn is not null)
        {
            ConsumeAmmo(attacker, wpn, ammoObj, needAmmo, returns);
        }

        var attval = attacker.GetProp(ActorProp.Combat);
        if (wpn is { Lucky: true })
        {
            attval += 3;
        }

        if (ainf is { Lucky: true })
        {
            attval += 3;
        }

        if (ranged)
        {
            attval += 6;
            if (wpn is { Uses: WeaponRecord.UsesPoorThrown })
            {
                attval -= dist;
            }
            else if (wpn is { Uses: WeaponRecord.UsesGoodThrown })
            {
                attval -= dist / 2;
            }

            LaunchProjectile(attacker, defender, wpn, weaponShape, ammoShape, proj, attval, returns);
            return true;
        }

        var autohit = wpn is { Autohit: true } || ainf is { Autohit: true };
        if (!autohit && defender.IsActor &&
            !RollToWin(attval, defender.GetProp(ActorProp.Combat)))
        {
            defender.Bark("miss");
            LastMessage = $"{NameOf(attacker)} misses {NameOf(defender)}";
            return false;
        }

        return HitWith(attacker, defender, wpn, points, ammoShape >= 0 && needAmmo > 0 ? _ammo[ammoShape] : null);
    }

    /// <summary>Exult <c>Actor::figure_hit_points</c> weapon part: weapon damage plus ammo damage, ammo type overrides.</summary>
    bool HitWith(U7Object attacker, U7Object defender, WeaponRecord? wpn, int wpoints, AmmoRecord? ainf)
    {
        var type = wpn?.DamageType ?? 0;
        if (ainf is not null)
        {
            wpoints += ainf.Damage;
            if (ainf.DamageType != 0)
            {
                type = ainf.DamageType;
            }
        }

        var hits = ApplyDamage(attacker, defender, attacker.GetProp(ActorProp.Strength), wpoints, type);
        if (hits > 0 && !defender.IsDead)
        {
            defender.Bark($"{hits}");
            LastMessage = $"{NameOf(attacker)} hits {NameOf(defender)} for {hits}";
        }

        return hits > 0;
    }

    /// <summary>Exult <c>Actor::get_effective_range</c>: thrown weapons reach as far as strength/combat allow.</summary>
    int EffectiveRange(U7Object actor, WeaponRecord? wpn, int reach)
    {
        var uses = wpn?.Uses ?? WeaponRecord.UsesMelee;
        if (uses is WeaponRecord.UsesMelee or WeaponRecord.UsesRanged)
        {
            return reach;
        }

        var eff = Math.Min(actor.GetProp(ActorProp.Strength), actor.GetProp(ActorProp.Combat));
        if (uses == WeaponRecord.UsesGoodThrown)
        {
            eff *= 2;
        }

        return Math.Clamp(Math.Max(eff, reach), 1, 31);
    }

    /// <summary>Exult <c>Game_object::get_weapon_ammo</c>: how much ammo a shot needs and where it is.</summary>
    int GetWeaponAmmo(U7Object actor, WeaponRecord? wpn, int weaponShape, int family, int proj, bool ranged, out U7Object? ammo)
    {
        ammo = null;
        if (wpn is null || weaponShape < 0)
        {
            return 0;
        }

        var needAmmo = family == -1 || !ranged
            ? (wpn.Uses == WeaponRecord.UsesMelee && wpn.UsesCharges ? 1 : 0)
            : 1;
        if (needAmmo > 0)
        {
            ammo = FindWeaponAmmo(actor, weaponShape, wpn, family, needAmmo);
        }

        return needAmmo;
    }

    /// <summary>Exult <c>Actor::find_weapon_ammo</c> / <c>find_best_ammo</c>.</summary>
    U7Object? FindWeaponAmmo(U7Object actor, int weaponShape, WeaponRecord wpn, int family, int needed)
    {
        if (family >= 0)
        {
            var quiver = Equipment.GetReadied(actor, ReadySpot.Ammo);
            if (quiver is { Removed: false } && _ammo.InFamily(quiver.Shape, family) &&
                Inventory.GetQuantity(quiver, _catalog) >= needed)
            {
                return quiver;
            }

            foreach (var obj in AllPossessions(actor))
            {
                if (!_ammo.InFamily(obj.Shape, family) || _ammo[obj.Shape] is null)
                {
                    continue;
                }

                if (Inventory.GetQuantity(obj, _catalog) >= needed)
                {
                    return obj;
                }
            }

            return null;
        }

        foreach (var spot in new[] { ReadySpot.Lhand, ReadySpot.Rhand, ReadySpot.Back2h, ReadySpot.Belt })
        {
            var obj = Equipment.GetReadied(actor, spot);
            if (obj is null || obj.Removed || obj.Shape != weaponShape)
            {
                continue;
            }

            if (family == -2)
            {
                if (!_catalog[obj.Shape].HasQuality || obj.Quality >= needed)
                {
                    return obj;
                }
            }
            else if (Inventory.GetQuantity(obj, _catalog) >= needed)
            {
                return obj;
            }
        }

        return null;
    }

    static IEnumerable<U7Object> AllPossessions(U7Object container)
    {
        foreach (var obj in container.Contents)
        {
            if (obj.Removed)
            {
                continue;
            }

            yield return obj;
            foreach (var inner in AllPossessions(obj))
            {
                yield return inner;
            }
        }
    }

    /// <summary>Exult <c>attack_target</c> ammo bookkeeping: charges or quantity, re-ready when the stack is gone.</summary>
    void ConsumeAmmo(U7Object attacker, WeaponRecord wpn, U7Object ammoObj, int needAmmo, bool returns)
    {
        var ready = ammoObj.ReadySlot >= 0;
        var needNewWeapon = false;
        if (wpn.UsesCharges)
        {
            if (_catalog[ammoObj.Shape].HasQuality)
            {
                ammoObj.Quality = Math.Max(0, ammoObj.Quality - needAmmo);
            }

            if (wpn.DeleteDepleted && (ammoObj.Quality == 0 || !_catalog[ammoObj.Shape].HasQuality))
            {
                _map.RemoveObject(ammoObj);
                needNewWeapon = true;
            }
        }
        else
        {
            var quant = Inventory.GetQuantity(ammoObj, _catalog);
            if (quant <= needAmmo)
            {
                _map.RemoveObject(ammoObj);
                needNewWeapon = true;
            }
            else
            {
                ammoObj.Quality = quant - needAmmo;
            }
        }

        if (needNewWeapon && ready && !returns)
        {
            Equipment.ReadyBestWeapon(attacker, _catalog, _weapons, _armor);
        }
    }

    // ------------------------------------------------------------------ projectiles

    /// <summary>Exult <c>Projectile_effect</c>: a missile in flight, stepped every half tick.</summary>
    sealed class Projectile
    {
        public required U7Object Attacker;
        public required U7Object Target;
        public WeaponRecord? Weapon;
        public int WeaponShape;
        public int AmmoShape;
        public int SpriteShape;
        public U7Object? Sprite;
        public int AttVal;
        public int Speed = 4;
        public bool AutoHit;
        public bool Returns;
        public List<TileCoord> Path = new();
        public int Step;
        public TileCoord Pos;
        public double Timer;
    }

    /// <summary>Exult <c>Projectile_effect::init</c>: path from the attacker's missile tile to the target's centre.</summary>
    void LaunchProjectile(U7Object attacker, U7Object target, WeaponRecord? wpn, int weaponShape, int ammoShape, int spriteShape, int attval, bool returns)
    {
        var start = new TileCoord(attacker.Tx, attacker.Ty, attacker.Tz + attacker.DimZ * 3 / 4);
        var dest = new TileCoord(target.Tx, target.Ty, target.Tz + target.DimZ / 2);
        var ainf = ammoShape >= 0 ? _ammo[ammoShape] : null;
        var pr = new Projectile
        {
            Attacker = attacker,
            Target = target,
            Weapon = wpn,
            WeaponShape = weaponShape,
            AmmoShape = ammoShape,
            SpriteShape = spriteShape,
            AttVal = attval,
            Speed = wpn is { MissileSpeed: > 0 } ? wpn.MissileSpeed : 4,
            AutoHit = wpn is { Autohit: true } || ainf is { Autohit: true },
            Returns = returns,
            Pos = start
        };
        pr.Path = LinePath(start, dest);
        if (spriteShape >= 0)
        {
            var rec = _catalog[spriteShape];
            int frame;
            if (rec.FrameCount >= 24)
            {
                frame = 8 + Dir16(start, dest);
            }
            else if (rec.FrameCount == 1)
            {
                frame = 0;
            }
            else
            {
                frame = -1; // Exult: skip rendering
            }

            if (frame >= 0)
            {
                pr.Sprite = CreateItem(spriteShape, frame, start.Tx, start.Ty, start.Tz, temporary: true);
            }
        }

        _projectiles.Add(pr);
        LastMessage = $"{NameOf(attacker)} fires at {NameOf(target)}";
        GD.Print($"{LastMessage} (weapon {weaponShape}, ammo {ammoShape}, sprite {spriteShape}, {pr.Path.Count} tiles)");
    }

    void TickProjectiles(double delta)
    {
        if (_projectiles.Count == 0)
        {
            return;
        }

        var halfTick = _stepInterval / 2;
        for (var i = _projectiles.Count - 1; i >= 0; i--)
        {
            var pr = _projectiles[i];
            pr.Timer += delta;
            while (pr.Timer >= halfTick)
            {
                pr.Timer -= halfTick;
                if (AdvanceProjectile(pr))
                {
                    _projectiles.RemoveAt(i);
                    break;
                }
            }
        }
    }

    /// <summary>Exult <c>Projectile_effect::handle_event</c>. Returns true when the flight is over.</summary>
    bool AdvanceProjectile(Projectile pr)
    {
        if (pr.Sprite is { } spr && pr.Weapon is { RotationSpeed: > 0 } w)
        {
            var nf = spr.Frame + w.RotationSpeed;
            spr.Frame = nf > 23 ? ((nf - 8) % 16) + 8 : nf;
        }

        var finished = false;
        for (var i = 0; i < pr.Speed; i++)
        {
            if (pr.Step >= pr.Path.Count)
            {
                finished = true;
                break;
            }

            pr.Pos = pr.Path[pr.Step++];
        }

        if (pr.Sprite is { } s)
        {
            _map.MoveObject(s, pr.Pos.Tx, pr.Pos.Ty, pr.Pos.Tz);
        }

        if (!finished && pr.Step < pr.Path.Count)
        {
            return false;
        }

        ProjectileArrived(pr);
        if (pr.Sprite is { } sp)
        {
            _map.RemoveObject(sp);
        }

        return true;
    }

    void ProjectileArrived(Projectile pr)
    {
        var ainf = pr.AmmoShape >= 0 ? _ammo[pr.AmmoShape] : null;
        var target = pr.Target;
        var hit = false;
        var centre = new TileCoord(target.Tx, target.Ty, target.Tz + target.DimZ / 2);
        if (!target.Removed && !target.IsDead && target != pr.Attacker && centre.Distance2d(pr.Pos) < 3)
        {
            var defval = target.GetProp(ActorProp.Combat) + (target.GetFlag(9) ? 3 : 0);
            hit = pr.AutoHit || RollToWin(pr.AttVal, defval);
            if (hit)
            {
                HitWith(pr.Attacker, target, pr.Weapon, pr.Weapon?.Damage ?? 1, ainf);
            }
            else
            {
                target.Bark("miss");
                LastMessage = $"{NameOf(pr.Attacker)} misses {NameOf(target)}";
            }
        }

        if (pr.Returns && !pr.Attacker.IsDead && !pr.Attacker.Removed)
        {
            // Boomerang / magic axe: comes straight back into the thrower's hands.
            var back = CreateItem(pr.SpriteShape, 0, pr.Attacker.Tx, pr.Attacker.Ty, pr.Attacker.Tz, temporary: false, place: false);
            if (!Equipment.AddToActor(pr.Attacker, back, _catalog, _map))
            {
                _map.PlaceInWorld(back, pr.Attacker.Tx, pr.Attacker.Ty, pr.Attacker.Tz);
            }

            return;
        }

        bool drop;
        if (pr.Weapon is null)
        {
            drop = true;
        }
        else if (ainf is not null)
        {
            var ammo = pr.Weapon.Ammo;
            var type = ainf.DropType;
            drop = (ammo >= 0 || ammo == -3) &&
                   (type == AmmoRecord.AlwaysDrop || (!hit && type != AmmoRecord.NeverDrop));
        }
        else
        {
            drop = false;
        }

        if (drop && pr.SpriteShape >= 0 && _map.FindSpot(pr.Pos.Tx, pr.Pos.Ty, pr.Pos.Tz, 3) is { } spot)
        {
            var temp = pr.Attacker.GetFlag(ObjFlag.Temporary);
            var item = CreateItem(pr.SpriteShape, 0, spot.Tx, spot.Ty, spot.Tz, temp);
            item.SetFlag(ObjFlag.OkayToTake);
        }
    }

    /// <summary>Create a world item (arrow on the ground, missile sprite, returned boomerang).</summary>
    U7Object CreateItem(int shape, int frame, int tx, int ty, int tz, bool temporary, bool place = true)
    {
        var rec = _catalog[shape];
        var reflected = (frame & 32) != 0;
        var obj = new U7Object
        {
            Tx = tx,
            Ty = ty,
            Tz = tz,
            Shape = shape,
            Frame = frame,
            Kind = ObjectKind.Ireg,
            DimX = reflected ? rec.DimY : rec.DimX,
            DimY = reflected ? rec.DimX : rec.DimY,
            DimZ = rec.DimZ,
            Solid = false,
            Quality = rec.HasQuantity ? 1 : 0
        };
        if (temporary)
        {
            obj.SetFlag(ObjFlag.Temporary);
        }

        if (place)
        {
            _map.AddObject(obj);
        }

        return obj;
    }

    /// <summary>Straight 3D line (Exult's Zombie pathfinder), excluding the start tile.</summary>
    static List<TileCoord> LinePath(TileCoord from, TileCoord to)
    {
        var dx = U7Constants.TileDelta(from.Tx, to.Tx);
        var dy = U7Constants.TileDelta(from.Ty, to.Ty);
        var dz = to.Tz - from.Tz;
        var steps = Math.Max(Math.Abs(dx), Math.Abs(dy));
        var path = new List<TileCoord>(Math.Max(1, steps));
        if (steps == 0)
        {
            path.Add(to);
            return path;
        }

        for (var i = 1; i <= steps; i++)
        {
            path.Add(new TileCoord(
                U7Constants.WrapTile(from.Tx + (int)Math.Round(dx * (double)i / steps)),
                U7Constants.WrapTile(from.Ty + (int)Math.Round(dy * (double)i / steps)),
                Math.Max(0, from.Tz + (int)Math.Round(dz * (double)i / steps))));
        }

        return path;
    }

    /// <summary>Exult <c>Get_dir16</c> / <c>Get_direction16</c>: 0 = north, clockwise, 16 steps.</summary>
    static int Dir16(TileCoord from, TileCoord to)
    {
        var deltay = U7Constants.TileDelta(to.Ty, from.Ty); // t1.ty - t2.ty
        var deltax = U7Constants.TileDelta(from.Tx, to.Tx); // t2.tx - t1.tx
        if (deltax == 0)
        {
            return deltay > 0 ? 0 : 8;
        }

        var dydx = 1024 * deltay / deltax;
        var adydx = Math.Abs(dydx);
        int angle;
        if (adydx < 1533)
        {
            angle = adydx < 204 ? 4 : adydx < 684 ? 3 : 2;
        }
        else
        {
            angle = adydx < 5148 ? 1 : 0;
        }

        if (deltay < 0)
        {
            angle = deltax > 0 ? 8 - angle : angle + 8;
        }
        else if (deltax < 0)
        {
            angle = 16 - angle;
        }

        return angle % 16;
    }

    /// <summary>
    /// Exult <c>Actor::get_weapon</c>: the left hand only, bare hands = 1.
    /// <c>Monster_actor::get_weapon</c> adds the monster's own shape entry
    /// (a rat's bite) when nothing is readied.
    /// </summary>
    WeaponRecord? GetWeapon(U7Object actor, out int points, out int shape)
    {
        foreach (var item in actor.Contents)
        {
            if (item.Removed || item.ReadySlot != ReadySpot.Lhand)
            {
                continue;
            }

            var w = _weapons[item.Shape];
            if (w is not null)
            {
                points = w.Damage;
                shape = item.Shape;
                return w;
            }

            break;
        }

        if (actor.IsMonster)
        {
            var innate = _weapons[actor.Shape];
            if (innate is not null)
            {
                points = innate.Damage;
                shape = actor.Shape;
                return innate;
            }
        }

        points = 1;
        shape = -1;
        return null;
    }

    int GetReach(U7Object actor)
    {
        var w = GetWeapon(actor, out _, out _);
        if (w is not null)
        {
            return Math.Max(1, w.Range);
        }

        return Math.Max(1, _monsters[actor.Shape].Reach);
    }

    U7Object? NearestFoe(U7Object self, int maxDist)
    {
        U7Object? best = null;
        var bestD = int.MaxValue;
        foreach (var npc in Fighters())
        {
            if (npc.Removed || npc.IsDead || npc == self)
            {
                continue;
            }

            if (!IsEnemy(self.Alignment, npc.Alignment))
            {
                continue;
            }

            var d = new TileCoord(self.Tx, self.Ty, 0).Distance2d(new TileCoord(npc.Tx, npc.Ty, 0));
            if (d <= maxDist && d < bestD)
            {
                bestD = d;
                best = npc;
            }
        }

        return best;
    }

    IEnumerable<U7Object> Fighters()
    {
        foreach (var m in _spawned)
        {
            yield return m;
        }

        foreach (var n in _engaged)
        {
            yield return n;
        }
    }

    void Die(U7Object victim, U7Object? attacker)
    {
        victim.SetFlag(ObjFlag.Dead);
        victim.SetProp(ActorProp.Health, 0);
        victim.Bark("slain");
        LastMessage = attacker is null
            ? $"{NameOf(victim)} is slain"
            : $"{NameOf(attacker)} slays {NameOf(victim)}";
        GD.Print(LastMessage);
        if (victim.NpcNum == 0)
        {
            LastMessage = "you have died";
            SetInCombat(false);
            AvatarDied?.Invoke();
            return;
        }

        Party?.RemoveFromParty(victim);
        LeaveBody(victim);
        _map.RemoveObject(victim);
        Forget(victim.Id);
        _engaged.Remove(victim);
        _prevSchedule.Remove(victim.Id);
        MonsterDied();
    }

    /// <summary>
    /// Exult <c>Actor::die</c>: shapes in the bodies table leave a corpse
    /// container holding the inventory; otherwise the items drop nearby.
    /// Everything becomes okay to take.
    /// </summary>
    void LeaveBody(U7Object victim)
    {
        U7Object? body = null;
        if (Bodies.TryGetBody(victim.Shape, out var bshape, out var bframe))
        {
            bframe |= victim.Frame & 32;
            var rec = _catalog[bshape];
            var reflected = (bframe & 32) != 0;
            body = new U7Object
            {
                Tx = victim.Tx,
                Ty = victim.Ty,
                Tz = victim.Tz,
                Shape = bshape,
                Frame = bframe,
                Kind = ObjectKind.Ireg,
                DimX = reflected ? rec.DimY : rec.DimX,
                DimY = reflected ? rec.DimX : rec.DimY,
                DimZ = rec.DimZ,
                Solid = rec.Solid,
                Quality = victim.NpcNum > 0 ? 1 : 0,
                LiveNpcNum = victim.NpcNum > 0 ? victim.NpcNum : -1
            };
            if (victim.GetFlag(ObjFlag.Temporary))
            {
                body.SetFlag(ObjFlag.Temporary);
            }
        }

        var items = victim.Contents.ToList();
        victim.Contents.Clear();
        foreach (var item in items)
        {
            item.Container = null;
            item.ReadySlot = -1;
            if (body is not null)
            {
                item.Tx = 255; // let the gump lay it out
                item.Ty = 255;
                item.Container = body;
                body.Contents.Add(item);
                continue;
            }

            var spot = _map.FindSpot(victim.Tx, victim.Ty, victim.Tz, 5);
            if (spot is { } s)
            {
                SetOkayToTake(item);
                _map.PlaceInWorld(item, s.Tx, s.Ty, s.Tz);
            }
        }

        if (body is not null)
        {
            SetOkayToTake(body);
            _map.AddObject(body);
        }
    }

    static void SetOkayToTake(U7Object obj)
    {
        obj.SetFlag(ObjFlag.OkayToTake);
        foreach (var c in obj.Contents)
        {
            SetOkayToTake(c);
        }
    }

    void ClearArena()
    {
        foreach (var m in _arena)
        {
            _spawned.Remove(m);
            Forget(m.Id);
            if (!m.Removed)
            {
                _map.RemoveObject(m);
            }
        }

        _arena.Clear();
    }

    void FlashHit(U7Object obj) =>
        obj.HitUntilMsec = Time.GetTicksMsec() + 200;

    void PurgeDead()
    {
        for (var i = _spawned.Count - 1; i >= 0; i--)
        {
            if (_spawned[i].Removed || _spawned[i].IsDead)
            {
                Forget(_spawned[i].Id);
                _spawned.RemoveAt(i);
            }
        }
    }

    int RandomizeStat(int val)
    {
        if (val > 7)
        {
            return val + _rng.Next(5) + _rng.Next(5) - 4;
        }

        if (val > 0)
        {
            return _rng.Next(val) + _rng.Next(val) + 1;
        }

        return 1;
    }

    static string NameOf(U7Object obj) =>
        obj.NpcNum == 0 ? "you" :
        !string.IsNullOrEmpty(obj.NpcName) ? obj.NpcName : $"shape {obj.Shape}";
}
