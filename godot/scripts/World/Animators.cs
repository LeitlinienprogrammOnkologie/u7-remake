using U7.Data;

namespace U7.World;

/// <summary>
/// Exult <c>Frame_animator</c> (objs/animate.cc) for the animated shapes:
/// each object on screen steps its frame on Exult's 100 ms beat, in the way
/// its TFA animation type says (<c>Animation_info::create_from_tfa</c>), or
/// its <c>shape_info.txt</c> animation (Serpent Isle has two; Black Gate
/// none), and the frame is written to the
/// object, so usecode and saves see it. Off the screen an animation stops
/// where it is (Exult stops the animator once its object isn't painted).
/// </summary>
public sealed class Animators(GameMap map, GameClock clock)
{
    enum AniType
    {
        TimeSynched,
        Hourly,
        NonLooping,
        Looping,
        RandomFrames
    }

    /// <summary>Exult <c>Animation_info</c>: the type, frames per cycle (-1: all), recycle, the chance (%) to leave frame 0, frame delay in beats.</summary>
    readonly record struct Info(AniType Type, int FrameCount = -1, int Recycle = 0, int FreezeFirst = 100, int FrameDelay = 1);

    /// <summary>One object's <c>Frame_animator</c> state.</summary>
    sealed class Animator
    {
        public Info Info;
        public int FirstFrame;
        public int Frames;
        public int CurrPos;
        public int Created;
        public int FrameCounter;
    }

    const int BeatMs = 100;

    readonly Dictionary<U7Object, Animator> _animators = new();
    readonly HashSet<U7Object> _painted = new();
    /// <summary>Exult <c>Shape_sfx</c>: per sounding object, when its sound ends, its last of a run, and the hour it last struck.</summary>
    sealed class Sound
    {
        public double Until;
        public int Last = -1;
        public int StruckHour = -1;
    }

    readonly Dictionary<U7Object, Sound> _sounds = new();
    readonly Random _rng = new();
    /// <summary>The sound effects (Exult <c>Audio</c>); null headless.</summary>
    public U7.Audio.SoundEffects? Sfx { get; set; }
    readonly List<U7Object> _beat = new();
    ulong _nextBeat;

    /// <summary>Exult <c>Animator::create</c>: an animated shape with more than one frame, not an actor.</summary>
    public bool IsAnimated(U7Object obj) =>
        !obj.IsActor && map.Catalog[obj.Shape] is { Animated: true, FrameCount: > 1 };

    /// <summary>Exult <c>Animator::create</c>'s other case: a shape with a sound of its own (<c>shape_sfx</c>), animated or not (<c>Sfx_animator</c>).</summary>
    bool Sounds(U7Object obj) => !obj.IsActor && Sfx is not null && map.Catalog[obj.Shape].Sfx is not null;

    /// <summary>The painter drew an animated or sounding object this frame (Exult <c>want_animation</c>).</summary>
    public void Painted(U7Object obj)
    {
        if (IsAnimated(obj) || Sounds(obj))
        {
            _painted.Add(obj);
        }
    }

    /// <summary>
    /// Exult's beat (<c>curtime + delay - curtime % delay</c>): once per 100 ms,
    /// every animator whose object was painted since the last beat advances.
    /// </summary>
    public void Update(ulong nowMs)
    {
        if (nowMs < _nextBeat)
        {
            return;
        }

        _nextBeat = nowMs + BeatMs - nowMs % BeatMs;
        _beat.Clear();
        _beat.AddRange(_painted);
        _painted.Clear();
        foreach (var obj in _beat)
        {
            var animated = !obj.Removed && IsAnimated(obj);
            if (!animated)
            {
                _animators.Remove(obj);
            }

            if (obj.Removed || !animated && !Sounds(obj))
            {
                _sounds.Remove(obj);
                continue;
            }

            Animator? anim = null;
            var advanced = false;
            if (animated)
            {
                if (!_animators.TryGetValue(obj, out anim))
                {
                    anim = new Animator();
                    Initialize(obj, anim);
                    _animators[obj] = anim;
                }

                // Exult Frame_animator::handle_event.
                if (--anim.FrameCounter <= 0)
                {
                    anim.FrameCounter = anim.Info.FrameDelay;
                    map.SetFrame(obj, NextFrame(obj, anim, nowMs));
                    advanced = true;
                }
            }

            if (Sounds(obj))
            {
                UpdateSound(obj, anim, advanced, nowMs);
            }
        }
    }

    /// <summary>
    /// Exult <c>Frame_animator::handle_event</c>'s sound: an animated
    /// object's plays as its frames turn (at the cycle's start, or every
    /// <c>sfx_delay</c> frames, or every frame), a merely sounding one's
    /// every beat; <c>Shape_sfx::update</c> then starts it again once the
    /// last has ended, by its chance, the next of its run; and the
    /// grandfather clock strikes its extra sound as many times as the hour
    /// when the hour turns.
    /// </summary>
    void UpdateSound(U7Object obj, Animator? anim, bool advanced, ulong nowMs)
    {
        var info = map.Catalog[obj.Shape].Sfx!.Value;
        bool play;
        if (anim is null)
        {
            play = true;
        }
        else if (!advanced)
        {
            play = false;
        }
        else
        {
            var sfxDelay = map.Catalog[obj.Shape].AnimationText?.SfxDelay ?? -1;
            play = sfxDelay < 0 ? anim.CurrPos == (anim.Info.FreezeFirst < 100 ? 1 : 0)
                : sfxDelay > 1 ? anim.CurrPos % sfxDelay == 0
                : true;
        }

        if (!_sounds.TryGetValue(obj, out var sound))
        {
            sound = new Sound();
            _sounds[obj] = sound;
        }

        var now = nowMs / 1000.0;
        if (play && now >= sound.Until && _rng.Next(100) < info.Chance)
        {
            int num;
            if (info.Range > 1)
            {
                if (info.Random)
                {
                    num = info.Num + _rng.Next(info.Range);
                }
                else
                {
                    sound.Last = (sound.Last + 1) % info.Range;
                    num = info.Num + sound.Last;
                }
            }
            else
            {
                num = info.Num;
            }

            var length = Sfx!.PlayAt(num, obj, info.Volume / 100f);
            sound.Until = now + Math.Max(length, 100) / 1000.0;
        }

        if (info.Extra >= 0 && clock.Minute == 0 && clock.Hour != sound.StruckHour)
        {
            sound.StruckHour = clock.Hour;
            var strikes = clock.Hour % 12;
            strikes = strikes == 0 ? 12 : strikes;
            for (var i = 0; i < strikes; i++)
            {
                Sfx!.PlayLater(info.Extra, obj, i * 700);
            }
        }
    }

    /// <summary>Exult <c>Animation_info::create_from_tfa</c>; types it doesn't know get <c>get_animation_info_safe</c>'s type 0.</summary>
    static Info FromTfa(int type, int frames) =>
        type switch
        {
            5 => new Info(AniType.Looping, frames, 0, 20),
            6 => new Info(AniType.RandomFrames, frames),
            8 => new Info(AniType.Hourly, frames),
            9 => new Info(AniType.Looping, frames, 8),
            10 => new Info(AniType.Looping, frames, 6),
            11 => new Info(AniType.Looping, frames, frames - 1, 0),
            12 or 14 => new Info(AniType.TimeSynched, frames, 0, 100, 4), // Slow advance, grandfather clocks.
            13 => new Info(AniType.NonLooping, frames),
            15 => new Info(AniType.TimeSynched, 6, 0, 100, 4),
            _ => new Info(AniType.TimeSynched, frames)
        };

    /// <summary>Exult <c>Frame_animator::Initialize</c>: the cycle the object's frame is in, and where in it.</summary>
    void Initialize(U7Object obj, Animator anim)
    {
        var shapeFrames = map.Catalog[obj.Shape].FrameCount;
        var lastFrame = obj.Frame & ~32;
        var rotflag = obj.Frame & 32;
        var info = map.Catalog[obj.Shape];
        anim.Info = info.AnimationText is { } text
            ? new Info((AniType)text.Type, text.FrameCount, text.Recycle, text.FreezeFirst, text.FrameDelay)
            : FromTfa(info.AnimType, shapeFrames);
        var frames = anim.Info.FrameCount < 0 ? shapeFrames : anim.Info.FrameCount;
        var first = frames == shapeFrames ? 0 : lastFrame - lastFrame % frames;
        if (first + frames >= shapeFrames)
        {
            frames = shapeFrames - first;
        }

        anim.Frames = Math.Max(1, frames);
        anim.FrameCounter = anim.Info.FrameDelay;
        anim.Created = anim.CurrPos = anim.Info.Type == AniType.TimeSynched
            ? lastFrame % anim.Frames
            : lastFrame - first;
        anim.FirstFrame = first | rotflag;
    }

    /// <summary>Exult <c>Frame_animator::get_next_frame</c>.</summary>
    int NextFrame(U7Object obj, Animator anim, ulong nowMs)
    {
        if (obj.Frame < anim.FirstFrame || obj.Frame >= anim.FirstFrame + anim.Frames)
        {
            Initialize(obj, anim); // Its frame was changed (usecode): start again from there.
        }

        if (anim.Frames == 1)
        {
            return anim.FirstFrame;
        }

        switch (anim.Info.Type)
        {
            case AniType.Hourly:
                return clock.Hour % anim.Frames; // (Exult adds no first frame here.)
            case AniType.NonLooping:
                anim.CurrPos = Math.Min(anim.CurrPos + 1, anim.Frames - 1);
                return anim.FirstFrame + anim.CurrPos;
            case AniType.TimeSynched:
                anim.CurrPos = (int)((nowMs / (ulong)(BeatMs * anim.Info.FrameDelay) + (ulong)anim.Created) % (ulong)anim.Frames);
                return anim.FirstFrame + anim.CurrPos;
            case AniType.RandomFrames:
                anim.CurrPos = Random.Shared.Next(anim.Frames);
                return anim.FirstFrame + anim.CurrPos;
            default:
            {
                // Looping, perhaps lingering on frame 0, perhaps going round only the last frames.
                var chance = anim.Info.FreezeFirst;
                if (anim.CurrPos != 0 || chance == 100 || (chance != 0 && Random.Shared.Next(100) < chance))
                {
                    anim.CurrPos = (anim.CurrPos + 1) % anim.Frames;
                    var recycle = anim.Info.Recycle;
                    if (anim.CurrPos == 0 && anim.Frames >= recycle)
                    {
                        anim.CurrPos = (anim.Frames - recycle) % anim.Frames;
                    }
                }

                return anim.FirstFrame + anim.CurrPos;
            }
        }
    }
}
