using U7.Core;
using U7.Data;

namespace U7.Gumps;

/// <summary>
/// Stats sheet, GUMPS.VGA shape 47. Numbers use FONTS.VGA font 2 (fallback 0).
/// </summary>
public sealed class StatsGump : Gump
{
    static readonly int[] TextY = [17, 26, 35, 46, 55, 67, 76, 86, 95, 104];
    const int TextX = 123;

    public StatsGump(U7Object owner, int x, int y)
        : base(owner, x, y, U7Constants.GumpStats)
    {
        SetObjectArea(new GumpArea(0, 0, 0, 0, 6, 136));
    }

    public override U7Object? FindObject(GumpView view, int mx, int my) => null;

    public override bool Add(GumpView view, U7Object obj, int mx, int my) => false;

    public override void Paint(GumpView view)
    {
        view.DrawGumpShape(GumpShape, 0, X, Y);
        foreach (var btn in Buttons)
        {
            btn.Paint(view);
        }

        var actor = Owner;
        var name = !string.IsNullOrEmpty(actor?.NpcName)
            ? actor.NpcName
            : (actor is { NpcNum: 0 } ? "Avatar" : (actor is null ? "" : view.Catalog[actor.Shape].Name));
        if (string.IsNullOrEmpty(name))
        {
            name = "Avatar";
        }

        view.DrawFontCentered(U7Constants.StatsFont, name, X + 30, Y + 6, 95);

        int[] stats;
        if (Owner is { NpcNum: >= 0 })
        {
            stats =
            [
                Owner.GetProp(U7.Actors.ActorProp.Strength),
                Owner.GetProp(U7.Actors.ActorProp.Dexterity),
                Owner.GetProp(U7.Actors.ActorProp.Intelligence),
                Owner.GetProp(U7.Actors.ActorProp.Combat),
                Owner.GetProp(U7.Actors.ActorProp.Magic),
                Owner.GetProp(U7.Actors.ActorProp.Health),
                Owner.GetProp(U7.Actors.ActorProp.Mana),
                Owner.GetProp(U7.Actors.ActorProp.Exp),
                Owner.GetLevel(),
                Owner.GetProp(U7.Actors.ActorProp.Training)
            ];
        }
        else
        {
            stats = [18, 18, 18, 18, 18, 18, 18, 0, 1, 0];
        }
        for (var i = 0; i < stats.Length && i < TextY.Length; i++)
        {
            view.DrawFontNumRight(U7Constants.StatsFont, stats[i], X + TextX, Y + TextY[i]);
        }
    }
}
