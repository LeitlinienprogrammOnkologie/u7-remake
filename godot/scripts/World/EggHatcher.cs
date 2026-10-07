using Godot;
using U7.Actors;
using U7.Audio;
using U7.Core;
using U7.Data;
using U7.Usecode;

namespace U7.World;

/// <summary>
/// Activates IREG eggs when the avatar (or a dropped item) moves.
/// Ports Exult <c>Egg_object::is_active</c> / <c>hatch</c> / <c>unhatch</c>
/// for BG v1: teleport, usecode, jukebox, button, monster. Missile/weather
/// are stubs. Eggs are found through the per-tile bits kept by
/// <see cref="GameMap.EggsAt"/>, like Exult's chunk cache.
/// </summary>
public sealed class EggHatcher
{
    /// <summary>Exult <c>Map_chunk::try_all_eggs</c> search half-width.</summary>
    public const int TryAllDist = 32;

    readonly GameMap _map;
    readonly GameClock _clock;
    readonly Random _rng = new();
    readonly HashSet<int> _loggedStub = new();
    int _jukeboxTrack = -1;
    bool _noRecurse;

    public UsecodeMachine? Usecode { get; set; }
    public CombatEngine? Combat { get; set; }
    public MusicPlayer? Music { get; set; }
    public PartyManager? Party { get; set; }
    public string LastMessage { get; private set; } = "";
    public int JukeboxTrack => Music is { } m ? m.CurrentTrack : _jukeboxTrack;

    public EggHatcher(GameMap map, GameClock clock)
    {
        _map = map;
        _clock = clock;
    }

    /// <summary>
    /// Exult <c>Main_actor::step</c>: unhatch eggs on the tile being left, then
    /// hatch eggs whose bits are set on the tile entered. With no from-tile
    /// (teleport-in, map load) this is <c>Map_chunk::try_all_eggs</c> instead.
    /// </summary>
    public void Activate(U7Object actor, int fromTx, int fromTy, bool must = false)
    {
        if (fromTx < 0 || fromTy < 0)
        {
            TryAllEggs(actor, must);
            return;
        }

        var tx = actor.Tx;
        var ty = actor.Ty;
        var tz = actor.Tz;
        var leaving = _map.EggsAt(fromTx, fromTy);
        if (leaving is not null)
        {
            foreach (var egg in leaving)
            {
                if (!egg.Removed && TestUnhatch(egg, actor, tx, ty, tz, fromTx, fromTy))
                {
                    Unhatch(egg);
                }
            }
        }

        var entering = _map.EggsAt(tx, ty);
        if (entering is null)
        {
            return;
        }

        foreach (var egg in entering)
        {
            if (egg.Removed || !IsActive(egg, actor, tx, ty, tz, fromTx, fromTy))
            {
                continue;
            }

            Hatch(egg, actor, must);
            if (actor.Tx != tx || actor.Ty != ty || actor.Tz != tz)
            {
                return; // teleported; the destination scan already ran
            }
        }
    }

    /// <summary>
    /// Exult <c>Barge_object::step</c>: the eggs on the tile the barge's hot
    /// spot stepped onto hatch for <paramref name="actor"/> (the avatar), as
    /// if it had walked there from the barge's last tile. None unhatch.
    /// </summary>
    public void ActivateAt(U7Object actor, TileCoord tile, int fromTx, int fromTy)
    {
        if (_map.EggsAt(tile.Tx, tile.Ty) is not { } eggs)
        {
            return;
        }

        foreach (var egg in eggs.ToList())
        {
            if (!egg.Removed && IsActive(egg, actor, tile.Tx, tile.Ty, tile.Tz, fromTx, fromTy))
            {
                Hatch(egg, actor, false);
            }
        }
    }

    /// <summary>
    /// Exult <c>Map_chunk::try_all_eggs</c>: after a teleport or map load, hatch
    /// every active egg within <see cref="TryAllDist"/> except jukebox and
    /// teleport eggs. Guarded against re-entry so chained teleports stop.
    /// </summary>
    void TryAllEggs(U7Object actor, bool must)
    {
        if (_noRecurse)
        {
            return;
        }

        _noRecurse = true;
        try
        {
            var tx = actor.Tx;
            var ty = actor.Ty;
            var tz = actor.Tz;
            var eggs = new List<U7Object>();
            foreach (var egg in _map.EggsNear(tx, ty, TryAllDist))
            {
                if (egg.EggType is EggType.Jukebox or EggType.Teleport)
                {
                    continue;
                }

                if (IsActive(egg, actor, tx, ty, tz, -1, -1))
                {
                    eggs.Add(egg);
                }
            }

            foreach (var egg in eggs)
            {
                if (!egg.Removed)
                {
                    Hatch(egg, actor, must);
                }
            }
        }
        finally
        {
            _noRecurse = false;
        }
    }

    public void ActivateSomethingOn(U7Object obj)
    {
        if (obj.IsActor)
        {
            return;
        }

        Activate(obj, obj.Tx, obj.Ty, must: false);
    }

    bool IsActive(U7Object egg, U7Object obj, int tx, int ty, int tz, int fromTx, int fromTy)
    {
        if ((egg.EggFlags & EggFlag.Hatched) != 0 && (egg.EggFlags & EggFlag.AutoReset) == 0)
        {
            return false;
        }

        if ((egg.EggFlags & EggFlag.Nocturnal) != 0)
        {
            var hour = _clock.Hour;
            if (hour < 21 && hour > 4)
            {
                return false;
            }
        }

        var cri = egg.EggCriteria;
        var type = egg.EggType;
        var dz = tz - egg.Tz;
        var inArea = InArea(egg, tx, ty);
        var fromIn = fromTx >= 0 && InArea(egg, fromTx, fromTy);
        var isAvatar = obj.NpcNum == 0;
        var inParty = isAvatar;

        switch (cri)
        {
            case EggCriteria.CachedIn:
                if (!isAvatar || !inArea)
                {
                    return false;
                }

                if ((egg.EggFlags & EggFlag.Hatched) == 0)
                {
                    return true;
                }

                return !fromIn;
            case EggCriteria.AvatarNear:
                if (!isAvatar)
                {
                    return false;
                }

                goto case EggCriteria.PartyNear;
            case EggCriteria.PartyNear:
                if (!inParty)
                {
                    return false;
                }

                if (type is EggType.Teleport or EggType.Intermap)
                {
                    return dz == 0 && inArea;
                }

                if (type is EggType.Jukebox or EggType.SoundSfx or EggType.Voice)
                {
                    return inArea;
                }

                return dz / 2 == 0 && inArea && !fromIn;
            case EggCriteria.AvatarFar:
            {
                if (!isAvatar || !inArea)
                {
                    return false;
                }

                var ix = egg.EggAreaX + 1;
                var iy = egg.EggAreaY + 1;
                var iw = Math.Max(0, egg.EggAreaW - 2);
                var ih = Math.Max(0, egg.EggAreaH - 2);
                var fromInside = fromTx >= ix && fromTx < ix + iw && fromTy >= iy && fromTy < iy + ih;
                var nowInside = tx >= ix && tx < ix + iw && ty >= iy && ty < iy + ih;
                return fromInside && !nowInside;
            }
            case EggCriteria.AvatarFootpad:
                return isAvatar && dz == 0 && inArea;
            case EggCriteria.PartyFootpad:
                return inParty && dz == 0 && inArea;
            case EggCriteria.SomethingOn:
                return !obj.IsActor && dz / 4 == 0 && inArea;
            default:
                return false;
        }
    }

    static bool InArea(U7Object egg, int tx, int ty) =>
        egg.EggAreaW > 0 &&
        tx >= egg.EggAreaX && tx < egg.EggAreaX + egg.EggAreaW &&
        ty >= egg.EggAreaY && ty < egg.EggAreaY + egg.EggAreaH;

    /// <summary>Exult <c>Usecode_script::activate_egg</c>: hatch on behalf of the avatar.</summary>
    public void HatchEgg(U7Object egg, bool must)
    {
        if (egg.IsEgg && !egg.Removed && Combat is { } c)
        {
            Hatch(egg, c.Avatar, must);
        }
    }

    /// <summary>Exult: only <c>Jukebox_egg</c> and its <c>Soundsfx_egg</c> subclass unhatch.</summary>
    static bool CanUnhatch(U7Object egg) =>
        egg.EggType is EggType.Jukebox or EggType.SoundSfx;

    /// <summary>Exult <c>Egg_object::hatch</c>.</summary>
    void Hatch(U7Object egg, U7Object obj, bool must)
    {
        // An unhatchable egg stays hatched until unhatch() clears the flag.
        if (CanUnhatch(egg) && (egg.EggFlags & EggFlag.Hatched) != 0)
        {
            return;
        }

        var roll = must ? 0 : 1 + _rng.Next(100);
        if (roll <= egg.EggProbability)
        {
            HatchNow(egg, obj, must);
            if (egg.Removed)
            {
                return;
            }

            // Once-only eggs go now; Exult's usecode egg defers this until its
            // script has run, which our synchronous usecode call has already done.
            if (!CanUnhatch(egg) && (egg.EggFlags & EggFlag.Once) != 0)
            {
                _map.RemoveObject(egg);
                return;
            }
        }

        egg.EggFlags |= EggFlag.Hatched;
    }

    /// <summary>Exult <c>Egg_object::test_unhatch</c>: only the avatar, only hatched unhatchable eggs.</summary>
    bool TestUnhatch(U7Object egg, U7Object obj, int tx, int ty, int tz, int fromTx, int fromTy)
    {
        if (obj.NpcNum != 0 || !CanUnhatch(egg) || (egg.EggFlags & EggFlag.Hatched) == 0)
        {
            return false;
        }

        if ((egg.EggFlags & EggFlag.Nocturnal) != 0)
        {
            var hour = _clock.Hour;
            if (hour < 21 && hour > 4)
            {
                return true;
            }
        }

        var type = egg.EggType;
        var dz = tz - egg.Tz;
        var inArea = InArea(egg, tx, ty);
        var fromIn = InArea(egg, fromTx, fromTy);
        switch (egg.EggCriteria)
        {
            case EggCriteria.CachedIn:
                return fromIn && !inArea;
            case EggCriteria.AvatarNear:
            case EggCriteria.PartyNear:
                if (type is EggType.Teleport or EggType.Intermap)
                {
                    return false;
                }

                if (type is EggType.Jukebox or EggType.SoundSfx or EggType.Voice)
                {
                    return !inArea && fromIn;
                }

                return !(dz / 2 == 0 && inArea && !fromIn);
            case EggCriteria.AvatarFar:
            {
                if (!fromIn)
                {
                    return false;
                }

                var ix = egg.EggAreaX + 1;
                var iy = egg.EggAreaY + 1;
                var iw = Math.Max(0, egg.EggAreaW - 2);
                var ih = Math.Max(0, egg.EggAreaH - 2);
                var fromInside = fromTx >= ix && fromTx < ix + iw && fromTy >= iy && fromTy < iy + ih;
                var nowInside = tx >= ix && tx < ix + iw && ty >= iy && ty < iy + ih;
                return !fromInside && nowInside;
            }
            case EggCriteria.AvatarFootpad:
            case EggCriteria.PartyFootpad:
                return dz != 0 || !inArea;
            case EggCriteria.SomethingOn:
                return (dz / 4 != 0 || !inArea) && !obj.IsActor;
            default:
                return false;
        }
    }

    /// <summary>Exult <c>Egg_object::unhatch</c>.</summary>
    void Unhatch(U7Object egg)
    {
        if (UnhatchNow(egg) && (egg.EggFlags & EggFlag.AutoReset) != 0)
        {
            egg.EggFlags &= ~EggFlag.Hatched;
        }

        if ((egg.EggFlags & EggFlag.Once) != 0)
        {
            _map.RemoveObject(egg);
        }
    }

    /// <summary>
    /// Exult <c>Jukebox_egg::unhatch_now</c>: stop only if the playing track is
    /// ours. Sound-effect eggs have nothing running, so they always reset.
    /// </summary>
    bool UnhatchNow(U7Object egg)
    {
        if (egg.EggType != EggType.Jukebox)
        {
            return true;
        }

        var track = egg.EggData1 & 0xff;
        if (Music is { } m)
        {
            if (m.CurrentTrack != track)
            {
                return false;
            }

            // Exult: let the track finish, but stop repeating once no
            // continuous egg holds it any more.
            if (m.EggCount <= 1)
            {
                m.Repeat = false;
                m.EggCount = 0;
            }
            else
            {
                m.EggCount--;
            }
        }
        else if (_jukeboxTrack != track)
        {
            return false;
        }

        _jukeboxTrack = -1;
        LastMessage = $"jukebox stop {track}";
        GD.Print($"egg jukebox stop {track} at {egg.Tx},{egg.Ty}");
        return true;
    }

    void HatchNow(U7Object egg, U7Object obj, bool must)
    {
        switch (egg.EggType)
        {
            case EggType.Teleport:
            case EggType.Intermap:
                Teleport(egg, obj);
                break;
            case EggType.Usecode:
                RunUsecode(egg, must);
                break;
            case EggType.Jukebox:
            {
                // Exult Jukebox_egg::hatch_now.
                var track = egg.EggData1 & 0xff;
                var loop = ((egg.EggData1 >> 8) & 1) != 0;
                _jukeboxTrack = track;
                LastMessage = $"jukebox track {track}" + (loop ? " (loop)" : "");
                GD.Print($"egg jukebox {track} loop={loop} at {egg.Tx},{egg.Ty}");
                if (Music is { } m)
                {
                    if (loop && m.CurrentTrack == track)
                    {
                        m.EggCount++;
                        m.Repeat = true;
                    }
                    else
                    {
                        m.Start(track, loop);
                        m.EggCount = loop ? 1 : 0;
                    }
                }

                break;
            }
            case EggType.Button:
                HatchButton(egg, obj);
                break;
            case EggType.Path:
                break;
            case EggType.Monster:
                Combat?.HatchMonsterEgg(egg);
                if (Combat is { LastMessage.Length: > 0 })
                {
                    LastMessage = Combat.LastMessage;
                }

                break;
            default:
                if (_loggedStub.Add(egg.EggType))
                {
                    GD.Print($"egg stub {EggType.Name(egg.EggType)} ({egg.EggType})");
                }

                break;
        }
    }

    void Teleport(U7Object egg, U7Object obj)
    {
        if (obj.NpcNum != 0)
        {
            return;
        }

        TileCoord pos;
        var eggnum = egg.EggType == EggType.Intermap ? 255 : egg.Quality;
        if (eggnum == 255)
        {
            var schunk = egg.EggData1 >> 8;
            var d2 = egg.EggData2;
            pos = new TileCoord(
                (schunk % 12) * U7Constants.TilesPerSuperchunk + (d2 & 0xff),
                (schunk / 12) * U7Constants.TilesPerSuperchunk + (d2 >> 8),
                egg.EggData3 & 0xff);
        }
        else
        {
            var path = _map.FindPathEgg(eggnum);
            if (path is null)
            {
                GD.Print($"teleport egg {eggnum}: no path egg");
                return;
            }

            pos = new TileCoord(path.Tx, path.Ty, path.Tz);
        }

        LastMessage = $"teleport {obj.Tx},{obj.Ty} → {pos.Tx},{pos.Ty} lift {pos.Tz}";
        GD.Print(LastMessage);
        _map.MoveObject(obj, pos.Tx, pos.Ty, pos.Tz);
        Party?.FollowTeleport();
        // Exult Game_window::teleport_party → Map_chunk::try_all_eggs (dice roll, no must).
        TryAllEggs(obj, must: false);
    }

    void RunUsecode(U7Object egg, bool must)
    {
        var fun = egg.EggData2;
        if (fun <= 0 || Usecode is null)
        {
            return;
        }

        LastMessage = $"egg usecode 0x{fun:X3} at {egg.Tx},{egg.Ty}";
        GD.Print(LastMessage);
        // Exult runs this on the next frame unless must; our VM has no script queue.
        _ = must;
        Usecode.Call(fun, egg, UsecodeEvent.EggProximity);
    }

    void HatchButton(U7Object egg, U7Object obj)
    {
        var dist = egg.EggData1 & 0xff;
        var origin = new TileCoord(egg.Tx, egg.Ty, egg.Tz);
        foreach (var other in _map.EggsNear(egg.Tx, egg.Ty, Math.Max(dist, 1)))
        {
            if (other == egg || other.Removed)
            {
                continue;
            }

            if (other.Shape is not (U7Constants.EggShape or U7Constants.EggShapeAlt))
            {
                continue;
            }

            if (origin.Distance2d(new TileCoord(other.Tx, other.Ty, other.Tz)) > dist)
            {
                continue;
            }

            if (other.EggCriteria != EggCriteria.External)
            {
                continue;
            }

            if ((other.EggFlags & EggFlag.Hatched) != 0)
            {
                continue;
            }

            Hatch(other, obj, must: false);
        }
    }
}
