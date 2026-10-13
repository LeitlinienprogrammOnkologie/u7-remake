using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Godot;

namespace U7.Rendering;

/// <summary>
/// The console's <c>art dump</c> and <c>art diff</c> (docs/RENDERER.md, R0):
/// an <see cref="IndexBuffer8"/>'s planes as lossless PNGs, to compare two
/// backends, or one before and after a change, bit for bit. index.png and
/// glow.png are grey (the byte itself); panes.png has the window in red and
/// the table in green, black where the glow plane has no pane mark (the
/// plane means nothing there); mix.png the second index in red and how far
/// in green; colour.png the colour plane as it is. Each plane also gets a
/// short hash, so two dumps' logs tell at a glance whether they agree.
/// </summary>
public static class PlaneDump
{
    static readonly string[] Planes = ["index", "glow", "panes", "mix", "colour", "lift"];

    /// <summary>Writes the CPU buffer's planes into <paramref name="dir"/> (made if missing); the size and each plane's hash.</summary>
    public static string Write(IndexBuffer8 buffer, string dir) =>
        Write(dir, buffer.Width, buffer.Height, buffer.Pixels, buffer.Glow, buffer.Panes, buffer.Mix, buffer.Colour, buffer.MixUsed,
            buffer.ColourUsed, buffer.Lift);

    /// <summary>
    /// Writes the GPU backend's planes, read back as RGBA8 (the index and
    /// glow in red, the mix and panes in red and green, the colour as it is).
    /// </summary>
    public static string Write(GpuCompositor gpu, int w, int h, string dir)
    {
        var n = w * h;
        var index = new byte[n];
        var glow = new byte[n];
        var panes = new ushort[n];
        var mix = new byte[n * 2];
        var lift = new byte[n];
        var (ri, rg, rm, rp, rl) = (gpu.ReadIndex!, gpu.ReadGlow!, gpu.ReadMix!, gpu.ReadPanes!, gpu.ReadLift!);
        for (var i = 0; i < n; i++)
        {
            index[i] = ri[i * 4];
            glow[i] = rg[i * 4];
            lift[i] = rl[i * 4];
            mix[i * 2] = rm[i * 4];
            mix[i * 2 + 1] = rm[i * 4 + 1];
            panes[i] = (ushort)(rp[i * 4] | (rp[i * 4 + 1] << 8));
        }

        // The CPU's colour plane is zero until it counts; the GPU's texture is zero wherever no pixel has its own colour.
        var colour = gpu.LastColourUsed && gpu.ReadColour is { } rc ? rc : new byte[n * 4];
        return Write(dir, w, h, index, glow, panes, mix, colour, gpu.LastMixUsed, gpu.LastColourUsed, lift);
    }

    static string Write(string dir, int w, int h, byte[] index, byte[] glowPlane, ushort[] panePlane, byte[] mixPlane, byte[] colour,
        bool mixUsed, bool colourUsed, byte[] lift)
    {
        Directory.CreateDirectory(dir);
        var n = w * h;
        var panes = new byte[n * 3];
        var mix = new byte[n * 3];
        for (var i = 0; i < n; i++)
        {
            if ((glowPlane[i] & IndexBuffer8.PaneMark) != 0)
            {
                panes[i * 3] = (byte)panePlane[i];
                panes[i * 3 + 1] = (byte)(panePlane[i] >> 8);
            }

            mix[i * 3] = mixPlane[i * 2];
            mix[i * 3 + 1] = mixPlane[i * 2 + 1];
        }

        var hashes = new List<string>();
        Save("index", Image.Format.L8, index);
        Save("glow", Image.Format.L8, glowPlane);
        Save("panes", Image.Format.Rgb8, panes);
        Save("mix", Image.Format.Rgb8, mix);
        Save("colour", Image.Format.Rgba8, colour);
        Save("lift", Image.Format.L8, lift);
        var summary = $"{w}x{h}, {string.Join(", ", hashes)}" +
                      $"{(mixUsed ? ", mixed" : "")}{(colourUsed ? ", own colours" : "")}";
        File.WriteAllText(Path.Combine(dir, "planes.txt"), summary + "\n");
        return summary;

        void Save(string plane, Image.Format format, byte[] data)
        {
            Image.CreateFromData(w, h, false, format, data).SavePng(Path.Combine(dir, plane + ".png"));
            hashes.Add($"{plane} {Convert.ToHexString(SHA256.HashData(data))[..8].ToLowerInvariant()}");
        }
    }

    /// <summary>Compares two dumps plane by plane: the same, or how many pixels differ and the first.</summary>
    public static string Diff(string dirA, string dirB)
    {
        var lines = new List<string>();
        foreach (var plane in Planes)
        {
            var a = Image.LoadFromFile(Path.Combine(dirA, plane + ".png"));
            var b = Image.LoadFromFile(Path.Combine(dirB, plane + ".png"));
            if (a is null || b is null)
            {
                lines.Add($"{plane}: missing");
                continue;
            }

            if (a.GetSize() != b.GetSize() || a.GetFormat() != b.GetFormat())
            {
                lines.Add($"{plane}: {a.GetSize()} {a.GetFormat()} against {b.GetSize()} {b.GetFormat()}");
                continue;
            }

            var (da, db) = (a.GetData(), b.GetData());
            var width = a.GetWidth();
            var bytes = da.Length / (width * a.GetHeight());
            var differ = 0;
            var first = "";
            for (var i = 0; i < da.Length; i += bytes)
            {
                if (!da.AsSpan(i, bytes).SequenceEqual(db.AsSpan(i, bytes)) && differ++ == 0)
                {
                    var p = i / bytes;
                    first = $" (first at {p % width},{p / width}: {Bytes(da, i, bytes)} for {Bytes(db, i, bytes)})";
                }
            }

            lines.Add(differ == 0 ? $"{plane}: same" : $"{plane}: {differ} pixels differ{first}");
        }

        return string.Join("; ", lines);
    }

    static string Bytes(byte[] data, int at, int count) => string.Join("/", data[at..(at + count)]);
}
