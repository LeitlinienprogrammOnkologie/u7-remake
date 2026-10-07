using U7.Core;
using U7.Data;
using U7.World;

namespace U7.Actors;

/// <summary>
/// Exult <c>Combat_schedule</c> (combat.cc): fighting. The NPC finds a foe
/// among those near the screen by its attack mode (nearest, weakest,
/// strongest, random, berserk, protect, defend, flank; manual leaves it to
/// the player), approaches it with the monster pathfinder, and strikes when
/// in reach and when its dexterity has built up (30 points a blow), or
/// shoots from range; it flees when told to or nearly dead, and gives up
/// after repeated failures (party members fall back to following, good
/// NPCs to their schedule). Mages teleport, summon and turn invisible. The
/// avatar fights this way too in combat mode while the player doesn't walk
/// it. Hits, damage and missiles are <see cref="CombatEngine.AttackTarget"/>.
/// </summary>
public class CombatSchedule : Schedule
{
    /// <summary>Exult <c>dex_to_attack</c>.</summary>
    public const int DexToAttack = 30;

    protected enum State
    {
        Initial,
        Approach,
        Strike,
        Fire,
        WaitReturn
    }

    protected State CurState = State.Initial;
    readonly int _prevSchedule;
    protected readonly List<U7Object> Opponents = new();
    /// <summary>Exult <c>practice_target</c>: a duelist's archery target or dummy.</summary>
    protected U7Object? PracticeTarget;
    protected int WeaponShape = -1;
    bool _noBlocking;
    int _yelled;
    int _fleed;
    protected int Failures;
    double _teleportTime;
    double _summonTime;
    double _invisibleTime;
    int _dexPoints;
    int _strikeBlocked;
    int _alignment;
    readonly bool _canYell;

    /// <summary>Exult <c>has_started_battle</c>.</summary>
    public bool StartedBattle { get; protected set; }

    protected CombatEngine Combat => Runner.Combat!;

    public CombatSchedule(NpcBrain brain, int prevSchedule)
        : base(brain)
    {
        _prevSchedule = prevSchedule;
        _alignment = brain.Npc.Alignment;
        SetWeapon();
        _canYell = CanSpeak;
        _summonTime = Runner.Ticks + 4000;
        _invisibleTime = Runner.Ticks + 4500;
    }

    public override void Begin()
    {
    }

    bool IsInParty(U7Object actor) => actor == Runner.Avatar || (Runner.Party?.IsInParty(actor) ?? false);

    /// <summary>Exult <c>Actor::can_act</c>.</summary>
    static bool CanAct(U7Object actor) =>
        !(actor.GetFlag(ObjFlag.Paralyzed) || actor.GetFlag(ObjFlag.Asleep) || actor.IsDead ||
          actor.GetProp(ActorProp.Health) <= 0);

    MonsterRecord? MonsterInfo => Combat.IsMonsterShape(Npc.Shape) ? Combat.MonsterInfo(Npc.Shape) : null;

    bool CantDie => MonsterInfo is { CantDie: true };

    int Dist(U7Object other) => ObjectGeometry.Distance(Npc, other);

    /// <summary>Exult <c>Combat_schedule::find_opponents</c>: enemies among those near the screen, the avatar too.</summary>
    protected virtual void FindOpponents()
    {
        Opponents.Clear();
        var sleeping = new List<U7Object>();
        var avatar = Runner.Avatar;
        var inParty = IsInParty(Npc);
        var npcAlign = Npc.Alignment;
        foreach (var actor in Runner.NearbyNpcs().Append(avatar))
        {
            // (Exult: invisible ones only for those who can see them; nobody here can.)
            if (actor.IsDead || actor.GetFlag(ObjFlag.Invisible))
            {
                continue;
            }

            if (CombatEngine.IsEnemy(npcAlign, actor.Alignment))
            {
                if (actor.GetFlag(ObjFlag.Asleep))
                {
                    sleeping.Add(actor);
                }
                else
                {
                    Opponents.Add(actor);
                }
            }
            else if (inParty)
            {
                // Attacking a party member?
                if (actor.CombatTarget is not { } t)
                {
                    continue;
                }

                if (IsInParty(t) && t.IsActor && CombatEngine.IsEnemy(npcAlign, t.Alignment))
                {
                    Opponents.Add(actor);
                }

                // Being attacked by a party member?
                if (actor.Oppressor is { } oppr && IsInParty(oppr) && CombatEngine.IsEnemy(npcAlign, oppr.Alignment))
                {
                    Opponents.Add(actor);
                }
            }
        }

        // None found? The avatar's, if of the same alignment.
        if (Opponents.Count == 0 && inParty && avatar.Alignment == npcAlign &&
            avatar.CombatTarget is { IsActor: true } opp && opp != Npc && opp.ScheduleType == ScheduleType.Combat)
        {
            Opponents.Add(opp);
        }

        // Still none? The sleeping ones.
        if (Opponents.Count == 0 && sleeping.Count > 0 && Npc != avatar)
        {
            Opponents.InsertRange(0, sleeping);
        }
    }

    U7Object? Weakest() => Opponents.Where(o => !o.Removed).MinBy(o => o.GetProp(ActorProp.Strength));

    U7Object? Strongest() => Opponents.Where(o => !o.Removed).MaxBy(o => o.GetProp(ActorProp.Strength));

    /// <summary>Exult <c>find_nearest_opponent</c>: preferring ones nobody else attacks, and not fleeing.</summary>
    U7Object? Nearest()
    {
        var bestDist = 4 * U7Constants.TilesPerChunk;
        U7Object? best = null;
        foreach (var opp in Opponents.Where(o => !o.Removed))
        {
            var dist = Dist(opp);
            if (opp.Oppressor is { } oppr && oppr != Npc)
            {
                dist += 16; // Penalize those that already have an attacker.
            }

            if (opp.AttackMode == AttackMode.Flee || !CanAct(opp))
            {
                dist += 32; // Avoid fleeing ones.
            }

            if (dist < bestDist)
            {
                bestDist = dist;
                best = opp;
            }
        }

        return best;
    }

    /// <summary>Exult <c>find_random_opponent</c>: three tries at one nobody else attacks, else the weakest.</summary>
    U7Object? RandomOpponent()
    {
        U7Object? pick = null;
        for (var tries = 0; tries < 3; tries++)
        {
            var opp = Opponents[Rng.Next(Opponents.Count)];
            if (opp.Removed || (opp.Oppressor is { } oppr && oppr != Npc))
            {
                continue;
            }

            pick = opp;
        }

        return pick ?? Weakest();
    }

    /// <summary>Exult <c>find_attacked_opponent</c>: the nearest one an ally is attacking.</summary>
    U7Object? Attacked()
    {
        var bestDist = 4 * U7Constants.TilesPerChunk;
        U7Object? best = null;
        foreach (var opp in Opponents.Where(o => !o.Removed))
        {
            var dist = Dist(opp);
            if (opp.Oppressor is not { } oppr || oppr == Npc ||
                (oppr.Alignment == Npc.Alignment && oppr.CombatTarget != opp))
            {
                dist += 16; // Not being attacked by an ally.
            }

            if (opp.AttackMode == AttackMode.Flee || !CanAct(opp))
            {
                dist += 32;
            }

            if (dist < bestDist)
            {
                bestDist = dist;
                best = opp;
            }
        }

        return best;
    }

    /// <summary>Exult <c>find_protected_attacker</c>: the nearest attacker of the party member under protection.</summary>
    U7Object? ProtectedAttacker()
    {
        if (Runner.Party is not { } party || !party.IsInParty(Npc))
        {
            return null;
        }

        var protectee = party.Members.Prepend(Runner.Avatar).FirstOrDefault(m => m.CombatProtected);
        if (protectee is null)
        {
            return null;
        }

        var best = Opponents.Where(o => !o.Removed && o.CombatTarget == protectee && Dist(o) < 4 * U7Constants.TilesPerChunk)
            .MinBy(Dist);
        if (best is not null && Failures < 5 && _yelled > 0 && Rng.Next(2) != 0 && Npc != protectee && _canYell)
        {
            Say(TextMessages.FirstWillHelp, TextMessages.LastWillHelp);
        }

        return best;
    }

    /// <summary>Exult <c>Combat_schedule::find_foe(mode)</c>.</summary>
    protected U7Object? FindFoe(int mode)
    {
        if (Npc.Alignment != _alignment)
        {
            Opponents.Clear(); // Alignment changed.
            _alignment = Npc.Alignment;
        }

        // Remove any that died (and, for the avatar, sleepers).
        Opponents.RemoveAll(o => o.Removed || o.IsDead || (Npc == Runner.Avatar && o.GetFlag(ObjFlag.Asleep)));
        if (Opponents.Count == 0)
        {
            FindOpponents();
            if (PracticeTarget is { } practice)
            {
                return practice; // For dueling.
            }
        }

        if (Opponents.Count == 0)
        {
            return null; // Nothing to do but give up.
        }

        var foe = mode switch
        {
            AttackMode.Weakest => Weakest(),
            AttackMode.Strongest => Strongest(),
            AttackMode.Nearest => Nearest(),
            AttackMode.Protect => ProtectedAttacker() ?? Opponents[0],
            AttackMode.Random => RandomOpponent(),
            AttackMode.Berserk => Rng.Next(2) == 0 ? RandomOpponent() : Nearest(),
            // Exult's guess: nearest when above half health, else the weakest.
            AttackMode.Defend => Npc.GetProp(ActorProp.Strength) <= 2 * Npc.GetProp(ActorProp.Health)
                ? Nearest()
                : Weakest(),
            AttackMode.Flank => Attacked(),
            _ => Opponents[0]
        };
        if (foe is not null)
        {
            Opponents.Remove(foe);
        }

        return foe is { Removed: false } ? foe : null;
    }

    /// <summary>Exult <c>find_foe()</c>: by the attack mode; manual means the player picks.</summary>
    protected U7Object? FindFoe() => Npc.AttackMode == AttackMode.Manual ? null : FindFoe(Npc.AttackMode);

    /// <summary>Exult <c>Combat_schedule::approach_foe</c>.</summary>
    protected void ApproachFoe(bool forProjectile = false)
    {
        var winf = Combat.GetWeapon(Npc, out _, out WeaponShape);
        var dist = forProjectile ? 1 : winf?.Range ?? 3;
        var opponent = Npc.CombatTarget;
        if (opponent is null && (opponent = FindFoe()) is null)
        {
            Failures++;
            Start(200, 400); // No one left to fight; again in 2/5 s.
            return;
        }

        Combat.SetTarget(Npc, opponent, false);
        var mode = Npc.AttackMode;
        // Time to run?
        if (!CantDie && (mode == AttackMode.Flee ||
                         (mode != AttackMode.Berserk && (Npc.TypeFlags & MoveFlags.All) != 0 &&
                          Npc != Runner.Avatar && Npc.GetProp(ActorProp.Health) < 3)))
        {
            RunAway();
            return;
        }

        if (Rng.Next(4) == 0 && CanCast(ActorFlags.CanTeleport(Npc.Shape)) && Teleport())
        {
            StartBattle();
            Start(Std, Std);
            return;
        }

        var path = PathTo(opponent, dist);
        if (path is null)
        {
            // Failed? Try the nearest opponent.
            Failures++;
            var retryOk = false;
            if (Npc.AttackMode != AttackMode.Manual)
            {
                var closest = FindFoe(AttackMode.Nearest);
                if (closest is null)
                {
                    Combat.SetTarget(Npc, null, false); // No one nearby.
                }
                else if (closest != opponent)
                {
                    opponent = closest;
                    Combat.SetTarget(Npc, opponent, false);
                    path = PathTo(opponent, dist);
                    retryOk = path is not null;
                }
            }

            if (!retryOk)
            {
                // Just try to walk towards the opponent.
                var dirx = opponent.Tx > Npc.Tx ? 2 : opponent.Tx < Npc.Tx ? -2 : Rng.Next(3) - 1;
                var diry = opponent.Ty > Npc.Ty ? 2 : opponent.Ty < Npc.Ty ? -2 : Rng.Next(3) - 1;
                var dest = new TileCoord(Npc.Tx + dirx * (1 + Rng.Next(4)), Npc.Ty + diry * (1 + Rng.Next(4)), Npc.Tz)
                    .Wrapped();
                StartAction(PathWalk.Line(Map, Npc, dest), 2 * Std, 500 + Rng.Next(500));
                Failures++;
                return;
            }
        }

        Failures = 0; // We succeeded.
        StartBattle();
        // First time and on screen: a battle cry, half the time.
        if (_yelled == 0 && Runner.OnScreen(Npc))
        {
            _yelled++;
            if (_canYell && Rng.Next(2) != 0)
            {
                Say(TextMessages.FirstToBattle, TextMessages.LastToBattle);
            }
        }

        // Walk there, checking half way. (Exult passes for_projectile as the goal distance.)
        StartAction(ApproachAction.FromPath(path!, opponent, forProjectile ? 1 : 0), Std,
            Runner.OffScreen(opponent) ? 5 * Std : Std);
    }

    /// <summary>Exult <c>Monster_pathfinder_client(npc, reach, opponent)</c>: an A* walk to within reach of it, stopping if blocked.</summary>
    PathWalk? PathTo(U7Object opponent, int reach) =>
        PathWalk.CreatePath(Map, Npc, ObjectGeometry.Tile(opponent), new MonsterPathClient(Map, Npc, opponent, reach),
            maxBlocked: 0);

    /// <summary>Exult <c>wander_for_attack</c>: a few steps aside, perpendicular to the opponent.</summary>
    void WanderForAttack()
    {
        if (Npc.CombatTarget is not { } opponent)
        {
            return;
        }

        var dir = (ObjectGeometry.Direction(Npc, opponent) + (Rng.Next(2) != 0 ? 2 : 6)) % 8;
        var pos = Here(Npc);
        for (var tries = 0; tries < 3; tries++)
        {
            for (var cnt = 2 + Rng.Next(3); cnt > 0; cnt--)
            {
                pos = pos.Neighbor(dir);
            }

            if (Map.FindSpot(pos, 3, Npc, 1) is { } dest && WalkPathTo(dest, Std, Rng.Next(1000)))
            {
                return;
            }
        }

        Start(250, Rng.Next(3000)); // Failed? Again a little later.
    }

    static bool NotInMeleeRange(WeaponRecord? winf, int dist, int reach) =>
        winf is null ? dist > reach : winf.Uses == WeaponRecord.UsesRanged || dist > reach;

    /// <summary>Exult <c>Combat_schedule::start_strike</c>: shoot or swing at the target, or get closer.</summary>
    protected void StartStrike()
    {
        var opponent = Npc.CombatTarget!;
        var checkLof = !_noBlocking;
        var winf = WeaponShape >= 0 ? Combat.Weapons[WeaponShape] : null;
        var dist = Dist(opponent);
        var reach = winf?.Range ?? Combat.MonsterReach(Npc.Shape);
        var ranged = NotInMeleeRange(winf, dist, reach);
        if (Combat.EffectiveRange(Npc, winf, reach) < dist)
        {
            CurState = State.Approach; // Out of range: get a path.
            ApproachFoe();
            return;
        }

        if (ranged)
        {
            // Out of ammunition or charges?
            if (winf is not null &&
                Combat.GetWeaponAmmo(Npc, winf, WeaponShape, winf.Ammo, winf.Projectile, true, out var ammo) > 0 &&
                ammo is null && !Combat.ReadyAmmo(Npc))
            {
                if (Npc.ScheduleType != ScheduleType.Duel)
                {
                    // Look in the pack for another weapon.
                    if (Combat.SwapWeapons(Npc))
                    {
                        SetWeapon();
                    }
                    else
                    {
                        SetHandToHand();
                    }
                }

                Npc.Frame = ActorWalker.DirFrame(ActorWalker.FacingOfFrame(Npc.Frame), 0);
                CurState = State.Approach;
                Combat.SetTarget(Npc, null, false);
                Start(200, 500);
                return;
            }

            CurState = State.Fire; // Clear to go.
        }
        else
        {
            checkLof = reach > 1;
            CurState = State.Strike;
        }

        if (checkLof && !Runner.IsStraightPath(Npc, opponent))
        {
            // Blocked from striking: try to get adjacent.
            CurState = State.Approach;
            ApproachFoe(true);
            if (_strikeBlocked < 5)
            {
                ++_strikeBlocked;
                if (dist > 5)
                {
                    WanderForAttack(); // Just move a bit.
                }
            }

            return;
        }

        _strikeBlocked = 0;
        StartBattle();
        // Some battle cries now and then (Exult's guess).
        if (_yelled > 0 && Rng.Next(20) == 0 && _canYell)
        {
            Say(TextMessages.FirstTaunt, TextMessages.LastTaunt);
        }

        // Those that neither walk, fly, swim nor float (the Reaper) strike facing north.
        var dir = (Npc.TypeFlags & MoveFlags.All) == 0 ? 0 : ObjectGeometry.Direction(Npc, opponent);
        var frames = CombatEngine.AttackFrames(Map.Catalog, Combat.Weapons, Npc, WeaponShape, ranged, dir);
        StartAction(new FramesAction(frames, Std), 250, 0);
        _dexPoints -= DexToAttack;
    }

    /// <summary>Exult <c>Combat_schedule::run_away</c>: off somewhere 8-15 tiles away, screaming the first time.</summary>
    void RunAway()
    {
        _fleed++;
        var dirx = Rng.Next(2) * 2 - 1;
        var diry = Rng.Next(2) * 2 - 1;
        var pos = new TileCoord(Npc.Tx + dirx * (8 + Rng.Next(8)), Npc.Ty + diry * (8 + Rng.Next(8)), Npc.Tz).Wrapped();
        StartAction(PathWalk.Line(Map, Npc, pos), Std, 0);
        if (_fleed == 1 && !Npc.GetFlag(ObjFlag.Tournament) && Rng.Next(3) != 0 && Runner.OnScreen(Npc))
        {
            _yelled++;
            if (_canYell)
            {
                if (Rng.Next(4) != 0)
                {
                    Say(TextMessages.FleeScreaming, TextMessages.FleeScreaming);
                }
                else
                {
                    Say(TextMessages.FirstFlee, TextMessages.LastFlee);
                }
            }
        }
    }

    bool CanCast(bool ability) => ability && !Npc.GetFlag(ObjFlag.NoSpellCasting);

    /// <summary>
    /// Exult <c>Combat_schedule::teleport</c>: at most every 2-4 s, to a free
    /// spot within 4 of the target (not too far, half the time), if it can see
    /// it. (Exult's fire field left behind is not ported.)
    /// </summary>
    bool Teleport()
    {
        if (Npc.CombatTarget is not { } trg || Runner.Ticks < _teleportTime)
        {
            return false;
        }

        _teleportTime = Runner.Ticks + 2000 + Rng.Next(2000);
        var near = new TileCoord(trg.Tx + 4 - Rng.Next(8), trg.Ty + 4 - Rng.Next(8), trg.Tz).Wrapped();
        if (Map.FindSpot(near, 3, Npc, 1) is not { } dest)
        {
            return false;
        }

        if (dest.Distance(Here(Npc)) > 7 && Rng.Next(2) != 0)
        {
            return false; // Give the avatar a chance to get away.
        }

        // (Exult checks the line of sight now, so that the spell seems to fail.)
        if (Runner.IsStraightPath(Npc, trg))
        {
            Map.MoveObject(Npc, dest.Tx, dest.Ty, dest.Tz);
            Runner.Effects?.AddSprite(7, Npc, 0, 0); // The stars.
        }

        return true;
    }

    /// <summary>Exult <c>Combat_schedule::summon</c>: the summon spell's usecode, if it can see the target.</summary>
    bool Summon()
    {
        if (Npc.CombatTarget is not { } trg || !Runner.IsStraightPath(Npc, trg) || Runner.CallUsecode is null)
        {
            return false;
        }

        Runner.CallUsecode(0x640 + 0x45, Npc);
        Start(Std, Std);
        return true;
    }

    /// <summary>Exult <c>Combat_schedule::be_invisible</c> (without its sound).</summary>
    void BeInvisible()
    {
        Runner.Effects?.AddSprite(12, Npc, 0, 0);
        Npc.SetFlag(ObjFlag.Invisible);
        Start(Std, Std);
    }

    /// <summary>Exult <c>Combat_schedule::set_weapon</c>: what it fights with, readying the best weapon if it has none.</summary>
    protected void SetWeapon(bool removed = false)
    {
        var info = Combat.GetWeapon(Npc, out _, out WeaponShape);
        // (Exult also skips this while the player drags something; spellbooks are not ported.)
        if (!removed && Npc.ScheduleType != ScheduleType.Duel && CurState != State.WaitReturn)
        {
            if (info is null)
            {
                Combat.ReadyBestWeapon(Npc);
                info = Combat.GetWeapon(Npc, out _, out WeaponShape);
            }
            else
            {
                Combat.ReadyBestShield(Npc);
            }
        }

        if (info is null)
        {
            SetHandToHand();
        }
        else
        {
            _noBlocking = false;
        }

        if (CurState is State.Strike or State.Fire)
        {
            CurState = State.Approach; // Got to restart the attack.
        }
    }

    /// <summary>Exult <c>Combat_schedule::set_hand_to_hand</c>: bare hands, the weapon put aside.</summary>
    protected void SetHandToHand()
    {
        WeaponShape = -1;
        _noBlocking = false;
        if (Equipment.GetReadied(Npc, ReadySpot.Lhand) is not { } weapon)
        {
            return;
        }

        Map.TakeFromWorld(weapon);
        foreach (var spot in (int[])[ReadySpot.Belt, ReadySpot.Back2h, ReadySpot.BackShield, ReadySpot.Rhand, ReadySpot.Back])
        {
            if (Equipment.GetReadied(Npc, spot) is null &&
                Equipment.AddReadied(Npc, weapon, spot, Map.Catalog, Map, forcePos: true))
            {
                return;
            }
        }

        Equipment.AddToActor(Npc, weapon, Map.Catalog, Map);
    }

    /// <summary>Exult <c>Need_new_opponent</c>: none, dead, invisible (one time in four), or gone off the screen.</summary>
    bool NeedNewOpponent()
    {
        var opponent = Npc.CombatTarget;
        if (opponent is null || opponent.Removed || (opponent.IsActor && opponent.IsDead) ||
            (opponent.GetFlag(ObjFlag.Invisible) && Rng.Next(4) == 0))
        {
            return true;
        }

        return Runner.OffScreen(opponent) && !Runner.OffScreen(Npc);
    }

    /// <summary>Exult <c>start_battle</c>: battle music for the avatar's fight.</summary>
    protected void StartBattle()
    {
        if (StartedBattle)
        {
            return;
        }

        Combat.StartBattleMusic(Npc, Opponents.Count > 0 || Npc.CombatTarget is { IsActor: true });
        StartedBattle = true;
    }

    public override void NowWhat()
    {
        if (CurState == State.Initial)
        {
            // Nothing in the first state, so that usecode can, e.g., set the
            // opponent. Way far away? Exult lets it go dormant.
            if (ObjectGeometry.Distance(Npc, Runner.Avatar) > 50)
            {
                return;
            }

            CurState = State.Approach;
            Start(200, 200);
            return;
        }

        if (Npc.GetFlag(ObjFlag.Asleep))
        {
            Start(200, 1000); // Check again in a second.
            return;
        }

        // Running away?
        if (Npc.AttackMode == AttackMode.Flee)
        {
            if (CantDie)
            {
                Npc.AttackMode = AttackMode.Nearest;
            }
            else if (_fleed > 2 && !Combat.InCombat && Runner.Party?.IsInParty(Npc) == true)
            {
                Runner.SetScheduleType(Npc, ScheduleType.FollowAvatar);
            }
            else
            {
                RunAway();
            }

            return;
        }

        // Does the opponent still breathe?
        if (NeedNewOpponent())
        {
            Combat.SetTarget(Npc, null, false);
            CurState = State.Approach;
        }

        var opponent = Npc.CombatTarget;
        switch (CurState)
        {
            case State.Approach:
                if (opponent is null)
                {
                    ApproachFoe();
                }
                else if (_dexPoints >= DexToAttack)
                {
                    var effint = Npc.GetProp(ActorProp.Intelligence);
                    if (!Npc.GetFlag(ObjFlag.Invisible) && CanCast(ActorFlags.CanBeInvisible(Npc.Shape)) &&
                        Rng.Next(300) < effint)
                    {
                        BeInvisible();
                        _dexPoints -= DexToAttack;
                    }
                    else if (CanCast(ActorFlags.CanSummon(Npc.Shape)) && Rng.Next(600) < effint && Summon())
                    {
                        _dexPoints -= DexToAttack;
                    }
                    else
                    {
                        StartStrike();
                    }
                }
                else
                {
                    _dexPoints += Npc.GetProp(ActorProp.Dexterity);
                    Start(Std, Std);
                }

                break;
            case State.Strike:
                // Back to the ready frame, and the blow lands (or misses).
                CurState = State.Approach;
                StartAction(new FramesAction([ActorWalker.DirFrame(ActorWalker.FacingOfFrame(Npc.Frame), 3)], Std), Std,
                    Std);
                if (opponent is not null && Combat.AttackTarget(Npc, opponent, WeaponShape, true))
                {
                    // Strike but once at objects.
                    if (Npc.CombatTarget is { IsActor: false })
                    {
                        Combat.SetTarget(Npc, null, false);
                    }

                    return;
                }

                break;
            case State.Fire:
                Failures = 0;
                CurState = State.Approach;
                if (opponent is not null)
                {
                    Combat.AttackTarget(Npc, opponent, WeaponShape, true);
                }

                StartAction(new FramesAction([ActorWalker.DirFrame(ActorWalker.FacingOfFrame(Npc.Frame), 3)], Std), Std,
                    Std);
                // Strike but once at objects.
                if (Npc.CombatTarget is { IsActor: false })
                {
                    Combat.SetTarget(Npc, null, false);
                    return;
                }

                break;
            case State.WaitReturn: // The boomerang should be back.
                CurState = State.Approach;
                _dexPoints += Npc.GetProp(ActorProp.Dexterity);
                Start(Std, Std);
                break;
        }

        if (Failures > 5 && Npc != Runner.Avatar)
        {
            GiveUp();
        }
    }

    /// <summary>Exult: too many failures; give up for now.</summary>
    void GiveUp()
    {
        if (Runner.Party?.IsInParty(Npc) == true)
        {
            Runner.SetScheduleType(Npc, ScheduleType.FollowAvatar);
        }
        else if (!Runner.OnScreen(Npc))
        {
            // Off screen: stop trying (Exult lets it go dormant).
        }
        else if (Npc.Alignment == Alignment.Good && _prevSchedule != ScheduleType.Combat)
        {
            // Back to its normal schedule.
            Runner.UpdateSchedule(Brain, null);
            if (Npc.ScheduleType == ScheduleType.Combat)
            {
                Runner.SetScheduleType(Npc, _prevSchedule);
            }
        }
        else
        {
            // Wander randomly.
            var dist = 2 + Rng.Next(3);
            var dest = new TileCoord(Npc.Tx - dist + Rng.Next(2 * dist), Npc.Ty - dist + Rng.Next(2 * dist), Npc.Tz)
                .Wrapped();
            StartAction(PathWalk.Line(Map, Npc, dest), 2 * Std, Rng.Next(1000));
        }
    }

    /// <summary>Exult <c>Combat_schedule::ending</c>: the avatar leaving a fight with foes close by gets the running-away music.</summary>
    public override void Ending(int newType)
    {
        if (Npc != Runner.Avatar || Runner.InUsecode)
        {
            return;
        }

        FindOpponents();
        if (Opponents.Any(o => !o.Removed && Dist(o) < 40 / 2 - 2 && FastPathClient.IsGrabable(Map, Npc, o)))
        {
            Combat.RunAwayMusic();
        }
    }

    /// <summary>Exult <c>set_state</c>, for a thrown weapon to come back.</summary>
    public void WaitForReturn() => CurState = State.WaitReturn;

    /// <summary>Exult <c>set_weapon(true)</c>: the weapon in hand was used up.</summary>
    public void WeaponRemoved() => SetWeapon(true);
}

/// <summary>
/// Exult <c>Duel_schedule</c>: play-fighting (no damage, no battle music).
/// One time in three at the archery target (735) with a bow and a couple of
/// arrows, else with a two-handed sword at the fencing dummy (860, one time
/// in three) or against another duelist within 24 tiles; every eighth blow,
/// or when the target is full of arrows, it breaks off and walks about.
/// </summary>
public sealed class DuelSchedule : CombatSchedule
{
    const int ArcheryTarget = 735;
    const int FencingDummy = 860;
    const int Bow = 597;
    const int Arrows = 722;
    const int TrainingSword = 602;

    readonly TileCoord _start;
    int _attacks;

    public DuelSchedule(NpcBrain brain)
        : base(brain, ScheduleType.Duel)
    {
        _start = Here(brain.Npc);
        StartedBattle = true; // Avoid playing music.
    }

    /// <summary>Exult <c>Ready_duel_weapon</c>: that weapon in hand (its own, or a new one), and 1-3 arrows if it needs them.</summary>
    void ReadyDuelWeapon(int wshape, int ashape)
    {
        var weap = Equipment.GetReadied(Npc, ReadySpot.Lhand);
        if (weap is null || weap.Shape != wshape)
        {
            var newweap = Carried(wshape).FirstOrDefault() ?? Map.CreateIregObject(wshape, 0);
            Map.TakeFromWorld(newweap);
            if (weap is not null)
            {
                Map.TakeFromWorld(weap);
            }

            Equipment.AddToActor(Npc, newweap, Map.Catalog, Map); // Should go in the right spot.
            if (weap is not null)
            {
                Equipment.AddToActor(Npc, weap, Map.Catalog, Map);
            }
        }

        if (ashape == -1)
        {
            return; // No ammo needed.
        }

        if (Equipment.GetReadied(Npc, ReadySpot.Ammo) is { } quiver)
        {
            Map.RemoveObject(quiver); // Toss the current ammo.
        }

        var arrows = Map.CreateIregObject(ashape, 0);
        arrows.Quality = 1 + Rng.Next(3);
        Equipment.AddToActor(Npc, arrows, Map.Catalog, Map);
    }

    /// <summary>Exult <c>Duel_schedule::find_opponents</c>.</summary>
    protected override void FindOpponents()
    {
        Opponents.Clear();
        _attacks = 0;
        PracticeTarget = null;
        var r = Rng.Next(3);
        if (r == 0)
        {
            // First look for practice targets: the archery target.
            PracticeTarget = FindClosest([ArcheryTarget], 24).FirstOrDefault();
            if (PracticeTarget is not null)
            {
                ReadyDuelWeapon(Bow, Arrows);
            }
        }

        if (PracticeTarget is null)
        {
            // The fencing dummy, or a duelling opponent.
            ReadyDuelWeapon(TrainingSword, -1);
            if (r == 1)
            {
                PracticeTarget = FindClosest([FencingDummy], 24).FirstOrDefault();
            }
        }

        SetWeapon();
        if (PracticeTarget is not null)
        {
            Combat.SetTarget(Npc, PracticeTarget, false);
            return;
        }

        Opponents.AddRange(Map.FindNearby(Here(Npc), U7Constants.AnyShape, 24, 8)
            .Where(opp => opp != Npc && opp.ScheduleType == ScheduleType.Duel &&
                          (opp.CombatTarget is null || opp.CombatTarget == Npc)));
    }

    public override void NowWhat()
    {
        if (CurState is not (State.Strike or State.Fire))
        {
            base.NowWhat();
            return;
        }

        _attacks++;
        // The practice target full?
        if (PracticeTarget is { Shape: ArcheryTarget } target && target.Frame > 0 && target.Frame % 3 == 0)
        {
            _attacks = 0; // Break off. (Exult: "should walk there".)
            target.Frame = 0;
        }

        if (_attacks % 8 != 0)
        {
            base.NowWhat();
            return;
        }

        // Time to break off: somewhere near where it started.
        Combat.SetTarget(Npc, null, false);
        var pos = new TileCoord(_start.Tx + Rng.Next(24) - 12, _start.Ty + Rng.Next(24) - 12, _start.Tz).Wrapped();
        if (Map.FindSpot(pos, 3, Npc, 1) is not { } dest || !WalkPathTo(dest, Std, Rng.Next(2000)))
        {
            Start(250, Rng.Next(3000)); // Failed? Again a little later.
        }
    }
}
