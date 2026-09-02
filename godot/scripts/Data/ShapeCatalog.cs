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
    public int Shape;
    public string Name = "";
    public string ClassName = "";
    public int DimX = 1;
    public int DimY = 1;
    public int DimZ;
    public bool Solid;
    public bool Water;
    public bool Animated;
    public bool Occludes;
    public bool Transparent;
    public bool Translucent;
    public bool Door;
    public bool LightSource;
    public bool TileShape;
    public int ShapeClass;
    public bool HasQuantity => ShapeClass == 3;
    public bool HasQualityFlags => ShapeClass == 5;
    public bool HasQuality => ShapeClass is 2 or 6 or 7 or 11 or 12 or 13;
    public bool IsContainerClass => ShapeClass == 6;
    public bool IsBargeClass => ShapeClass == 9;
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
/// Shape names, TFA flags, and per-frame hotspots from the extracted CSVs
/// (Exult <c>TFA.DAT</c> / <c>SHPDIMS.DAT</c> / shape headers).
/// </summary>
public sealed class ShapeCatalog
{
    public ShapeRecord[] Shapes { get; }

    public ShapeCatalog()
    {
        Shapes = new ShapeRecord[U7Constants.MaxShapes];
        for (var i = 0; i < Shapes.Length; i++)
        {
            Shapes[i] = new ShapeRecord { Shape = i, TileShape = i < U7Constants.TileShapeCount };
        }

        LoadNames();
        LoadTypeFlags();
        LoadMountainTops();
        LoadFrames();
        LoadContainerDat();
        ApplyContainerFallbacks();
        LoadReadyDat();
        LoadPaperdollInfo();
        Shapes[644].Lightweight = true;
        Shapes[842].Lightweight = true;
    }

    public ShapeRecord this[int shape] =>
        (uint)shape < (uint)Shapes.Length ? Shapes[shape] : Shapes[0];

    void LoadNames()
    {
        var path = Path.Combine(U7Paths.TextDir, "shape_names.txt");
        if (!File.Exists(path))
        {
            return;
        }

        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var tab = line.IndexOf('\t');
            if (tab <= 0)
            {
                continue;
            }

            if (!int.TryParse(line.AsSpan(0, tab), out var index) || (uint)index >= (uint)Shapes.Length)
            {
                continue;
            }

            Shapes[index].Name = line[(tab + 1)..].Trim();
        }
    }

    void LoadTypeFlags()
    {
        var path = Path.Combine(U7Paths.DataDir, "typeflags.csv");
        if (!File.Exists(path))
        {
            return;
        }

        using var reader = new StreamReader(path);
        var header = reader.ReadLine();
        if (header is null)
        {
            return;
        }

        var cols = SplitCsv(header);
        var iShape = IndexOf(cols, "shape");
        var iName = IndexOf(cols, "shape_class_name");
        var iDx = IndexOf(cols, "dims_x");
        var iDy = IndexOf(cols, "dims_y");
        var iDz = IndexOf(cols, "dims_z");
        var iSolid = IndexOf(cols, "solid");
        var iWater = IndexOf(cols, "water");
        var iAnim = IndexOf(cols, "animated");
        var iOcc = IndexOf(cols, "occludes");
        var iTrans = IndexOf(cols, "transparent");
        var iTluc = IndexOf(cols, "translucency");
        var iDoor = IndexOf(cols, "door");
        var iLight = IndexOf(cols, "light_source");
        var iClass = IndexOf(cols, "shape_class");
        var iWeight = IndexOf(cols, "weight");
        var iVolume = IndexOf(cols, "volume");

        while (reader.ReadLine() is { } line)
        {
            var c = SplitCsv(line);
            if (c.Length <= iShape || !int.TryParse(c[iShape], out var shape) ||
                (uint)shape >= (uint)Shapes.Length)
            {
                continue;
            }

            var rec = Shapes[shape];
            rec.ClassName = Get(c, iName);
            rec.DimX = Math.Max(1, ParseInt(Get(c, iDx), 1));
            rec.DimY = Math.Max(1, ParseInt(Get(c, iDy), 1));
            rec.DimZ = ParseInt(Get(c, iDz), 0);
            rec.Solid = Get(c, iSolid) == "1";
            rec.Water = Get(c, iWater) == "1";
            rec.Animated = Get(c, iAnim) == "1";
            rec.Occludes = Get(c, iOcc) == "1";
            rec.Transparent = Get(c, iTrans) == "1";
            rec.Translucent = Get(c, iTluc) == "1";
            rec.Door = Get(c, iDoor) == "1";
            rec.LightSource = Get(c, iLight) == "1";
            rec.ShapeClass = ParseInt(Get(c, iClass), 0);
            rec.Weight = ParseInt(Get(c, iWeight), 0);
            rec.Volume = ParseInt(Get(c, iVolume), 0);
        }
    }

    /// <summary>Exult <c>data/bg/shape_info.txt</c> <c>mountain_tops</c>.</summary>
    void LoadMountainTops()
    {
        foreach (var shape in new[] { 180, 182, 183, 324, 969, 983 })
        {
            Shapes[shape].MountainTop = 1;
        }
    }

    void LoadFrames()
    {
        var path = Path.Combine(U7Paths.DataDir, "shape_frames.csv");
        if (!File.Exists(path))
        {
            return;
        }

        var buckets = new List<FrameInfo>[U7Constants.MaxShapes];
        using var reader = new StreamReader(path);
        var header = reader.ReadLine();
        if (header is null)
        {
            return;
        }

        var cols = SplitCsv(header);
        var iShape = IndexOf(cols, "shape");
        var iTile = IndexOf(cols, "is_tile");
        var iW = IndexOf(cols, "width");
        var iH = IndexOf(cols, "height");
        var iOx = IndexOf(cols, "origin_x");
        var iOy = IndexOf(cols, "origin_y");
        var iHx = IndexOf(cols, "hotspot_x_from_left");
        var iHy = IndexOf(cols, "hotspot_y_from_top");

        while (reader.ReadLine() is { } line)
        {
            var c = SplitCsv(line);
            if (c.Length <= iShape || !int.TryParse(c[iShape], out var shape) ||
                (uint)shape >= (uint)Shapes.Length)
            {
                continue;
            }

            var isTile = Get(c, iTile).Equals("True", StringComparison.OrdinalIgnoreCase);
            var w = ParseInt(Get(c, iW), 8);
            var h = ParseInt(Get(c, iH), 8);
            var xleft = ParseInt(Get(c, iHx), isTile ? 0 : Math.Max(0, w - 1));
            var yabove = ParseInt(Get(c, iHy), isTile ? 0 : Math.Max(0, h - 1));
            var xright = ParseInt(Get(c, iOx), Math.Max(0, w - xleft - 1));
            var ybelow = ParseInt(Get(c, iOy), Math.Max(0, h - yabove - 1));
            buckets[shape] ??= new List<FrameInfo>();
            buckets[shape].Add(new FrameInfo(w, h, xleft, yabove, xright, ybelow, isTile));
        }

        for (var i = 0; i < Shapes.Length; i++)
        {
            if (buckets[i] is { Count: > 0 } list)
            {
                Shapes[i].Frames = list.ToArray();
                Shapes[i].FrameCount = list.Count;
                Shapes[i].TileShape = list[0].IsTile;
            }
        }
    }

    static string[] SplitCsv(string line) => line.Split(',');

    static int IndexOf(string[] cols, string name)
    {
        for (var i = 0; i < cols.Length; i++)
        {
            if (cols[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 0;
    }

    static string Get(string[] cols, int i) => (uint)i < (uint)cols.Length ? cols[i] : "";

    static int ParseInt(string s, int fallback) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    /// <summary>
    /// Exult <c>Gump_reader_functor</c>: version byte, count, then
    /// (u16 shape, u16 gump_shape[, u16 gump_font]).
    /// Original BG has no CONTAINER.DAT; Exult ships it in exult_bg.flx.
    /// </summary>
    void LoadContainerDat()
    {
        var path = Path.Combine(U7Paths.StaticDir, "CONTAINER.DAT");
        if (!File.Exists(path))
        {
            path = Path.Combine(U7Paths.StaticDir, "container.dat");
        }

        if (!File.Exists(path))
        {
            return;
        }

        var data = File.ReadAllBytes(path);
        if (data.Length < 2)
        {
            return;
        }

        var i = 0;
        var version = data[i++];
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

        var recSize = version >= 2 ? 6 : 4;
        for (var n = 0; n < count && i + recSize <= data.Length; n++)
        {
            var shape = BitConverter.ToUInt16(data, i);
            var gump = (short)BitConverter.ToUInt16(data, i + 2);
            i += recSize;
            if (shape < Shapes.Length)
            {
                Shapes[shape].GumpShape = gump;
            }
        }
    }

    /// <summary>
    /// Map named BG containers (and the avatar) when CONTAINER.DAT is missing.
    /// </summary>
    void ApplyContainerFallbacks()
    {
        ApplyGumpIfUnset(U7Constants.AvatarShape, U7Constants.GumpActorMale);
        foreach (var rec in Shapes)
        {
            if (rec.GumpShape >= 0)
            {
                continue;
            }

            var name = rec.Name;
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            if (name.Contains("chest", StringComparison.OrdinalIgnoreCase))
            {
                rec.GumpShape = U7Constants.GumpChest;
            }
            else if (name.Contains("crate", StringComparison.OrdinalIgnoreCase))
            {
                rec.GumpShape = U7Constants.GumpCrate;
            }
            else if (name.Contains("barrel", StringComparison.OrdinalIgnoreCase))
            {
                rec.GumpShape = U7Constants.GumpBarrel;
            }
            else if (name.Contains("backpack", StringComparison.OrdinalIgnoreCase))
            {
                rec.GumpShape = U7Constants.GumpBackpack;
            }
            else if (name.Equals("bag", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains(" pouch", StringComparison.OrdinalIgnoreCase))
            {
                rec.GumpShape = U7Constants.GumpBag;
            }
            else if (name.Contains("basket", StringComparison.OrdinalIgnoreCase))
            {
                rec.GumpShape = U7Constants.GumpBasket;
            }
            else if (name.Contains("drawer", StringComparison.OrdinalIgnoreCase) ||
                     name.Equals("desk", StringComparison.OrdinalIgnoreCase))
            {
                rec.GumpShape = U7Constants.GumpDrawer;
            }
            else if (name.Equals("body", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("corpse", StringComparison.OrdinalIgnoreCase))
            {
                rec.GumpShape = U7Constants.GumpBody;
            }
            else if (name.Equals("box", StringComparison.OrdinalIgnoreCase))
            {
                rec.GumpShape = U7Constants.GumpBox;
            }
            else if (rec.IsContainerClass)
            {
                rec.GumpShape = U7Constants.GumpBackpack;
            }
        }
    }

    void ApplyGumpIfUnset(int shape, int gump)
    {
        if ((uint)shape < (uint)Shapes.Length && Shapes[shape].GumpShape < 0)
        {
            Shapes[shape].GumpShape = gump;
        }
    }

    /// <summary>
    /// READY.DAT: count, then (u16 shape, u8 ready_type, 6 pad) per Exult.
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
                var spot = U7.Actors.ReadySpot.FromBg(ready >> 3);
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
    /// Exult <c>data/bg/paperdol_info.txt</c> items: which world shapes may occupy
    /// which ready spots. Used by <c>fits_in_spot</c> even when PAPERDOL.VGA is absent.
    /// </summary>
    void LoadPaperdollInfo()
    {
        var path = Path.Combine(U7Paths.RepoRoot, "exult", "exult-1.12.1", "data", "bg", "paperdol_info.txt");
        if (!File.Exists(path))
        {
            return;
        }

        var buckets = new List<PaperdollSpot>[U7Constants.MaxShapes];
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
