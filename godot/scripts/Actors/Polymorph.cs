using U7.Core;
using U7.Data;

namespace U7.Actors;

/// <summary>
/// Exult's polymorph (actors.cc): an actor turned into another shape keeps
/// its own in <see cref="U7Object.ShapeSave"/> and carries the polymorph flag
/// (Serpent Isle's snake amulet, Petra in the avatar's body).
/// </summary>
public static class Polymorph
{
    /// <summary>
    /// Exult <c>Actor::set_polymorph</c>: the avatar's shape (either sex)
    /// means the avatar's own body, given its skin; the actor's own shape
    /// turns it back; anything else changes it, standing the way it faces.
    /// </summary>
    public static void Set(GameMap map, U7Object actor, U7Object avatar, int shape)
    {
        if (shape == AvatarLook.Shape(false) || shape == AvatarLook.Shape(true))
        {
            shape = AvatarLook.BodyShape(avatar);
            if (actor == avatar)
            {
                actor.ShapeSave = shape; // Revert to the default unpolymorphed shape.
            }
        }

        if (shape == actor.ShapeSave)
        {
            map.SetShape(actor, actor.ShapeSave);
            actor.ShapeSave = -1;
            actor.ClearFlag(ObjFlag.Polymorph);
            return;
        }

        if (actor.ShapeSave == -1)
        {
            actor.ShapeSave = actor.Shape;
        }

        // Exult: "Taking a guess for SI amulet": standing the way it faces.
        map.SetShape(actor, shape);
        map.SetFrame(actor, ActorWalker.DirFrame(ActorWalker.FacingOfFrame(actor.Frame), 0));
        actor.SetFlag(ObjFlag.Polymorph);
    }

    /// <summary>
    /// Exult <c>Actor::set_polymorph_default</c>, for a new game's NPCs with
    /// the polymorph flag (in Serpent Isle only the avatar and Petra, 28):
    /// Petra in the avatar's body, an avatar with the Petra flag as the
    /// automaton (658), else the snake (530).
    /// </summary>
    public static void SetDefault(GameMap map, U7Object actor, U7Object avatar)
    {
        if (!actor.GetFlag(ObjFlag.Polymorph) || (actor.NpcNum != 0 && GameContext.IsSi && actor.NpcNum != 28))
        {
            return;
        }

        AvatarLook.SetActorShape(map, actor);
        actor.ShapeSave = actor.Shape;
        Set(map, actor, avatar, actor.NpcNum == 28 ? AvatarLook.Shape(false) : actor.GetFlag(ObjFlag.Petra) ? 658 : 530);
    }

    /// <summary>Exult <c>Actor::get_shape_real</c> for the record: the actor's own shape under a polymorph.</summary>
    public static int RealShape(U7Object actor) => actor.ShapeSave >= 0 ? actor.ShapeSave : actor.Shape;
}
