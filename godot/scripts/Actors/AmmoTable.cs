using U7.Data;

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

/// <summary>AMMO.DAT (Exult <c>Ammo_info</c>), a mod's over it.</summary>
public sealed class AmmoTable
{
    readonly Dictionary<int, AmmoRecord> _byShape = new();

    public AmmoRecord? this[int shape] =>
        _byShape.TryGetValue(shape, out var rec) ? rec : null;

    /// <summary>Exult <c>In_ammo_family</c>.</summary>
    public bool InFamily(int shape, int family) =>
        shape == family || (this[shape] is { } a && a.Family == family);

    /// <summary>Exult <c>Ammo_info::read</c>, entry by entry of AMMO.DAT.</summary>
    public static AmmoTable Load()
    {
        var table = new AmmoTable();
        foreach (var (shape, b) in ShapeDataFile.Read("AMMO.DAT", 13))
        {
            if (b is null)
            {
                continue;
            }

            var flags0 = b[5];
            var homing = ((flags0 >> 4) & 3) == 3;
            table._byShape[shape] = new AmmoRecord
            {
                Shape = shape,
                Family = BitConverter.ToUInt16(b, 0),
                Sprite = BitConverter.ToInt16(b, 2),
                Damage = b[4],
                Lucky = (flags0 & 1) != 0,
                Autohit = ((flags0 >> 1) & 1) != 0,
                Returns = ((flags0 >> 2) & 1) != 0,
                NoBlocking = ((flags0 >> 3) & 1) != 0,
                Homing = homing,
                DropType = homing ? 0 : (flags0 >> 4) & 3,
                Explodes = ((flags0 >> 6) & 1) != 0,
                DamageType = (b[7] >> 4) & 15,
                Powers = b[8]
            };
        }

        return table;
    }
}
