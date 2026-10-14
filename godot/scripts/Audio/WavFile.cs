using System.Buffers.Binary;

namespace U7.Audio;

/// <summary>A decoded sound: 16-bit samples (left and right in turn when stereo) at a rate.</summary>
public sealed record SfxSample(short[] Data, int Channels, int Rate)
{
    public int Frames => Data.Length / Channels;
    public int LengthMs => (int)(Frames * 1000L / Rate);
}

/// <summary>
/// Exult <c>WavAudioSample</c>: a RIFF WAVE whose size is the file's, its
/// <c>fmt </c> and <c>data</c> chunks found by walking the chunks (unpadded,
/// as Exult walks them), PCM only, mono or stereo, 8-bit (unsigned) or
/// 16-bit (signed) samples.
/// </summary>
public static class WavFile
{
    public static SfxSample? Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12 || !data[..4].SequenceEqual("RIFF"u8) || !data.Slice(8, 4).SequenceEqual("WAVE"u8)
            || BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) != data.Length - 8)
        {
            return null;
        }

        int fmt = 0, fmtSize = 0, start = 0, size = 0;
        var pos = 12;
        while (pos + 8 <= data.Length && (fmt == 0 || start == 0))
        {
            var id = data.Slice(pos, 4);
            var chunk = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(data[(pos + 4)..]), (uint)(data.Length - pos - 8));
            pos += 8;
            if (id.SequenceEqual("fmt "u8))
            {
                (fmt, fmtSize) = (pos, chunk);
            }
            else if (id.SequenceEqual("data"u8))
            {
                (start, size) = (pos, chunk);
            }

            pos += chunk;
        }

        if (fmt == 0 || start == 0 || fmtSize < 0x10)
        {
            return null;
        }

        var tag = BinaryPrimitives.ReadUInt16LittleEndian(data[fmt..]);
        var channels = BinaryPrimitives.ReadUInt16LittleEndian(data[(fmt + 2)..]);
        var rate = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[(fmt + 4)..]);
        var bits = BinaryPrimitives.ReadUInt16LittleEndian(data[(fmt + 14)..]);
        if (tag != 1 || channels is not (1 or 2) || bits is not (8 or 16) || rate <= 0)
        {
            return null;
        }

        var pcm = data.Slice(start, size);
        short[] samples;
        if (bits == 16)
        {
            samples = new short[pcm.Length / 2];
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = BinaryPrimitives.ReadInt16LittleEndian(pcm[(i * 2)..]);
            }
        }
        else
        {
            samples = new short[pcm.Length];
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = (short)((pcm[i] - 128) << 8);
            }
        }

        // A stereo sound's frames are whole pairs.
        if (channels == 2 && samples.Length % 2 != 0)
        {
            Array.Resize(ref samples, samples.Length - 1);
        }

        return samples.Length == 0 ? null : new SfxSample(samples, channels, rate);
    }
}
