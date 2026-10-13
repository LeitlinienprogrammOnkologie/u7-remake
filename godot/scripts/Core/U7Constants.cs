namespace U7.Core;

/// <summary>
/// World geometry copied from Exult <c>exult_constants.h</c>.
/// Ultima VII is a 3072×3072 tile plane of 8×8 pixel cells, grouped into
/// 16×16-tile chunks and 16×16-chunk superchunks.
/// </summary>
public static class U7Constants
{
    public const int TileSize = 8;
    public const int TilesPerChunk = 16;
    public const int ChunkSizePixels = TilesPerChunk * TileSize; // 128
    public const int ChunksPerSuperchunk = 16;
    public const int TilesPerSuperchunk = TilesPerChunk * ChunksPerSuperchunk; // 256
    public const int SuperchunksPerSide = 12;
    public const int EggShape = 275;
    public const int EggShapeAlt = 200;
    public const int PathEggFrame = 6;
    public const int NumChunks = SuperchunksPerSide * ChunksPerSuperchunk; // 192
    public const int NumTiles = TilesPerChunk * NumChunks; // 3072
    public const int NumSuperchunks = SuperchunksPerSide * SuperchunksPerSide; // 144
    public const int MaxShapes = 1024;
    public const int TileShapeCount = 150; // shapes 0-149 are 8×8 flats
    public const int StandardDelayMs = 200;
    /// <summary>Exult <c>Text_effect</c>: a bark shows at once and goes at its tenth event, a standard delay apart ("about 1-2 seconds").</summary>
    public const int TextEffectMs = 9 * StandardDelayMs;
    public const int TicksPerMinute = 25;
    public const int NpcActivityDist = 32;
    public const int PathMaxNodes = 1024;
    public const int AvatarShape = 721;
    public const int StartTileX = 1079; // Trinsic, from INITGAME npc.dat
    public const int StartTileY = 2214;
    /// <summary>Debug: place the avatar at StartTile instead of the npc.dat position.</summary>
    public static readonly bool DebugStartOverride = false;
    public const int StartLift = 0;
    public const int LastGflag = 2047;
    public const int AnyShape = -359;
    public const int NoRoof = 255;
    public const int ShapeClassBuilding = 14;
    /// <summary>The original's screen in pixels, which Exult's full-screen sprites (the crystal ball's frame) fill.</summary>
    public const int OriginalScreenW = 320;
    public const int OriginalScreenH = 200;

    // GUMPS.VGA shapes by Exult's names, per game (GameShapes: bggame.cc, sigame.cc).
    public static int GumpBox => GameShapes.Get("gumps/box");
    public static int GumpCrate => GameShapes.Get("gumps/crate");
    public static int GumpCheck => GameShapes.Get("gumps/check");
    public static int GumpBarrel => GameShapes.Get("gumps/barrel");
    public static int GumpBag => GameShapes.Get("gumps/bag");
    public static int GumpBackpack => GameShapes.Get("gumps/backpack");
    public static int GumpBasket => GameShapes.Get("gumps/basket");
    public static int GumpChest => GameShapes.Get("gumps/chest");
    public static int GumpHalo => GameShapes.Get("gumps/halo");
    public static int GumpCombatMode => GameShapes.Get("gumps/combatmode");
    public static int GumpDisk => GameShapes.Get("gumps/disk");
    public static int GumpHeart => GameShapes.Get("gumps/heart");
    public static int GumpShipHold => GameShapes.Get("gumps/shipshold");
    public static int GumpDrawer => GameShapes.Get("gumps/drawer");
    public static int GumpBook => GameShapes.Get("gumps/book");
    public static int GumpScroll => GameShapes.Get("gumps/scroll");
    public static int GumpCombat => GameShapes.Get("gumps/combat");
    public static int GumpStats => GameShapes.Get("gumps/statsdisplay");
    public static int GumpBody => GameShapes.Get("gumps/body");
    /// <summary>Black Gate's <c>Actor_gump</c> (Exult names none): the ready spots' overlay and the figure; Serpent Isle always has paperdolls.</summary>
    public const int GumpSpotOverlay = 48;
    public const int GumpActorMale = 65;
    public const int StatsFont = 2;
    /// <summary>Exult <c>Text_gump</c>'s default font for books and scrolls.</summary>
    public const int BookFont = 4;
    public const int MaxQuantity = 100;

    public const int MoveWalk = 1 << 5;
    public const int MoveSwim = 1 << 6;
    public const int MoveFly = 1 << 4;

    public static int WrapTile(int v)
    {
        v %= NumTiles;
        if (v < 0)
        {
            v += NumTiles;
        }

        return v;
    }

    public static int WrapChunk(int v)
    {
        v %= NumChunks;
        if (v < 0)
        {
            v += NumChunks;
        }

        return v;
    }

    public static int TileDelta(int from, int to)
    {
        var diff = to - from;
        if (diff >= NumTiles / 2)
        {
            return diff - NumTiles;
        }

        if (diff <= -NumTiles / 2)
        {
            return diff + NumTiles;
        }

        return diff;
    }
}
