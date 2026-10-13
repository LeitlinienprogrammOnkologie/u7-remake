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
    /// <summary>The NPC of a number (Exult <c>gwin->get_npc</c>), for the names that are an NPC's (Serpent Isle's corpses and urns).</summary>
    public static Func<int, U7Object?>? Npc { get; set; }

    /// <summary>Exult <c>Actor::get_name</c>: an NPC the avatar has met by its name, anyone else by the shape's.</summary>
    public static string Get(U7Object obj, ShapeCatalog catalog) =>
        obj.IsActor && obj.GetFlag(ObjFlag.Met) ? NpcName(obj, catalog) : OfThing(obj, catalog);

    /// <summary>Exult <c>Actor::get_npc_name</c>: its name, or the shape's if it has none (as monsters, here named after it).</summary>
    public static string NpcName(U7Object npc, ShapeCatalog catalog) =>
        npc.NpcName.Length > 0 && !npc.IsMonster ? npc.NpcName : OfThing(npc, catalog);

    /// <summary>
    /// Exult <c>Game_object::get_name</c>: the shape's name, unless its
    /// <c>framenames</c> entry for the frame and quality gives none (a type
    /// below 0), a misc name (type 0), or for shapes with a quality and
    /// bodies a misc name with the shape's or another before (odd types) or
    /// after it; from type 3 that other is the NPC the quality (a body's: its
    /// own) names, if met, else the entry's default.
    /// </summary>
    public static string OfThing(U7Object obj, ShapeCatalog catalog)
    {
        var info = catalog[obj.Shape];
        var qual = info.HasQuality && !info.IsNpcClass ? obj.Quality : -1;
        var entry = ShapeInfoText.Find(info.FrameNames, obj.Frame, qual, e => (e.Frame, e.Quality));
        var shapeName = info.Name;
        var type = entry?.Type ?? -255;
        string name;
        if (type < 0 && type != -255)
        {
            return "";
        }

        if (type == -255 || entry!.Value.MsgId >= TextMessages.MiscNameCount)
        {
            name = shapeName;
        }
        else if (type == 0)
        {
            name = TextMessages.MiscName(entry.Value.MsgId);
        }
        else if (!info.HasQuality && !info.IsBody)
        {
            name = shapeName;
        }
        else
        {
            var (msgId, otherMsg) = (entry.Value.MsgId, entry.Value.OtherMsg);
            var defName = false;
            string msg = "", other = "";
            if (type >= 3)
            {
                var npcNum = !info.IsBody ? obj.Quality : qual == 1 ? obj.LiveNpcNum : -1;
                if (npcNum >= 0 && Npc?.Invoke(npcNum) is { Unused: false } npc && (!info.IsBody || npc.GetFlag(ObjFlag.Met)))
                {
                    other = npc.NpcName;
                    defName = other.Length == 0;
                    msg = defName ? "" : TextMessages.MiscName(msgId);
                }
                else
                {
                    defName = true;
                }
            }
            else
            {
                msg = TextMessages.MiscName(msgId);
                other = otherMsg >= 0 && otherMsg < TextMessages.MiscNameCount ? TextMessages.MiscName(otherMsg) : shapeName;
            }

            if (!defName)
            {
                return (type & 1) != 0 ? other + msg : msg + other;
            }

            if (otherMsg >= 0 && otherMsg < TextMessages.MiscNameCount)
            {
                name = TextMessages.MiscName(otherMsg);
            }
            else if (otherMsg < 0 && otherMsg != -255)
            {
                return "";
            }
            else
            {
                name = shapeName;
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
