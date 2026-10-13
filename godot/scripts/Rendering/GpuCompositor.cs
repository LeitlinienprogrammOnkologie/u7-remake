using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;
using U7.Data;

namespace U7.Rendering;

/// <summary>
/// The GPU backend of the command stream (docs/RENDERER.md, R2: the
/// feasibility gate; R3: classic parity, 5.2's two resolves; R4: the art
/// store with its pages and the colour plane). The commands are binned to
/// tiles of the buffer on the CPU; a compute shader resolves every pixel by
/// walking its tile's list from the bottom up and applying each op as
/// <see cref="IndexBuffer8"/> does, the planes as per-thread state (the
/// reference resolve), or, with <see cref="Skip"/>, from the top down to
/// the topmost op that replaces every plane at the pixel and then the
/// transforms above it in order (the skipping resolve, the same planes by
/// 5.1), then writes the index, glow, mix, pane and colour planes as
/// textures that <see cref="WorldView"/> draws (<see cref="Texture2Drd"/>;
/// no readback but for <c>art dump</c>). The art store is one storage
/// buffer of dense frames, a uint a texel (the index, its alpha as the
/// coverage, the mix bytes) and a second one for a frame's own colours,
/// split into a ring of pages (<see cref="ArtLook.StoreMb"/>): a new frame
/// goes into the current page, and when it is full the oldest page is
/// freed and taken over, so a teleport evicts the old view page by page
/// rather than emptying the store. An animated shape's frames are packed
/// together (<see cref="Siblings"/>). Every device call runs on the render
/// thread (<c>CallOnRenderThread</c>); the device is the global one, null
/// headless and under Compatibility, when the CPU backend paints instead.
/// </summary>
public sealed class GpuCompositor
{
    /// <summary>Buffer pixels a tile is, each way: a work group resolves one.</summary>
    public const int TileSize = 16;
    const int CmdInts = 16;
    const int MaxMasks = 256;
    /// <summary>The store's pages: freed one at a time, the oldest first, when it is full.</summary>
    const int PageCount = 8;

    /// <summary>What of a frame an entry holds: its pixels, its coverage alone (the solid ops), or Exult's outline blocks.</summary>
    enum Kind : byte
    {
        Pixels,
        Solid,
        Outline
    }

    enum Op
    {
        Fill,
        Rle,
        Solid,
        Translucent,
        ThroughPanes,
        Transformed
    }

    /// <summary>A frame in the art store: where, how big, where its dense rectangle's top left lies from the hotspot, and its planes (a coloured frame takes two uints a texel).</summary>
    readonly record struct Entry(int Offset, int W, int H, int Dx, int Dy, bool HasMix, bool HasColour)
    {
        public bool IsEmpty => W == 0 || H == 0;
    }

    /// <summary>A page of the store: its uints, how many are used, the frames in it, and the last prepare that put one there.</summary>
    sealed class Page(int start, int size)
    {
        public readonly int Start = start;
        public readonly int Size = size;
        public int Used;
        public uint Serial;
        public readonly List<(ArtRef Source, int Scale, Kind Kind)> Keys = [];
    }

    /// <summary>One frame's CPU-side data, double-buffered: the render thread reads one while the next is built.</summary>
    sealed class FrameData
    {
        public int[] Cmds = new int[CmdInts * 4096];
        public int CmdCount;
        public int[] TileOffsets = [];
        public int[] Entries = new int[1 << 16];
        public int EntryCount;
        public uint[] Masks = new uint[MaxMasks * 8];
        public int MaskCount;
        public int Width, Height, TilesX, TilesY, MixFrom, ColourFrom, Scale;
        public readonly List<(uint Offset, uint[] Data)> Uploads = [];
        public bool Readback;
    }

    readonly RenderingDevice _rd;
    readonly XformTables _xforms;
    readonly Callable _initCallable;
    readonly Callable _submitCallable;
    readonly Callable _resizeCallable;
    readonly FrameData[] _frames = [new FrameData(), new FrameData()];
    int _parity;
    FrameData? _submitting;

    Rid _shader, _pipeline, _atlas, _cmdBuf, _tileBuf, _entryBuf, _maskBuf, _xformBuf, _dayBuf, _slopeBuf, _lutBuf, _set;
    int _lutSize;
    uint _cmdCap, _tileCap, _entryCap;
    uint _atlasBytes;
    Page[] _pages = [];
    int _page;
    uint _prepareSerial;
    bool _setDirty = true;
    volatile bool _ready;
    volatile bool _failed;
    volatile int _initState;

    /// <summary>
    /// The frames in the store by what they are (the command's identity and
    /// the scale painted), not by the frame object: a terrain's flats or a
    /// pack's frame made again is the same picture. Emptied when the art
    /// changes (<see cref="HiresArt.Version"/>).
    /// </summary>
    readonly Dictionary<(ArtRef Source, int Scale, Kind Kind), Entry> _entries = new();
    int _artVersion = -1;
    bool _packingSiblings;
    /// <summary>Scratch for the binning.</summary>
    int[] _tileCounts = [];
    int[] _tileFill = [];
    (int X0, int Y0, int X1, int Y1)[] _rects = new (int, int, int, int)[4096];

    Rid _indexTex, _glowTex, _mixTex, _panesTex, _colourTex, _liftTex;
    int _texW, _texH;
    volatile int _texState;
    int _wantW, _wantH;

    /// <summary>The planes as textures, drawn and sampled by the world shader once <see cref="TexturesReady"/>.</summary>
    public Texture2Drd? IndexTexture { get; private set; }
    public Texture2Drd? GlowTexture { get; private set; }
    public Texture2Drd? MixTexture { get; private set; }
    public Texture2Drd? PanesTexture { get; private set; }
    public Texture2Drd? ColourTexture { get; private set; }
    public Texture2Drd? LiftTexture { get; private set; }

    /// <summary>
    /// The skipping resolve (RENDERER.md 5.2) rather than the reference one:
    /// each pixel walked from the top down to its topmost replace-all op,
    /// then the transforms above it. Proven the same planes on the suite
    /// (R3) but no faster on this PC's integrated GPU, so the reference walk
    /// stays the default; the console's <c>art resolve reference|skip</c> switches.
    /// </summary>
    public static bool Skip;

    /// <summary>
    /// The frames to keep resident beside one just packed (RENDERER.md 5.4:
    /// an animated shape's frames together, so its animation never waits
    /// on a frame), by the frame's identity and the scale; null for none.
    /// </summary>
    public Func<ArtRef, int, IEnumerable<(ArtRef Source, ShapeFrame Frame)>>? Siblings;

    /// <summary>Why the last frame could not be prepared for the GPU (then the CPU painted it), or null.</summary>
    public string? Fallback { get; private set; }
    /// <summary>Whether <see cref="Fallback"/> passes by itself within a frame or two (the device starting, the textures being remade, a frame still submitting).</summary>
    public bool FallbackTransient { get; private set; }
    /// <summary>Milliseconds the last frame's binning and art-store filling took on the main thread.</summary>
    public double LastPrepareMs { get; private set; }
    /// <summary>Milliseconds the last frame's uploads and dispatch took on the render thread.</summary>
    public double LastSubmitMs { get; private set; }
    public int LastEntryCount { get; private set; }
    public int LastCmdCount { get; private set; }
    /// <summary>Whether the last frame's mix plane counts (<see cref="IndexBuffer8.MixUsed"/>): some painted frame had one.</summary>
    public bool LastMixUsed { get; private set; }
    /// <summary>Whether the last frame's colour plane counts (<see cref="IndexBuffer8.ColourUsed"/>): some painted frame had its own colours.</summary>
    public bool LastColourUsed { get; private set; }

    /// <summary>Megabytes of the art store in use, its size and pages, and how many frames it holds.</summary>
    public string AtlasUse
    {
        get
        {
            long used = 0;
            var filled = 0;
            foreach (var p in _pages)
            {
                used += p.Used;
                filled += p.Keys.Count > 0 ? 1 : 0;
            }

            return $"{used * 4 / (1 << 20)} of {_atlasBytes >> 20} MB in {filled} of {_pages.Length} pages, {_entries.Count} frames";
        }
    }

    public bool Failed => _failed;

    /// <summary>The readback of the last frame drawn with <see cref="RequestReadback"/>: RGBA8 per plane, or null until it is in.</summary>
    public byte[]? ReadIndex, ReadGlow, ReadMix, ReadPanes, ReadColour, ReadLift;
    public volatile bool ReadbackReady;
    bool _readbackWanted;

    public GpuCompositor(RenderingDevice rd, XformTables xforms)
    {
        _rd = rd;
        _xforms = xforms;
        _initCallable = Callable.From(Init);
        _submitCallable = Callable.From(Submit);
        _resizeCallable = Callable.From(Resize);
        RenderingServer.CallOnRenderThread(_initCallable);
    }

    /// <summary>Frees the device's resources on the render thread (the game's exit, or the backend given up).</summary>
    public void Dispose()
    {
        foreach (var t in new[] { IndexTexture, GlowTexture, MixTexture, PanesTexture, ColourTexture, LiftTexture })
        {
            if (t is not null)
            {
                t.TextureRdRid = default;
            }
        }

        RenderingServer.CallOnRenderThread(Callable.From(Free));
    }

    void Free()
    {
        _failed = true;
        _ready = false;
        DropSet();
        foreach (var rid in new[]
                 {
                     _indexTex, _glowTex, _mixTex, _panesTex, _colourTex, _liftTex, _cmdBuf, _tileBuf, _entryBuf, _maskBuf, _xformBuf, _dayBuf,
                     _slopeBuf, _lutBuf, _atlas, _pipeline, _shader
                 })
        {
            if (rid.IsValid)
            {
                _rd.FreeRid(rid);
            }
        }

        _indexTex = _glowTex = _mixTex = _panesTex = _colourTex = _liftTex = _cmdBuf = _tileBuf = _entryBuf = _maskBuf = _xformBuf = _dayBuf =
            _slopeBuf = _lutBuf = _atlas = _pipeline = _shader = default;
    }

    /// <summary>The planes' textures exist at the buffer's size (a size change costs one CPU-painted frame).</summary>
    public bool TexturesReady => _texState == 2 && _texW == _wantW && _texH == _wantH;

    /// <summary>The next frame's planes are read back for <c>art dump</c> (<see cref="ReadbackReady"/> once they are).</summary>
    public void RequestReadback()
    {
        ReadbackReady = false;
        _readbackWanted = true;
    }

    /// <summary>
    /// Prepares the stream for the GPU: the art store filled with the frames
    /// it lacks, the commands clipped, binned to tiles and laid out for the
    /// shader. False when the CPU must paint this frame (<see cref="Fallback"/>
    /// says why): the device isn't ready, the textures are being made at a
    /// new size, or the whole frame doesn't fit the art store.
    /// </summary>
    public bool Prepare(PaintStream stream, int width, int height, int scale, int artVersion)
    {
        if (_failed)
        {
            return Fail("the compute shader failed to build", false);
        }

        if (_initState == 0)
        {
            return Fail("the device is starting", true);
        }

        if (_wantW != width || _wantH != height)
        {
            _wantW = width;
            _wantH = height;
            if (_texState != 1)
            {
                _texState = 1;
                RenderingServer.CallOnRenderThread(_resizeCallable);
            }
        }

        if (!TexturesReady)
        {
            if (_texState == 2)
            {
                _texState = 1;
                RenderingServer.CallOnRenderThread(_resizeCallable);
            }

            return Fail("the planes' textures are being made", true);
        }

        if (IndexTexture is null || IndexTexture.TextureRdRid != _indexTex)
        {
            // Wrapped for the canvas on the main thread, as Godot's compute texture demo does.
            IndexTexture = new Texture2Drd { TextureRdRid = _indexTex };
            GlowTexture = new Texture2Drd { TextureRdRid = _glowTex };
            MixTexture = new Texture2Drd { TextureRdRid = _mixTex };
            PanesTexture = new Texture2Drd { TextureRdRid = _panesTex };
            ColourTexture = new Texture2Drd { TextureRdRid = _colourTex };
            LiftTexture = new Texture2Drd { TextureRdRid = _liftTex };
        }

        if (_submitting is not null)
        {
            return Fail("the last frame is still being submitted", true);
        }

        if (artVersion != _artVersion)
        {
            _artVersion = artVersion;
            Reset();
        }

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var frame = _frames[_parity];
        for (var attempt = 0; attempt < 2; attempt++)
        {
            _prepareSerial++;
            if (TryPrepare(stream, width, height, scale, frame))
            {
                LastPrepareMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                LastCmdCount = frame.CmdCount;
                LastEntryCount = frame.EntryCount;
                LastMixUsed = frame.MixFrom < frame.CmdCount;
                LastColourUsed = frame.ColourFrom < frame.CmdCount;
                Fallback = null;
                frame.Readback = _readbackWanted;
                _readbackWanted = false;
                _submitting = frame;
                _parity ^= 1;
                RenderingServer.CallOnRenderThread(_submitCallable);
                return true;
            }

            // The frame's art didn't fit round what this prepare had already put in: emptied, it is prepared again from nothing.
            Reset();
        }

        return Fail("the frame's art doesn't fit the art store", false);
    }

    bool Fail(string why, bool transient)
    {
        Fallback = why;
        FallbackTransient = transient;
        return false;
    }

    void Reset()
    {
        _entries.Clear();
        foreach (var page in _pages)
        {
            page.Used = 0;
            page.Keys.Clear();
            page.Serial = 0;
        }

        _page = 0;
    }

    bool TryPrepare(PaintStream stream, int width, int height, int scale, FrameData frame)
    {
        frame.Uploads.Clear();
        frame.CmdCount = 0;
        frame.Width = width;
        frame.Height = height;
        frame.TilesX = (width + TileSize - 1) / TileSize;
        frame.TilesY = (height + TileSize - 1) / TileSize;
        frame.MixFrom = int.MaxValue;
        frame.ColourFrom = int.MaxValue;
        frame.Scale = scale;
        var commands = stream.Commands;
        if (frame.Cmds.Length < commands.Length * CmdInts)
        {
            frame.Cmds = new int[Math.Max(commands.Length * CmdInts, frame.Cmds.Length * 2)];
        }

        if (_rects.Length < commands.Length)
        {
            _rects = new (int, int, int, int)[Math.Max(commands.Length, _rects.Length * 2)];
        }

        var cmds = frame.Cmds;
        var n = 0;
        for (var i = 0; i < commands.Length; i++)
        {
            ref readonly var c = ref commands[i];
            Op op;
            Entry e = default;
            int x0, y0, x1, y1;
            if (c.Op == PaintOp.Fill8)
            {
                op = Op.Fill;
                (x0, y0, x1, y1) = (c.X, c.Y, c.X + c.W, c.Y + c.H);
            }
            else
            {
                op = c.Op switch
                {
                    PaintOp.Copy8 or PaintOp.Rle => Op.Rle,
                    PaintOp.RleTranslucent => Op.Translucent,
                    PaintOp.RleThroughPanes => Op.ThroughPanes,
                    PaintOp.RleTransformed => Op.Transformed,
                    _ => Op.Solid
                };
                // A solid op reads no pixel of its frame (an outline band's are unused), only its coverage.
                var found = c.Op == PaintOp.RleOutline
                    ? OutlineEntry(c.Art!, c.Source, c.Scale, frame, out e)
                    : FrameEntry(c.Art!, c.Source, scale, op == Op.Solid, frame, out e);
                if (!found)
                {
                    return false;
                }

                if (e.IsEmpty)
                {
                    continue;
                }

                (x0, y0) = (c.X + e.Dx, c.Y + e.Dy);
                (x1, y1) = (x0 + e.W, y0 + e.H);
            }

            var cx0 = Math.Max(x0, 0);
            var cy0 = Math.Max(y0, 0);
            var cx1 = Math.Min(x1, width);
            var cy1 = Math.Min(y1, height);
            if (cx0 >= cx1 || cy0 >= cy1)
            {
                continue;
            }

            // IndexBuffer8's mix and colour planes count from the first frame with one that paints a scan into
            // the buffer (CopyMix, CopyColour): a frame whose rectangle enters but none of whose scans do doesn't count.
            if ((e.HasMix && frame.MixFrom == int.MaxValue || e.HasColour && frame.ColourFrom == int.MaxValue) && Enters(in c, width, height))
            {
                if (e.HasMix)
                {
                    frame.MixFrom = Math.Min(frame.MixFrom, n);
                }

                if (e.HasColour)
                {
                    frame.ColourFrom = Math.Min(frame.ColourFrom, n);
                }
            }

            var at = n * CmdInts;
            cmds[at] = (int)op;
            cmds[at + 1] = cx0;
            cmds[at + 2] = cy0;
            cmds[at + 3] = cx1;
            cmds[at + 4] = cy1;
            cmds[at + 5] = x0;
            cmds[at + 6] = y0;
            cmds[at + 7] = e.W;
            cmds[at + 8] = e.Offset;
            cmds[at + 9] = c.Glow | (c.Haze << 8) | (c.Window << 16) | (c.Colour << 24);
            cmds[at + 10] = c.Table;
            cmds[at + 11] = c.Mask;
            cmds[at + 12] = (c.Translucent ? 1 : 0) | (e.HasMix ? 2 : 0) | (e.HasColour ? 4 : 0) | (c.Lift << 8);
            cmds[at + 13] = c.Y;
            cmds[at + 14] = c.DimZ;
            cmds[at + 15] = c.X;
            _rects[n] = (cx0, cy0, cx1, cy1);
            n++;
        }

        frame.CmdCount = n;
        Bin(frame);

        frame.MaskCount = Math.Min(stream.MaskCount, MaxMasks);
        Array.Clear(frame.Masks, 0, frame.MaskCount * 8);
        for (var m = 0; m < frame.MaskCount; m++)
        {
            var mask = stream.Mask(m);
            for (var slot = 0; slot < 256; slot++)
            {
                if (mask[slot])
                {
                    frame.Masks[m * 8 + (slot >> 5)] |= 1u << (slot & 31);
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Whether <see cref="IndexBuffer8"/> paints any scan of the command's
    /// frame (its <c>ClipScan</c>), which is what makes its mix and colour
    /// planes count. A frame behind panes paints only on marked panes, which
    /// the stream can't know; it counts as entering (RENDERER.md, R3).
    /// </summary>
    static bool Enters(in PaintCommand c, int width, int height)
    {
        var art = c.Art!;
        if (!art.IsRle)
        {
            return true;
        }

        foreach (var s in art.Scans)
        {
            var py = c.Y + s.Y;
            var px = c.X + s.X;
            if ((uint)py < (uint)height && px < width && px + s.Length > 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Counting sort of the commands into the tiles their clipped rectangles touch, in command order within a tile.</summary>
    void Bin(FrameData frame)
    {
        var tiles = frame.TilesX * frame.TilesY;
        if (_tileCounts.Length < tiles + 1)
        {
            _tileCounts = new int[tiles + 1];
            _tileFill = new int[tiles + 1];
        }

        if (frame.TileOffsets.Length < tiles + 1)
        {
            frame.TileOffsets = new int[tiles + 1];
        }

        Array.Clear(_tileCounts, 0, tiles + 1);
        var total = 0;
        for (var i = 0; i < frame.CmdCount; i++)
        {
            var (x0, y0, x1, y1) = _rects[i];
            var tx0 = x0 / TileSize;
            var tx1 = (x1 - 1) / TileSize;
            var ty0 = y0 / TileSize;
            var ty1 = (y1 - 1) / TileSize;
            for (var ty = ty0; ty <= ty1; ty++)
            {
                var row = ty * frame.TilesX;
                for (var tx = tx0; tx <= tx1; tx++)
                {
                    _tileCounts[row + tx]++;
                }
            }

            total += (tx1 - tx0 + 1) * (ty1 - ty0 + 1);
        }

        if (frame.Entries.Length < total)
        {
            frame.Entries = new int[Math.Max(total, frame.Entries.Length * 2)];
        }

        var sum = 0;
        for (var t = 0; t < tiles; t++)
        {
            frame.TileOffsets[t] = sum;
            _tileFill[t] = sum;
            sum += _tileCounts[t];
        }

        frame.TileOffsets[tiles] = sum;
        frame.EntryCount = sum;
        for (var i = 0; i < frame.CmdCount; i++)
        {
            var (x0, y0, x1, y1) = _rects[i];
            var tx0 = x0 / TileSize;
            var tx1 = (x1 - 1) / TileSize;
            var ty0 = y0 / TileSize;
            var ty1 = (y1 - 1) / TileSize;
            for (var ty = ty0; ty <= ty1; ty++)
            {
                var row = ty * frame.TilesX;
                for (var tx = tx0; tx <= tx1; tx++)
                {
                    frame.Entries[_tileFill[row + tx]++] = i;
                }
            }
        }
    }

    /// <summary>The frame's entry in the art store, made (and queued for upload) if new; false when the store is full.</summary>
    bool FrameEntry(ShapeFrame art, ArtRef source, int scale, bool coverageOnly, FrameData frame, out Entry entry)
    {
        var key = (source, scale, coverageOnly ? Kind.Solid : Kind.Pixels);
        if (_entries.TryGetValue(key, out entry))
        {
            return true;
        }

        if (!MakeEntry(art, coverageOnly, frame, key, out entry))
        {
            return false;
        }

        // An animated shape's other frames beside it, so its animation never waits on one (best effort).
        if (!coverageOnly && !_packingSiblings && Siblings is not null && source.File is ArtFile.Shapes or ArtFile.Sprites)
        {
            _packingSiblings = true;
            try
            {
                foreach (var (sibSource, sibFrame) in Siblings(source, scale))
                {
                    var sibKey = (sibSource, scale, Kind.Pixels);
                    if (!_entries.ContainsKey(sibKey) && !MakeEntry(sibFrame, false, frame, sibKey, out _))
                    {
                        break;
                    }
                }
            }
            finally
            {
                _packingSiblings = false;
            }
        }

        return true;
    }

    bool MakeEntry(ShapeFrame art, bool coverageOnly, FrameData frame, (ArtRef Source, int Scale, Kind Kind) key, out Entry entry)
    {
        var hasMix = !coverageOnly && art.Mix is not null;
        var hasColour = !coverageOnly && art.Colour is not null;
        var stride = hasColour ? 2 : 1;
        if (!art.IsRle)
        {
            var (w, h) = (art.Width, art.Height);
            var data = new uint[w * h * stride];
            Pack(art, 0, w * h, data, 0, coverageOnly, stride);
            if (!Alloc(frame, data, key, out var offset))
            {
                entry = default;
                return false;
            }

            entry = new Entry(offset, w, h, -art.XLeft, -art.YAbove, hasMix, hasColour);
            _entries[key] = entry;
            return true;
        }

        if (art.Scans.Length == 0)
        {
            entry = new Entry(0, 0, 0, 0, 0, false, false);
            _entries[key] = entry;
            return true;
        }

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var s in art.Scans)
        {
            minX = Math.Min(minX, s.X);
            maxX = Math.Max(maxX, s.X + s.Length - 1);
            minY = Math.Min(minY, s.Y);
            maxY = Math.Max(maxY, s.Y);
        }

        var width = maxX - minX + 1;
        var height = maxY - minY + 1;
        var dense = new uint[width * height * stride];
        foreach (var s in art.Scans)
        {
            Pack(art, s.Offset, s.Length, dense, ((s.Y - minY) * width + s.X - minX) * stride, coverageOnly, stride);
        }

        if (!Alloc(frame, dense, key, out var off))
        {
            entry = default;
            return false;
        }

        entry = new Entry(off, width, height, minX, minY, hasMix, hasColour);
        _entries[key] = entry;
        return true;
    }

    /// <summary>
    /// <see cref="IndexBuffer8.PaintRleOutline"/>'s blocks as a frame of coverage: a
    /// <paramref name="scale"/>-square at both ends of every scan, whole scans
    /// on the first row and the row the frame's height below it.
    /// </summary>
    bool OutlineEntry(ShapeFrame original, ArtRef source, int scale, FrameData frame, out Entry entry)
    {
        var key = (source, scale, Kind.Outline);
        if (_entries.TryGetValue(key, out entry))
        {
            return true;
        }

        if (!original.IsRle || original.Scans.Length == 0)
        {
            entry = new Entry(0, 0, 0, 0, 0, false, false);
            _entries[key] = entry;
            return true;
        }

        var firsty = original.Scans[0].Y;
        var lasty = firsty + original.Height - 1;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var s in original.Scans)
        {
            minX = Math.Min(minX, s.X * scale);
            maxX = Math.Max(maxX, (s.X + s.Length) * scale - 1);
            minY = Math.Min(minY, s.Y * scale);
            maxY = Math.Max(maxY, s.Y * scale + scale - 1);
        }

        var width = maxX - minX + 1;
        var height = maxY - minY + 1;
        var dense = new uint[width * height];
        foreach (var s in original.Scans)
        {
            var sy = s.Y * scale - minY;
            var sx = s.X * scale - minX;
            var whole = s.Y == firsty || s.Y == lasty;
            for (var row = 0; row < scale; row++)
            {
                var at = (sy + row) * width + sx;
                if (whole)
                {
                    dense.AsSpan(at, s.Length * scale).Fill(0xFF00);
                }
                else
                {
                    dense.AsSpan(at, scale).Fill(0xFF00);
                    dense.AsSpan(at + (s.Length - 1) * scale, scale).Fill(0xFF00);
                }
            }
        }

        if (!Alloc(frame, dense, key, out var off))
        {
            return false;
        }

        entry = new Entry(off, width, height, minX, minY, false, false);
        _entries[key] = entry;
        return true;
    }

    /// <summary>
    /// <paramref name="len"/> texels of a frame from pixel <paramref name="src"/>
    /// into <paramref name="dense"/> at <paramref name="dest"/>: the index,
    /// its alpha as the coverage (255 without an alpha plane), the mix
    /// bytes; with a colour plane, the RGBA beside each (A 255 where the
    /// pixel has its own colour).
    /// </summary>
    static void Pack(ShapeFrame art, int src, int len, uint[] dense, int dest, bool coverageOnly, int stride)
    {
        if (coverageOnly)
        {
            dense.AsSpan(dest, len).Fill(0xFF00);
            return;
        }

        var pixels = art.Pixels;
        var mix = art.Mix;
        var alpha = art.Alpha;
        var colour = art.Colour;
        for (var i = 0; i < len; i++)
        {
            var t = pixels[src + i] | (uint)(alpha is null ? 255 : alpha[src + i]) << 8;
            if (mix is not null)
            {
                t |= (uint)mix[(src + i) * 2] << 16 | (uint)mix[(src + i) * 2 + 1] << 24;
            }

            dense[dest + i * stride] = t;
            if (stride == 2)
            {
                var c = (src + i) * 4;
                dense[dest + i * stride + 1] = colour![c] | (uint)colour[c + 1] << 8 | (uint)colour[c + 2] << 16 | (uint)colour[c + 3] << 24;
            }
        }
    }

    /// <summary>
    /// Room in the store for a frame: in the current page, else in the next
    /// one, freed first (its frames forgotten and packed again when next
    /// painted). False when the next page already holds frames of this
    /// prepare (the frame's art fills the store) or the frame is larger than a page.
    /// </summary>
    bool Alloc(FrameData frame, uint[] data, (ArtRef Source, int Scale, Kind Kind) key, out int offset)
    {
        offset = 0;
        if (_pages.Length == 0)
        {
            return false;
        }

        var page = _pages[_page];
        if (page.Used + data.Length > page.Size)
        {
            var next = (_page + 1) % _pages.Length;
            if (data.Length > _pages[next].Size || _pages[next].Serial == _prepareSerial && _pages[next].Keys.Count > 0)
            {
                return false;
            }

            FreePage(_pages[next]);
            _page = next;
            page = _pages[next];
        }

        offset = page.Start + page.Used;
        page.Used += data.Length;
        page.Serial = _prepareSerial;
        page.Keys.Add(key);
        frame.Uploads.Add(((uint)offset * 4, data));
        return true;
    }

    void FreePage(Page page)
    {
        foreach (var key in page.Keys)
        {
            _entries.Remove(key);
        }

        page.Keys.Clear();
        page.Used = 0;
        page.Serial = 0;
    }

    // ---- The render thread ----

    void Init()
    {
        try
        {
            var source = new RDShaderSource { SourceCompute = ShaderCode };
            var spirv = _rd.ShaderCompileSpirVFromSource(source);
            if (!string.IsNullOrEmpty(spirv.CompileErrorCompute))
            {
                GD.PushError("GpuCompositor: " + spirv.CompileErrorCompute);
                _failed = true;
                return;
            }

            _shader = _rd.ShaderCreateFromSpirV(spirv);
            _pipeline = _rd.ComputePipelineCreate(_shader);
            // The budget (RENDERER.md 5.4), halved until the device gives it.
            _atlasBytes = (uint)Math.Clamp(ArtLook.StoreMb, 16, 2048) << 20;
            while (_atlasBytes >= (16u << 20))
            {
                _atlas = _rd.StorageBufferCreate(_atlasBytes, []);
                if (_atlas.IsValid)
                {
                    break;
                }

                _atlasBytes >>= 1;
            }

            if (!_atlas.IsValid)
            {
                GD.PushError("GpuCompositor: no storage buffer for the art store");
                _failed = true;
                return;
            }

            var pageUints = (int)(_atlasBytes / 4 / PageCount);
            _pages = new Page[PageCount];
            for (var i = 0; i < PageCount; i++)
            {
                _pages[i] = new Page(i * pageUints, pageUints);
            }

            // The tables as bytes, four to a uint; the day palette likewise; the tables' colour slopes as floats.
            var tables = _xforms.All.ToArray();
            _xformBuf = _rd.StorageBufferCreate((uint)tables.Length, tables);
            var day = _xforms.Day.ToArray();
            _dayBuf = _rd.StorageBufferCreate((uint)day.Length, day);
            var slopes = MemoryMarshal.AsBytes(_xforms.Slopes).ToArray();
            _slopeBuf = _rd.StorageBufferCreate((uint)slopes.Length, slopes);
            // Modern colour's tables for the translucency tables (ColourLut), at the user's size.
            _lutSize = ArtLook.LutSize;
            var luts = MemoryMarshal.AsBytes(ColourLut.XformLuts(_xforms, _lutSize).AsSpan()).ToArray();
            _lutBuf = _rd.StorageBufferCreate((uint)luts.Length, luts);
            _maskBuf = _rd.StorageBufferCreate(MaxMasks * 8 * 4, []);
            _ready = true;
        }
        catch (Exception e)
        {
            GD.PushError("GpuCompositor: " + e.Message);
            _failed = true;
        }
        finally
        {
            _initState = 1;
        }
    }

    /// <summary>The planes' textures at the wanted size (the old ones freed); wrapped for the canvas on the main thread.</summary>
    void Resize()
    {
        var (w, h) = (_wantW, _wantH);
        DropSet();
        foreach (var rid in new[] { _indexTex, _glowTex, _mixTex, _panesTex, _colourTex, _liftTex })
        {
            if (rid.IsValid)
            {
                _rd.FreeRid(rid);
            }
        }

        _indexTex = MakeTexture(w, h);
        _glowTex = MakeTexture(w, h);
        _mixTex = MakeTexture(w, h);
        _panesTex = MakeTexture(w, h);
        _colourTex = MakeTexture(w, h);
        _liftTex = MakeTexture(w, h);
        _texW = w;
        _texH = h;
        _setDirty = true;
        _texState = 2;
    }

    Rid MakeTexture(int w, int h)
    {
        var format = new RDTextureFormat
        {
            Width = (uint)w,
            Height = (uint)h,
            Depth = 1,
            ArrayLayers = 1,
            Mipmaps = 1,
            Format = RenderingDevice.DataFormat.R8G8B8A8Unorm,
            TextureType = RenderingDevice.TextureType.Type2D,
            Samples = RenderingDevice.TextureSamples.Samples1,
            UsageBits = RenderingDevice.TextureUsageBits.StorageBit | RenderingDevice.TextureUsageBits.SamplingBit |
                        RenderingDevice.TextureUsageBits.CanCopyFromBit
        };
        return _rd.TextureCreate(format, new RDTextureView(), new Godot.Collections.Array<byte[]>());
    }

    void Submit()
    {
        var frame = _submitting;
        if (frame is null || !_ready)
        {
            _submitting = null;
            return;
        }

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            foreach (var (offset, data) in frame.Uploads)
            {
                _rd.BufferUpdate(_atlas, offset, (uint)data.Length * 4, MemoryMarshal.AsBytes(data.AsSpan()));
            }

            frame.Uploads.Clear();
            var cmdBytes = (uint)Math.Max(frame.CmdCount * CmdInts * 4, 16);
            var tileBytes = (uint)(frame.TilesX * frame.TilesY + 1) * 4;
            var entryBytes = (uint)Math.Max(frame.EntryCount * 4, 16);
            Ensure(ref _cmdBuf, ref _cmdCap, cmdBytes);
            Ensure(ref _tileBuf, ref _tileCap, tileBytes);
            Ensure(ref _entryBuf, ref _entryCap, entryBytes);
            if (frame.CmdCount > 0)
            {
                _rd.BufferUpdate(_cmdBuf, 0, (uint)frame.CmdCount * CmdInts * 4, MemoryMarshal.AsBytes(frame.Cmds.AsSpan(0, frame.CmdCount * CmdInts)));
            }

            _rd.BufferUpdate(_tileBuf, 0, tileBytes, MemoryMarshal.AsBytes(frame.TileOffsets.AsSpan(0, frame.TilesX * frame.TilesY + 1)));
            if (frame.EntryCount > 0)
            {
                _rd.BufferUpdate(_entryBuf, 0, (uint)frame.EntryCount * 4, MemoryMarshal.AsBytes(frame.Entries.AsSpan(0, frame.EntryCount)));
            }

            if (frame.MaskCount > 0)
            {
                _rd.BufferUpdate(_maskBuf, 0, (uint)frame.MaskCount * 32, MemoryMarshal.AsBytes(frame.Masks.AsSpan(0, frame.MaskCount * 8)));
            }

            if (_setDirty)
            {
                DropSet();
                var uniforms = new Godot.Collections.Array<RDUniform>
                {
                    Storage(0, _cmdBuf), Storage(1, _tileBuf), Storage(2, _entryBuf), Storage(3, _atlas), Storage(4, _xformBuf),
                    Storage(5, _maskBuf), Image(6, _indexTex), Image(7, _glowTex), Image(8, _mixTex), Image(9, _panesTex), Image(10, _colourTex),
                    Storage(11, _dayBuf), Storage(12, _slopeBuf), Storage(13, _lutBuf), Image(14, _liftTex)
                };
                _set = _rd.UniformSetCreate(uniforms, _shader, 0);
                _setDirty = false;
            }

            Span<int> push =
            [
                frame.Width, frame.Height, frame.TilesX, TileSize, frame.MixFrom, Skip ? 1 : 0, frame.ColourFrom, ArtLook.Modern ? _lutSize : 0,
                4 * frame.Scale, 0, 0, 0
            ];
            var list = _rd.ComputeListBegin();
            _rd.ComputeListBindComputePipeline(list, _pipeline);
            _rd.ComputeListBindUniformSet(list, _set, 0);
            _rd.ComputeListSetPushConstant(list, MemoryMarshal.AsBytes(push), 48);
            _rd.ComputeListDispatch(list, (uint)frame.TilesX, (uint)frame.TilesY, 1);
            _rd.ComputeListEnd();
            if (frame.Readback)
            {
                ReadIndex = _rd.TextureGetData(_indexTex, 0);
                ReadGlow = _rd.TextureGetData(_glowTex, 0);
                ReadMix = _rd.TextureGetData(_mixTex, 0);
                ReadPanes = _rd.TextureGetData(_panesTex, 0);
                ReadColour = _rd.TextureGetData(_colourTex, 0);
                ReadLift = _rd.TextureGetData(_liftTex, 0);
                ReadbackReady = true;
            }
        }
        catch (Exception e)
        {
            GD.PushError("GpuCompositor: " + e.Message);
            _failed = true;
        }
        finally
        {
            LastSubmitMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            _submitting = null;
        }
    }

    /// <summary>A storage buffer of at least <paramref name="bytes"/>, made anew (and the uniform set with it) when too small.</summary>
    void Ensure(ref Rid buffer, ref uint capacity, uint bytes)
    {
        if (buffer.IsValid && capacity >= bytes)
        {
            return;
        }

        if (buffer.IsValid)
        {
            DropSet();
            _rd.FreeRid(buffer);
        }

        capacity = Math.Max(bytes, capacity * 2);
        buffer = _rd.StorageBufferCreate(capacity, []);
        _setDirty = true;
    }

    /// <summary>The uniform set freed before any buffer or texture in it is (the device frees it with them, and twice is an error).</summary>
    void DropSet()
    {
        if (_set.IsValid)
        {
            _rd.FreeRid(_set);
            _set = default;
        }

        _setDirty = true;
    }

    static RDUniform Storage(int binding, Rid rid)
    {
        var u = new RDUniform { UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = binding };
        u.AddId(rid);
        return u;
    }

    static RDUniform Image(int binding, Rid rid)
    {
        var u = new RDUniform { UniformType = RenderingDevice.UniformType.Image, Binding = binding };
        u.AddId(rid);
        return u;
    }

    /// <summary>
    /// The two resolves of RENDERER.md 5.2, one thread a buffer pixel. The
    /// reference: the tile's commands from the bottom up, each op on the
    /// thread's planes as <see cref="IndexBuffer8"/>'s method does. The
    /// skipping one: from the top down to the first op that replaces every
    /// plane at the pixel (a fill, an opaque or solid texel, a translucent
    /// frame's plain texel; with the colour plane counting, only a texel
    /// whose colour doesn't blend over what is under), noting the
    /// transforms passed (see-through texels, panes, invisible actors, soft
    /// texels), then those from the bottom up; the same planes, since a
    /// replace-all op's state is its own (5.1). More than 24 transforms
    /// stacked on one pixel fall back to the reference walk. A texel's
    /// alpha is its coverage: from 128 it replaces the index planes
    /// (<see cref="ShapeFrame.Covered"/>), and with the colour plane
    /// counting any alpha blends into the colour as
    /// <see cref="IndexBuffer8"/>'s <c>BlendColour</c> does; a transform
    /// turns a pixel's own colour as <see cref="XformTables.TurnColour"/>
    /// does, in the same float order (<c>precise</c>).
    /// </summary>
    const string ShaderCode = """
        #version 450
        layout(local_size_x = 16, local_size_y = 16, local_size_z = 1) in;

        // A command: 4 ivec4: (op, x0, y0, x1) (y1, fx, fy, fw) (offset, glow|haze<<8|window<<16|colour<<24, table, mask)
        // (flags | lift << 8, the hotspot's y, the box's height in lifts, the hotspot's x).
        layout(set = 0, binding = 0, std430) restrict readonly buffer Cmds { ivec4 c[]; } cmds;
        layout(set = 0, binding = 1, std430) restrict readonly buffer TileOffsets { int o[]; } tiles;
        layout(set = 0, binding = 2, std430) restrict readonly buffer TileEntries { int e[]; } entries;
        // The art store: a uint a texel, index | alpha << 8 | mix index << 16 | mix fraction << 24; a coloured frame's
        // texels take two, the second r | g << 8 | b << 16 | own << 24 (own 255 where the pixel has its own colour).
        layout(set = 0, binding = 3, std430) restrict readonly buffer Atlas { uint t[]; } atlas;
        // XFORM.TBL's 17 tables of 256 bytes, four to a uint.
        layout(set = 0, binding = 4, std430) restrict readonly buffer Xforms { uint x[]; } xforms;
        // The pane masks: 256 bits each, the windows a hidden effect shows through.
        layout(set = 0, binding = 5, std430) restrict readonly buffer Masks { uint m[]; } masks;
        layout(set = 0, binding = 6, rgba8) uniform restrict writeonly image2D out_index;
        layout(set = 0, binding = 7, rgba8) uniform restrict writeonly image2D out_glow;
        layout(set = 0, binding = 8, rgba8) uniform restrict writeonly image2D out_mix;
        layout(set = 0, binding = 9, rgba8) uniform restrict writeonly image2D out_panes;
        layout(set = 0, binding = 10, rgba8) uniform restrict writeonly image2D out_colour;
        // The day palette, 768 bytes four to a uint, and each table's 3x3 colour slopes (XformTables).
        layout(set = 0, binding = 11, std430) restrict readonly buffer Day { uint d[]; } day_pal;
        layout(set = 0, binding = 12, std430) restrict readonly buffer Slopes { float m[]; } slopes;
        // Modern colour: each table as a lookup table over RGB (ColourLut.XformLuts), lut_size cubed RGBA8 cells, back to back.
        layout(set = 0, binding = 13, std430) restrict readonly buffer Luts { uint l[]; } luts;
        // The lift plane (IndexBuffer8.Lift): the lift of what each pixel shows.
        layout(set = 0, binding = 14, rgba8) uniform restrict writeonly image2D out_lift;
        layout(push_constant, std430) uniform Params {
            int width;
            int height;
            int tiles_x;
            int tile_size;
            // The first command from which the buffer's mix plane counts (IndexBuffer8.MixUsed).
            int mix_from;
            // 1 for the skipping resolve, 0 for the reference.
            int skip;
            // The first command from which the buffer's colour plane counts (IndexBuffer8.ColourUsed).
            int colour_from;
            // Modern colour (ArtLook.Modern): the tables' lookup tables' size, 0 for classic's anchor rule.
            int lut_size;
            // Buffer pixels a lift (4 at scale 1), for the box proxy's rise above a piece's hotspot.
            int lift_px;
            int pad9;
            int pad10;
            int pad11;
        } p;

        const int OP_FILL = 0;
        const int OP_RLE = 1;
        const int OP_SOLID = 2;
        const int OP_TRANSLUCENT = 3;
        const int OP_THROUGH_PANES = 4;
        const int OP_TRANSFORMED = 5;
        const uint GLOW_BITS = 0xFCu;
        const uint PANE_MARK = 2u;
        const uint FIRST_TRANSLUCENT = 0xEEu;
        const uint COVERED = 128u;
        const uint FIRST_CYCLING = 0xE0u;
        const int STACK = 24;

        // The planes at one pixel: IndexBuffer8's per-pixel state.
        struct State {
            uint idx;
            uint glow;
            uint panes;
            uvec2 mx;
            uvec3 col;
            bool own;
            uint lift;
        };

        uint xf(int table, uint i) {
            uint k = uint(table) * 256u + i;
            return (xforms.x[k >> 2] >> ((k & 3u) * 8u)) & 0xFFu;
        }

        uint day(uint index, int c) {
            uint k = index * 3u + uint(c);
            return (day_pal.d[k >> 2] >> ((k & 3u) * 8u)) & 0xFFu;
        }

        bool see_through(uint ci) {
            return ci >= FIRST_TRANSLUCENT && ci <= 0xFEu;
        }

        // Where command i's texel for px lies in the store, or -1 outside its clipped rectangle (-2 for a fill).
        int texel_at(int i, ivec2 px) {
            ivec4 a = cmds.c[i * 4];
            ivec4 b = cmds.c[i * 4 + 1];
            if (px.x < a.y || px.x >= a.w || px.y < a.z || px.y >= b.x) {
                return -1;
            }
            if (a.x == OP_FILL) {
                return -2;
            }
            int stride = (cmds.c[i * 4 + 3].x & 4) != 0 ? 2 : 1;
            return cmds.c[i * 4 + 2].x + ((px.y - b.z) * b.w + (px.x - b.y)) * stride;
        }

        // The command's texel: index, alpha and mix bytes; 0 (no coverage) outside its clipped rectangle.
        uint fetch(int i, ivec2 px) {
            int at = texel_at(i, px);
            return at == -1 ? 0u : at == -2 ? 0xFF00u : atlas.t[at];
        }

        // The texel's colour uint, 0 for a frame without colours.
        uint fetch_colour(int i, ivec2 px) {
            if ((cmds.c[i * 4 + 3].x & 4) == 0) {
                return 0u;
            }
            int at = texel_at(i, px);
            return at < 0 ? 0u : atlas.t[at + 1];
        }

        // A cell of table t's lookup table.
        vec3 lut_cell(int t, ivec3 c) {
            int n = p.lut_size;
            uint v = luts.l[(t * n * n + c.z * n + c.y) * n + c.x];
            return vec3(float(v & 0xFFu), float((v >> 8) & 0xFFu), float((v >> 16) & 0xFFu));
        }

        // The colour seen through table t by its lookup table, sampled trilinearly (modern colour).
        uvec3 lut_turn(int t, uvec3 col) {
            float n1 = float(p.lut_size - 1);
            vec3 u = vec3(col) / 255.0 * n1;
            ivec3 i0 = ivec3(floor(u));
            ivec3 i1 = min(i0 + 1, ivec3(p.lut_size - 1));
            vec3 f = u - vec3(i0);
            vec3 c00 = mix(lut_cell(t, ivec3(i0.x, i0.y, i0.z)), lut_cell(t, ivec3(i1.x, i0.y, i0.z)), f.x);
            vec3 c10 = mix(lut_cell(t, ivec3(i0.x, i1.y, i0.z)), lut_cell(t, ivec3(i1.x, i1.y, i0.z)), f.x);
            vec3 c01 = mix(lut_cell(t, ivec3(i0.x, i0.y, i1.z)), lut_cell(t, ivec3(i1.x, i0.y, i1.z)), f.x);
            vec3 c11 = mix(lut_cell(t, ivec3(i0.x, i1.y, i1.z)), lut_cell(t, ivec3(i1.x, i1.y, i1.z)), f.x);
            vec3 v = mix(mix(c00, c10, f.y), mix(c01, c11, f.y), f.z);
            return uvec3(clamp(v + 0.5, 0.0, 255.0));
        }

        // XformTables.TurnColour: a pixel's own colour seen through a table, its anchor turned from one index to
        // another; in modern mode by the table's lookup table, the colour alone deciding.
        void turn_colour(int table, uint from, uint to, inout State s) {
            if (!s.own) {
                return;
            }
            if (p.lut_size > 0) {
                s.col = lut_turn(table, s.col);
                return;
            }
            if (from >= FIRST_CYCLING || to >= FIRST_CYCLING) {
                s.own = false;
                s.col = uvec3(0u);
                return;
            }
            float dr = float(int(s.col.r) - int(day(from, 0)));
            float dg = float(int(s.col.g) - int(day(from, 1)));
            float db = float(int(s.col.b) - int(day(from, 2)));
            for (int c = 0; c < 3; c++) {
                int m = table * 9 + c * 3;
                precise float v = ((float(day(to, c)) + slopes.m[m] * dr) + slopes.m[m + 1] * dg) + slopes.m[m + 2] * db;
                s.col[c] = uint(clamp(v + 0.5, 0.0, 255.0));
            }
        }

        // IndexBuffer8.BlendColour, then the replace of a covering texel: a texel t (index, alpha, mix) with colour uint cu.
        void paint_texel(uint t, uint cu, bool has_mix, bool colour_used, uint cglow, uint lift, inout State s) {
            uint a = (t >> 8) & 0xFFu;
            uint ci = t & 0xFFu;
            if (colour_used) {
                uint ca = (cu >> 24) & 0xFFu;
                if (ca != 0u) {
                    uvec3 c = uvec3(cu & 0xFFu, (cu >> 8) & 0xFFu, (cu >> 16) & 0xFFu);
                    uvec3 under = s.own ? s.col : uvec3(day(s.idx, 0), day(s.idx, 1), day(s.idx, 2));
                    s.col = (c * a + under * (255u - a) + 127u) / 255u;
                    s.own = true;
                } else if (a >= COVERED) {
                    s.own = false;
                    s.col = uvec3(0u);
                }
            }
            if (a >= COVERED) {
                s.idx = ci;
                s.glow = cglow;
                s.mx = has_mix ? uvec2((t >> 16) & 0xFFu, (t >> 24) & 0xFFu) : uvec2(0u);
                s.lift = lift;
            }
        }

        // Command i's op on the state, as IndexBuffer8's method does at this pixel (t its texel from fetch, alpha above 0).
        void apply(int i, uint t, ivec2 px, inout State s) {
            ivec4 c = cmds.c[i * 4 + 2];
            int op = cmds.c[i * 4].x;
            uint packed = uint(c.y);
            uint cglow = packed & 0xFFu;
            uint haze = (packed >> 8) & 0xFFu;
            uint window = (packed >> 16) & 0xFFu;
            uint colour = (packed >> 24) & 0xFFu;
            int flags = cmds.c[i * 4 + 3].x;
            bool has_mix = (flags & 2) != 0;
            bool mix_used = i >= p.mix_from;
            bool colour_used = i >= p.colour_from;
            // The lift plane's value: the base lift, plus a building piece's height at the pixel, the smaller of what its
            // row and its column say from the hotspot (IndexBuffer8.LiftAt: exact on the south face, the east face and the top).
            int hy = cmds.c[i * 4 + 3].y;
            int dimz = cmds.c[i * 4 + 3].z;
            int hx = cmds.c[i * 4 + 3].w;
            uint lift = (uint(flags >> 8) & 0xFFu) + (dimz > 0 ? uint(clamp(min(hy - px.y, hx - px.x) / p.lift_px, 0, dimz)) : 0u);
            uint a = (t >> 8) & 0xFFu;
            uint ci = t & 0xFFu;
            if (op == OP_FILL || op == OP_SOLID) {
                s.idx = colour;
                s.glow = 0u;
                s.mx = uvec2(0u);
                s.own = false;
                s.col = uvec3(0u);
                s.lift = lift;
            } else if (op == OP_RLE) {
                paint_texel(t, fetch_colour(i, px), has_mix, colour_used, cglow, lift, s);
            } else if (op == OP_TRANSLUCENT) {
                if (see_through(ci)) {
                    if (a < COVERED) {
                        return;
                    }
                    s.lift = lift;
                    int table = int(ci - FIRST_TRANSLUCENT);
                    uint under = s.idx;
                    s.idx = xf(table, under);
                    turn_colour(table, under, s.idx, s);
                    if (mix_used) {
                        s.mx.x = xf(table, s.mx.x);
                    }
                    s.glow = haze;
                    if ((haze & PANE_MARK) != 0u) {
                        s.panes = (uint(table) << 8) | window;
                    }
                } else {
                    paint_texel(t, fetch_colour(i, px), has_mix, colour_used, cglow, lift, s);
                }
            } else if (op == OP_THROUGH_PANES) {
                if ((s.glow & PANE_MARK) == 0u || a < COVERED) {
                    return;
                }
                uint slot = s.panes & 0xFFu;
                if (((masks.m[c.w * 8 + int(slot >> 5u)] >> (slot & 31u)) & 1u) == 0u) {
                    return;
                }
                if ((flags & 1) != 0 && see_through(ci)) {
                    int table = int(ci - FIRST_TRANSLUCENT);
                    uint under = s.idx;
                    s.idx = xf(table, under);
                    turn_colour(table, under, s.idx, s);
                    if (mix_used) {
                        s.mx.x = xf(table, s.mx.x);
                    }
                } else {
                    int table = int(s.panes >> 8u);
                    s.idx = xf(table, ci);
                    if (colour_used) {
                        uint cu = fetch_colour(i, px);
                        s.own = ((cu >> 24) & 0xFFu) != 0u;
                        s.col = s.own ? uvec3(cu & 0xFFu, (cu >> 8) & 0xFFu, (cu >> 16) & 0xFFu) : uvec3(0u);
                    }
                    turn_colour(table, ci, s.idx, s);
                    s.mx = has_mix ? uvec2((t >> 16) & 0xFFu, (t >> 24) & 0xFFu) : uvec2(0u);
                    if (has_mix) {
                        s.mx.x = xf(table, s.mx.x);
                    }
                }
                s.glow = (cglow & GLOW_BITS) | (s.glow & ~GLOW_BITS);
            } else if (op == OP_TRANSFORMED) {
                if (a < COVERED) {
                    return;
                }
                uint under = s.idx;
                s.idx = xf(c.z, under);
                turn_colour(c.z, under, s.idx, s);
                if (mix_used) {
                    s.mx.x = xf(c.z, s.mx.x);
                }
                s.glow = 0u;
            }
        }

        // Whether command i replaces every plane at this pixel (its texel t, alpha above 0): then nothing below it shows.
        bool replaces_all(int i, uint t, ivec2 px) {
            int op = cmds.c[i * 4].x;
            if (op == OP_FILL || op == OP_SOLID) {
                return true;
            }
            uint a = (t >> 8) & 0xFFu;
            if (a < COVERED || !(op == OP_RLE || (op == OP_TRANSLUCENT && !see_through(t & 0xFFu)))) {
                return false;
            }
            // A texel's own colour blends over what is under unless it is wholly opaque.
            return i < p.colour_from || a == 255u || ((fetch_colour(i, px) >> 24) & 0xFFu) == 0u;
        }

        void reference(int from, int to, ivec2 px, inout State s) {
            for (int n = from; n < to; n++) {
                int i = entries.e[n];
                uint t = fetch(i, px);
                if (((t >> 8) & 0xFFu) != 0u) {
                    apply(i, t, px, s);
                }
            }
        }

        void skipping(int from, int to, ivec2 px, inout State s) {
            int stack[STACK];
            int sp = 0;
            for (int n = to - 1; n >= from; n--) {
                int i = entries.e[n];
                uint t = fetch(i, px);
                if (((t >> 8) & 0xFFu) == 0u) {
                    continue;
                }
                if (replaces_all(i, t, px)) {
                    apply(i, t, px, s);
                    break;
                }
                if (sp == STACK) {
                    reference(from, to, px, s);
                    return;
                }
                stack[sp++] = i;
            }
            for (int k = sp - 1; k >= 0; k--) {
                int i = stack[k];
                apply(i, fetch(i, px), px, s);
            }
        }

        void main() {
            ivec2 px = ivec2(gl_GlobalInvocationID.xy);
            if (px.x >= p.width || px.y >= p.height) {
                return;
            }
            int tile = (px.y / p.tile_size) * p.tiles_x + px.x / p.tile_size;
            int from = tiles.o[tile];
            int to = tiles.o[tile + 1];
            State s = State(0u, 0u, 0u, uvec2(0u), uvec3(0u), false, 0u);
            if (p.skip != 0) {
                skipping(from, to, px, s);
            } else {
                reference(from, to, px, s);
            }
            imageStore(out_index, px, vec4(float(s.idx) / 255.0, 0.0, 0.0, 1.0));
            imageStore(out_glow, px, vec4(float(s.glow) / 255.0, 0.0, 0.0, 1.0));
            imageStore(out_mix, px, vec4(float(s.mx.x) / 255.0, float(s.mx.y) / 255.0, 0.0, 1.0));
            bool pane = (s.glow & PANE_MARK) != 0u;
            imageStore(out_panes, px, pane ? vec4(float(s.panes & 0xFFu) / 255.0, float(s.panes >> 8u) / 255.0, 0.0, 1.0) : vec4(0.0, 0.0, 0.0, 1.0));
            imageStore(out_colour, px, s.own ? vec4(vec3(s.col) / 255.0, 1.0) : vec4(0.0));
            imageStore(out_lift, px, vec4(float(s.lift) / 255.0, 0.0, 0.0, 1.0));
        }
        """;
}
