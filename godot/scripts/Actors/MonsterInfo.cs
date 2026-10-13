using U7.Data;

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
    /// <summary>Exult <c>Monster_info::see_invisible</c> (bit 7 of its flags).</summary>
    public bool SeeInvisible;
    /// <summary>Exult <c>Monster_info</c>'s immunities to the sleep, charm, curse, paralysis and poison flags, and to all of them (power).</summary>
    public bool SleepSafe, CharmSafe, CurseSafe, ParalysisSafe, PoisonSafe, PowerSafe;
    /// <summary>Exult <c>m_death_safe</c> (Death Bolt can't kill it), <c>m_splits</c> (slimes) and <c>m_int_b1</c> (unknown, but worth experience).</summary>
    public bool DeathSafe, Splits, IntB1;
    /// <summary>Exult <c>Monster_info::flags</c>: fly 0, swim 1, walk 2, ethereal 3, start invisible 6, see invisible 7.</summary>
    public int Flags;

    /// <summary>Exult <c>Monster_info::get_base_xp_value</c> ("this formula is exact").</summary>
    public int BaseXpValue =>
        Armor + Weapon + (SleepSafe ? 1 : 0) + (CharmSafe ? 1 : 0) + (CurseSafe ? 1 : 0) + (ParalysisSafe ? 1 : 0) +
        (PoisonSafe ? 1 : 0) + (IntB1 ? 1 : 0) + (DeathSafe ? 1 : 0) + (PowerSafe ? 8 : 0) + ((Flags & 1) != 0 ? 1 : 0) +
        ((Flags & 2) != 0 ? 1 : 0) + ((Flags & 8) != 0 ? 2 : 0) + ((Flags & 32) != 0 ? 2 : 0) + ((Flags & 128) != 0 ? 2 : 0) +
        ((Flags & 64) != 0 ? 8 : 0) + (Splits ? 2 : 0) + (Reach > 5 ? 2 : Reach > 3 ? 1 : 0);
    /// <summary>Exult <c>Monster_info::cant_bleed</c> (MONSTERS.DAT): no blood from its wounds.</summary>
    public bool CantBleed;
    /// <summary>Exult <c>Monster_actor::create</c>: the fly/walk/swim/ethereal type flags it gets.</summary>
    public int MoveFlags = U7.Data.MoveFlags.Walk;
    /// <summary>
    /// Exult <c>m_attackmode</c>: the kind of fighter (0 noncombatant,
    /// opportunist, unpredictable, tactician, 4 berserker), which picks its
    /// attack mode when it is made.
    /// </summary>
    public int AttackModeClass = 2;
    /// <summary>Exult <c>equip_offset</c>: its EQUIP.DAT record, from 1; 0 for none.</summary>
    public int EquipOffset;
}

/// <summary>
/// Exult <c>Shape_info</c>'s actor flags (<c>shape_info.txt</c>'s
/// actor_flags): who can teleport, summon or turn invisible in combat
/// (mages, liches, ghosts, dragons, ...) and who survives Armageddon.
/// </summary>
public static class ActorFlags
{
    public const int ColdImmune = 0, DoesntEat = 1, Teleports = 2, Summons = 3, TurnsInvisible = 4, ArmageddonSafe = 5,
        QuakeWalk = 6, Goblin = 7;

    static bool Has(int shape, int flag) => (ShapeCatalog.Current[shape].ActorFlags & (1 << flag)) != 0;

    public static bool CanTeleport(int shape) => Has(shape, Teleports);
    public static bool CanSummon(int shape) => Has(shape, Summons);
    public static bool CanBeInvisible(int shape) => Has(shape, TurnsInvisible);

    /// <summary>Exult <c>survives_armageddon</c> (<c>armageddon_safe</c>): Black Gate's Batlin (403, 482) and Lord British (466).</summary>
    public static bool SurvivesArmageddon(int shape) => Has(shape, ArmageddonSafe);
}

/// <summary>MONSTERS.DAT (Exult <c>Monster_info</c>), a mod's over it.</summary>
public sealed class MonsterTable
{
    readonly Dictionary<int, MonsterRecord> _byShape = new();

    public static MonsterRecord Default { get; } = new();

    public MonsterRecord this[int shape] =>
        _byShape.TryGetValue(shape, out var rec) ? rec : Default;

    public bool Contains(int shape) => _byShape.ContainsKey(shape);

    /// <summary>Exult <c>Monster_info::read</c>, entry by entry of MONSTERS.DAT.</summary>
    public static MonsterTable Load()
    {
        var table = new MonsterTable();
        foreach (var (shape, b) in ShapeDataFile.Read("MONSTERS.DAT", 25))
        {
            if (b is null)
            {
                continue;
            }

            var flags = b[7];
            var attackMode = b[11] & 7;
            // Exult read_info: MONSTERS.DAT's own teleport, summon and invisible bits move to the shape's actor flags.
            var extra = b[13];
            if (shape < ShapeCatalog.Current.Shapes.Length)
            {
                ShapeCatalog.Current[shape].ActorFlags |= ((extra & 1) != 0 ? 1 << ActorFlags.Teleports : 0) |
                                                           ((extra & 2) != 0 ? 1 << ActorFlags.Summons : 0) |
                                                           ((extra & 4) != 0 ? 1 << ActorFlags.TurnsInvisible : 0);
            }

            table._byShape[shape] = new MonsterRecord
            {
                Shape = shape,
                SleepSafe = (b[0] & 1) != 0,
                CharmSafe = (b[0] & 2) != 0,
                Strength = b[0] >> 2,
                CurseSafe = (b[1] & 1) != 0,
                ParalysisSafe = (b[1] & 2) != 0,
                Dexterity = b[1] >> 2,
                PoisonSafe = (b[2] & 1) != 0,
                IntB1 = (b[2] & 2) != 0,
                Intelligence = b[2] >> 2,
                Alignment = b[3] & 3,
                Combat = b[3] >> 2,
                Splits = (b[4] & 1) != 0,
                CantDie = (b[4] & 2) != 0,
                PowerSafe = (b[4] & 4) != 0,
                DeathSafe = (b[4] & 8) != 0,
                Armor = b[4] >> 4,
                Reach = Math.Max(1, b[6] & 15),
                Weapon = b[6] >> 4,
                Flags = flags,
                NoBody = (flags & (1 << 4)) != 0,
                SeeInvisible = (flags & (1 << 7)) != 0,
                MoveFlags = MoveFlagsOf(flags),
                Vulnerable = b[8],
                Immune = b[9],
                CantYell = (b[10] & (1 << 5)) != 0,
                CantBleed = (b[10] & (1 << 6)) != 0,
                // Exult: 0 is invalid data from old ES versions, read as 2; else one less.
                AttackModeClass = attackMode == 0 ? 2 : attackMode - 1,
                EquipOffset = b[12]
            };
        }

        return table;
    }

    /// <summary>Exult <c>Monster_info::Monster_flags</c> fly 0, swim 1, walk 2, ethereal 3 as the movement flags.</summary>
    static int MoveFlagsOf(int flags) =>
        ((flags & 1) != 0 ? U7.Data.MoveFlags.Fly : 0) | ((flags & 2) != 0 ? U7.Data.MoveFlags.Swim : 0) |
        ((flags & 4) != 0 ? U7.Data.MoveFlags.Walk : 0) | ((flags & 8) != 0 ? U7.Data.MoveFlags.Ethereal : 0);
}
