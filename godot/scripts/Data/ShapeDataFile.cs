using System.IO;
using U7.Core;

namespace U7.Data;

/// <summary>
/// Exult's binary shape data files (WEAPONS.DAT, AMMO.DAT, ARMOR.DAT,
/// MONSTERS.DAT; <c>Base_reader::read_binary_internal</c> through
/// <c>Functor_multidata_reader</c>): a count (Exult <c>Read_count</c>: one
/// byte, 255 for a two-byte count after it), then per entry the shape and
/// the record. The game's file is read, then a mod's of the same name over it.
/// </summary>
public static class ShapeDataFile
{
    /// <summary>The entries by shape, the record without its shape; null where the last file deletes it (byte <c>size - 3</c> of the entry is 0xff).</summary>
    public static Dictionary<int, byte[]?> Read(string name, int entrySize)
    {
        var entries = new Dictionary<int, byte[]?>();
        foreach (var path in new[] { Path.Combine(U7Paths.StaticDir, name), U7Paths.PatchFile(name) })
        {
            if (path is null || !File.Exists(path))
            {
                continue;
            }

            var data = File.ReadAllBytes(path);
            var at = 0;
            int count = data.Length > 0 ? data[at++] : 0;
            if (count == 255 && at + 2 <= data.Length)
            {
                count = BitConverter.ToUInt16(data, at);
                at += 2;
            }

            for (var i = 0; i < count && at + entrySize <= data.Length; i++, at += entrySize)
            {
                var shape = BitConverter.ToUInt16(data, at);
                var record = data.AsSpan(at + 2, entrySize - 2).ToArray();
                // Exult Class_reader_functor: a deleted entry removes the one read before.
                entries[shape] = record[entrySize - 3] == 0xff ? null : record;
            }
        }

        return entries;
    }
}
