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
/// (the day's), and the lights: the light spell round the avatar and the
/// pools of the light sources in and around the view.
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
    (int From, int To, float T, int Override, int Class, float Lift)? _built;
    bool _snap;
    ulong _ticks;

    /// <summary>A light source found in the view: what it is, how it looks, and how it ranks.</summary>
    public readonly record struct SceneLight(
        U7Object Source, int Shape, int Frame, int Brightness, Glow Glow, Vector2 Centre, float RadiusPx, float Score, bool Carried);

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

    /// <summary>
    /// How far a dark ambient palette is lifted towards the palette of
    /// Exult's light class (candle, single, many). 0, the default, leaves the
    /// lighting to the pools.
    /// </summary>
    public float AmbientLift { get; set; }

    public SceneLighting(GameClock clock, GameMap map, ShapeCatalog catalog, U7Object avatar, EffectsManager? effects,
        PartyManager? party)
    {
        _clock = clock;
        _map = map;
        _catalog = catalog;
        _avatar = avatar;
        _effects = effects;
        _party = party;
        _frameOf = obj => WorldView.DisplayFrame(_catalog, obj, _ticks);
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
        BuildPalettes();

        LightCount = 0;
        var spell = Override != PaletteSet.Invisible && _clock.LightSpellShows(InDungeon);
        SpellStrength = _snap ? spell ? 1f : 0f
            : Mathf.MoveToward(SpellStrength, spell ? 1f : 0f, (float)delta * SpellFadeRate);
        _snap = false;
        if (SpellStrength > 0)
        {
            AddLight(FigureCentre(av), SpellRadius, KindSpell, new Vector3(SpellStrength, SpellStrength, SpellStrength), 0);
        }

        FindLights(ticks);
        var seconds = ticks / 1000.0;
        for (var i = 0; i < _found.Count && LightCount < MaxLights; i++)
        {
            var l = _found[i];
            var intensity = l.Glow.Intensity;
            var radius = l.RadiusPx;
            var phase = Phase(l);
            switch (l.Glow.Kind)
            {
                case GlowKind.Flame:
                {
                    var amount = FlickerAmount * (l.Glow.Small ? 0.6f : 1f);
                    var f = Flicker(seconds * (l.Glow.Small ? 1.5 : 1.0), phase);
                    intensity *= 1f - amount * (0.5f + 0.5f * f);
                    radius *= 1f + 0.04f * f;
                    break;
                }
                case GlowKind.Magic:
                    intensity *= 0.8f + 0.2f * (float)Math.Sin(seconds * 2.0 + phase);
                    break;
            }

            AddLight(l.Centre, radius, KindPool, l.Glow.Colour * intensity, 0);
        }
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
        var skip = _map.RoofHeight(av.Tx, av.Ty, av.Tz);
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
                    if (!WorldView.IsPainted(obj, skip, av) || (_map.DungeonHeight(obj.Tx, obj.Ty) != 0) != InDungeon)
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

        _found.Sort(ByScore);
    }

    static readonly Comparison<SceneLight> ByScore = (a, b) => b.Score.CompareTo(a.Score);

    void Add(U7Object source, int shape, int frame, int brightness, Vector2 centre, bool carried, Vector2 mid)
    {
        var glow = GlowTable.For(shape, brightness);
        var radius = glow.RadiusTiles * U7Constants.TileSize;
        if (!carried && GlowTable.IsPool(shape))
        {
            radius = Mathf.Max(radius, Mathf.Max(source.DimX, source.DimY) * U7Constants.TileSize / 2f);
        }

        var score = glow.Intensity * radius / (1f + centre.DistanceTo(mid) / 200f);
        _found.Add(new SceneLight(source, shape, frame, brightness, glow, centre, radius, score, carried));
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
        var key = (From, To, T, Override, Class, lift);
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
            _palettes.Blend(From, To, T, Ambient);
            _palettes.Get(PaletteSet.Day, Lit);
            if (lift > 0)
            {
                _palettes.Get(Class, _scratch);
                for (var i = 0; i < Ambient.Length; i++)
                {
                    Ambient[i] = (byte)(Ambient[i] + (_scratch[i] - Ambient[i]) * lift);
                }
            }
        }

        Version++;
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
            $"level {Level} ({(Class < 0 ? "none" : PaletteSet.Name(Class))}), carried {Carried}; lights {LightCount} of {_found.Count + (SpellStrength > 0 ? 1 : 0)} found"));
        for (var i = 0; i < _found.Count && i < 12; i++)
        {
            var l = _found[i];
            sb.Append(FormattableString.Invariant(
                $"\n  {l.Shape}:{l.Frame} b{l.Brightness} at {l.Source.Tx},{l.Source.Ty},{l.Source.Tz} {l.Glow.Kind}{(l.Carried ? $" carried by {(l.Source.NpcName.Length > 0 ? l.Source.NpcName : l.Source.Shape.ToString())}" : "")} radius {l.RadiusPx / U7Constants.TileSize:0.0} tiles"));
        }

        return sb.ToString();
    }
}
