using U7.Data;

namespace U7.Actors;

/// <summary>
/// Ready-slot helpers: Exult <c>Actor::add</c>, <c>add_readied</c>,
/// <c>find_best_spot</c>, the worn armour. The best weapon and shield are
/// <see cref="CombatEngine.ReadyBestWeapon"/>'s, which has the ammunition rules.
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
            // Exult (shapevga.cc): what READY.DAT leaves out is 'backpack' in Black Gate.
            preferred = ReadySpot.Back;
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
                AddToActor(actor, obj, catalog, map, dontCheck: true);
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

    /// <summary>
    /// Exult <c>Actor::add</c>: the preferred ready spot, else a readied bag
    /// with room; false if neither, unless <paramref name="dontCheck"/>
    /// (Exult's <c>dont_check</c>), which then ignores the bags' limits and at
    /// last puts it loose in the actor.
    /// </summary>
    public static bool AddToActor(U7Object actor, U7Object obj, ShapeCatalog catalog, GameMap map, bool dontCheck = false)
    {
        var index = FindBestSpot(actor, obj, catalog);
        if (index < 0)
        {
            // A bag with room (backpack, belt, hands); Exult's dont_check (reading
            // a save, its own swaps) then tries the bags regardless, then the actor.
            if (PutInBag(actor, obj, catalog, map, true) || (dontCheck && PutInBag(actor, obj, catalog, map, false)))
            {
                return true;
            }

            return dontCheck && TryPlace(map, obj, actor, 0, 0, catalog, checkLimits: false);
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

    /// <summary>Into the first readied bag (backpack, belt, hands) that takes it.</summary>
    static bool PutInBag(U7Object actor, U7Object obj, ShapeCatalog catalog, GameMap map, bool checkLimits)
    {
        foreach (var slot in (int[])[ReadySpot.Back, ReadySpot.Belt, ReadySpot.Lhand, ReadySpot.Rhand])
        {
            if (GetReadied(actor, slot) is { } bag && Inventory.IsContainer(bag, catalog) &&
                TryPlace(map, obj, bag, 8, 8, catalog, checkLimits))
            {
                return true;
            }
        }

        return false;
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
}
