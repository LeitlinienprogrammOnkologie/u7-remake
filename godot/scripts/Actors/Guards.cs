using Godot;
using U7.Core;
using U7.Data;
using U7.World;

namespace U7.Actors;

/// <summary>
/// Exult <c>Game_window</c>'s law: <c>theft</c> (a witness warns, then calls
/// the guards), <c>find_witness</c>, <c>call_guards</c> (guards run up from
/// off the screen to arrest the avatar, <see cref="ArrestAvatarSchedule"/>),
/// <c>attack_avatar</c> (guards and residents set on the avatar), and
/// <c>Actor::fight_back</c>'s cry for help when the party bullies someone.
/// </summary>
public sealed class Guards(GameMap map, U7Object avatar, CombatEngine combat, ScheduleRunner schedules, PartyManager party)
{
    /// <summary>
    /// Exult <c>Game_window::get_guard_shape</c>, by where the avatar is:
    /// Black Gate has the one guard everywhere; Serpent Isle has rangers in
    /// Moonshade, Fawn's guards and Monitor's pikemen, and none elsewhere (-1).
    /// </summary>
    public int GuardShape
    {
        get
        {
            if (!GameContext.IsSi)
            {
                return 0x3b2;
            }

            var (tx, ty) = (avatar.Tx, avatar.Ty);
            return tx >= 2054 && ty >= 1698 && tx < 2590 && ty < 2387 ? 0x103 // Moonshade's rangers.
                : tx >= 895 && ty >= 1604 && tx < 1173 && ty < 1960 ? 0x17d // Fawn's guards.
                : tx >= 670 && ty >= 2430 && tx < 1135 && ty < 2800 ? 0xe4 // Monitor's pikemen.
                : -1;
        }
    }

    /// <summary>Exult <c>Actor::is_goblin</c> (<c>shape_info.txt</c>'s goblin actor flag): Serpent Isle's goblins have their own lines.</summary>
    static bool IsGoblin(U7Object npc) => (ShapeCatalog.Current[npc.Shape].ActorFlags & (1 << ActorFlags.Goblin)) != 0;

    /// <summary>Exult <c>ArrestUsecode</c>: the guard's arrest conversation.</summary>
    public const int ArrestUsecode = 0x625;

    /// <summary>Exult <c>Game_window::in_dungeon</c>: no guards there.</summary>
    public Func<bool>? InDungeon { get; set; }

    /// <summary>Exult <c>close_all_gumps</c>, before the guards come.</summary>
    public Action? CloseGumps { get; set; }

    readonly Random _rng = new();
    int _theftWarnings;
    int _theftCx = -1;
    int _theftCy = -1;
    /// <summary>Exult <c>fight_back</c>'s <c>lastcall</c>: when someone last yelled for help (ms).</summary>
    ulong _lastCall;

    bool InDungeonNow => InDungeon?.Invoke() ?? false;

    /// <summary>Exult <c>Dragging_info::drop</c>: a possible theft is one outside dungeons.</summary>
    public void PossibleTheft()
    {
        if (!InDungeonNow)
        {
            Theft();
        }
    }

    /// <summary>
    /// Exult <c>Game_window::theft</c>: something not okay to take was moved.
    /// The nearest neutral NPC facing the avatar sees it, faces it and hounds
    /// it, and warns it two to four times in a chunk before calling the guards;
    /// unseen, someone near may have heard a noise; an invisible avatar is seen
    /// only by those who see the invisible.
    /// </summary>
    public void Theft()
    {
        var cx = avatar.ChunkX;
        var cy = avatar.ChunkY;
        if (cx != _theftCx || cy != _theftCy)
        {
            _theftCx = cx;
            _theftCy = cy;
            _theftWarnings = 0;
        }

        var witness = FindWitness(out var closest, Alignment.Neutral);
        if (witness is null)
        {
            if (closest is not null && _rng.Next(2) != 0)
            {
                SayOr(closest, TextMessages.GoblinHeardSomething, TextMessages.HeardSomething, TextMessages.HeardSomething);
            }

            return; // Didn't get caught.
        }

        if (avatar.GetFlag(ObjFlag.Invisible) && !combat.CanSeeInvisible(witness))
        {
            SayOr(witness, TextMessages.GoblinInvisTheft, TextMessages.FirstInvisTheft, TextMessages.LastInvisTheft);
            return;
        }

        witness.Frame = ActorWalker.DirFrame(ObjectGeometry.Direction(witness, avatar), 0);
        if (witness.ScheduleType is not (ScheduleType.Combat or ScheduleType.Hound))
        {
            schedules.SetScheduleType(witness, ScheduleType.Hound);
        }

        _theftWarnings++;
        if (_theftWarnings < 2 + _rng.Next(3))
        {
            // Just a warning this time.
            SayOr(witness, TextMessages.GoblinTheft, TextMessages.FirstTheft, TextMessages.LastTheft);

            return;
        }

        CloseGumps?.Invoke();
        CallGuards(witness, theft: true);
    }

    /// <summary>
    /// Exult <c>Game_window::find_witness</c>: of the NPCs within 12 tiles
    /// (not the party's, sentient, able to act, of the alignment, guards only
    /// for neutral, able to reach the avatar), the nearest looking its way or
    /// hounding it; <paramref name="closest"/> is the nearest of the others.
    /// </summary>
    public U7Object? FindWitness(out U7Object? closest, int align)
    {
        closest = null;
        var closestDist = 5000;
        U7Object? witness = null;
        var witnessDist = 5000;
        var gshape = GuardShape;
        foreach (var npc in NearbyActors(12))
        {
            if (party.IsInParty(npc) || !combat.IsSentient(npc) || !CombatSchedule.CanAct(npc))
            {
                continue;
            }

            // Guards only assist neutral (and chaotic) NPCs.
            if (npc.EffectiveAlignment != align || (npc.Shape == gshape && align != Alignment.Neutral))
            {
                continue;
            }

            var dist = ObjectGeometry.Distance(npc, avatar);
            if (dist >= witnessDist || !FastPathClient.IsGrabable(map, npc, avatar))
            {
                continue;
            }

            var dir = ObjectGeometry.Direction(npc, avatar);
            var dirdiff = (dir - ActorWalker.FacingOfFrame(npc.Frame) + 8) % 8;
            if (dirdiff < 3 || dirdiff > 5 || npc.ScheduleType == ScheduleType.Hound)
            {
                witness = npc;
                witnessDist = dist;
            }
            else if (dist < closestDist)
            {
                closest = npc;
                closestDist = dist;
            }
        }

        return witness;
    }

    /// <summary>
    /// Exult <c>Game_window::call_guards</c> (usecode's <c>call_guards</c>
    /// too): the witness (or one found) calls for them, and if there is room
    /// by the avatar, one to three guards are made off the screen, chaotic,
    /// to arrest it, and run up; the danger music plays. Not in dungeons or
    /// after Armageddon.
    /// </summary>
    public void CallGuards(U7Object? witness = null, bool theft = false)
    {
        if (combat.Armageddon || InDungeonNow)
        {
            return;
        }

        var gshape = GuardShape;
        var align = witness?.EffectiveAlignment ?? Alignment.Neutral;
        witness ??= FindWitness(out _, align);
        if (witness is not null)
        {
            if (IsGoblin(witness))
            {
                if (gshape < 0)
                {
                    SayOne(witness, TextMessages.GoblinNeedHelp);
                }
                else if (theft)
                {
                    SayOne(witness, TextMessages.GoblinCallGuardsTheft);
                }
                else
                {
                    Say(witness, TextMessages.FirstGoblinCallGuards, TextMessages.LastGoblinCallGuards);
                }
            }
            else if (combat.CanSpeak(witness))
            {
                if (gshape < 0)
                {
                    Say(witness, TextMessages.FirstNeedHelp, TextMessages.LastNeedHelp);
                }
                else if (theft)
                {
                    Say(witness, TextMessages.FirstCallGuardsTheft, TextMessages.LastCallGuardsTheft);
                }
                else
                {
                    Say(witness, TextMessages.FirstCallGuards, TextMessages.LastCallGuards);
                }
            }
        }

        if (gshape < 0)
        {
            // No local guards: on to attack_avatar.
            AttackAvatar(0, align);
            return;
        }

        if (map.FindSpot(Here(avatar), 5, gshape, 0, 1) is null)
        {
            return;
        }

        var numGuards = 1 + _rng.Next(3);
        for (var i = 0; i < numGuards; i++)
        {
            var guard = combat.CreateMonsterAt(gshape, Offscreen(), ScheduleType.ArrestAvatar, Alignment.Chaotic);
            ApproachAnother(guard, avatar);
        }

        DangerMusic();
    }

    /// <summary>
    /// Exult <c>Game_window::stop_arresting</c> (Serpent Isle's
    /// <c>stop_arresting</c>): the local guards within 20 tiles arresting the
    /// avatar wander off, neutral. Not in dungeons, after Armageddon or where
    /// there are no guards.
    /// </summary>
    public void StopArresting()
    {
        var gshape = GuardShape;
        if (combat.Armageddon || InDungeonNow || gshape < 0)
        {
            return;
        }

        foreach (var npc in map.FindNearbyExult(Here(avatar), gshape, 20, 0x28).Where(o => o.IsActor))
        {
            if (!party.IsInParty(npc) && npc.ScheduleType == ScheduleType.ArrestAvatar)
            {
                schedules.SetScheduleType(npc, ScheduleType.Wander);
                npc.Alignment = Alignment.Neutral; // Exult: prevent the guard from becoming hostile.
            }
        }
    }

    /// <summary>
    /// Exult <c>Game_window::attack_avatar</c> (usecode's <c>attack_avatar</c>
    /// too): <paramref name="createGuards"/> guards made off the screen run up
    /// to fight the avatar (not in dungeons), and up to three NPCs within 20
    /// tiles that can reach it join in: guards, and those of
    /// <paramref name="align"/>. The danger music plays.
    /// </summary>
    public void AttackAvatar(int createGuards = 0, int align = Alignment.Neutral)
    {
        if (combat.Armageddon)
        {
            return;
        }

        var inDungeon = InDungeonNow;
        var gshape = GuardShape;
        if (gshape >= 0 && !inDungeon)
        {
            while (createGuards-- > 0)
            {
                var guard = combat.CreateMonsterAt(gshape, Offscreen(), ScheduleType.Combat, Alignment.Chaotic);
                combat.SetTarget(guard, avatar, true);
                ApproachAnother(guard, avatar);
            }
        }

        var helpers = 0;
        foreach (var npc in NearbyActors(20))
        {
            if (CombatSchedule.CanAct(npc) && !party.IsInParty(npc) && combat.IsSentient(npc) &&
                ((npc.Shape == gshape && !inDungeon) || npc.EffectiveAlignment == align) &&
                FastPathClient.IsGrabable(map, npc, avatar))
            {
                combat.SetTarget(npc, avatar, true);
                if (++helpers >= 3)
                {
                    break;
                }
            }
        }

        DangerMusic();
    }

    /// <summary>
    /// Exult <c>Actor::fight_back</c>'s "being a bully?": a sentient NPC hit by
    /// one of the party, if it can act (or else a witness of its alignment
    /// saw, or one time in ten someone near heard), yells (at most every 10 s,
    /// or by the luck of the die): for the guards if it is neutral or a guard
    /// (one or two come, not in dungeons), else for help; and the guards and
    /// its own kind set on the avatar.
    /// </summary>
    public void Bullied(U7Object victim, U7Object attacker)
    {
        if (!party.IsInParty(attacker) || party.IsInParty(victim) || !combat.IsSentient(victim))
        {
            return;
        }

        var align = victim.EffectiveAlignment;
        var witness = victim;
        if (!CombatSchedule.CanAct(victim))
        {
            var seen = FindWitness(out var closest, align);
            if (seen is not null)
            {
                witness = seen;
            }
            else if (closest is not null && _rng.Next(10) == 0)
            {
                witness = closest;
            }
            else
            {
                return;
            }
        }

        var now = Time.GetTicksMsec();
        if (now - _lastCall <= 10000 && _rng.Next(20) != 0)
        {
            return;
        }

        var numGuards = 0;
        var gshape = GuardShape;
        if (!InDungeonNow && gshape >= 0 && (witness.Shape == gshape || align == Alignment.Neutral))
        {
            numGuards = 1 + _rng.Next(2);
        }

        victim.BarkUntilMsec = 0; // Exult remove_text_effect.
        if (IsGoblin(witness))
        {
            if (numGuards > 0)
            {
                Say(witness, TextMessages.FirstGoblinCallGuards, TextMessages.LastGoblinCallGuards);
            }
            else
            {
                SayOne(witness, TextMessages.GoblinNeedHelp);
            }
        }
        else if (combat.CanSpeak(witness))
        {
            if (numGuards > 0)
            {
                Say(witness, TextMessages.FirstCallPolice, TextMessages.LastCallPolice);
            }
            else
            {
                Say(witness, TextMessages.FirstNeedHelp, TextMessages.LastNeedHelp);
            }
        }

        _lastCall = now; // To reduce the guard pile-up.
        AttackAvatar(numGuards, align);
    }

    bool ApproachAnother(U7Object actor, U7Object other) => schedules.ApproachAnother(actor, other);

    /// <summary>Exult: guards are made 128 tiles southeast of the avatar.</summary>
    TileCoord Offscreen() => new TileCoord(avatar.Tx + 128, avatar.Ty + 128, avatar.Tz).Wrapped();

    /// <summary>
    /// Exult's "guaranteed way to do it": the danger music (track 10), unless
    /// battle music (9-12, 15-18) plays.
    /// </summary>
    void DangerMusic()
    {
        if (combat.Music is { } music && music.CurrentTrack is not (>= 9 and <= 12 or >= 15 and <= 18))
        {
            music.Start(10, repeat: true);
        }
    }

    /// <summary>Exult <c>find_nearby_actors(c_any_shapenum, dist, 0x28)</c>: living NPCs, invisible ones too.</summary>
    IEnumerable<U7Object> NearbyActors(int dist) =>
        map.FindNearbyExult(Here(avatar), U7Constants.AnyShape, dist, 0x28).Where(o => o.IsActor);

    void Say(U7Object npc, int first, int last) => schedules.Say?.Invoke(npc, TextMessages.Random(first, last));

    void SayOne(U7Object npc, int msg) => Say(npc, msg, msg);

    /// <summary>Exult's pattern: a goblin says its own line, anyone else who can speak one of the others.</summary>
    void SayOr(U7Object npc, int goblin, int first, int last)
    {
        if (IsGoblin(npc))
        {
            SayOne(npc, goblin);
        }
        else if (combat.CanSpeak(npc))
        {
            Say(npc, first, last);
        }
    }

    static TileCoord Here(U7Object obj) => new(obj.Tx, obj.Ty, obj.Tz);
}
