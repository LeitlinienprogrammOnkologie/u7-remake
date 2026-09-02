using U7.Actors;
using U7.Core;
using U7.Data;

namespace U7.Gumps;

public readonly record struct GumpArea(int X, int Y, int W, int H, int CheckX, int CheckY);

/// <summary>
/// Base gump: hotspot at (X,Y) in 320×200-style screen space, painted from GUMPS.VGA.
/// </summary>
public class Gump
{
    public static readonly GumpArea DefaultArea = new(52, 22, 60, 40, 8, 64);

    public static GumpArea AreaFor(int gumpShape) => gumpShape switch
    {
        U7Constants.GumpBox => new(46, 28, 74, 32, 8, 56),
        U7Constants.GumpCrate => new(50, 20, 80, 24, 8, 64),
        U7Constants.GumpBarrel => new(32, 32, 40, 40, 12, 124),
        U7Constants.GumpBag => new(48, 20, 66, 44, 8, 66),
        U7Constants.GumpBackpack => new(36, 36, 85, 40, 8, 62),
        U7Constants.GumpBasket => new(42, 32, 70, 26, 8, 56),
        U7Constants.GumpChest => new(40, 18, 60, 37, 8, 46),
        U7Constants.GumpShipHold => new(38, 10, 82, 80, 8, 92),
        U7Constants.GumpDrawer => new(36, 12, 70, 26, 8, 46),
        U7Constants.GumpBody => new(36, 46, 84, 40, 8, 70),
        _ => DefaultArea
    };

    public int X;
    public int Y;
    public int GumpShape;
    public GumpArea ObjectArea;
    public U7Object? Owner;
    public List<GumpButton> Buttons { get; } = new();
    public bool Persistent;
    public GumpManager Manager = null!;

    public Gump(U7Object? owner, int x, int y, int gumpShape)
    {
        Owner = owner;
        GumpShape = gumpShape;
        if (owner is { HasSavedGumpPos: true })
        {
            X = owner.GumpX;
            Y = owner.GumpY;
        }
        else
        {
            X = x;
            Y = y;
        }
    }

    public void SetObjectArea(GumpArea area, bool checkmark = true)
    {
        ObjectArea = area;
        if (checkmark && !Buttons.Exists(b => b.Kind == GumpButtonKind.Check))
        {
            Buttons.Add(new GumpButton(this, GumpButtonKind.Check,
                area.CheckX + 16, area.CheckY - 12, U7Constants.GumpCheck));
        }
    }

    public virtual void Close() => Manager.Close(this);

    public virtual bool HasPoint(GumpView view, int mx, int my)
    {
        var fi = view.Shapes.GetGumpFrame(GumpShape, 0);
        var tex = view.Shapes.GetGump(GumpShape, 0);
        if (tex is null)
        {
            return false;
        }

        var left = X - fi.XLeft;
        var top = Y - fi.YAbove;
        return mx >= left && my >= top && mx < left + tex.GetWidth() && my < top + tex.GetHeight();
    }

    public GumpButton? OnButton(GumpView view, int mx, int my)
    {
        for (var i = Buttons.Count - 1; i >= 0; i--)
        {
            if (Buttons[i].Contains(view, mx, my))
            {
                return Buttons[i];
            }
        }

        return null;
    }

    public virtual U7Object? FindObject(GumpView view, int mx, int my)
    {
        if (Owner is null)
        {
            return null;
        }

        U7Object? found = null;
        foreach (var obj in Owner.Contents)
        {
            if (obj.Removed || obj == view.Gumps.Drag?.Object || obj.ReadySlot is >= 0 and < 12)
            {
                continue;
            }

            GetShapeLocation(obj, out var ox, out var oy);
            if (view.WorldSpriteContains(obj, mx, my, ox, oy))
            {
                found = obj;
            }
        }

        foreach (var obj in Owner.Contents)
        {
            if (obj.Removed || obj == view.Gumps.Drag?.Object || obj.ReadySlot is not (>= 0 and < 12))
            {
                continue;
            }

            GetShapeLocation(obj, out var ox, out var oy);
            if (view.WorldSpriteContains(obj, mx, my, ox, oy))
            {
                found = obj;
            }
        }

        return found;
    }

    public void GetShapeLocation(U7Object obj, out int ox, out int oy)
    {
        ox = X + ObjectArea.X + obj.Tx;
        oy = Y + ObjectArea.Y + obj.Ty;
    }

    public virtual bool Add(GumpView view, U7Object obj, int mx, int my)
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

        var sx = mx - X - ObjectArea.X;
        var sy = my - Y - ObjectArea.Y;
        ClampToArea(view, obj, ref sx, ref sy);
        return Equipment.TryPlace(view.Map, obj, Owner, sx, sy, view.Catalog);
    }

    protected void ClampToArea(GumpView view, U7Object obj, ref int sx, ref int sy)
    {
        var fi = view.Catalog[obj.Shape].GetFrame(obj.Frame);
        if (sx - fi.XLeft < 0)
        {
            sx = fi.XLeft;
        }
        else if (sx + fi.XRight > ObjectArea.W)
        {
            sx = ObjectArea.W - fi.XRight;
        }

        if (sy - fi.YAbove < 0)
        {
            sy = fi.YAbove;
        }
        else if (sy + fi.YBelow > ObjectArea.H)
        {
            sy = ObjectArea.H - fi.YBelow;
        }

        sx = Math.Clamp(sx, 0, Math.Max(0, ObjectArea.W));
        sy = Math.Clamp(sy, 0, Math.Max(0, ObjectArea.H));
    }

    public virtual void Paint(GumpView view)
    {
        view.DrawGumpShape(GumpShape, 0, X, Y);
        LayoutContents(view);
        PaintContents(view);
        foreach (var btn in Buttons)
        {
            btn.Paint(view);
        }
    }

    protected void LayoutContents(GumpView view)
    {
        if (Owner is null || Owner.Contents.Count == 0 || ObjectArea.W <= 0)
        {
            return;
        }

        var same = true;
        var px = 0;
        var py = 0;
        var have = false;
        foreach (var obj in Owner.Contents)
        {
            if (obj.Removed || obj.ReadySlot is >= 0 and < 12)
            {
                continue;
            }

            if (!have)
            {
                px = obj.Tx;
                py = obj.Ty;
                have = true;
                continue;
            }

            if (obj.Tx != px || obj.Ty != py)
            {
                same = false;
                break;
            }
        }

        if (have && same)
        {
            foreach (var obj in Owner.Contents)
            {
                if (obj.ReadySlot is >= 0 and < 12)
                {
                    continue;
                }

                obj.Tx = 255;
                obj.Ty = 255;
            }
        }

        var cury = 0;
        var curx = 0;
        var loop = 0;
        foreach (var obj in Owner.Contents)
        {
            if (obj.Removed || obj.ReadySlot is >= 0 and < 12)
            {
                continue;
            }

            var fi = view.Catalog[obj.Shape].GetFrame(obj.Frame);
            var objx = obj.Tx - fi.XLeft + 1 + ObjectArea.X;
            var objy = obj.Ty - fi.YAbove + 1 + ObjectArea.Y;
            var inArea = objx >= ObjectArea.X && objy >= ObjectArea.Y &&
                         objx + fi.XRight - 1 < ObjectArea.X + ObjectArea.W &&
                         objy + fi.YBelow - 1 < ObjectArea.Y + ObjectArea.H;
            if (obj.Tx == 255 || !inArea)
            {
                var endx = ObjectArea.W;
                var endy = ObjectArea.H;
                var nx = curx + fi.Width;
                var ny = cury + fi.Height;
                if (nx > endx)
                {
                    nx = endx;
                }

                if (ny > endy)
                {
                    ny = endy;
                }

                obj.Tx = nx - fi.XRight;
                obj.Ty = ny - fi.YBelow;
                curx += Math.Max(1, fi.Width - 1);
                if (curx >= endx)
                {
                    cury += 8;
                    curx = 0;
                    if (cury >= endy)
                    {
                        cury = 2 * (++loop);
                    }
                }
            }
        }
    }

    protected void PaintContents(GumpView view)
    {
        if (Owner is null)
        {
            return;
        }

        foreach (var obj in Owner.Contents)
        {
            if (obj.Removed || obj == view.Gumps.Drag?.Object || obj.ReadySlot is >= 0 and < 12)
            {
                continue;
            }

            GetShapeLocation(obj, out var ox, out var oy);
            view.DrawWorldShape(obj.Shape, obj.Frame, ox, oy);
        }

        foreach (var obj in Owner.Contents)
        {
            if (obj.Removed || obj == view.Gumps.Drag?.Object || obj.ReadySlot is not (>= 0 and < 12))
            {
                continue;
            }

            GetShapeLocation(obj, out var ox, out var oy);
            view.DrawWorldShape(obj.Shape, obj.Frame, ox, oy);
        }
    }
}

public sealed class ContainerGump : Gump
{
    public ContainerGump(U7Object owner, int x, int y, int gumpShape)
        : base(owner, x, y, gumpShape)
    {
        SetObjectArea(AreaFor(gumpShape));
    }
}
