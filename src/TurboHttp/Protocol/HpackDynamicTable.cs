using System.Collections.Generic;
using System.Text;

namespace TurboHttp.Protocol;

internal sealed class HpackDynamicTable
{
    private readonly LinkedList<(string Name, string Value)> _entries = [];
    private int _currentSize;
    private int _maxSize = 4096;

    public void Add(string name, string value)
    {
        var size = EntrySize(name, value);
        if (size > _maxSize)
        {
            Clear();
            return;
        }

        _entries.AddFirst((name, value));
        _currentSize += size;
        Evict();
    }

    public void SetMaxSize(int maxSize)
    {
        _maxSize = maxSize;
        Evict();
    }

    public int FindFullMatch(string name, string value)
    {
        var idx = 1;
        foreach (var (n, v) in _entries)
        {
            if (n == name && v == value)
            {
                return idx;
            }

            idx++;
        }

        return -1;
    }

    public int FindNameMatch(string name)
    {
        var idx = 1;
        foreach (var (n, _) in _entries)
        {
            if (n == name)
            {
                return idx;
            }

            idx++;
        }

        return -1;
    }

    public (string Name, string Value)? GetEntry(int index)
    {
        if (index < 1 || index > _entries.Count) return null;
        var node = _entries.First;
        for (var i = 1; i < index; i++)
            node = node!.Next;
        return node!.Value;
    }

    public int Count => _entries.Count;

    private void Clear()
    {
        _entries.Clear();
        _currentSize = 0;
    }

    private void Evict()
    {
        while (_currentSize > _maxSize && _entries.Last is { } last)
        {
            _currentSize -= EntrySize(last.Value.Name, last.Value.Value);
            _entries.RemoveLast();
        }
    }

    private static int EntrySize(string name, string value) =>
        Encoding.UTF8.GetByteCount(name) + Encoding.UTF8.GetByteCount(value) + 32;
}