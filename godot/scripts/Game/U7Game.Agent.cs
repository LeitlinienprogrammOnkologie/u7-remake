using System.IO;
using System.Text;
using Godot;
using U7.Actors;
using U7.Core;
using U7.Data;
using U7.Gumps;
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
        "save <slot> | load <slot> | tile <x> <y> [z] | eggs [type] [radius] | weather [<n> [min] | lightning | eggs [radius]] | sprite <n> [frame] | damage <n> [type] | arena | combat [off] | close";

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

            // Exult's fades hold up the game: a command is over once its fade is.
            if (_usecode is { Wait: UsecodeWait.Fade } && _agentElapsed < _agentLimit + 10)
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

        if (_agentBusy is null && _usecode is { Wait: UsecodeWait.Fade })
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
                // A test shortcut: an object flag by number or ObjFlag name (invisible, charmed, poisoned, ...).
                var target = AgentTarget(parts[1]) ?? throw new ArgumentException("no such object/npc");
                var flag = AgentFlag(parts[2]);
                if (parts.Length > 3 && parts[3] == "0")
                {
                    target.ClearFlag(flag);
                }
                else
                {
                    target.SetFlag(flag);
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
                var target = AgentTarget(arg) ?? throw new ArgumentException("no such object/npc");
                AgentWalk(target.Tx + 1, target.Ty + 1);
                break;
            }
            case "tp":
            {
                var fromTx = av.Tx;
                var fromTy = av.Ty;
                var tz = parts.Length > 3 ? int.Parse(parts[3]) : av.Tz;
                _map.MoveObject(av, int.Parse(parts[1]), int.Parse(parts[2]), tz);
                _party.FollowTeleport();
                _eggs.Activate(av, fromTx, fromTy);
                break;
            }
            case "talk":
            case "use":
            {
                var target = AgentTarget(arg) ?? throw new ArgumentException("no such object/npc");
                AgentUse(target);
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
                var target = AgentTarget(arg) ?? throw new ArgumentException("no such object");
                AgentLog(Equipment.AddToActor(av, target, _catalog, _map)
                    ? $"took {AgentDescribe(target)}"
                    : "cannot take it (too heavy or no room)");
                break;
            }
            case "put":
            {
                // A drag into a container's gump (also takes a readied item off).
                var item = AgentTarget(parts[1]) ?? throw new ArgumentException("no such object");
                var cont = AgentTarget(parts[2]) ?? throw new ArgumentException("no such container");
                AgentLog(Equipment.TryPlace(_map, item, cont, 8, 8, _catalog)
                    ? $"put {AgentDescribe(item)} into {AgentName(cont)}"
                    : "it does not fit");
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
                var conv = _usecode!.Conv;
                var answer = arg.StartsWith('#') ? conv.Answers[int.Parse(arg[1..]) - 1]
                    : conv.Answers.FirstOrDefault(a => a.Equals(arg, StringComparison.OrdinalIgnoreCase))
                      ?? throw new ArgumentException($"no answer '{arg}'");
                _usecode.Choose(answer, conv.Answers.IndexOf(answer));
                _conversation.Refresh();
                break;
            }
            case "num":
                _usecode!.ResumeWait(UsecodeValue.FromInt(int.Parse(arg)));
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
                // Hold a walking direction (n, ne, e, ...) for a while, like a held key or button.
                var dir = Array.IndexOf(["n", "ne", "e", "se", "s", "sw", "w", "nw"], parts[1]);
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
                // Damage to the avatar through the normal path (no attacker): the red pulse or the outline.
                var hp = av.GetProp(ActorProp.Health);
                var pulses = _screenFx.Pulses;
                var type = parts.Length > 2 ? int.Parse(parts[2]) : 0;
                var taken = _combat.ReduceHealth(av, int.Parse(parts[1]), null, type);
                AgentLog($"damage {parts[1]} type {type}: took {taken}, hp {hp} -> {av.GetProp(ActorProp.Health)} of {av.GetProp(ActorProp.Strength)}, " +
                         (_screenFx.Pulses > pulses ? "red pulse" : av.HitUntilMsec > Time.GetTicksMsec() ? "red outline" : "nothing"));
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
                return false;
            }

            return true;
        };
    }

    void AgentUse(U7Object obj)
    {
        // The console attacks enemies straight away (a player turns combat on first).
        if (obj.IsActor && CombatEngine.IsEnemy(_avatar.Avatar.Alignment, obj.Alignment))
        {
            _combat.AttackClicked(obj);
            AgentLog(_combat.LastMessage);
            return;
        }

        // As ActivateUnderMouse: the avatar opens its paperdoll, other NPCs run their usecode.
        if (obj.NpcNum <= 0 && _gumps.ShowGump(obj))
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
        var hits = AgentObjectsAround(120)
            .Where(o => AgentName(o).Contains(text, StringComparison.OrdinalIgnoreCase) || o.Shape.ToString() == text)
            .OrderBy(AgentDist)
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
            AgentLog("  " + AgentDescribe(obj));
        }
    }

    string AgentName(U7Object obj) =>
        obj.NpcNum >= 0 && obj.NpcName.Length > 0 ? obj.NpcName :
        obj.IsMonster ? obj.NpcName :
        _catalog[obj.Shape].Name is { Length: > 0 } shapeName ? Singular(shapeName) : $"shape {obj.Shape}";

    /// <summary>"/gold coin//s" → "gold coin" (Exult plural pattern: /singular/plural-suffix).</summary>
    static string Singular(string name) =>
        name.StartsWith('/') && name.IndexOf('/', 1) is var end and > 0 ? name[1..end] : name;

    /// <summary>Exult <c>Actor::Attack_mode</c> by number (the combat-mode button's frames).</summary>
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
            AgentLog($"WAIT {vm.Wait}" + (vm.Wait == UsecodeWait.ClickToContinue ? $": {vm.Conv.NpcText.Replace('\n', ' ')}" : "") +
                     (vm is { Wait: UsecodeWait.Picture or UsecodeWait.WizardEye, Picture: { } pic }
                         ? $": sprite {pic.Sprite}:{pic.Frame}" + (pic.Mark is { } mark ? $", mark at {mark.X},{mark.Y}" : "") +
                           (pic.Area is { } at ? $", view at {at.Tx},{at.Ty}" : "") +
                           (vm.Wait == UsecodeWait.WizardEye ? $", {vm.WizardEyeMs / 1000:0.0} s left" : "")
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
