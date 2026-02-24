using TurboHttp.Protocol;

namespace TurboHttp.Tests;

public sealed class HpackTests
{
    [Fact]
    public void Encode_IndexedStaticEntry_SingleByte()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var headers = new List<(string, string)> { (":method", "GET") };
        var encoded = encoder.Encode(headers);

        Assert.Equal(1, encoded.Length);
        Assert.Equal(0x82, encoded.Span[0]);
    }

    [Fact]
    public void Encode_Decode_RoundTrip_PseudoHeaders()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":path", "/index.html"),
            (":scheme", "https"),
            (":authority", "example.com"),
        };

        var encoded = encoder.Encode(headers);
        var decoded = decoder.Decode(encoded.Span);

        Assert.Equal(headers.Count, decoded.Count);
        for (var i = 0; i < headers.Count; i++)
        {
            Assert.Equal(headers[i].Item1, decoded[i].Name);
            Assert.Equal(headers[i].Item2, decoded[i].Value);
        }
    }

    [Fact]
    public void Encode_Decode_RoundTrip_WithHuffman()
    {
        var encoder = new HpackEncoder(useHuffman: true);
        var decoder = new HpackDecoder();

        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":path", "/api/search?q=hello"),
            (":scheme", "https"),
            (":authority", "api.example.com"),
            ("content-type", "application/json"),
            ("authorization", "Bearer token123"),
            ("accept", "application/json, text/plain"),
        };

        var encoded = encoder.Encode(headers);
        var decoded = decoder.Decode(encoded.Span);

        Assert.Equal(headers.Count, decoded.Count);
        for (var i = 0; i < headers.Count; i++)
        {
            Assert.Equal(headers[i].Item1, decoded[i].Name);
            Assert.Equal(headers[i].Item2, decoded[i].Value);
        }
    }

    [Fact]
    public void Decode_LiteralNewName_CorrectOrder()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var headers = new List<(string, string)>
        {
            ("x-custom-header", "my-value"),
            ("x-another", "data"),
        };

        var encoded = encoder.Encode(headers);
        var decoded = decoder.Decode(encoded.Span);

        Assert.Equal(2, decoded.Count);
        Assert.Equal("x-custom-header", decoded[0].Name);
        Assert.Equal("my-value", decoded[0].Value);
        Assert.Equal("x-another", decoded[1].Name);
        Assert.Equal("data", decoded[1].Value);
    }

    [Fact]
    public void Decode_DynamicTableSizeUpdate_Respected()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var h1 = new List<(string, string)> { ("x-test", "value") };
        var e1 = encoder.Encode(h1);
        decoder.Decode(e1.Span);

        // RFC 7541 §6.3: Dynamic Table Size Update (001xxxxx) with size 0
        // 0x20 = 001 00000 = table size update to 0
        // Then encode a new header which will be literal with incremental indexing
        var h2 = new List<(string, string)> { ("x-fresh", "new") };

        // Create a size update followed by new header
        var sizeUpdate = new byte[] { 0x20 }; // table size to 0
        var encodedHeader = encoder.Encode(h2);

        var combined = new byte[sizeUpdate.Length + encodedHeader.Length];
        sizeUpdate.CopyTo(combined, 0);
        encodedHeader.Span.CopyTo(combined.AsSpan(sizeUpdate.Length));

        var decoded = decoder.Decode(combined);
        Assert.Single(decoded);
        Assert.Equal("x-fresh", decoded[0].Name);
        Assert.Equal("new", decoded[0].Value);
    }

    // ── US-201: RFC 7541 §2.3 — Dynamic table eviction ──────────────────────

    [Fact]
    public void DynamicTable_Eviction_OldestEntryRemovedWhenFull()
    {
        // RFC 7541 §4.4: When adding a new entry causes table size to exceed
        // MaxSize, the oldest (last) entries are evicted until the table fits.
        var table = new HpackDynamicTable();

        // Each entry = name UTF-8 bytes + value UTF-8 bytes + 32 overhead.
        // "a" (1) + "x" (1) + 32 = 34 bytes per entry.
        // Set max size to hold exactly 2 such entries: 68 bytes.
        table.SetMaxSize(68);

        table.Add("a", "x"); // oldest — will be evicted
        table.Add("b", "y"); // newer

        Assert.Equal(2, table.Count);

        // Adding a third entry must evict the oldest ("a","x")
        table.Add("c", "z");

        Assert.Equal(2, table.Count);
        // Index 1 = newest (c,z), Index 2 = (b,y). Oldest (a,x) is gone.
        var newest = table.GetEntry(1);
        Assert.NotNull(newest);
        Assert.Equal("c", newest.Value.Name);
        Assert.Equal("z", newest.Value.Value);

        var second = table.GetEntry(2);
        Assert.NotNull(second);
        Assert.Equal("b", second.Value.Name);
        Assert.Equal("y", second.Value.Value);

        // Original oldest entry is no longer accessible
        Assert.Null(table.GetEntry(3));
    }

    [Fact]
    public void DynamicTable_EvictionOrder_NewestSurvives()
    {
        // RFC 7541 §4.4: Eviction is FIFO — oldest entries removed first.
        // Fill with A, B, C; reduce capacity so D forces eviction; verify
        // D and the most-recent surviving entries remain, oldest gone.
        var table = new HpackDynamicTable();

        // Entry size for single-char name + single-char value = 1+1+32 = 34 bytes
        // Set to hold 3 entries: 102 bytes
        table.SetMaxSize(102);

        table.Add("a", "1"); // oldest
        table.Add("b", "2");
        table.Add("c", "3"); // newest
        Assert.Equal(3, table.Count);

        // Shrink table to hold only 2 entries (68 bytes), then add D.
        // Shrinking evicts A. Adding D evicts B. Result: D, C.
        table.SetMaxSize(68);
        Assert.Equal(2, table.Count);
        // After shrink: index 1 = c, index 2 = b (a evicted)

        table.Add("d", "4");
        Assert.Equal(2, table.Count);

        // Index 1 = newest (d,4), Index 2 = (c,3). Oldest entries (a,b) gone.
        var entry1 = table.GetEntry(1);
        Assert.NotNull(entry1);
        Assert.Equal("d", entry1.Value.Name);
        Assert.Equal("4", entry1.Value.Value);

        var entry2 = table.GetEntry(2);
        Assert.NotNull(entry2);
        Assert.Equal("c", entry2.Value.Name);
        Assert.Equal("3", entry2.Value.Value);

        // a and b are gone
        Assert.Null(table.GetEntry(3));
    }

    [Fact]
    public void DynamicTable_SizeTooBig_ThrowsHpackException()
    {
        // RFC 7541 §4.2: Negative table size is invalid and must be rejected.
        var decoder = new HpackDecoder();
        Assert.Throws<HpackException>(() => decoder.SetMaxAllowedTableSize(-1));
    }

    // ── End US-201 ────────────────────────────────────────────────────────────

    // ── US-202: RFC 7541 §5.1 — Integer representation edge cases ────────────

    [Fact]
    public void ReadInteger_FitsInPrefix_SingleByte()
    {
        // RFC 7541 §5.1: If the integer value fits within the prefix bits,
        // it is encoded in a single byte. Value 5 with 5-bit prefix (max 31)
        // fits in one byte.
        var data = new byte[] { 0x05 }; // 00000101 — value 5 in low 5 bits
        var pos = 0;
        var result = HpackDecoder.ReadInteger(data, ref pos, prefixBits: 5);

        Assert.Equal(5, result);
        Assert.Equal(1, pos); // consumed exactly 1 byte
    }

    [Fact]
    public void ReadInteger_MultiByteEncoding_DecodedCorrectly()
    {
        // RFC 7541 §5.1 example: integer 1337 encoded with 5-bit prefix.
        // 1337 = 31 + 1306; 1306 = 0x051A
        // Byte 0: prefix all-ones = 0x1F (31)
        // 1306 in 7-bit groups: 1306 = 10*128 + 26 → 0x9A (26 | 0x80), 0x0A (10)
        var data = new byte[] { 0x1F, 0x9A, 0x0A };
        var pos = 0;
        var result = HpackDecoder.ReadInteger(data, ref pos, prefixBits: 5);

        Assert.Equal(1337, result);
        Assert.Equal(3, pos);
    }

    [Fact]
    public void ReadInteger_MaxValue_Accepted()
    {
        // RFC 7541 §5.1: Values up to (1 << 28) - 1 must be accepted.
        // Round-trip through WriteInteger → ReadInteger.
        var maxValue = (1 << 28) - 1; // 268435455
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        HpackEncoder.WriteInteger(maxValue, prefixBits: 5, prefixFlags: 0x00, buffer);

        var encoded = buffer.WrittenSpan.ToArray();
        var pos = 0;
        var result = HpackDecoder.ReadInteger(encoded, ref pos, prefixBits: 5);

        Assert.Equal(maxValue, result);
    }

    [Fact]
    public void ReadInteger_Overflow_ThrowsHpackException()
    {
        // RFC 7541 §5.1: Integer overflow must be detected.
        // Craft a multi-byte integer that overflows 2^28.
        // Start with prefix all-ones for 5-bit prefix: 0x1F (31)
        // Then 5 continuation bytes each with 0xFF (all bits set + continuation)
        // followed by a stop byte. This represents a value far exceeding 2^28.
        var data = new byte[]
        {
            0x1F, // prefix max (31)
            0xFF, // continuation: 0x7F << 0  = 127
            0xFF, // continuation: 0x7F << 7
            0xFF, // continuation: 0x7F << 14
            0xFF, // continuation: 0x7F << 21
            0xFF, // shift=28 → triggers overflow check
        };
        var pos = 0;
        var ex = Assert.Throws<HpackException>(() =>
            HpackDecoder.ReadInteger(data, ref pos, prefixBits: 5));
        Assert.Contains("overflow", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadInteger_TruncatedData_ThrowsHpackException()
    {
        // RFC 7541 §5.1: A multi-byte integer with no stop bit (continuation
        // bit set on last available byte) is truncated — must throw.
        var data = new byte[]
        {
            0x1F, // prefix max (31) — triggers multi-byte path
            0x80, // continuation bit set, value bits = 0, no more bytes follow
        };
        // Remove the final stop byte — truncate after the continuation byte
        var truncated = new byte[] { 0x1F, 0x80 };
        var pos = 0;
        var ex = Assert.Throws<HpackException>(() =>
            HpackDecoder.ReadInteger(truncated, ref pos, prefixBits: 5));
        Assert.Contains("truncated", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── End US-202 ────────────────────────────────────────────────────────────

    [Fact]
    public void Decode_Rfc7541_AppendixC2_FirstRequest()
    {
        // RFC 7541 Appendix C.2.1 - First Request WITHOUT Huffman encoding
        // :method: GET, :scheme: http, :path: /, :authority: www.example.com
        var encoded = new byte[]
        {
            0x82, // :method: GET (indexed, static index 2)
            0x86, // :scheme: http (indexed, static index 6)
            0x84, // :path: / (indexed, static index 4)
            0x41, 0x0f, // :authority (indexed name at static index 1, literal value, no Huffman)
            (byte)'w', (byte)'w', (byte)'w', (byte)'.', (byte)'e', (byte)'x', (byte)'a', (byte)'m',
            (byte)'p', (byte)'l', (byte)'e', (byte)'.', (byte)'c', (byte)'o', (byte)'m',
        };

        var decoder = new HpackDecoder();
        var decoded = decoder.Decode(encoded);

        Assert.Equal(4, decoded.Count);
        Assert.Equal(":method", decoded[0].Name);
        Assert.Equal("GET", decoded[0].Value);
        Assert.Equal(":scheme", decoded[1].Name);
        Assert.Equal("http", decoded[1].Value);
        Assert.Equal(":path", decoded[2].Name);
        Assert.Equal("/", decoded[2].Value);
        Assert.Equal(":authority", decoded[3].Name);
        Assert.Equal("www.example.com", decoded[3].Value);
    }

    [Fact]
    public void Decode_Rfc7541_AppendixC3_AllThreeRequests()
    {
        // RFC 7541 Appendix C.3 — Request Examples WITH Huffman Coding
        // A single HpackDecoder shares dynamic table state across all three requests,
        // exactly as it would on a persistent HTTP/2 connection.
        var decoder = new HpackDecoder();

        // ── C.3.1 First Request ──────────────────────────────────────────────────
        // :method: GET, :scheme: http, :path: /, :authority: www.example.com
        // Dynamic table after: [62] :authority: www.example.com
        var req1 = new byte[]
        {
            0x82,                   // indexed :method: GET  (static 2)
            0x86,                   // indexed :scheme: http (static 6)
            0x84,                   // indexed :path: /      (static 4)
            0x41,                   // literal incr. indexing, name = static[1] (:authority)
            0x8c,                   // H=1 (Huffman), length=12
            0xf1, 0xe3, 0xc2, 0xe5, 0xf2, 0x3a, 0x6b, 0xa0, 0xab, 0x90, 0xf4, 0xff, // "www.example.com"
        };

        var d1 = decoder.Decode(req1);
        Assert.Equal(4, d1.Count);
        Assert.Equal(":method",    d1[0].Name); Assert.Equal("GET",             d1[0].Value);
        Assert.Equal(":scheme",    d1[1].Name); Assert.Equal("http",            d1[1].Value);
        Assert.Equal(":path",      d1[2].Name); Assert.Equal("/",               d1[2].Value);
        Assert.Equal(":authority", d1[3].Name); Assert.Equal("www.example.com", d1[3].Value);

        // ── C.3.2 Second Request ─────────────────────────────────────────────────
        // :method: GET, :scheme: http, :path: /, :authority: www.example.com (dynamic),
        // cache-control: no-cache
        // Dynamic table after: [62] cache-control: no-cache, [63] :authority: www.example.com
        var req2 = new byte[]
        {
            0x82,                               // indexed :method: GET  (static 2)
            0x86,                               // indexed :scheme: http (static 6)
            0x84,                               // indexed :path: /      (static 4)
            0xbe,                               // indexed dynamic[62] → :authority: www.example.com
            0x58,                               // literal incr. indexing, name = static[24] (cache-control)
            0x86,                               // H=1, length=6
            0xa8, 0xeb, 0x10, 0x64, 0x9c, 0xbf // "no-cache"
        };

        var d2 = decoder.Decode(req2);
        Assert.Equal(5, d2.Count);
        Assert.Equal(":method",       d2[0].Name); Assert.Equal("GET",             d2[0].Value);
        Assert.Equal(":scheme",       d2[1].Name); Assert.Equal("http",            d2[1].Value);
        Assert.Equal(":path",         d2[2].Name); Assert.Equal("/",               d2[2].Value);
        Assert.Equal(":authority",    d2[3].Name); Assert.Equal("www.example.com", d2[3].Value);
        Assert.Equal("cache-control", d2[4].Name); Assert.Equal("no-cache",        d2[4].Value);

        // ── C.3.3 Third Request ──────────────────────────────────────────────────
        // :method: GET, :scheme: https, :path: /index.html,
        // :authority: www.example.com (dynamic[63]), custom-key: custom-value
        // Dynamic table after: [62] custom-key: custom-value, [63] cache-control: no-cache,
        //                      [64] :authority: www.example.com
        var req3 = new byte[]
        {
            0x82,                                                             // :method: GET
            0x87,                                                             // :scheme: https (static 7)
            0x85,                                                             // :path: /index.html (static 5)
            0xbf,                                                             // dynamic[63] → :authority: www.example.com
            0x40,                                                             // literal incr. indexing, new literal name
            0x88,                                                             // H=1, length=8
            0x25, 0xa8, 0x49, 0xe9, 0x5b, 0xa9, 0x7d, 0x7f,                 // "custom-key"
            0x89,                                                             // H=1, length=9
            0x25, 0xa8, 0x49, 0xe9, 0x5b, 0xb8, 0xe8, 0xb4, 0xbf            // "custom-value"
        };

        var d3 = decoder.Decode(req3);
        Assert.Equal(5, d3.Count);
        Assert.Equal(":method",    d3[0].Name); Assert.Equal("GET",             d3[0].Value);
        Assert.Equal(":scheme",    d3[1].Name); Assert.Equal("https",           d3[1].Value);
        Assert.Equal(":path",      d3[2].Name); Assert.Equal("/index.html",     d3[2].Value);
        Assert.Equal(":authority", d3[3].Name); Assert.Equal("www.example.com", d3[3].Value);
        Assert.Equal("custom-key", d3[4].Name); Assert.Equal("custom-value",    d3[4].Value);
    }
}
