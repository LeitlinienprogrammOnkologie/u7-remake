using System.IO;
using U7.Core;

namespace U7.Usecode;

/// <summary>
/// Exult <c>usecode.dat</c> (<c>Usecode_internal::write</c> / <c>read</c>):
/// the party in the order it joined, the usecode timers (game hour each was
/// set) and a saved position. Its companion <c>usecode.var</c> holds usecode
/// statics, which only Exult-compiled usecode has; Black Gate's never uses
/// them, so it is written empty.
/// </summary>
public sealed class UsecodeDat
{
    /// <summary>Exult <c>EXULT_PARTY_MAX</c>.</summary>
    public const int PartyMax = 8;
    public const string FileName = "USECODE.DAT";
    public const string VarsFileName = "USECODE.VAR";

    /// <summary>Party members' NPC numbers, in party order.</summary>
    public List<int> Party { get; } = new();
    public Dictionary<int, int> Timers { get; } = new();
    /// <summary>Exult <c>saved_pos</c> / <c>saved_map</c> (kept for Exult; Black Gate never sets them).</summary>
    public TileCoord SavedPos { get; private set; } = new(-1, -1, -1);
    public int SavedMap { get; private set; } = -1;

    /// <summary>Read from the saved game, or null when there is none (a new game, or a save made before it was written).</summary>
    public static UsecodeDat? Read()
    {
        var data = U7Paths.ReadGameDat(FileName);
        if (data is null)
        {
            return null;
        }

        var dat = new UsecodeDat();
        try
        {
            using var r = new BinaryReader(new MemoryStream(data));
            int count = r.ReadUInt16();
            for (var i = 0; i < PartyMax; i++)
            {
                int member = r.ReadUInt16();
                if (i < count)
                {
                    dat.Party.Add(member);
                }
            }

            var cnt = r.ReadInt32();
            if (cnt == -1)
            {
                int tmr;
                while ((tmr = r.ReadUInt16()) != 0xffff)
                {
                    dat.Timers[tmr] = r.ReadInt32();
                }
            }
            else
            {
                // Older Exult saves: timers 0-19 in a row.
                dat.Timers[0] = cnt;
                for (var t = 1; t < 20; t++)
                {
                    dat.Timers[t] = r.ReadInt32();
                }
            }

            var pos = new TileCoord(r.ReadUInt16(), r.ReadUInt16(), r.ReadUInt16());
            dat.SavedPos = pos.Tz is < 0 or > 13 ? new TileCoord(-1, -1, -1) : pos;
            dat.SavedMap = r.BaseStream.Position + 2 <= r.BaseStream.Length ? r.ReadInt16() : -1;
        }
        catch (EndOfStreamException)
        {
            Godot.GD.Print($"{FileName} is short");
        }

        return dat;
    }

    /// <summary>Exult <c>Usecode_internal::write</c>: <c>usecode.dat</c> and an empty <c>usecode.var</c>.</summary>
    public static void Write(string dir, IReadOnlyList<int> party, IReadOnlyDictionary<int, int> timers)
    {
        using (var w = new BinaryWriter(File.Create(Path.Combine(dir, FileName))))
        {
            w.Write((ushort)party.Count);
            for (var i = 0; i < PartyMax; i++)
            {
                w.Write((ushort)(i < party.Count ? party[i] : 0));
            }

            w.Write(0xffffffffu);
            foreach (var (tnum, hours) in timers)
            {
                if (hours != 0) // Exult skips unused timers.
                {
                    w.Write((ushort)tnum);
                    w.Write(hours);
                }
            }

            w.Write((ushort)0xffff);
            w.Write((short)-1); // saved position
            w.Write((short)-1);
            w.Write((short)-1);
            w.Write((short)-1); // saved map
        }

        using (var w = new BinaryWriter(File.Create(Path.Combine(dir, VarsFileName))))
        {
            w.Write(0); // global statics
            w.Write(0xffffffffu); // no function statics
        }
    }
}
