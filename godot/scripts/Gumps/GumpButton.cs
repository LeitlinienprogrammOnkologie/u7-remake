using U7.Actors;
using U7.Core;
using U7.Rendering;

namespace U7.Gumps;

public enum GumpButtonKind
{
    Check,
    Heart,
    Disk,
    Combat,
    Halo,
    CombatMode,
    /// <summary>Spellbook page corner (<see cref="GumpButton.Index"/> -1 back, 1 forward).</summary>
    SpellPage,
    SpellBookmark,
    /// <summary>A spell in a spellbook (<see cref="GumpButton.Index"/> is the spell).</summary>
    Spell
}

/// <summary>
/// Checkmark / heart / disk / combat / halo / combat-mode, and the
/// spellbook's page corners, bookmark and spells.
/// Coords are relative to the parent gump hotspot.
/// </summary>
public sealed class GumpButton
{
    public Gump Parent { get; }
    public GumpButtonKind Kind { get; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Shape { get; }
    public bool Pushed;
    /// <summary>The spell, or the page delta.</summary>
    public int Index;
    /// <summary>The frame always painted (spellbook buttons are never drawn pushed), or -1.</summary>
    public int Frame = -1;

    public GumpButton(Gump parent, GumpButtonKind kind, int x, int y, int shape)
    {
        Parent = parent;
        Kind = kind;
        X = x;
        Y = y;
        Shape = shape;
    }

    public void LocalToScreen(out int sx, out int sy)
    {
        sx = Parent.X + X;
        sy = Parent.Y + Y;
    }

    int PaintFrame(GumpView view)
    {
        var owner = Parent.Owner;
        return Kind switch
        {
            GumpButtonKind.Combat => view.Gumps.IsCombatOn?.Invoke() == true ? 1 : 0,
            GumpButtonKind.Halo => owner is { CombatProtected: true } ? 1 : 0,
            GumpButtonKind.CombatMode => owner?.AttackMode ?? 0,
            _ when Frame >= 0 => Frame,
            _ => Pushed ? 1 : 0
        };
    }

    public bool Contains(GumpView view, int mx, int my)
    {
        LocalToScreen(out var sx, out var sy);
        var frame = PaintFrame(view);
        var fi = view.Shapes.GetGumpFrame(Shape, frame);
        var tex = view.Shapes.GetGump(Shape, frame) ?? view.Shapes.GetGump(Shape, 0);
        if (tex is null)
        {
            return Math.Abs(mx - sx) < 8 && Math.Abs(my - sy) < 8;
        }

        var left = sx - fi.XLeft;
        var top = sy - fi.YAbove;
        return mx >= left && my >= top && mx < left + tex.GetWidth() && my < top + tex.GetHeight();
    }

    public void Paint(GumpView view)
    {
        LocalToScreen(out var sx, out var sy);
        view.DrawGumpShape(Shape, PaintFrame(view), sx, sy);
    }

    public void Activate(GumpView view)
    {
        switch (Kind)
        {
            case GumpButtonKind.Check:
                Parent.Close();
                break;
            case GumpButtonKind.Heart:
                view.Gumps.Add(Parent.Owner, U7Constants.GumpStats);
                break;
            case GumpButtonKind.Disk:
                break;
            case GumpButtonKind.Combat:
                view.Gumps.ToggleCombat?.Invoke();
                break;
            case GumpButtonKind.Halo:
                if (Parent.Owner is { } haloNpc)
                {
                    haloNpc.CombatProtected = !haloNpc.CombatProtected;
                }

                break;
            case GumpButtonKind.CombatMode:
                if (Parent.Owner is { } npc)
                {
                    var frames = npc.NpcNum == 0 ? AttackMode.AvatarFrames : AttackMode.NpcFrames;
                    npc.AttackMode = (npc.AttackMode + 1) % frames;
                    npc.UserSetAttack = true;
                }

                break;
            case GumpButtonKind.SpellPage:
                (Parent as SpellbookGump)?.ChangePage(Index);
                break;
            case GumpButtonKind.SpellBookmark:
                (Parent as SpellbookGump)?.TurnToBookmark();
                break;
            case GumpButtonKind.Spell:
                (Parent as SpellbookGump)?.SelectSpell(Index);
                break;
        }
    }

    /// <summary>Exult <c>Gump_button::double_clicked</c>: a spell is cast. False if the button does nothing.</summary>
    public bool DoubleClick()
    {
        if (Kind != GumpButtonKind.Spell || Parent is not SpellbookGump book)
        {
            return false;
        }

        book.DoSpell(Index);
        return true;
    }
}
