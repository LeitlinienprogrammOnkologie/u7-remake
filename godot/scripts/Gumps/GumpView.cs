using Godot;
using U7.Core;
using U7.Data;
using U7.Rendering;

namespace U7.Gumps;

/// <summary>
/// Paints open gumps in unzoomed virtual pixels, then scales by the world zoom
/// so art stays pixel-aligned with the map.
/// </summary>
public partial class GumpView : Node2D
{
    public GumpManager Gumps = null!;
    public ShapeCache Shapes = null!;
    public ShapeCatalog Catalog = null!;
    public GameMap Map = null!;
    public float Zoom = 4f;

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.Nearest;
        ZIndex = 0;
    }

    public override void _Process(double delta)
    {
        var view = GetViewport().GetVisibleRect().Size;
        Gumps.ScreenW = Math.Max(1, Mathf.RoundToInt(view.X / Zoom));
        Gumps.ScreenH = Math.Max(1, Mathf.RoundToInt(view.Y / Zoom));
        QueueRedraw();
    }

    public Vector2I MouseVirtual()
    {
        var m = GetViewport().GetMousePosition();
        return new Vector2I(Mathf.FloorToInt(m.X / Zoom), Mathf.FloorToInt(m.Y / Zoom));
    }

    public override void _Draw()
    {
        DrawSetTransform(Vector2.Zero, 0, new Vector2(Zoom, Zoom));
        foreach (var gump in Gumps.Open)
        {
            gump.Paint(this);
        }

        var drag = Gumps.Drag;
        if (drag is { Moved: true, Object: { } obj })
        {
            DrawWorldShape(obj.Shape, obj.Frame, drag.PaintX, drag.PaintY);
        }
    }

    public void DrawGumpShape(int shape, int frame, int hx, int hy)
    {
        var tex = Shapes.GetGump(shape, frame);
        if (tex is null && frame != 0)
        {
            tex = Shapes.GetGump(shape, 0);
            frame = 0;
        }

        if (tex is null)
        {
            return;
        }

        var fi = Shapes.GetGumpFrame(shape, frame);
        DrawTexture(tex, new Vector2(hx - fi.XLeft, hy - fi.YAbove));
    }

    public void DrawWorldShape(int shape, int frame, int hx, int hy)
    {
        var tex = Shapes.Get(shape, frame);
        if (tex is null && (frame & 32) != 0)
        {
            tex = Shapes.Get(shape, frame & 0x1f);
            frame &= 0x1f;
        }

        if (tex is null)
        {
            return;
        }

        var fi = Catalog[shape].GetFrame(frame);
        DrawTexture(tex, new Vector2(hx - fi.XLeft, hy - fi.YAbove));
    }

    public bool WorldSpriteContains(U7Object obj, int mx, int my, int hx, int hy)
    {
        var tex = Shapes.Get(obj.Shape, obj.Frame);
        var frame = obj.Frame;
        if (tex is null && (frame & 32) != 0)
        {
            tex = Shapes.Get(obj.Shape, frame & 0x1f);
            frame &= 0x1f;
        }

        if (tex is null)
        {
            return false;
        }

        var fi = Catalog[obj.Shape].GetFrame(frame);
        var left = hx - fi.XLeft;
        var top = hy - fi.YAbove;
        if (mx < left || my < top || mx >= left + tex.GetWidth() || my >= top + tex.GetHeight())
        {
            return false;
        }

        var image = Shapes.GetImage(obj.Shape, frame);
        if (image is null)
        {
            return true;
        }

        var px = mx - left;
        var py = my - top;
        if (px < 0 || py < 0 || px >= image.GetWidth() || py >= image.GetHeight())
        {
            return false;
        }

        return image.GetPixel(px, py).A > 0.08f;
    }

    public void DrawFontCentered(int font, string text, int x, int y, int width)
    {
        var w = MeasureFont(font, text);
        DrawFontString(font, text, x + Math.Max(0, (width - w) / 2), y);
    }

    public void DrawFontNumRight(int font, int num, int rightX, int y)
    {
        var text = num.ToString();
        var w = MeasureFont(font, text);
        DrawFontString(font, text, rightX - w, y);
    }

    public void DrawFontString(int font, string text, int x, int y)
    {
        var cx = x;
        foreach (var ch in text)
        {
            var frame = (int)ch;
            var tex = Shapes.GetFontGlyph(font, frame) ?? Shapes.GetFontGlyph(0, frame);
            if (tex is null)
            {
                cx += 4;
                continue;
            }

            var fi = Shapes.GetFontFrame(font, frame);
            DrawTexture(tex, new Vector2(cx - fi.XLeft, y - fi.YAbove));
            cx += Math.Max(1, fi.Width);
        }
    }

    int MeasureFont(int font, string text)
    {
        var w = 0;
        foreach (var ch in text)
        {
            var frame = (int)ch;
            var fi = Shapes.GetFontFrame(font, frame);
            w += fi.Width > 0 ? fi.Width : 4;
        }

        return w;
    }
}
