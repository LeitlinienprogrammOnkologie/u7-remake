using System.IO;

namespace U7.Game;

/// <summary>
/// Exult <c>gamewin.dat</c>: the clock, the music, armageddon and combat.
/// The view's scroll, ambient light and infravision are written as zero
/// (the port centres the view on the avatar and has neither of the others).
/// </summary>
/// <param name="EggCount">Exult <c>set_egg_count(repeat >> 16)</c>: the continuous jukebox eggs holding the track.</param>
public readonly record struct GwinState(int Day, int Hour, int Minute, bool InCombat, int Track, bool Repeat, int EggCount,
    int SpecialLight, bool Armageddon)
{
    /// <summary>Exult <c>Game_window::write_gwin</c>.</summary>
    public void Write(BinaryWriter w)
    {
        w.Write((ushort)0); // scrolltx
        w.Write((ushort)0); // scrollty
        w.Write((ushort)Day);
        w.Write((ushort)Hour);
        w.Write((ushort)Minute);
        w.Write((uint)SpecialLight);
        w.Write(unchecked((uint)Track));
        w.Write((Repeat ? 1u : 0u) | ((uint)EggCount << 16));
        w.Write((byte)(Armageddon ? 1 : 0));
        w.Write((byte)0); // ambient light
        w.Write((byte)(InCombat ? 1 : 0));
        w.Write((byte)0); // infravision
    }

    /// <summary>
    /// Exult <c>Game_window::read_gwin</c> (the parts we keep): an older save
    /// ends early, and what it lacks keeps its default. Throws when even the
    /// clock is cut short, as Exult does.
    /// </summary>
    public static GwinState Read(BinaryReader r)
    {
        r.ReadUInt16(); // scrolltx
        r.ReadUInt16(); // scrollty
        int day = r.ReadUInt16();
        int hour = r.ReadUInt16();
        int minute = r.ReadUInt16();
        if (!Has(r, 4))
        {
            return new GwinState(day, hour, minute, false, -1, false, 0, 0, false);
        }

        var light = (int)r.ReadUInt32();
        if (!Has(r, 8))
        {
            return new GwinState(day, hour, minute, false, -1, false, 0, light, false);
        }

        var track = unchecked((int)r.ReadUInt32());
        var repeat = r.ReadUInt32();
        var armageddon = Has(r, 1) && r.ReadByte() == 1;
        if (Has(r, 1))
        {
            r.ReadByte(); // ambient light
        }

        var combat = Has(r, 1) && r.ReadByte() == 1;
        return new GwinState(day, hour, minute, combat, track, (repeat & 1) != 0, (int)(repeat >> 16), light, armageddon);
    }

    static bool Has(BinaryReader r, int bytes) => r.BaseStream.Length - r.BaseStream.Position >= bytes;
}
