using Godot;

namespace U7.Rendering;

/// <summary>
/// Whole-screen effects, drawn over the world, the gumps, the barks and the
/// conversation, as Exult's palette effects change the whole window. For now
/// the fades: Exult scales the palette towards black
/// (<c>Palette::fade_in</c>/<c>fade_out</c>), which a black veil over the
/// picture matches.
/// </summary>
public partial class ScreenFx : Node2D
{
    float _fade = 1f;

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

    public override void _Process(double delta)
    {
        if (_fade < 1f)
        {
            QueueRedraw(); // the window may change size
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
