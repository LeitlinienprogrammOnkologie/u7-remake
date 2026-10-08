using U7.Core;
using U7.Data;

namespace U7.Actors;

/// <summary>
/// Exult <c>Homing_projectile</c>: the death vortex or energy mist a homing
/// missile becomes where it lands. A SPRITES.VGA animation (the weapon's
/// explosion sprite) that drifts a tile at a time after its target for 20
/// seconds and hurts everyone outside the party under it once a second.
/// <see cref="CombatEngine"/> steps it every 100 ms; the world paints it
/// after the map, where Exult's <c>paint</c> does.
/// </summary>
public sealed class HomingMissile
{
    public const int StepMs = 100;
    public const double StepSeconds = StepMs / 1000.0;
    public const int LifeMs = 20_000;

    /// <summary>The weapon shape (Exult's <c>weapon</c>), for the damage and the sprite.</summary>
    public int Weapon;
    public U7Object? Attacker;
    /// <summary>The actor it follows; null while it looks for another.</summary>
    public U7Object? Target;
    /// <summary>No actor to follow: it parks at <see cref="Dest"/>.</summary>
    public bool Stationary;
    public TileCoord Dest;
    /// <summary>Exult <c>pos</c>: the tile it is at.</summary>
    public TileCoord Pos;
    /// <summary>Where it was before its last step, for the picture.</summary>
    public TileCoord PrevPos;
    public int Sprite;
    public int Frame;
    public int Frames;
    /// <summary>Milliseconds since it began, in whole steps.</summary>
    public int AgeMs;
    /// <summary>It hurts what is under it on its first step after this age (Exult <c>next_damage_time</c>).</summary>
    public int NextDamageMs;
    /// <summary>Seconds since its last step.</summary>
    public double Timer;

    /// <summary>How far it is through the wait for its next step, 0 to 1.</summary>
    public double Fraction => Math.Clamp(Timer / StepSeconds, 0, 1);
}
