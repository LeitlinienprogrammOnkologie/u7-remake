using U7.Actors;
using U7.Core;
using U7.Data;
using U7.Rendering;
using U7.UI;
using U7.World;

namespace U7.Gumps;

/// <summary>
/// Open-gump list (last = top). Exult <c>Gump_manager</c>.
/// </summary>
public sealed class GumpManager
{
    readonly List<Gump> _open = new();
    int _stagger;
    int _nonPersistent;

    public GameMap Map { get; }
    public ShapeCatalog Catalog { get; }
    public U7Object Avatar { get; }
    public DragState? Drag { get; private set; }
    public int ScreenW = 320;
    public int ScreenH = 200;
    public Action<U7Object>? ActivateUsecode { get; set; }
    public Action<U7Object>? DroppedInWorld { get; set; }
    /// <summary>
    /// Exult <c>Dragging_info::drop</c>'s theft check: something not okay to
    /// take was put into another gump than it came from, or moved more than
    /// 2 tiles in the world.
    /// </summary>
    public Action? PossibleTheft { get; set; }
    /// <summary>Exult <c>Mouse::flash_shape</c>: a drag refused says why on the cursor.</summary>
    public Action<int>? FlashMouse { get; set; }
    public Action? ToggleCombat { get; set; }
    public Func<bool>? IsCombatOn { get; set; }
    /// <summary>GUMPS.VGA, for gumps laid out from their art's sizes (the spellbook).</summary>
    public VgaShapeFile? GumpsVga { get; set; }
    /// <summary>Runs a spell's usecode (function, caster) as a double-click.</summary>
    public Action<int, U7Object>? CastSpell { get; set; }
    ItemQuantity? _quantities;
    public ItemQuantity Quantities => _quantities ??= new ItemQuantity(Catalog, Map, null, null);

    public IReadOnlyList<Gump> Open => _open;
    public bool ShowingGumps => _open.Count > 0;
    public bool GumpMode => _nonPersistent > 0;
    public bool IsDragging => Drag is { Moved: true } || Drag?.Button is not null;

    public GumpManager(GameMap map, U7Object avatar)
    {
        Map = map;
        Catalog = map.Catalog;
        Avatar = avatar;
    }

    public Gump? FindGump(int mx, int my, GumpView view)
    {
        Gump? found = null;
        foreach (var g in _open)
        {
            if (g.HasPoint(view, mx, my) || g.OnButton(view, mx, my) is not null)
            {
                found = g;
            }
        }

        return found;
    }

    public Gump? FindGump(U7Object owner, int shapenum)
    {
        foreach (var g in _open)
        {
            if (g.Owner == owner && (shapenum < 0 || g.GumpShape == shapenum))
            {
                return g;
            }
        }

        return null;
    }

    public Gump? FindGumpForContainer(U7Object container)
    {
        foreach (var g in _open)
        {
            if (g.Owner == container)
            {
                return g;
            }
        }

        return null;
    }

    public void Add(U7Object? obj, int shapenum, bool actorGump = false)
    {
        if (obj is not null)
        {
            var existing = FindGump(obj, shapenum);
            if (existing is not null)
            {
                Raise(existing);
                return;
            }
        }

        var x = (1 + _stagger) * ScreenW / 10;
        var y = (1 + _stagger) * ScreenH / 10;
        Gump gump;
        if (shapenum == U7Constants.GumpStats)
        {
            gump = new StatsGump(obj ?? Avatar, x, y);
        }
        else if (shapenum == SpellbookGump.BookShape && obj is not null && GumpsVga is not null)
        {
            gump = new SpellbookGump(obj, x, y, GumpsVga, Spellbook.Available(obj, Quantities));
        }
        else if (actorGump && obj is not null)
        {
            var shape = shapenum >= 0 ? shapenum : U7Constants.GumpActorMale;
            if (shape < 0)
            {
                shape = U7Constants.GumpActorMale;
            }

            gump = new ActorGump(obj, x, y, shape);
        }
        else if (obj is not null)
        {
            gump = new ContainerGump(obj, x, y, shapenum);
        }
        else
        {
            return;
        }

        gump.Manager = this;
        _open.Add(gump);
        if (!gump.Persistent)
        {
            _nonPersistent++;
        }

        _stagger = (_stagger + 1) % 8;
    }

    public void Raise(Gump gump)
    {
        _open.Remove(gump);
        _open.Add(gump);
    }

    public void Close(Gump gump)
    {
        if (gump.Owner is not null)
        {
            gump.Owner.GumpX = gump.X;
            gump.Owner.GumpY = gump.Y;
        }

        if (!_open.Remove(gump))
        {
            return;
        }

        if (!gump.Persistent && _nonPersistent > 0)
        {
            _nonPersistent--;
        }

        if (Drag?.SourceGump == gump)
        {
            CancelDrag();
        }
    }

    public void CloseAll(bool persistentToo = false)
    {
        for (var i = _open.Count - 1; i >= 0; i--)
        {
            if (persistentToo || !_open[i].Persistent)
            {
                Close(_open[i]);
            }
        }
    }

    public void CloseFor(U7Object obj)
    {
        for (var i = _open.Count - 1; i >= 0; i--)
        {
            if (_open[i].Owner == obj)
            {
                Close(_open[i]);
            }
        }
    }

    public void ShowInventory()
    {
        var gumpShape = Catalog[Avatar.Shape].GumpShape;
        if (gumpShape < 0)
        {
            gumpShape = U7Constants.GumpActorMale;
        }

        Add(Avatar, gumpShape, actorGump: true);
    }

    public bool ShowGump(U7Object obj)
    {
        if (Catalog[obj.Shape].IsSpellbookClass)
        {
            // Exult Spellbook_object::activate.
            Add(obj, SpellbookGump.BookShape);
            return true;
        }

        var gumpShape = Catalog[obj.Shape].GumpShape;
        if (gumpShape < 0)
        {
            return false;
        }

        Add(obj, gumpShape, obj.IsActor);
        return true;
    }

    public bool OnMouseDown(GumpView view, int mx, int my, bool right, bool doubleClick)
    {
        var gump = FindGump(mx, my, view);
        if (right)
        {
            if (gump is not null)
            {
                gump.Close();
                return true;
            }

            return false;
        }

        if (doubleClick)
        {
            if (gump is not null)
            {
                if (gump.OnButton(view, mx, my)?.DoubleClick() == true)
                {
                    return true;
                }

                var inside = gump.FindObject(view, mx, my);
                if (inside is not null)
                {
                    if (!ShowGump(inside))
                    {
                        ActivateUsecode?.Invoke(inside);
                    }

                    return true;
                }

                Raise(gump);
                return true;
            }

            return false;
        }

        if (gump is null)
        {
            return false;
        }

        var button = gump.OnButton(view, mx, my);
        if (button is not null)
        {
            button.Pushed = true;
            Drag = new DragState { Button = button, MouseX = mx, MouseY = my, SourceGump = gump };
            return true;
        }

        var obj = gump.FindObject(view, mx, my);
        if (obj is not null)
        {
            Drag = new DragState
            {
                Object = obj,
                SourceGump = gump,
                MouseX = mx,
                MouseY = my,
                PaintX = mx,
                PaintY = my,
                OldTx = obj.Tx,
                OldTy = obj.Ty,
                OldTz = obj.Tz,
                OldContainer = obj.Container,
                OldReadySlot = obj.ReadySlot
            };
            Raise(gump);
            return true;
        }

        Drag = new DragState
        {
            SourceGump = gump,
            MouseX = mx,
            MouseY = my,
            PaintX = gump.X,
            PaintY = gump.Y
        };
        Raise(gump);
        return true;
    }

    public bool OnWorldMouseDown(GumpView view, U7Object obj, int mx, int my)
    {
        _ = view;
        Drag = new DragState
        {
            Object = obj,
            MouseX = mx,
            MouseY = my,
            PaintX = mx,
            PaintY = my,
            OldTx = obj.Tx,
            OldTy = obj.Ty,
            OldTz = obj.Tz,
            FromWorld = true
        };
        return true;
    }

    public bool OnMouseMove(GumpView view, int mx, int my)
    {
        var drag = Drag;
        if (drag is null)
        {
            return false;
        }

        var dx = mx - drag.MouseX;
        var dy = my - drag.MouseY;
        if (!drag.Moved && Math.Abs(dx) <= 2 && Math.Abs(dy) <= 2)
        {
            return true;
        }

        if (!drag.Moved)
        {
            if (drag.Object is { } held && drag.Button is null && RefuseDrag(drag, held) is { } why)
            {
                Drag = null;
                FlashMouse?.Invoke(why);
                return true;
            }

            drag.Moved = true;
            if (drag.Object is { } obj && drag.Button is null)
            {
                if (drag.FromWorld)
                {
                    Map.TakeFromWorld(obj);
                }
                else if (drag.SourceGump is not null && obj.Container is not null)
                {
                    obj.Container.Contents.Remove(obj);
                    obj.Container = null;
                    obj.ReadySlot = -1;
                }
            }
        }

        drag.MouseX = mx;
        drag.MouseY = my;
        if (drag.IsWindow && drag.SourceGump is { } g)
        {
            g.X += dx;
            g.Y += dy;
            drag.PaintX = g.X;
            drag.PaintY = g.Y;
        }
        else if (drag.Object is not null)
        {
            drag.PaintX = mx;
            drag.PaintY = my;
        }

        return true;
    }

    public bool OnMouseUp(GumpView view, int mx, int my, int worldTx, int worldTy, int worldTz)
    {
        var drag = Drag;
        if (drag is null)
        {
            return false;
        }

        Drag = null;
        if (drag.Button is { } btn)
        {
            btn.Pushed = false;
            if (btn.Contains(view, mx, my))
            {
                btn.Activate(view);
            }

            return true;
        }

        if (drag.IsWindow)
        {
            return true;
        }

        if (drag.Object is null || !drag.Moved)
        {
            return true;
        }

        var obj = drag.Object;
        var okayToMove = obj.GetFlag(ObjFlag.OkayToTake);
        var dest = FindGump(mx, my, view);
        if (dest is not null)
        {
            if (RefuseDrop(dest, obj) is { } why)
            {
                FlashMouse?.Invoke(why);
                PutBack(drag);
            }
            else if (!dest.Add(view, obj, mx, my))
            {
                FlashMouse?.Invoke(MouseShape.WontFit);
                PutBack(drag);
            }
            else if (dest != drag.SourceGump && !okayToMove)
            {
                PossibleTheft?.Invoke();
            }

            return true;
        }

        Map.PlaceInWorld(obj, worldTx, worldTy, worldTz);
        DroppedInWorld?.Invoke(obj);
        if (drag.FromWorld && !okayToMove &&
            new TileCoord(obj.Tx, obj.Ty, obj.Tz).Distance(new TileCoord(drag.OldTx, drag.OldTy, drag.OldTz)) > 2)
        {
            PossibleTheft?.Invoke();
        }

        return true;
    }

    /// <summary>
    /// Exult <c>Dragging_info::start</c>: the cursor's flash if the thing can't
    /// be picked up, or null: in the world, what weighs nothing (walls,
    /// furniture, people; Exult <c>is_dragable</c>) is too heavy, and what the
    /// avatar can't reach is the red X; in a gump, its owner must be within
    /// reach.
    /// </summary>
    int? RefuseDrag(DragState drag, U7Object obj)
    {
        if (drag.FromWorld)
        {
            if (obj.Kind is not (ObjectKind.Ireg or ObjectKind.Actor) || Catalog[obj.Shape].Weight <= 0)
            {
                return MouseShape.TooHeavy;
            }

            return FastPathClient.IsGrabable(Map, Avatar, obj) ? null : MouseShape.RedX;
        }

        return drag.SourceGump?.Owner is { } owner && !FastPathClient.IsGrabable(Map, Avatar, Outermost(owner))
            ? MouseShape.OutOfRange
            : null;
    }

    /// <summary>
    /// Exult <c>Dragging_info::drop_on_gump</c>'s checks: not into itself (the
    /// red X), the gump's owner within reach, and a party member not carrying
    /// more than they can (Exult <c>Check_weight</c>: too heavy); null if fine.
    /// </summary>
    int? RefuseDrop(Gump dest, U7Object obj)
    {
        if (dest.Owner is not { } owner)
        {
            return null;
        }

        var outer = Outermost(owner);
        if (outer == obj)
        {
            return MouseShape.RedX;
        }

        if (!FastPathClient.IsGrabable(Map, Avatar, outer))
        {
            return MouseShape.OutOfRange;
        }

        return outer.GetFlag(ObjFlag.InParty) &&
               (Inventory.GetWeight(outer, Catalog) + Inventory.GetWeight(obj, Catalog)) / 10 > Inventory.GetMaxWeight(outer)
            ? MouseShape.TooHeavy
            : null;
    }

    static U7Object Outermost(U7Object obj)
    {
        while (obj.Container is { } c)
        {
            obj = c;
        }

        return obj;
    }

    public void PutBack(DragState drag)
    {
        if (drag.Object is null)
        {
            return;
        }

        var obj = drag.Object;
        obj.ReadySlot = drag.OldReadySlot;
        if (drag.OldContainer is { } cont)
        {
            Map.PlaceInContainer(obj, cont, drag.OldTx, drag.OldTy);
        }
        else if (drag.FromWorld)
        {
            Map.PlaceInWorld(obj, drag.OldTx, drag.OldTy, drag.OldTz);
        }
    }

    public void CancelDrag()
    {
        if (Drag is { } d)
        {
            if (d.Button is not null)
            {
                d.Button.Pushed = false;
            }
            else if (d.Object is not null && d.Moved)
            {
                PutBack(d);
            }

            Drag = null;
        }
    }

    public U7Object? PickAnywhere(GumpView view, int mx, int my)
    {
        var gump = FindGump(mx, my, view);
        return gump?.FindObject(view, mx, my);
    }

    public string DebugText()
    {
        if (_open.Count == 0)
        {
            return "gumps: none";
        }

        var parts = _open.Select(g =>
        {
            var name = g.Owner is null ? "?" : Catalog[g.Owner.Shape].Name;
            if (string.IsNullOrEmpty(name))
            {
                name = $"shape {(g.Owner?.Shape ?? 0)}";
            }

            var n = g.Owner?.Contents.Count ?? 0;
            return $"{g.GumpShape}:{name}[{n}] @{g.X},{g.Y}";
        });
        return "gumps: " + string.Join(" | ", parts);
    }
}
