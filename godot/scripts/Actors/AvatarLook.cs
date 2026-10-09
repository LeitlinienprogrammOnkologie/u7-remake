using U7.Data;

namespace U7.Actors;

/// <summary>
/// The avatar's look by sex, Exult's <c>data/bg/avatar_data.txt</c> for the
/// original skin (its other skins need Exult's own face art): the shape
/// (<c>defaultshape</c>) and the face, FACES.VGA shape 0 in the sex's frame
/// (<c>multiracial_table</c>). The sex is the actor's type flag
/// <c>tf_sex</c> (npc.dat's, inverted on a new game as Exult's <c>fix_first</c>).
/// </summary>
public static class AvatarLook
{
    public const int MaleShape = 721;
    public const int FemaleShape = 989;
    /// <summary>Exult <c>get_face_shape</c>'s shape 0: the avatar's face, whatever its frame.</summary>
    public const int FaceShape = 0;
    /// <summary>Exult <c>Actor::tf_sex</c>.</summary>
    public const int TypeFlagSex = 9;

    public static bool IsFemale(U7Object actor) => (actor.TypeFlags & (1 << TypeFlagSex)) != 0;

    public static void SetFemale(U7Object actor, bool female) =>
        actor.TypeFlags = female ? actor.TypeFlags | (1 << TypeFlagSex) : actor.TypeFlags & ~(1 << TypeFlagSex);

    public static int Shape(bool female) => female ? FemaleShape : MaleShape;

    public static int FaceFrame(bool female) => female ? 1 : 0;

    /// <summary>Exult <c>Actor::set_actor_shape</c>: the avatar, unless polymorphed, takes its sex's shape.</summary>
    public static void SetActorShape(GameMap map, U7Object avatar)
    {
        if (avatar.NpcNum != 0 || avatar.GetFlag(ObjFlag.Polymorph))
        {
            return;
        }

        var shape = Shape(IsFemale(avatar));
        if (avatar.Shape != shape)
        {
            map.SetShape(avatar, shape);
        }
    }
}
