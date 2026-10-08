using System.IO;
using Godot;
using U7.Core;
using U7.Data;

namespace U7.UI;

/// <summary>Exult <c>Mouse::Mouse_shapes</c>: the frames of POINTERS.SHP.</summary>
public static class MouseShape
{
    public const int Hand = 0;
    public const int RedX = 1;
    /// <summary>Exult <c>greenselect</c>: the crosshair for picking a target.</summary>
    public const int GreenSelect = 2;
    public const int TooHeavy = 3;
    public const int OutOfRange = 4;
    public const int OutOfAmmo = 5;
    public const int WontFit = 6;
    public const int Hourglass = 7;
    /// <summary>Arrows by direction (0 north, clockwise): short, medium and long, and the red combat ones.</summary>
    public const int ShortArrows = 8;
    public const int MediumArrows = 16;
    public const int LongArrows = 24;
    public const int ShortCombatArrows = 32;
    public const int MediumCombatArrows = 40;
    public const int Blocked = 49;

    /// <summary>The frames that are words, shown as text in the user's look.</summary>
    public static string? Message(int shape) =>
        shape switch
        {
            TooHeavy => "Too heavy",
            OutOfRange => "Out of range",
            OutOfAmmo => "Out of ammo",
            WontFit => "Won't fit",
            Blocked => "Blocked",
            _ => null
        };

    public static string Name(int shape) =>
        Message(shape)?.ToLowerInvariant() ?? shape switch
        {
            Hand => "hand",
            RedX => "red x",
            GreenSelect => "crosshair",
            Hourglass => "hourglass",
            >= ShortArrows and < MediumArrows => $"short arrow {shape - ShortArrows}",
            >= MediumArrows and < LongArrows => $"medium arrow {shape - MediumArrows}",
            >= LongArrows and < ShortCombatArrows => $"long arrow {shape - LongArrows}",
            >= ShortCombatArrows and < MediumCombatArrows => $"short combat arrow {shape - ShortCombatArrows}",
            >= MediumCombatArrows and < Blocked => $"medium combat arrow {shape - MediumCombatArrows}",
            _ => $"frame {shape}"
        };
}

/// <summary>
/// Exult <c>Mouse</c>: the cursor, a frame of POINTERS.SHP chosen by the
/// game (<see cref="Shape"/>), or one flashed for 600 ms while the whole game
/// holds (Exult <c>flash_shape</c>'s <c>SDL_Delay</c>; <see cref="Holding"/>).
/// The user's look: Exult's frames at the world's zoom, crisp, in the day
/// palette, as the system cursor; the flashed words ("Too heavy",
/// "Blocked", ...) in MedievalSharp, gold on dark wood, centred on the
/// cursor (sized as the barks are).
/// </summary>
public sealed partial class MouseCursor : Control
{
    public const double FlashSeconds = 0.6;
    /// <summary>The largest cursor image the system takes.</summary>
    const int MaxCursorSize = 256;

    readonly VgaShapeFile _pointers = VgaShapeFile.SingleShape(Path.Combine(U7Paths.StaticDir, "POINTERS.SHP"));
    readonly byte[] _palette = U7Palette.DayRgb();
    readonly Dictionary<(int Shape, float Zoom), (Image Image, Vector2 Hotspot)> _images = new();
    Image? _hidden;
    int _shown = -1;
    float _shownZoom;
    int? _flash;
    double _flashLeft;

    /// <summary>The shape the game wants (Exult starts with the short east arrow).</summary>
    public int Shape { get; set; } = MouseShape.ShortArrows + 2;

    /// <summary>The world's zoom, which the cursor is drawn at.</summary>
    public float Zoom { get; set; } = 1;

    /// <summary>No cursor (the endgame, painted without Exult's mouse).</summary>
    public bool HideCursor { get; set; }

    /// <summary>A flash is showing and the game holds.</summary>
    public bool Holding => _flashLeft > 0;

    /// <summary>A flash began (for the agent console's log).</summary>
    public event Action<int>? Flashed;

    /// <summary>Exult <c>Mouse::flash_shape</c>: show the shape for 600 ms while the game holds.</summary>
    public void Flash(int shape)
    {
        _flash = shape;
        _flashLeft = FlashSeconds;
        Flashed?.Invoke(shape);
        Apply();
    }

    /// <summary>Count the flash down; true when it ended this time.</summary>
    public bool Tick(double delta)
    {
        if (_flashLeft <= 0)
        {
            return false;
        }

        _flashLeft -= delta;
        if (_flashLeft > 0)
        {
            return false;
        }

        _flash = null;
        return true;
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta)
    {
        Apply();
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_flash is not { } flash || MouseShape.Message(flash) is not { } text)
        {
            return;
        }

        var size = Mathf.Clamp(Mathf.RoundToInt(6.5f * Zoom), 14, 40);
        var font = UiTheme.Font;
        var textSize = font.GetStringSize(text, HorizontalAlignment.Left, -1, size);
        var pad = new Vector2(size * 0.5f, size * 0.3f);
        var box = new Rect2(GetLocalMousePosition() - textSize / 2 - pad, textSize + pad * 2);
        // Kept on the screen, as the barks are.
        var view = GetViewportRect().Size;
        box.Position = new Vector2(Mathf.Clamp(box.Position.X, 0, Math.Max(0, view.X - box.Size.X)),
            Mathf.Clamp(box.Position.Y, 0, Math.Max(0, view.Y - box.Size.Y)));
        DrawStyleBox(UiTheme.Box(UiTheme.Wood, UiTheme.Gold, 2, Mathf.RoundToInt(size * 0.3f), 0), box);
        var baseline = box.Position.Y + pad.Y + font.GetAscent(size);
        DrawString(font, new Vector2(box.Position.X + pad.X, baseline), text, HorizontalAlignment.Left, -1, size,
            UiTheme.GoldBright);
    }

    /// <summary>Set the system cursor to the frame showing, at the zoom; none while words show.</summary>
    void Apply()
    {
        var shape = HideCursor ? -1 : _flash ?? Shape;
        if (shape == _shown && Zoom == _shownZoom)
        {
            return;
        }

        _shown = shape;
        _shownZoom = Zoom;
        if (shape < 0 || MouseShape.Message(shape) is not null || Image(shape, Zoom) is not { } cursor)
        {
            _hidden ??= Godot.Image.CreateEmpty(1, 1, false, Godot.Image.Format.Rgba8);
            Input.SetCustomMouseCursor(_hidden, Input.CursorShape.Arrow, Vector2.Zero);
            Input.SetCustomMouseCursor(_hidden, Input.CursorShape.PointingHand, Vector2.Zero);
            return;
        }

        Input.SetCustomMouseCursor(cursor.Image, Input.CursorShape.Arrow, cursor.Hotspot);
        Input.SetCustomMouseCursor(cursor.Image, Input.CursorShape.PointingHand, cursor.Hotspot);
    }

    /// <summary>The frame scaled to the zoom with whole pixels, its hot spot where Exult's is.</summary>
    (Image Image, Vector2 Hotspot)? Image(int shape, float zoom)
    {
        if (_images.TryGetValue((shape, zoom), out var cached))
        {
            return cached;
        }

        var info = _pointers.Get(0, shape);
        if (_pointers.Decode(0, shape, _palette) is not { } image || info.Width <= 0 || info.Height <= 0)
        {
            return null;
        }

        var scale = Math.Min(zoom, (float)MaxCursorSize / Math.Max(info.Width, info.Height));
        var w = Math.Max(1, Mathf.RoundToInt(info.Width * scale));
        var h = Math.Max(1, Mathf.RoundToInt(info.Height * scale));
        image.Resize(w, h, Godot.Image.Interpolation.Nearest);
        var hotspot = new Vector2(Math.Min(w - 1, Mathf.RoundToInt(info.XLeft * scale)),
            Math.Min(h - 1, Mathf.RoundToInt(info.YAbove * scale)));
        _images[(shape, zoom)] = (image, hotspot);
        return (image, hotspot);
    }
}
