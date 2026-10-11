using System.IO;
using System.Text;
using Godot;
using U7.Audio;
using U7.Core;
using U7.Data;
using U7.UI;

namespace U7.Game;

/// <summary>
/// Exult <c>BG_Game::play_intro</c>: Lord British presents; the trees with
/// "Ultima VII" and the butterfly; the Guardian surfacing through the static
/// and speaking (lip-synced from MAINSHP.FLX 0x0F, his words from 0x0D as
/// subtitles, and the voice of INTROSND.DAT); the desk, the monitor and the Orb; the
/// moongate behind the house. The pictures are ENDSHAPE.FLX's, in INTROPAL's
/// palettes, on the endgame's screen; the music is the intro's. Esc, or a
/// second click within half a second, ends it all; Space or Enter the scene
/// (in the moongate's, it all; Exult <c>wait_delay</c> and its
/// <c>UserBreakException</c> / <c>UserSkipException</c>). Not ported: the
/// sound effects and the hand slapping the monitor (Exult's own files, not
/// the game's).
/// </summary>
public sealed class Intro : Show
{
    const int UltimaTextShp = 0x0D, ButterflyShp = 0x0E, LordBritishShp = 0x11, TreesShp = 0x12,
        GuardianMouthShp = 0x1E, GuardianForeheadShp = 0x1F, GuardianEyesShp = 0x20, DotShp = 0x14, FakeTitleShp = 0x1D;
    const int BirdSongMidi = 0, HomeSongMidi = 1, GuardianMidi = 2;
    const int CenterX = EndgameView.ScreenWidth / 2, CenterY = EndgameView.ScreenHeight / 2;
    const int Width = EndgameView.ScreenWidth, Height = EndgameView.ScreenHeight;
    /// <summary>A static frame lasts at least this long (Exult draws as fast as it can, waiting 2 ms).</summary>
    const int StaticFrameMs = 16;

    readonly VgaShapeFile _shapes = new(Path.Combine(U7Paths.StaticDir, "ENDSHAPE.FLX"));
    readonly Random _rng = new();
    bool _break;
    bool _skipScene;
    ulong _lastClickMsec;
    /// <summary>Exult <c>wait_delay</c>'s <c>last_rotate</c>: when the palette was last turned.</summary>
    double _lastRotate;

    protected override string LogName => "INTRO";

    public Intro(EndgameView view, MusicPlayer music) : base(view, music) => _steps = Scenes().GetEnumerator();

    /// <summary>
    /// Exult <c>wait_delay</c>'s answers: Esc (1) or a second click of
    /// either button within half a second ends the intro; Space or Enter (2)
    /// the scene.
    /// </summary>
    public override void Input(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventKey { Pressed: true, Echo: false } key:
                if (key.Keycode == Key.Escape)
                {
                    _break = true;
                }
                else if (key.Keycode is Key.Space or Key.Enter or Key.KpEnter)
                {
                    _skipScene = true;
                }

                break;
            case InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left or MouseButton.Right }:
                var now = Time.GetTicksMsec();
                if (now - _lastClickMsec < 500)
                {
                    _break = true;
                }

                _lastClickMsec = now;
                break;
        }
    }

    /// <summary>Space or Enter (the console's <c>cont</c>): the scene ends.</summary>
    public override void Skip() => _skipScene = true;

    /// <summary>Esc (the console's <c>cont all</c>): the intro ends.</summary>
    public void Break() => _break = true;

    IEnumerable<double> Scenes()
    {
        _music.Stop();
        _view.Clear();
        _view.Present();
        var scenes = new (string Name, Func<IEnumerable<double>> Steps, bool SkipEnds)[]
        {
            ("Lord British presents", LordBritish, false), ("the butterfly", Butterfly, false), ("the Guardian", Guardian, false),
            ("the desk", Desk, false), ("the moongate", Moongate, true)
        };
        foreach (var (name, scene, skipEnds) in scenes)
        {
            Log?.Invoke("INTRO " + name);
            _skipScene = false;
            using (var steps = scene().GetEnumerator())
            {
                while (!_break && !_skipScene && steps.MoveNext())
                {
                    yield return steps.Current;
                }
            }

            // The moongate catches no UserSkipException: it ends the intro.
            if (_break || (_skipScene && skipEnds))
            {
                break;
            }
        }

        foreach (var w in Fade(FadeTime, fadeIn: false, null))
        {
            yield return w;
        }

        _view.Clear();
        _view.Present();
        _music.Stop();
    }

    /// <summary>
    /// Exult <c>wait_delay(ms, startcol, ncol, rotspd)</c>: the wait in
    /// steps of 10 ms (50 without colours to turn), the palette's range turned
    /// by one every <paramref name="speed"/> ms (a negative count the other way).
    /// </summary>
    IEnumerable<double> Wait(int ms, int first = 0, int count = 0, int speed = 100)
    {
        var step = count == 0 ? 50 : 10;
        ms = Math.Max(1, ms);
        var steps = ms <= 2 * step ? 1 : ms / step;
        var each = ms <= 2 * step ? ms : step;
        for (var i = 0; i < steps; i++)
        {
            yield return each;
            if (Math.Abs(count) > 1 && Now > _lastRotate + speed)
            {
                _view.RotateColors(first, count);
                while (Now > _lastRotate + speed)
                {
                    _lastRotate += speed;
                }

                _view.Present();
            }
        }
    }

    IEnumerable<double> Cycle1(int ms) => Wait(ms, 16, 78, 50);
    IEnumerable<double> Cycle2(int ms) => Wait(ms, 250, 5);
    IEnumerable<double> Cycle3(int ms) => Wait(ms, 240, 15);
    IEnumerable<double> Cycle4(int ms) => Wait(ms, 16, 78, 15);
    IEnumerable<double> Cycle5(int ms) => Wait(ms, 16, -78, 50);
    IEnumerable<double> Cycle6(int ms) => Wait(ms, 16, -78, 15);

    /// <summary>Exult <c>pal->load(INTROPAL_DAT, n)</c>; shown at once when <paramref name="apply"/>, else after a fade.</summary>
    void LoadPalette(int index, bool apply)
    {
        var (palette, from) = IntroPalette(index);
        _view.SetPalette(palette, from);
        if (apply)
        {
            _view.SetFade(1, 1);
        }
    }

    /// <summary>Exult <c>sman->paint_shape</c> of an ENDSHAPE.FLX frame, its hot spot at (x, y).</summary>
    void Paint(int shape, int frame, int x, int y)
    {
        if (_shapes.DecodeFrame(shape, frame, false) is { } f)
        {
            _view.PaintFrame(f, x, y);
        }
    }

    byte[] Snapshot() => (byte[])_view.Pixels.Clone();

    void Restore(byte[] snapshot) => snapshot.CopyTo(_view.Pixels, 0);

    /// <summary>Exult's FLASH_SHAPE: the frame shown for the wait, then the picture as it was.</summary>
    IEnumerable<double> Flash(int shape, int frame, int x, int y, IEnumerable<double> wait)
    {
        var under = Snapshot();
        Paint(shape, frame, x, y);
        _view.Present();
        foreach (var w in wait)
        {
            yield return w;
        }

        Restore(under);
    }

    /// <summary>Exult <c>Image_buffer8::fill_static(0, 7, 15)</c>: black, grey or (one in five) white pixels.</summary>
    void FillStatic(int x0, int y0, int w, int h)
    {
        for (var y = Math.Max(0, y0); y < Math.Min(Height, y0 + h); y++)
        {
            for (var x = Math.Max(0, x0); x < Math.Min(Width, x0 + w); x++)
            {
                _view.Pixels[y * Width + x] = _rng.Next(5) switch
                {
                    0 or 1 => 0,
                    2 or 3 => 7,
                    _ => 15
                };
            }
        }
    }

    /// <summary>Static on the whole screen for <paramref name="ms"/>, the plasma's colours turning (Exult's WAITDELAYCYCLE1(2) loop).</summary>
    IEnumerable<double> Static(int ms)
    {
        var until = Now + ms;
        while (Now <= until)
        {
            FillStatic(0, 0, Width, Height);
            _view.Present();
            foreach (var w in Cycle1(StaticFrameMs))
            {
                yield return w;
            }
        }
    }

    void Fill(byte colour, int x0, int y0, int w, int h)
    {
        for (var y = Math.Max(0, y0); y < Math.Min(Height, y0 + h); y++)
        {
            for (var x = Math.Max(0, x0); x < Math.Min(Width, x0 + w); x++)
            {
                _view.Pixels[y * Width + x] = colour;
            }
        }
    }

    /// <summary>Exult <c>Game_window::plasma</c>: the screen in colours 16-92, splashed with small crosses of random ones.</summary>
    byte[] Plasma(byte first, byte last)
    {
        var saved = Snapshot();
        Fill(first, 0, 0, Width, Height);
        for (var i = 0; i < Width * Height; i++)
        {
            var colour = (byte)(first + _rng.Next(last - first + 1));
            var px = _rng.Next(Width);
            var py = _rng.Next(Height);
            for (var j = 0; j < 6; j++)
            {
                var px2 = px + _rng.Next(17) - 8;
                var py2 = py + _rng.Next(17) - 8;
                Fill(colour, px2 - 1, py2, 3, 1);
                Fill(colour, px2, py2 - 1, 1, 3);
            }
        }

        var plasma = Snapshot();
        Restore(saved);
        return plasma;
    }

    /// <summary>Exult's point scaler: the part (x, y, w, h) of the picture made the whole screen.</summary>
    void Zoom(byte[] picture, int sx, int sy, int sw, int sh)
    {
        for (var y = 0; y < Height; y++)
        {
            var row = Math.Min(Height - 1, sy + y * sh / Height) * Width;
            for (var x = 0; x < Width; x++)
            {
                _view.Pixels[y * Width + x] = picture[row + Math.Min(Width - 1, sx + x * sw / Width)];
            }
        }
    }

    void StartMusic(int track) =>
        _music.StartFile(Path.Combine(U7Paths.AssetsDir, "intro_endgame", "music_mt32", $"{track:D4}_INTRORDM.MID"), -1, false);

    /// <summary>Exult <c>scene_lord_british</c>: "Lord British presents" in palette 3, faded in and out.</summary>
    IEnumerable<double> LordBritish()
    {
        foreach (var w in Wait(1500))
        {
            yield return w;
        }

        LoadPalette(3, apply: false);
        _view.Clear();
        Paint(LordBritishShp, 0, 0, 0);
        foreach (var w in Fade(FadeTime, fadeIn: true, null))
        {
            yield return w;
        }

        foreach (var w in Wait(2000))
        {
            yield return w;
        }

        foreach (var w in Fade(FadeTime, fadeIn: false, null))
        {
            yield return w;
        }

        _view.Clear();
        _view.Present();
    }

    static readonly int[] ButterflyX =
    [
        6, 18, 30, 41, 52, 62, 70, 78, 86, 95, 104, 113, 122, 132, 139, 146, 151, 155, 157, 158, 157, 155, 151, 146,
        139, 132, 124, 116, 108, 102, 96, 93, 93, 93, 95, 99, 109, 111, 118, 125, 132, 140, 148, 157, 164, 171, 178, 184,
        190, 196, 203, 211, 219, 228, 237, 246, 254, 259, 262, 264, 265, 265, 263, 260, 256, 251, 245, 239, 232, 226, 219, 212,
        208, 206, 206, 209, 212, 216, 220, 224, 227, 234, 231, 232, 233, 233, 233, 233, 234, 236, 239, 243, 247, 250, 258, 265
    ];

    static readonly int[] ButterflyY =
    [
        155, 153, 151, 150, 149, 148, 148, 148, 148, 149, 150, 150, 150, 149, 147, 142, 137, 131, 125, 118, 110, 103, 98, 94,
        92, 91, 91, 91, 92, 95, 99, 104, 110, 117, 123, 127, 131, 134, 135, 135, 135, 135, 135, 134, 132, 129, 127, 123,
        119, 115, 112, 109, 104, 102, 101, 102, 109, 109, 114, 119, 125, 131, 138, 144, 149, 152, 156, 158, 159, 159, 158, 155,
        150, 144, 137, 130, 124, 118, 112, 105, 99, 93, 86, 80, 73, 66, 59, 53, 47, 42, 38, 35, 32, 29, 26, 25
    ];

    static readonly int[] ButterflyEndFrames = [3, 4, 3, 4, 3, 2, 1, 0];
    static readonly int[] ButterflyEndDelays = [167, 416, 250, 416, 416, 416, 416, 333];

    /// <summary>
    /// Exult <c>scene_butterfly</c>: the trees and "Ultima VII" in palette 4
    /// to the birdsong, faded in, then the butterfly along its path (three
    /// steps between points, 23 ms each, flapping four times in five) and
    /// fluttering at its end; then the birdsong plays out.
    /// </summary>
    IEnumerable<double> Butterfly()
    {
        const int frameMs = 23, steps = 3;
        LoadPalette(4, apply: false);
        StartMusic(BirdSongMidi);
        _view.Clear();
        Paint(TreesShp, 0, 0, 0);
        Paint(UltimaTextShp, 0, 160, 50);
        _view.Present();
        foreach (var w in Wait(4500))
        {
            yield return w;
        }

        foreach (var w in Fade(FadeTime, fadeIn: true, null))
        {
            yield return w;
        }

        foreach (var w in Wait(4000).Concat(Wait(7100)))
        {
            yield return w;
        }

        var frame = 0;
        var dir = 0;
        for (var i = 0; i < ButterflyX.Length - 1; i++)
        {
            for (var j = 0; j < steps; j++)
            {
                var x = ButterflyX[i] + j * (ButterflyX[i + 1] - ButterflyX[i]) / steps;
                var y = ButterflyY[i] + j * (ButterflyY[i + 1] - ButterflyY[i]) / steps;
                foreach (var w in Flash(ButterflyShp, frame, x, y, Wait(frameMs)))
                {
                    yield return w;
                }

                // Not always: the butterfly glides now and then.
                if (_rng.Next(5) < 4)
                {
                    dir = frame == 3 ? -1 : frame == 0 ? 1 : dir;
                    frame += dir;
                }
            }
        }

        for (var i = 0; i < ButterflyEndFrames.Length; i++)
        {
            foreach (var w in Flash(ButterflyShp, ButterflyEndFrames[i], ButterflyX[^1], ButterflyY[^1], Wait(ButterflyEndDelays[i])))
            {
                yield return w;
            }
        }

        foreach (var w in Wait(1000))
        {
            yield return w;
        }

        while (_music.IsPlaying)
        {
            foreach (var w in Wait(20))
            {
                yield return w;
            }
        }
    }

    /// <summary>
    /// Exult <c>LipSynchReader::translate_code</c>: codes 8-22 the mouth's
    /// frame, 7 the eyes looking down, 1-6 the eyebrows and eyes by the
    /// table, from the last eye frame.
    /// </summary>
    static readonly int[,] EyeFrames =
    {
        { 1, 2, 3, 1, 2, 3, 1, 2, 3 }, { 4, 5, 6, 4, 5, 6, 4, 5, 6 }, { 7, 8, 9, 7, 8, 9, 7, 8, 9 },
        { 1, 1, 1, 4, 4, 4, 7, 7, 7 }, { 2, 2, 2, 5, 5, 5, 8, 8, 8 }, { 3, 3, 3, 6, 6, 6, 9, 9, 9 }
    };

    static void Translate(int code, ref int mouth, ref int eyes, ref int lastEyes)
    {
        if (code is >= 8 and < 23)
        {
            mouth = code - 8;
        }
        else if (code == 7)
        {
            eyes = 10;
        }
        else if (code is > 0 and < 7)
        {
            eyes = EyeFrames[code - 1, lastEyes - 1];
            lastEyes = eyes;
        }
    }

    /// <summary>Exult <c>LipSynchReader</c>: events of a u16 time (60ths of a second) and a code byte.</summary>
    static List<(int Ms, int Code)> LipSync(byte[] data)
    {
        var events = new List<(int, int)>();
        for (var i = 0; i + 3 <= data.Length; i += 3)
        {
            events.Add(((int)((data[i] | data[i + 1] << 8) * 1000.0 / 60.0), data[i + 2]));
        }

        return events;
    }

    static readonly byte[] SurfacingData =
    [
        0x03, 0x00, 0x02, // eyebrows neutral
        0x03, 0x00, 0x05, // eyes half-open
        0x15, 0x00, 0x04, // eyes fully open
        0x33, 0x00, 0x07, // eyes look down
        0x54, 0x00, 0x04, // eyes fully open
        0x77, 0x00, 0x00
    ];

    static readonly int[] TextTimes = [250, 1700, 5250, 8200, 10550, 12900, 16000, 19950, 22700, 27200, 32400, 36400, 39650, 42400, 44000];

    /// <summary>
    /// Exult <c>scene_guardian</c>: static and the blue plasma in palette 2;
    /// the Guardian's music; three tries at surfacing, the third his face;
    /// his eyes opening, then his speech lip-synced with its subtitles in the
    /// Guardian's lettering; his face sinking back; static, and the white dot
    /// shivering in the black. The plasma's colours turn all the while.
    /// </summary>
    IEnumerable<double> Guardian()
    {
        const int eyesDist = 12, foreheadDist = 49;
        var plasma = Plasma(16, 16 + 76);
        LoadPalette(2, apply: true);
        foreach (var (ms, keep) in new[] { (400, 200), (200, 200), (100, 0) })
        {
            foreach (var w in Static(ms))
            {
                yield return w;
            }

            Restore(plasma);
            _view.Present();
            foreach (var w in Cycle1(keep))
            {
                yield return w;
            }
        }

        StartMusic(GuardianMidi);
        foreach (var w in Cycle1(3800))
        {
            yield return w;
        }

        // The first two tries, then the third with frame 0 under the others.
        for (var i = 8; i >= -8; i--)
        {
            foreach (var w in Flash(0x21, 1 + Math.Abs(i), CenterX - 53, CenterY - 68, Cycle4(80)))
            {
                yield return w;
            }
        }

        foreach (var w in Cycle1(2000))
        {
            yield return w;
        }

        for (var i = 9; i >= -9; i--)
        {
            foreach (var w in Flash(0x22, 9 - Math.Abs(i), CenterX, CenterY - 45, Cycle4(80)))
            {
                yield return w;
            }
        }

        foreach (var w in Cycle1(2000))
        {
            yield return w;
        }

        var clean = Snapshot();
        Paint(0x23, 0, CenterX, CenterY);
        for (var i = 1; i < 16; i++)
        {
            foreach (var w in Flash(0x23, i, CenterX, CenterY, Cycle4(70)))
            {
                yield return w;
            }
        }

        Paint(0x23, 15, CenterX, CenterY);
        _view.Present();
        foreach (var w in Cycle1(500))
        {
            yield return w;
        }

        Restore(clean);

        // The face: the mouth and eyes over their frame 0, the forehead still.
        Paint(GuardianMouthShp, 0, CenterX, CenterY);
        Paint(GuardianEyesShp, 0, CenterX, CenterY - eyesDist);
        Paint(GuardianForeheadShp, 0, CenterX, CenterY - foreheadDist);
        var face = Snapshot();
        var font = new Lettering(VgaFont.FromShape(_mainshp, 3, -2), LineSize, UiTheme.GuardianRed, true);
        var lines = SpeechLines();
        var textY = Height - font.TextHeight - 16;
        int eyes = 3, lastEyes = 3, mouth = 1, textIndex = -1;
        string? line = null;

        void DrawSpeech()
        {
            Restore(face);
            _view.ClearWords();
            Paint(GuardianEyesShp, eyes, CenterX, CenterY - eyesDist);
            Paint(GuardianMouthShp, mouth, CenterX, CenterY);
            if (textIndex > 0 && textIndex < TextTimes.Length && line is not null)
            {
                _view.Paint(font.Words(line, CenterX, textY, 0));
            }

            _view.Present();
        }

        var surfacing = LipSync(SurfacingData);
        var next = 0;
        var start = Now;
        DrawSpeech();
        while (Now - start < 2000)
        {
            if (next < surfacing.Count && Now - start >= surfacing[next].Ms)
            {
                Translate(surfacing[next++].Code, ref mouth, ref eyes, ref lastEyes);
                DrawSpeech();
            }

            foreach (var w in Cycle5(15))
            {
                yield return w;
            }
        }

        // Exult's SpeechManager: INTROSND's voice from here to the face's end (stopped if the scene is cut).
        // With it the loop lasts as long as the voice, else the lip-sync's own events time it; the subtitles
        // show without a voice or with the user's subtitles (Exult's speech_with_subs).
        var lipsync = LipSync(new FlexFile(_mainshpPath).Get(0x0F).ToArray());
        var speech = Speech?.PlayFile(Path.Combine(U7Paths.StaticDir, "INTROSND.DAT")) == true;
        try
        {
            next = 0;
            start = Now;
            textIndex = !speech || Speech!.WithSubtitles ? 0 : TextTimes.Length;
            var lineAt = 0;
            // Exult: until the voice ends, or without one until the event waiting is the last (never shown).
            while (speech ? Speech!.IsPlaying : next < lipsync.Count - 1)
            {
                var redraw = false;
                if (next < lipsync.Count - 1 && lipsync[next].Code != 0 && Now - start >= lipsync[next].Ms)
                {
                    Translate(lipsync[next++].Code, ref mouth, ref eyes, ref lastEyes);
                    redraw = true;
                }
                else if (next < lipsync.Count - 1 && lipsync[next].Code == 0)
                {
                    next++;
                }

                if (textIndex < TextTimes.Length && Now - start >= TextTimes[textIndex])
                {
                    textIndex++;
                    line = lineAt < lines.Count ? lines[lineAt++] : null;
                    if (line is not null && textIndex < TextTimes.Length)
                    {
                        Log?.Invoke("INTRO " + line);
                    }

                    redraw = true;
                }

                if (redraw)
                {
                    DrawSpeech();
                }

                foreach (var w in Cycle6(15))
                {
                    yield return w;
                }
            }

            foreach (var w in Cycle6(1000))
            {
                yield return w;
            }

            _view.ClearWords();
            Restore(clean);
        }
        finally
        {
            Speech?.Stop();
        }

        // He sinks back (0x23 again).
        Paint(0x23, 0, CenterX, CenterY);
        for (var i = 15; i > 0; i--)
        {
            foreach (var w in Flash(0x23, i, CenterX, CenterY, Cycle4(70)))
            {
                yield return w;
            }
        }

        Restore(clean);
        _view.Present();
        foreach (var w in Cycle1(1200))
        {
            yield return w;
        }

        _music.Stop();
        foreach (var w in Static(400))
        {
            yield return w;
        }

        _view.Clear();
        _view.Present();
        var until = Now + 800;
        while (Now <= until)
        {
            foreach (var w in Flash(DotShp, 0, CenterX + _rng.Next(3) - 1, CenterY + _rng.Next(3) - 1, Cycle1(1)))
            {
                yield return w;
            }

            foreach (var w in Cycle1(StaticFrameMs))
            {
                yield return w;
            }
        }
    }

    /// <summary>MAINSHP.FLX text 0x0D: the Guardian's lines, a CR LF after each (Exult's <c>AdvanceTextPointer</c>).</summary>
    List<string> SpeechLines()
    {
        var text = Encoding.Latin1.GetString(new FlexFile(_mainshpPath).Get(0x0D)).TrimEnd('\0');
        return text.Split("\r\n").ToList();
    }

    /// <summary>
    /// Exult's <c>Hand_Handler</c> script without the hand (Exult's own
    /// art): the monitor's picture black, static or the fake title, a step
    /// every 60 ms.
    /// </summary>
    enum MonitorOp
    {
        Nop,
        HandHit,
        HandRecoil,
        Black,
        Static,
        FlashFakeTitle,
        FakeTitle
    }

    static readonly MonitorOp[] MonitorScript =
    [
        MonitorOp.HandHit, MonitorOp.Black, MonitorOp.FakeTitle, MonitorOp.Static, MonitorOp.HandRecoil, MonitorOp.Nop,
        MonitorOp.HandRecoil, MonitorOp.Nop, MonitorOp.HandRecoil, MonitorOp.Black, MonitorOp.HandHit, MonitorOp.Nop,
        MonitorOp.FakeTitle, MonitorOp.Static, MonitorOp.HandRecoil, MonitorOp.Nop, MonitorOp.HandRecoil, MonitorOp.Nop,
        MonitorOp.HandRecoil, MonitorOp.Black, MonitorOp.HandHit, MonitorOp.Nop, MonitorOp.FlashFakeTitle, MonitorOp.HandRecoil,
        MonitorOp.FlashFakeTitle, MonitorOp.HandRecoil, MonitorOp.FlashFakeTitle, MonitorOp.HandRecoil, MonitorOp.FakeTitle
    ];

    void PaintMonitor(int dx, int dy)
    {
        foreach (var shape in new[] { 0x07, 0x09, 0x08, 0x0A })
        {
            Paint(shape, 0, CenterX + dx, CenterY + dy);
        }
    }

    /// <summary>
    /// Exult <c>scene_desk</c>: the monitor in palette 1 to the home song,
    /// zoomed out from its screen (the dot shivering on it), the screen's
    /// slaps (without the hand), "Something is obviously amiss", the view
    /// scrolling right to the map and down to the mouse and the Orb with
    /// their words, then fading out.
    /// </summary>
    IEnumerable<double> Desk()
    {
        StartMusic(HomeSongMidi);
        _view.Clear();
        LoadPalette(1, apply: true);
        PaintMonitor(0, 0);

        // The zoom out of the screen, the dot shivering on it.
        var unzoomed = Snapshot();
        var dotSpot = Snapshot();
        Paint(DotShp, 0, CenterX + 12, CenterY - 22);
        var withDot = Snapshot();
        Restore(dotSpot);
        const int zx = 88, zy = 22, zw = 166, zh = 112;
        for (var i = 0; i < 40; i++)
        {
            var frame = (byte[])unzoomed.Clone();
            var (jx, jy) = (_rng.Next(3) - 1, _rng.Next(3) - 1);
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    var at = y * Width + x;
                    if (withDot[at] != unzoomed[at])
                    {
                        var tx = x + jx;
                        var ty = y + jy;
                        if ((uint)tx < Width && (uint)ty < Height)
                        {
                            frame[ty * Width + tx] = withDot[at];
                        }
                    }
                }
            }

            Zoom(frame, zx + (0 - zx) * i / 40, zy + (0 - zy) * i / 40, zw + (Width - zw) * i / 40, zh + (Height - zh) * i / 40);
            _view.Present();
            foreach (var w in Wait(10))
            {
                yield return w;
            }
        }

        Restore(unzoomed);
        _view.Present();

        // The monitor's script: black, the fake title or static on its screen.
        var screen = _shapes.DecodeFrame(FakeTitleShp, 0, false);
        var (sw, sh) = screen is null ? (160, 99) : (screen.Width, screen.Height);
        var sx = CenterX + 12 - sw / 2;
        var sy = CenterY - 22 - sh / 2;
        var background = MonitorOp.Black;
        foreach (var op in MonitorScript)
        {
            // Exult shows the picture in these branches only; a flashed title stays seen for the
            // step while black is drawn under it. (The hand's own steps showed only the hand.)
            switch (op)
            {
                case MonitorOp.HandHit or MonitorOp.HandRecoil when background == MonitorOp.Static:
                case MonitorOp.Static or MonitorOp.Nop:
                    if (op == MonitorOp.Static)
                    {
                        background = op;
                    }

                    FillStatic(sx, sy, 160, 99);
                    _view.Present();
                    break;
                case MonitorOp.FlashFakeTitle or MonitorOp.FakeTitle:
                    Paint(FakeTitleShp, 0, CenterX + 12, CenterY - 22);
                    _view.Present();
                    if (op == MonitorOp.FlashFakeTitle)
                    {
                        Fill(0, sx, sy, sw, sh);
                    }
                    else
                    {
                        background = op;
                    }

                    break;
                case MonitorOp.Black:
                    background = op;
                    Fill(0, sx, sy, sw, sh);
                    _view.Present();
                    break;
            }

            foreach (var w in Wait(60))
            {
                yield return w;
            }
        }

        foreach (var w in Wait(1300))
        {
            yield return w;
        }

        Paint(0x15, 0, CenterX, CenterY + 50); // "Something is obviously amiss"
        _view.Present();
        foreach (var w in Wait(3000))
        {
            yield return w;
        }

        for (var i = 0; i <= 194; i += 2)
        {
            PaintMonitor(-i, 0);
            Paint(FakeTitleShp, 0, CenterX - i + 12, CenterY - 22);
            Paint(0x06, 0, Width - i, 0);
            if (i is > 75 and < 194)
            {
                Paint(0x16, 0, CenterX, CenterY + 50); // "It has been a long time..."
            }

            _view.Present();
            foreach (var w in Wait(110))
            {
                yield return w;
            }
        }

        foreach (var w in Wait(1500))
        {
            yield return w;
        }

        for (var i = 0; i <= 50; i += 2)
        {
            PaintMonitor(-194, -i);
            Paint(FakeTitleShp, 0, CenterX - 194 + 12, CenterY - 22 - i);
            Paint(0x06, 0, 319 - 194, -i);
            Paint(0x0B, 0, 319, 199 - i);
            Paint(0x17, 0, CenterX, 0); // "The mystical Orb beckons you"
            _view.Present();
            foreach (var w in Cycle2(110))
            {
                yield return w;
            }
        }

        PaintMonitor(-194, -50);
        Paint(FakeTitleShp, 0, CenterX - 182, CenterY - 72);
        Paint(0x06, 0, 319 - 194, -50);
        Paint(0x0B, 0, 319, 149);
        Paint(0x18, 0, CenterX, 0); // "It has opened gateways to Britannia in the past"
        _view.Present();
        foreach (var w in Cycle2(3200))
        {
            yield return w;
        }

        foreach (var w in Fade(100, fadeIn: false, null))
        {
            yield return w;
        }

        _view.Clear();
        _view.Present();
    }

    void PaintMoongate()
    {
        foreach (var shape in new[] { 0x02, 0x03, 0x04, 0x05 })
        {
            Paint(shape, 0, CenterX + 1, CenterY + 1);
        }
    }

    /// <summary>
    /// Exult <c>scene_moongate</c>: "Behind your house is the circle of
    /// stones" faded in in palette 5; the bushes parting before the moongate
    /// with its words; the home song played out; the run into the moongate.
    /// </summary>
    IEnumerable<double> Moongate()
    {
        _view.Clear();
        LoadPalette(5, apply: false);
        Paint(0x19, 0, CenterX, CenterY + 50);
        foreach (var w in Fade(FadeTime, fadeIn: true, null))
        {
            yield return w;
        }

        foreach (var w in Wait(4000))
        {
            yield return w;
        }

        for (var i = 50; i >= -170; i -= 2)
        {
            PaintMoongate();
            Paint(0x00, 0, CenterX + i, -((i - 60) / 4));
            Paint(0x01, 0, CenterX - i, -((i - 60) / 4));
            if (i is < 40 and > -100)
            {
                Paint(0x1A, 0, CenterX, CenterY + 50); // "Why is a moongate already there?"
            }
            else if (i <= -100)
            {
                Paint(0x1C, 0, CenterX, CenterY + 50); // "You have but one path to the answer"
            }

            _view.Present();
            foreach (var w in Cycle3(80))
            {
                yield return w;
            }
        }

        while (_music.IsPlaying)
        {
            foreach (var w in Cycle3(50))
            {
                yield return w;
            }
        }

        PaintMoongate();
        var unzoomed = Snapshot();
        const int zx = 151, zy = 81, zw = 5, zh = 4;
        for (var i = 159; i >= 0; i--)
        {
            Zoom(unzoomed, zx + (0 - zx) * i / 160, zy + (0 - zy) * i / 160, zw + (Width - zw) * i / 160, zh + (Height - zh) * i / 160);
            _view.Present();
            foreach (var w in Cycle3(5))
            {
                yield return w;
            }
        }
    }
}
