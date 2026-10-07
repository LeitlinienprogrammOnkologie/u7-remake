namespace U7.Core;

/// <summary>
/// 3D tile coordinate. <c>Tz</c> is lift (height), matching Exult <c>Tile_coord</c>.
/// </summary>
public readonly record struct TileCoord(int Tx, int Ty, int Tz)
{
    public TileCoord Wrapped() =>
        new(U7Constants.WrapTile(Tx), U7Constants.WrapTile(Ty), Tz);

    public int ChunkX => U7Constants.WrapTile(Tx) / U7Constants.TilesPerChunk;
    public int ChunkY => U7Constants.WrapTile(Ty) / U7Constants.TilesPerChunk;
    public int Superchunk =>
        SuperchunksPerSideY * U7Constants.SuperchunksPerSide + SuperchunksPerSideX;

    public int SuperchunksPerSideX => ChunkX / U7Constants.ChunksPerSuperchunk;
    public int SuperchunksPerSideY => ChunkY / U7Constants.ChunksPerSuperchunk;

    public int Distance2d(TileCoord other)
    {
        var dx = Math.Abs(U7Constants.TileDelta(Tx, other.Tx));
        var dy = Math.Abs(U7Constants.TileDelta(Ty, other.Ty));
        return Math.Max(dx, dy);
    }

    /// <summary>Exult <c>Tile_coord::distance</c>: the 2D distance or the lift difference, whichever is larger.</summary>
    public int Distance(TileCoord other) => Math.Max(Distance2d(other), Math.Abs(other.Tz - Tz));

    /// <summary>Exult <c>Tile_coord::get_neighbor</c> (0 north, clockwise).</summary>
    public TileCoord Neighbor(int dir) =>
        new(U7Constants.WrapTile(Tx + NeighborDx[dir]), U7Constants.WrapTile(Ty + NeighborDy[dir]), Tz);

    static readonly int[] NeighborDx = [0, 1, 1, 1, 0, -1, -1, -1];
    static readonly int[] NeighborDy = [-1, -1, 0, 1, 1, 1, 0, -1];

    public override string ToString() => $"({Tx},{Ty},{Tz})";
}
