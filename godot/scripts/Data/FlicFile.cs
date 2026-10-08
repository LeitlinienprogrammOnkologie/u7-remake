namespace U7.Data;

/// <summary>
/// Exult <c>playfli</c>'s decoder: an Autodesk FLI/FLC animation (the
/// endgame's, from ENDGAME.DAT, after its 8-byte name) decoded frame by frame
/// into an 8-bit picture (<see cref="Pixels"/>) and its 6-bit palette
/// (<see cref="Palette"/>). The playing (which frames, when) is the caller's.
/// </summary>
public sealed class FlicFile
{
    const int Color256 = 4, Ss2 = 7, Color = 11, Lc = 12, Black = 13, Brun = 15, Copy = 16, Pstamp = 18;

    readonly byte[] _data;
    readonly int _start;
    int _pos;

    public int Frames { get; }
    public int Width { get; }
    public int Height { get; }
    /// <summary>Exult <c>fli_speed * 10</c>: milliseconds a frame.</summary>
    public int FrameMs { get; }
    /// <summary>The next frame to decode (Exult <c>frame</c>).</summary>
    public int Frame { get; private set; }
    public byte[] Pixels { get; }
    /// <summary>256 colours, 6 bits each (Exult's palette values).</summary>
    public byte[] Palette { get; } = new byte[768];

    public FlicFile(ReadOnlySpan<byte> data)
    {
        _data = data.ToArray();
        var at = 0;
        // Exult initfli: a U7 flic has an 8-byte name in front.
        if (_data.Length >= 6 && Magic(4) is not (0xaf11 or 0xaf12))
        {
            at = 8;
        }

        if (_data.Length < at + 128 || Magic(at + 4) is not (0xaf11 or 0xaf12))
        {
            Pixels = [];
            return;
        }

        Frames = U16(at + 6);
        Width = U16(at + 8);
        Height = U16(at + 10);
        FrameMs = U16(at + 16) * 10;
        _start = _pos = at + 128;
        Pixels = new byte[Width * Height];
    }

    /// <summary>Decode up to <paramref name="frame"/> (inclusive), from the start again if it is behind.</summary>
    public void DecodeTo(int frame)
    {
        if (frame < Frame - 1)
        {
            Frame = 0;
            _pos = _start;
        }

        while (Frame <= frame && Frame < Frames)
        {
            DecodeNext();
        }
    }

    int Magic(int at) => U16(at);

    int U16(int at) => at + 1 < _data.Length ? _data[at] | (_data[at + 1] << 8) : 0;

    int S16(int at) => (short)U16(at);

    int U32(int at) => at + 3 < _data.Length ? _data[at] | (_data[at + 1] << 8) | (_data[at + 2] << 16) | (_data[at + 3] << 24) : 0;

    /// <summary>Exult <c>playfli::play</c>'s loop body: one frame's chunks.</summary>
    void DecodeNext()
    {
        var frameSize = U32(_pos);
        var chunks = U16(_pos + 6);
        var p = _pos + 16;
        for (var c = 0; c < chunks && p + 6 <= _data.Length; c++)
        {
            var chunkSize = U32(p);
            var type = U16(p + 4);
            var d = p + 6;
            switch (type)
            {
                case Color256:
                    ReadPalette(d, quarter: true);
                    break;
                case Color:
                    ReadPalette(d, quarter: false);
                    break;
                case Lc:
                    DecodeLc(d);
                    break;
                case Black:
                    Array.Clear(Pixels);
                    break;
                case Brun:
                    DecodeBrun(d);
                    break;
                case Copy:
                    _data.AsSpan(d, Math.Min(Pixels.Length, _data.Length - d)).CopyTo(Pixels);
                    break;
                case Ss2:
                    DecodeSs2(d);
                    break;
                case Pstamp:
                    break;
            }

            p += Math.Max(6, chunkSize);
        }

        _pos += Math.Max(16, frameSize);
        Frame++;
    }

    /// <summary>
    /// Exult's <c>read_palette</c>: a new palette, black but for packets of
    /// (bytes to skip, colours to change, 0 meaning 256), FLI_COLOR256's 8-bit
    /// values made 6-bit. (Exult skips bytes, not colours, and doesn't move on
    /// after a packet; the endgame's have one packet of all 256.)
    /// </summary>
    void ReadPalette(int d, bool quarter)
    {
        var packets = U16(d);
        d += 2;
        var current = 0;
        Array.Clear(Palette);
        for (var i = 0; i < packets; i++)
        {
            current += _data[d++];
            var change = _data[d++];
            var count = change == 0 ? 256 : change;
            for (var k = 0; k < count * 3 && current + k < 768 && d + k < _data.Length; k++)
            {
                var v = _data[d + k];
                Palette[current + k] = (byte)(quarter ? v / 4 : v);
            }

            d += count * 3;
        }
    }

    void DecodeLc(int d)
    {
        var skipLines = U16(d);
        var changeLines = U16(d + 2);
        d += 4;
        for (var line = 0; line < changeLines; line++)
        {
            var row = (skipLines + line) * Width;
            var packets = _data[d++];
            var x = 0;
            for (var p = 0; p < packets; p++)
            {
                x += _data[d++];
                var size = (sbyte)_data[d++];
                if (size < 0)
                {
                    Pixels.AsSpan(row + x, Math.Min(-size, Width - x)).Fill(_data[d++]);
                    x += -size;
                }
                else
                {
                    _data.AsSpan(d, Math.Min(size, Width - x)).CopyTo(Pixels.AsSpan(row + x));
                    d += size;
                    x += size;
                }
            }
        }
    }

    void DecodeBrun(int d)
    {
        for (var line = 0; line < Height; line++)
        {
            var row = line * Width;
            var packets = _data[d++];
            var x = 0;
            for (var p = 0; p < packets && x < Width; p++)
            {
                var size = (sbyte)_data[d++];
                if (size > 0)
                {
                    Pixels.AsSpan(row + x, Math.Min(size, Width - x)).Fill(_data[d++]);
                    x += size;
                }
                else
                {
                    var count = Math.Min(-size, Width - x);
                    _data.AsSpan(d, count).CopyTo(Pixels.AsSpan(row + x));
                    d += -size;
                    x += -size;
                }
            }
        }
    }

    void DecodeSs2(int d)
    {
        var changeLines = U16(d);
        d += 2;
        for (var line = 0; line < changeLines && line < Height; line++)
        {
            var packets = U16(d);
            d += 2;
            while ((packets & 0x8000) != 0)
            {
                if ((packets & 0x4000) != 0)
                {
                    line += Math.Abs((short)packets); // Lines to skip.
                }
                else
                {
                    Pixels[line * Width + Width - 1] = (byte)(packets & 0xff); // An odd width's last pixel.
                }

                packets = U16(d);
                d += 2;
            }

            var row = line * Width;
            var x = 0;
            for (var p = 0; p < packets; p++)
            {
                x += _data[d++];
                var size = (sbyte)_data[d++];
                if (size < 0)
                {
                    var lo = _data[d];
                    var hi = _data[d + 1];
                    d += 2;
                    for (var i = 0; i < -size && x + 1 < Width; i++, x += 2)
                    {
                        Pixels[row + x] = lo;
                        Pixels[row + x + 1] = hi;
                    }
                }
                else
                {
                    var count = Math.Min(2 * size, Width - x);
                    _data.AsSpan(d, count).CopyTo(Pixels.AsSpan(row + x));
                    d += 2 * size;
                    x += 2 * size;
                }
            }
        }
    }
}
