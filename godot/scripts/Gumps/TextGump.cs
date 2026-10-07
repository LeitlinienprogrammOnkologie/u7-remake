using U7.Core;
using U7.Data;

namespace U7.Gumps;

/// <summary>
/// Exult <c>Text_gump</c>, the base of books and scrolls. In book mode the
/// usecode's says are appended here (<see cref="AddText"/>) and shown a page,
/// or a pair of pages, at a time (<see cref="ShowNextPage"/>). '~' ends a
/// line and '*' a page. Never in the open-gump list: the usecode machine
/// owns it and <see cref="GumpView"/> paints it while a page waits for a click.
/// </summary>
public abstract class TextGump : Gump
{
    string _text = "";
    int _curtop;
    int _curend;
    readonly List<TextLine> _lines = new();

    public VgaFont Font { get; }
    /// <summary>The lines of the page(s) shown, relative to the gump's position.</summary>
    public IReadOnlyList<TextLine> Lines => _lines;

    protected TextGump(int gumpShape, int font)
        : base(null, 0, 0, gumpShape)
    {
        Font = VgaFont.Get(font);
    }

    bool IsScroll => GumpShape == U7Constants.GumpScroll;

    /// <summary>Exult <c>Text_gump::add_text</c>: append, starting a new line unless the text ended a page.</summary>
    public void AddText(string str)
    {
        if (_text.Length > 0 && _text[^1] != '*')
        {
            _text += "~";
        }

        _text += str;
    }

    /// <summary>Exult <c>Text_gump::show_next_page</c>: lay out the next page(s); false once everything was shown.</summary>
    public bool ShowNextPage()
    {
        if (_curend >= _text.Length)
        {
            return false;
        }

        _curtop = _curend;
        while (_curtop < _text.Length && _text[_curtop] == '~')
        {
            _curtop++;
        }

        _lines.Clear();
        _curend = LayoutPages(_curtop);
        return true;
    }

    /// <summary>The subclass's Exult <c>paint</c>: lay out its page(s) from <paramref name="top"/>; returns the end.</summary>
    protected abstract int LayoutPages(int top);

    /// <summary>
    /// Exult <c>Text_gump::paint_page</c>: lay out text from <paramref name="start"/>
    /// until the box is full. '~' ends a line and '*' (on Black Gate scrolls
    /// also " ~~") ends the page, except that a scroll carries on when the
    /// block after the break still fits with two lines to spare.
    /// </summary>
    /// <returns>The offset past the laid-out text.</returns>
    protected int PaintPage((int X, int Y, int W, int H) box, int start)
    {
        const int vlead = 1;
        var text = _text;
        var textlen = text.Length;
        var textheight = Font.TextHeight + vlead;
        var ypos = 0;
        var str = start;
        var pageBreakStar = -1;
        var extraBreak = -1;
        while (str < textlen && ypos + textheight <= box.H)
        {
            var lineBreak = text.IndexOf('~', str);
            pageBreakStar = text.IndexOf('*', str);
            if (IsScroll)
            {
                extraBreak = text.IndexOf(" ~~", str, StringComparison.Ordinal);
                // A " ~~" right after "~~" is not a break.
                if (extraBreak >= 2 && text[extraBreak - 2] == '~' && text[extraBreak - 1] == '~')
                {
                    extraBreak = -1;
                }
            }

            int epage;
            int eol;
            if (extraBreak >= 0 && lineBreak >= 0 && extraBreak + 1 == lineBreak)
            {
                epage = eol = extraBreak;
            }
            else if (pageBreakStar >= 0 && (lineBreak < 0 || pageBreakStar < lineBreak))
            {
                epage = eol = pageBreakStar;
            }
            else
            {
                eol = lineBreak;
                epage = -1;
            }

            if (eol < 0)
            {
                eol = textlen;
            }

            var eolchr = eol < textlen ? text[eol] : '\0';
            var endoff = Font.PaintTextBox(text, str, eol, box.X, box.Y + ypos, box.W, box.H - ypos, vlead, _lines);
            if (endoff <= 0)
            {
                // Out of room: the page ends where the box did.
                str += -endoff;
                break;
            }

            if (epage >= 0 && IsScroll)
            {
                var remaining = box.H - (ypos + endoff);
                if (remaining >= 2 * textheight)
                {
                    // Peek whether the next block fits in the rest, keeping two lines spare.
                    var available = remaining - 2 * textheight;
                    var skip = epage == extraBreak ? 3 : 1;
                    var earliest = textlen;
                    if ((pageBreakStar > eol + 1 && pageBreakStar < earliest) ||
                        (extraBreak > eol + skip && extraBreak < earliest))
                    {
                        earliest = pageBreakStar >= 0 && pageBreakStar < earliest ? pageBreakStar : extraBreak;
                    }

                    var peekStart = eol + skip;
                    var peekEnd = earliest >= peekStart ? earliest : textlen;
                    var peekHeight = Font.PaintTextBox(text, peekStart, peekEnd, box.X, -1000, box.W, available, vlead, null);
                    if (peekHeight > 0 && peekHeight <= available + 5)
                    {
                        ypos += endoff;
                        str = eol + (epage == extraBreak ? 2 : 1);
                        continue;
                    }

                    str = eol + skip;
                    break;
                }
            }
            else if (eolchr == '~' || lineBreak == eol)
            {
                eol++;
            }

            ypos += endoff;
            str = eol;
            if (str < textlen && text[str] == ' ')
            {
                str++;
            }
        }

        if (pageBreakStar >= 0 && pageBreakStar == str)
        {
            str++;
        }

        return str;
    }

    /// <summary>Exult <c>Gump::set_pos</c>: centre the gump on a screen of the given size.</summary>
    public void SetPos(int screenW, int screenH, FrameInfo frame)
    {
        X = (screenW - frame.Width) / 2 + frame.XLeft;
        Y = (screenH - frame.Height) / 2 + frame.YAbove;
    }

    public override void Paint(GumpView view)
    {
        view.DrawGumpShape(GumpShape, 0, X, Y);
        foreach (var line in _lines)
        {
            view.DrawFontText(Font, line.Text, X + line.X, Y + line.Y);
        }
    }
}

/// <summary>Exult <c>Book_gump</c>: text on two facing pages.</summary>
public sealed class BookGump : TextGump
{
    public BookGump(int font = U7Constants.BookFont)
        : base(U7Constants.GumpBook, font)
    {
    }

    protected override int LayoutPages(int top) =>
        PaintPage((173, 8, 125, 130), PaintPage((35, 8, 125, 130), top));
}

/// <summary>Exult <c>Scroll_gump</c>: one page of text.</summary>
public sealed class ScrollGump : TextGump
{
    public ScrollGump(int font = U7Constants.BookFont)
        : base(U7Constants.GumpScroll, font)
    {
    }

    protected override int LayoutPages(int top) => PaintPage((51, 31, 142, 118), top);
}
