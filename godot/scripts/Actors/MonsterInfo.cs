using System.Globalization;
using System.IO;
using U7.Core;

namespace U7.Actors;

/// <summary>One <c>MONSTERS.DAT</c> row (Exult <c>Monster_info</c>).</summary>
public sealed class MonsterRecord
{
    public int Shape;
    public int Strength = 10;
    public int Dexterity = 10;
    public int Intelligence = 10;
    public int Alignment;
    public int Combat = 10;
    public int Armor;
    /// <summary>Exult only reads this in the map editor; bare-hand damage is 1.</summary>
    public int Weapon;
    public int Reach = 3;
    public int Immune;
    public int Vulnerable;
    public bool CantDie;
    /// <summary>Exult <c>Monster_info::cant_yell</c>: an animal or the like, which cannot speak.</summary>
    public bool CantYell;
    public bool NoBody;
    /// <summary>Exult <c>Monster_actor::create</c>: the fly/walk/swim/ethereal type flags it gets.</summary>
    public int MoveFlags = U7.Data.MoveFlags.Walk;
}

/// <summary>Loads <c>assets/data/monsters.csv</c>.</summary>
public sealed class MonsterTable
{
    readonly Dictionary<int, MonsterRecord> _byShape = new();

    public static MonsterRecord Default { get; } = new();

    public MonsterRecord this[int shape] =>
        _byShape.TryGetValue(shape, out var rec) ? rec : Default;

    public bool Contains(int shape) => _byShape.ContainsKey(shape);

    public static MonsterTable Load()
    {
        var table = new MonsterTable();
        var path = Path.Combine(U7Paths.DataDir, "monsters.csv");
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
        var iDel = Col("deleted");
        var iStr = Col("strength");
        var iDex = Col("dexterity");
        var iInt = Col("intelligence");
        var iAlign = Col("alignment");
        var iCombat = Col("combat");
        var iArmor = Col("armor");
        var iWpn = Col("weapon");
        var iReach = Col("reach");
        var iImm = Col("immune");
        var iVuln = Col("vulnerable");
        var iCantDie = Col("cant_die");
        var iCantYell = Col("cant_yell");
        var iFlags = Col("move_flags");

        string Get(string[] c, int i) => (uint)i < (uint)c.Length ? c[i] : "";
        int Num(string[] c, int i, int fallback = 0) =>
            int.TryParse(Get(c, i), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
                ? v
                : fallback;

        while (reader.ReadLine() is { } line)
        {
            var c = line.Split(',');
            var shape = Num(c, iShape, -1);
            if (shape < 0 || Num(c, iDel) != 0)
            {
                continue;
            }

            var rec = new MonsterRecord
            {
                Shape = shape,
                Strength = Num(c, iStr, 10),
                Dexterity = Num(c, iDex, 10),
                Intelligence = Num(c, iInt, 10),
                Alignment = Num(c, iAlign),
                Combat = Num(c, iCombat, 10),
                Armor = Num(c, iArmor),
                Weapon = Num(c, iWpn),
                Reach = Math.Max(1, Num(c, iReach, 3)),
                Immune = Num(c, iImm),
                Vulnerable = Num(c, iVuln),
                CantDie = Num(c, iCantDie) != 0,
                CantYell = Num(c, iCantYell) != 0,
                NoBody = Get(c, iFlags).Contains("no_body", StringComparison.OrdinalIgnoreCase),
                MoveFlags = ParseMoveFlags(Get(c, iFlags))
            };
            table._byShape[shape] = rec;
        }

        return table;
    }

    static int ParseMoveFlags(string flags)
    {
        var result = 0;
        foreach (var f in flags.Split('|'))
        {
            result |= f switch
            {
                "fly" => U7.Data.MoveFlags.Fly,
                "walk" => U7.Data.MoveFlags.Walk,
                "swim" => U7.Data.MoveFlags.Swim,
                "ethereal" => U7.Data.MoveFlags.Ethereal,
                _ => 0
            };
        }

        return result;
    }
}
