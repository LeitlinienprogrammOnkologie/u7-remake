using Godot;

namespace U7.UI;

/// <summary>
/// The endgame's screen: Exult's 320×200 8-bit window during
/// <c>end_game</c> and the credits, a picture of palette indices (the
/// movies' frames, the credits' pictures) shown through a 6-bit palette at
/// Exult's brightness (<c>Get_color8</c>) and fade step
/// (<c>Palette::fade_in</c>/<c>fade_out</c>), scaled to fit the window, black
/// around it. The words are the user's look: MedievalSharp at the window's
/// size where Exult paints its fonts (<see cref="Words"/>), dimmed with the
/// palette's fades and brightness.
/// </summary>
public sealed partial class EndgameView : Control
{
    /// <summary>
    /// Words on the picture: <see cref="Text"/> with its line's top at
    /// <see cref="Y"/> (picture pixels), at <see cref="X"/> its centre
    /// (<see cref="Align"/> 0), right end (-1) or left end (1), the font
    /// <see cref="Size"/> picture pixels high.
    /// </summary>
    public readonly record struct Words(string Text, float X, float Y, float Size, int Align, Color Color, bool Outline);

    public const int ScreenWidth = 320;
    public const int ScreenHeight = 200;

    readonly byte[] _rgba = new byte[ScreenWidth * ScreenHeight * 4];
    readonly byte[] _palette = new byte[768];
    readonly byte[] _fadeFrom = new byte[768];
    readonly List<Words> _painted = new();
    List<Words> _shown = new();
    float _shownLight = 1;
    Image? _image;
    ImageTexture? _texture;
    int _fadeStep = 1;
    int _fadeCycles = 1;

    /// <summary>The picture, palette indices (Exult's window buffer).</summary>
    public byte[] Pixels { get; } = new byte[ScreenWidth * ScreenHeight];

    /// <summary>Exult <c>Palette::set_brightness</c>, percent.</summary>
    public int Brightness { get; set; } = 100;

    float _rise;

    /// <summary>
    /// How far, in picture pixels (0-1), everything shown has glided up
    /// towards the next step of a scroll (the credits rise a pixel a step in
    /// Exult; here they glide between the steps).
    /// </summary>
    public float Rise
    {
        get => _rise;
        set
        {
            if (value != _rise)
            {
                _rise = value;
                QueueRedraw();
            }
        }
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Nearest;
    }

    /// <summary>
    /// Exult's <c>pal1</c> and <c>pal2</c>: 256 colours, 6 bits each, and
    /// what fades start from and end at (black unless a double palette gives
    /// another).
    /// </summary>
    public void SetPalette(ReadOnlySpan<byte> palette6, ReadOnlySpan<byte> fadeFrom = default)
    {
        palette6[..Math.Min(768, palette6.Length)].CopyTo(_palette);
        Array.Clear(_fadeFrom);
        fadeFrom[..Math.Min(768, fadeFrom.Length)].CopyTo(_fadeFrom);
    }

    /// <summary>A fade's step: <paramref name="step"/> of <paramref name="cycles"/> from the fade's start to the palette.</summary>
    public void SetFade(int step, int cycles)
    {
        _fadeStep = step;
        _fadeCycles = Math.Max(1, cycles);
    }

    /// <summary>Exult <c>fill8(0)</c>: the picture black, its words gone.</summary>
    public void Clear()
    {
        Array.Clear(Pixels);
        _painted.Clear();
    }

    /// <summary>Exult <c>win->put</c> of a movie frame: the picture replaced, words and all.</summary>
    public void PutPicture(ReadOnlySpan<byte> pixels)
    {
        pixels[..Math.Min(pixels.Length, Pixels.Length)].CopyTo(Pixels);
        _painted.Clear();
    }

    /// <summary>Exult <c>Font::draw_text</c>: words on the picture (the same words twice are painted once).</summary>
    public void Paint(Words words)
    {
        if (!_painted.Contains(words))
        {
            _painted.Add(words);
        }
    }

    /// <summary>Exult <c>paint_shape</c>: a frame with its hot spot at (x, y).</summary>
    public void PaintFrame(U7.Data.ShapeFrame frame, int x, int y)
    {
        foreach (var scan in frame.Scans)
        {
            var py = y + scan.Y;
            if ((uint)py >= ScreenHeight)
            {
                continue;
            }

            for (var i = 0; i < scan.Length; i++)
            {
                var px = x + scan.X + i;
                if ((uint)px < ScreenWidth)
                {
                    Pixels[py * ScreenWidth + px] = frame.Pixels[scan.Offset + i];
                }
            }
        }
    }

    /// <summary>Exult <c>Image_window::show</c>: what is painted is seen.</summary>
    public void Present()
    {
        for (var i = 0; i < Pixels.Length; i++)
        {
            var c = Pixels[i] * 3;
            _rgba[i * 4] = Color8(c);
            _rgba[i * 4 + 1] = Color8(c + 1);
            _rgba[i * 4 + 2] = Color8(c + 2);
            _rgba[i * 4 + 3] = 255;
        }

        _shown = new List<Words>(_painted);
        _shownLight = (float)_fadeStep / _fadeCycles * Brightness / 100f;
        if (_image is null)
        {
            _image = Image.CreateFromData(ScreenWidth, ScreenHeight, false, Image.Format.Rgba8, _rgba);
            _texture = ImageTexture.CreateFromImage(_image);
        }
        else
        {
            _image.SetData(ScreenWidth, ScreenHeight, false, Image.Format.Rgba8, _rgba);
            _texture!.Update(_image);
        }

        QueueRedraw();
    }

    /// <summary>Exult's fade (<c>(pal1 - pal2) · i / cycles + pal2</c>) and <c>Get_color8</c> (v·brightness·255 / (100·63)).</summary>
    byte Color8(int at)
    {
        var faded = (_palette[at] - _fadeFrom[at]) * _fadeStep / _fadeCycles + _fadeFrom[at];
        return (byte)Math.Clamp(faded * Brightness * 255 / (100 * 63), 0, 255);
    }

    public override void _Draw()
    {
        var view = GetViewportRect().Size;
        DrawRect(new Rect2(Vector2.Zero, view), Colors.Black);
        if (_texture is null)
        {
            return;
        }

        var scale = Math.Min(view.X / ScreenWidth, view.Y / ScreenHeight);
        var size = new Vector2(ScreenWidth, ScreenHeight) * scale;
        var origin = (view - size) / 2 - new Vector2(0, Rise * scale);
        DrawTextureRect(_texture, new Rect2(origin, size), false);
        var font = UiTheme.Font;
        foreach (var w in _shown)
        {
            var px = Math.Max(1, Mathf.RoundToInt(w.Size * scale));
            var width = font.GetStringSize(w.Text, HorizontalAlignment.Left, -1, px).X;
            var x = origin.X + w.X * scale - (w.Align == 0 ? width / 2 : w.Align < 0 ? width : 0);
            var pos = new Vector2(x, origin.Y + w.Y * scale + font.GetAscent(px));
            var color = new Color(w.Color.R * _shownLight, w.Color.G * _shownLight, w.Color.B * _shownLight);
            if (w.Outline)
            {
                DrawStringOutline(font, pos, w.Text, HorizontalAlignment.Left, -1, px, Math.Max(2, px / 6),
                    new Color(UiTheme.Ink, Math.Min(1, _shownLight * 1.5f)));
            }

            DrawString(font, pos, w.Text, HorizontalAlignment.Left, -1, px, color);
        }
    }
}
