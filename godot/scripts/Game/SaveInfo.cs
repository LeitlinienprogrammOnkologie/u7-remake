using System.IO;
using System.Text;
using U7.Actors;
using U7.Data;

namespace U7.Game;

/// <summary>
/// Exult <c>SaveGame_Details</c> (Newfile_gump.h): when a game was saved,
/// by the computer's clock and the game's, and how many times it has been.
/// </summary>
public readonly record struct SaveDetails(DateTime Real, int GameDay, int GameHour, int GameMinute, int SaveCount);

/// <summary>
/// Exult <c>SaveGame_Party</c>: a party member as the save/load screen shows
/// it. <paramref name="Face"/> (FACES.VGA) is ours, kept in Exult's first
/// reserved word, which Exult writes as zero and never reads.
/// </summary>
public readonly record struct SaveParty(string Name, int Shape, int Exp, uint Flags, uint Flags2, int Food, int Str,
    int Combat, int Dext, int Intel, int Magic, int Mana, int Training, int Health, int Face);

/// <summary>
/// Exult's <c>saveinfo.dat</c> in GAMEDAT, as <c>Game_window::write_saveinfo</c>
/// writes it: 64 bytes of details, then 64 for each party member, the avatar
/// first.
/// </summary>
public sealed record SaveInfo(SaveDetails Details, IReadOnlyList<SaveParty> Party)
{
    public const string FileName = "SAVEINFO.DAT";
    const int DetailsSize = 64, PartySize = 64, NameSize = 18;

    /// <summary>
    /// Exult <c>write_saveinfo</c>'s fields for the game as it is: the time
    /// now, the game's clock, the party (Exult <c>get_party</c> order), and
    /// the save count.
    /// </summary>
    public static SaveInfo Of(DateTime now, int day, int hour, int minute, int saveCount, IEnumerable<U7Object> party) =>
        new(new SaveDetails(now, day, hour, minute, saveCount), party.Select(Member).ToList());

    static SaveParty Member(U7Object npc) =>
        new(npc.NpcName, npc.Shape, npc.Props[ActorProp.Exp], npc.Flags, npc.Flags2, npc.Props[ActorProp.FoodLevel],
            npc.Props[ActorProp.Strength], npc.Props[ActorProp.Combat], npc.Props[ActorProp.Dexterity],
            npc.Props[ActorProp.Intelligence], npc.Props[ActorProp.Magic], npc.Props[ActorProp.Mana],
            npc.Props[ActorProp.Training], npc.Props[ActorProp.Health], npc.NpcNum == 0 ? AvatarLook.FaceShape : npc.FaceNum);

    public void Write(BinaryWriter w)
    {
        var d = Details;
        w.Write((byte)d.Real.Minute);
        w.Write((byte)d.Real.Hour);
        w.Write((byte)d.Real.Day);
        w.Write((byte)d.Real.Month);
        w.Write((ushort)d.Real.Year);
        w.Write((byte)d.GameMinute);
        w.Write((byte)d.GameHour);
        w.Write((ushort)d.GameDay);
        w.Write((ushort)d.SaveCount);
        w.Write((byte)Party.Count);
        w.Write((byte)0); // unused
        w.Write((byte)d.Real.Second);
        w.Write(new byte[DetailsSize - 15]);
        foreach (var p in Party)
        {
            var name = new byte[NameSize];
            Encoding.Latin1.GetBytes(p.Name, 0, Math.Min(p.Name.Length, NameSize), name, 0);
            w.Write(name);
            w.Write((ushort)p.Shape);
            w.Write((uint)p.Exp);
            w.Write(p.Flags);
            w.Write(p.Flags2);
            foreach (var stat in new[] { p.Food, p.Str, p.Combat, p.Dext, p.Intel, p.Magic, p.Mana, p.Training })
            {
                w.Write((byte)stat);
            }

            w.Write((short)p.Health);
            w.Write((short)0); // shape file: SHAPES.VGA
            w.Write(p.Face);
            w.Write(new byte[PartySize - 48]);
        }
    }

    /// <summary>Exult <c>Game_window::get_saveinfo</c>'s reading; null when the file is cut short or its date is no date.</summary>
    public static SaveInfo? Read(byte[]? data)
    {
        if (data is null || data.Length < DetailsSize)
        {
            return null;
        }

        var r = new BinaryReader(new MemoryStream(data));
        int min = r.ReadByte(), hour = r.ReadByte(), day = r.ReadByte(), month = r.ReadByte(), year = r.ReadUInt16();
        int gmin = r.ReadByte(), ghour = r.ReadByte(), gday = r.ReadUInt16();
        int count = r.ReadUInt16(), size = r.ReadByte();
        r.ReadByte();
        int sec = r.ReadByte();
        DateTime real;
        try
        {
            real = new DateTime(year, month, day, hour, min, Math.Min(sec, 59));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }

        var party = new List<SaveParty>();
        for (var i = 0; i < size && DetailsSize + (i + 1) * PartySize <= data.Length; i++)
        {
            r.BaseStream.Position = DetailsSize + i * PartySize;
            var name = Encoding.Latin1.GetString(r.ReadBytes(NameSize)).TrimEnd('\0');
            int shape = r.ReadUInt16(), exp = (int)r.ReadUInt32();
            uint flags = r.ReadUInt32(), flags2 = r.ReadUInt32();
            var stats = r.ReadBytes(8);
            int health = r.ReadInt16();
            r.ReadInt16(); // shape file
            var face = r.ReadInt32();
            party.Add(new SaveParty(name, shape, exp, flags, flags2, stats[0], stats[1], stats[2], stats[3], stats[4],
                stats[5], stats[6], stats[7], health, face));
        }

        return new SaveInfo(new SaveDetails(real, gday, ghour, gmin, count), party);
    }
}
