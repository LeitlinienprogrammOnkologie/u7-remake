using System.Collections.Generic;
using System.IO;
using U7.Core;
using U7.Data;

namespace U7.Rendering;

/// <summary>
/// The world's art at <see cref="Scale"/> times the original resolution, for
/// <see cref="WorldView"/> to paint: a hi-res pack's frames (the user's pick,
/// <see cref="ArtLook"/>), or where it has none the original frame blown up
/// (<see cref="ShapeFrame.Scaled"/>). Reflected frames (bit 5) are made from
/// the hi-res frame as Exult makes them from the original. Hit tests and
/// gumps keep the original frames.
/// </summary>
public sealed class HiresArt
{
    /// <summary>Bytes of hi-res frames kept before the cache starts over.</summary>
    const long CacheBudget = 256L << 20;

    readonly ShapeCache _shapes;
    HiresPack? _shapePack;
    HiresPack? _spritePack;
    HiresPack? _gumpPack;
    HiresPack? _facePack;
    HiresPack? _paperdollPack;
    HiresPack? _chunkPack;
    readonly Dictionary<(int Shape, int Frame), ShapeFrame?> _shapeFrames = new();
    readonly Dictionary<(int Sprite, int Frame), ShapeFrame?> _spriteFrames = new();
    long _cachedBytes;

    public HiresArt(ShapeCache shapes)
    {
        _shapes = shapes;
    }

    /// <summary>Buffer pixels per world pixel, each way, that the art is painted at.</summary>
    public int Scale { get; private set; } = 1;

    /// <summary>The pack the art comes from (assets/hires/&lt;name&gt;), null for the original art.</summary>
    public string? Pack { get; private set; }

    /// <summary>The scale the original art is blown up to while no pack is used.</summary>
    public void SetScale(int scale)
    {
        scale = Math.Clamp(scale, 1, 8);
        if (Pack is null && scale != Scale)
        {
            Scale = scale;
            Clear();
        }
    }

    /// <summary>
    /// The art of the pack <paramref name="name"/> (its shapes.hrp and
    /// sprites.hrp under assets/hires), at the pack's scale; null, or a pack
    /// that isn't there, for the original art at <paramref name="scale"/>.
    /// </summary>
    public void UsePack(string? name, int scale)
    {
        _shapePack = _spritePack = _gumpPack = _facePack = _paperdollPack = _chunkPack = null;
        Pack = null;
        Scale = Math.Clamp(scale, 1, 8);
        if (name is not null)
        {
            var dir = Path.Combine(U7Paths.HiresDir, name);
            _shapePack = HiresPack.Open(Path.Combine(dir, "shapes.hrp"));
            _spritePack = HiresPack.Open(Path.Combine(dir, "sprites.hrp"));
            _gumpPack = HiresPack.Open(Path.Combine(dir, "gumps.hrp"));
            _facePack = HiresPack.Open(Path.Combine(dir, "faces.hrp"));
            _paperdollPack = HiresPack.Open(Path.Combine(dir, "paperdolls.hrp"));
            _chunkPack = HiresPack.Open(Path.Combine(dir, "chunks.hrp"));
            if ((_shapePack ?? _spritePack ?? _gumpPack ?? _facePack ?? _paperdollPack ?? _chunkPack) is { } pack)
            {
                Pack = name;
                Scale = pack.Scale;
            }
        }

        Clear();
    }

    /// <summary>How many frames each pack has, for the console.</summary>
    public string Describe() =>
        Pack is null
            ? $"original art at {Scale}"
            : $"{Pack} (version {(_shapePack ?? _spritePack ?? _gumpPack ?? _facePack ?? _paperdollPack ?? _chunkPack)?.FormatVersion}) at {Scale}: " +
              $"{_shapePack?.Count ?? 0} shape, {_spritePack?.Count ?? 0} sprite, {_gumpPack?.Count ?? 0} gump, " +
              $"{_facePack?.Count ?? 0} face, {_paperdollPack?.Count ?? 0} paperdoll frames, {_chunkPack?.Count ?? 0} painted chunks " +
              $"(paperdolls {(ArtLook.RedrawnPaperdolls ? "redrawn" : "original")})";

    /// <summary>
    /// A terrain (chunk template) painted whole, the pack's chunks.hrp made by
    /// scripts/upscale/ground.py (assembled from painted materials) and
    /// pack.py (the user's pick, 2026-10-12), at
    /// the pack's scale; null where it isn't painted and its tiles show.
    /// </summary>
    public ShapeFrame? Terrain(int terrain) =>
        _chunkPack is not null && _chunkPack.Scale == Scale ? AtScale(_chunkPack.Get(terrain, 0, ArtLook.Mix, ArtLook.TrueColour)) : null;

    /// <summary>
    /// A pack's frame at the buffer's scale: a version 3 frame painted at
    /// another scale is resampled by nearest pixel (<see cref="ShapeFrame.Rescaled"/>),
    /// so its placement is exact (RENDERER.md 5.4, R4).
    /// </summary>
    ShapeFrame? AtScale(ShapeFrame? frame) => frame is not null && frame.Scale16 != Scale * 16 ? frame.Rescaled(Scale * 16) : frame;

    /// <summary>Whether the pack has the art's own colours (<see cref="ArtLook.TrueColour"/>; packs from version 2).</summary>
    public bool HasColours => (_shapePack ?? _spritePack ?? _gumpPack ?? _facePack ?? _chunkPack)?.HasColours == true;

    /// <summary>Whether the pack has redrawn paperdolls to switch to.</summary>
    public bool HasPaperdolls => _paperdollPack is { Count: > 0 };

    /// <summary>
    /// For the gumps, pictures and faces: a pack's frame (SHAPES.VGA shapes and
    /// SPRITES.VGA sprites with their reflections, gumps and faces with the frame
    /// wrapped as painted), or null where the pack has none and the original is shown.
    /// </summary>
    public ShapeFrame? UiFrame(UiArt kind, int shape, int frame, int count)
    {
        switch (kind)
        {
            case UiArt.Shape:
                return _shapePack is not null && _shapePack.Has(shape, _shapes.IsReflection(shape, frame) ? frame & 0x1f : frame)
                    ? Shape(shape, frame, Scale)
                    : null;
            case UiArt.Sprite:
                return _spritePack is not null && _spritePack.Has(shape, _shapes.IsSpriteReflection(shape, frame) ? frame & 0x1f : frame)
                    ? Sprite(shape, frame, Scale)
                    : null;
            case UiArt.Gump:
                return _gumpPack?.Get(shape, VgaShapeFile.Wrap(frame, count), ArtLook.Mix, ArtLook.TrueColour);
            case UiArt.Paperdoll:
                return ArtLook.RedrawnPaperdolls ? _paperdollPack?.Get(shape, frame, ArtLook.Mix, ArtLook.TrueColour) : null;
            default:
                return _facePack?.Get(shape, VgaShapeFile.Wrap(frame, count), ArtLook.Mix, ArtLook.TrueColour);
        }
    }

    /// <summary>A SHAPES.VGA frame at <paramref name="scale"/> (1 or <see cref="Scale"/>).</summary>
    public ShapeFrame? Shape(int shape, int frame, int scale)
    {
        if (scale == 1)
        {
            return _shapes.GetFrame8(shape, frame);
        }

        if (_shapeFrames.TryGetValue((shape, frame), out var cached))
        {
            return cached;
        }

        var made = _shapes.IsReflection(shape, frame) && _shapes.GetFrame8(shape, frame) is not null
            ? Shape(shape, frame & 0x1f, scale)?.Reflect()
            : AtScale(_shapePack?.Get(shape, frame, ArtLook.Mix, ArtLook.TrueColour)) ?? _shapes.GetFrame8(shape, frame)?.Scaled(Scale);
        return Keep(_shapeFrames, (shape, frame), made);
    }

    /// <summary>A SPRITES.VGA frame at <paramref name="scale"/> (1 or <see cref="Scale"/>).</summary>
    public ShapeFrame? Sprite(int sprite, int frame, int scale)
    {
        if (scale == 1)
        {
            return _shapes.GetSprite8(sprite, frame);
        }

        if (_spriteFrames.TryGetValue((sprite, frame), out var cached))
        {
            return cached;
        }

        var made = _shapes.IsSpriteReflection(sprite, frame) && _shapes.GetSprite8(sprite, frame) is not null
            ? Sprite(sprite, frame & 0x1f, scale)?.Reflect()
            : AtScale(_spritePack?.Get(sprite, frame, ArtLook.Mix, ArtLook.TrueColour)) ?? _shapes.GetSprite8(sprite, frame)?.Scaled(Scale);
        return Keep(_spriteFrames, (sprite, frame), made);
    }

    ShapeFrame? Keep(Dictionary<(int, int), ShapeFrame?> cache, (int, int) key, ShapeFrame? frame)
    {
        var bytes = frame is null ? 0 : frame.Pixels.Length + (frame.Mix?.Length ?? 0) + (frame.Colour?.Length ?? 0) + (frame.Alpha?.Length ?? 0);
        if (_cachedBytes + bytes > CacheBudget)
        {
            Clear();
        }

        _cachedBytes += bytes;
        cache[key] = frame;
        return frame;
    }

    /// <summary>Changes whenever the frames made are forgotten (what was painted from them is stale).</summary>
    public int Version { get; private set; }

    /// <summary>Forgets the frames made (the look has changed).</summary>
    public void Clear()
    {
        Version++;
        _shapeFrames.Clear();
        _spriteFrames.Clear();
        _cachedBytes = 0;
    }
}

/// <summary>Which file a picture over the world comes from (<see cref="HiresArt.UiFrame"/>).</summary>
public enum UiArt
{
    Shape,
    Sprite,
    Gump,
    Face,
    Paperdoll
}
