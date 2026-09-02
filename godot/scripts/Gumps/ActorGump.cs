using U7.Actors;
using U7.Core;
using U7.Data;

namespace U7.Gumps;

/// <summary>
/// Avatar / NPC inventory. 12 ready slots + backpack-like object area. Exult <c>Actor_gump</c>
/// (Black Gate paper doll: GUMPS.VGA silhouette with world sprites on the spots).
/// </summary>
public sealed class ActorGump : Gump
{
    static readonly (int X, int Y)[] Slots =
    [
        (114, 10), // head
        (115, 24), // backpack / back
        (115, 37), // belt
        (115, 55), // lhand
        (115, 71), // lfinger
        (114, 85), // legs
        (76, 98),  // feet
        (35, 70),  // rfinger
        (37, 56),  // rhand
        (37, 37),  // torso
        (37, 24),  // neck
        (37, 11)   // ammo
    ];

    public ActorGump(U7Object owner, int x, int y, int gumpShape)
        : base(owner, x, y, gumpShape)
    {
        SetObjectArea(new GumpArea(26, 0, 104, 132, 6, 136));
        Buttons.Add(new GumpButton(this, GumpButtonKind.Heart, 124, 132, U7Constants.GumpHeart));
        if (owner.NpcNum == 0)
        {
            Buttons.Add(new GumpButton(this, GumpButtonKind.Disk, 124, 115, U7Constants.GumpDisk));
            Buttons.Add(new GumpButton(this, GumpButtonKind.Combat, 52, 100, U7Constants.GumpCombat));
        }

        Buttons.Add(new GumpButton(this, GumpButtonKind.Halo, 47, 110, U7Constants.GumpHalo));
        Buttons.Add(new GumpButton(this, GumpButtonKind.CombatMode, 48, 132, U7Constants.GumpCombatMode));
    }

    public override bool Add(GumpView view, U7Object obj, int mx, int my)
    {
        if (Owner is null || obj == Owner)
        {
            return false;
        }

        var onobj = FindObject(view, mx, my);
        if (onobj is not null && onobj != obj)
        {
            if (Inventory.TryCombine(onobj, obj, view.Catalog))
            {
                return true;
            }

            if (Inventory.IsContainer(onobj, view.Catalog))
            {
                if (Inventory.TryCombineInto(onobj, obj, view.Catalog))
                {
                    return true;
                }

                return Equipment.TryPlace(view.Map, obj, onobj, 8, 8, view.Catalog);
            }
        }

        var slot = FindClosest(mx, my, onlyEmpty: true);
        if (slot >= 0 && Equipment.AddReadied(Owner, obj, slot, view.Catalog, view.Map))
        {
            if (!obj.Removed)
            {
                SetToSpot(view, obj, obj.ReadySlot >= 0 ? obj.ReadySlot : slot);
            }

            return true;
        }

        return Equipment.AddToActor(Owner, obj, view.Catalog, view.Map);
    }

    public override void Paint(GumpView view)
    {
        if (Owner is not null)
        {
            foreach (var obj in Owner.Contents)
            {
                if (obj.ReadySlot is >= 0 and < 12)
                {
                    SetToSpot(view, obj, obj.ReadySlot);
                }
            }
        }

        base.Paint(view);

        if (Owner is { } actor)
        {
            if (Equipment.IsTwoFingered(actor, view.Catalog))
            {
                view.DrawGumpShape(U7Constants.GumpSpotOverlay, 1, X + 36, Y + 70);
            }

            if (Equipment.IsTwoHanded(actor, view.Catalog))
            {
                view.DrawGumpShape(U7Constants.GumpSpotOverlay, 0, X + 36, Y + 55);
            }

            var maxWt = Inventory.GetMaxWeight(actor);
            var wt = Inventory.GetWeight(actor, view.Catalog) / 10;
            view.DrawFontCentered(U7Constants.StatsFont, $"{wt}/{maxWt}", X + 28, Y + 120, 102);
        }
    }

    int FindClosest(int mx, int my, bool onlyEmpty)
    {
        mx -= X;
        my -= Y;
        var best = -1;
        var bestD = 1_000_000;
        for (var i = 0; i < Slots.Length; i++)
        {
            if (onlyEmpty && GetReadied(i) is not null)
            {
                continue;
            }

            var dx = mx - Slots[i].X;
            var dy = my - Slots[i].Y;
            var d = dx * dx + dy * dy;
            if (d < bestD)
            {
                bestD = d;
                best = i;
            }
        }

        return best;
    }

    U7Object? GetReadied(int slot) => Owner is null ? null : Equipment.GetReadied(Owner, slot);

    void SetToSpot(GumpView view, U7Object obj, int index)
    {
        if ((uint)index >= (uint)Slots.Length)
        {
            return;
        }

        var fi = view.Catalog[obj.Shape].GetFrame(obj.Frame);
        var w = fi.Width;
        var h = fi.Height;
        var tx = Slots[index].X + fi.XLeft - w / 2 - ObjectArea.X;
        var ty = Slots[index].Y + fi.YAbove - h / 2 - ObjectArea.Y;
        var x0 = tx - fi.XLeft;
        var y0 = ty - fi.YAbove;
        if (x0 < 0)
        {
            tx -= x0;
        }

        if (y0 < 0)
        {
            ty -= y0;
        }

        if (x0 + w > ObjectArea.W)
        {
            tx -= x0 + w - ObjectArea.W;
        }

        if (y0 + h > ObjectArea.H)
        {
            ty -= y0 + h - ObjectArea.H;
        }

        obj.Tx = tx;
        obj.Ty = ty;
    }
}
