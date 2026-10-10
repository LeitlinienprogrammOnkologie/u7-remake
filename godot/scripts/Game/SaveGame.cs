using System.IO;
using Godot;
using U7.Actors;
using U7.Core;
using U7.Data;
using U7.Usecode;

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
    const string TmpSuffix = ".tmp";
    const string OldSuffix = ".old";

    public static string SlotDir(string slot) => Path.Combine(U7Paths.SavesDir, slot);

    /// <summary>Whether the slot holds a save; first puts back one cut off between <see cref="Swap"/>'s two renames.</summary>
    public static bool Exists(string slot)
    {
        var dir = SlotDir(slot);
        if (!Directory.Exists(dir) && Directory.Exists(dir + OldSuffix))
        {
            Directory.Move(dir + OldSuffix, dir);
        }

        return File.Exists(Path.Combine(dir, "NPC.DAT"));
    }

    /// <summary>
    /// Exult <c>Game_window::write</c>, into a fresh <c>slot.tmp</c> that
    /// replaces the slot only once every file is written, so a failure
    /// part-way leaves the old save whole.
    /// </summary>
    public static void Write(string slot, GameMap map, List<U7Object?> npcs, UsecodeMachine? usecode,
        IEnumerable<U7Object> monsters, GwinState gwin)
    {
        var dir = SlotDir(slot);
        var tmp = dir + TmpSuffix;
        if (Directory.Exists(tmp))
        {
            Directory.Delete(tmp, recursive: true);
        }

        Directory.CreateDirectory(tmp);
        map.WriteIregFiles(tmp);
        NpcDat.Save(tmp, npcs, map);
        NpcDat.SaveMonsters(tmp, monsters, map);
        if (usecode is not null)
        {
            File.WriteAllBytes(Path.Combine(tmp, "FLAGINIT"), usecode.GFlags);
            UsecodeDat.Write(tmp, usecode.Party?.Members.Select(m => m.NpcNum).ToList() ?? [], usecode.Timers);
        }

        using (var w = new BinaryWriter(File.Create(Path.Combine(tmp, GwinName))))
        {
            gwin.Write(w);
        }

        var identity = Path.Combine(U7Paths.RepoRoot, "u7", "GAMEDAT", "IDENTITY");
        if (File.Exists(identity))
        {
            File.Copy(identity, Path.Combine(tmp, "IDENTITY"));
        }

        KeepUnwritten(dir, tmp);
        Swap(dir, tmp);
        GD.Print($"saved to {dir}");
    }

    /// <summary>
    /// Copies the old slot's files this save didn't write, which writing over
    /// the slot used to keep: FLAGINIT and USECODE.* without usecode, IDENTITY
    /// without the GOG copy. Not GWIN.DAT, which GAMEWIN.DAT replaces.
    /// </summary>
    static void KeepUnwritten(string dir, string tmp)
    {
        if (!Directory.Exists(dir))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(dir))
        {
            var name = Path.GetFileName(file);
            var dest = Path.Combine(tmp, name);
            if (!name.Equals(OldGwinName, StringComparison.OrdinalIgnoreCase) && !File.Exists(dest))
            {
                File.Copy(file, dest);
            }
        }
    }

    /// <summary>
    /// Puts the new slot in place of the old. Windows can't swap directories
    /// in one step, so the old one is renamed aside first and put back if the
    /// new one can't take its place (<see cref="Exists"/> puts it back after a crash).
    /// </summary>
    static void Swap(string dir, string tmp)
    {
        var old = dir + OldSuffix;
        if (Directory.Exists(old))
        {
            Directory.Delete(old, recursive: true);
        }

        if (!Directory.Exists(dir))
        {
            Directory.Move(tmp, dir);
            return;
        }

        Directory.Move(dir, old);
        try
        {
            Directory.Move(tmp, dir);
        }
        catch
        {
            Directory.Move(old, dir);
            throw;
        }

        Directory.Delete(old, recursive: true);
    }

    /// <summary>Exult <c>gamewin.dat</c>; saves made before this used the name GWIN.DAT.</summary>
    const string GwinName = "GAMEWIN.DAT";
    const string OldGwinName = "GWIN.DAT";

    /// <summary>The loaded save's <see cref="GwinState"/>; null for a new game or an unreadable file.</summary>
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
            return GwinState.Read(r);
        }
        catch (Exception ex)
        {
            GD.Print($"{GwinName} unreadable: {ex.Message}");
            return null;
        }
    }
}
