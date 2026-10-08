using System.Runtime.CompilerServices;
using Godot;
using U7.World;

namespace U7.Rendering;

/// <summary>
/// Draws each <see cref="RainEffect"/>'s rain, snow or sparkles over the
/// world, below the screen effects. The particles move as Exult's
/// <c>Basicdrop</c>s do (one step per 100 ms of effect time, so they stand
/// still in gump mode and conversations; a drop that leaves the view, or a
/// sparkle at its last frame, starts again at a random spot in the view's
/// top-left seven eighths by three quarters), drawn between their last two
/// steps. Exult's 200 drops are for a 320×200 view: the count scales with
/// the view. Under a roof only sparkles of weather 3 show, as in Exult.
/// </summary>
public partial class WeatherView : Node2D
{
    const float StepMs = 100;
    /// <summary>Exult's drops are for a 320×200 view.</summary>
    const float ExultViewArea = 320 * 200;
    /// <summary>The most steps caught up at once; past that the drops just start again.</summary>
    const int MaxCatchUp = 30;

    public EffectsManager Effects = null!;
    public SceneLighting Lighting = null!;

    readonly Dictionary<RainEffect, Drops> _drops = new();
    readonly List<RainEffect> _gone = new();
    readonly List<Vector2> _lines = new();
    readonly List<Color> _lineColours = new();
    readonly List<Vector2> _splashes = new();
    readonly List<Color> _splashColours = new();
    Node2D _glints = null!;
    Texture2D _dot = null!;
    Texture2D _star = null!;
    Rect2 _view;

    /// <summary>One effect's particles: Exult's <c>Particle</c>s, at their last two steps.</summary>
    sealed class Drops(RainEffect effect, int capacity)
    {
        public readonly RainEffect Effect = effect;
        public readonly Random Rng = new(RuntimeHelpers.GetHashCode(effect));
        public readonly float[] X = new float[capacity];
        public readonly float[] Y = new float[capacity];
        public readonly float[] PrevX = new float[capacity];
        public readonly float[] PrevY = new float[capacity];
        /// <summary>Exult's frame, -1 before the first step.</summary>
        public readonly int[] Frame = new int[capacity];
        public readonly bool[] Forward = new bool[capacity];
        /// <summary>Steps since it last started again.</summary>
        public readonly int[] Age = new int[capacity];
        public int Count;
        /// <summary>Steps taken (the effect's events so far).</summary>
        public long Steps;
    }

    public override void _Ready()
    {
        ZIndex = 1;
        _dot = SoftDot(16);
        _star = Star(15);
        _glints = new Node2D
        {
            Name = "Glints",
            Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add }
        };
        _glints.Draw += DrawGlints;
        AddChild(_glints);
    }

    public override void _Process(double delta)
    {
        if (Effects is null || Lighting is null)
        {
            return;
        }

        var cam = GetViewport().GetCamera2D();
        if (cam is null)
        {
            return;
        }

        var size = GetViewport().GetVisibleRect().Size / cam.Zoom;
        _view = new Rect2(cam.GlobalPosition - size / 2, size);
        var now = Effects.NowMs;
        foreach (var w in Effects.Weather)
        {
            if (w is not RainEffect e || now < e.StartMs)
            {
                continue;
            }

            if (!_drops.TryGetValue(e, out var drops))
            {
                drops = new Drops(e, WeatherLook.MaxParticles);
                _drops[e] = drops;
            }

            Advance(drops, now);
        }

        _gone.Clear();
        foreach (var e in _drops.Keys)
        {
            if (!Effects.Weather.Contains(e))
            {
                _gone.Add(e);
            }
        }

        foreach (var e in _gone)
        {
            _drops.Remove(e);
        }

        QueueRedraw();
        _glints.QueueRedraw();
    }

    /// <summary>Exult <c>Rain_effect::handle_event</c>'s moves, up to the effect's time.</summary>
    void Advance(Drops d, double now)
    {
        var e = d.Effect;
        var steps = (long)((now - e.StartMs) / StepMs) + 1;
        var target = Math.Min(WeatherLook.MaxParticles,
            (int)MathF.Round(e.Drops * _view.Size.X * _view.Size.Y / ExultViewArea * Density(e.Kind)));
        for (var i = d.Count; i < target; i++)
        {
            d.Frame[i] = -1;
            d.Forward[i] = true;
            d.X[i] = d.Y[i] = float.NaN;
        }

        d.Count = target;
        // Exult moves no drops inside (sparkles of weather 3 excepted).
        if (Covered(e) || steps - d.Steps > MaxCatchUp)
        {
            d.Steps = Math.Max(d.Steps, steps - (Covered(e) ? 0 : 1));
        }

        for (; d.Steps < steps; d.Steps++)
        {
            Step(d);
        }
    }

    bool Covered(RainEffect e) => Lighting.Inside && !e.ShownIndoors;

    static float Density(DropKind kind) =>
        kind == DropKind.Raindrop ? WeatherLook.Rain.Density
        : kind == DropKind.Snowflake ? WeatherLook.Snow.Density
        : WeatherLook.Sparkles.Density;

    /// <summary>Exult <c>Basicdrop::do_move</c> for each particle.</summary>
    void Step(Drops d)
    {
        var k = d.Effect.Kind;
        var w = _view.Size.X;
        var h = _view.Size.Y;
        for (var i = 0; i < d.Count; i++)
        {
            d.PrevX[i] = d.X[i];
            d.PrevY[i] = d.Y[i];
            NextFrame(d, i, k);
            var x = d.X[i] - _view.Position.X;
            var y = d.Y[i] - _view.Position.Y;
            if (!(x >= 0 && x < w && y >= 0 && y < h) || (k.Delta == 0 && d.Frame[i] == k.FrameN))
            {
                d.X[i] = d.PrevX[i] = _view.Position.X + d.Rng.NextSingle() * (w - w / 8);
                d.Y[i] = d.PrevY[i] = _view.Position.Y + d.Rng.NextSingle() * (h - h / 4);
                d.Age[i] = 0;
            }
            else
            {
                d.X[i] += k.Delta;
                d.Y[i] += k.Delta;
                d.Age[i]++;
            }
        }
    }

    /// <summary>Exult <c>set_frame</c>: from the first frame (sparkles a random one, either way) up to the last and back.</summary>
    static void NextFrame(Drops d, int i, DropKind k)
    {
        var frame = d.Frame[i];
        if (frame < 0)
        {
            if (k.Randomize)
            {
                var back = d.Rng.Next(2);
                d.Forward[i] = back == 0;
                frame = k.Frame0 + d.Rng.Next(k.FrameN - k.Frame0) + back;
            }
            else
            {
                frame = k.Frame0;
            }
        }
        else if (d.Forward[i])
        {
            if (++frame == k.FrameN)
            {
                d.Forward[i] = false;
            }
        }
        else if (--frame == k.Frame0)
        {
            d.Forward[i] = true;
        }

        d.Frame[i] = frame;
    }

    /// <summary>How far between its last two steps the effect is, 0 to 1.</summary>
    float Between(Drops d) =>
        Mathf.Clamp((float)((Effects.NowMs - d.Effect.StartMs) / StepMs - (d.Steps - 1)), 0f, 1f);

    /// <summary>Particles darken with the time of day and brighten in a lightning flash.</summary>
    float Light()
    {
        var level = 0.3f + 0.7f * Lighting.AmbientLevel;
        var flash = Lighting.Override == PaletteSet.Lightning ? 1f : Lighting.Flash;
        return Mathf.Lerp(level, 1.6f, flash);
    }

    public override void _Draw()
    {
        var light = Light();
        _lines.Clear();
        _lineColours.Clear();
        _splashes.Clear();
        _splashColours.Clear();
        foreach (var d in _drops.Values)
        {
            if (Covered(d.Effect))
            {
                continue;
            }

            var t = Between(d);
            if (d.Effect.Kind == DropKind.Raindrop)
            {
                AddRain(d, t, light);
            }
            else if (d.Effect.Kind == DropKind.Snowflake)
            {
                DrawSnow(d, t, light);
            }
        }

        if (_lines.Count > 0)
        {
            var width = WeatherLook.Rain.Width;
            DrawMultilineColors(_lines.ToArray(), _lineColours.ToArray(), width > 0 ? width : -1f, width > 0);
        }

        if (_splashes.Count > 0)
        {
            DrawMultilineColors(_splashes.ToArray(), _splashColours.ToArray());
        }
    }

    /// <summary>A streak along +x+y behind each drop, brighter towards its head; Exult's frames 3-6 go from darker to lighter blue.</summary>
    void AddRain(Drops d, float t, float light)
    {
        var look = WeatherLook.Rain;
        var dir = new Vector2(1, 1).Normalized();
        for (var i = 0; i < d.Count; i++)
        {
            if (float.IsNaN(d.X[i]) || d.Frame[i] < 0)
            {
                continue;
            }

            var head = new Vector2(Mathf.Lerp(d.PrevX[i], d.X[i], t), Mathf.Lerp(d.PrevY[i], d.Y[i], t));
            var fresh = Mathf.Clamp((d.Age[i] + t) / 2f, 0f, 1f);
            var shade = 0.75f + 0.25f * (d.Frame[i] - DropKind.Raindrop.Frame0) / 3f;
            var colour = new Color(0.72f * shade * light, 0.78f * shade * light, 1f * light, look.Alpha * fresh);
            var tail = head - dir * look.Length;
            var mid = head - dir * look.Length * 0.5f;
            _lines.Add(tail);
            _lines.Add(mid);
            _lineColours.Add(colour with { A = colour.A * 0.4f });
            _lines.Add(mid);
            _lines.Add(head);
            _lineColours.Add(colour);
            if (look.Splash > 0 && d.Frame[i] == DropKind.Raindrop.FrameN)
            {
                // Exult's frame 7, a little blue splash: a thin ring that opens through the step.
                var ring = new Color(0.6f * light, 0.68f * light, 1f * light, look.Splash * (1f - t) * fresh);
                var r = 1f + 2.5f * t;
                for (var s = 0; s < 8; s++)
                {
                    var a0 = s * Mathf.Tau / 8;
                    var a1 = (s + 1) * Mathf.Tau / 8;
                    _splashes.Add(head + new Vector2(Mathf.Cos(a0) * r, Mathf.Sin(a0) * r * 0.5f));
                    _splashes.Add(head + new Vector2(Mathf.Cos(a1) * r, Mathf.Sin(a1) * r * 0.5f));
                    _splashColours.Add(ring);
                }
            }
        }
    }

    /// <summary>Soft flakes drifting +x+y, swaying as Exult's frames 13-20 move the pixel round its box.</summary>
    void DrawSnow(Drops d, float t, float light)
    {
        var look = WeatherLook.Snow;
        var seconds = (float)(Effects.NowMs / 1000.0);
        for (var i = 0; i < d.Count; i++)
        {
            if (float.IsNaN(d.X[i]) || d.Frame[i] < 0)
            {
                continue;
            }

            var phase = i * 2.399f;
            var pos = new Vector2(Mathf.Lerp(d.PrevX[i], d.X[i], t), Mathf.Lerp(d.PrevY[i], d.Y[i], t));
            pos.X += look.Sway * Mathf.Sin(seconds * Mathf.Tau / 1.4f + phase);
            var fresh = Mathf.Clamp((d.Age[i] + t) / 3f, 0f, 1f);
            var size = look.Size * (0.7f + 0.3f * ((i * 7919) % 100) / 100f);
            var grey = (0.85f + 0.15f * Mathf.Sin(seconds * 2.1f + phase)) * light;
            DrawTextureRect(_dot, new Rect2(pos - new Vector2(size, size) / 2, new Vector2(size, size)), false,
                new Color(grey, grey, grey, look.Alpha * fresh));
        }
    }

    /// <summary>
    /// Sparkles, added to what is under them: Exult's frames 21-27 go from
    /// white to dark grey, and a sparkle starts again at a new spot at 27.
    /// Weather 3's are magic and don't darken at night; the fog's do.
    /// </summary>
    void DrawGlints()
    {
        var look = WeatherLook.Sparkles;
        var light = Light();
        foreach (var d in _drops.Values)
        {
            if (d.Effect.Kind != DropKind.Sparkle || Covered(d.Effect))
            {
                continue;
            }

            var t = Between(d);
            var dim = d.Effect.ShownIndoors ? 1f : light;
            var k = DropKind.Sparkle;
            for (var i = 0; i < d.Count; i++)
            {
                if (float.IsNaN(d.X[i]) || d.Frame[i] < 0)
                {
                    continue;
                }

                // Towards the next frame through the step, so the glint swells and fades smoothly.
                var next = d.Frame[i] + (d.Forward[i] ? 1 : -1);
                var frame = Mathf.Lerp(d.Frame[i], Mathf.Clamp(next, k.Frame0, k.FrameN), t);
                var bright = 1f - (frame - k.Frame0) / (k.FrameN - k.Frame0);
                if (d.Age[i] == 0)
                {
                    bright *= t;
                }

                var size = look.Size * (0.5f + 0.5f * bright);
                var a = look.Brightness * bright * bright * dim;
                var pos = new Vector2(d.X[i], d.Y[i]);
                _glints.DrawTextureRect(_star, new Rect2(pos - new Vector2(size, size) / 2, new Vector2(size, size)), false,
                    new Color(a, a, a * 0.95f + 0.05f, 1f));
            }
        }
    }

    /// <summary>A white dot, solid in its middle third, fading to its edge.</summary>
    static ImageTexture SoftDot(int n)
    {
        var img = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        var c = (n - 1) / 2f;
        for (var y = 0; y < n; y++)
        {
            for (var x = 0; x < n; x++)
            {
                var r = new Vector2(x - c, y - c).Length() / (n / 2f);
                var a = 1f - Mathf.SmoothStep(0.35f, 1f, r);
                img.SetPixel(x, y, new Color(1, 1, 1, a));
            }
        }

        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>A four-pointed glint: a bright core and thin rays, black where it adds nothing.</summary>
    static ImageTexture Star(int n)
    {
        var img = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        var c = (n - 1) / 2f;
        for (var y = 0; y < n; y++)
        {
            for (var x = 0; x < n; x++)
            {
                var dx = Mathf.Abs(x - c) / c;
                var dy = Mathf.Abs(y - c) / c;
                var core = Mathf.Clamp(1f - Mathf.Sqrt(dx * dx + dy * dy) * 2.2f, 0f, 1f);
                var rays = Mathf.Max(Mathf.Clamp(1f - dx, 0f, 1f) * Mathf.Clamp(1f - dy * 6f, 0f, 1f),
                    Mathf.Clamp(1f - dy, 0f, 1f) * Mathf.Clamp(1f - dx * 6f, 0f, 1f));
                var v = Mathf.Clamp(core + rays * 0.8f, 0f, 1f);
                img.SetPixel(x, y, new Color(v, v, v, 1f));
            }
        }

        return ImageTexture.CreateFromImage(img);
    }
}
