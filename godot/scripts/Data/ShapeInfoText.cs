using System.IO;
using U7.Core;

namespace U7.Data;

/// <summary>An entry keyed by frame and quality (-1 each for any), as Exult's frame-dependent shape information keeps them.</summary>
public readonly record struct FrameValue(int Frame, int Quality, int Value);

/// <summary>Exult <c>Frame_name_info</c>: type -255 is the shape's name, below 0 none, 0 a misc name, 1-2 with the other name before or after, 3+ an NPC's.</summary>
public readonly record struct FrameName(int Frame, int Quality, int Type, int MsgId, int OtherMsg);

/// <summary>Exult <c>Frame_usecode_info</c>: a function by name (UCC's), else by number (-1 none).</summary>
public readonly record struct FrameUsecode(int Frame, int Quality, string Name, int Usecode);

/// <summary>Exult <c>Content_rules</c>: whether a container takes a shape (-1 any).</summary>
public readonly record struct ContentRule(int Shape, bool Accept);

/// <summary>Exult <c>Animation_info</c> from <c>shape_info.txt</c>: Exult's <c>AniType</c> and its settings.</summary>
public readonly record struct AnimationText(int Type, int FrameCount, int FrameDelay, int SfxDelay, int FreezeFirst, int Recycle);

/// <summary>
/// Exult <c>SFX_info</c> (<c>shape_sfx</c>): the sound a shape makes while
/// shown: its number, the chance (%) of playing when its turn comes, a run
/// of <c>Range</c> numbers from it (random or in turn), an extra sound the
/// grandfather clock strikes the hour with, and its volume (%).
/// </summary>
public readonly record struct SfxInfo(int Num, int Chance, int Range, bool Random, int Extra, int Volume);

/// <summary>
/// Exult <c>Shapes_vga_file::Read_Shapeinf_text_data_file</c> and
/// <c>Read_Bodies_text_data_file</c>: the game's <c>shape_info.txt</c> and
/// <c>bodies.txt</c> (Exult's own, in <c>godot/data/bg</c> or <c>si</c>),
/// then the remake's additions' and a mod's over them, section by section,
/// each entry its shape and then the section's values. Read after TFA.DAT and
/// READY.DAT, so its animations and alternate spots win over theirs.
/// </summary>
public static class ShapeInfoText
{
    public static void Read(ShapeRecord[] shapes)
    {
        ReadFile("shape_info", shapes, ShapeInfoSections);
        ReadFile("bodies", shapes, BodySections);
    }

    delegate void SectionReader(ShapeRecord rec, EntryReader e, int version);

    static readonly (string Name, SectionReader Read)[] ShapeInfoSections =
    [
        ("explosions", Explosion),
        ("shape_sfx", ShapeSfx),
        ("animation", Animation),
        ("usecode_events", (r, e, _) => r.UsecodeEvents = Bit(e)),
        ("mountain_tops", (r, e, _) => r.MountainTop = e.Int()),
        ("monster_food", (r, e, _) => r.MonsterFood = e.Int()),
        ("actor_flags", (r, e, _) => r.ActorFlags = BitField(e, 8)),
        ("effective_hps", (r, e, _) => r.EffectiveHps = Add(r.EffectiveHps, new FrameValue(FrameOf(e), Quality(e.Int()), e.Int() & 0xff))),
        ("lightweight_object", (r, e, _) => r.Lightweight = Bit(e)),
        ("light_data", (r, e, _) => r.Lights = Add(r.Lights, new FrameValue(FrameOf(e), -1, e.Int() & 0xff))),
        ("warmth_data", (r, e, _) => r.Warmth = Add(r.Warmth, new FrameValue(FrameOf(e), -1, e.Int() & 0xff))),
        ("quantity_frames", (r, e, _) => r.QuantityFrames = Bit(e)),
        ("locked_containers", (r, e, _) => r.Locked = Bit(e)),
        ("content_rules", ContentRule),
        ("volatile_explosive", (r, e, _) => r.Volatile = Bit(e)),
        ("framenames", FrameNameEntry),
        ("altready", (r, e, _) => (r.ReadyAlt1, r.ReadyAlt2) = (Spot(e.Int(-1)), Spot(e.Int(-1)))),
        ("barge_type", (r, e, _) => r.BargeType = e.Int()),
        ("frame_powers", FrameFlags),
        ("is_jawbone", (r, e, _) => r.Jawbone = Bit(e)),
        ("is_mirror", (r, e, _) => r.Mirror = Bit(e)),
        ("on_fire", (r, e, _) => r.OnFire = Bit(e)),
        ("extradimensional_storage", (r, e, _) => r.ExtradimensionalStorage = Bit(e)),
        ("field_type", (r, e, _) => r.FieldType = (sbyte)e.Int()),
        ("frame_usecode", FrameUsecodeEntry)
    ];

    static readonly (string Name, SectionReader Read)[] BodySections =
    [
        ("bodyshapes", (r, e, _) => r.IsBody = Bit(e)),
        ("bodylist", Body)
    ];

    /// <summary>Exult <c>Read_text_data_file</c>: each section from the game's file, then from each patch over it, each file read at its own version.</summary>
    static void ReadFile(string name, ShapeRecord[] shapes, (string Name, SectionReader Read)[] sections)
    {
        var files = new List<ExultDataFile>();
        if (ExultDataFile.Static(name) is { } own)
        {
            files.Add(own);
        }

        files.AddRange(ExultDataFile.Layers(name).Select(l => l.File));
        foreach (var (section, read) in sections)
        {
            foreach (var file in files)
            {
                var version = file.Version;
                foreach (var (index, text) in file.Entries(section))
                {
                    var e = new EntryReader(text);
                    // Exult Body_ID_reader: version 1 of bodies.txt numbers its body shapes by line.
                    var shape = name == "bodies" && section == "bodyshapes" && version == 1 ? index : e.Int();
                    if ((uint)shape < (uint)shapes.Length)
                    {
                        read(shapes[shape], e, version);
                    }
                }
            }
        }
    }

    /// <summary>Exult <c>Bit_text_reader_functor</c>: on unless a 0 follows ("for backwards compatibility").</summary>
    static bool Bit(EntryReader e) => e.Int(1) != 0;

    /// <summary>Exult <c>Bit_field_text_reader_functor</c>: a bit per number while the entry lasts.</summary>
    static int BitField(EntryReader e, int bits)
    {
        var flags = 0;
        for (var bit = 0; e.Good && bit < bits; bit++)
        {
            if (e.Int() != 0)
            {
                flags |= 1 << bit;
            }
        }

        return flags;
    }

    /// <summary>Exult's frame field: below 0 any (-1), else its low byte.</summary>
    static int FrameOf(EntryReader e) => Quality(e.Int());

    static int Quality(int v) => v < 0 ? -1 : v & 0xff;

    /// <summary>Exult's alternate spots are unsigned chars: -1 is 255, none.</summary>
    static int Spot(int v) => v & 0xff;

    /// <summary>Exult <c>add_vector_info</c>: an entry of the same frame and quality is replaced, else added.</summary>
    static T[] Add<T>(T[] list, T entry) where T : struct
    {
        var key = KeyOf(entry);
        var i = Array.FindIndex(list, x => KeyOf(x) == key);
        if (i >= 0)
        {
            list[i] = entry;
            return list;
        }

        return [.. list, entry];
    }

    static (int, int) KeyOf<T>(T entry) => entry switch
    {
        FrameValue v => (v.Frame, v.Quality),
        FrameName n => (n.Frame, n.Quality),
        FrameUsecode u => (u.Frame, u.Quality),
        ContentRule c => (c.Shape, 0),
        _ => (0, 0)
    };

    /// <summary>Exult <c>Explosion_info::read</c>: the sprite (-0xff deletes) and sound (255 none).</summary>
    /// <summary>Exult <c>SFX_info::read</c>: <c>:shape/sfx/chance/range/rand/extra/volume</c>, the first two required.</summary>
    static void ShapeSfx(ShapeRecord r, EntryReader e, int _)
    {
        var num = e.Int();
        if (num == -0xff)
        {
            r.Sfx = null;
            return;
        }

        var chance = e.Int(100);
        if (chance < 1 || chance > 100)
        {
            chance = 100;
        }

        var range = Math.Max(1, e.Int(1));
        var random = e.Int() != 0;
        var extra = e.Int(-1);
        var volume = e.Int(100);
        r.Sfx = new SfxInfo(num, chance, range, random, extra, volume);
    }

    static void Explosion(ShapeRecord r, EntryReader e, int version)
    {
        var sprite = e.Int();
        if (sprite == -0xff)
        {
            r.Explosion = null;
            return;
        }

        var sfx = e.Int(-1);
        r.Explosion = (sprite, sfx == 255 ? -1 : sfx);
    }

    /// <summary>Exult <c>Animation_info::read</c> (from version 5): the type, frame count, then delays, and for a loop its freeze and recycle.</summary>
    static void Animation(ShapeRecord r, EntryReader e, int version)
    {
        if (version < 5)
        {
            return;
        }

        var type = e.Int();
        if (type == -0xff)
        {
            r.AnimationText = null;
            return;
        }

        var a = new AnimationText(type, e.Int(), 1, 0, 100, 0);
        // (The animation entry's own sfx_delay is read below; FA_HOURLY has none.)
        if (type != 1) // FA_HOURLY reads nothing more.
        {
            a = a with { FrameDelay = e.Int(), SfxDelay = e.Int() };
            if (type == 3) // FA_LOOPING.
            {
                a = a with { FreezeFirst = e.Int(), Recycle = e.Int() };
            }
        }

        r.AnimationText = a;
    }

    /// <summary>Exult <c>Content_rules::read</c>: a shape (-1 any) and whether the container takes it.</summary>
    static void ContentRule(ShapeRecord r, EntryReader e, int version)
    {
        var shape = e.Int();
        r.ContentRules = Add(r.ContentRules, new ContentRule(shape < 0 ? -1 : shape, e.Int() != 0));
    }

    /// <summary>Exult <c>Frame_name_info::read</c>.</summary>
    static void FrameNameEntry(ShapeRecord r, EntryReader e, int version)
    {
        var frame = FrameOf(e);
        var quality = Quality(e.Int());
        var type = e.Int();
        var (msg, other) = type >= 0 ? (e.Int(), e.Int(-255)) : (-1, -255);
        r.FrameNames = Add(r.FrameNames, new FrameName(frame, quality, type, msg, other));
    }

    /// <summary>Exult <c>Frame_flags_info::read</c>: the quality from version 6, then up to 32 flag bits.</summary>
    static void FrameFlags(ShapeRecord r, EntryReader e, int version)
    {
        var frame = FrameOf(e);
        var quality = version >= 6 ? Quality(e.Int()) : -1;
        r.FrameFlags = Add(r.FrameFlags, new FrameValue(frame, quality, BitField(e, 32)));
    }

    /// <summary>Exult <c>Frame_usecode_info::read</c>: a name after a nonzero type, else a number.</summary>
    static void FrameUsecodeEntry(ShapeRecord r, EntryReader e, int version)
    {
        var frame = FrameOf(e);
        var quality = Quality(e.Int());
        var entry = e.Int() != 0
            ? new FrameUsecode(frame, quality, e.Str(), -1)
            : new FrameUsecode(frame, quality, "", e.Int(-1));
        r.FrameUsecodes = Add(r.FrameUsecodes, entry);
    }

    /// <summary>Exult <c>Body_info::read</c>: the body's shape (-0xff deletes) and frame.</summary>
    static void Body(ShapeRecord r, EntryReader e, int version)
    {
        var shape = e.Int();
        r.Body = shape == -0xff ? null : (shape, e.Int());
    }

    /// <summary>
    /// Exult <c>Search_vector_data_double_wildcards</c>: the entry for the
    /// frame and quality, else the frame with any quality, else any frame with
    /// the quality, else any of both.
    /// </summary>
    public static T? Find<T>(T[] list, int frame, int quality, Func<T, (int Frame, int Quality)> key) where T : struct
    {
        if (list.Length == 0)
        {
            return null;
        }

        T? Match(int f, int q) => Array.FindIndex(list, x => key(x) == (f, q)) is var i and >= 0 ? list[i] : null;
        return Match(frame, quality) ??
               (quality != -1 ? Match(frame, -1) ?? Match(-1, quality) : null) ??
               Match(-1, -1);
    }

    /// <summary>
    /// Exult's <c>container.dat</c> (Gump_reader_functor), the game's then a
    /// mod's: a version byte, Exult's count, then per entry the shape, its
    /// gump and, from version 2, the gump's font.
    /// </summary>
    public static void ReadContainers(ShapeRecord[] shapes)
    {
        foreach (var path in new[] { Path.Combine(U7Paths.GameDataDir, "container.dat"), U7Paths.PatchFile("container.dat") })
        {
            if (path is null || !File.Exists(path))
            {
                continue;
            }

            var data = File.ReadAllBytes(path);
            if (data.Length < 2)
            {
                continue;
            }

            var version = data[0];
            var at = 1;
            int count = data[at++];
            if (count == 255 && at + 2 <= data.Length)
            {
                count = BitConverter.ToUInt16(data, at);
                at += 2;
            }

            var size = version >= 2 ? 6 : 4;
            for (var i = 0; i < count && at + size <= data.Length; i++, at += size)
            {
                var shape = BitConverter.ToUInt16(data, at);
                if (shape < shapes.Length)
                {
                    shapes[shape].GumpShape = BitConverter.ToInt16(data, at + 2);
                }
            }
        }
    }
}
