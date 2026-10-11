using Godot;

namespace U7.Audio;

/// <summary>
/// Exult <c>VocAudioSample</c>: a Creative Voice File from 0x1A on, its
/// sound blocks' 8-bit samples at the first block's rate (1 000 000 / (256 -
/// time constant)). Exult stops at a silence block (its length counts as 0).
/// Black Gate's speech is all uncompressed; compressed blocks are left out.
/// </summary>
public static class VocFile
{
    const int DataStart = 0x1A;

    /// <summary>The sound as a Godot stream, or null if it isn't a VOC (an IFF entry's "voc" header is skipped, Exult <c>ExVoiceBuffer</c>).</summary>
    public static AudioStreamWav? Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 8 && data[..3].SequenceEqual("voc"u8))
        {
            data = data[8..];
        }

        if (data.Length <= DataStart || !data[..19].SequenceEqual("Creative Voice File"u8))
        {
            return null;
        }

        var samples = new List<byte>(data.Length);
        var rate = 0;
        var compression = 0;
        var pos = DataStart;
        while (pos + 4 <= data.Length)
        {
            var type = data[pos++];
            if (type == 0)
            {
                break;
            }

            var length = data[pos] | data[pos + 1] << 8 | data[pos + 2] << 16;
            pos += 3;
            if (type == 1)
            {
                if (rate == 0 && data[pos] < 255)
                {
                    rate = 1_000_000 / (256 - data[pos]);
                }

                compression = data[pos + 1];
                pos += 2;
                length -= 2;
            }
            else if (type == 3)
            {
                break;
            }

            length = Math.Min(length, data.Length - pos);
            if (length <= 0)
            {
                break;
            }

            if (type is 1 or 2 && compression == 0)
            {
                // Godot's 8-bit samples are signed; the VOC's are unsigned.
                foreach (var b in data.Slice(pos, length))
                {
                    samples.Add((byte)(b ^ 0x80));
                }
            }

            pos += length;
        }

        if (rate == 0 || samples.Count == 0)
        {
            return null;
        }

        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format8Bits,
            MixRate = rate,
            Stereo = false,
            Data = samples.ToArray()
        };
    }
}
