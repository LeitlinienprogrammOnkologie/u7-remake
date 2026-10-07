using Godot;

namespace U7.Rendering;

/// <summary>
/// The light spell (Exult <c>special_light</c>, from <c>cause_light</c>) drawn
/// as a magical glow around the avatar instead of Exult's brighter palette
/// for the whole screen. While it shows, the world is tinted towards full
/// light and this multiplies everything outside a softly breathing,
/// shimmering circle back down to the time of day's tint, with a faint blue
/// band at the rim.
/// </summary>
public partial class LightSpellOverlay : Node2D
{
    const string ShaderCode = """
        shader_type canvas_item;
        render_mode blend_mul, unshaded;

        // What brings the world's tint back down to the time of day's outside the glow.
        uniform vec3 outside = vec3(1.0);
        uniform vec2 center;
        uniform float radius = 56.0;
        uniform float strength = 1.0;

        varying vec2 world_pos;

        void vertex() {
            world_pos = VERTEX;
        }

        void fragment() {
            vec2 d = world_pos - center;
            float ang = atan(d.y, d.x);
            // Breathing, with ripples running round the rim.
            float r = radius * (1.0 + 0.05 * sin(TIME * 1.9)
                + 0.025 * sin(ang * 5.0 + TIME * 1.3)
                + 0.015 * sin(ang * 11.0 - TIME * 2.7));
            float dist = length(d);
            float pulse = 0.5 + 0.5 * sin(TIME * 1.1);
            vec3 inner = mix(vec3(1.0), vec3(0.88, 0.93, 1.0), 0.35 * pulse * strength);
            vec3 rim = mix(vec3(1.0), vec3(0.62, 0.74, 1.0), strength);
            vec3 c = mix(outside, rim, smoothstep(r, r * 0.78, dist));
            COLOR = vec4(mix(c, inner, smoothstep(r * 0.8, r * 0.4, dist)), 1.0);
        }
        """;

    /// <summary>How fast the glow comes and goes (strength per second).</summary>
    const float FadeRate = 1.5f;

    ShaderMaterial _material = null!;
    bool _draw;

    /// <summary>Radius of the glow in world pixels (8 per tile).</summary>
    public float Radius = 7 * 8;

    /// <summary>How far the light has come in, 0 to 1: the world is tinted that far towards full light.</summary>
    public float Strength { get; private set; }

    public override void _Ready()
    {
        _material = new ShaderMaterial { Shader = new Shader { Code = ShaderCode } };
        Material = _material;
        ZIndex = 1;
    }

    /// <summary>Ease the light in while the spell shows, out when it ends.</summary>
    public void Advance(double delta, bool on) =>
        Strength = Mathf.MoveToward(Strength, on ? 1f : 0f, (float)delta * FadeRate);

    /// <summary>
    /// Outside the glow, bring the world's current tint back down to the time
    /// of day's (<paramref name="baseTint"/> over <paramref name="worldTint"/>).
    /// </summary>
    public void Update(Color baseTint, Color worldTint, Vector2 center)
    {
        var outside = new Vector3(Ratio(baseTint.R, worldTint.R), Ratio(baseTint.G, worldTint.G), Ratio(baseTint.B, worldTint.B));
        _draw = outside.X < 0.999f || outside.Y < 0.999f || outside.Z < 0.999f || Strength > 0;
        _material.SetShaderParameter("outside", outside);
        _material.SetShaderParameter("center", center);
        _material.SetShaderParameter("radius", Radius);
        _material.SetShaderParameter("strength", Strength);
        QueueRedraw();
    }

    static float Ratio(float target, float current) => current <= 0.001f ? 1f : Mathf.Min(1f, target / current);

    public override void _Draw()
    {
        if (!_draw)
        {
            return;
        }

        var cam = GetViewport().GetCamera2D();
        var view = GetViewport().GetVisibleRect().Size;
        var zoom = cam?.Zoom ?? Vector2.One;
        var half = view / zoom / 2f + new Vector2(16, 16);
        var mid = cam?.GlobalPosition ?? Vector2.Zero;
        DrawRect(new Rect2(mid - half, half * 2), Colors.White);
    }
}
