using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Godot;
using U7.Core;

namespace U7.Audio;

/// <summary>
/// Plays the extracted General-MIDI music tracks (<c>assets/audio/music_gm</c>)
/// through the Windows MIDI mapper (the built-in GS wavetable synth), the way
/// Exult's Windows MIDI driver does. A background thread sequences the file.
/// On other platforms music is silent until a soundfont renderer exists.
/// Mirrors Exult <c>MyMidiPlayer</c>: current track, repeat flag, egg count.
/// </summary>
public sealed class MusicPlayer : IDisposable
{
    [DllImport("winmm.dll")]
    static extern int midiOutOpen(out IntPtr handle, uint deviceId, IntPtr callback, IntPtr instance, uint flags);

    [DllImport("winmm.dll")]
    static extern int midiOutShortMsg(IntPtr handle, uint msg);

    [DllImport("winmm.dll")]
    static extern int midiOutReset(IntPtr handle);

    [DllImport("winmm.dll")]
    static extern int midiOutClose(IntPtr handle);

    const uint MidiMapper = 0xFFFFFFFF;

    readonly object _gate = new();
    Thread? _thread;
    volatile bool _stopRequested;
    volatile bool _repeat;
    volatile int _currentTrack = -1;
    IntPtr _device;

    public bool Enabled { get; set; } = true;
    public bool Available => OperatingSystem.IsWindows();
    /// <summary>Track playing, or -1 (Exult <c>get_current_track</c>).</summary>
    public int CurrentTrack => _currentTrack;
    /// <summary>Exult <c>MyMidiPlayer::egg_count</c>: continuous jukebox eggs holding the track.</summary>
    public int EggCount { get; set; }
    public string LastError { get; private set; } = "";

    public bool Repeat
    {
        get => _repeat;
        set => _repeat = value;
    }

    /// <summary>The MT-32 export; patches are converted to General MIDI at load like Exult.</summary>
    public static string TrackPath(int track) =>
        Path.Combine(U7Paths.AssetsDir, "audio", "music_mt32", $"{track:D4}_MT32MUS.MID");

    /// <summary>Exult <c>MyMidiPlayer::start_music(num, repeat)</c>.</summary>
    public bool Start(int track, bool repeat)
    {
        Stop();
        if (!Enabled)
        {
            return false;
        }

        var path = TrackPath(track);
        if (!File.Exists(path))
        {
            LastError = $"music {track}: no file";
            GD.Print(LastError);
            return false;
        }

        if (!Available)
        {
            LastError = "music: no MIDI output on this platform";
            return false;
        }

        List<MidiEvent> events;
        int division;
        try
        {
            events = MidiFile.Load(path, out division);
        }
        catch (Exception ex)
        {
            LastError = $"music {track}: {ex.Message}";
            GD.Print(LastError);
            return false;
        }

        lock (_gate)
        {
            if (_device == IntPtr.Zero && midiOutOpen(out _device, MidiMapper, IntPtr.Zero, IntPtr.Zero, 0) != 0)
            {
                _device = IntPtr.Zero;
                LastError = "music: midiOutOpen failed";
                GD.Print(LastError);
                return false;
            }
        }

        _stopRequested = false;
        _repeat = repeat;
        _currentTrack = track;
        _thread = new Thread(() => Run(events, division)) { IsBackground = true, Name = "u7-midi" };
        _thread.Start();
        return true;
    }

    /// <summary>Exult <c>MyMidiPlayer::stop_music</c>.</summary>
    public void Stop()
    {
        _stopRequested = true;
        var t = _thread;
        if (t is not null && t != Thread.CurrentThread)
        {
            t.Join();
        }

        _thread = null;
        _currentTrack = -1;
        EggCount = 0;
        lock (_gate)
        {
            if (_device != IntPtr.Zero)
            {
                midiOutReset(_device);
            }
        }
    }

    public void Dispose()
    {
        Stop();
        lock (_gate)
        {
            if (_device != IntPtr.Zero)
            {
                midiOutClose(_device);
                _device = IntPtr.Zero;
            }
        }
    }

    void Run(List<MidiEvent> events, int division)
    {
        do
        {
            var sw = Stopwatch.StartNew();
            double usPerTick = 500000.0 / division; // 120 bpm default
            double targetUs = 0;
            long lastTick = 0;
            foreach (var e in events)
            {
                targetUs += (e.Tick - lastTick) * usPerTick;
                lastTick = e.Tick;
                while (!_stopRequested)
                {
                    var remaining = targetUs - sw.Elapsed.TotalMilliseconds * 1000.0;
                    if (remaining <= 0)
                    {
                        break;
                    }

                    if (remaining > 2500)
                    {
                        Thread.Sleep(1);
                    }
                    else
                    {
                        Thread.SpinWait(50);
                    }
                }

                if (_stopRequested)
                {
                    break;
                }

                if (e.Tempo > 0)
                {
                    usPerTick = (double)e.Tempo / division;
                }
                else
                {
                    lock (_gate)
                    {
                        if (_device != IntPtr.Zero)
                        {
                            midiOutShortMsg(_device, e.Message);
                        }
                    }
                }
            }

            if (!_stopRequested)
            {
                lock (_gate)
                {
                    if (_device != IntPtr.Zero)
                    {
                        midiOutReset(_device); // all notes off between loops
                    }
                }
            }
        }
        while (_repeat && !_stopRequested);

        if (!_stopRequested)
        {
            _currentTrack = -1; // finished on its own
        }
    }

    public readonly record struct MidiEvent(long Tick, uint Message, int Tempo);

    /// <summary>
    /// Minimal Standard MIDI File reader: formats 0 and 1, merged into one event
    /// list, with Exult's <c>XMIDIFILE_CONVERT_MT32_TO_GM</c> applied: MT-32 patch
    /// numbers mapped through <c>mt32asgm</c>, no patch changes on the rhythm
    /// channel, bank selects dropped, volumes through the volume curve.
    /// </summary>
    public static class MidiFile
    {
        /// <summary>Exult <c>XMidiFile::mt32asgm</c>.</summary>
        static readonly byte[] Mt32AsGm =
        [
            0, 1, 2, 4, 4, 5, 5, 3, 16, 17, 18, 16, 19, 19, 19, 21,
            6, 6, 6, 7, 7, 7, 8, 8, 62, 63, 62, 63, 38, 39, 38, 39,
            88, 90, 52, 92, 97, 99, 14, 54, 98, 96, 68, 95, 81, 87, 112, 80,
            48, 48, 44, 45, 40, 40, 42, 42, 43, 46, 46, 24, 25, 26, 27, 104,
            32, 32, 33, 34, 36, 37, 35, 35, 73, 73, 72, 72, 74, 75, 64, 65,
            66, 67, 71, 71, 68, 69, 70, 22, 56, 56, 57, 57, 60, 60, 58, 61,
            61, 11, 11, 99, 112, 9, 14, 13, 12, 107, 111, 77, 78, 78, 76, 76,
            47, 117, 116, 118, 118, 116, 115, 119, 115, 112, 55, 124, 123, 94, 98, 121
        ];

        /// <summary>Exult <c>XMidiFile::VolumeCurve</c> (gamma 1 = identity by default).</summary>
        static readonly byte[] VolumeCurve = BuildVolumeCurve(1.0);

        static byte[] BuildVolumeCurve(double gamma)
        {
            var t = new byte[128];
            for (var i = 0; i < 128; i++)
            {
                t[i] = (byte)Math.Min(127, (int)(Math.Pow(i / 128.0, 1 / gamma) * 128));
            }

            return t;
        }

        public static List<MidiEvent> Load(string path, out int division)
        {
            var data = File.ReadAllBytes(path);
            if (data.Length < 14 || data[0] != (byte)'M' || data[1] != (byte)'T' || data[2] != (byte)'h' || data[3] != (byte)'d')
            {
                throw new InvalidDataException("not a MIDI file");
            }

            var tracks = (data[10] << 8) | data[11];
            division = (data[12] << 8) | data[13];
            if ((division & 0x8000) != 0)
            {
                throw new InvalidDataException("SMPTE time division unsupported");
            }

            var events = new List<MidiEvent>();
            var pos = 8 + (((data[4] << 24) | (data[5] << 16) | (data[6] << 8) | data[7]));
            for (var t = 0; t < tracks && pos + 8 <= data.Length; t++)
            {
                if (data[pos] != (byte)'M' || data[pos + 1] != (byte)'T' || data[pos + 2] != (byte)'r' || data[pos + 3] != (byte)'k')
                {
                    break;
                }

                var len = (data[pos + 4] << 24) | (data[pos + 5] << 16) | (data[pos + 6] << 8) | data[pos + 7];
                ReadTrack(data, pos + 8, Math.Min(data.Length, pos + 8 + len), events);
                pos += 8 + len;
            }

            // Exult ApplyFirstState: channels with notes but no volume get CC7 = 90.
            var hasVolume = new bool[16];
            var hasNotes = new bool[16];
            foreach (var e in events)
            {
                if (e.Tempo > 0)
                {
                    continue;
                }

                var ch = (int)(e.Message & 0xF);
                var hi = e.Message & 0xF0;
                if (hi == 0xB0 && ((e.Message >> 8) & 0x7F) == 7)
                {
                    hasVolume[ch] = true;
                }
                else if (hi == 0x90)
                {
                    hasNotes[ch] = true;
                }
            }

            for (var ch = 0; ch < 16; ch++)
            {
                if (hasNotes[ch] && !hasVolume[ch])
                {
                    events.Add(new MidiEvent(0, (uint)(0xB0 | ch | (7 << 8) | (VolumeCurve[90] << 16)), 0));
                }
            }

            // Stable sort by tick keeps each track's own order.
            return events.OrderBy(e => e.Tick).ToList();
        }

        static void ReadTrack(byte[] d, int p, int end, List<MidiEvent> events)
        {
            long tick = 0;
            byte status = 0;
            while (p < end)
            {
                tick += ReadVarLen(d, ref p, end);
                if (p >= end)
                {
                    break;
                }

                var b = d[p];
                if (b == 0xFF)
                {
                    var type = d[p + 1];
                    p += 2;
                    var len = ReadVarLen(d, ref p, end);
                    if (type == 0x51 && len == 3 && p + 3 <= end)
                    {
                        events.Add(new MidiEvent(tick, 0, (d[p] << 16) | (d[p + 1] << 8) | d[p + 2]));
                    }

                    p += (int)len;
                    continue;
                }

                if (b == 0xF0 || b == 0xF7)
                {
                    p++;
                    var len = ReadVarLen(d, ref p, end);
                    p += (int)len;
                    continue;
                }

                if ((b & 0x80) != 0)
                {
                    status = b;
                    p++;
                }

                var hi = status & 0xF0;
                if (hi < 0x80)
                {
                    break; // garbage
                }

                var d1 = p < end ? d[p++] : (byte)0;
                byte d2 = 0;
                if (hi != 0xC0 && hi != 0xD0)
                {
                    d2 = p < end ? d[p++] : (byte)0;
                }

                var ch = status & 0xF;
                switch (hi)
                {
                    case 0xC0 when ch == 9:
                        continue; // Exult: no patch changes on the rhythm channel
                    case 0xC0:
                        d1 = Mt32AsGm[d1 & 0x7F];
                        break;
                    case 0xB0 when d1 is 0 or 32:
                        continue; // bank select: MT-32 banks mean nothing to GM
                    case 0xB0 when d1 == 7:
                        d2 = VolumeCurve[d2 & 0x7F];
                        break;
                    case 0x90 when d2 > 0:
                        d2 = VolumeCurve[d2 & 0x7F];
                        break;
                }

                events.Add(new MidiEvent(tick, (uint)(status | (d1 << 8) | (d2 << 16)), 0));
            }
        }

        static long ReadVarLen(byte[] d, ref int p, int end)
        {
            long v = 0;
            for (var i = 0; i < 4 && p < end; i++)
            {
                var b = d[p++];
                v = (v << 7) | (uint)(b & 0x7F);
                if ((b & 0x80) == 0)
                {
                    break;
                }
            }

            return v;
        }
    }
}
