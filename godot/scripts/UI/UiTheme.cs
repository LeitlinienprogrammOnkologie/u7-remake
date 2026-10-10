using Godot;

namespace U7.UI;

/// <summary>Fonts, colours and box styles shared by the conversation panel and barks.</summary>
public static class UiTheme
{
    public const string FontPath = "res://fonts/MedievalSharp.ttf";
    /// <summary>
    /// The .ttf as Godot imports it (the settings in its .import file), saved
    /// as a plain resource: game runs need no editor import, so every machine
    /// shows the same font. Re-save it after changing the import settings.
    /// </summary>
    public const string BakedFontPath = "res://fonts/MedievalSharp.res";

    public static readonly Color Gold = new(0.86f, 0.70f, 0.38f);
    public static readonly Color GoldBright = new(1f, 0.87f, 0.55f);
    public static readonly Color Parchment = new(0.94f, 0.90f, 0.80f);
    public static readonly Color GuardianRed = new(0.95f, 0.32f, 0.25f);
    public static readonly Color Wood = new(0.12f, 0.085f, 0.055f, 0.94f);
    public static readonly Color WoodDark = new(0.06f, 0.045f, 0.03f, 0.96f);
    public static readonly Color BarkYellow = new(1f, 0.87f, 0.32f);
    public static readonly Color Ink = new(0.06f, 0.035f, 0.01f);

    static Font? _font;

    /// <summary>MedievalSharp (SIL OFL, see fonts/OFL.txt): the baked resource, else read from the .ttf as it is.</summary>
    public static Font Font => _font ??= LoadFont();

    static Font LoadFont()
    {
        if (ResourceLoader.Exists(BakedFontPath) && ResourceLoader.Load<FontFile>(BakedFontPath) is { } baked)
        {
            return baked;
        }

        var font = new FontFile();
        font.LoadDynamicFont(ProjectSettings.GlobalizePath(FontPath));
        return font;
    }

    /// <summary>The size of the words over the world (barks, the cursor's flashes): Exult's font at the world's zoom, kept readable.</summary>
    public static int WorldTextSize(float zoom) => Mathf.Clamp(Mathf.RoundToInt(6.5f * zoom), 14, 40);

    /// <summary>
    /// A gold-on-wood button's boxes: the backgrounds of its states, its
    /// border's colour at rest and when disabled (bright gold under the mouse
    /// and pressed), its width, corner radius and margin.
    /// </summary>
    public sealed record ButtonLook(Color Normal, Color Hover, Color Pressed, Color HoverPressed, Color Border, Color DisabledBorder,
        int BorderWidth, int Radius, int Margin);

    /// <summary>
    /// Every button (the user's pick, 2026-10-10: the new game screen's look
    /// for the conversation's answers too): dark wood, a firm gold border.
    /// Buttons that size to their words add a margin (<c>with { Margin = ... }</c>).
    /// </summary>
    public static readonly ButtonLook PanelLook = new(WoodDark, new Color(0.24f, 0.17f, 0.09f), new Color(0.35f, 0.26f, 0.12f),
        new Color(0.38f, 0.28f, 0.13f), Gold with { A = 0.5f }, Gold with { A = 0.25f }, 2, 6, 0);

    /// <summary>A gold-on-wood button: gold words, bright under the mouse and pressed, faded when disabled, in the look's boxes.</summary>
    public static void StyleButton(Button button, ButtonLook look)
    {
        button.FocusMode = Control.FocusModeEnum.None;
        button.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        button.AddThemeColorOverride("font_color", Gold);
        button.AddThemeColorOverride("font_hover_color", GoldBright);
        button.AddThemeColorOverride("font_pressed_color", GoldBright);
        button.AddThemeColorOverride("font_hover_pressed_color", GoldBright);
        button.AddThemeColorOverride("font_disabled_color", Gold with { A = 0.45f });
        button.AddThemeStyleboxOverride("normal", Box(look.Normal, look.Border, look.BorderWidth, look.Radius, look.Margin));
        button.AddThemeStyleboxOverride("hover", Box(look.Hover, GoldBright, look.BorderWidth, look.Radius, look.Margin));
        button.AddThemeStyleboxOverride("pressed", Box(look.Pressed, GoldBright, look.BorderWidth, look.Radius, look.Margin));
        button.AddThemeStyleboxOverride("hover_pressed", Box(look.HoverPressed, GoldBright, look.BorderWidth, look.Radius, look.Margin));
        button.AddThemeStyleboxOverride("disabled", Box(look.Normal, look.DisabledBorder, look.BorderWidth, look.Radius, look.Margin));
    }

    public static StyleBoxFlat Box(Color bg, Color border, int borderWidth, int radius, int margin)
    {
        var box = new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
            AntiAliasing = true
        };
        box.SetBorderWidthAll(borderWidth);
        box.SetCornerRadiusAll(radius);
        box.SetContentMarginAll(margin);
        return box;
    }
}
