using Godot;
using U7.Actors;
using U7.Core;
using U7.Data;
using U7.Gumps;
using U7.Rendering;
using U7.Usecode;
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
    Label _say = null!;
    Label _debug = null!;
    VBoxContainer _answers = null!;
    TextureRect[] _faces = [];
    AvatarController _avatar = null!;
    GameMap _map = null!;
    ShapeCatalog _catalog = null!;
    ShapeCache _shapes = null!;
    UsecodeMachine? _usecode;
    GameClock _clock = null!;
    ScheduleRunner _schedules = null!;
    EggHatcher _eggs = null!;
    CombatEngine _combat = null!;
    List<U7Object?> _npcs = new();
    float _zoom = 4f;
    bool _ready;
    bool _debugOn;
    bool _suppressWalk;
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
                Solid = false,
                IsActor = true
            };
            _map.AddObject(avatar);
            _avatar = new AvatarController(avatar, _map);

            try
            {
                _npcs = NpcDat.Load(_map, avatar);
            }
            catch (Exception ex)
            {
                GD.PushError("NPC load failed: " + ex);
                _npcs = [avatar];
            }

            _clock = new GameClock();
            var schedTable = ScheduleTable.Load();
            _schedules = new ScheduleRunner(_map, avatar, _npcs, schedTable, _clock);
            _combat = new CombatEngine(_map, avatar, _catalog);
            _eggs = new EggHatcher(_map, _clock) { Combat = _combat };
            _avatar.Moved = (actor, fromTx, fromTy) => _eggs.Activate(actor, fromTx, fromTy);
            _combat.AvatarMoved = _avatar.Moved;
            _combat.Schedules = _schedules;

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

            _say = new Label
            {
                Name = "Say",
                Position = new Vector2(12, 520),
                Size = new Vector2(800, 160),
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                TextureFilter = TextureFilterEnum.Nearest
            };
            _say.AddThemeColorOverride("font_color", new Color(1f, 0.92f, 0.55f));
            layer.AddChild(_say);

            _answers = new VBoxContainer
            {
                Name = "Answers",
                Position = new Vector2(900, 200)
            };
            layer.AddChild(_answers);

            _faces = new TextureRect[2];
            for (var i = 0; i < _faces.Length; i++)
            {
                _faces[i] = new TextureRect
                {
                    Name = $"Face{i}",
                    Visible = false,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspect,
                    TextureFilter = TextureFilterEnum.Nearest,
                    MouseFilter = Control.MouseFilterEnum.Ignore
                };
                layer.AddChild(_faces[i]);
            }

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
            GD.Print(usecodeFile.DisassemblePrefix(0x0096));
            _usecode = new UsecodeMachine(usecodeFile, _map, avatar);
            _usecode.Gumps = _gumps;
            _usecode.Npcs = _npcs;
            _usecode.Clock = _clock;
            _usecode.Schedules = _schedules;
            _usecode.Combat = _combat;
            _eggs.Usecode = _usecode;
            _usecode.Say += text => _say.Text = text;
            _usecode.AnswersChanged += RebuildAnswers;
            _usecode.FacesChanged += RefreshFaces;

            var dummy = new U7Object { Shape = 0x96, Frame = 0, Tx = avatar.Tx, Ty = avatar.Ty };
            var rc = _usecode.Call(0x0096, dummy, UsecodeEvent.DoubleClick);
            GD.Print($"debug call 0x0096 → {rc} ip={_usecode.LastIp} last={_usecode.LastIntrinsic} (frame={dummy.Frame} flags=0x{dummy.Flags:X})");
            if (_usecode.InUsecode || _usecode.WaitingForChoice)
            {
                GD.Print("debug call left usecode running; resetting.");
                _usecode.Reset();
            }

            _say.Text = "";
            _usecode.HudMessage = "";

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
            _eggs.Activate(avatar, -1, -1, must: true);

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

    public override void _Process(double delta)
    {
        if (!_ready)
        {
            return;
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

        if (!inUsecode && !_suppressWalk && !gumpBusy && !_avatar.Avatar.IsDead &&
            Input.IsMouseButtonPressed(MouseButton.Left) && _camera is not null)
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
                    _eggs.Activate(_avatar.Avatar, fromTx, fromTy);
                }
            }
            else
            {
                click = new Vector2I(tile.Tx, tile.Ty);
            }
        }

        if (!Input.IsMouseButtonPressed(MouseButton.Left))
        {
            _suppressWalk = false;
        }

        var frozen = inUsecode || gumpBusy || _avatar.Avatar.IsDead;
        if (!inUsecode && !gumpBusy && !_avatar.Avatar.IsDead)
        {
            _avatar.Update(delta, click, _combat.InCombat);
        }

        _clock.Update(delta);
        _world.Modulate = _world.Modulate.Lerp(_clock.WorldModulate, (float)Math.Min(1, delta * 2.5));
        _schedules.Update(delta, frozen);
        _combat.Update(delta, frozen, _avatar.IsPlayerMoving);

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
        camera.GlobalPosition = new Vector2(Mathf.Round(cam.X), Mathf.Round(cam.Y));

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
            $"WASD/arrows walk · I inventory · C combat · F4 invincible · [ ] hour · click walk · double-click / E · F2 debug · F3 arena · Home Trinsic\n" +
            $"hp {av.GetProp(ActorProp.Health)}  {(_combat.InCombat ? "combat" : "peace")}" +
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
            var virt = _gumpView.MouseVirtual();
            if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed)
            {
                _zoom = Mathf.Clamp(_zoom + 0.5f, 1f, 8f);
                _camera.Zoom = new Vector2(_zoom, _zoom);
                _gumpView.Zoom = _zoom;
                RefreshFaces();
            }
            else if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed)
            {
                _zoom = Mathf.Clamp(_zoom - 0.5f, 1f, 8f);
                _camera.Zoom = new Vector2(_zoom, _zoom);
                _gumpView.Zoom = _zoom;
                RefreshFaces();
            }
            else if (mb.Pressed && mb.ButtonIndex is MouseButton.Left or MouseButton.Right)
            {
                if (HandleClickOnItem(virt.X, virt.Y, mb.ButtonIndex == MouseButton.Right))
                {
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
            switch (key.Keycode)
            {
                case Key.Home:
                {
                    var fromTx = _avatar.Avatar.Tx;
                    var fromTy = _avatar.Avatar.Ty;
                    _map.MoveObject(_avatar.Avatar, U7Constants.StartTileX, U7Constants.StartTileY, 0);
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
            }
        }
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
        RebuildAnswers();
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

        if (obj.IsActor && CombatEngine.IsEnemy(_avatar.Avatar.Alignment, obj.Alignment))
        {
            _combat.Attack(_avatar.Avatar, obj);
            _statusExtra = _combat.LastMessage;
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

    void RunUsecode(U7Object obj)
    {
        if (_usecode is null)
        {
            return;
        }

        var fun = obj.NpcNum >= 0 ? obj.GetUsecode() : UsecodeMachine.GetShapeFun(obj.Shape);
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
            _say.Text = _usecode.HudMessage;
        }

        RebuildAnswers();
    }

    void RefreshFaces()
    {
        if (_faces.Length == 0 || _usecode is null || _shapes is null)
        {
            return;
        }

        var view = GetViewport().GetVisibleRect().Size;
        for (var i = 0; i < _faces.Length; i++)
        {
            var slot = _usecode.Conv.Faces[i];
            if (slot is not { } face)
            {
                _faces[i].Visible = false;
                _faces[i].Texture = null;
                continue;
            }

            var tex = _shapes.GetFace(face.Shape, face.Frame);
            _faces[i].Texture = tex;
            _faces[i].Visible = tex is not null;
            if (tex is null)
            {
                continue;
            }

            var size = tex.GetSize() * _zoom;
            _faces[i].Size = size;
            _faces[i].Position = i == 0
                ? new Vector2(16, 72)
                : new Vector2(Mathf.Max(16, view.X - size.X - 16), 72);
        }
    }

    void RebuildAnswers()
    {
        foreach (var child in _answers.GetChildren())
        {
            child.QueueFree();
        }

        var machine = _usecode;
        if (machine is null || !machine.WaitingForChoice)
        {
            return;
        }

        for (var i = 0; i < machine.Conv.Answers.Count; i++)
        {
            var text = machine.Conv.Answers[i];
            var idx = i;
            var btn = new Button { Text = text };
            btn.Pressed += () =>
            {
                machine.Choose(text, idx);
                RebuildAnswers();
            };
            _answers.AddChild(btn);
        }
    }
}
