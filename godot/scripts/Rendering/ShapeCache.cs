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
    readonly FrameTable _gumps8;
    readonly FrameTable? _paperdolls8;
    readonly Dictionary<(int Shape, int Frame, bool Translucent), Texture2D?> _paperdollTex = new();
    readonly VgaShapeFile _gumpsVga;
    readonly VgaShapeFile _fontsVga;
    readonly VgaShapeFile _facesVga;
    readonly PaintedFaces _paintedFaces = new(GameContext.Id);
    bool _facesPainted = ArtLook.PaintedFaces;
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
    /// <summary>The redrawn art, when the user's look has a pack: the gumps, pictures and faces show it too.</summary>
    public HiresArt? Hires;
    int _hiresVersion;

    public ShapeCache(ShapeCatalog catalog)
    {
        _catalog = catalog;
        _shapes8 = new FrameTable(catalog.ShapesVga);
        _gumpsVga = new VgaShapeFile(Path.Combine(U7Paths.StaticDir, "GUMPS.VGA"));
        AddPatchLayer(_gumpsVga, "GUMPS.VGA");
        _fontsVga = VgaFont.File;
        _facesVga = new VgaShapeFile(Path.Combine(U7Paths.StaticDir, "FACES.VGA"));
        // Exult BG_Game: its own skin colours' faces (files/mrfacesvga) between the game's and a mod's.
        if (ShapeList.Read(Path.Combine(U7Paths.GameDataDir, "flx", "bg_mr_faces.in")) is { } mrFaces)
        {
            _facesVga.AddLayer(mrFaces);
        }

        AddPatchLayer(_facesVga, "FACES.VGA");
        _spritesVga = new VgaShapeFile(Path.Combine(U7Paths.StaticDir, "SPRITES.VGA"));
        AddPatchLayer(_spritesVga, "SPRITES.VGA");
        Paperdolls = LoadPaperdolls(catalog.Files);
        _sprites8 = new FrameTable(_spritesVga);
        _gumps8 = new FrameTable(_gumpsVga, wrap: true);
        _paperdolls8 = Paperdolls is null ? null : new FrameTable(Paperdolls);
        _palette = U7Palette.DayRgb();
        _translucentPalette = U7Palette.DayRgbaTranslucent();
        Xforms = XformTables.Load();
    }

    /// <summary>
    /// Exult's <c>paperdol.vga</c> for Black Gate, null without Serpent Isle
    /// (Exult <c>can_have_paperdolls</c>): SI's PAPERDOL.VGA, Exult's own
    /// Black Gate art over it (<c>flx</c>), a mod's over both; SI's paperdoll
    /// gumps imported into GUMPS.VGA (<c>gump_imports</c>, from 2048).
    /// </summary>
    public VgaShapeFile? Paperdolls { get; }

    VgaShapeFile? LoadPaperdolls(ShapeFiles files)
    {
        if (U7Paths.SerpentStaticFile("PAPERDOL.VGA") is not { } siPaperdolls ||
            U7Paths.SerpentStaticFile("GUMPS.VGA") is not { } siGumps)
        {
            return null;
        }

        VgaShapeFile? file = null;
        foreach (var source in files.PaperdollSources)
        {
            IShapeSource? layer = source switch
            {
                "si" => new FlexFile(siPaperdolls),
                "flx" => ShapeList.Read(Path.Combine(U7Paths.GameDataDir, "flx", "bg_paperdol.in")),
                "static" or "bg" when File.Exists(Path.Combine(U7Paths.StaticDir, "PAPERDOL.VGA")) =>
                    new FlexFile(Path.Combine(U7Paths.StaticDir, "PAPERDOL.VGA")),
                _ => null
            };
            if (layer is null)
            {
                continue;
            }

            if (file is null)
            {
                file = new VgaShapeFile(layer);
            }
            else
            {
                file.AddLayer(layer);
            }
        }

        if (file is null)
        {
            return null;
        }

        AddPatchLayer(file, "PAPERDOL.VGA");
        var gumps = new FlexFile(siGumps);
        foreach (var (dest, src) in files.GumpImports)
        {
            _gumpsVga.Import(gumps, src, dest);
        }

        return file;
    }

    /// <summary>Exult's <c>PATCH_*</c> files: the remake's additions' and a mod's own shapes in the same slots, over the game's.</summary>
    public static void AddPatchLayer(VgaShapeFile file, string name)
    {
        foreach (var path in U7Paths.PatchLayers(name))
        {
            file.AddLayer(new FlexFile(path));
        }
    }

    /// <summary>A SHAPES.VGA frame by palette index; bit 5 is the reflection. Null when the shape has no such frame.</summary>
    public ShapeFrame? GetFrame8(int shape, int frame) => _shapes8.Get(shape, frame);

    /// <summary>A GUMPS.VGA frame by palette index (the frame number wrapping, as painted), for Exult's pixel hit-tests.</summary>
    public ShapeFrame? GetGump8(int shape, int frame) => _gumps8.Get(shape, frame);

    /// <summary>A SPRITES.VGA frame by palette index.</summary>
    public ShapeFrame? GetSprite8(int sprite, int frame) => _sprites8.Get(sprite, frame);

    /// <summary>Whether a SHAPES.VGA frame is made by reflecting frame & 0x1f (Exult <c>Shape::read</c>).</summary>
    public bool IsReflection(int shape, int frame) => FrameTable.IsReflection(_catalog.ShapesVga, shape, frame);

    /// <summary>Whether a SPRITES.VGA frame is made by reflecting frame & 0x1f.</summary>
    public bool IsSpriteReflection(int sprite, int frame) => FrameTable.IsReflection(_spritesVga, sprite, frame);

    /// <summary>
    /// A SPRITES.VGA frame as an RGBA texture in the day palette, for pictures
    /// over the gumps: opaque (Exult <c>paint_shape</c>), or with the
    /// translucent colours as their blend colour and alpha.
    /// </summary>
    public Texture2D? GetSprite(int sprite, int frame, bool translucent = false)
    {
        CheckHires();
        var key = (sprite, frame, translucent);
        if (_spriteTex.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var palette = translucent ? _translucentPalette : _palette;
        var tex = HiresTexture(UiArt.Sprite, sprite, frame, 0, palette, translucent) ??
                  (GetSprite8(sprite, frame) is { } decoded ? WithIndex(ImageTexture.CreateFromImage(decoded.ToImage(palette)), decoded, translucent, false) : null);
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
        CheckHires();
        var key = (shape, frame);
        if (_textures.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var translucent = _catalog[shape].Translucent;
        var palette = translucent ? _translucentPalette : _palette;
        var tex = HiresTexture(UiArt.Shape, shape, frame, 0, palette, translucent) ??
                  (GetFrame8(shape, frame) is { } decoded ? WithIndex(ImageTexture.CreateFromImage(decoded.ToImage(palette)), decoded, translucent, false) : null);
        _textures[key] = tex;
        return tex;
    }

    public Texture2D? GetGump(int shape, int frame)
    {
        CheckHires();
        var key = (shape, frame);
        if (_gumpTex.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var image = GetGumpImage(shape, frame);
        var tex = image is null
            ? null
            : HiresTexture(UiArt.Gump, shape, frame, _gumpsVga.FrameCount(shape), _palette) ??
              (_gumpsVga.DecodeFrame(shape, frame, true) is { } decoded
                  ? WithIndex(ImageTexture.CreateFromImage(image), decoded, false, false)
                  : ImageTexture.CreateFromImage(image));
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

        var image = _gumpsVga.Decode(shape, frame, _palette);
        _gumpImg[key] = image;
        return image;
    }

    public FrameInfo GetGumpFrame(int shape, int frame) => _gumpsVga.Get(shape, frame);

    /// <summary>A PAPERDOL.VGA frame by palette index (no wrap: a frame past the last is none), for hit tests.</summary>
    public ShapeFrame? GetPaperdoll8(int shape, int frame) => _paperdolls8?.Get(shape, frame);

    /// <summary>
    /// A PAPERDOL.VGA frame as an RGBA texture in the day palette, its
    /// translucent colours see-through for the ghostly ones (Exult
    /// <c>paint_shape(.., translucent)</c>): the original art, or the pack's
    /// when the paperdolls are switched to redrawn (<see cref="ArtLook.RedrawnPaperdolls"/>).
    /// </summary>
    public Texture2D? GetPaperdoll(int shape, int frame, bool translucent)
    {
        CheckHires();
        var key = (shape, frame, translucent);
        if (_paperdollTex.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var palette = translucent ? _translucentPalette : _palette;
        var tex = GetPaperdoll8(shape, frame) is { } decoded
            ? HiresTexture(UiArt.Paperdoll, shape, frame, 0, palette, translucent) ??
              WithIndex(ImageTexture.CreateFromImage(decoded.ToImage(palette)), decoded, translucent, false)
            : null;
        _paperdollTex[key] = tex;
        return tex;
    }

    public Texture2D? GetFontGlyph(int font, int frame)
    {
        var key = (font, frame);
        if (_fontTex.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var image = _fontsVga.Decode(font, frame, _palette);
        var tex = image is null ? null : ImageTexture.CreateFromImage(image);
        _fontTex[key] = tex;
        return tex;
    }

    public FrameInfo GetFontFrame(int font, int frame) => _fontsVga.Get(font, frame);

    public Texture2D? GetFace(int shape, int frame)
    {
        CheckHires();
        var key = (shape, frame);
        if (_faceTex.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var image = _facesVga.Decode(shape, frame, _palette);
        var count = _facesVga.FrameCount(shape);
        var tex = image is null
            ? null
            : PaintedFace(shape, frame, count, image.GetHeight()) ?? HiresTexture(UiArt.Face, shape, frame, count, _palette) ??
              ImageTexture.CreateFromImage(image);
        _faceTex[key] = tex;
        return tex;
    }

    /// <summary>
    /// The user's painting of a face (<see cref="PaintedFaces"/>), when it is
    /// the game's own face: one a mod or Exult brings shows as theirs.
    /// </summary>
    Texture2D? PaintedFace(int shape, int frame, int count, int height) =>
        ArtLook.PaintedFaces && _facesVga.IsOriginal(shape)
            ? _paintedFaces.Get(shape, VgaShapeFile.Wrap(frame, count), height)
            : null;

    /// <summary>Whether there are paintings of the faces to show.</summary>
    public bool HasPaintedFaces => _paintedFaces.Any;

    /// <summary>
    /// A pack's frame (<see cref="HiresArt.UiFrame"/>) as a texture that
    /// reports the original's size, so it is drawn where and as large as the
    /// original, its finer pixels smoothly filtered; null where the original
    /// is shown.
    /// </summary>
    Texture2D? HiresTexture(UiArt kind, int shape, int frame, int count, ReadOnlySpan<byte> palette, bool translucentShape = false)
    {
        if (Hires is not { Pack: not null } hires || hires.UiFrame(kind, shape, frame, count) is not { } hi)
        {
            return null;
        }

        var image = hi.ToImage(palette);
        image.GenerateMipmaps();
        var tex = ImageTexture.CreateFromImage(image);
        // At the frame's own scale (a version 3 painting fitted by height has its own); the picture is its visual bounds.
        tex.SetSizeOverride(new Vector2I(Math.Max(1, image.GetWidth() * 16 / hi.Scale16), Math.Max(1, image.GetHeight() * 16 / hi.Scale16)));
        return WithIndex(tex, hi, translucentShape, true, CanvasItem.TextureFilterEnum.LinearWithMipmaps);
    }

    /// <summary>
    /// A gump texture with the frame's index plane riding as its normal map
    /// (docs/RENDERER.md 5.9, R8): the gumps' shader reads the palette index
    /// from there for cycling, the current palette and the translucency
    /// tables, which the RGBA texture alone has lost.
    /// </summary>
    static CanvasTexture WithIndex(Texture2D diffuse, ShapeFrame frame, bool translucentShape, bool hires,
        CanvasItem.TextureFilterEnum filter = CanvasItem.TextureFilterEnum.ParentNode) =>
        new() { DiffuseTexture = diffuse, NormalTexture = IndexPlane(frame, translucentShape, hires), TextureFilter = filter };

    /// <summary>
    /// The frame's index plane over its visual bounds as an RG8 texture: R the
    /// palette index; G a marker: 255 an original frame's pixel (the palette
    /// shows it), 254 a see-through pixel of a translucent shape (what is
    /// under it is turned through the pixel's table), 253 a hi-res frame's
    /// pixel (its own colour, its index for cycling); 0 where the frame has no pixel.
    /// </summary>
    static ImageTexture IndexPlane(ShapeFrame frame, bool translucentShape, bool hires)
    {
        var (left, above, right, below) = frame.VisualBounds();
        var w = Math.Max(1, left + right + 1);
        var h = Math.Max(1, above + below + 1);
        var data = new byte[w * h * 2];
        foreach (var scan in frame.Scans)
        {
            var y = above + scan.Y;
            if ((uint)y >= (uint)h)
            {
                continue;
            }

            for (var k = 0; k < scan.Length; k++)
            {
                var x = left + scan.X + k;
                if ((uint)x >= (uint)w)
                {
                    continue;
                }

                var index = frame.Pixels[scan.Offset + k];
                var at = (y * w + x) * 2;
                data[at] = index;
                data[at + 1] = translucentShape && index is >= XformTables.FirstTranslucent and <= 0xFE ? (byte)254 : hires ? (byte)253 : (byte)255;
            }
        }

        return ImageTexture.CreateFromImage(Image.CreateFromData(w, h, false, Image.Format.Rg8, data));
    }

    /// <summary>Forgets the textures made when the art has changed (the pack, or the faces painted or not).</summary>
    void CheckHires()
    {
        if (_facesPainted != ArtLook.PaintedFaces)
        {
            _facesPainted = ArtLook.PaintedFaces;
            _faceTex.Clear();
        }

        var version = Hires?.Version ?? 0;
        if (version == _hiresVersion)
        {
            return;
        }

        _hiresVersion = version;
        _textures.Clear();
        _gumpTex.Clear();
        _faceTex.Clear();
        _spriteTex.Clear();
        _paperdollTex.Clear();
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
        /// <summary>Frame numbers wrap round the count (gumps), instead of bit 5 reflecting (shapes, sprites).</summary>
        readonly bool _wrap;
        readonly ShapeFrame?[]?[] _frames;
        /// <summary>Per shape, a bit for each slot already decoded (the frame may be null).</summary>
        readonly ulong[] _decoded;

        public FrameTable(VgaShapeFile file, bool wrap = false)
        {
            _file = file;
            _wrap = wrap;
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
                return _file.DecodeFrame(shape, frame, _wrap);
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
            if (_wrap)
            {
                return _file.DecodeFrame(shape, frame, true);
            }

            if (IsReflection(_file, shape, frame))
            {
                return Get(shape, frame & 0x1f)?.Reflect();
            }

            return _file.DecodeFrame(shape, frame, false);
        }

        /// <summary>Exult <c>Shape::read</c>: a stored frame, or past them with bit 5 the reflection; terrain ignores the bit.</summary>
        public static bool IsReflection(VgaShapeFile file, int shape, int frame) =>
            frame >= 32 && frame >= file.FrameCount(shape) && !file.Get(shape, 0).IsTile;
    }
}
