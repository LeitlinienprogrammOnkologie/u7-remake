using Godot;
using U7.Usecode;

namespace U7.UI;

/// <summary>
/// Exult's <c>display_map</c> picture drawn the modern way: the painted map
/// (<see cref="PaintedMap"/>) smooth at the largest size that fits the window,
/// centred like <c>Paint_centered</c>, over the world as Exult paints it; the
/// avatar's mark where the warp puts the avatar's tile. Exult decides when
/// the mark shows (<c>Paint_map</c>: a sextant in the party, the avatar
/// outdoors); its look is the user's pick (2026-10-10) of Exult's cross, an
/// ink cross, the avatar's face and this: a pulsing gold ring.
/// </summary>
public sealed partial class MapView : Node2D
{
    /// <summary>The picture the usecode waits on, if any.</summary>
    public Func<UsecodePicture?>? ShownPicture;

    static readonly Color Outline = new(0.08f, 0.05f, 0.02f, 0.9f);
    const float RippleMsec = 1600;

    PaintedMap? _map;
    bool _loaded;
    bool _drawn;

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
    }

    /// <summary>The painted map, read the first time the map is shown; null without it.</summary>
    public PaintedMap? Map
    {
        get
        {
            if (!_loaded)
            {
                _loaded = true;
                _map = PaintedMap.Load();
            }

            return _map;
        }
    }

    /// <summary>Whether this view paints <paramref name="picture"/> (the map, with its painting at hand).</summary>
    public bool Paints(UsecodePicture picture) => picture.Sprite == BgIntrinsics.MapSprite && Map is not null;

    public override void _Process(double delta)
    {
        // Redrawn while shown (the ripple), and once more when it goes.
        if (_drawn || ShownPicture?.Invoke() is { } picture && Paints(picture))
        {
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        _drawn = false;
        if (ShownPicture?.Invoke() is not { } picture || !Paints(picture) || Map is not { } map)
        {
            return;
        }

        _drawn = true;
        var view = GetViewport().GetVisibleRect().Size;
        var scale = Math.Min(view.X * 0.95f / map.Size.X, view.Y * 0.95f / map.Size.Y);
        var shown = map.Size * scale;
        var origin = ((view - shown) / 2).Floor();
        DrawTextureRect(map.Texture, new Rect2(origin, shown), false);
        if (picture.Mark is { } tile)
        {
            // One pixel of Exult's 202-pixel map sprite at this size (its cross is 5 across).
            DrawMark(origin + map.ToPixel(tile) * scale, shown.X / 202f);
        }
    }

    /// <summary>A gold ring and dot with a dark outline, a fading ripple running out of it.</summary>
    void DrawMark(Vector2 at, float unit)
    {
        var line = Math.Max(1f, unit * 0.35f);
        var radius = unit * 3f;
        var thick = Math.Max(2f, unit * 0.7f);
        var t = Time.GetTicksMsec() % (ulong)RippleMsec / RippleMsec;
        DrawArc(at, radius * (1 + 1.4f * t), 0, Mathf.Tau, 48, UiTheme.GoldBright with { A = 0.8f * (1 - t) }, thick * 0.8f, true);
        DrawArc(at, radius, 0, Mathf.Tau, 48, Outline, thick + 2 * line, true);
        DrawArc(at, radius, 0, Mathf.Tau, 48, UiTheme.GoldBright, thick, true);
        DrawCircle(at, thick * 0.9f + line, Outline);
        DrawCircle(at, thick * 0.9f, UiTheme.GoldBright);
    }
}
