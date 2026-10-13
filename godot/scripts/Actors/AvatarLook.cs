using U7.Data;

namespace U7.Actors;

/// <summary>
/// The avatar's look by skin colour and sex, Exult's <c>avatar_data.txt</c>
/// (<see cref="AvatarData"/>): its body (<c>Actor::set_actor_shape</c>) and
/// face (<c>get_face_shape</c>'s face 0). Black Gate's own skin is the
/// default; Serpent Isle's bodies, when it is installed, give the others,
/// with Exult's faces. The sex is the actor's type flag <c>tf_sex</c>
/// (npc.dat's, inverted on a new game as Exult's <c>fix_first</c>).
/// </summary>
public static class AvatarLook
{
    public const int MaleShape = 721;
    public const int FemaleShape = 989;
    /// <summary>Exult <c>get_face_shape</c>'s shape 0: the avatar's face, whatever its frame.</summary>
    public const int FaceShape = 0;
    /// <summary>Exult <c>Actor::tf_sex</c>.</summary>
    public const int TypeFlagSex = 9;
    /// <summary>Exult's flags <c>tattooed</c> (SI's alternative face) and <c>naked</c> (the skin's bare body).</summary>
    public const int TattooedFlag = 33, NakedFlag = 38;

    static AvatarData? _data;
    static bool _serpentShapes;
    static HashSet<int> _imported = new();

    /// <summary>The skins and whether Serpent Isle's bodies are there (Exult <c>have_si_shapes</c>).</summary>
    public static void Use(ShapeCatalog catalog)
    {
        _data = catalog.Avatar;
        _serpentShapes = catalog.HasSerpentShapes;
        _imported = catalog.Files.SkinImports.Select(i => i.Dest).ToHashSet();
    }

    public static bool IsFemale(U7Object actor) => (actor.TypeFlags & (1 << TypeFlagSex)) != 0;

    public static void SetFemale(U7Object actor, bool female) =>
        actor.TypeFlags = female ? actor.TypeFlags | (1 << TypeFlagSex) : actor.TypeFlags & ~(1 << TypeFlagSex);

    /// <summary>Exult <c>GetMaleAvShape</c> / <c>GetFemaleAvShape</c>: the default avatar's shape of that sex.</summary>
    public static int Shape(bool female) =>
        _data is { } d && d.Defaults.TryGetValue(female, out var def) ? def.Shape : female ? FemaleShape : MaleShape;

    /// <summary>The skin a new game starts with (Exult <c>baseracesex</c>).</summary>
    public static int DefaultSkin => _data?.DefaultSkin ?? -1;

    /// <summary>
    /// Exult <c>GetSkinInfoSafe(skin, sex, have_si_shapes)</c>: the skin and
    /// sex's entry, else the default skin's; a skin made of Serpent Isle's
    /// bodies counts only when they are there.
    /// </summary>
    public static SkinData? Skin(int skin, bool female)
    {
        if (_data is null)
        {
            return null;
        }

        if (_data.Skin(skin, female) is { } sk && Usable(sk))
        {
            return sk;
        }

        return _data.Skin(_data.DefaultSkin, female);
    }

    static bool Usable(SkinData sk) =>
        _serpentShapes || (!_imported.Contains(sk.Shape) && !_imported.Contains(sk.NakedShape));

    /// <summary>The skins the new game offers for a sex, in order (Exult <c>ScrollSkins</c>: usable, not unselectable).</summary>
    public static IEnumerable<int> SelectableSkins(bool female) =>
        _data is null
            ? []
            : _data.Skins.Where(s => s.Female == female && Usable(s) && !_data.Unselectable.Contains(s.Skin))
                .Select(s => s.Skin).Distinct().OrderBy(s => s);

    /// <summary>
    /// Exult <c>get_face_shape</c>'s face 0: the avatar's skin's face (by
    /// <paramref name="female"/>, the speaker's sex when an NPC speaks as the
    /// avatar), its alternative when tattooed. Black Gate's own: FACES.VGA 0
    /// in the sex's frame.
    /// </summary>
    public static (int Shape, int Frame) Face(U7Object avatar, bool female) =>
        Skin(avatar.SkinColor, female) is { } sk
            ? avatar.GetFlag(TattooedFlag) ? (sk.AlterFaceShape, sk.AlterFaceFrame) : (sk.FaceShape, sk.FaceFrame)
            : (FaceShape, female ? 1 : 0);

    /// <summary>
    /// Exult <c>Shapeinfo_lookup::GetFaceReplacement</c>: while the avatar has
    /// the Petra flag (Serpent Isle's swapped bodies), the faces of
    /// <c>avatar_data.txt</c>'s <c>petra_face_table</c> show each other's.
    /// </summary>
    public static int FaceReplacement(U7Object avatar, int face) =>
        avatar.GetFlag(ObjFlag.Petra) && _data is { } d && d.PetraFaces.TryGetValue(face, out var other) ? other : face;

    /// <summary>A skin's face for the new game's picture (before there is an avatar to ask).</summary>
    public static (int Shape, int Frame) Face(int skin, bool female) =>
        Skin(skin, female) is { } sk ? (sk.FaceShape, sk.FaceFrame) : (FaceShape, female ? 1 : 0);

    /// <summary>The face of the skin whose body (or bare body) is that shape: the saved games' party faces.</summary>
    public static (int Shape, int Frame) FaceOfBody(int shape)
    {
        if (_data?.Skins.Find(s => s.Shape == shape || s.NakedShape == shape) is { } sk)
        {
            return (sk.FaceShape, sk.FaceFrame);
        }

        return (FaceShape, shape == FemaleShape ? 1 : 0);
    }

    /// <summary>Exult <c>GetSkinInfoSafe(avatar)</c>'s body: its skin and sex's shape, bare if naked; the default avatar's when the skin's bodies aren't there.</summary>
    public static int BodyShape(U7Object avatar)
    {
        var female = IsFemale(avatar);
        return Skin(avatar.SkinColor, female) is { } sk && Usable(sk)
            ? avatar.GetFlag(NakedFlag) ? sk.NakedShape : sk.Shape
            : Shape(female);
    }

    /// <summary>
    /// Exult <c>Actor::set_actor_shape</c>: the avatar with a skin colour,
    /// unless polymorphed (and not bare), takes its skin and sex's body, or
    /// bare body; the default avatar's when the skin's bodies aren't there.
    /// </summary>
    public static void SetActorShape(GameMap map, U7Object avatar)
    {
        if (avatar.NpcNum != 0 || avatar.SkinColor < 0 ||
            (avatar.GetFlag(ObjFlag.Polymorph) && !avatar.GetFlag(NakedFlag)))
        {
            return;
        }

        var shape = BodyShape(avatar);
        if (avatar.Shape != shape)
        {
            map.SetShape(avatar, shape);
        }
    }
}
