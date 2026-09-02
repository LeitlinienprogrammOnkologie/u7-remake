using System.IO;

namespace U7.Data;

/// <summary>
/// Ultima VII FLEX archive (Exult <c>files/Flex.*</c>).
/// Header is 128 bytes, then <c>count</c> little-endian (offset, size) pairs.
/// </summary>
public sealed class FlexFile
{
    public const uint Magic1 = 0xffff1a00;
    public const int HeaderLength = 128;

    public string Title { get; }
    public int Count { get; }
    readonly byte[] _data;
    readonly (int Offset, int Size)[] _entries;

    public FlexFile(string path)
    {
        _data = File.ReadAllBytes(path);
        Title = ReadCString(_data, 0, 80);
        var magic = BitConverter.ToUInt32(_data, 80);
        if (magic != Magic1)
        {
            throw new InvalidDataException($"{path} is not a FLEX file (magic 0x{magic:X8}).");
        }

        Count = BitConverter.ToInt32(_data, 84);
        _entries = new (int, int)[Count];
        for (var i = 0; i < Count; i++)
        {
            var at = HeaderLength + i * 8;
            _entries[i] = (BitConverter.ToInt32(_data, at), BitConverter.ToInt32(_data, at + 4));
        }
    }

    public ReadOnlySpan<byte> Get(int index)
    {
        if ((uint)index >= (uint)Count)
        {
            return ReadOnlySpan<byte>.Empty;
        }

        var (offset, size) = _entries[index];
        if (size <= 0 || offset < 0 || offset + size > _data.Length)
        {
            return ReadOnlySpan<byte>.Empty;
        }

        return _data.AsSpan(offset, size);
    }

    static string ReadCString(byte[] data, int offset, int max)
    {
        var end = offset;
        var stop = Math.Min(offset + max, data.Length);
        while (end < stop && data[end] != 0)
        {
            end++;
        }

        return System.Text.Encoding.ASCII.GetString(data, offset, end - offset);
    }
}
