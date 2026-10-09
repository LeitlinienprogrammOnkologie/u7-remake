using System.Globalization;
using System.IO;
using U7.Core;

namespace U7.Actors;

/// <summary>One <c>ammo.dat</c> row (Exult <c>Ammo_info</c>).</summary>
public sealed class AmmoRecord
{
    public const int DropNormally = 0, NeverDrop = 1, AlwaysDrop = 2;

    public int Shape;
    public int Family;
    /// <summary>Sprite to fly with: -1 none, -3 the ammo/weapon itself.</summary>
    public int Sprite = -1;
    public int Damage;
    public int DamageType;
    public bool Lucky;
    public bool Autohit;
    public bool Returns;
    public bool NoBlocking;
    public bool Explodes;
    /// <summary>Exult <c>is_homing</c>: an exploding missile that becomes a <see cref="HomingMissile"/> (energy mist, death vortex).</summary>
    public bool Homing;
    public int DropType;
    /// <summary>Exult <c>Weapon_data::Powers</c> bits: sleep 1, charm 2, curse 4, poison 8, paralyze 16, magebane 32, no damage 128.</summary>
    public int Powers;

    /// <summary>Exult <c>Ammo_info::get_base_strength</c> (its own "utter guesses"), for <c>find_best_ammo</c>.</summary>
    public int BaseStrength
    {
        get
        {
            var strength = Damage;
            strength += (Powers & 128) != 0 ? 10 : 0; // no damage
            strength += (Powers & 1) != 0 ? 10 : 0; // sleep
            strength += (Powers & 16) != 0 ? 10 : 0; // paralyze
            strength += (Powers & 2) != 0 ? 10 : 0; // charm
            strength += (Powers & 8) != 0 ? 5 : 0; // poison
            strength += (Powers & 4) != 0 ? 5 : 0; // curse
            strength += (Powers & 32) != 0 ? 5 : 0; // magebane
            strength += Lucky ? 5 : 0;
            strength += DamageType != 0 ? 5 : 0;
            if (Autohit)
            {
                strength *= 2;
            }

            if (NoBlocking)
            {
                strength *= 2;
            }

            return strength;
        }
    }
}

/// <summary>Loads <c>assets/data/ammo.csv</c>.</summary>
public sealed class AmmoTable
{
    readonly Dictionary<int, AmmoRecord> _byShape = new();

    public AmmoRecord? this[int shape] =>
        _byShape.TryGetValue(shape, out var rec) ? rec : null;

    /// <summary>Exult <c>In_ammo_family</c>.</summary>
    public bool InFamily(int shape, int family) =>
        shape == family || (this[shape] is { } a && a.Family == family);

    public static AmmoTable Load()
    {
        var table = new AmmoTable();
        var path = Path.Combine(U7Paths.DataDir, "ammo.csv");
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
        var iFamily = Col("family_shape");
        var iSprite = Col("sprite_shape");
        var iDmg = Col("damage");
        var iType = Col("damage_type");
        var iLucky = Col("lucky");
        var iAuto = Col("autohit");
        var iReturns = Col("returns");
        var iNoBlock = Col("no_blocking");
        var iDrop = Col("drop_type");
        var iExplodes = Col("explodes");
        var iHoming = Col("homing");
        var iPowers = Col("powers");

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

            table._byShape[shape] = new AmmoRecord
            {
                Shape = shape,
                Family = Num(c, iFamily, shape),
                Sprite = Num(c, iSprite, -1),
                Damage = Num(c, iDmg),
                DamageType = Num(c, iType),
                Lucky = Num(c, iLucky) != 0,
                Autohit = Num(c, iAuto) != 0,
                Returns = Num(c, iReturns) != 0,
                NoBlocking = Num(c, iNoBlock) != 0,
                DropType = Num(c, iDrop),
                Explodes = Num(c, iExplodes) != 0,
                Homing = Num(c, iHoming) != 0,
                Powers = Num(c, iPowers)
            };
        }

        return table;
    }
}
