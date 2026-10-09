using U7.Data;

namespace U7.World;

/// <summary>
/// Exult <c>Frame_animator</c> (objs/animate.cc) for the animated shapes:
/// each object on screen steps its frame on Exult's 100 ms beat, in the way
/// its TFA animation type says (<c>Animation_info::create_from_tfa</c>; Black
/// Gate's <c>shape_info.txt</c> adds none), and the frame is written to the
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
    readonly List<U7Object> _beat = new();
    ulong _nextBeat;

    /// <summary>Exult <c>Animator::create</c>: an animated shape with more than one frame, not an actor.</summary>
    public bool IsAnimated(U7Object obj) =>
        !obj.IsActor && map.Catalog[obj.Shape] is { Animated: true, FrameCount: > 1 };

    /// <summary>The painter drew an animated object this frame (Exult <c>want_animation</c>).</summary>
    public void Painted(U7Object obj)
    {
        if (IsAnimated(obj))
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
            if (obj.Removed || !IsAnimated(obj))
            {
                _animators.Remove(obj);
                continue;
            }

            if (!_animators.TryGetValue(obj, out var anim))
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
        anim.Info = FromTfa(map.Catalog[obj.Shape].AnimType, shapeFrames);
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
