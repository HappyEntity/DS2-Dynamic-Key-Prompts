namespace DynamicKeyPrompts.Formats;

/// DDS textures: DXT5 (BC3) encoder and a decoder for the formats the game uses.
public static class Dds
{
    public static byte[] EncodeDxt5(int w, int h, byte[] rgba)
    {
        var dds = new byte[128 + (w / 4) * (h / 4) * 16];
        WriteHeader(dds, w, h);
        int p = 128;
        Span<byte> block = stackalloc byte[64];
        for (int by = 0; by < h / 4; by++)
            for (int bx = 0; bx < w / 4; bx++)
            {
                for (int i = 0; i < 16; i++)
                {
                    int s = ((by * 4 + i / 4) * w + bx * 4 + i % 4) * 4;
                    block[i * 4] = rgba[s]; block[i * 4 + 1] = rgba[s + 1]; block[i * 4 + 2] = rgba[s + 2]; block[i * 4 + 3] = rgba[s + 3];
                }
                EncodeAlpha(block, dds.AsSpan(p, 8));
                EncodeColor(block, dds.AsSpan(p + 8, 8));
                p += 16;
            }
        return dds;
    }

    static void WriteHeader(byte[] d, int w, int h)
    {
        "DDS "u8.CopyTo(d);
        BitConverter.TryWriteBytes(d.AsSpan(4), 124);
        BitConverter.TryWriteBytes(d.AsSpan(8), 0x1 | 0x2 | 0x4 | 0x1000 | 0x80000); // caps|height|width|pixelformat|linearsize
        BitConverter.TryWriteBytes(d.AsSpan(12), h);
        BitConverter.TryWriteBytes(d.AsSpan(16), w);
        BitConverter.TryWriteBytes(d.AsSpan(20), w * h);
        BitConverter.TryWriteBytes(d.AsSpan(24), 1);   // depth (as in the game's files)
        BitConverter.TryWriteBytes(d.AsSpan(28), 1);   // mip count
        BitConverter.TryWriteBytes(d.AsSpan(76), 32);  // pixel format size
        BitConverter.TryWriteBytes(d.AsSpan(80), 0x4); // FOURCC
        "DXT5"u8.CopyTo(d.AsSpan(84));
        BitConverter.TryWriteBytes(d.AsSpan(108), 0x1000); // texture
    }

    static void EncodeAlpha(ReadOnlySpan<byte> px, Span<byte> o)
    {
        int lo = 255, hi = 0;
        for (int i = 0; i < 16; i++) { int a = px[i * 4 + 3]; lo = Math.Min(lo, a); hi = Math.Max(hi, a); }
        o[0] = (byte)hi; o[1] = (byte)lo;
        Span<int> pal = stackalloc int[8];
        pal[0] = hi; pal[1] = lo;
        for (int i = 1; i < 7; i++) pal[i + 1] = ((7 - i) * hi + i * lo) / 7;
        ulong bits = 0;
        if (hi != lo)
            for (int i = 0; i < 16; i++)
            {
                int a = px[i * 4 + 3], best = 0, bd = int.MaxValue;
                for (int k = 0; k < 8; k++) { int d = Math.Abs(pal[k] - a); if (d < bd) { bd = d; best = k; } }
                bits |= (ulong)best << (3 * i);
            }
        for (int i = 0; i < 6; i++) o[2 + i] = (byte)(bits >> (8 * i));
    }

    static void EncodeColor(ReadOnlySpan<byte> px, Span<byte> o)
    {
        // Endpoints along the principal axis of the (alpha-weighted) colours in the block.
        Span<float> mean = stackalloc float[3];
        float wsum = 0;
        for (int i = 0; i < 16; i++)
        {
            float w = px[i * 4 + 3] / 255f + 0.01f;
            for (int c = 0; c < 3; c++) mean[c] += px[i * 4 + c] * w;
            wsum += w;
        }
        for (int c = 0; c < 3; c++) mean[c] /= wsum;
        Span<float> cov = stackalloc float[6];
        for (int i = 0; i < 16; i++)
        {
            float w = px[i * 4 + 3] / 255f + 0.01f;
            float r = px[i * 4] - mean[0], g = px[i * 4 + 1] - mean[1], b = px[i * 4 + 2] - mean[2];
            cov[0] += r * r * w; cov[1] += r * g * w; cov[2] += r * b * w; cov[3] += g * g * w; cov[4] += g * b * w; cov[5] += b * b * w;
        }
        float ax = 1, ay = 1, az = 1;
        for (int it = 0; it < 8; it++)
        {
            float nx = cov[0] * ax + cov[1] * ay + cov[2] * az;
            float ny = cov[1] * ax + cov[3] * ay + cov[4] * az;
            float nz = cov[2] * ax + cov[4] * ay + cov[5] * az;
            float len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len < 1e-6f) break;
            ax = nx / len; ay = ny / len; az = nz / len;
        }
        float tmin = float.MaxValue, tmax = float.MinValue;
        for (int i = 0; i < 16; i++)
        {
            if (px[i * 4 + 3] == 0) continue;
            float t = (px[i * 4] - mean[0]) * ax + (px[i * 4 + 1] - mean[1]) * ay + (px[i * 4 + 2] - mean[2]) * az;
            tmin = Math.Min(tmin, t); tmax = Math.Max(tmax, t);
        }
        if (tmin > tmax) tmin = tmax = 0;
        ushort c0 = Pack(mean[0] + ax * tmax, mean[1] + ay * tmax, mean[2] + az * tmax);
        ushort c1 = Pack(mean[0] + ax * tmin, mean[1] + ay * tmin, mean[2] + az * tmin);
        if (c0 < c1) (c0, c1) = (c1, c0);
        if (c0 == c1)
        {
            // Force 4-colour mode (c0 > c1), otherwise index 3 would mean "transparent black".
            if (c0 > 0) c1 = (ushort)(c0 - 1); else c0 = 1;
        }

        Span<int> pal = stackalloc int[12];
        Unpack(c0, pal[..3]); Unpack(c1, pal.Slice(3, 3));
        for (int c = 0; c < 3; c++) { pal[6 + c] = (2 * pal[c] + pal[3 + c]) / 3; pal[9 + c] = (pal[c] + 2 * pal[3 + c]) / 3; }
        uint idx = 0;
        for (int i = 0; i < 16; i++)
        {
            int best = 0, bd = int.MaxValue;
            for (int k = 0; k < 4; k++)
            {
                int dr = px[i * 4] - pal[k * 3], dg = px[i * 4 + 1] - pal[k * 3 + 1], db = px[i * 4 + 2] - pal[k * 3 + 2];
                int d = dr * dr + dg * dg + db * db;
                if (d < bd) { bd = d; best = k; }
            }
            idx |= (uint)best << (2 * i);
        }
        BitConverter.TryWriteBytes(o, c0);
        BitConverter.TryWriteBytes(o[2..], c1);
        BitConverter.TryWriteBytes(o[4..], idx);
    }

    static ushort Pack(float r, float g, float b)
    {
        int R = Math.Clamp((int)MathF.Round(r * 31 / 255f), 0, 31);
        int G = Math.Clamp((int)MathF.Round(g * 63 / 255f), 0, 63);
        int B = Math.Clamp((int)MathF.Round(b * 31 / 255f), 0, 31);
        return (ushort)(R << 11 | G << 5 | B);
    }

    static void Unpack(ushort c, Span<int> o)
    {
        o[0] = (c >> 11) * 255 / 31; o[1] = ((c >> 5) & 63) * 255 / 63; o[2] = (c & 31) * 255 / 31;
    }

    // ------------------------------------------------------------------ decoding

    /// Decodes the first mip of a DXT1/DXT3/DXT5/BC4/BC5 or uncompressed 32-bit DDS to RGBA8.
    public static RgbaImage Decode(byte[] d)
    {
        int h = BitConverter.ToInt32(d, 12), w = BitConverter.ToInt32(d, 16);
        uint pfFlags = BitConverter.ToUInt32(d, 0x50);
        string four = System.Text.Encoding.ASCII.GetString(d, 0x54, 4);
        int off = 0x80;
        if (four == "DX10")
        {
            int dxgi = BitConverter.ToInt32(d, 0x80);
            off += 20;
            four = dxgi switch
            {
                71 or 72 => "DXT1", 74 or 75 => "DXT3", 77 or 78 => "DXT5", 80 => "ATI1", 83 => "ATI2",
                87 or 88 => "BGRA", 28 or 29 => "RGBA", _ => "DXGI" + dxgi,
            };
        }
        var o = new byte[w * h * 4];
        if ((pfFlags & 4) == 0 || four is "BGRA" or "RGBA")
        {
            bool bgra = (pfFlags & 4) == 0 ? BitConverter.ToUInt32(d, 0x5C) == 0x00FF0000 : four == "BGRA";
            for (int i = 0; i < w * h; i++)
            {
                int s = off + i * 4;
                o[i * 4] = d[s + (bgra ? 2 : 0)]; o[i * 4 + 1] = d[s + 1]; o[i * 4 + 2] = d[s + (bgra ? 0 : 2)]; o[i * 4 + 3] = d[s + 3];
            }
            return new RgbaImage(w, h, o);
        }
        int bs = four is "DXT1" or "ATI1" ? 8 : 16;
        int bx = (w + 3) / 4, by = (h + 3) / 4;
        var px = new byte[64];
        for (int y = 0; y < by; y++)
            for (int x = 0; x < bx; x++)
            {
                int s = off + (y * bx + x) * bs;
                switch (four)
                {
                    case "DXT1": DecodeColor(d, s, px, true); break;
                    case "DXT3":
                        DecodeColor(d, s + 8, px, false);
                        for (int i = 0; i < 16; i++) px[i * 4 + 3] = (byte)(((d[s + i / 2] >> ((i & 1) * 4)) & 0xF) * 17);
                        break;
                    case "DXT5": DecodeColor(d, s + 8, px, false); DecodeAlpha(d, s, px, 3); break;
                    case "ATI1":
                        DecodeAlpha(d, s, px, 0);
                        for (int i = 0; i < 16; i++) { px[i * 4 + 1] = px[i * 4 + 2] = px[i * 4]; px[i * 4 + 3] = 255; }
                        break;
                    case "ATI2":
                        DecodeAlpha(d, s, px, 0); DecodeAlpha(d, s + 8, px, 1);
                        for (int i = 0; i < 16; i++) { px[i * 4 + 2] = 0; px[i * 4 + 3] = 255; }
                        break;
                    default: throw new NotSupportedException("DDS format " + four);
                }
                for (int i = 0; i < 16; i++)
                {
                    int X = x * 4 + (i & 3), Y = y * 4 + (i >> 2);
                    if (X < w && Y < h) Array.Copy(px, i * 4, o, (Y * w + X) * 4, 4);
                }
            }
        return new RgbaImage(w, h, o);
    }

    static void DecodeColor(byte[] d, int s, byte[] px, bool dxt1)
    {
        ushort c0 = BitConverter.ToUInt16(d, s), c1 = BitConverter.ToUInt16(d, s + 2);
        Span<int> pal = stackalloc int[16];
        Unpack(c0, pal[..3]); Unpack(c1, pal.Slice(4, 3));
        pal[3] = pal[7] = 255;
        if (!dxt1 || c0 > c1)
            for (int c = 0; c < 3; c++) { pal[8 + c] = (2 * pal[c] + pal[4 + c]) / 3; pal[12 + c] = (pal[c] + 2 * pal[4 + c]) / 3; }
        else
            for (int c = 0; c < 3; c++) { pal[8 + c] = (pal[c] + pal[4 + c]) / 2; pal[12 + c] = 0; }
        pal[11] = 255;
        pal[15] = !dxt1 || c0 > c1 ? 255 : 0;
        uint idx = BitConverter.ToUInt32(d, s + 4);
        for (int i = 0; i < 16; i++)
        {
            int k = (int)((idx >> (i * 2)) & 3);
            for (int c = 0; c < 4; c++) px[i * 4 + c] = (byte)pal[k * 4 + c];
        }
    }

    static void DecodeAlpha(byte[] d, int s, byte[] px, int channel)
    {
        int a0 = d[s], a1 = d[s + 1];
        Span<int> a = stackalloc int[8];
        a[0] = a0; a[1] = a1;
        if (a0 > a1) for (int i = 1; i < 7; i++) a[i + 1] = ((7 - i) * a0 + i * a1) / 7;
        else { for (int i = 1; i < 5; i++) a[i + 1] = ((5 - i) * a0 + i * a1) / 5; a[6] = 0; a[7] = 255; }
        ulong bits = 0;
        for (int i = 0; i < 6; i++) bits |= (ulong)d[s + 2 + i] << (8 * i);
        for (int i = 0; i < 16; i++) px[i * 4 + channel] = (byte)a[(int)((bits >> (3 * i)) & 7)];
    }
}
