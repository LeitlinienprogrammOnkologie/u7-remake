using Godot;
using U7.Actors;
using U7.Core;
using U7.UI;

namespace U7.Game;

/// <summary>
/// How fast the player walks the avatar, in milliseconds per step (Exult
/// <c>avatar_speed = 200 * std_delay / factor</c>): slow 400, medium 200,
/// fast 100, and 266 in combat or with a hostile nearby. There is no run
/// animation; only the step rate changes.
/// </summary>
public static class WalkSpeed
{
    // Exult Mouse::Avatar_Speed_Factors.
    const int Slow = 100;
    const int MediumCombat = 150;
    const int Medium = 200;
    const int Fast = 400;
    const int BaseSpeed = 200 * U7Constants.StandardDelayMs;

    /// <summary>
    /// Exult <c>Mouse::set_speed_cursor</c>: from how far the cursor is from
    /// the avatar (Chebyshev, in game pixels) within a square half the game
    /// window's size, but at least 200 pixels: under 0.4 of the way to its
    /// edge slow, under 0.8 medium, beyond fast. Combat caps it at medium
    /// combat speed; a hostile nearby or a no-halt avatar script rules out fast.
    /// The arrow (POINTERS.SHP) points from the avatar to the cursor, short,
    /// medium or long by the speed, red in combat (which has no long arrows).
    /// </summary>
    public static (int Arrow, int Speed) SpeedCursor(Vector2 avatar, Vector2 mouse, Vector2 gameSize, bool inCombat,
        bool hostileNearby, bool noHaltScript)
    {
        var dx = mouse.X - avatar.X;
        var dy = avatar.Y - mouse.Y;
        var dir = ActorWalker.DirectionNoWrap((int)dy, (int)dx);
        var minSide = (int)Math.Min(gameSize.X, gameSize.Y);
        var rectSize = Math.Max(Math.Min(200, minSide), minSide / 2);
        var half = rectSize / 2;
        var inRect = mouse.X >= avatar.X - half && mouse.X < avatar.X - half + rectSize &&
                     mouse.Y >= avatar.Y - half && mouse.Y < avatar.Y - half + rectSize;
        if (!inRect)
        {
            return inCombat ? (MouseShape.MediumCombatArrows + dir, BaseSpeed / MediumCombat)
                : hostileNearby || noHaltScript ? (MouseShape.MediumArrows + dir, BaseSpeed / Medium)
                : (MouseShape.LongArrows + dir, BaseSpeed / Fast);
        }

        var section = Math.Max(Math.Abs(dx), Math.Abs(dy)) / half;
        if (section < 0.4f)
        {
            return ((inCombat ? MouseShape.ShortCombatArrows : MouseShape.ShortArrows) + dir, BaseSpeed / Slow);
        }

        if (section < 0.8f || inCombat || hostileNearby || noHaltScript)
        {
            return ((inCombat ? MouseShape.MediumCombatArrows : MouseShape.MediumArrows) + dir,
                inCombat || hostileNearby ? BaseSpeed / MediumCombat : BaseSpeed / Medium);
        }

        return (MouseShape.LongArrows + dir, BaseSpeed / Fast);
    }

    /// <summary>
    /// Exult <c>get_walking_speed</c> (keyactions.cc): the arrow keys walk
    /// fast, with Shift medium; in combat or with a hostile nearby both are
    /// medium combat speed.
    /// </summary>
    public static int Keyboard(bool shift, bool inCombat, bool hostileNearby) =>
        inCombat || hostileNearby ? BaseSpeed / MediumCombat
        : shift ? BaseSpeed / Medium
        : BaseSpeed / Fast;
}
