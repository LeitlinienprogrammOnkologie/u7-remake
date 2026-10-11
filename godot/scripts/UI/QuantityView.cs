using Godot;

namespace U7.UI;

/// <summary>
/// Exult's <c>Slider_gump</c> as <c>Gump_manager::prompt_for_number</c>
/// shows it when part of a stack is dropped: how many, from 0 to all (all at
/// first), by one; Left and Right, the wheel (with Alt by ten), - and + move
/// it, Enter or OK takes the number, Esc or Cancel none (Exult's 0: the
/// stack goes back). The user's look (2026-10-10, B): a small panel in the
/// UI's dark wood and gold, "How many?", the count in words, a slider, OK and
/// Cancel, laid out on 1280×720 scaled to fit.
/// </summary>
public sealed partial class QuantityView : Control
{
    const float RefWidth = 1280, RefHeight = 720;

    /// <summary>The number chosen, 0 for none.</summary>
    public Action<int>? Done;

    readonly int _max;
    readonly string _what;
    readonly HSlider _slider = new() { MinValue = 0, Step = 1, FocusMode = FocusModeEnum.None };
    readonly Label _title = new() { Text = "How many?", HorizontalAlignment = HorizontalAlignment.Center };
    readonly Label _count = new() { HorizontalAlignment = HorizontalAlignment.Center };
    readonly Button _less = new() { Text = "-" };
    readonly Button _more = new() { Text = "+" };
    readonly Button _ok = new() { Text = "OK" };
    readonly Button _cancel = new() { Text = "Cancel" };
    readonly Panel _panel = new();

    /// <param name="max">The stack's quantity, also the number offered first.</param>
    /// <param name="what">The stack as a click names it ("100 gold coins").</param>
    public QuantityView(int max, string what)
    {
        _max = max;
        _what = what;
    }

    public int Value => (int)_slider.Value;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        _panel.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Wood with { A = 0.97f }, UiTheme.Gold, 3, 14, 0));
        AddChild(_panel);
        _title.AddThemeColorOverride("font_color", UiTheme.Gold);
        _count.AddThemeColorOverride("font_color", UiTheme.GoldBright);
        AddChild(_title);
        AddChild(_count);

        _slider.MaxValue = _max;
        _slider.Value = _max;
        _slider.ValueChanged += _ => ShowCount();
        AddChild(_slider);

        foreach (var (button, action) in new (Button, Action)[]
                 {
                     (_less, () => Move(-1)), (_more, () => Move(1)), (_ok, () => Finish(Value)), (_cancel, () => Finish(0))
                 })
        {
            UiTheme.StyleButton(button, UiTheme.PanelLook);
            button.Pressed += action;
            AddChild(button);
        }

        _ok.AddThemeStyleboxOverride("normal", UiTheme.Box(new Color(0.35f, 0.26f, 0.12f), UiTheme.GoldBright, 2, 6, 0));
        _ok.AddThemeColorOverride("font_color", UiTheme.GoldBright);
        ShowCount();
        Resized += Layout;
        Layout();
    }

    void ShowCount() => _count.Text = $"{Value} of {_what}";

    /// <summary>Exult <c>move_diamond</c>: by steps, kept between 0 and all.</summary>
    public void Move(int steps) => _slider.Value = Math.Clamp(Value + steps, 0, _max);

    /// <summary>Exult: Enter (or the checkmark) takes the number; Esc closes the gump, which answers 0.</summary>
    public void Finish(int value)
    {
        if (Done is { } done)
        {
            Done = null;
            done(Math.Clamp(value, 0, _max));
        }
    }

    float ViewScale => Math.Min(Size.X / RefWidth, Size.Y / RefHeight);

    Rect2 At(float x, float y, float w, float h)
    {
        var origin = (Size - new Vector2(RefWidth, RefHeight) * ViewScale) / 2;
        return new Rect2(origin + new Vector2(x, y) * ViewScale, new Vector2(w, h) * ViewScale);
    }

    void Layout()
    {
        Place(_panel, At(440, 270, 400, 200));
        Place(_title, At(440, 284, 400, 40));
        Place(_count, At(440, 330, 400, 36));
        Place(_less, At(462, 373, 36, 34));
        Place(_slider, At(510, 378, 260, 24));
        Place(_more, At(782, 373, 36, 34));
        Place(_ok, At(520, 420, 110, 38));
        Place(_cancel, At(650, 420, 110, 38));
        var sizes = new (Control Control, float Size)[] { (_title, 26), (_count, 24), (_less, 24), (_more, 24), (_ok, 20), (_cancel, 20) };
        foreach (var (control, size) in sizes)
        {
            control.AddThemeFontOverride("font", UiTheme.Font);
            control.AddThemeFontSizeOverride("font_size", Math.Max(8, Mathf.RoundToInt(size * ViewScale)));
        }

        StyleSlider();
    }

    /// <summary>The slider in gold: a thin track, the part up to the knob brighter, the knob a gold disc with a dark rim, all at the window's size.</summary>
    void StyleSlider()
    {
        var half = Math.Max(2, Mathf.RoundToInt(4 * ViewScale));
        StyleBoxFlat Track(Color colour)
        {
            var box = UiTheme.Box(colour, colour, 0, half, 0);
            box.ContentMarginTop = box.ContentMarginBottom = half;
            return box;
        }

        _slider.AddThemeStyleboxOverride("slider", Track(UiTheme.Gold with { A = 0.25f }));
        _slider.AddThemeStyleboxOverride("grabber_area", Track(UiTheme.Gold with { A = 0.8f }));
        _slider.AddThemeStyleboxOverride("grabber_area_highlight", Track(UiTheme.GoldBright));
        var size = Math.Max(10, Mathf.RoundToInt(22 * ViewScale));
        _slider.AddThemeIconOverride("grabber", Knob(size, UiTheme.GoldBright));
        _slider.AddThemeIconOverride("grabber_highlight", Knob(size, Colors.White.Lerp(UiTheme.GoldBright, 0.5f)));
    }

    static ImageTexture Knob(int size, Color fill)
    {
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        var centre = (size - 1) / 2f;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var d = new Vector2(x - centre, y - centre).Length();
                var colour = d > size / 2f ? Colors.Transparent : d > size / 2f - Math.Max(2, size / 10f) ? UiTheme.WoodDark with { A = 1 } : fill;
                image.SetPixel(x, y, colour);
            }
        }

        return ImageTexture.CreateFromImage(image);
    }

    static void Place(Control control, Rect2 rect)
    {
        control.Position = rect.Position;
        control.Size = rect.Size;
    }

    public override void _Input(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventKey { Pressed: true } key:
                switch (key.Keycode)
                {
                    case Key.Enter or Key.KpEnter when !key.Echo:
                        Finish(Value);
                        break;
                    case Key.Escape when !key.Echo:
                        Finish(0);
                        break;
                    case Key.Left:
                        Move(-1);
                        break;
                    case Key.Right:
                        Move(1);
                        break;
                    default:
                        return;
                }

                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel:
                // Exult mousewheel_up/down: up lowers, down raises; with Alt by ten.
                Move((wheel.ButtonIndex == MouseButton.WheelUp ? -1 : 1) * (wheel.AltPressed ? 10 : 1));
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    public override void _Draw() => DrawRect(new Rect2(Vector2.Zero, Size), new Color(0, 0, 0, 0.35f));
}
