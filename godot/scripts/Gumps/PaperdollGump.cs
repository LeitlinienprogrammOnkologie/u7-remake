using U7.Actors;
using U7.Core;
using U7.Data;

namespace U7.Gumps;

/// <summary>
/// Exult <c>Paperdoll_gump</c>: an actor's inventory as Serpent Isle's
/// paperdoll (PAPERDOL.VGA shape 123), which Exult's Black Gate shows when
/// SI is installed. The body, head and arms are the character's
/// (<see cref="PaperdollInfo"/>); a worn thing is drawn in its paperdoll art
/// where it has one, else as itself over a blue box. Painted and hit in
/// Exult's order.
/// </summary>
public sealed class PaperdollGump : Gump
{
    /// <summary>PAPERDOL.VGA's paperdoll background.</summary>
    public const int Background = 123;
    /// <summary>PAPERDOL.VGA's belt (frame 0 a woman's, 1 a man's).</summary>
    const int BeltShape = 10;

    // Exult's coords, coords_blue and coords_hot, by spot.
    static readonly (int X, int Y)[] Coords =
    [
        (76, 20), (115, 27), (105, 65), (38, 55), (55, 62), (80, 80), (84, 105), (90, 50), (117, 55),
        (45, 35), (44, 26), (69, 59), (59, 19), (94, 20), (76, 26), (76, 33), (73, 53), (0, 0)
    ];

    static readonly (int X, int Y)[] CoordsBlue =
    [
        (76, 20), (64, 27), (54, 56), (30, 58), (55, 62), (80, 80), (84, 105), (90, 50), (68, 50),
        (45, 37), (22, 26), (69, 59), (59, 19), (94, 20), (76, 26), (76, 33), (73, 53), (0, 0)
    ];

    static readonly (int X, int Y)[] CoordsHot =
    [
        (76, 20), (94, 27), (92, 61), (38, 55), (55, 62), (80, 80), (84, 105), (90, 50), (117, 55),
        (83, 43), (76, 41), (69, 59), (59, 19), (94, 20), (76, 26), (76, 33), (73, 53), (0, 0)
    ];

    // Exult's paperdoll positions.
    static readonly (int X, int Y) Body = (46, 33), HeadP = (46, 22);
    static readonly (int X, int Y) BeltF = (58, 52), NeckF = (46, 47), BeltM = (57, 55), NeckM = (46, 44);
    static readonly (int X, int Y) LegsP = (57, 66), FeetP = (46, 99);
    static readonly (int X, int Y) Hands = (68, 44), RhandP = (68, 44), LhandP = (34, 65), AHand = (28, 59), AmmoP = (28, 59);
    static readonly (int X, int Y) BackF = (68, 28), Back2F = (34, 22), BackM = (68, 22), Back2M = (35, 22);
    static readonly (int X, int Y) ShieldF = (56, 25), ShieldM = (57, 22);

    readonly PaperdollInfo _dolls;

    public PaperdollGump(U7Object owner, int x, int y, int gumpShape, PaperdollInfo dolls)
        : base(owner, x, y, gumpShape)
    {
        _dolls = dolls;
        SetObjectArea(new GumpArea(26, 0, 104, 140, 6, 145));
        Buttons.Add(new GumpButton(this, GumpButtonKind.Heart, 97, 137, U7Constants.GumpHeart));
        // Black Gate's: the halo and combat mode (Serpent Isle's has its combat stats button instead).
        Buttons.Add(owner.NpcNum == 0
            ? new GumpButton(this, GumpButtonKind.Halo, 123, 120, U7Constants.GumpHalo)
            : new GumpButton(this, GumpButtonKind.Halo, 123, 137, U7Constants.GumpHalo));
        Buttons.Add(new GumpButton(this, GumpButtonKind.CombatMode, 75, 140, U7Constants.GumpCombatMode));
        if (owner.NpcNum == 0)
        {
            Buttons.Add(new GumpButton(this, GumpButtonKind.Disk, 123, 137, U7Constants.GumpDisk));
            Buttons.Add(new GumpButton(this, GumpButtonKind.Combat, 51, 142, U7Constants.GumpCombat));
        }
    }

    /// <summary>Exult <c>Gump::has_point</c> on the paperdoll's own pixels.</summary>
    public override bool HasPoint(GumpView view, int mx, int my) =>
        view.Shapes.GetPaperdoll8(Background, 0) is { } frame && frame.Covers(mx - X, my - Y);

    /// <summary>
    /// Exult <c>get_npc_paperdoll</c> down to <c>get_npc_paperdoll_safe</c>:
    /// the actor's shape's character, else the default avatar's of its sex.
    /// </summary>
    PaperdollNpc? Doll(ShapeCatalog catalog)
    {
        var actor = Owner!;
        var female = AvatarLook.IsFemale(actor);
        if (_dolls.Npc(actor.Shape) is { } own)
        {
            return own;
        }

        return catalog.Avatar.Defaults.TryGetValue(female, out var def) ? _dolls.Npc(def.Shape) : null;
    }

    /// <summary>Whether a paperdoll can be shown for the actor at all.</summary>
    public static bool CanShow(U7Object actor, PaperdollInfo dolls, ShapeCatalog catalog) =>
        dolls.Npc(actor.Shape) is not null ||
        (catalog.Avatar.Defaults.TryGetValue(AvatarLook.IsFemale(actor), out var def) && dolls.Npc(def.Shape) is not null);

    bool Female(PaperdollNpc doll) => AvatarLook.IsFemale(Owner!) || doll.Female;

    U7Object? Readied(int spot) => Equipment.GetReadied(Owner!, spot);

    public override void Paint(GumpView view)
    {
        view.DrawPaperdollShape(Background, 0, X, Y);
        foreach (var check in Buttons.Where(b => b.Kind == GumpButtonKind.Check))
        {
            check.Paint(view);
        }

        if (Owner is { } actor && Doll(view.Catalog) is { } doll)
        {
            PaintDoll(view, actor, doll);
        }

        foreach (var btn in Buttons.Where(b => b.Kind != GumpButtonKind.Check))
        {
            btn.Paint(view);
        }

        if (Owner is { } owner)
        {
            var text = Manager.FailedCopyProtection()
                ? U7.Usecode.UsecodeMachine.Oink
                : $"{Inventory.GetWeight(owner, view.Catalog) / 10}/{Inventory.GetMaxWeight(owner)}";
            view.DrawFontCentered(U7Constants.StatsFont, text, X + 84 - 50, Y + 114, 100);
        }
    }

    /// <summary>Exult <c>Paperdoll_gump::paint</c>'s order, back to front.</summary>
    void PaintDoll(GumpView view, U7Object actor, PaperdollNpc doll)
    {
        var catalog = view.Catalog;
        var female = Female(doll);
        var (shield, back2, back, neck, beltp) = female
            ? (ShieldF, Back2F, BackF, NeckF, BeltF)
            : (ShieldM, Back2M, BackM, NeckM, BeltM);
        var scabbard = Equipment.UsesScabbard(actor, catalog);
        var neckUsed = Equipment.UsesNeck(actor, catalog);
        if (scabbard)
        {
            PaintObject(view, doll, ReadySpot.Belt, shield, 0, ReadySpot.BackShield);
            PaintObject(view, doll, ReadySpot.Belt, back2, 0, ReadySpot.Back2h);
        }
        else
        {
            PaintObject(view, doll, ReadySpot.BackShield, shield);
            PaintObject(view, doll, ReadySpot.Back2h, back2);
        }

        PaintObject(view, doll, ReadySpot.Back, back);
        PaintObject(view, doll, neckUsed ? ReadySpot.Neck : ReadySpot.Cloak, Body, 0, neckUsed ? ReadySpot.Cloak : -1);
        PaintPart(view, doll, doll.BodyShape, doll.BodyFrame, Body);
        PaintObject(view, doll, ReadySpot.Legs, LegsP);
        PaintObject(view, doll, ReadySpot.Feet, FeetP);
        PaintObject(view, doll, ReadySpot.Ammo, AmmoP, 0, -1);
        PaintObject(view, doll, ReadySpot.Torso, Body);
        PaintPart(view, doll, BeltShape, female ? 0 : 1, BeltM);
        PaintPart(view, doll, doll.HeadShape, HeadFrame(doll), HeadP);
        if (!neckUsed || !NeckFillsCloak())
        {
            PaintObject(view, doll, ReadySpot.Neck, neck);
        }

        if (!scabbard)
        {
            PaintObject(view, doll, ReadySpot.Belt, beltp);
        }

        PaintPart(view, doll, doll.ArmsShape, doll.ArmsFrames[ArmType()], Body);
        PaintObject(view, doll, ReadySpot.Torso, Body, 1 + ArmType(), ReadySpot.Torso);
        PaintObject(view, doll, ReadySpot.Earrings, HeadP);
        PaintObject(view, doll, ReadySpot.Head, HeadP);
        PaintObject(view, doll, neckUsed ? ReadySpot.Neck : ReadySpot.Cloak, Body, 0, PaperdollInfo.CloakClasp);
        PaintObject(view, doll, ReadySpot.Rfinger, LhandP, ArmType());
        if (Equipment.IsTwoFingered(actor, catalog) && LeftRingIsGloves())
        {
            PaintObject(view, doll, ReadySpot.Lfinger, Hands, ArmType(), ReadySpot.Gloves);
        }
        else
        {
            PaintObject(view, doll, ReadySpot.Lfinger, RhandP, ArmType());
        }

        PaintObject(view, doll, ReadySpot.Gloves, Hands, ArmType());
        PaintObject(view, doll, ReadySpot.Lhand, LhandP);
        PaintObject(view, doll, ReadySpot.Ammo, AHand, 2, -1);
        PaintObject(view, doll, ReadySpot.Rhand, RhandP);
    }

    /// <summary>Whether the amulet spot's thing is drawn as a cloak (Exult's <c>is_neck_used</c> paint).</summary>
    bool NeckFillsCloak() =>
        Readied(ReadySpot.Neck) is { } obj &&
        (_dolls.Item(obj.Shape, obj.Frame, ReadySpot.Cloak) is not null ||
         _dolls.Item(obj.Shape, obj.Frame, PaperdollInfo.CloakClasp) is not null);

    bool LeftRingIsGloves() =>
        Readied(ReadySpot.Lfinger) is { } obj && _dolls.Item(obj.Shape, obj.Frame, ReadySpot.Gloves) is not null;

    /// <summary>Exult <c>paint_head</c>'s frame: the helmet's when a helmet asks for it.</summary>
    int HeadFrame(PaperdollNpc doll) =>
        Readied(ReadySpot.Head) is { } helm && _dolls.Item(helm.Shape, helm.Frame, ReadySpot.Head) is { Type: not 0 }
            ? doll.HeadFrameHelm
            : doll.HeadFrame;

    /// <summary>Exult <c>get_arm_type</c>: a two-handed weapon's arm frames, else 0.</summary>
    int ArmType()
    {
        if (Readied(ReadySpot.Lhand) is not { } obj || Manager.Catalog[obj.Shape].ReadyType != ReadySpot.BothHands)
        {
            return 0;
        }

        return _dolls.Item(obj.Shape, obj.Frame, ReadySpot.Lhand)?.Type ?? 0;
    }

    void PaintPart(GumpView view, PaperdollNpc doll, int shape, int frame, (int X, int Y) at) =>
        view.DrawPaperdollShape(shape, frame, X + ObjectArea.X + at.X, Y + ObjectArea.Y + at.Y, doll.Translucent);

    /// <summary>
    /// Exult <c>paint_object</c>: the readied thing's paperdoll art for
    /// <paramref name="itemType"/> (the spot, -1 for it), or the thing itself
    /// over the spot's blue box when it has none; nothing when an item type
    /// was asked for and it has none.
    /// </summary>
    void PaintObject(GumpView view, PaperdollNpc doll, int spot, (int X, int Y) at, int frame = 0, int itemType = -1)
    {
        if (Readied(spot) is not { } obj)
        {
            return;
        }

        var oldType = itemType;
        if (itemType == -1)
        {
            itemType = spot;
        }

        var bx = X + ObjectArea.X;
        var by = Y + ObjectArea.Y;
        var item = _dolls.Item(obj.Shape, obj.Frame, itemType);
        if (item is null || item.Frames[0] == -1 || item.Shape == -1)
        {
            if ((oldType != -1 && item is null) || (spot == ReadySpot.Ammo && frame == 2))
            {
                return;
            }

            SetToSpot(view, obj, spot);
            view.DrawGumpShape(BlueShape(spot), 0, bx + CoordsBlue[spot].X, by + CoordsBlue[spot].Y);
            view.DrawWorldShape(obj.Shape, obj.Frame, bx + obj.Tx, by + obj.Ty);
            return;
        }

        if (spot == ReadySpot.Ammo && !AmmoFrame(obj, ref frame))
        {
            return;
        }

        var f = PaperdollFrame(item, frame, doll);
        view.DrawPaperdollShape(item.Shape, f, bx + at.X, by + at.Y, item.Translucent);
    }

    /// <summary>The item's frame for that arm or ammo variant, one on for a man when it is sexed.</summary>
    int PaperdollFrame(PaperdollItem item, int frame, PaperdollNpc doll) =>
        item.Frame(frame) + (item.Gender && !Female(doll) ? 1 : 0);

    /// <summary>
    /// Exult <c>Get_ammo_frame</c>: ammunition shows in the hand (frame 2)
    /// only with a weapon that fires it; in the quiver as frame 1 beside a
    /// weapon.
    /// </summary>
    bool AmmoFrame(U7Object ammo, ref int frame)
    {
        if (Readied(ReadySpot.Lhand) is { } weapon)
        {
            if (Manager.Weapons?[weapon.Shape] is not { } winf)
            {
                return frame != 2;
            }

            var family = Manager.Ammo?[ammo.Shape]?.Family ?? ammo.Shape;
            var inFamily = winf.Ammo == family;
            if (frame == 2 && !inFamily)
            {
                return false;
            }

            if (frame == 0)
            {
                frame++;
            }
        }
        else if (frame == 2)
        {
            return false;
        }

        return true;
    }

    /// <summary>Exult <c>Shapeinfo_lookup::GetBlueShapeData</c>: the spot's blue box, the first entry for it or for any.</summary>
    int BlueShape(int spot)
    {
        foreach (var (s, gump) in Manager.Catalog.Files.BlueShapes)
        {
            if (s == -1 || s == spot)
            {
                return gump;
            }
        }

        return 54;
    }

    /// <summary>Exult <c>set_to_spot</c>: the thing centred on the spot's point.</summary>
    void SetToSpot(GumpView view, U7Object obj, int spot)
    {
        var fi = view.Catalog[obj.Shape].GetFrame(obj.Frame);
        obj.Tx = Coords[spot].X + fi.XLeft - fi.Width / 2 - ObjectArea.X;
        obj.Ty = Coords[spot].Y + fi.YAbove - fi.Height / 2 - ObjectArea.Y;
    }

    /// <summary>Exult <c>Paperdoll_gump::find_object</c>: front to back, the body parts hiding what is behind them.</summary>
    public override U7Object? FindObject(GumpView view, int mx, int my)
    {
        if (Owner is not { } actor || Doll(view.Catalog) is not { } doll)
        {
            return null;
        }

        mx -= X + ObjectArea.X;
        my -= Y + ObjectArea.Y;
        var catalog = view.Catalog;
        var female = Female(doll);
        var (shield, back2, back, neck, beltp) = female
            ? (ShieldF, Back2F, BackF, NeckF, BeltF)
            : (ShieldM, Back2M, BackM, NeckM, BeltM);
        var scabbard = Equipment.UsesScabbard(actor, catalog);
        var neckUsed = Equipment.UsesNeck(actor, catalog);
        var dragged = view.Gumps.Drag?.Object;
        U7Object? Check(int spot, (int X, int Y) at, int frame = 0, int itemType = -1)
        {
            var found = CheckObject(view, doll, mx, my, spot, at, frame, itemType);
            return found == dragged ? null : found;
        }

        if ((Check(ReadySpot.Rhand, RhandP) ?? Check(ReadySpot.Ammo, AHand, 2, -1) ?? Check(ReadySpot.Lhand, LhandP) ??
             Check(ReadySpot.Gloves, Hands, ArmType())) is { } hand)
        {
            return hand;
        }

        if (Equipment.IsTwoFingered(actor, catalog))
        {
            if ((LeftRingIsGloves()
                    ? Check(ReadySpot.Lfinger, Hands, ArmType(), ReadySpot.Gloves)
                    : Check(ReadySpot.Lfinger, RhandP, ArmType())) is { } ring)
            {
                return ring;
            }
        }
        else if (Check(ReadySpot.Lfinger, RhandP, ArmType()) is { } ring)
        {
            return ring;
        }

        if ((Check(ReadySpot.Rfinger, LhandP, ArmType()) ??
             Check(neckUsed ? ReadySpot.Neck : ReadySpot.Cloak, Body, 0, PaperdollInfo.CloakClasp) ??
             Check(ReadySpot.Head, HeadP) ?? Check(ReadySpot.Earrings, HeadP) ??
             Check(ReadySpot.Torso, Body, 1 + ArmType(), ReadySpot.Torso)) is { } upper)
        {
            return upper;
        }

        if (Hit(view, doll.ArmsShape, doll.ArmsFrames[ArmType()], mx - Body.X, my - Body.Y))
        {
            return null;
        }

        if (!scabbard && Check(ReadySpot.Belt, beltp) is { } belt)
        {
            return belt;
        }

        if ((!neckUsed || !NeckFillsCloak()) && Check(ReadySpot.Neck, neck) is { } amulet)
        {
            return amulet;
        }

        if (Hit(view, doll.HeadShape, HeadFrame(doll), mx - HeadP.X, my - HeadP.Y) ||
            (female
                ? Hit(view, BeltShape, 0, mx - BeltF.X, my - BeltF.Y)
                : Hit(view, BeltShape, 1, mx - BeltM.X, my - BeltM.Y)))
        {
            return null;
        }

        if ((Check(ReadySpot.Torso, Body) ?? Check(ReadySpot.Ammo, AmmoP, 0, -1) ?? Check(ReadySpot.Feet, FeetP) ??
             Check(ReadySpot.Legs, LegsP)) is { } lower)
        {
            return lower;
        }

        if (Hit(view, doll.BodyShape, doll.BodyFrame, mx - Body.X, my - Body.Y))
        {
            return null;
        }

        return Check(neckUsed ? ReadySpot.Neck : ReadySpot.Cloak, Body, 0, neckUsed ? ReadySpot.Cloak : -1) ??
               Check(ReadySpot.Back, back) ??
               (scabbard
                   ? Check(ReadySpot.Belt, back2, 0, ReadySpot.Back2h) ?? Check(ReadySpot.Belt, shield, 0, ReadySpot.BackShield)
                   : Check(ReadySpot.Back2h, back2) ?? Check(ReadySpot.BackShield, shield));
    }

    /// <summary>
    /// Exult <c>check_object</c>: under the mouse in its paperdoll art (the
    /// thing then put where the mouse is, for the drag), or as itself on the
    /// spot.
    /// </summary>
    U7Object? CheckObject(GumpView view, PaperdollNpc doll, int mx, int my, int spot, (int X, int Y) at, int frame,
        int itemType)
    {
        if (Readied(spot) is not { } obj)
        {
            return null;
        }

        var oldType = itemType;
        if (itemType == -1)
        {
            itemType = spot;
        }

        var item = _dolls.Item(obj.Shape, obj.Frame, itemType);
        if (item is null || item.Frames[0] == -1 || item.Shape == -1)
        {
            if ((oldType != -1 && item is null) || (spot == ReadySpot.Ammo && frame == 2))
            {
                return null;
            }

            if (obj.Tx == 0 && obj.Ty == 0)
            {
                SetToSpot(view, obj, spot);
            }

            return view.Shapes.GetFrame8(obj.Shape, obj.Frame) is { } own && own.Covers(mx - obj.Tx, my - obj.Ty)
                ? obj
                : null;
        }

        if (spot == ReadySpot.Ammo && !AmmoFrame(obj, ref frame))
        {
            return null;
        }

        if (!Hit(view, item.Shape, PaperdollFrame(item, frame, doll), mx - at.X, my - at.Y))
        {
            return null;
        }

        var fi = view.Catalog[obj.Shape].GetFrame(obj.Frame);
        obj.Tx = mx + fi.XLeft - fi.Width / 2;
        obj.Ty = my + fi.YAbove - fi.Height / 2;
        return obj;
    }

    /// <summary>Exult <c>check_shape</c> on a PAPERDOL.VGA frame, relative to its hotspot.</summary>
    static bool Hit(GumpView view, int shape, int frame, int px, int py) =>
        view.Shapes.GetPaperdoll8(shape, frame) is { } f && f.Covers(px, py);

    /// <summary>
    /// Exult <c>Paperdoll_gump::find_closest</c>: the nearest spot to the
    /// mouse (by its hot point), a spot a multi-spot thing fills counting as
    /// the one it is in.
    /// </summary>
    int FindClosest(ShapeCatalog catalog, int mx, int my, bool onlyEmpty)
    {
        mx -= X;
        my -= Y;
        var actor = Owner!;
        var best = -1;
        var bestD = 1_000_000L;
        for (var i = 0; i < CoordsHot.Length; i++)
        {
            var spot = i;
            var dx = mx - CoordsHot[i].X;
            var dy = my - CoordsHot[i].Y;
            long d = dx * dx + dy * dy;
            if ((i == ReadySpot.BackShield || i == ReadySpot.Back2h) && Equipment.UsesScabbard(actor, catalog))
            {
                spot = ReadySpot.Belt;
            }
            else if (i == ReadySpot.Cloak && Equipment.UsesNeck(actor, catalog))
            {
                spot = ReadySpot.Neck;
            }
            else if (i == ReadySpot.Rhand && Equipment.IsTwoHanded(actor, catalog))
            {
                spot = ReadySpot.Lhand;
            }
            else if ((i == ReadySpot.Rfinger || i == ReadySpot.Gloves) && Equipment.IsTwoFingered(actor, catalog))
            {
                spot = ReadySpot.Lfinger;
            }

            if (d < bestD && !(onlyEmpty && Readied(spot) is not null))
            {
                bestD = d;
                best = spot;
            }
        }

        return best;
    }

    /// <summary>
    /// Exult <c>Paperdoll_gump::add</c>: onto a thing under the mouse, else
    /// readied in the nearest free spot, else into the pack.
    /// </summary>
    public override bool Add(GumpView view, U7Object obj, int mx, int my)
    {
        if (Owner is null || obj == Owner)
        {
            return false;
        }

        var onobj = FindObject(view, mx, my);
        if (onobj is not null && onobj != obj && DropOnto(view, onobj, obj))
        {
            return true;
        }

        var spot = FindClosest(view.Catalog, mx, my, onlyEmpty: true);
        if (spot >= 0 && Equipment.AddReadied(Owner, obj, spot, view.Catalog, view.Map))
        {
            return true;
        }

        return Equipment.AddToActor(Owner, obj, view.Catalog, view.Map, combine: view.Quantities);
    }
}
