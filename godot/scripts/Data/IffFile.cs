using System.IO;

namespace U7.Data;

/// <summary>
/// Exult <c>IFF</c>: an archive of typed objects (ENDGAME.DAT: FORM ENDG with
/// FLIC, FONT, VOCF and SHAP objects), each a big-endian length and its data,
/// word-aligned. The originals' objects begin with an 8-byte name, which
/// <see cref="Get"/> keeps (as Exult's <c>retrieve</c> does).
/// </summary>
public sealed class IffFile
{
    readonly byte[] _data;
    readonly List<(int Offset, int Size)> _objects = new();

    public IffFile(string path)
    {
        _data = File.Exists(path) ? File.ReadAllBytes(path) : [];
        if (_data.Length < 12 || _data[0] != 'F' || _data[1] != 'O' || _data[2] != 'R' || _data[3] != 'M')
        {
            return;
        }

        var end = Math.Min(_data.Length, 8 + BigEndian(4));
        var pos = 12;
        while (pos + 8 <= end)
        {
            var size = BigEndian(pos + 4);
            var offset = pos + 8;
            if (size <= 0 || offset + size > _data.Length)
            {
                break;
            }

            _objects.Add((offset, size));
            pos = offset + size + (size & 1);
        }
    }

    public int Count => _objects.Count;

    /// <summary>Object <paramref name="index"/>'s data, its name included; empty if there is none.</summary>
    public ReadOnlySpan<byte> Get(int index) =>
        (uint)index < (uint)_objects.Count ? _data.AsSpan(_objects[index].Offset, _objects[index].Size) : default;

    int BigEndian(int at) => (_data[at] << 24) | (_data[at + 1] << 16) | (_data[at + 2] << 8) | _data[at + 3];
}
