using Godot;
using U7.Rendering;
using U7.Usecode;

namespace U7.UI;

/// <summary>
/// Conversation panel: dark wood with gold trim along the bottom of the screen,
/// the speaker's portrait and name, the text paged to fit (Exult
/// <c>Conversation::show_npc_message</c>), answers, the number prompt, and a
/// smaller portrait for a second speaker. While text waits for a click the
/// panel takes every click (Exult <c>click_to_continue</c>); Space or Enter
/// also continue, 1–9 pick an answer.
/// </summary>
public sealed partial class ConversationPanel : Control
{
    const int TextSize = 22;
    const int NameSize = 26;
    const int AnswerSize = 20;
    const float FaceScale = 3f;

    public UsecodeMachine? Machine { get; set; }
    public ShapeCache? Shapes { get; set; }

    PanelContainer _frame = null!;
    PanelContainer _portraitBox = null!;
    TextureRect _portrait = null!;
    TextureRect _sidePortrait = null!;
    Label _name = null!;
    Label _text = null!;
    HFlowContainer _answers = null!;
    HBoxContainer _numeric = null!;
    SpinBox _spin = null!;
    ContinueMarker _marker = null!;

    string _shownText = "";
    readonly List<string> _pages = new();
    int _page;
    bool _pagesDirty;
    Vector2 _pagedFor;
    UsecodeWait _builtFor = UsecodeWait.None;
    string? _builtAnswers;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        var font = UiTheme.Font;

        _frame = new PanelContainer
        {
            AnchorLeft = 0,
            AnchorRight = 1,
            AnchorTop = 1,
            AnchorBottom = 1,
            OffsetLeft = 40,
            OffsetRight = -40,
            OffsetTop = -262,
            OffsetBottom = -22,
            MouseFilter = MouseFilterEnum.Ignore
        };
        var frameBox = UiTheme.Box(UiTheme.Wood, UiTheme.Gold, 2, 10, 18);
        frameBox.ShadowColor = new Color(0, 0, 0, 0.55f);
        frameBox.ShadowSize = 10;
        frameBox.ShadowOffset = new Vector2(0, 4);
        _frame.AddThemeStyleboxOverride("panel", frameBox);
        AddChild(_frame);

        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 20);
        _frame.AddChild(row);

        _portraitBox = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkBegin };
        _portraitBox.AddThemeStyleboxOverride("panel",
            UiTheme.Box(UiTheme.WoodDark, UiTheme.Gold with { A = 0.6f }, 1, 6, 6));
        row.AddChild(_portraitBox);
        _portrait = new TextureRect
        {
            CustomMinimumSize = new Vector2(41, 52) * FaceScale,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            TextureFilter = TextureFilterEnum.Nearest,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _portraitBox.AddChild(_portrait);

        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore
        };
        column.AddThemeConstantOverride("separation", 6);
        row.AddChild(column);

        _name = new Label { MouseFilter = MouseFilterEnum.Ignore };
        _name.AddThemeFontOverride("font", font);
        _name.AddThemeFontSizeOverride("font_size", NameSize);
        _name.AddThemeColorOverride("font_color", UiTheme.GoldBright);
        _name.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.7f));
        _name.AddThemeConstantOverride("shadow_offset_y", 2);
        column.AddChild(_name);

        _text = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            VerticalAlignment = VerticalAlignment.Top,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            ClipText = true,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _text.AddThemeFontOverride("font", font);
        _text.AddThemeFontSizeOverride("font_size", TextSize);
        _text.AddThemeColorOverride("font_color", UiTheme.Parchment);
        _text.AddThemeConstantOverride("line_spacing", 2);
        column.AddChild(_text);

        _answers = new HFlowContainer { MouseFilter = MouseFilterEnum.Ignore };
        _answers.AddThemeConstantOverride("h_separation", 10);
        _answers.AddThemeConstantOverride("v_separation", 8);
        column.AddChild(_answers);

        _numeric = new HBoxContainer { Visible = false };
        _numeric.AddThemeConstantOverride("separation", 10);
        _spin = new SpinBox { CustomMinimumSize = new Vector2(140, 0) };
        _spin.GetLineEdit().AddThemeFontOverride("font", font);
        _spin.GetLineEdit().AddThemeFontSizeOverride("font_size", AnswerSize);
        _numeric.AddChild(_spin);
        var ok = MakeButton("OK");
        ok.Pressed += () =>
        {
            Machine?.ResumeWait(UsecodeValue.FromInt((int)_spin.Value));
            Refresh();
        };
        _numeric.AddChild(ok);
        column.AddChild(_numeric);

        _sidePortrait = new TextureRect
        {
            CustomMinimumSize = new Vector2(41, 52) * 2f,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            TextureFilter = TextureFilterEnum.Nearest,
            SizeFlagsVertical = SizeFlags.ShrinkBegin,
            Modulate = new Color(1, 1, 1, 0.85f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        row.AddChild(_sidePortrait);

        _marker = new ContinueMarker
        {
            AnchorLeft = 1,
            AnchorRight = 1,
            AnchorTop = 1,
            AnchorBottom = 1,
            OffsetLeft = -64,
            OffsetRight = -44,
            OffsetTop = -50,
            OffsetBottom = -34,
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_marker);
    }

    /// <summary>Rebuild from the VM's conversation state (faces, text, answers, waits).</summary>
    public void Refresh()
    {
        if (Machine is not { } vm || _frame is null)
        {
            return;
        }

        var conv = vm.Conv;
        var wait = vm.Wait;
        var choosing = wait is UsecodeWait.Converse or UsecodeWait.SelectMenu or UsecodeWait.SelectMenuIndex;
        var hasContent = conv.FaceCount > 0 || conv.NpcText.Length > 0 || (choosing && conv.Answers.Count > 0) ||
                         wait == UsecodeWait.NumericInput;
        // A book or scroll page is painted over everything instead.
        Visible = (vm.InUsecode || vm.WaitingForChoice) && wait is not (UsecodeWait.ClickOnItem or UsecodeWait.BookPage) &&
                  hasContent;
        MouseFilter = Visible && wait == UsecodeWait.ClickToContinue ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
        if (!Visible)
        {
            return;
        }

        // Speaker: the face that said the text, else the face shown last.
        var main = conv.TextFace >= 0 ? conv.TextFace : conv.LastFace;
        var mainFace = main >= 0 ? conv.Faces[main] : null;
        var tex = mainFace is { } mf ? Shapes?.GetFace(mf.Shape, mf.Frame) : null;
        _portrait.Texture = tex;
        _portraitBox.Visible = tex is not null;
        if (tex is not null)
        {
            _portrait.CustomMinimumSize = tex.GetSize() * FaceScale;
        }

        var side = main == 0 ? 1 : 0;
        var sideFace = main >= 0 ? conv.Faces[side] : null;
        _sidePortrait.Texture = sideFace is { } sf ? Shapes?.GetFace(sf.Shape, sf.Frame) : null;
        _sidePortrait.Visible = _sidePortrait.Texture is not null;

        var name = main >= 0 ? conv.FaceNames[main] : "";
        _name.Text = name;
        _name.Visible = name.Length > 0;
        // Exult paints the Guardian's and the serpents' large faces' text in red (font 7).
        _text.AddThemeColorOverride("font_color", tex is not null && tex.GetWidth() > 100 ? UiTheme.GuardianRed : UiTheme.Parchment);

        if (conv.NpcText != _shownText)
        {
            _shownText = conv.NpcText;
            _pagesDirty = true;
        }

        _numeric.Visible = wait == UsecodeWait.NumericInput;
        if (_numeric.Visible && _builtFor != UsecodeWait.NumericInput)
        {
            var (min, max, step, def) = vm.NumericPrompt;
            _spin.MinValue = Math.Min(min, max);
            _spin.MaxValue = Math.Max(min, max);
            _spin.Step = step;
            _spin.Value = def;
        }

        var answerKey = choosing ? string.Join("", conv.Answers) : "";
        if (answerKey != _builtAnswers)
        {
            RebuildAnswers(choosing ? conv.Answers : []);
            _builtAnswers = answerKey;
        }

        _builtFor = wait;
    }

    void RebuildAnswers(IReadOnlyList<string> answers)
    {
        foreach (var child in _answers.GetChildren())
        {
            child.QueueFree();
        }

        for (var i = 0; i < answers.Count; i++)
        {
            var text = answers[i];
            var index = i;
            var btn = MakeButton(i < 9 ? $"{i + 1}  {text}" : text);
            btn.Pressed += () => Choose(text, index);
            _answers.AddChild(btn);
        }
    }

    void Choose(string text, int index)
    {
        if (Machine is not { WaitingForChoice: true } vm || vm.Wait is UsecodeWait.ClickToContinue or UsecodeWait.NumericInput)
        {
            return;
        }

        vm.Choose(text, index);
        Refresh();
    }

    static Button MakeButton(string text)
    {
        var btn = new Button { Text = text, FocusMode = FocusModeEnum.None, MouseDefaultCursorShape = CursorShape.PointingHand };
        btn.AddThemeFontOverride("font", UiTheme.Font);
        btn.AddThemeFontSizeOverride("font_size", AnswerSize);
        btn.AddThemeColorOverride("font_color", UiTheme.Gold);
        btn.AddThemeColorOverride("font_hover_color", UiTheme.GoldBright);
        btn.AddThemeColorOverride("font_pressed_color", UiTheme.GoldBright);
        btn.AddThemeStyleboxOverride("normal", UiTheme.Box(new Color(0.20f, 0.14f, 0.08f, 0.65f), UiTheme.Gold with { A = 0.45f }, 1, 6, 8));
        btn.AddThemeStyleboxOverride("hover", UiTheme.Box(new Color(0.34f, 0.24f, 0.12f, 0.92f), UiTheme.GoldBright, 1, 6, 8));
        btn.AddThemeStyleboxOverride("pressed", UiTheme.Box(new Color(0.42f, 0.30f, 0.14f, 0.95f), UiTheme.GoldBright, 1, 6, 8));
        return btn;
    }

    public override void _Process(double delta)
    {
        if (!Visible || Machine is not { } vm)
        {
            _marker.Visible = false;
            return;
        }

        // Page the text once the label has its size (and again if the window changes).
        if (_pagesDirty || _text.Size != _pagedFor)
        {
            Paginate();
        }

        _marker.Visible = vm.Wait == UsecodeWait.ClickToContinue;
    }

    /// <summary>
    /// Split the current text into pages that fit the text box: a '*' inside the
    /// text starts a new page (Exult <c>Font::paint_text_box</c>), '^' capitalises
    /// the next letter.
    /// </summary>
    void Paginate()
    {
        // Same text in a resized box (answers appeared): stay on the last page if we were there.
        var keepLast = !_pagesDirty && _pages.Count > 0 && _page == _pages.Count - 1;
        _pagesDirty = false;
        _pagedFor = _text.Size;
        _pages.Clear();
        _page = 0;
        var width = Math.Max(100f, _text.Size.X);
        // Leave room for the label's extra line spacing.
        var height = Math.Max(TextSize * 2f, _text.Size.Y - 12);
        var font = UiTheme.Font;
        foreach (var part in Normalize(_shownText).Split('*'))
        {
            var words = part.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var page = "";
            foreach (var word in words)
            {
                var candidate = page.Length == 0 ? word : page + " " + word;
                var size = font.GetMultilineStringSize(candidate, HorizontalAlignment.Left, width, TextSize);
                if (page.Length > 0 && size.Y > height)
                {
                    _pages.Add(page);
                    page = word;
                }
                else
                {
                    page = candidate;
                }
            }

            if (page.Length > 0)
            {
                _pages.Add(page);
            }
        }

        if (_pages.Count == 0)
        {
            _pages.Add("");
        }

        if (keepLast)
        {
            _page = _pages.Count - 1;
        }

        _text.Text = _pages[_page];
    }

    static string Normalize(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        var upper = false;
        foreach (var ch in text)
        {
            if (ch == '^')
            {
                upper = true;
                continue;
            }

            sb.Append(upper ? char.ToUpperInvariant(ch) : ch);
            upper = false;
        }

        // '@' is the originals' quote mark (Exult Text_effect shows it as '"').
        return sb.ToString().Replace('\n', ' ').Replace('@', '"');
    }

    /// <summary>Next page of this text, else let the usecode continue.</summary>
    public void Advance()
    {
        if (Machine is not { Wait: UsecodeWait.ClickToContinue } vm)
        {
            return;
        }

        if (_page < _pages.Count - 1)
        {
            _page++;
            _text.Text = _pages[_page];
            return;
        }

        vm.ContinueText();
        Refresh();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left or MouseButton.Right })
        {
            Advance();
            AcceptEvent();
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (!Visible || Machine is not { } vm || @event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        if (vm.Wait == UsecodeWait.ClickToContinue && key.Keycode is Key.Space or Key.Enter or Key.KpEnter)
        {
            Advance();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (vm.Wait is UsecodeWait.Converse or UsecodeWait.SelectMenu or UsecodeWait.SelectMenuIndex &&
            key.Keycode is >= Key.Key1 and <= Key.Key9)
        {
            var index = (int)(key.Keycode - Key.Key1);
            if (index < vm.Conv.Answers.Count)
            {
                Choose(vm.Conv.Answers[index], index);
                GetViewport().SetInputAsHandled();
            }
        }
    }

    /// <summary>Gold "click to continue" triangle that gently pulses.</summary>
    sealed partial class ContinueMarker : Control
    {
        double _t;

        public override void _Process(double delta)
        {
            _t += delta;
            QueueRedraw();
        }

        public override void _Draw()
        {
            var a = 0.55f + 0.45f * (float)Math.Sin(_t * 4.0);
            var w = Size.X;
            var h = Size.Y;
            var bob = 2f * (float)Math.Sin(_t * 4.0);
            DrawColoredPolygon(
                [new Vector2(0, bob), new Vector2(w, bob), new Vector2(w / 2, h + bob)],
                UiTheme.GoldBright with { A = a });
        }
    }
}
