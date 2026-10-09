using Godot;
using U7.Actors;
using U7.Core;
using U7.Data;

namespace U7.World;

/// <summary>
/// Exult <c>Field_object</c>: fire, sleep and poison fields, caltrops and
/// campfires (<c>shape_info.txt</c>'s <c>field_type</c>) are eggs hatched by
/// any actor stepping onto their footprint at their lift (party footpad,
/// auto-reset), and an animated one also checks the actors standing in it now
/// and then (<c>Field_frame_animator</c>: one frame in ten, while on screen).
/// A sleep or poison field that takes effect is used up.
/// </summary>
public sealed class Fields
{
    const int Fire = 0, Sleep = 1, Poison = 2, Caltrops = 3, Campfire = 4;
    /// <summary>Exult <c>Weapon_data::fire_damage</c>.</summary>
    const int FireDamage = 1;

    readonly GameMap _map;
    readonly CombatEngine _combat;
    readonly NpcTimers _timers;
    readonly ScheduleRunner _runner;
    readonly Random _rng = new();
    /// <summary>Fields whose animator stopped checking (Exult <c>deactivate_animator</c>) until someone steps in.</summary>
    readonly HashSet<U7Object> _idle = new();
    double _animMs;

    public Fields(GameMap map, CombatEngine combat, NpcTimers timers, ScheduleRunner runner)
    {
        _map = map;
        _combat = combat;
        _timers = timers;
        _runner = runner;
    }

    /// <summary>Exult <c>Shape_info::get_field_type</c>, or -1.</summary>
    public static int TypeOf(int shape) => shape switch
    {
        895 => Fire,
        902 => Sleep,
        900 => Poison,
        756 => Caltrops,
        825 => Campfire,
        _ => -1
    };

    /// <summary>Exult <c>Actor::step</c>'s <c>activate_eggs</c> for the fields: those under the tile stepped onto, at its lift.</summary>
    public void Stepped(U7Object actor)
    {
        if (!actor.IsActor || actor.IsDead)
        {
            return;
        }

        foreach (var field in FieldsNear(actor.Tx, actor.Ty, 1).ToList())
        {
            if (!field.Removed && field.Tz == actor.Tz && Footprint(field).HasPoint(new Vector2I(actor.Tx, actor.Ty)))
            {
                Hatch(field, actor);
                if (actor.IsDead)
                {
                    return;
                }
            }
        }
    }

    /// <summary>Exult <c>Field_frame_animator</c>: the animated fields on screen check who stands in them.</summary>
    public void Update(double delta, bool frozen)
    {
        if (frozen)
        {
            return;
        }

        _animMs += delta * 1000;
        if (_animMs < U7Constants.StandardDelayMs)
        {
            return;
        }

        _animMs = 0;
        _idle.RemoveWhere(f => f.Removed);
        var view = _combat.ViewTiles;
        var centre = view.Position + view.Size / 2;
        var reach = Math.Max(view.Size.X, view.Size.Y) / 2 + 2;
        foreach (var field in FieldsNear(centre.X, centre.Y, reach).ToList())
        {
            if (!field.Removed && !_idle.Contains(field) && _map.Catalog[field.Shape].Animated &&
                view.Intersects(Footprint(field)) && _rng.Next(10) == 0)
            {
                Check(field);
            }
        }
    }

    /// <summary>Exult <c>Field_object::activate(npc_proximity)</c>.</summary>
    void Check(U7Object field)
    {
        _idle.Add(field);
        var foot = Footprint(field);
        foreach (var actor in _runner.NearbyNpcs().Append(_runner.Avatar).ToList())
        {
            if (field.Removed)
            {
                return;
            }

            if (!actor.IsDead && Distance(actor, field) <= 4 && Footprint(actor).Intersects(foot))
            {
                Hatch(field, actor);
            }
        }
    }

    /// <summary>Exult <c>Field_object::hatch</c>: apply it, and delete a used sleep or poison field.</summary>
    void Hatch(U7Object field, U7Object actor)
    {
        if (FieldEffect(field, actor))
        {
            _idle.Remove(field);
            _map.RemoveObject(field);
        }
    }

    /// <summary>Exult <c>Field_object::field_effect</c>: true to delete the field.</summary>
    bool FieldEffect(U7Object field, U7Object actor)
    {
        var del = false;
        switch (TypeOf(field.Shape))
        {
            case Poison:
                if (_rng.Next(2) != 0 && !actor.GetFlag(ObjFlag.Poisoned))
                {
                    _timers.SetFlag(actor, ObjFlag.Poisoned);
                    del = true;
                }

                break;
            case Sleep:
                if (_rng.Next(2) != 0 && !actor.GetFlag(ObjFlag.Asleep))
                {
                    _timers.SetFlag(actor, ObjFlag.Asleep);
                    del = true;
                }

                break;
            case Campfire when field.Frame == 0:
                // A burnt out campfire doesn't hurt.
                return false;
            case Fire:
            case Campfire:
                _combat.ReduceHealth(actor, 2 + _rng.Next(3), null, FireDamage);
                // But no sleeping here.
                if (actor.GetFlag(ObjFlag.Asleep) && actor.GetProp(ActorProp.Health) > 0)
                {
                    _timers.ClearFlag(actor, ObjFlag.Asleep);
                }

                break;
            case Caltrops:
                if (actor.GetProp(ActorProp.Intelligence) < _rng.Next(40))
                {
                    _combat.ReduceHealth(actor, 1 + _rng.Next(2), null);
                }

                return false;
        }

        if (!del)
        {
            _idle.Remove(field); // Tell the animator to keep checking.
        }

        return del;
    }

    IEnumerable<U7Object> FieldsNear(int tx, int ty, int dist)
    {
        var size = U7Constants.TilesPerChunk;
        for (var cy = (ty - dist - 3) / size; cy <= (ty + dist) / size; cy++)
        {
            for (var cx = (tx - dist - 3) / size; cx <= (tx + dist) / size; cx++)
            {
                foreach (var obj in _map.ObjectsInChunk(cx, cy))
                {
                    if (TypeOf(obj.Shape) >= 0 && obj.Container is null)
                    {
                        yield return obj;
                    }
                }
            }
        }
    }

    /// <summary>Exult <c>get_footprint</c>: the tiles under an object, its hot spot at the lower right.</summary>
    static Rect2I Footprint(U7Object obj) =>
        new(obj.Tx - Math.Max(1, obj.DimX) + 1, obj.Ty - Math.Max(1, obj.DimY) + 1, Math.Max(1, obj.DimX), Math.Max(1, obj.DimY));

    static int Distance(U7Object a, U7Object b) =>
        Math.Max(Math.Abs(U7Constants.TileDelta(a.Tx, b.Tx)), Math.Abs(U7Constants.TileDelta(a.Ty, b.Ty)));
}
