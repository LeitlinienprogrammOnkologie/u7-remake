using U7.Core;
using U7.Data;

namespace U7.World;

/// <summary>
/// The light-occlusion grid (docs/RENDERER.md 5.6, R6): per tile and lift,
/// whether something stops light, kept beside <see cref="ChunkBlocking"/>
/// from the same per-chunk object lists and updated as it is. Movement
/// blocking isn't light blocking: a window blocks walking and passes light,
/// a table blocks walking and casts no shadow worth the name. So each
/// shape has a light class (<see cref="LightClass"/>): glass (TFA
/// translucent) passes, opaque stops, none is ignored. The classes come
/// from <c>light_classes.txt</c> in the game's patch folder (the remake's
/// own file, as <c>shape_additions.txt</c> is) over a default rule: a
/// translucent shape is glass; a solid building piece, a door, or any solid
/// thing three lifts tall or more (mountains, trees, boulders) is opaque;
/// the rest, furniture included, is none. Actors never cast (they would
/// flicker). A door's footprint turns with its frame, so a closed door
/// stops light in the doorway and an open one beside it, as it blocks
/// walking. Roofs keep today's rule (<see cref="GameMap.CoverAbove"/>).
/// </summary>
public sealed class LightOcclusion
{
    public const int MaxLifts = 32;
    const int Tiles = U7Constants.TilesPerChunk * U7Constants.TilesPerChunk;

    public enum LightClass : byte
    {
        Default,
        None,
        Opaque,
        Glass
    }

    sealed class Chunk
    {
        /// <summary>[lift * 256 + tile]: opaque objects there, 0-3.</summary>
        public readonly byte[] Counts = new byte[MaxLifts * Tiles];
        /// <summary>[tile]: bit n set when lift n stops light.</summary>
        public readonly uint[] Mask = new uint[Tiles];
    }

    readonly GameMap _map;
    readonly Chunk?[] _chunks = new Chunk?[U7Constants.NumChunks * U7Constants.NumChunks];
    readonly LightClass[] _classes;

    public LightOcclusion(GameMap map)
    {
        _map = map;
        _classes = new LightClass[4096];
        Load();
    }

    /// <summary>
    /// <c>light_classes.txt</c>: sections <c>opaque</c>, <c>glass</c> and
    /// <c>none</c>, each entry a shape (<c>:889</c>) or a run (<c>:150-160</c>);
    /// a mod's file layers over the remake's.
    /// </summary>
    void Load()
    {
        foreach (var (file, _) in ExultDataFile.Layers("light_classes"))
        {
            foreach (var (section, cls) in new[] { ("opaque", LightClass.Opaque), ("glass", LightClass.Glass), ("none", LightClass.None) })
            {
                foreach (var (_, text) in file.Entries(section))
                {
                    var parts = text.Split('-');
                    if (!int.TryParse(parts[0].Trim(), out var first))
                    {
                        continue;
                    }

                    var last = parts.Length > 1 && int.TryParse(parts[1].Trim(), out var l) ? l : first;
                    for (var s = Math.Max(0, first); s <= last && s < _classes.Length; s++)
                    {
                        _classes[s] = cls;
                    }
                }
            }
        }
    }

    /// <summary>The shape's light class: the data file's, else the default rule.</summary>
    public LightClass ClassOf(int shape)
    {
        if ((uint)shape < (uint)_classes.Length && _classes[shape] != LightClass.Default)
        {
            return _classes[shape];
        }

        var info = _map.Catalog[shape];
        if (info.Translucent)
        {
            return LightClass.Glass;
        }

        if (info.Solid && info.DimZ > 0 && (info.IsBuilding || info.Door || info.DimZ >= 3))
        {
            return LightClass.Opaque;
        }

        return LightClass.None;
    }

    bool Stops(U7Object obj) => !obj.IsActor && !obj.IsEgg && obj.Solid && obj.DimZ > 0 && ClassOf(obj.Shape) == LightClass.Opaque;

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
                    if (Stops(obj) && !obj.Removed && obj.Container is null)
                    {
                        Mark(chunk, cx, cy, obj, add: true);
                    }
                }
            }
        }

        return chunk;
    }

    /// <summary>Add or remove an object in every built chunk its footprint covers (as <see cref="ChunkBlocking.Update"/>).</summary>
    public void Update(U7Object obj, bool add)
    {
        if (!Stops(obj))
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

    /// <summary>The tile's lifts that stop light, bit n for lift n.</summary>
    public uint Column(int tx, int ty)
    {
        tx = U7Constants.WrapTile(tx);
        ty = U7Constants.WrapTile(ty);
        var chunk = Need(tx / U7Constants.TilesPerChunk, ty / U7Constants.TilesPerChunk);
        return chunk.Mask[ty % U7Constants.TilesPerChunk * U7Constants.TilesPerChunk + tx % U7Constants.TilesPerChunk];
    }
}
