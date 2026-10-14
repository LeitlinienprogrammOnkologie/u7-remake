using Godot;

namespace U7.Audio;

/// <summary>
/// Exult <c>AudioMixer</c> and <c>AudioChannel</c> for the digital sound
/// effects: 32 channels (<c>MIXER_CHANNELS</c>) mixed in software into one
/// Godot stream, each with its left and right volume (0-256), its 2D
/// position (<c>calculate2DVolume</c>: the distance, 0-256, fades both
/// sides, the balance, -256 to 256, fades the far one) and its loop count
/// (-1 for ever). A new sound finds a free channel or doesn't play (Exult's
/// priorities are all 0). A thread of its own keeps the stream a little
/// ahead, as Exult's SDL callback does, so a sound starts at once whatever
/// the frame rate. Headless (no sound card) the channels run by the clock,
/// so effects still end.
/// </summary>
public sealed partial class SfxMixer : Node
{
    public const int ChannelCount = 32;
    /// <summary>The mix's rate: Exult's packs are all 22 050 Hz; Godot resamples to the output.</summary>
    const int MixRate = 22050;
    /// <summary>How far ahead the stream is kept (s): a sound starts this late at most.</summary>
    const double Ahead = 0.06;

    sealed class Channel
    {
        public SfxSample? Sample;
        public int Id;
        public double Pos;
        public int Loop;
        public int LVol;
        public int RVol;
        public int Distance;
        public int Balance;
    }

    readonly Channel[] _channels = Enumerable.Range(0, ChannelCount).Select(_ => new Channel()).ToArray();
    readonly object _gate = new();
    readonly bool _headless = DisplayServer.GetName() == "headless";
    AudioStreamGeneratorPlayback? _playback;
    Vector2[] _mix = new Vector2[2048];
    int _capacity;
    Thread? _feeder;
    volatile bool _quit;

    /// <summary>How many channels are playing.</summary>
    public int Busy
    {
        get
        {
            lock (_gate)
            {
                return _channels.Count(c => c.Sample is not null);
            }
        }
    }

    /// <summary>For the console: the stream's state and how often it ran dry.</summary>
    public string Describe() =>
        _headless ? "mixed by the clock (headless)"
        : _playback is null ? "no stream"
        : $"{_capacity - _playback.GetFramesAvailable()} frames queued, {_playback.GetSkips()} skips";

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        if (_headless)
        {
            return;
        }

        var player = new AudioStreamPlayer
        {
            Name = "Effects",
            Stream = new AudioStreamGenerator
            {
                MixRateMode = AudioStreamGenerator.AudioStreamGeneratorMixRate.Custom,
                MixRate = MixRate,
                BufferLength = 0.25f
            }
        };
        AddChild(player);
        player.Play();
        _playback = player.GetStreamPlayback() as AudioStreamGeneratorPlayback;
        if (_playback is null)
        {
            return;
        }

        _capacity = _playback.GetFramesAvailable();
        _feeder = new Thread(Feed) { IsBackground = true, Name = "u7-sfx-mixer" };
        _feeder.Start();
    }

    public override void _ExitTree()
    {
        _quit = true;
        _feeder?.Join();
        _feeder = null;
    }

    public override void _Process(double delta)
    {
        if (_headless)
        {
            lock (_gate)
            {
                Mix((int)(delta * MixRate));
            }
        }
    }

    /// <summary>Exult's audio callback: the stream topped up to <see cref="Ahead"/> every few milliseconds.</summary>
    void Feed()
    {
        var target = (int)(Ahead * MixRate);
        while (!_quit)
        {
            var count = target - (_capacity - _playback!.GetFramesAvailable());
            if (count > 0)
            {
                lock (_gate)
                {
                    Mix(count);
                }

                _playback.PushBuffer(_mix.AsSpan(0, count));
            }

            Thread.Sleep(5);
        }
    }

    /// <summary>Exult <c>AudioMixer::playSample</c> and <c>set2DPosition</c>: the sample on a free channel under <paramref name="id"/>; false if every channel is busy.</summary>
    public bool Play(int id, SfxSample sample, int loop, int lvol, int rvol, int distance, int balance)
    {
        lock (_gate)
        {
            var ch = Array.Find(_channels, c => c.Sample is null);
            if (ch is null)
            {
                return false;
            }

            (ch.Sample, ch.Id, ch.Pos, ch.Loop) = (sample, id, 0, loop);
            (ch.LVol, ch.RVol, ch.Distance, ch.Balance) = (lvol, rvol, distance, balance);
            return true;
        }
    }

    /// <summary>Exult <c>AudioMixer::isPlaying</c>.</summary>
    public bool IsPlaying(int id)
    {
        lock (_gate)
        {
            return Find(id) is not null;
        }
    }

    /// <summary>Exult <c>AudioMixer::set2DPosition</c>: whether the sound still plays.</summary>
    public bool Set2DPosition(int id, int distance, int balance)
    {
        lock (_gate)
        {
            if (Find(id) is not { } ch)
            {
                return false;
            }

            (ch.Distance, ch.Balance) = (distance, balance);
            return true;
        }
    }

    /// <summary>Exult <c>AudioMixer::stopSample</c>.</summary>
    public void Stop(int id)
    {
        lock (_gate)
        {
            if (Find(id) is { } ch)
            {
                ch.Sample = null;
            }
        }
    }

    public void StopAll()
    {
        lock (_gate)
        {
            foreach (var ch in _channels)
            {
                ch.Sample = null;
            }
        }
    }

    Channel? Find(int id) => id < 0 ? null : Array.Find(_channels, c => c.Sample is not null && c.Id == id);

    /// <summary>Exult <c>AudioChannel::resampleAndMix</c>: the next <paramref name="count"/> frames of every channel, summed.</summary>
    void Mix(int count)
    {
        if (_mix.Length < count)
        {
            _mix = new Vector2[count];
        }

        Array.Clear(_mix, 0, count);
        foreach (var ch in _channels)
        {
            if (ch.Sample is { } sample)
            {
                MixChannel(ch, sample, count);
            }
        }
    }

    void MixChannel(Channel ch, SfxSample sample, int count)
    {
        var (lvol, rvol) = Volume2D(ch);
        var left = lvol / (256f * 32768f);
        var right = rvol / (256f * 32768f);
        var step = sample.Rate / (double)MixRate;
        var frames = sample.Frames;
        var data = sample.Data;
        var stereo = sample.Channels == 2;
        for (var i = 0; i < count; i++)
        {
            if (ch.Pos >= frames)
            {
                // Exult DecompressNextFrame: the end loops back while loops are left.
                if (ch.Loop == 0)
                {
                    ch.Sample = null;
                    return;
                }

                if (ch.Loop > 0)
                {
                    ch.Loop--;
                }

                ch.Pos %= frames;
            }

            var at = (int)ch.Pos;
            float l, r;
            if (step == 1.0)
            {
                l = stereo ? data[at * 2] : data[at];
                r = stereo ? data[at * 2 + 1] : l;
            }
            else
            {
                // Exult's channels interpolate a sample at another rate; linearly here.
                var next = Math.Min(at + 1, frames - 1);
                var t = (float)(ch.Pos - at);
                l = stereo ? Lerp(data[at * 2], data[next * 2], t) : Lerp(data[at], data[next], t);
                r = stereo ? Lerp(data[at * 2 + 1], data[next * 2 + 1], t) : l;
            }

            _mix[i] += new Vector2(l * left, r * right);
            ch.Pos += step;
        }
    }

    static float Lerp(short a, short b, float t) => a + (b - a) * t;

    /// <summary>Exult <c>AudioChannel::calculate2DVolume</c>: each side's volume (0-256) after the distance and the balance.</summary>
    static (int L, int R) Volume2D(Channel ch)
    {
        if (ch.Distance > 255)
        {
            return (0, 0);
        }

        var lbal = 256;
        var rbal = 256;
        if (ch.Balance < 0)
        {
            rbal = ch.Balance < -256 ? 0 : ch.Balance + 256;
        }
        else if (ch.Balance > 0)
        {
            lbal = ch.Balance > 256 ? 0 : 256 - ch.Balance;
        }

        return (ch.LVol * (256 - ch.Distance) * lbal / 65536, ch.RVol * (256 - ch.Distance) * rbal / 65536);
    }
}
