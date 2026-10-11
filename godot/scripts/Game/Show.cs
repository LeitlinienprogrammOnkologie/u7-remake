using System.IO;
using System.Text;
using Godot;
using U7.Audio;
using U7.Core;
using U7.Data;
using U7.UI;

namespace U7.Game;

/// <summary>
/// One of Exult's shows on the endgame's screen (<see cref="EndgameView"/>):
/// the endgame, the credits and quotes, the intro. Exult's code step by step,
/// as an iterator of waits in milliseconds on the show's own clock, with what
/// they share: the fades, INTROPAL's palettes, MAINSHP.FLX's texts and the
/// user's lettering.
/// </summary>
public abstract class Show
{
    protected const int FadeTime = 30; // Exult c_fade_in_time, c_fade_out_time.
    /// <summary>MedievalSharp's size, in picture pixels, for Exult's 14-pixel fonts and its 9-pixel menu font.</summary>
    protected const float LineSize = 12.5f, MenuSize = 10f;

    /// <summary>
    /// The user's look for one of Exult's fonts: MedievalSharp at
    /// <see cref="Size"/> picture pixels in its place, the lines as far apart
    /// as the font's (<see cref="Layout"/>).
    /// </summary>
    protected sealed record Lettering(VgaFont Layout, float Size, Color Color, bool Outline)
    {
        public int TextHeight => Layout.TextHeight;

        public EndgameView.Words Words(string text, float x, float y, int align) =>
            new(text, x, y, Size, align, Color, Outline);
    }

    protected readonly EndgameView _view;
    protected readonly MusicPlayer _music;
    protected readonly string _mainshpPath = Path.Combine(U7Paths.StaticDir, "MAINSHP.FLX");
    protected readonly VgaShapeFile _mainshp;
    protected IEnumerator<double> _steps = Enumerable.Empty<double>().GetEnumerator();
    double _clock;
    protected double _due;
    bool _skip;
    /// <summary>The credits' step under way: when it began and how long it lasts (0: no glide).</summary>
    protected double _riseFrom;
    protected double _riseMs;

    public bool Done { get; private set; }

    /// <summary>The credits roll: any key but Shift, or a click, ends them (Exult <c>TextScroller::run</c>).</summary>
    public bool InCredits { get; protected set; }

    /// <summary>The credits or quotes rolled to their end, not cut short (Exult <c>TextScroller::run</c>'s true).</summary>
    public bool WatchedThrough { get; protected set; }

    /// <summary>What shows, for the agent console.</summary>
    public Action<string>? Log { get; set; }

    /// <summary>Exult's speech for the show's voices (null: none plays).</summary>
    public SpeechPlayer? Speech { get; set; }

    /// <summary>Exult's <c>subtitles</c>: the words show without speech, or with it when the subtitles are on.</summary>
    protected bool Subtitles => Speech is not { Enabled: true } || Speech.WithSubtitles;

    /// <summary>Exult <c>Game::waitforspeech</c>: a voice from the game still speaking is heard out first.</summary>
    protected IEnumerable<double> WaitForSpeech()
    {
        while (Speech is { IsPlaying: true })
        {
            yield return 50;
        }
    }

    /// <summary>The console's name for the show (its log lines start so).</summary>
    protected virtual string LogName => "ENDGAME";

    protected Show(EndgameView view, MusicPlayer music)
    {
        _view = view;
        _music = music;
        _mainshp = new VgaShapeFile(_mainshpPath);
    }

    /// <summary>A key or click as Exult's waits in the show read it.</summary>
    public abstract void Input(InputEvent @event);

    /// <summary>The sequence's own time, at the step running (ms).</summary>
    protected double Now => _due;

    /// <summary>A key or click: Exult's <c>wait_delay</c> saw it.</summary>
    public virtual void Skip() => _skip = true;

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
                Log?.Invoke(LogName + " over");
                return;
            }

            _due += Math.Max(0, _steps.Current);
        }

        _view.Rise = _riseMs > 0 ? (float)Math.Clamp((_clock - _riseFrom) / _riseMs, 0, 1) : 0;
    }

    protected bool TakeSkip()
    {
        var skip = _skip;
        _skip = false;
        return skip;
    }

    /// <summary>Exult <c>Palette::fade_in</c> / <c>fade_out</c>: a step every 20 ms, from black or to it.</summary>
    protected IEnumerable<double> Fade(int cycles, bool fadeIn, byte[]? palette)
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

    /// <summary>Exult's MENU_FONT (MAINSHP.FLX shape 9, lead 1), in the user's look.</summary>
    protected Lettering MenuFont() => new(VgaFont.FromShape(_mainshp, 9, 1), MenuSize, UiTheme.GoldBright, false);

    /// <summary>A text of MAINSHP.FLX, its lines (Exult's <c>TextScroller</c>: LF ends a line, a CR before it goes).</summary>
    protected List<string> MainshpText(int index)
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
    protected static byte[] DayPalette()
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
    protected static (byte[] Palette, byte[] FadeFrom) IntroPalette(int index)
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
