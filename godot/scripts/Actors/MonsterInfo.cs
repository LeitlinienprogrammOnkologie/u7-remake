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
    /// <summary>Exult <c>Monster_info::see_invisible</c> (bit 7 of its flags).</summary>
    public bool SeeInvisible;
    /// <summary>Exult <c>Monster_info</c>'s immunities to the sleep, charm, curse, paralysis and poison flags, and to all of them (power).</summary>
    public bool SleepSafe, CharmSafe, CurseSafe, ParalysisSafe, PoisonSafe, PowerSafe;
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
/// Exult <c>data/bg/shape_info.txt</c> <c>actor_flags</c>, which Exult
/// hard-codes for Black Gate: who can teleport, summon or turn invisible in
/// combat (mages, liches, ghosts, dragons, ...).
/// </summary>
public static class ActorFlags
{
    // shape: teleports, summons, turn_invis
    static readonly Dictionary<int, (bool Teleports, bool Summons, bool TurnInvis)> Flags = new()
    {
        [154] = (true, true, true), [445] = (true, true, true), [446] = (true, true, true), // Mages.
        [299] = (false, true, true), [317] = (false, true, true), // Ghosts.
        [354] = (true, true, true), [519] = (true, true, true), // Liches.
        [504] = (false, false, true), [511] = (false, false, true), // Dragons.
        [382] = (false, true, false), [534] = (false, true, false)
    };

    public static bool CanTeleport(int shape) => Flags.TryGetValue(shape, out var f) && f.Teleports;
    public static bool CanSummon(int shape) => Flags.TryGetValue(shape, out var f) && f.Summons;
    public static bool CanBeInvisible(int shape) => Flags.TryGetValue(shape, out var f) && f.TurnInvis;

    /// <summary>Exult <c>survives_armageddon</c> (<c>armageddon_safe</c>): Batlin (403, 482) and Lord British (466).</summary>
    public static bool SurvivesArmageddon(int shape) => shape is 403 or 482 or 466;
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
        var iFlagBits = Col("flags");
        var iMode = Col("attack_mode");
        var iEquip = Col("equip_offset");
        var iSleepSafe = Col("sleep_safe");
        var iCharmSafe = Col("charm_safe");
        var iCurseSafe = Col("curse_safe");
        var iParalysisSafe = Col("paralysis_safe");
        var iPoisonSafe = Col("poison_safe");
        var iPowerSafe = Col("power_safe");

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
                SeeInvisible = (Num(c, iFlagBits) & (1 << 7)) != 0,
                SleepSafe = Num(c, iSleepSafe) != 0,
                CharmSafe = Num(c, iCharmSafe) != 0,
                CurseSafe = Num(c, iCurseSafe) != 0,
                ParalysisSafe = Num(c, iParalysisSafe) != 0,
                PoisonSafe = Num(c, iPoisonSafe) != 0,
                PowerSafe = Num(c, iPowerSafe) != 0,
                MoveFlags = ParseMoveFlags(Get(c, iFlags)),
                AttackModeClass = Math.Clamp(Num(c, iMode, 2), 0, 4),
                EquipOffset = Num(c, iEquip)
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
