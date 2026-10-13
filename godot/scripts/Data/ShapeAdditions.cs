using System.IO;
using U7.Core;

namespace U7.Data;

/// <summary>One new shape: its number, art, the shape whose information it takes, and its name.</summary>
public sealed record ShapeAddition(int Shape, IShapeSource Art, int ArtShape, int InfoFrom, string Name);

/// <summary>
/// New shapes for Black Gate, from <c>shape_additions.txt</c> in the remake's
/// additions (<see cref="U7Paths.BasePatchDir"/>) and in a mod's patch folder.
/// The remake's own file: Exult gives a mod's new shapes through whole
/// replacement tables (TFA.DAT, WGTVOL.DAT, names), which, being made from
/// the game's, can't come with the remake. An entry is
/// <c>:shape/art/info from/name</c>: the art an Exult shape file beside it
/// (<c>keyring.shp</c>) or Serpent Isle's (<c>si:262</c>), the information
/// copied from an existing shape as Exult's <c>copy_info</c> does.
/// </summary>
public static class ShapeAdditions
{
    public static List<ShapeAddition> Load()
    {
        var list = new List<ShapeAddition>();
        FlexFile? serpent = null;
        foreach (var (file, dir) in ExultDataFile.Layers("shape_additions"))
        {
            foreach (var (_, text) in file.Entries("shapes"))
            {
                var p = text.Split('/');
                if (p.Length < 4 || !int.TryParse(p[0].Trim(), out var shape) || !int.TryParse(p[2].Trim(), out var from))
                {
                    continue;
                }

                var art = p[1].Trim();
                IShapeSource? source = null;
                var artShape = 0;
                if (art.StartsWith("si:", StringComparison.OrdinalIgnoreCase) && int.TryParse(art[3..], out var si))
                {
                    if (U7Paths.SerpentStaticFile("SHAPES.VGA") is { } path)
                    {
                        source = serpent ??= new FlexFile(path);
                        artShape = si;
                    }
                }
                else if (File.Exists(Path.Combine(dir, art)))
                {
                    source = ShapeList.Single(File.ReadAllBytes(Path.Combine(dir, art)));
                }

                if (source is null)
                {
                    continue;
                }

                list.RemoveAll(a => a.Shape == shape);
                list.Add(new ShapeAddition(shape, source, artShape, from, string.Join('/', p[3..]).Trim()));
            }
        }

        return list;
    }
}
