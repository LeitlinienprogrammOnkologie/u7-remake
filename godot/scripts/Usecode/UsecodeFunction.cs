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

    public string GetString(int offset) => UsecodeValue.ReadCString(Data, offset);

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
