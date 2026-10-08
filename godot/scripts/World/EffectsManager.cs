using Godot;
using U7.Core;
using U7.Data;

namespace U7.World;

/// <summary>
/// Exult <c>Sprites_effect</c>: an animation from SPRITES.VGA at a tile or
/// following an object, one frame per standard delay, through its frames once
/// or for a number of repetitions.
/// </summary>
public sealed class SpriteEffect
{
    public int Sprite;
    public int Frame;
    public int Frames;
    /// <summary>The object it follows, or null.</summary>
    public U7Object? Item;
    public TileCoord Pos;
    /// <summary>Offset from the position in pixels.</summary>
    public int XOff;
    public int YOff;
    /// <summary>Added to the offset on each frame.</summary>
    public int DeltaX;
    public int DeltaY;
    /// <summary>Repetitions, or &lt;0 to go through the frames once.</summary>
    public int Reps;
    /// <summary>Milliseconds until its next event.</summary>
    public double Due;
    /// <summary>Exult <c>Explosion_effect</c>: what happens a quarter of the way through (the blast).</summary>
    public Action<SpriteEffect>? AtQuarter;

    public bool Visible => Frame < Frames;
}

/// <summary>
/// Exult <c>Effects_manager</c> for the SPRITES.VGA animations, explosions,
/// lightning and the weather (<see cref="WeatherEffect"/>; missiles are the
/// combat engine's, texts the bark overlay's). Ticked like Exult's time
/// queue, so it stands still in a conversation's wait and in gump mode.
/// </summary>
public sealed class EffectsManager
{
    readonly List<SpriteEffect> _sprites = new();
    readonly List<WeatherEffect> _weather = new();
    readonly VgaShapeFile _spritesVga;
    /// <summary>Milliseconds of effect time (Exult's ticks while the time queue runs).</summary>
    double _nowMs;

    public EffectsManager(VgaShapeFile spritesVga, GameClock clock)
    {
        _spritesVga = spritesVga;
        Clock = clock;
    }

    /// <summary>Its overcast and fog counters are the weather's.</summary>
    public GameClock Clock { get; }

    /// <summary>Exult <c>Game_window::is_in_dungeon</c>: a storm's lightning doesn't flash there.</summary>
    public Func<bool> InDungeon { get; set; } = () => false;

    /// <summary>Effect time in milliseconds.</summary>
    public double NowMs => _nowMs;

    /// <summary>Exult <c>Lightning_effect::active</c>: one flash at a time.</summary>
    internal bool LightningActive { get; set; }

    /// <summary>Lightning flashes so far (for the console).</summary>
    public int Flashes { get; internal set; }

    /// <summary>Newest first, the order Exult paints them in (<c>add_effect</c> puts it at the front).</summary>
    public IReadOnlyList<SpriteEffect> Sprites => _sprites;

    /// <summary>The weather effects, newest first.</summary>
    public IReadOnlyList<WeatherEffect> Weather => _weather;

    /// <summary>A lightning flash is showing (Exult's PALETTE_LIGHTNING).</summary>
    public bool LightningFlash => _weather.Exists(w => w is LightningEffect { Flashing: true });

    /// <summary>The size of a SPRITES.VGA frame.</summary>
    public FrameInfo SpriteFrame(int sprite, int frame) => _spritesVga.Get(sprite, frame);

    public int SpriteFrameCount(int sprite) => _spritesVga.FrameCount(sprite);

    /// <summary>Exult <c>Sprites_effect(num, tile, dx, dy, delay, frm, rps)</c>.</summary>
    public SpriteEffect AddSprite(int num, TileCoord pos, int dx = 0, int dy = 0, int delayMs = 0, int frame = 0, int reps = -1) =>
        Add(new SpriteEffect
        {
            Sprite = num,
            Frames = _spritesVga.FrameCount(num),
            Pos = pos,
            DeltaX = dx,
            DeltaY = dy,
            Frame = frame,
            Reps = reps,
            Due = delayMs
        });

    /// <summary>Exult <c>Sprites_effect(num, obj, xf, yf, dx, dy, frm, rps)</c>: starts at once and follows the object.</summary>
    public SpriteEffect AddSprite(int num, U7Object item, int xoff, int yoff, int dx = 0, int dy = 0, int frame = 0, int reps = -1) =>
        Add(new SpriteEffect
        {
            Sprite = num,
            Frames = _spritesVga.FrameCount(num),
            Item = item,
            Pos = new TileCoord(item.Tx, item.Ty, item.Tz),
            XOff = xoff,
            YOff = yoff,
            DeltaX = dx,
            DeltaY = dy,
            Frame = frame,
            Reps = reps
        });

    SpriteEffect Add(SpriteEffect e)
    {
        _sprites.Insert(0, e);
        GD.Print($"sprite effect {e.Sprite} ({e.Frames} frames) at {e.Pos.Tx},{e.Pos.Ty},{e.Pos.Tz}");
        return e;
    }

    /// <summary>Exult <c>Effects_manager::add_effect</c>: at the front.</summary>
    public void Add(WeatherEffect effect) => _weather.Insert(0, effect);

    /// <summary>Exult <c>remove_effect</c>, and the effect's destructor.</summary>
    public void Remove(WeatherEffect effect)
    {
        if (_weather.Remove(effect))
        {
            effect.OnRemoved();
        }
    }

    /// <summary>Exult <c>Effects_manager::get_weather</c>: the newest numbered weather, 0 for none.</summary>
    public int GetWeather() => _weather.Find(w => w.Num >= 0)?.Num ?? 0;

    /// <summary>Exult <c>remove_weather_effects()</c>: all of them.</summary>
    public void RemoveWeather()
    {
        foreach (var w in _weather.ToList())
        {
            Remove(w);
        }
    }

    /// <summary>
    /// Exult <c>remove_weather_effects(dist)</c>, on every chunk the avatar
    /// enters (<c>Game_window::emulate_cache</c>, 120 tiles): those from eggs
    /// at least that far away.
    /// </summary>
    public void RemoveWeather(TileCoord avatar, int dist)
    {
        foreach (var w in _weather.Where(w => w.OutOfRange(avatar, dist)).ToList())
        {
            Remove(w);
        }
    }

    /// <summary>
    /// Exult <c>Egg_object::set_weather</c>: weather <paramref name="weather"/>
    /// (0 clears it) for <paramref name="minutes"/> game minutes (0: a long
    /// 6000). Unless it is fog, it replaces all weather when it is sparkles or
    /// differs from the current weather; the same again is added alongside.
    /// </summary>
    public void SetWeather(int weather, int minutes = 15, TileCoord? egg = null)
    {
        if (minutes == 0)
        {
            minutes = 6000; // Confirmed from originals.
        }

        var cur = GetWeather();
        GD.Print($"weather is {cur}; setting {weather} for {minutes} minutes");
        if (weather != 4 && (weather == 3 || cur != weather))
        {
            RemoveWeather();
        }

        switch (weather)
        {
            case 0:
                RemoveWeather();
                break;
            case 1:
                Add(new SnowstormEffect(this, minutes, 0, egg));
                break;
            case 2:
                Add(new StormEffect(this, minutes, 0, egg));
                break;
            case 3:
                RemoveWeather();
                Add(new SparkleEffect(this, minutes, 0, egg));
                break;
            case 4:
                Add(new FogEffect(this, minutes, 0, egg));
                break;
            case 5:
            case 6:
                Add(new CloudsEffect(this, minutes, 0, egg, weather));
                break;
        }
    }

    /// <summary>
    /// Exult <c>UI_lightning</c>: a <c>Lightning_effect</c> from usecode,
    /// replacing usecode's last one. Exult passes 1000, which the weather
    /// effect counts in game minutes, so it flashes now and then for long.
    /// </summary>
    public void AddUsecodeLightning()
    {
        foreach (var w in _weather.Where(w => w is LightningEffect { FromUsecode: true }).ToList())
        {
            Remove(w);
        }

        Add(new LightningEffect(this, 1000, 0, fromUsecode: true));
    }

    public void Update(double delta)
    {
        var ms = delta * 1000;
        _nowMs += ms;
        // Exult's time queue: the weather's events in time order, each at the
        // time it was due, so a long frame catches up step by step.
        while (NextDue() is { } due)
        {
            due.HandleEvent(due.DueMs);
        }

        for (var i = _sprites.Count - 1; i >= 0; i--)
        {
            var e = _sprites[i];
            e.Due -= ms;
            while (e.Due <= 0)
            {
                if (Step(e))
                {
                    _sprites.RemoveAt(i);
                    break;
                }

                e.Due += U7Constants.StandardDelayMs;
            }
        }
    }

    WeatherEffect? NextDue()
    {
        WeatherEffect? next = null;
        foreach (var w in _weather)
        {
            if (w.DueMs <= _nowMs && (next is null || w.DueMs < next.DueMs))
            {
                next = w;
            }
        }

        return next;
    }

    /// <summary>Exult <c>Sprites_effect::handle_event</c>. Returns true when the animation is over.</summary>
    static bool Step(SpriteEffect e)
    {
        if (e.AtQuarter is { } blast && e.Frame == e.Frames / 4)
        {
            e.AtQuarter = null;
            blast(e);
        }

        // Exult ends at frame == frames; a start frame past the end (or no
        // frames, which would divide by zero) would never end.
        if (e.Reps == 0 || e.Frames == 0 || (e.Reps < 0 && e.Frame >= e.Frames))
        {
            return true;
        }

        if (e.Item is { Removed: false, Container: null } item)
        {
            e.Pos = new TileCoord(item.Tx, item.Ty, item.Tz);
        }

        e.XOff += e.DeltaX;
        e.YOff += e.DeltaY;
        var frame = e.Frame + 1;
        if (e.Reps > 0)
        {
            e.Reps--;
            frame %= e.Frames;
        }

        e.Frame = frame;
        return false;
    }
}
