using U7.Core;
using U7.Data;

namespace U7.Actors;

/// <summary>
/// Exult <c>Projectile_effect</c>: a missile in flight. As in Exult it is an
/// effect, not an object on the map: <see cref="CombatEngine"/> steps it
/// every half tick and the world paints it after the map.
/// </summary>
public sealed class Missile
{
    /// <summary>Who shot it: an actor, a missile egg, or null (a returning weapon on its way back, an egg's shot at the party).</summary>
    public U7Object? Attacker;
    /// <summary>What it flies at (the thrower, on the way back); null for an egg's shot in a direction until it runs into something.</summary>
    public U7Object? Target;
    public WeaponRecord? Weapon;
    public int WeaponShape;
    /// <summary>Exult <c>projectile_shape</c>: the ammunition, for its ammo.csv entry.</summary>
    public int AmmoShape;
    /// <summary>The shape it flies as (and is dropped or comes back as), or -1.</summary>
    public int SpriteShape;
    /// <summary>The frame shown, or -1 when Exult does not draw it (<c>skip_render</c>).</summary>
    public int Frame = -1;
    public int AttVal;
    /// <summary>Tiles per step.</summary>
    public int Speed = 4;
    public bool AutoHit;
    /// <summary>Exult <c>no_blocking</c> (weapon or ammo): a shot without a target flies through what is in its way.</summary>
    public bool NoBlocking;
    public bool Returns;
    /// <summary>Exult <c>return_path</c>: a returning weapon on its way back.</summary>
    public bool ReturnPath;
    /// <summary>The tiles still ahead in <see cref="Path"/> start at <see cref="Step"/>.</summary>
    public List<TileCoord> Path = new();
    public int Step;
    /// <summary>Exult <c>pos</c>: the tile it is at.</summary>
    public TileCoord Pos;
    /// <summary>Seconds since its last step.</summary>
    public double Timer;
    /// <summary>Seconds between steps.</summary>
    public double Interval;

    /// <summary>The tile its next step takes it to (where it is, once its path is used up).</summary>
    public TileCoord Next => Step < Path.Count ? Path[Math.Min(Step + Speed, Path.Count) - 1] : Pos;

    /// <summary>How far it is through the wait for its next step, 0 to 1.</summary>
    public double Fraction => Interval > 0 ? Math.Clamp(Timer / Interval, 0, 1) : 0;
}
