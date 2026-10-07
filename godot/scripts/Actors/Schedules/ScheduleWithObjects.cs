using U7.Core;
using U7.Data;

namespace U7.Actors;

/// <summary>
/// Exult <c>Schedule_with_objects</c>: a schedule that creates items for the
/// NPC to work with (desk things, a waiter's dishes) and removes those still
/// in its hands when it ends; it walks to items and puts them down on tables.
/// </summary>
public abstract class ScheduleWithObjects(NpcBrain brain) : Schedule(brain)
{
    readonly List<U7Object> _created = new();
    U7Object? _currentItem;

    /// <summary>Exult <c>items_in_hand</c>: how many of its items the NPC carries.</summary>
    protected int ItemsInHand;

    /// <summary>Exult <c>current_item</c>: the one the NPC is using or walking to.</summary>
    protected U7Object? CurrentItem
    {
        get => _currentItem is { Removed: false } item ? item : null;
        set => _currentItem = value;
    }

    /// <summary>Exult <c>find_items</c>: the items of this kind within <paramref name="dist"/>.</summary>
    protected abstract List<U7Object> FindItems(int dist);

    /// <summary>Exult <c>add_object</c>: remember an item created for the NPC.</summary>
    protected void AddObject(U7Object obj) => _created.Add(obj);

    /// <summary>Exult <c>Actor::add(obj, true)</c>: into the NPC's inventory, unchecked.</summary>
    protected void GiveToNpc(U7Object item) => Map.PlaceInContainer(item, Npc, 255, 255);

    /// <summary>Exult <c>Schedule_with_objects::cleanup</c>: remove the created items the NPC still has.</summary>
    protected void Cleanup()
    {
        foreach (var item in _created)
        {
            if (!item.Removed && Owner(item) == Npc)
            {
                Map.RemoveObject(item);
            }
        }

        _created.Clear();
    }

    /// <summary>Exult: the created items go when the schedule does.</summary>
    public override void Ending(int newType) => Cleanup();

    /// <summary>
    /// Exult <c>walk_to_random_item</c>: walk to a free spot next to one of
    /// the items (on the NPC's floor), which becomes the current item.
    /// </summary>
    protected bool WalkToRandomItem(int dist = 16)
    {
        CurrentItem = null;
        var items = FindItems(dist);
        if (items.Count == 0)
        {
            return false;
        }

        var item = items[Rng.Next(items.Count)];
        CurrentItem = item;
        var spot = new TileCoord(item.Tx, item.Ty, Npc.Tz / 5 * 5);
        return Map.FindSpot(spot, 1, Npc) is { } pos &&
               WalkPathTo(pos, U7Constants.StandardDelayMs, 1000 + Rng.Next(1000));
    }

    /// <summary>
    /// Exult <c>drop_item</c>: put the item down on the table at the edge
    /// nearest the NPC, if there is room on top.
    /// </summary>
    protected bool DropItem(U7Object toDrop, U7Object table)
    {
        var (fx, fy, fw, fh) = ObjectGeometry.Footprint(table);
        int sx = Npc.Tx, sy = Npc.Ty;
        if (sy >= fy && sy < fy + fh)
        {
            sx = sx <= fx ? fx : fx + fw - 1; // East or west of the table.
        }
        else
        {
            sy = sy <= fy ? fy : fy + fh - 1; // North or south.
        }

        var top = table.Tz + Map.Catalog[table.Shape].DimZ;
        if (Map.FindSpot(new TileCoord(sx, sy, top), 1, toDrop.Shape, toDrop.Frame) is not { } pos || pos.Tz != top ||
            !ObjectGeometry.InFootprint(table, pos.Tx, pos.Ty))
        {
            return false;
        }

        SetAction(new PickupAction(Map, toDrop, pos, 250, temporary: false));
        ItemsInHand--;
        return true;
    }
}
