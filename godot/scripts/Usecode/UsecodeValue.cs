using System.Globalization;
using System.Text;
using U7.Data;

namespace U7.Usecode;

public enum UsecodeValType
{
    Int = 0,
    String = 1,
    Array = 2,
    Pointer = 3
}

/// <summary>
/// Tagged usecode value. Matches Exult <c>Usecode_value</c> (int / string /
/// array / itemref). Copies are deep for arrays; undefined vs 0 is preserved.
/// </summary>
public sealed class UsecodeValue
{
    UsecodeValType _type = UsecodeValType.Int;
    long _int;
    string _str = "";
    List<UsecodeValue>? _array;
    U7Object? _obj;
    bool _undefined = true;

    public UsecodeValType Type => _type;
    public bool IsUndefined => _undefined;
    public bool IsArray => _type == UsecodeValType.Array;
    public bool IsInt => _type == UsecodeValType.Int;
    public bool IsPtr => _type == UsecodeValType.Pointer;
    public bool IsString => _type == UsecodeValType.String;

    public static UsecodeValue Undefined { get; } = new();

    public static UsecodeValue FromInt(long v)
    {
        var u = new UsecodeValue();
        u._int = v;
        u._undefined = false;
        return u;
    }

    public static UsecodeValue FromString(string s)
    {
        var u = new UsecodeValue
        {
            _type = UsecodeValType.String,
            _str = s ?? "",
            _undefined = false
        };
        return u;
    }

    public static UsecodeValue FromObject(U7Object? obj)
    {
        var u = new UsecodeValue
        {
            _type = UsecodeValType.Pointer,
            _obj = obj,
            _undefined = false
        };
        return u;
    }

    public static UsecodeValue FromArray(int size, UsecodeValue? first = null)
    {
        var u = new UsecodeValue
        {
            _type = UsecodeValType.Array,
            _array = new List<UsecodeValue>(Math.Max(size, 0)),
            _undefined = false
        };
        for (var i = 0; i < size; i++)
        {
            u._array.Add(FromInt(0));
        }

        if (first is not null && size > 0)
        {
            u._array[0] = first.Clone();
        }

        return u;
    }

    public UsecodeValue Clone()
    {
        var c = new UsecodeValue
        {
            _type = _type,
            _int = _int,
            _str = _str,
            _obj = _obj,
            _undefined = _undefined
        };
        if (_array is not null)
        {
            c._array = new List<UsecodeValue>(_array.Count);
            foreach (var e in _array)
            {
                c._array.Add(e.Clone());
            }
        }

        return c;
    }

    public long IntValue => _type == UsecodeValType.Int ? _int : 0;

    public U7Object? PtrValue => _type == UsecodeValType.Pointer ? _obj : null;

    /// <summary>
    /// Exult <c>get_str_value</c>: string, or "" if undefined/empty array, else null.
    /// </summary>
    public string? StrValue
    {
        get
        {
            if (_type == UsecodeValType.String)
            {
                return _str;
            }

            if (_undefined || (_type == UsecodeValType.Array && (_array is null || _array.Count == 0)))
            {
                return "";
            }

            return null;
        }
    }

    public int ArraySize => _type == UsecodeValType.Array ? _array?.Count ?? 0 : 0;

    public long NeedIntValue()
    {
        var str = StrValue;
        if (str is not null)
        {
            return int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                ? n
                : 0;
        }

        if (_type == UsecodeValType.Array && ArraySize > 0)
        {
            return _array![0].NeedIntValue();
        }

        if (_type == UsecodeValType.Pointer)
        {
            return _obj is null ? 0 : _obj.Id & 0x7ffffff;
        }

        return IntValue;
    }

    public UsecodeValue GetElem(int i)
    {
        if (_type == UsecodeValType.Array && _array is not null && (uint)i < (uint)_array.Count)
        {
            return _array[i];
        }

        return FromInt(0);
    }

    public UsecodeValue GetElem0()
    {
        if (_type != UsecodeValType.Array)
        {
            return this;
        }

        return ArraySize > 0 ? _array![0] : FromInt(0);
    }

    public void PutElem(int i, UsecodeValue val)
    {
        if (_type != UsecodeValType.Array || _array is null)
        {
            return;
        }

        if ((uint)i >= (uint)_array.Count)
        {
            return;
        }

        _array[i] = val.Clone();
    }

    public bool Resize(int newSize)
    {
        if (_type != UsecodeValType.Array)
        {
            var elem = Clone();
            _type = UsecodeValType.Array;
            _array = new List<UsecodeValue>(Math.Max(newSize, 1));
            _undefined = false;
            for (var i = 0; i < newSize; i++)
            {
                _array.Add(i == 0 ? elem : FromInt(0));
            }

            return true;
        }

        _array ??= new List<UsecodeValue>();
        while (_array.Count < newSize)
        {
            _array.Add(FromInt(0));
        }

        if (_array.Count > newSize)
        {
            _array.RemoveRange(newSize, _array.Count - newSize);
        }

        return true;
    }

    public int FindElem(UsecodeValue val)
    {
        if (_type != UsecodeValType.Array || _array is null)
        {
            return -1;
        }

        for (var i = 0; i < _array.Count; i++)
        {
            if (_array[i].EqualsValue(val))
            {
                return i;
            }
        }

        return -1;
    }

    public UsecodeValue Concat(UsecodeValue val2)
    {
        if (_type != UsecodeValType.Array)
        {
            var tmp = FromArray(1, this);
            _type = UsecodeValType.Array;
            _array = tmp._array;
            _undefined = false;
        }

        _array ??= new List<UsecodeValue>();
        if (val2._type != UsecodeValType.Array)
        {
            _array.Add(val2.Clone());
        }
        else if (val2._array is not null)
        {
            foreach (var e in val2._array)
            {
                _array.Add(e.Clone());
            }
        }

        return this;
    }

    /// <summary>
    /// Used by ARRC: write val2 at index, flattening arrays. Returns count added.
    /// </summary>
    public int AddValues(int index, UsecodeValue val2)
    {
        var size = ArraySize;
        if (!val2.IsArray)
        {
            if (index >= size)
            {
                _array!.Add(val2.Clone());
            }
            else
            {
                _array![index] = val2.Clone();
            }

            return 1;
        }

        var size2 = val2.ArraySize;
        if (index + size2 > size)
        {
            Resize(index + size2);
        }

        for (var i = 0; i < size2; i++)
        {
            _array![index + i] = val2.GetElem(i).Clone();
        }

        return size2;
    }

    public bool IsFalse =>
        _type switch
        {
            UsecodeValType.Int => _int == 0,
            UsecodeValType.Pointer => _obj is null,
            UsecodeValType.Array => ArraySize == 0,
            _ => false
        };

    public bool IsTrue => !IsFalse;

    public bool EqualsValue(UsecodeValue v2)
    {
        if (ReferenceEquals(this, v2))
        {
            return true;
        }

        switch (_type)
        {
            case UsecodeValType.Int:
                return v2._type switch
                {
                    UsecodeValType.Int => _int == v2._int,
                    UsecodeValType.Pointer => _int == 0 && v2._obj is null,
                    UsecodeValType.Array => EqualsValue(v2.GetElem0()),
                    _ => false
                };
            case UsecodeValType.Pointer:
                return v2._type switch
                {
                    UsecodeValType.Int => _obj is null && v2._int == 0,
                    UsecodeValType.Pointer => ReferenceEquals(_obj, v2._obj),
                    UsecodeValType.Array => v2.ArraySize > 0 && EqualsValue(v2.GetElem(0)),
                    _ => false
                };
            case UsecodeValType.Array:
                return v2._type switch
                {
                    UsecodeValType.Int => GetElem0().EqualsValue(v2),
                    UsecodeValType.Pointer => ArraySize > 0 && GetElem(0).EqualsValue(v2),
                    UsecodeValType.Array => ArraysEqual(v2),
                    _ => false
                };
            case UsecodeValType.String:
                return v2._type == UsecodeValType.String && _str == v2._str;
            default:
                return false;
        }
    }

    bool ArraysEqual(UsecodeValue v2)
    {
        var a = _array;
        var b = v2._array;
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!a[i].EqualsValue(b[i]))
            {
                return false;
            }
        }

        return true;
    }

    public UsecodeValue Add(UsecodeValue v2)
    {
        var v1 = Clone();
        if (v1._undefined)
        {
            return v2.Clone();
        }

        if (v2._undefined)
        {
            return v1;
        }

        if (v1._type == UsecodeValType.Int)
        {
            if (v2._type == UsecodeValType.Int)
            {
                v1._int += v2._int;
            }
            else if (v2._type == UsecodeValType.String)
            {
                v1._type = UsecodeValType.String;
                v1._str = v1._int.ToString(CultureInfo.InvariantCulture) + v2._str;
            }

            return v1;
        }

        if (v1._type == UsecodeValType.String)
        {
            if (v2._type == UsecodeValType.Int)
            {
                v1._str += v2._int.ToString(CultureInfo.InvariantCulture);
            }
            else if (v2._type == UsecodeValType.String)
            {
                v1._str += v2._str;
            }

            return v1;
        }

        return FromInt(0);
    }

    public UsecodeValue Sub(UsecodeValue v2) => Operate(v2, (a, b) => a - b);

    public UsecodeValue Mul(UsecodeValue v2) => Operate(v2, (a, b) => a * b);

    public UsecodeValue Div(UsecodeValue v2) =>
        Operate(v2, (a, b) => b == 0 ? (a < 0 ? long.MinValue : long.MaxValue) : a / b);

    public UsecodeValue Mod(UsecodeValue v2) =>
        Operate(v2, (a, b) => b == 0 ? (a < 0 ? long.MinValue : long.MaxValue) : a % b);

    UsecodeValue Operate(UsecodeValue v2, Func<long, long, long> op)
    {
        var v1 = Clone();
        if (v1._undefined)
        {
            return FromInt(op(0, v2.NeedIntValue()));
        }

        if (v2._undefined)
        {
            return FromInt(0);
        }

        if (v1._type == UsecodeValType.Int)
        {
            if (v2._type is UsecodeValType.Int or UsecodeValType.String)
            {
                v1._int = op(v1._int, v2.NeedIntValue());
            }

            return v1;
        }

        if (v1._type == UsecodeValType.String && v2._type == UsecodeValType.String)
        {
            v1._str += " " + v2._str;
            return v1;
        }

        return FromInt(0);
    }

    public override string ToString()
    {
        return _type switch
        {
            UsecodeValType.Int => _undefined ? "undef" : _int.ToString(CultureInfo.InvariantCulture),
            UsecodeValType.String => $"\"{_str}\"",
            UsecodeValType.Pointer => _obj is null ? "null" : $"obj#{_obj.Id} shape {_obj.Shape}",
            UsecodeValType.Array => $"[{ArraySize}]",
            _ => "?"
        };
    }

    public static string ReadCString(byte[] data, int offset)
    {
        if ((uint)offset >= (uint)data.Length)
        {
            return "";
        }

        var end = offset;
        while (end < data.Length && data[end] != 0)
        {
            end++;
        }

        return Encoding.Latin1.GetString(data, offset, end - offset);
    }
}
