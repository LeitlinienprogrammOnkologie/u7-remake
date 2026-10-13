using U7.Data;

namespace U7.Actors;

/// <summary>One <c>armor.dat</c> row (Exult <c>Armor_info</c>).</summary>
public sealed class ArmorRecord
{
    public int Shape;
    public int Protection;
    public int Immunity;
}

/// <summary>ARMOR.DAT (Exult <c>Armor_info</c>), a mod's over it.</summary>
public sealed class ArmorTable
{
    readonly Dictionary<int, ArmorRecord> _byShape = new();

    public ArmorRecord? this[int shape] =>
        _byShape.TryGetValue(shape, out var rec) ? rec : null;

    /// <summary>Exult <c>Armor_info::read</c>, entry by entry of ARMOR.DAT.</summary>
    public static ArmorTable Load()
    {
        var table = new ArmorTable();
        foreach (var (shape, b) in ShapeDataFile.Read("ARMOR.DAT", 10))
        {
            if (b is not null)
            {
                table._byShape[shape] = new ArmorRecord { Shape = shape, Protection = b[0], Immunity = b[2] };
            }
        }

        return table;
    }
}
