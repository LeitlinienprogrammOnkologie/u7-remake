using Godot;

namespace U7.Rendering;

/// <summary>
/// Whole-screen effects, drawn over the world, the gumps, the barks and the
/// conversation, as Exult's palette effects change the whole window: the
/// fades (Exult scales the palette towards black, <c>Palette::fade_in</c>/
/// <c>fade_out</c>, which a black veil over the picture matches) and the red
/// flash of a badly hurt avatar (<c>Palette::flash_red</c>, the RED palette
/// for 100 ms), drawn as a red pulse at the screen's edges instead.
/// </summary>
public partial class ScreenFx : Node2D
{
    const string PulseShader = """
        shader_type canvas_item;

        uniform float strength = 0.0;
        // How far in from the edges the red reaches, as a part of the shorter side.
        uniform float width = 0.2;
        uniform vec3 colour = vec3(0.85, 0.05, 0.02);
        uniform vec2 size = vec2(1280.0, 800.0);

        void fragment() {
            vec2 px = SCREEN_UV * size;
            float edge = min(min(px.x, size.x - px.x), min(px.y, size.y - px.y)) / min(size.x, size.y);
            float a = strength * (1.0 - smoothstep(0.0, width, edge));
            COLOR = vec4(colour, a);
        }
        """;

    /// <summary>The pulse's timing: up in 30 ms, held to 100 ms, gone 300 ms later.</summary>
    const double RiseMs = 30, HoldMs = 100, DecayMs = 300;

    float _fade = 1f;
    Node2D _pulse = null!;
    ShaderMaterial _pulseMaterial = null!;
    double _pulseMs = double.MaxValue;
    float _pulseFrom;

    /// <summary>
    /// The red pulse at its brightest, 0 to 1, and how far in from the edges
    /// it reaches, as a part of the shorter side: the user's picks
    /// (2026-10-08) of 0.35, 0.55 and 0.8 and of 0.12, 0.2 and 0.32.
    /// </summary>
    const float PulseStrength = 0.35f;
    const float PulseWidth = 0.12f;
    /// <summary>How many red pulses have started (for the agent console).</summary>
    public int Pulses { get; private set; }
    /// <summary>The pulse now, 0 to 1 of <see cref="PulseStrength"/>.</summary>
    public float Pulse { get; private set; }

    /// <summary>How bright the screen is under the fade, 0 black to 1 untouched.</summary>
    public float Fade
    {
        get => _fade;
        set
        {
            if (value != _fade)
            {
                _fade = value;
                QueueRedraw();
            }
        }
    }

    public override void _Ready()
    {
        _pulseMaterial = new ShaderMaterial { Shader = new Shader { Code = PulseShader } };
        // Behind the fade: on a black screen the flash is not seen, as Exult's palette isn't set then.
        _pulse = new Node2D { Name = "RedPulse", Material = _pulseMaterial, ShowBehindParent = true, Visible = false };
        _pulse.Draw += () => _pulse.DrawRect(GetViewportRect(), Colors.White);
        AddChild(_pulse);
    }

    /// <summary>Exult <c>Palette::flash_red</c>: the red pulse starts again (rising from where it is).</summary>
    public void PulseRed()
    {
        _pulseFrom = Pulse;
        _pulseMs = 0;
        Pulses++;
    }

    public override void _Process(double delta)
    {
        if (_fade < 1f)
        {
            QueueRedraw(); // the window may change size
        }

        _pulseMs += delta * 1000;
        Pulse = _pulseMs < RiseMs ? Mathf.Lerp(_pulseFrom, 1f, (float)(_pulseMs / RiseMs))
            : _pulseMs < HoldMs ? 1f
            : Mathf.Max(0f, 1f - (float)((_pulseMs - HoldMs) / DecayMs));
        _pulse.Visible = Pulse > 0;
        if (_pulse.Visible)
        {
            _pulseMaterial.SetShaderParameter("strength", Pulse * PulseStrength);
            _pulseMaterial.SetShaderParameter("width", PulseWidth);
            _pulseMaterial.SetShaderParameter("size", GetViewportRect().Size);
            _pulse.QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (_fade < 1f)
        {
            DrawRect(GetViewportRect(), new Color(0, 0, 0, 1f - _fade));
        }
    }
}
