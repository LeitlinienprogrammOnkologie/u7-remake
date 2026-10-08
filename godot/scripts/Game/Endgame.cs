using System.IO;
using System.Text;
using Godot;
using U7.Audio;
using U7.Core;
using U7.Data;
using U7.UI;

namespace U7.Game;

/// <summary>
/// Exult <c>UI_run_endgame</c>: <c>BG_Game::end_game</c> and, after a win,
/// <c>show_credits</c>. Won: the three movies of ENDGAME.DAT with ENDSCORE's
/// music and the Guardian's lines as subtitles (no speech is played), the
/// text screens, the congratulations, then the credits scrolling up. Lost:
/// MAINSHP.FLX's text of the Guardian's victory and "THE END". Exult's code
/// step by step, as an iterator of waits in milliseconds on its own clock; a
/// key or click (Exult <c>wait_delay</c>) skips the rest of the movies, cuts
/// a wait of the lost ending short, or ends the credits. The words are the
/// user's look (<see cref="Lettering"/>): MedievalSharp where Exult paints
/// its fonts, the Guardian's red with a dark outline, the rest gold.
/// </summary>
public sealed class Endgame
{
    const int FadeTime = 30; // Exult c_fade_in_time, c_fade_out_time.
    const int CreditsMidi = 4;
    /// <summary>MedievalSharp's size, in picture pixels, for Exult's 14-pixel fonts and its 9-pixel menu font.</summary>
    const float LineSize = 12.5f, MenuSize = 10f;

    /// <summary>
    /// The user's look for one of Exult's fonts: MedievalSharp at
    /// <see cref="Size"/> picture pixels in its place, the lines as far apart
    /// as the font's (<see cref="Layout"/>).
    /// </summary>
    sealed record Lettering(VgaFont Layout, float Size, Color Color, bool Outline)
    {
        public int TextHeight => Layout.TextHeight;

        public EndgameView.Words Words(string text, float x, float y, int align) =>
            new(text, x, y, Size, align, Color, Outline);
    }

    readonly EndgameView _view;
    readonly MusicPlayer _music;
    readonly int _totalHours;
    readonly IffFile _data = new(Path.Combine(U7Paths.StaticDir, "ENDGAME.DAT"));
    readonly string _mainshpPath = Path.Combine(U7Paths.StaticDir, "MAINSHP.FLX");
    readonly VgaShapeFile _mainshp;
    readonly IEnumerator<double> _steps;
    double _clock;
    double _due;
    bool _skip;
    /// <summary>Exult <c>playfli::play</c>'s ticks: when the next frame shows.</summary>
    double _ticks;
    /// <summary>The credits' step under way: when it began and how long it lasts (0: no glide).</summary>
    double _riseFrom;
    double _riseMs;

    public bool Done { get; private set; }

    /// <summary>The credits roll: any key but Shift, or a click, ends them (Exult <c>TextScroller::run</c>).</summary>
    public bool InCredits { get; private set; }

    /// <summary>What shows, for the agent console.</summary>
    public Action<string>? Log { get; set; }

    public Endgame(EndgameView view, MusicPlayer music, int totalHours, bool success)
    {
        _view = view;
        _music = music;
        _totalHours = totalHours;
        _mainshp = new VgaShapeFile(_mainshpPath);
        _steps = (success ? Won() : Lost()).GetEnumerator();
    }

    /// <summary>The sequence's own time, at the step running (ms).</summary>
    double Now => _due;

    /// <summary>A key or click: Exult's <c>wait_delay</c> saw it.</summary>
    public void Skip() => _skip = true;

    public void Update(double delta)
    {
        if (Done)
        {
            return;
        }

        _clock += delta * 1000;
        for (var guard = 0; guard < 100000 && _clock >= _due; guard++)
        {
            if (!_steps.MoveNext())
            {
                Done = true;
                _music.Stop();
                Log?.Invoke("ENDGAME over");
                return;
            }

            _due += Math.Max(0, _steps.Current);
        }

        _view.Rise = _riseMs > 0 ? (float)Math.Clamp((_clock - _riseFrom) / _riseMs, 0, 1) : 0;
    }

    bool TakeSkip()
    {
        var skip = _skip;
        _skip = false;
        return skip;
    }

    /// <summary>Exult <c>run_endgame(true)</c>: the movies (skippable as a whole), then the credits.</summary>
    IEnumerable<double> Won()
    {
        _music.Stop();
        using (var movie = EndGame().GetEnumerator())
        {
            while (true)
            {
                if (TakeSkip())
                {
                    // Exult's UserSkipException.
                    _view.Brightness = 80;
                    _view.Clear();
                    _view.Present();
                    break;
                }

                if (!movie.MoveNext())
                {
                    break;
                }

                yield return movie.Current;
            }
        }

        _music.Stop();
        _view.Clear();
        _view.Present();
        foreach (var w in Credits())
        {
            yield return w;
        }
    }

    /// <summary>Exult <c>BG_Game::end_game(true)</c>.</summary>
    IEnumerable<double> EndGame()
    {
        Log?.Invoke("ENDGAME won");
        _view.Clear();
        _view.SetFade(0, 1);
        _view.Present();
        var fli1 = Flic(0);
        var fli2 = Flic(1);
        var fli3 = Flic(2);
        var end2 = new Lettering(EndgameFont(4, -1), LineSize, UiTheme.GuardianRed, true);
        var end3 = new Lettering(EndgameFont(5, -2), LineSize, UiTheme.GuardianRed, true);
        var normal = new Lettering(VgaFont.FromShape(VgaFont.File, 0, -1), LineSize, UiTheme.GoldBright, false);

        _ticks = 0;
        foreach (var w in Play(fli1, 0, 0))
        {
            yield return w;
        }

        StartScore(1);
        _ticks = 0;
        for (var i = 0; i < 240; i++)
        {
            foreach (var w in Play(fli1, 0, 1))
            {
                yield return w;
            }
        }

        for (var i = 1; i < 150; i++)
        {
            foreach (var w in Play(fli1, i, i + 1))
            {
                yield return w;
            }
        }

        foreach (var w in Subtitled(fli1, 150, 204, end2, TextMessages.YouCannotDoThat))
        {
            yield return w;
        }

        StartScore(2);
        foreach (var w in Subtitled(fli2, 0, 100, end2, TextMessages.DamnAvatar))
        {
            yield return w;
        }

        foreach (var w in Brighten(down: true))
        {
            yield return w;
        }

        foreach (var w in TextScreen(normal, 80, (EndgameView.ScreenHeight - normal.TextHeight) / 2, 30,
                     TextMessages.BlackgateDestroyed))
        {
            yield return w;
        }

        foreach (var w in TextScreen(normal, 80, (EndgameView.ScreenHeight - normal.TextHeight) / 2, 30,
                     TextMessages.GuardianHasStopped))
        {
            yield return w;
        }

        foreach (var w in Play(fli3, 0, 0))
        {
            yield return w;
        }

        foreach (var w in Brighten(down: false))
        {
            yield return w;
        }

        // The Guardian's threat over the last movie, round and round for 28 s.
        var starty = (EndgameView.ScreenHeight - end3.TextHeight * 8) / 2;
        Log?.Invoke("ENDGAME " + Lines(TextMessages.TextScreen0, 8));
        _ticks = Now;
        var until = _ticks + 28000;
        while (until > _ticks)
        {
            for (var j = 0; j < fli3.Frames; j++)
            {
                foreach (var w in Play(fli3, j, j))
                {
                    yield return w;
                }

                for (var m = 0; m < 8; m++)
                {
                    Center(end3, TextMessages.Get(TextMessages.TextScreen0 + m), starty + end3.TextHeight * m);
                }

                _view.Present();
                yield return 10;
            }
        }

        foreach (var w in Brighten(down: true))
        {
            yield return w;
        }

        // Exult fits the German version's 11 lines into 200 pixels.
        var screens = new (int First, int Count, int StartY, int Tenths)[]
        {
            (TextMessages.TextScreen1, 11, (int)((EndgameView.ScreenHeight - normal.TextHeight * 11) / 2.5), 100),
            (TextMessages.TextScreen2, 9, (EndgameView.ScreenHeight - normal.TextHeight * 9) / 2, 80),
            (TextMessages.TextScreen3, 8, (EndgameView.ScreenHeight - normal.TextHeight * 8) / 2, 80),
            (TextMessages.TextScreen4, 5, (EndgameView.ScreenHeight - normal.TextHeight * 5) / 2, 50)
        };
        foreach (var (first, count, y, tenths) in screens)
        {
            _view.Brightness = 80;
            _view.Clear();
            Log?.Invoke("ENDGAME " + Lines(first, count));
            for (var i = 0; i < count; i++)
            {
                Center(normal, TextMessages.Get(first + i), y + normal.TextHeight * i);
            }

            foreach (var w in Fade(50, fadeIn: true, DayPalette()))
            {
                yield return w;
            }

            yield return tenths * 100;
            foreach (var w in Fade(50, fadeIn: false, null))
            {
                yield return w;
            }

            if (first != TextMessages.TextScreen4)
            {
                yield return 10;
            }
        }

        foreach (var w in Congratulations(normal))
        {
            yield return w;
        }
    }

    /// <summary>
    /// Exult <c>Game::show_congratulations</c>: how long the game took (in
    /// months of 28 days, days and hours, less the first 6 hours), laid out
    /// by the normal font (Exult's own EXULT_END_FONT isn't in the game's
    /// files).
    /// </summary>
    IEnumerable<double> Congratulations(Lettering font)
    {
        yield return 100;
        _view.Clear();
        var starty = (EndgameView.ScreenHeight - font.TextHeight * 8) / 2;
        var lines = new List<string>();
        for (var i = 0; i < 9; i++)
        {
            var message = TextMessages.Get(TextMessages.Congrats + i);
            if (i == 2)
            {
                message = PlayedFor(message);
            }

            lines.Add(message);
            Center(font, message, starty + font.TextHeight * i);
        }

        Log?.Invoke("ENDGAME " + string.Join(" / ", lines.Where(l => l.Length > 0)));
        foreach (var w in Fade(50, fadeIn: true, DayPalette()))
        {
            yield return w;
        }

        yield return 8000;
        foreach (var w in Fade(50, fadeIn: false, null))
        {
            yield return w;
        }
    }

    /// <summary>Exult's line of time played, from its tokens (" & |only |exactly | year| years| month|...").</summary>
    string PlayedFor(string format)
    {
        var tokens = format.Split('|');
        Array.Resize(ref tokens, 12);
        string Token(int i) => tokens[i] ?? "";
        const int and = 0, only = 1, exactly = 2, month = 5, months = 6, day = 7, days = 8, hour = 9, hours = 10,
            negative = 11;
        var total = _totalHours - 6;
        if (total < 0)
        {
            return Token(negative);
        }

        var m = total / 672;
        total %= 672;
        var d = total / 24;
        var h = total % 24;
        string Count(int value, int one, int many) => value + Token(value == 1 ? one : many);
        var sb = new StringBuilder();
        if (m > 0)
        {
            if (d == 0 && h == 0)
            {
                sb.Append(Token(exactly));
            }

            sb.Append(Count(m, month, months));
            if (d > 0 && h > 0)
            {
                sb.Append(", ");
            }
            else if (d > 0 || h > 0)
            {
                sb.Append(Token(and));
            }
        }

        if (d > 0)
        {
            if (m == 0 && h == 0)
            {
                sb.Append(Token(exactly));
            }

            sb.Append(Count(d, day, days));
            if (h > 0)
            {
                sb.Append(Token(and));
            }
        }

        if (h > 0 || (m == 0 && d == 0))
        {
            if (m == 0 && d == 0)
            {
                sb.Append(Token(only));
            }

            sb.Append(Count(h, hour, hours));
        }

        return sb.Append('.').ToString();
    }

    /// <summary>
    /// Exult <c>end_game(false)</c>: the Guardian's victory (MAINSHP.FLX text
    /// 0x15) in INTROPAL's first palette, then "THE END OF ULTIMA VII" and
    /// "...OF BRITANNIA AS YOU KNOW IT"; a key only cuts a wait short.
    /// </summary>
    IEnumerable<double> Lost()
    {
        Log?.Invoke("ENDGAME lost");
        _music.Stop();
        var menu = MenuFont();
        var text = MainshpText(0x15);
        _view.Clear();
        _view.Brightness = 100;
        for (var i = 0; i < text.Count; i++)
        {
            ShowLine(text[i], menu, 0, EndgameView.ScreenWidth, 20 + i * 12, null);
        }

        Log?.Invoke("ENDGAME " + string.Join(" / ", text.Select(l => l.Replace("\\C", "")).Where(l => l.Length > 0)));
        var (lost, lostFrom) = IntroPalette(0);
        _view.SetPalette(lost, lostFrom);
        foreach (var w in Fade(FadeTime, fadeIn: true, null))
        {
            yield return w;
        }

        foreach (var w in Wait(10000))
        {
            yield return w;
        }

        foreach (var w in Fade(FadeTime, fadeIn: false, null))
        {
            yield return w;
        }

        foreach (var message in new[] { TextMessages.EndOfUltima7, TextMessages.EndOfBritannia })
        {
            _view.Clear();
            Center(menu, TextMessages.Get(message), EndgameView.ScreenHeight / 2 - 10);
            Log?.Invoke("ENDGAME " + TextMessages.Get(message));
            foreach (var w in Fade(FadeTime, fadeIn: true, null))
            {
                yield return w;
            }

            foreach (var w in Wait(4000))
            {
                yield return w;
            }

            foreach (var w in Fade(FadeTime, fadeIn: false, null))
            {
                yield return w;
            }
        }

        _view.Clear();
        _view.Present();
    }

    /// <summary>Exult <c>wait_delay(ms)</c> whose answer is ignored: a key ends it early.</summary>
    IEnumerable<double> Wait(int ms)
    {
        for (var t = 0; t < ms; t += 50)
        {
            if (TakeSkip())
            {
                yield break;
            }

            yield return Math.Min(50, ms - t);
        }
    }

    /// <summary>
    /// Exult <c>BG_Game::show_credits</c> and <c>TextScroller::run</c>:
    /// MAINSHP.FLX text 0x0E in the menu font with its pictures (shape 0x14),
    /// in INTROPAL's palette 6 to the intro's credits music, rising a pixel
    /// every 120 ms (Shift held: as fast as it can) until it is gone or a key
    /// or click ends it.
    /// </summary>
    IEnumerable<double> Credits()
    {
        Log?.Invoke("ENDGAME credits");
        InCredits = true;
        var text = MainshpText(0x0E);
        var menu = MenuFont();
        _view.Brightness = 100;
        var (credits, creditsFrom) = IntroPalette(6);
        _view.SetPalette(credits, creditsFrom);
        _view.SetFade(1, 1);
        _music.StartFile(Path.Combine(U7Paths.AssetsDir, "intro_endgame", "music_mt32", $"{CreditsMidi:D4}_INTRORDM.MID"),
            -1, false);
        if (text.Count == 0)
        {
            yield break;
        }

        const int top = 0;
        const int bottom = EndgameView.ScreenHeight;
        var starty = bottom;
        var startline = 0;
        var looping = true;
        var first = true;
        while (looping)
        {
            var ypos = starty;
            var curline = startline;
            _view.Clear();
            do
            {
                if (curline == text.Count)
                {
                    break;
                }

                ypos = ShowLine(text[curline++], menu, 0, EndgameView.ScreenWidth, ypos, Pictures);
                if (ypos < top)
                {
                    // That line is gone: start below it next time.
                    startline++;
                    starty = ypos;
                    if (startline >= text.Count)
                    {
                        looping = false;
                        break;
                    }
                }
            }
            while (ypos < bottom);

            _view.Present();
            if (TakeSkip())
            {
                looping = false;
            }

            if (!looping)
            {
                _riseMs = 0;
                foreach (var w in Fade(FadeTime, fadeIn: false, null))
                {
                    yield return w;
                }

                break;
            }

            // Exult holds the first picture 200 ms, then rises a pixel every 120 ms (Shift held:
            // as fast as the screen shows); here everything glides up towards the next step meanwhile.
            var step = first ? 200 : Input.IsKeyPressed(Key.Shift) ? 10 : 120;
            first = false;
            _riseFrom = Now;
            _riseMs = step;
            yield return step;
            starty--;
        }

        _riseMs = 0;
        _view.Clear();
        _view.Present();
    }

    VgaShapeFile? _pictures;

    /// <summary>The credits' pictures: MAINSHP.FLX shape 0x14 (Exult <c>menushapes.extract_shape(0x14)</c>).</summary>
    (VgaShapeFile File, int Shape) Pictures => (_pictures ??= _mainshp, 0x14);

    /// <summary>
    /// Exult <c>TextScroller::show_line</c>: one line of a MAINSHP.FLX text
    /// at <paramref name="y"/>, with its codes: \Px a picture (frame x),
    /// \C centred, \L left of the centre line, \R right of it (the
    /// default), '|' more on the same line, #nnn a character by number.
    /// </summary>
    /// <returns>The y below it.</returns>
    int ShowLine(string line, Lettering font, int left, int right, int y, (VgaShapeFile File, int Shape)? pictures)
    {
        const int vspace = 2;
        var ypos = y;
        var align = -1;
        var center = (right + left) / 2;
        var addLine = true;
        var txt = new StringBuilder();
        var i = 0;
        while (i < line.Length)
        {
            if (string.CompareOrdinal(line, i, "\\P", 0, 2) == 0)
            {
                var pix = i + 2 < line.Length ? line[i + 2] - '0' : 0;
                i += 3;
                if (pictures is { } p && p.File.DecodeFrame(p.Shape, pix, false) is { } frame)
                {
                    _view.PaintFrame(frame, center - frame.Width / 2, ypos);
                    ypos += frame.Height + vspace;
                }
            }
            else if (string.CompareOrdinal(line, i, "\\C", 0, 2) == 0)
            {
                i += 2;
                align = 0;
            }
            else if (string.CompareOrdinal(line, i, "\\L", 0, 2) == 0)
            {
                i += 2;
                align = 1;
            }
            else if (string.CompareOrdinal(line, i, "\\R", 0, 2) == 0)
            {
                i += 2;
                align = -1;
            }
            else if (line[i] == '|' || i + 1 == line.Length)
            {
                if (i + 1 == line.Length && line[i] != '|')
                {
                    txt.Append(line[i]);
                    addLine = false;
                }

                // Exult: -1 ends at the centre line, 0 is centred on it, 1 starts there.
                _view.Paint(font.Words(txt.ToString(), center, ypos, align));
                if (line[i] != '|')
                {
                    ypos += font.TextHeight + vspace;
                }

                txt.Clear();
                i++;
            }
            else if (line[i] == '#')
            {
                i++;
                if (i < line.Length && line[i] == '#')
                {
                    txt.Append('#');
                    i++;
                    continue;
                }

                var digits = 0;
                var value = 0;
                while (i < line.Length && digits < 3 && char.IsAsciiDigit(line[i]))
                {
                    value = value * 10 + line[i++] - '0';
                    digits++;
                }

                txt.Append((char)value);
            }
            else
            {
                txt.Append(line[i++]);
            }
        }

        if (addLine)
        {
            ypos += font.TextHeight;
        }

        return ypos;
    }

    /// <summary>
    /// Exult <c>playfli::play(first, last, ticks)</c> at brightness 100: the
    /// frames up to <paramref name="last"/> decoded, each from
    /// <paramref name="first"/> on put on the screen at its time, a frame
    /// time apart; a single frame (first == last) is put but not shown, for
    /// text to go over it.
    /// </summary>
    IEnumerable<double> Play(FlicFile fli, int first, int last)
    {
        var dontShow = first == last;
        if (first == last)
        {
            last++;
        }

        last = Math.Min(last, fli.Frames);
        if (_ticks == 0)
        {
            _ticks = Now;
        }

        _view.Brightness = 100;
        for (var f = first; f < last; f++)
        {
            fli.DecodeTo(f);
            _view.SetPalette(fli.Palette);
            _view.SetFade(1, 1);
            _view.PutPicture(fli.Pixels);
            if (_ticks > Now)
            {
                yield return _ticks - Now;
            }

            _ticks += fli.FrameMs;
            if (!dontShow)
            {
                _view.Present();
            }
        }
    }

    /// <summary>A movie's frames with a line of the Guardian's below (Exult's subtitles; speech isn't played).</summary>
    IEnumerable<double> Subtitled(FlicFile fli, int first, int end, Lettering font, int message)
    {
        var text = TextMessages.Get(message);
        var y = EndgameView.ScreenHeight - font.TextHeight * 2;
        Log?.Invoke("ENDGAME " + text);
        for (var i = first; i < end; i++)
        {
            foreach (var w in Play(fli, i, i))
            {
                yield return w;
            }

            _view.Paint(font.Words(text, EndgameView.ScreenWidth / 2f, y, 0));
            _view.Present();
        }
    }

    /// <summary>Exult's 1-second brightness ramps in 10 ms steps: down from 100, or up from 0.</summary>
    IEnumerable<double> Brighten(bool down)
    {
        for (var left = 1000; left > 0; left -= 10)
        {
            _view.Brightness = down ? left / 10 : 100 - left / 10;
            _view.Present();
            yield return 10;
        }
    }

    /// <summary>
    /// A text screen of the won ending: black, the line centred, faded in over
    /// a second in the day palette at <paramref name="brightness"/>, held,
    /// faded out (Exult <c>pal->fade(50, 1, 0)</c>, 30 × 100 ms, <c>pal->fade(50, 0, 0)</c>).
    /// </summary>
    IEnumerable<double> TextScreen(Lettering font, int brightness, int y, int tenths, int message)
    {
        _view.Brightness = brightness;
        _view.Clear();
        Center(font, TextMessages.Get(message), y);
        Log?.Invoke("ENDGAME " + TextMessages.Get(message));
        foreach (var w in Fade(50, fadeIn: true, DayPalette()))
        {
            yield return w;
        }

        yield return tenths * 100;
        foreach (var w in Fade(50, fadeIn: false, DayPalette()))
        {
            yield return w;
        }
    }

    /// <summary>Exult <c>Palette::fade_in</c> / <c>fade_out</c>: a step every 20 ms, from black or to it.</summary>
    IEnumerable<double> Fade(int cycles, bool fadeIn, byte[]? palette)
    {
        if (palette is not null)
        {
            _view.SetPalette(palette);
        }

        for (var i = 0; i <= cycles; i++)
        {
            _view.SetFade(fadeIn ? i : cycles - i, cycles);
            _view.Present();
            yield return 20;
        }
    }

    void Center(Lettering font, string text, int y) => _view.Paint(font.Words(text, EndgameView.ScreenWidth / 2f, y, 0));

    static string Lines(int first, int count) =>
        string.Join(" / ", Enumerable.Range(first, count).Select(TextMessages.Get).Where(l => l.Length > 0));

    void StartScore(int sequence) =>
        _music.StartFile(Path.Combine(U7Paths.AssetsDir, "audio", "endscore", "ENDSCORE.MID"), sequence, false);

    FlicFile Flic(int index) => new(_data.Get(index));

    /// <summary>Exult <c>Font::load</c> of an ENDGAME.DAT font (after its 8-byte name).</summary>
    VgaFont EndgameFont(int index, int horLead)
    {
        var entry = _data.Get(index);
        return VgaFont.FromShape(VgaShapeFile.SingleShape(entry.Length > 8 ? entry[8..] : entry), 0, horLead);
    }

    /// <summary>Exult's MENU_FONT (MAINSHP.FLX shape 9, lead 1), in the user's look.</summary>
    Lettering MenuFont() => new(VgaFont.FromShape(_mainshp, 9, 1), MenuSize, UiTheme.GoldBright, false);

    /// <summary>A text of MAINSHP.FLX, its lines (Exult's <c>TextScroller</c>: LF ends a line, a CR before it goes).</summary>
    List<string> MainshpText(int index)
    {
        var lines = new List<string>();
        if (!File.Exists(_mainshpPath))
        {
            return lines;
        }

        var text = Encoding.Latin1.GetString(new FlexFile(_mainshpPath).Get(index));
        var start = 0;
        int lf;
        while ((lf = text.IndexOf('\n', start)) >= 0)
        {
            var end = lf > start && text[lf - 1] == '\r' ? lf - 1 : lf;
            lines.Add(text[start..end]);
            start = lf + 1;
        }

        return lines;
    }

    /// <summary>PALETTES.FLX 0, index 255 the border's black (Exult <c>border255</c>).</summary>
    static byte[] DayPalette()
    {
        var pal = U7Palette.Raw6(0) ?? new byte[768];
        pal[765] = pal[766] = pal[767] = 0;
        return pal;
    }

    /// <summary>
    /// A palette of INTROPAL.DAT, Exult <c>Palette::set_loaded</c>: a double
    /// palette, the colours in the even bytes and what fades start from in
    /// the odd ones.
    /// </summary>
    static (byte[] Palette, byte[] FadeFrom) IntroPalette(int index)
    {
        var path = Path.Combine(U7Paths.StaticDir, "INTROPAL.DAT");
        var pal = new byte[768];
        var from = new byte[768];
        if (!File.Exists(path))
        {
            return (pal, from);
        }

        var entry = new FlexFile(path).Get(index);
        if (entry.Length >= 1536)
        {
            for (var i = 0; i < 768; i++)
            {
                pal[i] = entry[i * 2];
                from[i] = entry[i * 2 + 1];
            }
        }
        else
        {
            entry[..Math.Min(768, entry.Length)].CopyTo(pal);
        }

        return (pal, from);
    }
}
