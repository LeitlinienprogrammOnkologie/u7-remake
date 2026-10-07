using U7.Data;

namespace U7.Actors;

/// <summary>
/// Ready-slot helpers. Ports Exult <c>Actor::ready_best_weapon</c> /
/// <c>ready_best_shield</c> / worn-armor sum (no ammo swap).
/// </summary>
public static class Equipment
{
    public static U7Object? GetReadied(U7Object actor, int slot)
    {
        foreach (var obj in actor.Contents)
        {
            if (!obj.Removed && obj.ReadySlot == slot)
            {
                return obj;
            }
        }

        return null;
    }

    public static int WornArmor(U7Object actor, ArmorTable armor, out int immunity)
    {
        var points = 0;
        immunity = 0;
        foreach (var obj in actor.Contents)
        {
            if (obj.Removed || obj.ReadySlot < 0)
            {
                continue;
            }

            var rec = armor[obj.Shape];
            if (rec is null)
            {
                continue;
            }

            points += rec.Protection;
            immunity |= rec.Immunity;
        }

        return points;
    }

    public static bool ReadyBestWeapon(
        U7Object actor, ShapeCatalog catalog, WeaponTable weapons, ArmorTable armor)
    {
        if (HasReadiedWeapon(actor, weapons))
        {
            ReadyBestShield(actor, catalog, weapons, armor);
            return true;
        }

        U7Object? best = null;
        var bestDmg = -1;
        foreach (var obj in AllPossessions(actor))
        {
            var w = weapons[obj.Shape];
            if (w is null)
            {
                continue;
            }

            var rdy = catalog[obj.Shape].ReadyType;
            if (rdy is not (ReadySpot.Lhand or ReadySpot.Rhand or ReadySpot.BothHands
                or ReadySpot.Back))
            {
                continue;
            }

            if (w.Damage > bestDmg)
            {
                best = obj;
                bestDmg = w.Damage;
            }
        }

        if (best is null)
        {
            ReadyBestShield(actor, catalog, weapons, armor);
            return false;
        }

        UnequipSlot(actor, ReadySpot.Lhand, catalog);
        if (catalog[best.Shape].ReadyType == ReadySpot.BothHands)
        {
            UnequipSlot(actor, ReadySpot.Rhand, catalog);
        }

        Equip(actor, best, ReadySpot.Lhand);
        ReadyBestShield(actor, catalog, weapons, armor);
        return true;
    }

    static bool HasReadiedWeapon(U7Object actor, WeaponTable weapons) =>
        (GetReadied(actor, ReadySpot.Lhand) is { } left && weapons[left.Shape] is not null) ||
        (GetReadied(actor, ReadySpot.Rhand) is { } right && weapons[right.Shape] is not null);

    static void ReadyBestShield(
        U7Object actor, ShapeCatalog catalog, WeaponTable weapons, ArmorTable armor)
    {
        if (GetReadied(actor, ReadySpot.Lhand) is { } left &&
            catalog[left.Shape].ReadyType == ReadySpot.BothHands)
        {
            return;
        }

        if (GetReadied(actor, ReadySpot.Rhand) is { } cur)
        {
            if (armor[cur.Shape] is not null || weapons[cur.Shape] is not null)
            {
                return;
            }
        }

        U7Object? best = null;
        var bestPts = -1;
        foreach (var obj in AllPossessions(actor))
        {
            if (obj.ReadySlot == ReadySpot.Lhand)
            {
                continue;
            }

            var rec = armor[obj.Shape];
            if (rec is null)
            {
                continue;
            }

            var rdy = catalog[obj.Shape].ReadyType;
            if (rdy is not (ReadySpot.Lhand or ReadySpot.Back or ReadySpot.Rhand))
            {
                continue;
            }

            if (rec.Protection > bestPts)
            {
                best = obj;
                bestPts = rec.Protection;
            }
        }

        if (best is null)
        {
            return;
        }

        UnequipSlot(actor, ReadySpot.Rhand, catalog);
        Equip(actor, best, ReadySpot.Rhand);
    }

    public static bool IsTwoHanded(U7Object actor, ShapeCatalog catalog) =>
        GetReadied(actor, ReadySpot.Lhand) is { } left &&
        catalog[left.Shape].ReadyType == ReadySpot.BothHands;

    public static bool IsTwoFingered(U7Object actor, ShapeCatalog catalog) =>
        GetReadied(actor, ReadySpot.Lfinger) is { } ring &&
        catalog[ring.Shape].ReadyType == ReadySpot.GlovesPair;

    public static bool UsesScabbard(U7Object actor, ShapeCatalog catalog)
    {
        if (GetReadied(actor, ReadySpot.Belt) is { } belt)
        {
            var rec = catalog[belt.Shape];
            if (rec.ReadyAlt1 == ReadySpot.Scabbard || rec.ReadyAlt2 == ReadySpot.Scabbard)
            {
                return true;
            }
        }

        return GetReadied(actor, ReadySpot.Back2h) is not null ||
               GetReadied(actor, ReadySpot.BackShield) is not null;
    }

    public static bool UsesNeck(U7Object actor, ShapeCatalog catalog)
    {
        if (GetReadied(actor, ReadySpot.Neck) is { } neck &&
            catalog[neck.Shape].ReadyType == ReadySpot.NeckFill)
        {
            return true;
        }

        return GetReadied(actor, ReadySpot.Cloak) is not null;
    }

    public static void GetPreferredSlots(
        U7Object obj, ShapeCatalog catalog, out int preferred, out int alt1, out int alt2)
    {
        var rec = catalog[obj.Shape];
        preferred = rec.ReadyType;
        alt1 = rec.ReadyAlt1;
        alt2 = rec.ReadyAlt2;
        if (alt1 == ReadySpot.Invalid)
        {
            alt1 = ReadySpot.Lhand;
        }

        if (alt2 == ReadySpot.Invalid)
        {
            alt2 = ReadySpot.Lhand;
        }

        if (preferred == ReadySpot.Invalid)
        {
            preferred = ReadySpot.Lhand;
        }

        if (preferred == ReadySpot.Lhand)
        {
            if (rec.IsObjectAllowed(obj.Frame, ReadySpot.Rhand))
            {
                if (!rec.IsObjectAllowed(obj.Frame, ReadySpot.Lhand))
                {
                    preferred = ReadySpot.Rhand;
                }
                else
                {
                    alt1 = ReadySpot.Rhand;
                }
            }
            else
            {
                alt1 = ReadySpot.Rhand;
            }
        }
    }

    /// <summary>Exult <c>Actor::fits_in_spot</c> (BG actor-gump spots plus alts).</summary>
    public static bool FitsInSpot(U7Object actor, U7Object obj, int spot, ShapeCatalog catalog)
    {
        var rec = catalog[obj.Shape];
        var rtype = rec.ReadyType;
        var alt1 = rec.ReadyAlt1;
        var alt2 = rec.ReadyAlt2;
        var canScabbard = alt1 == ReadySpot.Scabbard || alt2 == ReadySpot.Scabbard;
        // Exult: can_neck tests the 'neck' ready type (0x14), not the amulet spot.
        var canNeck = rtype == ReadySpot.NeckFill || alt1 == ReadySpot.NeckFill || alt2 == ReadySpot.NeckFill;
        if (spot == ReadySpot.BothHands)
        {
            spot = ReadySpot.Lhand;
        }
        else if (spot == ReadySpot.GlovesPair)
        {
            spot = ReadySpot.Lfinger;
        }
        else if (spot == ReadySpot.NeckFill)
        {
            spot = ReadySpot.Neck;
        }
        else if (spot == ReadySpot.Scabbard)
        {
            spot = ReadySpot.Belt;
        }

        if (spot < 0)
        {
            return false;
        }

        if (GetReadied(actor, spot) is not null)
        {
            return false;
        }

        var twoHanded = IsTwoHanded(actor, catalog);
        var twoFingered = IsTwoFingered(actor, catalog);
        if ((rtype == ReadySpot.BothHands || twoHanded) && spot == ReadySpot.Rhand)
        {
            return false;
        }

        if ((rtype == ReadySpot.GlovesPair || twoFingered) &&
            (spot == ReadySpot.Rfinger || spot == ReadySpot.Gloves))
        {
            return false;
        }

        if ((canScabbard || UsesScabbard(actor, catalog)) &&
            (spot == ReadySpot.Back2h || spot == ReadySpot.BackShield))
        {
            return false;
        }

        if ((canNeck || UsesNeck(actor, catalog)) && spot == ReadySpot.Cloak)
        {
            return false;
        }

        if (rtype == ReadySpot.BothHands && spot == ReadySpot.Lhand &&
            GetReadied(actor, ReadySpot.Rhand) is not null)
        {
            return false;
        }

        if (rtype == ReadySpot.GlovesPair && spot == ReadySpot.Lfinger &&
            GetReadied(actor, ReadySpot.Rfinger) is not null)
        {
            return false;
        }

        if (canScabbard && spot == ReadySpot.Belt &&
            (GetReadied(actor, ReadySpot.Back2h) is not null ||
             GetReadied(actor, ReadySpot.BackShield) is not null))
        {
            return false;
        }

        if (canNeck && spot == ReadySpot.Neck && GetReadied(actor, ReadySpot.Cloak) is not null)
        {
            return false;
        }

        if (spot is ReadySpot.Lhand or ReadySpot.Rhand)
        {
            return true;
        }

        if (spot == ReadySpot.Belt && (rec.IsSpell || canScabbard))
        {
            return true;
        }

        if ((spot == ReadySpot.Back2h || spot == ReadySpot.BackShield) && canScabbard)
        {
            return true;
        }

        if ((spot == ReadySpot.Neck || spot == ReadySpot.Cloak) && canNeck)
        {
            return true;
        }

        return rec.IsObjectAllowed(obj.Frame, spot);
    }

    public static int FindBestSpot(U7Object actor, U7Object obj, ShapeCatalog catalog)
    {
        GetPreferredSlots(obj, catalog, out var preferred, out var alt1, out var alt2);
        if (FitsInSpot(actor, obj, preferred, catalog))
        {
            return preferred;
        }

        if (alt1 >= 0 && FitsInSpot(actor, obj, alt1, catalog))
        {
            return alt1;
        }

        if (alt2 >= 0 && FitsInSpot(actor, obj, alt2, catalog))
        {
            return alt2;
        }

        foreach (var spot in new[]
                 {
                     ReadySpot.Belt, ReadySpot.Back, ReadySpot.Back2h, ReadySpot.BackShield,
                     ReadySpot.Lhand, ReadySpot.Rhand
                 })
        {
            if (FitsInSpot(actor, obj, spot, catalog))
            {
                return spot;
            }
        }

        return -1;
    }

    /// <summary>
    /// Exult <c>Actor::empty_hands</c>: what is in either hand goes to the
    /// belt or backpack spot (into what is there, if anything), else wherever
    /// <c>Actor::add</c> puts it.
    /// </summary>
    public static void EmptyHands(U7Object actor, ShapeCatalog catalog, GameMap map)
    {
        foreach (var hand in (int[])[ReadySpot.Lhand, ReadySpot.Rhand])
        {
            if (GetReadied(actor, hand) is not { } obj)
            {
                continue;
            }

            map.TakeFromWorld(obj);
            if (!AddReadied(actor, obj, ReadySpot.Belt, catalog, map, forcePos: true) &&
                !AddReadied(actor, obj, ReadySpot.Back, catalog, map, forcePos: true))
            {
                AddToActor(actor, obj, catalog, map);
            }
        }
    }

    /// <summary>Exult <c>Actor::add_readied</c>. Occupied spots try nest/combine.</summary>
    public static bool AddReadied(
        U7Object actor, U7Object obj, int index, ShapeCatalog catalog, GameMap map, bool forcePos = false)
    {
        if (index == ReadySpot.BothHands)
        {
            index = ReadySpot.Lhand;
        }
        else if (index == ReadySpot.GlovesPair)
        {
            index = ReadySpot.Lfinger;
        }
        else if (index == ReadySpot.NeckFill)
        {
            index = ReadySpot.Neck;
        }
        else if (index == ReadySpot.Scabbard)
        {
            index = ReadySpot.Belt;
        }

        if (index < 0)
        {
            return false;
        }

        if (GetReadied(actor, index) is { } occupied)
        {
            if (Inventory.TryCombine(occupied, obj, catalog))
            {
                return true;
            }

            return Inventory.IsContainer(occupied, catalog) &&
                   TryPlace(map, obj, occupied, 8, 8, catalog);
        }

        if (!forcePos && !FitsInSpot(actor, obj, index, catalog))
        {
            return false;
        }

        if (!TryPlace(map, obj, actor, 0, 0, catalog, checkLimits: false))
        {
            return false;
        }

        obj.ReadySlot = index;
        return true;
    }

    /// <summary>Exult <c>Actor::add</c>: preferred ready spot, then bags, then unpack.</summary>
    public static bool AddToActor(U7Object actor, U7Object obj, ShapeCatalog catalog, GameMap map)
    {
        var index = FindBestSpot(actor, obj, catalog);
        if (index < 0)
        {
            foreach (var slot in new[] { ReadySpot.Back, ReadySpot.Belt, ReadySpot.Lhand, ReadySpot.Rhand })
            {
                if (GetReadied(actor, slot) is { } bag &&
                    Inventory.IsContainer(bag, catalog) &&
                    TryPlace(map, obj, bag, 8, 8, catalog))
                {
                    return true;
                }
            }

            return TryPlace(map, obj, actor, 0, 0, catalog);
        }

        if (index == ReadySpot.BothHands)
        {
            index = ReadySpot.Lhand;
        }
        else if (index == ReadySpot.GlovesPair)
        {
            index = ReadySpot.Lfinger;
        }
        else if (index == ReadySpot.Scabbard)
        {
            index = ReadySpot.Belt;
        }
        else if (index == ReadySpot.NeckFill)
        {
            index = ReadySpot.Neck;
        }

        if (!TryPlace(map, obj, actor, 0, 0, catalog, checkLimits: false))
        {
            return false;
        }

        obj.ReadySlot = index;
        return true;
    }

    public static bool TryPlace(
        GameMap map, U7Object obj, U7Object container, int gx, int gy, ShapeCatalog catalog,
        bool checkLimits = true)
    {
        if (!Inventory.CanAdd(container, obj, catalog, checkLimits))
        {
            return false;
        }

        map.PlaceInContainer(obj, container, gx, gy);
        obj.ReadySlot = -1;
        return true;
    }

    static void Equip(U7Object actor, U7Object obj, int slot)
    {
        if (obj.Container != actor)
        {
            obj.Container?.Contents.Remove(obj);
            obj.Container = actor;
            if (!actor.Contents.Contains(obj))
            {
                actor.Contents.Add(obj);
            }
        }

        obj.ReadySlot = slot;
    }

    /// <summary>
    /// Exult re-adds a displaced item with <c>add(obj, true)</c>: it lands in a
    /// readied bag if one has room, otherwise loose in the main inventory with
    /// a fresh gump position, never dangling at its old spot.
    /// </summary>
    static void UnequipSlot(U7Object actor, int slot, ShapeCatalog catalog)
    {
        var obj = GetReadied(actor, slot);
        if (obj is null)
        {
            return;
        }

        obj.ReadySlot = -1;
        obj.Tx = 255;
        obj.Ty = 255;
        foreach (var bagSlot in new[] { ReadySpot.Back, ReadySpot.Belt })
        {
            if (GetReadied(actor, bagSlot) is { } bag && bag != obj &&
                Inventory.IsContainer(bag, catalog) && Inventory.CanAdd(bag, obj, catalog, true))
            {
                actor.Contents.Remove(obj);
                obj.Container = bag;
                bag.Contents.Add(obj);
                return;
            }
        }
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
            foreach (var nested in AllPossessions(obj))
            {
                yield return nested;
            }
        }
    }
}
