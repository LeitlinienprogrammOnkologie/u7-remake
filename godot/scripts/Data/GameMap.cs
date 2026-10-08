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
    readonly Dictionary<(int, int, int), int> _covers = new();
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
    /// <summary>Exult <c>Chunk_cache</c> blocked flags: what solid objects occupy, per tile and lift.</summary>
    public ChunkBlocking Blocking { get; }

    public GameMap(ShapeCatalog catalog)
    {
        Catalog = catalog;
        Blocking = new ChunkBlocking(this);
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

    /// <summary>A barge in barge mode when the map was read (Exult <c>set_moving_barge</c> on reading).</summary>
    public U7Object? LoadedMovingBarge { get; set; }

    /// <summary>Whether a barge is the one in barge mode, for saving.</summary>
    public Func<U7Object, bool>? IsMovingBarge { get; set; }

    /// <summary>Exult <c>Barge_object::move</c>: moving a barge (by usecode too) takes all on it along.</summary>
    public Action<U7Object, int, int, int>? MoveBarge { get; set; }

    /// <summary>
    /// Exult <c>Barge_object::finish_move</c>: take every object out of the
    /// world first, then put each back at its new place (and frame).
    /// </summary>
    public void MoveGroup(IReadOnlyList<U7Object> objs, IReadOnlyList<TileCoord> positions, IReadOnlyList<int>? frames = null)
    {
        foreach (var obj in objs)
        {
            RemoveFromChunk(obj);
            if (obj.IsEgg)
            {
                UpdateEgg(obj, add: false);
            }
        }

        for (var k = 0; k < objs.Count; k++)
        {
            var obj = objs[k];
            if (frames is not null)
            {
                SetFrameDims(obj, frames[k]);
            }

            obj.Tx = U7Constants.WrapTile(positions[k].Tx);
            obj.Ty = U7Constants.WrapTile(positions[k].Ty);
            obj.Tz = positions[k].Tz;
            if (obj.IsEgg)
            {
                SetEggArea(obj);
                UpdateEgg(obj, add: true);
            }

            InsertIntoChunk(obj);
        }
    }

    /// <summary>A new frame, and the footprint that goes with it (reflected frames swap x and y).</summary>
    void SetFrameDims(U7Object obj, int frame)
    {
        obj.Frame = frame;
        var info = Catalog[obj.Shape];
        var reflected = (frame & 32) != 0;
        obj.DimX = reflected ? info.DimY : info.DimX;
        obj.DimY = reflected ? info.DimX : info.DimY;
    }

    public void MoveObject(U7Object obj, int newTx, int newTy, int newTz)
    {
        if (obj.IsBarge && !obj.Removed && MoveBarge is { } moveBarge)
        {
            moveBarge(obj, newTx, newTy, newTz);
            return;
        }

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
        Blocking.Update(obj, add: true);
        if (Catalog[obj.Shape].IsBuilding)
        {
            _covers.Clear();
        }

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
        if (ChunkObjects[cx][cy].Remove(obj))
        {
            Blocking.Update(obj, add: false);
            if (Catalog[obj.Shape].IsBuilding)
            {
                _covers.Clear();
            }
        }

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
    /// <paramref name="dist"/> tiles of <paramref name="origin"/>, of the
    /// given quality and frame (-359 = any).
    /// </summary>
    public List<U7Object> FindNearby(
        TileCoord origin, int shape, int dist, int mask = 0,
        int qual = U7Constants.AnyShape, int frame = U7Constants.AnyShape)
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

                    if ((qual != U7Constants.AnyShape && obj.Quality != qual) ||
                        (frame != U7Constants.AnyShape && obj.Frame != frame))
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

    /// <summary>
    /// Exult <c>Game_object::find_nearby</c> with Exult's mask (objs/find_nearby.h),
    /// for usecode: NPCs count unless the mask narrows to NPC shapes (4) or
    /// live ones (8); eggs and barges only with 0x10, invisible objects only
    /// with 0x20 (or 0x40 for the party's), transparent shapes only with 0x80.
    /// A shape of -1 or -359 is any, and a shape given ignores mask 4. Objects
    /// whose tile is within <paramref name="delta"/> tiles each way (24 if
    /// negative), sorted right to left, near to far (Exult <c>Object_reverse_sorter</c>).
    /// </summary>
    public List<U7Object> FindNearbyExult(TileCoord pos, int shape, int delta, int mask,
        int qual = U7Constants.AnyShape, int frame = U7Constants.AnyShape)
    {
        if (delta < 0)
        {
            delta = 24;
        }

        if (shape > 0 && mask == 4)
        {
            mask = 0;
        }

        var result = new List<U7Object>();
        var chunks = delta / U7Constants.TilesPerChunk + 1;
        var ocx = pos.Tx / U7Constants.TilesPerChunk;
        var ocy = pos.Ty / U7Constants.TilesPerChunk;
        var span = Math.Min(2 * chunks + 1, U7Constants.NumChunks);
        for (var cy = ocy - chunks; cy < ocy - chunks + span; cy++)
        {
            for (var cx = ocx - chunks; cx < ocx - chunks + span; cx++)
            {
                foreach (var obj in ObjectsInChunk(cx, cy))
                {
                    if (obj.Removed || (shape >= 0 && obj.Shape != shape) ||
                        (qual != U7Constants.AnyShape && obj.Quality != qual) ||
                        (frame != U7Constants.AnyShape && obj.Frame != frame) ||
                        !FindMaskAllows(obj, mask))
                    {
                        continue;
                    }

                    if (Math.Abs(U7Constants.TileDelta(pos.Tx, obj.Tx)) <= delta &&
                        Math.Abs(U7Constants.TileDelta(pos.Ty, obj.Ty)) <= delta)
                    {
                        result.Add(obj);
                    }
                }
            }
        }

        result.Sort((a, b) => b.RenderOrder.CompareTo(a.RenderOrder));
        return result;
    }

    /// <summary>Exult <c>find_nearby</c>'s <c>Check_mask</c>.</summary>
    bool FindMaskAllows(U7Object obj, int mask)
    {
        var info = Catalog[obj.Shape];
        if ((mask & 4) != 0 && !info.IsNpcClass)
        {
            return false;
        }

        if ((mask & 8) != 0 && (!info.IsNpcClass || obj.IsDead))
        {
            return false;
        }

        if ((mask & 0x10) == 0 && info.ShapeClass is 7 or 9)
        {
            return false; // Eggs and barges.
        }

        if ((mask & 0x80) == 0 && info.Transparent)
        {
            return false;
        }

        return !obj.GetFlag(U7.Actors.ObjFlag.Invisible) || (mask & 0x20) != 0 ||
               ((mask & 0x40) != 0 && obj.GetFlag(U7.Actors.ObjFlag.InParty));
    }

    /// <summary>
    /// Whether a solid object occupies the tile at this lift (Exult
    /// <c>is_tile_occupied</c>), or it is water at ground level.
    /// </summary>
    public bool IsBlocked(int tx, int ty, int lift) =>
        Blocking.Test(tx, ty, lift) || (lift <= 0 && Catalog[GetFlat(tx, ty).Shape].Water);

    /// <summary>Exult <c>Game_object::blocks</c>: a solid object with height covers the tile at its lift.</summary>
    public static bool Blocks(U7Object obj, TileCoord tile)
    {
        if (obj.Tx < tile.Tx || obj.Ty < tile.Ty || obj.Tz > tile.Tz || !obj.Solid || obj.DimZ <= 0)
        {
            return false;
        }

        return tile.Tx > obj.Tx - obj.DimX && tile.Ty > obj.Ty - obj.DimY && tile.Tz < obj.Tz + obj.DimZ;
    }

    /// <summary>Exult <c>Game_object::find_blocking</c>: an object of the tile's chunk that blocks it.</summary>
    public U7Object? FindBlocking(TileCoord tile)
    {
        tile = tile.Wrapped();
        foreach (var obj in ObjectsInChunk(tile.ChunkX, tile.ChunkY))
        {
            if (!obj.Removed && Blocks(obj, tile))
            {
                return obj;
            }
        }

        return null;
    }

    /// <summary>Exult <c>Game_object::find_door</c>: a door of the tile's chunk that blocks it.</summary>
    public U7Object? FindDoor(TileCoord tile)
    {
        tile = tile.Wrapped();
        foreach (var obj in ObjectsInChunk(tile.ChunkX, tile.ChunkY))
        {
            if (!obj.Removed && Catalog[obj.Shape].Door && Blocks(obj, tile))
            {
                return obj;
            }
        }

        return null;
    }

    /// <summary>Exult <c>Game_object::is_closed_door</c>: a door with something solid at both ends of its long side.</summary>
    public bool IsClosedDoor(U7Object door)
    {
        if (!Catalog[door.Shape].Door)
        {
            return false;
        }

        var (before, after) = door.DimX > door.DimY
            ? (new TileCoord(door.Tx - door.DimX, door.Ty, door.Tz), new TileCoord(door.Tx + 1, door.Ty, door.Tz))
            : (new TileCoord(door.Tx, door.Ty - door.DimY, door.Tz), new TileCoord(door.Tx, door.Ty + 1, door.Tz));
        return Blocking.Test(before.Tx, before.Ty, before.Tz) && Blocking.Test(after.Tx, after.Ty, after.Tz);
    }

    /// <summary>
    /// Change a world object's shape (and footprint) in place, keeping the
    /// blocking flags right (Exult removes and re-adds it to its chunk).
    /// </summary>
    public void SetShape(U7Object obj, int shape)
    {
        var inWorld = obj.Container is null && !obj.Removed;
        if (inWorld)
        {
            Blocking.Update(obj, add: false);
            if (Catalog[obj.Shape].IsBuilding || Catalog[shape].IsBuilding)
            {
                _covers.Clear();
            }
        }

        obj.Shape = shape;
        var info = Catalog[shape];
        var reflected = (obj.Frame & 32) != 0;
        obj.DimX = reflected ? info.DimY : info.DimX;
        obj.DimY = reflected ? info.DimX : info.DimY;
        obj.DimZ = info.DimZ;
        obj.Solid = info.Solid;
        if (inWorld)
        {
            Blocking.Update(obj, add: true);
        }
    }

    /// <summary>Exult <c>Map_chunk::find_spot</c>, 1-tile version.</summary>
    /// <summary>
    /// Exult <c>Map_chunk::find_spot</c>: a free spot for an object of the
    /// shape and frame (its footprint and height), at <paramref name="pos"/>
    /// or on the squares around it out to <paramref name="dist"/>, starting
    /// on the preferred side (<paramref name="dir"/>, 0 north clockwise;
    /// random if -1). It may end up to <paramref name="maxDrop"/> lifts higher
    /// or lower. With <paramref name="inside"/> the squares' spots must be
    /// under a roof (true) or not (false), Exult's <c>Find_spot_where</c>.
    /// Null if there is none.
    /// </summary>
    public TileCoord? FindSpot(TileCoord pos, int dist, int shape, int frame, int maxDrop = 0, int dir = -1, bool? inside = null)
    {
        var info = Catalog[shape];
        var reflected = (frame & 32) != 0;
        var xs = Math.Max(1, reflected ? info.DimY : info.DimX);
        var ys = Math.Max(1, reflected ? info.DimX : info.DimY);
        var zs = info.DimZ;
        // MOVE_FLY here means: look upwards by max_drop too.
        const int flags = MoveFlags.Walk | MoveFlags.Fly;
        if (!Blocking.IsBlockedArea(zs, pos.Tz, pos.Tx - xs + 1, pos.Ty - ys + 1, xs, ys, out var lift, flags, maxDrop))
        {
            return pos.Wrapped() with { Tz = lift };
        }

        if (dir < 0)
        {
            dir = Random.Shared.Next(8);
        }

        dir = (dir + 1) % 8; // Make NW the 0 point.
        for (var d = 1; d <= dist; d++)
        {
            var count = 8 * d;
            var index = (dir * d - d / 2 + count) % count;
            for (var n = 0; n < count; n++, index++)
            {
                var p = SquareTile(pos, d, index % count);
                if (!Blocking.IsBlockedArea(zs, p.Tz, p.Tx - xs + 1, p.Ty - ys + 1, xs, ys, out lift, flags, maxDrop) &&
                    (inside is not { } want || want == (RoofHeight(p.Tx, p.Ty, lift) < U7Constants.NoRoof)))
                {
                    return p with { Tz = lift };
                }
            }
        }

        return null;
    }

    /// <summary>Exult <c>find_spot(pos, dist, obj)</c>: a spot near <paramref name="pos"/> for the object, preferring the side it comes from.</summary>
    public TileCoord? FindSpot(TileCoord pos, int dist, U7Object obj, int maxDrop = 0) =>
        FindSpot(pos, dist, obj.Shape, obj.Frame, maxDrop, ObjectGeometry.Direction(pos.Ty - obj.Ty, obj.Tx - pos.Tx));

    /// <summary>
    /// Exult <c>Get_square</c>: the i-th of the 8·<paramref name="dist"/> tiles
    /// on the square around <paramref name="pos"/>, from its northwest corner
    /// clockwise.
    /// </summary>
    static TileCoord SquareTile(TileCoord pos, int dist, int i)
    {
        var side = 2 * dist;
        int x, y;
        if (i <= side)
        {
            (x, y) = (-dist + i, -dist); // Along the top.
        }
        else if (i <= 2 * side)
        {
            (x, y) = (dist, -dist + i - side); // Down the right side.
        }
        else if (i <= 3 * side)
        {
            (x, y) = (dist - (i - 2 * side), dist); // Back along the bottom.
        }
        else
        {
            (x, y) = (-dist, dist - (i - 3 * side)); // Up the left side.
        }

        return new TileCoord(U7Constants.WrapTile(pos.Tx + x), U7Constants.WrapTile(pos.Ty + y), pos.Tz);
    }

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

    /// <summary>
    /// The lift of the lowest roof or upper floor over the tile that starts
    /// above <paramref name="lift"/>: a solid object of Exult's building class
    /// ("roof, window, mountain"), so not the wall a light hangs on, the table
    /// it stands on or a dish lying in a campfire. <see cref="U7Constants.NoRoof"/> if none.
    /// </summary>
    public int CoverAbove(int tx, int ty, int lift)
    {
        // Cached per tile until a building-class object comes, goes or changes;
        // walkers carrying lights keep adding tiles, so the cache is bounded.
        if (_covers.Count > 4096)
        {
            _covers.Clear();
        }

        if (!_covers.TryGetValue((tx, ty, lift), out var cover))
        {
            var height = LowestBlocked(tx, ty, lift + 1, buildingsAbove: true);
            cover = height < 0 ? U7Constants.NoRoof : height;
            _covers[(tx, ty, lift)] = cover;
        }

        return cover;
    }

    /// <summary>
    /// Whether something solid other than an actor fills the tile at the
    /// lift: the chunk cache's answer without the walkers (walls, closed
    /// doors, furniture).
    /// </summary>
    public bool StaticBlocked(int tx, int ty, int lift)
    {
        tx = U7Constants.WrapTile(tx);
        ty = U7Constants.WrapTile(ty);
        if (!Blocking.Test(tx, ty, lift))
        {
            return false;
        }

        var ocx = tx / U7Constants.TilesPerChunk;
        var ocy = ty / U7Constants.TilesPerChunk;
        for (var dcy = 0; dcy <= 1; dcy++)
        {
            for (var dcx = 0; dcx <= 1; dcx++)
            {
                foreach (var obj in ObjectsInChunk(ocx + dcx, ocy + dcy))
                {
                    if (!obj.Removed && !obj.IsActor && obj.BlocksAt(tx, ty, lift))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
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

    int LowestBlocked(int tx, int ty, int fromLift, bool buildingsAbove = false)
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
                    if (top < fromLift || (buildingsAbove && (obj.Tz < fromLift || !Catalog[obj.Shape].IsBuilding)))
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
    /// inventory. Length 0/1 ends a container. Items start out okay to take
    /// (Exult's default flags); a container's contents take its flags, but
    /// not invisibility.
    /// </summary>
    public void ReadIregObjects(byte[] data, ref int i, int scx, int scy, U7Object? container) =>
        ParseIreg(data, ref i, scx, scy, container);

    const uint OkayToTakeFlag = 1u << 11;

    /// <param name="untilEnd">Stop at the next end of a list (a barge's parts, placed in the world).</param>
    void ParseIreg(byte[] data, ref int i, int scx, int scy, U7Object? container, uint inherit = OkayToTakeFlag,
        bool untilEnd = false)
    {
        var readyIndex = -1;
        U7Object? last = null;
        while (i < data.Length)
        {
            var entlen = data[i++];
            if (entlen == 0 || entlen == 1)
            {
                if (container is not null || untilEnd)
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

            // (Exult skips an 18-byte entry that is not a spellbook as invalid.)
            if (testlen is not (6 or 10) && !(testlen == 18 && info.IsSpellbookClass))
            {
                continue;
            }

            var lift = NibbleSwap(entry[liftIndex]) & 0xf;
            var qualityIndex = extended ? 6 : 5;
            var quality = qualityIndex < entry.Length ? entry[qualityIndex] : 0;
            byte[]? circles = null;
            var bookmark = -1;
            if (testlen == 18)
            {
                // Exult Spellbook_object: five circles, the lift, four circles, three unknowns, the bookmark.
                var b = extended ? 1 : 0;
                circles = new byte[9];
                entry.Slice(4 + b, 5).CopyTo(circles);
                entry.Slice(10 + b, 4).CopyTo(circles.AsSpan(5));
                lift = NibbleSwap(entry[9 + b]) & 0xf;
                quality = 0;
                bookmark = entry[17 + b] == 255 ? -1 : entry[17 + b];
            }

            var flags = inherit;
            if (testlen == 10 && qualityIndex + 1 < entry.Length && (entry[qualityIndex + 1] & 1) != 0)
            {
                flags |= 1u << 18; // Temporary.
            }

            if (info.HasQuantity)
            {
                // Exult's "weird use of flag": the quantity's top bit is okay-to-take.
                if ((quality & 0x80) != 0)
                {
                    flags |= OkayToTakeFlag;
                    quality &= 0x7f;
                }
                else
                {
                    flags &= ~OkayToTakeFlag;
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
                nested.SpellCircles = circles;
                nested.SpellBookmark = bookmark;
                AddContained(container, nested, readyIndex);
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
                obj.SpellCircles = circles;
                obj.SpellBookmark = bookmark;
                ChunkObjects[wcx][wcy].Add(obj);
                last = obj;
            }
        }
    }

    /// <summary>
    /// Exult <c>read_ireg_objects</c>' add into a container: into an actor at
    /// the index record's spot (<c>add_readied</c>) when that is free, else
    /// with <c>Actor::add(obj, true)</c>: its best spot, a bag, or loose. The
    /// originals' inventories have no index records, so what they carry is
    /// worn; Exult's saves record each item's spot, 255 for none.
    /// </summary>
    void AddContained(U7Object container, U7Object obj, int readyIndex)
    {
        if (!container.IsActor)
        {
            obj.Container = container;
            container.Contents.Add(obj);
            return;
        }

        if (readyIndex is >= 0 and <= U7.Actors.ReadySpot.Ucont && U7.Actors.Equipment.GetReadied(container, readyIndex) is null)
        {
            obj.Container = container;
            obj.ReadySlot = readyIndex;
            container.Contents.Add(obj);
            return;
        }

        if (!U7.Actors.Equipment.AddToActor(container, obj, Catalog, this, dontCheck: true))
        {
            obj.Container = container;
            container.Contents.Add(obj);
        }
    }

    U7Object? ReadIregContainer(
        byte[] data, ref int i, ReadOnlySpan<byte> entry, int testlen,
        int scx, int scy, int cx, int cy, int tilex, int tiley,
        int shape, int frame, U7Object? parent, int readyIndex)
    {
        var info = Catalog[shape];
        var skip = info.Name.Contains("jawbone", StringComparison.OrdinalIgnoreCase);
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
                AddContained(parent, obj, readyIndex);
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

        if (info.IsVirtueStoneClass)
        {
            // Exult Virtue_stone_object: where it was marked (tile in its superchunk, superchunk, lift) and the map; no contents.
            if (obj is not null)
            {
                obj.VirtueTarget = new TileCoord(
                    entry[6] % 12 * U7Constants.TilesPerSuperchunk + entry[4],
                    entry[6] / 12 * U7Constants.TilesPerSuperchunk + entry[5], entry[7]);
                obj.VirtueMap = entry[10];
            }

            return obj;
        }

        if (info.IsBargeClass && obj is not null && parent is null)
        {
            // Exult Barge_object: size, facing in quality bits 1-2, barge mode in bit 3.
            obj.IsBarge = true;
            obj.BargeXTiles = entry[4];
            obj.BargeYTiles = entry[5];
            obj.BargeDir = (quality >> 1) & 3;
            if ((quality & 8) != 0)
            {
                LoadedMovingBarge ??= obj;
            }

            // Its "contents" (the rest of the chunk in the originals, none in saves) go into the world.
            ParseIreg(data, ref i, scx, scy, null, flags & ~1u, untilEnd: true);
            return obj;
        }

        if (type != 0)
        {
            ParseIreg(data, ref i, scx, scy, obj ?? new U7Object(), flags & ~1u); // Don't pass along invisibility.
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

        if (obj.IsBarge)
        {
            // Exult Barge_object::write_ireg: size, facing and barge mode; its parts are written as world objects.
            WriteCommonIreg(w, obj, 12, contained);
            w.Write((byte)obj.BargeXTiles);
            w.Write((byte)obj.BargeYTiles);
            w.Write((byte)0);
            w.Write((byte)((obj.BargeDir << 1) | (IsMovingBarge?.Invoke(obj) == true ? 8 : 0)));
            w.Write((byte)0);
            w.Write((byte)NibbleSwap(obj.Tz));
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((byte)1); // A 01 ends the (empty) list.
            return;
        }

        if (info.IsVirtueStoneClass)
        {
            // Exult Virtue_stone_object::write_ireg: the target's tile in its superchunk, the superchunk and lift, the stone's lift, the map.
            var t = obj.VirtueTarget;
            WriteCommonIreg(w, obj, 12, contained);
            w.Write((byte)(t.Tx % U7Constants.TilesPerSuperchunk));
            w.Write((byte)(t.Ty % U7Constants.TilesPerSuperchunk));
            w.Write((byte)(t.Ty / U7Constants.TilesPerSuperchunk * 12 + t.Tx / U7Constants.TilesPerSuperchunk));
            w.Write((byte)t.Tz);
            w.Write((byte)0);
            w.Write((byte)NibbleSwap(obj.Tz));
            w.Write((byte)obj.VirtueMap);
            w.Write((byte)0);
            return;
        }

        if (info.IsSpellbookClass)
        {
            // Exult Spellbook_object::write_ireg: the circles the way U7 stores them, around the lift (no flags).
            var circles = obj.SpellCircles ?? new byte[9];
            WriteCommonIreg(w, obj, 18, contained);
            w.Write(circles, 0, 5);
            w.Write((byte)NibbleSwap(obj.Tz));
            w.Write(circles, 5, 4);
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((byte)(obj.SpellBookmark >= 0 ? obj.SpellBookmark : 255));
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

    /// <summary>Exult <c>Game_map::create_ireg_object</c>: a new item that is not in the world yet.</summary>
    public U7Object CreateIregObject(int shape, int frame)
    {
        var obj = MakeObject(0, 0, 0, shape, frame, 0, ObjectKind.Ireg);
        obj.Removed = true;
        return obj;
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
