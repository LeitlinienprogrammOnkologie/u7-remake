using System.Collections.Generic;
using Godot;
using U7.Core;
using U7.Data;

namespace U7.Rendering;

/// <summary>
/// Paints the visible map using Exult's screen mapping:
/// <c>x = (tx+1)*8 - 1 - 4*tz</c>, hotspot is (xleft, yabove) from the frame.
/// Flat 8×8 tiles are cached per chunk as 128×128 textures.
/// </summary>
public partial class WorldView : Node2D
{
    public GameMap Map = null!;
    public ShapeCatalog Catalog = null!;
    public ShapeCache Shapes = null!;
    public U7Object Avatar = null!;
    public int SkipAboveLift = U7Constants.NoRoof;
    public int InDungeonLift;

    readonly Dictionary<Vector2I, Texture2D> _chunkFlats = new();
    readonly Queue<Vector2I> _chunkOrder = new();
    const int MaxCachedChunks = 220;
    uint _renderSeq;
    uint _frameNo;
    int _paintCounter;

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.Nearest;
        ZIndex = 0;
    }

    public override void _Process(double delta)
    {
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Map is null || Avatar is null)
        {
            return;
        }

        SkipAboveLift = Map.RoofHeight(Avatar.Tx, Avatar.Ty, Avatar.Tz);
        InDungeonLift = Map.DungeonHeight(Avatar.Tx, Avatar.Ty);
        _frameNo++;
        _paintCounter = 0;

        var cam = GetViewport().GetCamera2D();
        var view = GetViewport().GetVisibleRect().Size;
        var zoom = cam is not null ? cam.Zoom : Vector2.One;
        var center = cam is not null ? cam.GlobalPosition : Vector2.Zero;
        var half = view / zoom / 2f;
        var margin = 160f;
        var left = center.X - half.X - margin;
        var top = center.Y - half.Y - margin;
        var right = center.X + half.X + margin;
        var bottom = center.Y + half.Y + margin;

        var c0x = Mathf.FloorToInt(left / U7Constants.ChunkSizePixels) - 1;
        var c0y = Mathf.FloorToInt(top / U7Constants.ChunkSizePixels) - 1;
        var c1x = Mathf.CeilToInt(right / U7Constants.ChunkSizePixels) + 1;
        var c1y = Mathf.CeilToInt(bottom / U7Constants.ChunkSizePixels) + 1;

        for (var cy = c0y; cy <= c1y; cy++)
        {
            for (var cx = c0x; cx <= c1x; cx++)
            {
                var wcx = U7Constants.WrapChunk(cx);
                var wcy = U7Constants.WrapChunk(cy);
                var tex = GetChunkFlat(wcx, wcy);
                if (tex is null)
                {
                    continue;
                }

                DrawTexture(tex, new Vector2(cx * U7Constants.ChunkSizePixels, cy * U7Constants.ChunkSizePixels));
            }
        }

        var ticks = Time.GetTicksMsec();

        // Exult Game_render::paint_map: flat RLE objects for every chunk first ...
        for (var cy = c0y; cy <= c1y; cy++)
        {
            for (var cx = c0x; cx <= c1x; cx++)
            {
                Map.EnsureChunkOrdered(cx, cy);
                foreach (var obj in Map.ObjectsInChunk(cx, cy))
                {
                    if (obj.IsFlat && !obj.Removed && !obj.InvisibleEgg && obj.Container is null)
                    {
                        DrawObject(obj, ticks);
                    }
                }
            }
        }

        // ... then non-flat objects chunk by chunk, each after its dependencies.
        _renderSeq++;
        for (var cy = c0y; cy <= c1y; cy++)
        {
            for (var cx = c0x; cx <= c1x; cx++)
            {
                var list = Map.ObjectsInChunk(cx, cy);
                for (var i = 0; i < list.Count; i++)
                {
                    var obj = list[i];
                    if (!obj.IsFlat && obj.RenderSeq != _renderSeq)
                    {
                        PaintObject(obj, ticks);
                    }
                }
            }
        }

        if (InDungeonLift != 0 && InDungeonLift >= SkipAboveLift)
        {
            PaintDungeonBlackness(c0x, c0y, c1x, c1y);
        }

        // Exult paints text effects after the map; the bark overlay draws these on screen.
        _barkBack.Clear();
        foreach (var obj in _barks)
        {
            _barkBack.Add(MakeBark(obj));
        }

        (_barkInfo, _barkBack) = (_barkBack, _barkInfo);
        _barks.Clear();
    }

    /// <summary>
    /// Top-most object whose sprite contains <paramref name="world"/> (alpha pick).
    /// </summary>
    public U7Object? PickObject(Vector2 world)
    {
        if (Map is null)
        {
            return null;
        }

        // Pick what is visibly on top: the object painted last in the most recent frame.
        var tile = WorldToTile(world);
        var cx = tile.Tx / U7Constants.TilesPerChunk;
        var cy = tile.Ty / U7Constants.TilesPerChunk;
        U7Object? best = null;
        var bestStamp = long.MinValue;
        for (var dy = -2; dy <= 2; dy++)
        {
            for (var dx = -2; dx <= 2; dx++)
            {
                foreach (var obj in Map.ObjectsInChunk(cx + dx, cy + dy))
                {
                    if (!PaintedRecently(obj) || !SpriteContains(obj, world))
                    {
                        continue;
                    }

                    if (obj.PaintStamp >= bestStamp)
                    {
                        bestStamp = obj.PaintStamp;
                        best = obj;
                    }
                }
            }
        }

        return best ?? PickByTile(tile);
    }

    /// <summary>Drawn in this or the previous frame (PickObject may run before this frame's _Draw).</summary>
    bool PaintedRecently(U7Object obj) =>
        !obj.Removed && obj.Container is null && (obj.PaintStamp >> 32) + 1 >= _frameNo;

    U7Object? PickByTile(TileCoord tile)
    {
        U7Object? best = null;
        var bestStamp = long.MinValue;
        foreach (var obj in Map.ObjectsInChunk(tile.ChunkX, tile.ChunkY))
        {
            if (!PaintedRecently(obj) || !obj.Occupies(tile.Tx, tile.Ty))
            {
                continue;
            }

            if (obj.PaintStamp >= bestStamp)
            {
                bestStamp = obj.PaintStamp;
                best = obj;
            }
        }

        return best;
    }

    bool SpriteContains(U7Object obj, Vector2 world)
    {
        var info = Catalog[obj.Shape];
        var frame = obj.Frame;
        var tex = Shapes.Get(obj.Shape, frame);
        if (tex is null && (frame & 32) != 0)
        {
            tex = Shapes.Get(obj.Shape, frame & 0x1f);
            frame &= 0x1f;
        }

        if (tex is null)
        {
            var t = WorldToTile(world, obj.Tz);
            return obj.Occupies(t.Tx, t.Ty);
        }

        var fi = info.GetFrame(frame);
        ShapeLocation(obj.Tx, obj.Ty, obj.Tz, out var hx, out var hy);
        var pos = new Vector2(hx - fi.XLeft, hy - fi.YAbove);
        var size = tex.GetSize();
        if (world.X < pos.X || world.Y < pos.Y || world.X >= pos.X + size.X || world.Y >= pos.Y + size.Y)
        {
            return false;
        }

        var image = Shapes.GetImage(obj.Shape, frame);
        if (image is null)
        {
            return true;
        }

        var px = (int)(world.X - pos.X);
        var py = (int)(world.Y - pos.Y);
        if (px < 0 || py < 0 || px >= image.GetWidth() || py >= image.GetHeight())
        {
            return false;
        }

        return image.GetPixel(px, py).A > 0.08f;
    }

    void DrawBark(U7Object obj, ulong ticks)
    {
        if (obj.BarkUntilMsec == 0 || ticks > obj.BarkUntilMsec)
        {
            if (obj.BarkUntilMsec != 0)
            {
                obj.BarkText = "";
                obj.BarkUntilMsec = 0;
            }

            return;
        }

        if (string.IsNullOrEmpty(obj.BarkText))
        {
            return;
        }

        _barks.Add(obj);
    }

    readonly List<U7Object> _barks = new();
    List<BarkInfo> _barkInfo = new();
    List<BarkInfo> _barkBack = new();

    /// <summary>A bark to draw: its text and the top centre of the speaker's sprite in world pixels.</summary>
    public readonly record struct BarkInfo(string Text, Vector2 WorldTop);

    /// <summary>Barks painted in the last frame (drawn on screen by the bark overlay).</summary>
    public IReadOnlyList<BarkInfo> Barks => _barkInfo;

    /// <summary>
    /// Exult <c>Text_effect</c>: one line per speaker, the '@' quote marks shown
    /// as '"'. Anchored at the top centre of the shape rectangle.
    /// </summary>
    BarkInfo MakeBark(U7Object obj)
    {
        var text = obj.BarkText;
        if (text.StartsWith('@'))
        {
            text = '"' + text[1..];
        }

        if (text.EndsWith('@'))
        {
            text = text[..^1] + '"';
        }

        var fi = Catalog[obj.Shape].GetFrame(obj.Frame);
        ShapeLocation(obj.Tx, obj.Ty, obj.Tz, out var hx, out var hy);
        var left = hx - fi.XLeft;
        var width = fi.XLeft + fi.XRight + 1;
        return new BarkInfo(text, new Vector2(left + width / 2f, hy - fi.YAbove));
    }

    /// <summary>Exult <c>Game_render::paint_object</c>: dependencies first, then the object.</summary>
    void PaintObject(U7Object obj, ulong ticks)
    {
        if (obj.Tz >= SkipAboveLift)
        {
            return;
        }

        obj.RenderSeq = _renderSeq;
        if (obj.Dependencies is { Count: > 0 } deps)
        {
            foreach (var dep in deps)
            {
                if (dep.RenderSeq != _renderSeq && !dep.Removed)
                {
                    PaintObject(dep, ticks);
                }
            }
        }

        if (obj.Removed || obj.Container is not null || obj.InvisibleEgg ||
            (obj.IsActor && obj.GetFlag(U7.Actors.ObjFlag.DontMove)))
        {
            return;
        }

        DrawObject(obj, ticks);
        DrawBark(obj, ticks);
    }

    void DrawObject(U7Object obj, ulong ticks)
    {
        var info = Catalog[obj.Shape];
        var frame = obj.Frame;
        if (info.Animated && info.FrameCount > 1 && !obj.IsActor)
        {
            frame = (int)((ticks / 180) % (ulong)info.FrameCount);
        }

        var tex = Shapes.Get(obj.Shape, frame);
        if (tex is null && (frame & 32) != 0)
        {
            tex = Shapes.Get(obj.Shape, frame & 0x1f);
            frame &= 0x1f;
        }

        if (tex is null)
        {
            return;
        }

        var fi = info.GetFrame(frame);
        ShapeLocation(obj.Tx, obj.Ty, obj.Tz, out var hx, out var hy);
        var pos = new Vector2(hx - fi.XLeft, hy - fi.YAbove);
        var hit = ticks < obj.HitUntilMsec;
        DrawTexture(tex, pos, hit ? new Color(1f, 0.35f, 0.35f) : Colors.White);
        obj.PaintStamp = ((long)_frameNo << 32) | (uint)(++_paintCounter);
    }

    void PaintDungeonBlackness(int c0x, int c0y, int c1x, int c1y)
    {
        var off = 4 * InDungeonLift;
        var black = new Color(0, 0, 0, 1);
        var chunkPx = U7Constants.ChunkSizePixels;
        var tilePx = U7Constants.TileSize;
        for (var cy = c0y; cy <= c1y; cy++)
        {
            for (var cx = c0x; cx <= c1x; cx++)
            {
                var origin = new Vector2(cx * chunkPx - off, cy * chunkPx - off);
                if (!Map.ChunkHasDungeon(cx, cy))
                {
                    DrawRect(new Rect2(origin, new Vector2(chunkPx, chunkPx)), black);
                    continue;
                }

                for (var ly = 0; ly < U7Constants.TilesPerChunk; ly++)
                {
                    var run = 0;
                    var runX = 0;
                    for (var lx = 0; lx < U7Constants.TilesPerChunk; lx++)
                    {
                        if (Map.DungeonHeightLocal(cx, cy, lx, ly) == 0)
                        {
                            if (run == 0)
                            {
                                runX = lx;
                            }

                            run++;
                        }
                        else if (run != 0)
                        {
                            DrawRect(
                                new Rect2(
                                    origin.X + runX * tilePx, origin.Y + ly * tilePx,
                                    run * tilePx, tilePx),
                                black);
                            run = 0;
                        }
                    }

                    if (run != 0)
                    {
                        DrawRect(
                            new Rect2(
                                origin.X + runX * tilePx, origin.Y + ly * tilePx,
                                run * tilePx, tilePx),
                            black);
                    }
                }
            }
        }
    }

    public static void ShapeLocation(int tx, int ty, int tz, out int x, out int y)
    {
        // Exult Game_window::Get_shape_location with scroll = 0.
        var lift = 4 * tz;
        x = (tx + 1) * U7Constants.TileSize - 1 - lift;
        y = (ty + 1) * U7Constants.TileSize - 1 - lift;
    }

    public static Vector2 AvatarCameraPoint(U7Object avatar)
    {
        ShapeLocation(avatar.Tx, avatar.Ty, avatar.Tz, out var x, out var y);
        return new Vector2(x, y);
    }

    public static TileCoord WorldToTile(Vector2 world, int lift = 0)
    {
        var liftPx = 4 * lift;
        var tx = Mathf.FloorToInt((world.X + 1 + liftPx) / (float)U7Constants.TileSize) - 1;
        var ty = Mathf.FloorToInt((world.Y + 1 + liftPx) / (float)U7Constants.TileSize) - 1;
        return new TileCoord(U7Constants.WrapTile(tx), U7Constants.WrapTile(ty), lift);
    }

    Texture2D? GetChunkFlat(int cx, int cy)
    {
        var key = new Vector2I(cx, cy);
        if (_chunkFlats.TryGetValue(key, out var tex))
        {
            return tex;
        }

        tex = BuildChunkFlat(cx, cy);
        _chunkFlats[key] = tex;
        _chunkOrder.Enqueue(key);
        while (_chunkOrder.Count > MaxCachedChunks)
        {
            var old = _chunkOrder.Dequeue();
            if (old == key)
            {
                continue;
            }

            _chunkFlats.Remove(old);
        }

        return tex;
    }

    Texture2D BuildChunkFlat(int cx, int cy)
    {
        var image = Image.CreateEmpty(
            U7Constants.ChunkSizePixels, U7Constants.ChunkSizePixels, false, Image.Format.Rgba8);
        image.Fill(new Color(0, 0, 0, 0));

        var tnum = Map.TerrainMap[cx, cy];
        var cells = tnum < Map.Terrains.Length ? Map.Terrains[tnum] : null;
        if (cells is not null)
        {
            for (var ly = 0; ly < 16; ly++)
            {
                for (var lx = 0; lx < 16; lx++)
                {
                    var cell = cells[ly * 16 + lx];
                    if (cell.IsRle)
                    {
                        continue;
                    }

                    var src = Shapes.GetImage(cell.Shape, cell.Frame);
                    if (src is null)
                    {
                        continue;
                    }

                    var dest = new Rect2I(lx * 8, ly * 8, 8, 8);
                    var srcRect = new Rect2I(0, 0, Math.Min(8, src.GetWidth()), Math.Min(8, src.GetHeight()));
                    image.BlitRect(src, srcRect, dest.Position);
                }
            }
        }

        return ImageTexture.CreateFromImage(image);
    }
}
