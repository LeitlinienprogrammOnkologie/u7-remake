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
/// Exult <c>Weather_effect</c>'s bookkeeping: which weather (1 snow, 2 storm,
/// 3 magic sparkles, 4 fog, 5 overcast, 6 clouds) until when. The rain,
/// snow, fog and clouds themselves are not drawn.
/// </summary>
public class WeatherEffect
{
    /// <summary>The weather's number, or -1 (lightning).</summary>
    public int Num;
    public double StopMs;
    /// <summary>Where the egg that started it is, if one did.</summary>
    public TileCoord? EggLoc;
}

/// <summary>
/// Exult <c>Lightning_effect</c>: the screen flashes (PALETTE_LIGHTNING) for
/// 25-50 ms, then again every 4-7 s (now and then sooner) until it ends.
/// </summary>
public sealed class LightningEffect : WeatherEffect
{
    public bool FromUsecode;
    public bool Flashing;
    public double NextMs;
}

/// <summary>
/// Exult <c>Effects_manager</c> for the SPRITES.VGA animations, explosions,
/// lightning and the weather (missiles are the combat engine's, texts the
/// bark overlay's). Ticked like Exult's time queue, so it stands still in a
/// conversation's wait and in gump mode.
/// </summary>
public sealed class EffectsManager
{
    readonly List<SpriteEffect> _sprites = new();
    readonly List<WeatherEffect> _weather = new();
    readonly VgaShapeFile _spritesVga;
    /// <summary>Milliseconds of effect time (Exult's ticks while the time queue runs).</summary>
    double _nowMs;
    /// <summary>Exult <c>Lightning_effect::active</c>: one flash at a time.</summary>
    bool _lightningActive;

    public EffectsManager(VgaShapeFile spritesVga) => _spritesVga = spritesVga;

    /// <summary>Newest first, the order Exult paints them in (<c>add_effect</c> puts it at the front).</summary>
    public IReadOnlyList<SpriteEffect> Sprites => _sprites;

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

    /// <summary>Exult <c>Effects_manager::get_weather</c>: the newest numbered weather, 0 for none.</summary>
    public int GetWeather() => _weather.Find(w => w.Num >= 0)?.Num ?? 0;

    /// <summary>Exult <c>remove_weather_effects(dist)</c>: all, or those from eggs at least that far away.</summary>
    public void RemoveWeather(TileCoord? avatar = null, int dist = 0) =>
        _weather.RemoveAll(w => dist == 0 || avatar is null || (w.EggLoc is { } loc && loc.Distance(avatar.Value) >= dist));

    /// <summary>
    /// Exult <c>Egg_object::set_weather</c>: weather <paramref name="weather"/>
    /// (0 clears it) for <paramref name="minutes"/> game minutes (0: a long
    /// 6000), replacing other weather unless it is fog or the same kind.
    /// </summary>
    public void SetWeather(int weather, int minutes = 15, TileCoord? egg = null)
    {
        if (minutes == 0)
        {
            minutes = 6000; // Confirmed from originals.
        }

        var cur = GetWeather();
        if (weather != 4 && (weather == 3 || cur != weather))
        {
            _weather.Clear();
        }

        if (weather is < 1 or > 6)
        {
            _weather.Clear();
            return;
        }

        _weather.Insert(0, new WeatherEffect { Num = weather, StopMs = StopTime(minutes), EggLoc = egg });
        GD.Print($"weather {weather} for {minutes} minutes");
    }

    /// <summary>
    /// Exult <c>UI_lightning</c>: a <c>Lightning_effect</c> from usecode,
    /// replacing usecode's last one. Exult passes 1000, which the weather
    /// effect counts in game minutes, so it flashes now and then for long.
    /// </summary>
    public void AddUsecodeLightning()
    {
        _weather.RemoveAll(w => w is LightningEffect { FromUsecode: true });
        _weather.Insert(0, new LightningEffect { Num = -1, StopMs = StopTime(1000), FromUsecode = true, NextMs = _nowMs });
    }

    /// <summary>Exult <c>Weather_effect</c>: a game minute is 25 standard delays.</summary>
    double StopTime(int minutes) => _nowMs + (double)minutes * U7Constants.StandardDelayMs * U7Constants.TicksPerMinute;

    public void Update(double delta)
    {
        var ms = delta * 1000;
        _nowMs += ms;
        for (var i = _weather.Count - 1; i >= 0; i--)
        {
            if (_weather[i] is LightningEffect lightning)
            {
                if (!StepLightning(lightning))
                {
                    _weather.RemoveAt(i);
                }
            }
            else if (_nowMs >= _weather[i].StopMs)
            {
                _weather.RemoveAt(i);
            }
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

    /// <summary>Exult <c>Lightning_effect::handle_event</c>, as its time comes. False once it has ended.</summary>
    bool StepLightning(LightningEffect l)
    {
        while (_nowMs >= l.NextMs)
        {
            var r = Random.Shared.Next();
            double delay = 100;
            if (l.Flashing)
            {
                l.Flashing = false;
                _lightningActive = false;
                if (_nowMs >= l.StopMs)
                {
                    return false;
                }

                delay = r % 50 == 0 ? (1 + r % 7) * 40 : 4000 + r % 3000;
            }
            else if (!_lightningActive)
            {
                // (Exult also plays thunder, and flashes from storms only outside dungeons.)
                _lightningActive = true;
                l.Flashing = true;
                delay = (1 + r % 2) * 25;
            }

            l.NextMs += delay;
        }

        return true;
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
