using Godot;
using U7.Gumps;
using U7.Rendering;

namespace U7.UI;

/// <summary>
/// Barks (item_say, script say) and the names a click shows, in screen space:
/// MedievalSharp in yellow with a dark outline, sized with the zoom, centred
/// above the speaker's sprite and kept on screen. Positions come from
/// <see cref="WorldView.Barks"/> and, for things in open gumps,
/// <see cref="GumpView.Texts"/>.
/// </summary>
public sealed partial class BarkOverlay : Control
{
    public WorldView? World { get; set; }
    public GumpView? Gumps { get; set; }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (World is not { } world)
        {
            return;
        }

        var xf = world.GetGlobalTransformWithCanvas();
        var zoom = xf.X.X;
        var size = Mathf.Clamp(Mathf.RoundToInt(6.5f * zoom), 14, 40);
        var outline = Math.Max(4, size / 5);
        var font = UiTheme.Font;
        var view = GetViewportRect().Size;
        foreach (var bark in world.Barks)
        {
            DrawText(bark.Text, xf * bark.WorldTop);
        }

        if (Gumps is { } gumps)
        {
            foreach (var (text, top) in gumps.Texts())
            {
                DrawText(text, top);
            }
        }

        void DrawText(string text, Vector2 top)
        {
            var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
            var x = Mathf.Clamp(top.X - width / 2, 8, Math.Max(8, view.X - width - 8));
            var baseline = Mathf.Max(top.Y - font.GetDescent(size) - 4, font.GetAscent(size) + 8);
            var pos = new Vector2(x, baseline);
            DrawStringOutline(font, pos, text, HorizontalAlignment.Left, -1, size, outline, UiTheme.Ink);
            DrawString(font, pos, text, HorizontalAlignment.Left, -1, size, UiTheme.BarkYellow);
        }
    }
}
