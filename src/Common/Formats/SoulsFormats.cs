using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace DynamicKeyPrompts.Formats;

/// DCX container with DFLT (zlib) compression, as used by DS2 SotFS.
public static class Dcx
{
    public static bool Is(ReadOnlySpan<byte> b) => b.Length > 4 && b[..4].SequenceEqual("DCX\0"u8);

    public static byte[] Decompress(byte[] b)
    {
        if (!Is(b)) return b;
        if (Encoding.ASCII.GetString(b, 0x28, 4) != "DFLT") throw new InvalidDataException("unsupported DCX compression");
        int uncompressed = BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(0x1C));
        int compressed = BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(0x20));
        int dca = b.AsSpan().IndexOf("DCA\0"u8);
        int start = dca + BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(dca + 4));
        using var z = new ZLibStream(new MemoryStream(b, start, compressed), CompressionMode.Decompress);
        var o = new MemoryStream(uncompressed);
        z.CopyTo(o);
        return o.ToArray();
    }

    /// Compresses with the same header layout the game ships (DFLT, level 9).
    public static byte[] Compress(byte[] data)
    {
        var comp = new MemoryStream();
        using (var z = new ZLibStream(comp, CompressionLevel.SmallestSize, true)) z.Write(data);
        var o = new MemoryStream();
        void BE(int v) { Span<byte> s = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(s, v); o.Write(s); }
        o.Write("DCX\0"u8); BE(0x10000); BE(0x18); BE(0x24); BE(0x24); BE(0x2C);
        o.Write("DCS\0"u8); BE(data.Length); BE((int)comp.Length);
        o.Write("DCP\0"u8); o.Write("DFLT"u8); BE(0x20); BE(0x09000000); BE(0); BE(0); BE(0); BE(0x00010100);
        o.Write("DCA\0"u8); BE(8);
        comp.Position = 0; comp.CopyTo(o);
        return o.ToArray();
    }
}

/// BND4 archive. Reads any BND4; writes the layout used by DS2's .fontbnd
/// (IDs + names + "compression" size field, 32-bit offsets, Shift-JIS names, no hash table).
public sealed class Bnd4
{
    public sealed record Entry(int Id, string Name, byte[] Data, byte Flags = 0x40);
    public List<Entry> Files { get; } = new();
    byte[] _header = []; // first 0x40 bytes, reused on write

    // Font BND names are plain ASCII; Latin-1 reads them exactly without the code-pages package.
    static readonly Encoding ShiftJis = Encoding.Latin1;

    public static Bnd4 Read(byte[] b)
    {
        if (Encoding.ASCII.GetString(b, 0, 4) != "BND4") throw new InvalidDataException("not BND4");
        var bnd = new Bnd4 { _header = b[..0x40] };
        int count = BitConverter.ToInt32(b, 0x0C);
        long entrySize = BitConverter.ToInt64(b, 0x20);
        bool unicode = b[0x30] != 0;
        byte fmt = Format(b);
        bool ids = (fmt & 2) != 0, names = (fmt & 0xC) != 0, longOff = (fmt & 0x10) != 0, comp = (fmt & 0x20) != 0;
        for (int i = 0; i < count; i++)
        {
            int q = 0x40 + (int)(i * entrySize);
            byte flags = b[q]; q += 8;
            long size = BitConverter.ToInt64(b, q); q += 8;
            if (comp) q += 8;
            long off = longOff ? BitConverter.ToInt64(b, q) : BitConverter.ToUInt32(b, q); q += longOff ? 8 : 4;
            int id = -1; if (ids) { id = BitConverter.ToInt32(b, q); q += 4; }
            string name = "";
            if (names)
            {
                int no = BitConverter.ToInt32(b, q);
                int e = no;
                if (unicode) { while (b[e] != 0 || b[e + 1] != 0) e += 2; name = Encoding.Unicode.GetString(b, no, e - no); }
                else { while (b[e] != 0) e++; name = ShiftJis.GetString(b, no, e - no); }
            }
            bnd.Files.Add(new Entry(id, name, b.AsSpan((int)off, (int)size).ToArray(), flags));
        }
        return bnd;
    }

    static byte Format(byte[] b)
    {
        bool bitBigEndian = b[0x09] != 0;
        byte raw = b[0x31];
        bool reverse = bitBigEndian || (raw & 1) != 0 && (raw & 0x80) == 0;
        if (reverse) return raw;
        byte r = 0;
        for (int i = 0; i < 8; i++) if ((raw & (1 << i)) != 0) r |= (byte)(0x80 >> i);
        return r;
    }

    public byte[] Write()
    {
        byte fmt = _header.Length == 0x40 ? Format(_header) : (byte)0x2A;
        if (fmt != 0x2A || _header[0x30] != 0 || _header[0x32] != 0)
            throw new NotSupportedException($"BND4 writer only supports the fontbnd layout (format {fmt:X2})");
        const int entrySize = 0x24, align = 0x200;
        int namesStart = 0x40 + Files.Count * entrySize;
        var names = new MemoryStream();
        var nameOffsets = new int[Files.Count];
        for (int i = 0; i < Files.Count; i++)
        {
            nameOffsets[i] = namesStart + (int)names.Length;
            names.Write(ShiftJis.GetBytes(Files[i].Name));
            names.WriteByte(0);
        }
        int headerEnd = namesStart + (int)names.Length;
        int dataStart = Align(headerEnd, align);
        var offsets = new int[Files.Count];
        int pos = dataStart;
        for (int i = 0; i < Files.Count; i++) { offsets[i] = pos; pos = Align(pos + Files[i].Data.Length, align); }

        var o = new byte[pos];
        _header.CopyTo(o, 0);
        BitConverter.TryWriteBytes(o.AsSpan(0x0C), Files.Count);
        BitConverter.TryWriteBytes(o.AsSpan(0x28), (long)headerEnd);
        for (int i = 0; i < Files.Count; i++)
        {
            var s = o.AsSpan(0x40 + i * entrySize);
            s[0] = Files[i].Flags;
            BitConverter.TryWriteBytes(s[4..], -1);
            BitConverter.TryWriteBytes(s[8..], (long)Files[i].Data.Length);
            BitConverter.TryWriteBytes(s[0x10..], (long)Files[i].Data.Length);
            BitConverter.TryWriteBytes(s[0x18..], offsets[i]);
            BitConverter.TryWriteBytes(s[0x1C..], Files[i].Id);
            BitConverter.TryWriteBytes(s[0x20..], nameOffsets[i]);
            Files[i].Data.CopyTo(o, offsets[i]);
        }
        names.ToArray().CopyTo(o, namesStart);
        return o;
    }

    static int Align(int v, int a) => (v + a - 1) / a * a;
}

/// TPF texture container (PC). Only what fonts need: read the textures, write one DXT5 texture.
public static class Tpf
{
    public sealed record Texture(string Name, byte Format, byte[] Dds);

    public static List<Texture> Read(byte[] b)
    {
        int count = BitConverter.ToInt32(b, 8);
        bool unicode = b[0x0E] == 1;
        var res = new List<Texture>();
        for (int i = 0; i < count; i++)
        {
            int p = 0x10 + i * 0x14;
            int off = BitConverter.ToInt32(b, p), size = BitConverter.ToInt32(b, p + 4), no = BitConverter.ToInt32(b, p + 0xC);
            int e = no;
            string name;
            if (unicode) { while (b[e] != 0 || b[e + 1] != 0) e += 2; name = Encoding.Unicode.GetString(b, no, e - no); }
            else { while (b[e] != 0) e++; name = Encoding.ASCII.GetString(b, no, e - no); }
            res.Add(new Texture(name, b[p + 8], b.AsSpan(off, size).ToArray()));
        }
        return res;
    }

    /// Same layout as the game's FeFont_*_000N.tpf: one texture, format 5 (DXT5), 1 mip.
    public static byte[] WriteSingle(string name, byte[] dds)
    {
        byte[] nameBytes = Encoding.ASCII.GetBytes(name + "\0");
        int nameOff = 0x24, dataOff = nameOff + nameBytes.Length;
        var o = new byte[dataOff + dds.Length];
        "TPF\0"u8.CopyTo(o);
        BitConverter.TryWriteBytes(o.AsSpan(4), dds.Length);
        BitConverter.TryWriteBytes(o.AsSpan(8), 1);
        o[0x0C] = 0; o[0x0D] = 3; o[0x0E] = 2; o[0x0F] = 0;
        BitConverter.TryWriteBytes(o.AsSpan(0x10), dataOff);
        BitConverter.TryWriteBytes(o.AsSpan(0x14), dds.Length);
        o[0x18] = 5; o[0x19] = 0; o[0x1A] = 1; o[0x1B] = 0;
        BitConverter.TryWriteBytes(o.AsSpan(0x1C), nameOff);
        BitConverter.TryWriteBytes(o.AsSpan(0x20), 0);
        nameBytes.CopyTo(o, nameOff);
        dds.CopyTo(o, dataOff);
        return o;
    }
}
