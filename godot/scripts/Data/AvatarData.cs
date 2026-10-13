namespace U7.Data;

/// <summary>Exult <c>Skin_data</c>: one skin colour and sex's body, naked body and faces.</summary>
public sealed record SkinData(int Skin, bool Female, int Shape, int NakedShape, int FaceShape, int FaceFrame,
    int AlterFaceShape, int AlterFaceFrame, bool CopyInfo);

/// <summary>
/// Exult <c>Shapeinfo_lookup::setup_avatar_data</c> (<c>avatar_data.txt</c>,
/// then a mod's own): the avatar's default shapes and faces by sex, the
/// default skin, and Black Gate's skin colours (two of them Serpent Isle's
/// imported bodies, <c>%0</c>... from <see cref="ShapeFiles"/>).
/// </summary>
public sealed class AvatarData
{
    /// <summary>Exult <c>def_av_info</c>: by sex, the shape and face.</summary>
    public Dictionary<bool, (int Shape, int FaceShape, int FaceFrame)> Defaults { get; } = new();
    /// <summary>Exult <c>base_av_info</c>: the skin and sex a new game starts with.</summary>
    public int DefaultSkin { get; private set; }
    public bool DefaultFemale { get; private set; }
    public List<SkinData> Skins { get; } = new();
    /// <summary>Exult <c>unselectable_skins</c>: skins the new game's screen doesn't offer.</summary>
    public HashSet<int> Unselectable { get; } = new();
    /// <summary>Exult <c>Avatar_data::last_skin</c>: the highest skin number.</summary>
    public int LastSkin { get; private set; }
    /// <summary>Exult <c>petra_table</c> (Serpent Isle): with the Petra flag, a face shows another's face.</summary>
    public Dictionary<int, int> PetraFaces { get; } = new();

    public static AvatarData Load(ShapeFiles files)
    {
        var data = new AvatarData();
        if (ExultDataFile.Static("avatar_data") is { } bundled)
        {
            data.Read(bundled, false, files);
        }

        if (ExultDataFile.Patch("avatar_data") is { } patch)
        {
            data.Read(patch, true, files);
        }

        return data;
    }

    void Read(ExultDataFile file, bool patch, ShapeFiles files)
    {
        foreach (var (_, text) in file.Entries("defaultshape"))
        {
            var v = Ints(text);
            if (v.Length >= 4)
            {
                Defaults[v[0] != 0] = (v[1], v[2], v[3]);
            }
        }

        foreach (var (_, text) in file.Entries("baseracesex"))
        {
            var v = Ints(text);
            if (v.Length >= 2)
            {
                DefaultSkin = v[0];
                DefaultFemale = v[1] != 0;
            }
        }

        foreach (var (_, text) in file.Entries("multiracial_table"))
        {
            // Exult Multiracial_parser: skin/sex/shape/naked/face/frame/alterface/alterframe[/copydata].
            var p = text.Split('/');
            if (p.Length < 8 || !int.TryParse(p[0], out var skin) ||
                ShapeFiles.Ref(p[2], files.SkinVars) is not { } shape ||
                ShapeFiles.Ref(p[3], files.SkinVars) is not { } naked)
            {
                continue;
            }

            LastSkin = Math.Max(LastSkin, skin);
            var entry = new SkinData(skin, Int(p[1]) != 0, shape, naked, Int(p[4]), Int(p[5]), Int(p[6]), Int(p[7]),
                file.Version != 2 || p.Length < 9 || Int(p[8]) != 0);
            var found = patch ? Skins.FindIndex(s => s.Skin == skin && s.Female == entry.Female) : -1;
            if (found >= 0)
            {
                Skins[found] = entry;
            }
            else
            {
                Skins.Add(entry);
            }
        }

        foreach (var (_, text) in file.Entries("unselectable_races_table"))
        {
            Unselectable.Add(Int(text));
        }

        foreach (var (_, text) in file.Entries("petra_face_table"))
        {
            var v = Ints(text);
            if (v.Length >= 2)
            {
                PetraFaces[v[0]] = v[1];
            }
        }
    }

    /// <summary>Exult <c>GetSkinInfo</c>: the skin and sex's entry, or null.</summary>
    public SkinData? Skin(int skin, bool female) => Skins.Find(s => s.Skin == skin && s.Female == female);

    static int[] Ints(string text) => text.Split('/').Select(Int).ToArray();

    static int Int(string s)
    {
        s = s.Trim();
        return s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToInt32(s[2..], 16)
            : int.TryParse(s, out var n) ? n : 0;
    }
}
