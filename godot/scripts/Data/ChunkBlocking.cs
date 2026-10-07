using U7.Core;

namespace U7.Data;

/// <summary>Exult movement flags (<c>exult_constants.h</c> MOVE_*), the same bits as an actor's type flags.</summary>
public static class MoveFlags
{
    public const int NoDrop = 1 << 3;
    public const int Fly = 1 << 4;
    public const int Levitate = Fly | NoDrop;
    public const int Walk = 1 << 5;
    public const int Swim = 1 << 6;
    public const int Ethereal = 1 << 7;
    public const int All = Fly | Walk | Swim | Ethereal;
}

/// <summary>
/// Exult <c>Chunk_cache</c>'s 'blocked' flags: per chunk, tile and lift, a
/// count (saturating at 3) of solid objects with height there. A solid
/// object counts in every chunk its footprint covers, not just the one it is
/// listed in. Built per chunk on first use and kept up to date as objects
/// enter and leave the world. Each tile also keeps its blocked lifts as bits
/// (Exult reads them the same way, <c>set_tflags</c>).
/// </summary>
public sealed class ChunkBlocking
{
    /// <summary>Lifts 0-31 (Exult grows to 255; Black Gate objects end below 24).</summary>
    public const int MaxLifts = 32;
    const int Tiles = U7Constants.TilesPerChunk * U7Constants.TilesPerChunk;
    readonly GameMap _map;
    readonly Chunk?[] _chunks = new Chunk?[U7Constants.NumChunks * U7Constants.NumChunks];

    sealed class Chunk
    {
        /// <summary>[lift * 256 + tile]: solid objects there, 0-3.</summary>
        public readonly byte[] Counts = new byte[MaxLifts * Tiles];
        /// <summary>[tile]: bit n set when lift n is blocked.</summary>
        public readonly uint[] Mask = new uint[Tiles];
    }

    public ChunkBlocking(GameMap map) => _map = map;

    static int Index(int cx, int cy) => cy * U7Constants.NumChunks + cx;

    Chunk Need(int cx, int cy)
    {
        var i = Index(cx, cy);
        if (_chunks[i] is { } chunk)
        {
            return chunk;
        }

        chunk = new Chunk();
        _chunks[i] = chunk;
        // An object's footprint runs west and north of its tile, so objects in
        // this chunk and the ones east and south of it can cover it.
        for (var dy = 0; dy <= 1; dy++)
        {
            for (var dx = 0; dx <= 1; dx++)
            {
                foreach (var obj in _map.ObjectsInChunk(cx + dx, cy + dy))
                {
                    if (IsObstacle(obj) && !obj.Removed && obj.Container is null)
                    {
                        Mark(chunk, cx, cy, obj, add: true);
                    }
                }
            }
        }

        return chunk;
    }

    /// <summary>Exult <c>Chunk_cache::update_object</c>: solid objects with height block; actors are left out.</summary>
    static bool IsObstacle(U7Object obj) => obj.Solid && obj.DimZ > 0 && !obj.IsActor && !obj.IsEgg;

    /// <summary>Add or remove an object in every built chunk its footprint covers.</summary>
    public void Update(U7Object obj, bool add)
    {
        if (!IsObstacle(obj))
        {
            return;
        }

        var cx0 = U7Constants.WrapTile(obj.Tx - obj.DimX + 1) / U7Constants.TilesPerChunk;
        var cy0 = U7Constants.WrapTile(obj.Ty - obj.DimY + 1) / U7Constants.TilesPerChunk;
        var cx1 = U7Constants.WrapTile(obj.Tx) / U7Constants.TilesPerChunk;
        var cy1 = U7Constants.WrapTile(obj.Ty) / U7Constants.TilesPerChunk;
        for (var y = 0; y < (cy1 == cy0 ? 1 : 2); y++)
        {
            for (var x = 0; x < (cx1 == cx0 ? 1 : 2); x++)
            {
                var cx = x == 0 ? cx0 : cx1;
                var cy = y == 0 ? cy0 : cy1;
                if (_chunks[Index(cx, cy)] is { } chunk)
                {
                    Mark(chunk, cx, cy, obj, add);
                }
            }
        }
    }

    /// <summary>Exult <c>Set_blocked_tile</c> / <c>Clear_blocked_tile</c> over the part of the footprint in this chunk.</summary>
    static void Mark(Chunk chunk, int cx, int cy, U7Object obj, bool add)
    {
        var baseX = cx * U7Constants.TilesPerChunk;
        var baseY = cy * U7Constants.TilesPerChunk;
        var top = Math.Min(MaxLifts, obj.Tz + obj.DimZ);
        for (var fy = 0; fy < obj.DimY; fy++)
        {
            var ty = U7Constants.TileDelta(baseY, obj.Ty - fy);
            if ((uint)ty >= U7Constants.TilesPerChunk)
            {
                continue;
            }

            for (var fx = 0; fx < obj.DimX; fx++)
            {
                var tx = U7Constants.TileDelta(baseX, obj.Tx - fx);
                if ((uint)tx >= U7Constants.TilesPerChunk)
                {
                    continue;
                }

                var tile = ty * U7Constants.TilesPerChunk + tx;
                for (var z = Math.Max(0, obj.Tz); z < top; z++)
                {
                    ref var count = ref chunk.Counts[z * Tiles + tile];
                    count = add ? (byte)Math.Min(3, count + 1) : (byte)Math.Max(0, count - 1);
                    if (count > 0)
                    {
                        chunk.Mask[tile] |= 1u << z;
                    }
                    else
                    {
                        chunk.Mask[tile] &= ~(1u << z);
                    }
                }
            }
        }
    }

    /// <summary>Exult <c>set_tflags</c>: the tile's blocked lifts, bit n for lift n.</summary>
    public uint Column(int tx, int ty)
    {
        tx = U7Constants.WrapTile(tx);
        ty = U7Constants.WrapTile(ty);
        var chunk = Need(tx / U7Constants.TilesPerChunk, ty / U7Constants.TilesPerChunk);
        return chunk.Mask[(ty % U7Constants.TilesPerChunk) * U7Constants.TilesPerChunk + tx % U7Constants.TilesPerChunk];
    }

    /// <summary>Exult <c>is_tile_occupied</c>: a solid object at this lift.</summary>
    public bool Test(int tx, int ty, int lift) => (uint)lift < MaxLifts && ((Column(tx, ty) >> lift) & 1) != 0;

    /// <summary>Exult <c>get_highest_blocked</c>: highest blocked lift below <paramref name="lift"/>, or -1.</summary>
    public static int HighestBlocked(uint column, int lift)
    {
        int i;
        for (i = Math.Min(lift, MaxLifts) - 1; i >= 0 && ((column >> i) & 1) == 0; i--)
        {
        }

        return i;
    }

    /// <summary>Exult <c>get_lowest_blocked</c>: lowest blocked lift at or above <paramref name="lift"/>, or -1.</summary>
    public static int LowestBlocked(uint column, int lift)
    {
        int i;
        for (i = Math.Max(0, lift); i < MaxLifts && ((column >> i) & 1) == 0; i++)
        {
        }

        return i >= MaxLifts ? -1 : i;
    }

    /// <summary>
    /// Exult <c>Check_terrain</c> for the chunk terrain at a tile: bit 0 land,
    /// bit 1 water, bit 2 a solid flat.
    /// </summary>
    int Terrain(int tx, int ty)
    {
        var info = _map.Catalog[_map.GetFlat(tx, ty).Shape];
        return info.Water ? 2 : info.Solid ? 4 : 1;
    }

    /// <summary>
    /// Exult <c>Chunk_cache::is_blocked</c>: can something <paramref name="height"/>
    /// lifts tall stand on (tx, ty) coming from <paramref name="lift"/>? It
    /// climbs at most <paramref name="maxRise"/> (walkers 1) onto the first
    /// free lift with headroom, or else drops at most <paramref name="maxDrop"/>
    /// onto what is below; at lift 0 the terrain decides between walkers,
    /// swimmers and flyers. When not blocked, <paramref name="newLift"/> is
    /// where it ends up.
    /// </summary>
    public bool IsBlocked(int height, int lift, int tx, int ty, out int newLift, int moveFlags,
        int maxDrop = 1, int maxRise = -1)
    {
        var canWalk = (moveFlags & MoveFlags.Walk) != 0;
        var canSwim = (moveFlags & MoveFlags.Swim) != 0;
        var canFly = (moveFlags & MoveFlags.Fly) != 0;
        var levitating = (moveFlags & MoveFlags.Levitate) != 0;
        if ((moveFlags & MoveFlags.Ethereal) != 0)
        {
            newLift = lift;
            return false;
        }

        if (maxRise == -1)
        {
            maxRise = canFly ? maxDrop : canWalk ? 1 : 0;
        }

        var column = Column(tx, ty);
        var maxLift = Math.Min(255, lift + maxRise);
        for (newLift = lift; newLift <= maxLift; newLift++)
        {
            if (newLift >= MaxLifts || ((column >> newLift) & 1) == 0)
            {
                var high = LowestBlocked(column, newLift);
                if (high == -1 || high >= newLift + height)
                {
                    break;
                }
            }
        }

        if (newLift > maxLift)
        {
            // Look downwards.
            newLift = HighestBlocked(column, lift) + 1;
            if (newLift >= lift)
            {
                return true;
            }

            var high = LowestBlocked(column, newLift);
            if (high != -1 && high < newLift + height)
            {
                return true;
            }
        }

        if (newLift <= lift)
        {
            // Not going up: see if falling.
            newLift = levitating ? lift : HighestBlocked(column, lift) + 1;
            if (lift - newLift > maxDrop)
            {
                return true;
            }

            var high = LowestBlocked(column, newLift);
            if (high != -1 && high < newLift + height)
            {
                return true;
            }
        }

        if (newLift == 0)
        {
            if (!canWalk && !canSwim && !canFly)
            {
                return true;
            }

            var ter = Terrain(tx, ty);
            if (canSwim && !canWalk && !canFly && (ter & 2) == 0)
            {
                return true; // Swimmers stay in water.
            }

            if (canWalk && !canSwim && !canFly && (ter & 2) != 0)
            {
                return true; // Walkers stay out of it.
            }

            return !canSwim && !canFly && (ter & 4) != 0;
        }

        return !canWalk && !canFly;
    }

    /// <summary>Exult <c>Map_chunk::is_blocked</c> over a rectangle: blocked if any tile is, else the highest new lift.</summary>
    public bool IsBlockedArea(int height, int lift, int startx, int starty, int xtiles, int ytiles,
        out int newLift, int moveFlags, int maxDrop = 1, int maxRise = -1)
    {
        newLift = 0;
        for (var y = 0; y < ytiles; y++)
        {
            for (var x = 0; x < xtiles; x++)
            {
                if (IsBlocked(height, lift, startx + x, starty + y, out var thisLift, moveFlags, maxDrop, maxRise))
                {
                    return true;
                }

                newLift = Math.Max(newLift, thisLift);
            }
        }

        return false;
    }

    /// <summary>Exult <c>Tile_coord::gte</c>: t1 ≥ t2 across the world's wrap.</summary>
    static bool Gte(int t1, int t2)
    {
        var diff = t1 - t2;
        return diff >= 0 ? diff < U7Constants.NumTiles / 2 : diff < -U7Constants.NumTiles / 2;
    }

    static int Incr(int t) => U7Constants.WrapTile(t + 1);

    /// <summary>
    /// Exult <c>Map_chunk::is_blocked</c> for an object bigger than 1×1
    /// stepping from <paramref name="from"/> to <paramref name="to"/>: only the
    /// strips of tiles it newly covers are tested, and all of them must agree
    /// on any change of lift, which goes into <paramref name="to"/>.
    /// </summary>
    public bool IsBlockedStep(int xtiles, int ytiles, int ztiles, TileCoord from, ref TileCoord to,
        int moveFlags, int maxDrop = 1, int maxRise = -1)
    {
        var horizx0 = U7Constants.WrapTile(to.Tx + 1 - xtiles);
        var horizx1 = Incr(to.Tx);
        int vertx0;
        int vertx1;
        if (Gte(to.Tx, from.Tx))
        {
            vertx0 = Incr(from.Tx);
            vertx1 = Incr(to.Tx);
        }
        else
        {
            vertx0 = U7Constants.WrapTile(to.Tx + 1 - xtiles);
            vertx1 = U7Constants.WrapTile(from.Tx + 1 - xtiles);
        }

        var verty0 = U7Constants.WrapTile(to.Ty + 1 - ytiles);
        var verty1 = Incr(to.Ty);
        int horizy0;
        int horizy1;
        if (Gte(to.Ty, from.Ty))
        {
            horizy0 = Incr(from.Ty);
            horizy1 = Incr(to.Ty);
            if (to.Ty != from.Ty)
            {
                verty1 = U7Constants.WrapTile(verty1 - 1);
            }
        }
        else
        {
            horizy0 = U7Constants.WrapTile(to.Ty + 1 - ytiles);
            horizy1 = U7Constants.WrapTile(from.Ty + 1 - ytiles);
            verty0 = Incr(verty0);
        }

        var newLift = from.Tz;
        var newLift0 = -1;
        for (var y = horizy0; y != horizy1; y = Incr(y))
        {
            for (var x = horizx0; x != horizx1; x = Incr(x))
            {
                if (IsBlocked(ztiles, from.Tz, x, y, out newLift, moveFlags, maxDrop, maxRise) ||
                    !SameLiftChange(from.Tz, newLift, ref newLift0))
                {
                    return true;
                }
            }
        }

        for (var x = vertx0; x != vertx1; x = Incr(x))
        {
            for (var y = verty0; y != verty1; y = Incr(y))
            {
                if (IsBlocked(ztiles, from.Tz, x, y, out newLift, moveFlags, maxDrop, maxRise) ||
                    !SameLiftChange(from.Tz, newLift, ref newLift0))
                {
                    return true;
                }
            }
        }

        to = to with { Tz = newLift };
        return false;
    }

    static bool SameLiftChange(int fromLift, int newLift, ref int first)
    {
        if (newLift == fromLift)
        {
            return true;
        }

        if (first == -1)
        {
            first = newLift;
            return true;
        }

        return newLift == first;
    }
}
