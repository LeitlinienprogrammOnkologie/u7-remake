using System.IO;
using System.Text;
using U7.Core;

namespace U7.Data;

/// <summary>A line laid out by <see cref="VgaFont.PaintTextBox"/>: its top-left corner and text.</summary>
public readonly record struct TextLine(int X, int Y, string Text);

/// <summary>
/// One FONTS.VGA font, Exult <c>Font</c>: glyph widths plus the font's
/// horizontal lead, line height and baseline from the tallest glyphs
/// (<c>calc_highlow</c>), and the word-wrapping text box.
/// </summary>
public sealed class VgaFont
{
    /// <summary>Exult <c>Fonts_vga_file::init</c>: horizontal lead of each font.</summary>
    static readonly int[] HorLeads = [-2, -1, 0, -1, 0, 0, -1, -2, -1, -1];
    static VgaShapeFile? _file;
    static readonly Dictionary<int, VgaFont> Fonts = new();

    readonly int[] _widths;

    /// <summary>FONTS.VGA, loaded once.</summary>
    public static VgaShapeFile File => _file ??= new VgaShapeFile(Path.Combine(U7Paths.StaticDir, "FONTS.VGA"));

    public int Number { get; }
    public int HorLead { get; }
    /// <summary>Exult <c>Font::highest</c>: the tallest glyph's extent above its hotspot.</summary>
    public int Highest { get; }
    /// <summary>Exult <c>Font::lowest</c>: the deepest glyph's extent below its hotspot.</summary>
    public int Lowest { get; }
    /// <summary>Exult <c>get_text_height</c>.</summary>
    public int TextHeight => Highest + Lowest + 1;
    /// <summary>Exult <c>get_text_baseline</c>: glyph hotspots sit this far below a line's top.</summary>
    public int Baseline => Highest;

    public static VgaFont Get(int number)
    {
        if (!Fonts.TryGetValue(number, out var font))
        {
            font = new VgaFont(number);
            Fonts[number] = font;
        }

        return font;
    }

    VgaFont(int number)
    {
        Number = number;
        HorLead = number < HorLeads.Length ? HorLeads[number] : 0;
        _widths = new int[File.FrameCount(number)];
        var unset = true;
        for (var i = 0; i < _widths.Length; i++)
        {
            var fi = File.Get(number, i);
            _widths[i] = fi.Width;
            if (unset)
            {
                unset = false;
                Highest = fi.YAbove;
                Lowest = fi.YBelow;
                continue;
            }

            Highest = Math.Max(Highest, fi.YAbove);
            Lowest = Math.Max(Lowest, fi.YBelow);
        }
    }

    /// <summary>Exult <c>Shape_file::get_frame</c> returns null past the last frame; such characters are skipped.</summary>
    public bool HasGlyph(char c) => c < _widths.Length;

    /// <summary>Exult <c>get_text_width</c> for one character: glyph width plus lead, 0 without a glyph.</summary>
    public int CharWidth(char c) => HasGlyph(c) ? _widths[c] + HorLead : 0;

    public int TextWidth(string text, int start, int length)
    {
        var width = 0;
        for (var i = start; i < start + length; i++)
        {
            width += CharWidth(text[i]);
        }

        return width;
    }

    static bool IsSpace(char c) => c is ' ' or '\n' or '\t';

    /// <summary>
    /// Exult <c>Font::paint_text_box</c> (no punctuation break, not centred),
    /// laying out <paramref name="text"/>[start, end) in a w×h box at (x, y):
    /// words wrap, '\n' starts a line, '^' capitalises the next letter and a
    /// '*' at a word start past the first line ends the box. The lines go to
    /// <paramref name="output"/> when given.
    /// </summary>
    /// <returns>The height used, or minus the offset (from start) where the box ran out of room.</returns>
    public int PaintTextBox(string text, int start, int end, int x, int y, int w, int h, int vertLead,
        List<TextLine>? output)
    {
        char At(int i) => i < end ? text[i] : '\0';

        var endx = x + w;
        var curx = x;
        var cury = y;
        var height = TextHeight + vertLead;
        var spaceWidth = CharWidth(' ');
        var maxLines = Math.Max(0, h / height);
        var lines = new StringBuilder[maxLines + 1];
        for (var i = 0; i < lines.Length; i++)
        {
            lines[i] = new StringBuilder();
        }

        var curLine = 0;
        var p = start;
        while (p < end)
        {
            switch (text[p])
            {
                case '\n':
                    curx = x;
                    p++;
                    curLine++;
                    cury += height;
                    if (curLine >= maxLines)
                    {
                        break;
                    }

                    continue;
                case '\r':
                    p++;
                    continue;
                case ' ':
                case '\t':
                {
                    var wrd = p;
                    while (At(wrd) is ' ' or '\t')
                    {
                        wrd++;
                    }

                    var sw = TextWidth(text, p, wrd - p);
                    if (sw <= 0)
                    {
                        sw = spaceWidth;
                    }

                    var nsp = spaceWidth > 0 ? sw / spaceWidth : 0;
                    lines[curLine].Append(' ', nsp);
                    curx += nsp * spaceWidth;
                    p = wrd;
                    break;
                }
            }

            if (curLine >= maxLines)
            {
                break;
            }

            if (At(p) == '*')
            {
                p++;
                if (curLine > 0)
                {
                    break;
                }
            }

            var ucaseNext = At(p) == '^';
            if (ucaseNext)
            {
                p++;
            }

            var ewrd = p;
            while (ewrd < end && text[ewrd] != '^' && (!IsSpace(text[ewrd]) || text[ewrd] is '\f' or '\v'))
            {
                ewrd++;
            }

            var width = ucaseNext && ewrd > p
                ? CharWidth(char.ToUpperInvariant(text[p])) + TextWidth(text, p + 1, ewrd - p - 1)
                : TextWidth(text, p, ewrd - p);
            if (curx + width - HorLead > endx)
            {
                if (ucaseNext)
                {
                    p--; // Put the '^' back.
                }

                curx = x;
                curLine++;
                cury += height;
                if (curLine >= maxLines)
                {
                    break;
                }
            }

            if (ucaseNext && p < end)
            {
                lines[curLine].Append(char.ToUpperInvariant(text[p]));
                p++;
            }

            if (ewrd > p)
            {
                lines[curLine].Append(text, p, ewrd - p);
            }

            curx += width;
            p = Math.Max(p, ewrd);
        }

        cury = y;
        for (var i = 0; i <= curLine && i < lines.Length; i++)
        {
            // Exult also paints the (empty) line after a full box, clipped away.
            if (i < maxLines)
            {
                output?.Add(new TextLine(x, cury, lines[i].ToString()));
            }

            cury += height;
        }

        return p < end ? -(p - start) : cury - y;
    }
}
