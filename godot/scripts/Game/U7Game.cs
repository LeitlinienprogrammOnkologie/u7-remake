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
    /// <summary>The right button went down to walk: holding it steers (Exult's main loop calls <c>start_actor</c>).</summary>
    bool _rightWalk;
    /// <summary>Exult <c>right_on_gump</c>: the right button went down on a gump in gump mode, which closes it when let go.</summary>
    bool _rightOnGump;
    /// <summary>Exult <c>last_b3_click</c>: when the right button was last let go.</summary>
    ulong _lastRightUpMsec;
    /// <summary>Exult <c>show_items_time</c>: when a left click names what it hit, 0 if none waits.</summary>
    ulong _showItemsMsec;
    Vector2I _showItemsVirt;
    Vector2 _showItemsWorld;
    /// <summary>A walking key is held.</summary>
    bool _keyWalking;
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
            _combat.AdoptMonsters(NpcDat.LoadMonsters(_map));
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
                TextureFilter = TextureFilterEnum.Nearest
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
                if (!_usecode.InUsecode && !_usecode.WaitingForChoice)
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
            _combat.WeaponUsecode = (fun, target) => _usecode.Call(fun, target, UsecodeEvent.Weapon);
            _gumps.CastSpell = (fun, caster) =>
            {
                _usecode.Call(fun, caster, UsecodeEvent.DoubleClick);
                _conversation.Refresh();
            };
            _gumps.FailedCopyProtection = () => _usecode.FailedCopyProtection;
            UsecodeAction.Call = (fun, item, eventId) =>
            {
                if (!_usecode.InUsecode && !_usecode.WaitingForChoice)
                {
                    _usecode.Call(fun, item, (UsecodeEvent)eventId);
                    _conversation.Refresh();
                }
            };
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
            _schedules.Start(restore: U7Paths.GameDatOverride is not null);
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
                    // Exult's cheat teleport.
                    TeleportParty(tile);
                }
            }
            else if (_walkPress)
            {
                StartActor(world);
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
            _suppressWalk = false;
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

    public override void _UnhandledInput(InputEvent @event)
    {
        // Exult's flash_shape holds the game (SDL_Delay): nothing answers the mouse or keys meanwhile.
        if (!_ready || _cursor.Holding || _newGame is not null)
        {
            return;
        }

        if (_endgame is { } endgame)
        {
            EndgameInput(endgame, @event);
            return;
        }

        if (@event is InputEventMouseButton mb)
        {
            if (mb is { ButtonIndex: MouseButton.Right, Pressed: false })
            {
                // Letting go ends a right-button walk or gump click, whatever came up meanwhile.
                RightButtonUp(_gumpView.MouseVirtual(), MouseWorld());
                GetViewport().SetInputAsHandled();
                return;
            }

            if (HandleBookClick(mb))
            {
                return;
            }

            var virt = _gumpView.MouseVirtual();
            if (mb is { ButtonIndex: MouseButton.Right, Pressed: true })
            {
                RightButtonDown(virt, MouseWorld());
                GetViewport().SetInputAsHandled();
            }
            else if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed)
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
            else if (mb is { ButtonIndex: MouseButton.Left, Pressed: true })
            {
                _walkPress = false;
                if (mb.DoubleClick)
                {
                    // Exult: a double-click names nothing.
                    _showItemsMsec = 0;
                }

                if (HandleClickOnItem(virt.X, virt.Y))
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

                if (_gumps.OnMouseDown(_gumpView, virt.X, virt.Y, mb.DoubleClick))
                {
                    _suppressWalk = true;
                    GetViewport().SetInputAsHandled();
                    return;
                }

                if (mb.DoubleClick)
                {
                    _suppressWalk = true;
                    ActivateUnderMouse();
                    GetViewport().SetInputAsHandled();
                    return;
                }

                var worldObj = _world.PickObject(_camera.GetGlobalMousePosition());
                if (worldObj is not null)
                {
                    WorldView.ShapeLocation(worldObj.Tx, worldObj.Ty, worldObj.Tz, out var hx, out var hy);
                    var hot = WorldToVirtual(new Vector2(hx, hy));
                    _gumps.OnWorldMouseDown(worldObj, virt.X, virt.Y, hot.X, hot.Y);
                    _suppressWalk = true;
                    GetViewport().SetInputAsHandled();
                }
                else
                {
                    _walkPress = true;
                    _walkPressMsec = Time.GetTicksMsec();
                }
            }
            else if (!mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                if (_gumps.Drag is { } drag)
                {
                    var world = _camera.GetGlobalMousePosition();
                    _gumps.OnMouseUp(_gumpView, virt.X, virt.Y);
                    _suppressWalk = true;
                    GetViewport().SetInputAsHandled();
                    // Exult Dragging_info::drop: a thing or gump clicked, not moved, leaves the click to name it,
                    // half a second later unless it was the first of a double-click.
                    if (drag is { Moved: false, Button: null } && CombatSchedule.CanAct(_avatar.Avatar) && !UsecodeRunning)
                    {
                        _showItemsMsec = Time.GetTicksMsec() + 500;
                        _showItemsVirt = virt;
                        _showItemsWorld = world;
                    }
                }
                else if (_walkPress)
                {
                    EndWalkPress();
                }
            }
        }
        else if (@event is InputEventMouseMotion)
        {
            var virt = _gumpView.MouseVirtual();
            if (_rightOnGump && _gumps.FindGump(virt.X, virt.Y, _gumpView) is null)
            {
                _rightOnGump = false;
            }

            if (_gumps.Drag is not null)
            {
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

            if (_usecode is { Wait: UsecodeWait.Picture } viewer &&
                key.Keycode is Key.Escape or Key.Space or Key.Enter or Key.KpEnter)
            {
                viewer.ClosePicture();
                _conversation.Refresh();
                GetViewport().SetInputAsHandled();
                return;
            }

            // Exult Wizard_eye: Esc closes the eye; no other key does anything.
            if (_usecode is { Wait: UsecodeWait.WizardEye } looker)
            {
                if (key.Keycode == Key.Escape)
                {
                    looker.EndWizardEye();
                    _conversation.Refresh();
                }

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
                    DebugDie(restart: key.ShiftPressed);
                    break;
                case Key.F5:
                    try
                    {
                        SaveGame.Write(SaveGame.QuickSlot, _map, _npcs, _usecode, _clock, _combat.InCombat, _music, _combat.Spawned, _combat.Armageddon);
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

    /// <summary>The gumps' virtual pixels (the screen at the world's zoom) for a world pixel, and back.</summary>
    Vector2I WorldToVirtual(Vector2 world) =>
        (Vector2I)(world - _camera.GlobalPosition + GetViewport().GetVisibleRect().Size / (2 * _zoom)).Floor();

    Vector2 VirtualToWorld(Vector2 virt) => virt + _camera.GlobalPosition - GetViewport().GetVisibleRect().Size / (2 * _zoom);

    /// <summary>
    /// Exult <c>Dragging_info::drop_on_map</c>: a thing let go on the map.
    /// Off the screen it is refused. Dropped on something, that may take it
    /// (<see cref="DropOn"/>), else it is set on top if that is no higher than
    /// 5 lifts over the avatar (the red X if the thing is taller than that,
    /// "blocked" if only too high). Otherwise it goes down where it is shown
    /// (<see cref="DropAtLift"/>), from the lift it was picked up at upwards,
    /// up to 5 over the avatar and under the roof hidden over it.
    /// </summary>
    MapDrop DropOnMap(U7Object obj, DragState drag)
    {
        var screen = GetViewport().GetVisibleRect().Size / _zoom;
        if (drag.MouseX < 0 || drag.MouseY < 0 || drag.MouseX >= screen.X || drag.MouseY >= screen.Y)
        {
            _cursor.Flash(MouseShape.RedX);
            return MapDrop.Refused;
        }

        var av = _avatar.Avatar;
        var maxLift = Math.Min(av.Tz + 5, _world.SkipAboveLift - 1);
        var paint = VirtualToWorld(new Vector2(drag.PaintX, drag.PaintY));
        var dropped = 0;
        if (_world.PickObject(VirtualToWorld(new Vector2(drag.MouseX, drag.MouseY))) is { } found && found != obj)
        {
            if (!CheckWeight(obj, found))
            {
                return MapDrop.Refused;
            }

            if (DropOn(found, obj))
            {
                return MapDrop.Taken;
            }

            var height = _catalog[found.Shape].DimZ;
            if (found.Tz + height <= maxLift)
            {
                dropped = DropAtLift(obj, paint, found.Tz + height);
            }
            else
            {
                _cursor.Flash(height > maxLift ? MouseShape.RedX : MouseShape.Blocked);
                return MapDrop.Refused;
            }
        }

        var oldLift = drag.FromWorld ? drag.OldTz : Inventory.Outermost(drag.OldContainer ?? av).Tz;
        for (var lift = oldLift; dropped == 0 && lift <= maxLift; lift++)
        {
            dropped = DropAtLift(obj, paint, lift);
        }

        if (dropped <= 0)
        {
            _cursor.Flash(MouseShape.RedX);
            return MapDrop.Refused;
        }

        return MapDrop.Placed;
    }

    /// <summary>
    /// Exult <c>Game_window::drop_at_lift</c>: the tile under the thing's
    /// painted spot at that lift, where it would rest (it falls up to 5
    /// lifts, Exult <c>is_blocked</c> over its footprint) if the avatar can
    /// reach it; it lands there and the eggs under it hatch. 1 if dropped.
    /// (Exult also refuses a spot hidden behind a wall, judged by what is
    /// painted over it; here only the reach decides.)
    /// </summary>
    int DropAtLift(U7Object obj, Vector2 paint, int atLift)
    {
        var tx = U7Constants.WrapTile(Mathf.FloorToInt((paint.X + atLift * 4 - 1) / U7Constants.TileSize));
        var ty = U7Constants.WrapTile(Mathf.FloorToInt((paint.Y + atLift * 4 - 1) / U7Constants.TileSize));
        if (_map.Blocking.IsBlockedArea(_catalog[obj.Shape].DimZ, atLift, tx - obj.DimX + 1, ty - obj.DimY + 1,
                obj.DimX, obj.DimY, out var lift, MoveFlags.Walk, maxDrop: 5) ||
            !FastPathClient.IsGrabable(_map, _avatar.Avatar, new TileCoord(tx, ty, lift)))
        {
            return 0;
        }

        _map.PlaceInWorld(obj, tx, ty, lift);
        _eggs.ActivateSomethingOn(obj);
        return 1;
    }

    /// <summary>
    /// Exult <c>Game_object::drop</c> (and <c>Actor::drop</c>): a party member
    /// takes what is dropped on it; a stack of the same kind (any frame, for
    /// the shapes with pile frames) takes it in if they make no more than 100.
    /// </summary>
    bool DropOn(U7Object found, U7Object obj)
    {
        if (found.IsActor)
        {
            return found.GetFlag(ObjFlag.InParty) && Equipment.AddToActor(found, obj, _catalog, _map);
        }

        var info = _catalog[found.Shape];
        if (found.Shape != obj.Shape || !info.HasQuantity ||
            (!ItemQuantity.HasQuantityFrames(found.Shape) && found.Frame != obj.Frame))
        {
            return false;
        }

        var quantity = Inventory.GetQuantity(obj, _catalog);
        if (Inventory.GetQuantity(found, _catalog) + quantity > U7Constants.MaxQuantity)
        {
            return false;
        }

        _combat.Quantities.Modify(found, quantity, out _);
        _map.RemoveObject(obj);
        return true;
    }

    /// <summary>Exult <c>Check_weight</c>: a party member (or what one carries) takes no more than it can carry.</summary>
    bool CheckWeight(U7Object obj, U7Object onto)
    {
        var owner = Inventory.Outermost(onto);
        if (!owner.GetFlag(ObjFlag.InParty) ||
            (Inventory.GetWeight(owner, _catalog) + Inventory.GetWeight(obj, _catalog)) / 10 <= Inventory.GetMaxWeight(owner))
        {
            return true;
        }

        _cursor.Flash(MouseShape.TooHeavy);
        return false;
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
    /// Letting go of the left button after it went down on open ground. The
    /// left button walks too, the user's pick (2026-10-09) besides Exult's
    /// right button: a quick click finds a path (Exult
    /// <c>start_actor_along_path</c>), letting go after holding stops
    /// (<c>stop_actor</c>). Exult's left click on open ground names nothing.
    /// </summary>
    void EndWalkPress()
    {
        _walkPress = false;
        var canWalk = !UsecodeRunning && !_gumps.GumpMode && !_avatar.Avatar.IsDead && !ObjFlag.DontMoveMode(_avatar.Avatar);
        if (!canWalk || Input.IsKeyPressed(Key.Shift))
        {
            return;
        }

        if (Time.GetTicksMsec() - _walkPressMsec < QuickClickMsec)
        {
            StartActorAlongPath(_camera.GetGlobalMousePosition());
        }
        else
        {
            StopActor();
        }
    }

    const ulong QuickClickMsec = 300;

    /// <summary>Exult: two right clicks let go within half a second are a double-click.</summary>
    const ulong DoubleRightMsec = 500;

    /// <summary>Usecode runs or waits for an answer (Exult's conversations hold its event loop).</summary>
    bool UsecodeRunning => _usecode is { InUsecode: true } or { WaitingForChoice: true };

    /// <summary>The console's mouse: the view's centre plus this, in world pixels (a held mouse stays put as the view moves).</summary>
    Vector2? _agentMouse;
    /// <summary>The console holds the right button.</summary>
    bool _agentRightHeld;

    Vector2 MouseWorld() => _agentMouse is { } m ? _camera.GlobalPosition + m : _camera.GetGlobalMousePosition();

    bool RightHeld => _agentRightHeld || Input.IsMouseButtonPressed(MouseButton.Right);

    /// <summary>
    /// Exult <c>start_actor</c>: the avatar heads for the cursor, aiming a few
    /// tiles ahead; in barge mode the barge does.
    /// </summary>
    void StartActor(Vector2 world)
    {
        if (_barges.Moving is { } barge)
        {
            SteerBarge(barge, WorldView.WorldToTile(world, _avatar.Avatar.Tz), MouseWalkSpeed(world));
            _bargeMouse = true;
        }
        else
        {
            _avatar.Steer(world, MouseWalkSpeed(world));
        }
    }

    /// <summary>Exult <c>start_actor_along_path</c>: an A* walk to the tile under the cursor ("For now, don't do barges").</summary>
    void StartActorAlongPath(Vector2 world)
    {
        if (_barges.Moving is null)
        {
            _avatar.PathTo(WorldView.WorldToTile(world, _avatar.Avatar.Tz), MouseWalkSpeed(world));
        }
    }

    /// <summary>Exult <c>stop_actor</c>: the barge in barge mode, else the avatar.</summary>
    void StopActor()
    {
        _bargeMouse = false;
        if (_barges.Moving is { } barge)
        {
            barge.Stop();
        }
        else
        {
            _avatar.Stop();
        }
    }

    /// <summary>
    /// Exult's right button going down: on a gump in gump mode it marks the
    /// gump to close when let go (<c>right_on_gump</c>, Exult's default
    /// <c>right_click_closes_gumps</c>); elsewhere the avatar heads for the
    /// cursor (<c>start_actor</c>), and holding the button keeps it going.
    /// </summary>
    void RightButtonDown(Vector2I virt, Vector2 world)
    {
        if (ObjFlag.DontMoveMode(_avatar.Avatar) || UsecodeRunning)
        {
            return;
        }

        if (_gumps.Drag is null && _gumps.GumpMode && _gumps.FindGump(virt.X, virt.Y, _gumpView) is not null)
        {
            _rightOnGump = true;
        }
        else if (CombatSchedule.CanAct(_avatar.Avatar) && !_gumps.GumpMode && _gumps.Drag is null)
        {
            _rightWalk = true;
            StartActor(world);
        }
    }

    /// <summary>
    /// Exult's right button let go: in gump mode the gump it went down on
    /// closes, if the mouse is still on it; otherwise the avatar stops
    /// (<c>stop_actor</c>), or, let go a second time within half a second,
    /// walks a path to the spot (Exult's default <c>allow_right_pathfind</c>,
    /// "double": <c>start_actor_along_path</c>).
    /// </summary>
    void RightButtonUp(Vector2I virt, Vector2 world)
    {
        var now = Time.GetTicksMsec();
        var walking = _rightWalk;
        _rightWalk = false;
        if (_gumps.GumpMode)
        {
            if (_rightOnGump && !UsecodeRunning && _gumps.FindGump(virt.X, virt.Y, _gumpView) is { } gump)
            {
                gump.Close();
            }
        }
        else if (!ObjFlag.DontMoveMode(_avatar.Avatar) && !UsecodeRunning && CombatSchedule.CanAct(_avatar.Avatar))
        {
            if (now - _lastRightUpMsec < DoubleRightMsec)
            {
                StartActorAlongPath(world);
            }
            else
            {
                StopActor();
            }
        }
        else if (walking)
        {
            StopActor();
        }

        _rightOnGump = false;
        _lastRightUpMsec = now;
    }

    /// <summary>
    /// Exult <c>Game_window::show_items</c>: a left click names what it hit;
    /// in a gump the thing under the mouse, else the gump's container or
    /// actor; in the world the thing on top. Nothing on open ground.
    /// </summary>
    U7Object? ShowItems(Vector2I virt, Vector2 world)
    {
        if (UsecodeRunning)
        {
            return null;
        }

        var gump = _gumps.FindGump(virt.X, virt.Y, _gumpView);
        var obj = gump is not null ? gump.FindObject(_gumpView, virt.X, virt.Y) ?? gump.ContOrActor : _world.PickObject(world);
        if (obj is not null)
        {
            ShowName(obj);
        }

        return obj;
    }

    /// <summary>
    /// Exult <c>show_items</c>' text over a thing: its name (Exult
    /// <c>Get_object_name</c>: the avatar is "yourself"), "Oink!" for the avatar
    /// and things after the failed copy protection. As Exult's
    /// <c>add_text</c>, a thing already showing a text keeps it.
    /// </summary>
    string ShowName(U7Object obj)
    {
        var av = _avatar.Avatar;
        var name = obj == av ? TextMessages.MiscName(TextMessages.Yourself) : ObjectNames.Get(obj, _catalog);
        if (_usecode is { FailedCopyProtection: true } && (obj == av || !obj.IsActor))
        {
            name = UsecodeMachine.Oink;
        }

        if (name.Length > 0 && (obj.BarkText.Length == 0 || obj.BarkUntilMsec < Time.GetTicksMsec()))
        {
            obj.Bark(name);
        }

        return name;
    }

    /// <summary>Exult <c>Mouse::set_speed_cursor</c>'s speed for the cursor at this world point.</summary>
    int MouseWalkSpeed(Vector2 world) => SpeedCursor(world).Speed;

    /// <summary>
    /// Exult <c>Mouse::set_speed_cursor</c>: the arrow and the walking speed
    /// for the cursor at this world point, measured from the avatar, or from
    /// the centre of the barge in barge mode.
    /// </summary>
    (int Arrow, int Speed) SpeedCursor(Vector2 world)
    {
        int ax, ay;
        if (_barges.Moving is { } barge)
        {
            WorldView.ShapeLocation(barge.Obj.Tx, barge.Obj.Ty, barge.Obj.Tz, out ax, out ay);
            ax -= barge.Obj.BargeXTiles * (U7Constants.TileSize / 2);
            ay -= barge.Obj.BargeYTiles * (U7Constants.TileSize / 2);
        }
        else
        {
            var av = _avatar.Avatar;
            WorldView.ShapeLocation(av.Tx, av.Ty, av.Tz, out ax, out ay);
        }

        var game = GetViewport().GetVisibleRect().Size / _zoom;
        return WalkSpeed.SpeedCursor(new Vector2(ax, ay), world, game, _combat.InCombat, HostileNearby(),
            AvatarNoHaltScript());
    }

    /// <summary>
    /// Exult's cursor: <c>Get_click</c>'s shape while usecode waits for a
    /// click (the hand for a conversation, book, sign or picture, the
    /// crosshair for a target), the Wizard Eye's short arrows from the
    /// screen's centre; otherwise <c>set_speed_cursor</c>: the hand while
    /// usecode runs the avatar, in gump mode or dragging, else the walking
    /// arrow.
    /// </summary>
    void UpdateCursor()
    {
        _cursor.Zoom = _zoom;
        var view = GetViewport();
        if (CursorFor(_camera.GetGlobalMousePosition(), view.GetMousePosition()) is { } shape)
        {
            _cursor.Shape = shape;
        }
    }

    /// <summary>The cursor for the mouse at this world (and screen) point; null to leave it (Exult <c>dontchange</c>).</summary>
    int? CursorFor(Vector2 world, Vector2 screen)
    {
        switch (_usecode?.Wait)
        {
            case UsecodeWait.Fade or UsecodeWait.Flash:
                return null;
            case UsecodeWait.ClickOnItem:
                return MouseShape.GreenSelect;
            case UsecodeWait.WizardEye:
            {
                var c = GetViewport().GetVisibleRect().Size / 2;
                return MouseShape.ShortArrows + ActorWalker.DirectionNoWrap((int)(c.Y - screen.Y), (int)(screen.X - c.X));
            }
            case not (UsecodeWait.None or null):
                return MouseShape.Hand;
        }

        return ObjFlag.DontMoveMode(_avatar.Avatar) || _gumps.GumpMode || _gumps.IsDragging
            ? MouseShape.Hand
            : SpeedCursor(world).Arrow;
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
            var aim = new Vector2(ax + 50 * x, ay + 50 * y);
            var speed = WalkSpeed.Keyboard(Input.IsKeyPressed(Key.Shift), _combat.InCombat, HostileNearby());
            if (_barges.Moving is { } barge)
            {
                SteerBarge(barge, WorldView.WorldToTile(aim, av.Tz), speed);
            }
            else
            {
                _avatar.Steer(aim, speed);
            }

            _keyWalking = true;
        }
        else if (_keyWalking)
        {
            _keyWalking = false;
            if (_barges.Moving is { } barge)
            {
                barge.Stop();
            }
            else
            {
                _avatar.Stop();
            }
        }
    }

    /// <summary>
    /// Exult <c>start_actor</c> in barge mode: the barge goes so that its
    /// centre heads for the tile, twice as fast as walking.
    /// </summary>
    static void SteerBarge(Barge barge, TileCoord tile, int speedMs) =>
        barge.TravelTo(new TileCoord(
            tile.Tx + barge.Obj.Tx - barge.Center.Tx, tile.Ty + barge.Obj.Ty - barge.Center.Ty, barge.Obj.Tz), speedMs / 2);

    double _eyeStep;

    /// <summary>
    /// Exult <c>Wizard_eye</c>'s loop: every 50 ms, with the right button held,
    /// the eye moves a tile towards the mouse (<c>Shift_wizards_eye</c>, eight
    /// directions from the screen's centre); the time running out closes it.
    /// </summary>
    void UpdateWizardEye(double delta)
    {
        _eyeStep += delta;
        while (_eyeStep >= 0.05)
        {
            _eyeStep -= 0.05;
            if (Input.IsMouseButtonPressed(MouseButton.Right))
            {
                var centre = GetViewport().GetVisibleRect().Size / 2;
                var m = GetViewport().GetMousePosition();
                var dir = ActorWalker.DirectionNoWrap((int)(centre.Y - m.Y), (int)(m.X - centre.X));
                _usecode!.MoveWizardEye(EyeDeltas[2 * dir], EyeDeltas[2 * dir + 1]);
            }
        }

        _usecode!.UpdateWizardEye(delta);
        if (_usecode.Wait != UsecodeWait.WizardEye)
        {
            _conversation.Refresh();
        }
    }

    /// <summary>Exult <c>Shift_wizards_eye</c>'s steps by direction (north clockwise).</summary>
    static readonly int[] EyeDeltas = [0, -1, 1, -1, 1, 0, 1, 1, 0, 1, -1, 1, -1, 0, -1, -1];

    /// <summary>
    /// Exult <c>Get_click</c> while a book page or a picture (the map) is
    /// shown: releasing the left button turns the page or closes the picture,
    /// and no other click reaches the game. Only a press made while reading
    /// counts, not the double-click that opened the book.
    /// </summary>
    bool HandleBookClick(InputEventMouseButton mb)
    {
        // Exult Wizard_eye: the buttons only steer the eye (held right button, UpdateWizardEye).
        if (_usecode is { Wait: UsecodeWait.WizardEye })
        {
            _suppressWalk = true;
            GetViewport().SetInputAsHandled();
            return true;
        }

        var reading = _usecode is { Wait: UsecodeWait.BookPage or UsecodeWait.Picture };
        if (mb.ButtonIndex == MouseButton.Left)
        {
            var pressedWhileReading = _bookPress;
            _bookPress = mb.Pressed && reading;
            if (reading && !mb.Pressed && pressedWhileReading)
            {
                if (_usecode!.Wait == UsecodeWait.Picture)
                {
                    _usecode.ClosePicture();
                }
                else
                {
                    _usecode.TurnBookPage();
                }

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

    bool HandleClickOnItem(int mx, int my)
    {
        if (_usecode is not { Wait: UsecodeWait.ClickOnItem })
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

        // Exult Game_window::double_clicked: a thing (not an actor) out of the
        // avatar's reach is refused, and an avatar that cannot act uses nothing.
        if (!obj.IsActor && !FastPathClient.IsGrabable(_map, _avatar.Avatar, obj))
        {
            _statusExtra = "blocked";
            _cursor.Flash(MouseShape.Blocked);
            return;
        }

        if (!CombatSchedule.CanAct(_avatar.Avatar))
        {
            return;
        }

        if (_combat.InCombat && CombatClick(obj))
        {
            return;
        }

        // Exult Actor::activate: the avatar shows its inventory, NPCs and monsters run their usecode.
        if (obj.IsActor && obj.NpcNum != 0)
        {
            if (!ShowPartyInventory(obj))
            {
                RunUsecode(obj);
            }

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

    /// <summary>
    /// Exult <c>Actor::activate</c>'s <c>show_party_inv</c>: with gumps open or
    /// in combat, a party member shows its inventory instead of talking.
    /// </summary>
    bool ShowPartyInventory(U7Object npc)
    {
        if (!_party.IsInParty(npc) || !(_gumps.ShowingGumps || _combat.InCombat))
        {
            return false;
        }

        _gumps.ShowInventory(npc);
        return true;
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
