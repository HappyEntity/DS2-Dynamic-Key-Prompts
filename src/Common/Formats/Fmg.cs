using System.Text;

namespace DynamicKeyPrompts.Formats;

/// FMG message file (UTF-16 strings by id), as used by DS2 SotFS.
public static class Fmg
{
    /// All entries in file order; text is null for ids without a string.
    public static List<(int Id, string? Text)> Read(byte[] b)
    {
        byte version = b[2];
        bool wide = version == 2 || BitConverter.ToInt32(b, 0x14) == 0xFF;
        int groupCount = BitConverter.ToInt32(b, 0x0C);
        int p = wide ? 0x18 : 0x14;
        long strings = wide ? BitConverter.ToInt64(b, p) : BitConverter.ToInt32(b, p);
        p += wide ? 16 : 8;
        var res = new List<(int, string?)>();
        for (int g = 0; g < groupCount; g++)
        {
            int index = BitConverter.ToInt32(b, p), first = BitConverter.ToInt32(b, p + 4), last = BitConverter.ToInt32(b, p + 8);
            p += wide ? 16 : 12;
            for (int id = first; id <= last; id++)
            {
                int k = index + id - first;
                long so = wide ? BitConverter.ToInt64(b, (int)strings + k * 8) : BitConverter.ToInt32(b, (int)strings + k * 4);
                res.Add((id, so == 0 ? null : ReadString(b, (int)so)));
            }
        }
        return res;
    }

    static string ReadString(byte[] b, int o)
    {
        int e = o;
        while (b[e] != 0 || b[e + 1] != 0) e += 2;
        return Encoding.Unicode.GetString(b, o, e - o);
    }
}
