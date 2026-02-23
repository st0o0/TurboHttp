using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TurboMqtt.Protocol;

public sealed class HpackEncoder(bool useHuffman = true)
{
    private readonly HpackDynamicTable _table = new();

    public byte[] Encode(IEnumerable<(string Name, string Value)> headers)
    {
        using var ms = new MemoryStream();
        foreach (var (name, value) in headers)
        {
            EncodeHeader(ms, name, value);
        }
        return ms.ToArray();
    }

    public byte[] Encode(IReadOnlyList<(string Name, string Value)> headers)
        => Encode((IEnumerable<(string, string)>)headers);

    private void EncodeHeader(MemoryStream ms, string name, string value)
    {
        var staticIdx = HpackStaticTable.FindIndex(name, value);
        if (staticIdx > 0 && HpackStaticTable.Entries[staticIdx].Value == value)
        {
            WriteInteger(ms, staticIdx, 7, 0x80);
            return;
        }

        var dynIdx = _table.FindFullMatch(name, value);
        if (dynIdx > 0)
        {
            WriteInteger(ms, HpackStaticTable.StaticCount + dynIdx, 7, 0x80);
            return;
        }

        var nameIdx = staticIdx > 0 ? staticIdx : _table.FindNameMatch(name);
        var absIdx = nameIdx switch
        {
            > 0 when nameIdx <= HpackStaticTable.StaticCount => nameIdx,
            > 0 => HpackStaticTable.StaticCount + nameIdx,
            _ => 0
        };

        if (absIdx > 0)
        {
            WriteInteger(ms, absIdx, 6, 0x40);
        }
        else
        {
            ms.WriteByte(0x40);
            WriteString(ms, name);
        }

        WriteString(ms, value);
        _table.Add(name, value);
    }

    public static void WriteInteger(MemoryStream ms, int value, int prefixBits, byte prefix)
    {
        var max = (1 << prefixBits) - 1;
        if (value < max)
        {
            ms.WriteByte((byte)(prefix | value));
            return;
        }

        ms.WriteByte((byte)(prefix | max));
        value -= max;
        while (value >= 128)
        {
            ms.WriteByte((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }

        ms.WriteByte((byte)value);
    }

    private void WriteString(MemoryStream ms, string value)
    {
        var raw = Encoding.UTF8.GetBytes(value);
        if (useHuffman)
        {
            using var huf = new MemoryStream(raw.Length);
            HuffmanCodec.Encode(raw, huf);
            var hufBytes = huf.ToArray();
            // H-Bit = 1, Länge
            WriteInteger(ms, hufBytes.Length, 7, 0x80);
            ms.Write(hufBytes);
        }
        else
        {
            // H-Bit = 0
            WriteInteger(ms, raw.Length, 7, 0x00);
            ms.Write(raw);
        }
    }
}