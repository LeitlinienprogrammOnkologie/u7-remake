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
    readonly byte[]?[][] _dungeonLevels;

    public GameMap(ShapeCatalog catalog)
    {
        Catalog = catalog;
        ChunkObjects = new List<U7Object>[U7Constants.NumChunks][];
        _dungeonLevels = new byte[]?[U7Constants.NumChunks][];
        for (var x = 0; x < U7Constants.NumChunks; x++)
        {
            ChunkObjects[x] = new List<U7Object>[U7Constants.NumChunks];
            _dungeonLevels[x] = new byte[]?[U7Constants.NumChunks];
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
        var cx = U7Constants.WrapChunk(obj.Tx / U7Constants.TilesPerChunk);
        var cy = U7Constants.WrapChunk(obj.Ty / U7Constants.TilesPerChunk);
        ChunkObjects[cx][cy].Add(obj);
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

        var cx = U7Constants.WrapChunk(obj.Tx / U7Constants.TilesPerChunk);
        var cy = U7Constants.WrapChunk(obj.Ty / U7Constants.TilesPerChunk);
        ChunkObjects[cx][cy].Remove(obj);
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
        var ocx = U7Constants.WrapChunk(obj.Tx / U7Constants.TilesPerChunk);
        var ocy = U7Constants.WrapChunk(obj.Ty / U7Constants.TilesPerChunk);
        obj.Tx = U7Constants.WrapTile(newTx);
        obj.Ty = U7Constants.WrapTile(newTy);
        obj.Tz = newTz;
        var ncx = obj.ChunkX;
        var ncy = obj.ChunkY;
        if (ocx == ncx && ocy == ncy)
        {
            return;
        }

        ChunkObjects[ocx][ocy].Remove(obj);
        ChunkObjects[ncx][ncy].Add(obj);
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

        var cx = U7Constants.WrapChunk(obj.Tx / U7Constants.TilesPerChunk);
        var cy = U7Constants.WrapChunk(obj.Ty / U7Constants.TilesPerChunk);
        ChunkObjects[cx][cy].Remove(obj);
        if (obj.IsEgg)
        {
            Eggs.Remove(obj);
            if (obj.EggType == U7.World.EggType.Path &&
                _pathEggs.TryGetValue(obj.Quality, out var list))
            {
                list.Remove(obj);
            }
        }
    }

    public List<U7Object> EggsNear(int tx, int ty, int dist)
    {
        var result = new List<U7Object>();
        var chunks = Math.Max(1, (dist + 15) / 16 + 1);
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
            var name = Path.Combine(U7Paths.GameDatDir, $"U7IREG{schunk:X2}");
            if (!File.Exists(name))
            {
                continue;
            }

            var scy = 16 * (schunk / 12);
            var scx = 16 * (schunk % 12);
            var i = 0;
            ParseIreg(File.ReadAllBytes(name), ref i, scx, scy, null);
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
                        i += 2 + len;
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
                ReadIregEgg(entry, testlen, extended, scx, scy, cx, cy, tilex, tiley, shape, frame);
                continue;
            }

            if (testlen is 12 or 13)
            {
                ReadIregContainer(
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
            }
        }
    }

    static int ActorReadySlot(U7Object? container, int readyIndex) =>
        container is { IsActor: true } && readyIndex is >= 0 and < 12 ? readyIndex : -1;

    void ReadIregContainer(
        byte[] data, ref int i, ReadOnlySpan<byte> entry, int testlen,
        int scx, int scy, int cx, int cy, int tilex, int tiley,
        int shape, int frame, U7Object? parent, int readyIndex)
    {
        var info = Catalog[shape];
        var skip = info.IsBargeClass ||
                   info.Name.Contains("jawbone", StringComparison.OrdinalIgnoreCase);
        var type = entry[4] + 256 * entry[5];
        var lift = NibbleSwap(entry[9]) & 0xf;
        var quality = entry.Length > 7 ? entry[7] : 0;
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
                    ChunkObjects[wcx][wcy].Add(obj);
                }
            }
        }

        if (type != 0)
        {
            ParseIreg(data, ref i, scx, scy, obj ?? new U7Object());
        }
    }

    void ReadIregEgg(
        ReadOnlySpan<byte> entry, int testlen, bool extended,
        int scx, int scy, int cx, int cy, int tilex, int tiley, int shape, int frame)
    {
        var off = extended ? 1 : 0;
        if (4 + off + 8 > entry.Length)
        {
            return;
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
            return;
        }

        var egg = MakeObject(
            wcx * 16 + tilex, wcy * 16 + tiley, lift, shape, frame, data1 & 0xff, ObjectKind.Ireg);
        egg.Solid = false;
        egg.IsEgg = true;
        FillEgg(egg, itype, prob, data1, data2, data3);
        ChunkObjects[wcx][wcy].Add(egg);
        Eggs.Add(egg);
        if (egg.EggType == U7.World.EggType.Path)
        {
            if (!_pathEggs.TryGetValue(egg.Quality, out var list))
            {
                list = new List<U7Object>();
                _pathEggs[egg.Quality] = list;
            }

            list.Add(egg);
        }
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
