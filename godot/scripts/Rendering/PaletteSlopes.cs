using Godot;
using U7.Data;

namespace U7.Rendering;

/// <summary>
/// How the palettes change the art's own colours (the remake's true colour,
/// <see cref="ArtLook.TrueColour"/>; Exult has none): a true-colour pixel
/// shows as <c>palette[anchor] + M (rgb - day[anchor])</c>, its anchor the
/// nearest palette entry and M the slopes of the affine map that best fits
/// how the palette changes the day palette's colours round that entry
/// (least squares weighted by a Gaussian of <see cref="Sigma"/> levels).
/// So a pixel of its anchor's own colour is exactly the index pixel, and
/// every palette Exult shows (the hours' blends, overcast, fog, invisible,
/// lightning) changes the shades between entries as it changes the entries
/// near them. A ratio <c>palette / day</c> per entry would not do: on the
/// invisible, fog and red palettes it misses neighbouring entries by 30 to
/// 60 levels; these slopes by 1 to 3 (12 on lightning's).
/// </summary>
public sealed class PaletteSlopes
{
    /// <summary>The entries a true colour can be anchored to: those below Exult's cycling ranges.</summary>
    public const int Anchors = 0xE0;
    /// <summary>The fit's reach in 8-bit levels (the closest fit of 25 to 80 on Black Gate's palettes).</summary>
    const double Sigma = 30;
    /// <summary>Texture rows: the day palette, then three rows of slopes for each of the ambient and lit palettes.</summary>
    public const int Rows = 7;

    readonly byte[] _day;
    /// <summary>
    /// Per anchor, three rows of <see cref="Anchors"/> weights: slope k of
    /// any palette is <c>sum_j K[k][j] palette[j]</c>, the weighted least
    /// squares solved once for the day palette's colours.
    /// </summary>
    readonly float[] _k = new float[Anchors * 3 * Anchors];
    readonly float[] _texels = new float[256 * Rows * 4];
    readonly Image _image;

    public ImageTexture Texture { get; }

    /// <summary>Slope k (the input's red, green or blue) of output channel <paramref name="c"/> at an anchor, for the palette of <paramref name="row"/> (1 ambient, 4 lit), as last built.</summary>
    public float Slope(int row, int anchor, int c, int k) => _texels[((row + c) * 256 + anchor) * 4 + k];

    public PaletteSlopes(ReadOnlySpan<byte> dayRgb)
    {
        _day = dayRgb[..768].ToArray();
        var weights = new double[Anchors];
        for (var a = 0; a < Anchors; a++)
        {
            for (var j = 0; j < Anchors; j++)
            {
                double d = 0;
                for (var c = 0; c < 3; c++)
                {
                    var e = _day[j * 3 + c] - _day[a * 3 + c];
                    d += e * e;
                }

                weights[j] = Math.Exp(-d / (2 * Sigma * Sigma));
            }

            var inverse = ColourFit.Invert(ColourFit.Normal(_day, weights, Anchors));
            for (var j = 0; j < Anchors; j++)
            {
                Span<double> x = [_day[j * 3], _day[j * 3 + 1], _day[j * 3 + 2], 1];
                for (var k = 0; k < 3; k++)
                {
                    double s = 0;
                    for (var r = 0; r < 4; r++)
                    {
                        s += inverse[k * 4 + r] * x[r];
                    }

                    _k[(a * 3 + k) * Anchors + j] = (float)(s * weights[j]);
                }
            }
        }

        for (var i = 0; i < 256; i++)
        {
            for (var c = 0; c < 3; c++)
            {
                _texels[i * 4 + c] = _day[i * 3 + c] / 255f;
            }

            _texels[i * 4 + 3] = 1;
        }

        Fit(_day, 1);
        Fit(_day, 4);
        _image = Image.CreateFromData(256, Rows, false, Image.Format.Rgbaf, Bytes());
        Texture = ImageTexture.CreateFromImage(_image);
    }

    /// <summary>The slopes for an ambient and a lit palette (8-bit RGB, before rotation: anchors don't cycle).</summary>
    public void Build(ReadOnlySpan<byte> ambientRgb, ReadOnlySpan<byte> litRgb)
    {
        Fit(ambientRgb, 1);
        Fit(litRgb, 4);
        _image.SetData(256, Rows, false, Image.Format.Rgbaf, Bytes());
        Texture.Update(_image);
    }

    /// <summary>
    /// Rows <paramref name="row"/> to row + 2: for each anchor, slope row c
    /// (RGB) is how output channel c follows the input's red, green and
    /// blue. The day palette itself gives the identity.
    /// </summary>
    void Fit(ReadOnlySpan<byte> pal, int row)
    {
        var identity = pal[..(Anchors * 3)].SequenceEqual(_day.AsSpan(0, Anchors * 3));
        for (var a = 0; a < 256; a++)
        {
            for (var c = 0; c < 3; c++)
            {
                var at = ((row + c) * 256 + a) * 4;
                for (var k = 0; k < 3; k++)
                {
                    float s;
                    if (identity || a >= Anchors)
                    {
                        s = c == k ? 1 : 0;
                    }
                    else
                    {
                        var weights = _k.AsSpan((a * 3 + k) * Anchors, Anchors);
                        s = 0;
                        for (var j = 0; j < Anchors; j++)
                        {
                            s += weights[j] * pal[j * 3 + c];
                        }
                    }

                    _texels[at + k] = s;
                }

                _texels[at + 3] = 0;
            }
        }
    }

    byte[] Bytes()
    {
        var bytes = new byte[_texels.Length * 4];
        Buffer.BlockCopy(_texels, 0, bytes, 0, bytes.Length);
        return bytes;
    }
}
