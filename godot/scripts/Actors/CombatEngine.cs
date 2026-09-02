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

    readonly GameMap _map;
    readonly U7Object _avatar;
    readonly ShapeCatalog _catalog;
    readonly MonsterTable _monsters;
    readonly WeaponTable _weapons;
    readonly ArmorTable _armor;
    readonly List<U7Object> _spawned = new();
    readonly List<U7Object> _arena = new();
    /// <summary>Permanent NPCs pulled into combat (Exult <c>Combat_schedule</c> on a non-monster).</summary>
    readonly List<U7Object> _engaged = new();
    /// <summary>Exult <c>Combat_schedule::prev_schedule</c>, keyed by object id.</summary>
    readonly Dictionary<int, int> _prevSchedule = new();
    readonly Dictionary<int, double> _cooldown = new();
    readonly Random _rng = new();
    readonly double _stepInterval = U7Constants.StandardDelayMs / 1000.0;
    double _avatarCooldown;

    public Action<U7Object, int, int>? AvatarMoved { get; set; }
    /// <summary>Schedule runner that engaged NPCs are returned to when combat ends.</summary>
    public ScheduleRunner? Schedules { get; set; }
    public bool InCombat { get; private set; }
    public bool AvatarInvincible { get; private set; }
    public string LastMessage { get; private set; } = "";
    public IReadOnlyList<U7Object> Spawned => _spawned;
    public IReadOnlyList<U7Object> Engaged => _engaged;

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

    public void ToggleCombat()
    {
        InCombat = !InCombat;
        LastMessage = InCombat ? "combat" : "peace";
        GD.Print(LastMessage);
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

        var inf = _monsters[shape];
        var rec = _catalog[shape];
        var npc = new U7Object
        {
            Tx = spot.Value.Tx,
            Ty = spot.Value.Ty,
            Tz = spot.Value.Tz,
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
            ScheduleType = sched < 0 ? ScheduleType.Loiter : sched,
            NpcName = string.IsNullOrEmpty(rec.Name) ? $"shape {shape}" : rec.Name
        };
        npc.SetFlag(ObjFlag.Temporary);
        var str = RandomizeStat(inf.Strength);
        npc.SetProp(ActorProp.Strength, str);
        npc.SetProp(ActorProp.Health, str);
        npc.SetProp(ActorProp.Dexterity, RandomizeStat(inf.Dexterity));
        npc.SetProp(ActorProp.Intelligence, RandomizeStat(inf.Intelligence));
        npc.SetProp(ActorProp.Combat, RandomizeStat(inf.Combat));
        _map.AddObject(npc);
        _spawned.Add(npc);
        LastMessage = $"spawn {npc.NpcName} at {npc.Tx},{npc.Ty} align {npc.Alignment}";
        GD.Print(LastMessage);
        return npc;
    }

    /// <summary>Debug F3: heal avatar, spawn three chaotic rats, enter combat.</summary>
    public void SpawnArena()
    {
        ClearArena();
        _avatar.ClearFlag(ObjFlag.Dead);
        _avatar.CombatTarget = null;
        var maxHp = Math.Max(1, _avatar.GetProp(ActorProp.Strength));
        _avatar.SetProp(ActorProp.Health, maxHp);
        InCombat = true;

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
        foreach (var npc in _spawned)
        {
            TickFighter(npc, delta);
        }

        for (var i = _engaged.Count - 1; i >= 0; i--)
        {
            TickEngaged(_engaged[i], delta);
        }

        TickAvatar(delta, playerSteering);
    }

    public bool Attack(U7Object attacker, U7Object defender)
    {
        if (!attacker.IsActor || !defender.IsActor || attacker.IsDead || defender.IsDead)
        {
            return false;
        }

        InCombat = true;
        attacker.CombatTarget = defender;
        Engage(defender, attacker);

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

        if (wpoints >= 127 || type is 3 or 4 || armor < 0)
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
            TryStrike(_avatar, foe);
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

        if (!IsEnemy(npc.Alignment, _avatar.Alignment) || _avatar.IsDead)
        {
            return;
        }

        var dist = new TileCoord(npc.Tx, npc.Ty, 0)
            .Distance2d(new TileCoord(_avatar.Tx, _avatar.Ty, 0));
        if (dist > SightRange)
        {
            return;
        }

        npc.CombatTarget = _avatar;
        npc.ScheduleType = ScheduleType.Combat;
        InCombat = true;
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
        var reach = GetReach(npc);
        if (dist <= reach)
        {
            TryStrike(npc, _avatar);
            return;
        }

        StepToward(npc, _avatar.Tx, _avatar.Ty);
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
        _cooldown.Remove(npc.Id);
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
        if (!attacker.IsActor || !victim.IsActor || victim.NpcNum == 0 ||
            victim.CombatTarget is not null ||
            !IsEnemy(victim.Alignment, attacker.Alignment))
        {
            return;
        }

        InCombat = true;
        Engage(victim, attacker);
    }

    void TickEngaged(U7Object npc, double delta)
    {
        if (npc.Removed || npc.IsDead)
        {
            Disengage(npc);
            return;
        }

        var target = npc.CombatTarget ?? _avatar;
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
            TryStrike(npc, target);
            return;
        }

        StepToward(npc, target.Tx, target.Ty);
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

    bool TryStrike(U7Object attacker, U7Object defender)
    {
        var wpn = GetWeapon(attacker, out var points, out _);
        var attval = attacker.GetProp(ActorProp.Combat);
        if (wpn is { Lucky: true })
        {
            attval += 3;
        }

        var autohit = wpn is { Autohit: true };
        if (!autohit && defender.IsActor &&
            !RollToWin(attval, defender.GetProp(ActorProp.Combat)))
        {
            defender.Bark("miss");
            LastMessage = $"{NameOf(attacker)} misses {NameOf(defender)}";
            return false;
        }

        var hits = ApplyDamage(attacker, defender, attacker.GetProp(ActorProp.Strength), points, 0);
        if (hits > 0 && !defender.IsDead)
        {
            defender.Bark($"{hits}");
            LastMessage = $"{NameOf(attacker)} hits {NameOf(defender)} for {hits}";
        }

        return hits > 0;
    }

    WeaponRecord? GetWeapon(U7Object actor, out int points, out int shape)
    {
        foreach (var item in actor.Contents)
        {
            if (item.Removed || item.ReadySlot is not (ReadySpot.Rhand or ReadySpot.Lhand))
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
        }

        var innate = _weapons[actor.Shape];
        if (innate is not null)
        {
            points = innate.Damage;
            shape = actor.Shape;
            return innate;
        }

        var inf = _monsters[actor.Shape];
        points = inf.Weapon > 0 ? inf.Weapon : 1;
        shape = actor.Shape;
        return null;
    }

    int GetReach(U7Object actor)
    {
        var w = GetWeapon(actor, out _, out _);
        if (w is not null)
        {
            return w.Melee ? Math.Max(1, w.Range) : 1;
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
            return;
        }

        _map.RemoveObject(victim);
        _cooldown.Remove(victim.Id);
        _engaged.Remove(victim);
        _prevSchedule.Remove(victim.Id);
    }

    void ClearArena()
    {
        foreach (var m in _arena)
        {
            _spawned.Remove(m);
            _cooldown.Remove(m.Id);
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
                _cooldown.Remove(_spawned[i].Id);
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
