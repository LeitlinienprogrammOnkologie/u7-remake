using System.Globalization;
using System.IO;
using U7.Core;

namespace U7.Actors;

/// <summary>One <c>weapons.dat</c> row (Exult <c>Weapon_info</c>).</summary>
public sealed class WeaponRecord
{
    public int Shape;
    public int Damage = 1;
    public int Range = 3;
    public bool Lucky;
    public bool Autohit;
    public bool Melee = true;
}

/// <summary>Loads <c>assets/data/weapons.csv</c>.</summary>
public sealed class WeaponTable
{
    readonly Dictionary<int, WeaponRecord> _byShape = new();

    public WeaponRecord? this[int shape] =>
        _byShape.TryGetValue(shape, out var rec) ? rec : null;

    public static WeaponTable Load()
    {
        var table = new WeaponTable();
        var path = Path.Combine(U7Paths.DataDir, "weapons.csv");
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
        var iDmg = Col("damage");
        var iRange = Col("range");
        var iLucky = Col("lucky");
        var iAuto = Col("autohit");
        var iUses = Col("uses_name");

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

            var uses = Get(c, iUses);
            table._byShape[shape] = new WeaponRecord
            {
                Shape = shape,
                Damage = Math.Max(0, Num(c, iDmg, 1)),
                Range = Math.Max(1, Num(c, iRange, 3)),
                Lucky = Num(c, iLucky) != 0,
                Autohit = Num(c, iAuto) != 0,
                Melee = uses.Contains("melee", StringComparison.OrdinalIgnoreCase) ||
                        string.IsNullOrEmpty(uses)
            };
        }

        return table;
    }
}
