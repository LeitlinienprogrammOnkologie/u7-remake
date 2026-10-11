using Godot;
using U7.Core;

namespace U7.UI;

/// <summary>
/// The painted map of Britannia (the user's pick, 2026-10-10) that
/// <c>display_map</c> shows in place of Exult's <c>sprites/map</c>, and where
/// a tile lies on it. The painting bends the real geography, so Exult's
/// straight <c>Paint_map</c> scale would miss its towns: the tile goes through
/// a cubic B-spline warp fitted to the painting's towns and coasts
/// (<c>scripts/painted_map_warp.py</c>, <c>data/map/britannia_warp.txt</c>).
/// </summary>
public sealed class PaintedMap
{
    readonly float _spacing;
    readonly int _size;
    readonly Vector2[] _points;

    PaintedMap(Texture2D texture, float spacing, int size, Vector2[] points)
    {
        Texture = texture;
        _spacing = spacing;
        _size = size;
        _points = points;
    }

    /// <summary>The painting, with mipmaps: it is shown smooth at any size.</summary>
    public Texture2D Texture { get; }

    public Vector2 Size => Texture.GetSize();

    /// <summary>The painting and its warp from <c>godot/data/map</c>, or null without them (the original map then shows).</summary>
    public static PaintedMap? Load()
    {
        var dir = Path.Combine(U7Paths.RepoRoot, "godot", "data", "map");
        var picture = Path.Combine(dir, "britannia.png");
        var warp = Path.Combine(dir, "britannia_warp.txt");
        if (!File.Exists(picture) || !File.Exists(warp))
        {
            return null;
        }

        var image = Image.LoadFromFile(picture);
        if (image is null || image.IsEmpty())
        {
            return null;
        }

        image.GenerateMipmaps();
        var spacing = 0f;
        var size = 0;
        var points = new List<Vector2>();
        foreach (var line in File.ReadLines(warp))
        {
            var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length != 2 || line.StartsWith('#'))
            {
                continue;
            }

            switch (words[0])
            {
                case "spacing":
                    spacing = float.Parse(words[1], System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "size":
                    size = int.Parse(words[1], System.Globalization.CultureInfo.InvariantCulture);
                    break;
                default:
                    points.Add(new Vector2(float.Parse(words[0], System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(words[1], System.Globalization.CultureInfo.InvariantCulture)));
                    break;
            }
        }

        if (spacing <= 0 || size < 4 || points.Count != size * size)
        {
            GD.PushWarning($"{warp}: not a warp grid; the original map shows.");
            return null;
        }

        return new PaintedMap(ImageTexture.CreateFromImage(image), spacing, size, points.ToArray());
    }

    /// <summary>The painting's pixel (from its top left) where a tile lies: the tile's centre through the warp.</summary>
    public Vector2 ToPixel(TileCoord tile)
    {
        var u = (tile.Tx + 0.5f) / _spacing + 1;
        var v = (tile.Ty + 0.5f) / _spacing + 1;
        var iu = Math.Clamp((int)MathF.Floor(u), 0, _size - 4);
        var iv = Math.Clamp((int)MathF.Floor(v), 0, _size - 4);
        Span<float> bu = stackalloc float[4];
        Span<float> bv = stackalloc float[4];
        Basis(u - iu, bu);
        Basis(v - iv, bv);
        var at = Vector2.Zero;
        for (var b = 0; b < 4; b++)
        {
            for (var a = 0; a < 4; a++)
            {
                at += bu[a] * bv[b] * _points[(iv + b) * _size + iu + a];
            }
        }

        return at;
    }

    /// <summary>The uniform cubic B-spline's four weights at <paramref name="t"/> (0-1).</summary>
    static void Basis(float t, Span<float> w)
    {
        var s = 1 - t;
        w[0] = s * s * s / 6;
        w[1] = (3 * t * t * t - 6 * t * t + 4) / 6;
        w[2] = (-3 * t * t * t + 3 * t * t + 3 * t + 1) / 6;
        w[3] = t * t * t / 6;
    }
}
