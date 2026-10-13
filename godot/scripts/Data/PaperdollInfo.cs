namespace U7.Data;

/// <summary>Exult <c>Paperdoll_npc</c>: a character's paperdoll body, head and arms (PAPERDOL.VGA shapes).</summary>
public sealed record PaperdollNpc(bool Female, bool Translucent, int BodyShape, int BodyFrame, int HeadShape,
    int HeadFrame, int HeadFrameHelm, int ArmsShape, int[] ArmsFrames);

/// <summary>
/// Exult <c>Paperdoll_item</c>: how a worn thing (world frame, -1 for any)
/// shows in a spot: its PAPERDOL.VGA shape and frames, the arms or helmet
/// variant (<see cref="Type"/>), a frame on for men (<see cref="Gender"/>).
/// </summary>
public sealed record PaperdollItem(int WorldFrame, bool Translucent, int Spot, int Type, bool Gender, int Shape,
    int[] Frames)
{
    /// <summary>Exult <c>get_paperdoll_frame</c>: that alternate (-1, none, draws nothing), the base frame outside 0-3.</summary>
    public int Frame(int num) => num is >= 0 and < 4 ? Frames[num] : Frames[0];
}

/// <summary>
/// Exult's <c>paperdol_info.txt</c> (and a mod's): the characters
/// (by shape, the imported skins by <c>%n</c>) and the items.
/// </summary>
public sealed class PaperdollInfo
{
    /// <summary>Exult's spots for paperdolling only (<c>Ready_type_Exult</c>).</summary>
    public const int CloakClasp = 102;

    readonly Dictionary<int, PaperdollNpc> _npcs = new();
    readonly Dictionary<int, List<PaperdollItem?>> _items = new();

    public static PaperdollInfo Load(ShapeFiles files)
    {
        var info = new PaperdollInfo();
        if (ExultDataFile.Static("paperdol_info") is { } bundled)
        {
            info.Read(bundled, files);
        }

        if (ExultDataFile.Patch("paperdol_info") is { } patch)
        {
            info.Read(patch, files);
        }

        return info;
    }

    void Read(ExultDataFile file, ShapeFiles files)
    {
        var version = file.Version;
        foreach (var (_, text) in file.Entries("characters"))
        {
            var p = Fields(text);
            if (p.Length < 2 || ShapeFiles.Ref(p[0], files.SkinVars) is not { } shape)
            {
                continue;
            }

            var sex = Int(p[1]);
            if (sex == -0xff)
            {
                _npcs.Remove(shape);
                continue;
            }

            if (p.Length < 12)
            {
                continue;
            }

            _npcs[shape] = new PaperdollNpc(sex != 0, Int(p[2]) != 0, Int(p[3]), Int(p[4]), Int(p[5]), Int(p[6]),
                Int(p[7]), Int(p[8]), [Int(p[9]), Int(p[10]), Int(p[11])]);
        }

        foreach (var (_, text) in file.Entries("items"))
        {
            var p = Fields(text);
            if (p.Length < 5 || !int.TryParse(p[0], out var shape))
            {
                continue;
            }

            var worldFrame = Int(p[1]);
            var spot = Int(p[3]);
            var ty = Int(p[4]);
            var list = _items.TryGetValue(shape, out var l) ? l : _items[shape] = new List<PaperdollItem?>();
            list.RemoveAll(i => i is not null && i.WorldFrame == worldFrame && i.Spot == spot);
            if (ty == -255 || p.Length < 8)
            {
                // Exult's 'invalid' marker: hides the entry.
                list.Add(new PaperdollItem(worldFrame, false, spot, -255, false, -1, [-1, -1, -1, -1]));
                continue;
            }

            // Exult Paperdoll_item::read: the arm or helmet type counts only in the head and left hand.
            var type = spot is not (0 or 3) ? 0
                : version == 1 ? ty switch { 2 or 7 => 1, 3 => 2, _ => 0 }
                : ty;
            int Opt(int i) => p.Length > i ? Int(p[i]) : -1;
            list.Add(new PaperdollItem(worldFrame, Int(p[2]) != 0, spot, type, Int(p[5]) != 0, Int(p[6]),
                [Int(p[7]), Opt(8), Opt(9), Opt(10)]));
        }
    }

    public PaperdollNpc? Npc(int shape) => _npcs.GetValueOrDefault(shape);

    /// <summary>
    /// Exult <c>Shape_info::get_item_paperdoll</c>: the entry for this world
    /// frame and spot, else for any frame; the spots that fill others map to
    /// theirs (both hands to the left, the glove pair to the left finger, the
    /// neck to the amulet, the scabbard to the belt). Null when none (or invalid).
    /// </summary>
    public PaperdollItem? Item(int shape, int frame, int spot)
    {
        if (!_items.TryGetValue(shape, out var list))
        {
            return null;
        }

        spot = spot switch
        {
            U7.Actors.ReadySpot.BothHands => U7.Actors.ReadySpot.Lhand,
            U7.Actors.ReadySpot.GlovesPair => U7.Actors.ReadySpot.Lfinger,
            U7.Actors.ReadySpot.NeckFill => U7.Actors.ReadySpot.Neck,
            U7.Actors.ReadySpot.Scabbard => U7.Actors.ReadySpot.Belt,
            _ => spot
        };
        var found = list.Find(i => i is not null && i.Spot == spot && i.WorldFrame == frame) ??
                    list.Find(i => i is not null && i.Spot == spot && i.WorldFrame == -1);
        return found is { Type: -255 } ? null : found;
    }

    static string[] Fields(string text) => text.Split('/').Select(f => f.Trim()).ToArray();

    static int Int(string s) =>
        s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToInt32(s[2..], 16)
        : int.TryParse(s, out var n) ? n : 0;
}
