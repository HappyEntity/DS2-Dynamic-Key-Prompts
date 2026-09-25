using System.Numerics;
using System.Security.Cryptography;
using System.Text;

// DS2 SotFS BHD5/BDT archive reader. The .bhd header is "encrypted" with the RSA
// *public* key (raw RSA, per-block), the .bdt holds the file data, optionally with
// AES-128-ECB encrypted ranges described per entry.
class Ds2Archive
{
    public record Entry(uint Hash, int Size, long Offset, long AesKeyOffset);
    public readonly List<Entry> Entries = new();
    readonly byte[] bhd;
    readonly string bdtPath;

    public Ds2Archive(string bhdPath, string pemPath, string bdtPath)
    {
        this.bdtPath = bdtPath;
        bhd = RsaDecrypt(File.ReadAllBytes(bhdPath), File.ReadAllText(pemPath));
        if (Encoding.ASCII.GetString(bhd, 0, 4) != "BHD5") throw new Exception("BHD5 decrypt failed");
        int bucketCount = BitConverter.ToInt32(bhd, 0x10);
        int bucketsOff = BitConverter.ToInt32(bhd, 0x14);
        for (int i = 0; i < bucketCount; i++)
        {
            int n = BitConverter.ToInt32(bhd, bucketsOff + i * 8), off = BitConverter.ToInt32(bhd, bucketsOff + i * 8 + 4);
            for (int j = 0; j < n; j++)
            {
                int e = off + j * 0x20;
                Entries.Add(new Entry(BitConverter.ToUInt32(bhd, e), BitConverter.ToInt32(bhd, e + 4), BitConverter.ToInt64(bhd, e + 8), BitConverter.ToInt64(bhd, e + 0x18)));
            }
        }
    }

    public static byte[] RsaDecrypt(byte[] input, string pem)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        var p = rsa.ExportParameters(false);
        var n = new BigInteger(p.Modulus!, true, true);
        var e = new BigInteger(p.Exponent!, true, true);
        int ks = p.Modulus!.Length, os = ks - 1;
        var o = new MemoryStream();
        for (int i = 0; i + ks <= input.Length; i += ks)
        {
            var c = new BigInteger(input.AsSpan(i, ks), true, true);
            var m = BigInteger.ModPow(c, e, n).ToByteArray(true, true);
            var blk = new byte[os];
            Array.Copy(m, 0, blk, os - m.Length, m.Length);
            o.Write(blk);
        }
        return o.ToArray();
    }

    public static uint Hash(string path)
    {
        path = path.ToLowerInvariant().Replace('\\', '/');
        if (!path.StartsWith("/")) path = "/" + path;
        uint h = 0;
        foreach (char ch in path) h = h * 37 + ch;
        return h;
    }

    public byte[] Read(Entry en)
    {
        using var fs = File.OpenRead(bdtPath);
        fs.Position = en.Offset;
        var d = new byte[en.Size];
        fs.ReadExactly(d);
        if (en.AesKeyOffset != 0)
        {
            var key = bhd.AsSpan((int)en.AesKeyOffset, 16).ToArray();
            int rc = BitConverter.ToInt32(bhd, (int)(en.AesKeyOffset + 16));
            using var aes = Aes.Create(); aes.Key = key;
            for (int r = 0; r < rc; r++)
            {
                long s = BitConverter.ToInt64(bhd, (int)(en.AesKeyOffset + 20 + r * 16)), t = BitConverter.ToInt64(bhd, (int)(en.AesKeyOffset + 28 + r * 16));
                if (s == -1 || t <= s) continue;
                var dec = aes.DecryptEcb(d.AsSpan((int)s, (int)(t - s)), PaddingMode.None);
                dec.CopyTo(d, (int)s);
            }
        }
        return d;
    }

    public Entry? Find(string path) { uint h = Hash(path); return Entries.FirstOrDefault(x => x.Hash == h); }
}
