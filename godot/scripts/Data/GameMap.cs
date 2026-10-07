using System.IO;
using U7.Core;

namespace U7.Data;

public readonly record struct TerrainCell(int Shape, int Frame, bool IsRle);

/// <summary>
/// Loads the Black Gate world the same way Exult does:
/// U7MAP (chunk → terrain template), U7CHUNKS (16×16 flat/RLE cells),
/// U7IFIXxx (fixed scenery), U7IREGxx (moveable objects).
/// </summary>
public sealed class GameMap
{
    public const int TerrainCount = 3072;

    public ShapeCatalog Catalog { get; }
    public ushort[,] TerrainMap { get; } = new ushort[U7Constants.NumChunks, U7Constants.NumChunks];
    public TerrainCell[][] Terrains { get; } = new TerrainCell[TerrainCount][];
    public List<U7Object>[][] ChunkObjects { get; }
    public List<U7Object> Eggs { get; } = new();
    readonly Dictionary<int, List<U7Object>> _pathEggs = new();
    /// <summary>
    /// Exult <c>Chunk_cache::eggs</c>: per chunk, 16 bits per tile naming the
    /// eggs whose active area (or its perimeter) covers that tile. Bit 15
    /// stands for "egg index 15 and above".
    /// </summary>
    readonly ChunkEggs?[][] _chunkEggs;
    /// <summary>Saved usecode scripts read with their objects, handed to the usecode machine once it exists.</summary>
    public List<(U7Object Obj, byte[] Script)> PendingScripts { get; } = new();
    /// <summary>Supplies the running scripts of an object when saving (Exult <c>write_scheduled</c>).</summary>
    public Func<U7Object, IEnumerable<byte[]>>? ScriptSaver { get; set; }
    /// <summary>Chunks whose paint dependencies have been computed (done lazily on first draw).</summary>
    readonly bool[][] _chunkOrdered;
    readonly byte[]?[][] _dungeonLevels;

    public GameMap(ShapeCatalog catalog)
    {
        Catalog = catalog;
        ChunkObjects = new List<U7Object>[U7Constants.NumChunks][];
        _dungeonLevels = new byte[]?[U7Constants.NumChunks][];
        _chunkEggs = new ChunkEggs?[U7Constants.NumChunks][];
        _chunkOrdered = new bool[U7Constants.NumChunks][];
        for (var x = 0; x < U7Constants.NumChunks; x++)
        {
            _chunkOrdered[x] = new bool[U7Constants.NumChunks];
            ChunkObjects[x] = new List<U7Object>[U7Constants.NumChunks];
            _dungeonLevels[x] = new byte[]?[U7Constants.NumChunks];
            _chunkEggs[x] = new ChunkEggs?[U7Constants.NumChunks];
            for (var y = 0; y < U7Constants.NumChunks; y++)
            {
                ChunkObjects[x][y] = new List<U7Object>();
            }
        }

        LoadChunks(Path.Combine(U7Paths.StaticDir, "U7CHUNKS"));
        LoadMap(Path.Combine(U7Paths.StaticDir, "U7MAP"));
        PlaceTerrainRleObjects();
        LoadAllIfix();
        try
        {
            LoadAllIreg();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"IREG load failed: {ex.Message}");
        }

        LogEggSummary();
        SetupDungeonLevels();
    }

    public TerrainCell GetFlat(int tileX, int tileY)
    {
        tileX = U7Constants.WrapTile(tileX);
        tileY = U7Constants.WrapTile(tileY);
        var cx = tileX / U7Constants.TilesPerChunk;
        var cy = tileY / U7Constants.TilesPerChunk;
        var tnum = TerrainMap[cx, cy];
        if (tnum >= Terrains.Length || Terrains[tnum] is null)
        {
            return default;
        }

        var lx = tileX % U7Constants.TilesPerChunk;
        var ly = tileY % U7Constants.TilesPerChunk;
        return Terrains[tnum][ly * U7Constants.TilesPerChunk + lx];
    }

    public List<U7Object> ObjectsInChunk(int cx, int cy) =>
        ChunkObjects[U7Constants.WrapChunk(cx)][U7Constants.WrapChunk(cy)];

    public void AddObject(U7Object obj)
    {
        InsertIntoChunk(obj);
        if (obj.IsEgg && !Eggs.Contains(obj))
        {
            Eggs.Add(obj);
            SetEggArea(obj);
            UpdateEgg(obj, add: true);
        }
    }

    /// <summary>
    /// Remove from the chunk list without marking <see cref="U7Object.Removed"/>.
    /// Contained items are not in a chunk.
    /// </summary>
    public void TakeFromWorld(U7Object obj)
    {
        if (obj.Container is { } parent)
        {
            parent.Contents.Remove(obj);
            obj.Container = null;
            obj.ReadySlot = -1;
            return;
        }

        RemoveFromChunk(obj);
    }

    public void PlaceInWorld(U7Object obj, int tx, int ty, int tz)
    {
        TakeFromWorld(obj);
        obj.Removed = false;
        obj.Container = null;
        obj.ReadySlot = -1;
        obj.Tx = U7Constants.WrapTile(tx);
        obj.Ty = U7Constants.WrapTile(ty);
        obj.Tz = tz;
        AddObject(obj);
    }

    public void PlaceInContainer(U7Object obj, U7Object container, int gx, int gy)
    {
        if (obj == container)
        {
            return;
        }

        TakeFromWorld(obj);
        obj.Removed = false;
        obj.Container = container;
        obj.Tx = gx;
        obj.Ty = gy;
        if (!container.Contents.Contains(obj))
        {
            container.Contents.Add(obj);
        }
    }

    public void MoveObject(U7Object obj, int newTx, int newTy, int newTz)
    {
        if (obj.Removed)
        {
            // A removed object (dead NPC) keeps a position but never re-enters a chunk.
            obj.Tx = U7Constants.WrapTile(newTx);
            obj.Ty = U7Constants.WrapTile(newTy);
            obj.Tz = newTz;
            return;
        }

        RemoveFromChunk(obj);
        if (obj.IsEgg)
        {
            UpdateEgg(obj, add: false);
        }

        obj.Tx = U7Constants.WrapTile(newTx);
        obj.Ty = U7Constants.WrapTile(newTy);
        obj.Tz = newTz;
        if (obj.IsEgg)
        {
            SetEggArea(obj);
            UpdateEgg(obj, add: true);
        }

        InsertIntoChunk(obj);
    }

    /// <summary>
    /// Exult <c>Map_chunk::add</c>: append to the chunk list and, once the
    /// chunk's ordering exists, compute paint dependencies against the chunk
    /// and its neighbours.
    /// </summary>
    void InsertIntoChunk(U7Object obj)
    {
        var cx = U7Constants.WrapChunk(obj.Tx / U7Constants.TilesPerChunk);
        var cy = U7Constants.WrapChunk(obj.Ty / U7Constants.TilesPerChunk);
        ChunkObjects[cx][cy].Add(obj);
        if (_chunkOrdered[cx][cy] && !obj.IsFlat)
        {
            AddDependencies(obj, cx, cy, onlyEarlierInOwnChunk: false);
        }
    }

    /// <summary>Exult <c>Map_chunk::remove</c>: drop from the list and clear dependencies.</summary>
    void RemoveFromChunk(U7Object obj)
    {
        var cx = U7Constants.WrapChunk(obj.Tx / U7Constants.TilesPerChunk);
        var cy = U7Constants.WrapChunk(obj.Ty / U7Constants.TilesPerChunk);
        ChunkObjects[cx][cy].Remove(obj);
        ClearDependencies(obj);
    }

    /// <summary>
    /// Compute paint dependencies for every non-flat object in the chunk the
    /// first time it is drawn (Exult does this as chunks are read in).
    /// </summary>
    public void EnsureChunkOrdered(int cx, int cy)
    {
        cx = U7Constants.WrapChunk(cx);
        cy = U7Constants.WrapChunk(cy);
        if (_chunkOrdered[cx][cy])
        {
            return;
        }

        _chunkOrdered[cx][cy] = true;
        var list = ChunkObjects[cx][cy];
        for (var i = 0; i < list.Count; i++)
        {
            var obj = list[i];
            if (!obj.IsFlat && !obj.Removed)
            {
                AddDependencies(obj, cx, cy, onlyEarlierInOwnChunk: true, ownIndex: i);
            }
        }
    }

    /// <summary>
    /// Exult <c>Map_chunk::add_dependencies</c> over the chunk and its eight
    /// neighbours (Exult limits the neighbours by overlap flags; comparing
    /// against all of them only adds dependencies Exult would also accept).
    /// </summary>
    void AddDependencies(U7Object obj, int cx, int cy, bool onlyEarlierInOwnChunk, int ownIndex = int.MaxValue)
    {
        const int Reach = 24; // tiles; sprites never extend further
        var info = new RenderOrdering.Info(obj, Catalog);
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var list = ObjectsInChunk(cx + dx, cy + dy);
                var own = dx == 0 && dy == 0;
                var end = own && onlyEarlierInOwnChunk ? Math.Min(ownIndex, list.Count) : list.Count;
                for (var i = 0; i < end; i++)
                {
                    var other = list[i];
                    if (other == obj || other.Removed || other.IsFlat ||
                        Math.Abs(other.Tx - obj.Tx) > Reach || Math.Abs(other.Ty - obj.Ty) > Reach)
                    {
                        continue;
                    }

                    var cmp = RenderOrdering.Compare(info, new RenderOrdering.Info(other, Catalog));
                    if (cmp == 1)
                    {
                        (obj.Dependencies ??= new HashSet<U7Object>()).Add(other);
                        (other.Dependors ??= new HashSet<U7Object>()).Add(obj);
                    }
                    else if (cmp == -1)
                    {
                        (other.Dependencies ??= new HashSet<U7Object>()).Add(obj);
                        (obj.Dependors ??= new HashSet<U7Object>()).Add(other);
                    }
                }
            }
        }
    }

    /// <summary>Exult <c>Game_object::clear_dependencies</c>.</summary>
    static void ClearDependencies(U7Object obj)
    {
        if (obj.Dependencies is { } deps)
        {
            foreach (var d in deps)
            {
                d.Dependors?.Remove(obj);
            }

            deps.Clear();
        }

        if (obj.Dependors is { } dependors)
        {
            foreach (var d in dependors)
            {
                d.Dependencies?.Remove(obj);
            }

            dependors.Clear();
        }
    }

    public void RemoveObject(U7Object obj)
    {
        obj.Removed = true;
        obj.BarkText = "";
        if (obj.Container is { } parent)
        {
            parent.Contents.Remove(obj);
            obj.Container = null;
            return;
        }

        RemoveFromChunk(obj);
        if (obj.IsEgg)
        {
            UpdateEgg(obj, add: false);
            Eggs.Remove(obj);
            if (obj.EggType == U7.World.EggType.Path &&
                _pathEggs.TryGetValue(obj.Quality, out var list))
            {
                list.Remove(obj);
            }
        }
    }

    /// <summary>
    /// Eggs whose active area covers the tile, from the chunk egg bits. Returns
    /// null when the tile has no egg bits (the common case on a step). Ports the
    /// bit walk in Exult <c>Chunk_cache::activate_eggs</c>.
    /// </summary>
    public List<U7Object>? EggsAt(int tx, int ty)
    {
        const int T = U7Constants.TilesPerChunk;
        tx = U7Constants.WrapTile(tx);
        ty = U7Constants.WrapTile(ty);
        var ce = _chunkEggs[tx / T][ty / T];
        if (ce is null)
        {
            return null;
        }

        int bits = ce.Bits[(ty % T) * T + tx % T];
        if (bits == 0)
        {
            return null;
        }

        var result = new List<U7Object>(4);
        var n = ce.Eggs.Count;
        for (var i = 0; i < 15 && i < n; i++)
        {
            if ((bits & (1 << i)) != 0 && ce.Eggs[i] is { Removed: false } egg)
            {
                result.Add(egg);
            }
        }

        if ((bits & 0x8000) != 0)
        {
            for (var i = 15; i < n; i++)
            {
                if (ce.Eggs[i] is { Removed: false } egg)
                {
                    result.Add(egg);
                }
            }
        }

        return result;
    }

    /// <summary>Exult <c>Egg_object::set_area</c>: criteria/types whose whole area is egged.</summary>
    static bool EggSolidArea(U7Object egg) =>
        egg.EggCriteria is U7.World.EggCriteria.SomethingOn or U7.World.EggCriteria.CachedIn ||
        egg.EggType is U7.World.EggType.Teleport or U7.World.EggType.Intermap;

    /// <summary>Exult <c>Chunk_cache::update_egg</c>: whole area, or just its perimeter.</summary>
    void UpdateEgg(U7Object egg, bool add)
    {
        var x = egg.EggAreaX;
        var y = egg.EggAreaY;
        var w = egg.EggAreaW;
        var h = egg.EggAreaH;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        if (EggSolidArea(egg))
        {
            SetEggedRect(egg, x, y, w, h, add);
            return;
        }

        SetEggedRect(egg, x, y, w, 1, add);
        SetEggedRect(egg, x, y + h - 1, w, 1, add);
        SetEggedRect(egg, x, y + 1, 1, h - 2, add);
        SetEggedRect(egg, x + w - 1, y + 1, 1, h - 2, add);
    }

    /// <summary>
    /// Walk the chunks a world rect intersects (Exult <c>Chunk_intersect_iterator</c>),
    /// clipped to the world like <c>Egg_object::set_area</c>.
    /// </summary>
    void SetEggedRect(U7Object egg, int x, int y, int w, int h, bool add)
    {
        const int T = U7Constants.TilesPerChunk;
        var x0 = Math.Max(0, x);
        var y0 = Math.Max(0, y);
        var x1 = Math.Min(U7Constants.NumTiles, x + w);
        var y1 = Math.Min(U7Constants.NumTiles, y + h);
        if (x1 <= x0 || y1 <= y0)
        {
            return;
        }

        for (var cy = y0 / T; cy <= (y1 - 1) / T; cy++)
        {
            for (var cx = x0 / T; cx <= (x1 - 1) / T; cx++)
            {
                SetEgged(cx, cy, egg,
                    Math.Max(x0, cx * T) - cx * T, Math.Max(y0, cy * T) - cy * T,
                    Math.Min(x1, cx * T + T) - cx * T, Math.Min(y1, cy * T + T) - cy * T,
                    add);
            }
        }
    }

    /// <summary>Exult <c>Chunk_cache::set_egged</c> for one chunk; bounds are chunk-local, end exclusive.</summary>
    void SetEgged(int cx, int cy, U7Object egg, int lx0, int ly0, int lx1, int ly1, bool add)
    {
        const int T = U7Constants.TilesPerChunk;
        var ce = _chunkEggs[cx][cy];
        if (ce is null)
        {
            if (!add)
            {
                return;
            }

            ce = _chunkEggs[cx][cy] = new ChunkEggs();
        }

        var eggnum = ce.Eggs.IndexOf(egg);
        if (add)
        {
            if (eggnum < 0)
            {
                eggnum = ce.Eggs.IndexOf(null);
                if (eggnum >= 0)
                {
                    ce.Eggs[eggnum] = egg;
                }
                else
                {
                    ce.Eggs.Add(egg);
                    eggnum = ce.Eggs.Count - 1;
                }
            }

            var bit = (ushort)(1 << Math.Min(eggnum, 15));
            for (var ly = ly0; ly < ly1; ly++)
            {
                for (var lx = lx0; lx < lx1; lx++)
                {
                    ce.Bits[ly * T + lx] |= bit;
                }
            }

            return;
        }

        if (eggnum < 0)
        {
            return;
        }

        ce.Eggs[eggnum] = null;
        if (eggnum >= 15)
        {
            for (var i = 15; i < ce.Eggs.Count; i++)
            {
                if (ce.Eggs[i] is not null)
                {
                    return;
                }
            }

            eggnum = 15;
        }

        var mask = (ushort)~(1 << eggnum);
        for (var ly = ly0; ly < ly1; ly++)
        {
            for (var lx = lx0; lx < lx1; lx++)
            {
                ce.Bits[ly * T + lx] &= mask;
            }
        }
    }

    sealed class ChunkEggs
    {
        public readonly ushort[] Bits = new ushort[U7Constants.TilesPerChunk * U7Constants.TilesPerChunk];
        public readonly List<U7Object?> Eggs = new();
    }

    public List<U7Object> EggsNear(int tx, int ty, int dist)
    {
        var result = new List<U7Object>();
        var chunks = Math.Max(1, (dist + 15) / 16);
        var ocx = tx / U7Constants.TilesPerChunk;
        var ocy = ty / U7Constants.TilesPerChunk;
        for (var cy = ocy - chunks; cy <= ocy + chunks; cy++)
        {
            for (var cx = ocx - chunks; cx <= ocx + chunks; cx++)
            {
                foreach (var obj in ObjectsInChunk(cx, cy))
                {
                    if (!obj.IsEgg || obj.Removed)
                    {
                        continue;
                    }

                    var d = new TileCoord(tx, ty, 0).Distance2d(new TileCoord(obj.Tx, obj.Ty, 0));
                    if (d <= dist)
                    {
                        result.Add(obj);
                    }
                }
            }
        }

        return result;
    }

    public U7Object? FindPathEgg(int quality)
    {
        if (!_pathEggs.TryGetValue(quality, out var list) || list.Count == 0)
        {
            return null;
        }

        foreach (var e in list)
        {
            if (!e.Removed)
            {
                return e;
            }
        }

        return null;
    }

    /// <summary>
    /// Objects of <paramref name="shape"/> (-359 = any) within Chebyshev
    /// <paramref name="dist"/> tiles of <paramref name="origin"/>.
    /// </summary>
    public List<U7Object> FindNearby(TileCoord origin, int shape, int dist, int mask = 0)
    {
        var npcOnly = mask == 8;
        var includeActors = (mask & 8) != 0 || (mask & 0x80) != 0;
        var includeObjects = !npcOnly;
        var result = new List<U7Object>();
        var chunks = Math.Max(1, (dist + 15) / 16 + 1);
        var ocx = origin.Tx / U7Constants.TilesPerChunk;
        var ocy = origin.Ty / U7Constants.TilesPerChunk;
        for (var cy = ocy - chunks; cy <= ocy + chunks; cy++)
        {
            for (var cx = ocx - chunks; cx <= ocx + chunks; cx++)
            {
                foreach (var obj in ObjectsInChunk(cx, cy))
                {
                    if (obj.Removed)
                    {
                        continue;
                    }

                    if (obj.IsEgg && (mask & 16) == 0)
                    {
                        continue;
                    }

                    if (obj.IsActor)
                    {
                        if (!includeActors)
                        {
                            continue;
                        }
                    }
                    else if (!includeObjects)
                    {
                        continue;
                    }

                    if (shape != U7Constants.AnyShape && obj.Shape != shape)
                    {
                        continue;
                    }

                    if (origin.Distance2d(new TileCoord(obj.Tx, obj.Ty, obj.Tz)) <= dist)
                    {
                        result.Add(obj);
                    }
                }
            }
        }

        result.Sort((a, b) => b.RenderOrder.CompareTo(a.RenderOrder));
        return result;
    }

    public bool IsBlocked(int tx, int ty, int lift)
    {
        tx = U7Constants.WrapTile(tx);
        ty = U7Constants.WrapTile(ty);
        var flat = GetFlat(tx, ty);
        var info = Catalog[flat.Shape];
        if (info.Water && lift <= 0)
        {
            return true;
        }

        var cx = tx / U7Constants.TilesPerChunk;
        var cy = ty / U7Constants.TilesPerChunk;
        foreach (var obj in ChunkObjects[cx][cy])
        {
            if (obj.Removed || obj.IsActor || obj.IsEgg)
            {
                continue;
            }

            if (obj.BlocksAt(tx, ty, lift))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Exult <c>Map_chunk::find_spot</c>, 1-tile version.</summary>
    public TileCoord? FindSpot(int tx, int ty, int tz, int dist)
    {
        tx = U7Constants.WrapTile(tx);
        ty = U7Constants.WrapTile(ty);
        if (!IsBlocked(tx, ty, tz))
        {
            return new TileCoord(tx, ty, tz);
        }

        for (var d = 1; d <= dist; d++)
        {
            for (var dy = -d; dy <= d; dy++)
            {
                for (var dx = -d; dx <= d; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != d)
                    {
                        continue;
                    }

                    var nx = U7Constants.WrapTile(tx + dx);
                    var ny = U7Constants.WrapTile(ty + dy);
                    if (!IsBlocked(nx, ny, tz))
                    {
                        return new TileCoord(nx, ny, tz);
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Exult <c>Map_chunk::is_roof</c>: lowest solid lift at or above
    /// <c>lift + 4</c>, or <see cref="U7Constants.NoRoof"/> if none.
    /// </summary>
    public int RoofHeight(int tx, int ty, int lift)
    {
        var height = LowestBlocked(tx, ty, lift + 4);
        return height < 0 ? U7Constants.NoRoof : height;
    }

    public int DungeonHeight(int tx, int ty)
    {
        tx = U7Constants.WrapTile(tx);
        ty = U7Constants.WrapTile(ty);
        var arr = _dungeonLevels[tx / U7Constants.TilesPerChunk][ty / U7Constants.TilesPerChunk];
        if (arr is null)
        {
            return 0;
        }

        var lx = tx % U7Constants.TilesPerChunk;
        var ly = ty % U7Constants.TilesPerChunk;
        return arr[ly * U7Constants.TilesPerChunk + lx];
    }

    public bool ChunkHasDungeon(int cx, int cy) =>
        _dungeonLevels[U7Constants.WrapChunk(cx)][U7Constants.WrapChunk(cy)] is not null;

    public int DungeonHeightLocal(int cx, int cy, int lx, int ly)
    {
        var arr = _dungeonLevels[U7Constants.WrapChunk(cx)][U7Constants.WrapChunk(cy)];
        if (arr is null)
        {
            return 0;
        }

        return arr[ly * U7Constants.TilesPerChunk + lx];
    }

    int LowestBlocked(int tx, int ty, int fromLift)
    {
        tx = U7Constants.WrapTile(tx);
        ty = U7Constants.WrapTile(ty);
        var best = -1;
        var ocx = tx / U7Constants.TilesPerChunk;
        var ocy = ty / U7Constants.TilesPerChunk;
        for (var dcy = 0; dcy <= 1; dcy++)
        {
            for (var dcx = 0; dcx <= 1; dcx++)
            {
                foreach (var obj in ObjectsInChunk(ocx + dcx, ocy + dcy))
                {
                    if (obj.Removed || obj.IsActor || obj.IsEgg || !obj.Solid || obj.DimZ <= 0)
                    {
                        continue;
                    }

                    if (!obj.Occupies(tx, ty))
                    {
                        continue;
                    }

                    var top = obj.Tz + obj.DimZ - 1;
                    if (top < fromLift)
                    {
                        continue;
                    }

                    var z = Math.Max(obj.Tz, fromLift);
                    if (best < 0 || z < best)
                    {
                        best = z;
                    }
                }
            }
        }

        return best;
    }

    void SetupDungeonLevels()
    {
        for (var cy = 0; cy < U7Constants.NumChunks; cy++)
        {
            for (var cx = 0; cx < U7Constants.NumChunks; cx++)
            {
                foreach (var obj in ChunkObjects[cx][cy])
                {
                    var info = Catalog[obj.Shape];
                    if (!info.IsBuilding || info.MountainTop == 0)
                    {
                        continue;
                    }

                    if (info.Translucent && obj.Frame == 0)
                    {
                        AddDungeonTiles(
                            cx * U7Constants.TilesPerChunk,
                            cy * U7Constants.TilesPerChunk,
                            U7Constants.TilesPerChunk,
                            U7Constants.TilesPerChunk,
                            obj.Tz);
                    }
                    else
                    {
                        AddDungeonTiles(
                            obj.Tx - obj.DimX + 1,
                            obj.Ty - obj.DimY + 1,
                            obj.DimX,
                            obj.DimY,
                            obj.Tz);
                    }
                }
            }
        }
    }

    void AddDungeonTiles(int x0, int y0, int w, int h, int lift)
    {
        var z = (byte)Math.Clamp(lift, 0, 255);
        for (var ty = y0; ty < y0 + h; ty++)
        {
            for (var tx = x0; tx < x0 + w; tx++)
            {
                var wtx = U7Constants.WrapTile(tx);
                var wty = U7Constants.WrapTile(ty);
                var cx = wtx / U7Constants.TilesPerChunk;
                var cy = wty / U7Constants.TilesPerChunk;
                var arr = _dungeonLevels[cx][cy];
                if (arr is null)
                {
                    arr = new byte[U7Constants.TilesPerChunk * U7Constants.TilesPerChunk];
                    _dungeonLevels[cx][cy] = arr;
                }

                var lx = wtx % U7Constants.TilesPerChunk;
                var ly = wty % U7Constants.TilesPerChunk;
                arr[ly * U7Constants.TilesPerChunk + lx] = z;
            }
        }
    }

    void LoadChunks(string path)
    {
        var data = File.ReadAllBytes(path);
        var expected = TerrainCount * 256 * 2;
        if (data.Length < expected)
        {
            throw new InvalidDataException($"U7CHUNKS is {data.Length} bytes, expected {expected}.");
        }

        var off = 0;
        for (var t = 0; t < TerrainCount; t++)
        {
            var cells = new TerrainCell[256];
            for (var i = 0; i < 256; i++)
            {
                var b0 = data[off++];
                var b1 = data[off++];
                var shape = b0 + 256 * (b1 & 3);
                var frame = (b1 >> 2) & 0x1f;
                var isRle = !Catalog[shape].TileShape;
                cells[i] = new TerrainCell(shape, frame, isRle);
            }

            Terrains[t] = cells;
        }
    }

    void LoadMap(string path)
    {
        var data = File.ReadAllBytes(path);
        var off = 0;
        for (var schunk = 0; schunk < U7Constants.NumSuperchunks; schunk++)
        {
            var scy = 16 * (schunk / 12);
            var scx = 16 * (schunk % 12);
            for (var cy = 0; cy < 16; cy++)
            {
                for (var cx = 0; cx < 16; cx++)
                {
                    TerrainMap[scx + cx, scy + cy] = BitConverter.ToUInt16(data, off);
                    off += 2;
                }
            }
        }
    }

    void PlaceTerrainRleObjects()
    {
        for (var cy = 0; cy < U7Constants.NumChunks; cy++)
        {
            for (var cx = 0; cx < U7Constants.NumChunks; cx++)
            {
                var tnum = TerrainMap[cx, cy];
                if (tnum >= Terrains.Length || Terrains[tnum] is null)
                {
                    continue;
                }

                var cells = Terrains[tnum];
                for (var ly = 0; ly < 16; ly++)
                {
                    for (var lx = 0; lx < 16; lx++)
                    {
                        var cell = cells[ly * 16 + lx];
                        if (!cell.IsRle)
                        {
                            continue;
                        }

                        ChunkObjects[cx][cy].Add(MakeObject(
                            cx * 16 + lx, cy * 16 + ly, 0, cell.Shape, cell.Frame, 0,
                            ObjectKind.TerrainRle));
                    }
                }
            }
        }
    }

    void LoadAllIfix()
    {
        for (var schunk = 0; schunk < U7Constants.NumSuperchunks; schunk++)
        {
            var name = Path.Combine(U7Paths.StaticDir, $"U7IFIX{schunk:X2}");
            if (!File.Exists(name))
            {
                continue;
            }

            var flex = new FlexFile(name);
            var scy = 16 * (schunk / 12);
            var scx = 16 * (schunk % 12);
            for (var cy = 0; cy < 16; cy++)
            {
                for (var cx = 0; cx < 16; cx++)
                {
                    var entry = flex.Get(cy * 16 + cx);
                    ParseIfix(entry, scx + cx, scy + cy);
                }
            }
        }
    }

    void ParseIfix(ReadOnlySpan<byte> entry, int cx, int cy)
    {
        // Original IFIX: 4 bytes/object (Exult Game_map::get_ifix_chunk_objects).
        var count = entry.Length / 4;
        for (var i = 0; i < count; i++)
        {
            var e = entry.Slice(i * 4, 4);
            var tx = (e[0] >> 4) & 0xf;
            var ty = e[0] & 0xf;
            var tz = e[1] & 0xf;
            var shape = e[2] + 256 * (e[3] & 3);
            var frame = e[3] >> 2;
            ChunkObjects[cx][cy].Add(MakeObject(
                cx * 16 + tx, cy * 16 + ty, tz, shape, frame, 0, ObjectKind.Ifix));
        }
    }

    void LoadAllIreg()
    {
        for (var schunk = 0; schunk < U7Constants.NumSuperchunks; schunk++)
        {
            var data = U7Paths.ReadGameDat($"U7IREG{schunk:X2}");
            if (data is null)
            {
                continue;
            }

            var scy = 16 * (schunk / 12);
            var scx = 16 * (schunk % 12);
            var i = 0;
            ParseIreg(data, ref i, scx, scy, null);
        }
    }

    /// <summary>
    /// Exult <c>Game_map::read_ireg_objects</c>. Used for U7IREG and npc.dat
    /// inventory. Length 0/1 ends a container.
    /// </summary>
    public void ReadIregObjects(byte[] data, ref int i, int scx, int scy, U7Object? container) =>
        ParseIreg(data, ref i, scx, scy, container);

    void ParseIreg(byte[] data, ref int i, int scx, int scy, U7Object? container)
    {
        var readyIndex = -1;
        U7Object? last = null;
        while (i < data.Length)
        {
            var entlen = data[i++];
            if (entlen == 0 || entlen == 1)
            {
                if (container is not null)
                {
                    return;
                }

                continue;
            }

            if (entlen == 2)
            {
                if (i + 2 > data.Length)
                {
                    break;
                }

                readyIndex = unchecked((sbyte)data[i]);
                i += 2;
                continue;
            }

            var extended = false;
            if (entlen is 254 or 253)
            {
                extended = entlen == 254;
                if (i >= data.Length)
                {
                    break;
                }

                entlen = data[i++];
            }

            if (entlen == 255)
            {
                if (i < data.Length)
                {
                    var kind = data[i++];
                    if (kind == 1 && i + 2 <= data.Length)
                    {
                        var len = BitConverter.ToUInt16(data, i);
                        i += 2;
                        if (last is not null && i + len <= data.Length)
                        {
                            PendingScripts.Add((last, data.AsSpan(i, len).ToArray()));
                        }

                        i += len;
                    }
                }

                continue;
            }

            if (i + entlen > data.Length)
            {
                break;
            }

            var entry = data.AsSpan(i, entlen);
            i += entlen;
            var testlen = entlen - (extended ? 1 : 0);

            int shape;
            int frame;
            int liftIndex;
            if (extended)
            {
                shape = entry[2] + 256 * entry[3];
                frame = entry[4];
                liftIndex = 5;
            }
            else
            {
                shape = entry[2] + 256 * (entry[3] & 3);
                frame = entry[3] >> 2;
                liftIndex = 4;
            }

            var info = Catalog[shape];
            int tilex;
            int tiley;
            var cx = entry[0] >> 4;
            var cy = entry[1] >> 4;
            if (container is not null)
            {
                tilex = entry[0];
                tiley = entry[1];
            }
            else
            {
                tilex = entry[0] & 0xf;
                tiley = entry[1] & 0xf;
            }

            if (info.IsHatchable)
            {
                last = ReadIregEgg(entry, testlen, extended, scx, scy, cx, cy, tilex, tiley, shape, frame);
                continue;
            }

            if (testlen is 12 or 13)
            {
                last = ReadIregContainer(
                    data, ref i, entry, testlen, scx, scy, cx, cy,
                    tilex, tiley, shape, frame, container, readyIndex);
                continue;
            }

            if (testlen is not (6 or 10 or 18))
            {
                continue;
            }

            var lift = NibbleSwap(entry[liftIndex]) & 0xf;
            var qualityIndex = extended ? 6 : 5;
            var quality = qualityIndex < entry.Length ? entry[qualityIndex] : 0;
            uint flags = 0;
            if (info.HasQuantity)
            {
                if ((quality & 0x80) != 0)
                {
                    flags |= 1u << 11;
                    quality &= 0x7f;
                }
            }
            else if (info.HasQualityFlags)
            {
                flags = ((uint)(quality & 1) << 0) | (((uint)(quality >> 3) & 1u) << 11);
                quality = 0;
            }

            if (container is not null)
            {
                var nested = MakeObject(tilex, tiley, lift, shape, frame, quality, ObjectKind.Ireg);
                nested.Flags = flags;
                nested.Container = container;
                nested.ReadySlot = ActorReadySlot(container, readyIndex);
                container.Contents.Add(nested);
                last = nested;
            }
            else
            {
                var wcx = scx + cx;
                var wcy = scy + cy;
                if ((uint)wcx >= U7Constants.NumChunks || (uint)wcy >= U7Constants.NumChunks)
                {
                    continue;
                }

                var obj = MakeObject(
                    wcx * 16 + tilex, wcy * 16 + tiley, lift, shape, frame, quality,
                    ObjectKind.Ireg);
                obj.Flags = flags;
                ChunkObjects[wcx][wcy].Add(obj);
                last = obj;
            }
        }
    }

    static int ActorReadySlot(U7Object? container, int readyIndex) =>
        container is { IsActor: true } && readyIndex is >= 0 and < 12 ? readyIndex : -1;

    U7Object? ReadIregContainer(
        byte[] data, ref int i, ReadOnlySpan<byte> entry, int testlen,
        int scx, int scy, int cx, int cy, int tilex, int tiley,
        int shape, int frame, U7Object? parent, int readyIndex)
    {
        var info = Catalog[shape];
        var skip = info.IsBargeClass ||
                   info.Name.Contains("jawbone", StringComparison.OrdinalIgnoreCase);
        var type = entry[4] + 256 * entry[5];
        // 13-byte entries are Exult Dead_body records: npc num at [8..9], lift at [10].
        var isBody = testlen == 13;
        var lift = NibbleSwap(entry[isBody ? 10 : 9]) & 0xf;
        var quality = entry.Length > 7 ? entry[7] : 0;
        var liveNpc = isBody && entry.Length > 9 ? entry[8] | (entry[9] << 8) : -1;
        var flagByte = testlen == 13
            ? (entry.Length > 12 ? entry[12] : (byte)0)
            : (entry.Length > 11 ? entry[11] : (byte)0);
        uint flags = ((uint)(flagByte & 1) << 0) | (((uint)(flagByte >> 3) & 1u) << 11);

        U7Object? obj = null;
        if (!skip)
        {
            if (parent is not null)
            {
                obj = MakeObject(tilex, tiley, lift, shape, frame, quality, ObjectKind.Ireg);
                obj.Flags = flags;
                obj.LiveNpcNum = liveNpc;
                obj.Container = parent;
                obj.ReadySlot = ActorReadySlot(parent, readyIndex);
                parent.Contents.Add(obj);
            }
            else
            {
                var wcx = scx + cx;
                var wcy = scy + cy;
                if ((uint)wcx < U7Constants.NumChunks && (uint)wcy < U7Constants.NumChunks)
                {
                    obj = MakeObject(
                        wcx * 16 + tilex, wcy * 16 + tiley, lift, shape, frame, quality,
                        ObjectKind.Ireg);
                    obj.Flags = flags;
                    obj.LiveNpcNum = liveNpc;
                    ChunkObjects[wcx][wcy].Add(obj);
                }
            }
        }

        if (type != 0)
        {
            ParseIreg(data, ref i, scx, scy, obj ?? new U7Object());
        }

        return obj;
    }

    U7Object? ReadIregEgg(
        ReadOnlySpan<byte> entry, int testlen, bool extended,
        int scx, int scy, int cx, int cy, int tilex, int tiley, int shape, int frame)
    {
        var off = extended ? 1 : 0;
        if (4 + off + 8 > entry.Length)
        {
            return null;
        }

        var itype = entry[4 + off] + 256 * entry[5 + off];
        var prob = entry[6 + off];
        var data1 = entry[7 + off] + 256 * entry[8 + off];
        var lift = NibbleSwap(entry[9 + off]) & 0xf;
        var data2 = entry[10 + off] + 256 * entry[11 + off];
        var data3 = testlen >= 14 && 13 + off < entry.Length
            ? entry[12 + off] + 256 * entry[13 + off]
            : 0;

        var wcx = scx + cx;
        var wcy = scy + cy;
        if ((uint)wcx >= U7Constants.NumChunks || (uint)wcy >= U7Constants.NumChunks)
        {
            return null;
        }

        var egg = MakeObject(
            wcx * 16 + tilex, wcy * 16 + tiley, lift, shape, frame, data1 & 0xff, ObjectKind.Ireg);
        egg.Solid = false;
        egg.IsEgg = true;
        FillEgg(egg, itype, prob, data1, data2, data3);
        ChunkObjects[wcx][wcy].Add(egg);
        Eggs.Add(egg);
        UpdateEgg(egg, add: true);
        if (egg.EggType == U7.World.EggType.Path)
        {
            if (!_pathEggs.TryGetValue(egg.Quality, out var list))
            {
                list = new List<U7Object>();
                _pathEggs[egg.Quality] = list;
            }

            list.Add(egg);
        }

        return egg;
    }

    /// <summary>
    /// Exult <c>Game_map::cache_out</c>: once the avatar is in a new superchunk,
    /// temporary objects (spawned monsters, their corpses, temporary items)
    /// outside the surrounding 3×3 superchunks are deleted. Returns the count.
    /// </summary>
    public int CacheOut(int avatarTx, int avatarTy)
    {
        const int T = U7Constants.TilesPerSuperchunk;
        var sx = avatarTx / T;
        var sy = avatarTy / T;
        var removed = 0;
        for (var cy = 0; cy < U7Constants.NumChunks; cy++)
        {
            var scy = cy / 16;
            var dy = Math.Abs(scy - sy);
            dy = Math.Min(dy, 12 - dy);
            if (dy <= 1)
            {
                continue;
            }

            for (var cx = 0; cx < U7Constants.NumChunks; cx++)
            {
                var scx = cx / 16;
                var dx = Math.Abs(scx - sx);
                dx = Math.Min(dx, 12 - dx);
                if (dx <= 1)
                {
                    continue;
                }

                var list = ChunkObjects[cx][cy];
                for (var i = list.Count - 1; i >= 0; i--)
                {
                    var obj = list[i];
                    var temp = obj.IsActor ? obj.NpcNum < 0 : obj.Kind == ObjectKind.Ireg && obj.GetFlag(U7.Actors.ObjFlag.Temporary);
                    if (temp && !obj.Removed)
                    {
                        RemoveObject(obj);
                        removed++;
                    }
                }
            }
        }

        return removed;
    }

    // ------------------------------------------------------------------ saving

    /// <summary>Exult <c>Game_map::write_ireg</c>: one U7IREGxx file per superchunk.</summary>
    public void WriteIregFiles(string dir)
    {
        for (var schunk = 0; schunk < U7Constants.NumSuperchunks; schunk++)
        {
            var scy = 16 * (schunk / 12);
            var scx = 16 * (schunk % 12);
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            for (var cy = 0; cy < 16; cy++)
            {
                for (var cx = 0; cx < 16; cx++)
                {
                    foreach (var obj in ChunkObjects[scx + cx][scy + cy])
                    {
                        if (obj.Kind != ObjectKind.Ireg || obj.IsActor || obj.Removed || obj.Container is not null)
                        {
                            continue;
                        }

                        WriteIregObject(w, obj, contained: false);
                    }

                    w.Write((ushort)0); // end of chunk
                }
            }

            w.Flush();
            File.WriteAllBytes(Path.Combine(dir, $"U7IREG{schunk:X2}"), ms.ToArray());
        }
    }

    /// <summary>Exult <c>Actor::write_contents</c>: readied items first with their spot, then the rest.</summary>
    public void WriteActorContents(BinaryWriter w, U7Object actor)
    {
        foreach (var item in actor.Contents)
        {
            if (item.Removed || item.ReadySlot < 0)
            {
                continue;
            }

            w.Write((byte)2);
            w.Write((ushort)item.ReadySlot);
            WriteIregObject(w, item, contained: true);
        }

        foreach (var item in actor.Contents)
        {
            if (item.Removed || item.ReadySlot >= 0)
            {
                continue;
            }

            w.Write((byte)2);
            w.Write((ushort)0xff);
            WriteIregObject(w, item, contained: true);
        }

        w.Write((byte)1);
    }

    /// <summary>Exult <c>Game_map::write_scheduled</c>: IREG_SPECIAL + IREG_UCSCRIPT + length + script.</summary>
    public void WriteScheduled(BinaryWriter w, U7Object obj)
    {
        if (ScriptSaver is null)
        {
            return;
        }

        foreach (var blob in ScriptSaver(obj))
        {
            if (blob.Length == 0 || blob.Length > ushort.MaxValue)
            {
                continue;
            }

            w.Write((byte)255);
            w.Write((byte)1);
            w.Write((ushort)blob.Length);
            w.Write(blob);
        }
    }

    /// <summary>Exult <c>Ireg_game_object</c> / <c>Container_game_object</c> / <c>Egg_object::write_ireg</c>.</summary>
    void WriteIregObject(BinaryWriter w, U7Object obj, bool contained)
    {
        WriteIregEntry(w, obj, contained);
        WriteScheduled(w, obj);
    }

    void WriteIregEntry(BinaryWriter w, U7Object obj, bool contained)
    {
        var info = Catalog[obj.Shape];
        if (obj.IsEgg)
        {
            var sz = obj.EggData3 > 0 ? 14 : 12;
            WriteCommonIreg(w, obj, sz, contained);
            var flags = obj.EggFlags;
            var tword = obj.EggType & 0xf;
            tword |= (obj.EggCriteria & 7) << 4;
            tword |= ((flags & U7.World.EggFlag.Nocturnal) != 0 ? 1 : 0) << 7;
            tword |= ((flags & U7.World.EggFlag.Once) != 0 ? 1 : 0) << 8;
            tword |= ((flags & U7.World.EggFlag.Hatched) != 0 ? 1 : 0) << 9;
            tword |= (obj.EggDistance & 0x1f) << 10;
            tword |= ((flags & U7.World.EggFlag.AutoReset) != 0 ? 1 : 0) << 15;
            w.Write((ushort)tword);
            w.Write((byte)obj.EggProbability);
            w.Write((ushort)obj.EggData1);
            w.Write((byte)NibbleSwap(obj.Tz));
            w.Write((ushort)obj.EggData2);
            if (obj.EggData3 > 0)
            {
                w.Write((ushort)obj.EggData3);
            }

            return;
        }

        var invisible = obj.GetFlag(0);
        var okayToTake = obj.GetFlag(U7.Actors.ObjFlag.OkayToTake);
        var flagByte = (byte)((invisible ? 1 : 0) | (okayToTake ? 1 << 3 : 0));
        var live = obj.Contents.Where(c => !c.Removed).ToList();
        if (U7.Actors.Bodies.IsBodyShape(obj.Shape))
        {
            // Exult Dead_body::write_ireg: 13-byte entry carrying the NPC number.
            WriteCommonIreg(w, obj, 13, contained);
            w.Write((ushort)(live.Count > 0 ? live[^1].Shape : 0));
            w.Write((byte)0);
            w.Write((byte)obj.Quality);
            w.Write((ushort)(obj.LiveNpcNum < 0 ? 0xffff : obj.LiveNpcNum));
            w.Write((byte)NibbleSwap(obj.Tz));
            w.Write((byte)0); // hp
            w.Write(flagByte);
            if (live.Count > 0)
            {
                foreach (var item in live)
                {
                    WriteIregObject(w, item, contained: true);
                }

                w.Write((byte)1);
            }

            return;
        }

        if (info.IsContainerClass || live.Count > 0)
        {
            WriteCommonIreg(w, obj, 12, contained);
            w.Write((ushort)(live.Count > 0 ? live[^1].Shape : 0));
            w.Write((byte)0);
            w.Write((byte)obj.Quality);
            w.Write((byte)0);
            w.Write((byte)NibbleSwap(obj.Tz));
            w.Write((byte)0); // resistance
            w.Write(flagByte);
            if (live.Count > 0)
            {
                foreach (var item in live)
                {
                    WriteIregObject(w, item, contained: true);
                }

                w.Write((byte)1);
            }

            return;
        }

        WriteCommonIreg(w, obj, 10, contained);
        w.Write((byte)NibbleSwap(obj.Tz));
        var value = obj.Quality;
        if (info.HasQualityFlags)
        {
            value = flagByte;
        }
        else if (okayToTake && info.HasQuantity)
        {
            value |= 0x80;
        }

        w.Write((byte)value);
        w.Write((byte)(obj.GetFlag(U7.Actors.ObjFlag.Temporary) ? 1 : 0));
        w.Write((byte)0);
        w.Write((byte)0);
        w.Write((byte)0);
    }

    /// <summary>Exult <c>Ireg_game_object::write_common_ireg</c>.</summary>
    static void WriteCommonIreg(BinaryWriter w, U7Object obj, int normLen, bool contained)
    {
        byte x;
        byte y;
        if (contained)
        {
            x = (byte)obj.Tx;
            y = (byte)obj.Ty;
        }
        else
        {
            x = (byte)((((obj.Tx / 16) % 16) << 4) | (obj.Tx % 16));
            y = (byte)((((obj.Ty / 16) % 16) << 4) | (obj.Ty % 16));
        }

        if (obj.Shape >= 1024 || obj.Frame >= 64)
        {
            w.Write((byte)254); // IREG_EXTENDED
            w.Write((byte)(normLen + 1));
            w.Write(x);
            w.Write(y);
            w.Write((byte)(obj.Shape & 0xff));
            w.Write((byte)(obj.Shape >> 8));
            w.Write((byte)obj.Frame);
            return;
        }

        if (obj.Tz > 15)
        {
            w.Write((byte)253); // IREG_EXTENDED2
        }

        w.Write((byte)normLen);
        w.Write(x);
        w.Write(y);
        w.Write((byte)(obj.Shape & 0xff));
        w.Write((byte)(((obj.Shape >> 8) & 3) | (obj.Frame << 2)));
    }

    static void FillEgg(U7Object egg, int itype, int prob, int data1, int data2, int data3)
    {
        var type = itype & 0xf;
        if (type == U7.World.EggType.Teleport && egg.Frame == U7Constants.PathEggFrame &&
            egg.Shape == U7Constants.EggShape)
        {
            type = U7.World.EggType.Path;
        }

        var criteria = (itype & (7 << 4)) >> 4;
        var distance = (itype >> 10) & 0x1f;
        var nocturnal = (itype >> 7) & 1;
        var once = (itype >> 8) & 1;
        var hatched = type == U7.World.EggType.Missile ? 0 : (itype >> 9) & 1;
        var autoReset = (itype >> 15) & 1;
        var flags = (nocturnal * U7.World.EggFlag.Nocturnal) |
                    (once * U7.World.EggFlag.Once) |
                    (hatched * U7.World.EggFlag.Hatched) |
                    (autoReset * U7.World.EggFlag.AutoReset);
        if (criteria == U7.World.EggCriteria.PartyNear && (flags & U7.World.EggFlag.AutoReset) != 0)
        {
            criteria = U7.World.EggCriteria.AvatarNear;
        }

        egg.EggType = type;
        egg.EggCriteria = criteria;
        egg.EggDistance = distance;
        egg.EggProbability = prob;
        egg.EggData1 = data1;
        egg.EggData2 = data2;
        egg.EggData3 = data3;
        egg.EggFlags = flags;
        egg.Quality = data1 & 0xff;
        SetEggArea(egg);
    }

    static void SetEggArea(U7Object egg)
    {
        if (egg.EggProbability == 0 || egg.EggType == U7.World.EggType.Path)
        {
            egg.EggAreaW = 0;
            egg.EggAreaH = 0;
            return;
        }

        var t = egg;
        var dist = egg.EggDistance;
        switch (egg.EggCriteria)
        {
            case U7.World.EggCriteria.CachedIn:
                egg.EggAreaX = t.Tx - 32;
                egg.EggAreaY = t.Ty - 32;
                egg.EggAreaW = 64;
                egg.EggAreaH = 64;
                break;
            case U7.World.EggCriteria.AvatarFootpad:
            case U7.World.EggCriteria.PartyFootpad:
                egg.EggAreaX = t.Tx - t.DimX + 1;
                egg.EggAreaY = t.Ty - t.DimY + 1;
                egg.EggAreaW = t.DimX;
                egg.EggAreaH = t.DimY;
                break;
            case U7.World.EggCriteria.AvatarFar:
                egg.EggAreaX = t.Tx - dist - 1;
                egg.EggAreaY = t.Ty - dist - 1;
                egg.EggAreaW = 2 * dist + 3;
                egg.EggAreaH = 2 * dist + 3;
                break;
            default:
            {
                var width = 2 * dist + 1;
                if (dist <= 1 && egg.EggCriteria == U7.World.EggCriteria.External)
                {
                    width += 2;
                }

                egg.EggAreaX = t.Tx - dist;
                egg.EggAreaY = t.Ty - dist;
                egg.EggAreaW = width;
                egg.EggAreaH = width;
                break;
            }
        }
    }

    void LogEggSummary()
    {
        var counts = new int[12];
        var trinsic = 0;
        foreach (var e in Eggs)
        {
            if ((uint)e.EggType < (uint)counts.Length)
            {
                counts[e.EggType]++;
            }

            if (Math.Abs(e.Tx - U7Constants.StartTileX) <= 80 &&
                Math.Abs(e.Ty - U7Constants.StartTileY) <= 80)
            {
                trinsic++;
            }
        }

        Console.WriteLine(
            $"eggs: {Eggs.Count} total, {trinsic} near Trinsic start; " +
            $"teleport={counts[U7.World.EggType.Teleport]} usecode={counts[U7.World.EggType.Usecode]} " +
            $"jukebox={counts[U7.World.EggType.Jukebox]} button={counts[U7.World.EggType.Button]} " +
            $"path={counts[U7.World.EggType.Path]} monster={counts[U7.World.EggType.Monster]}");
    }

    U7Object MakeObject(int tx, int ty, int tz, int shape, int frame, int quality, ObjectKind kind)
    {
        var info = Catalog[shape];
        var reflected = (frame & 32) != 0;
        return new U7Object
        {
            Tx = tx,
            Ty = ty,
            Tz = tz,
            Shape = shape,
            Frame = frame,
            Quality = quality,
            Kind = kind,
            DimX = reflected ? info.DimY : info.DimX,
            DimY = reflected ? info.DimX : info.DimY,
            DimZ = info.DimZ,
            Solid = info.Solid
        };
    }

    static int NibbleSwap(int val) => ((val << 4) | (val >> 4)) & 0xff;
}
