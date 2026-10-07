using U7.Actors;
using U7.Core;
using U7.Data;
using U7.Rendering;

namespace U7.World;

/// <summary>
/// Exult's light-source rules: how bright an object is, how much of it
/// reaches the avatar, what an actor carries, the light level Exult's
/// painter counts and the palette that level picks at night.
/// </summary>
public static class LightSources
{
    /// <summary>The ready spots <c>Actor::refigure_gear</c> looks at.</summary>
    static readonly int[] GearSpots =
    [
        ReadySpot.Head, ReadySpot.Belt, ReadySpot.Lhand, ReadySpot.Lfinger, ReadySpot.Legs, ReadySpot.Feet,
        ReadySpot.Rfinger, ReadySpot.Rhand, ReadySpot.Torso, ReadySpot.Neck, ReadySpot.Earrings,
        ReadySpot.Cloak, ReadySpot.Gloves
    ];

    /// <summary>
    /// Exult <c>Shape_info::get_object_light</c>: 0 unless the shape has
    /// TFA's light-source flag, else its <c>light_data</c> entry for the
    /// frame (data/bg/shape_info.txt), else 1, a candle. Black Gate's
    /// flagged shapes are 179, 198, 338, 435, 440, 442, 526, 534, 551, 553,
    /// 701, 739, 825 and 895. The file's "198:/-1/3" line is read as line 198
    /// of the section with no shape number, so it becomes an entry for shape
    /// 0 and shape 198 stays a candle; flaming oil (782) has an entry but no flag.
    /// </summary>
    public static int Brightness(ShapeCatalog catalog, int shape, int frame)
    {
        if ((uint)shape >= U7Constants.MaxShapes || !catalog[shape].LightSource)
        {
            return 0;
        }

        frame &= 31;
        return shape switch
        {
            179 => frame == 0 ? 0 : 5, // lightning
            338 => frame switch // lit light source
            {
                2 or 7 or 8 or 9 or 12 => 2,
                3 or 4 or 5 or 10 or 11 => 5,
                _ => 1
            },
            435 or 551 or 553 or 701 => 5, // sconce, fire sword, firedoom staff, torch
            526 or 825 => 7, // lamp, campfire
            739 => frame < 8 ? (frame & 3) * 2 : 1, // firepit
            _ => 1
        };
    }

    /// <summary>Exult <c>Game_object::get_center_tile</c> (x and y).</summary>
    public static (int Tx, int Ty) CenterTile(U7Object obj) =>
        (obj.Tx - ((obj.DimX - 1) >> 1), obj.Ty - ((obj.DimY - 1) >> 1));

    /// <summary>
    /// Exult <c>Get_light_strength</c>: <c>max(0, 75 - 2|dx| - 3|dy|)</c>
    /// times the brightness, from centre tile to centre tile, lift ignored.
    /// </summary>
    public static int Strength(U7Object obj, U7Object avatar, int brightness)
    {
        var (x1, y1) = CenterTile(obj);
        var (x2, y2) = CenterTile(avatar);
        var dx = Math.Abs(U7Constants.TileDelta(x1, x2));
        var dy = Math.Abs(U7Constants.TileDelta(y1, y2));
        return Math.Max(0, 75 - 2 * dx - 3 * dy) * brightness;
    }

    /// <summary>
    /// Exult <c>Actor::refigure_gear</c>'s light: the sum of the readied
    /// light sources, where one on the belt counts only if it isn't held in
    /// the hands (a torch tucked in the belt is out). The per-frame recount
    /// is what Exult gets from <c>set_light</c> and readying.
    /// </summary>
    public static int CarriedLight(U7Object actor, ShapeCatalog catalog) => CarriedLight(actor, catalog, out _);

    /// <summary><see cref="CarriedLight(U7Object, ShapeCatalog)"/>, with the brightest light carried.</summary>
    public static int CarriedLight(U7Object actor, ShapeCatalog catalog, out U7Object? brightest)
    {
        var total = 0;
        var best = 0;
        brightest = null;
        foreach (var worn in actor.Contents)
        {
            if (worn.Removed || worn.ReadySlot < 0 || Array.IndexOf(GearSpots, worn.ReadySlot) < 0)
            {
                continue;
            }

            var info = catalog[worn.Shape];
            if (!info.LightSource ||
                (worn.ReadySlot == ReadySpot.Belt && info.ReadyType is ReadySpot.Lhand or ReadySpot.Rhand or ReadySpot.BothHands))
            {
                continue;
            }

            var b = Brightness(catalog, worn.Shape, worn.Frame);
            total += b;
            if (b > best)
            {
                best = b;
                brightest = worn;
            }
        }

        return total;
    }

    /// <summary>
    /// The light level Exult's <c>Game_window::paint</c> gives the clock:
    /// <c>paint_map</c> counts the light sources in its chunks (from one
    /// tile above and left of the view to two chunks past it, for a
    /// 320×200 window round <paramref name="focus"/>), only the dungeon ones
    /// in a dungeon and only the others outside (<c>Map_chunk</c>'s two
    /// light lists), each by <see cref="Strength"/>; then the party's
    /// carried lights.
    /// </summary>
    public static int Level(GameMap map, ShapeCatalog catalog, U7Object avatar, IEnumerable<U7Object> party,
        (int Tx, int Ty) focus, bool inDungeon, Func<U7Object, int> frameOf)
    {
        // Exult center_view: the 40×25-tile window centred on the focus.
        var scrolltx = U7Constants.WrapTile(focus.Tx - 320 / U7Constants.TileSize / 2);
        var scrollty = U7Constants.WrapTile(focus.Ty - 200 / U7Constants.TileSize / 2);
        var startCx = U7Constants.WrapChunk((scrolltx - 1) / U7Constants.TilesPerChunk);
        var startCy = U7Constants.WrapChunk((scrollty - 1) / U7Constants.TilesPerChunk);
        var stopCx = U7Constants.WrapChunk(2 + (scrolltx + (320 + U7Constants.TileSize - 2) / U7Constants.TileSize +
                                                U7Constants.TilesPerChunk / 2) / U7Constants.TilesPerChunk);
        var stopCy = U7Constants.WrapChunk(2 + (scrollty + (200 + U7Constants.TileSize - 2) / U7Constants.TileSize +
                                                U7Constants.TilesPerChunk / 2) / U7Constants.TilesPerChunk);
        var level = 0;
        for (var cy = startCy; cy != stopCy; cy = U7Constants.WrapChunk(cy + 1))
        {
            for (var cx = startCx; cx != stopCx; cx = U7Constants.WrapChunk(cx + 1))
            {
                foreach (var obj in map.ObjectsInChunk(cx, cy))
                {
                    if (obj.Removed || obj.Container is not null || !catalog[obj.Shape].LightSource ||
                        (map.DungeonHeight(obj.Tx, obj.Ty) != 0) != inDungeon)
                    {
                        continue;
                    }

                    level += Strength(obj, avatar, Brightness(catalog, obj.Shape, frameOf(obj)));
                }
            }
        }

        level += Strength(avatar, avatar, CarriedLight(avatar, catalog));
        foreach (var member in party)
        {
            level += Strength(member, avatar, CarriedLight(member, catalog));
        }

        return level;
    }

    /// <summary>
    /// Exult <c>get_final_palette</c>'s light palettes, for a dark hour:
    /// below 224 candle, below 640 a single light, else many; -1 without light.
    /// </summary>
    public static int Classify(int level) =>
        level <= 0 ? -1
        : level < 224 ? PaletteSet.Candle
        : level < 640 ? PaletteSet.SingleLight
        : PaletteSet.ManyLights;
}
