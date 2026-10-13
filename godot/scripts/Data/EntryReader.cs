using System.Globalization;

namespace U7.Data;

/// <summary>
/// Exult's <c>ReadInt</c> / <c>ReadStr</c> (files/utils.h) over one data
/// file entry, as the <c>istringstream</c> they read: a decimal number, then
/// everything up to and past the next '/'. A read that finds no number fails,
/// and the stream stays failed, so every read after it gives its default, as
/// one at the end does.
/// </summary>
public sealed class EntryReader(string text)
{
    int _pos;
    bool _fail;
    bool _eof;

    /// <summary>Exult <c>in.good()</c>.</summary>
    public bool Good => !_fail && !_eof;

    /// <summary>Exult <c>ReadInt(in, def)</c>.</summary>
    public int Int(int def = 0)
    {
        if (_eof || _fail)
        {
            return def;
        }

        var i = _pos;
        while (i < text.Length && char.IsWhiteSpace(text[i]))
        {
            i++;
        }

        var start = i;
        if (i < text.Length && text[i] is '-' or '+')
        {
            i++;
        }

        var digits = i;
        while (i < text.Length && char.IsAsciiDigit(text[i]))
        {
            i++;
        }

        _eof = i >= text.Length;
        if (i == digits || !int.TryParse(text.AsSpan(start, i - start), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var num))
        {
            _fail = true;
            return def;
        }

        SkipPastSlash(i);
        return num;
    }

    /// <summary>Exult <c>ReadStr(in)</c>: up to the next '/' (<c>getline</c>), past it.</summary>
    public string Str()
    {
        if (_eof || _fail)
        {
            _fail = true;
            return "";
        }

        var slash = text.IndexOf('/', _pos);
        var end = slash < 0 ? text.Length : slash;
        var s = text[_pos..end];
        SkipPastSlash(_pos);
        return s;
    }

    void SkipPastSlash(int from)
    {
        var slash = text.IndexOf('/', from);
        if (slash < 0)
        {
            _pos = text.Length;
            _eof = true;
        }
        else
        {
            _pos = slash + 1;
        }
    }
}
