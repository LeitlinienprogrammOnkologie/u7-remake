using U7.Core;

namespace U7.World;

/// <summary>
/// Exult <c>Weather_effect</c>: a weather effect on the effects' time queue,
/// first due <c>delay</c> ms after it is made and lasting its game minutes
/// from then (a minute is 25 standard delays, 5 s). It moves itself on by
/// setting <see cref="DueMs"/> or leaves the list through
/// <see cref="EffectsManager.Remove"/>; <see cref="OnRemoved"/> stands in for
/// Exult's destructors. Nothing here is drawn.
/// </summary>
public abstract class WeatherEffect
{
    public const double MinuteMs = U7Constants.StandardDelayMs * U7Constants.TicksPerMinute;

    protected readonly EffectsManager Effects;

    protected WeatherEffect(EffectsManager effects, int minutes, int delayMs, int num, TileCoord? egg)
    {
        Effects = effects;
        Num = num;
        EggLoc = egg;
        StartMs = effects.NowMs + delayMs;
        StopMs = StartMs + minutes * MinuteMs;
        DueMs = StartMs;
    }

    /// <summary>The weather's number (1 snow, 2 storm, 3 sparkles, 4 fog, 5 overcast, 6 clouds), or -1 for a part of one and lightning.</summary>
    public int Num { get; }
    /// <summary>Where the egg that started it is, if one did.</summary>
    public TileCoord? EggLoc { get; }
    /// <summary>When its first event is due, in effect time.</summary>
    public double StartMs { get; }
    public double StopMs { get; }
    /// <summary>When its next event is due (its entry in Exult's time queue).</summary>
    public double DueMs { get; protected set; }

    public abstract string Name { get; }

    /// <summary>Exult <c>Weather_effect::out_of_range</c>: only an egg's weather is too far away.</summary>
    public bool OutOfRange(TileCoord avatar, int dist) => EggLoc is { } loc && loc.Distance(avatar) >= dist;

    /// <summary>Exult <c>handle_event</c>, with the time it was due.</summary>
    public abstract void HandleEvent(double now);

    /// <summary>Exult's destructor: it has left the list.</summary>
    public virtual void OnRemoved()
    {
    }

    public string Describe(double now)
    {
        var start = now < StartMs ? FormattableString.Invariant($"starts in {(StartMs - now) / 1000:0.0} s, ") : "";
        var egg = EggLoc is { } e ? $" from the egg at {e.Tx},{e.Ty},{e.Tz}" : "";
        return $"{Name}{(Num >= 0 ? $" ({Num})" : "")}{Details}{egg}: {start}" +
               FormattableString.Invariant($"{(StopMs - now) / MinuteMs:0.0} min left");
    }

    protected virtual string Details => "";

    protected static int Rand() => Random.Shared.Next();
}

/// <summary>
/// What <c>Storm_effect</c>, <c>Snowstorm_effect</c>, <c>Sparkle_effect</c>
/// and <c>Fog_effect</c> share in Exult: one event as they start, one as they
/// end. Their rain, clouds and lightning are effects of their own.
/// </summary>
public abstract class StartStopEffect : WeatherEffect
{
    bool _start = true;

    protected StartStopEffect(EffectsManager effects, int minutes, int delayMs, int num, TileCoord? egg)
        : base(effects, minutes, delayMs, num, egg)
    {
    }

    protected virtual void Started()
    {
    }

    public override void HandleEvent(double now)
    {
        if (!_start)
        {
            Effects.Remove(this);
            return;
        }

        // Nothing more to do but end.
        _start = false;
        DueMs = StopMs;
        Started();
    }
}

/// <summary>
/// Exult <c>Storm_effect</c> (weather 2): clouds for a minute longer, rain
/// for two (starting gradually 20 ms to a second in) and lightning for two
/// minutes less (up to half a second after the rain). Only the storm itself
/// carries the egg, so leaving the egg behind ends the storm and leaves its
/// parts to run their time: Exult's way, kept.
/// </summary>
public sealed class StormEffect : StartStopEffect
{
    public StormEffect(EffectsManager effects, int minutes, int delayMs, TileCoord? egg)
        : base(effects, minutes, delayMs, 2, egg)
    {
        effects.Add(new CloudsEffect(effects, minutes + 1, delayMs));
        var rainDelay = 20 + Rand() % 1000;
        effects.Add(new RainEffect(effects, DropKind.Raindrop, minutes + 2, rainDelay, 0));
        var lightningDelay = rainDelay + Rand() % 500;
        effects.Add(new LightningEffect(effects, minutes - 2, lightningDelay));
    }

    public override string Name => "storm";
}

/// <summary>Exult <c>Snowstorm_effect</c> (weather 1): clouds for a minute longer, snow for two, starting gradually.</summary>
public sealed class SnowstormEffect : StartStopEffect
{
    public SnowstormEffect(EffectsManager effects, int minutes, int delayMs, TileCoord? egg)
        : base(effects, minutes, delayMs, 1, egg)
    {
        effects.Add(new CloudsEffect(effects, minutes + 1, delayMs));
        effects.Add(new RainEffect(effects, DropKind.Snowflake, minutes + 2, 20 + Rand() % 1000, 0));
    }

    public override string Name => "snowstorm";
}

/// <summary>
/// Exult <c>Sparkle_effect</c> (weather 3, on Ambrosia and in the
/// generators): 33 sparkles, numbered 3 and from the egg too, so they show
/// indoors and go when the egg is left behind.
/// </summary>
public sealed class SparkleEffect : StartStopEffect
{
    public SparkleEffect(EffectsManager effects, int minutes, int delayMs, TileCoord? egg)
        : base(effects, minutes, delayMs, 3, egg)
    {
        effects.Add(new RainEffect(effects, DropKind.Sparkle, minutes, delayMs, RainEffect.MaxDrops / 6, 3, egg));
    }

    public override string Name => "sparkle";
}

/// <summary>
/// Exult <c>Fog_effect</c> (weather 4): the clock is foggy from its start to
/// its end, and 100 sparkles start a quarter to 1.25 s in (Exult adds them,
/// as Serpent Isle does, to every game's fog).
/// </summary>
public sealed class FogEffect : StartStopEffect
{
    bool _started;

    public FogEffect(EffectsManager effects, int minutes, int delayMs, TileCoord? egg)
        : base(effects, minutes, delayMs, 4, egg)
    {
        effects.Add(new RainEffect(effects, DropKind.Sparkle, minutes, 250 + Rand() % 1000, RainEffect.MaxDrops / 2));
    }

    public override string Name => "fog";

    protected override void Started()
    {
        _started = true;
        Effects.Clock.SetFog(true);
    }

    /// <summary>Exult's destructor counts the fog down even if it never started; here only one that did (the user's pick).</summary>
    public override void OnRemoved()
    {
        if (_started)
        {
            Effects.Clock.SetFog(false);
        }
    }
}

/// <summary>
/// Exult <c>Clouds_effect</c>: 2-6 clouds (one more half the time when
/// overcast), SPRITES.VGA shape 2, drifting with the wind a step every
/// 100 ms. Weather 5 and the storms' clouds make the sky overcast. For
/// weather 6, clouds alone, Exult counts the overcast down as it starts and
/// never back up, so a later storm isn't overcast; here it leaves the count
/// alone (the user's pick).
/// </summary>
public sealed class CloudsEffect : WeatherEffect
{
    readonly List<(int Dx, int Dy)> _clouds = new();

    public CloudsEffect(EffectsManager effects, int minutes, int delayMs, TileCoord? egg = null, int num = -1)
        : base(effects, minutes, delayMs, num, egg)
    {
        Overcast = num != 6;
        if (Overcast)
        {
            effects.Clock.SetOvercast(true);
        }

        var count = 2 + Rand() % 5;
        if (Overcast)
        {
            count += Rand() % 2;
        }

        var dx = Rand() % 5 - 2;
        var dy = Rand() % 5 - 2;
        if (dx == 0 && dy == 0)
        {
            dx = 1 + Rand() % 2;
            dy = 1 - Rand() % 3;
        }

        Wind = (dx, dy);
        for (var i = 0; i < count; i++)
        {
            // Some go half as fast again.
            var (cdx, cdy) = (dx, dy);
            if (Rand() % 2 == 0)
            {
                cdx += cdx / 2;
                cdy += cdy / 2;
            }

            _clouds.Add((cdx, cdy));
        }
    }

    public bool Overcast { get; }
    /// <summary>World pixels per 100 ms step.</summary>
    public (int Dx, int Dy) Wind { get; }
    /// <summary>Each cloud's drift per step.</summary>
    public IReadOnlyList<(int Dx, int Dy)> Clouds => _clouds;

    public override string Name => Overcast ? "overcast" : "clouds";

    protected override string Details => $", {_clouds.Count} clouds, wind {Wind.Dx},{Wind.Dy}";

    public override void HandleEvent(double now)
    {
        if (now >= StopMs)
        {
            Effects.Remove(this);
            return;
        }

        // (Exult moves its cloud shapes here.)
        DueMs = now + 100;
    }

    public override void OnRemoved()
    {
        if (Overcast)
        {
            Effects.Clock.SetOvercast(false);
        }
    }
}

/// <summary>
/// Exult's <c>Basicdrop</c> particles, SPRITES.VGA shape 0: frames
/// <see cref="Frame0"/> to <see cref="FrameN"/> and back, moving
/// <see cref="Delta"/> world pixels right and down a step. Sparkles stay put,
/// start at a random frame and direction, and restart elsewhere at their last
/// frame; the others restart elsewhere when they leave the view.
/// </summary>
public sealed record DropKind(string Name, int Frame0, int FrameN, int Delta, bool Randomize)
{
    public static readonly DropKind Raindrop = new("rain", 3, 7, 6, false);
    public static readonly DropKind Snowflake = new("snow", 13, 20, 1, false);
    public static readonly DropKind Sparkle = new("sparkles", 21, 27, 0, true);
}

/// <summary>
/// Exult <c>Rain_effect</c>: raindrops, snowflakes or sparkles, stepped every
/// 100 ms. One made with no drops (the storms') starts gradually, adding 0-4
/// a step up to 200, and ends gradually, taking 0-14 a step in its last
/// 2.5 s. Exult hides them inside buildings and in gump mode, except
/// weather 3's sparkles, which show indoors.
/// </summary>
/// <remarks>
/// Once none are left in the last 2.5 s, Exult adds 0-4 again each step, so
/// a few flicker until it stops; here none come back (the user's pick).
/// </remarks>
public sealed class RainEffect : WeatherEffect
{
    public const int MaxDrops = 200;

    public RainEffect(EffectsManager effects, DropKind kind, int minutes, int delayMs = 0, int drops = MaxDrops,
        int num = -1, TileCoord? egg = null)
        : base(effects, minutes, delayMs, num, egg)
    {
        Kind = kind;
        Drops = drops;
        Gradual = drops == 0;
    }

    public DropKind Kind { get; }
    /// <summary>Exult <c>num_drops</c>: how many are falling.</summary>
    public int Drops { get; private set; }
    public bool Gradual { get; }
    public bool ShownIndoors => Num == 3;

    public override string Name => Kind.Name;

    protected override string Details => $", {Drops} drops" + (Gradual ? ", gradual" : "") + (ShownIndoors ? ", shown indoors" : "");

    public override void HandleEvent(double now)
    {
        ChangeDrops(now);
        // (Exult moves the drops here.)
        if (now >= StopMs)
        {
            Effects.Remove(this);
            return;
        }

        DueMs = now + 100;
    }

    /// <summary>Exult <c>Rain_effect::change_ndrops</c>.</summary>
    void ChangeDrops(double now)
    {
        if (!Gradual)
        {
            return;
        }

        if (now > StopMs - 2500)
        {
            if (Drops != 0)
            {
                Drops = Math.Max(0, Drops - Rand() % 15);
            }
        }
        else
        {
            if (Drops < MaxDrops)
            {
                Drops += Rand() % 5;
            }

            Drops = Math.Min(Drops, MaxDrops);
        }
    }
}

/// <summary>
/// Exult <c>Lightning_effect</c>: the screen flashes (PALETTE_LIGHTNING) for
/// 25-50 ms, then again after 4-7 s (one time in 50 after 40-280 ms), one
/// flash at a time, until its time is up. A storm's lightning doesn't flash
/// while the avatar is in a dungeon; usecode's always flashes.
/// </summary>
/// <remarks>
/// Two of Exult's bugs are fixed (the user's pick): Exult checks the end only
/// as a flash ends, so lightning held back in a dungeon flashes once out of
/// a clear sky when the avatar comes out; here no flash starts past the end.
/// And Exult's destructor leaves the one-at-a-time flag set if it goes
/// mid-flash, which stops all lightning until a restore; here it is cleared.
/// </remarks>
public sealed class LightningEffect : WeatherEffect
{
    public LightningEffect(EffectsManager effects, int minutes, int delayMs = 0, bool fromUsecode = false)
        : base(effects, minutes, delayMs, -1, null)
    {
        FromUsecode = fromUsecode;
    }

    public bool FromUsecode { get; }
    public bool Flashing { get; private set; }

    public override string Name => "lightning";

    protected override string Details => (FromUsecode ? " from usecode" : "") + (Flashing ? ", flashing" : "");

    public override void HandleEvent(double now)
    {
        var r = Rand();
        var delay = 100;
        if (Flashing)
        {
            Flashing = false;
            Effects.LightningActive = false;
            if (now >= StopMs)
            {
                Effects.Remove(this);
                return;
            }

            delay = r % 50 == 0 ? (1 + r % 7) * 40 : 4000 + r % 3000;
        }
        else if (now >= StopMs)
        {
            Effects.Remove(this);
            return;
        }
        else if ((FromUsecode || !Effects.InDungeon()) && !Effects.LightningActive)
        {
            // (Exult also plays thunder, sfx 62.)
            Effects.LightningActive = true;
            Flashing = true;
            Effects.Flashes++;
            delay = (1 + r % 2) * 25;
        }

        DueMs = now + delay;
    }

    public override void OnRemoved()
    {
        if (Flashing)
        {
            Effects.LightningActive = false;
        }
    }
}
