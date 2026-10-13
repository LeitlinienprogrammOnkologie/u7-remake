using System.Buffers.Binary;
using System.IO;
using U7.Core;

namespace U7.Usecode;

/// <summary>
/// Loads <c>STATIC/USECODE</c> as a concatenation of 16-bit (or Exult-extended)
/// function records, then a mod's patch usecode over it (Exult
/// <c>Usecode_internal</c>'s constructor and <c>read_usecode</c>). See Exult
/// <c>Usecode_function</c>.
/// </summary>
public sealed class UsecodeFile
{
    public IReadOnlyDictionary<int, UsecodeFunction> Functions => _functions;
    public int Count => _functions.Count;
    /// <summary>Exult <c>symtbl</c>: the last symbol table read (UCC's, from the patch); null for the originals.</summary>
    public UsecodeSymbolTable? Symbols { get; private set; }

    readonly Dictionary<int, UsecodeFunction> _functions = new();

    public static UsecodeFile Load()
    {
        var path = Path.Combine(U7Paths.StaticDir, "USECODE");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Missing STATIC/USECODE", path);
        }

        var file = Load(File.ReadAllBytes(path));
        var layer = 0;
        foreach (var patch in U7Paths.PatchLayers("usecode"))
        {
            file.Read(File.ReadAllBytes(patch), ++layer);
        }

        return file;
    }

    public static UsecodeFile Load(byte[] data)
    {
        var file = new UsecodeFile();
        file.Read(data, 0);
        return file;
    }

    /// <summary>
    /// Exult <c>read_usecode</c>: a function read from a patch keeps the one
    /// it replaces as its <c>Orig</c>; one from the originals replaces
    /// outright. Exult has one patch level, where a function given twice keeps
    /// the first original; here the remake's additions and a mod's are two
    /// (<paramref name="layer"/> 1 and 2), and a mod's function keeps the
    /// additions' as its original, so their <c>original()</c> calls chain.
    /// A later symbol table is merged into the earlier ones.
    /// </summary>
    void Read(byte[] data, int layer)
    {
        var ip = 0;
        if (UsecodeSymbolTable.HasSymbolTable(data, ref ip))
        {
            var table = UsecodeSymbolTable.Read(data, ref ip);
            Symbols = Symbols is null ? table : Symbols.Merge(table);
        }

        while (ip + 4 <= data.Length)
        {
            int id = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(ip, 2));
            ip += 2;
            int len;
            var extended = false;
            if (id == 0xFFFE)
            {
                if (ip + 8 > data.Length)
                {
                    break;
                }

                id = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(ip, 4));
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
            var fun = new UsecodeFunction(id, extended, code) { Layer = layer };
            if (layer > 0 && _functions.TryGetValue(id, out var old))
            {
                fun.Orig = old.Layer == layer ? old.Orig ?? old : old;
            }

            _functions[id] = fun;
        }
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
