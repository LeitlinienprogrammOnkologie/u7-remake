using System.Buffers.Binary;
using System.Text;

namespace U7.Usecode;

/// <summary>Exult <c>Usecode_symbol::Symbol_kind</c>.</summary>
public enum UsecodeSymbolKind
{
    FunDefined = 1,
    FunExterned,
    FunExternDefined,
    ClassScope,
    TableScope,
    ShapeFun,
    ObjectFun
}

/// <summary>Exult <c>Usecode_symbol</c>: a named function, class or table in UCC's output.</summary>
public sealed record UsecodeSymbol(string Name, UsecodeSymbolKind Kind, int Value, int Extra);

/// <summary>
/// Exult <c>Usecode_symbol_table</c> (<c>ucsymtbl.cc</c>): the table UCC
/// writes before its functions. It names functions (statics are saved by
/// name), maps shapes above 0x3FF to their functions, and says which
/// functions take the phantom item argument.
/// </summary>
public sealed class UsecodeSymbolTable
{
    /// <summary>Exult <c>UCSYMTBL_MAGIC0</c>, <c>UCSYMTBL_MAGIC1</c> ("UCSY").</summary>
    const uint Magic0 = 0xffffffffu;
    const uint Magic1 = ('U' << 24) + ('C' << 16) + ('S' << 8) + 'Y';

    readonly List<UsecodeSymbol> _symbols = new();
    readonly Dictionary<string, UsecodeSymbol> _byName = new();
    readonly Dictionary<int, UsecodeSymbol> _byVal = new();
    readonly Dictionary<int, int> _shapeFuns = new();

    public IReadOnlyList<UsecodeSymbol> Symbols => _symbols;

    /// <summary>Exult <c>has_symbol_table</c>: the two magic words; moves past them when present.</summary>
    public static bool HasSymbolTable(byte[] data, ref int ip)
    {
        if (ip + 8 > data.Length
            || BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(ip, 4)) != Magic0
            || BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(ip + 4, 4)) != Magic1)
        {
            return false;
        }

        ip += 8;
        return true;
    }

    public static UsecodeSymbolTable Read(byte[] data, ref int ip)
    {
        var table = new UsecodeSymbolTable();
        table.ReadScope(data, ref ip, true);
        return table;
    }

    /// <summary>
    /// Exult <c>Usecode_scope_symbol::read</c>. A class's own scope is read
    /// to keep the stream in step, then its method table
    /// (<c>Usecode_class_symbol::read</c>); classes aren't run here.
    /// </summary>
    void ReadScope(byte[] data, ref int ip, bool top)
    {
        var cnt = ReadI32(data, ref ip);
        ReadI32(data, ref ip); // Version.
        for (var i = 0; i < cnt; i++)
        {
            var name = ReadCString(data, ref ip);
            var kind = (UsecodeSymbolKind)ReadU16(data, ref ip);
            var val = ReadI32(data, ref ip);
            var shape = 0;
            if (kind == UsecodeSymbolKind.ClassScope)
            {
                ReadScope(data, ref ip, false);
                var methods = ReadU16(data, ref ip);
                ip += 2 * methods;
                ReadU16(data, ref ip); // Class variables.
            }
            else if (kind == UsecodeSymbolKind.ShapeFun)
            {
                shape = ReadI32(data, ref ip);
                if (top)
                {
                    _shapeFuns[shape] = val;
                }
            }

            if (!top)
            {
                continue;
            }

            var sym = new UsecodeSymbol(name, kind, val, shape);
            _symbols.Add(sym);
            _byName.TryAdd(name, sym);
            if (kind != UsecodeSymbolKind.ClassScope)
            {
                _byVal.TryAdd(val, sym);
            }
        }
    }

    /// <summary>Another layer's table added to this one (its names and shape functions win where both have one).</summary>
    public UsecodeSymbolTable Merge(UsecodeSymbolTable other)
    {
        foreach (var sym in other._symbols)
        {
            _symbols.Add(sym);
            _byName[sym.Name] = sym;
            if (sym.Kind != UsecodeSymbolKind.ClassScope)
            {
                _byVal[sym.Value] = sym;
            }
        }

        foreach (var (shape, fun) in other._shapeFuns)
        {
            _shapeFuns[shape] = fun;
        }

        return this;
    }

    public UsecodeSymbol? Find(string name) => _byName.GetValueOrDefault(name);

    public UsecodeSymbol? Find(int val) => _byVal.GetValueOrDefault(val);

    /// <summary>Exult <c>get_high_shape_fun</c>: a shape above 0x3FF's function, -1 when the table names none.</summary>
    public int GetHighShapeFun(int shape)
    {
        if (_shapeFuns.Count == 0)
        {
            return 0x1000 + (shape - 0x400);
        }

        return _shapeFuns.TryGetValue(shape, out var fun) ? fun : -1;
    }

    /// <summary>Exult <c>Usecode_scope_symbol::is_object_fun</c>: functions the table doesn't know keep the originals' rule.</summary>
    public bool IsObjectFun(int val)
    {
        if (!_byVal.TryGetValue(val, out var sym))
        {
            return val < 0x800;
        }

        return sym.Kind is UsecodeSymbolKind.ShapeFun or UsecodeSymbolKind.ObjectFun;
    }

    static string ReadCString(byte[] d, ref int ip)
    {
        var start = ip;
        while (ip < d.Length && d[ip] != 0)
        {
            ip++;
        }

        var s = Encoding.Latin1.GetString(d, start, ip - start);
        ip++;
        return s;
    }

    static int ReadU16(byte[] d, ref int ip)
    {
        var v = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(ip, 2));
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
