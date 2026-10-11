using Godot;

namespace U7.UI;

/// <summary>What the main menu offers (Exult <c>Game::show_menu</c>'s entries).</summary>
public enum MenuChoice
{
    Journey,
    NewGame,
    Intro,
    Credits,
    Quotes,
    Endgame,
    Quit
}

/// <summary>
/// The main menu, Exult <c>Game::show_menu</c>: Journey Onward (chosen at
/// first), Start New Game, View Introduction, View Credits, then View Quotes
/// once the credits were watched to their end and View Endgame once the game
/// was won, and Quit in place of Exult's Return to Menu. Up and Down choose
/// (round the ends), Enter takes it, the mouse chooses by pointing; Esc does
/// nothing, Alt+Q or Alt+X quits (Exult <c>MenuList::handle_events</c>). The user's look
/// (2026-10-10, B): MAINSHP.FLX's title with the new game screen's dark
/// wood and gold panel of MedievalSharp buttons, the chosen one lit.
/// </summary>
public sealed partial class MainMenuView : Control
{
    const float PictureWidth = 320, PictureHeight = 200;

    public Action<MenuChoice>? Chosen;

    readonly List<(MenuChoice Choice, Button Button)> _entries = [];
    readonly Panel _panel = new();
    Texture2D? _title;
    int _selection;
    /// <summary>Exult <c>journey_failed_text</c>, shown until a key or click, or null.</summary>
    string[]? _message;

    public MainMenuView(bool quotes, bool endgame)
    {
        var choices = new List<MenuChoice> { MenuChoice.Journey, MenuChoice.NewGame, MenuChoice.Intro, MenuChoice.Credits };
        if (quotes)
        {
            choices.Add(MenuChoice.Quotes);
        }

        if (endgame)
        {
            choices.Add(MenuChoice.Endgame);
        }

        choices.Add(MenuChoice.Quit);
        foreach (var choice in choices)
        {
            _entries.Add((choice, new Button { Text = Words(choice) }));
        }
    }

    static string Words(MenuChoice choice) => choice switch
    {
        MenuChoice.Journey => "Journey Onward",
        MenuChoice.NewGame => "Start New Game",
        MenuChoice.Intro => "View Introduction",
        MenuChoice.Credits => "View Credits",
        MenuChoice.Quotes => "View Quotes",
        MenuChoice.Endgame => "View Endgame",
        _ => "Quit"
    };

    /// <summary>The entries offered and the one chosen, for the console.</summary>
    public string Describe() =>
        _message is { } message
            ? string.Join(" ", message)
            : string.Join(" | ", _entries.Select((e, i) => (i == _selection ? "*" : "") + e.Button.Text));

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        TextureFilter = TextureFilterEnum.Nearest;
        _title = NewGameView.LoadTitle();
        _panel.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Wood with { A = 0.97f }, UiTheme.Gold, 3, 14, 0));
        AddChild(_panel);
        for (var i = 0; i < _entries.Count; i++)
        {
            var index = i;
            var button = _entries[i].Button;
            UiTheme.StyleButton(button, UiTheme.PanelLook);
            button.MouseEntered += () => Select(index);
            button.Pressed += () => Take(index);
            AddChild(button);
        }

        Select(0);
        Resized += Layout;
        Layout();
    }

    /// <summary>Exult <c>MenuList::set_selection</c>: the chosen entry is lit (the new game screen's Journey Onward look).</summary>
    public void Select(int index)
    {
        _selection = (index % _entries.Count + _entries.Count) % _entries.Count;
        for (var i = 0; i < _entries.Count; i++)
        {
            var button = _entries[i].Button;
            UiTheme.StyleButton(button, UiTheme.PanelLook);
            if (i == _selection)
            {
                button.AddThemeStyleboxOverride("normal", UiTheme.Box(new Color(0.35f, 0.26f, 0.12f), UiTheme.GoldBright, 2, 8, 0));
                button.AddThemeStyleboxOverride("hover", UiTheme.Box(new Color(0.38f, 0.28f, 0.13f), UiTheme.GoldBright, 2, 8, 0));
                button.AddThemeColorOverride("font_color", UiTheme.GoldBright);
            }
        }
    }

    /// <summary>Choose the entry by its choice (the console); false if it isn't offered.</summary>
    public bool Take(MenuChoice choice)
    {
        var index = _entries.FindIndex(e => e.Choice == choice);
        if (index < 0)
        {
            return false;
        }

        Take(index);
        return true;
    }

    void Take(int index)
    {
        if (_message is not null)
        {
            return;
        }

        Select(index);
        Chosen?.Invoke(_entries[index].Choice);
    }

    /// <summary>
    /// Exult <c>show_journey_failed</c>: no game to journey on with. The
    /// words stand over the title until a key or click, then Start New Game
    /// is chosen (Exult <c>set_selection(1)</c>).
    /// </summary>
    public void JourneyFailed()
    {
        _message = ["You must start a new game first.", "Press ESC to return."];
        ShowEntries(false);
        QueueRedraw();
    }

    /// <summary>The words go and the menu is back (the console's key).</summary>
    public void DismissMessage()
    {
        if (_message is null)
        {
            return;
        }

        _message = null;
        ShowEntries(true);
        Select(_entries.FindIndex(e => e.Choice == MenuChoice.NewGame));
        QueueRedraw();
    }

    void ShowEntries(bool shown)
    {
        _panel.Visible = shown;
        foreach (var (_, button) in _entries)
        {
            button.Visible = shown;
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (_message is not null)
        {
            if (@event is InputEventKey { Pressed: true, Echo: false } or InputEventMouseButton { Pressed: true })
            {
                DismissMessage();
                GetViewport().SetInputAsHandled();
            }

            return;
        }

        if (@event is not InputEventKey { Pressed: true } key)
        {
            return;
        }

        switch (key.Keycode)
        {
            case Key.Up:
                Select(_selection - 1);
                break;
            case Key.Down:
                Select(_selection + 1);
                break;
            case Key.Enter or Key.KpEnter when !key.Echo:
                Take(_selection);
                break;
            case Key.Q or Key.X when key.AltPressed && !key.Echo:
                Chosen?.Invoke(MenuChoice.Quit);
                break;
            default:
                return;
        }

        GetViewport().SetInputAsHandled();
    }

    float PictureScale => Math.Min(Size.X / PictureWidth, Size.Y / PictureHeight);

    Vector2 Origin => (Size - new Vector2(PictureWidth, PictureHeight) * PictureScale) / 2;

    Rect2 At(float x, float y, float w, float h) => new(Origin + new Vector2(x, y) * PictureScale, new Vector2(w, h) * PictureScale);

    /// <summary>The panel under the title, as tall as its buttons need: roomier with four, closer with up to seven.</summary>
    void Layout()
    {
        var n = _entries.Count;
        var step = n <= 4 ? 17.8f : n <= 6 ? 15f : 13.5f;
        var height = n * step + 10;
        var top = Math.Min(108, 196 - height);
        Place(_panel, At(85, top, 150, height));
        var size = Math.Max(10, Mathf.RoundToInt(8.5f * PictureScale));
        for (var i = 0; i < n; i++)
        {
            var button = _entries[i].Button;
            Place(button, At(92.8f, top + 5.5f + i * step, 134.4f, step - 3.6f));
            button.AddThemeFontOverride("font", UiTheme.Font);
            button.AddThemeFontSizeOverride("font_size", size);
        }

        QueueRedraw();
    }

    static void Place(Control control, Rect2 rect)
    {
        control.Position = rect.Position;
        control.Size = rect.Size;
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Colors.Black);
        if (_title is { } title)
        {
            DrawTextureRect(title, new Rect2(Origin, new Vector2(title.GetWidth(), title.GetHeight()) * PictureScale), false);
        }

        if (_message is { } message)
        {
            // Exult: centred at centery + 30 and + 42 in its menu font.
            var font = UiTheme.Font;
            var px = Math.Max(10, Mathf.RoundToInt(9.5f * PictureScale));
            for (var i = 0; i < message.Length; i++)
            {
                var width = font.GetStringSize(message[i], HorizontalAlignment.Left, -1, px).X;
                var at = Origin + new Vector2(160, 130 + 12 * i) * PictureScale;
                DrawString(font, new Vector2(at.X - width / 2, at.Y), message[i], HorizontalAlignment.Left, -1, px,
                    i == 0 ? UiTheme.GoldBright : UiTheme.Gold);
            }
        }
    }
}
