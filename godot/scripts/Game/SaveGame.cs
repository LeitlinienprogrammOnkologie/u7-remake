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
    /// <summary>The slot last saved or loaded: Exult's GAMEDAT, which Journey Onward goes on with.</summary>
    const string LastFile = "LAST.TXT";
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
            // Exult Usecode_internal::write: any game but Black Gate keeps a keyring.
            if (!GameContext.IsBg)
            {
                File.WriteAllBytes(Path.Combine(tmp, KeyringName),
                    usecode.Keyring.SelectMany(k => BitConverter.GetBytes((ushort)k)).ToArray());
            }

            File.WriteAllBytes(Path.Combine(tmp, "FLAGINIT"), usecode.GFlags);
            UsecodeDat.Write(tmp, usecode.Party?.Members.Select(m => m.NpcNum).ToList() ?? [], usecode.Timers, usecode.SavedPos);
            UsecodeVars.Write(tmp, usecode);
            usecode.Schedules?.Table.Write(tmp, npcs.Count);
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
        // Exult: GAMEDAT's IDENTITY is INITGAME.DAT's, which loading checks.
        if (U7Paths.IdentityFile is { } identity)
        {
            File.WriteAllBytes(Path.Combine(tmp, IdentityName), identity);
        }

        KeepUnwritten(dir, tmp, keepShot: false);
        Swap(dir, tmp);
        SetLast(slot);
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
    const string IdentityName = "IDENTITY";
    /// <summary>Exult <c>KEYRINGDAT</c>: the keyring's keys, two bytes each.</summary>
    public const string KeyringName = "KEYRING.DAT";

    /// <summary>Exult <c>Keyring::read</c>: the loaded game's keys (none in a new game or an old save).</summary>
    public static IEnumerable<int> ReadKeyring()
    {
        var data = U7Paths.ReadGameDat(KeyringName) ?? [];
        for (var i = 0; i + 1 < data.Length; i += 2)
        {
            yield return BitConverter.ToUInt16(data, i);
        }
    }
    /// <summary>The files beside the slots that belong to a mode's saves (Exult's <c>&lt;SAVEGAME&gt;</c>).</summary>
    static readonly string[] ModeFiles = [LastFile, "endgame.flg", "quotes.flg"];

    /// <summary>
    /// The game the slot was saved in, by its IDENTITY (Exult
    /// <c>get_game_identity</c>); null for a save without one or of a game
    /// not known.
    /// </summary>
    public static GameType? SlotGame(string slot) =>
        U7Paths.ReadIdentity(Path.Combine(SlotDir(slot), IdentityName)) is { } id ? GameContext.TypeOf(id) : null;

    /// <summary>
    /// Saves from before the modes had folders (<c>saves/&lt;slot&gt;</c>) are
    /// Black Gate's: they move to <c>saves/bg</c>, with the files beside them,
    /// unless a slot of that name is there already. Run at each start, so a
    /// save written by an older build follows too.
    /// </summary>
    public static void MoveLegacySaves()
    {
        var root = U7Paths.SavesRoot;
        if (!Directory.Exists(root))
        {
            return;
        }

        var bg = Path.Combine(root, "bg");
        var moved = 0;
        try
        {
            foreach (var dir in Directory.GetDirectories(root))
            {
                var name = Path.GetFileName(dir);
                if (name is "bg" or "si" or "saga" || !File.Exists(Path.Combine(dir, "NPC.DAT")) ||
                    Directory.Exists(Path.Combine(bg, name)))
                {
                    continue;
                }

                Directory.CreateDirectory(bg);
                Directory.Move(dir, Path.Combine(bg, name));
                moved++;
            }

            foreach (var file in ModeFiles)
            {
                var from = Path.Combine(root, file);
                if (File.Exists(from) && !File.Exists(Path.Combine(bg, file)))
                {
                    Directory.CreateDirectory(bg);
                    File.Move(from, Path.Combine(bg, file));
                }
            }
        }
        catch (IOException ex)
        {
            // Another game starting at the same time may be moving them.
            GD.PushWarning($"moving saves to {bg}: {ex.Message}");
        }

        if (moved > 0)
        {
            GD.Print($"moved {moved} Black Gate saves to {bg}");
        }
    }

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

    /// <summary>Exult: every save and restore goes through GAMEDAT, so it holds the game last saved or loaded.</summary>
    public static void SetLast(string slot)
    {
        try
        {
            Directory.CreateDirectory(U7Paths.SavesDir);
            File.WriteAllText(Path.Combine(U7Paths.SavesDir, LastFile), slot);
        }
        catch (IOException ex)
        {
            GD.PushError($"{LastFile}: {ex.Message}");
        }
    }

    /// <summary>
    /// Exult <c>init_gamedat(false)</c> for Journey Onward: the game last
    /// saved or loaded; without that note, the newest saved game; null when
    /// there is none (Exult's "You must start a new game first.").
    /// </summary>
    public static string? JourneySlot()
    {
        var last = Path.Combine(U7Paths.SavesDir, LastFile);
        if (File.Exists(last) && File.ReadAllText(last).Trim() is { Length: > 0 } slot && Exists(slot))
        {
            return slot;
        }

        var games = List().Where(e => e.Readable).ToList();
        if (Exists(QuickSlot))
        {
            games.Add(Describe(QuickSlot));
        }

        return games.OrderByDescending(e => e.Info?.Details.Real ?? Directory.GetLastWriteTime(SlotDir(e.Slot)))
            .Select(e => e.Slot)
            .FirstOrDefault();
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
