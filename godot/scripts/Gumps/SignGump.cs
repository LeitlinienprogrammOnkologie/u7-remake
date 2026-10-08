using System.Text;
using U7.Data;

namespace U7.Gumps;

/// <summary>
/// Exult <c>Sign_gump</c>: a wooden sign, tombstone or gold plaque (GUMPS.VGA
/// 49-51; the scroll 55) with its lines spread evenly down the shape's text
/// area, each centred, in the sign's rune font (<c>display_runes</c>). The
/// rune fonts have runes for lower case and letters for capitals.
/// </summary>
public sealed class SignGump
{
    public const int WoodSign = 49, Tombstone = 50, GoldSign = 51, Scroll = 55;

    public int GumpShape { get; }
    public IReadOnlyList<string> Lines { get; }

    public SignGump(int gumpShape, IReadOnlyList<string> lines)
    {
        GumpShape = gumpShape;
        Lines = lines;
    }

    /// <summary>Exult <c>Sign_gump::paint</c>'s font: embossed on the gold plaque, engraved on the tombstone, else the plain runes.</summary>
    public int Font => GumpShape switch
    {
        GoldSign => 6,
        Tombstone => 3,
        _ => 1
    };

    /// <summary>Exult <c>Sign_gump</c>'s object area for the text (x, y, w, h from the gump's position); the whole frame otherwise.</summary>
    public (int X, int Y, int W, int H) TextArea(FrameInfo frame) => GumpShape switch
    {
        WoodSign => (0, 4, 196, 92),
        Tombstone => (0, 8, 200, 112),
        GoldSign => (0, 4, 232, 96),
        Scroll => (48, 30, 146, 118),
        _ => (-frame.XLeft, -frame.YAbove, frame.Width, frame.Height)
    };

    /// <summary>
    /// Exult <c>Sign_gump::add_text</c> for an avatar who can read runes (the
    /// read flag): the rune ligatures spelled out (TH, EE, NG, EA, ST), the
    /// word gap a space, all in capitals, the rune fonts' letters.
    /// </summary>
    public static string Readable(string text)
    {
        var sb = new StringBuilder(text.Length + 8);
        foreach (var ch in text)
        {
            sb.Append(ch switch
            {
                '(' => "TH",
                ')' => "EE",
                '*' => "NG",
                '+' => "EA",
                ',' => "ST",
                '|' => " ",
                _ => char.ToUpperInvariant(ch).ToString()
            });
        }

        return sb.ToString();
    }

    /// <summary>
    /// The lines in letters, one after the other, for the translation shown
    /// beneath the sign (the user's pick); null when they hold no runes (the
    /// Fellowship's signs are in capitals, and so is all for an avatar who
    /// can read runes).
    /// </summary>
    public string? Translation
    {
        get
        {
            var readable = Lines.Select(Readable).ToList();
            if (readable.SequenceEqual(Lines))
            {
                return null;
            }

            return string.Join(" ", readable.Select(l => l.Trim()).Where(l => l.Length > 0));
        }
    }

    /// <summary>
    /// Paint at the gump's position (its hotspot): the shape, then the lines
    /// a line height apart with equal space round them (Exult <c>paint</c>).
    /// </summary>
    public void Paint(GumpView view, int x, int y, FrameInfo frame)
    {
        view.DrawGumpShape(GumpShape, 0, x, y);
        var font = VgaFont.Get(Font);
        var area = TextArea(frame);
        var lheight = font.TextHeight;
        var lspace = (area.H - Lines.Count * lheight) / (Lines.Count + 1);
        var ypos = y + area.Y;
        foreach (var line in Lines)
        {
            ypos += lspace;
            if (line.Length == 0)
            {
                continue;
            }

            view.DrawFontText(font, line, x + area.X + (area.W - font.TextWidth(line, 0, line.Length)) / 2, ypos);
            ypos += lheight;
        }
    }
}
