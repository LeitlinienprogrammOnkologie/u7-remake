using Godot;

namespace U7.UI;

/// <summary>Fonts, colours and box styles shared by the conversation panel and barks.</summary>
public static class UiTheme
{
    public const string FontPath = "res://fonts/MedievalSharp.ttf";

    public static readonly Color Gold = new(0.86f, 0.70f, 0.38f);
    public static readonly Color GoldBright = new(1f, 0.87f, 0.55f);
    public static readonly Color Parchment = new(0.94f, 0.90f, 0.80f);
    public static readonly Color GuardianRed = new(0.95f, 0.32f, 0.25f);
    public static readonly Color Wood = new(0.12f, 0.085f, 0.055f, 0.94f);
    public static readonly Color WoodDark = new(0.06f, 0.045f, 0.03f, 0.96f);
    public static readonly Color BarkYellow = new(1f, 0.87f, 0.32f);
    public static readonly Color Ink = new(0.06f, 0.035f, 0.01f);

    static Font? _font;

    /// <summary>MedievalSharp (SIL OFL, see fonts/OFL.txt); read from the file when it is not imported yet.</summary>
    public static Font Font => _font ??= LoadFont();

    static Font LoadFont()
    {
        if (ResourceLoader.Exists(FontPath) && ResourceLoader.Load<FontFile>(FontPath) is { } imported)
        {
            return imported;
        }

        var font = new FontFile();
        font.LoadDynamicFont(ProjectSettings.GlobalizePath(FontPath));
        return font;
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
