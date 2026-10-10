using System.IO;
using Godot;
using U7.Actors;
using U7.Core;
using U7.Data;
using U7.Usecode;

namespace U7.Game;

/// <summary>
/// Saved games in Exult's GAMEDAT layout: a directory with U7IREGxx, NPC.DAT,
/// MONSNPCS.DAT, FLAGINIT, GAMEWIN.DAT, USECODE.DAT and USECODE.VAR, and what
/// the save/load screen shows: Exult's SAVEINFO.DAT, the game's name (Exult
/// keeps it in its save file) and a picture of the world (Exult's
/// scrnshot.shp, here a PNG of the world as it was drawn). Loading points
/// <see cref="U7Paths.GameDatOverride"/> at the directory and restarts the
/// scene, so the normal loaders read it.
/// </summary>
public static class SaveGame
{
    public const string QuickSlot = "quick";
    /// <summary>Exult <c>MAX_SAVEGAME_NAME_LEN</c>: a name is shorter than this.</summary>
    public const int MaxNameLength = 0x50;
    const string NameFile = "SAVENAME.TXT";
    const string ShotFile = "SCRNSHOT.PNG";
    /// <summary>The slots the save/load screen makes, numbered as Exult's exultNNbg.sav are.</summary>
    const string NumberedPrefix = "save";
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
    /// <param name="name">The game's name, or null to keep the slot's.</param>
    /// <param name="shot">The world's picture; without one the slot keeps none (Exult removes the old one).</param>
    public static void Write(string slot, GameMap map, List<U7Object?> npcs, UsecodeMachine? usecode,
        IEnumerable<U7Object> monsters, GwinState gwin, SaveInfo info, string? name = null, Image? shot = null)
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

        using (var w = new BinaryWriter(File.Create(Path.Combine(tmp, SaveInfo.FileName))))
        {
            info.Write(w);
        }

        if (name is not null)
        {
            File.WriteAllText(Path.Combine(tmp, NameFile), name);
        }

        shot?.SavePng(Path.Combine(tmp, ShotFile));
        var identity = Path.Combine(U7Paths.RepoRoot, "u7", "GAMEDAT", "IDENTITY");
        if (File.Exists(identity))
        {
            File.Copy(identity, Path.Combine(tmp, "IDENTITY"));
        }

        KeepUnwritten(dir, tmp, keepShot: false);
        Swap(dir, tmp);
        GD.Print($"saved to {dir}");
    }

    /// <summary>
    /// Copies the old slot's files this save didn't write, which writing over
    /// the slot used to keep: FLAGINIT and USECODE.* without usecode, IDENTITY
    /// without the GOG copy. Not GWIN.DAT, which GAMEWIN.DAT replaces.
    /// </summary>
    static void KeepUnwritten(string dir, string tmp, bool keepShot)
    {
        if (!Directory.Exists(dir))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(dir))
        {
            var name = Path.GetFileName(file);
            var dest = Path.Combine(tmp, name);
            if (!name.Equals(OldGwinName, StringComparison.OrdinalIgnoreCase) && !File.Exists(dest) &&
                (keepShot || !name.Equals(ShotFile, StringComparison.OrdinalIgnoreCase)))
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

    /// <summary>The loaded game's SAVEINFO.DAT (Exult reads GAMEDAT's for the save count); null for a new game.</summary>
    public static SaveInfo? ReadCurrentInfo() => SaveInfo.Read(U7Paths.ReadGameDat(SaveInfo.FileName));

    /// <summary>A saved game as the save/load screen lists it (Exult <c>Newfile_gump::SaveInfo</c>).</summary>
    /// <param name="Readable">Whether it can be loaded (Exult: its file could be read).</param>
    public sealed record Entry(string Slot, string Name, SaveInfo? Info, string? ShotPath, bool Readable);

    /// <summary>The slot as an entry: its name (else the slot's), details and picture.</summary>
    public static Entry Describe(string slot)
    {
        var dir = SlotDir(slot);
        var namePath = Path.Combine(dir, NameFile);
        var name = File.Exists(namePath) ? File.ReadAllText(namePath).Trim() : slot;
        var infoPath = Path.Combine(dir, SaveInfo.FileName);
        var info = File.Exists(infoPath) ? SaveInfo.Read(File.ReadAllBytes(infoPath)) : null;
        var shot = Path.Combine(dir, ShotFile);
        return new Entry(slot, name.Length > 0 ? name : slot, info, File.Exists(shot) ? shot : null, Exists(slot));
    }

    /// <summary>
    /// Exult <c>Newfile_gump::LoadSaveGameDetails</c>: every saved game but
    /// the quick one, newest first by the computer's clock, those without a
    /// date last (Exult <c>SaveInfo::CompareThis</c>; then by name here, where
    /// Exult goes by number).
    /// </summary>
    public static List<Entry> List()
    {
        if (!Directory.Exists(U7Paths.SavesDir))
        {
            return [];
        }

        return Directory.GetDirectories(U7Paths.SavesDir)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(s => !s.EndsWith(TmpSuffix) && !s.EndsWith(OldSuffix) && s != QuickSlot)
            .Select(Describe)
            .Where(e => e.Readable || e.Info is not null)
            .OrderBy(e => e.Info is null)
            .ThenByDescending(e => e.Info?.Details.Real)
            .ThenBy(e => e.Slot, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Exult's <c>first_free</c>: the lowest numbered slot not taken.</summary>
    public static string FirstFreeSlot()
    {
        for (var n = 0; ; n++)
        {
            var slot = $"{NumberedPrefix}{n:00}";
            if (!Directory.Exists(SlotDir(slot)) && !Directory.Exists(SlotDir(slot) + OldSuffix))
            {
                return slot;
            }
        }
    }

    /// <summary>Exult <c>Newfile_gump::delete_file</c>: the saved game is removed.</summary>
    public static void Delete(string slot)
    {
        var dir = SlotDir(slot);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
