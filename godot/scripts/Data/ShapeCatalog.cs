using System.Globalization;
using System.IO;
using U7.Core;

namespace U7.Data;

public readonly record struct FrameInfo(
    int Width,
    int Height,
    int XLeft,
    int YAbove,
    int XRight,
    int YBelow,
    bool IsTile);

/// <summary>One row from Exult <c>paperdol_info.txt</c> <c>items</c>.</summary>
public readonly record struct PaperdollSpot(int Frame, int Spot);

public sealed class ShapeRecord
{
    /// <summary>This shape's information under another number, keeping that one's own frames (Exult <c>copy_info</c>).</summary>
    public ShapeRecord CopyAs(int shape, ShapeRecord own)
    {
        var copy = (ShapeRecord)MemberwiseClone();
        copy.Shape = shape;
        copy.Frames = own.Frames;
        copy.FrameCount = own.FrameCount;
        copy.TileShape = own.TileShape;
        return copy;
    }

    public int Shape;
    public string Name = "";
    public string ClassName = "";
    public int DimX = 1;
    public int DimY = 1;
    public int DimZ;
    public bool Solid;
    public bool Water;
    public bool Animated;
    /// <summary>The TFA animation type (Exult <c>Animation_info::create_from_tfa</c>), -1 if none.</summary>
    public int AnimType = -1;
    public bool Occludes;
    public bool Transparent;
    public bool Translucent;
    /// <summary>TFA <c>barge_part</c>: a piece of a ship, cart or carpet (Exult <c>is_barge_part</c>).</summary>
    public bool BargePart;
    /// <summary>Exult <c>Shape_info::Barge_types</c> (BG <c>shape_info.txt</c>): 2 seat, 3 sails, 4 wheel, 5 draft animal.</summary>
    public int BargeType;
    public bool Door;
    /// <summary>Exult <c>is_poisonous</c>: swamp tiles.</summary>
    public bool Poisonous;
    public bool LightSource;
    public bool TileShape;
    public int ShapeClass;
    public bool HasQuantity => ShapeClass == 3;
    public bool HasQualityFlags => ShapeClass == 5;
    public bool HasQuality => ShapeClass is 2 or 6 or 7 or 11 or 12 or 13;
    public bool IsContainerClass => ShapeClass == 6;
    public bool IsBargeClass => ShapeClass == 9;
    public bool IsVirtueStoneClass => ShapeClass == 11;
    public bool IsSpellbookClass => ShapeClass == 8;
    public bool IsHatchable => ShapeClass == 7 ||
        ClassName.Equals("hatchable", StringComparison.OrdinalIgnoreCase);
    public bool IsBuilding => ShapeClass == U7.Core.U7Constants.ShapeClassBuilding;
    public bool IsNpcClass => ShapeClass is 12 or 13;
    /// <summary>Exult <c>mountain_top</c>: 1 = dungeon roof, 2 = ice (SI).</summary>
    public int MountainTop;
    /// <summary>GUMPS.VGA shape, or -1 if this world shape has no gump.</summary>
    public int GumpShape = -1;
    public int ReadyType = U7.Actors.ReadySpot.Invalid;
    public int ReadyAlt1 = U7.Actors.ReadySpot.Invalid;
    public int ReadyAlt2 = U7.Actors.ReadySpot.Invalid;
    public bool IsSpell;
    public int Weight;
    public int Volume;
    public bool Lightweight;
    /// <summary>Exult <c>paperdol_info.txt</c> item spots (frame −1 = all).</summary>
    public PaperdollSpot[] Paperdoll = Array.Empty<PaperdollSpot>();
    public int FrameCount;
    public FrameInfo[] Frames = Array.Empty<FrameInfo>();
    // Exult's shape_info.txt and bodies.txt (ShapeInfoText), per game.
    /// <summary>Exult <c>Explosion_info</c>: the SPRITES.VGA explosion and its sound (-1 none); null for the default (sprite 5).</summary>
    public (int Sprite, int Sfx)? Explosion;
    /// <summary>Exult <c>SFX_info</c>: the sound the shape makes while shown (<c>shape_sfx</c>), null for none.</summary>
    public SfxInfo? Sfx;
    /// <summary>Exult <c>Animation_info</c> from <c>shape_info.txt</c>, over TFA.DAT's type; null for TFA's.</summary>
    public AnimationText? AnimationText;
    /// <summary>Exult <c>usecode_events</c>: its usecode is also run on Exult's extra events.</summary>
    public bool UsecodeEvents;
    /// <summary>Exult <c>monster_food</c>: the food frame the monster carries, -1 any.</summary>
    public int MonsterFood = -1;
    /// <summary>Exult <c>Shape_info::Actor_flags</c> bits: cold_immune 0, doesnt_eat, teleports, summons, turns_invisible, armageddon_safe, quake_walk, goblin 7.</summary>
    public int ActorFlags;
    /// <summary>Exult <c>effective_hps</c>: hit points by frame and quality.</summary>
    public FrameValue[] EffectiveHps = [];
    /// <summary>Exult <c>light_data</c>: light by frame.</summary>
    public FrameValue[] Lights = [];
    /// <summary>Exult <c>warmth_data</c>: warmth by frame.</summary>
    public FrameValue[] Warmth = [];
    /// <summary>Exult <c>quantity_frames</c>: its frame follows its quantity.</summary>
    public bool QuantityFrames;
    /// <summary>Exult <c>locked_containers</c>.</summary>
    public bool Locked;
    /// <summary>Exult <c>content_rules</c>: what the container takes or refuses.</summary>
    public ContentRule[] ContentRules = [];
    /// <summary>Exult <c>volatile_explosive</c>: blows up readily (the powder keg).</summary>
    public bool Volatile;
    /// <summary>Exult <c>framenames</c>.</summary>
    public FrameName[] FrameNames = [];
    /// <summary>Exult <c>frame_powers</c> (<c>Frame_flags_info</c>): flags by frame and quality.</summary>
    public FrameValue[] FrameFlags = [];
    /// <summary>Exult <c>is_jawbone</c>, <c>is_mirror</c>, <c>on_fire</c>, <c>extradimensional_storage</c>.</summary>
    public bool Jawbone, Mirror, OnFire, ExtradimensionalStorage;
    /// <summary>Exult <c>field_type</c>: 0 fire, 1 sleep, 2 poison, 3 caltrops, 4 campfire; -1 none.</summary>
    public int FieldType = -1;
    /// <summary>Exult <c>frame_usecode</c>.</summary>
    public FrameUsecode[] FrameUsecodes = [];
    /// <summary>Exult <c>bodyshapes</c>: a corpse.</summary>
    public bool IsBody;
    /// <summary>Exult <c>Body_info</c> (<c>bodylist</c>): the corpse an actor of this shape leaves.</summary>
    public (int Shape, int Frame)? Body;

    /// <summary>Exult <c>Shape_info::weapon_offsets</c> (WIHH.DAT): a held weapon's spot, x and y per frame (255: none); null if the shape has none.</summary>
    public byte[]? WeaponOffsets;

    /// <summary>Exult <c>Shape_info::get_weapon_offset</c>: false where no weapon is drawn.</summary>
    public bool WeaponOffset(int frame, out int x, out int y)
    {
        x = y = 255;
        if (WeaponOffsets is { } offsets && frame * 2 + 1 < offsets.Length)
        {
            x = offsets[frame * 2];
            y = offsets[frame * 2 + 1];
        }

        return x != 255;
    }

    public bool IsObjectAllowed(int frame, int spot)
    {
        if (Paperdoll.Length == 0)
        {
            return ReadyType == spot || ReadyAlt1 == spot || ReadyAlt2 == spot;
        }

        frame &= 0x1f;
        foreach (var p in Paperdoll)
        {
            if (p.Spot == spot && (p.Frame < 0 || (p.Frame & 0x1f) == frame))
            {
                return true;
            }
        }

        return false;
    }

    public FrameInfo GetFrame(int frame)
    {
        // Bit 5 (32) is Exult's NW–SE reflection flag, not a stored frame.
        var reflect = (frame & 32) != 0;
        var stored = frame & 0x1f;
        FrameInfo fi;
        if (Frames.Length == 0)
        {
            fi = new FrameInfo(8, 8, 0, 0, 7, 7, TileShape);
        }
        else
        {
            var i = stored % Frames.Length;
            if (i < 0)
            {
                i += Frames.Length;
            }

            fi = Frames[i];
        }

        if (!reflect)
        {
            return fi;
        }

        // Shape_frame::reflect swaps extents across the isometric diagonal.
        return new FrameInfo(fi.Height, fi.Width, fi.YAbove, fi.XLeft, fi.YBelow, fi.XRight, fi.IsTile);
    }
}

/// <summary>
/// Exult <c>Shapes_vga_file</c>'s shape information: names from TEXT.FLX,
/// TFA.DAT's flags, WGTVOL.DAT, OCCLUDE.DAT, READY.DAT and WIHH.DAT, the
/// frames' extents from SHAPES.VGA itself.
/// </summary>
public sealed class ShapeCatalog
{
    public ShapeRecord[] Shapes { get; }

    /// <summary>SHAPES.VGA, read once: the frames' extents here, the frames themselves for the painter (ShapeCache).</summary>
    public VgaShapeFile ShapesVga { get; }

    /// <summary>
    /// Each shape's frame extents from SHAPES.VGA (Exult <c>Shape_frame</c>).
    /// A raw 8x8 terrain frame is (0, 0, 7, 7) here, the convention paint
    /// order and hit tests use, where Exult's is (8, 8, -1, -1).
    /// </summary>
    void LoadVgaFrames()
    {
        for (var s = 0; s < Shapes.Length && s < ShapesVga.ShapeCount; s++)
        {
            var count = ShapesVga.FrameCount(s);
            if (count == 0)
            {
                continue;
            }

            var frames = new FrameInfo[count];
            for (var f = 0; f < count; f++)
            {
                var fi = ShapesVga.Get(s, f);
                frames[f] = fi.IsTile ? new FrameInfo(8, 8, 0, 0, 7, 7, true) : fi;
            }

            Shapes[s].Frames = frames;
            Shapes[s].FrameCount = count;
            Shapes[s].TileShape = frames[0].IsTile;
        }
    }


    public ShapeCatalog()
    {
        ShapesVga = new VgaShapeFile(Path.Combine(U7Paths.StaticDir, "SHAPES.VGA"));
        U7.Rendering.ShapeCache.AddPatchLayer(ShapesVga, "SHAPES.VGA");
        Files = ShapeFiles.Load();
        Avatar = AvatarData.Load(Files);
        Dolls = PaperdollInfo.Load(Files);
        ImportSerpentShapes();
        ImportAdditions();
        Shapes = new ShapeRecord[Math.Max(U7Constants.MaxShapes, ShapesVga.ShapeCount)];
        for (var i = 0; i < Shapes.Length; i++)
        {
            Shapes[i] = new ShapeRecord { Shape = i, TileShape = i < U7Constants.TileShapeCount };
        }

        LoadNames();
        LoadTypeFlags();
        LoadVgaFrames();
        LoadWeaponOffsets();
        ShapeInfoText.ReadContainers(Shapes);
        LoadReadyDat();
        ShapeInfoText.Read(Shapes);
        LoadPaperdollInfo();
        // Exult read_info: "Ensure valid ready spots for all shapes", the backpack in Black Gate, the right hand in Serpent Isle.
        var defaultReady = GameContext.IsSi ? U7.Actors.ReadySpot.Rhand : U7.Actors.ReadySpot.Back;
        foreach (var rec in Shapes.Where(r => r.ReadyType == U7.Actors.ReadySpot.Invalid))
        {
            rec.ReadyType = defaultReady;
        }

        CopySkinInfo();
        CopyAdditionInfo();
        GumpAreas = LoadGumpAreas();
        Current = this;
        U7.Actors.AvatarLook.Use(this);
    }

    /// <summary>The scene's catalog, for the static helpers that ask about a shape (bodies, locked containers, actor flags, ...).</summary>
    public static ShapeCatalog Current { get; private set; } = null!;

    /// <summary>
    /// Exult <c>Gump::set_object_area</c>'s <c>gump_area_info.txt</c> (the
    /// game's, or a mod's in its place): a container gump's object area and
    /// check mark by gump shape, from the file's global section.
    /// </summary>
    public Dictionary<int, (int X, int Y, int W, int H, int CheckX, int CheckY)> GumpAreas { get; private set; } = new();

    static Dictionary<int, (int, int, int, int, int, int)> LoadGumpAreas()
    {
        var areas = new Dictionary<int, (int, int, int, int, int, int)>();
        var path = U7Paths.PatchFile("gump_area_info.txt") ?? Path.Combine(U7Paths.GameDataDir, "gump_area_info.txt");
        if (ExultDataFile.Read(path) is not { } file)
        {
            return areas;
        }

        foreach (var (gump, text) in file.Entries(ExultDataFile.GlobalSection))
        {
            // Exult read_int_and_advance: six integers separated by commas.
            var v = text.Split(',').Select(t => int.TryParse(t.Trim(), out var n) ? n : (int?)null).ToArray();
            if (v.Length >= 6 && v.Take(6).All(n => n is not null))
            {
                areas[gump] = (v[0]!.Value, v[1]!.Value, v[2]!.Value, v[3]!.Value, v[4]!.Value, v[5]!.Value);
            }
        }

        return areas;
    }

    /// <summary>Exult's <c>shape_files.txt</c> (and a mod's): the imports and the paperdolls' sources.</summary>
    public ShapeFiles Files { get; }
    /// <summary>Exult's <c>avatar_data.txt</c> (and a mod's): the avatar's shapes, faces and skins.</summary>
    public AvatarData Avatar { get; }
    /// <summary>Exult's <c>paperdol_info.txt</c> (and a mod's): the characters' and items' paperdoll art.</summary>
    public PaperdollInfo Dolls { get; }
    /// <summary>Whether Serpent Isle's shapes were imported (Exult <c>got_si_shapes</c>): its skin colours can be chosen.</summary>
    public bool HasSerpentShapes { get; private set; }

    /// <summary>
    /// Exult <c>Shape_manager::load</c> for Black Gate: with Serpent Isle
    /// installed, its avatar bodies (<c>multiracial_imports</c>) come in at
    /// 2048 and up.
    /// </summary>
    void ImportSerpentShapes()
    {
        // Exult: Serpent Isle has its own skin bodies (got_si_shapes), nothing to import.
        HasSerpentShapes = GameContext.IsSi;
        if (GameContext.IsSi || Files.SkinImports.Count == 0 || U7Paths.SerpentStaticFile("SHAPES.VGA") is not { } path)
        {
            return;
        }

        var serpent = new FlexFile(path);
        foreach (var (dest, src) in Files.SkinImports)
        {
            ShapesVga.Import(serpent, src, dest);
        }

        HasSerpentShapes = true;
    }

    /// <summary>The new shapes (<see cref="ShapeAdditions"/>): the remake's own, then a mod's.</summary>
    public List<ShapeAddition> Additions { get; private set; } = new();

    void ImportAdditions()
    {
        Additions = ShapeAdditions.Load();
        foreach (var add in Additions)
        {
            ShapesVga.Import(add.Art, add.ArtShape, add.Shape);
        }
    }

    /// <summary>A new shape takes its model's information (Exult <c>copy_info</c>), keeping its own frames, and its own name.</summary>
    void CopyAdditionInfo()
    {
        foreach (var add in Additions)
        {
            if ((uint)add.Shape < (uint)Shapes.Length && (uint)add.InfoFrom < (uint)Shapes.Length)
            {
                Shapes[add.Shape] = Shapes[add.InfoFrom].CopyAs(add.Shape, Shapes[add.Shape]);
                Shapes[add.Shape].Name = add.Name;
            }
        }
    }

    /// <summary>
    /// Exult <c>Shape_manager::read_shape_info</c>: a skin's bodies take the
    /// default avatar's information (<c>copydata</c>), keeping their own frames.
    /// </summary>
    void CopySkinInfo()
    {
        foreach (var skin in Avatar.Skins.Where(s => s.CopyInfo))
        {
            if (!Avatar.Defaults.TryGetValue(skin.Female, out var def) || (uint)def.Shape >= (uint)Shapes.Length)
            {
                continue;
            }

            foreach (var dest in new[] { skin.Shape, skin.NakedShape })
            {
                if (dest != def.Shape && (uint)dest < (uint)Shapes.Length && Shapes[dest].FrameCount > 0)
                {
                    var own = Shapes[dest];
                    Shapes[dest] = Shapes[def.Shape].CopyAs(dest, own);
                }
            }
        }
    }

    public ShapeRecord this[int shape] =>
        (uint)shape < (uint)Shapes.Length ? Shapes[shape] : Shapes[0];

    /// <summary>
    /// Exult <c>Setup_item_names</c>: a shape's name is TEXT.FLX's entry of
    /// its number, for the first 0x400 shapes (the rest have none there).
    /// </summary>
    void LoadNames()
    {
        var path = U7Paths.PatchFile("text.flx") ?? Path.Combine(U7Paths.StaticDir, "TEXT.FLX");
        if (!File.Exists(path))
        {
            return;
        }

        var text = new FlexFile(path);
        var count = Math.Min(Math.Min(text.Count, 0x400), Shapes.Length);
        for (var s = 0; s < count; s++)
        {
            var data = text.Get(s);
            var end = data.IndexOf((byte)0);
            Shapes[s].Name = System.Text.Encoding.Latin1.GetString(end >= 0 ? data[..end] : data).Trim();
        }
    }

    /// <summary>A STATIC file, or the mod's of that name (Exult's <c>IExultDataSource(file, patch)</c>); null when neither is there.</summary>
    static byte[]? ReadStatic(string name)
    {
        var path = U7Paths.PatchFile(name) ?? Path.Combine(U7Paths.StaticDir, name);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    /// <summary>
    /// Exult <c>Shapes_vga_file::read_info</c>: WGTVOL.DAT (weight and
    /// volume), TFA.DAT (three bytes a shape, <c>Shape_info::set_tfa_data</c>
    /// and the flags' accessors), each read on while the file lasts, so
    /// shapes past the file's end keep none; TFA.DAT's animation types (a
    /// nibble a shape from 3 * 1024, <c>Animation_info::create_from_tfa</c>);
    /// OCCLUDE.DAT (a bit a shape).
    /// </summary>
    void LoadTypeFlags()
    {
        if (ReadStatic("WGTVOL.DAT") is { } wgtvol)
        {
            for (var s = 0; s < Shapes.Length && 2 * s + 1 < wgtvol.Length; s++)
            {
                Shapes[s].Weight = wgtvol[2 * s];
                Shapes[s].Volume = wgtvol[2 * s + 1];
            }
        }

        if (ReadStatic("TFA.DAT") is { } tfa)
        {
            for (var s = 0; s < Shapes.Length && 3 * s + 2 < tfa.Length; s++)
            {
                var (t0, t1, t2) = (tfa[3 * s], tfa[3 * s + 1], tfa[3 * s + 2]);
                var rec = Shapes[s];
                rec.DimX = 1 + (t2 & 7);
                rec.DimY = 1 + ((t2 >> 3) & 7);
                rec.DimZ = t0 >> 5;
                rec.Animated = (t0 & (1 << 2)) != 0;
                rec.Solid = (t0 & (1 << 3)) != 0;
                rec.Water = (t0 & (1 << 4)) != 0;
                rec.ShapeClass = t1 & 15;
                rec.ClassName = ClassNames.GetValueOrDefault(rec.ShapeClass, "");
                rec.Poisonous = (t1 & (1 << 4)) != 0;
                rec.Door = (t1 & (1 << 5)) != 0;
                rec.BargePart = (t1 & (1 << 6)) != 0;
                rec.Transparent = (t1 & (1 << 7)) != 0;
                rec.LightSource = (t2 & (1 << 6)) != 0;
                rec.Translucent = (t2 & (1 << 7)) != 0;
            }
        }

        // The animation types are read from the game's own TFA.DAT only.
        var stfa = Path.Combine(U7Paths.StaticDir, "TFA.DAT");
        if (File.Exists(stfa))
        {
            var data = File.ReadAllBytes(stfa);
            for (var i = 0; i < 512 && 3 * 1024 + i < data.Length; i++)
            {
                var val = (int)data[3 * 1024 + i];
                for (var s = 2 * i; val != 0; val >>= 4, s++)
                {
                    if ((val & 0xf) != 0 && s < Shapes.Length)
                    {
                        Shapes[s].AnimType = val & 0xf;
                    }
                }
            }
        }

        if (ReadStatic("OCCLUDE.DAT") is { } occ)
        {
            for (var i = 0; i < occ.Length; i++)
            {
                for (int b = 0, bits = occ[i]; bits != 0; b++, bits >>= 1)
                {
                    if ((bits & 1) != 0 && i * 8 + b < Shapes.Length)
                    {
                        Shapes[i * 8 + b].Occludes = true;
                    }
                }
            }
        }
    }

    /// <summary>Exult <c>Shape_info::Shape_class</c>'s names, as the class names elsewhere expect them.</summary>
    static readonly Dictionary<int, string> ClassNames = new()
    {
        [2] = "quality", [3] = "quantity", [4] = "has_hp", [5] = "quality_flags",
        [6] = "container", [7] = "hatchable", [8] = "spellbook", [9] = "barge", [11] = "virtue_stone",
        [12] = "monster", [13] = "human", [14] = "building"
    };

    static int ParseInt(string s, int fallback) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    /// <summary>
    /// READY.DAT: count, then (u16 shape, u8 ready_type, 6 pad) per Exult
    /// (<c>Ready_type_functor</c>), the spot in the game's numbering.
    /// </summary>
    void LoadReadyDat()
    {
        var path = Path.Combine(U7Paths.StaticDir, "READY.DAT");
        if (!File.Exists(path))
        {
            return;
        }

        var data = File.ReadAllBytes(path);
        if (data.Length < 1)
        {
            return;
        }

        var i = 0;
        var count = (int)data[i++];
        if (count == 255)
        {
            if (i + 2 > data.Length)
            {
                return;
            }

            count = BitConverter.ToUInt16(data, i);
            i += 2;
        }

        const int recSize = 2 + 1 + 6;
        for (var n = 0; n < count && i + recSize <= data.Length; n++)
        {
            var shape = BitConverter.ToUInt16(data, i);
            var ready = data[i + 2];
            i += recSize;
            if (shape < Shapes.Length)
            {
                // READY.DAT packs spell_flag in bit 0; type is bits 3+.
                var rec = Shapes[shape];
                rec.IsSpell = (ready & 1) != 0;
                var spot = GameContext.IsSi ? U7.Actors.ReadySpot.FromSi(ready >> 3) : U7.Actors.ReadySpot.FromBg(ready >> 3);
                rec.ReadyType = spot;
                switch (spot)
                {
                    case U7.Actors.ReadySpot.Lfinger:
                        rec.ReadyAlt1 = U7.Actors.ReadySpot.Rfinger;
                        break;
                    case U7.Actors.ReadySpot.Lhand:
                        rec.ReadyAlt1 = U7.Actors.ReadySpot.Rhand;
                        rec.ReadyAlt2 = U7.Actors.ReadySpot.Belt;
                        break;
                    case U7.Actors.ReadySpot.BothHands:
                        rec.ReadyAlt1 = U7.Actors.ReadySpot.Back2h;
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Exult <c>Shapes_vga_file::read_info</c>'s WIHH.DAT: an offset per shape
    /// (0, or one into the table or past the end: none), each record 32
    /// frames of x, y; a value above 63 (the file has 64 and 255) is "not
    /// drawn".
    /// </summary>
    void LoadWeaponOffsets()
    {
        var path = Path.Combine(U7Paths.StaticDir, "WIHH.DAT");
        if (!File.Exists(path))
        {
            return;
        }

        var data = File.ReadAllBytes(path);
        var count = Math.Min(Math.Min(Shapes.Length, ShapesVga.ShapeCount), data.Length / 2);
        for (var s = 0; s < count; s++)
        {
            var at = BitConverter.ToUInt16(data, s * 2);
            if (at == 0 || at > data.Length - 64 || at < 2048)
            {
                continue;
            }

            var offsets = new byte[64];
            for (var f = 0; f < 32; f++)
            {
                var x = data[at + f * 2];
                var y = data[at + f * 2 + 1];
                (offsets[f * 2], offsets[f * 2 + 1]) = x > 63 || y > 63 ? ((byte)255, (byte)255) : (x, y);
            }

            Shapes[s].WeaponOffsets = offsets;
        }
    }

    /// <summary>
    /// Exult <c>data/bg/paperdol_info.txt</c> items: which world shapes may occupy
    /// which ready spots. Used by <c>fits_in_spot</c> even when PAPERDOL.VGA is absent.
    /// </summary>
    void LoadPaperdollInfo()
    {
        // Copied from Exult data/bg/paperdol_info.txt (GPL) so the Exult tree is not needed at runtime.
        var path = Path.Combine(U7Paths.GameDataDir, "paperdol_info.txt");
        if (!File.Exists(path))
        {
            return;
        }

        var buckets = new List<PaperdollSpot>[Shapes.Length];
        var inItems = false;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.StartsWith("%%section", StringComparison.OrdinalIgnoreCase))
            {
                inItems = line.Contains("items", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (line.StartsWith("%%endsection", StringComparison.OrdinalIgnoreCase))
            {
                inItems = false;
                continue;
            }

            if (!inItems || line.Length == 0 || line[0] is '#' or ';')
            {
                continue;
            }

            if (line[0] != ':')
            {
                continue;
            }

            var parts = line[1..].Split('/');
            if (parts.Length < 4)
            {
                continue;
            }

            var shape = ParseInt(parts[0].Trim(), -1);
            var frame = ParseInt(parts[1].Trim(), -1);
            var spot = ParseInt(parts[3].Trim(), -1);
            if ((uint)shape >= (uint)Shapes.Length || spot < 0)
            {
                continue;
            }

            buckets[shape] ??= new List<PaperdollSpot>();
            buckets[shape].Add(new PaperdollSpot(frame, spot));
        }

        for (var i = 0; i < buckets.Length; i++)
        {
            if (buckets[i] is { Count: > 0 } list)
            {
                Shapes[i].Paperdoll = list.ToArray();
            }
        }
    }
}
