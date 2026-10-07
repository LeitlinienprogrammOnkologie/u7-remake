using U7.Core;
using U7.Data;

namespace U7.Actors;

/// <summary>
/// Quantity-aware container operations for usecode: Exult
/// <c>Container_game_object::add_quantity</c> / <c>create_quantity</c> /
/// <c>remove_quantity</c> / <c>count_objects</c> and
/// <c>Game_object::modify_quantity</c>.
/// </summary>
public sealed class ItemQuantity
{
    /// <summary>Exult <c>shape_info.txt</c> quantity_frames (BG): bolts, arrows, musket ammo, lockpicks, money.</summary>
    static readonly HashSet<int> QuantityFrameShapes = [417, 723, 948, 554, 556, 558, 560, 568, 722, 581, 627, 644];

    /// <summary>Exult <c>shape_info.txt</c> locked_containers (BG).</summary>
    static readonly HashSet<int> LockedContainers = [522, 798];

    readonly ShapeCatalog _catalog;
    readonly GameMap _map;
    readonly WeaponTable? _weapons;
    readonly AmmoTable? _ammo;

    public ItemQuantity(ShapeCatalog catalog, GameMap map, WeaponTable? weapons, AmmoTable? ammo)
    {
        _catalog = catalog;
        _map = map;
        _weapons = weapons;
        _ammo = ammo;
    }

    static bool CanBeAdded(U7Object cont, int shape, bool allowLocked = false) =>
        cont.Shape != shape && (allowLocked || !LockedContainers.Contains(cont.Shape));

    static bool Matches(U7Object obj, int shape, int qual, int frame) =>
        (shape == U7Constants.AnyShape || obj.Shape == shape) &&
        (frame == U7Constants.AnyShape || (obj.Frame & 31) == frame) &&
        (qual == U7Constants.AnyShape || obj.Quality == qual);

    /// <summary>Exult <c>Container_game_object::count_objects</c>: sums quantities, recursively.</summary>
    public int Count(U7Object cont, int shape, int qual, int frame)
    {
        if (!CanBeAdded(cont, shape, allowLocked: true))
        {
            return 0;
        }

        var total = 0;
        foreach (var obj in cont.Contents)
        {
            if (obj.Removed)
            {
                continue;
            }

            if (Matches(obj, shape, qual, frame))
            {
                total += Inventory.GetQuantity(obj, _catalog);
            }

            total += Count(obj, shape, qual, frame);
        }

        return total;
    }

    /// <summary>Exult <c>Container_game_object::find_item</c>: the first match, depth first.</summary>
    public static U7Object? FindItem(U7Object cont, int shape, int qual, int frame)
    {
        if (cont.Contents.Count == 0 || !CanBeAdded(cont, shape, allowLocked: true))
        {
            return null;
        }

        foreach (var obj in cont.Contents)
        {
            if (obj.Removed)
            {
                continue;
            }

            if (obj.Shape == shape && Matches(obj, shape, qual, frame))
            {
                return obj;
            }

            if (FindItem(obj, shape, qual, frame) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Exult <c>Game_object::modify_quantity</c>: add (delta &gt;= 0) or remove.
    /// Returns what could not be added or removed (negative when removing).
    /// </summary>
    public int Modify(U7Object obj, int delta, out bool deleted)
    {
        deleted = false;
        var info = _catalog[obj.Shape];
        if (!info.HasQuantity)
        {
            if (delta > 0)
            {
                return delta;
            }

            _map.RemoveObject(obj);
            deleted = true;
            return delta + 1;
        }

        var quant = obj.Quality & 0x7f;
        if (quant == 0)
        {
            quant = 1;
        }

        var newquant = quant + delta;
        if (delta >= 0)
        {
            newquant = Math.Min(newquant, U7Constants.MaxQuantity);
        }
        else if (newquant <= 0)
        {
            _map.RemoveObject(obj);
            deleted = true;
            return delta + quant;
        }

        obj.Quality = newquant;
        if (_weapons?[obj.Shape] is not null)
        {
            obj.Frame = 0;
        }
        else if (QuantityFrameShapes.Contains(obj.Shape))
        {
            var baseFrame = _ammo?[obj.Shape] is not null || info.ReadyType == ReadySpot.TripleBolts ? 24 : 0;
            obj.Frame = baseFrame + (newquant > 12 ? 7 : newquant > 6 ? 6 : newquant - 1);
        }

        return delta - (newquant - quant);
    }

    /// <summary>
    /// Exult <c>Container_game_object::add_quantity</c>: fill matching stacks
    /// (recursively), then create new objects. Returns how many could not be added.
    /// </summary>
    public int Add(U7Object cont, int delta, int shape, int qual, int frame, bool dontCreate, bool temporary)
    {
        if (delta <= 0 || !CanBeAdded(cont, shape))
        {
            return delta;
        }

        var cantAdd = 0;
        var maxWeight = Inventory.GetMaxWeight(cont) * 10;
        if (maxWeight > 0)
        {
            var avail = maxWeight - Inventory.GetWeight(Inventory.Outermost(cont), _catalog);
            var objWeight = Inventory.ShapeWeight(shape, delta, _catalog);
            if (objWeight > 0 && objWeight > avail)
            {
                var weight1 = 10 * objWeight / delta;
                cantAdd = delta - 10 * avail / (weight1 != 0 ? weight1 : 1);
                if (cantAdd >= delta)
                {
                    return delta;
                }

                delta -= cantAdd;
            }
        }

        var hasQuantity = _catalog[shape].HasQuantity;
        var hasQuantityFrames = hasQuantity && QuantityFrameShapes.Contains(shape);
        var children = cont.Contents.Where(c => !c.Removed).ToList();
        foreach (var obj in children)
        {
            if (delta == 0)
            {
                break;
            }

            if (hasQuantity && obj.Shape == shape &&
                (frame == U7Constants.AnyShape || hasQuantityFrames || obj.Frame == frame))
            {
                delta = Modify(obj, delta, out _);
            }
        }

        foreach (var obj in children)
        {
            if (Inventory.IsContainer(obj, _catalog) && !obj.IsActor)
            {
                delta = Add(obj, delta, shape, qual, frame, dontCreate: true, temporary);
            }
        }

        if (delta == 0 || dontCreate)
        {
            return delta + cantAdd;
        }

        return cantAdd + Create(cont, delta, shape, qual, frame == U7Constants.AnyShape ? 0 : frame, temporary);
    }

    /// <summary>Exult <c>Container_game_object::create_quantity</c>.</summary>
    int Create(U7Object cont, int delta, int shape, int qual, int frame, bool temporary)
    {
        if (!CanBeAdded(cont, shape) || _catalog[cont.Shape].ReadyType == ReadySpot.Ucont)
        {
            return delta;
        }

        if (!_catalog[shape].HasQuality)
        {
            qual = U7Constants.AnyShape;
        }

        while (delta > 0)
        {
            var obj = NewItem(shape, frame);
            if (!AddTo(cont, obj))
            {
                break;
            }

            if (temporary)
            {
                obj.SetFlag(ObjFlag.Temporary);
            }

            if (qual != U7Constants.AnyShape)
            {
                obj.Quality = qual;
            }

            delta--;
            if (delta > 0)
            {
                delta = Modify(obj, delta, out _);
            }
        }

        if (delta == 0)
        {
            return 0;
        }

        foreach (var obj in cont.Contents.Where(c => !c.Removed).ToList())
        {
            if (Inventory.IsContainer(obj, _catalog) && !obj.IsActor)
            {
                delta = Create(obj, delta, shape, qual, frame, temporary);
            }
        }

        return delta;
    }

    /// <summary>Exult <c>Container_game_object::remove_quantity</c>. Returns how many are still owed.</summary>
    public int Remove(U7Object cont, int delta, int shape, int qual, int frame)
    {
        if (cont.Contents.Count == 0 || !CanBeAdded(cont, shape))
        {
            return delta;
        }

        foreach (var obj in cont.Contents.ToList())
        {
            if (delta == 0)
            {
                break;
            }

            if (obj.Removed)
            {
                continue;
            }

            var deleted = false;
            if (Matches(obj, shape, qual, frame))
            {
                delta = -Modify(obj, -delta, out deleted);
            }

            if (!deleted)
            {
                delta = Remove(obj, delta, shape, qual, frame);
            }
        }

        return delta;
    }

    /// <summary>Exult <c>Game_map::create_ireg_object</c>: a new item that is not in the world yet.</summary>
    public U7Object NewItem(int shape, int frame) => _map.CreateIregObject(shape, frame);

    /// <summary>Exult <c>Container_game_object::add</c> / <c>Actor::add</c> (no combining).</summary>
    public bool AddTo(U7Object cont, U7Object obj)
    {
        if (cont.IsActor)
        {
            return Equipment.AddToActor(cont, obj, _catalog, _map);
        }

        // 255,255: let the gump pick a fresh spot (Exult sets cx/cy invalid).
        return Equipment.TryPlace(_map, obj, cont, 255, 255, _catalog);
    }
}
