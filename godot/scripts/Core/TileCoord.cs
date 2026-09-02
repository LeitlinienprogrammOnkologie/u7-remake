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

    public override string ToString() => $"({Tx},{Ty},{Tz})";
}
