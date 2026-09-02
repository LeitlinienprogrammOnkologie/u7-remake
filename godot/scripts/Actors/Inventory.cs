using U7.Core;
using U7.Data;

namespace U7.Actors;

/// <summary>
/// Weight, volume, stacking. Ports Exult <c>Game_object::get_weight</c> /
/// <c>Container_game_object::add</c> combine + volume, and actor max weight.
/// </summary>
public static class Inventory
{
    public static bool IsContainer(U7Object obj, ShapeCatalog catalog)
    {
        var rec = catalog[obj.Shape];
        return rec.GumpShape >= 0 || rec.IsContainerClass || obj.IsActor;
    }

    public static int GetQuantity(U7Object obj, ShapeCatalog catalog) =>
        catalog[obj.Shape].HasQuantity ? Math.Max(1, obj.Quality & 0x7f) : 1;

    public static U7Object Outermost(U7Object obj)
    {
        var top = obj;
        while (top.Container is { } parent)
        {
            top = parent;
        }

        return top;
    }

    /// <summary>Weight in 1/10 stones (Exult <c>get_weight</c>).</summary>
    public static int GetWeight(U7Object obj, ShapeCatalog catalog)
    {
        var rec = catalog[obj.Shape];
        var wt = GetQuantity(obj, catalog) * rec.Weight;
        if (rec.Lightweight)
        {
            wt /= 10;
            if (wt <= 0)
            {
                wt = 1;
            }
        }

        if (rec.HasQuantity && wt <= 0)
        {
            wt = 1;
        }

        if (!rec.IsContainerClass && !obj.IsActor)
        {
            return wt;
        }

        foreach (var child in obj.Contents)
        {
            if (!child.Removed)
            {
                wt += GetWeight(child, catalog);
            }
        }

        return wt;
    }

    /// <summary>Exult volume; quantity is ignored.</summary>
    public static int GetVolume(U7Object obj, ShapeCatalog catalog) => catalog[obj.Shape].Volume;

    public static int VolumeUsed(U7Object container, ShapeCatalog catalog)
    {
        var used = 0;
        foreach (var child in container.Contents)
        {
            if (!child.Removed)
            {
                used += GetVolume(child, catalog);
            }
        }

        return used;
    }

    /// <summary>Stones the outermost NPC can carry, or 0 if no limit.</summary>
    public static int GetMaxWeight(U7Object obj)
    {
        var top = Outermost(obj);
        if (!top.IsActor && top.NpcNum < 0)
        {
            return 0;
        }

        return 2 * Math.Max(1, top.GetProp(ActorProp.Strength));
    }

    public static bool WouldNest(U7Object obj, U7Object container)
    {
        if (obj == container)
        {
            return true;
        }

        for (var p = container; p is not null; p = p.Container)
        {
            if (p == obj)
            {
                return true;
            }
        }

        return false;
    }

    public static bool CanAdd(U7Object container, U7Object obj, ShapeCatalog catalog, bool checkLimits = true)
    {
        if (WouldNest(obj, container))
        {
            return false;
        }

        if (!checkLimits)
        {
            return true;
        }

        if (!container.IsActor)
        {
            var maxVol = catalog[container.Shape].Volume;
            if (maxVol > 0 && VolumeUsed(container, catalog) + GetVolume(obj, catalog) > maxVol)
            {
                return false;
            }
        }

        var maxWt = GetMaxWeight(container);
        if (maxWt > 0)
        {
            var avail = maxWt * 10 - GetWeight(Outermost(container), catalog);
            if (GetWeight(obj, catalog) > avail)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Merge <paramref name="src"/> into <paramref name="dest"/> if they stack.
    /// Returns true when <paramref name="src"/> is fully consumed.
    /// </summary>
    public static bool TryCombine(U7Object dest, U7Object src, ShapeCatalog catalog)
    {
        if (dest == src || dest.Removed || src.Removed)
        {
            return false;
        }

        var rec = catalog[dest.Shape];
        if (!rec.HasQuantity || dest.Shape != src.Shape || dest.Frame != src.Frame)
        {
            return false;
        }

        var total = GetQuantity(dest, catalog) + GetQuantity(src, catalog);
        if (total > U7Constants.MaxQuantity)
        {
            return false;
        }

        dest.Quality = total;
        src.Removed = true;
        src.Container = null;
        src.ReadySlot = -1;
        return true;
    }

    public static bool TryCombineInto(U7Object container, U7Object src, ShapeCatalog catalog)
    {
        foreach (var child in container.Contents)
        {
            if (!child.Removed && TryCombine(child, src, catalog))
            {
                return true;
            }
        }

        return false;
    }
}
