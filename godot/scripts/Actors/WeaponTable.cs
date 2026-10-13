using U7.Data;

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
    /// <summary>Exult <c>Weapon_data::Powers</c> bits: sleep 1, charm 2, curse 4, poison 8, paralyze 16, magebane 32, no damage 128.</summary>
    public int Powers;

    public bool UsesCharges => Ammo == -2;

    /// <summary>Exult <c>Weapon_info::get_base_xp_value</c> ("this formula is exact").</summary>
    public int BaseXpValue =>
        Damage + 2 * System.Numerics.BitOperations.PopCount((uint)Powers) + (Explodes ? 1 : 0) + (Autohit ? 10 : 0) + (NoBlocking ? 1 : 0);

    /// <summary>Exult <c>Weapon_info::get_base_strength</c> (its own "utter guesses"), for <c>ready_best_weapon</c>.</summary>
    public int BaseStrength
    {
        get
        {
            if (Explodes && Uses == UsesMelee)
            {
                return -50; // Avoid hand-held explosives at all costs.
            }

            if (Usecode == 0x689)
            {
                return 5000; // Causes death in BG.
            }

            if (Explodes)
            {
                return 3000;
            }

            var strength = (Powers & 128) != 0 ? 0 : Damage;
            strength += (Powers & 1) != 0 ? 25 : 0; // sleep
            strength += (Powers & 16) != 0 ? 25 : 0; // paralyze
            strength += (Powers & 2) != 0 ? 20 : 0; // charm
            strength += (Powers & 8) != 0 ? 10 : 0; // poison
            strength += (Powers & 4) != 0 ? 5 : 0; // curse
            strength += Lucky ? 5 : 0;
            strength += DamageType != 0 ? 10 : 0;
            if (Autohit)
            {
                strength *= 2;
            }

            return strength;
        }
    }
}

/// <summary>WEAPONS.DAT (Exult <c>Weapon_info</c>), a mod's over it.</summary>
public sealed class WeaponTable
{
    readonly Dictionary<int, WeaponRecord> _byShape = new();

    public WeaponRecord? this[int shape] =>
        _byShape.TryGetValue(shape, out var rec) ? rec : null;

    /// <summary>Exult <c>Weapon_info::read</c>, entry by entry of WEAPONS.DAT.</summary>
    public static WeaponTable Load()
    {
        var table = new WeaponTable();
        foreach (var (shape, b) in ShapeDataFile.Read("WEAPONS.DAT", 21))
        {
            if (b is null)
            {
                continue;
            }

            var (flags0, range, flags1, flags2) = (b[5], b[6], b[7], b[8]);
            var speed = (flags2 >> 5) & 7;
            var uses = (range >> 1) & 3;
            table._byShape[shape] = new WeaponRecord
            {
                Shape = shape,
                Ammo = BitConverter.ToInt16(b, 0),
                Projectile = BitConverter.ToInt16(b, 2),
                Damage = b[4],
                Lucky = (flags0 & 1) != 0,
                Explodes = ((flags0 >> 1) & 1) != 0,
                NoBlocking = ((flags0 >> 2) & 1) != 0,
                DeleteDepleted = ((flags0 >> 3) & 1) != 0,
                DamageType = (flags0 >> 4) & 15,
                Autohit = (range & 1) != 0,
                Uses = uses,
                Melee = uses == WeaponRecord.UsesMelee,
                Range = Math.Max(1, range >> 3),
                Returns = (flags1 & 1) != 0,
                MissileSpeed = ((flags1 >> 2) & 3) != 0 ? 4 : speed == 0 ? 3 : speed < 3 ? 2 : 1,
                RotationSpeed = (flags1 >> 4) & 15,
                ActorFrames = flags2 & 15,
                Powers = b[9],
                Usecode = BitConverter.ToUInt16(b, 11)
            };
        }

        return table;
    }
}
