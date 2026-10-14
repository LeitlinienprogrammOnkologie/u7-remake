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
    MapView _mapView = null!;
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
    /// <summary>A show on the endgame's screen: the endgame (once it runs, the game is over), or the menu's shows.</summary>
    Show? _endgame;
    /// <summary>Exult <c>BG_Game::new_game</c>'s screen while it is shown: the game waits for the avatar's name.</summary>
    NewGameView? _newGame;
    MusicPlayer _music = null!;
    SoundEffects _sfx = null!;
    bool _quaking;
    SpeechPlayer _speech = null!;
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
    /// <summary>The status overlay (the clock, the keys, what is under the cursor) shows; F1 toggles it, kept in settings.cfg.</summary>
    bool _overlayOn = true;
    int _lastSchunk = -1;
    (int Cx, int Cy) _lastChunk = (-1, -1);
    string _statusExtra = "";

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.Nearest;
        try
        {
            U7Paths.Initialize();
            SaveGame.MoveLegacySaves();
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

            ObjectNames.Npc = n => (uint)n < (uint)_npcs.Count ? _npcs[n] : null;
            _clock = new GameClock();
            var gwin = SaveGame.ReadGwin();
            if (gwin is { } g0)
            {
                _clock.Set(g0.Day, g0.Hour, g0.Minute);
                _clock.SpecialLight = g0.SpecialLight;
                _clock.AmbientLight = g0.AmbientLight;
                _clock.Infravision = g0.Infravision;
            }

            var schedTable = ScheduleTable.Load();
            _schedules = new ScheduleRunner(_map, avatar, _npcs, schedTable, _clock);
            var usecodeDat = UsecodeDat.Read();
            _party = new PartyManager(_map, avatar, _npcs) { Schedules = _schedules };
            _party.LinkParty(usecodeDat?.Party);
            _schedules.Party = _party;
            _schedules.AvatarWalk = () => _avatar.Walk;
            _combat = new CombatEngine(_map, avatar, _catalog);
            _music = new MusicPlayer();
            var sfxMixer = new SfxMixer { Name = "SfxMixer" };
            AddChild(sfxMixer);
            _sfx = new SoundEffects(_music, sfxMixer, Settings.Get("audio", "sfx_pack", "auto").AsString())
            {
                Enabled = Settings.Get("audio", "effects", true).AsBool()
            };
            _combat.Sfx = _sfx;
            _speech = new SpeechPlayer { Name = "Speech" };
            AddChild(_speech);
            _eggs = new EggHatcher(_map, _clock) { Combat = _combat, Music = _music, Party = _party, Sfx = _sfx };
            _avatar.WalkStarted = () =>
            {
                _cameraTile = null;
                _party.CallFollowers(follow: false);
            };
            _avatar.FollowersCalled = () =>
            {
                // Exult stop_actor leaves the party be in gump mode.
                if (!_gumps.GumpMode)
                {
                    _party.CallFollowers();
                }
            };
            _avatar.Moved = (actor, fromTx, fromTy) =>
            {
                _eggs.Activate(actor, fromTx, fromTy);
                if (_avatar.LeadsParty)
                {
                    _party.AvatarStepped(fromTx, fromTy);
                }
            };
            _combat.Schedules = _schedules;
            _schedules.Combat = _combat;
            _schedules.AvatarMoved = (actor, fromTx, fromTy) => _eggs.Activate(actor, fromTx, fromTy);
            _schedules.AvatarLedParty = (fromTx, fromTy) => _party.AvatarStepped(fromTx, fromTy);
            _schedules.AvatarWalkEnded = () => _party.CallFollowers();
            _schedules.AvatarBusy = () => _avatar.IsPlayerMoving;
            _combat.Party = _party;
            _combat.Music = _music;
            var monsters = NpcDat.LoadMonsters(_map);
            _combat.AdoptMonsters(monsters);
            _timers = new NpcTimers(_schedules, _combat, _clock);
            _clock.HourTicked += () => _timers.HourPassed(_npcs);
            _timers.Restore(_npcs.Concat(monsters));
            _combat.Timers = _timers;
            _fields = new Fields(_map, _combat, _timers, _schedules);
            ActorWalker.Stepped = actor =>
            {
                _fields.Stepped(actor);
                _timers.SteppedOn(actor);
            };
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

            _effects = new EffectsManager(_shapes.SpritesVga, _clock) { InDungeon = () => _lighting.InDungeon, Sfx = _sfx };
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
                Animators = new Animators(_map, _clock) { Sfx = _sfx },
                Art = new HiresArt(_shapes)
            };
            ArtLook.RedrawnPaperdolls = Settings.Get("art", "redrawn_paperdolls", false).AsBool();
            ArtLook.PaintedFaces = Settings.Get("art", "painted_faces", true).AsBool();
            ArtLook.TrueColour = Settings.Get("art", "true_colour", ArtLook.TrueColour).AsBool();
            ArtLook.Pack = Settings.Get("art", "pack", ArtLook.Pack ?? "original").AsString() is var pack && pack != "original" ? pack : null;
            ArtLook.Renderer = Settings.Get("art", "renderer", ArtLook.Renderer).AsString();
            ArtLook.StoreMb = Settings.Get("art", "store_mb", ArtLook.StoreMb).AsInt32();
            ArtLook.Modern = Settings.Get("art", "modern", ArtLook.Modern).AsBool();
            ArtLook.LutSize = Settings.Get("art", "lut", ArtLook.LutSize).AsInt32();
            ArtLook.Occlusion = Settings.Get("art", "occlusion", ArtLook.Occlusion).AsBool();
            ArtLook.Bloom = (float)Settings.Get("art", "bloom", ArtLook.Bloom).AsDouble();
            ArtLook.BloomRadius = (float)Settings.Get("art", "bloom_radius", ArtLook.BloomRadius).AsDouble();
            ArtLook.GumpPalette = Settings.Get("art", "gump_palette", ArtLook.GumpPalette).AsBool();
            _world.Art.UsePack(ArtLook.Pack, ArtLook.Scale);
            _shapes.Hires = _world.Art;
            AddChild(_world);
            _lighting = new SceneLighting(_clock, _map, _catalog, avatar, _effects, _party, new GlowColours(_shapes, _catalog))
            {
                Missiles = _combat.Missiles,
                HomingMissiles = _combat.HomingMissiles
            };
            _world.Lighting = _lighting;
            // Rain, snow and sparkles over the world, under the gumps and the screen effects.
            AddChild(new WeatherView { Name = "WeatherView", Effects = _effects, Lighting = _lighting });

            _gumps = new GumpManager(_map, avatar) { Sfx = _sfx };
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
            _gumps.OpenFileMenu = OpenSaveLoad;
            _gumps.AskQuantity = AskQuantity;
            _gumps.AlternateDrop = () => Input.IsKeyPressed(Key.Ctrl);
            _gumps.IsCombatOn = () => _combat.InCombat;
            _gumps.GumpsVga = _shapes.GumpsVga;
            _gumps.PaperdollsAvailable = _shapes.Paperdolls is not null;
            _gumps.Weapons = _combat.Weapons;
            _gumps.Ammo = _combat.Ammo;

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
                World = _world,
                TextureFilter = TextureFilterEnum.Nearest
            };
            gumpLayer.AddChild(_gumpView);
            // The painted map over the gumps, drawn smooth (the gumps stay pixel-crisp).
            _mapView = new MapView { Name = "MapView" };
            gumpLayer.AddChild(_mapView);

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

            // The status overlay and the usecode lines under it wrap at the window's width (set each frame).
            _overlayOn = Settings.Get("ui", "overlay", true).AsBool();
            _hud = new Label
            {
                Name = "Status",
                Position = new Vector2(12, 8),
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                Visible = _overlayOn,
                TextureFilter = TextureFilterEnum.Nearest
            };
            _hud.AddThemeColorOverride("font_color", new Color(0.95f, 0.9f, 0.7f));
            layer.AddChild(_hud);

            _debug = new Label
            {
                Name = "UsecodeDebug",
                Position = new Vector2(12, 90),
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                Visible = false,
                TextureFilter = TextureFilterEnum.Nearest
            };
            _debug.AddThemeColorOverride("font_color", new Color(0.55f, 0.95f, 0.7f));
            layer.AddChild(_debug);

            var usecodeFile = UsecodeFile.Load();
            GD.Print($"USECODE loaded: {usecodeFile.Count} functions.");
            _usecode = new UsecodeMachine(usecodeFile, _map, avatar) { Sfx = _sfx };
            _usecode.Frame8 = _shapes.GetFrame8;
            UsecodeVars.Read(_usecode);
            if (!GameContext.IsBg)
            {
                _usecode.Keyring.UnionWith(SaveGame.ReadKeyring());
            }

            if (usecodeDat?.SavedPos is { Tx: >= 0 } saved)
            {
                _usecode.SavedPos = saved;
            }

            // Exult Actor::read: a function a save assigned by name runs through UCC's table (find_function).
            foreach (var npc in _npcs)
            {
                if (npc?.UsecodeName is { } fname)
                {
                    npc.AssignedUsecode = usecodeFile.Symbols?.Find(fname)?.Value ?? -1;
                }
            }
            foreach (var (tnum, hours) in usecodeDat?.Timers ?? [])
            {
                _usecode.Timers[tnum] = hours;
            }

            // Exult Game_window::read, for Black Gate: until the first scene (global
            // flag 0x3b) has played, the avatar is invisible and under usecode control.
            if (GameContext.IsBg)
            {
                if (_usecode.GFlags[UsecodeMachine.DidFirstSceneFlag] == 0)
                {
                    avatar.SetFlag(ObjFlag.DontMove);
                }
                else
                {
                    avatar.ClearFlag(ObjFlag.DontMove);
                }
            }

            _usecode.Gumps = _gumps;
            _usecode.FlashMouse = _cursor.Flash;
            _usecode.RunEndgame = StartEndgame;
            _usecode.Npcs = _npcs;
            _usecode.Clock = _clock;
            _usecode.Schedules = _schedules;
            _usecode.Party = _party;
            _usecode.Speech = _speech;
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
                CenterView(_avatar.Avatar);
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
            _usecode.CenterView = t => _cameraTile = t;
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
            PathWalk.ActivateDoor = door => TryCallUsecode(_usecode!.GetItemFun(door), door, UsecodeEvent.DoubleClick);
            PathWalk.IsSentient = _combat.IsSentient;
            _combat.WeaponUsecode = (fun, target) => _usecode.Call(fun, target, UsecodeEvent.Weapon);
            _combat.DoSpeech = _usecode.DoSpeech;
            _combat.DeathUsecode = (npc, finish) => _usecode.WhenDone(() =>
            {
                _usecode.Call(_usecode.GetShapeFun(npc.Shape), npc, UsecodeEvent.InternalExec);
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
                var fun = npc.GetUsecode() >= 0 ? npc.GetUsecode() : _usecode!.GetShapeFun(npc.Shape);
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
            _schedules.Activate = (obj, eventId) =>
            {
                if (UsecodeRunning)
                {
                    return false;
                }

                RunUsecode(obj, (UsecodeEvent)eventId); // Exult Game_object::activate.
                return true;
            };
            _schedules.CallUsecode = (fun, item) => TryCallUsecode(fun, item, UsecodeEvent.DoubleClick);
            _schedules.CastBookmark = CastBookmark;
            _schedules.ScriptLength = npc => _usecode.Scripts.LastOrDefault(s => s.Obj == npc)?.Count ?? 0;
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
            _gumpView.ShownPicture = () => _usecode is { Wait: UsecodeWait.Picture or UsecodeWait.WizardEye, Picture: { } shown } &&
                                           !_mapView.Paints(shown) ? shown : null;
            _mapView.ShownPicture = () => _usecode is { Wait: UsecodeWait.Picture } vm ? vm.Picture : null;
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

            // Exult Game::show_menu: a start without a game to load shows the main menu, whose new game
            // asks for the avatar's name and sex first; the opening (the eggs round the avatar) waits for
            // them. The console names it itself (avatar), and opens the menu with `menu`.
            // Serpent Isle's shows (sigame.cc) are docs/SERPENT_ISLE.md's M7: until then it starts in the world.
            if (U7Paths.GameDatOverride is null && GameContext.IsBg &&
                string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("U7_AGENT")))
            {
                ShowIntroThenMenu();
            }
            else
            {
                _eggs.Activate(avatar, -1, -1);
            }

            AgentInit();

            _ready = true;
            GD.Print(GameContext.IsSi ? "Serpent Isle loaded." : "Britannia loaded.");
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
        _sfx?.Dispose();
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

        // Exult BG_Game::new_game and the main menu: nothing else runs meanwhile.
        if (_newGame is not null || _mainMenu is not null)
        {
            Vector2I? noWalk = null;
            AgentUpdate(delta, ref noWalk);
            return;
        }

        // Exult Newfile_gump and Slider_gump: modal gumps; the time queue is paused while they show.
        if (_saveLoad is not null || _quantityView is not null)
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
            if (endgame.Done)
            {
                ShowDone(endgame);
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
        // Target mode holds the game as a usecode wait for a click does (Exult's Get_click pauses the time queue).
        var inUsecode = UsecodeRunning || _targetMode;
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

        // Exult's time queue, paused in target mode: the clock and the usecode scripts stand still.
        if (!_targetMode)
        {
            _clock.Update(delta);
            _usecode?.UpdateFade(delta);
            _usecode?.TickScripts(delta);
        }

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
        _world.RotateColors = !fadedOut && !_targetMode && _usecode is not { Wait: UsecodeWait.ClickOnItem or UsecodeWait.Picture };

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
        // The sound effects: their distances from the view's centre, the delayed ones, Exult's background noise.
        _sfx.ViewCentre = focus;
        _sfx.Update(delta, outside: !_lighting.Inside && !_lighting.InDungeon, night: _clock.Hour < 6 || _clock.Hour > 20,
            musicPlaying: _music.CurrentTrack != -1);

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
        // The overlay wraps inside the window, the usecode lines placed under it.
        var width = Math.Max(64, GetViewport().GetVisibleRect().Size.X - 2 * hud.Position.X);
        hud.Size = new Vector2(width, 0);
        _debug.Position = new Vector2(hud.Position.X, hud.Position.Y + hud.Size.Y + 4);
        _debug.Size = new Vector2(width, 0);
        hud.Text =
            $"Ultima VII  {_clock.HudText()}  tile {av.Tx},{av.Ty}  lift {av.Tz}  zoom {_zoom:0.#}×\n" +
            $"WASD/arrows walk · I inventory · C combat · T target · F4 invincible · [ ] hour · right or left button walk · click name · double-click / E · F1 overlay · F2 debug · F3 arena · PgUp/PgDn lift · M music · F5/F9 save/load · F6 die · F7 paperdoll art · Home Trinsic\n" +
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
    /// <summary>
    /// Exult <c>Game_window::center_view</c>: the window is over the place at
    /// once, so usecode asking what is on screen (<c>npc_nearby</c>) in the
    /// same script sees the new place; the camera follows next frame.
    /// </summary>
    void CenterView(U7Object at)
    {
        var view = _combat.ViewTiles;
        _combat.ViewTiles = view with { Position = new Vector2I(at.Tx - view.Size.X / 2, at.Ty - view.Size.Y / 2) };
    }

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
        CenterView(av);
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
        _newGame.Finished = (name, female, skin) =>
        {
            SetAvatar(name, female, skin);
            layer.QueueFree();
            _newGame = null;
            _eggs.Activate(_avatar.Avatar, -1, -1);
        };
        _newGame.Cancelled = () =>
        {
            layer.QueueFree();
            _newGame = null;
            ShowMainMenu();
        };
        layer.AddChild(_newGame);
        _cursor.Shape = MouseShape.Hand;
    }

    /// <summary>
    /// The new game's choice, as Exult's <c>Actor::read</c> applies it to NPC 0
    /// (<c>set_avname</c>, <c>set_avsex</c>) and <c>read_npcs</c> its shape.
    /// </summary>
    void SetAvatar(string name, bool female, int skin = -1)
    {
        var av = _avatar.Avatar;
        av.NpcName = name;
        AvatarLook.SetFemale(av, female);
        // Exult BG_Game::new_game: the skin chosen (the default skin, baseracesex, unless another).
        av.SkinColor = skin >= 0 ? skin : AvatarLook.DefaultSkin;
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

        if (success)
        {
            // Exult run_endgame: a win earns the menu's View Endgame.
            WriteFlag(EndgameFlag);
        }

        PlayShow(view => new Endgame(view, _music, _clock.TotalHours, success), success ? AfterEndgame() : null);
    }

    /// <summary>
    /// The remake's own, for the saga (Exult's game ends with the endgame): a
    /// mod's usecode function named <c>AfterEndgame</c> takes over after a won
    /// endgame (the bridge to Serpent Isle). The usecode that ran the endgame
    /// is ended first, as it would never have gone on; without such a
    /// function the game ends.
    /// </summary>
    Action? AfterEndgame()
    {
        if (_usecode?.File.Symbols?.Find("AfterEndgame") is not { } next)
        {
            return null;
        }

        return () =>
        {
            _usecode.Reset();
            _usecode.Call(next.Value, _avatar.Avatar, UsecodeEvent.InternalExec);
            _conversation.Refresh();
        };
    }

    /// <summary>
    /// Exult <c>Spellbook_object::do_spell(act, true)</c> and
    /// <c>execute_spell</c> in combat: the mana and reagents paid, the spell's
    /// usecode run with the weapon event (Black Gate's combat spells, Douse to
    /// Sword Strike, take it), its click answered with the caster's target
    /// (<c>intercept_click_on_item</c>, the old intercept kept and restored).
    /// Not while other usecode runs.
    /// </summary>
    bool CastBookmark(U7Object caster, U7Object book)
    {
        if (_usecode is not { } vm || UsecodeRunning)
        {
            return false;
        }

        return Spellbook.DoSpell(book, caster, book.SpellBookmark, false, _combat.Quantities, (fun, who) =>
        {
            var saved = vm.InterceptItem;
            vm.InterceptItem = caster.CombatTarget;
            vm.Call(fun, who, UsecodeEvent.Weapon);
            vm.InterceptItem = saved;
            _conversation.Refresh();
        });
    }

    /// <summary>Usecode runs or waits for an answer (Exult's conversations hold its event loop).</summary>
    bool UsecodeRunning => _usecode is { InUsecode: true } or { WaitingForChoice: true };

    /// <summary>
    /// Whether the player may walk the avatar (mouse or keys): no usecode
    /// running or waiting, not held by the opening (<c>dont_move</c>), and
    /// Exult's <c>avatar_can_act</c>: not asleep, paralysed, dead or knocked out.
    /// </summary>
    bool CanWalk => !UsecodeRunning && !_targetMode && !ObjFlag.DontMoveMode(_avatar.Avatar) && CombatSchedule.CanAct(_avatar.Avatar);

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

    void RunUsecode(U7Object obj, UsecodeEvent eventId = UsecodeEvent.DoubleClick)
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

        var fun = obj.NpcNum >= 0 || obj.AssignedUsecode >= 0 ? obj.GetUsecode() : _usecode!.GetItemFun(obj);
        if (fun < 0)
        {
            fun = _usecode!.GetShapeFun(obj.Shape);
        }

        if (obj.IsActor && obj != _avatar.Avatar && _usecode.FailedCopyProtection)
        {
            fun = UsecodeMachine.FailCopyProtectionUsecode;
        }

        _statusExtra = obj.NpcNum >= 0
            ? $"usecode 0x{fun:X3}  npc {obj.NpcNum} {obj.NpcName}"
            : $"usecode 0x{fun:X3}  {obj.Shape}";
        var rc = _usecode.Call(fun, obj, eventId);
        if (rc < 0)
        {
            _statusExtra = _usecode.HudMessage;
        }

        _conversation.Refresh();
    }

    /// <summary>
    /// Loads the slot by restarting the scene on it (F9, the console's
    /// <c>load</c>), in the game its IDENTITY names (a saga's either); false
    /// when it holds no save.
    /// </summary>
    bool LoadFrom(string slot)
    {
        if (!SaveGame.Exists(slot))
        {
            return false;
        }

        if (SaveGame.SlotGame(slot) is { } game && GameContext.Installed(game))
        {
            GameContext.Switch(GameContext.Mode, game);
        }

        U7Paths.GameDatOverride = SaveGame.SlotDir(slot);
        SaveGame.SetLast(slot);
        GD.Print("loading " + U7Paths.GameDatOverride);
        GetTree().ReloadCurrentScene();
        return true;
    }

    /// <summary>
    /// A new game in another mode (the console's <c>game</c>; later the
    /// picker): the scene restarts on that mode's first game, from its
    /// INITGAME.DAT. False when that game isn't installed.
    /// </summary>
    bool StartMode(PlayMode mode)
    {
        if (!GameContext.Installed(mode == PlayMode.SerpentIsle ? GameType.SerpentIsle : GameType.BlackGate))
        {
            return false;
        }

        GameContext.Switch(mode);
        U7Paths.GameDatOverride = null;
        GD.Print($"new game: {GameContext.Describe()}");
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
            _quaking = false;
            return _quakeOffset;
        }

        if (!_quaking)
        {
            // Exult Earthquake::handle_event: the sound once, as the shaking starts.
            _quaking = true;
            _sfx.Play(SoundEffects.GameSfx(60));
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
