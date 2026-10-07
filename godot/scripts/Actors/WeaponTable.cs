using System.Globalization;
using System.IO;
using U7.Core;

namespace U7.Actors;

/// <summary>One <c>weapons.dat</c> row (Exult <c>Weapon_info</c>).</summary>
public sealed class WeaponRecord
{
    public const int UsesMelee = 0, UsesPoorThrown = 1, UsesGoodThrown = 2, UsesRanged = 3;

    public int Shape;
    public int Damage = 1;
    public int DamageType;
    public int Range = 3;
    public bool Lucky;
    public bool Autohit;
    public bool Melee = true;
    /// <summary>Exult <c>Weapon_info::uses</c>: 0 melee, 1 poor thrown, 2 good thrown, 3 ranged.</summary>
    public int Uses;
    /// <summary>Exult <c>ammo</c>: -1 none, -2 charges (quality), -3 the weapon itself, else ammo family shape.</summary>
    public int Ammo = -1;
    /// <summary>Projectile shape, or -1; -3 = the weapon itself.</summary>
    public int Projectile = -1;
    public bool Returns;
    public bool Explodes;
    public bool NoBlocking;
    public bool DeleteDepleted;
    public int MissileSpeed = 4;
    public int RotationSpeed;
    /// <summary>Exult <c>actor_frames</c>: the attack frames, 0 reach, 1 raise, 2 fast swing, 3 slow swing; bits 2-3 when shooting or throwing.</summary>
    public int ActorFrames = 2;
    /// <summary>Exult <c>Weapon_info::usecode</c>: run on whatever it hits, with the weapon event (0: none).</summary>
    public int Usecode;

    public bool UsesCharges => Ammo == -2;
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
        var iType = Col("damage_type");
        var iRange = Col("range");
        var iLucky = Col("lucky");
        var iAuto = Col("autohit");
        var iUses = Col("uses_name");
        var iUsesNum = Col("uses");
        var iAmmo = Col("ammo");
        var iProj = Col("projectile");
        var iReturns = Col("returns");
        var iExplodes = Col("explodes");
        var iNoBlock = Col("no_blocking");
        var iDelete = Col("delete_depleted");
        var iSpeed = Col("missile_speed");
        var iRot = Col("rotation_speed");
        var iFrames = Col("actor_frames");
        var iUsecode = Col("usecode");

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
            var usesNum = Num(c, iUsesNum, 0);
            table._byShape[shape] = new WeaponRecord
            {
                Shape = shape,
                Damage = Math.Max(0, Num(c, iDmg, 1)),
                DamageType = Num(c, iType),
                Range = Math.Max(1, Num(c, iRange, 3)),
                Lucky = Num(c, iLucky) != 0,
                Autohit = Num(c, iAuto) != 0,
                Melee = usesNum == 0 || uses.Contains("melee", StringComparison.OrdinalIgnoreCase) ||
                        string.IsNullOrEmpty(uses),
                Uses = usesNum,
                Ammo = Num(c, iAmmo, -1),
                Projectile = Num(c, iProj, -1),
                Returns = Num(c, iReturns) != 0,
                Explodes = Num(c, iExplodes) != 0,
                NoBlocking = Num(c, iNoBlock) != 0,
                DeleteDepleted = Num(c, iDelete) != 0,
                MissileSpeed = Num(c, iSpeed, 4),
                RotationSpeed = Num(c, iRot),
                ActorFrames = Num(c, iFrames, 2),
                Usecode = Num(c, iUsecode)
            };
        }

        return table;
    }
}
