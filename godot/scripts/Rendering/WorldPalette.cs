using Godot;
using U7.Data;

namespace U7.Rendering;

/// <summary>
/// The palettes the world shader turns indices into colours with, two rows
/// of 256 RGBA8 texels: row 0 the ambient palette (Exult's final palette),
/// row 1 the lit one (the day palette, what a light shows). Both cycle
/// their colours as Exult's window palette does, and Exult's special pixels
/// (<c>Shape_manager::load</c>'s <c>special_pixels</c>, shapeid.cc) are the
/// indices nearest to fixed colours in the 6-bit day palette, for the outlines.
/// </summary>
public sealed class WorldPalette
{
    /// <summary>
    /// Milliseconds of real time between colour rotations. Exult's
    /// <c>rotatecolours</c> uses 100 in a paletted or unscaled window and 200
    /// in its default scaled one (<c>fast_palette_rotate</c>); the user chose 100.
    /// </summary>
    public const int RotateMs = 100;

    public const int Rows = 2;

    /// <summary>Exult <c>rotatecolours</c>' ranges: first index and count.</summary>
    static readonly (int First, int Count)[] Ranges = [(0xFC, 3), (0xF8, 4), (0xF4, 4), (0xF0, 4), (0xE8, 8), (0xE0, 8)];

    /// <summary>Each row as set, before rotation (8-bit RGB).</summary>
    readonly byte[][] _rows = [new byte[768], new byte[768]];
    readonly byte[] _rgba = new byte[Rows * 256 * 4];
    readonly Image _image;
    /// <summary>Exult's <c>last_rotate</c>.</summary>
    ulong _lastRotate;
    /// <summary>
    /// Rotations so far. Exult's window palette loses its rotation when a
    /// new palette is set (each minute of a transition); here the ranges
    /// keep turning across palette changes.
    /// </summary>
    int _rotations;

    public ImageTexture Texture { get; }
    /// <summary>Exult <c>POISON_PIXEL</c>: bright green.</summary>
    public byte Poison { get; }
    /// <summary>Exult <c>PROTECT_PIXEL</c>: light grey.</summary>
    public byte Protect { get; }
    /// <summary>Exult <c>CURSED_PIXEL</c>: yellow.</summary>
    public byte Cursed { get; }
    /// <summary>Exult <c>CHARMED_PIXEL</c>: light blue.</summary>
    public byte Charmed { get; }
    /// <summary>Exult <c>HIT_PIXEL</c>: red, for a hit in battle.</summary>
    public byte Hit { get; }
    /// <summary>Exult <c>PARALYZE_PIXEL</c>: purple.</summary>
    public byte Paralyze { get; }
    /// <summary>Exult <c>BLACK_PIXEL</c>.</summary>
    public byte Black { get; }

    public WorldPalette(ReadOnlySpan<byte> dayRgb)
    {
        dayRgb[..768].CopyTo(_rows[0]);
        dayRgb[..768].CopyTo(_rows[1]);
        Build();
        _image = Image.CreateFromData(256, Rows, false, Image.Format.Rgba8, _rgba);
        Texture = ImageTexture.CreateFromImage(_image);

        var raw = U7Palette.Raw6(U7Palette.Day) ?? new byte[768];
        Poison = Find(raw, 4, 63, 4);
        Protect = Find(raw, 62, 62, 55);
        Cursed = Find(raw, 62, 62, 5);
        Charmed = Find(raw, 30, 40, 63);
        Hit = Find(raw, 63, 4, 4);
        Paralyze = Find(raw, 49, 27, 49);
        Black = Find(raw, 0, 0, 0);
    }

    /// <summary>The ambient and lit rows (8-bit RGB), shown with the current rotation.</summary>
    public void SetRows(ReadOnlySpan<byte> ambientRgb, ReadOnlySpan<byte> litRgb)
    {
        ambientRgb[..768].CopyTo(_rows[0]);
        litRgb[..768].CopyTo(_rows[1]);
        Build();
        Update();
    }

    /// <summary>
    /// Exult <c>Game_window::rotatecolours</c>: once <see cref="RotateMs"/>
    /// has passed since the last rotation, each of the six ranges of colours
    /// from 0xE0 to 0xFE moves round by one (water, flames, the void). The
    /// texture changes only then. Returns whether it rotated.
    /// </summary>
    public bool Advance(ulong ticksMsec)
    {
        var speed = (ulong)RotateMs;
        if (ticksMsec <= _lastRotate + speed)
        {
            return false;
        }

        _rotations = (_rotations + 1) % 24; // every range's length divides 24
        while (ticksMsec > _lastRotate + speed)
        {
            _lastRotate += speed;
        }

        Build();
        Update();
        return true;
    }

    /// <summary>
    /// The rows into RGBA with the ranges rotated as often as Exult's
    /// <c>Image_window8::rotate_colors</c> upward would have: each step
    /// moves the last colour of a range to its first slot.
    /// </summary>
    void Build()
    {
        for (var row = 0; row < Rows; row++)
        {
            var src = _rows[row];
            var dst = _rgba.AsSpan(row * 1024, 1024);
            for (var i = 0; i < 256; i++)
            {
                dst[i * 4] = src[i * 3];
                dst[i * 4 + 1] = src[i * 3 + 1];
                dst[i * 4 + 2] = src[i * 3 + 2];
                dst[i * 4 + 3] = 255;
            }

            foreach (var (first, count) in Ranges)
            {
                var shift = _rotations % count;
                for (var k = 0; k < count; k++)
                {
                    var from = (first + k) * 3;
                    var to = (first + (k + shift) % count) * 4;
                    dst[to] = src[from];
                    dst[to + 1] = src[from + 1];
                    dst[to + 2] = src[from + 2];
                }
            }
        }
    }

    void Update()
    {
        _image.SetData(256, Rows, false, Image.Format.Rgba8, _rgba);
        Texture.Update(_image);
    }

    static byte Find(ReadOnlySpan<byte> raw6, int r, int g, int b) => (byte)U7Palette.FindColor(raw6, r, g, b);
}
