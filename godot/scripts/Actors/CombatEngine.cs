using Godot;
using U7.Core;
using U7.Data;
using U7.World;

namespace U7.Actors;

/// <summary>
/// Combat's shared parts: monster eggs and Exult <c>Monster_actor::create</c>,
/// combat mode (<c>toggle_combat</c>), targets (<c>set_target</c>,
/// <c>fight_back</c>), the blow itself (<c>Combat_schedule::attack_target</c>,
/// <c>roll_to_win</c>, <c>apply_damage</c>), missiles
/// (<c>Projectile_effect</c>), death and battle music. The fighting is each
/// actor's <see cref="CombatSchedule"/>.
/// </summary>
public sealed class CombatEngine
{
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
    /// <summary>Exult <c>Game_object::rotate</c>: frame band per direction 0-7 (N, NE, E, ...).</summary>
    static readonly int[] Rotate = [0, 0, 48, 48, 16, 16, 32, 32];
    /// <summary>
    /// Exult's attack frame sets by <c>Weapon_info::Actor_frames</c> (reach,
    /// raise, fast swing, slow swing): one-handed, then two-handed.
    /// </summary>
    static readonly int[][] AttackFrames1 = [[3, 6], [3, 4, 6], [3, 5, 6], [3, 4, 5, 6]];
    static readonly int[][] AttackFrames2 = [[3, 9], [3, 7, 9], [3, 8, 9], [3, 7, 8, 9]];
    /// <summary>Exult <c>visible_frames</c>: substitute when a frame is empty (1-handed ↔ 2-handed).</summary>
    static readonly int[] VisibleFrames = [0, 0, 0, 0, 7, 8, 9, 4, 5, 6, 0, 12, 11, 0, 9, 3];
    readonly Random _rng = new();
    // Exult monster_mode_odds / monster_modes: noncombatants, opportunists,
    // unpredictable, tacticians, berserkers.
    static readonly int[,] ModeOdds =
        { { 20, 45, 70, 100 }, { 50, 100, 0, 0 }, { 35, 70, 100, 0 }, { 35, 55, 70, 100 }, { 50, 100, 0, 0 } };
    static readonly int[,] Modes =
    {
        { AttackMode.Nearest, AttackMode.Random, AttackMode.Flee, AttackMode.Nearest },
        { AttackMode.Weakest, AttackMode.Nearest, AttackMode.Nearest, AttackMode.Nearest },
        { AttackMode.Nearest, AttackMode.Random, AttackMode.Nearest, AttackMode.Nearest },
        { AttackMode.Flank, AttackMode.Defend, AttackMode.Weakest, AttackMode.Strongest },
        { AttackMode.Berserk, AttackMode.Nearest, AttackMode.Nearest, AttackMode.Nearest }
    };
    readonly double _stepInterval = U7Constants.StandardDelayMs / 1000.0;
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

    public void SetInCombat(bool on)
    {
        if (InCombat == on)
        {
            return;
        }

        InCombat = on;
        LastMessage = on ? "combat" : "peace";
        GD.Print(LastMessage);
        var newsched = on ? ScheduleType.Combat : ScheduleType.FollowAvatar;
        if (Party is { } party)
        {
            foreach (var member in party.Members)
            {
                var sched = member.ScheduleType;
                if (!member.IsDead && sched != newsched && sched is not (ScheduleType.Wait or ScheduleType.Loiter))
                {
                    SetSchedule(member, newsched);
                }
            }
        }

        if (_avatar.ScheduleType != newsched)
        {
            SetSchedule(_avatar, newsched);
        }

        if (!on)
        {
            return;
        }

        ReadyBestWeapon(_avatar);
        foreach (var act in PartyAndAvatar())
        {
            // Did usecode set them to flee? (Exult keeps a mode the player chose.)
            if (act.AttackMode == AttackMode.Flee)
            {
                act.AttackMode = AttackMode.Nearest;
            }

            // And avoid attacking party members, in case of a usecode bug.
            if (act.CombatTarget is { } targ && Party?.IsInParty(targ) == true)
            {
                SetTarget(act, null, false);
            }
        }
    }

    IEnumerable<U7Object> PartyAndAvatar() =>
        Party is { } party ? party.Members.Prepend(_avatar) : [_avatar];

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
        Schedules?.AddMonster(npc);
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
            Solid = rec.Solid,
            DimX = rec.DimX,
            DimY = rec.DimY,
            DimZ = rec.DimZ,
            Alignment = align == Alignment.Neutral ? inf.Alignment : align,
            TypeFlags = inf.MoveFlags,
            ScheduleType = sched,
            NpcName = string.IsNullOrEmpty(rec.Name) ? $"shape {shape}" : rec.Name
        };
        var str = RandomizeStat(inf.Strength);
        npc.SetProp(ActorProp.Strength, str);
        npc.SetProp(ActorProp.Health, str);
        npc.SetProp(ActorProp.Dexterity, RandomizeStat(inf.Dexterity));
        npc.SetProp(ActorProp.Intelligence, RandomizeStat(inf.Intelligence));
        npc.SetProp(ActorProp.Combat, RandomizeStat(inf.Combat));
        // Its attack mode, by its kind of fighter.
        var prob = _rng.Next(100);
        var i = 0;
        while (i < 3 && prob >= ModeOdds[inf.AttackModeClass, i])
        {
            i++;
        }

        npc.AttackMode = Modes[inf.AttackModeClass, i];
        if (sched == ScheduleType.Combat)
        {
            ReadyBestWeapon(npc);
        }

        return npc;
    }

    /// <summary>Let the monster AI drive a monster that usecode placed in the world.</summary>
    public void AdoptMonster(U7Object npc)
    {
        if (npc.IsMonster && !_spawned.Contains(npc))
        {
            _spawned.Add(npc);
            Schedules?.AddMonster(npc);
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

    /// <summary>Missiles in flight; the fighting itself is the actors' combat schedules.</summary>
    public void Update(double delta, bool frozen)
    {
        if (frozen)
        {
            return;
        }

        PurgeDead();
        TickProjectiles(delta);
    }

    /// <summary>
    /// Exult <c>Game_window::double_clicked</c> in combat mode: the avatar
    /// takes it on, and everyone goes into combat.
    /// </summary>
    public void AttackClicked(U7Object target)
    {
        SetTarget(_avatar, target, false);
        InCombat = false; // Want everyone to be in combat.
        SetInCombat(true);
        LastMessage = $"attacking {NameOf(target)}";
    }

    /// <summary>
    /// Exult <c>Actor::set_target</c>: whom it fights, and (if asked, and it
    /// is not of the party) the combat schedule; the target learns who
    /// oppresses it, and an oppressor who no longer fights it is forgotten.
    /// </summary>
    public void SetTarget(U7Object npc, U7Object? target, bool startCombat)
    {
        npc.CombatTarget = target;
        var inParty = npc == _avatar || Party?.IsInParty(npc) == true;
        if (startCombat && !inParty &&
            (npc.ScheduleType != ScheduleType.Combat || Schedules?.BrainOf(npc)?.Schedule is not CombatSchedule))
        {
            SetSchedule(npc, ScheduleType.Combat);
        }

        if (target is { IsActor: true } opponent)
        {
            opponent.Oppressor = npc.NpcNum >= 0 ? npc : null;
        }

        // Exult's "pure guess".
        if (npc.Oppressor is { } oppr && (oppr.CombatTarget != npc || oppr.ScheduleType != ScheduleType.Combat))
        {
            npc.Oppressor = null;
        }
    }

    public void Fight(U7Object npc, U7Object foe)
    {
        SetSchedule(npc, ScheduleType.Combat);
        SetTarget(npc, foe, false);
    }

    public MonsterRecord MonsterInfo(int shape) => _monsters[shape];

    /// <summary>Exult <c>CSRun_Away</c>: leaving a fight with foes close by.</summary>
    public void RunAwayMusic() => Music?.Start(16, repeat: false);

    /// <summary>Exult: a monster's own reach, or the default 3.</summary>
    public int MonsterReach(int shape) => _monsters[shape].Reach;

    /// <summary>Exult <c>Actor::ready_best_shield</c>.</summary>
    public void ReadyBestShield(U7Object npc) => Equipment.ReadyBestShield(npc, _catalog, _weapons, _armor);

    /// <summary>
    /// Exult <c>Actor::ready_ammo</c>: whether the weapon in hand can shoot,
    /// i.e. needs no ammunition (and has charges, if it uses them) or has some.
    /// (Exult also moves found ammunition to the quiver.)
    /// </summary>
    public bool ReadyAmmo(U7Object actor)
    {
        if (Equipment.GetReadied(actor, ReadySpot.Lhand) is not { } weapon || _weapons[weapon.Shape] is not { } winf)
        {
            return false;
        }

        if (winf.Ammo < 0)
        {
            return !winf.UsesCharges || !_catalog[weapon.Shape].HasQuality || weapon.Quality > 0;
        }

        return FindWeaponAmmo(actor, weapon.Shape, winf, winf.Ammo, 1) is not null;
    }

    /// <summary>Exult <c>Get_usable_weapon</c>: a weapon at that ready spot it could fight with now.</summary>
    U7Object? UsableWeapon(U7Object npc, int spot)
    {
        if (Equipment.GetReadied(npc, spot) is not { } bobj || _weapons[bobj.Shape] is not { } winf)
        {
            return null;
        }

        // Ranged first, then melee.
        if (GetWeaponAmmo(npc, winf, bobj.Shape, winf.Ammo, winf.Projectile, true, out var aobj) > 0 && aobj is null &&
            GetWeaponAmmo(npc, winf, bobj.Shape, winf.Ammo, winf.Projectile, false, out aobj) > 0 && aobj is null)
        {
            return null;
        }

        if (_catalog[bobj.Shape].ReadyType == ReadySpot.BothHands && Equipment.GetReadied(npc, ReadySpot.Rhand) is not null)
        {
            return null; // Needs two free hands.
        }

        return bobj;
    }

    /// <summary>
    /// Exult <c>Swap_weapons</c>: the weapon on the belt (or the back) into
    /// the hand, the old one where it was; NPCs not of the party otherwise
    /// ready their best weapon.
    /// </summary>
    public bool SwapWeapons(U7Object npc)
    {
        var index = ReadySpot.Belt;
        var bobj = UsableWeapon(npc, index);
        if (bobj is null)
        {
            index = ReadySpot.Back2h;
            bobj = UsableWeapon(npc, index);
            if (bobj is null)
            {
                return npc != _avatar && Party?.IsInParty(npc) != true &&
                       Equipment.ReadyBestWeapon(npc, _catalog, _weapons, _armor);
            }
        }

        var oldweap = Equipment.GetReadied(npc, ReadySpot.Lhand);
        if (oldweap is not null)
        {
            _map.TakeFromWorld(oldweap);
        }

        _map.TakeFromWorld(bobj);
        Equipment.AddToActor(npc, bobj, _catalog, _map); // Should go into the weapon hand.
        if (oldweap is not null)
        {
            Equipment.AddReadied(npc, oldweap, index, _catalog, _map, forcePos: true); // The old where the new was.
        }

        return true;
    }

    /// <summary>
    /// Exult <c>Combat_schedule::back_off</c>: a hit actor whose weapon reaches
    /// farther than its attacker stands steps back a tile, facing it.
    /// (Exult works out the direction from x minus the attacker's y; kept.)
    /// </summary>
    void BackOff(U7Object npc, U7Object attacker)
    {
        if (npc.GetFlag(ObjFlag.Paralyzed) || npc.GetFlag(ObjFlag.Asleep) || npc.IsDead ||
            npc.GetProp(ActorProp.Health) <= 0)
        {
            return;
        }

        var winf = GetWeapon(npc, out _, out _);
        var weaponDist = winf?.Range ?? 3;
        if (npc.CombatTarget == attacker && weaponDist <= ObjectGeometry.Distance(npc, attacker))
        {
            return; // Stay within our weapon's range.
        }

        var dx = Math.Sign(npc.Tx - attacker.Ty);
        var dy = Math.Sign(npc.Ty - attacker.Ty);
        (int, int)[] spots = dx != 0
            ? dy != 0 ? [(dx, 0), (dx, dy), (0, dy)] : [(dx, 0), (dx, -1), (dx, 1)]
            : [(0, dy), (-1, dy), (1, dy)];
        var ind = _rng.Next(3);
        for (var tries = 0; tries < 3; tries++, ind = (ind + 1) % 3)
        {
            var spot = new TileCoord(npc.Tx + spots[ind].Item1, npc.Ty + spots[ind].Item2, npc.Tz).Wrapped();
            if (ActorWalker.IsBlocked(_map, npc, ref spot))
            {
                continue;
            }

            _map.MoveObject(npc, spot.Tx, spot.Ty, spot.Tz);
            npc.Frame = ActorWalker.DirFrame(ObjectGeometry.FacingDirection(npc, attacker), 0);
            return;
        }
    }

    /// <summary>Exult <c>Actor::ready_best_weapon</c>.</summary>
    public void ReadyBestWeapon(U7Object npc) => Equipment.ReadyBestWeapon(npc, _catalog, _weapons, _armor);

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

    /// <summary>
    /// Exult <c>Actor::fight_back</c>: a party member hit in combat mode (or
    /// with the avatar unable to act) brings the party into the fight; an
    /// NPC with no target takes on its attacker (play-fighting duelists
    /// don't start a real fight). (Exult's calling of guards is not ported.)
    /// </summary>
    void FightBack(U7Object victim, U7Object attacker)
    {
        if (!attacker.IsActor || victim.Alignment == attacker.Alignment)
        {
            return; // Friendly fire doesn't cause a fight.
        }

        var inParty = victim == _avatar || Party?.IsInParty(victim) == true;
        if (inParty && !InCombat)
        {
            SetInCombat(true);
        }

        if (inParty)
        {
            foreach (var member in PartyAndAvatar())
            {
                if (member.ScheduleType is not (ScheduleType.Combat or ScheduleType.Wait or ScheduleType.Loiter))
                {
                    SetSchedule(member, ScheduleType.Combat);
                }
            }
        }

        if (victim.CombatTarget is null && !inParty)
        {
            SetTarget(victim, attacker, attacker.ScheduleType != ScheduleType.Duel);
        }
    }



    /// <summary>Restore spawned monsters from a saved game (MONSNPCS.DAT).</summary>
    /// <summary>Exult <c>Actor::can_speak</c>: no monster info, or one that can yell.</summary>
    public bool CanSpeak(U7Object actor) => !_monsters.Contains(actor.Shape) || !_monsters[actor.Shape].CantYell;

    /// <summary>Exult <c>Actor::is_sentient</c>: monster intelligence 6 or more (opens doors, joins fights).</summary>
    public bool IsSentient(U7Object actor) => _monsters[actor.Shape].Intelligence >= 6;

    /// <summary>
    /// Exult <c>Game_window::is_hostile_nearby</c>: an evil or chaotic actor
    /// in the tile rectangle that is fighting (Exult: a combat schedule that
    /// has started its battle; here, one with a target).
    /// </summary>
    public bool IsHostileNearby(int x0, int y0, int w, int h)
    {
        foreach (var b in Schedules?.Brains ?? [])
        {
            var npc = b.Npc;
            if (!npc.IsDead && !npc.Removed && npc.Alignment >= Alignment.Evil &&
                b.Schedule is CombatSchedule { StartedBattle: true } &&
                U7Constants.TileDelta(x0, npc.Tx) is var dx && dx >= 0 && dx < w &&
                U7Constants.TileDelta(y0, npc.Ty) is var dy && dy >= 0 && dy < h)
            {
                return true;
            }
        }

        return false;
    }

    public void AdoptMonsters(IEnumerable<U7Object> monsters)
    {
        foreach (var m in monsters)
        {
            // Saves from before type flags were kept have none: use the monster's own.
            if ((m.TypeFlags & MoveFlags.All) == 0)
            {
                m.TypeFlags |= _monsters[m.Shape].MoveFlags;
            }

            if (!m.Removed && !m.IsDead)
            {
                _spawned.Add(m);
                Schedules?.AddMonster(m);
            }
        }
    }

    /// <summary>
    /// Exult <c>Combat_schedule::start_battle</c>: when the avatar has a foe and
    /// no battle music played in the last 30 s, play one of the two attack tracks.
    /// </summary>
    public void StartBattleMusic(U7Object npc, bool hasOpponents)
    {
        if (Music is null || npc != _avatar || !hasOpponents)
        {
            return;
        }

        var now = Time.GetTicksMsec();
        if (now - _battleTime < 30000)
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

        foreach (var npc in Schedules?.NearbyNpcs() ?? [])
        {
            if (!npc.IsDead && npc.AttackMode != AttackMode.Flee && npc.Alignment >= Alignment.Evil)
            {
                return; // Still possible enemies.
            }
        }

        _battleEndTime = Time.GetTicksMsec();
        var len = (_battleEndTime - _battleTime) / 1000;
        var hard = len > 15 && (ulong)_rng.Next(60) < len;
        Music.Start(hard ? 9 : 15, repeat: false);
    }

    /// <summary>
    /// Exult <c>Actor::get_attack_frames</c>: the frames for attacking with
    /// <paramref name="weaponShape"/> (-1 bare-handed: a fast swing, or a
    /// reach when shooting), by the weapon's kind of stroke and whether a
    /// two-handed weapon is readied, turned to <paramref name="dir"/> (0-7); a
    /// frame the shape lacks becomes the other hand's, else standing. (Sea
    /// serpents' and slimes' own are not ported.)
    /// </summary>
    public static int[] AttackFrames(ShapeCatalog catalog, WeaponTable weapons, U7Object actor, int weaponShape,
        bool projectile, int dir)
    {
        var kind = weaponShape >= 0 && weapons[weaponShape] is { } winfo
            ? (projectile ? winfo.ActorFrames >> 2 : winfo.ActorFrames) & 3
            : projectile ? 0 : 2;
        var which = (Equipment.IsTwoHanded(actor, catalog) ? AttackFrames2 : AttackFrames1)[kind];
        var band = Rotate[dir & 7];
        var rec = catalog[actor.Shape];
        var frames = new int[which.Length];
        for (var i = 0; i < which.Length; i++)
        {
            var fr = which[i] + band;
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

        return frames;
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

    bool TryToHit(U7Object target, int attval) =>
        !target.IsActor || RollToWin(attval, target.GetProp(ActorProp.Combat) + (target.GetFlag(ObjFlag.Protection) ? 3 : 0));

    /// <summary>
    /// Exult <c>Combat_schedule::attack_target</c>: the blow itself (the
    /// swing is the schedule's): out of range or ammunition, nothing; at
    /// range a missile flies; in melee a roll to hit, damage, and the target
    /// may back off. False if it could not attack or missed.
    /// </summary>
    public bool AttackTarget(U7Object attacker, U7Object target, int weaponShape, bool combat)
    {
        if (attacker.IsActor && attacker.IsDead)
        {
            return false;
        }

        var wpn = weaponShape >= 0 ? _weapons[weaponShape] : null;
        int reach;
        var family = -1;
        var proj = -1;
        if (wpn is null)
        {
            reach = _monsters[attacker.Shape].Reach;
        }
        else
        {
            reach = wpn.Range;
            proj = wpn.Projectile;
            family = wpn.Ammo;
        }

        var dist = ObjectGeometry.Distance(attacker, target);
        var ranged = wpn is null ? dist > reach : wpn.Uses == WeaponRecord.UsesRanged || dist > reach;
        if (EffectiveRange(attacker, wpn, reach) < dist)
        {
            LastMessage = "out of range";
            return false;
        }

        var needAmmo = GetWeaponAmmo(attacker, wpn, weaponShape, family, proj, ranged, out var ammoObj);
        if (needAmmo > 0 && ammoObj is null)
        {
            LastMessage = $"{NameOf(attacker)}: out of ammo";
            return false;
        }

        if (proj == -3)
        {
            proj = weaponShape; // Use the weapon as the missile.
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
            ConsumeAmmo(attacker, wpn, ammoObj, needAmmo, returns, combat);
        }

        var attval = attacker.IsActor ? attacker.GetProp(ActorProp.Combat) : 0;
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

            LaunchProjectile(attacker, target, wpn, weaponShape, ammoShape, proj, attval, returns);
            return true;
        }

        var autohit = wpn is { Autohit: true } || ainf is { Autohit: true };
        if (!autohit && !TryToHit(target, attval))
        {
            if (target.IsActor)
            {
                target.Bark("miss");
            }

            LastMessage = $"{NameOf(attacker)} misses {NameOf(target)}";
            return false;
        }

        // (Exult blows up a powder keg used as a weapon instead; explosions are not ported.)
        HitWith(attacker, target, wpn, wpn?.Damage ?? 1, ammoObj is not null && needAmmo > 0 ? _ammo[ammoObj.Shape] : null,
            ammoObj?.Shape ?? -1);
        if (target is { IsActor: true, IsDead: false, Removed: false })
        {
            BackOff(target, attacker);
        }

        return true;
    }

    bool HitWith(U7Object attacker, U7Object defender, WeaponRecord? wpn, int wpoints, AmmoRecord? ainf, int ammoShape)
    {
        if (!defender.IsActor)
        {
            ObjectAttacked(defender, ammoShape);
            return false;
        }

        if (attacker.IsActor && attacker.ScheduleType == ScheduleType.Duel)
        {
            // Exult Actor::attacked: just play-fighting.
            if (attacker.NpcNum >= 0)
            {
                defender.Oppressor = attacker;
            }

            return false;
        }

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

    /// <summary>
    /// Exult <c>Game_object::attacked</c> for things: arrows in the archery
    /// target (735) stick in it, frame by frame. (Breakable objects are not
    /// ported.)
    /// </summary>
    static void ObjectAttacked(U7Object obj, int ammoShape)
    {
        if (obj.Shape == 735 && ammoShape == 722)
        {
            var frnum = obj.Frame;
            obj.Frame = frnum == 0 ? 3 * Random.Shared.Next(8) + 1 : frnum % 3 != 0 ? frnum + 1 : frnum;
        }
    }

    /// <summary>Exult <c>Actor::get_effective_range</c>: thrown weapons reach as far as strength/combat allow.</summary>
    public int EffectiveRange(U7Object actor, WeaponRecord? wpn, int reach)
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
    public int GetWeaponAmmo(U7Object actor, WeaponRecord? wpn, int weaponShape, int family, int proj, bool ranged,
        out U7Object? ammo)
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

    void ConsumeAmmo(U7Object attacker, WeaponRecord wpn, U7Object ammoObj, int needAmmo, bool returns, bool combat)
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

        if (!attacker.IsActor || !needNewWeapon || !ready)
        {
            return;
        }

        // The readied weapon was used up.
        if (returns)
        {
            // It comes back: wait for it.
            if (combat && Schedules?.BrainOf(attacker)?.Schedule is CombatSchedule waiting)
            {
                waiting.WaitForReturn();
            }
        }
        else if (!ReadyAmmo(attacker))
        {
            // A new weapon, and tell the schedule.
            Equipment.ReadyBestWeapon(attacker, _catalog, _weapons, _armor);
            if (Schedules?.BrainOf(attacker)?.Schedule is CombatSchedule schedule)
            {
                schedule.WeaponRemoved();
            }
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
            hit = pr.AutoHit || TryToHit(target, pr.AttVal);
            if (hit)
            {
                HitWith(pr.Attacker, target, pr.Weapon, pr.Weapon?.Damage ?? 1, ainf, pr.AmmoShape);
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
    /// <summary>Exult <c>Actor::get_weapon</c>: the weapon in hand, or a monster's own (its shape).</summary>
    public WeaponRecord? GetWeapon(U7Object actor, out int points, out int shape)
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
