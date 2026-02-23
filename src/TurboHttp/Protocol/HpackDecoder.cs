using System;
using System.Collections.Generic;
using System.Text;

namespace TurboHttp.Protocol;

public sealed class HpackDecoder
{
    private readonly HpackDynamicTable _table = new();

    public List<(string Name, string Value)> Decode(ReadOnlySpan<byte> data)
    {
        var result = new List<(string, string)>();
        var pos = 0;

        while (pos < data.Length)
        {
            var b = data[pos];

            if ((b & 0x80) != 0)
            {
                var idx = ReadInteger(data, ref pos, 7);
                result.Add(Lookup(idx));
            }
            else if ((b & 0x40) != 0)
            {
                var (name, value) = ReadLiteralHeader(data, ref pos, 6);
                _table.Add(name, value);
                result.Add((name, value));
            }
            else if ((b & 0x20) != 0)
            {
                var newSize = ReadInteger(data, ref pos, 5);
                _table.SetMaxSize(newSize);
            }
            else
            {
                var (name, value) = ReadLiteralHeader(data, ref pos, 4);
                result.Add((name, value));
            }
        }

        return result;
    }

    private (string Name, string Value) ReadLiteralHeader(ReadOnlySpan<byte> data, ref int pos, int prefixBits)
    {
        var idx = ReadInteger(data, ref pos, prefixBits);

        var name = idx == 0 ? ReadString(data, ref pos) : Lookup(idx).Name;

        var value = ReadString(data, ref pos);
        return (name, value);
    }

    private (string Name, string Value) Lookup(int idx)
    {
        if (idx <= 0)
        {
            throw new HpackException($"Ungültiger HPACK-Index: {idx}");
        }

        if (idx <= HpackStaticTable.StaticCount)
        {
            return HpackStaticTable.Entries[idx];
        }

        var dynIdx = idx - HpackStaticTable.StaticCount;
        return _table.GetEntry(dynIdx)
               ?? throw new HpackException($"HPACK Dynamischer Index {idx} außerhalb des Bereichs.");
    }

    internal static int ReadInteger(ReadOnlySpan<byte> data, ref int pos, int prefixBits)
    {
        var mask = (1 << prefixBits) - 1;
        var value = data[pos] & mask;
        pos++;

        if (value < mask)
        {
            return value;
        }

        var shift = 0;
        while (pos < data.Length)
        {
            var b = data[pos++];
            value += (b & 0x7F) << shift;
            shift += 7;
            if ((b & 0x80) == 0)
            {
                break;
            }
        }

        return value;
    }

    private static string ReadString(ReadOnlySpan<byte> data, ref int pos)
    {
        var huffman = (data[pos] & 0x80) != 0;
        var length = ReadInteger(data, ref pos, 7);

        if (pos + length > data.Length)
        {
            throw new HpackException(nameof(data));
        }

        var strBytes = data[pos..(pos + length)];
        pos += length;

        var rawBytes = huffman
            ? HuffmanCodec.Decode(strBytes)
            : strBytes.ToArray();

        return Encoding.UTF8.GetString(rawBytes);
    }
}