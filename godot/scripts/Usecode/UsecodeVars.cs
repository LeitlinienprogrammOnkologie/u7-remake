using System.IO;
using System.Text;
using U7.Core;

namespace U7.Usecode;

/// <summary>
/// Exult <c>usecode.var</c> (<c>Usecode_internal::write</c>,
/// <c>read_usevars</c>): UCC's statics. First the global ones, then each
/// function's: by name (0xFFFFFFFE, a 16-bit length, the name) when the
/// symbol table knows the function, else by number; -1 ends the list. The
/// originals have no statics, so their file is just the two words.
/// </summary>
public static class UsecodeVars
{
    public const string FileName = "USECODE.VAR";

    public static void Write(string dir, UsecodeMachine vm)
    {
        using var w = new BinaryWriter(File.Create(Path.Combine(dir, FileName)));
        w.Write(vm.Statics.Count);
        foreach (var v in vm.Statics)
        {
            WriteValue(w, v);
        }

        var symbols = vm.File.Symbols;
        // Exult walks its function table: the live functions, not the originals a patch replaced.
        foreach (var top in vm.File.Functions.Values)
        {
            if (top.Statics.Count == 0)
            {
                continue;
            }

            if (symbols?.Find(top.Id) is { } sym)
            {
                var name = Encoding.Latin1.GetBytes(sym.Name);
                w.Write(0xfffffffeu);
                w.Write((ushort)name.Length);
                w.Write(name);
            }
            else
            {
                w.Write(top.Id);
            }

            w.Write(top.Statics.Count);
            foreach (var v in top.Statics)
            {
                WriteValue(w, v);
            }
        }

        w.Write(0xffffffffu);
    }

    /// <summary>Restores the statics from the loaded save; nothing for a new game or a save without the file.</summary>
    public static void Read(UsecodeMachine vm)
    {
        var data = U7Paths.ReadGameDat(FileName);
        if (data is null || data.Length < 4)
        {
            return;
        }

        try
        {
            using var r = new BinaryReader(new MemoryStream(data));
            var cnt = r.ReadInt32();
            vm.Statics.Clear();
            for (var i = 0; i < cnt; i++)
            {
                vm.Statics.Add(ReadValue(r));
            }

            while (r.BaseStream.Position + 4 <= r.BaseStream.Length)
            {
                var funid = r.ReadUInt32();
                if (funid == 0xffffffffu)
                {
                    break;
                }

                var id = (int)funid;
                if (funid == 0xfffffffeu)
                {
                    var name = Encoding.Latin1.GetString(r.ReadBytes(r.ReadUInt16()));
                    id = vm.File.Symbols?.Find(name)?.Value ?? -1;
                }

                var n = r.ReadInt32();
                var fun = vm.File.Get(id);
                var values = new List<UsecodeValue>(n);
                for (var i = 0; i < n; i++)
                {
                    values.Add(ReadValue(r));
                }

                if (fun is not null)
                {
                    fun.Statics.Clear();
                    fun.Statics.AddRange(values);
                }
            }
        }
        catch (EndOfStreamException)
        {
            Godot.GD.Print($"{FileName} is short");
        }
    }

    /// <summary>Exult <c>Usecode_value::save</c>: a type byte, then the value; an item is written as 0, as Exult does.</summary>
    static void WriteValue(BinaryWriter w, UsecodeValue v)
    {
        w.Write((byte)v.Type);
        switch (v.Type)
        {
            case UsecodeValType.Int:
                w.Write((int)v.IntValue);
                break;
            case UsecodeValType.String:
            {
                var bytes = Encoding.Latin1.GetBytes(v.StrValue ?? "");
                w.Write((ushort)bytes.Length);
                w.Write(bytes);
                break;
            }
            case UsecodeValType.Array:
                w.Write((ushort)v.ArraySize);
                for (var i = 0; i < v.ArraySize; i++)
                {
                    WriteValue(w, v.GetElem(i));
                }

                break;
            default:
                w.Write(0);
                break;
        }
    }

    /// <summary>Exult <c>Usecode_value::restore</c>; an item comes back empty.</summary>
    static UsecodeValue ReadValue(BinaryReader r)
    {
        var type = (UsecodeValType)r.ReadByte();
        switch (type)
        {
            case UsecodeValType.Int:
                return UsecodeValue.FromInt(r.ReadInt32());
            case UsecodeValType.String:
                return UsecodeValue.FromString(Encoding.Latin1.GetString(r.ReadBytes(r.ReadUInt16())));
            case UsecodeValType.Array:
            {
                int n = r.ReadUInt16();
                var arr = UsecodeValue.FromArray(n);
                for (var i = 0; i < n; i++)
                {
                    arr.PutElem(i, ReadValue(r));
                }

                return arr;
            }
            case UsecodeValType.Pointer:
                r.ReadInt32();
                return UsecodeValue.FromObject(null);
            default:
                throw new InvalidDataException($"{FileName}: value type {(int)type} (classes aren't run here)");
        }
    }
}
