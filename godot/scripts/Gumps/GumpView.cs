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
    /// <summary>Stacks for drops: Exult's <c>drop</c> and <c>add(..., combine)</c>.</summary>
    public U7.Actors.ItemQuantity Quantities = null!;
    public float Zoom = 4f;
    /// <summary>The book or scroll whose page the usecode waits on; Exult paints it over everything.</summary>
    public Func<TextGump?>? ShownBook;
    /// <summary>The sign the usecode waits on (<c>display_runes</c>); Exult paints it over everything.</summary>
    public Func<SignGump?>? ShownSign;
    /// <summary>The picture the usecode waits on (the map, a view elsewhere); Exult paints it over everything.</summary>
    public Func<U7.Usecode.UsecodePicture?>? ShownPicture;
    /// <summary>Exult <c>Paint_map</c>'s mark colour, a palette index.</summary>
    const int MapMarkColour = 50;
    /// <summary>The world view, for the palette and modern colour's tables the gumps' shader reads (R8).</summary>
    public U7.Rendering.WorldView World = null!;

    /// <summary>
    /// The gumps' palette services (docs/RENDERER.md 5.9, R8): each gump
    /// texture carries its frame's index plane as its normal map
    /// (<see cref="U7.Rendering.ShapeCache"/>), and this shader shows an
    /// original pixel by the current palette (row 0, cycling and all, when
    /// <see cref="U7.Rendering.ArtLook.GumpPalette"/>; row 1, the day
    /// palette, otherwise), a see-through pixel of a translucent shape by
    /// what is on the screen under it turned through its table (modern
    /// colour's table for the table, else the blend colour as before), and
    /// a hi-res pixel by its own colour through modern colour's ambient
    /// table, its kept cycling pixels by the palette. Fonts and painted
    /// faces, which carry no index plane, are plain colours.
    /// </summary>
    const string GumpCode = """
        shader_type canvas_item;

        uniform sampler2D palette_tex : filter_nearest;
        uniform sampler3D lut_ambient : filter_linear, repeat_disable;
        uniform sampler3D xform_lut : filter_linear, repeat_disable;
        uniform float lut_scale = 0.96875;
        uniform float lut_offset = 0.015625;
        uniform int lut_size = 32;
        uniform bool use_palette = true;
        uniform bool use_lut = false;
        uniform sampler2D screen_tex : hint_screen_texture, filter_nearest;

        varying vec4 vcol;

        void vertex() {
            vcol = COLOR;
        }

        // A colour seen through translucency table t by its lookup table (ColourLut.XformTexture).
        vec3 through_table(int t, vec3 c) {
            float n = float(lut_size);
            vec3 u = vec3(c.rg * lut_scale + lut_offset, (float(t) * n + c.b * (n - 1.0) + 0.5) / (17.0 * n));
            return texture(xform_lut, u).rgb;
        }

        void fragment() {
            vec4 tex = texture(TEXTURE, UV);
            ivec2 nsize = textureSize(NORMAL_TEXTURE, 0);
            vec4 idx = texelFetch(NORMAL_TEXTURE, clamp(ivec2(UV * vec2(nsize)), ivec2(0), nsize - ivec2(1)), 0);
            int marker = int(idx.g * 255.0 + 0.5);
            int index = int(idx.r * 255.0 + 0.5);
            int row = use_palette ? 0 : 1;
            vec3 rgb = tex.rgb;
            float a = tex.a;
            if (marker == 255 || (marker == 253 && index >= 0xE0)) {
                rgb = texelFetch(palette_tex, ivec2(index, row), 0).rgb;
            } else if (marker == 254) {
                vec3 under = texture(screen_tex, SCREEN_UV).rgb;
                rgb = use_lut ? through_table(index - 0xEE, under) : mix(under, tex.rgb, tex.a);
                a = 1.0;
            } else if (use_palette && use_lut) {
                rgb = texture(lut_ambient, rgb * lut_scale + lut_offset).rgb;
            }
            COLOR = vec4(rgb, a) * vcol;
        }
        """;

    static readonly StringName PaletteTex = "palette_tex";
    static readonly StringName LutAmbient = "lut_ambient";
    static readonly StringName XformLut = "xform_lut";
    static readonly StringName LutScale = "lut_scale";
    static readonly StringName LutOffset = "lut_offset";
    static readonly StringName LutSize = "lut_size";
    static readonly StringName UsePalette = "use_palette";
    static readonly StringName UseLut = "use_lut";

    /// <summary>What a pass draws: everything, the opaque art and items, or the see-through items alone (over a fresh copy of the screen).</summary>
    enum Pass
    {
        All,
        Opaque,
        SeeThrough
    }

    /// <summary>One draw pass of a gump (RENDERER.md 5.9): a child drawn after its parent, so a see-through pass sees the opaque pass on the screen.</summary>
    public partial class GumpPass : Node2D
    {
        public Action<CanvasItem>? OnDraw;

        public override void _Draw() => OnDraw?.Invoke(this);
    }

    ShaderMaterial _material = null!;
    ImageTexture3D? _xformLut;
    int _xformLutSize;
    readonly List<GumpPass> _passes = [];
    /// <summary>The pass being drawn: where the Draw calls go, and what they may draw.</summary>
    CanvasItem _target = null!;
    Pass _pass = Pass.All;

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.Nearest;
        ZIndex = 0;
        _material = new ShaderMaterial { Shader = new Shader { Code = GumpCode } };
        _target = this;
    }

    /// <summary>Whether the pass draws an item that is see-through (<paramref name="translucent"/>) or not.</summary>
    bool Draws(bool translucent) => _pass == Pass.All || (_pass == Pass.SeeThrough) == translucent;

    /// <summary>
    /// The passes for the open gumps (two each, opaque then see-through) and
    /// one for the drag, the book, the picture and the sign, made or dropped
    /// as gumps open and close, and the shader's palette and tables for this frame.
    /// </summary>
    void SyncPasses()
    {
        var open = Gumps.Open.ToList();
        var wanted = open.Count * 2 + 1;
        while (_passes.Count < wanted)
        {
            var pass = new GumpPass { Name = $"Pass{_passes.Count}", Material = _material };
            _passes.Add(pass);
            AddChild(pass);
        }

        while (_passes.Count > wanted)
        {
            var last = _passes[^1];
            _passes.RemoveAt(_passes.Count - 1);
            last.QueueFree();
        }

        for (var i = 0; i < open.Count; i++)
        {
            var gump = open[i];
            _passes[i * 2].OnDraw = target => PaintGump(target, gump, Pass.Opaque);
            _passes[i * 2 + 1].OnDraw = target => PaintGump(target, gump, Pass.SeeThrough);
        }

        _passes[^1].OnDraw = PaintTop;
        foreach (var pass in _passes)
        {
            pass.QueueRedraw();
        }

        _material.SetShaderParameter(PaletteTex, World.PaletteTexture);
        _material.SetShaderParameter(UsePalette, U7.Rendering.ArtLook.GumpPalette);
        var modern = U7.Rendering.ArtLook.Modern;
        _material.SetShaderParameter(UseLut, modern);
        if (modern)
        {
            var lut = World.Lut();
            _material.SetShaderParameter(LutAmbient, lut.AmbientTexture);
            _material.SetShaderParameter(LutScale, (lut.Size - 1f) / lut.Size);
            _material.SetShaderParameter(LutOffset, 0.5f / lut.Size);
            if (_xformLut is null || _xformLutSize != lut.Size)
            {
                _xformLutSize = lut.Size;
                _xformLut = U7.Rendering.ColourLut.XformTexture(Shapes.Xforms, lut.Size);
                _material.SetShaderParameter(XformLut, _xformLut);
                _material.SetShaderParameter(LutSize, lut.Size);
            }
        }
    }

    void PaintGump(CanvasItem target, Gump gump, Pass pass)
    {
        _target = target;
        _pass = pass;
        _target.DrawSetTransform(Vector2.Zero, 0, new Vector2(Zoom, Zoom));
        gump.Paint(this);
        _target = this;
        _pass = Pass.All;
    }

    /// <summary>The drag, the book, the picture and the sign, over every gump.</summary>
    void PaintTop(CanvasItem target)
    {
        _target = target;
        _pass = Pass.All;
        _target.DrawSetTransform(Vector2.Zero, 0, new Vector2(Zoom, Zoom));
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

        if (ShownSign?.Invoke() is { } sign)
        {
            PaintSign(sign);
        }

        _target = this;
    }

    public override void _Process(double delta)
    {
        var view = GetViewport().GetVisibleRect().Size;
        Gumps.ScreenW = Math.Max(1, Mathf.RoundToInt(view.X / Zoom));
        Gumps.ScreenH = Math.Max(1, Mathf.RoundToInt(view.Y / Zoom));
        SyncPasses();
        QueueRedraw();
    }

    public Vector2I MouseVirtual()
    {
        var m = GetViewport().GetMousePosition();
        return new Vector2I(Mathf.FloorToInt(m.X / Zoom), Mathf.FloorToInt(m.Y / Zoom));
    }

    /// <summary>
    /// Exult <c>Text_effect::Figure_text_pos</c> in a gump: the texts over
    /// things shown in the open gumps (in the first gump showing their
    /// container), at the top centre of their sprites, in screen pixels.
    /// </summary>
    public IEnumerable<(string Text, Vector2 Top)> Texts()
    {
        var now = Time.GetTicksMsec();
        var seen = new HashSet<U7Object>();
        foreach (var gump in Gumps.Open)
        {
            if (gump.Owner is not { } owner || !gump.ShowsContents || !seen.Add(owner))
            {
                continue;
            }

            foreach (var obj in owner.Contents)
            {
                if (obj.Removed || obj.BarkText.Length == 0 || obj.BarkUntilMsec < now || obj == Gumps.Drag?.Object)
                {
                    continue;
                }

                gump.GetShapeLocation(obj, out var ox, out var oy);
                var top = WorldView.SpriteTop(Catalog[obj.Shape].GetFrame(obj.Frame), ox, oy);
                yield return (WorldView.ShownText(obj.BarkText), top * Zoom);
            }
        }
    }

    public override void _Draw()
    {
        // The passes (children) draw the gumps; see SyncPasses.
    }

    /// <summary>
    /// A sign is read like a book, at the largest whole scale that fits the
    /// window, centred (Exult <c>Gump::set_pos</c>). Runes are translated
    /// beneath it in the UI font, gold on dark wood: the user's pick
    /// (2026-10-08) of runes alone, Exult's letters on the sign, and this.
    /// The sign then keeps 72% of the height and moves up by 8% of it.
    /// </summary>
    void PaintSign(SignGump sign)
    {
        var view = GetViewport().GetVisibleRect().Size;
        var fi = Shapes.GetGumpFrame(sign.GumpShape, 0);
        if (fi.Width <= 0 || fi.Height <= 0)
        {
            return;
        }

        var translation = sign.Translation;
        var scale = Math.Max(1, (int)Math.Min(view.X * 0.95f / fi.Width, view.Y * (translation is null ? 0.95f : 0.72f) / fi.Height));
        _target.DrawSetTransform(Vector2.Zero, 0, new Vector2(scale, scale));
        var x = ((int)(view.X / scale) - fi.Width) / 2 + fi.XLeft;
        var y = ((int)(view.Y / scale) - fi.Height) / 2 + fi.YAbove - (translation is null ? 0 : (int)(view.Y * 0.08f / scale));
        sign.Paint(this, x, y, fi);
        if (translation is not null)
        {
            _target.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            PaintTranslation(translation, (y - fi.YAbove + fi.Height) * scale + view.Y * 0.03f, view);
        }
    }

    /// <summary>A sign's text in letters, in a dark wood box 60% of the window wide, below <paramref name="top"/>.</summary>
    void PaintTranslation(string text, float top, Vector2 view)
    {
        var size = Math.Max(18, (int)(view.Y * 0.035f));
        var font = U7.UI.UiTheme.Font;
        var width = view.X * 0.6f;
        var lines = Math.Max(1, (int)Math.Ceiling(font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X / width));
        _target.DrawRect(new Rect2(view.X * 0.2f, top, width, lines * size * 1.25f + size * 0.6f), U7.UI.UiTheme.Wood);
        _target.DrawMultilineString(font, new Vector2(view.X * 0.2f, top + size * 1.05f), text, HorizontalAlignment.Center, width, size,
            -1, U7.UI.UiTheme.Gold);
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
        _target.DrawSetTransform(Vector2.Zero, 0, new Vector2(scale, scale));
        var x = ((int)(view.X / scale) - w) / 2;
        var y = ((int)(view.Y / scale) - h) / 2;
        _target.DrawTexture(tex, new Vector2(x, y));
        if (picture.Mark is { } tile)
        {
            var mark = U7.Usecode.UsecodePicture.MapSpritePixel(tile);
            var pal = Shapes.DayPalette;
            var colour = Color.Color8(pal[MapMarkColour * 3], pal[MapMarkColour * 3 + 1], pal[MapMarkColour * 3 + 2]);
            _target.DrawRect(new Rect2(x + mark.X, y + mark.Y - 2, 1, 5), colour);
            _target.DrawRect(new Rect2(x + mark.X - 2, y + mark.Y, 5, 1), colour);
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
        _target.DrawSetTransform(Vector2.Zero, 0, new Vector2(scale, scale));
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
        _target.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        var w = U7Constants.OriginalScreenW * Zoom;
        var h = U7Constants.OriginalScreenH * Zoom;
        var x0 = Mathf.Floor((view.X - w) / 2);
        var y0 = Mathf.Floor((view.Y - h) / 2);
        if (x0 > 0)
        {
            _target.DrawRect(new Rect2(0, 0, x0, view.Y), Colors.Black);
            _target.DrawRect(new Rect2(x0 + w, 0, view.X - x0 - w, view.Y), Colors.Black);
        }

        if (y0 > 0)
        {
            _target.DrawRect(new Rect2(0, 0, view.X, y0), Colors.Black);
            _target.DrawRect(new Rect2(0, y0 + h, view.X, view.Y - y0 - h), Colors.Black);
        }

        if (Shapes.GetSprite(picture.Sprite, picture.Frame, translucent: true) is { } tex)
        {
            _target.DrawTextureRect(tex, new Rect2(x0, y0, w, h), false);
        }
    }

    public void DrawGumpShape(int shape, int frame, int hx, int hy)
    {
        if (!Draws(false))
        {
            return;
        }

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
        _target.DrawTexture(tex, new Vector2(hx - fi.XLeft, hy - fi.YAbove));
    }

    /// <summary>A PAPERDOL.VGA frame with its hotspot at <paramref name="hx"/>, <paramref name="hy"/> (Exult <c>SF_PAPERDOL_VGA</c>).</summary>
    public void DrawPaperdollShape(int shape, int frame, int hx, int hy, bool translucent = false)
    {
        if (!Draws(translucent) || Shapes.GetPaperdoll(shape, frame, translucent) is not { } tex ||
            Shapes.GetPaperdoll8(shape, frame) is not { } fi)
        {
            return;
        }

        _target.DrawTexture(tex, new Vector2(hx - fi.XLeft, hy - fi.YAbove));
    }

    public void DrawWorldShape(int shape, int frame, int hx, int hy)
    {
        if (!Draws(Catalog[shape].Translucent))
        {
            return;
        }

        var tex = Shapes.Get(shape, frame);
        if (tex is null)
        {
            return;
        }

        var fi = Catalog[shape].GetFrame(frame);
        _target.DrawTexture(tex, new Vector2(hx - fi.XLeft, hy - fi.YAbove));
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
        if (!Draws(false))
        {
            return;
        }

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
                _target.DrawTexture(tex, new Vector2(x - fi.XLeft, y - fi.YAbove));
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
        if (!Draws(false))
        {
            return;
        }

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
            _target.DrawTexture(tex, new Vector2(cx - fi.XLeft, baseline - fi.YAbove));
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
