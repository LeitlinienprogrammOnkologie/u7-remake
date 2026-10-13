namespace U7.Data;

/// <summary>
/// Weighted least squares of an affine colour map, <c>to = M from + b</c>,
/// from pairs of palette colours: how a palette or a translucency table,
/// which Exult defines only on its 256 entries, changes the colours between
/// them (the remake's true colour, <c>ArtLook.TrueColour</c>; Exult has none).
/// </summary>
public static class ColourFit
{
    /// <summary>
    /// The normal equations' matrix <c>sum w x x^T</c> (4x4, x = (r, g, b, 1))
    /// of the colours <paramref name="from"/> (8-bit RGB, <paramref name="count"/>
    /// of them) weighted by <paramref name="weights"/>.
    /// </summary>
    public static double[] Normal(ReadOnlySpan<byte> from, ReadOnlySpan<double> weights, int count)
    {
        var n = new double[16];
        for (var i = 0; i < count; i++)
        {
            var w = weights[i];
            Span<double> x = [from[3 * i], from[3 * i + 1], from[3 * i + 2], 1];
            for (var r = 0; r < 4; r++)
            {
                for (var c = 0; c < 4; c++)
                {
                    n[r * 4 + c] += w * x[r] * x[c];
                }
            }
        }

        // A touch of ridge on the slopes, so a degenerate set still solves.
        var trace = n[0] + n[5] + n[10];
        for (var k = 0; k < 3; k++)
        {
            n[k * 5] += 1e-6 * trace + 1e-9;
        }

        return n;
    }

    /// <summary>The inverse of a 4x4 matrix (Gauss-Jordan with partial pivoting).</summary>
    public static double[] Invert(double[] m)
    {
        var a = (double[])m.Clone();
        var inv = new double[16];
        for (var i = 0; i < 4; i++)
        {
            inv[i * 5] = 1;
        }

        for (var col = 0; col < 4; col++)
        {
            var pivot = col;
            for (var r = col + 1; r < 4; r++)
            {
                if (Math.Abs(a[r * 4 + col]) > Math.Abs(a[pivot * 4 + col]))
                {
                    pivot = r;
                }
            }

            for (var c = 0; c < 4; c++)
            {
                (a[col * 4 + c], a[pivot * 4 + c]) = (a[pivot * 4 + c], a[col * 4 + c]);
                (inv[col * 4 + c], inv[pivot * 4 + c]) = (inv[pivot * 4 + c], inv[col * 4 + c]);
            }

            var d = a[col * 4 + col];
            for (var c = 0; c < 4; c++)
            {
                a[col * 4 + c] /= d;
                inv[col * 4 + c] /= d;
            }

            for (var r = 0; r < 4; r++)
            {
                if (r == col)
                {
                    continue;
                }

                var f = a[r * 4 + col];
                for (var c = 0; c < 4; c++)
                {
                    a[r * 4 + c] -= f * a[col * 4 + c];
                    inv[r * 4 + c] -= f * inv[col * 4 + c];
                }
            }
        }

        return inv;
    }

    /// <summary>
    /// The slopes M (row-major 3x3, written to <paramref name="slopes"/>) of the
    /// affine map taking the colours <paramref name="from"/> to <paramref name="to"/>
    /// (8-bit RGB, <paramref name="count"/> pairs) under <paramref name="weights"/>.
    /// </summary>
    public static void Slopes(ReadOnlySpan<byte> from, ReadOnlySpan<byte> to, ReadOnlySpan<double> weights, int count, Span<float> slopes)
    {
        var inverse = Invert(Normal(from, weights, count));
        Span<double> b = stackalloc double[12];
        for (var i = 0; i < count; i++)
        {
            var w = weights[i];
            Span<double> x = [from[3 * i], from[3 * i + 1], from[3 * i + 2], 1];
            for (var r = 0; r < 4; r++)
            {
                for (var c = 0; c < 3; c++)
                {
                    b[r * 3 + c] += w * x[r] * to[3 * i + c];
                }
            }
        }

        // The solution's first three rows are M transposed: row k, column c is d(to_c)/d(from_k).
        for (var c = 0; c < 3; c++)
        {
            for (var k = 0; k < 3; k++)
            {
                double s = 0;
                for (var r = 0; r < 4; r++)
                {
                    s += inverse[k * 4 + r] * b[r * 3 + c];
                }

                slopes[c * 3 + k] = (float)s;
            }
        }
    }
}
