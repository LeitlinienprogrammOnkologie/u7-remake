using System.Text;
using U7.Core;
using U7.Data;

namespace U7.Gumps;

/// <summary>
/// Exult <c>Sign_gump</c>: a wooden sign, tombstone or gold plaque (the
/// game's <c>gumps/woodsign</c>, <c>tombstone</c>, <c>goldsign</c>; also its
/// scroll) with its lines spread evenly down the shape's text area, each
/// centred, in the sign's rune font (<c>display_runes</c>). The rune fonts
/// have runes for lower case and letters for capitals.
/// </summary>
public sealed class SignGump
{
    public int GumpShape { get; }
    public IReadOnlyList<string> Lines { get; }
    /// <summary>Exult's <c>serpentine</c>: Serpent Isle's serpent runes, for an avatar who can't read them.</summary>
    public bool Serpentine { get; }

    /// <summary>
    /// Exult <c>Sign_gump</c>'s constructor and <c>add_text</c>: in Serpent
    /// Isle shape 49 ("THIS IS A HACK") is the gold plaque in serpent runes
    /// unless the avatar can read them; an avatar who can read runes (the
    /// read flag) sees the other signs' lines in letters.
    /// </summary>
    public SignGump(int gumpShape, IReadOnlyList<string> lines, bool canRead)
    {
        if (GameContext.IsSi && gumpShape == 49 && !canRead)
        {
            Serpentine = true;
            gumpShape = GameShapes.Get("gumps/goldsign");
        }

        GumpShape = gumpShape;
        Lines = !Serpentine && canRead ? lines.Select(Readable).ToList() : lines;
        Translation = Serpentine ? null : Translate(Lines);
    }

    /// <summary>Exult <c>Sign_gump::paint</c>'s font: embossed on the gold plaque (serpent runes there 10, elsewhere 8), engraved on the tombstone, else the plain runes.</summary>
    public int Font =>
        GumpShape == GameShapes.Get("gumps/goldsign") ? Serpentine ? 10 : 6
        : Serpentine ? 8
        : GumpShape == GameShapes.Get("gumps/tombstone") ? 3
        : 1;

    /// <summary>Exult <c>Sign_gump</c>'s object area for the text (x, y, w, h from the gump's position; Serpent Isle's gold plaque is wider); the whole frame otherwise.</summary>
    public (int X, int Y, int W, int H) TextArea(FrameInfo frame) =>
        GumpShape == GameShapes.Get("gumps/woodsign") ? (0, 4, 196, 92)
        : GumpShape == GameShapes.Get("gumps/tombstone") ? (0, 8, 200, 112)
        : GumpShape == GameShapes.Get("gumps/goldsign") ? GameContext.IsSi ? (4, 4, 312, 96) : (0, 4, 232, 96)
        : GumpShape == GameShapes.Get("gumps/scroll") ? (48, 30, 146, 118)
        : (-frame.XLeft, -frame.YAbove, frame.Width, frame.Height);

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
    public string? Translation { get; }

    static string? Translate(IReadOnlyList<string> lines)
    {
        var readable = lines.Select(Readable).ToList();
        if (readable.SequenceEqual(lines))
        {
            return null;
        }

        return string.Join(" ", readable.Select(l => l.Trim()).Where(l => l.Length > 0));
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
