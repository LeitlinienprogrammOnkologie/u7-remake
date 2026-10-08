using System.IO;
using U7.Core;

namespace U7.Actors;

/// <summary>
/// Exult <c>Equip_record</c>s from <c>STATIC/EQUIP.DAT</c> (shapevga.cc):
/// a count, then ten elements a record of shape (2 bytes), chance in 100,
/// quantity and two unused bytes. A monster's <c>equip_offset</c> picks one.
/// </summary>
public sealed class MonsterEquipment
{
    public readonly record struct Element(int Shape, int Probability, int Quantity);

    readonly List<Element[]> _records = new();

    /// <summary>The record for an <c>equip_offset</c> (from 1), or null.</summary>
    public Element[]? this[int offset] =>
        offset >= 1 && offset <= _records.Count ? _records[offset - 1] : null;

    /// <summary>
    /// Exult <c>data/bg/shape_info.txt</c> <c>monster_food</c>: the food frame a
    /// monster carries (chicken, beef, venison, ...); -1, any frame, for the rest.
    /// </summary>
    static readonly Dictionary<int, int> FoodFrames = new()
    {
        [498] = 10, [500] = 9, [502] = 14, [509] = 12, [811] = 9, [970] = 8, [727] = 23, [329] = 11
    };

    public static int FoodFrame(int monsterShape) => FoodFrames.GetValueOrDefault(monsterShape, -1);

    public static MonsterEquipment Load()
    {
        var table = new MonsterEquipment();
        var path = Path.Combine(U7Paths.StaticDir, "EQUIP.DAT");
        if (!File.Exists(path))
        {
            return table;
        }

        var data = File.ReadAllBytes(path);
        if (data.Length == 0)
        {
            return table;
        }

        // Exult Read_count: one byte, or 255 and then two.
        var pos = 0;
        int count = data[pos++];
        if (count == 255 && data.Length >= 3)
        {
            count = data[pos] | (data[pos + 1] << 8);
            pos += 2;
        }

        for (var i = 0; i < count && pos + 60 <= data.Length; i++)
        {
            var rec = new Element[10];
            for (var e = 0; e < 10; e++, pos += 6)
            {
                rec[e] = new Element(data[pos] | (data[pos + 1] << 8), data[pos + 2], data[pos + 3]);
            }

            table._records.Add(rec);
        }

        return table;
    }
}
