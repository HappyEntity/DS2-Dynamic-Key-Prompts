namespace DynamicKeyPrompts.Formats;

/// DS2 CCM2 font glyph table.
///   0x00 u32 version 0x20000   0x04 i32 file size
///   0x08 i16 line height ("full width")   0x0A i16 tex width   0x0C i16 tex height
///   0x0E i16 region count      0x10 i16 glyph count   0x12 i16 0
///   0x14 i32 regions offset (0x20)   0x18 i32 glyphs offset
///   0x1C u8 unk   0x1D u8 unk   0x1E u8 texture count   0x1F u8 0
/// Regions: { i16 x1, y1, x2, y2 }.  Glyphs (24 bytes, sorted by code — looked up by binary search):
///   { i32 code; i32 region offset; i16 texture; i16 pre-space; i16 width; i16 advance; i32 0; i32 0 }
public sealed class Ccm
{
    public record struct Region(short X1, short Y1, short X2, short Y2);
    public record struct Glyph(int Code, Region Region, short Texture, short PreSpace, short Width, short Advance);

    byte[] _header = [];
    public short LineHeight => BitConverter.ToInt16(_header, 0x08);
    public byte TextureCount { get => _header[0x1E]; set => _header[0x1E] = value; }
    public List<Glyph> Glyphs { get; } = new();

    public static Ccm Read(byte[] b)
    {
        if (BitConverter.ToInt32(b, 0) != 0x20000) throw new InvalidDataException("not a DS2 CCM2 font");
        var ccm = new Ccm { _header = b[..0x20] };
        int glyphCount = BitConverter.ToInt16(b, 0x10);
        int glyphOff = BitConverter.ToInt32(b, 0x18);
        for (int i = 0; i < glyphCount; i++)
        {
            int p = glyphOff + i * 24;
            int reg = BitConverter.ToInt32(b, p + 4);
            var r = new Region(BitConverter.ToInt16(b, reg), BitConverter.ToInt16(b, reg + 2), BitConverter.ToInt16(b, reg + 4), BitConverter.ToInt16(b, reg + 6));
            ccm.Glyphs.Add(new Glyph(BitConverter.ToInt32(b, p), r, BitConverter.ToInt16(b, p + 8),
                BitConverter.ToInt16(b, p + 10), BitConverter.ToInt16(b, p + 12), BitConverter.ToInt16(b, p + 14)));
        }
        return ccm;
    }

    public byte[] Write()
    {
        var glyphs = Glyphs.OrderBy(g => (uint)g.Code).ToList();
        // One region per glyph (the originals share none that matter; duplicates are harmless).
        int regionsOff = 0x20, glyphOff = regionsOff + glyphs.Count * 8;
        int size = glyphOff + glyphs.Count * 24;
        var o = new byte[size];
        _header.CopyTo(o, 0);
        BitConverter.TryWriteBytes(o.AsSpan(0x04), size);
        BitConverter.TryWriteBytes(o.AsSpan(0x0E), (short)glyphs.Count);
        BitConverter.TryWriteBytes(o.AsSpan(0x10), (short)glyphs.Count);
        BitConverter.TryWriteBytes(o.AsSpan(0x14), regionsOff);
        BitConverter.TryWriteBytes(o.AsSpan(0x18), glyphOff);
        for (int i = 0; i < glyphs.Count; i++)
        {
            var g = glyphs[i];
            int r = regionsOff + i * 8, p = glyphOff + i * 24;
            BitConverter.TryWriteBytes(o.AsSpan(r), g.Region.X1);
            BitConverter.TryWriteBytes(o.AsSpan(r + 2), g.Region.Y1);
            BitConverter.TryWriteBytes(o.AsSpan(r + 4), g.Region.X2);
            BitConverter.TryWriteBytes(o.AsSpan(r + 6), g.Region.Y2);
            BitConverter.TryWriteBytes(o.AsSpan(p), g.Code);
            BitConverter.TryWriteBytes(o.AsSpan(p + 4), r);
            BitConverter.TryWriteBytes(o.AsSpan(p + 8), g.Texture);
            BitConverter.TryWriteBytes(o.AsSpan(p + 10), g.PreSpace);
            BitConverter.TryWriteBytes(o.AsSpan(p + 12), g.Width);
            BitConverter.TryWriteBytes(o.AsSpan(p + 14), g.Advance);
        }
        return o;
    }
}
