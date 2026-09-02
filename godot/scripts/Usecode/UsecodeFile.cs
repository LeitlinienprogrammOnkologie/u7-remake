using System.Buffers.Binary;
using System.IO;
using U7.Core;

namespace U7.Usecode;

/// <summary>
/// Loads <c>STATIC/USECODE</c> as a concatenation of 16-bit (or Exult-extended)
/// function records. See Exult <c>Usecode_function</c>.
/// </summary>
public sealed class UsecodeFile
{
    public IReadOnlyDictionary<int, UsecodeFunction> Functions => _functions;
    public int Count => _functions.Count;

    readonly Dictionary<int, UsecodeFunction> _functions = new();

    public static UsecodeFile Load()
    {
        var path = Path.Combine(U7Paths.StaticDir, "USECODE");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Missing STATIC/USECODE", path);
        }

        return Load(File.ReadAllBytes(path));
    }

    public static UsecodeFile Load(byte[] data)
    {
        var file = new UsecodeFile();
        var ip = 0;
        while (ip + 4 <= data.Length)
        {
            var id = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(ip, 2));
            ip += 2;
            int len;
            var extended = false;
            if (id == 0xFFFE)
            {
                if (ip + 8 > data.Length)
                {
                    break;
                }

                id = (ushort)(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(ip, 4)) & 0xffff);
                ip += 4;
                len = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(ip, 4));
                ip += 4;
                extended = true;
            }
            else if (id == 0xFFFF)
            {
                if (ip + 6 > data.Length)
                {
                    break;
                }

                id = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(ip, 2));
                ip += 2;
                len = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(ip, 4));
                ip += 4;
                extended = true;
            }
            else
            {
                len = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(ip, 2));
                ip += 2;
            }

            if (len < 0 || ip + len > data.Length)
            {
                throw new InvalidDataException(
                    $"USECODE function 0x{id:X4} length {len} overruns file at {ip}.");
            }

            var code = new byte[len];
            Buffer.BlockCopy(data, ip, code, 0, len);
            ip += len;
            file._functions[id] = new UsecodeFunction(id, extended, code);
        }

        return file;
    }

    public UsecodeFunction? Get(int id) =>
        _functions.TryGetValue(id, out var f) ? f : null;

    public bool Exists(int id) => _functions.ContainsKey(id);

    /// <summary>
    /// Disassemble the first instructions of a function for debug comparison
    /// against <c>assets/usecode/usecode_disasm.txt</c>.
    /// </summary>
    public string DisassemblePrefix(int id, int maxBytes = 64)
    {
        var fun = Get(id);
        if (fun is null)
        {
            return $"function 0x{id:X4} not found";
        }

        var bc = fun.Bytecode;
        var n = Math.Min(maxBytes, bc.Length);
        var hex = BitConverter.ToString(bc, 0, n).Replace("-", " ");
        return $"Function 0x{id:X4} args={fun.NumArgs} locals={fun.NumVars} " +
               $"externs={fun.Externs.Length} code={bc.Length}\n{hex}";
    }
}
