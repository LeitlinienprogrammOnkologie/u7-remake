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
    EffectsManager _effects = null!;
    Barges _barges = null!;
    /// <summary>The mouse is steering the barge in barge mode (it stops when the button is let go).</summary>
    bool _bargeMouse;
    /// <summary>Exult <c>camera_actor</c>: whom the view follows when not the avatar (<c>set_camera</c>).</summary>
    U7Object? _cameraActor;
    /// <summary>Exult <c>center_view</c> on an object: the view stays there (a moving barge takes it along) until the avatar walks.</summary>
    TileCoord? _cameraTile;
    /// <summary>Exult <c>center_view</c> from usecode (<c>view_tile</c>): the view stays there until the avatar moves.</summary>
    TileCoord? _viewTile;
    TileCoord _viewTileFrom;
    SceneLighting _lighting = null!;
    ScreenFx _screenFx = null!;
    MouseCursor _cursor = null!;
    /// <summary>Exult <c>run_endgame</c>: once it runs, the game is over.</summary>
    Endgame? _endgame;
    /// <summary>Exult <c>BG_Game::new_game</c>'s screen while it is shown: the game waits for the avatar's name.</summary>
    NewGameView? _newGame;
    MusicPlayer _music = null!;
    PartyManager _party = null!;
    CombatEngine _combat = null!;
    NpcTimers _timers = null!;
    Fields _fields = null!;
    List<U7Object?> _npcs = new();
    float _zoom = 4f;
    double _quakeTimer;
    Vector2 _quakeOffset;
    bool _ready;
    bool _debugOn;
    int _lastSchunk = -1;
    (int Cx, int Cy) _lastChunk = (-1, -1);
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

            var avatar = _map.CreateActor(U7Constants.AvatarShape, 16);
            _map.PlaceInWorld(avatar, U7Constants.StartTileX, U7Constants.StartTileY, U7Constants.StartLift);
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
                _clock.SpecialLight = g0.SpecialLight;
            }

            var schedTable = ScheduleTable.Load();
            _schedules = new ScheduleRunner(_map, avatar, _npcs, schedTable, _clock);
            var usecodeDat = UsecodeDat.Read();
            _party = new PartyManager(_map, avatar, _npcs) { Schedules = _schedules };
            _party.LinkParty(usecodeDat?.Party);
            _schedules.Party = _party;
            _schedules.AvatarMoving = () => _avatar.IsPlayerMoving;
            _combat = new CombatEngine(_map, avatar, _catalog);
            _music = new MusicPlayer();
            _eggs = new EggHatcher(_map, _clock) { Combat = _combat, Music = _music, Party = _party };
            _avatar.WalkStarted = () =>
            {
                _cameraTile = null;
                _party.CallFollowers();
            };
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
            var monsters = NpcDat.LoadMonsters(_map);
            _combat.AdoptMonsters(monsters);
            _timers = new NpcTimers(_schedules, _combat);
            _timers.Restore(_npcs.Concat(monsters));
            _combat.Timers = _timers;
            _fields = new Fields(_map, _combat, _timers, _schedules);
            ActorWalker.Stepped = _fields.Stepped;
            if (gwin is { } g1)
            {
                _combat.Armageddon = g1.Armageddon;
                if (g1.InCombat)
                {
                    _combat.SetInCombat(true);
                }

                if (g1.Track >= 0)
                {
                    _music.Start(g1.Track, g1.Repeat);
                }

                // After the start, which clears it (Exult's stop_music doesn't).
                _music.EggCount = g1.EggCount;
            }

            _effects = new EffectsManager(_shapes.SpritesVga, _clock) { InDungeon = () => _lighting.InDungeon };
            _eggs.Effects = _effects;
            _combat.Effects = _effects;
            _schedules.Effects = _effects;
            _world = new WorldView
            {
                Name = "WorldView",
                Map = _map,
                Catalog = _catalog,
                Shapes = _shapes,
                Avatar = avatar,
                Effects = _effects,
                Missiles = _combat.Missiles,
                HomingMissiles = _combat.HomingMissiles,
                TextureFilter = TextureFilterEnum.Nearest,
                Animators = new Animators(_map, _clock)
            };
            AddChild(_world);
            _lighting = new SceneLighting(_clock, _map, _catalog, avatar, _effects, _party, new GlowColours(_shapes, _catalog))
            {
                Missiles = _combat.Missiles,
                HomingMissiles = _combat.HomingMissiles
            };
            _world.Lighting = _lighting;
            // Rain, snow and sparkles over the world, under the gumps and the screen effects.
            AddChild(new WeatherView { Name = "WeatherView", Effects = _effects, Lighting = _lighting });

            _gumps = new GumpManager(_map, avatar);
            _gumps.ActivateUsecode = obj => RunUsecode(obj);
            _gumps.DropOnMap = DropOnMap;
            _gumps.LiftedFromWorld = (obj, drag) =>
            {
                // Exult Dragging_info::drop: the eggs where it was, and what stood on it falls.
                var old = new TileCoord(drag.OldTx, drag.OldTy, drag.OldTz);
                _eggs.ActivateAt(obj, old, old.Tx, old.Ty);
                _map.Gravity(old.Tx - obj.DimX + 1, old.Ty - obj.DimY + 1, obj.DimX, obj.DimY, old.Tz + _catalog[obj.Shape].DimZ);
            };
            _combat.Guards = new Guards(_map, avatar, _combat, _schedules, _party)
            {
                InDungeon = () => _lighting.InDungeon,
                CloseGumps = () => _gumps.CloseAll()
            };
            _gumps.PossibleTheft = _combat.Guards.PossibleTheft;
            _gumps.ToggleCombat = ToggleCombat;
            _gumps.IsCombatOn = () => _combat.InCombat;
            _gumps.GumpsVga = _shapes.GumpsVga;

            var gumpLayer = new CanvasLayer { Name = "Gumps", Layer = 10 };
            AddChild(gumpLayer);
            _gumpView = new GumpView
            {
                Name = "GumpView",
                Gumps = _gumps,
                Shapes = _shapes,
                Catalog = _catalog,
                Map = _map,
                Quantities = _combat.Quantities,
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
            layer.AddChild(new BarkOverlay { Name = "Barks", World = _world, Gumps = _gumpView });
            _conversation = new ConversationPanel { Name = "Conversation", Shapes = _shapes };
            layer.AddChild(_conversation);
            // Over the game's picture (world, gumps, barks, conversation); the debug lines stay on top.
            _screenFx = new ScreenFx { Name = "ScreenFx" };
            layer.AddChild(_screenFx);
            var cursorLayer = new CanvasLayer { Name = "Cursor", Layer = 30 };
            AddChild(cursorLayer);
            _cursor = new MouseCursor { Name = "MouseCursor", Zoom = _zoom };
            cursorLayer.AddChild(_cursor);
            _gumps.FlashMouse = _cursor.Flash;
            _combat.FlashMouse = _cursor.Flash;

            _hud = new Label
            {
                Name = "Status",
                Position = new Vector2(12, 8),
                TextureFilter = TextureFilterEnum.Nearest
            };
            _hud.AddThemeColorOverride("font_color", new Color(0.95f, 0.9f, 0.7f));
            layer.AddChild(_hud);

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
            _usecode.Frame8 = _shapes.GetFrame8;
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
            _usecode.FlashMouse = _cursor.Flash;
            _usecode.RunEndgame = StartEndgame;
            _usecode.Npcs = _npcs;
            _usecode.Clock = _clock;
            _usecode.Schedules = _schedules;
            _usecode.Party = _party;
            _usecode.Combat = _combat;
            _usecode.Music = _music;
            _usecode.Eggs = _eggs;
            _usecode.Effects = _effects;
            _usecode.Lighting = _lighting;
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
            _usecode.TeleportParty = t => TeleportParty(t);
            _eggs.TeleportParty = t => TeleportParty(t);
            _usecode.AvatarTeleported = (fromTx, fromTy) =>
            {
                // Exult move_object of the avatar: set_action(nullptr), center_view, try_all_eggs.
                _avatar.ClearPath();
                _cameraTile = null;
                _viewTile = null;
                _eggs.Activate(_avatar.Avatar, -1, -1);
            };
            _usecode.AvatarMovedByScript = actor =>
            {
                _eggs.Activate(actor, actor.Tx, actor.Ty);
                _party.AvatarStepped(actor.Tx, actor.Ty);
            };
            _combat.AvatarFlashRed = _screenFx.PulseRed;
            _combat.AvatarDied = () =>
            {
                _gumps.CloseAll(true);
                _avatar.ClearPath();
                GD.Print("avatar died: running death usecode 0x60E");
                _usecode.Call(0x60E, avatar, UsecodeEvent.Weapon);
            };
            _eggs.Usecode = _usecode;
            _barges = new Barges(_map)
            {
                // Exult Barge_object::step: the eggs on the barge's new tile, for the avatar.
                Stepped = (barge, from) => _eggs.ActivateAt(avatar, new TileCoord(barge.Obj.Tx, barge.Obj.Ty, barge.Obj.Tz), from.Tx, from.Ty),
                // Exult finish_move scrolls to the barge's centre: a view centred elsewhere follows it.
                Moved = barge =>
                {
                    if (_cameraTile is not null)
                    {
                        _cameraTile = barge.Center;
                    }
                }
            };
            _map.IsMovingBarge = obj => _barges.Moving?.Obj == obj;
            _map.MoveBarge = (obj, tx, ty, tz) => _barges.Of(obj).Move(tx, ty, tz);
            // (Exult furls the sails from within usecode too; here only outside of it.)
            Barge.Activate = obj =>
            {
                if (!UsecodeRunning)
                {
                    RunUsecode(obj);
                }
            };
            _usecode.Barges = _barges;
            _usecode.SetCamera = obj =>
            {
                if (obj.IsActor)
                {
                    _cameraActor = obj == avatar ? null : obj;
                    _cameraTile = null;
                }
                else
                {
                    _cameraTile = new TileCoord(obj.Tx, obj.Ty, obj.Tz);
                }
            };
            _schedules.Barges = _barges;
            if (_map.LoadedMovingBarge is { } movingBarge)
            {
                _barges.SetMoving(_barges.Of(movingBarge), avatar);
            }
            // Walkers open doors through the doors' own usecode (Exult Path_walking_actor_action::open_door).
            PathWalk.ActivateDoor = door => TryCallUsecode(UsecodeMachine.GetItemFun(door), door, UsecodeEvent.DoubleClick);
            PathWalk.IsSentient = _combat.IsSentient;
            _combat.WeaponUsecode = (fun, target) => _usecode.Call(fun, target, UsecodeEvent.Weapon);
            _combat.DoSpeech = _usecode.DoSpeech;
            _combat.DeathUsecode = (npc, finish) => _usecode.WhenDone(() =>
            {
                _usecode.Call(UsecodeMachine.GetShapeFun(npc.Shape), npc, UsecodeEvent.InternalExec);
                _conversation.Refresh();
                _usecode.WhenDone(finish);
            });
            _gumps.CastSpell = (fun, caster) =>
            {
                _usecode.Call(fun, caster, UsecodeEvent.DoubleClick);
                _conversation.Refresh();
            };
            _gumps.FailedCopyProtection = () => _usecode.FailedCopyProtection;
            UsecodeAction.Call = (fun, item, eventId) => TryCallUsecode(fun, item, (UsecodeEvent)eventId);
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
            _schedules.ReadyBestWeapon = npc => _combat.ReadyBestWeapon(npc);
            _schedules.Weapons = _combat.Weapons;
            _schedules.Activate = obj =>
            {
                if (UsecodeRunning)
                {
                    return false;
                }

                RunUsecode(obj); // Exult Game_object::activate, as a double-click.
                return true;
            };
            _schedules.CallUsecode = (fun, item) => TryCallUsecode(fun, item, UsecodeEvent.DoubleClick);
            _schedules.IsInUsecode = () => _usecode.InUsecode;
            _schedules.Say = _usecode.Bark;
            SitAction.Say = _usecode.Bark;
            _schedules.CanSpeak = _combat.CanSpeak;
            _schedules.InUsecodeControl = _usecode.InUsecodeControl;
            if (U7Paths.GameDatOverride is null)
            {
                // Exult read_npcs: a new game readies the avatar's and Iolo's best weapons.
                foreach (var id in (int[])[0, 1])
                {
                    if (id < _npcs.Count && _npcs[id] is { } npc)
                    {
                        _combat.ReadyBestWeapon(npc);
                    }
                }
            }

            _schedules.Start(restore: U7Paths.GameDatOverride is not null);
            _timers.TerminateScripts = _usecode.TerminateScripts;
            _schedules.TerminateScripts = _usecode.TerminateScripts;
            _schedules.ForceSleep = _timers.ForceSleep;
            _usecode.ActorFlags = _timers;
            _conversation.Machine = _usecode;
            _gumpView.ShownBook = () => _usecode is { Wait: UsecodeWait.BookPage } vm ? vm.Book : null;
            _gumpView.ShownPicture = () => _usecode is { Wait: UsecodeWait.Picture or UsecodeWait.WizardEye } vm ? vm.Picture : null;
            _gumpView.ShownSign = () => _usecode is { Wait: UsecodeWait.Picture } vm ? vm.Sign : null;
            _usecode.ViewTile = tile =>
            {
                _viewTile = tile;
                _viewTileFrom = new TileCoord(_avatar.Avatar.Tx, _avatar.Avatar.Ty, _avatar.Avatar.Tz);
            };
            _usecode.ViewRecentred = () => _viewTile = null;
            _usecode.Say += _ => _conversation.Refresh();
            _usecode.AnswersChanged += _conversation.Refresh;
            _usecode.FacesChanged += _conversation.Refresh;

            // Exult BG_Game::new_game: a new game asks for the avatar's name and sex first, and the
            // opening (the eggs round the avatar) waits for them. The console names it itself (avatar).
            if (U7Paths.GameDatOverride is null && string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("U7_AGENT")))
            {
                ShowNewGame();
            }
            else
            {
                _eggs.Activate(avatar, -1, -1);
            }

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

        // Exult BG_Game::new_game: nothing else runs until the avatar is named.
        if (_newGame is not null)
        {
            Vector2I? noWalk = null;
            AgentUpdate(delta, ref noWalk);
            return;
        }

        // Exult run_endgame: only the endgame runs; when it is over, so is the game.
        if (_endgame is { } endgame)
        {
            endgame.Update(delta);
            Vector2I? noWalk = null;
            AgentUpdate(delta, ref noWalk);
            if (endgame.Done && _agentDir is null)
            {
                GetTree().Quit();
            }

            return;
        }

        // Exult flash_shape: while the cursor flashes, the whole game holds (its SDL_Delay).
        if (_cursor.Holding)
        {
            if (!_cursor.Tick(delta))
            {
                _world.Frozen = true;
                return;
            }

            _usecode?.EndFlash();
            _conversation.Refresh();
        }

        UpdateCursor();
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

        // Exult Main_actor::switched_chunks → emulate_cache: weather from eggs 120 tiles away ends.
        var chunk = (avPos.Tx / U7Constants.TilesPerChunk, avPos.Ty / U7Constants.TilesPerChunk);
        if (chunk != _lastChunk)
        {
            if (_lastChunk.Cx >= 0)
            {
                _effects.RemoveWeather(new TileCoord(avPos.Tx, avPos.Ty, avPos.Tz), 120);
                EmulateCache(_lastChunk, chunk);
            }

            _lastChunk = chunk;
        }

        Vector2I? click = null;
        var inUsecode = UsecodeRunning;
        var virt = _gumpView.MouseVirtual();
        var overGump = _gumps.FindGump(virt.X, virt.Y, _gumpView) is not null;
        var gumpBusy = _gumps.GumpMode || _gumps.IsDragging || overGump || _gumps.Drag is not null;
        if (_gumps.Drag is not null)
        {
            _gumps.OnMouseMove(_gumpView, virt.X, virt.Y);
        }

        var canWalk = CanWalk && !gumpBusy;
        var viewTiles = GetViewport().GetVisibleRect().Size / _zoom / U7Constants.TileSize;
        Pathfinder.ScreenTilesWide = Math.Max(1, (int)viewTiles.X);
        _schedules.ScreenTiles = (Math.Max(1, (int)viewTiles.X), Math.Max(1, (int)viewTiles.Y));
        if (canWalk && _leftPress == LeftPress.Walk && Input.IsMouseButtonPressed(MouseButton.Left))
        {
            var world = _camera.GetGlobalMousePosition();
            var tile = WorldView.WorldToTile(world, _avatar.Avatar.Tz);
            if (!Input.IsKeyPressed(Key.Shift))
            {
                StartActor(world);
            }
            else if (tile.Tx != _avatar.Avatar.Tx || tile.Ty != _avatar.Avatar.Ty)
            {
                // Exult's cheat teleport.
                TeleportParty(tile);
            }
        }
        else if (canWalk && _rightWalk && !_rightOnGump && RightHeld)
        {
            StartActor(MouseWorld());
        }

        if (_showItemsMsec != 0 && Time.GetTicksMsec() > _showItemsMsec)
        {
            _showItemsMsec = 0;
            ShowItems(_showItemsVirt, _showItemsWorld);
        }

        KeyboardWalk(canWalk);
        AgentUpdate(delta, ref click);
        if (!IsInsideTree())
        {
            return; // the console loaded a game: this scene is being replaced
        }

        if (click is { } c && _barges.Moving is null)
        {
            // (Exult start_actor_along_path: "For now, don't do barges.")
            _avatar.PathTo(new TileCoord(c.X, c.Y, _avatar.Avatar.Tz), WalkSpeed.Keyboard(false, false, false));
        }

        if (!Input.IsMouseButtonPressed(MouseButton.Left))
        {
            _leftPress = LeftPress.None;
            if (_bargeMouse && !_rightWalk)
            {
                // Exult stop_actor.
                _bargeMouse = false;
                _barges.Moving?.Stop();
            }
        }

        // Exult's Wizard Eye loop runs the time queue: the world goes on while the player looks about.
        var eye = _usecode is { Wait: UsecodeWait.WizardEye };
        var frozen = (inUsecode && !eye) || gumpBusy || _avatar.Avatar.IsDead;
        if (eye)
        {
            UpdateWizardEye(delta);
        }

        if (canWalk)
        {
            _avatar.Update(delta, _schedules.AvatarActing,
                _usecode?.InUsecodeControl(_avatar.Avatar) ?? false);
        }

        _clock.Update(delta);
        _usecode?.UpdateFade(delta);
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
        _timers.Update(delta, frozen);
        _fields.Update(delta, frozen);
        // Exult pauses its time queue while usecode waits for a click and in gump mode.
        if (!inUsecode && !_gumps.GumpMode)
        {
            _effects.Update(delta);
            _barges.Update(delta);
        }

        if (_combat.InCombat && _barges.Moving is not null)
        {
            // Exult toggle_combat: combat ends barge mode.
            _barges.SetMoving(null, _avatar.Avatar);
        }

        var camera = _camera;
        var hud = _hud;
        var catalog = _catalog;
        var map = _map;
        if (camera is null || hud is null || catalog is null || map is null)
        {
            return;
        }

        // Usecode's fades: while the screen goes or stays black, the world and the view hold still.
        var fadedOut = _usecode is { FadedOut: true };
        _screenFx.Fade = _usecode?.FadeLevel ?? 1f;
        _world.Frozen = fadedOut;
        // Exult's Get_click (a target, the map, the crystal ball) holds the colours still.
        _world.RotateColors = !fadedOut && _usecode is not { Wait: UsecodeWait.ClickOnItem or UsecodeWait.Picture };

        var av = _avatar.Avatar;
        // Exult display_area: the view goes to the area while it is shown (up to lift 4, no dungeon),
        // Wizard Eye to where the player has moved it; view_tile until the avatar moves.
        var area = _usecode is { Wait: UsecodeWait.Picture or UsecodeWait.WizardEye, Picture.Area: { } shown } ? shown : (TileCoord?)null;
        _world.RemoteView = _lighting.RemoteView = area is not null && _usecode!.Wait == UsecodeWait.Picture;
        if (_viewTile is not null && (av.Tx, av.Ty, av.Tz) != (_viewTileFrom.Tx, _viewTileFrom.Ty, _viewTileFrom.Tz))
        {
            _viewTile = null;
        }

        var focus = area ?? _viewTile ?? _cameraTile ?? (_cameraActor is { Removed: false } ca
            ? new TileCoord(ca.Tx, ca.Ty, ca.Tz)
            : new TileCoord(av.Tx, av.Ty, av.Tz));
        WorldView.ShapeLocation(focus.Tx, focus.Ty, focus.Tz, out var camX, out var camY);
        var quake = QuakeOffset(delta);
        if (!fadedOut)
        {
            camera.GlobalPosition = new Vector2(camX, camY) + quake;
        }

        // The lights for the view the world is about to draw.
        var viewSize = GetViewport().GetVisibleRect().Size / camera.Zoom;
        _lighting.View = new Rect2(camera.GlobalPosition - viewSize / 2, viewSize);
        // Exult get_win_tile_rect, for missile eggs (used next frame).
        var viewTopLeft = (camera.GlobalPosition - viewSize / 2) / U7Constants.TileSize;
        _combat.ViewTiles = new Rect2I(Mathf.FloorToInt(viewTopLeft.X), Mathf.FloorToInt(viewTopLeft.Y),
            Mathf.CeilToInt(viewSize.X / U7Constants.TileSize), Mathf.CeilToInt(viewSize.Y / U7Constants.TileSize));
        _lighting.Focus = (focus.Tx, focus.Ty);
        _lighting.Update(delta);

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
            $"WASD/arrows walk · I inventory · C combat · F4 invincible · [ ] hour · right or left button walk · click name · double-click / E · F2 debug · F3 arena · PgUp/PgDn lift · M music · F5/F9 save/load · F6 die · Home Trinsic\n" +
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

    /// <summary>
    /// Exult <c>Game_window::emulate_cache</c> when the avatar enters another
    /// chunk: the scripts not yet started more than 4 chunks off are purged,
    /// and in the chunks of the old 5×5 round it that are not round it any
    /// more the eggs reset (they can hatch again) and temporary things go.
    /// </summary>
    void EmulateCache((int Cx, int Cy) old, (int Cx, int Cy) now)
    {
        _usecode?.PurgeScripts(new TileCoord(now.Cx * U7Constants.TilesPerChunk, now.Cy * U7Constants.TilesPerChunk, 0),
            4 * U7Constants.TilesPerChunk);
        for (var y = -2; y <= 2; y++)
        {
            for (var x = -2; x <= 2; x++)
            {
                var cx = U7Constants.WrapChunk(old.Cx + x);
                var cy = U7Constants.WrapChunk(old.Cy + y);
                if (ChunkDistance(cx, now.Cx) <= 2 && ChunkDistance(cy, now.Cy) <= 2)
                {
                    continue;
                }

                foreach (var obj in _map.ObjectsInChunk(cx, cy).ToList())
                {
                    if (obj.IsEgg)
                    {
                        _eggs.Reset(obj);
                    }
                    else if (GameMap.IsTemporary(obj) && !obj.Removed)
                    {
                        _map.RemoveObject(obj);
                    }
                }
            }
        }
    }

    static int ChunkDistance(int a, int b)
    {
        var d = Math.Abs(a - b) % U7Constants.NumChunks;
        return Math.Min(d, U7Constants.NumChunks - d);
    }

    /// <summary>
    /// Exult <c>Game_window::teleport_party</c>: the avatar's walk and barge
    /// mode end, the eggs on the tile left let go, the avatar moves and the
    /// palette is set at once, the party (not those waiting or dead) stands
    /// on free spots round it, and every egg round the new spot is tried
    /// (<c>try_all_eggs</c>) unless <paramref name="skipEggs"/>.
    /// </summary>
    void TeleportParty(TileCoord t, bool skipEggs = false)
    {
        var av = _avatar.Avatar;
        var (fromTx, fromTy) = (av.Tx, av.Ty);
        _avatar.ClearPath();
        _barges.SetMoving(null, av);
        if (!skipEggs)
        {
            _eggs.UnhatchLeaving(av, t, fromTx, fromTy);
        }

        _map.MoveObject(av, t.Tx, t.Ty, t.Tz);
        _cameraTile = null;
        _viewTile = null;
        _lighting.ResetPalette();
        _party.FollowTeleport();
        if (!skipEggs)
        {
            _eggs.Activate(av, -1, -1);
        }
    }

    /// <summary>
    /// Exult <c>BG_Game::new_game</c>: the avatar's name and sex are chosen on a
    /// screen over everything, the hand for the cursor; then the opening begins.
    /// </summary>
    void ShowNewGame()
    {
        var layer = new CanvasLayer { Name = "NewGame", Layer = 25 };
        AddChild(layer);
        _newGame = new NewGameView(_shapes) { Name = "NewGameView" };
        _newGame.Finished = (name, female) =>
        {
            SetAvatar(name, female);
            layer.QueueFree();
            _newGame = null;
            _eggs.Activate(_avatar.Avatar, -1, -1);
        };
        layer.AddChild(_newGame);
        _cursor.Shape = MouseShape.Hand;
    }

    /// <summary>
    /// The new game's choice, as Exult's <c>Actor::read</c> applies it to NPC 0
    /// (<c>set_avname</c>, <c>set_avsex</c>) and <c>read_npcs</c> its shape.
    /// </summary>
    void SetAvatar(string name, bool female)
    {
        var av = _avatar.Avatar;
        av.NpcName = name;
        AvatarLook.SetFemale(av, female);
        AvatarLook.SetActorShape(_map, av);
    }

    /// <summary>
    /// Exult <c>UI_run_endgame</c>: the game stops, and the endgame plays on a
    /// screen over everything, without the mouse cursor (Exult paints none).
    /// </summary>
    void StartEndgame(bool success)
    {
        if (_endgame is not null)
        {
            return;
        }

        var layer = new CanvasLayer { Name = "Endgame", Layer = 25 };
        AddChild(layer);
        var view = new EndgameView { Name = "EndgameView" };
        layer.AddChild(view);
        _cursor.HideCursor = true;
        _endgame = new Endgame(view, _music, _clock.TotalHours, success)
        {
            Log = _agentDir is null ? null : AgentLog
        };
    }

    /// <summary>
    /// Exult's endgame input: in the movies <c>wait_delay</c> (Esc, Space,
    /// Enter, a double-click) skips; in the credits any key but Shift, or a
    /// click, ends them (<c>TextScroller::run</c>).
    /// </summary>
    static void EndgameInput(Endgame endgame, InputEvent @event)
    {
        switch (@event)
        {
            case InputEventKey { Pressed: true, Echo: false } key when endgame.InCredits
                ? key.Keycode != Key.Shift
                : key.Keycode is Key.Escape or Key.Space or Key.Enter or Key.KpEnter:
            case InputEventMouseButton { ButtonIndex: MouseButton.Left or MouseButton.Right } button when endgame.InCredits
                ? !button.Pressed
                : button is { Pressed: true, DoubleClick: true }:
                endgame.Skip();
                break;
        }
    }

    /// <summary>Usecode runs or waits for an answer (Exult's conversations hold its event loop).</summary>
    bool UsecodeRunning => _usecode is { InUsecode: true } or { WaitingForChoice: true };

    /// <summary>
    /// Whether the player may walk the avatar (mouse or keys): no usecode
    /// running or waiting, not held by the opening (<c>dont_move</c>), and
    /// Exult's <c>avatar_can_act</c>: not asleep, paralysed, dead or knocked out.
    /// </summary>
    bool CanWalk => !UsecodeRunning && !ObjFlag.DontMoveMode(_avatar.Avatar) && CombatSchedule.CanAct(_avatar.Avatar);

    /// <summary>A call from the engine (doors, schedules, scripts' actions): only while no usecode runs or waits.</summary>
    bool TryCallUsecode(int fun, U7Object? item, UsecodeEvent ev)
    {
        if (_usecode is null || UsecodeRunning)
        {
            return false;
        }

        _usecode.Call(fun, item, ev);
        _conversation.Refresh();
        return true;
    }

    /// <summary>The combat button or the C key: combat on or off; going into combat drops the avatar's walk.</summary>
    void ToggleCombat()
    {
        _combat.ToggleCombat();
        if (_combat.InCombat)
        {
            _avatar.ClearPath();
        }

        _statusExtra = _combat.LastMessage;
    }

    void RunUsecode(U7Object obj)
    {
        if (_usecode is null)
        {
            return;
        }

        // Exult Actor::activate: an NPC asleep, fighting outside the party, or
        // under a Time Stop outside the party, doesn't answer.
        if (obj.IsActor && obj != _avatar.Avatar)
        {
            var inParty = _party.IsInParty(obj);
            if ((obj.ScheduleType == ScheduleType.Sleep && (obj.Frame & 0xf) == ActorWalker.SleepFrame) ||
                obj.GetFlag(ObjFlag.Asleep) ||
                (obj.ScheduleType == ScheduleType.Combat && !inParty) ||
                (!inParty && _clock.TimeStopped))
            {
                return;
            }
        }

        var fun = obj.NpcNum >= 0 || obj.AssignedUsecode >= 0 ? obj.GetUsecode() : UsecodeMachine.GetItemFun(obj);
        if (fun < 0)
        {
            fun = UsecodeMachine.GetShapeFun(obj.Shape);
        }

        if (obj.IsActor && obj != _avatar.Avatar && _usecode.FailedCopyProtection)
        {
            fun = UsecodeMachine.FailCopyProtectionUsecode;
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

    /// <summary>Saves the game into the slot (F5, the console's <c>save</c>).</summary>
    void SaveTo(string slot) =>
        SaveGame.Write(slot, _map, _npcs, _usecode, _combat.Spawned,
            new GwinState(_clock.Day, _clock.Hour, _clock.Minute, _combat.InCombat, _music.CurrentTrack, _music.Repeat,
                _music.EggCount, _clock.SpecialLight, _combat.Armageddon));

    /// <summary>Loads the slot by restarting the scene on it (F9, the console's <c>load</c>); false when it holds no save.</summary>
    bool LoadFrom(string slot)
    {
        if (!SaveGame.Exists(slot))
        {
            return false;
        }

        U7Paths.GameDatOverride = SaveGame.SlotDir(slot);
        GD.Print("loading " + U7Paths.GameDatOverride);
        GetTree().ReloadCurrentScene();
        return true;
    }

    /// <summary>
    /// Debug (F6): a lethal hit on the avatar through the normal damage path.
    /// Usecode 0x60E restarts the game unless global flag 0x57 is set; with it
    /// set you wake up in the Fellowship shelter in Paws (Feridwyn and Brita).
    /// It is set unless <paramref name="restart"/> (Shift).
    /// </summary>
    void DebugDie(bool restart)
    {
        if (!restart && _usecode is { } uc && 0x57 < uc.GFlags.Length)
        {
            uc.GFlags[0x57] = 1;
        }

        _combat.ReduceHealth(_avatar.Avatar, 1000, null, 0);
        _statusExtra = _combat.LastMessage;
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
