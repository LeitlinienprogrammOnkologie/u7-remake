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

    // GUMPS.VGA indices from Exult bggame.cc (Black Gate).
    public const int GumpBox = 0;
    public const int GumpCrate = 1;
    public const int GumpCheck = 2;
    public const int GumpBarrel = 8;
    public const int GumpBag = 9;
    public const int GumpBackpack = 10;
    public const int GumpBasket = 11;
    public const int GumpChest = 22;
    public const int GumpHalo = 7;
    public const int GumpCombatMode = 12;
    public const int GumpDisk = 24;
    public const int GumpHeart = 25;
    public const int GumpShipHold = 26;
    public const int GumpDrawer = 27;
    public const int GumpCombat = 46;
    public const int GumpStats = 47;
    public const int GumpSpotOverlay = 48;
    public const int GumpBody = 53;
    public const int GumpActorMale = 65;
    public const int StatsFont = 2;
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
