namespace U7.Data;

/// <summary>
/// Exult's breakable objects: <c>Game_object::get_obj_hp</c>,
/// <c>set_obj_hp</c> and <c>get_effective_obj_hp</c>, with Black Gate's
/// <c>shape_info.txt</c> <c>effective_hps</c> (copied here so the Exult tree
/// is not needed at runtime). Objects of the hit-point class and containers
/// keep their hit points in their quality; for the rest, and for those at
/// quality 0, the table decides; none means indestructible.
/// </summary>
public static class ObjectHitPoints
{
    const int ClassHasHp = 4, ClassContainer = 6;

    /// <summary>shapenum, frame, quality, hps; a frame or quality of -1 matches any.</summary>
    static readonly (int Shape, int Frame, int Quality, int Hps)[] Effective =
    [
        (522, -1, 0, 4), (522, -1, 255, 4), // Locked chest.
        (270, 0, 0, 6), (270, 1, 0, 6), (270, 2, 0, 6), (270, 8, 0, 6), (270, 9, 0, 6), (270, 10, 0, 6),
        (270, 16, 0, 6), (270, 17, 0, 6), (270, 18, 0, 6), // Vertical door.
        (376, 0, 0, 6), (376, 1, 0, 6), (376, 2, 0, 6), (376, 8, 0, 6), (376, 9, 0, 6), (376, 10, 0, 6),
        (376, 16, 0, 6), (376, 17, 0, 6), (376, 18, 0, 6), // Horizontal door.
        (432, 0, 0, 6), (432, 1, 0, 6), (432, 2, 0, 6), (432, 4, 0, 6), (432, 5, 0, 6), (432, 6, 0, 6), // Vertical door.
        (433, 0, 0, 6), (433, 1, 0, 6), (433, 2, 0, 6), (433, 4, 0, 6), (433, 5, 0, 6), (433, 6, 0, 6), // Horizontal door.
        (704, -1, 0, 4) // Powder keg.
    ];

    /// <summary>Exult <c>Has_hitpoints</c>: the hit-point class and containers (their "resistance") keep hit points in their quality.</summary>
    public static bool HasHitpoints(ShapeRecord rec) => rec.ShapeClass is ClassHasHp or ClassContainer;

    /// <summary>
    /// Exult <c>get_effective_obj_hp</c>: the object's own hit points, else
    /// the table's for its frame and quality (the quality ignored for a shape
    /// without one), exact first, then any quality, any frame, both; 0 is
    /// indestructible.
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
        return Find(obj.Shape, obj.Frame, quality) ?? (quality != -1 ? Find(obj.Shape, obj.Frame, -1) ?? Find(obj.Shape, -1, quality) : null) ??
            Find(obj.Shape, -1, -1) ?? 0;
    }

    /// <summary>Exult <c>set_obj_hp</c>: only where the quality holds them.</summary>
    public static void Set(U7Object obj, int hp, ShapeCatalog catalog)
    {
        if (HasHitpoints(catalog[obj.Shape]))
        {
            obj.Quality = hp;
        }
    }

    static int? Find(int shape, int frame, int quality)
    {
        foreach (var e in Effective)
        {
            if (e.Shape == shape && e.Frame == frame && e.Quality == quality)
            {
                return e.Hps;
            }
        }

        return null;
    }
}
