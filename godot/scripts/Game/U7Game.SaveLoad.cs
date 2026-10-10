using Godot;
using U7.Data;
using U7.UI;

namespace U7.Game;

/// <summary>
/// Saving and loading: the quick save, the save/load screen (Exult
/// <c>Newfile_gump</c>, opened by Esc with no gumps to close and by the
/// paperdoll's disk) and what a save carries for it: Exult's details and a
/// picture of the world.
/// </summary>
public partial class U7Game
{
    /// <summary>The save/load screen while it is shown: the game stands still (Exult pauses the time queue).</summary>
    SaveLoadView? _saveLoad;
    /// <summary>Exult SAVEINFO.DAT's save count in GAMEDAT: the loaded game's, read when first needed.</summary>
    int? _saveCount;
    /// <summary>The picture is three times Exult's mini screenshot (96×60).</summary>
    const int ShotWidth = 288, ShotHeight = 180;

    /// <summary>
    /// Exult <c>Game_window::write</c> into the slot (F5, the screen, the
    /// console's <c>save</c>), with <c>write_saveinfo</c>'s details: the
    /// save count one up, the party, the clocks.
    /// </summary>
    void SaveTo(string slot, string? name = null, Image? shot = null)
    {
        var count = (_saveCount ??= SaveGame.ReadCurrentInfo()?.Details.SaveCount ?? 0) + 1;
        SaveGame.Write(slot, _map, _npcs, _usecode, _combat.Spawned,
            new GwinState(_clock.Day, _clock.Hour, _clock.Minute, _combat.InCombat, _music.CurrentTrack, _music.Repeat,
                _music.EggCount, _clock.SpecialLight, _combat.Armageddon),
            CurrentInfo(count), name, shot);
        _saveCount = count;
    }

    /// <summary>Exult <c>write_saveinfo</c>'s (and <c>LoadSaveGameDetails</c>'s <c>cur_details</c>) view of the game now.</summary>
    SaveInfo CurrentInfo(int saveCount) =>
        SaveInfo.Of(DateTime.Now, _clock.Day, _clock.Hour, _clock.Minute, saveCount,
            _party.Members.Prepend(_avatar.Avatar).Distinct());

    /// <summary>
    /// Exult <c>ActionQuicksave</c>: the quick save, its picture taken first,
    /// and one time in eight the avatar's bad feeling about it.
    /// </summary>
    void QuickSave() =>
        CaptureWorld(shot =>
        {
            try
            {
                SaveTo(SaveGame.QuickSlot, shot: shot);
                _statusExtra = "game saved";
                BadFeeling(8);
            }
            catch (Exception ex)
            {
                GD.PushError("save failed: " + ex);
                _statusExtra = "save failed";
            }
        });

    /// <summary>Exult <c>got_bad_feeling</c>: one time in <paramref name="odds"/> the avatar says misc name 0x44.</summary>
    void BadFeeling(int odds)
    {
        if (Random.Shared.Next(odds) == 0)
        {
            _avatar.Avatar.Bark(TextMessages.MiscName(0x44));
        }
    }

    /// <summary>
    /// Exult <c>ActionCloseOrMenu</c> (Esc) and the disk button: the
    /// save/load screen over the game, its picture of the world taken first
    /// (Exult <c>create_mini_screenshot</c> as the screen opens).
    /// </summary>
    void OpenSaveLoad()
    {
        if (_saveLoad is not null || _newGame is not null || _endgame is not null)
        {
            return;
        }

        CaptureWorld(ShowSaveLoad);
    }

    void ShowSaveLoad(Image? shot)
    {
        if (_saveLoad is not null || !IsInsideTree())
        {
            return;
        }

        _gumps.CancelDrag();
        var layer = new CanvasLayer { Name = "SaveLoad", Layer = 25 };
        AddChild(layer);
        var view = new SaveLoadView(_shapes, () => CurrentInfo(_saveCount ??= SaveGame.ReadCurrentInfo()?.Details.SaveCount ?? 0), shot)
        {
            Name = "SaveLoadView"
        };
        view.Save = (slot, name) =>
        {
            try
            {
                SaveTo(slot, name, shot);
                // Exult Newfile_gump::save: one time in four.
                BadFeeling(4);
            }
            catch (Exception ex)
            {
                GD.PushError("save failed: " + ex);
                _statusExtra = "save failed";
            }
        };
        view.Delete = SaveGame.Delete;
        view.Load = slot => LoadFrom(slot);
        view.Quit = () => GetTree().Quit();
        view.Closed = () =>
        {
            layer.QueueFree();
            _saveLoad = null;
            _world.Frozen = false;
        };
        layer.AddChild(view);
        _saveLoad = view;
        _world.Frozen = true;
        _cursor.Shape = MouseShape.Hand;
    }

    /// <summary>
    /// The world as drawn, without the gumps, the words over it, the weather
    /// and the cursor (Exult paints only the map for it), cut to 16:10 and
    /// made <see cref="ShotWidth"/>×<see cref="ShotHeight"/>; null headless.
    /// The layers are hidden for the one frame it takes.
    /// </summary>
    void CaptureWorld(Action<Image?> done)
    {
        if (DisplayServer.GetName() == "headless")
        {
            done(null);
            return;
        }

        var hidden = new[] { "Gumps", "HUD", "Cursor", "WeatherView" }
            .Select(n => GetNodeOrNull<Node>(n))
            .Where(n => n is CanvasLayer { Visible: true } or CanvasItem { Visible: true })
            .ToList();
        foreach (var node in hidden)
        {
            SetShown(node, false);
        }

        void Grab()
        {
            RenderingServer.FramePostDraw -= Grab;
            foreach (var node in hidden)
            {
                SetShown(node, true);
            }

            var image = GetViewport().GetTexture().GetImage();
            done(image is null || image.IsEmpty() ? null : Thumbnail(image));
        }

        RenderingServer.FramePostDraw += Grab;
    }

    static void SetShown(Node node, bool shown)
    {
        if (node is CanvasLayer layer)
        {
            layer.Visible = shown;
        }
        else if (node is CanvasItem item)
        {
            item.Visible = shown;
        }
    }

    static Image Thumbnail(Image image)
    {
        var (w, h) = (image.GetWidth(), image.GetHeight());
        var cropW = Math.Min(w, h * ShotWidth / ShotHeight);
        var cropH = Math.Min(h, w * ShotHeight / ShotWidth);
        var cut = image.GetRegion(new Rect2I((w - cropW) / 2, (h - cropH) / 2, cropW, cropH));
        cut.Resize(ShotWidth, ShotHeight, Image.Interpolation.Lanczos);
        return cut;
    }
}
