using System.Buffers.Binary;
using System.IO.Compression;

namespace DynamicKeyPrompts.Formats;

/// Minimal PNG reader/writer (no System.Drawing in NativeAOT). Reads non-interlaced 8-bit
/// greyscale, grey+alpha, RGB, RGBA and palette images, plus 16-bit ones (downsampled to 8 bits).
public static class Png
{
    public static byte[] Write(int w, int h, byte[] rgba)
    {
        var o = new MemoryStream();
        o.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, w);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), h);
        ihdr[8] = 8; ihdr[9] = 6;
        Chunk(o, "IHDR", ihdr);
        var raw = new MemoryStream();
        for (int y = 0; y < h; y++) { raw.WriteByte(0); raw.Write(rgba, y * w * 4, w * 4); }
        var comp = new MemoryStream();
        using (var z = new ZLibStream(comp, CompressionLevel.Optimal, true)) { raw.Position = 0; raw.CopyTo(z); }
        Chunk(o, "IDAT", comp.ToArray());
        Chunk(o, "IEND", []);
        return o.ToArray();
    }

    public static (int W, int H, byte[] Rgba) Read(byte[] b)
    {
        if (b.Length < 8 || b[0] != 0x89 || b[1] != 'P') throw new InvalidDataException("not a PNG");
        int w = 0, h = 0, depth = 0, color = 0;
        byte[]? palette = null, trns = null;
        var idat = new MemoryStream();
        for (int p = 8; p + 8 <= b.Length;)
        {
            int len = BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(p));
            string type = System.Text.Encoding.ASCII.GetString(b, p + 4, 4);
            var data = b.AsSpan(p + 8, len);
            switch (type)
            {
                case "IHDR":
                    w = BinaryPrimitives.ReadInt32BigEndian(data); h = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                    depth = data[8]; color = data[9];
                    if (data[12] != 0) throw new NotSupportedException("interlaced PNG");
                    if (depth != 8 && depth != 16) throw new NotSupportedException($"PNG bit depth {depth}");
                    break;
                case "PLTE": palette = data.ToArray(); break;
                case "tRNS": trns = data.ToArray(); break;
                case "IDAT": idat.Write(data); break;
            }
            p += 12 + len;
        }
        int channels = color switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => throw new NotSupportedException($"PNG colour type {color}") };
        int bpp = channels * depth / 8, stride = w * bpp;
        idat.Position = 0;
        var raw = new MemoryStream();
        using (var z = new ZLibStream(idat, CompressionMode.Decompress)) z.CopyTo(raw);
        byte[] src = raw.ToArray();
        var cur = new byte[stride]; var prev = new byte[stride];
        var o = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            int f = src[y * (stride + 1)];
            Array.Copy(src, y * (stride + 1) + 1, cur, 0, stride);
            for (int i = 0; i < stride; i++)
            {
                int a = i >= bpp ? cur[i - bpp] : 0, up = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                cur[i] = f switch
                {
                    1 => (byte)(cur[i] + a),
                    2 => (byte)(cur[i] + up),
                    3 => (byte)(cur[i] + (a + up) / 2),
                    4 => (byte)(cur[i] + Paeth(a, up, c)),
                    _ => cur[i],
                };
            }
            for (int x = 0; x < w; x++)
            {
                int s = x * bpp, d = (y * w + x) * 4, step = depth / 8;
                byte S(int k) => cur[s + k * step]; // high byte of 16-bit samples
                switch (color)
                {
                    case 0: o[d] = o[d + 1] = o[d + 2] = S(0); o[d + 3] = 255; break;
                    case 2: o[d] = S(0); o[d + 1] = S(1); o[d + 2] = S(2); o[d + 3] = 255; break;
                    case 4: o[d] = o[d + 1] = o[d + 2] = S(0); o[d + 3] = S(1); break;
                    case 6: o[d] = S(0); o[d + 1] = S(1); o[d + 2] = S(2); o[d + 3] = S(3); break;
                    case 3:
                        int idx = cur[x];
                        o[d] = palette![idx * 3]; o[d + 1] = palette[idx * 3 + 1]; o[d + 2] = palette[idx * 3 + 2];
                        o[d + 3] = trns != null && idx < trns.Length ? trns[idx] : (byte)255;
                        break;
                }
            }
            (cur, prev) = (prev, cur);
        }
        return (w, h, o);
    }

    static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    static void Chunk(Stream s, string type, byte[] data)
    {
        Span<byte> be = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(be, data.Length); s.Write(be);
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(t); s.Write(data);
        uint c = 0xFFFFFFFF;
        foreach (var x in t) c = CrcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        foreach (var x in data) c = CrcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        BinaryPrimitives.WriteUInt32BigEndian(be, c ^ 0xFFFFFFFF); s.Write(be);
    }

    /// Scales an image to exactly `height` pixels tall, keeping the aspect ratio (area filter).
    public static RgbaImage ScaleToHeight(int w, int h, byte[] rgba, int height)
    {
        int nw = Math.Max(1, (int)Math.Round((double)w * height / h));
        var o = new byte[nw * height * 4];
        double sx = (double)w / nw, sy = (double)h / height;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < nw; x++)
            {
                double x0 = x * sx, x1 = x0 + sx, y0 = y * sy, y1 = y0 + sy;
                double r = 0, g = 0, bl = 0, a = 0, area = 0;
                for (int yy = (int)y0; yy < Math.Min(h, (int)Math.Ceiling(y1)); yy++)
                    for (int xx = (int)x0; xx < Math.Min(w, (int)Math.Ceiling(x1)); xx++)
                    {
                        double cw = Math.Min(xx + 1, x1) - Math.Max(xx, x0), ch = Math.Min(yy + 1, y1) - Math.Max(yy, y0);
                        double k = cw * ch; if (k <= 0) continue;
                        int i = (yy * w + xx) * 4; double al = rgba[i + 3] / 255.0;
                        r += rgba[i] * al * k; g += rgba[i + 1] * al * k; bl += rgba[i + 2] * al * k; a += al * k; area += k;
                    }
                int d = (y * nw + x) * 4;
                if (a > 1e-6) { o[d] = (byte)Math.Round(r / a); o[d + 1] = (byte)Math.Round(g / a); o[d + 2] = (byte)Math.Round(bl / a); }
                o[d + 3] = (byte)Math.Round(255 * a / Math.Max(area, 1e-6));
            }
        return new RgbaImage(nw, height, o);
    }
}
