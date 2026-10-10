using Godot;
using U7.Actors;
using U7.Core;
using U7.Data;
using U7.Gumps;
using U7.Rendering;
using U7.Usecode;
using U7.UI;
using U7.World;

namespace U7.Game;

/// <summary>
/// The player's input: the keys, both mouse buttons (Exult's walking with
/// either, a left click naming things, double-clicks, drags and their drops
/// on the map, Exult drag.cc), the cursor, books and pictures waiting for a
/// click, and the Wizard Eye.
/// </summary>
public partial class U7Game
{
    /// <summary>What the left button's press went to, until it is let go.</summary>
    LeftPress _leftPress;
    ulong _walkPressMsec;

    /// <summary>What a press of the left button went to, kept until it is let go.</summary>
    enum LeftPress
    {
        /// <summary>Not down, or its press never reached the game (a GUI control, the new game screen).</summary>
        None,
        /// <summary>Down on open ground: holding it walks the avatar (with Shift the cheat teleport); a quick release walks a path.</summary>
        Walk,
        /// <summary>Taken by something else: a gump, a drag, a double-click, a target for usecode, the Wizard Eye.</summary>
        Consumed,
        /// <summary>Down while a book page or a picture was shown: letting go turns the page or closes it.</summary>
        Book
    }

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

    public override void _UnhandledInput(InputEvent @event)
    {
        // Exult's flash_shape holds the game (SDL_Delay): nothing answers the mouse or keys meanwhile.
        // The save/load screen takes its own input, as Exult's modal gumps do.
        if (!_ready || _cursor.Holding || _newGame is not null || _saveLoad is not null)
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
                // Every way through below says where the press went.
                if (mb.DoubleClick)
                {
                    // Exult: a double-click names nothing.
                    _showItemsMsec = 0;
                }

                if (HandleClickOnItem(virt.X, virt.Y))
                {
                    _leftPress = LeftPress.Consumed;
                    GetViewport().SetInputAsHandled();
                    return;
                }

                if (ObjFlag.DontMoveMode(_avatar.Avatar))
                {
                    // Exult: no double-clicks, drags or walking while usecode runs the avatar.
                    _leftPress = LeftPress.Consumed;
                    GetViewport().SetInputAsHandled();
                    return;
                }

                if (_gumps.OnMouseDown(_gumpView, virt.X, virt.Y, mb.DoubleClick))
                {
                    _leftPress = LeftPress.Consumed;
                    GetViewport().SetInputAsHandled();
                    return;
                }

                if (mb.DoubleClick)
                {
                    _leftPress = LeftPress.Consumed;
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
                    _leftPress = LeftPress.Consumed;
                    GetViewport().SetInputAsHandled();
                }
                else
                {
                    _leftPress = LeftPress.Walk;
                    _walkPressMsec = Time.GetTicksMsec();
                }
            }
            else if (!mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                if (_gumps.Drag is { } drag)
                {
                    var world = _camera.GetGlobalMousePosition();
                    _gumps.OnMouseUp(_gumpView, virt.X, virt.Y);
                    _leftPress = LeftPress.Consumed;
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
                else if (_leftPress == LeftPress.Walk)
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
                    ToggleCombat();
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
                    QuickSave();
                    break;
                case Key.Escape:
                    // Exult ActionCloseOrMenu: the gumps close; with none open, the save/load screen.
                    if (_gumps.GumpMode)
                    {
                        _gumps.CloseAll();
                    }
                    else if (!UsecodeRunning)
                    {
                        OpenSaveLoad();
                    }

                    break;
                case Key.F9:
                    if (!LoadFrom(SaveGame.QuickSlot))
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
    /// takes what is dropped on it (<c>add(obj, false, true)</c>, filling its
    /// bags' stacks first); a stack of the same kind takes it in.
    /// </summary>
    bool DropOn(U7Object found, U7Object obj) =>
        found.IsActor
            ? found.GetFlag(ObjFlag.InParty) && Equipment.AddToActor(found, obj, _catalog, _map, combine: _combat.Quantities)
            : _combat.Quantities.Drop(found, obj);

    /// <summary>Exult <c>Check_weight</c>: a party member (or what one carries) takes no more than it can carry.</summary>
    bool CheckWeight(U7Object obj, U7Object onto)
    {
        if (Inventory.CheckWeight(obj, onto, _catalog))
        {
            return true;
        }

        _cursor.Flash(MouseShape.TooHeavy);
        return false;
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
        _leftPress = LeftPress.None;
        if (!CanWalk || _gumps.GumpMode || Input.IsKeyPressed(Key.Shift))
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
        else if (CanWalk && !_gumps.GumpMode && _gumps.Drag is null)
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
        else if (CanWalk)
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
                return MouseShape.ShortArrows + Directions.NoWrap((int)(c.Y - screen.Y), (int)(screen.X - c.X));
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
                var dir = Directions.NoWrap((int)(centre.Y - m.Y), (int)(m.X - centre.X));
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
            ConsumeLeftPress();
            GetViewport().SetInputAsHandled();
            return true;
        }

        var reading = _usecode is { Wait: UsecodeWait.BookPage or UsecodeWait.Picture };
        if (mb.ButtonIndex == MouseButton.Left && reading)
        {
            var pressedWhileReading = _leftPress == LeftPress.Book;
            _leftPress = mb.Pressed ? LeftPress.Book : LeftPress.Consumed;
            if (!mb.Pressed && pressedWhileReading)
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

        ConsumeLeftPress();
        GetViewport().SetInputAsHandled();
        return true;
    }

    /// <summary>Something else takes the click: the left button no longer walks, but a press made while reading still turns the page.</summary>
    void ConsumeLeftPress()
    {
        if (_leftPress != LeftPress.Book)
        {
            _leftPress = LeftPress.Consumed;
        }
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
        if (_usecode is null || UsecodeRunning)
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
}
