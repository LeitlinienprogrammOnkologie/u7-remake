using System.Collections.Generic;
using System.IO;
using Godot;
using U7.Core;
using U7.Data;

namespace U7.Rendering;

/// <summary>
/// The original shape files, decoded on demand: SHAPES.VGA and SPRITES.VGA
/// frames by palette index (<see cref="ShapeFrame"/>), RGBA textures of the shapes
/// in the day palette, and the gump, font and face frames.
/// </summary>
public sealed class ShapeCache
{
    readonly Dictionary<(int Shape, int Frame), Texture2D?> _textures = new();
    readonly Dictionary<(int Shape, int Frame), Texture2D?> _gumpTex = new();
    readonly Dictionary<(int Shape, int Frame), Image?> _gumpImg = new();
    readonly Dictionary<(int Font, int Frame), Texture2D?> _fontTex = new();
    readonly Dictionary<(int Shape, int Frame), Texture2D?> _faceTex = new();
    readonly Dictionary<(int Sprite, int Frame, bool Translucent), Texture2D?> _spriteTex = new();
    readonly FrameTable _shapes8;
    readonly FrameTable _sprites8;
    readonly VgaShapeFile _gumpsVga;
    readonly VgaShapeFile _fontsVga;
    readonly VgaShapeFile _facesVga;
    readonly VgaShapeFile _spritesVga;
    readonly byte[] _palette;
    readonly byte[] _translucentPalette;
    readonly ShapeCatalog _catalog;

    public VgaShapeFile GumpsVga => _gumpsVga;
    public VgaShapeFile FontsVga => _fontsVga;
    public VgaShapeFile SpritesVga => _spritesVga;
    /// <summary>The day palette, 256 RGB entries.</summary>
    public ReadOnlySpan<byte> DayPalette => _palette;
    /// <summary>Exult's translucency tables (XFORM.TBL).</summary>
    public XformTables Xforms { get; }

    public ShapeCache(ShapeCatalog catalog)
    {
        _catalog = catalog;
        _shapes8 = new FrameTable(new VgaShapeFile(Path.Combine(U7Paths.StaticDir, "SHAPES.VGA")));
        _gumpsVga = new VgaShapeFile(Path.Combine(U7Paths.StaticDir, "GUMPS.VGA"));
        _fontsVga = VgaFont.File;
        _facesVga = new VgaShapeFile(Path.Combine(U7Paths.StaticDir, "FACES.VGA"));
        _spritesVga = new VgaShapeFile(Path.Combine(U7Paths.StaticDir, "SPRITES.VGA"));
        _sprites8 = new FrameTable(_spritesVga);
        _palette = U7Palette.DayRgb();
        _translucentPalette = U7Palette.DayRgbaTranslucent();
        Xforms = XformTables.Load();
    }

    /// <summary>A SHAPES.VGA frame by palette index; bit 5 is the reflection. Null when the shape has no such frame.</summary>
    public ShapeFrame? GetFrame8(int shape, int frame) => _shapes8.Get(shape, frame);

    /// <summary>A SPRITES.VGA frame by palette index.</summary>
    public ShapeFrame? GetSprite8(int sprite, int frame) => _sprites8.Get(sprite, frame);

    /// <summary>
    /// A SPRITES.VGA frame as an RGBA texture in the day palette, for pictures
    /// over the gumps: opaque (Exult <c>paint_shape</c>), or with the
    /// translucent colours as their blend colour and alpha.
    /// </summary>
    public Texture2D? GetSprite(int sprite, int frame, bool translucent = false)
    {
        var key = (sprite, frame, translucent);
        if (_spriteTex.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var tex = GetSprite8(sprite, frame) is { } decoded
            ? ImageTexture.CreateFromImage(decoded.ToImage(translucent ? _translucentPalette : _palette))
            : null;
        _spriteTex[key] = tex;
        return tex;
    }

    /// <summary>
    /// A SHAPES.VGA frame as an RGBA texture in the day palette, for gumps.
    /// TFA-translucent shapes get the translucent colours as their blend
    /// colour and alpha, an approximation of Exult, which turns the gump's
    /// pixels under them through its tables.
    /// </summary>
    public Texture2D? Get(int shape, int frame)
    {
        var key = (shape, frame);
        if (_textures.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var tex = GetFrame8(shape, frame) is { } decoded ? ImageTexture.CreateFromImage(decoded.ToImage(_catalog[shape].Translucent ? _translucentPalette : _palette)) : null;
        _textures[key] = tex;
        return tex;
    }

    public Texture2D? GetGump(int shape, int frame)
    {
        var key = (shape, frame);
        if (_gumpTex.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var image = GetGumpImage(shape, frame);
        var tex = image is null ? null : ImageTexture.CreateFromImage(image);
        _gumpTex[key] = tex;
        return tex;
    }

    public Image? GetGumpImage(int shape, int frame)
    {
        var key = (shape, frame);
        if (_gumpImg.TryGetValue(key, out var cached))
        {
            return cached;
        }

        Image? image = _gumpsVga.Decode(shape, frame, _palette);
        if (image is null)
        {
            var path = U7Paths.GumpPng(shape, frame);
            if (File.Exists(path))
            {
                image = Image.LoadFromFile(path);
            }
        }

        _gumpImg[key] = image;
        return image;
    }

    public FrameInfo GetGumpFrame(int shape, int frame)
    {
        var fi = _gumpsVga.Get(shape, frame);
        if (fi.Width > 0)
        {
            return fi;
        }

        var img = GetGumpImage(shape, frame);
        if (img is null)
        {
            return default;
        }

        var w = img.GetWidth();
        var h = img.GetHeight();
        return new FrameInfo(w, h, 0, 0, w - 1, h - 1, false);
    }

    public Texture2D? GetFontGlyph(int font, int frame)
    {
        var key = (font, frame);
        if (_fontTex.TryGetValue(key, out var cached))
        {
            return cached;
        }

        Texture2D? tex = null;
        var image = _fontsVga.Decode(font, frame, _palette);
        if (image is null)
        {
            var path = U7Paths.FontPng(font, frame);
            if (File.Exists(path))
            {
                image = Image.LoadFromFile(path);
            }
        }

        if (image is not null)
        {
            tex = ImageTexture.CreateFromImage(image);
        }

        _fontTex[key] = tex;
        return tex;
    }

    public FrameInfo GetFontFrame(int font, int frame)
    {
        var fi = _fontsVga.Get(font, frame);
        if (fi.Width > 0)
        {
            return fi;
        }

        var tex = GetFontGlyph(font, frame);
        if (tex is null)
        {
            return default;
        }

        return new FrameInfo(tex.GetWidth(), tex.GetHeight(), 0, 0, tex.GetWidth() - 1, tex.GetHeight() - 1, false);
    }

    public Texture2D? GetFace(int shape, int frame)
    {
        var key = (shape, frame);
        if (_faceTex.TryGetValue(key, out var cached))
        {
            return cached;
        }

        Texture2D? tex = null;
        var image = _facesVga.Decode(shape, frame, _palette);
        if (image is null)
        {
            var path = U7Paths.FacePng(shape, frame);
            if (!File.Exists(path) && frame != 0)
            {
                path = U7Paths.FacePng(shape, 0);
            }

            if (File.Exists(path))
            {
                image = Image.LoadFromFile(path);
            }
        }

        if (image is not null)
        {
            tex = ImageTexture.CreateFromImage(image);
        }

        _faceTex[key] = tex;
        return tex;
    }

    /// <summary>
    /// A file's decoded frames, 64 slots a shape like Exult's <c>Shape</c>:
    /// the stored frames, then from 32 their reflections, made from the
    /// decoded frame when first asked for (Exult <c>Shape::reflect</c>).
    /// </summary>
    sealed class FrameTable
    {
        const int Slots = 64;
        readonly VgaShapeFile _file;
        readonly ShapeFrame?[]?[] _frames;
        /// <summary>Per shape, a bit for each slot already decoded (the frame may be null).</summary>
        readonly ulong[] _decoded;

        public FrameTable(VgaShapeFile file)
        {
            _file = file;
            var count = file.ShapeCount;
            _frames = new ShapeFrame?[count][];
            _decoded = new ulong[count];
        }

        public ShapeFrame? Get(int shape, int frame)
        {
            if ((uint)shape >= (uint)_frames.Length)
            {
                return null;
            }

            if ((uint)frame >= Slots)
            {
                return _file.DecodeFrame(shape, frame, false);
            }

            var frames = _frames[shape] ??= new ShapeFrame?[Slots];
            var bit = 1UL << frame;
            if ((_decoded[shape] & bit) == 0)
            {
                _decoded[shape] |= bit;
                frames[frame] = Decode(shape, frame);
            }

            return frames[frame];
        }

        ShapeFrame? Decode(int shape, int frame)
        {
            // Exult Shape::read: a stored frame, or past them with bit 5 the
            // reflection; terrain ignores the bit.
            if (frame >= 32 && frame >= _file.FrameCount(shape) && !_file.Get(shape, 0).IsTile)
            {
                return Get(shape, frame & 0x1f)?.Reflect();
            }

            return _file.DecodeFrame(shape, frame, false);
        }
    }
}
