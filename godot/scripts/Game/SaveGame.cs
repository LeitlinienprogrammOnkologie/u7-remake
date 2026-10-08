using System.IO;
using Godot;
using U7.Actors;
using U7.Audio;
using U7.Core;
using U7.Data;
using U7.Usecode;
using U7.World;

namespace U7.Game;

/// <summary>
/// Saved games in Exult's GAMEDAT layout: a directory with U7IREGxx, NPC.DAT,
/// MONSNPCS.DAT, FLAGINIT, GAMEWIN.DAT, USECODE.DAT and USECODE.VAR. Loading
/// points <see cref="U7Paths.GameDatOverride"/> at the directory and restarts
/// the scene, so the normal loaders read it.
/// </summary>
public static class SaveGame
{
    public const string QuickSlot = "quick";

    public static string SlotDir(string slot) => Path.Combine(U7Paths.SavesDir, slot);

    public static bool Exists(string slot) => File.Exists(Path.Combine(SlotDir(slot), "NPC.DAT"));

    public static void Write(string slot, GameMap map, List<U7Object?> npcs, UsecodeMachine? usecode,
        GameClock clock, bool inCombat, MusicPlayer? music, IEnumerable<U7Object>? monsters = null, bool armageddon = false)
    {
        var dir = SlotDir(slot);
        Directory.CreateDirectory(dir);
        foreach (var old in Directory.GetFiles(dir, "U7IREG*"))
        {
            File.Delete(old);
        }

        map.WriteIregFiles(dir);
        NpcDat.Save(dir, npcs, map);
        NpcDat.SaveMonsters(dir, monsters ?? Array.Empty<U7Object>(), map);
        if (usecode is not null)
        {
            File.WriteAllBytes(Path.Combine(dir, "FLAGINIT"), usecode.GFlags);
            UsecodeDat.Write(dir, usecode.Party?.Members.Select(m => m.NpcNum).ToList() ?? [], usecode.Timers);
        }

        File.Delete(Path.Combine(dir, OldGwinName));
        WriteGwin(Path.Combine(dir, GwinName), clock, inCombat, music, armageddon);
        var identity = Path.Combine(U7Paths.RepoRoot, "u7", "GAMEDAT", "IDENTITY");
        if (File.Exists(identity))
        {
            File.Copy(identity, Path.Combine(dir, "IDENTITY"), overwrite: true);
        }

        GD.Print($"saved to {dir}");
    }

    /// <summary>Exult <c>gamewin.dat</c>; saves made before this used the name GWIN.DAT.</summary>
    const string GwinName = "GAMEWIN.DAT";
    const string OldGwinName = "GWIN.DAT";

    /// <summary>Exult <c>Game_window::write_gwin</c>.</summary>
    static void WriteGwin(string path, GameClock clock, bool inCombat, MusicPlayer? music, bool armageddon)
    {
        using var w = new BinaryWriter(File.Create(path));
        w.Write((ushort)0); // scrolltx
        w.Write((ushort)0); // scrollty
        w.Write((ushort)clock.Day);
        w.Write((ushort)clock.Hour);
        w.Write((ushort)clock.Minute);
        w.Write((uint)clock.SpecialLight);
        var track = music?.CurrentTrack ?? -1;
        w.Write(unchecked((uint)track));
        w.Write((uint)((music is { Repeat: true } ? 1u : 0u) | ((uint)(music?.EggCount ?? 0) << 16)));
        w.Write((byte)(armageddon ? 1 : 0));
        w.Write((byte)0); // ambient light
        w.Write((byte)(inCombat ? 1 : 0));
        w.Write((byte)0); // infravision
    }

    public readonly record struct GwinState(int Day, int Hour, int Minute, bool InCombat, int Track, bool Repeat, int SpecialLight, bool Armageddon);

    /// <summary>Exult <c>Game_window::read_gwin</c> (the parts we keep).</summary>
    public static GwinState? ReadGwin()
    {
        var data = U7Paths.ReadGameDat(GwinName) ?? U7Paths.ReadGameDat(OldGwinName);
        if (data is null)
        {
            return null;
        }

        try
        {
            using var r = new BinaryReader(new MemoryStream(data));
            r.ReadUInt16();
            r.ReadUInt16();
            int day = r.ReadUInt16();
            int hour = r.ReadUInt16();
            int minute = r.ReadUInt16();
            var track = -1;
            var repeat = false;
            var combat = false;
            var armageddon = false;
            var light = 0;
            if (r.BaseStream.Length - r.BaseStream.Position >= 12)
            {
                light = (int)r.ReadUInt32();
                track = unchecked((int)r.ReadUInt32());
                repeat = (r.ReadUInt32() & 1) != 0;
                if (r.BaseStream.Length - r.BaseStream.Position >= 3)
                {
                    armageddon = r.ReadByte() == 1;
                    r.ReadByte();
                    combat = r.ReadByte() != 0;
                }
            }

            return new GwinState(day, hour, minute, combat, track, repeat, light, armageddon);
        }
        catch (Exception ex)
        {
            GD.Print($"{GwinName} unreadable: {ex.Message}");
            return null;
        }
    }
}
