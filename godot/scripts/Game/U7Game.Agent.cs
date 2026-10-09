using System.IO;
using System.Text;
using Godot;
using U7.Actors;
using U7.Core;
using U7.Data;
using U7.Gumps;
using U7.UI;
using U7.Usecode;

namespace U7.Game;

/// <summary>
/// Agent console for automated play-testing, enabled by the environment
/// variable <c>U7_AGENT=&lt;dir&gt;</c>. Commands are read one per line from
/// <c>&lt;dir&gt;/cmd.txt</c>; results are appended to <c>&lt;dir&gt;/out.txt</c>,
/// each command ending with <c>DONE &lt;n&gt;</c>. Game time is frozen between
/// commands. Type <c>help</c> for the command list.
/// </summary>
public partial class U7Game
{
    const string AgentHelp =
        "look [r] | find <text> | npc <num|name> | state | inv [npcnum|id] | flags [<hex> <0|1>] | setflag <npc|id> <flag> [0|1] | timer [n] [hours-ago] | stubs | " +
        "walk <x> <y> | walkto <id|npc:num> | steer <dir> <sec> [ms] | tp <x> <y> [z] | talk <npcnum|name> | use <id> | take <id> | put <id> <container-id> | sail <x> <y> | book [page] | cast <spell> | " +
        "cont [n|all] | choose <answer|#n> | num <n> | click <id>|<x> <y> [z] | wait <sec> | hour <h> [m] | light | shot <name> | quit | die [restart] | " +
        "save <slot> | load <slot> | tile <x> <y> [z] | eggs [type] [radius] | weather [<n> [min] | lightning | eggs [radius]] | sprite <n> [frame] | damage <n> [type] | arena | combat [off] | attack <id> | drag <id> | cursor <x> <y> | avatar <name> [male|female] | name <id>|gump | rmouse <x> <y> <sec>|double | rclose | endgame [won|lost] | close";

    /// <summary>Set once the console has started; a load reloads the scene and the console carries on.</summary>
    static bool _agentStarted;

    string? _agentDir;
    // Static: a load reloads the scene, and the console must keep its place.
    static int _agentLines;
    static int _agentSeq;
    static readonly Queue<string> _agentQueue = new();
    Func<bool>? _agentBusy;
    double _agentElapsed;
    double _agentLimit;
    Vector2I? _agentClick;
    byte[] _agentFlags0 = [];
    readonly Dictionary<int, U7Object> _agentIds = new();

    void AgentInit()
    {
        _agentDir = System.Environment.GetEnvironmentVariable("U7_AGENT");
        if (string.IsNullOrEmpty(_agentDir) || _usecode is null)
        {
            _agentDir = null;
            return;
        }

        Directory.CreateDirectory(_agentDir);
        if (!_agentStarted)
        {
            File.WriteAllText(Path.Combine(_agentDir, "out.txt"), "");
        }

        var cmd = Path.Combine(_agentDir, "cmd.txt");
        if (!_agentStarted)
        {
            _agentLines = File.Exists(cmd) ? File.ReadAllLines(cmd).Length : 0;
        }
        _music.Enabled = false;
        _music.Stop();
        _agentFlags0 = (byte[])_usecode.GFlags.Clone();
        _usecode.Say += text => AgentLog("SAY " + text.Replace('\n', ' '));
        _usecode.ItemSay += (obj, text) => AgentLog($"BARK {AgentName(obj)}: {text}");
        _usecode.FadeStarted += (cycles, fadeIn) =>
            AgentLog($"FADE {(fadeIn ? "in" : "out")}" +
                     (cycles == 0 ? " at once" : $", {cycles + 1} steps ({(cycles + 1) * UsecodeMachine.FadeStepMs} ms)"));
        _cursor.Flashed += shape => AgentLog($"FLASH {MouseShape.Name(shape)}");
        _usecode.BookPageShown += book =>
            AgentLog($"BOOK {(book is U7.Gumps.ScrollGump ? "scroll" : "book")}: " +
                     string.Join(" / ", book.Lines.Select(l => l.Text.Trim())));
        Engine.TimeScale = 0;
        if (_agentStarted)
        {
            AgentLog($"(scene reloaded from {U7Paths.GameDatOverride ?? "INITGAME"})");
            AgentStatus();
            return;
        }

        _agentStarted = true;
        AgentLog($"agent ready: {AgentHelp}");
        AgentLog($"DONE {_agentSeq}");
    }

    /// <summary>A fade or a cursor flash holds the game.</summary>
    bool AgentHeld => _usecode is { Wait: UsecodeWait.Fade or UsecodeWait.Flash } || _cursor.Holding;

    /// <summary>Called every frame from _Process; may supply a walk target.</summary>
    void AgentUpdate(double delta, ref Vector2I? click)
    {
        if (_agentDir is null)
        {
            return;
        }

        if (_agentClick is { } c)
        {
            click = c;
            _agentClick = null;
        }

        if (_agentBusy is not null)
        {
            _agentElapsed += delta;
            if (_agentBusy() && _agentElapsed < _agentLimit)
            {
                return;
            }

            // Exult's fades and cursor flashes hold up the game: a command is over once they are.
            if (AgentHeld && _agentElapsed < _agentLimit + 10)
            {
                return;
            }

            _agentBusy = null;
            Engine.TimeScale = 0;
            AgentStatus();
            AgentLog($"DONE {_agentSeq}");
        }

        var path = Path.Combine(_agentDir, "cmd.txt");
        if (File.Exists(path))
        {
            string[] lines;
            using (var fs = new FileStream(path, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs))
            {
                lines = sr.ReadToEnd().Split('\n');
            }

            if (lines.Length - 1 < _agentLines)
            {
                _agentLines = 0; // the file was truncated: start over
            }

            // The last element is empty or a line still being written.
            for (; _agentLines < lines.Length - 1; _agentLines++)
            {
                var line = lines[_agentLines].Trim();
                if (line.Length > 0)
                {
                    _agentQueue.Enqueue(line);
                }
            }
        }

        if (_agentQueue.Count == 0)
        {
            return;
        }

        var cmd = _agentQueue.Dequeue();
        _agentSeq++;
        AgentLog($"> {cmd}");
        try
        {
            AgentRun(cmd);
        }
        catch (Exception ex)
        {
            AgentLog($"ERROR {ex.GetType().Name}: {ex.Message}");
            _agentBusy = null;
        }

        if (_agentBusy is null && AgentHeld)
        {
            Engine.TimeScale = 8;
            _agentElapsed = 0;
            _agentLimit = 0;
            _agentBusy = () => false;
        }

        if (_agentBusy is null)
        {
            Engine.TimeScale = 0;
            AgentStatus();
            AgentLog($"DONE {_agentSeq}");
        }
    }

    void AgentRun(string cmd)
    {
        var parts = cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var verb = parts[0].ToLowerInvariant();
        var arg = parts.Length > 1 ? cmd[(cmd.IndexOf(' ') + 1)..].Trim() : "";
        var av = _avatar.Avatar;
        switch (verb)
        {
            case "help":
                AgentLog(AgentHelp);
                break;
            case "look":
                AgentLook(parts.Length > 1 ? int.Parse(parts[1]) : 16);
                break;
            case "find":
                AgentFind(arg);
                break;
            case "npc":
                if (AgentNpc(arg) is { } npcObj)
                {
                    AgentLog(AgentDescribe(npcObj) + $" typeflags 0x{npcObj.TypeFlags:X}" +
                             $" amode {AttackModeNames[npcObj.AttackMode & 0xf]}{(npcObj.UserSetAttack ? " (player's)" : "")}" +
                             (npcObj.Oppressor is { } oppr ? $" oppressor {AgentName(oppr)}" : ""));
                }

                break;
            case "state":
                break; // AgentStatus prints it
            case "inv":
                AgentInventory(parts.Length > 1 ? AgentNpc(arg) ?? AgentTarget(arg) ?? av : av, 0);
                break;
            case "flags":
                if (parts.Length > 2)
                {
                    // A test shortcut (e.g. 0x1B3, Seance's sight of the dead).
                    var flag = Convert.ToInt32(parts[1], 16);
                    _usecode!.GFlags[flag] = byte.Parse(parts[2]);
                    AgentLog($"flag 0x{flag:X3} = {_usecode.GFlags[flag]}");
                    break;
                }

                for (var i = 0; i < _usecode!.GFlags.Length; i++)
                {
                    if (_usecode.GFlags[i] != _agentFlags0[i])
                    {
                        AgentLog($"flag 0x{i:X3} = {_usecode.GFlags[i]} (was {_agentFlags0[i]})");
                    }
                }

                break;
            case "setflag":
            {
                // A test shortcut: an object flag by number or ObjFlag name (invisible, charmed, poisoned, ...),
                // as usecode sets it (Exult Actor::set_flag: an actor's timers start).
                var target = AgentTarget(parts[1]) ?? throw new ArgumentException("no such object/npc");
                var flag = AgentFlag(parts[2]);
                if (parts.Length > 3 && parts[3] == "0")
                {
                    _timers.ClearFlag(target, flag);
                }
                else
                {
                    _timers.SetFlag(target, flag);
                }

                AgentLog($"{AgentDescribe(target)} flag {flag} = {(target.GetFlag(flag) ? 1 : 0)}");
                break;
            }
            case "stubs":
                AgentLog(BgIntrinsics.StubSummary());
                break;
            case "walk":
                AgentWalk(int.Parse(parts[1]), int.Parse(parts[2]));
                break;
            case "sail":
                AgentSail(int.Parse(parts[1]), int.Parse(parts[2]));
                break;
            case "walkto":
            {
                // To a free tile next to it (an actor's own tile is blocked).
                var target = AgentTarget(arg) ?? throw new ArgumentException("no such object/npc");
                var spot = _map.FindSpot(new TileCoord(target.Tx, target.Ty, target.Tz), 2, av) ??
                           new TileCoord(target.Tx + 1, target.Ty + 1, target.Tz);
                AgentWalk(spot.Tx, spot.Ty);
                break;
            }
            case "tp":
                // Exult's cheat teleport: teleport_party, every egg round the spot tried.
                TeleportParty(new TileCoord(int.Parse(parts[1]), int.Parse(parts[2]), parts.Length > 3 ? int.Parse(parts[3]) : av.Tz));
                break;
            case "talk":
            case "use":
            {
                // As the mouse (ActivateUnderMouse): nothing new starts while usecode runs or waits.
                if (_usecode!.InUsecode || _usecode.WaitingForChoice)
                {
                    AgentLog("usecode is running (finish it first)");
                    break;
                }

                var target = AgentTarget(arg) ?? throw new ArgumentException("no such object/npc");
                if (verb == "talk")
                {
                    // With gumps open a party member would show its inventory (Exult Actor::activate).
                    _gumps.CloseAll();
                }

                AgentUse(target);
                break;
            }
            case "drop":
            {
                // A drag onto the map (Exult drop_on_map), the whole stack: let go with the thing painted
                // standing on tile x,y at lift z and the mouse over that spot. Without z, on top of what
                // is at the tile (where it comes to rest let fall from the highest open lift up to 5 over
                // the avatar).
                var item = AgentTarget(parts[1]) ?? throw new ArgumentException("no such object");
                var (dx, dy) = (int.Parse(parts[2]), int.Parse(parts[3]));
                var tz = av.Tz;
                if (parts.Length > 4)
                {
                    tz = int.Parse(parts[4]);
                }
                else
                {
                    for (var from = Math.Min(av.Tz + 5, 15); from >= 0; from--)
                    {
                        if (!_map.Blocking.IsBlocked(Math.Max(1, _catalog[item.Shape].DimZ), from, dx, dy, out var rest,
                                MoveFlags.Walk, maxDrop: 16, maxRise: 0))
                        {
                            tz = rest;
                            break;
                        }
                    }
                }

                U7.Rendering.WorldView.ShapeLocation(dx, dy, tz, out var hx, out var hy);
                var hot = WorldToVirtual(new Vector2(hx, hy));
                var mouse = WorldToVirtual(new Vector2(hx - U7Constants.TileSize / 2f, hy - U7Constants.TileSize / 2f));
                var drag = new DragState
                {
                    Object = item,
                    Moved = true,
                    FromWorld = item.Container is null,
                    OldTx = item.Tx,
                    OldTy = item.Ty,
                    OldTz = item.Tz,
                    OldContainer = item.Container,
                    OldReadySlot = item.ReadySlot,
                    PaintX = hot.X,
                    PaintY = hot.Y,
                    MouseX = mouse.X,
                    MouseY = mouse.Y
                };
                _gumps.LiftUp(drag);
                var result = _gumps.DropInWorld(drag);
                AgentLog($"{result.ToString().ToLowerInvariant()} (aimed at {dx},{dy} lift {tz}): {AgentDescribe(item)}");
                break;
            }
            case "eye":
            {
                // Wizard Eye: what holding the right button towards a direction (0 north, clockwise) does, a tile a step.
                var dir = int.Parse(parts[1]) & 7;
                var steps = parts.Length > 2 ? int.Parse(parts[2]) : 1;
                for (var i = 0; i < steps; i++)
                {
                    _usecode!.MoveWizardEye(EyeDeltas[2 * dir], EyeDeltas[2 * dir + 1]);
                }

                break;
            }
            case "call":
            {
                // A test shortcut: run a usecode function (hex) on an object, by default the avatar, event 1.
                if (_usecode!.InUsecode || _usecode.WaitingForChoice)
                {
                    AgentLog("usecode is running");
                    break;
                }

                var fun = Convert.ToInt32(parts[1], 16);
                var target = parts.Length > 2 ? AgentTarget(parts[2]) ?? throw new ArgumentException("no such object/npc") : av;
                var ev = parts.Length > 3 ? int.Parse(parts[3]) : (int)UsecodeEvent.DoubleClick;
                _usecode.Call(fun, target, (UsecodeEvent)ev);
                _conversation.Refresh();
                AgentLog($"called 0x{fun:X3} on {AgentName(target)}, event {ev}");
                break;
            }
            case "take":
            {
                // A drag into the avatar's gump, with Exult's theft check.
                var target = AgentTarget(arg) ?? throw new ArgumentException("no such object");
                var okayToMove = target.GetFlag(ObjFlag.OkayToTake);
                var lifted = AgentLiftedFrom(target);
                if (!Equipment.AddToActor(av, target, _catalog, _map))
                {
                    AgentLog("cannot take it (too heavy or no room)");
                    break;
                }

                lifted();
                AgentLog($"took {AgentDescribe(target)}");
                if (!okayToMove)
                {
                    _gumps.PossibleTheft?.Invoke();
                }

                break;
            }
            case "put":
            {
                // A drag into a container's gump (also takes a readied item off), with Exult's theft check;
                // into an actor (its paperdoll), Exult's Actor::add readies it in its best free spot.
                var item = AgentTarget(parts[1]) ?? throw new ArgumentException("no such object");
                var cont = AgentTarget(parts[2]) ?? throw new ArgumentException("no such container");
                var from = item.Container;
                var okayToMove = item.GetFlag(ObjFlag.OkayToTake);
                var lifted = AgentLiftedFrom(item);
                if (!(cont.IsActor
                        ? Equipment.AddToActor(cont, item, _catalog, _map)
                        : Equipment.TryPlace(_map, item, cont, 8, 8, _catalog)))
                {
                    AgentLog("it does not fit");
                    break;
                }

                lifted();

                AgentLog($"put {AgentDescribe(item)} into {AgentName(cont)}");
                if (cont != from && !okayToMove)
                {
                    _gumps.PossibleTheft?.Invoke();
                }

                break;
            }
            case "book":
            case "cast":
            {
                if (_gumps.Open.OfType<SpellbookGump>().LastOrDefault() is not { } sb)
                {
                    AgentLog("no spellbook open (use one first)");
                    break;
                }

                if (verb == "cast")
                {
                    // A double-click on the spell.
                    var spell = int.Parse(parts[1]);
                    var mana = av.GetProp(ActorProp.Mana);
                    AgentLog(sb.DoSpell(spell)
                        ? $"cast spell {spell} (usecode 0x{Spellbook.BaseSpellsUsecode + spell:X3}), mana {mana} -> {av.GetProp(ActorProp.Mana)}"
                        : $"cannot cast spell {spell} (not in the book, or mana {mana}, level {av.GetLevel()} or reagents short)");
                    break;
                }

                if (parts.Length > 1)
                {
                    sb.ChangePage(int.Parse(parts[1]) - sb.Page);
                }

                AgentLog($"spellbook page {sb.Page}, bookmark {sb.Book.SpellBookmark}: " +
                         string.Join(", ", sb.PageSpells().Select(p => sb.Page == 0 ? $"{p.Spell}" : $"{p.Spell} (x{p.Available})")));
                break;
            }
            case "cont":
            {
                if (_endgame is { } endgame)
                {
                    // As a key that skips the endgame's movies or ends its credits.
                    endgame.Skip();
                    AgentLog("ENDGAME skip");
                    break;
                }

                var n = arg == "all" ? 60 : parts.Length > 1 ? int.Parse(parts[1]) : 1;
                for (var i = 0; i < n && _usecode is { Wait: UsecodeWait.ClickToContinue or UsecodeWait.BookPage or UsecodeWait.Picture or UsecodeWait.WizardEye }; i++)
                {
                    if (_usecode.Wait == UsecodeWait.BookPage)
                    {
                        _usecode.TurnBookPage();
                        _conversation.Refresh();
                    }
                    else if (_usecode.Wait == UsecodeWait.Picture)
                    {
                        _usecode.ClosePicture();
                        _conversation.Refresh();
                    }
                    else if (_usecode.Wait == UsecodeWait.WizardEye)
                    {
                        _usecode.EndWizardEye(); // Esc
                        _conversation.Refresh();
                    }
                    else
                    {
                        _conversation.Advance();
                    }
                }

                break;
            }
            case "choose":
            {
                if (_usecode!.Wait is not (UsecodeWait.Converse or UsecodeWait.SelectMenu or UsecodeWait.SelectMenuIndex))
                {
                    throw new ArgumentException($"no answers wait now ({_usecode.Wait}): cont first");
                }

                var conv = _usecode.Conv;
                var answer = arg.StartsWith('#') ? conv.Answers[int.Parse(arg[1..]) - 1]
                    : conv.Answers.FirstOrDefault(a => a.Equals(arg, StringComparison.OrdinalIgnoreCase))
                      ?? throw new ArgumentException($"no answer '{arg}'");
                _usecode.Choose(answer, conv.Answers.IndexOf(answer));
                _conversation.Refresh();
                break;
            }
            case "num":
                if (_usecode!.Wait != UsecodeWait.NumericInput)
                {
                    throw new ArgumentException($"no number is asked for now ({_usecode.Wait})");
                }

                _usecode.ResumeWait(UsecodeValue.FromInt(int.Parse(arg)));
                _conversation.Refresh();
                break;
            case "click":
            {
                // An object, or a tile (Exult click_on_item: no object, the tile's coordinates).
                var target = parts.Length >= 3 ? null : AgentTarget(arg) ?? throw new ArgumentException("no such object");
                var arr = UsecodeValue.FromArray(4, UsecodeValue.FromObject(target));
                arr.PutElem(1, UsecodeValue.FromInt(target?.Tx ?? int.Parse(parts[1])));
                arr.PutElem(2, UsecodeValue.FromInt(target?.Ty ?? int.Parse(parts[2])));
                arr.PutElem(3, UsecodeValue.FromInt(target?.Tz ?? (parts.Length > 3 ? int.Parse(parts[3]) : av.Tz)));
                _usecode!.ResumeWait(arr);
                _conversation.Refresh();
                break;
            }
            case "wait":
            {
                Engine.TimeScale = 8;
                _agentElapsed = 0;
                _agentLimit = double.Parse(arg, System.Globalization.CultureInfo.InvariantCulture);
                _agentBusy = () => _usecode is not { WaitingForPlayer: true };
                break;
            }
            case "timer":
            {
                // List usecode timers, or set one as if set some hours ago.
                if (parts.Length > 2)
                {
                    _usecode!.Timers[int.Parse(parts[1])] = _clock.TotalHours - int.Parse(parts[2]);
                }

                foreach (var (tnum, hours) in _usecode!.Timers.OrderBy(t => t.Key))
                {
                    AgentLog($"timer {tnum}: set at hour {hours}, {_clock.TotalHours - hours} hours ago");
                }

                break;
            }
            case "steer":
            {
                // Hold a walking direction (n, ne, e, ... or 0-7) for a while, like a held key or button.
                var dir = int.TryParse(parts[1], out var dirNum)
                    ? dirNum & 7
                    : Array.IndexOf(["n", "ne", "e", "se", "s", "sw", "w", "nw"], parts[1]);
                if (dir < 0)
                {
                    throw new ArgumentException($"no direction '{parts[1]}' (n, ne, e, se, s, sw, w, nw or 0-7)");
                }

                var secs = double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
                var speed = parts.Length > 3 ? int.Parse(parts[3]) : WalkSpeed.Keyboard(false, false, false);
                var step = new TileCoord(0, 0, 0).Neighbor(dir);
                var (dx, dy) = (U7Constants.TileDelta(0, step.Tx), U7Constants.TileDelta(0, step.Ty));
                _gumps.CloseAll();
                Engine.TimeScale = 4;
                _agentElapsed = 0;
                _agentLimit = secs + 1;
                _agentBusy = () =>
                {
                    if (_agentElapsed >= secs)
                    {
                        _avatar.Stop();
                        return false;
                    }

                    U7.Rendering.WorldView.ShapeLocation(av.Tx, av.Ty, av.Tz, out var ax, out var ay);
                    _avatar.Steer(new Vector2(ax + 50 * dx, ay + 50 * dy), speed);
                    return true;
                };
                break;
            }
            case "arena":
                // The F3 debug fight: three chaotic rats around the avatar, combat on.
                _combat.SpawnArena();
                AgentLog(_combat.LastMessage);
                break;
            case "combat":
                _combat.SetInCombat(arg != "off");
                AgentLog(_combat.LastMessage);
                break;
            case "endgame":
                // A test shortcut: usecode's run_endgame, won or lost.
                StartEndgame(arg != "lost");
                break;
            case "cursor":
            {
                // The cursor with the mouse over a tile (at the avatar's lift), and the walking speed it means.
                var tx = int.Parse(parts[1]);
                var ty = int.Parse(parts[2]);
                U7.Rendering.WorldView.ShapeLocation(tx, ty, av.Tz, out var px, out var py);
                var world = new Vector2(px - U7Constants.TileSize / 2f, py - U7Constants.TileSize / 2f);
                var shape = CursorFor(world, GetViewport().GetVisibleRect().Size / 2);
                AgentLog(shape is { } s
                    ? $"cursor {MouseShape.Name(s)}" + (s >= MouseShape.ShortArrows && s < MouseShape.Blocked
                        ? $", walking {SpeedCursor(world).Speed} ms a step"
                        : "")
                    : "cursor unchanged");
                break;
            }
            case "avatar":
            {
                // The new game screen's choice (headless games skip it): avatar <name> [male|female].
                var female = parts[^1] is "female";
                var words = parts[1..].Where(w => w is not ("male" or "female")).ToArray();
                SetAvatar(words.Length > 0 ? string.Join(' ', words) : av.NpcName, female);
                AgentLog($"{AgentDescribe(av)}: {(AvatarLook.IsFemale(av) ? "female" : "male")}");
                break;
            }
            case "name":
            {
                // A left click (Exult show_items): on a thing, or on the topmost gump.
                if (arg == "gump")
                {
                    var gump = _gumps.Open.LastOrDefault() ?? throw new ArgumentException("no gump open");
                    var p = AgentPointOn(gump) ?? throw new ArgumentException("no point on the gump");
                    var named = ShowItems(p, MouseWorld());
                    AgentLog(named is null ? "NAME nothing" : $"NAME {AgentDescribe(named)}: {named.BarkText}");
                    break;
                }

                var target = AgentTarget(arg) ?? throw new ArgumentException("no such object");
                AgentLog($"NAME {AgentDescribe(target)}: {ShowName(target)}");
                break;
            }
            case "rmouse":
            {
                // The right button over a tile: held for a while (the mouse stays put on the screen as the
                // view follows the avatar), then let go; or a double-click, a path there.
                var tx = int.Parse(parts[1]);
                var ty = int.Parse(parts[2]);
                U7.Rendering.WorldView.ShapeLocation(tx, ty, av.Tz, out var px, out var py);
                _gumps.CloseAll();
                _agentMouse = new Vector2(px - U7Constants.TileSize / 2f, py - U7Constants.TileSize / 2f) - _camera.GlobalPosition;
                _lastRightUpMsec = 0;
                if (parts[3] == "double")
                {
                    for (var i = 0; i < 2; i++)
                    {
                        RightButtonDown(AgentVirt(), MouseWorld());
                        RightButtonUp(AgentVirt(), MouseWorld());
                    }

                    _agentMouse = null;
                    AgentAwaitArrival(tx, ty);
                    break;
                }

                var secs = double.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture);
                _agentRightHeld = true;
                RightButtonDown(AgentVirt(), MouseWorld());
                Engine.TimeScale = 4;
                _agentElapsed = 0;
                _agentLimit = secs + 1;
                _agentBusy = () =>
                {
                    if (_agentElapsed < secs)
                    {
                        return true;
                    }

                    _agentRightHeld = false;
                    RightButtonUp(AgentVirt(), MouseWorld());
                    _agentMouse = null;
                    return false;
                };
                break;
            }
            case "rclose":
            {
                // A right-click on the topmost gump (Exult right_click_closes_gumps): it closes when let go.
                var gump = _gumps.Open.LastOrDefault() ?? throw new ArgumentException("no gump open");
                var p = AgentPointOn(gump) ?? throw new ArgumentException("no point on the gump");
                RightButtonDown(p, MouseWorld());
                RightButtonUp(p, MouseWorld());
                AgentLog(_gumps.Open.Contains(gump) ? "the gump is still open" : "the gump closed");
                break;
            }
            case "drag":
            {
                // The start of a mouse drag of a thing in the world (Exult Dragging_info::start), then put back.
                var target = AgentTarget(arg) ?? throw new ArgumentException("no such object");
                _gumps.OnWorldMouseDown(target, 0, 0, 0, 0);
                _gumps.OnMouseMove(_gumpView, 10, 10);
                if (_gumps.Drag is { Moved: true })
                {
                    _gumps.CancelDrag();
                    AgentLog($"{AgentDescribe(target)} can be dragged");
                }
                else
                {
                    AgentLog($"{AgentDescribe(target)} can't be dragged");
                }

                break;
            }
            case "attack":
            {
                // A double-click in combat mode on anyone (Exult double_clicked), friend or foe.
                var target = AgentTarget(arg) ?? throw new ArgumentException("no such object/npc");
                _combat.AttackClicked(target);
                AgentLog(_combat.LastMessage);
                break;
            }
            case "hour":
            {
                var h = int.Parse(parts[1]);
                _clock.SkipHours(((h - _clock.Hour) % 24 + 24) % 24);
                if (parts.Length > 2)
                {
                    _clock.Set(_clock.Day, _clock.Hour, int.Parse(parts[2]));
                }

                _lighting.Update(0);
                AgentLog(_lighting.Describe());
                break;
            }
            case "light":
                _lighting.Update(0);
                AgentLog(_lighting.Describe());
                break;
            case "shot":
            {
                if (DisplayServer.GetName() == "headless")
                {
                    AgentLog("no screenshots in headless mode");
                    break;
                }

                var file = Path.Combine(_agentDir!, $"{arg}.png");
                GetViewport().GetTexture().GetImage().SavePng(file);
                AgentLog($"saved {file}");
                break;
            }
            case "damage":
            {
                // Damage through the normal path (no attacker), to the avatar or the actor given (a test
                // shortcut): the red pulse or the outline, and a death.
                var victim = parts.Length > 3 ? AgentTarget(parts[3]) ?? throw new ArgumentException("no such object/npc") : av;
                var hp = victim.GetProp(ActorProp.Health);
                var pulses = _screenFx.Pulses;
                var type = parts.Length > 2 ? int.Parse(parts[2]) : 0;
                var taken = _combat.ReduceHealth(victim, int.Parse(parts[1]), null, type);
                AgentLog($"damage {parts[1]} type {type}: took {taken}, hp {hp} -> {victim.GetProp(ActorProp.Health)} of {victim.GetProp(ActorProp.Strength)}, " +
                         (_screenFx.Pulses > pulses ? "red pulse" : victim.HitUntilMsec > Time.GetTicksMsec() ? "red outline" : "nothing") +
                         (victim.IsDead ? ", dead" : ""));
                break;
            }
            case "sprite":
            {
                // A SPRITES.VGA animation played once over the avatar.
                var sprite = int.Parse(parts[1]);
                var frame = parts.Length > 2 ? int.Parse(parts[2]) : 0;
                var e = _effects.AddSprite(sprite, new TileCoord(av.Tx, av.Ty, av.Tz), frame: frame);
                AgentLog($"sprite {sprite} ({e.Frames} frames) from frame {frame} at {av.Tx},{av.Ty},{av.Tz}");
                break;
            }
            case "eggs":
                AgentEggs(parts.Length > 1 ? AgentEggType(parts[1]) : -1, parts.Length > 2 ? int.Parse(parts[2]) : 40);
                break;
            case "weather":
                if (parts.Length > 1 && parts[1] == "eggs")
                {
                    AgentEggs(U7.World.EggType.Weather, parts.Length > 2 ? int.Parse(parts[2]) : 64);
                    break;
                }

                if (parts.Length > 1 && parts[1] == "lightning")
                {
                    // Usecode's lightning (UI_lightning), which flashes in dungeons too.
                    _effects.AddUsecodeLightning();
                }
                else if (parts.Length > 1)
                {
                    // Usecode's set_weather (15 minutes), or as long as asked.
                    _effects.SetWeather(int.Parse(parts[1]), parts.Length > 2 ? int.Parse(parts[2]) : 15);
                }

                if (parts.Length > 1)
                {
                    _effects.Update(0); // what is due now starts (fog sets the clock, lightning flashes)
                }

                AgentWeather();
                break;
            case "die":
                // F6 (with "restart": Shift+F6).
                DebugDie(restart: arg == "restart");
                AgentLog(_statusExtra);
                break;
            case "quit":
                // Windowed runs end this way: only headless games may be stopped from outside.
                GetTree().Quit();
                break;
            case "tile":
            {
                var tx = int.Parse(parts[1]);
                var ty = int.Parse(parts[2]);
                var tz = parts.Length > 3 ? int.Parse(parts[3]) : av.Tz;
                var column = _map.Blocking.Column(tx, ty);
                var lifts = string.Join(",", Enumerable.Range(0, ChunkBlocking.MaxLifts).Where(z => ((column >> z) & 1) != 0));
                var probe = new TileCoord(tx, ty, tz);
                var avatarBlocked = ActorWalker.IsBlocked(_map, av, ref probe);
                AgentLog($"tile {tx},{ty},{tz}: blocked={_map.IsBlocked(tx, ty, tz)} flat={_map.GetFlat(tx, ty).Shape} " +
                         $"lifts [{lifts}] avatar {(avatarBlocked ? "blocked" : $"stands at {probe.Tz}")}");
                foreach (var o in _map.ObjectsInChunk(tx / U7Constants.TilesPerChunk, ty / U7Constants.TilesPerChunk)
                             .Concat(_map.ObjectsInChunk(tx / U7Constants.TilesPerChunk + 1, ty / U7Constants.TilesPerChunk))
                             .Concat(_map.ObjectsInChunk(tx / U7Constants.TilesPerChunk, ty / U7Constants.TilesPerChunk + 1))
                             .Concat(_map.ObjectsInChunk(tx / U7Constants.TilesPerChunk + 1, ty / U7Constants.TilesPerChunk + 1)))
                {
                    if (o.Occupies(tx, ty) && !o.Removed)
                    {
                        AgentLog($"  {AgentDescribe(o)} dims {o.DimX}x{o.DimY}x{o.DimZ} solid={o.Solid} blocks={o.BlocksAt(tx, ty, tz)}");
                    }
                }

                break;
            }
            case "close":
                if (_usecode is { Wait: UsecodeWait.BookPage })
                {
                    // Esc: stop reading.
                    _usecode.TurnBookPage(stop: true);
                    _conversation.Refresh();
                    break;
                }

                _gumps.CloseAll();
                break;
            case "save":
                if (_usecode!.InUsecode || _usecode.WaitingForChoice)
                {
                    AgentLog("usecode is running (finish it first)");
                    break;
                }

                SaveGame.Write(arg, _map, _npcs, _usecode, _clock, _combat.InCombat, _music, _combat.Spawned, _combat.Armageddon);
                AgentLog($"saved to {SaveGame.SlotDir(arg)}");
                break;
            case "load":
                if (!SaveGame.Exists(arg))
                {
                    AgentLog($"no saved game '{arg}'");
                    break;
                }

                U7Paths.GameDatOverride = SaveGame.SlotDir(arg);
                GetTree().ReloadCurrentScene();
                AgentLog("loading...");
                break;
            default:
                AgentLog($"unknown command; {AgentHelp}");
                break;
        }
    }

    /// <summary>In barge mode, steer the barge's centre to the tile (a held click there) until it stops.</summary>
    void AgentSail(int tx, int ty)
    {
        if (_barges.Moving is not { } barge)
        {
            AgentLog("not in barge mode");
            return;
        }

        SteerBarge(barge, new TileCoord(tx, ty, _avatar.Avatar.Tz), WalkSpeed.Keyboard(false, false, false));
        Engine.TimeScale = 6;
        _agentElapsed = 0;
        _agentLimit = 120;
        _agentBusy = () => barge.IsMoving && _usecode is not { WaitingForPlayer: true };
    }

    void AgentWalk(int tx, int ty)
    {
        // Open containers put the game in gump mode, which stops walking (Exult default).
        _gumps.CloseAll();
        _agentClick = new Vector2I(tx, ty);
        AgentAwaitArrival(tx, ty);
    }

    /// <summary>What a drag of a thing in the world does once it was dropped elsewhere (its eggs, gravity), for later.</summary>
    Action AgentLiftedFrom(U7Object item)
    {
        if (item.Container is not null)
        {
            return () => { };
        }

        var drag = new DragState { Object = item, FromWorld = true, OldTx = item.Tx, OldTy = item.Ty, OldTz = item.Tz };
        return () => _gumps.LiftedFromWorld?.Invoke(item, drag);
    }

    /// <summary>The console's mouse in the gumps' virtual pixels.</summary>
    Vector2I AgentVirt()
    {
        var screen = (MouseWorld() - _camera.GlobalPosition) * _zoom + GetViewport().GetVisibleRect().Size / 2;
        return new Vector2I(Mathf.FloorToInt(screen.X / _zoom), Mathf.FloorToInt(screen.Y / _zoom));
    }

    /// <summary>A point (virtual pixels) where the mouse would be on this gump, not under another.</summary>
    Vector2I? AgentPointOn(Gump gump)
    {
        for (var dy = -100; dy <= 160; dy += 2)
        {
            for (var dx = -100; dx <= 200; dx += 2)
            {
                if (_gumps.FindGump(gump.X + dx, gump.Y + dy, _gumpView) == gump)
                {
                    return new Vector2I(gump.X + dx, gump.Y + dy);
                }
            }
        }

        return null;
    }

    /// <summary>The command goes on until the avatar arrives, is blocked for two seconds or a conversation starts.</summary>
    void AgentAwaitArrival(int tx, int ty)
    {
        Engine.TimeScale = 6;
        _agentElapsed = 0;
        _agentLimit = 90;
        var av = _avatar.Avatar;
        var last = (av.Tx, av.Ty);
        var lastMove = 0.0;
        _agentBusy = () =>
        {
            if (_usecode is { WaitingForPlayer: true })
            {
                return false; // a conversation started on the way
            }

            if ((av.Tx, av.Ty) != last)
            {
                last = (av.Tx, av.Ty);
                lastMove = _agentElapsed;
            }

            // Done on arrival, or when no step was made for two seconds (blocked).
            var arrived = Math.Abs(av.Tx - tx) <= 0 && Math.Abs(av.Ty - ty) <= 0;
            if (arrived || _agentElapsed - lastMove > 2.0)
            {
                _avatar.ClearPath();
                if (!arrived)
                {
                    AgentLog(lastMove == 0
                        ? $"no way to {tx},{ty} (no path, or blocked at once)"
                        : $"stopped at {av.Tx},{av.Ty}, short of {tx},{ty} (blocked)");
                }

                return false;
            }

            return true;
        };
    }

    void AgentUse(U7Object obj)
    {
        // Exult Game_window::double_clicked: in combat mode an enemy is attacked; otherwise its usecode runs.
        if (_combat.InCombat && obj.IsActor && CombatEngine.IsEnemy(_avatar.Avatar.Alignment, obj.Alignment))
        {
            _combat.AttackClicked(obj);
            AgentLog(_combat.LastMessage);
            return;
        }

        // As ActivateUnderMouse: the avatar opens its paperdoll, so does a party member with gumps open
        // or in combat; other NPCs run their usecode.
        if (((!obj.IsActor || obj.NpcNum == 0) && _gumps.ShowGump(obj)) || (obj.IsActor && obj.NpcNum > 0 && ShowPartyInventory(obj)))
        {
            AgentLog($"opened {AgentDescribe(obj)}:");
            AgentInventory(obj, 1);
            return;
        }

        RunUsecode(obj);
        if (_usecode is { InUsecode: false, WaitingForChoice: false })
        {
            AgentLog($"usecode ran: {_statusExtra}");
        }
    }

    U7Object? AgentTarget(string arg)
    {
        if (arg.StartsWith("npc:"))
        {
            return AgentNpc(arg[4..]);
        }

        if (int.TryParse(arg, out var id) && _agentIds.TryGetValue(id, out var obj))
        {
            return obj;
        }

        return AgentNpc(arg);
    }

    /// <summary>An object flag by number or by the name of its <see cref="ObjFlag"/> constant.</summary>
    static int AgentFlag(string arg)
    {
        if (int.TryParse(arg, out var flag))
        {
            return flag;
        }

        var field = typeof(ObjFlag).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .FirstOrDefault(f => f.IsLiteral && f.Name.Equals(arg, StringComparison.OrdinalIgnoreCase));
        return field?.GetRawConstantValue() is int value ? value : throw new ArgumentException($"no flag {arg}");
    }

    U7Object? AgentNpc(string arg)
    {
        if (int.TryParse(arg, out var num))
        {
            return num >= 0 && num < _npcs.Count ? _npcs[num] : null;
        }

        return _npcs.FirstOrDefault(n => n is not null && n.NpcName.Equals(arg, StringComparison.OrdinalIgnoreCase));
    }

    IEnumerable<U7Object> AgentObjectsAround(int radius)
    {
        var av = _avatar.Avatar;
        var c0x = (av.Tx - radius) / U7Constants.TilesPerChunk - 1;
        var c1x = (av.Tx + radius) / U7Constants.TilesPerChunk + 1;
        var c0y = (av.Ty - radius) / U7Constants.TilesPerChunk - 1;
        var c1y = (av.Ty + radius) / U7Constants.TilesPerChunk + 1;
        for (var cy = c0y; cy <= c1y; cy++)
        {
            for (var cx = c0x; cx <= c1x; cx++)
            {
                foreach (var obj in _map.ObjectsInChunk(cx, cy))
                {
                    if (!obj.Removed && obj != av && AgentDist(obj) <= radius)
                    {
                        yield return obj;
                    }
                }
            }
        }
    }

    int AgentDist(U7Object obj) =>
        new TileCoord(obj.Tx, obj.Ty, 0).Distance2d(new TileCoord(_avatar.Avatar.Tx, _avatar.Avatar.Ty, 0));

    void AgentLook(int radius)
    {
        var list = AgentObjectsAround(radius)
            .Where(o => o.IsActor || (o.Kind == ObjectKind.Ireg && !o.InvisibleEgg) || (o.IsEgg && o.EggType == World.EggType.Usecode))
            .OrderBy(AgentDist)
            .Take(60)
            .ToList();
        AgentLog($"{list.Count} things within {radius}:");
        foreach (var obj in list)
        {
            AgentLog("  " + AgentDescribe(obj));
        }
    }

    void AgentFind(string text)
    {
        // "find owned": portable things, in containers too, not okay to take (Exult's theft).
        var hits = (text == "owned"
                ? AgentObjectsAround(120).Where(o => !o.IsActor).SelectMany(o =>
                {
                    var all = new List<U7Object> { o };
                    o.CollectContents(all);
                    return all;
                }).Where(o => o.Kind == ObjectKind.Ireg && !o.IsEgg && !o.GetFlag(ObjFlag.OkayToTake) &&
                              _catalog[o.Shape].Weight > 0)
                : AgentObjectsAround(120).Where(o =>
                    AgentName(o).Contains(text, StringComparison.OrdinalIgnoreCase) || o.Shape.ToString() == text))
            .Concat(text == "owned" ? [] : AgentPartyItems().Where(o =>
                AgentName(o).Contains(text, StringComparison.OrdinalIgnoreCase) || o.Shape.ToString() == text))
            .Distinct()
            .OrderBy(o => AgentDist(Inventory.Outermost(o)))
            .Take(30)
            .ToList();
        foreach (var n in _npcs)
        {
            if (n is not null && !hits.Contains(n) && n.NpcName.Contains(text, StringComparison.OrdinalIgnoreCase))
            {
                hits.Add(n);
            }
        }

        foreach (var obj in hits)
        {
            AgentLog("  " + AgentDescribe(obj) + (obj.Container is { } c ? $" in #{c.Id} {AgentName(c)}" : ""));
        }
    }

    /// <summary>What the party carries, in its packs too.</summary>
    IEnumerable<U7Object> AgentPartyItems()
    {
        var all = new List<U7Object>();
        foreach (var member in _party.Members.Prepend(_avatar.Avatar))
        {
            member.CollectContents(all);
        }

        return all;
    }

    string AgentName(U7Object obj) =>
        obj.NpcNum >= 0 && obj.NpcName.Length > 0 ? obj.NpcName :
        obj.IsMonster ? obj.NpcName :
        _catalog[obj.Shape].Name is { Length: > 0 } shapeName ? Singular(shapeName) : $"shape {obj.Shape}";

    /// <summary>"/gold coin//s" → "gold coin" (Exult plural pattern: /singular/plural-suffix).</summary>
    static string Singular(string name) =>
        name.StartsWith('/') && name.IndexOf('/', 1) is var end and > 0 ? name[1..end] : name;

    /// <summary>The status flags <c>look</c> and <c>npc</c> show.</summary>
    static readonly (int Flag, string Word)[] StatusWords =
    [
        (ObjFlag.Asleep, "asleep"), (ObjFlag.Poisoned, "poisoned"), (ObjFlag.Paralyzed, "paralyzed"), (ObjFlag.Invisible, "invisible"),
        (ObjFlag.Charmed, "charmed"), (ObjFlag.Cursed, "cursed"), (ObjFlag.Protection, "protected"), (ObjFlag.Might, "might")
    ];

    /// <summary>Exult <c>Actor::Attack_mode</c> by number (the combat-mode button's frames).</summary>
    static readonly string[] AlignmentNames = ["neutral", "good", "evil", "chaotic"];

    static readonly string[] AttackModeNames =
    [
        "nearest", "weakest", "strongest", "berserk", "protect", "defend", "flank", "flee", "random", "manual",
        "10", "11", "12", "13", "14", "15"
    ];

    string AgentDescribe(U7Object obj)
    {
        _agentIds[obj.Id] = obj;
        var sb = new StringBuilder($"#{obj.Id} {AgentName(obj)} (shape {obj.Shape}:{obj.Frame & 31}) at {obj.Tx},{obj.Ty},{obj.Tz} d={AgentDist(obj)}");
        if (obj.NpcNum >= 0)
        {
            sb.Append($" npc {obj.NpcNum} sched {obj.ScheduleType}");
            if (obj.GetFlag(ObjFlag.Met))
            {
                sb.Append(" met");
            }

            if (obj.GetFlag(ObjFlag.InParty))
            {
                sb.Append(" party");
            }
        }
        else if (obj.IsActor)
        {
            sb.Append($" monster sched {obj.ScheduleType}");
        }

        if (obj.IsActor)
        {
            sb.Append($" {AlignmentNames[obj.Alignment & 3]} faces {"N?E?S?W?"[ActorWalker.FacingOfFrame(obj.Frame)]} hp {obj.GetProp(ActorProp.Health)}");
            foreach (var (flag, word) in StatusWords)
            {
                if (obj.GetFlag(flag))
                {
                    sb.Append(' ').Append(word);
                }
            }
        }
        else if (obj.Kind == ObjectKind.Ireg && !obj.IsEgg && !obj.GetFlag(ObjFlag.OkayToTake))
        {
            sb.Append(" owned"); // Not okay to take: moving it may be seen as theft.
        }

        if (obj.IsDead)
        {
            sb.Append(" DEAD");
        }

        if (obj.IsEgg)
        {
            sb.Append($" egg usecode 0x{obj.GetUsecode():X3}");
        }

        if (obj.SpellCircles is { } circles)
        {
            sb.Append($" spells {Convert.ToHexString(circles)}");
        }

        if (obj.Quality != 0 && !obj.IsActor)
        {
            sb.Append($" q={obj.Quality}");
        }

        if (obj.Contents.Count > 0)
        {
            sb.Append($" [{obj.Contents.Count} items]");
        }

        return sb.ToString();
    }

    void AgentInventory(U7Object owner, int depth)
    {
        foreach (var item in owner.Contents)
        {
            if (!item.Removed)
            {
                AgentLog(new string(' ', 2 + depth * 2) + AgentDescribe(item) + (item.ReadySlot >= 0 ? $" readied {item.ReadySlot}" : ""));
                AgentInventory(item, depth + 1);
            }
        }
    }

    void AgentStatus()
    {
        if (_usecode is not { } vm)
        {
            return;
        }

        var av = _avatar.Avatar;
        var party = string.Join(", ", _party.Members.Select(m => m.NpcName));
        var gold = av.Contents.Count == 0 ? 0 : AgentCount(644);
        var scripts = vm.Scripts.Count(sc => sc.Obj == av && !sc.Done);
        AgentLog($"@ {av.Tx},{av.Ty},{av.Tz} {_clock.HudText()} hp {av.GetProp(ActorProp.Health)} gold {gold} party [{party}]" +
                 (ObjFlag.DontMoveMode(av) ? " (avatar flag 16/22)" : "") +
                 (vm.InUsecodeControl(av) ? $" (avatar under usecode control, {scripts} scripts)" : "") +
                 (vm.FadedOut ? " (screen faded out)" : ""));
        if (_barges.Moving is { } moving)
        {
            AgentLog($"barge mode: barge at {moving.Obj.Tx},{moving.Obj.Ty},{moving.Obj.Tz} centre {moving.Center.Tx},{moving.Center.Ty} " +
                     $"facing {"NESW"[moving.Obj.BargeDir]}{(moving.IsMoving ? ", moving" : "")}");
        }

        if (_cameraTile is { } viewTile)
        {
            AgentLog($"view centred on {viewTile.Tx},{viewTile.Ty},{viewTile.Tz}");
        }
        else if (_cameraActor is { } viewActor)
        {
            AgentLog($"view follows {AgentName(viewActor)} at {viewActor.Tx},{viewActor.Ty}");
        }

        if (_effects.Sprites.Count > 0)
        {
            AgentLog("sprites " + string.Join(", ", _effects.Sprites.Select(e =>
                $"{e.Sprite} frame {e.Frame}/{e.Frames} at {e.Pos.Tx},{e.Pos.Ty},{e.Pos.Tz}")));
        }

        if (_effects.Weather.Count > 0 || _clock.Overcast != 0 || _clock.Fog != 0)
        {
            AgentLog($"weather {_effects.GetWeather()}: [{string.Join(", ", _effects.Weather.Select(w => w.Name))}]" +
                     $" overcast {_clock.Overcast} fog {_clock.Fog} flashes {_effects.Flashes}");
        }

        if (_combat.Missiles.Count > 0)
        {
            AgentLog("missiles " + string.Join(", ", _combat.Missiles.Select(m =>
                $"{m.SpriteShape} frame {m.Frame} at {m.Pos.Tx},{m.Pos.Ty},{m.Pos.Tz}" +
                $" -> {(m.Target is { } t ? AgentName(t) : "ahead")}{(m.ReturnPath ? " (coming back)" : "")}")));
        }

        if (_combat.HomingMissiles.Count > 0)
        {
            AgentLog("homing " + string.Join(", ", _combat.HomingMissiles.Select(h =>
                $"sprite {h.Sprite} frame {h.Frame} at {h.Pos.Tx},{h.Pos.Ty},{h.Pos.Tz} {h.AgeMs / 1000.0:0.0}s" +
                $" -> {(h.Target is { } t ? AgentName(t) : h.Stationary ? "parked" : "looking")}")));
        }

        if (vm.WaitingForChoice)
        {
            AgentLog($"WAIT {vm.Wait}" + (vm.Wait == UsecodeWait.ClickToContinue ? $"{_conversation.PageNote}: {vm.Conv.NpcText.Replace('\n', ' ')}" : "") +
                     (vm is { Wait: UsecodeWait.Picture or UsecodeWait.WizardEye, Picture: { } pic }
                         ? $": sprite {pic.Sprite}:{pic.Frame}" + (pic.Mark is { } mark ? $", mark at {mark.X},{mark.Y}" : "") +
                           (pic.Area is { } at ? $", view at {at.Tx},{at.Ty}" : "") +
                           (vm.Wait == UsecodeWait.WizardEye ? $", {vm.WizardEyeMs / 1000:0.0} s left" : "")
                         : "") +
                     (vm is { Wait: UsecodeWait.Picture, Sign: { } sign }
                         ? $": sign {sign.GumpShape}: {string.Join(" / ", sign.Lines)} ({string.Join(" / ", sign.Lines.Select(Gumps.SignGump.Readable))})"
                         : "") +
                     (vm.Wait is UsecodeWait.Converse or UsecodeWait.SelectMenu or UsecodeWait.SelectMenuIndex
                         ? $"  answers: {string.Join(" | ", vm.Conv.Answers)}"
                         : ""));
        }
    }

    static readonly string[] AgentCriteria =
        ["cached in", "party near", "avatar near", "avatar far", "avatar footpad", "party footpad", "something on", "external"];

    /// <summary>An egg type by number or by name (missile, usecode, ...).</summary>
    static int AgentEggType(string arg) =>
        int.TryParse(arg, out var n) ? n : Array.IndexOf(U7.World.EggType.Names, arg.ToLowerInvariant());

    /// <summary>The eggs within <paramref name="radius"/> tiles of the avatar (of one type, or all): criteria, chance, data, state.</summary>
    void AgentEggs(int type, int radius)
    {
        var av = _avatar.Avatar;
        var here = new TileCoord(av.Tx, av.Ty, av.Tz);
        // (Distinct: a radius past half the map wraps onto chunks already searched.)
        var eggs = _map.EggsNear(av.Tx, av.Ty, radius).Distinct()
            .Where(e => !e.Removed && (type < 0 || e.EggType == type) &&
                        here.Distance2d(new TileCoord(e.Tx, e.Ty, e.Tz)) <= radius)
            .OrderBy(AgentDist)
            .ToList();
        AgentLog($"{eggs.Count} eggs within {radius}" + (type >= 0 ? $" of type {U7.World.EggType.Name(type)}" : "") + ":");
        foreach (var e in eggs.Take(40))
        {
            _agentIds[e.Id] = e;
            var crit = (uint)e.EggCriteria < (uint)AgentCriteria.Length ? AgentCriteria[e.EggCriteria] : $"{e.EggCriteria}";
            AgentLog($"  #{e.Id} {U7.World.EggType.Name(e.EggType)} egg (shape {e.Shape}:{e.Frame}) at {e.Tx},{e.Ty},{e.Tz} d={AgentDist(e)}" +
                     $" {crit} dist {e.EggDistance} prob {e.EggProbability} d1 0x{e.EggData1:X4} d2 0x{e.EggData2:X4}" +
                     (e.EggType == U7.World.EggType.Weather ? $" (weather {e.EggData1 & 0xff} for {e.EggData1 >> 8} min)" : "") +
                     ((e.EggFlags & U7.World.EggFlag.Hatched) != 0 ? " hatched" : "") +
                     ((e.EggFlags & U7.World.EggFlag.Once) != 0 ? " once" : ""));
        }
    }

    static readonly string[] AgentWeatherNames = ["none", "snowstorm", "storm", "sparkles", "fog", "overcast", "clouds"];

    /// <summary>The weather (Exult <c>get_weather</c>), the clock's counters and each weather effect.</summary>
    void AgentWeather()
    {
        var cur = _effects.GetWeather();
        AgentLog($"weather {cur} ({((uint)cur < (uint)AgentWeatherNames.Length ? AgentWeatherNames[cur] : "?")}), " +
                 $"overcast {_clock.Overcast} ({(_clock.Cloudy ? "cloudy" : "clear")}), fog {_clock.Fog} ({(_clock.Foggy ? "foggy" : "none")}), " +
                 $"{_effects.Flashes} flashes so far{(_effects.LightningFlash ? ", flashing now" : "")}, " +
                 $"{(_lighting.InDungeon ? "in a dungeon" : "outdoors")}, {_clock.HudText()}");
        foreach (var w in _effects.Weather)
        {
            AgentLog("  " + w.Describe(_effects.NowMs));
        }
    }

    int AgentCount(int shape)
    {
        var total = 0;
        foreach (var member in _party.Members.Prepend(_avatar.Avatar))
        {
            var list = new List<U7Object>();
            member.CollectContents(list);
            total += list.Where(o => o.Shape == shape && !o.Removed).Sum(o => Inventory.GetQuantity(o, _catalog));
        }

        return total;
    }

    void AgentLog(string line)
    {
        if (_agentDir is not null)
        {
            File.AppendAllText(Path.Combine(_agentDir, "out.txt"), line + "\n");
        }
    }
}
