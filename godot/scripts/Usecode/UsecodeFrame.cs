using U7.Data;

namespace U7.Usecode;

public enum UsecodeEvent
{
    NpcProximity = 0,
    DoubleClick = 1,
    InternalExec = 2,
    EggProximity = 3,
    Weapon = 4,
    Readied = 5,
    Unreadied = 6
}

/// <summary>
/// One activation of a usecode function. IP is an index into <see cref="UsecodeFunction.Bytecode"/>.
/// </summary>
public sealed class UsecodeFrame
{
    public UsecodeFunction Function { get; }
    public int Ip;
    public int InsIp;
    public UsecodeValue[] Locals { get; }
    public int EventId;
    public U7Object? Caller;
    public int SaveSp;
    public int CallDepth;
    public bool InitializingLoop;

    public int NumArgs => Function.NumArgs;
    public int NumVars => Function.NumVars;
    public int NumLocals => NumArgs + NumVars;
    public byte[] Code => Function.Bytecode;

    public UsecodeFrame(UsecodeFunction fun, int eventId, U7Object? caller, int depth)
    {
        Function = fun;
        EventId = eventId;
        Caller = caller;
        CallDepth = depth;
        Locals = new UsecodeValue[fun.NumArgs + fun.NumVars];
        for (var i = 0; i < Locals.Length; i++)
        {
            Locals[i] = new UsecodeValue();
        }
    }
}
