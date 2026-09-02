using System.Collections.Generic;
using System.IO;
using Godot;
using U7.Core;
using U7.Data;

namespace U7.Rendering;

/// <summary>
/// Loads extracted shape PNGs on demand. Tile shapes (0–149) are warmed at
/// startup so chunk-flat blits stay cheap.
/// </summary>
public sealed class ShapeCache
{
    readonly Dictionary<(int Shape, int Frame), Texture2D?> _textures = new();
    readonly Dictionary<(int Shape, int Frame), Image?> _images = new();
    readonly Dictionary<(int Shape, int Frame), Texture2D?> _gumpTex = new();
    readonly Dictionary<(int Shape, int Frame), Image?> _gumpImg = new();
    readonly Dictionary<(int Font, int Frame), Texture2D?> _fontTex = new();
    readonly Dictionary<(int Shape, int Frame), Texture2D?> _faceTex = new();
    readonly ShapeCatalog _catalog;
    readonly VgaShapeFile _gumpsVga;
    readonly VgaShapeFile _fontsVga;
    readonly byte[] _palette;

    public VgaShapeFile GumpsVga => _gumpsVga;
    public VgaShapeFile FontsVga => _fontsVga;

    public ShapeCache(ShapeCatalog catalog)
    {
        _catalog = catalog;
        _gumpsVga = new VgaShapeFile(Path.Combine(U7Paths.StaticDir, "GUMPS.VGA"));
        _fontsVga = new VgaShapeFile(Path.Combine(U7Paths.StaticDir, "FONTS.VGA"));
        _palette = U7Palette.DayRgb();
    }

    public void WarmTiles()
    {
        for (var shape = 0; shape < U7Constants.TileShapeCount; shape++)
        {
            var count = Math.Max(1, _catalog[shape].FrameCount);
            for (var frame = 0; frame < count; frame++)
            {
                GetImage(shape, frame);
            }
        }
    }

    public Texture2D? Get(int shape, int frame)
    {
        var key = (shape, frame);
        if (_textures.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var image = GetImage(shape, frame);
        var tex = image is null ? null : ImageTexture.CreateFromImage(image);
        _textures[key] = tex;
        return tex;
    }

    public Image? GetImage(int shape, int frame)
    {
        var key = (shape, frame);
        if (_images.TryGetValue(key, out var cached))
        {
            return cached;
        }

        Image? image = null;
        var path = U7Paths.ShapePng(shape, frame);
        if (File.Exists(path))
        {
            image = Image.LoadFromFile(path);
        }
        else if ((frame & 32) != 0)
        {
            // East/west (and other reflected) frames are not stored in SHAPES.VGA.
            // Exult builds them with Shape_frame::reflect() — transpose about the hotspot.
            var src = GetImage(shape, frame & 0x1f);
            if (src is not null)
            {
                image = ReflectNwSe(src);
            }
        }

        _images[key] = image;
        return image;
    }

    static Image ReflectNwSe(Image src)
    {
        var w = src.GetWidth();
        var h = src.GetHeight();
        var dst = Image.CreateEmpty(h, w, false, Image.Format.Rgba8);
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                dst.SetPixel(y, x, src.GetPixel(x, y));
            }
        }

        return dst;
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
        var path = U7Paths.FacePng(shape, frame);
        if (!File.Exists(path) && frame != 0)
        {
            path = U7Paths.FacePng(shape, 0);
        }

        if (File.Exists(path))
        {
            var image = Image.LoadFromFile(path);
            if (image is not null)
            {
                tex = ImageTexture.CreateFromImage(image);
            }
        }

        _faceTex[key] = tex;
        return tex;
    }
}
