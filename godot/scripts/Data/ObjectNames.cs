using U7.Actors;

namespace U7.Data;

/// <summary>
/// Exult <c>Game_object::get_name</c> (objs/objnames.cc): the name a click on
/// a thing shows. TEXT.FLX writes a name with a quantity as
/// <c>article/name/singular/plural</c> (<c>a/black pearl//s</c>,
/// <c>/kni/fe/ves</c>): one is "a black pearl", more are "3 black pearls".
/// </summary>
public static class ObjectNames
{
    const int ShapeName = -255, NoName = -1;

    /// <summary>
    /// Black Gate's frame names (Exult <c>data/bg/shape_info.txt</c>,
    /// framenames), by shape and frame (-1: any other frame): a misc name, the
    /// shape's own name, or none. Food, sextants, desk items, reagents and
    /// amulets are named by frame; blood has a name in its first four frames only.
    /// </summary>
    static readonly Dictionary<(int Shape, int Frame), int> FrameNames = BuildFrameNames();

    static Dictionary<(int, int), int> BuildFrameNames()
    {
        var names = new Dictionary<(int, int), int>();
        for (var f = 0; f < 32; f++)
        {
            names[(377, f)] = 11 + f;
        }

        names[(650, 0)] = 44;
        names[(650, 1)] = 43;
        for (var f = 0; f <= 20; f++)
        {
            names[(675, f)] = 45 + f;
        }

        for (var f = 0; f < 8; f++)
        {
            names[(842, f)] = f;
        }

        for (var f = 0; f < 4; f++)
        {
            names[(912, f)] = ShapeName;
        }

        names[(912, -1)] = NoName;
        for (var f = 0; f < 3; f++)
        {
            names[(955, f)] = 8 + f;
        }

        return names;
    }

    /// <summary>Exult <c>Actor::get_name</c>: an NPC the avatar has met by its name, anyone else by the shape's.</summary>
    public static string Get(U7Object obj, ShapeCatalog catalog) =>
        obj.IsActor && obj.GetFlag(ObjFlag.Met) ? NpcName(obj, catalog) : OfThing(obj, catalog);

    /// <summary>Exult <c>Actor::get_npc_name</c>: its name, or the shape's if it has none (as monsters, here named after it).</summary>
    public static string NpcName(U7Object npc, ShapeCatalog catalog) =>
        npc.NpcName.Length > 0 && !npc.IsMonster ? npc.NpcName : OfThing(npc, catalog);

    /// <summary>Exult <c>Game_object::get_name</c>.</summary>
    public static string OfThing(U7Object obj, ShapeCatalog catalog)
    {
        var info = catalog[obj.Shape];
        var name = info.Name;
        if (FrameNames.TryGetValue((obj.Shape, obj.Frame), out var msg) || FrameNames.TryGetValue((obj.Shape, -1), out msg))
        {
            if (msg == NoName)
            {
                return "";
            }

            if (msg != ShapeName)
            {
                name = TextMessages.MiscName(msg);
            }
        }

        if (string.IsNullOrEmpty(name))
        {
            return "";
        }

        var quantity = info.HasQuantity ? obj.Quality & 0x7f : 1;
        if (!name.Contains('/'))
        {
            return quantity <= 1 ? name : $"{quantity} {name}";
        }

        var parts = name.Split('/');
        if (parts.Length < 4)
        {
            return "?";
        }

        return quantity <= 1
            ? (parts[0].Length > 0 ? parts[0] + " " : "") + parts[1] + parts[2]
            : $"{quantity} {parts[1]}{string.Join('/', parts[3..])}";
    }
}
