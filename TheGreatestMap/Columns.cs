using System;
using System.Collections.Generic;
using System.Text;

namespace TheGreatestMap
{
    /// <summary>
    /// Writing a run of one field rather than a run of whole markers.
    ///
    /// A map used to go out marker by marker: owner, author, name, place, source, and on to the
    /// next. Compression finds repetition in bytes that lie near each other, and written that way
    /// almost nothing does -- five owners spread across four thousand records are ninety bytes
    /// apart every time, so each is paid for afresh. Gathered into one run they collapse to
    /// nothing, and the same goes for every field that repeats.
    ///
    /// It also makes the two things worth doing to numbers possible. A column of positions can be
    /// sorted so that neighbours on the map are neighbours in the file, and each one written as
    /// the small step from the one before; a column of times can be written as seconds from a
    /// fixed day rather than as hundred-nanosecond counts since the year 1. Neither is available
    /// when the fields are interleaved, because there is nothing to take a difference against.
    ///
    /// And the record framing simply goes. Every marker used to carry four bytes saying how long
    /// it was, so a reader could step over one it did not understand -- eighteen kilobytes of
    /// length on this map. A column carries one length for the lot, and a reader that meets a
    /// column it does not know skips the whole run just as cleanly. Seventeen lengths instead of
    /// four thousand six hundred.
    /// </summary>
    internal sealed class ColumnWriter
    {
        private readonly List<byte> _bytes = new List<byte>();

        /// <summary>A whole number, in as few bytes as it needs: under 128 takes one.</summary>
        internal void Var(long value)
        {
            ulong rest = (ulong)value;
            while (true)
            {
                byte piece = (byte)(rest & 0x7f);
                rest >>= 7;
                if (rest != 0) _bytes.Add((byte)(piece | 0x80));
                else { _bytes.Add(piece); return; }
            }
        }

        /// <summary>
        /// A number that may be negative, folded so that small negatives stay small: -1 becomes
        /// 1, 1 becomes 2. Without this a step of one meter westward costs ten bytes, because a
        /// negative number has every high bit set.
        /// </summary>
        internal void Zig(long value) => Var((value << 1) ^ (value >> 63));

        internal void Byte(byte value) => _bytes.Add(value);

        internal void Long(long value)
        {
            for (int i = 0; i < 8; i++) _bytes.Add((byte)(value >> (i * 8)));
        }

        internal void Str(string value)
        {
            var utf8 = Encoding.UTF8.GetBytes(value ?? "");
            Var(utf8.Length);
            _bytes.AddRange(utf8);
        }

        internal byte[] Done() => _bytes.ToArray();
    }

    /// <summary>Reads back what <see cref="ColumnWriter"/> wrote, in the same order.</summary>
    internal sealed class ColumnReader
    {
        private readonly byte[] _bytes;
        private int _at;

        internal ColumnReader(byte[] bytes) { _bytes = bytes ?? new byte[0]; }

        internal bool More => _at < _bytes.Length;

        internal long Var()
        {
            ulong value = 0;
            int shift = 0;
            while (_at < _bytes.Length)
            {
                byte piece = _bytes[_at++];
                value |= (ulong)(piece & 0x7f) << shift;
                if ((piece & 0x80) == 0) break;
                shift += 7;
            }
            return (long)value;
        }

        internal long Zig()
        {
            long folded = Var();
            return (folded >> 1) ^ -(folded & 1);
        }

        internal byte Byte() => _at < _bytes.Length ? _bytes[_at++] : (byte)0;

        internal long Long()
        {
            long value = 0;
            for (int i = 0; i < 8 && _at < _bytes.Length; i++) value |= (long)_bytes[_at++] << (i * 8);
            return value;
        }

        internal string Str()
        {
            int length = (int)Var();
            if (length <= 0 || _at + length > _bytes.Length) return "";
            string text = Encoding.UTF8.GetString(_bytes, _at, length);
            _at += length;
            return text;
        }
    }

    /// <summary>
    /// A column of values drawn from a small set -- five owners, forty icons, fifteen kinds --
    /// written once as a list and then referred to by number.
    ///
    /// Compression would find most of this itself, which is why interning looked barely worth
    /// doing when it was measured on its own. What it buys here is that the numbers are tiny and
    /// uniform, so the column of them compresses to almost nothing and the strings are paid for
    /// exactly once rather than once per marker in the raw file as well.
    /// </summary>
    internal sealed class StringTable
    {
        private readonly List<string> _values = new List<string>();
        private readonly Dictionary<string, int> _index = new Dictionary<string, int>(StringComparer.Ordinal);

        internal int Of(string value)
        {
            value = value ?? "";
            if (_index.TryGetValue(value, out int at)) return at;
            at = _values.Count;
            _values.Add(value);
            _index[value] = at;
            return at;
        }

        internal void Write(ColumnWriter into)
        {
            into.Var(_values.Count);
            foreach (var value in _values) into.Str(value);
        }

        internal static List<string> Read(ColumnReader from)
        {
            int count = (int)from.Var();
            var values = new List<string>(count);
            for (int i = 0; i < count; i++) values.Add(from.Str());
            return values;
        }
    }
}
