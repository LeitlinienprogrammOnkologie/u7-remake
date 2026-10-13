using System.IO;
using Godot;
using U7.Core;

namespace U7.Rendering;

/// <summary>
/// The user's painted portraits in place of FACES.VGA's (2026-10-12): the
/// pictures in <c>u7-new-faces</c>, made ready by
/// <c>scripts/faces/prepare.py</c> into
/// <c>assets/faces/bg/face_SSS_frame_FFF.png</c> by FACES.VGA shape and
/// frame. Exult still decides which face shows, when and where; only its
/// picture is new. A face without a painting, or with none made, shows the
/// original art.
/// </summary>
public sealed class PaintedFaces
{
    readonly string _dir;

    /// <param name="game">The game's folder under assets/faces: bg (Serpent Isle's, si, are prepared alongside).</param>
    public PaintedFaces(string game)
    {
        _dir = Path.Combine(U7Paths.AssetsDir, "faces", game);
    }

    public bool Any => Directory.Exists(_dir);

    /// <summary>
    /// The painting of a face as a texture that reports the original's height
    /// (and its own shape), so it takes the original's place at the size the
    /// callers lay out, its finer pixels smoothly filtered; null without one.
    /// </summary>
    public Texture2D? Get(int shape, int frame, int originalHeight)
    {
        var path = Path.Combine(_dir, $"face_{shape:000}_frame_{frame:000}.png");
        if (!File.Exists(path) || Image.LoadFromFile(path) is not { } image || image.IsEmpty())
        {
            return null;
        }

        image.Convert(Image.Format.Rgba8);
        image.GenerateMipmaps();
        var tex = ImageTexture.CreateFromImage(image);
        var height = Math.Max(1, originalHeight);
        tex.SetSizeOverride(new Vector2I(Math.Max(1, Mathf.RoundToInt(height * (float)image.GetWidth() / image.GetHeight())), height));
        return new CanvasTexture { DiffuseTexture = tex, TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps };
    }
}
