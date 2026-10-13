using U7.Data;

namespace U7.Actors;

/// <summary>
/// Which shapes are corpses and which corpse an actor's shape leaves: the
/// game's <c>bodies.txt</c> (Exult's <c>bodyshapes</c> and <c>bodylist</c>,
/// read into the shapes by <see cref="ShapeInfoText"/>).
/// </summary>
public static class Bodies
{
    /// <summary>Exult <c>Shape_info::is_body_shape</c>.</summary>
    public static bool IsBodyShape(int shape) => ShapeCatalog.Current[shape].IsBody;

    /// <summary>Exult <c>Shape_info::has_body_info</c> / <c>get_body_shape</c> / <c>get_body_frame</c>.</summary>
    public static bool TryGetBody(int npcShape, out int bodyShape, out int bodyFrame)
    {
        (bodyShape, bodyFrame) = ShapeCatalog.Current[npcShape].Body ?? (0, 0);
        return ShapeCatalog.Current[npcShape].Body is not null;
    }
}
