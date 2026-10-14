using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Godot;
using U7.Core;
using U7.Data;

namespace U7.Audio;

/// <summary>
/// Exult <c>Audio</c>'s sound effects (<c>Init_sfx</c>,
/// <c>play_sound_effect</c>, <c>update_sound_effect</c>): from Exult's
/// digital packs when they are in <c>assets/sfx</c> (Simon Quinn's Roland
/// MT-32 recordings first, then Joseph Morris's SoundBlaster ones, Exult's
/// order; WAVs indexed by the game's own numbers, mixed by
/// <see cref="SfxMixer"/> with Exult's distance and balance), else from
/// Exult's MIDI effects (<c>midisfx.flx</c>, GPL, in godot/data): one XMIDI
/// sequence an effect, indexed by Serpent Isle's numbering, Black Gate's
/// numbers mapped through Exult's <c>bgconv</c>, played through the music's
/// MIDI device as a sequence of its own, its channels moved onto channels
/// 11-16, which the music leaves alone. Exult's MIDI effects know no volume
/// or position; here a note's velocity falls with the distance as the
/// digital ones do. A played effect gets a channel number that tells
/// whether it still plays, follows its object and stops (Exult
/// <c>Object_sfx</c>, <c>Shape_sfx</c>). Exult's background noise
/// (<c>Background_noise</c>: birds by day, crickets by night, outside) is
/// ported too.
/// </summary>
public sealed class SoundEffects : IDisposable
{
    /// <summary>Exult <c>MAX_SOUND_FALLOFF</c>: tiles from the view's centre beyond which an effect is silent.</summary>
    public const int MaxFalloff = 24;
    /// <summary>The most a MIDI effect lasts: a note held longer is cut, as Exult's next effect would cut it.</summary>
    public const int MaxSeconds = 10;
    /// <summary>Exult <c>AUDIO_MAX_VOLUME</c>.</summary>
    const int MaxVolume = 256;
    /// <summary>A MIDI effect's repeats (the clock's strikes) are this far apart: Exult's MIDI effects play once whatever the repeat.</summary>
    const int MidiRepeatMs = 700;

    /// <summary>Exult <c>fnames.h</c> <c>SFX_ROLAND_*</c> and <c>SFX_BLASTER_*</c>: the digital packs in <c>Init_sfx</c>'s order, by game.</summary>
    static readonly (string Name, string Bg, string Si)[] Packs =
    [
        ("roland", "sqsfxbg.flx", "sqsfxsi.flx"),
        ("blaster", "jmsfx.flx", "jmsisfx.flx")
    ];

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

    /// <summary>
    /// Exult <c>Object_sfx</c>: an effect that follows its object, started
    /// when due (a weapon's sound part way through the swing) if the object
    /// is in range, its position kept up, stopped out of range.
    /// </summary>
    sealed class Follower(int num, U7Object obj, double due)
    {
        public readonly int Num = num;
        public readonly U7Object Obj = obj;
        public readonly double Due = due;
        public int Channel = -1;
        public TileCoord LastPos;
    }

    /// <summary>A MIDI effect playing: when it ends, and whether it was stopped.</summary>
    sealed class MidiPlay
    {
        public double End;
        public volatile bool Stopped;
    }

    readonly MusicPlayer _midi;
    readonly SfxMixer? _mixer;
    readonly Dictionary<int, Effect?> _effects = new();
    readonly Dictionary<int, SfxSample?> _samples = new();
    readonly Dictionary<int, MidiPlay> _midiPlays = new();
    readonly FlexFile? _flex;
    FlexFile? _wave;
    readonly bool[] _busy = new bool[16];
    readonly object _gate = new();
    readonly List<Follower> _followers = [];
    readonly List<(int Channel, int Num, float Volume, double Due)> _midiRepeats = [];
    readonly Random _rng = new();
    double _now;
    double _ambientDue = 5;
    int _ambientRepeats;
    int _ambientLast = -1;
    int _nextChannel;
    bool _enabled = true;

    /// <summary>Exult's <c>effects_enabled</c>; turned off, every effect stops.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (!value)
            {
                StopAll();
            }
        }
    }

    /// <summary>The player's choice: <c>auto</c> (Exult's order: Roland, SoundBlaster, then the MIDI effects), <c>roland</c>, <c>blaster</c> or <c>midi</c>.</summary>
    public string Choice { get; private set; } = "auto";
    /// <summary>What plays: <c>roland</c>, <c>blaster</c> or <c>midi</c>.</summary>
    public string Pack { get; private set; } = "midi";
    /// <summary>Whether effects can sound: a digital pack, or a MIDI device and the MIDI effects.</summary>
    public bool Available => _wave is not null || _midi.Available && _flex is not null;
    /// <summary>The view's centre (Exult's camera actor's tile), for the distance an effect falls off with.</summary>
    public TileCoord ViewCentre { get; set; }
    public string LastError { get; private set; } = "";
    /// <summary>How many effects have been started (for the console).</summary>
    public int Started { get; private set; }
    /// <summary>The last effect started: its game number, entry and volume (for the console).</summary>
    public string Last { get; private set; } = "none";
    /// <summary>The digital mixer's state (for the console).</summary>
    public string MixerState => _mixer?.Describe() ?? "no mixer";
    /// <summary>How many of the effects' channels are playing (for the console).</summary>
    public int Busy
    {
        get
        {
            if (_wave is not null)
            {
                return _mixer?.Busy ?? 0;
            }

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

    public SoundEffects(MusicPlayer midi, SfxMixer? mixer, string choice)
    {
        _midi = midi;
        _mixer = mixer;
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

        Choose(choice);
    }

    /// <summary>
    /// Exult <c>Init_sfx</c>: the digital pack chosen, or the first of
    /// Exult's order found in <c>assets/sfx</c> (the game's own: Serpent
    /// Isle's packs are numbered as it is); without one, the MIDI effects.
    /// </summary>
    public void Choose(string choice)
    {
        StopAll();
        _wave = null;
        _samples.Clear();
        Choice = choice is "roland" or "blaster" or "midi" ? choice : "auto";
        Pack = "midi";
        if (_mixer is null || Choice == "midi")
        {
            return;
        }

        foreach (var (name, bg, si) in Packs)
        {
            var path = Path.Combine(U7Paths.AssetsDir, "sfx", GameContext.IsBg ? bg : si);
            if (Choice != "auto" && Choice != name || !File.Exists(path))
            {
                continue;
            }

            try
            {
                _wave = new FlexFile(path);
                Pack = name;
                return;
            }
            catch (Exception e)
            {
                LastError = $"sfx: {Path.GetFileName(path)}: {e.Message}";
            }
        }
    }

    /// <summary>Exult <c>Audio::game_sfx</c>: a number Exult's code gives in Black Gate's numbering, as this game numbers it.</summary>
    public static int GameSfx(int bgNumber) =>
        GameContext.IsBg || bgNumber < 0 || bgNumber >= BgConv.Length ? bgNumber : BgConv[bgNumber];

    /// <summary>Exult <c>start_sound_effect</c>: the MIDI effects' entry for a game number (Black Gate's through <c>bgconv</c>).</summary>
    static int MidiIndex(int num) =>
        GameContext.IsBg ? num >= 0 && num < BgConv.Length ? BgConv[num] : -1 : num;

    /// <summary>Exult <c>play_sound_effect(num, volume)</c>: an effect at the view's centre.</summary>
    public bool Play(int num, float volume = 1f) => Start(num, volume, 0, 0, 0) >= 0;

    /// <summary>Exult <c>play_sound_effect(num, tile, volume, repeat)</c>: an effect at a tile; its channel, -1 if it didn't start.</summary>
    public int PlayAt(int num, TileCoord at, float scale = 1f, int repeat = 0)
    {
        var (distance, balance) = Position(at);
        return Start(num, scale, repeat, Math.Min(distance, MaxVolume), balance);
    }

    /// <summary>Exult <c>play_sound_effect(num, obj, volume, repeat)</c>: at the object's outermost owner's centre.</summary>
    public int PlayAt(int num, U7Object obj, float scale = 1f, int repeat = 0) => PlayAt(num, CentreOf(obj), scale, repeat);

    /// <summary>
    /// Exult <c>Object_sfx::Play(obj, sfx, delay)</c>: the effect at the
    /// object after <paramref name="delayMs"/> (at once for 0), following it
    /// while it plays.
    /// </summary>
    public void PlayOn(int num, U7Object obj, int delayMs = 20)
    {
        var follower = new Follower(num, obj, _now + delayMs / 1000.0);
        if (delayMs <= 0)
        {
            Follow(follower);
            if (follower.Channel == -1)
            {
                return;
            }
        }

        _followers.Add(follower);
    }

    /// <summary>Exult <c>AudioMixer::isPlaying</c>: whether a channel <see cref="PlayAt(int, U7Object, float, int)"/> gave still plays.</summary>
    public bool IsPlaying(int channel)
    {
        if (channel < 0)
        {
            return false;
        }

        if (_midiPlays.TryGetValue(channel, out var play))
        {
            return !play.Stopped && _now < play.End;
        }

        return _mixer?.IsPlaying(channel) ?? false;
    }

    /// <summary>Exult <c>update_sound_effect(chan, obj)</c>: the channel's effect moved to the object, stopped if out of range; the channel, or -1.</summary>
    public int Reposition(int channel, U7Object obj) => Reposition(channel, CentreOf(obj));

    /// <summary>Exult <c>update_sound_effect(chan, tile)</c>.</summary>
    int Reposition(int channel, TileCoord at)
    {
        var (distance, balance) = Position(at);
        if (distance > MaxVolume)
        {
            Stop(channel);
            return -1;
        }

        if (_midiPlays.ContainsKey(channel))
        {
            return IsPlaying(channel) ? channel : -1;
        }

        return _mixer?.Set2DPosition(channel, distance, balance) == true ? channel : -1;
    }

    /// <summary>Exult <c>Get_sfx_out_of_range</c>: whether the object is too far from the view's centre to be heard.</summary>
    public bool OutOfRange(U7Object obj) => Position(CentreOf(obj)).Distance > MaxVolume;

    /// <summary>Exult <c>stop_sound_effect</c>.</summary>
    public void Stop(int channel)
    {
        if (_midiPlays.Remove(channel, out var play))
        {
            play.Stopped = true;
        }
        else if (channel >= 0)
        {
            _mixer?.Stop(channel);
        }
    }

    /// <summary>Exult <c>stop_sound_effects</c>.</summary>
    public void StopAll()
    {
        foreach (var play in _midiPlays.Values)
        {
            play.Stopped = true;
        }

        _midiPlays.Clear();
        _midiRepeats.Clear();
        _followers.Clear();
        _mixer?.StopAll();
    }

    /// <summary>
    /// Exult <c>play_sound_effect(num, volume, balance, repeat, distance)</c>:
    /// the pack's sound (<c>play_wave_sfx</c>), or the MIDI effect
    /// (<c>start_sound_effect</c>, its velocity scaled by the distance); the
    /// channel, -1 if nothing started.
    /// </summary>
    int Start(int num, float volume, int repeat, int distance, int balance)
    {
        if (!Enabled || volume <= 0f)
        {
            return -1;
        }

        if (_wave is { } wave)
        {
            return StartWave(wave, num, volume, repeat, distance, balance);
        }

        volume = volume * (MaxVolume - distance) / MaxVolume;
        var play = new MidiPlay();
        var length = PlayMidi(num, volume, play);
        if (length <= 0)
        {
            return -1;
        }

        var channel = _nextChannel++;
        for (var i = 1; i <= repeat; i++)
        {
            _midiRepeats.Add((channel, num, volume, _now + i * MidiRepeatMs / 1000.0));
        }

        play.End = _now + (Math.Max(length, 100) + Math.Max(repeat, 0) * MidiRepeatMs) / 1000.0;
        _midiPlays[channel] = play;
        return channel;
    }

    /// <summary>Exult <c>play_wave_sfx</c>: the pack's entry (cached, Exult's <c>SFX_cache_manager</c>) on a free channel.</summary>
    int StartWave(FlexFile wave, int num, float volume, int repeat, int distance, int balance)
    {
        if (num < 0 || num >= wave.Count)
        {
            LastError = $"sfx {num} is out of range ({Pack}: {wave.Count} sounds)";
            return -1;
        }

        if (!_samples.TryGetValue(num, out var sample))
        {
            sample = WavFile.Decode(wave.Get(num));
            _samples[num] = sample;
        }

        if (sample is null)
        {
            LastError = $"sfx {num}: not a sound Exult can play";
            return -1;
        }

        var vol = (int)(MaxVolume * Math.Min(volume, 1f));
        var channel = _nextChannel++;
        if (!_mixer!.Play(channel, sample, repeat, vol, vol, distance, balance))
        {
            LastError = $"sfx {num}: no channel was available";
            return -1;
        }

        Started++;
        Last = $"{num} ({Pack}, {sample.LengthMs} ms{(repeat != 0 ? $", repeat {repeat}" : "")}) at {vol}/256, distance {distance}, balance {balance}";
        return channel;
    }

    /// <summary>Exult <c>Object_sfx::handle_event</c>: a follower started when due and in range, then kept up with its object.</summary>
    void Follow(Follower f)
    {
        if (!f.Obj.Removed)
        {
            f.LastPos = CentreOf(f.Obj);
        }

        if (f.Channel != -1)
        {
            f.Channel = Reposition(f.Channel, f.LastPos);
        }
        else if (Position(f.LastPos).Distance <= MaxVolume)
        {
            f.Channel = PlayAt(f.Num, f.LastPos);
        }
    }

    /// <summary>Plays a MIDI effect (until <paramref name="play"/> is stopped) and tells how long it lasts in milliseconds (0 if it didn't start).</summary>
    int PlayMidi(int num, float volume, MidiPlay play)
    {
        if (_flex is null || !_midi.Available || volume <= 0f)
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
        var thread = new Thread(() => Run(effect, map, volume, play)) { IsBackground = true, Name = "u7-sfx" };
        thread.Start();
        return Math.Max(1, effect.Events[^1].Tick * 1000 / 120);
    }

    /// <summary>
    /// Exult <c>get_2d_position_for_tile</c>: a tile's distance from the
    /// view's centre (0-256 by the squared screen-space distance, 257
    /// beyond <see cref="MaxFalloff"/> tiles) and its balance (-256 left to
    /// 256 right).
    /// </summary>
    (int Distance, int Balance) Position(TileCoord at)
    {
        var c = ViewCentre;
        var sqr = ScreenDistanceSquared(c, at);
        if (sqr > MaxFalloff * MaxFalloff)
        {
            return (MaxVolume + 1, 0);
        }

        return (sqr * MaxVolume / (MaxFalloff * MaxFalloff), (U7Constants.TileDelta(c.Tx, at.Tx) * 2 - at.Tz - c.Tz) * 32 / 5);
    }

    /// <summary>Exult <c>Tile_coord::square_distance_screen_space</c>: a lift shows half a tile up and left.</summary>
    static int ScreenDistanceSquared(TileCoord a, TileCoord b)
    {
        var dz = b.Tz - a.Tz;
        var dx = Math.Abs(U7Constants.TileDelta(a.Tx, b.Tx)) * 2 - dz;
        var dy = Math.Abs(U7Constants.TileDelta(a.Ty, b.Ty)) * 2 - dz;
        return (dx * dx + dy * dy) / 4;
    }

    /// <summary>Exult <c>get_outermost()->get_center_tile()</c>: where an object's sound comes from.</summary>
    static TileCoord CentreOf(U7Object obj)
    {
        var outer = obj;
        while (outer.Container is { } c)
        {
            outer = c;
        }

        return ObjectGeometry.CenterTile(outer);
    }

    /// <summary>The followers kept up (Exult <c>Object_sfx::handle_event</c>), the MIDI effects' repeats, and Exult's background noise; called every frame.</summary>
    public void Update(double delta, bool outside, bool night, bool musicPlaying)
    {
        _now += delta;
        for (var i = _midiRepeats.Count - 1; i >= 0; i--)
        {
            var (channel, num, volume, due) = _midiRepeats[i];
            if (_now >= due)
            {
                _midiRepeats.RemoveAt(i);
                if (_midiPlays.TryGetValue(channel, out var play) && !play.Stopped)
                {
                    PlayMidi(num, volume, play);
                }
            }
        }

        foreach (var (channel, play) in _midiPlays)
        {
            if (_now >= play.End)
            {
                _midiPlays.Remove(channel);
            }
        }

        for (var i = _followers.Count - 1; i >= 0; i--)
        {
            var f = _followers[i];
            if (_now < f.Due)
            {
                continue;
            }

            // An object gone before its sound started takes the sound with it.
            if (f.Channel == -1 && f.Obj.Removed)
            {
                _followers.RemoveAt(i);
                continue;
            }

            Follow(f);
            if (f.Channel == -1)
            {
                _followers.RemoveAt(i);
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

    void Run(Effect effect, int[] map, float volume, MidiPlay play)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            foreach (var (tick, msg) in effect.Events)
            {
                var targetMs = tick * 1000.0 / 120.0;
                while (sw.Elapsed.TotalMilliseconds < targetMs && !play.Stopped)
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

                if (play.Stopped)
                {
                    break;
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
        StopAll();
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
