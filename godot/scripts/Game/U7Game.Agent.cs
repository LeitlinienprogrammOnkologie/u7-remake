using System.IO;
using System.Text;
using Godot;
using U7.Actors;
using U7.Core;
using U7.Data;
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
        "look [r] | find <text> | npc <num|name> | state | inv [npcnum] | flags | stubs | " +
        "walk <x> <y> | walkto <id|npc:num> | steer <dir> <sec> [ms] | tp <x> <y> [z] | talk <npcnum|name> | use <id> | take <id> | " +
        "cont [n|all] | choose <answer|#n> | num <n> | click <id> | wait <sec> | hour <h> | shot <name> | " +
        "save <slot> | load <slot> | tile <x> <y> [z] | close";

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
                    AgentLog(AgentDescribe(npcObj) + $" typeflags 0x{npcObj.TypeFlags:X}");
                }

                break;
            case "state":
                break; // AgentStatus prints it
            case "inv":
                AgentInventory(parts.Length > 1 ? AgentNpc(arg) ?? av : av, 0);
                break;
            case "flags":
                for (var i = 0; i < _usecode!.GFlags.Length; i++)
                {
                    if (_usecode.GFlags[i] != _agentFlags0[i])
                    {
                        AgentLog($"flag 0x{i:X3} = {_usecode.GFlags[i]} (was {_agentFlags0[i]})");
                    }
                }

                break;
            case "stubs":
                AgentLog(BgIntrinsics.StubSummary());
                break;
            case "walk":
                AgentWalk(int.Parse(parts[1]), int.Parse(parts[2]));
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
            case "take":
            {
                var target = AgentTarget(arg) ?? throw new ArgumentException("no such object");
                AgentLog(Equipment.AddToActor(av, target, _catalog, _map)
                    ? $"took {AgentDescribe(target)}"
                    : "cannot take it (too heavy or no room)");
                break;
            }
            case "cont":
            {
                var n = arg == "all" ? 60 : parts.Length > 1 ? int.Parse(parts[1]) : 1;
                for (var i = 0; i < n && _usecode is { Wait: UsecodeWait.ClickToContinue or UsecodeWait.BookPage }; i++)
                {
                    if (_usecode.Wait == UsecodeWait.BookPage)
                    {
                        _usecode.TurnBookPage();
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
                var target = AgentTarget(arg) ?? throw new ArgumentException("no such object");
                var arr = UsecodeValue.FromArray(4, UsecodeValue.FromObject(target));
                arr.PutElem(1, UsecodeValue.FromInt(target.Tx));
                arr.PutElem(2, UsecodeValue.FromInt(target.Ty));
                arr.PutElem(3, UsecodeValue.FromInt(target.Tz));
                _usecode!.ResumeWait(arr);
                _conversation.Refresh();
                break;
            }
            case "wait":
            {
                Engine.TimeScale = 8;
                _agentElapsed = 0;
                _agentLimit = double.Parse(arg, System.Globalization.CultureInfo.InvariantCulture);
                _agentBusy = () => _usecode is not { WaitingForChoice: true };
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
            case "hour":
            {
                var h = int.Parse(arg);
                _clock.SkipHours(((h - _clock.Hour) % 24 + 24) % 24);
                break;
            }
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
                SaveGame.Write(arg, _map, _npcs, _usecode, _clock, _combat.InCombat, _music, _combat.Spawned);
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
            if (_usecode is { WaitingForChoice: true })
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
        if (obj.IsActor && CombatEngine.IsEnemy(_avatar.Avatar.Alignment, obj.Alignment))
        {
            _combat.Attack(_avatar.Avatar, obj);
            AgentLog(_combat.LastMessage);
            return;
        }

        if (obj.NpcNum < 0 && _gumps.ShowGump(obj))
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
                 (vm.InUsecodeControl(av) ? $" (avatar under usecode control, {scripts} scripts)" : ""));
        if (vm.WaitingForChoice)
        {
            AgentLog($"WAIT {vm.Wait}" + (vm.Wait == UsecodeWait.ClickToContinue ? $": {vm.Conv.NpcText.Replace('\n', ' ')}" : "") +
                     (vm.Wait is UsecodeWait.Converse or UsecodeWait.SelectMenu or UsecodeWait.SelectMenuIndex
                         ? $"  answers: {string.Join(" | ", vm.Conv.Answers)}"
                         : ""));
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
