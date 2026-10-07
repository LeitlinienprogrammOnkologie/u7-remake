using Godot;
using U7.Actors;
using U7.Audio;
using U7.Core;
using U7.Data;
using U7.Gumps;
using U7.Rendering;
using U7.Usecode;
using U7.UI;
using U7.World;

namespace U7.Game;

/// <summary>
/// Bootstraps the Black Gate world: load STATIC map data, extracted shapes,
/// and run a walker at the Trinsic start tile. Double-click (or E) runs usecode.
/// C toggles combat.
/// </summary>
public partial class U7Game : Node2D
{
    WorldView _world = null!;
    GumpView _gumpView = null!;
    GumpManager _gumps = null!;
    Camera2D _camera = null!;
    Label _hud = null!;
    Label _debug = null!;
    ConversationPanel _conversation = null!;
    AvatarController _avatar = null!;
    GameMap _map = null!;
    ShapeCatalog _catalog = null!;
    ShapeCache _shapes = null!;
    UsecodeMachine? _usecode;
    GameClock _clock = null!;
    ScheduleRunner _schedules = null!;
    EggHatcher _eggs = null!;
    MusicPlayer _music = null!;
    PartyManager _party = null!;
    CombatEngine _combat = null!;
    List<U7Object?> _npcs = new();
    float _zoom = 4f;
    double _quakeTimer;
    Vector2 _quakeOffset;
    bool _ready;
    bool _debugOn;
    bool _suppressWalk;
    /// <summary>The left button went down while a book page was shown.</summary>
    bool _bookPress;
    /// <summary>The left button went down on open ground: holding it walks the avatar.</summary>
    bool _walkPress;
    ulong _walkPressMsec;
    /// <summary>A walking key is held.</summary>
    bool _keyWalking;
    int _lastSchunk = -1;
    string _statusExtra = "";

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.Nearest;
        try
        {
            U7Paths.Initialize();
            _catalog = new ShapeCatalog();
            _shapes = new ShapeCache(_catalog);
            _map = new GameMap(_catalog);

            var avatar = new U7Object
            {
                Tx = U7Constants.StartTileX,
                Ty = U7Constants.StartTileY,
                Tz = U7Constants.StartLift,
                Shape = U7Constants.AvatarShape,
                Frame = 16,
                Kind = ObjectKind.Actor,
                DimX = _catalog[U7Constants.AvatarShape].DimX,
                DimY = _catalog[U7Constants.AvatarShape].DimY,
                DimZ = _catalog[U7Constants.AvatarShape].DimZ,
                Solid = _catalog[U7Constants.AvatarShape].Solid,
                IsActor = true
            };
            _map.AddObject(avatar);
            _avatar = new AvatarController(avatar, _map);

            try
            {
                _npcs = NpcDat.Load(_map, avatar);
                if (U7Constants.DebugStartOverride)
                {
                    _map.MoveObject(avatar, U7Constants.StartTileX, U7Constants.StartTileY, 0);
                }
            }
            catch (Exception ex)
            {
                GD.PushError("NPC load failed: " + ex);
                _npcs = [avatar];
            }

            _clock = new GameClock();
            var gwin = SaveGame.ReadGwin();
            if (gwin is { } g0)
            {
                _clock.Set(g0.Day, g0.Hour, g0.Minute);
            }

            var schedTable = ScheduleTable.Load();
            _schedules = new ScheduleRunner(_map, avatar, _npcs, schedTable, _clock, restore: U7Paths.GameDatOverride is not null);
            var usecodeDat = UsecodeDat.Read();
            _party = new PartyManager(_map, avatar, _npcs) { Schedules = _schedules };
            _party.LinkParty(usecodeDat?.Party);
            _schedules.Party = _party;
            _schedules.AvatarMoving = () => _avatar.IsPlayerMoving;
            _combat = new CombatEngine(_map, avatar, _catalog);
            _music = new MusicPlayer();
            _eggs = new EggHatcher(_map, _clock) { Combat = _combat, Music = _music, Party = _party };
            _avatar.Moved = (actor, fromTx, fromTy) =>
            {
                _eggs.Activate(actor, fromTx, fromTy);
                _party.AvatarStepped(fromTx, fromTy);
            };
            _combat.Schedules = _schedules;
            _schedules.Combat = _combat;
            _schedules.AvatarMoved = _avatar.Moved;
            _schedules.AvatarBusy = () => _avatar.IsPlayerMoving;
            _combat.Party = _party;
            _combat.Music = _music;
            _combat.AdoptMonsters(NpcDat.LoadMonsters(_map));
            if (gwin is { } g1)
            {
                if (g1.InCombat)
                {
                    _combat.SetInCombat(true);
                }

                if (g1.Track >= 0)
                {
                    _music.Start(g1.Track, g1.Repeat);
                }
            }

            _world = new WorldView
            {
                Name = "WorldView",
                Map = _map,
                Catalog = _catalog,
                Shapes = _shapes,
                Avatar = avatar,
                TextureFilter = TextureFilterEnum.Nearest
            };
            AddChild(_world);

            _gumps = new GumpManager(_map, avatar);
            _gumps.ActivateUsecode = obj => RunUsecode(obj);
            _gumps.DroppedInWorld = obj => _eggs.ActivateSomethingOn(obj);
            _gumps.ToggleCombat = () =>
            {
                _combat.ToggleCombat();
                if (_combat.InCombat)
                {
                    _avatar.ClearPath();
                }

                _statusExtra = _combat.LastMessage;
            };
            _gumps.IsCombatOn = () => _combat.InCombat;

            var gumpLayer = new CanvasLayer { Name = "Gumps", Layer = 10 };
            AddChild(gumpLayer);
            _gumpView = new GumpView
            {
                Name = "GumpView",
                Gumps = _gumps,
                Shapes = _shapes,
                Catalog = _catalog,
                Map = _map,
                Zoom = _zoom,
                TextureFilter = TextureFilterEnum.Nearest
            };
            gumpLayer.AddChild(_gumpView);

            _camera = new Camera2D
            {
                Name = "Camera",
                Zoom = new Vector2(_zoom, _zoom),
                PositionSmoothingEnabled = false,
                IgnoreRotation = true
            };
            AddChild(_camera);

            var layer = new CanvasLayer { Name = "HUD", Layer = 20 };
            AddChild(layer);
            _hud = new Label
            {
                Name = "Status",
                Position = new Vector2(12, 8),
                TextureFilter = TextureFilterEnum.Nearest
            };
            _hud.AddThemeColorOverride("font_color", new Color(0.95f, 0.9f, 0.7f));
            layer.AddChild(_hud);

            layer.AddChild(new BarkOverlay { Name = "Barks", World = _world });
            _conversation = new ConversationPanel { Name = "Conversation", Shapes = _shapes };
            layer.AddChild(_conversation);

            _debug = new Label
            {
                Name = "UsecodeDebug",
                Position = new Vector2(12, 90),
                Visible = false,
                TextureFilter = TextureFilterEnum.Nearest
            };
            _debug.AddThemeColorOverride("font_color", new Color(0.55f, 0.95f, 0.7f));
            layer.AddChild(_debug);

            var usecodeFile = UsecodeFile.Load();
            GD.Print($"USECODE loaded: {usecodeFile.Count} functions.");
            _usecode = new UsecodeMachine(usecodeFile, _map, avatar);
            foreach (var (tnum, hours) in usecodeDat?.Timers ?? [])
            {
                _usecode.Timers[tnum] = hours;
            }

            // Exult Game_window::read: until the first scene (global flag 0x3b)
            // has played, the avatar is invisible and under usecode control.
            if (_usecode.GFlags[UsecodeMachine.DidFirstSceneFlag] == 0)
            {
                avatar.SetFlag(ObjFlag.DontMove);
            }
            else
            {
                avatar.ClearFlag(ObjFlag.DontMove);
            }

            _usecode.Gumps = _gumps;
            _usecode.Npcs = _npcs;
            _usecode.Clock = _clock;
            _usecode.Schedules = _schedules;
            _usecode.Party = _party;
            _usecode.Combat = _combat;
            _usecode.Music = _music;
            _usecode.Eggs = _eggs;
            _map.ScriptSaver = obj => _usecode.SaveScripts(obj);
            foreach (var (obj, blob) in _map.PendingScripts)
            {
                _usecode.RestoreScript(obj, blob);
            }

            if (_map.PendingScripts.Count > 0)
            {
                GD.Print($"usecode scripts restored: {_map.PendingScripts.Count}");
            }

            _map.PendingScripts.Clear();
            _usecode.AvatarMovedByScript = actor =>
            {
                _eggs.Activate(actor, actor.Tx, actor.Ty);
                _party.AvatarStepped(actor.Tx, actor.Ty);
            };
            _combat.AvatarDied = () =>
            {
                _gumps.CloseAll(true);
                _avatar.ClearPath();
                GD.Print("avatar died: running death usecode 0x60E");
                _usecode.Call(0x60E, avatar, UsecodeEvent.Weapon);
            };
            _eggs.Usecode = _usecode;
            // Walkers open doors through the doors' own usecode (Exult Path_walking_actor_action::open_door).
            PathWalk.ActivateDoor = door =>
            {
                if (_usecode.InUsecode || _usecode.WaitingForChoice)
                {
                    return false;
                }

                _usecode.Call(UsecodeMachine.GetItemFun(door), door, UsecodeEvent.DoubleClick);
                return true;
            };
            PathWalk.IsSentient = _combat.IsSentient;
            _schedules.ProximityUsecode = npc =>
            {
                // Exult try_proximity_usecode: dont_halt, usecode2 <fun> npc_proximity, as a script.
                var fun = npc.GetUsecode() >= 0 ? npc.GetUsecode() : UsecodeMachine.GetShapeFun(npc.Shape);
                var code = UsecodeValue.FromArray(4);
                code.PutElem(0, UsecodeValue.FromInt(0x23));
                code.PutElem(1, UsecodeValue.FromInt(0x80));
                code.PutElem(2, UsecodeValue.FromInt(fun));
                code.PutElem(3, UsecodeValue.FromInt((int)UsecodeEvent.NpcProximity));
                _usecode.StartScript(npc, code, 0);
            };
            _schedules.Script = (npc, ops) =>
            {
                var code = UsecodeValue.FromArray(ops.Length);
                for (var i = 0; i < ops.Length; i++)
                {
                    code.PutElem(i, ops[i] is string text ? UsecodeValue.FromString(text) : UsecodeValue.FromInt((int)ops[i]));
                }

                _usecode.StartScript(npc, code, 0);
            };
            _schedules.Fight = _combat.Fight;
            _schedules.ReadyBestWeapon = _combat.ReadyBestWeapon;
            _schedules.Weapons = _combat.Weapons;
            _schedules.Activate = obj =>
            {
                if (_usecode.InUsecode || _usecode.WaitingForChoice)
                {
                    return false;
                }

                RunUsecode(obj); // Exult Game_object::activate, as a double-click.
                return true;
            };
            _schedules.CallUsecode = (fun, item) =>
            {
                if (_usecode.InUsecode || _usecode.WaitingForChoice)
                {
                    return false;
                }

                _usecode.Call(fun, item, UsecodeEvent.DoubleClick);
                _conversation.Refresh();
                return true;
            };
            _schedules.IsInUsecode = () => _usecode.InUsecode;
            _schedules.Say = _usecode.Bark;
            SitAction.Say = _usecode.Bark;
            _schedules.CanSpeak = _combat.CanSpeak;
            _schedules.InUsecodeControl = _usecode.InUsecodeControl;
            _conversation.Machine = _usecode;
            _gumpView.ShownBook = () => _usecode is { Wait: UsecodeWait.BookPage } vm ? vm.Book : null;
            _usecode.Say += _ => _conversation.Refresh();
            _usecode.AnswersChanged += _conversation.Refresh;
            _usecode.FacesChanged += _conversation.Refresh;

            GD.Print(
                $"gumps: chest={_catalog[800].GumpShape} crate={_catalog[804].GumpShape} " +
                $"bag={_catalog[802].GumpShape} backpack={_catalog[801].GumpShape} " +
                $"barrel={_catalog[819].GumpShape} avatar={_catalog[U7Constants.AvatarShape].GumpShape}");
            var filled = 0;
            for (var cy = 0; cy < U7Constants.NumChunks; cy++)
            {
                for (var cx = 0; cx < U7Constants.NumChunks; cx++)
                {
                    foreach (var o in _map.ObjectsInChunk(cx, cy))
                    {
                        if (o.Contents.Count > 0)
                        {
                            filled++;
                        }
                    }
                }
            }

            GD.Print($"IREG containers with contents: {filled}");
            var near = 0;
            foreach (var n in _npcs)
            {
                if (n is not { Unused: false, NpcNum: > 0 })
                {
                    continue;
                }

                if (new TileCoord(n.Tx, n.Ty, n.Tz).Distance2d(
                        new TileCoord(avatar.Tx, avatar.Ty, avatar.Tz)) <= 40)
                {
                    near++;
                    GD.Print($"nearby NPC {n.NpcNum} '{n.NpcName}' shape {n.Shape} at {n.Tx},{n.Ty} sched {n.ScheduleType}");
                }
            }

            GD.Print($"NPCs within 40 tiles of avatar: {near}");

            var eggNear = 0;
            foreach (var e in _map.EggsNear(avatar.Tx, avatar.Ty, 80))
            {
                eggNear++;
                if (eggNear <= 12)
                {
                    GD.Print(
                        $"nearby egg {EggType.Name(e.EggType)} crit={e.EggCriteria} " +
                        $"dist={e.EggDistance} p={e.EggProbability} at {e.Tx},{e.Ty} " +
                        $"q={e.Quality} d1=0x{e.EggData1:X4} d2=0x{e.EggData2:X4}");
                }
            }

            GD.Print($"eggs within 80 tiles of avatar: {eggNear} (map total {_map.Eggs.Count})");
            _eggs.Activate(avatar, -1, -1);
            AgentInit();

            _ready = true;
            GD.Print("Britannia loaded.");
        }
        catch (Exception ex)
        {
            GD.PushError(ex.ToString());
            var err = new Label
            {
                Text = "Failed to load Ultima VII data:\n" + ex.Message,
                Position = new Vector2(24, 24)
            };
            AddChild(err);
        }
    }


    public override void _ExitTree()
    {
        _music?.Dispose();
        BgIntrinsics.WriteStubReport();
    }

    public override void _Process(double delta)
    {
        // Also skip the last frame of a scene being replaced by a load.
        if (!_ready || !IsInsideTree())
        {
            return;
        }

        var avPos = _avatar.Avatar;
        var schunk = (avPos.Ty / U7Constants.TilesPerSuperchunk) * 12 + avPos.Tx / U7Constants.TilesPerSuperchunk;
        if (schunk != _lastSchunk)
        {
            _lastSchunk = schunk;
            var gone = _map.CacheOut(avPos.Tx, avPos.Ty);
            if (gone > 0)
            {
                GD.Print($"cache out: {gone} temporary objects");
            }
        }

        Vector2I? click = null;
        var inUsecode = _usecode is { InUsecode: true } or { WaitingForChoice: true };
        var virt = _gumpView.MouseVirtual();
        var overGump = _gumps.FindGump(virt.X, virt.Y, _gumpView) is not null;
        var gumpBusy = _gumps.GumpMode || _gumps.IsDragging || overGump || _gumps.Drag is not null;
        if (_gumps.Drag is not null)
        {
            _gumps.OnMouseMove(_gumpView, virt.X, virt.Y);
        }

        var dontMove = ObjFlag.DontMoveMode(_avatar.Avatar);
        var canWalk = !inUsecode && !gumpBusy && !_avatar.Avatar.IsDead && !dontMove;
        var viewTiles = GetViewport().GetVisibleRect().Size / _zoom / U7Constants.TileSize;
        Pathfinder.ScreenTilesWide = Math.Max(1, (int)viewTiles.X);
        _schedules.ScreenTiles = (Math.Max(1, (int)viewTiles.X), Math.Max(1, (int)viewTiles.Y));
        if (canWalk && !_suppressWalk && Input.IsMouseButtonPressed(MouseButton.Left))
        {
            var world = _camera.GetGlobalMousePosition();
            var tile = WorldView.WorldToTile(world, _avatar.Avatar.Tz);
            if (Input.IsKeyPressed(Key.Shift))
            {
                if (tile.Tx != _avatar.Avatar.Tx || tile.Ty != _avatar.Avatar.Ty)
                {
                    var fromTx = _avatar.Avatar.Tx;
                    var fromTy = _avatar.Avatar.Ty;
                    _map.MoveObject(_avatar.Avatar, tile.Tx, tile.Ty, _avatar.Avatar.Tz);
                    _party.FollowTeleport();
                    _eggs.Activate(_avatar.Avatar, fromTx, fromTy);
                }
            }
            else if (_walkPress)
            {
                // Exult start_actor: holding the button steers toward the cursor.
                _avatar.Steer(world, MouseWalkSpeed(world));
            }
        }

        KeyboardWalk(canWalk);
        AgentUpdate(delta, ref click);
        if (click is { } c)
        {
            _avatar.PathTo(new TileCoord(c.X, c.Y, _avatar.Avatar.Tz), WalkSpeed.Keyboard(false, false, false));
        }

        if (!Input.IsMouseButtonPressed(MouseButton.Left))
        {
            _suppressWalk = false;
        }

        var frozen = inUsecode || gumpBusy || _avatar.Avatar.IsDead;
        if (canWalk)
        {
            _avatar.Update(delta, _schedules.AvatarActing,
                _usecode?.InUsecodeControl(_avatar.Avatar) ?? false);
        }

        _clock.Update(delta);
        var targetModulate = _usecode is { FadedOut: true } ? Colors.Black : _clock.WorldModulate;
        _world.Modulate = _world.Modulate.Lerp(targetModulate, (float)Math.Min(1, delta * 2.5));
        _usecode?.TickScripts(delta);
        if (_usecode is { RestartRequested: true })
        {
            _usecode.RestartRequested = false;
            U7Paths.GameDatOverride = null;
            GD.Print("restart requested by usecode");
            GetTree().ReloadCurrentScene();
            return;
        }

        _schedules.Update(delta, frozen);
        _combat.Update(delta, frozen);

        var camera = _camera;
        var hud = _hud;
        var catalog = _catalog;
        var map = _map;
        if (camera is null || hud is null || catalog is null || map is null)
        {
            return;
        }

        var av = _avatar.Avatar;
        var cam = WorldView.AvatarCameraPoint(av);
        camera.GlobalPosition = new Vector2(Mathf.Round(cam.X), Mathf.Round(cam.Y)) + QuakeOffset(delta);

        var under = WorldView.WorldToTile(camera.GetGlobalMousePosition(), av.Tz);
        var picked = _world.PickObject(camera.GetGlobalMousePosition());
        var name = picked is not null
            ? (!string.IsNullOrEmpty(picked.NpcName)
                ? picked.NpcName
                : (string.IsNullOrEmpty(catalog[picked.Shape].Name)
                    ? $"shape {picked.Shape}"
                    : catalog[picked.Shape].Name))
            : catalog[map.GetFlat(under.Tx, under.Ty).Shape].Name;
        if (string.IsNullOrEmpty(name))
        {
            name = picked is null ? $"shape {map.GetFlat(under.Tx, under.Ty).Shape}" : $"shape {picked.Shape}";
        }

        var extra = _usecode is { HudMessage.Length: > 0 } ? "" : _statusExtra;
        hud.Text =
            $"Ultima VII  {_clock.HudText()}  tile {av.Tx},{av.Ty}  lift {av.Tz}  zoom {_zoom:0.#}×\n" +
            $"WASD/arrows walk · I inventory · C combat · F4 invincible · [ ] hour · click walk · double-click / E · F2 debug · F3 arena · PgUp/PgDn lift · M music · F5/F9 save/load · F6 die · Home Trinsic\n" +
            $"hp {av.GetProp(ActorProp.Health)}  {(_combat.InCombat ? "combat" : "peace")}{_party.HudText()}" +
            (av.IsDead ? "  dead" : "") +
            (_combat.AvatarInvincible ? "  invincible" : "") +
            $"  cursor {under.Tx},{under.Ty}  {name}" +
            (picked is null ? "" : $"  frame {picked.Frame}  flags 0x{picked.Flags:X}" +
                (picked.NpcNum >= 0 ? $"  npc {picked.NpcNum} {picked.NpcName}" : $"  gump {_catalog[picked.Shape].GumpShape}")) +
            (_usecode is null ? "" : $"\nUSECODE {_usecode.File.Count} functions") +
            (_eggs.JukeboxTrack >= 0 ? $"\njukebox track {_eggs.JukeboxTrack}" : "") +
            (string.IsNullOrEmpty(_eggs.LastMessage) ? "" : "\n" + _eggs.LastMessage) +
            (string.IsNullOrEmpty(_combat.LastMessage) ? "" : "\n" + _combat.LastMessage) +
            (string.IsNullOrEmpty(extra) ? "" : "\n" + extra);

        if (_debugOn && _usecode is not null)
        {
            _debug.Text = _usecode.DebugText() + "\n" + _gumps.DebugText();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_ready)
        {
            return;
        }

        if (@event is InputEventMouseButton mb)
        {
            if (HandleBookClick(mb))
            {
                return;
            }

            var virt = _gumpView.MouseVirtual();
            if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed)
            {
                _zoom = Mathf.Clamp(_zoom + 0.5f, 1f, 8f);
                _camera.Zoom = new Vector2(_zoom, _zoom);
                _gumpView.Zoom = _zoom;
            }
            else if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed)
            {
                _zoom = Mathf.Clamp(_zoom - 0.5f, 1f, 8f);
                _camera.Zoom = new Vector2(_zoom, _zoom);
                _gumpView.Zoom = _zoom;
            }
            else if (mb.Pressed && mb.ButtonIndex is MouseButton.Left or MouseButton.Right)
            {
                if (mb.ButtonIndex == MouseButton.Left)
                {
                    _walkPress = false;
                }

                if (HandleClickOnItem(virt.X, virt.Y, mb.ButtonIndex == MouseButton.Right))
                {
                    _suppressWalk = true;
                    GetViewport().SetInputAsHandled();
                    return;
                }

                if (ObjFlag.DontMoveMode(_avatar.Avatar))
                {
                    // Exult: no double-clicks, drags or walking while usecode runs the avatar.
                    _suppressWalk = true;
                    GetViewport().SetInputAsHandled();
                    return;
                }

                if (_gumps.OnMouseDown(_gumpView, virt.X, virt.Y,
                        mb.ButtonIndex == MouseButton.Right, mb.DoubleClick))
                {
                    _suppressWalk = true;
                    GetViewport().SetInputAsHandled();
                    return;
                }

                if (mb.ButtonIndex == MouseButton.Left && mb.DoubleClick)
                {
                    _suppressWalk = true;
                    ActivateUnderMouse();
                    GetViewport().SetInputAsHandled();
                    return;
                }

                if (mb.ButtonIndex == MouseButton.Left && !mb.DoubleClick)
                {
                    var worldObj = _world.PickObject(_camera.GetGlobalMousePosition());
                    if (worldObj is not null)
                    {
                        _gumps.OnWorldMouseDown(_gumpView, worldObj, virt.X, virt.Y);
                        _suppressWalk = true;
                        GetViewport().SetInputAsHandled();
                    }
                    else
                    {
                        _walkPress = true;
                        _walkPressMsec = Time.GetTicksMsec();
                    }
                }
            }
            else if (!mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                if (_gumps.Drag is not null)
                {
                    var tile = WorldView.WorldToTile(_camera.GetGlobalMousePosition(), _avatar.Avatar.Tz);
                    _gumps.OnMouseUp(_gumpView, virt.X, virt.Y, tile.Tx, tile.Ty, _avatar.Avatar.Tz);
                    _suppressWalk = true;
                    GetViewport().SetInputAsHandled();
                }
                else if (_walkPress)
                {
                    EndWalkPress();
                }
            }
        }
        else if (@event is InputEventMouseMotion)
        {
            if (_gumps.Drag is not null)
            {
                var virt = _gumpView.MouseVirtual();
                _gumps.OnMouseMove(_gumpView, virt.X, virt.Y);
                GetViewport().SetInputAsHandled();
            }
        }
        else if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            if (_usecode is { Wait: UsecodeWait.BookPage } reader &&
                key.Keycode is Key.Escape or Key.Space or Key.Enter or Key.KpEnter)
            {
                // Exult Get_click: Esc stops reading. Space and Enter turn the page, as in conversations.
                reader.TurnBookPage(stop: key.Keycode == Key.Escape);
                _conversation.Refresh();
                GetViewport().SetInputAsHandled();
                return;
            }

            // Exult restricts key actions in dont_move mode; debug, save, music and zoom stay.
            if (ObjFlag.DontMoveMode(_avatar.Avatar) &&
                key.Keycode is Key.Home or Key.E or Key.I or Key.C or Key.F3 or Key.F6 or Key.Pageup or Key.Pagedown)
            {
                return;
            }

            switch (key.Keycode)
            {
                case Key.Home:
                {
                    var fromTx = _avatar.Avatar.Tx;
                    var fromTy = _avatar.Avatar.Ty;
                    _map.MoveObject(_avatar.Avatar, U7Constants.StartTileX, U7Constants.StartTileY, 0);
                    _party.FollowTeleport();
                    _eggs.Activate(_avatar.Avatar, fromTx, fromTy);
                    break;
                }
                case Key.E:
                    ActivateUnderMouse();
                    break;
                case Key.I:
                    _gumps.ShowInventory();
                    break;
                case Key.C:
                    _combat.ToggleCombat();
                    if (_combat.InCombat)
                    {
                        _avatar.ClearPath();
                    }

                    _statusExtra = _combat.LastMessage;
                    break;
                case Key.F4:
                    _combat.ToggleInvincible();
                    _statusExtra = _combat.LastMessage;
                    break;
                case Key.Bracketleft:
                    _clock.SkipHours(-1);
                    break;
                case Key.Bracketright:
                    _clock.SkipHours(1);
                    break;
                case Key.F2:
                    _debugOn = !_debugOn;
                    _debug.Visible = _debugOn;
                    break;
                case Key.F3:
                    _avatar.ClearPath();
                    _combat.SpawnArena();
                    _statusExtra = _combat.LastMessage;
                    break;
                case Key.F6:
                    // Debug: lethal hit on the avatar through the normal damage path.
                    // Usecode 0x60E restarts the game unless global flag 0x57 is set;
                    // with it set you wake up in the Fellowship shelter in Paws
                    // (Feridwyn and Brita). Set it unless Shift is held.
                    if (!key.ShiftPressed && _usecode is { } uc && 0x57 < uc.GFlags.Length)
                    {
                        uc.GFlags[0x57] = 1;
                    }

                    _combat.ReduceHealth(_avatar.Avatar, 1000, null, 0);
                    _statusExtra = _combat.LastMessage;
                    break;
                case Key.F5:
                    try
                    {
                        SaveGame.Write(SaveGame.QuickSlot, _map, _npcs, _usecode, _clock, _combat.InCombat, _music, _combat.Spawned);
                        _statusExtra = "game saved";
                    }
                    catch (Exception ex)
                    {
                        GD.PushError("save failed: " + ex);
                        _statusExtra = "save failed";
                    }

                    break;
                case Key.F9:
                    if (SaveGame.Exists(SaveGame.QuickSlot))
                    {
                        U7Paths.GameDatOverride = SaveGame.SlotDir(SaveGame.QuickSlot);
                        GD.Print("loading " + U7Paths.GameDatOverride);
                        GetTree().ReloadCurrentScene();
                    }
                    else
                    {
                        _statusExtra = "no saved game";
                    }

                    break;
                case Key.M:
                    _music.Enabled = !_music.Enabled;
                    if (!_music.Enabled)
                    {
                        _music.Stop();
                    }

                    _statusExtra = _music.Enabled ? "music on" : "music off";
                    break;
                case Key.Pageup:
                    ShiftLift(1);
                    break;
                case Key.Pagedown:
                    ShiftLift(-1);
                    break;
            }
        }
    }

    /// <summary>Debug: move the avatar one lift level up or down in place, then re-check eggs there.</summary>
    void ShiftLift(int delta)
    {
        var av = _avatar.Avatar;
        var tz = Math.Clamp(av.Tz + delta, 0, 15);
        if (tz == av.Tz)
        {
            return;
        }

        _avatar.ClearPath();
        _map.MoveObject(av, av.Tx, av.Ty, tz);
        _statusExtra = $"lift {tz}";
        _eggs.Activate(av, av.Tx, av.Ty);
    }

    /// <summary>
    /// Letting go of the walking button. Exult walks with the right button:
    /// holding it steers and letting go stops, and a double right-click finds
    /// a path to the spot. Here the left button does both: a quick click
    /// finds a path (Exult <c>start_actor_along_path</c>), letting go after
    /// holding stops (<c>stop_actor</c>).
    /// </summary>
    void EndWalkPress()
    {
        _walkPress = false;
        var canWalk = _usecode is not ({ InUsecode: true } or { WaitingForChoice: true }) && !_gumps.GumpMode &&
                      !_avatar.Avatar.IsDead && !ObjFlag.DontMoveMode(_avatar.Avatar);
        if (!canWalk || Input.IsKeyPressed(Key.Shift))
        {
            return;
        }

        if (Time.GetTicksMsec() - _walkPressMsec < QuickClickMsec)
        {
            var world = _camera.GetGlobalMousePosition();
            _avatar.PathTo(WorldView.WorldToTile(world, _avatar.Avatar.Tz), MouseWalkSpeed(world));
        }
        else
        {
            _avatar.Stop();
        }
    }

    const ulong QuickClickMsec = 300;

    /// <summary>Exult <c>Mouse::set_speed_cursor</c>'s speed for the cursor at this world point.</summary>
    int MouseWalkSpeed(Vector2 world)
    {
        var av = _avatar.Avatar;
        WorldView.ShapeLocation(av.Tx, av.Ty, av.Tz, out var ax, out var ay);
        var game = GetViewport().GetVisibleRect().Size / _zoom;
        return WalkSpeed.Mouse(new Vector2(ax, ay), world, game, _combat.InCombat, HostileNearby(), AvatarNoHaltScript());
    }

    /// <summary>Exult <c>is_hostile_nearby</c> over the visible part of the map.</summary>
    bool HostileNearby()
    {
        var av = _avatar.Avatar;
        var size = GetViewport().GetVisibleRect().Size / _zoom / U7Constants.TileSize;
        var w = (int)size.X + 1;
        var h = (int)size.Y + 1;
        return _combat.IsHostileNearby(av.Tx - w / 2, av.Ty - h / 2, w, h);
    }

    /// <summary>Exult: an active no-halt usecode script on the avatar rules out walking fast.</summary>
    bool AvatarNoHaltScript() =>
        _usecode?.Scripts.Any(s => s.Obj == _avatar.Avatar && s.Activated && !s.Done && s.NoHalt) ?? false;

    /// <summary>
    /// Exult <c>ActionWalk</c> / <c>ActionStopWalking</c>: while WASD or an
    /// arrow key is held the avatar steers that way (Shift: medium speed);
    /// letting go stops.
    /// </summary>
    void KeyboardWalk(bool canWalk)
    {
        var x = (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right) ? 1 : 0) -
                (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left) ? 1 : 0);
        var y = (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down) ? 1 : 0) -
                (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up) ? 1 : 0);
        if (canWalk && (x != 0 || y != 0))
        {
            var av = _avatar.Avatar;
            WorldView.ShapeLocation(av.Tx, av.Ty, av.Tz, out var ax, out var ay);
            _avatar.Steer(new Vector2(ax + 50 * x, ay + 50 * y),
                WalkSpeed.Keyboard(Input.IsKeyPressed(Key.Shift), _combat.InCombat, HostileNearby()));
            _keyWalking = true;
        }
        else if (_keyWalking)
        {
            _keyWalking = false;
            _avatar.Stop();
        }
    }

    /// <summary>
    /// Exult <c>Get_click</c> while a book page is shown: releasing the left
    /// button turns the page, and no other click reaches the game. Only a
    /// press made while reading counts, not the double-click that opened the book.
    /// </summary>
    bool HandleBookClick(InputEventMouseButton mb)
    {
        var reading = _usecode is { Wait: UsecodeWait.BookPage };
        if (mb.ButtonIndex == MouseButton.Left)
        {
            var pressedWhileReading = _bookPress;
            _bookPress = mb.Pressed && reading;
            if (reading && !mb.Pressed && pressedWhileReading)
            {
                _usecode!.TurnBookPage();
                _conversation.Refresh();
            }
        }

        if (!reading || mb.ButtonIndex is not (MouseButton.Left or MouseButton.Right))
        {
            return false;
        }

        _suppressWalk = true;
        GetViewport().SetInputAsHandled();
        return true;
    }

    bool HandleClickOnItem(int mx, int my, bool right)
    {
        if (_usecode is not { Wait: UsecodeWait.ClickOnItem } || right)
        {
            return false;
        }

        var gumpObj = _gumps.PickAnywhere(_gumpView, mx, my);
        var worldObj = gumpObj ?? _world.PickObject(_camera.GetGlobalMousePosition());
        var tile = WorldView.WorldToTile(_camera.GetGlobalMousePosition(), _avatar.Avatar.Tz);
        var arr = UsecodeValue.FromArray(4, UsecodeValue.FromObject(worldObj));
        arr.PutElem(1, UsecodeValue.FromInt(worldObj?.Tx ?? tile.Tx));
        arr.PutElem(2, UsecodeValue.FromInt(worldObj?.Ty ?? tile.Ty));
        arr.PutElem(3, UsecodeValue.FromInt(worldObj?.Tz ?? tile.Tz));
        _usecode.ResumeWait(arr);
        _conversation.Refresh();
        return true;
    }

    void ActivateUnderMouse()
    {
        if (_usecode is null)
        {
            return;
        }

        if (_usecode.InUsecode || _usecode.WaitingForChoice)
        {
            return;
        }

        var virt = _gumpView.MouseVirtual();
        var gump = _gumps.FindGump(virt.X, virt.Y, _gumpView);
        if (gump is not null)
        {
            var inside = gump.FindObject(_gumpView, virt.X, virt.Y);
            if (inside is not null)
            {
                if (_gumps.ShowGump(inside))
                {
                    _statusExtra = $"gump {_catalog[inside.Shape].GumpShape}  {inside.Shape}";
                    return;
                }

                RunUsecode(inside);
                return;
            }

            return;
        }

        var obj = _world.PickObject(_camera.GetGlobalMousePosition());
        if (obj is null)
        {
            _statusExtra = "nothing to use";
            return;
        }

        if (_combat.InCombat && CombatClick(obj))
        {
            return;
        }

        if (obj.NpcNum > 0)
        {
            RunUsecode(obj);
            return;
        }

        if (_gumps.ShowGump(obj))
        {
            _statusExtra = $"gump {_catalog[obj.Shape].GumpShape}  {obj.Shape}  items {obj.Contents.Count}";
            return;
        }

        RunUsecode(obj);
    }

    /// <summary>
    /// Exult <c>Game_window::double_clicked</c> in combat mode: anything but
    /// a party member or a body is attacked, except unlocked doors and
    /// containers, which open. False if it is used as usual.
    /// </summary>
    bool CombatClick(U7Object obj)
    {
        var info = _catalog[obj.Shape];
        if ((obj.IsActor && _party.IsInParty(obj)) || Bodies.IsBodyShape(obj.Shape))
        {
            return false;
        }

        if ((info.Door && obj.Frame % 4 < 2) || (Inventory.IsContainer(obj, _catalog) && obj.Shape is not (522 or 798)))
        {
            return false;
        }

        _combat.AttackClicked(obj);
        _statusExtra = _combat.LastMessage;
        return true;
    }

    void RunUsecode(U7Object obj)
    {
        if (_usecode is null)
        {
            return;
        }

        var fun = obj.NpcNum >= 0 ? obj.GetUsecode() : UsecodeMachine.GetItemFun(obj);
        if (fun < 0)
        {
            fun = UsecodeMachine.GetShapeFun(obj.Shape);
        }

        _statusExtra = obj.NpcNum >= 0
            ? $"usecode 0x{fun:X3}  npc {obj.NpcNum} {obj.NpcName}"
            : $"usecode 0x{fun:X3}  {obj.Shape}";
        var rc = _usecode.Call(fun, obj, UsecodeEvent.DoubleClick);
        if (rc < 0)
        {
            _statusExtra = _usecode.HudMessage;
        }

        _conversation.Refresh();
    }


    /// <summary>Exult <c>Earthquake::handle_event</c>: a random ±4 pixel jolt every 100 ms while usecode asks for one.</summary>
    Vector2 QuakeOffset(double delta)
    {
        if (_usecode is not { QuakeSteps: > 0 } machine)
        {
            _quakeOffset = Vector2.Zero;
            return _quakeOffset;
        }

        _quakeTimer -= delta;
        if (_quakeTimer <= 0)
        {
            _quakeTimer = 0.1;
            machine.QuakeSteps--;
            _quakeOffset = new Vector2(GD.RandRange(-4, 4), GD.RandRange(-4, 4));
        }

        return _quakeOffset;
    }
}
