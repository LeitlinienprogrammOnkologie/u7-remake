using Godot;
using U7.Actors;
using U7.Game;
using U7.Rendering;

namespace U7.UI;

/// <summary>
/// Exult <c>Newfile_gump</c>, the save/load screen: a list of names (an empty
/// slot, the quick save, then the saved games, newest first), the chosen
/// game's picture, details and party, and Save, Load, Delete and Cancel,
/// offered as Exult offers them. A name is typed in its row. The user's look
/// (2026-10-10, layout A): the list on the left and the details on the right
/// in the conversation panel's dark wood and gold, MedievalSharp, the party
/// as faces, laid out on 1280×720 scaled to fit, with Quit (Exult's game
/// menu has it) and the title "Save or Load".
/// </summary>
public sealed partial class SaveLoadView : Control
{
    const float RefWidth = 1280, RefHeight = 720;
    const int Rows = 10;
    const float RowHeight = 40, ListX = 200, ListY = 140, ListRight = 658, ListBottom = 548;
    const float TextX = 220, TextMaxWidth = 400, TrackX = 642, TrackTop = 150, TrackBottom = 538;
    const float ShotX = 688, ShotY = 140, ShotW = 288, ShotH = 180, InfoY = 338, FaceX = 1000;
    const int Empty = -2, Quick = -1, None = -3;
    static readonly string[] Months = ["Jan", "Feb", "March", "April", "May", "June", "July", "Aug", "Sept", "Oct", "Nov", "Dec"];

    public Func<List<SaveGame.Entry>> ListGames = SaveGame.List;
    public Func<SaveGame.Entry?> QuickGame = () => SaveGame.Exists(SaveGame.QuickSlot) ? SaveGame.Describe(SaveGame.QuickSlot) : null;
    /// <summary>Save into the slot under the name (null: the quick save, which keeps its own).</summary>
    public Action<string, string?>? Save;
    public Action<string>? Load;
    public Action<string>? Delete;
    public Action? Quit;
    public Action? Closed;

    readonly ShapeCache _shapes;
    /// <summary>Exult <c>cur_details</c>: the game as it is now, read again with the list.</summary>
    readonly Func<SaveInfo> _currentGame;
    SaveInfo _current;
    readonly Texture2D? _currentShot;
    readonly Dictionary<string, Texture2D?> _shots = new();
    List<SaveGame.Entry> _games = [];
    SaveGame.Entry? _quick;
    /// <summary>Exult <c>list_position</c>: the game in the top row (-2 the empty slot, -1 the quick save).</summary>
    int _listPosition = Empty;
    /// <summary>Exult <c>selected</c>: -3 none, -2 the empty slot, -1 the quick save, else a game.</summary>
    int _selected = None;
    SaveInfo? _info;
    Texture2D? _shot;
    bool _readable = true;
    string? _file;
    float _slideStart = -1;
    string _lastText = "";
    Action? _onYes;

    readonly Button _save = new() { Text = "Save" };
    readonly Button _load = new() { Text = "Load" };
    readonly Button _delete = new() { Text = "Delete" };
    readonly Button _cancel = new() { Text = "Cancel" };
    readonly Button _quit = new() { Text = "Quit" };
    readonly LineEdit _edit = new() { MaxLength = SaveGame.MaxNameLength - 1, ContextMenuEnabled = false, CaretBlink = true, Visible = false };
    readonly Control _confirm = new() { Visible = false, MouseFilter = MouseFilterEnum.Stop };
    readonly Panel _confirmPanel = new();
    readonly Label _confirmText = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
    readonly Button _yes = new() { Text = "Yes" };
    readonly Button _no = new() { Text = "No" };
    readonly StyleBoxFlat _panelBox = UiTheme.Box(UiTheme.Wood with { A = 0.97f }, UiTheme.Gold, 3, 14, 0);
    readonly StyleBoxFlat _listBox = UiTheme.Box(UiTheme.WoodDark, UiTheme.Gold with { A = 0.5f }, 2, 8, 0);
    readonly StyleBoxFlat _rowBox = UiTheme.Box(new Color(0.24f, 0.17f, 0.09f), UiTheme.GoldBright, 2, 6, 0);
    readonly StyleBoxFlat _frameBox = UiTheme.Box(UiTheme.WoodDark, UiTheme.Gold with { A = 0.6f }, 2, 6, 0);
    readonly StyleBoxFlat _faceBox = UiTheme.Box(UiTheme.WoodDark, UiTheme.Gold with { A = 0.5f }, 1, 6, 0);

    public SaveLoadView(ShapeCache shapes, Func<SaveInfo> currentGame, Image? currentShot)
    {
        _shapes = shapes;
        _currentGame = currentGame;
        _current = currentGame();
        _currentShot = currentShot is null ? null : ImageTexture.CreateFromImage(currentShot);
    }

    /// <summary>The rows' names as the list shows them, for the console.</summary>
    public IEnumerable<string> RowNames => new[] { "Empty Slot", "Quick Save" }.Concat(_games.Select(g => g.Name));

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        foreach (var (button, action) in new (Button, Action)[]
                 {
                     (_save, SavePressed), (_load, LoadPressed), (_delete, DeletePressed), (_cancel, Close), (_quit, QuitPressed),
                     (_yes, () => Answer(true)), (_no, () => Answer(false))
                 })
        {
            UiTheme.StyleButton(button, UiTheme.PanelLook);
            button.Pressed += action;
            (button == _yes || button == _no ? (Node)_confirmPanel : this).AddChild(button);
        }

        _edit.AddThemeColorOverride("font_color", UiTheme.GoldBright);
        _edit.AddThemeColorOverride("caret_color", UiTheme.GoldBright);
        _edit.AddThemeColorOverride("selection_color", UiTheme.Gold with { A = 0.35f });
        _edit.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        _edit.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        _edit.TextChanged += NameChanged;
        _edit.TextSubmitted += _ => EnterPressed();
        AddChild(_edit);

        _confirmPanel.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Wood with { A = 0.98f }, UiTheme.Gold, 3, 12, 0));
        _confirmText.AddThemeColorOverride("font_color", UiTheme.Parchment);
        _confirmPanel.AddChild(_confirmText);
        _confirm.AddChild(_confirmPanel);
        _confirm.Draw += () => _confirm.DrawRect(new Rect2(Vector2.Zero, _confirm.Size), new Color(0, 0, 0, 0.45f));
        AddChild(_confirm);

        Refresh();
        Resized += Layout;
        Layout();
    }

    float ViewScale => Math.Min(Size.X / RefWidth, Size.Y / RefHeight);

    Vector2 Origin => (Size - new Vector2(RefWidth, RefHeight) * ViewScale) / 2;

    Rect2 At(float x, float y, float w, float h) => new(Origin + new Vector2(x, y) * ViewScale, new Vector2(w, h) * ViewScale);

    Vector2 ToRef(Vector2 pos) => (pos - Origin) / ViewScale;

    int FontSize(float refSize) => Math.Max(8, Mathf.RoundToInt(refSize * ViewScale));

    void Layout()
    {
        Place(_save, At(200, 580, 130, 46));
        Place(_load, At(346, 580, 130, 46));
        Place(_delete, At(492, 580, 130, 46));
        Place(_cancel, At(638, 580, 130, 46));
        Place(_quit, At(950, 580, 130, 46));
        Place(_confirm, new Rect2(Vector2.Zero, Size));
        Place(_confirmPanel, At(400, 265, 480, 190));
        _confirmText.Position = new Vector2(24, 18) * ViewScale;
        _confirmText.Size = new Vector2(432, 96) * ViewScale;
        _yes.Position = new Vector2(96, 124) * ViewScale;
        _yes.Size = new Vector2(120, 44) * ViewScale;
        _no.Position = new Vector2(264, 124) * ViewScale;
        _no.Size = new Vector2(120, 44) * ViewScale;
        foreach (var control in new Control[] { _save, _load, _delete, _cancel, _quit, _yes, _no, _confirmText, _edit })
        {
            control.AddThemeFontOverride("font", UiTheme.Font);
            control.AddThemeFontSizeOverride("font_size", FontSize(control == _confirmText ? 22 : control == _edit ? 22 : 24));
        }

        PlaceEdit();
        QueueRedraw();
    }

    static void Place(Control control, Rect2 rect)
    {
        control.Position = rect.Position;
        control.Size = rect.Size;
    }

    /// <summary>The name being typed sits in the chosen row while it shows (Exult paints <c>newname</c> there).</summary>
    void PlaceEdit()
    {
        var line = _selected - _listPosition;
        var editable = _selected >= Empty && _selected != Quick;
        _edit.Visible = editable && line >= 0 && line < Rows;
        if (_edit.Visible)
        {
            Place(_edit, At(TextX, ListY + 6 + line * RowHeight, ListRight - 22 - TextX, RowHeight - 4));
        }
    }

    /// <summary>Exult <c>LoadSaveGameDetails</c>: the games read again, nothing chosen, the current game's details shown.</summary>
    void Refresh()
    {
        _games = ListGames();
        _quick = QuickGame();
        _current = _currentGame();
        _shots.Clear();
        _selected = None;
        _listPosition = Math.Clamp(_listPosition, Empty, Math.Max(Empty, _games.Count - Rows));
        ShowCurrent();
        _file = null;
        _readable = true;
        Offer(load: false, save: false, delete: false);
        _edit.Visible = false;
        QueueRedraw();
    }

    void ShowCurrent()
    {
        _info = _current;
        _shot = _currentShot;
    }

    void Offer(bool load, bool save, bool delete)
    {
        _load.Visible = load;
        _save.Visible = save;
        _delete.Visible = delete;
    }

    Texture2D? ShotOf(SaveGame.Entry entry)
    {
        if (!_shots.TryGetValue(entry.Slot, out var texture))
        {
            texture = entry.ShotPath is { } path && Image.LoadFromFile(path) is { } image && !image.IsEmpty()
                ? ImageTexture.CreateFromImage(image)
                : null;
            _shots[entry.Slot] = texture;
        }

        return texture;
    }

    /// <summary>
    /// Exult <c>Newfile_gump::mouse_down</c> on a row: the game is chosen,
    /// its details shown, and the buttons it allows offered: none for the
    /// empty slot until a name is typed, Load and Save for the quick save
    /// (Load only if there is one), all three for a game (Load if readable).
    /// </summary>
    public void Choose(int game)
    {
        if (game < Empty || game >= _games.Count || game == _selected)
        {
            return;
        }

        _selected = game;
        if (game < _listPosition || game >= _listPosition + Rows)
        {
            _listPosition = Math.Clamp(game, Empty, Math.Max(Empty, _games.Count - Rows));
        }

        string name;
        if (game == Empty)
        {
            ShowCurrent();
            name = "";
            _readable = true;
            _file = null;
            Offer(load: false, save: false, delete: false);
        }
        else if (game == Quick)
        {
            _info = _quick?.Info;
            _shot = _quick is { } quick ? ShotOf(quick) : null;
            name = "Quick Save";
            _readable = true;
            _file = null;
            Offer(load: _quick is { Readable: true }, save: true, delete: false);
        }
        else
        {
            var entry = _games[game];
            _info = entry.Info;
            _shot = ShotOf(entry);
            name = entry.Name;
            _readable = entry.Readable;
            _file = entry.Slot;
            Offer(load: entry.Readable, save: true, delete: true);
        }

        _lastText = name;
        _edit.SetBlockSignals(true);
        _edit.Text = name;
        _edit.SetBlockSignals(false);
        PlaceEdit();
        if (_edit.Visible)
        {
            _edit.GrabFocus();
            _edit.CaretColumn = name.Length;
        }

        QueueRedraw();
    }

    /// <summary>
    /// Exult <c>character_input</c>: a changed name can't be loaded or
    /// deleted, only saved (if not empty), and the details are the current
    /// game's; a name too wide for its row (Exult <c>textw</c>) is refused.
    /// </summary>
    void NameChanged(string text)
    {
        if (UiTheme.Font.GetStringSize(text, HorizontalAlignment.Left, -1, 22).X > TextMaxWidth)
        {
            var caret = Math.Max(0, _edit.CaretColumn - (text.Length - _lastText.Length));
            _edit.SetBlockSignals(true);
            _edit.Text = _lastText;
            _edit.SetBlockSignals(false);
            _edit.CaretColumn = Math.Min(caret, _lastText.Length);
            return;
        }

        _lastText = text;
        Offer(load: false, save: text.Length > 0, delete: false);
        ShowCurrent();
        QueueRedraw();
    }

    /// <summary>Exult: Enter saves when Save is the only choice.</summary>
    void EnterPressed()
    {
        if (!_load.Visible && _save.Visible)
        {
            SavePressed();
        }
    }

    /// <summary>The name typed in the console (as keys would type it).</summary>
    public void TypeName(string text)
    {
        if (_edit.Visible)
        {
            _edit.Text = text;
            _edit.CaretColumn = text.Length;
            NameChanged(text);
        }
    }

    /// <summary>
    /// Exult <c>Newfile_gump::save</c>: not without a name; over a saved
    /// game only once asked; the empty slot takes the first free number, the
    /// quick save keeps its own. The list is read again and nothing is chosen;
    /// the screen stays.
    /// </summary>
    public void SavePressed()
    {
        var name = _edit.Visible ? _edit.Text : "";
        if (!_save.Visible || _selected == None || (_selected != Quick && name.Length == 0))
        {
            return;
        }

        var slot = _selected switch
        {
            Empty => SaveGame.FirstFreeSlot(),
            Quick => SaveGame.QuickSlot,
            _ => _games[_selected].Slot
        };
        void Write()
        {
            Save?.Invoke(slot, _selected == Quick ? null : name);
            Refresh();
        }

        if (_selected == Empty || (_selected == Quick && _quick is null))
        {
            Write();
        }
        else
        {
            Ask("Okay to write over existing saved game?", Write);
        }
    }

    /// <summary>Exult <c>Newfile_gump::load</c>: the chosen game is read and the screen is done.</summary>
    public void LoadPressed()
    {
        if (!_load.Visible || _selected < Quick)
        {
            return;
        }

        var slot = _selected == Quick ? SaveGame.QuickSlot : _games[_selected].Slot;
        Close();
        Load?.Invoke(slot);
    }

    /// <summary>Exult <c>Newfile_gump::delete_file</c>: once asked, the game is removed and the list read again.</summary>
    public void DeletePressed()
    {
        if (!_delete.Visible || _selected < 0)
        {
            return;
        }

        var slot = _games[_selected].Slot;
        Ask("Okay to delete saved game?", () =>
        {
            Delete?.Invoke(slot);
            Refresh();
        });
    }

    /// <summary>Exult <c>Gump_manager::okay_to_quit</c>: once asked, the game ends.</summary>
    public void QuitPressed() => Ask("Do you really want to quit?", () => Quit?.Invoke());

    /// <summary>The screen closes (Cancel, Esc).</summary>
    public void Close()
    {
        if (Closed is { } closed)
        {
            Closed = null;
            closed();
        }
    }

    /// <summary>Exult <c>Yesno_gump::ask</c>: the question over the screen until Yes or No.</summary>
    void Ask(string question, Action yes)
    {
        _confirmText.Text = question;
        _onYes = yes;
        _confirm.Visible = true;
        _confirm.QueueRedraw();
    }

    /// <summary>The question in front, for the console; null if none.</summary>
    public string? Question => _confirm.Visible ? _confirmText.Text : null;

    public void Answer(bool yes)
    {
        if (!_confirm.Visible)
        {
            return;
        }

        _confirm.Visible = false;
        var then = _onYes;
        _onYes = null;
        if (yes)
        {
            then?.Invoke();
        }

        if (_edit.Visible)
        {
            _edit.GrabFocus();
        }
    }

    /// <summary>What the console reports: the rows, the choice, the buttons offered and the details shown.</summary>
    public string Describe()
    {
        var rows = string.Join(" | ", RowNames.Select((n, i) => (i - 2 == _selected ? "*" : "") + n));
        var buttons = string.Join(" ", new[] { _save, _load, _delete, _cancel, _quit }.Where(b => b.Visible).Select(b => b.Text));
        var name = _edit.Visible ? $" name \"{_edit.Text}\"" : "";
        return $"rows: {rows}\nchosen {_selected}{name}; buttons: {buttons}\n" + string.Join("\n", InfoLines()) +
               (_shot is null ? "\n(no picture)" : $"\npicture {_shot.GetWidth()}x{_shot.GetHeight()}") +
               (Question is { } q ? $"\nasks: {q}" : "");
    }

    /// <summary>Exult's <c>infostring</c>, or "No Info" / "Unreadable Savegame", and the file.</summary>
    List<string> InfoLines()
    {
        var lines = new List<string>();
        if (_info is { Party.Count: > 0 } info)
        {
            var av = info.Party[0];
            var d = info.Details;
            lines.AddRange([
                $"Avatar: {av.Name}", $"Exp: {av.Exp}  Hp: {av.Health}", $"Str: {av.Str}  Dxt: {av.Dext}",
                $"Int: {av.Intel}  Trn: {av.Training}", "", $"Game Day: {d.GameDay}", $"Game Time: {d.GameHour:00}:{d.GameMinute:00}",
                "", $"Save Count: {d.SaveCount}", $"Date: {d.Real.Day}{Suffix(d.Real.Day)} {Months[d.Real.Month - 1]} {d.Real.Year:0000}",
                $"Time: {d.Real.Hour:00}:{d.Real.Minute:00}"
            ]);
        }
        else
        {
            lines.Add(_readable ? "No Info" : "Unreadable Savegame");
        }

        if (_file is { } file)
        {
            lines.Add($"File: {file}");
        }

        return lines;
    }

    static string Suffix(int day) =>
        day % 10 == 1 && day != 11 ? "st" : day % 10 == 2 && day != 12 ? "nd" : day % 10 == 3 && day != 13 ? "rd" : "th";

    /// <summary>Exult <c>scroll_line</c>: the list moves, the empty slot at most at the top, the last game at the bottom.</summary>
    void ScrollLine(int dir)
    {
        _listPosition = Math.Clamp(_listPosition + dir, Empty, Math.Max(Empty, _games.Count - Rows));
        PlaceEdit();
        QueueRedraw();
    }

    /// <summary>Exult's slider: the positions it can be in, and where it is.</summary>
    int SliderPositions => Math.Max(1, 2 + _games.Count - Rows);

    float SliderHeight => Math.Max(28, (TrackBottom - TrackTop) * Math.Min(1f, (float)Rows / (2 + _games.Count)));

    float SliderTop => TrackTop + (TrackBottom - TrackTop - SliderHeight) * (_listPosition + 2) / SliderPositions;

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel:
                // Exult mousewheel_up/down: a line, with Alt a page.
                ScrollLine((wheel.ButtonIndex == MouseButton.WheelUp ? -1 : 1) * (wheel.AltPressed ? Rows : 1));
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                if (button.Pressed)
                {
                    MouseDown(ToRef(button.Position));
                }
                else
                {
                    _slideStart = -1;
                }

                AcceptEvent();
                break;
            case InputEventMouseMotion motion when _slideStart >= 0:
                SlideTo(ToRef(motion.Position));
                AcceptEvent();
                break;
        }
    }

    /// <summary>Exult <c>Newfile_gump::mouse_down</c>: the slider's track pages, the slider drags, a row is chosen.</summary>
    void MouseDown(Vector2 at)
    {
        _slideStart = -1;
        if (at.X >= TrackX - 4 && at.X < TrackX + 12 && at.Y >= TrackTop && at.Y < TrackBottom)
        {
            if (at.Y < SliderTop)
            {
                ScrollLine(-Rows);
            }
            else if (at.Y >= SliderTop + SliderHeight)
            {
                ScrollLine(Rows);
            }
            else
            {
                _slideStart = at.Y;
            }

            return;
        }

        if (at.X < ListX + 6 || at.X >= TrackX - 4 || at.Y < ListY + 6)
        {
            return;
        }

        var line = (int)((at.Y - ListY - 6) / RowHeight);
        if (line < Rows)
        {
            Choose(line + _listPosition);
        }
    }

    /// <summary>Exult <c>Newfile_gump::mouse_drag</c>: the list follows the slider.</summary>
    void SlideTo(Vector2 at)
    {
        var positions = 2 + _games.Count - Rows;
        if (positions < 1)
        {
            return;
        }

        var range = TrackBottom - TrackTop - SliderHeight;
        var sy = Math.Clamp(at.Y - TrackTop - SliderHeight / 2, 0, range);
        var pos = Mathf.RoundToInt(sy * positions / range) - 2;
        if (pos != _listPosition)
        {
            _listPosition = pos;
            PlaceEdit();
            QueueRedraw();
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        if (_confirm.Visible)
        {
            // Exult Yesno_gump::key_down: y or Enter yes, n or Esc no.
            if (key.Keycode is Key.Y or Key.Enter or Key.KpEnter)
            {
                Answer(true);
            }
            else if (key.Keycode is Key.N or Key.Escape)
            {
                Answer(false);
            }

            GetViewport().SetInputAsHandled();
        }
        else if (key.Keycode == Key.Escape)
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0, 0, 0, 0.55f));
        DrawStyleBox(_panelBox, At(170, 70, 940, 580));
        DrawCentred("Save or Load", 640, 104, 30, UiTheme.Gold);
        DrawStyleBox(_listBox, At(ListX, ListY, ListRight - ListX, ListBottom - ListY));
        for (var line = 0; line < Rows; line++)
        {
            var game = line + _listPosition;
            if (game < Empty || game >= _games.Count)
            {
                continue;
            }

            var y = ListY + 6 + line * RowHeight;
            var chosen = game == _selected;
            if (chosen)
            {
                DrawStyleBox(_rowBox, At(ListX + 6, y, TrackX - 10 - ListX, RowHeight - 4));
            }

            if (chosen && _edit.Visible)
            {
                continue; // the name being typed shows there
            }

            var text = game == Quick ? "Quick Save" : game == Empty ? "Empty Slot" : _games[game].Name;
            var colour = game == Empty ? UiTheme.Parchment with { A = 0.55f } : chosen ? UiTheme.GoldBright : UiTheme.Parchment;
            DrawLeft(text, TextX, y + (RowHeight - 4) / 2, 22, colour, TextMaxWidth);
        }

        DrawRect(At(TrackX, TrackTop, 8, TrackBottom - TrackTop), UiTheme.Gold with { A = 0.18f });
        DrawRect(At(TrackX, SliderTop, 8, SliderHeight), UiTheme.Gold with { A = 0.8f });

        DrawStyleBox(_frameBox, At(ShotX - 4, ShotY - 4, ShotW + 8, ShotH + 8));
        if (_shot is { } shot)
        {
            DrawTextureRect(shot, At(ShotX, ShotY, ShotW, ShotH), false);
        }

        var lines = InfoLines();
        if (_info is { Party.Count: > 0 })
        {
            var y = InfoY;
            foreach (var line in lines)
            {
                if (line.Length > 0)
                {
                    DrawLeft(line, ShotX + 2, y + 9, 18, UiTheme.Parchment, ShotW + 80);
                }

                y += line.Length > 0 ? 21 : 9;
            }

            DrawFaces(_info.Party);
        }
        else
        {
            for (var i = 0; i < lines.Count; i++)
            {
                DrawCentred(lines[i], ShotX + ShotW / 2, InfoY + 60 + i * 26, i == 0 ? 22 : 18, UiTheme.Parchment);
            }
        }
    }

    /// <summary>The party's faces (Exult paints their shapes), a column of four, or two columns of smaller ones.</summary>
    void DrawFaces(IReadOnlyList<SaveParty> party)
    {
        var big = party.Count <= 4;
        var (w, h, step) = big ? (70f, 80f, 96f) : (46f, 52f, 62f);
        for (var i = 0; i < party.Count && i < 10; i++)
        {
            var x = big ? FaceX : FaceX - 6 + (i % 2) * (w + 10);
            var y = ShotY + 2 + (big ? i : i / 2) * step;
            DrawStyleBox(_faceBox, At(x - 4, y - 4, w + 8, h + 8));
            var p = party[i];
            // The avatar's face is its skin's: the one whose body it was saved with.
            var (faceShape, frame) = i == 0 ? AvatarLook.FaceOfBody(p.Shape) : (p.Face, 0);
            if (_shapes.GetFace(faceShape, frame) is { } face)
            {
                var size = new Vector2(face.GetWidth(), face.GetHeight());
                var fit = Math.Min(w / size.X, h / size.Y);
                var drawn = size * fit;
                DrawTextureRect(face, At(x + (w - drawn.X) / 2, y + (h - drawn.Y) / 2, drawn.X, drawn.Y), false);
            }
        }
    }

    void DrawLeft(string text, float x, float centreY, float size, Color colour, float maxWidth)
    {
        var font = UiTheme.Font;
        var px = FontSize(size);
        var baseline = Origin.Y + centreY * ViewScale + (font.GetAscent(px) - font.GetDescent(px)) / 2;
        DrawString(font, new Vector2(Origin.X + x * ViewScale, baseline), text, HorizontalAlignment.Left, maxWidth * ViewScale, px, colour);
    }

    void DrawCentred(string text, float cx, float centreY, float size, Color colour)
    {
        var font = UiTheme.Font;
        var px = FontSize(size);
        var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, px).X;
        var baseline = Origin.Y + centreY * ViewScale + (font.GetAscent(px) - font.GetDescent(px)) / 2;
        DrawString(font, new Vector2(Origin.X + cx * ViewScale - width / 2, baseline), text, HorizontalAlignment.Left, -1, px, colour);
    }
}
