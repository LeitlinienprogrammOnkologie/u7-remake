using System.Buffers.Binary;
using System.IO;
using System.Text;
using U7.Core;

namespace U7.Actors;

/// <summary>Exult <c>Schedule_change</c>: from a time slot (0-7, three hours each), this schedule at this spot.</summary>
public readonly record struct ScheduleEntry(int Npc, int TimeSlot, int Type, int Tx, int Ty, int Tz, int Days = 0x7F);

/// <summary>
/// The NPCs' schedules (Exult <c>Game_window::read_schedules</c>): the saved
/// game's <c>schedule.dat</c> when it has one, else <c>STATIC/SCHEDULE.DAT</c>.
/// Usecode can change them (Exult's <c>set_new_schedules</c>,
/// <c>modify_schedule</c>, <c>revert_schedule</c>), so a save writes them
/// back in Exult's format (<c>write_schedules</c>).
/// </summary>
public sealed class ScheduleTable
{
    public const string FileName = "SCHEDULE.DAT";

    readonly Dictionary<int, List<ScheduleEntry>> _byNpc = new();
    /// <summary>Exult <c>Schedule_change::script_names</c>: kept from an Exult file and written back.</summary>
    readonly List<string> _scriptNames = new();

    public static ScheduleTable Load()
    {
        var table = new ScheduleTable();
        var data = U7Paths.ReadGameDat(FileName) ?? ReadStatic();
        if (data is null)
        {
            Godot.GD.PushWarning($"{FileName} not found");
            return table;
        }

        table.Read(data, null);
        Godot.GD.Print($"schedules: {table._byNpc.Count} NPCs");
        return table;
    }

    static byte[]? ReadStatic()
    {
        var path = Path.Combine(U7Paths.StaticDir, FileName);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    /// <summary>
    /// Exult <c>Set_to_read_schedules</c> and <c>Read_a_schedule</c>: the
    /// originals' 4-byte entries, or Exult's 8-byte ones (version -1, or -2
    /// with script names). <paramref name="only"/> reads just that NPC's.
    /// </summary>
    void Read(byte[] d, int? only)
    {
        var ip = 0;
        var entsize = 4;
        var numNpcs = ReadI32(d, ref ip);
        var numScripts = 0;
        if (numNpcs == -1)
        {
            entsize = 8;
            numNpcs = ReadI32(d, ref ip);
        }
        else if (numNpcs == -2)
        {
            entsize = 8;
            numNpcs = ReadI32(d, ref ip);
            numScripts = ReadU16(d, ref ip);
        }

        var offsets = new int[numNpcs];
        for (var i = 0; i < numNpcs; i++)
        {
            offsets[i] = (short)ReadU16(d, ref ip);
        }

        if (numScripts > 0)
        {
            var total = ReadU16(d, ref ip);
            var end = ip + total;
            if (only is null)
            {
                _scriptNames.Clear();
                for (var i = 0; i < numScripts && ip < end; i++)
                {
                    var len = ReadU16(d, ref ip);
                    _scriptNames.Add(Encoding.Latin1.GetString(d, ip, len));
                    ip += len;
                }
            }

            ip = end;
        }

        var entriesStart = ip;
        // The avatar has none: NPC n's entries run from offsets[n - 1] to offsets[n].
        for (var npc = 1; npc < numNpcs; npc++)
        {
            if (only is { } o && npc != o)
            {
                continue;
            }

            var list = new List<ScheduleEntry>();
            for (var j = offsets[npc - 1]; j < offsets[npc]; j++)
            {
                var e = entriesStart + j * entsize;
                if (e + entsize > d.Length)
                {
                    break;
                }

                list.Add(entsize == 4 ? Entry4(npc, d, e) : Entry8(npc, d, e));
            }

            _byNpc[npc] = list;
        }
    }

    /// <summary>Exult <c>Schedule_change::set4</c>: the originals' entry, a tile within a superchunk.</summary>
    static ScheduleEntry Entry4(int npc, byte[] d, int e)
    {
        var schunk = d[e + 3];
        var sx = schunk % U7Constants.SuperchunksPerSide;
        var sy = schunk / U7Constants.SuperchunksPerSide;
        return new ScheduleEntry(npc, d[e] & 7, d[e] >> 3,
            sx * U7Constants.TilesPerSuperchunk + d[e + 1], sy * U7Constants.TilesPerSuperchunk + d[e + 2], 0);
    }

    /// <summary>Exult <c>Schedule_change::set8</c>.</summary>
    static ScheduleEntry Entry8(int npc, byte[] d, int e) =>
        new(npc, d[e + 5], d[e + 6],
            BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(e, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(e + 2, 2)), d[e + 4], d[e + 7]);

    /// <summary>Exult <c>Game_window::write_schedules</c>: version -2, every NPC but the avatar, 8-byte entries.</summary>
    public void Write(string dir, int numNpcs)
    {
        using var w = new BinaryWriter(File.Create(Path.Combine(dir, FileName)));
        w.Write(-2);
        w.Write(numNpcs);
        w.Write((ushort)_scriptNames.Count);
        w.Write((ushort)0);
        short offset = 0;
        for (var i = 1; i < numNpcs; i++)
        {
            offset += (short)For(i).Count;
            w.Write(offset);
        }

        if (_scriptNames.Count > 0)
        {
            w.Write((ushort)_scriptNames.Sum(n => 2 + Encoding.Latin1.GetByteCount(n)));
            foreach (var name in _scriptNames)
            {
                var bytes = Encoding.Latin1.GetBytes(name);
                w.Write((ushort)bytes.Length);
                w.Write(bytes);
            }
        }

        for (var i = 1; i < numNpcs; i++)
        {
            foreach (var e in For(i))
            {
                w.Write((ushort)e.Tx);
                w.Write((ushort)e.Ty);
                w.Write((byte)e.Tz);
                w.Write((byte)e.TimeSlot);
                w.Write((byte)e.Type);
                w.Write((byte)e.Days);
            }
        }
    }

    public IReadOnlyList<ScheduleEntry> For(int npc) =>
        _byNpc.TryGetValue(npc, out var list) ? list : Array.Empty<ScheduleEntry>();

    /// <summary>Exult <c>Npc_actor::set_schedules</c>: a new list (<c>Schedule_change::set</c> keeps the time below 8).</summary>
    public void SetSchedules(int npc, IEnumerable<(int Time, int Type, int Tx, int Ty)> list) =>
        _byNpc[npc] = list.Select(s => new ScheduleEntry(npc, s.Time % 8, s.Type & 0xff, s.Tx, s.Ty, 0)).ToList();

    /// <summary>Exult <c>Npc_actor::set_schedule_time_type</c>: that slot's schedule changed (its spot kept), or a new entry at 0,0.</summary>
    public void SetTimeType(int npc, int time, int type) =>
        ChangeSlot(npc, time, e => e with { Type = type & 0xff },
            () => new ScheduleEntry(npc, time % 8, type & 0xff, 0, 0, 0));

    /// <summary>Exult <c>Npc_actor::set_schedule_time_location</c>: that slot's spot changed, or a new entry of schedule 0 there.</summary>
    public void SetTimeLocation(int npc, int time, int tx, int ty) =>
        ChangeSlot(npc, time, e => e with { Tx = tx, Ty = ty, Tz = 0 },
            () => new ScheduleEntry(npc, time % 8, 0, tx, ty, 0));

    void ChangeSlot(int npc, int time, Func<ScheduleEntry, ScheduleEntry> change, Func<ScheduleEntry> add)
    {
        if (!_byNpc.TryGetValue(npc, out var list))
        {
            list = new List<ScheduleEntry>();
            _byNpc[npc] = list;
        }

        var i = list.FindIndex(e => e.TimeSlot == time);
        if (i < 0)
        {
            list.Add(add());
        }
        else
        {
            list[i] = change(list[i]) with { TimeSlot = time % 8 };
        }
    }

    /// <summary>Exult <c>Game_window::revert_schedules</c>: the NPC's schedules from <c>STATIC/SCHEDULE.DAT</c> again.</summary>
    public void Revert(int npc)
    {
        if (npc <= 0 || ReadStatic() is not { } data)
        {
            return;
        }

        Read(data, npc);
    }

    /// <summary>
    /// Exact slot, else the most recent earlier slot wrapping the 8-slot day
    /// (Exult <c>find_schedule_at_time</c>).
    /// </summary>
    public ScheduleEntry? ForSlot(int npc, int slot)
    {
        if (!_byNpc.TryGetValue(npc, out var list) || list.Count == 0)
        {
            return null;
        }

        slot &= 7;
        ScheduleEntry? exact = null;
        var closestDist = 100;
        ScheduleEntry? closest = null;
        foreach (var e in list)
        {
            if (e.TimeSlot == slot)
            {
                exact = e;
            }

            var dist = (slot - e.TimeSlot + 8) % 8;
            if (dist < closestDist)
            {
                closestDist = dist;
                closest = e;
            }
        }

        return exact ?? closest;
    }

    static int ReadU16(byte[] d, ref int ip)
    {
        var v = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(ip, 2));
        ip += 2;
        return v;
    }

    static int ReadI32(byte[] d, ref int ip)
    {
        var v = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(ip, 4));
        ip += 4;
        return v;
    }
}
