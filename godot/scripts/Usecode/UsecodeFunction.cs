using System.Buffers.Binary;

namespace U7.Usecode;

/// <summary>
/// One parsed usecode function. Header layout matches Exult <c>Stack_frame</c>.
/// </summary>
public sealed class UsecodeFunction
{
    public int Id { get; }
    public bool Extended { get; }
    public byte[] Data { get; }
    public int NumArgs { get; }
    public int NumVars { get; }
    public ushort[] Externs { get; }
    public byte[] Bytecode { get; }
    /// <summary>Exult <c>Usecode_function::orig</c>: the function a patch replaced, which UCC's <c>original</c> calls (CALLO).</summary>
    public UsecodeFunction? Orig { get; set; }
    /// <summary>Where it was read from: 0 the game's USECODE, 1 the remake's additions, 2 a mod's patch.</summary>
    public int Layer { get; init; }
    /// <summary>Exult <c>Usecode_function::statics</c>: UCC's function-local statics, saved in <c>usecode.var</c>.</summary>
    public List<UsecodeValue> Statics { get; } = new();

    public UsecodeFunction(int id, bool extended, byte[] code)
    {
        Id = id;
        Extended = extended;
        var ip = 0;
        var dataLen = extended ? ReadI32(code, ref ip) : ReadU16(code, ref ip);
        if (dataLen < 0 || ip + dataLen > code.Length)
        {
            throw new InvalidDataException($"Function 0x{id:X4}: data_size {dataLen} overruns code.");
        }

        Data = new byte[dataLen];
        Buffer.BlockCopy(code, ip, Data, 0, dataLen);
        ip += dataLen;

        NumArgs = ReadU16(code, ref ip);
        NumVars = ReadU16(code, ref ip);
        var numExterns = ReadU16(code, ref ip);
        Externs = new ushort[numExterns];
        for (var i = 0; i < numExterns; i++)
        {
            Externs[i] = (ushort)ReadU16(code, ref ip);
        }

        var remaining = code.Length - ip;
        Bytecode = remaining > 0 ? code[ip..] : Array.Empty<byte>();
    }

    /// <summary>The string at the offset in the data, decoded once (PUSHS and ADDSI ask again on every pass of a converse loop).</summary>
    public string GetString(int offset)
    {
        _strings ??= new Dictionary<int, string>();
        if (!_strings.TryGetValue(offset, out var s))
        {
            s = UsecodeValue.ReadCString(Data, offset);
            _strings[offset] = s;
        }

        return s;
    }

    Dictionary<int, string>? _strings;

    public int GetExtern(int index) =>
        (uint)index < (uint)Externs.Length ? Externs[index] : -1;

    static int ReadU16(byte[] d, ref int ip)
    {
        if (ip + 2 > d.Length)
        {
            throw new EndOfStreamException();
        }

        var v = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(ip, 2));
        ip += 2;
        return v;
    }

    static int ReadI32(byte[] d, ref int ip)
    {
        if (ip + 4 > d.Length)
        {
            throw new EndOfStreamException();
        }

        var v = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(ip, 4));
        ip += 4;
        return v;
    }
}
