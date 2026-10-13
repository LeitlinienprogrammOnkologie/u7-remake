using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Godot;
using U7.Core;
using U7.Data;

namespace U7.Audio;

/// <summary>
/// Exult <c>Audio::play_sound_effect</c> and <c>MyMidiPlayer::start_sound_effect</c>:
/// the sound effects. The originals have no digital effects (their sounds
/// came from the MIDI synthesiser), and Exult's digital packs are its own
/// recordings, so the effects here are Exult's MIDI ones (<c>midisfx.flx</c>,
/// GPL, in godot/data): one XMIDI sequence an effect, indexed by Serpent
/// Isle's numbering, Black Gate's numbers mapped through Exult's
/// <c>bgconv</c>. An effect plays through the music's MIDI device as a
/// sequence of its own, its channels moved onto channels 11-16, which the
/// music leaves alone, so the two don't fight over programs. Exult's MIDI
/// effects know no volume or position; here a note's velocity falls with
/// the distance from the view's centre as Exult's digital ones do
/// (<c>get_2d_position_for_tile</c>, silent beyond <see cref="MaxFalloff"/>
/// tiles), and Exult's background noise (<c>Background_noise</c>: birds by
/// day, crickets by night, outside) is ported too.
/// </summary>
public sealed class SoundEffects : IDisposable
{
    /// <summary>Exult <c>MAX_SOUND_FALLOFF</c>: tiles from the view's centre beyond which an effect is silent.</summary>
    public const int MaxFalloff = 24;
    /// <summary>The most an effect lasts: a note held longer is cut, as Exult's next effect would cut it.</summary>
    public const int MaxSeconds = 10;

    /// <summary>
    /// Exult <c>audio/conv.cc</c> <c>bgconv</c>: Black Gate's effect numbers
    /// to Serpent Isle's (and the MIDI effects' entries). 135 is silence.
    /// </summary>
    static readonly int[] BgConv =
    [
        12, 80, 9, 11, 125, 61, 92, 40, 41, 42, 127, 71, 44, 65, 94, 56, 121, 117, 118, 16, 45, 46, 47, 28, 30, 72, 78, 68, 70, 135,
        32, 31, 89, 88, 35, 34, 135, 95, 99, 126, 37, 18, 17, 2, 1, 49, 33, 7, 50, 109, 107, 133, 129, 135, 114, 110, 111, 112, 113, 132,
        39, 22, 116, 128, 64, 20, 67, 130, 57, 48, 84, 82, 83, 134, 134, 108, 43, 55, 54, 26, 101, 115, 96, 135, 135, 15, 10, 52, 124, 135,
        36, 38, 135, 135, 135, 135, 135, 100, 79, 59, 93, 135, 73, 103, 75, 86, 65, 62, 131, 90, 91, 66, 21, 5, 74, 255, 136
    ];

    /// <summary>The physical MIDI channels effects play on (0-based 10-15; the music keeps the rest, the drums stay on 9).</summary>
    static readonly int[] Pool = [10, 11, 12, 13, 14, 15];

    /// <summary>One decoded effect: its events at 120 Hz ticks, and the logical channels it plays notes on (the others carry setup alone and are dropped).</summary>
    sealed record Effect(List<(int Tick, uint Msg)> Events, int Channels);

    /// <summary>A play put off (Exult <c>Object_sfx::Play</c> with a delay: a weapon's sound part way through the swing).</summary>
    sealed record Pending(int Num, U7Object Obj, double DueSeconds);

    readonly MusicPlayer _midi;
    readonly Dictionary<int, Effect?> _effects = new();
    readonly FlexFile? _flex;
    readonly bool[] _busy = new bool[16];
    readonly object _gate = new();
    readonly List<Pending> _pending = [];
    readonly Random _rng = new();
    double _now;
    double _ambientDue = 5;
    int _ambientRepeats;
    int _ambientLast = -1;

    public bool Enabled { get; set; } = true;
    /// <summary>Whether effects can sound: a MIDI device and the effects file.</summary>
    public bool Available => _midi.Available && _flex is not null;
    /// <summary>The view's centre (Exult's camera actor's tile), for the distance an effect falls off with.</summary>
    public TileCoord ViewCentre { get; set; }
    public string LastError { get; private set; } = "";
    /// <summary>How many effects have been started (for the console).</summary>
    public int Started { get; private set; }
    /// <summary>The last effect started: its game number, MIDI entry and volume (for the console).</summary>
    public string Last { get; private set; } = "none";
    /// <summary>How many of the effects' channels are playing (for the console).</summary>
    public int Busy
    {
        get
        {
            lock (_gate)
            {
                var n = 0;
                foreach (var p in Pool)
                {
                    n += _busy[p] ? 1 : 0;
                }

                return n;
            }
        }
    }

    public SoundEffects(MusicPlayer midi)
    {
        _midi = midi;
        var path = Path.Combine(U7Paths.RepoRoot, "godot", "data", "midisfx.flx");
        try
        {
            _flex = File.Exists(path) ? new FlexFile(path) : null;
            if (_flex is null)
            {
                LastError = "sfx: no godot/data/midisfx.flx";
            }
        }
        catch (Exception e)
        {
            LastError = $"sfx: {e.Message}";
        }
    }

    /// <summary>Exult <c>Audio::game_sfx</c>: a number Exult's code gives in Black Gate's numbering, as this game numbers it.</summary>
    public static int GameSfx(int bgNumber) =>
        GameContext.IsBg || bgNumber < 0 || bgNumber >= BgConv.Length ? bgNumber : BgConv[bgNumber];

    /// <summary>Exult <c>start_sound_effect</c>: the MIDI effects' entry for a game number (Black Gate's through <c>bgconv</c>).</summary>
    static int MidiIndex(int num) =>
        GameContext.IsBg ? num >= 0 && num < BgConv.Length ? BgConv[num] : -1 : num;

    /// <summary>Exult <c>play_sound_effect(num)</c>: an effect at full volume, wherever the player is.</summary>
    public bool Play(int num, float volume = 1f) => PlayFor(num, volume) > 0;

    /// <summary>Plays an effect and tells how long it lasts in milliseconds (0 if it didn't start).</summary>
    public int PlayFor(int num, float volume)
    {
        if (!Enabled || _flex is null || !_midi.Available || volume <= 0f)
        {
            return 0;
        }

        var index = MidiIndex(num);
        if (index < 0 || index >= _flex.Count || !_midi.EnsureDevice())
        {
            return 0;
        }

        if (!_effects.TryGetValue(index, out var effect))
        {
            try
            {
                effect = Xmidi.Decode(_flex.Get(index));
            }
            catch (Exception e)
            {
                LastError = $"sfx {num} (entry {index}): {e.Message}";
                effect = null;
            }

            _effects[index] = effect;
        }

        if (effect is null || effect.Events.Count == 0)
        {
            LastError = effect is null ? LastError : $"sfx {num}: entry {index} has no events";
            return 0;
        }

        int[]? map;
        lock (_gate)
        {
            map = Allocate(effect.Channels);
        }

        if (map is null)
        {
            LastError = $"sfx {num}: every effect channel busy";
            return 0;
        }

        Started++;
        Last = $"{num} (entry {index}, {effect.Events.Count} events, {effect.Events[^1].Tick * 1000 / 120} ms) at {volume:0.00}";
        var thread = new Thread(() => Run(effect, map, volume)) { IsBackground = true, Name = "u7-sfx" };
        thread.Start();
        return Math.Max(1, effect.Events[^1].Tick * 1000 / 120);
    }

    /// <summary>Exult <c>play_sound_effect(num, tile)</c>: an effect at a tile, its volume falling with the distance from the view's centre.</summary>
    public bool PlayAt(int num, TileCoord at, float scale = 1f) => Play(num, VolumeAt(at) * scale);

    /// <summary>Exult <c>play_sound_effect(num, obj)</c>: at the object's outermost owner's centre; the length in milliseconds, 0 if it didn't start.</summary>
    public int PlayAt(int num, U7Object obj, float scale = 1f)
    {
        var outer = obj;
        while (outer.Container is { } c)
        {
            outer = c;
        }

        return PlayFor(num, VolumeAt(new TileCoord(outer.Tx, outer.Ty, outer.Tz + outer.DimZ / 2)) * scale);
    }

    /// <summary>Exult <c>Object_sfx::Play(obj, sfx, delay)</c>: the effect at the object after <paramref name="delayMs"/>.</summary>
    public void PlayLater(int num, U7Object obj, int delayMs)
    {
        if (delayMs <= 0)
        {
            PlayAt(num, obj);
            return;
        }

        _pending.Add(new Pending(num, obj, _now + delayMs / 1000.0));
    }

    /// <summary>
    /// Exult <c>get_2d_position_for_tile</c>: how loud an effect at a tile
    /// is, 1 at the view's centre, falling with the squared screen-space
    /// distance to nothing at <see cref="MaxFalloff"/> tiles.
    /// </summary>
    public float VolumeAt(TileCoord at)
    {
        var c = ViewCentre;
        // Exult square_distance_screen_space: a lift shows half a tile up and left.
        var dx = U7Constants.TileDelta(c.Tx - c.Tz / 2, at.Tx - at.Tz / 2);
        var dy = U7Constants.TileDelta(c.Ty - c.Tz / 2, at.Ty - at.Tz / 2);
        var sqr = dx * dx + dy * dy;
        return sqr > MaxFalloff * MaxFalloff ? 0f : 1f - (float)sqr / (MaxFalloff * MaxFalloff);
    }

    /// <summary>The delayed plays due, and Exult's background noise; called every frame.</summary>
    public void Update(double delta, bool outside, bool night, bool musicPlaying)
    {
        _now += delta;
        for (var i = _pending.Count - 1; i >= 0; i--)
        {
            var p = _pending[i];
            if (_now >= p.DueSeconds)
            {
                _pending.RemoveAt(i);
                if (!p.Obj.Removed)
                {
                    PlayAt(p.Num, p.Obj);
                }
            }
        }

        Ambient(outside, night, musicPlaying);
    }

    /// <summary>
    /// Exult <c>Background_noise::handle_event</c>: outside and not in a
    /// dungeon, birds by day (effects 82 and 85, only while no music plays)
    /// and crickets by night (61, or nothing), a little quieter than full,
    /// now and then repeated after half a second to a second and a half,
    /// else four to seven seconds apart.
    /// </summary>
    void Ambient(bool outside, bool night, bool musicPlaying)
    {
        if (_now < _ambientDue)
        {
            return;
        }

        if (!outside)
        {
            _ambientDue = _now + 1;
            _ambientRepeats = 0;
            return;
        }

        int sound;
        if (_ambientRepeats > 0)
        {
            sound = _ambientLast;
        }
        else
        {
            ReadOnlySpan<int> bgNight = [61, 61, -1];
            ReadOnlySpan<int> bgDay = [82, 85, 85];
            sound = night ? bgNight[_rng.Next(bgNight.Length)] : !musicPlaying ? bgDay[_rng.Next(bgDay.Length)] : -1;
            _ambientLast = sound;
        }

        if (sound >= 0)
        {
            Play(GameSfx(sound), 0.75f);
            _ambientRepeats++;
            if (_rng.Next(_ambientRepeats + 1) == 0)
            {
                _ambientDue = _now + 0.5 + _rng.NextDouble();
                return;
            }
        }

        _ambientRepeats = 0;
        _ambientDue = _now + 4 + _rng.NextDouble() * 3;
    }

    /// <summary>Physical channels for an effect's logical ones (the drums stay on 9), or null when too many effects play.</summary>
    int[]? Allocate(int channels)
    {
        var map = new int[16];
        Array.Fill(map, -1);
        for (var ch = 0; ch < 16; ch++)
        {
            if ((channels & (1 << ch)) == 0)
            {
                continue;
            }

            if (ch == 9)
            {
                map[ch] = 9;
                continue;
            }

            var found = -1;
            foreach (var p in Pool)
            {
                if (!_busy[p])
                {
                    found = p;
                    break;
                }
            }

            if (found < 0)
            {
                foreach (var m in map)
                {
                    if (m >= 0 && m != 9)
                    {
                        _busy[m] = false;
                    }
                }

                return null;
            }

            _busy[found] = true;
            map[ch] = found;
        }

        return map;
    }

    void Run(Effect effect, int[] map, float volume)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            foreach (var (tick, msg) in effect.Events)
            {
                var targetMs = tick * 1000.0 / 120.0;
                while (sw.Elapsed.TotalMilliseconds < targetMs)
                {
                    var remaining = targetMs - sw.Elapsed.TotalMilliseconds;
                    if (remaining > 2.5)
                    {
                        Thread.Sleep(1);
                    }
                    else
                    {
                        Thread.SpinWait(50);
                    }
                }

                var ch = (int)(msg & 0xF);
                if (map[ch] < 0)
                {
                    continue; // a channel without notes: setup for nothing, kept off the music's channels
                }

                var m = (msg & ~0xFu) | (uint)map[ch];
                if ((msg & 0xF0) == 0x90 && volume < 1f)
                {
                    var vel = (int)((msg >> 16) & 0x7F);
                    m = (m & 0xFFFFu) | (uint)Math.Clamp((int)(vel * volume), 1, 127) << 16;
                }

                _midi.Send(m);
            }
        }
        finally
        {
            lock (_gate)
            {
                for (var ch = 0; ch < 16; ch++)
                {
                    if (map[ch] >= 0 && map[ch] != 9)
                    {
                        _midi.Send((uint)(0xB0 | map[ch] | (123 << 8))); // all notes off
                        _busy[map[ch]] = false;
                    }
                }
            }
        }
    }

    public void Dispose()
    {
        _pending.Clear();
    }

    /// <summary>
    /// Exult <c>XMidiFile::ConvertFiletoList</c> for an XMIDI file: the
    /// EVNT chunk's events, a delta byte below 0x80 adding to the time (in
    /// 120ths of a second), a note on carrying its length after the
    /// velocity (so a note off is made for it), tempo events ignored (XMIDI
    /// doesn't use them), XMIDI's own controllers (110 and up) dropped.
    /// </summary>
    static class Xmidi
    {
        public static Effect Decode(ReadOnlySpan<byte> data)
        {
            var (start, end) = FindEvents(data);
            var events = new List<(int Tick, uint Msg)>();
            var channels = 0;
            var time = 0;
            var p = start;
            while (p < end)
            {
                var b = data[p++];
                if (b < 0x80)
                {
                    time += b;
                    continue;
                }

                var status = b;
                var type = status & 0xF0;
                var ch = status & 0xF;
                switch (type)
                {
                    case 0x90:
                    {
                        var note = data[p++];
                        var vel = data[p++];
                        var duration = VarLen(data, ref p, end);
                        channels |= 1 << ch;
                        events.Add((time, (uint)(status | (note << 8) | (vel << 16))));
                        events.Add((time + duration, (uint)(0x80 | ch | (note << 8) | (0x40 << 16))));
                        break;
                    }
                    case 0x80:
                    case 0xA0:
                    case 0xE0:
                    {
                        var d1 = data[p++];
                        var d2 = data[p++];
                        events.Add((time, (uint)(status | (d1 << 8) | (d2 << 16))));
                        break;
                    }
                    case 0xB0:
                    {
                        var d1 = data[p++];
                        var d2 = data[p++];
                        if (d1 < 110)
                        {
                            events.Add((time, (uint)(status | (d1 << 8) | (d2 << 16))));
                        }

                        break;
                    }
                    case 0xC0:
                    case 0xD0:
                    {
                        var d1 = data[p++];
                        events.Add((time, (uint)(status | (d1 << 8))));
                        break;
                    }
                    case 0xF0:
                    {
                        if (status == 0xFF)
                        {
                            var meta = data[p++];
                            var len = VarLen(data, ref p, end);
                            if (meta == 0x2F)
                            {
                                p = end;
                                break;
                            }

                            p += len;
                        }
                        else
                        {
                            var len = VarLen(data, ref p, end);
                            p += len;
                        }

                        break;
                    }
                    default:
                        p = end;
                        break;
                }
            }

            // A stable sort: a note's off lands after any note on at the same tick. An effect ends after MaxSeconds
            // (the birds are one tweet held for minutes, which Exult's single effect sequence cuts off at the next
            // effect; the closing all-notes-off ends a held note here).
            var cap = MaxSeconds * 120;
            return new Effect(events.Select(e => e.Tick > cap ? (Tick: cap, e.Msg) : e).OrderBy(e => e.Tick).ToList(), channels);
        }

        /// <summary>The EVNT chunk inside the IFF (FORM XDIR, CAT XMID, FORM XMID): its data's start and end.</summary>
        static (int Start, int End) FindEvents(ReadOnlySpan<byte> data)
        {
            var p = 0;
            while (p + 8 <= data.Length)
            {
                var id = System.Text.Encoding.ASCII.GetString(data.Slice(p, 4));
                var size = (data[p + 4] << 24) | (data[p + 5] << 16) | (data[p + 6] << 8) | data[p + 7];
                if (id is "FORM" or "CAT ")
                {
                    p += 12; // into the container, past its type
                    continue;
                }

                if (id == "EVNT")
                {
                    return (p + 8, Math.Min(data.Length, p + 8 + size));
                }

                p += 8 + ((size + 1) & ~1);
            }

            throw new InvalidDataException("no EVNT chunk");
        }

        static int VarLen(ReadOnlySpan<byte> d, ref int p, int end)
        {
            var v = 0;
            for (var i = 0; i < 4 && p < end; i++)
            {
                var b = d[p++];
                v = (v << 7) | (b & 0x7F);
                if ((b & 0x80) == 0)
                {
                    break;
                }
            }

            return v;
        }
    }
}
