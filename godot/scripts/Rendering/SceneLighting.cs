using Godot;
using U7.Actors;
using U7.Core;
using U7.Data;
using U7.World;

namespace U7.Rendering;

/// <summary>
/// How the world shader lights the frame, worked out each frame before
/// <see cref="WorldView"/> draws: the ambient palette (Exult's final palette,
/// <c>Game_clock::set_time_palette</c>), the lit palette that lights reveal
/// (the day's), and the lights: the light spell round the avatar, magic
/// lights (glowing sprites, missiles, vortices and magic objects, cosmetic)
/// and the pools of the light sources in and around the view.
/// </summary>
public sealed class SceneLighting
{
    public const int MaxLights = 64;
    /// <summary>The light kinds the shader knows (<c>light_a.w</c>).</summary>
    public const int KindPool = 0;
    public const int KindSpell = 1;

    /// <summary>The light spell's radius in world pixels.</summary>
    const float SpellRadius = 7 * U7Constants.TileSize;
    /// <summary>How fast the light spell comes and goes (strength per second).</summary>
    const float SpellFadeRate = 1.5f;
    /// <summary>
    /// The darkness knob on the ambient palette (<see cref="PaletteSet.SetNightTint"/>):
    /// 0 is Exult's NIGHT palette, 1 the day palette under the old blue
    /// night tint. The user chose 1 (2026-10-07).
    /// </summary>
    const float NightTint = 1f;
    /// <summary>How far a flame's light dips as it flickers (the user's pick of 0.05, 0.12 and 0.25).</summary>
    const float FlickerAmount = 0.25f;
    /// <summary>A magic light's intensity before <see cref="GlowTable.MagicLook.Light"/>.</summary>
    const float MagicIntensity = 0.8f;
    /// <summary>How far the full-screen lightning sprite lifts the ambient palette towards the lit one at its brightest.</summary>
    const float FlashAmount = 0.6f;
    /// <summary>
    /// Exult eases a change of weather into the palette over 20 clock ticks
    /// (<c>set_time_palette</c>'s <c>Palette_transition</c> of 20 steps, 4 s).
    /// </summary>
    const double WeatherEaseSeconds = 20 * U7Constants.StandardDelayMs / 1000.0;
    /// <summary>How long a cloud shadow takes to come and go as its effect starts and ends, in effect milliseconds.</summary>
    const double CloudFadeMs = 4000;
    /// <summary>The fog's mist drifts this way (with the clouds' wind when there are clouds).</summary>
    static readonly Vector2 MistDirection = new Vector2(1f, 0.35f).Normalized();


    readonly GameClock _clock;
    readonly GameMap _map;
    readonly ShapeCatalog _catalog;
    readonly EffectsManager? _effects;
    readonly PartyManager? _party;
    readonly U7Object _avatar;
    readonly PaletteSet _palettes = new();
    readonly byte[] _scratch = new byte[768];
    readonly List<SceneLight> _found = new();
    readonly Func<U7Object, int> _frameOf;
    readonly GlowColours? _colours;
    (int From, int To, float T, int Override, int Class, float Lift, int Flash, int Overcast, int Fog)? _built;
    bool _snap;
    int _skip = U7Constants.NoRoof;
    ulong _ticks;
    double _effectMs;
    Vector2 _wind;

    /// <summary>
    /// A light source found in the view: what it is, how it looks, and how it
    /// ranks; <see cref="Kind"/> and <see cref="Extra"/> are the world
    /// shader's kind and spare value (a window's, <see cref="WindowLights"/>).
    /// </summary>
    public readonly record struct SceneLight(
        U7Object Source, int Shape, int Frame, int Brightness, Glow Glow, Vector2 Centre, float RadiusPx, float Score, bool Carried,
        int Kind = KindPool, float Extra = 0f);

    /// <summary>Light from roofed rooms through their windows.</summary>
    public WindowLights Windows { get; }

    /// <summary>The ambient palette, 8-bit RGB.</summary>
    public byte[] Ambient { get; } = new byte[768];
    /// <summary>What a light shows, 8-bit RGB.</summary>
    public byte[] Lit { get; } = new byte[768];
    /// <summary>Changes whenever <see cref="Ambient"/> or <see cref="Lit"/> do.</summary>
    public int Version { get; private set; }

    /// <summary>Per light: centre in world pixels, radius, kind. Always <see cref="MaxLights"/> long.</summary>
    public Vector4[] LightA { get; } = new Vector4[MaxLights];
    /// <summary>Per light: colour times intensity, animation phase. Always <see cref="MaxLights"/> long.</summary>
    public Vector4[] LightB { get; } = new Vector4[MaxLights];
    public int LightCount { get; private set; }

    /// <summary>The world pixels the camera shows, set before each <see cref="Update"/>.</summary>
    public Rect2 View { get; set; }
    /// <summary>The tile the view is centred on (Exult's scroll position, for its light count).</summary>
    public (int Tx, int Ty) Focus { get; set; }

    /// <summary>Exult <c>Game_window::in_dungeon</c> (<c>set_in_dungeon</c>, for the avatar).</summary>
    public bool InDungeon { get; private set; }
    public int From { get; private set; }
    public int To { get; private set; }
    public float T { get; private set; }
    /// <summary>A palette that replaces the time of day's (lightning, invisible), or -1.</summary>
    public int Override { get; private set; } = -1;
    /// <summary>How far the light spell has come in, 0 to 1.</summary>
    public float SpellStrength { get; private set; }
    /// <summary>Exult's light source level (<see cref="LightSources.Level"/>).</summary>
    public int Level { get; private set; }
    /// <summary>The palette Exult would pick for <see cref="Level"/> at night, or -1.</summary>
    public int Class { get; private set; } = -1;
    /// <summary>The light the avatar carries (<see cref="LightSources.CarriedLight(U7Object, ShapeCatalog)"/>).</summary>
    public int Carried { get; private set; }
    /// <summary>The light sources found, best first; the first <see cref="MaxLights"/> - 1 are shown.</summary>
    public IReadOnlyList<SceneLight> Found => _found;
    /// <summary>Missiles and vortices in flight, for their magic lights.</summary>
    public IReadOnlyList<Missile>? Missiles { get; set; }
    public IReadOnlyList<HomingMissile>? HomingMissiles { get; set; }
    /// <summary>The magic lights of this frame (glowing sprites, missiles and vortices).</summary>
    public int MagicLights { get; private set; }
    /// <summary>The full-screen lightning sprite's flash on the ambient palette, 0 to 1.</summary>
    public float Flash { get; private set; }

    /// <summary>Exult <c>is_main_actor_inside</c>: a roof above the avatar. No rain, snow, clouds or mist then.</summary>
    public bool Inside { get; private set; }
    /// <summary>How far the overcast has come into the day palettes, 0 to 1 (eased like Exult's).</summary>
    public float OvercastWeight { get; private set; }
    /// <summary>How far the fog has come into the day palettes, 0 to 1 (eased like Exult's).</summary>
    public float FogWeight { get; private set; }
    /// <summary>How bright the ambient palette is against the day's, 0 to 1 (for the weather's particles).</summary>
    public float AmbientLevel { get; private set; } = 1f;
    /// <summary>The ambient palette's average colour.</summary>
    public Vector3 AmbientMean { get; private set; } = Vector3.One;
    /// <summary>The cloud shadows' drift so far, in world pixels.</summary>
    public Vector2 CloudOffset { get; private set; }
    /// <summary>How much of the ground the cloud shadows cover, 0 to 1.</summary>
    public float CloudCover { get; private set; }
    /// <summary>How dark the cloud shadows are, 0 for none (indoors, or no clouds).</summary>
    public float CloudShadow { get; private set; }
    /// <summary>The mist's drift so far, in world pixels.</summary>
    public Vector2 MistOffset { get; private set; }
    /// <summary>How thick the fog's mist is, 0 for none.</summary>
    public float Mist { get; private set; }

    /// <summary>
    /// How far a dark ambient palette is lifted towards the palette of
    /// Exult's light class (candle, single, many). 0, the default, leaves the
    /// lighting to the pools.
    /// </summary>
    public float AmbientLift { get; set; }

    public SceneLighting(GameClock clock, GameMap map, ShapeCatalog catalog, U7Object avatar, EffectsManager? effects,
        PartyManager? party, GlowColours? colours = null)
    {
        _colours = colours;
        _clock = clock;
        _map = map;
        _catalog = catalog;
        _avatar = avatar;
        _effects = effects;
        _party = party;
        _frameOf = obj => WorldView.DisplayFrame(_catalog, obj, _ticks);
        Windows = new WindowLights(map);
        _palettes.SetNightTint(NightTint);
        _palettes.Get(PaletteSet.Day, Lit);
    }

    /// <summary>Exult <c>Game_clock::reset_palette</c> (<c>set_time_palette</c>): the light spell shows at once, without easing.</summary>
    public void ResetPalette() => _snap = true;

    public void Update(double delta)
    {
        var av = _avatar;
        var ticks = _ticks = Time.GetTicksMsec();
        InDungeon = _map.DungeonHeight(av.Tx, av.Ty) != 0;
        (From, To, T) = _clock.PaletteBlend(InDungeon);
        // Exult get_final_palette: an invisible avatar sees the invisible
        // palette; Lightning_effect sets its palette over whatever shows.
        // Either one is the whole screen's, lights included.
        Override = _effects is { LightningFlash: true } ? PaletteSet.Lightning
            : av.GetFlag(ObjFlag.Invisible) ? PaletteSet.Invisible
            : -1;
        Level = LightSources.Level(_map, _catalog, av, _party?.Members ?? [], Focus, InDungeon, _frameOf);
        Class = LightSources.Classify(Level);
        Carried = LightSources.CarriedLight(av, _catalog);
        Flash = LightningFlash();
        _skip = _map.RoofHeight(av.Tx, av.Ty, av.Tz);
        Inside = _skip < U7Constants.NoRoof;
        // Exult get_final_palette: overcast and fog on the day palettes.
        var ease = (float)(delta / WeatherEaseSeconds);
        OvercastWeight = Mathf.MoveToward(OvercastWeight, _clock.Cloudy ? 1f : 0f, ease);
        FogWeight = Mathf.MoveToward(FogWeight, _clock.Foggy ? 1f : 0f, ease);
        BuildPalettes();
        UpdateSky();

        LightCount = 0;
        var spell = Override != PaletteSet.Invisible && _clock.LightSpellShows(InDungeon);
        SpellStrength = _snap ? spell ? 1f : 0f
            : Mathf.MoveToward(SpellStrength, spell ? 1f : 0f, (float)delta * SpellFadeRate);
        _snap = false;
        if (SpellStrength > 0)
        {
            AddLight(FigureCentre(av), SpellRadius, KindSpell, new Vector3(SpellStrength, SpellStrength, SpellStrength), 0);
        }

        Windows.Begin(InDungeon ? -1 : _skip);
        AddMagicLights();
        FindLights(ticks);
        var seconds = ticks / 1000.0;
        for (var i = 0; i < _found.Count && LightCount < MaxLights; i++)
        {
            var l = _found[i];
            var (intensity, radius) = Animate(l, seconds);
            AddLight(l.Centre, radius, l.Kind, l.Glow.Colour * intensity, l.Extra);
        }
    }

    /// <summary>A light's intensity and radius now: flames flicker, magic pulses.</summary>
    static (float Intensity, float Radius) Animate(SceneLight l, double seconds)
    {
        var intensity = l.Glow.Intensity;
        var radius = l.RadiusPx;
        switch (l.Glow.Kind)
        {
            case GlowKind.Flame:
            {
                var amount = FlickerAmount * (l.Glow.Small ? 0.6f : 1f);
                var f = Flicker(seconds * (l.Glow.Small ? 1.5 : 1.0), Phase(l));
                intensity *= 1f - amount * (0.5f + 0.5f * f);
                radius *= 1f + 0.04f * f;
                break;
            }
            case GlowKind.Magic:
                intensity *= 0.8f + 0.2f * (float)Math.Sin(seconds * 2.0 + Phase(l));
                break;
        }

        return (intensity, radius);
    }

    /// <summary>
    /// The light sources WorldView paints in the view and a chunk round it,
    /// those in the dungeon while the avatar is in one, else the others
    /// (Exult's two light lists): objects with their brightness at the
    /// centre of the frame shown, actors with what they carry at the centre
    /// of their figure. Ranked by how much they show near the view's centre.
    /// </summary>
    void FindLights(ulong ticks)
    {
        _found.Clear();
        var view = View;
        if (view.Size.X <= 0)
        {
            return;
        }

        var av = _avatar;
        var mid = view.GetCenter();
        const int chunkPx = U7Constants.ChunkSizePixels;
        var c0x = Mathf.FloorToInt(view.Position.X / chunkPx) - 1;
        var c0y = Mathf.FloorToInt(view.Position.Y / chunkPx) - 1;
        var c1x = Mathf.FloorToInt(view.End.X / chunkPx) + 1;
        var c1y = Mathf.FloorToInt(view.End.Y / chunkPx) + 1;
        for (var cy = c0y; cy <= c1y; cy++)
        {
            for (var cx = c0x; cx <= c1x; cx++)
            {
                foreach (var obj in _map.ObjectsInChunk(cx, cy))
                {
                    if (!WorldView.IsPainted(obj, _skip, av) || (_map.DungeonHeight(obj.Tx, obj.Ty) != 0) != InDungeon)
                    {
                        continue;
                    }

                    if (obj.IsActor)
                    {
                        var carried = LightSources.CarriedLight(obj, _catalog, out var brightest);
                        if (carried > 0 && brightest is not null)
                        {
                            Add(obj, brightest.Shape, brightest.Frame, carried, FigureCentre(obj), true, mid);
                        }

                        // Exult's chunks list every light-source shape, actors too (the wisp).
                        if (_catalog[obj.Shape].LightSource && LightSources.Brightness(_catalog, obj.Shape, obj.Frame) is var own and > 0)
                        {
                            Add(obj, obj.Shape, obj.Frame, own, FigureCentre(obj), false, mid);
                        }

                        continue;
                    }

                    if (WindowLights.IsWindow(obj.Shape))
                    {
                        Windows.AddWindow(obj);
                        continue;
                    }

                    if (!_catalog[obj.Shape].LightSource && GlowTable.IsEmitter(obj.Shape))
                    {
                        AddEmitter(obj, WorldView.DisplayFrame(_catalog, obj, ticks), mid);
                        continue;
                    }

                    if (!_catalog[obj.Shape].LightSource || GlowTable.IsLampPool(obj.Shape))
                    {
                        continue;
                    }

                    var frame = WorldView.DisplayFrame(_catalog, obj, ticks);
                    var b = LightSources.Brightness(_catalog, obj.Shape, frame);
                    if (b > 0)
                    {
                        Add(obj, obj.Shape, frame, b, FrameCentre(obj, frame), false, mid);
                    }
                }
            }
        }

        // Windows only show against the dark: none by day, full at night.
        Windows.Finish(Mathf.Clamp((1f - AmbientLevel) / 0.5f, 0f, 1f), mid, _found);
        _found.Sort(ByScore);
    }

    static readonly Comparison<SceneLight> ByScore = (a, b) => b.Score.CompareTo(a.Score);

    void Add(U7Object source, int shape, int frame, int brightness, Vector2 centre, bool carried, Vector2 mid, Glow? look = null)
    {
        var glow = look ?? GlowTable.For(shape, brightness);
        if (Roofed(source.Tx, source.Ty, source.Tz))
        {
            // Hidden, it may still show through the room's windows, flickering as it would.
            var (now, _) = Animate(new SceneLight(source, shape, frame, brightness, glow, centre, 0, 0, carried), _ticks / 1000.0);
            Windows.AddHidden(source.Tx, source.Ty, source.Tz, now, glow.Colour);
            return;
        }

        var radius = glow.RadiusTiles * U7Constants.TileSize;
        if (!carried && GlowTable.IsPool(shape))
        {
            radius = Mathf.Max(radius, Mathf.Max(source.DimX, source.DimY) * U7Constants.TileSize / 2f);
        }

        var score = glow.Intensity * radius / (1f + centre.DistanceTo(mid) / 200f);
        _found.Add(new SceneLight(source, shape, frame, brightness, glow, centre, radius, score, carried));
    }

    /// <summary>
    /// A magic object (cosmetic): a slowly pulsing light from the middle of
    /// its frame, as wide as the frame, in the object's own colour.
    /// </summary>
    void AddEmitter(U7Object obj, int frame, Vector2 mid)
    {
        if (_colours is null)
        {
            return;
        }

        var fi = _catalog[obj.Shape].GetFrame(frame & 31);
        var look = GlowTable.Magic;
        var radius = MagicRadius(Mathf.Max(fi.Width, fi.Height)) / U7Constants.TileSize;
        var glow = new Glow(GlowKind.Magic, radius, MagicIntensity * look.Light, _colours.OfShape(obj.Shape));
        Add(obj, obj.Shape, frame, 0, FrameCentre(obj, frame), false, mid, glow);
    }

    /// <summary>
    /// Magic lights (cosmetic; Exult has none) for the glowing sprites (a
    /// blast flaring up and dying away), magic missiles in flight and the
    /// vortices, in their own colours, from the middle of the frame shown.
    /// </summary>
    void AddMagicLights()
    {
        var before = LightCount;
        if (_colours is not null && _effects is not null)
        {
            foreach (var e in _effects.Sprites)
            {
                if (!e.Visible || !GlowTable.IsGlowingSprite(e.Sprite))
                {
                    continue;
                }

                var fi = _effects.SpriteFrame(e.Sprite, e.Frame);
                var (x, y) = WorldView.SpriteHotspot(e);
                var strength = GlowTable.IsBlast(e.Sprite) ? GlowTable.BlastEnvelope(e.Frame, e.Frames) : 1f;
                AddMagic(e.Pos, x, y, fi.XLeft, fi.YAbove, fi.Width, fi.Height, _colours.OfSprite(e.Sprite), strength);
            }
        }

        if (_colours is not null && Missiles is not null)
        {
            foreach (var m in Missiles)
            {
                if (m.Frame < 0 || !GlowTable.IsGlowingMissile(m.SpriteShape))
                {
                    continue;
                }

                var fi = _catalog[m.SpriteShape].GetFrame(m.Frame & 31);
                var (x, y) = WorldView.MissileHotspot(m);
                var colour = m.SpriteShape == GlowTable.LightningMissile ? GlowTable.LightningColour : _colours.OfShape(m.SpriteShape);
                AddMagic(m.Pos, x, y, fi.XLeft, fi.YAbove, fi.Width, fi.Height, colour, 1f);
            }
        }

        if (_colours is not null && _effects is not null && HomingMissiles is not null)
        {
            foreach (var h in HomingMissiles)
            {
                var fi = _effects.SpriteFrame(h.Sprite, h.Frame);
                var (x, y) = WorldView.HomingHotspot(h);
                AddMagic(h.Pos, x, y, fi.XLeft, fi.YAbove, fi.Width, fi.Height, _colours.OfSprite(h.Sprite), 1f);
            }
        }

        MagicLights = LightCount - before;
    }

    void AddMagic(TileCoord at, int x, int y, int xleft, int yabove, int width, int height, Vector3 colour, float strength)
    {
        if (strength <= 0)
        {
            return;
        }

        var intensity = MagicIntensity * GlowTable.Magic.Light * strength;
        if (Roofed(at.Tx, at.Ty, at.Tz))
        {
            // A blast, missile or vortex in a roofed room flares at its windows.
            Windows.AddHidden(at.Tx, at.Ty, at.Tz, intensity, colour);
            return;
        }

        var centre = new Vector2(x - xleft + width / 2f, y - yabove + height / 2f);
        AddLight(centre, MagicRadius(Mathf.Max(width, height)), KindPool, colour * intensity, 0);
    }

    /// <summary>
    /// A roof (or upper floor) WorldView paints over the tile hides a light
    /// under it, its pool too: seen from outside, a house's candles don't
    /// light its roof or the street. The user's rule; Exult has no pools.
    /// </summary>
    bool Roofed(int tx, int ty, int tz) => _map.CoverAbove(tx, ty, tz) < _skip;

    /// <summary>A magic light's radius in pixels for a frame this big: three quarters of it, at least two tiles.</summary>
    static float MagicRadius(int size) =>
        Mathf.Max(2 * U7Constants.TileSize, size * 0.75f) * GlowTable.Magic.Radius;

    /// <summary>The full-screen lightning sprite (17): brightest as it starts, gone by its last frame.</summary>
    float LightningFlash()
    {
        var flash = 0f;
        if (_effects is null)
        {
            return flash;
        }

        foreach (var e in _effects.Sprites)
        {
            if (e.Visible && e.Sprite == GlowTable.LightningSprite)
            {
                flash = Mathf.Max(flash, 1f - e.Frame / (float)Math.Max(1, e.Frames));
            }
        }

        return flash;
    }

    /// <summary>Where the light of the frame shown sits (<see cref="GlowTable.LightOffset"/>).</summary>
    Vector2 FrameCentre(U7Object obj, int frame)
    {
        WorldView.ShapeLocation(obj.Tx, obj.Ty, obj.Tz, out var x, out var y);
        return new Vector2(x, y) + GlowTable.LightOffset(obj.Shape, frame, _catalog[obj.Shape]);
    }

    /// <summary>The centre of an actor's figure, which is drawn up and to the left of its hotspot.</summary>
    Vector2 FigureCentre(U7Object actor)
    {
        WorldView.ShapeLocation(actor.Tx, actor.Ty, actor.Tz, out var x, out var y);
        var fi = _catalog[actor.Shape].GetFrame(actor.Frame);
        return new Vector2(x - fi.XLeft + fi.Width / 2f, y - fi.YAbove + fi.Height / 2f);
    }

    /// <summary>A fixed phase per light, from its tile and shape (an actor's id for a carried light), so flames don't flicker in step.</summary>
    static float Phase(SceneLight l)
    {
        var h = l.Carried
            ? (uint)l.Source.Id * 2654435761u ^ (uint)l.Shape * 40503u
            : (uint)l.Source.Tx * 73856093u ^ (uint)l.Source.Ty * 19349663u ^ (uint)l.Shape * 83492791u;
        h ^= h >> 15;
        h *= 2246822519u;
        h ^= h >> 13;
        return h % 100000 / 100000f * Mathf.Tau * 8f;
    }

    /// <summary>A flame's flicker, -1 to 1: three sines that don't repeat in step.</summary>
    static float Flicker(double t, float phase) =>
        (float)(0.5 * Math.Sin(t * 7.3 + phase) + 0.3 * Math.Sin(t * 13.1 + phase * 1.7) + 0.2 * Math.Sin(t * 23.7 + phase * 2.3));

    void AddLight(Vector2 centre, float radius, int kind, Vector3 colour, float phase)
    {
        if (LightCount >= MaxLights)
        {
            return;
        }

        LightA[LightCount] = new Vector4(centre.X, centre.Y, radius, kind);
        LightB[LightCount] = new Vector4(colour.X, colour.Y, colour.Z, phase);
        LightCount++;
    }

    void BuildPalettes()
    {
        var lift = Override < 0 && Class >= 0 && (GameClock.IsDarkPalette(From) || GameClock.IsDarkPalette(To))
            ? AmbientLift
            : 0f;
        // The flash is part of the magic look: none when magic doesn't light.
        var flash = Override < 0 ? Mathf.RoundToInt(Flash * Mathf.Min(1f, GlowTable.Magic.Light) * 32) : 0;
        var overcast = Mathf.RoundToInt(OvercastWeight * WeatherLook.Overcast.Palette * 64);
        var fog = Mathf.RoundToInt(FogWeight * WeatherLook.Fog.Palette * 64);
        var key = (From, To, T, Override, Class, lift, flash, overcast, fog);
        if (_built == key)
        {
            return;
        }

        _built = key;
        if (Override >= 0)
        {
            _palettes.Get(Override, Ambient);
            Ambient.AsSpan().CopyTo(Lit);
        }
        else
        {
            var weather = new PaletteSet.Weather(overcast / 64f, fog / 64f, WeatherLook.Overcast.Grey);
            _palettes.Blend(From, To, T, weather, Ambient);
            _palettes.Get(PaletteSet.Day, Lit);
            if (lift > 0)
            {
                _palettes.Get(Class, _scratch);
                for (var i = 0; i < Ambient.Length; i++)
                {
                    Ambient[i] = (byte)(Ambient[i] + (_scratch[i] - Ambient[i]) * lift);
                }
            }

            if (flash > 0)
            {
                var amount = flash / 32f * FlashAmount;
                for (var i = 0; i < Ambient.Length; i++)
                {
                    Ambient[i] = (byte)(Ambient[i] + (Lit[i] - Ambient[i]) * amount);
                }
            }
        }

        // Over the colours the world mostly uses (not the cycling ones from 0xE0).
        var sum = Vector3.Zero;
        var litSum = 0f;
        for (var i = 0; i < 0xE0; i++)
        {
            sum += new Vector3(Ambient[3 * i], Ambient[3 * i + 1], Ambient[3 * i + 2]);
            litSum += Lit[3 * i] + Lit[3 * i + 1] + Lit[3 * i + 2];
        }

        AmbientMean = sum / (0xE0 * 255f);
        AmbientLevel = litSum > 0 ? Mathf.Clamp((sum.X + sum.Y + sum.Z) / litSum, 0f, 1f) : 1f;
        Version++;
    }

    /// <summary>
    /// The cloud shadows and the fog's mist for the world shader. Exult's
    /// clouds (SPRITES.VGA shape 2 in colour 253) darken the ground they
    /// cross; here shadows drift with the newest clouds' wind (world pixels
    /// per 100 ms of effect time), as many as the clouds there are, coming
    /// and going over 4 s. Neither shows indoors or in a dungeon.
    /// </summary>
    void UpdateSky()
    {
        if (_effects is null)
        {
            return;
        }

        var now = _effects.NowMs;
        var steps = (float)((now - _effectMs) / 100);
        _effectMs = now;
        var clouds = 0f;
        Vector2? wind = null;
        foreach (var w in _effects.Weather)
        {
            if (w is not CloudsEffect c || now < c.StartMs)
            {
                continue;
            }

            var fade = Math.Clamp((now - c.StartMs) / CloudFadeMs, 0, 1) * Math.Clamp((c.StopMs - now) / CloudFadeMs, 0, 1);
            clouds += c.Clouds.Count * (float)fade;
            if (wind is null)
            {
                var drift = Vector2.Zero;
                foreach (var (dx, dy) in c.Clouds)
                {
                    drift += new Vector2(dx, dy);
                }

                wind = drift / Math.Max(1, c.Clouds.Count);
            }
        }

        if (wind is { } blowing)
        {
            _wind = blowing;
        }

        var outdoors = !Inside && !InDungeon;
        var look = WeatherLook.Clouds;
        CloudOffset += _wind * steps;
        CloudCover = Mathf.Clamp(clouds * look.CoverPerCloud, 0f, 0.85f);
        CloudShadow = outdoors ? look.Shadow * Mathf.Clamp(clouds, 0f, 1f) : 0f;
        var mistSpeed = _wind != Vector2.Zero ? _wind.Normalized() : MistDirection;
        MistOffset += mistSpeed * WeatherLook.Fog.Drift * (steps / 10f);
        Mist = outdoors ? FogWeight * WeatherLook.Fog.Mist : 0f;
    }

    /// <summary>For the agent console's <c>light</c>.</summary>
    public string Describe()
    {
        var final = Override >= 0 ? PaletteSet.Name(Override)
            : From == To || T <= 0 ? PaletteSet.Name(From)
            : $"{PaletteSet.Name(From)}+{PaletteSet.Name(To)}";
        var left = _clock.SpecialLight == 0 ? "none" : $"{_clock.SpecialLight - _clock.TotalMinutes} min left";
        var sb = new System.Text.StringBuilder();
        sb.Append(FormattableString.Invariant(
            $"palette {PaletteSet.Name(From)} -> {PaletteSet.Name(To)} t {T:0.00}, dungeon {(InDungeon ? "yes" : "no")}, final {final}; "));
        sb.Append(FormattableString.Invariant(
            $"light spell {left}, shows {(_clock.LightSpellShows(InDungeon) ? "yes" : "no")}, strength {SpellStrength:0.00}; "));
        sb.Append(FormattableString.Invariant(
            $"level {Level} ({(Class < 0 ? "none" : PaletteSet.Name(Class))}), carried {Carried}; lights {LightCount} of {_found.Count + MagicLights + (SpellStrength > 0 ? 1 : 0)} found, {MagicLights} magic, flash {Flash:0.00}; "));
        sb.Append(FormattableString.Invariant(
            $"{(Inside ? "inside" : "outside")}, overcast {OvercastWeight:0.00}, fog {FogWeight:0.00}, ambient {AmbientLevel:0.00}, clouds cover {CloudCover:0.00} shadow {CloudShadow:0.00}, mist {Mist:0.00}; windows {Windows.Count} of roofed rooms, {Windows.Lit} lit"));
        for (var i = 0; i < _found.Count && i < 12; i++)
        {
            var l = _found[i];
            sb.Append(FormattableString.Invariant(
                $"\n  {l.Shape}:{l.Frame} b{l.Brightness} at {l.Source.Tx},{l.Source.Ty},{l.Source.Tz} {l.Glow.Kind}{(l.Glow.Kind == GlowKind.Window ? $" facing {"SENW"[l.Kind - WindowLights.KindWindow]}" : "")}{(l.Carried ? $" carried by {(l.Source.NpcName.Length > 0 ? l.Source.NpcName : l.Source.Shape.ToString())}" : "")} radius {l.RadiusPx / U7Constants.TileSize:0.0} tiles"));
        }

        return sb.ToString();
    }
}
