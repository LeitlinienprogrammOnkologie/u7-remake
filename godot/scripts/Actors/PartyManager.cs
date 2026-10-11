using U7.Core;
using U7.Data;

namespace U7.Actors;

/// <summary>
/// Exult <c>Party_manager</c>: who is in the party, formation stepping when
/// the avatar walks (<c>get_followers</c> / <c>move_followers</c> /
/// <c>step</c>), and keeping the party together on teleports.
/// </summary>
public sealed class PartyManager
{
    public const int MaxParty = 8;

    // For each party member, the party ids of its two followers:
    //        A
    //       0 1
    //      2 3 4
    //     7 5 6 8
    static readonly int[,] Followers =
    {
        { 0, 1 }, { 2, 3 }, { -1, 4 }, { 7, -1 }, { 5, 6 }, { -1, 8 }, { -1, -1 }, { -1, -1 }, { -1, -1 }
    };

    // Follower offsets by 4-way direction (N, E, S, W): behind-left, behind-right.
    static readonly int[,] LeftOffsets = { { -2, 2 }, { -2, -2 }, { 2, -2 }, { 2, 2 } };
    static readonly int[,] RightOffsets = { { 2, 2 }, { -2, 2 }, { -2, -2 }, { 2, -2 } };
    static readonly int[] DeltaDir = [0, 1, 7, 2, 6, 3, 5];
    const int MaxCost = 10000;

    /// <summary>Exult <c>Actor::follow</c> spread when the leader has stopped.</summary>
    public static readonly int[] XOffs = [-1, 1, -2, 2, -3, 3, -4, 4, -5, 5];
    public static readonly int[] YOffs = [1, -1, 2, -2, 3, -3, 4, -4, 5, -5];

    readonly GameMap _map;
    readonly U7Object _avatar;
    readonly List<U7Object?> _npcs;
    readonly List<U7Object> _members = new();
    readonly List<U7Object> _valid = new();

    public ScheduleRunner? Schedules { get; set; }
    public IReadOnlyList<U7Object> Members => _members;

    /// <summary>
    /// Exult <c>Party_manager::dead_party</c> (16 at most): members who died,
    /// in order, back in the party if raised. Exult saves only the living, so
    /// a load forgets them.
    /// </summary>
    readonly List<U7Object> _dead = new();
    const int MaxDeadParty = 16;
    public IReadOnlyList<U7Object> DeadMembers => _dead;

    /// <summary>
    /// Exult <c>Party_manager::update_party_status</c> for an actor that died
    /// or was raised: a dead member goes to the dead list, a raised one on it
    /// back into the party.
    /// </summary>
    public void UpdatePartyStatus(U7Object npc)
    {
        if (npc.IsDead)
        {
            if (RemoveFromParty(npc) && _dead.Count < MaxDeadParty && !_dead.Contains(npc))
            {
                _dead.Add(npc);
            }
        }
        else if (_dead.Remove(npc))
        {
            AddToParty(npc);
        }
    }

    /// <summary>
    /// Exult <c>Main_actor::get_followers</c> (when the avatar sets off on a
    /// path, stops, ends a walk in formation or is teleported): members not
    /// fighting, waiting or loitering follow again (those sitting on a barge
    /// or a chair get up), and those already following set off after the
    /// avatar (<see cref="FollowAvatarSchedule.Follow"/>). With
    /// <paramref name="follow"/> false only the first: as a held walk starts,
    /// which Exult leaves to the formation (the remake calls the party along
    /// then too, as Exult's walk without formation does).
    /// </summary>
    public void CallFollowers(bool follow = true)
    {
        foreach (var npc in _members)
        {
            if (npc.GetFlag(ObjFlag.Asleep) || npc.IsDead ||
                npc.ScheduleType is ScheduleType.Combat or ScheduleType.Wait or ScheduleType.Loiter)
            {
                continue;
            }

            if (npc.ScheduleType != ScheduleType.FollowAvatar)
            {
                Schedules?.SetScheduleType(npc, ScheduleType.FollowAvatar);
            }
            else if (follow && Schedules?.ScheduleOf(npc) is FollowAvatarSchedule schedule)
            {
                schedule.Follow(_avatar);
            }
        }
    }
    public int Count => _members.Count;

    public PartyManager(GameMap map, U7Object avatar, List<U7Object?> npcs)
    {
        _map = map;
        _avatar = avatar;
        _npcs = npcs;
    }

    /// <summary>Party id (0-based), or -1 for the avatar and non-members.</summary>
    public int PartyId(U7Object npc) => _members.IndexOf(npc);

    public bool IsInParty(U7Object obj) => obj == _avatar || _members.Contains(obj);

    /// <summary>Exult <c>Party_manager::add_to_party</c>.</summary>
    public bool AddToParty(U7Object? npc)
    {
        if (npc is null || npc == _avatar || npc.NpcNum <= 0 || _members.Count >= MaxParty || _members.Contains(npc))
        {
            return false;
        }

        _dead.Remove(npc); // Exult: "Just to be sure."
        npc.Alignment = Alignment.Good;
        npc.SetFlag(ObjFlag.InParty);
        npc.SetFlagWithContents(ObjFlag.OkayToTake);
        _members.Add(npc);
        return true;
    }

    /// <summary>Exult <c>Party_manager::remove_from_party</c>.</summary>
    public bool RemoveFromParty(U7Object? npc)
    {
        if (npc is null || !_members.Remove(npc))
        {
            return false;
        }

        npc.ClearFlag(ObjFlag.InParty);
        return true;
    }

    /// <summary>
    /// Exult <c>Party_manager::link_party</c>: rebuild the party after a load
    /// from the members <c>usecode.dat</c> saved, in their order.
    /// </summary>
    public void LinkParty(IReadOnlyList<int>? saved = null)
    {
        _avatar.SetFlag(ObjFlag.InParty);
        _avatar.SetFlagWithContents(ObjFlag.OkayToTake);
        if (saved is not null)
        {
            // Exult: the members saved in usecode.dat, in their order; bad entries are skipped
            // and the dead go to the dead-party list.
            _members.Clear();
            _dead.Clear();
            foreach (var num in saved)
            {
                if (num <= 0 || num >= _npcs.Count || _npcs[num] is not { } member || _members.Contains(member))
                {
                    continue;
                }

                if (member.IsDead)
                {
                    if (_dead.Count < MaxDeadParty && !_dead.Contains(member))
                    {
                        _dead.Add(member);
                    }

                    continue;
                }

                member.SetFlag(ObjFlag.InParty);
                member.SetFlagWithContents(ObjFlag.OkayToTake);
                _members.Add(member);
            }

            return;
        }

        // Saves from before usecode.dat was written: the in_party flags, in NPC order.
        foreach (var npc in _npcs)
        {
            if (npc is { Unused: false, NpcNum: > 0 } && npc.GetFlag(ObjFlag.InParty) && !npc.IsDead)
            {
                npc.ClearFlag(ObjFlag.InParty);
                AddToParty(npc);
            }
        }
    }

    public string HudText() =>
        _members.Count == 0 ? "" : "  party: " + string.Join(", ", _members.Select(m => m.NpcName));

    /// <summary>Called after every avatar step; teleports are handled by <see cref="FollowTeleport"/>.</summary>
    public void AvatarStepped(int fromTx, int fromTy)
    {
        var dx = U7Constants.TileDelta(fromTx, _avatar.Tx);
        var dy = U7Constants.TileDelta(fromTy, _avatar.Ty);
        if (Math.Abs(dx) > 1 || Math.Abs(dy) > 1 || (dx == 0 && dy == 0))
        {
            return;
        }

        GetFollowers(ActorWalker.DirIndex(dx, dy));
    }

    /// <summary>Exult <c>Party_manager::get_followers</c>: members free to walk in formation take a step.</summary>
    public void GetFollowers(int dir)
    {
        _valid.Clear();
        foreach (var npc in _members)
        {
            if (npc.IsDead || npc.GetFlag(ObjFlag.Asleep) || npc.GetFlag(ObjFlag.Paralyzed))
            {
                continue;
            }

            var sched = npc.ScheduleType;
            if (sched is ScheduleType.Combat or ScheduleType.Wait or ScheduleType.Loiter ||
                Schedules?.InQueue(npc) == true) // Already walking?
            {
                continue;
            }

            _valid.Add(npc);
        }

        if (_valid.Count > 0)
        {
            MoveFollowers(_avatar, dir);
        }
    }

    /// <summary>Exult <c>Party_manager::move_followers</c>: each member drags its two followers along.</summary>
    void MoveFollowers(U7Object leader, int dir)
    {
        var id = leader == _avatar ? -1 : PartyId(leader);
        var lnum = Followers[1 + id, 0];
        var rnum = Followers[1 + id, 1];
        if (lnum == -1 && rnum == -1)
        {
            return;
        }

        var dir4 = dir / 2;
        var lnpc = lnum == -1 || lnum >= _valid.Count ? null : _valid[lnum];
        var rnpc = rnum == -1 || rnum >= _valid.Count ? null : _valid[rnum];
        var ldir = -1;
        var rdir = -1;
        if (lnpc is not null)
        {
            ldir = Step(lnpc, leader, dir,
                U7Constants.WrapTile(leader.Tx + LeftOffsets[dir4, 0]),
                U7Constants.WrapTile(leader.Ty + LeftOffsets[dir4, 1]));
        }

        if (rnpc is not null)
        {
            rdir = Step(rnpc, leader, dir,
                U7Constants.WrapTile(leader.Tx + RightOffsets[dir4, 0]),
                U7Constants.WrapTile(leader.Ty + RightOffsets[dir4, 1]));
        }

        if (ldir >= 0 && lnpc is { IsDead: false })
        {
            MoveFollowers(lnpc, ldir);
        }

        if (rdir >= 0 && rnpc is { IsDead: false })
        {
            MoveFollowers(rnpc, rdir);
        }
    }

    /// <summary>
    /// Exult <c>Party_manager::step</c>: one tile toward the formation spot, or
    /// the cheapest nearby step if that is blocked. Returns the direction moved,
    /// or <paramref name="dir"/> when the follower stayed put.
    /// </summary>
    int Step(U7Object npc, U7Object leader, int dir, int destTx, int destTy)
    {
        var dx = Math.Clamp(U7Constants.TileDelta(npc.Tx, destTx), -1, 1);
        var dy = Math.Clamp(U7Constants.TileDelta(npc.Ty, destTy), -1, 1);
        if (dx == 0 && dy == 0)
        {
            return dir;
        }

        var tx = U7Constants.WrapTile(npc.Tx + dx);
        var ty = U7Constants.WrapTile(npc.Ty + dy);
        var facing = ActorWalker.DirIndex(dx, dy);
        if (IsStepOkay(npc, leader, tx, ty, out var nz))
        {
            ActorWalker.MoveTo(_map, npc, tx, ty, nz, facing);
            return facing;
        }

        if (!npc.IsDead && TakeBestStep(npc, leader, facing, out var moved))
        {
            return moved;
        }

        return dir;
    }

    /// <summary>Exult <c>Is_step_okay</c>: dz to the leader at most 2, and a clear line to them.</summary>
    bool IsStepOkay(U7Object npc, U7Object leader, int tx, int ty, out int nz)
    {
        if (!ActorWalker.ResolveStep(_map, npc, tx, ty, npc.Tz, out nz))
        {
            return false;
        }

        var dz = nz - leader.Tz;
        if (dz * dz > 4)
        {
            return false;
        }

        var dist = new TileCoord(tx, ty, 0).Distance2d(new TileCoord(leader.Tx, leader.Ty, 0));
        if (dist == 1)
        {
            return dz * dz <= 1;
        }

        return ClearToLeader(npc, leader, tx, ty, nz);
    }

    /// <summary>Exult <c>Clear_to_leader</c>: straight path to the leader is walkable and under 5 tiles.</summary>
    bool ClearToLeader(U7Object npc, U7Object leader, int fx, int fy, int fz)
    {
        var dist = new TileCoord(fx, fy, 0).Distance2d(new TileCoord(leader.Tx, leader.Ty, 0));
        if (dist > 4)
        {
            return false;
        }

        while (--dist > 0)
        {
            var d = ActorWalker.DirIndex(
                Math.Sign(U7Constants.TileDelta(fx, leader.Tx)),
                Math.Sign(U7Constants.TileDelta(fy, leader.Ty)));
            var nx = U7Constants.WrapTile(fx + Directions.Dx[d]);
            var ny = U7Constants.WrapTile(fy + Directions.Dy[d]);
            if (!ActorWalker.ResolveStep(_map, npc, nx, ny, fz, out var nz))
            {
                return false;
            }

            fx = nx;
            fy = ny;
            fz = nz;
        }

        var difftz = fz - leader.Tz;
        return difftz * difftz <= 1;
    }

    /// <summary>
    /// Exult <c>Get_cost</c>: squared distance to the leader, plus a penalty
    /// when the way on is blocked. A tile a member later in the party stands
    /// on costs a point more, to swap places with it (<paramref name="inWay"/>).
    /// </summary>
    int Cost(U7Object npc, U7Object leader, int tx, int ty, out int nz, out U7Object? inWay)
    {
        inWay = null;
        var cost = 0;
        if (!ActorWalker.ResolveStep(_map, npc, tx, ty, npc.Tz, out nz))
        {
            inWay = MemberBlocking(tx, ty, PartyId(npc) + 1);
            if (inWay is null)
            {
                return MaxCost;
            }

            nz = inWay.Tz;
            cost += 1; // One point to swap.
        }

        var dz = nz - leader.Tz;
        var dtx = U7Constants.TileDelta(tx, leader.Tx);
        var dty = U7Constants.TileDelta(ty, leader.Ty);
        var xy2 = dtx * dtx + dty * dty;
        cost += dz * dz + xy2;
        if (xy2 > 2 && !ClearToLeader(npc, leader, tx, ty, nz))
        {
            cost += 16;
        }

        return cost;
    }

    /// <summary>Exult <c>Find_member_blocking</c>: the member, from party id <paramref name="first"/> on, standing on the tile.</summary>
    U7Object? MemberBlocking(int tx, int ty, int first)
    {
        for (var i = Math.Max(0, first); i < _members.Count; i++)
        {
            var member = _members[i];
            var dx = U7Constants.TileDelta(tx, member.Tx);
            var dy = U7Constants.TileDelta(ty, member.Ty);
            if (dx >= 0 && dx < Math.Max(1, member.DimX) && dy >= 0 && dy < Math.Max(1, member.DimY))
            {
                return member;
            }
        }

        return null;
    }

    /// <summary>
    /// Exult <c>Take_best_step</c>: try the wanted direction and its
    /// neighbours, cheapest wins; a member in the way of the best swaps
    /// places with the walker.
    /// </summary>
    bool TakeBestStep(U7Object npc, U7Object leader, int dir, out int moved)
    {
        var bestCost = MaxCost + 8;
        var bestDir = -1;
        var bestTx = 0;
        var bestTy = 0;
        var bestTz = 0;
        U7Object? bestInWay = null;
        foreach (var i in DeltaDir)
        {
            var diri = (dir + i) % 8;
            var tx = U7Constants.WrapTile(npc.Tx + Directions.Dx[diri]);
            var ty = U7Constants.WrapTile(npc.Ty + Directions.Dy[diri]);
            var cost = Cost(npc, leader, tx, ty, out var nz, out var inWay);
            if (cost < bestCost)
            {
                bestCost = cost;
                bestDir = diri;
                bestTx = tx;
                bestTy = ty;
                bestTz = nz;
                bestInWay = inWay;
            }
        }

        moved = bestDir;
        if (bestCost >= MaxCost || bestDir < 0)
        {
            return false;
        }

        if (bestInWay is null)
        {
            ActorWalker.MoveTo(_map, npc, bestTx, bestTy, bestTz, bestDir);
            return true;
        }

        // Swap positions.
        var (px, py, pz) = (npc.Tx, npc.Ty, npc.Tz);
        ActorWalker.MoveTo(_map, npc, bestInWay.Tx, bestInWay.Ty, bestInWay.Tz, bestDir);
        _map.MoveObject(bestInWay, px, py, pz);
        return true;
    }

    /// <summary>
    /// Exult <c>Game_window::teleport_party</c>: bring the party along, each
    /// to a free spot nearby; those waiting stay, and so do those who can't
    /// act (asleep, paralysed, knocked out: Exult's <c>can_act</c>).
    /// </summary>
    public void FollowTeleport()
    {
        var t = new TileCoord(_avatar.Tx, _avatar.Ty, _avatar.Tz);
        foreach (var npc in _members)
        {
            if (npc.ScheduleType == ScheduleType.Wait || !CombatSchedule.CanAct(npc))
            {
                continue;
            }

            // Exult: a spot within 8 tiles, up to a lift lower or higher; with none the member stays.
            if (_map.FindSpot(t, 8, npc.Shape, npc.Frame, 1, moverFlags: npc.TypeFlags & MoveFlags.All) is not { } spot)
            {
                continue;
            }

            if (Schedules is { } s)
            {
                s.TeleportNpc(npc, spot);
            }
            else
            {
                _map.MoveObject(npc, spot.Tx, spot.Ty, spot.Tz);
                ActorWalker.Stand(npc, 4);
            }
        }

        CallFollowers();
    }
}
