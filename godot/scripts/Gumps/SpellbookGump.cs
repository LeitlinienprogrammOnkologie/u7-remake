using U7.Actors;
using U7.Data;

namespace U7.Gumps;

/// <summary>
/// Exult <c>Spellbook_gump</c>: an open spellbook, a page per circle (Black
/// Gate's linear spells on the first), each spell's rune with how often the
/// reagents of the book's holder allow it, the circle's name, the page corners
/// and the bookmark ribbon. A click marks a spell, a double-click casts it.
/// </summary>
public sealed class SpellbookGump : Gump
{
    // GUMPS.VGA (Black Gate).
    public const int BookShape = 43;
    /// <summary>First of eight shapes, one per spell of a circle; the frame is the circle.</summary>
    const int SpellsShape = 33;
    const int TurningPageShape = 41;
    const int BookmarkShape = 42;
    const int LeftPageShape = 44;
    const int RightPageShape = 45;
    /// <summary>TEXT.FLX misc names: "Circle", then "First" to "Eighth".</summary>
    const int CircleName = 0x45;
    const int NumbersFont = 5;
    const int TurnFrameMs = 50;

    readonly VgaShapeFile _gumpsVga;
    readonly GumpButton _check;
    readonly GumpButton _leftPage;
    readonly GumpButton _rightPage;
    readonly GumpButton _bookmark;
    readonly GumpButton?[] _spells = new GumpButton?[Spellbook.SpellCount];
    readonly int[] _avail;
    readonly int _spellWidth;
    ulong _turnStart;
    /// <summary>Exult <c>turning_page</c>: 1 turning back, -1 forward, 0 not turning.</summary>
    int _turning;

    /// <summary>The circle shown (Exult <c>page</c>).</summary>
    public int Page { get; private set; }

    public U7Object Book => Owner!;

    public SpellbookGump(U7Object book, int x, int y, VgaShapeFile gumpsVga, int[] avail)
        : base(book, x, y, BookShape)
    {
        _gumpsVga = gumpsVga;
        _avail = avail;
        SetObjectArea(new GumpArea(36, 28, 102, 66, 7, 54));
        _check = Buttons[0];
        if (book.SpellBookmark >= 0)
        {
            Page = book.SpellBookmark / 8;
        }

        _leftPage = new GumpButton(this, GumpButtonKind.SpellPage, 43, 25, LeftPageShape) { Index = -1, Frame = 0 };
        _rightPage = new GumpButton(this, GumpButtonKind.SpellPage, 137, 25, RightPageShape) { Index = 1, Frame = 0 };
        _bookmark = new GumpButton(this, GumpButtonKind.SpellBookmark, 0, 0, BookmarkShape) { Frame = 0 };
        var spell0 = gumpsVga.Get(SpellsShape, 0);
        _spellWidth = spell0.Width;
        SetBookmark();
        var vertspace = (ObjectArea.H - 4 * spell0.Height) / 4;
        for (var spell = 0; spell < Spellbook.SpellCount; spell++)
        {
            if (!Spellbook.HasSpell(book, spell))
            {
                continue;
            }

            var s = spell % 8;
            _spells[spell] = new GumpButton(
                this, GumpButtonKind.Spell,
                s < 4 ? ObjectArea.X + spell0.XLeft + 1 : ObjectArea.X + ObjectArea.W - spell0.XRight - 2,
                ObjectArea.Y + spell0.YAbove + (spell0.Height + vertspace) * (s % 4),
                SpellsShape + s) { Index = spell, Frame = spell / 8 };
        }

        UpdateButtons();
    }

    /// <summary>The buttons in Exult's <c>on_button</c> order, last tested first: checkmark, page corners, spells, bookmark.</summary>
    void UpdateButtons()
    {
        Buttons.Clear();
        Buttons.Add(_bookmark);
        for (var s = 7; s >= 0; s--)
        {
            if (_spells[Page * 8 + s] is { } spell)
            {
                Buttons.Add(spell);
            }
        }

        Buttons.Add(_rightPage);
        Buttons.Add(_leftPage);
        Buttons.Add(_check);
    }

    /// <summary>The spells on the page shown, with what the reagents allow.</summary>
    public IEnumerable<(int Spell, int Available)> PageSpells()
    {
        for (var s = 0; s < 8; s++)
        {
            if (_spells[Page * 8 + s] is not null)
            {
                yield return (Page * 8 + s, _avail[Page * 8 + s]);
            }
        }
    }

    /// <summary>Exult <c>Bookmark_button::set</c>: beside the marked spell on its page, else at the edge towards it.</summary>
    void SetBookmark()
    {
        var mark = Book.SpellBookmark;
        var markPage = mark / 8;
        var s = mark % 8;
        var left = markPage == Page ? s < 4 : markPage < Page;
        var shape = _gumpsVga.Get(BookmarkShape, _bookmark.Frame);
        _bookmark.X = (left ? ObjectArea.X + _spellWidth / 2 : ObjectArea.X + ObjectArea.W - _spellWidth / 2 - 2) + shape.XLeft;
        _bookmark.Y = ObjectArea.Y - 14 + shape.YAbove;
        _bookmark.Frame = markPage == Page ? 1 + s % 4 : 0;
    }

    /// <summary>
    /// Exult <c>Spellbook_gump::change_page</c>. The page turns at once; the
    /// turning-page animation is painted over it (Exult swaps the page halfway
    /// through, blocking the game while it plays).
    /// </summary>
    public void ChangePage(int delta)
    {
        if (delta == 0 || (delta > 0 && Page == 8) || (delta < 0 && Page == 0))
        {
            return;
        }

        _turning = delta > 0 ? -1 : 1;
        _turnStart = Godot.Time.GetTicksMsec();
        Page = Math.Clamp(Page + delta, 0, 8);
        SetBookmark();
        UpdateButtons();
    }

    /// <summary>Exult <c>Bookmark_button::activate</c>: turn to the bookmark's page.</summary>
    public void TurnToBookmark()
    {
        var markPage = Book.SpellBookmark / 8;
        if (Book.SpellBookmark >= 0 && markPage != Page)
        {
            ChangePage(markPage - Page);
        }
    }

    /// <summary>Exult <c>Spellbook_gump::select_spell</c>: move the bookmark to a spell the book has.</summary>
    public void SelectSpell(int spell)
    {
        if (spell is >= 0 and < Spellbook.SpellCount && _spells[spell] is not null)
        {
            Book.SpellBookmark = spell;
            SetBookmark();
        }
    }

    /// <summary>
    /// Exult <c>Spellbook_gump::do_spell</c>: the avatar casts it, closing the
    /// gumps so the spell's animations can play. False if it cannot, and the
    /// red X cursor flashes.
    /// </summary>
    public bool DoSpell(int spell)
    {
        var caster = Manager.Avatar;
        if (!Spellbook.CanDoSpell(Book, caster, spell, Manager.Quantities))
        {
            Manager.FlashMouse?.Invoke(U7.UI.MouseShape.RedX);
            return false;
        }

        Close();
        Spellbook.DoSpell(Book, caster, spell, true, Manager.Quantities, (fun, item) => Manager.CastSpell?.Invoke(fun, item));
        Manager.CloseAll();
        return true;
    }

    public override void Paint(GumpView view)
    {
        view.DrawGumpShape(GumpShape, 0, X, Y);
        _check.Paint(view);
        if (Page > 0)
        {
            _leftPage.Paint(view);
        }

        if (Page < 8)
        {
            _rightPage.Paint(view);
        }

        var font = VgaFont.Get(NumbersFont);
        for (var s = 0; s < 8; s++)
        {
            if (_spells[Page * 8 + s] is not { } spell)
            {
                continue;
            }

            spell.Paint(view);
            if (Page == 0)
            {
                continue; // No quantities for Black Gate's linear spells.
            }

            var num = _avail[Page * 8 + s];
            var text = num <= 0 ? "" : num >= 1000 ? "999" : num.ToString();
            view.DrawFontText(font, text, X + spell.X + 1 - font.TextWidth(text, 0, text.Length), Y + spell.Y - 4);
        }

        if (Page > 0)
        {
            var number = TextMessages.MiscName(CircleName + Page);
            var circle = TextMessages.MiscName(CircleName);
            view.DrawFontText(font, number, X + 40 + (44 - font.TextWidth(number, 0, number.Length)) / 2, Y + 20);
            view.DrawFontText(font, circle, X + 92 + (44 - font.TextWidth(circle, 0, circle.Length)) / 2, Y + 20);
        }

        if (Book.SpellBookmark >= 0)
        {
            _bookmark.Paint(view);
        }

        PaintTurningPage(view);
    }

    void PaintTurningPage(GumpView view)
    {
        if (_turning == 0)
        {
            return;
        }

        var frames = _gumpsVga.FrameCount(TurningPageShape);
        var step = (int)((Godot.Time.GetTicksMsec() - _turnStart) / TurnFrameMs);
        if (step >= frames)
        {
            _turning = 0;
            return;
        }

        var frame = _turning == 1 ? step : frames - 1 - step;
        var fi = _gumpsVga.Get(TurningPageShape, frame);
        view.DrawGumpShape(TurningPageShape, frame, X + ObjectArea.X + fi.XLeft + 5, Y + fi.YAbove + 3);
    }
}
