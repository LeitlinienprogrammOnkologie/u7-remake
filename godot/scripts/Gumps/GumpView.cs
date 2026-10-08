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
    /// <summary>The book or scroll whose page the usecode waits on; Exult paints it over everything.</summary>
    public Func<TextGump?>? ShownBook;
    /// <summary>The picture the usecode waits on (the map, a view elsewhere); Exult paints it over everything.</summary>
    public Func<U7.Usecode.UsecodePicture?>? ShownPicture;
    /// <summary>Exult <c>Paint_map</c>'s mark colour, a palette index.</summary>
    const int MapMarkColour = 50;

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

        if (ShownBook?.Invoke() is { } book)
        {
            PaintBook(book);
        }

        if (ShownPicture?.Invoke() is { } picture)
        {
            PaintPicture(picture);
        }
    }

    /// <summary>
    /// A picture is shown like a book, at the largest whole scale that fits the
    /// window, centred (Exult <c>Paint_centered</c>); the map's mark is a cross
    /// of two 5-pixel bars (<c>Paint_map</c>).
    /// </summary>
    void PaintPicture(U7.Usecode.UsecodePicture picture)
    {
        if (picture.Area is not null)
        {
            PaintRemoteView(picture);
            return;
        }

        if (Shapes.GetSprite(picture.Sprite, picture.Frame) is not { } tex)
        {
            return;
        }

        var view = GetViewport().GetVisibleRect().Size;
        var w = tex.GetWidth();
        var h = tex.GetHeight();
        var scale = Math.Max(1, (int)Math.Min(view.X * 0.95f / w, view.Y * 0.95f / h));
        DrawSetTransform(Vector2.Zero, 0, new Vector2(scale, scale));
        var x = ((int)(view.X / scale) - w) / 2;
        var y = ((int)(view.Y / scale) - h) / 2;
        DrawTexture(tex, new Vector2(x, y));
        if (picture.Mark is { } mark)
        {
            var pal = Shapes.DayPalette;
            var colour = Color.Color8(pal[MapMarkColour * 3], pal[MapMarkColour * 3 + 1], pal[MapMarkColour * 3 + 2]);
            DrawRect(new Rect2(x + mark.X, y + mark.Y - 2, 1, 5), colour);
            DrawRect(new Rect2(x + mark.X - 2, y + mark.Y, 5, 1), colour);
        }
    }

    /// <summary>
    /// A book or scroll is read at the largest whole scale that fits the window,
    /// whatever the map zoom, centred like Exult's <c>Gump::set_pos</c>.
    /// </summary>
    void PaintBook(TextGump book)
    {
        var view = GetViewport().GetVisibleRect().Size;
        var fi = Shapes.GetGumpFrame(book.GumpShape, 0);
        if (fi.Width <= 0 || fi.Height <= 0)
        {
            return;
        }

        var scale = Math.Max(1, (int)Math.Min(view.X * 0.95f / fi.Width, view.Y * 0.95f / fi.Height));
        DrawSetTransform(Vector2.Zero, 0, new Vector2(scale, scale));
        book.SetPos((int)(view.X / scale), (int)(view.Y / scale), fi);
        book.Paint(this);
    }

    /// <summary>
    /// The frame over a view of the world elsewhere (the world itself is drawn
    /// round the area's tile): Exult's, sprite 10 painted translucent at the
    /// world's zoom over the original 320x200 screen, black outside it. The
    /// user's pick (2026-10-08) of Exult's frame, the frame redrawn smooth at
    /// the window's size, and the view filling the window.
    /// </summary>
    void PaintRemoteView(U7.Usecode.UsecodePicture picture)
    {
        var view = GetViewport().GetVisibleRect().Size;
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        var w = U7Constants.OriginalScreenW * Zoom;
        var h = U7Constants.OriginalScreenH * Zoom;
        var x0 = Mathf.Floor((view.X - w) / 2);
        var y0 = Mathf.Floor((view.Y - h) / 2);
        if (x0 > 0)
        {
            DrawRect(new Rect2(0, 0, x0, view.Y), Colors.Black);
            DrawRect(new Rect2(x0 + w, 0, view.X - x0 - w, view.Y), Colors.Black);
        }

        if (y0 > 0)
        {
            DrawRect(new Rect2(0, 0, view.X, y0), Colors.Black);
            DrawRect(new Rect2(0, y0 + h, view.X, view.Y - y0 - h), Colors.Black);
        }

        if (Shapes.GetSprite(picture.Sprite, picture.Frame, translucent: true) is { } tex)
        {
            DrawTextureRect(tex, new Rect2(x0, y0, w, h), false);
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
        if (tex is null)
        {
            return;
        }

        var fi = Catalog[shape].GetFrame(frame);
        DrawTexture(tex, new Vector2(hx - fi.XLeft, hy - fi.YAbove));
    }

    /// <summary>Exult <c>has_point</c> for an item shown with its hotspot at <paramref name="hx"/>, <paramref name="hy"/>.</summary>
    public bool WorldSpriteContains(U7Object obj, int mx, int my, int hx, int hy) =>
        Shapes.GetFrame8(obj.Shape, obj.Frame) is { } frame && frame.Covers(mx - hx, my - hy);

    /// <summary>
    /// Exult <c>Font::paint_text</c>: <paramref name="y"/> is the top of the line;
    /// each glyph advances by its width plus the font's lead, and characters
    /// the font has no glyph for are skipped.
    /// </summary>
    public void DrawFontText(VgaFont font, string text, int x, int y)
    {
        y += font.Baseline;
        foreach (var ch in text)
        {
            if (!font.HasGlyph(ch))
            {
                continue;
            }

            if (Shapes.GetFontGlyph(font.Number, ch) is { } tex)
            {
                var fi = Shapes.GetFontFrame(font.Number, ch);
                DrawTexture(tex, new Vector2(x - fi.XLeft, y - fi.YAbove));
            }

            x += font.CharWidth(ch);
        }
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

    /// <summary>Exult <c>Font::paint_text</c>: <paramref name="y"/> is the top of the text, the glyphs stand on the font's baseline.</summary>
    public void DrawFontString(int font, string text, int x, int y)
    {
        var cx = x;
        var baseline = y + VgaFont.Get(font).Baseline;
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
            DrawTexture(tex, new Vector2(cx - fi.XLeft, baseline - fi.YAbove));
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
