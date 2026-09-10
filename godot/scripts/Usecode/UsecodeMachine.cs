using System.Buffers.Binary;
using System.IO;
using System.Text;
using Godot;
using U7.Core;
using U7.Data;
using U7.Gumps;

namespace U7.Usecode;

public enum UsecodeWait
{
    None,
    Converse,
    SelectMenu,
    SelectMenuIndex,
    ClickOnItem
}

/// <summary>
/// Black Gate usecode interpreter. Opcode loop ports Exult
/// <c>Usecode_internal::run()</c>; CALL/CALLE use the phantom-arg rule
/// (<c>is_object_fun</c>: id &lt; 0x800).
/// </summary>
public sealed class UsecodeMachine
{
    public const int LastGflag = 2047;
    public const int StackSize = 1024;
    public const int AnyShape = -359;
    const int MaxInstructionsPerRun = 250_000;

    public UsecodeFile File { get; }
    public GameMap Map { get; }
    public U7Object Avatar { get; }
    public ShapeCatalog Catalog { get; }
    public Conversation Conv { get; } = new();
    public byte[] GFlags { get; } = new byte[LastGflag + 1];
    public bool InUsecode => _callStack.Count > 0;
    public UsecodeWait Wait { get; private set; }
    public bool WaitingForChoice => Wait != UsecodeWait.None;
    public string? UserChoice { get; private set; }
    public string StringReg { get; private set; } = "";
    public string LastIntrinsic { get; private set; } = "";
    public int LastFunctionId { get; private set; } = -1;
    public int LastIp { get; private set; }
    public int LastEvent { get; private set; }
    public string HudMessage { get; set; } = "";
    public readonly List<string> FlagLog = new();
    public readonly List<string> IntrinsicLog = new();
    public event Action<string>? Say;
    public event Action<U7Object, string>? ItemSay;
    public event Action? AnswersChanged;
    public event Action? FacesChanged;
    public GumpManager? Gumps { get; set; }
    public List<U7Object?> Npcs { get; set; } = new();
    public U7.World.GameClock? Clock { get; set; }
    public U7.Actors.ScheduleRunner? Schedules { get; set; }
    public U7.Actors.PartyManager? Party { get; set; }
    public U7.Actors.CombatEngine? Combat { get; set; }
    public U7.Audio.MusicPlayer? Music { get; set; }
    public U7.World.EggHatcher? Eggs { get; set; }
    /// <summary>Called when a script steps the avatar, so eggs and followers react.</summary>
    public Action<U7Object>? AvatarMovedByScript { get; set; }
    /// <summary>Exult <c>fade_palette</c>: true while the screen is faded to black.</summary>
    public bool FadedOut { get; set; }
    /// <summary>Set by the restart_game intrinsic; the game reloads from the initial data.</summary>
    public bool RestartRequested { get; set; }
    public const double StdDelaySeconds = U7.Core.U7Constants.StandardDelayMs / 1000.0;

    readonly List<UsecodeScript> _scripts = new();
    public IReadOnlyList<UsecodeScript> Scripts => _scripts;

    /// <summary>Exult <c>Usecode_internal::create_script</c> + <c>Usecode_script::start</c>.</summary>
    public void StartScript(U7Object? obj, UsecodeValue code, double delaySeconds)
    {
        if (obj is null)
        {
            return;
        }

        var script = new UsecodeScript(this, obj, code, delaySeconds);
        if (!script.NoHalt)
        {
            TerminateScripts(obj);
        }

        _scripts.Add(script);
    }

    /// <summary>Exult <c>Usecode_script::terminate</c>.</summary>
    public void TerminateScripts(U7Object obj)
    {
        foreach (var s in _scripts)
        {
            if (s.Obj == obj)
            {
                s.Halt();
            }
        }
    }

    public bool HasScript(U7Object obj) => _scripts.Any(s => s.Obj == obj && !s.Done);

    /// <summary>Exult <c>Usecode_script::save</c> for every running script on the object.</summary>
    public IEnumerable<byte[]> SaveScripts(U7Object obj)
    {
        foreach (var s in _scripts)
        {
            if (s.Obj != obj || s.Done)
            {
                continue;
            }

            using var ms = new System.IO.MemoryStream();
            using var w = new System.IO.BinaryWriter(ms);
            w.Write((ushort)s.Count);
            w.Write((ushort)Math.Max(0, s.Index));
            var ok = true;
            for (var j = 0; j < s.Count && ok; j++)
            {
                ok = SaveValue(w, s.Code.GetElem(j));
            }

            if (!ok)
            {
                continue;
            }

            w.Write((ushort)0); // frame_index
            w.Write((ushort)(s.NoHalt ? 1 : 0));
            w.Write((uint)Math.Max(0, (int)(s.Wait * 1000)));
            w.Flush();
            yield return ms.ToArray();
        }
    }

    /// <summary>Exult <c>Usecode_script::restore</c>: recreate a script from a saved blob.</summary>
    public void RestoreScript(U7Object obj, byte[] blob)
    {
        try
        {
            using var r = new System.IO.BinaryReader(new System.IO.MemoryStream(blob));
            int cnt = r.ReadUInt16();
            int index = r.ReadUInt16();
            var code = UsecodeValue.FromArray(cnt);
            for (var j = 0; j < cnt; j++)
            {
                var v = RestoreValue(r);
                if (v is null)
                {
                    return;
                }

                code.PutElem(j, v);
            }

            if (r.BaseStream.Length - r.BaseStream.Position < 8)
            {
                return;
            }

            r.ReadUInt16(); // frame_index
            var noHalt = r.ReadUInt16() != 0;
            var delayMs = r.ReadUInt32();
            _scripts.Add(new UsecodeScript(this, obj, code, index, noHalt, delayMs / 1000.0));
        }
        catch (Exception ex)
        {
            Godot.GD.Print($"script restore failed: {ex.Message}");
        }
    }

    // Exult Usecode_value::save / restore (int 0, string 1, array 2, pointer 3).
    static bool SaveValue(System.IO.BinaryWriter w, UsecodeValue v)
    {
        if (v.IsArray)
        {
            w.Write((byte)2);
            w.Write((ushort)v.ArraySize);
            for (var i = 0; i < v.ArraySize; i++)
            {
                if (!SaveValue(w, v.GetElem(i)))
                {
                    return false;
                }
            }

            return true;
        }

        if (v.IsString)
        {
            var bytes = System.Text.Encoding.Latin1.GetBytes(v.StrValue ?? "");
            w.Write((byte)1);
            w.Write((ushort)bytes.Length);
            w.Write(bytes);
            return true;
        }

        if (v.IsPtr)
        {
            w.Write((byte)3);
            w.Write((uint)0);
            return true;
        }

        w.Write((byte)0);
        w.Write((int)v.IntValue);
        return true;
    }

    static UsecodeValue? RestoreValue(System.IO.BinaryReader r)
    {
        var type = r.ReadByte();
        switch (type)
        {
            case 0:
                return UsecodeValue.FromInt(r.ReadInt32());
            case 1:
            {
                int len = r.ReadUInt16();
                return UsecodeValue.FromString(System.Text.Encoding.Latin1.GetString(r.ReadBytes(len)));
            }
            case 2:
            {
                int n = r.ReadUInt16();
                var arr = UsecodeValue.FromArray(n);
                for (var i = 0; i < n; i++)
                {
                    var e = RestoreValue(r);
                    if (e is null)
                    {
                        return null;
                    }

                    arr.PutElem(i, e);
                }

                return arr;
            }
            case 3:
                r.ReadUInt32();
                return UsecodeValue.FromObject(null);
            default:
                return null; // class types: not supported
        }
    }

    /// <summary>
    /// Advance running scripts (Exult's time queue). Paused while usecode is
    /// executing or waiting on a conversation, since scripts call back into
    /// the machine.
    /// </summary>
    public void TickScripts(double delta)
    {
        if (InUsecode || WaitingForChoice)
        {
            return;
        }

        for (var i = 0; i < _scripts.Count; i++)
        {
            var s = _scripts[i];
            if (s.Done)
            {
                continue;
            }

            s.Wait -= delta;
            if (s.Wait > 0)
            {
                continue;
            }

            s.Wait = s.Exec(finish: false);
            if (InUsecode || WaitingForChoice)
            {
                break;
            }
        }

        _scripts.RemoveAll(s => s.Done);
    }

    /// <summary>Exult <c>set_item_frame</c>: keep the reflection bit, reset the walk cycle for actors.</summary>
    public void SetItemFrame(U7Object item, int frame)
    {
        item.Frame = (item.Frame & 32) | (frame & 31);
        if (item.IsActor)
        {
            item.WalkFrameIndex = 0;
        }
    }

    /// <summary>
    /// Exult <c>Actor::resurrect(body)</c>: give the NPC its items back, remove
    /// the corpse, put the NPC where the corpse lay with full health, and
    /// resume following or loitering.
    /// </summary>
    public U7Object? Resurrect(U7Object body)
    {
        var num = body.LiveNpcNum;
        if (num <= 0 || num >= Npcs.Count || Npcs[num] is not { } npc || !npc.IsDead)
        {
            return null;
        }

        foreach (var item in body.Contents.ToList())
        {
            item.Container = npc;
            item.ReadySlot = -1;
            item.Tx = 255;
            item.Ty = 255;
            npc.Contents.Add(item);
        }

        body.Contents.Clear();
        var tx = body.Tx;
        var ty = body.Ty;
        var tz = body.Tz;
        Map.RemoveObject(body);
        npc.SetProp(U7.Actors.ActorProp.Health, npc.GetProp(U7.Actors.ActorProp.Strength));
        foreach (var flag in new[] { U7.Actors.ObjFlag.Dead, 8, U7.Actors.ObjFlag.Paralyzed, U7.Actors.ObjFlag.Asleep, 9, 3, 2 })
        {
            npc.ClearFlag(flag);
        }

        npc.Removed = false;
        Map.PlaceInWorld(npc, tx, ty, tz);
        U7.Actors.ActorWalker.Stand(npc, 4);
        if (npc.GetFlag(U7.Actors.ObjFlag.InParty))
        {
            npc.ClearFlag(U7.Actors.ObjFlag.InParty);
            Party?.AddToParty(npc);
        }

        Schedules?.Revive(npc);
        var sched = Party?.IsInParty(npc) == true ? U7.Actors.ScheduleType.FollowAvatar : U7.Actors.ScheduleType.Loiter;
        if (Schedules is { } s)
        {
            s.SetScheduleType(npc, sched);
        }
        else
        {
            npc.ScheduleType = sched;
        }

        Godot.GD.Print($"{npc.NpcName} is resurrected");
        return npc;
    }

    readonly UsecodeValue[] _stack = new UsecodeValue[StackSize];
    int _sp;
    readonly List<UsecodeFrame> _callStack = new();
    bool _foundAnswer;
    bool _exitRun;
    bool _aborted;
    bool _pendingCallisPush;
    readonly Random _rng = new();
    readonly BgIntrinsics _intrinsics;

    public UsecodeMachine(UsecodeFile file, GameMap map, U7Object avatar)
    {
        File = file;
        Map = map;
        Avatar = avatar;
        Catalog = map.Catalog;
        _intrinsics = new BgIntrinsics(this);
        LoadFlagInit();
        for (var i = 0; i < _stack.Length; i++)
        {
            _stack[i] = UsecodeValue.FromInt(0);
        }
    }

    void LoadFlagInit()
    {
        var path = Path.Combine(U7Paths.GameDatDir, "FLAGINIT");
        if (!System.IO.File.Exists(path))
        {
            return;
        }

        var data = System.IO.File.ReadAllBytes(path);
        var n = Math.Min(data.Length, GFlags.Length);
        Array.Copy(data, GFlags, n);
    }

    public static int GetShapeFun(int shape) => shape < 0x400 ? shape : 0x1000 + (shape - 0x400);

    public static bool IsObjectFun(int n) => n < 0x800;

    public int Call(int id, U7Object? item, UsecodeEvent ev)
    {
        Conv.ClearAnswers();
        Conv.InitFaces();
        NotifyFaces();
        HudMessage = "";
        Wait = UsecodeWait.None;
        UserChoice = null;
        _foundAnswer = false;
        _pendingCallisPush = false;
        if (!CallFunction(id, (int)ev, item, entrypoint: true))
        {
            HudMessage = $"no usecode 0x{id:X3}";
            return -1;
        }

        return Run();
    }

    public void Choose(string answer, int index = -1)
    {
        if (!WaitingForChoice)
        {
            return;
        }

        UserChoice = answer;
        var wait = Wait;
        Wait = UsecodeWait.None;
        if (wait is UsecodeWait.SelectMenu or UsecodeWait.SelectMenuIndex && _pendingCallisPush)
        {
            _pendingCallisPush = false;
            if (wait == UsecodeWait.SelectMenuIndex)
            {
                var n = index >= 0 ? index + 1 : Conv.LocateAnswer(answer) + 1;
                Push(UsecodeValue.FromInt(n));
            }
            else
            {
                Push(UsecodeValue.FromString(answer));
            }
        }

        _foundAnswer = false;
        Run();
        if (!InUsecode && !WaitingForChoice)
        {
            Conv.InitFaces();
            FacesChanged?.Invoke();
        }
    }

    public void ResumeWait(UsecodeValue result)
    {
        if (!WaitingForChoice)
        {
            return;
        }

        Wait = UsecodeWait.None;
        if (_pendingCallisPush)
        {
            _pendingCallisPush = false;
            Push(result);
        }

        Run();
        if (!InUsecode && !WaitingForChoice)
        {
            Conv.InitFaces();
            FacesChanged?.Invoke();
        }
    }

    public int Run()
    {
        _exitRun = false;
        _aborted = false;
        var steps = 0;
        while (_callStack.Count > 0 && !_exitRun && Wait == UsecodeWait.None)
        {
            var frame = _callStack[^1];
            var frameChanged = false;
            while (!frameChanged)
            {
                if (++steps > MaxInstructionsPerRun)
                {
                    Log($"usecode runaway in 0x{frame.Function.Id:X4} ip {frame.Ip}");
                    Abort();
                    return 0;
                }

                if (frame.Ip < 0 || frame.Ip >= frame.Code.Length)
                {
                    Log($"jumped outside function 0x{frame.Function.Id:X4}");
                    Abort();
                    return 0;
                }

                frame.InsIp = frame.Ip;
                LastFunctionId = frame.Function.Id;
                LastIp = frame.Ip;
                LastEvent = frame.EventId;
                var opcode = (UsecodeOp)frame.Code[frame.Ip++];
                frameChanged = Step(frame, opcode);
                if (Wait != UsecodeWait.None)
                {
                    return 1;
                }

                if (_exitRun || _callStack.Count == 0)
                {
                    break;
                }

                if (frameChanged && _callStack.Count > 0)
                {
                    break;
                }
            }
        }

        if (_aborted)
        {
            return 0;
        }

        return 1;
    }

    bool Step(UsecodeFrame frame, UsecodeOp opcode)
    {
        var code = frame.Code;
        var numLocals = frame.NumLocals;
        int offset;
        int sval;

        switch (opcode)
        {
            case UsecodeOp.Converse:
            case UsecodeOp.Converse32:
            {
                offset = opcode == UsecodeOp.Converse ? ReadI16(code, ref frame.Ip) : ReadI32(code, ref frame.Ip);
                _foundAnswer = false;
                if (Conv.Answers.Count == 0)
                {
                    frame.Ip += offset;
                    break;
                }

                Wait = UsecodeWait.Converse;
                AnswersChanged?.Invoke();
                return true;
            }
            case UsecodeOp.Jne:
            case UsecodeOp.Jne32:
            {
                offset = opcode == UsecodeOp.Jne ? ReadI16(code, ref frame.Ip) : ReadI32(code, ref frame.Ip);
                if (Pop().IsFalse)
                {
                    frame.Ip += offset;
                }

                break;
            }
            case UsecodeOp.Jmp:
            case UsecodeOp.Jmp32:
                offset = opcode == UsecodeOp.Jmp ? ReadI16(code, ref frame.Ip) : ReadI32(code, ref frame.Ip);
                frame.Ip += offset;
                break;
            case UsecodeOp.Cmps:
            case UsecodeOp.Cmps32:
            {
                var cnt = ReadU16(code, ref frame.Ip);
                offset = opcode == UsecodeOp.Cmps ? ReadI16(code, ref frame.Ip) : ReadI32(code, ref frame.Ip);
                var matched = false;
                while (!matched && !_foundAnswer && cnt-- > 0)
                {
                    var s = Pop();
                    var str = s.StrValue;
                    if (str is not null && UserChoice is not null && str == UserChoice)
                    {
                        matched = true;
                        _foundAnswer = true;
                    }
                }

                while (cnt-- > 0)
                {
                    Pop();
                }

                if (!matched)
                {
                    frame.Ip += offset;
                }

                break;
            }
            case UsecodeOp.Add:
            {
                var v2 = Pop();
                var v1 = Pop();
                Push(v1.Add(v2));
                break;
            }
            case UsecodeOp.Sub:
            {
                var v2 = Pop();
                var v1 = Pop();
                Push(v1.Sub(v2));
                break;
            }
            case UsecodeOp.Div:
            {
                var v2 = Pop();
                var v1 = Pop();
                Push(v1.Div(v2));
                break;
            }
            case UsecodeOp.Mul:
            {
                var v2 = Pop();
                var v1 = Pop();
                Push(v1.Mul(v2));
                break;
            }
            case UsecodeOp.Mod:
            {
                var v2 = Pop();
                var v1 = Pop();
                Push(v1.Mod(v2));
                break;
            }
            case UsecodeOp.And:
            {
                var v1 = Pop();
                var v2 = Pop();
                PushI(v1.IsTrue && v2.IsTrue ? 1 : 0);
                break;
            }
            case UsecodeOp.Or:
            {
                var v1 = Pop();
                var v2 = Pop();
                PushI(v1.IsTrue || v2.IsTrue ? 1 : 0);
                break;
            }
            case UsecodeOp.Not:
                PushI(Pop().IsTrue ? 0 : 1);
                break;
            case UsecodeOp.Pop:
            {
                offset = ReadU16(code, ref frame.Ip);
                var val = Pop();
                if ((uint)offset < (uint)numLocals)
                {
                    frame.Locals[offset] = val;
                }

                break;
            }
            case UsecodeOp.PushTrue:
                PushI(1);
                break;
            case UsecodeOp.PushFalse:
                PushI(0);
                break;
            case UsecodeOp.CmpGt:
                sval = PopI();
                PushI(PopI() > sval ? 1 : 0);
                break;
            case UsecodeOp.CmpLt:
                sval = PopI();
                PushI(PopI() < sval ? 1 : 0);
                break;
            case UsecodeOp.CmpGe:
                sval = PopI();
                PushI(PopI() >= sval ? 1 : 0);
                break;
            case UsecodeOp.CmpLe:
                sval = PopI();
                PushI(PopI() <= sval ? 1 : 0);
                break;
            case UsecodeOp.CmpNe:
            {
                var val1 = Pop();
                var val2 = Pop();
                PushI(val1.EqualsValue(val2) ? 0 : 1);
                break;
            }
            case UsecodeOp.AddSi:
            case UsecodeOp.AddSi32:
            {
                offset = opcode == UsecodeOp.AddSi ? ReadU16(code, ref frame.Ip) : ReadI32(code, ref frame.Ip);
                AppendString(frame.Function.GetString(offset));
                break;
            }
            case UsecodeOp.PushS:
            case UsecodeOp.PushS32:
            {
                offset = opcode == UsecodeOp.PushS ? ReadU16(code, ref frame.Ip) : ReadI32(code, ref frame.Ip);
                Push(UsecodeValue.FromString(frame.Function.GetString(offset)));
                break;
            }
            case UsecodeOp.ArrC:
            {
                var num = ReadU16(code, ref frame.Ip);
                var cnt = num;
                var arr = UsecodeValue.FromArray(num);
                var to = 0;
                while (cnt-- > 0)
                {
                    to += arr.AddValues(to, Pop());
                }

                if (to < num)
                {
                    arr.Resize(to);
                }

                Push(arr);
                break;
            }
            case UsecodeOp.PushI:
            case UsecodeOp.PushI32:
            {
                var ival = opcode == UsecodeOp.PushI ? ReadI16(code, ref frame.Ip) : ReadI32(code, ref frame.Ip);
                PushI(ival);
                break;
            }
            case UsecodeOp.Push:
            {
                offset = ReadU16(code, ref frame.Ip);
                if ((uint)offset < (uint)numLocals)
                {
                    Push(frame.Locals[offset].Clone());
                }
                else
                {
                    PushI(0);
                }

                break;
            }
            case UsecodeOp.CmpEq:
            {
                var val1 = Pop();
                var val2 = Pop();
                PushI(val1.EqualsValue(val2) ? 1 : 0);
                break;
            }
            case UsecodeOp.Call:
            {
                offset = ReadU16(code, ref frame.Ip);
                var funcid = frame.Function.GetExtern(offset);
                if (funcid < 0)
                {
                    Log($"bad extern {offset} in 0x{frame.Function.Id:X4}");
                    break;
                }

                CallFunction(funcid, frame.EventId, frame.Caller, entrypoint: false);
                return true;
            }
            case UsecodeOp.Call32:
            {
                offset = ReadI32(code, ref frame.Ip);
                CallFunction(offset, frame.EventId, frame.Caller, entrypoint: false);
                return true;
            }
            case UsecodeOp.Ret:
            case UsecodeOp.Ret2:
                ShowPendingText();
                ReturnFromProcedure();
                return true;
            case UsecodeOp.AIdx:
            {
                sval = PopI() - 1;
                offset = ReadU16(code, ref frame.Ip);
                if ((uint)offset >= (uint)numLocals)
                {
                    PushI(0);
                    break;
                }

                var val = frame.Locals[offset];
                if (sval < 0)
                {
                    PushI(0);
                }
                else if (val.IsArray && sval >= val.ArraySize)
                {
                    PushI(0);
                }
                else if (sval == 0)
                {
                    Push(val.GetElem0().Clone());
                }
                else
                {
                    Push(val.GetElem(sval).Clone());
                }

                break;
            }
            case UsecodeOp.RetV:
            {
                ShowPendingText();
                ReturnFromFunction(Pop());
                return true;
            }
            case UsecodeOp.Loop:
            case UsecodeOp.Loop32:
                frame.InitializingLoop = true;
                break;
            case UsecodeOp.LoopTop:
            case UsecodeOp.LoopTop32:
            {
                var local1 = ReadU16(code, ref frame.Ip);
                var local2 = ReadU16(code, ref frame.Ip);
                var local3 = ReadU16(code, ref frame.Ip);
                var local4 = ReadU16(code, ref frame.Ip);
                offset = opcode == UsecodeOp.LoopTop
                    ? ReadI16(code, ref frame.Ip)
                    : ReadI32(code, ref frame.Ip);
                DoLoopTop(frame, local1, local2, local3, local4, offset, numLocals);
                break;
            }
            case UsecodeOp.AddSv:
            {
                offset = ReadU16(code, ref frame.Ip);
                if ((uint)offset >= (uint)numLocals)
                {
                    break;
                }

                var loc = frame.Locals[offset];
                var str = loc.StrValue;
                if (str is not null)
                {
                    AppendString(str);
                }
                else if (loc.IntValue >= 0)
                {
                    AppendString(loc.IntValue.ToString());
                }

                break;
            }
            case UsecodeOp.In:
            {
                var arr = Pop();
                var val = Pop().GetElem0();
                PushI(arr.FindElem(val) >= 0 ? 1 : 0);
                break;
            }
            case UsecodeOp.Default:
            case UsecodeOp.Default32:
                frame.Ip += 2;
                offset = opcode == UsecodeOp.Default ? ReadI16(code, ref frame.Ip) : ReadI32(code, ref frame.Ip);
                if (!_foundAnswer)
                {
                    _foundAnswer = true;
                }
                else
                {
                    frame.Ip += offset;
                }

                break;
            case UsecodeOp.RetZ:
                ShowPendingText();
                ReturnFromFunction(UsecodeValue.FromInt(0));
                return true;
            case UsecodeOp.Say:
                SayString();
                break;
            case UsecodeOp.CallIS:
            {
                offset = ReadU16(code, ref frame.Ip);
                sval = code[frame.Ip++];
                var ival = CallIntrinsic(offset, sval);
                if (Wait != UsecodeWait.None)
                {
                    _pendingCallisPush = true;
                    return true;
                }

                Push(ival);
                return true;
            }
            case UsecodeOp.CallI:
            {
                offset = ReadU16(code, ref frame.Ip);
                sval = code[frame.Ip++];
                CallIntrinsic(offset, sval);
                return true;
            }
            case UsecodeOp.PushItemRef:
                Push(UsecodeValue.FromObject(frame.Caller));
                break;
            case UsecodeOp.Abrt:
            case UsecodeOp.Throw:
                ShowPendingText();
                if (opcode == UsecodeOp.Throw)
                {
                    Pop();
                }

                Abort();
                return true;
            case UsecodeOp.ConverseLoc:
                _foundAnswer = true;
                break;
            case UsecodeOp.PushF:
            case UsecodeOp.PushFVar:
            {
                offset = opcode == UsecodeOp.PushFVar ? PopI() : ReadU16(code, ref frame.Ip);
                PushI((uint)offset < (uint)GFlags.Length ? GFlags[offset] : 0);
                break;
            }
            case UsecodeOp.PopF:
            case UsecodeOp.PopFVar:
            {
                offset = opcode == UsecodeOp.PopFVar ? PopI() : ReadU16(code, ref frame.Ip);
                var v = (byte)PopI();
                if ((uint)offset < (uint)GFlags.Length)
                {
                    GFlags[offset] = v;
                    FlagLog.Add($"flag 0x{offset:X3} = {v}");
                    if (FlagLog.Count > 16)
                    {
                        FlagLog.RemoveAt(0);
                    }
                }

                break;
            }
            case UsecodeOp.PushB:
                PushI(code[frame.Ip++]);
                break;
            case UsecodeOp.PopArr:
            {
                offset = ReadU16(code, ref frame.Ip);
                if ((uint)offset >= (uint)numLocals)
                {
                    PopI();
                    Pop();
                    break;
                }

                var arr = frame.Locals[offset];
                var index = PopI() - 1;
                var val = Pop();
                var size = arr.ArraySize;
                if (index >= 0 && (index < size || arr.Resize(index + 1)))
                {
                    arr.PutElem(index, val);
                }

                break;
            }
            case UsecodeOp.CallE:
            case UsecodeOp.CallE32:
            {
                var ival = Pop();
                var caller = GetItem(ival);
                offset = opcode == UsecodeOp.CallE ? ReadU16(code, ref frame.Ip) : ReadI32(code, ref frame.Ip);
                CallFunction(offset, frame.EventId, caller, entrypoint: false);
                return true;
            }
            case UsecodeOp.PushEventId:
                PushI(frame.EventId);
                break;
            case UsecodeOp.ArrA:
            {
                var val = Pop();
                var arr = Pop();
                Push(arr.Concat(val));
                break;
            }
            case UsecodeOp.PopEventId:
                frame.EventId = PopI();
                break;
            case UsecodeOp.PushChoice:
                Push(UsecodeValue.FromString(UserChoice ?? ""));
                break;
            case UsecodeOp.DbgLine:
                ReadU16(code, ref frame.Ip);
                break;
            default:
                Log($"unknown opcode 0x{(int)opcode:X2} in 0x{frame.Function.Id:X4} @ {frame.InsIp}");
                Abort();
                return true;
        }

        return false;
    }

    void DoLoopTop(UsecodeFrame frame, int local1, int local2, int local3, int local4, int offset, int numLocals)
    {
        if ((uint)local1 >= (uint)numLocals || (uint)local2 >= (uint)numLocals ||
            (uint)local3 >= (uint)numLocals || (uint)local4 >= (uint)numLocals)
        {
            return;
        }

        var arr = frame.Locals[local4];
        var initializing = frame.InitializingLoop;
        frame.InitializingLoop = false;
        if (initializing && arr.IsUndefined)
        {
            frame.Ip += offset;
            return;
        }

        var next = frame.Locals[local1].IntValue;
        if (initializing)
        {
            var cnt = arr.IsArray ? arr.ArraySize : 1;
            frame.Locals[local2] = UsecodeValue.FromInt(cnt);
            frame.Locals[local1] = UsecodeValue.FromInt(0);
            next = 0;
        }

        var count = arr.IsArray ? arr.ArraySize : 1;
        if (count != frame.Locals[local2].IntValue)
        {
            frame.Locals[local2] = UsecodeValue.FromInt(count);
        }

        if (next >= frame.Locals[local2].IntValue)
        {
            frame.Ip += offset;
        }
        else
        {
            frame.Locals[local3] = arr.IsArray ? arr.GetElem((int)next).Clone() : arr.Clone();
            frame.Locals[local1] = UsecodeValue.FromInt(next + 1);
        }
    }

    public bool CallFunction(int funcid, int eventid, U7Object? caller, bool entrypoint, int givenargs = 0)
    {
        var fun = File.Get(funcid);
        if (fun is null)
        {
            Log($"missing function 0x{funcid:X4}");
            return false;
        }

        int depth;
        int oldstack;
        if (entrypoint)
        {
            depth = 0;
            oldstack = 0;
        }
        else
        {
            var parent = _callStack[^1];
            depth = parent.CallDepth + 1;
            oldstack = _sp - parent.SaveSp;
            caller ??= parent.Caller;
        }

        var frame = new UsecodeFrame(fun, eventid, caller, depth);
        var numArgs = Math.Max(frame.NumArgs, givenargs);
        if (givenargs == 0 && IsObjectFun(funcid))
        {
            numArgs--;
            if (numArgs < 0)
            {
                numArgs = 0;
            }
        }

        while (numArgs > oldstack)
        {
            PushI(0);
            oldstack++;
        }

        for (var i = 0; i < numArgs; i++)
        {
            frame.Locals[numArgs - i - 1] = Pop();
        }

        frame.SaveSp = _sp;
        _callStack.Add(frame);
        LastFunctionId = funcid;
        LastEvent = eventid;
        return true;
    }

    void PreviousStackFrame()
    {
        var frame = _callStack[^1];
        _callStack.RemoveAt(_callStack.Count - 1);
        _sp = frame.SaveSp;
        if (frame.CallDepth == 0)
        {
            _exitRun = true;
        }
    }

    void ReturnFromFunction(UsecodeValue retval)
    {
        PreviousStackFrame();
        if (!_exitRun)
        {
            Push(retval);
        }
    }

    void ReturnFromProcedure() => PreviousStackFrame();

    void Abort()
    {
        _aborted = true;
        _callStack.Clear();
        _sp = 0;
        _exitRun = true;
        Wait = UsecodeWait.None;
    }

    public void Push(UsecodeValue val)
    {
        if (_sp >= _stack.Length)
        {
            Log("stack overflow");
            return;
        }

        _stack[_sp++] = val;
    }

    public UsecodeValue Pop()
    {
        if (_sp <= 0)
        {
            Log("stack underflow");
            return UsecodeValue.FromInt(0);
        }

        return _stack[--_sp];
    }

    public void PushI(long v) => Push(UsecodeValue.FromInt(v));

    public int PopI() => (int)Pop().NeedIntValue();

    public void AppendString(string? str)
    {
        if (string.IsNullOrEmpty(str))
        {
            return;
        }

        StringReg += str;
    }

    public void ShowPendingText()
    {
        if (StringReg.Length == 0)
        {
            return;
        }

        SayString();
    }

    public void SayString()
    {
        if (StringReg.Length == 0)
        {
            return;
        }

        var text = StringReg.Replace("~~", "\n").Replace('~', '\n').Replace("*", "");
        StringReg = "";
        ShowText(text);
    }

    public void ShowText(string text)
    {
        Conv.NpcText = text;
        HudMessage = text;
        Say?.Invoke(text);
    }

    public void Bark(U7Object? obj, string? text)
    {
        if (obj is null || string.IsNullOrEmpty(text))
        {
            return;
        }

        obj.BarkText = text;
        obj.BarkUntilMsec = Time.GetTicksMsec() + 4000;
        ItemSay?.Invoke(obj, text);
    }

    UsecodeValue CallIntrinsic(int id, int argc)
    {
        var parms = new UsecodeValue[12];
        for (var i = 0; i < parms.Length; i++)
        {
            parms[i] = UsecodeValue.FromInt(0);
        }

        for (var i = 0; i < argc; i++)
        {
            parms[i] = Pop();
        }

        var name = BgIntrinsics.Name(id);
        LastIntrinsic = $"UI_{name}(0x{id:X2}@{argc:D2})";
        IntrinsicLog.Add(LastIntrinsic);
        if (IntrinsicLog.Count > 24)
        {
            IntrinsicLog.RemoveAt(0);
        }

        return _intrinsics.Call(id, parms, argc);
    }

    public void Reset()
    {
        _callStack.Clear();
        _sp = 0;
        _exitRun = true;
        _aborted = false;
        Wait = UsecodeWait.None;
        _pendingCallisPush = false;
        UserChoice = null;
        StringReg = "";
        Conv.ClearAnswers();
        Conv.InitFaces();
        NotifyFaces();
    }

    public U7Object? GetItem(UsecodeValue itemref)
    {
        var elem = itemref.GetElem0();
        if (elem.IsPtr)
        {
            return elem.PtrValue;
        }

        var val = elem.IntValue;
        if (val == 0)
        {
            return null;
        }

        if (val == -356)
        {
            return Avatar;
        }

        if (val < -356 && val > -360)
        {
            return null;
        }

        if (val < 0 && val > -Npcs.Count)
        {
            return Npcs[(int)-val];
        }

        if (val >= 0 && val < 0x400 && _callStack.Count > 0)
        {
            var caller = _callStack[^1].Caller;
            if (caller is not null && !itemref.IsArray && val == caller.Shape)
            {
                return caller;
            }
        }

        return null;
    }

    public UsecodeFrame? CurrentFrame => _callStack.Count > 0 ? _callStack[^1] : null;

    public int Random(int range)
    {
        if (range <= 0)
        {
            return 0;
        }

        return 1 + _rng.Next(range);
    }

    public void RequestWait(UsecodeWait kind)
    {
        Wait = kind;
        AnswersChanged?.Invoke();
    }

    public void NotifyFaces() => FacesChanged?.Invoke();

    public void NotifyAnswers() => AnswersChanged?.Invoke();

    public string DebugText()
    {
        var fun = LastFunctionId;
        var sb = new StringBuilder();
        sb.AppendLine($"usecode  fun 0x{fun:X4}  event {LastEvent}  ip 0x{LastIp:X4}  stack {_callStack.Count}");
        sb.AppendLine($"last {LastIntrinsic}");
        if (WaitingForChoice)
        {
            sb.AppendLine($"waiting: {Wait}  answers: {string.Join(", ", Conv.Answers)}");
        }

        if (FlagLog.Count > 0)
        {
            sb.AppendLine("flags: " + string.Join("; ", FlagLog));
        }

        return sb.ToString().TrimEnd();
    }

    public void Log(string msg)
    {
        GD.Print("[usecode] " + msg);
        IntrinsicLog.Add(msg);
        if (IntrinsicLog.Count > 24)
        {
            IntrinsicLog.RemoveAt(0);
        }
    }

    static int ReadU16(byte[] d, ref int ip)
    {
        var v = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(ip, 2));
        ip += 2;
        return v;
    }

    static int ReadI16(byte[] d, ref int ip)
    {
        var v = BinaryPrimitives.ReadInt16LittleEndian(d.AsSpan(ip, 2));
        ip += 2;
        return v;
    }

    static int ReadI32(byte[] d, ref int ip)
    {
        var v = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(ip, 4));
        ip += 4;
        return v;
    }
}
