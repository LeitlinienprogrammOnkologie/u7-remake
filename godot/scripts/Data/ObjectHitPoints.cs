namespace U7.Data;

/// <summary>
/// Exult's breakable objects: <c>Game_object::get_obj_hp</c>,
/// <c>set_obj_hp</c> and <c>get_effective_obj_hp</c>, with the game's
/// <c>shape_info.txt</c> <c>effective_hps</c>. Objects of the hit-point
/// class and containers keep their hit points in their quality; for the
/// rest, and for those at quality 0, the table decides; none means
/// indestructible.
/// </summary>
public static class ObjectHitPoints
{
    const int ClassHasHp = 4, ClassContainer = 6;

    /// <summary>Exult <c>Has_hitpoints</c>: the hit-point class and containers (their "resistance") keep hit points in their quality.</summary>
    public static bool HasHitpoints(ShapeRecord rec) => rec.ShapeClass is ClassHasHp or ClassContainer;

    /// <summary>
    /// Exult <c>get_effective_obj_hp</c>: the object's own hit points, else
    /// <c>Shape_info::get_effective_hps</c> for its frame and quality (the
    /// quality ignored for a shape without one); 0 is indestructible.
    /// </summary>
    public static int Get(U7Object obj, ShapeCatalog catalog)
    {
        var rec = catalog[obj.Shape];
        var hp = HasHitpoints(rec) ? obj.Quality : 0;
        if (hp != 0)
        {
            return hp;
        }

        var quality = rec.HasQuality ? obj.Quality : -1;
        return ShapeInfoText.Find(rec.EffectiveHps, obj.Frame, quality, e => (e.Frame, e.Quality))?.Value ?? 0;
    }

    /// <summary>Exult <c>set_obj_hp</c>: only where the quality holds them.</summary>
    public static void Set(U7Object obj, int hp, ShapeCatalog catalog)
    {
        if (HasHitpoints(catalog[obj.Shape]))
        {
            obj.Quality = hp;
        }
    }
}
