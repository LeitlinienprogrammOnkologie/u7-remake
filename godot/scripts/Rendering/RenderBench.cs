using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Godot;

namespace U7.Rendering;

/// <summary>
/// The console's <c>art bench</c> (docs/RENDERER.md, R0): the view shown for
/// some frames at each zoom from 2 to 8, at the original scale and at the
/// art's, the scale forced past <see cref="ArtLook.MaxBufferPixels"/> and
/// <see cref="ArtLook.MinScreenPixels"/> (what the game would choose is
/// listed beside it). Each row has the means over the frames after a
/// warm-up: painting (and its worst), the upload, the viewport's render
/// times on the CPU and the GPU, and the whole frame (vsync included).
/// </summary>
public sealed class RenderBench
{
    /// <summary>Frames left out after each change: new frames are read and the render times lag a frame or two.</summary>
    const int WarmFrames = 10;

    readonly WorldView _world;
    readonly Action<float> _setZoom;
    readonly float _zoomBefore;
    readonly int _frames;
    readonly List<(float Zoom, int Scale)> _steps = [];
    readonly List<string> _rows = [];
    int _step = -1;
    int _frame;
    long _stamp;
    double _paint, _paintWorst, _upload, _cpu, _gpu, _wall;
    int _painted = -1;

    public RenderBench(WorldView world, Action<float> setZoom, float zoom, int frames)
    {
        _world = world;
        _setZoom = setZoom;
        _zoomBefore = zoom;
        _frames = Math.Max(1, frames);
        var scales = world.Art is { Scale: > 1 } art ? new[] { 1, art.Scale } : [1];
        for (var z = 2; z <= 8; z++)
        {
            foreach (var s in scales)
            {
                _steps.Add((z, s));
            }
        }
    }

    /// <summary>Called once a frame, before the world is drawn; false once every row is measured.</summary>
    public bool Step()
    {
        var now = Stopwatch.GetTimestamp();
        var wall = _stamp == 0 ? 0 : Stopwatch.GetElapsedTime(_stamp, now).TotalMilliseconds;
        _stamp = now;
        if (_step >= 0 && _frame > WarmFrames)
        {
            // What the last frame drawn took.
            var rid = _world.GetViewport().GetViewportRid();
            _paint += _world.LastPaintMs;
            _paintWorst = Math.Max(_paintWorst, _world.LastPaintMs);
            _upload += _world.LastUploadMs;
            _cpu += RenderingServer.ViewportGetMeasuredRenderTimeCpu(rid);
            _gpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid);
            _wall += wall;
            _painted = _world.PaintedScale;
        }

        if (_step < 0 || _frame++ >= WarmFrames + _frames)
        {
            if (_step >= 0)
            {
                Record();
            }

            if (++_step >= _steps.Count)
            {
                _world.ForcedScale = null;
                _setZoom(_zoomBefore);
                return false;
            }

            var (zoom, scale) = _steps[_step];
            _setZoom(zoom);
            _world.ForcedScale = scale;
            _frame = 1;
            _paint = _paintWorst = _upload = _cpu = _gpu = _wall = 0;
        }

        return true;
    }

    void Record()
    {
        var (zoom, scale) = _steps[_step];
        var (w, h) = (_world.BufferWidth, _world.BufferHeight);
        var n = (double)_frames;
        _rows.Add(string.Create(CultureInfo.InvariantCulture, $"| {zoom} | {scale}{(_painted != scale ? $" (painted at {_painted})" : "")} | {_world.ChosenScale} | {w}x{h} | " +
                  $"{w * h / 1e6:0.00} | {_paint / n:0.00} | {_paintWorst:0.0} | {_upload / n:0.00} | {_cpu / n:0.00} | {_gpu / n:0.00} | {_wall / n:0.0} |"));
    }

    /// <summary>The rows as a Markdown table, under what was measured with.</summary>
    public string Report()
    {
        var window = DisplayServer.WindowGetSize();
        var view = _world.GetViewport().GetVisibleRect().Size;
        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"art bench: window {window.X}x{window.Y} (view {view.X:0}x{view.Y:0}, {window.X / view.X:0.##} screen pixels a view pixel), " +
            $"{RenderingServer.GetVideoAdapterName()}, {RenderingServer.GetCurrentRenderingMethod()}, art {_world.Art?.Describe()}, " +
            $"{_frames} frames a row after {WarmFrames}"),
            "| zoom | scale | game's | buffer | Mpx | paint ms | worst | upload ms | render cpu | gpu | frame ms |",
            "|---|---|---|---|---|---|---|---|---|---|---|"
        };
        lines.AddRange(_rows);
        return string.Join("\n", lines);
    }
}
