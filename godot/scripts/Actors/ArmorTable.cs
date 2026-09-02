using System.Globalization;
using System.IO;
using U7.Core;

namespace U7.Actors;

/// <summary>One <c>armor.dat</c> row (Exult <c>Armor_info</c>).</summary>
public sealed class ArmorRecord
{
    public int Shape;
    public int Protection;
    public int Immunity;
}

/// <summary>Loads <c>assets/data/armor.csv</c>.</summary>
public sealed class ArmorTable
{
    readonly Dictionary<int, ArmorRecord> _byShape = new();

    public ArmorRecord? this[int shape] =>
        _byShape.TryGetValue(shape, out var rec) ? rec : null;

    public static ArmorTable Load()
    {
        var table = new ArmorTable();
        var path = Path.Combine(U7Paths.DataDir, "armor.csv");
        if (!File.Exists(path))
        {
            return table;
        }

        using var reader = new StreamReader(path);
        var header = reader.ReadLine();
        if (header is null)
        {
            return table;
        }

        var cols = header.Split(',');
        int Col(string name)
        {
            for (var i = 0; i < cols.Length; i++)
            {
                if (cols[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        var iShape = Col("shape");
        var iProt = Col("protection");
        var iImm = Col("immunity");

        string Get(string[] c, int i) => (uint)i < (uint)c.Length ? c[i] : "";
        int Num(string[] c, int i, int fallback = 0) =>
            int.TryParse(Get(c, i), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
                ? v
                : fallback;

        while (reader.ReadLine() is { } line)
        {
            var c = line.Split(',');
            var shape = Num(c, iShape, -1);
            if (shape < 0)
            {
                continue;
            }

            table._byShape[shape] = new ArmorRecord
            {
                Shape = shape,
                Protection = Math.Max(0, Num(c, iProt)),
                Immunity = Num(c, iImm)
            };
        }

        return table;
    }
}
