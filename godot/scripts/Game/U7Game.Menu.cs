using System.IO;
using Godot;
using U7.Core;
using U7.UI;

namespace U7.Game;

/// <summary>
/// The main menu (Exult <c>Game::show_menu</c>) before a game: Journey
/// Onward, a new game, the credits, quotes and endgame once earned, Quit;
/// and the shows it starts on the endgame's screen.
/// </summary>
public partial class U7Game
{
    /// <summary>The main menu while it shows: nothing else runs.</summary>
    MainMenuView? _mainMenu;
    CanvasLayer? _menuLayer;
    /// <summary>What follows a show started from the menu (the endgame from the game quits instead).</summary>
    Action? _endgameAfter;
    CanvasLayer? _endgameLayer;
    /// <summary>Exult's <c>&lt;SAVEGAME&gt;/quotes.flg</c> and <c>endgame.flg</c>: the menu's earned entries.</summary>
    static string QuotesFlag => Path.Combine(U7Paths.SavesDir, "quotes.flg");
    static string EndgameFlag => Path.Combine(U7Paths.SavesDir, "endgame.flg");
    const int MenuMidi = 3;

    /// <summary>Exult's start (exult.cc): the intro unless the splash is skipped, then the main menu.</summary>
    void ShowIntroThenMenu() => PlayShow(view => new Intro(view, _music), ShowMainMenu);

    /// <summary>Exult <c>Game::show_menu</c> and <c>BG_Game::top_menu</c>: the menu over everything, to its music.</summary>
    void ShowMainMenu()
    {
        CloseMainMenu();
        var layer = new CanvasLayer { Name = "MainMenu", Layer = 25 };
        AddChild(layer);
        var menu = new MainMenuView(File.Exists(QuotesFlag), File.Exists(EndgameFlag)) { Name = "MainMenuView" };
        menu.Chosen = MenuChosen;
        layer.AddChild(menu);
        _mainMenu = menu;
        _menuLayer = layer;
        _cursor.HideCursor = false;
        _cursor.Shape = MouseShape.Hand;
        _music.StartFile(Path.Combine(U7Paths.AssetsDir, "intro_endgame", "music_mt32", $"{MenuMidi:D4}_INTRORDM.MID"), -1, true);
    }

    void CloseMainMenu()
    {
        _menuLayer?.QueueFree();
        _menuLayer = null;
        _mainMenu = null;
    }

    void MenuChosen(MenuChoice choice)
    {
        switch (choice)
        {
            case MenuChoice.Journey:
                // Exult init_gamedat(false): GAMEDAT, the game last saved or restored, else "journey failed".
                if (SaveGame.JourneySlot() is { } slot)
                {
                    _music.Stop();
                    CloseMainMenu();
                    LoadFrom(slot);
                }
                else
                {
                    _mainMenu?.JourneyFailed();
                }

                break;
            case MenuChoice.NewGame:
                _music.Stop();
                CloseMainMenu();
                ShowNewGame();
                break;
            case MenuChoice.Intro:
                // Exult: the intro, then the menu again.
                _music.Stop();
                CloseMainMenu();
                PlayShow(view => new Intro(view, _music), ShowMainMenu);
                break;
            case MenuChoice.Credits:
            case MenuChoice.Quotes:
                CloseMainMenu();
                PlayShow(view => Endgame.Scroll(view, _music, choice == MenuChoice.Quotes), ShowMainMenu);
                break;
            case MenuChoice.Endgame:
                // Exult end_game(true, false): the movies alone, then the menu again.
                CloseMainMenu();
                PlayShow(view => new Endgame(view, _music, _clock.TotalHours, success: true, credits: false), ShowMainMenu);
                break;
            case MenuChoice.Quit:
                _music.Stop();
                GetTree().Quit();
                break;
        }
    }

    /// <summary>A show on the endgame's screen, without the mouse cursor (Exult paints none); then <paramref name="after"/>, or the game ends.</summary>
    void PlayShow(Func<EndgameView, Show> make, Action? after)
    {
        var layer = new CanvasLayer { Name = "Endgame", Layer = 25 };
        AddChild(layer);
        var view = new EndgameView { Name = "EndgameView" };
        layer.AddChild(view);
        _cursor.HideCursor = true;
        _endgameLayer = layer;
        _endgameAfter = after;
        _endgame = make(view);
        _endgame.Log = _agentDir is null ? null : AgentLog;
        _endgame.Speech = _speech;
    }

    /// <summary>
    /// The show is over: credits watched to their end earn View Quotes
    /// (Exult <c>show_credits</c>); from the menu, back to it; from the game,
    /// the game ends as the original's did (the console's game stays).
    /// </summary>
    void ShowDone(Show show)
    {
        if (show.WatchedThrough)
        {
            WriteFlag(QuotesFlag);
        }

        if (_endgameAfter is { } after)
        {
            _endgameAfter = null;
            _endgameLayer?.QueueFree();
            _endgameLayer = null;
            _endgame = null;
            _cursor.HideCursor = false;
            after();
        }
        else if (_agentDir is null)
        {
            GetTree().Quit();
        }
    }

    static void WriteFlag(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, []);
        }
        catch (IOException ex)
        {
            GD.PushError($"{path}: {ex.Message}");
        }
    }
}
