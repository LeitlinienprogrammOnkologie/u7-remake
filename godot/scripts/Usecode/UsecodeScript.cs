using Godot;
using U7.Actors;
using U7.Core;
using U7.Data;

namespace U7.Usecode;

/// <summary>
/// Exult <c>Usecode_script</c> (ucsched.cc): an array of script opcodes run
/// against one object over time, created by the execute_usecode_array
/// intrinsics. Delays are in standard 200 ms ticks.
/// </summary>
public sealed class UsecodeScript
{
    const int Cont = 0x01, Nop1 = 0x02, Reset = 0x0a, Repeat = 0x0b, Repeat2 = 0x0c, Nop2 = 0x21;
    const int DontHalt = 0x23, WaitWhileNear = 0x24, DelayTicks = 0x27, DelayMinutes = 0x28, DelayHours = 0x29;
    const int WaitWhileFar = 0x2b, Finish = 0x2c, Remove = 0x2d, Descend = 0x38, Rise = 0x39;
    const int Frame = 0x46, Egg = 0x48, SetEgg = 0x49, NextFrameMax = 0x4d, NextFrame = 0x4e;
    const int PrevFrameMin = 0x4f, PrevFrame = 0x50, Say = 0x52, Step = 0x53, Music = 0x54;
    const int Usecode = 0x55, Speech = 0x56, Sfx = 0x58, FaceDir = 0x59, Weather = 0x5a;
    const int Hit = 0x78, Attack = 0x7a, Usecode2 = 0x80, Resurrect = 0x81;
    const int TicksPerMinute = 25; // Exult gameclk.h

    readonly UsecodeMachine _vm;
    readonly UsecodeValue _code;
    readonly int _cnt;
    int _i;
    bool _noHalt;

    public U7Object Obj { get; }
    /// <summary>Seconds until the next execution.</summary>
    public double Wait { get; set; }
    public UsecodeValue Code => _code;
    public int Count => _cnt;
    public int Index
    {
        get => _i;
        set => _i = value;
    }
    public bool Done => _i >= _cnt;
    public bool Activated => _i > 0;
    public bool NoHalt => _noHalt;

    public UsecodeScript(UsecodeMachine vm, U7Object obj, UsecodeValue code, double delaySeconds)
    {
        _vm = vm;
        Obj = obj;
        _code = code.IsArray ? code : UsecodeValue.FromArray(1, code);
        _cnt = _code.ArraySize;
        Wait = delaySeconds;
        for (var i = 0; i < _cnt; i++)
        {
            var op = (int)_code.GetElem(i).IntValue;
            if (op == DontHalt)
            {
                _noHalt = true;
            }
            else if (op == Finish)
            {
                _mustFinish = true;
            }
            else
            {
                break;
            }
        }
    }

    /// <summary>Restore from a saved game (Exult <c>Usecode_script::restore</c>).</summary>
    public UsecodeScript(UsecodeMachine vm, U7Object obj, UsecodeValue code, int index, bool noHalt, double delaySeconds)
        : this(vm, obj, code, delaySeconds)
    {
        _i = index;
        _noHalt = noHalt;
    }

    public void Halt()
    {
        if (!_noHalt)
        {
            _i = _cnt;
        }
    }

    /// <summary>Exult <c>must_finish</c>: a purged script runs to its end first.</summary>
    public bool MustFinish => _mustFinish;

    bool _mustFinish;

    /// <summary>Exult <c>Usecode_script::purge</c>'s halt: no_halt cleared, then stopped.</summary>
    public void ForceHalt()
    {
        _noHalt = false;
        Halt();
    }

    int Int(int index) => index < _cnt ? (int)_code.GetElem(index).IntValue : 0;

    bool AvatarNear(int maxDist)
    {
        var av = _vm.Avatar;
        var d = new TileCoord(Obj.Tx, Obj.Ty, 0).Distance2d(new TileCoord(av.Tx, av.Ty, 0));
        return d <= maxDist && (!Obj.IsEgg || Obj.Tz == av.Tz);
    }

    /// <summary>Exult <c>Usecode_script::exec</c>. Returns the delay in seconds before the next call.</summary>
    public double Exec(bool finish)
    {
        var delay = UsecodeMachine.StdDelaySeconds;
        var doAnother = true;
        int opcode;
        for (; _i < _cnt && ((opcode = Int(_i)) == Cont || doAnother); _i++)
        {
            if (Obj.Removed && opcode != Remove && opcode != Resurrect && opcode != Usecode && opcode != Usecode2)
            {
                // Exult: a script whose object is gone just ends.
                if (!Obj.IsActor)
                {
                    _i = _cnt;
                    return delay;
                }
            }

            doAnother = finish;
            switch (opcode)
            {
                case Cont:
                    doAnother = true;
                    break;
                case Reset:
                    if (!finish)
                    {
                        _i = -1;
                    }

                    break;
                case Repeat:
                case Repeat2:
                {
                    doAnother = true;
                    var numRepeats = Int(_i + 2);
                    if (numRepeats <= 0)
                    {
                        if (opcode == Repeat2)
                        {
                            _code.PutElem(_i + 2, _code.GetElem(_i + 3)); // restore counter
                            _i += 3;
                        }
                        else
                        {
                            _i += 2;
                        }
                    }
                    else
                    {
                        if (numRepeats != 255)
                        {
                            _code.PutElem(_i + 2, UsecodeValue.FromInt(numRepeats - 1));
                        }

                        _i += Int(_i + 1) - 1;
                        if (_i < -1)
                        {
                            _i = -1;
                        }
                    }

                    break;
                }
                case WaitWhileNear:
                {
                    var dist = Int(++_i);
                    if (!finish && AvatarNear(dist))
                    {
                        _i -= 2;
                    }

                    break;
                }
                case WaitWhileFar:
                {
                    var dist = Int(++_i);
                    if (!finish && !AvatarNear(dist))
                    {
                        _i -= 2;
                    }

                    break;
                }
                case Nop1:
                case Nop2:
                    break;
                case Finish:
                    doAnother = true;
                    break;
                case DontHalt:
                    _noHalt = true;
                    doAnother = true;
                    break;
                case DelayTicks:
                    delay *= Int(++_i);
                    break;
                case DelayMinutes:
                    delay *= TicksPerMinute * Int(++_i);
                    break;
                case DelayHours:
                    delay *= 60 * TicksPerMinute * Int(++_i);
                    break;
                case Remove:
                    _vm.Map.RemoveObject(Obj);
                    break;
                case Rise:
                    if (Obj.Tz < 10)
                    {
                        _vm.Map.MoveObject(Obj, Obj.Tx, Obj.Ty, Obj.Tz + 1);
                    }

                    break;
                case Descend:
                    if (Obj.Tz > 0)
                    {
                        _vm.Map.MoveObject(Obj, Obj.Tx, Obj.Ty, Obj.Tz - 1);
                    }

                    break;
                case Frame:
                    _vm.SetItemFrame(Obj, Int(++_i));
                    break;
                case Egg:
                    if (Obj.IsEgg && Obj.EggType is World.EggType.Monster or World.EggType.Button or World.EggType.Missile)
                    {
                        _vm.Eggs?.HatchEgg(Obj, must: true);
                    }

                    break;
                case SetEgg:
                {
                    var crit = Int(++_i);
                    var dist = Int(++_i);
                    if (Obj.IsEgg)
                    {
                        Obj.EggCriteria = crit;
                        Obj.EggDistance = dist;
                        _vm.Map.MoveObject(Obj, Obj.Tx, Obj.Ty, Obj.Tz); // recompute area and index
                    }

                    break;
                }
                case NextFrameMax:
                {
                    var n = _vm.Catalog[Obj.Shape].FrameCount;
                    if (Obj.Frame % 32 < n - 1)
                    {
                        _vm.SetItemFrame(Obj, Obj.Frame + 1);
                    }

                    break;
                }
                case NextFrame:
                {
                    var n = _vm.Catalog[Obj.Shape].FrameCount;
                    if (n > 0)
                    {
                        _vm.SetItemFrame(Obj, (Obj.Frame + 1) % n);
                    }

                    break;
                }
                case PrevFrameMin:
                    if (Obj.Frame > 0)
                    {
                        _vm.SetItemFrame(Obj, Obj.Frame - 1);
                    }

                    break;
                case PrevFrame:
                {
                    var n = _vm.Catalog[Obj.Shape].FrameCount;
                    if (n > 0)
                    {
                        _vm.SetItemFrame(Obj, (Obj.Frame - 1 + n) % n);
                    }

                    break;
                }
                case Say:
                {
                    var str = _code.GetElem(++_i);
                    _vm.Bark(Obj, str.IsString ? str.StrValue : str.IntValue.ToString());
                    break;
                }
                case Step:
                {
                    var val = Int(++_i);
                    ++_i;
                    var dz = _i < _cnt ? Int(_i) : 0;
                    var destz = Obj.Tz + dz;
                    if (destz < 0 || dz > 15 || dz < -15)
                    {
                        doAnother = true;
                        break;
                    }

                    DoStep(val >= 0 ? val & 7 : -1, dz);
                    break;
                }
                case Music:
                {
                    var song = Int(++_i);
                    var continuous = false;
                    if (song < 0 || song > 0xff)
                    {
                        continuous = (song >> 8) != 0;
                        song &= 0xff;
                    }
                    else
                    {
                        ++_i;
                        var inRange = _i < _cnt;
                        var value = inRange ? Int(_i) : 0;
                        if (inRange && value is 0 or 1)
                        {
                            continuous = value != 0;
                        }
                        else
                        {
                            --_i;
                        }
                    }

                    _vm.Music?.Start(song, continuous);
                    break;
                }
                case Usecode:
                {
                    var fun = Int(++_i);
                    if (fun is >= 0 and < 0x100)
                    {
                        ++_i;
                        var inRange = _i < _cnt;
                        var value = inRange ? Int(_i) : 0;
                        if (!inRange || value != 0)
                        {
                            --_i;
                        }
                    }

                    var ev = UsecodeEvent.InternalExec;
                    if (Obj.IsEgg && Obj.EggType < 12)
                    {
                        ev = UsecodeEvent.EggProximity;
                    }
                    else if (fun == _vm.TelekenesisFun)
                    {
                        // The telekinesis spell's call.
                        ev = UsecodeEvent.DoubleClick;
                        _vm.TelekenesisFun = -1;
                    }

                    _vm.Call(fun, Obj, ev);
                    break;
                }
                case Usecode2:
                {
                    var fun = Int(++_i);
                    var evid = Int(++_i);
                    _vm.Call(fun, Obj, (UsecodeEvent)evid);
                    break;
                }
                case Speech:
                    ++_i; // no speech playback yet
                    break;
                case Sfx:
                    ++_i; // no sound effects yet
                    break;
                case FaceDir:
                {
                    // Exult: the frame turned that way, an empty one skipped; the walk starts over.
                    var dir = Int(++_i) & 7;
                    _vm.SetItemFrame(Obj, (Obj.Frame & 0xf) | Directions.FrameRotation[dir], checkEmpty: true, setRotated: true);
                    Obj.WalkFrameIndex = 0;
                    break;
                }
                case Weather:
                {
                    // Seems to match the originals (Exult).
                    var type = Int(++_i) & 0xff;
                    if (type == 0xff || (_vm.Effects?.GetWeather() ?? 0) != 0)
                    {
                        _vm.Effects?.SetWeather(type == 0xff ? 0 : type);
                    }

                    break;
                }
                case Hit:
                {
                    var hps = Int(++_i);
                    var type = Int(++_i);
                    _vm.Combat?.ReduceHealth(Obj, hps, null, type);
                    break;
                }
                case Attack:
                    // Finish set_to_attack.
                    if (Obj.IsActor)
                    {
                        _vm.Combat?.UsecodeAttack(Obj);
                    }

                    break;
                case Resurrect:
                    _vm.Resurrect(Obj);
                    break;
                default:
                    if (opcode is >= 0x61 and <= 0x70)
                    {
                        // Frames with the actor's facing ("U7-verified", Exult), an empty one skipped.
                        _vm.SetItemFrame(Obj, (Obj.Frame & 48) | (opcode - 0x61), checkEmpty: true, setRotated: true);
                    }
                    else if (opcode is >= 0x30 and < 0x38)
                    {
                        DoStep(opcode & 7, 0);
                        doAnother = true;
                    }
                    else
                    {
                        GD.Print($"script: unknown opcode 0x{opcode:X2} for shape {Obj.Shape}");
                    }

                    break;
            }
        }

        return delay;
    }

    /// <summary>
    /// Exult <c>Usecode_script::step</c>: an actor makes a forced one-tile
    /// move with its walk frame, a barge four forced steps (the Skara Brae
    /// ferry); anything else stays put.
    /// </summary>
    void DoStep(int dir, int dz)
    {
        if (Obj.IsBarge)
        {
            if (_vm.Barges is { } barges)
            {
                var barge = barges.Of(Obj);
                for (var i = 0; i < 4; i++)
                {
                    var t = dir >= 0
                        ? new TileCoord(U7Constants.WrapTile(Obj.Tx + Directions.Dx[dir]), U7Constants.WrapTile(Obj.Ty + Directions.Dy[dir]), Obj.Tz)
                        : new TileCoord(Obj.Tx, Obj.Ty, Obj.Tz);
                    barge.Step(t with { Tz = Math.Max(0, t.Tz + dz / 4 + (i == 0 ? dz % 4 : 0)) }, force: true);
                }
            }

            return;
        }

        if (!Obj.IsActor)
        {
            return;
        }

        var tx = Obj.Tx;
        var ty = Obj.Ty;
        if (dir >= 0)
        {
            tx = U7Constants.WrapTile(tx + Directions.Dx[dir]);
            ty = U7Constants.WrapTile(ty + Directions.Dy[dir]);
        }

        var tz = Math.Max(0, Obj.Tz + dz);
        ActorWalker.MoveTo(_vm.Map, Obj, tx, ty, tz, dir >= 0 ? dir : 4);
        if (Obj.NpcNum == 0)
        {
            _vm.AvatarMovedByScript?.Invoke(Obj);
        }
    }
}
