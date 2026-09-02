using U7.Core;

namespace U7.Actors;

public readonly record struct ScheduleEntry(
    int Npc, int TimeSlot, int Type, string Name, int Tx, int Ty, int Tz, int Days);

/// <summary>Loads <c>assets/data/schedules.csv</c> (dumped SCHEDULE.DAT).</summary>
public sealed class ScheduleTable
{
    readonly Dictionary<int, List<ScheduleEntry>> _byNpc = new();

    public static ScheduleTable Load()
    {
        var table = new ScheduleTable();
        var path = Path.Combine(U7Paths.DataDir, "schedules.csv");
        if (!File.Exists(path))
        {
            Godot.GD.PushWarning($"schedules.csv not found at {path}");
            return table;
        }

        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("npc,", StringComparison.Ordinal))
            {
                continue;
            }

            var p = line.Split(',');
            if (p.Length < 8)
            {
                continue;
            }

            var npc = int.Parse(p[0]);
            var slot = int.Parse(p[2]);
            var type = int.Parse(p[3]);
            var name = p[4];
            var tx = int.Parse(p[5]);
            var ty = int.Parse(p[6]);
            var tz = int.Parse(p[7]);
            var days = 0x7F;
            if (p.Length > 8 && p[8].StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                days = Convert.ToInt32(p[8], 16);
            }

            if (!table._byNpc.TryGetValue(npc, out var list))
            {
                list = new List<ScheduleEntry>();
                table._byNpc[npc] = list;
            }

            list.Add(new ScheduleEntry(npc, slot, type, name, tx, ty, tz, days));
        }

        Godot.GD.Print($"schedules: {table._byNpc.Count} NPCs");
        return table;
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
}
