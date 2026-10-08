using Godot;
using U7.Data;

namespace U7.Rendering;

/// <summary>
/// The colour a magic glow casts (cosmetic: Exult has no such light), found
/// once per shape from its own pixels: the average of the brightest fifth of
/// its opaque pixels over all its frames, in the day palette, scaled so its
/// brightest channel is 1.
/// </summary>
public sealed class GlowColours
{
    static readonly Vector3 Fallback = new(0.8f, 0.85f, 1f);

    readonly ShapeCache _shapes;
    readonly ShapeCatalog _catalog;
    readonly Dictionary<int, Vector3> _ofShape = new();
    readonly Dictionary<int, Vector3> _ofSprite = new();
    readonly List<(float Luma, Vector3 Rgb)> _pixels = new();

    public GlowColours(ShapeCache shapes, ShapeCatalog catalog)
    {
        _shapes = shapes;
        _catalog = catalog;
    }

    /// <summary>A SHAPES.VGA shape's glow colour.</summary>
    public Vector3 OfShape(int shape)
    {
        if (!_ofShape.TryGetValue(shape, out var c))
        {
            _ofShape[shape] = c = Derive(_catalog[shape].FrameCount, f => _shapes.GetFrame8(shape, f));
        }

        return c;
    }

    /// <summary>A SPRITES.VGA animation's glow colour.</summary>
    public Vector3 OfSprite(int sprite)
    {
        if (!_ofSprite.TryGetValue(sprite, out var c))
        {
            _ofSprite[sprite] = c = Derive(_shapes.SpritesVga.FrameCount(sprite), f => _shapes.GetSprite8(sprite, f));
        }

        return c;
    }

    Vector3 Derive(int frames, Func<int, ShapeFrame?> get)
    {
        var palette = _shapes.DayPalette;
        _pixels.Clear();
        for (var f = 0; f < frames; f++)
        {
            if (get(f) is not { IsRle: true } frame)
            {
                continue;
            }

            foreach (var scan in frame.Scans)
            {
                for (var i = 0; i < scan.Length; i++)
                {
                    // Translucent colours (0xEE-0xFE) only tint what is under them.
                    var c = frame.Pixels[scan.Offset + i];
                    if (c >= XformTables.FirstTranslucent)
                    {
                        continue;
                    }

                    var rgb = new Vector3(palette[3 * c], palette[3 * c + 1], palette[3 * c + 2]) / 255f;
                    _pixels.Add((0.299f * rgb.X + 0.587f * rgb.Y + 0.114f * rgb.Z, rgb));
                }
            }
        }

        if (_pixels.Count == 0)
        {
            return Fallback;
        }

        _pixels.Sort((a, b) => b.Luma.CompareTo(a.Luma));
        var n = Math.Max(1, _pixels.Count / 5);
        var sum = Vector3.Zero;
        for (var i = 0; i < n; i++)
        {
            sum += _pixels[i].Rgb;
        }

        var mean = sum / n;
        var max = Mathf.Max(mean.X, Mathf.Max(mean.Y, mean.Z));
        return max > 0 ? mean / max : Fallback;
    }
}
