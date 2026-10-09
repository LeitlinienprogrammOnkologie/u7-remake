using System.IO;
using Godot;
using U7.Actors;
using U7.Core;
using U7.Data;
using U7.Rendering;

namespace U7.UI;

/// <summary>
/// The new game's screen, Exult <c>BG_Game::new_game</c>: a name (16
/// characters, npc.dat's field) and the sex, with the avatar's portrait, then
/// "Journey Onward". The user's look (2026-10-09): the original title from
/// MAINSHP.FLX through Exult's menu palette (INTROPAL.DAT 6), the form below
/// it in the conversation panel's dark wood and gold, MedievalSharp, laid out
/// on the 320×200 picture scaled to fit the window. A name is required.
/// </summary>
public sealed partial class NewGameView : Control
{
    /// <summary>Exult's menu shapes: the title.</summary>
    const int TitleShape = 0x2;
    /// <summary>INTROPAL.DAT's palette Exult maps the menu shapes from.</summary>
    const int MenuPalette = 6;
    const int MaxNameLength = 16;
    const float PictureWidth = 320, PictureHeight = 200;

    /// <summary>The choice: the name and whether the avatar is a woman.</summary>
    public Action<string, bool>? Finished;

    readonly ShapeCache _shapes;
    Texture2D? _title;
    readonly Panel _panel = new();
    readonly Label _nameLabel = new() { Text = "Name", VerticalAlignment = VerticalAlignment.Center };
    readonly LineEdit _name = new() { MaxLength = MaxNameLength, ContextMenuEnabled = false, CaretBlink = true, MouseDefaultCursorShape = CursorShape.Arrow };
    readonly Label _sexLabel = new() { Text = "Sex", VerticalAlignment = VerticalAlignment.Center };
    readonly Button _male = new() { Text = "Male", ToggleMode = true, ButtonPressed = true };
    readonly Button _female = new() { Text = "Female", ToggleMode = true };
    readonly TextureRect _portrait = new()
    {
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        TextureFilter = TextureFilterEnum.Nearest
    };
    readonly Button _go = new() { Text = "Journey Onward", Disabled = true };
    readonly StyleBoxFlat _nameBox = UiTheme.Box(UiTheme.WoodDark, UiTheme.Gold, 2, 6, 0);
    readonly StyleBoxFlat _nameFocus = UiTheme.Box(Colors.Transparent, UiTheme.GoldBright, 2, 6, 0);

    public NewGameView(ShapeCache shapes) => _shapes = shapes;

    bool Female => _female.ButtonPressed;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        TextureFilter = TextureFilterEnum.Nearest;
        _title = LoadTitle();

        _panel.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Wood with { A = 0.97f }, UiTheme.Gold, 3, 14, 0));
        AddChild(_panel);
        foreach (var label in new[] { _nameLabel, _sexLabel })
        {
            label.AddThemeColorOverride("font_color", UiTheme.Gold);
            AddChild(label);
        }

        _name.AddThemeColorOverride("font_color", UiTheme.GoldBright);
        _name.AddThemeColorOverride("caret_color", UiTheme.GoldBright);
        _name.AddThemeColorOverride("selection_color", UiTheme.Gold with { A = 0.35f });
        _name.AddThemeStyleboxOverride("normal", _nameBox);
        _name.AddThemeStyleboxOverride("focus", _nameFocus);
        _name.TextChanged += _ => _go.Disabled = _name.Text.Trim().Length == 0;
        _name.TextSubmitted += _ => Finish();
        AddChild(_name);

        var sexes = new ButtonGroup();
        foreach (var button in new[] { _male, _female, _go })
        {
            if (button != _go)
            {
                button.ButtonGroup = sexes;
                button.Toggled += _ => ShowPortrait();
            }

            button.FocusMode = FocusModeEnum.None;
            button.MouseDefaultCursorShape = CursorShape.PointingHand;
            button.AddThemeColorOverride("font_color", UiTheme.Gold);
            button.AddThemeColorOverride("font_hover_color", UiTheme.GoldBright);
            button.AddThemeColorOverride("font_pressed_color", UiTheme.GoldBright);
            button.AddThemeColorOverride("font_hover_pressed_color", UiTheme.GoldBright);
            button.AddThemeColorOverride("font_disabled_color", UiTheme.Gold with { A = 0.45f });
            button.AddThemeStyleboxOverride("normal", UiTheme.Box(UiTheme.WoodDark, UiTheme.Gold with { A = 0.5f }, 2, 6, 0));
            button.AddThemeStyleboxOverride("hover", UiTheme.Box(new Color(0.24f, 0.17f, 0.09f), UiTheme.GoldBright, 2, 6, 0));
            button.AddThemeStyleboxOverride("pressed", UiTheme.Box(new Color(0.35f, 0.26f, 0.12f), UiTheme.GoldBright, 2, 6, 0));
            button.AddThemeStyleboxOverride("hover_pressed", UiTheme.Box(new Color(0.38f, 0.28f, 0.13f), UiTheme.GoldBright, 2, 6, 0));
            button.AddThemeStyleboxOverride("disabled", UiTheme.Box(UiTheme.WoodDark, UiTheme.Gold with { A = 0.25f }, 2, 6, 0));
            AddChild(button);
        }

        _go.AddThemeStyleboxOverride("normal", UiTheme.Box(new Color(0.35f, 0.26f, 0.12f), UiTheme.GoldBright, 2, 8, 0));
        _go.AddThemeColorOverride("font_color", UiTheme.GoldBright);
        _go.Pressed += Finish;
        AddChild(_portrait);
        ShowPortrait();
        Resized += Layout;
        Layout();
        _name.GrabFocus();
    }

    /// <summary>MAINSHP.FLX's title through INTROPAL.DAT's palette 6 (16-bit entries, 6-bit values).</summary>
    static Texture2D? LoadTitle()
    {
        var palette = new FlexFile(Path.Combine(U7Paths.StaticDir, "INTROPAL.DAT")).Get(MenuPalette);
        var raw6 = new byte[768];
        for (var i = 0; i < raw6.Length && 2 * i < palette.Length; i++)
        {
            raw6[i] = palette[2 * i];
        }

        var menu = new VgaShapeFile(Path.Combine(U7Paths.StaticDir, "MAINSHP.FLX"));
        return menu.Decode(TitleShape, 0, U7Palette.ToRgb(raw6)) is { } image ? ImageTexture.CreateFromImage(image) : null;
    }

    void ShowPortrait() => _portrait.Texture = _shapes.GetFace(AvatarLook.FaceShape, AvatarLook.FaceFrame(Female));

    void Finish()
    {
        var name = _name.Text.Trim();
        if (name.Length > 0 && Finished is { } finished)
        {
            Finished = null;
            finished(name, Female);
        }
    }

    float PictureScale => Math.Min(Size.X / PictureWidth, Size.Y / PictureHeight);

    Vector2 Origin => (Size - new Vector2(PictureWidth, PictureHeight) * PictureScale) / 2;

    /// <summary>A rectangle given in picture pixels, in the window.</summary>
    Rect2 At(float x, float y, float w, float h) => new(Origin + new Vector2(x, y) * PictureScale, new Vector2(w, h) * PictureScale);

    void Layout()
    {
        var size = Math.Max(10, Mathf.RoundToInt(9.5f * PictureScale));
        Place(_panel, At(47.5f, 105, 225, 85));
        Place(_nameLabel, At(60, 112, 36, 15));
        Place(_name, At(97.5f, 114.5f, 90, 13.5f));
        Place(_sexLabel, At(60, 133.5f, 36, 15));
        Place(_male, At(97.5f, 135.5f, 42.5f, 13.5f));
        Place(_female, At(145, 135.5f, 42.5f, 13.5f));
        Place(_go, At(60, 165, 127.5f, 15));
        Place(_portrait, At(204, 116, 56, 62));
        foreach (var control in new Control[] { _nameLabel, _sexLabel, _name, _male, _female, _go })
        {
            control.AddThemeFontOverride("font", UiTheme.Font);
            control.AddThemeFontSizeOverride("font_size", size);
        }

        _name.AddThemeConstantOverride("minimum_character_width", 0);
        _nameBox.ContentMarginLeft = _nameBox.ContentMarginRight = 4 * PictureScale;
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
    }
}
