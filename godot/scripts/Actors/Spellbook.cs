using U7.Core;
using U7.Data;

namespace U7.Actors;

/// <summary>
/// Exult <c>Spellbook_object</c>: a book's spells (a bit per spell in each of
/// nine circles, Black Gate's linear spells first), what a spell needs (mana
/// by circle, the caster's level, a reagent of each kind it takes) and casting
/// it (usecode 0x640 + spell).
/// </summary>
public static class Spellbook
{
    public const int Reagents = 842;
    public const int SpellCount = 9 * 8;
    /// <summary>Exult <c>BaseSpellsUsecode</c>.</summary>
    public const int BaseSpellsUsecode = 0x640;

    // Reagent bits match the reagent's frame.
    const int Bp = 1; // Black pearl.
    const int Bm = 2; // Blood moss.
    const int Ns = 4; // Nightshade.
    const int Mr = 8; // Mandrake root.
    const int Gr = 16; // Garlic.
    const int Gn = 32; // Ginseng.
    const int Ss = 64; // Spider silk.
    const int Sa = 128; // Sulphurous ash.
    const int ReagentKinds = 11;

    /// <summary>Exult <c>Spellbook_object::bg_reagents</c>.</summary>
    static readonly int[] BgReagents =
    [
        0, 0, 0, 0, 0, 0, 0, 0, // Linear spells require no reagents.
        // Circle 1:
        Gr | Gn | Mr, Gr | Gn, Ns | Ss, Gr | Ss, Sa | Ss, Sa, Ns, Gr | Gn,
        // Circle 2:
        Bm | Sa, Bp | Mr, Bp | Sa, Mr | Sa, Gr | Gn | Mr, Gr | Gn | Sa,
        Bp | Bm | Mr, Bm | Ns | Mr | Sa | Bp | Ss,
        // Circle 3:
        Gr | Ns | Sa, Gr | Gn | Ss, Ns | Mr | Bm, Sa | Gn | Gr | Mr, Ns | Ss,
        Ns | Mr, Bp | Ns | Bm, Ns | Ss | Bp,
        // Circle 4:
        Ss | Mr, Bp | Sa | Mr, Mr | Bp | Bm, Gr | Mr | Ns | Sa, Mr | Bp | Bm,
        Bm | Sa, Bm | Mr | Ns | Ss | Sa, Bm | Sa,
        // Circle 5:
        Bp | Ns | Ss, Mr | Gr | Bm, Gr | Bp | Sa | Ss, Bm | Bp | Mr | Sa,
        Gn | Ss | Mr | Gr, Ns | Bm, Bp | Sa | Ss, Gn | Ns | Ss,
        // Circle 6:
        Gr | Mr | Ns, Sa | Ss | Bm | Gn | Ns | Mr, Bp | Mr | Ss | Sa,
        Sa | Bp | Bm, Mr | Ns | Sa | Bm, Ns | Ss | Bp, Gn | Ss | Bp,
        Bm | Sa | Mr,
        // Circle 7:
        Mr | Ss, Bp | Ns | Sa, Bm | Bp | Mr | Ss | Sa, Bp | Mr | Ss | Sa,
        Bm | Mr | Ns | Sa, Bp | Ns | Ss | Mr, Bp | Gn | Mr, Gr | Gn | Mr | Ss,
        // Circle 8:
        Bp | Bm | Gr | Gn | Mr | Ns | Ss | Sa, Bm | Mr | Ns | Sa,
        Gr | Gn | Mr | Ns | Bm, Mr | Ns | Bm | Bp, Gr | Gn | Ss | Sa,
        Bm | Gr | Mr, Bp | Mr | Ns, Bm | Gr | Mr
    ];

    public static bool HasSpell(U7Object book, int spell) =>
        spell is >= 0 and < SpellCount && book.SpellCircles is { } c && (c[spell / 8] & (1 << (spell % 8))) != 0;

    /// <summary>Exult <c>Spellbook_object::can_do_spell</c>: the spell, mana for its circle, the level, the reagents.</summary>
    public static bool CanDoSpell(U7Object book, U7Object caster, int spell, ItemQuantity quantities)
    {
        if (!HasSpell(book, spell))
        {
            return false;
        }

        var circle = spell / 8;
        if (caster.GetProp(ActorProp.Mana) < circle || caster.GetLevel() < circle)
        {
            return false;
        }

        var flags = BgReagents[spell];
        for (var r = 0; flags != 0; r++, flags >>= 1)
        {
            if ((flags & 1) != 0 && quantities.Count(caster, Reagents, U7Constants.AnyShape, r) == 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Exult <c>Spellbook_object::do_spell</c>: pay the mana and a reagent of
    /// each kind, then run the spell's usecode as a double-click on the caster.
    /// False if it cannot be cast.
    /// </summary>
    public static bool DoSpell(U7Object book, U7Object caster, int spell, bool canDo, ItemQuantity quantities,
        Action<int, U7Object> runUsecode)
    {
        if (!canDo && !CanDoSpell(book, caster, spell, quantities))
        {
            return false;
        }

        caster.SetProp(ActorProp.Mana, caster.GetProp(ActorProp.Mana) - spell / 8);
        var flags = BgReagents[spell];
        for (var r = 0; flags != 0; r++, flags >>= 1)
        {
            if ((flags & 1) != 0)
            {
                quantities.Remove(caster, 1, Reagents, U7Constants.AnyShape, r);
            }
        }

        // (Exult also shows the casting frames, shape 859, in the caster's hand.)
        runUsecode(BaseSpellsUsecode + spell, caster);
        return true;
    }

    /// <summary>
    /// Exult <c>Spellbook_gump::set_avail</c>: for each spell, how many times
    /// the reagents of the book's owner allow it (none when nobody holds it).
    /// </summary>
    public static int[] Available(U7Object book, ItemQuantity quantities)
    {
        var avail = new int[SpellCount];
        var owner = book;
        while (owner.Container is { } outer)
        {
            owner = outer;
        }

        if (owner == book)
        {
            return avail;
        }

        var counts = new int[ReagentKinds];
        for (var r = 0; r < ReagentKinds; r++)
        {
            counts[r] = quantities.Count(owner, Reagents, U7Constants.AnyShape, r);
        }

        for (var i = 0; i < SpellCount; i++)
        {
            avail[i] = 10000;
            var flags = BgReagents[i];
            for (var r = 0; flags != 0; r++, flags >>= 1)
            {
                if ((flags & 1) != 0 && counts[r] < avail[i])
                {
                    avail[i] = counts[r];
                }
            }
        }

        return avail;
    }
}
