// Ds2Tool — command-line helper for Dark Souls II SotFS file formats, used while developing
// Dynamic Key Prompts. Run without arguments for the list of commands.
using System.Diagnostics;
using System.Text;
using DynamicKeyPrompts.Formats;
using DynamicKeyPrompts.Icons;

var commands = new Dictionary<string, (string Usage, Action<string[]> Run)>
{
    ["undcx"] = ("<in.dcx> <out>                       decompress a DCX file", a =>
        File.WriteAllBytes(a[2], Dcx.Decompress(File.ReadAllBytes(a[1])))),
    ["bnd"] = ("<file> [out-dir]                      list / extract a BND4 archive (DCX ok)", Bnd),
    ["tpf"] = ("<file> [out-dir]                      list / extract the textures of a TPF", TpfCmd),
    ["dds2png"] = ("<file.dds>...                        convert DDS textures to PNG next to them", Dds2Png),
    ["ccm"] = ("<file.ccm> [all]                      list font glyphs (special symbols only unless 'all')", CcmCmd),
    ["fmg"] = ("<file.fmg>...                        dump message files (pad icons shown as {U+XXXX})", FmgCmd),
    ["arc"] = ("<game-dir> <GameData|...> <out-dir> <path|@list>...  extract files from the game archives", Arc),
    ["wstrings"] = ("<file>                             list UTF-16 strings with file offsets", WStrings),
    ["crop"] = ("<file.dds> <out.png> <scale> x1,y1,x2,y2...  crop texture regions side by side", Crop),
    ["fontpatch"] = ("<in.fontbnd.dcx> <out.fontbnd.dcx> [preview-dir]  add the key icons to a game font", FontPatch),
    ["sheet"] = ("<theme> <out-dir>                    write a built-in theme's icon sheet (.png + .txt)", Sheet),
    ["keycap-preview"] = ("<out.png> [line-height=27] [scale=2]  sample icons of every theme", KeycapPreview),
};

if (args.Length == 0 || !commands.TryGetValue(args[0], out var command))
{
    Console.WriteLine("Ds2Tool <command> ...");
    foreach (var (name, (usage, _)) in commands) Console.WriteLine($"  {name,-15} {usage}");
    return args.Length == 0 ? 0 : 1;
}
command.Run(args);
return 0;

static void Bnd(string[] a)
{
    foreach (var f in Bnd4.Read(Dcx.Decompress(File.ReadAllBytes(a[1]))).Files)
    {
        Console.WriteLine($"{f.Id,8} {f.Data.Length,10} {f.Name}");
        if (a.Length <= 2) continue;
        var path = Path.Combine(a[2], f.Name.Replace(':', '_').TrimStart('\\', '/').Replace('\\', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, f.Data);
    }
}

static void TpfCmd(string[] a)
{
    foreach (var t in Tpf.Read(Dcx.Decompress(File.ReadAllBytes(a[1]))))
    {
        Console.WriteLine($"{t.Format,4} {t.Dds.Length,10} {t.Name}");
        if (a.Length <= 2) continue;
        Directory.CreateDirectory(a[2]);
        File.WriteAllBytes(Path.Combine(a[2], t.Name + ".dds"), t.Dds);
    }
}

static void Dds2Png(string[] a)
{
    foreach (var f in a.Skip(1))
    {
        var img = Dds.Decode(File.ReadAllBytes(f));
        File.WriteAllBytes(Path.ChangeExtension(f, ".png"), Png.Write(img.Width, img.Height, img.Rgba));
        Console.WriteLine($"{img.Width}x{img.Height} {f}");
    }
}

static void CcmCmd(string[] a)
{
    var ccm = Ccm.Read(File.ReadAllBytes(a[1]));
    bool all = a.Length > 2 && a[2] == "all";
    Console.WriteLine($"line height {ccm.LineHeight}, {ccm.Glyphs.Count} glyphs, {ccm.TextureCount} textures");
    foreach (var g in ccm.Glyphs)
    {
        bool special = !(g.Code < 0x7F || g.Code is >= 0xA0 and < 0x180 || g.Code is >= 0x400 and < 0x460);
        if (all || special)
            Console.WriteLine($"U+{g.Code:X4} tex{g.Texture} [{g.Region.X1},{g.Region.Y1}-{g.Region.X2},{g.Region.Y2}] pre{g.PreSpace} w{g.Width} adv{g.Advance}");
    }
}

static void FmgCmd(string[] a)
{
    foreach (var f in a.Skip(1))
    {
        Console.WriteLine($"### {Path.GetFileName(f)}");
        foreach (var (id, text) in Fmg.Read(File.ReadAllBytes(f)))
        {
            if (text == null) continue;
            var sb = new StringBuilder();
            foreach (char ch in text)
                sb.Append(ch is >= ' ' and < '㐀' or >= '' and < '豈' ? $"{{U+{(int)ch:X4}}}" : ch == '\n' ? "\\n" : ch.ToString());
            Console.WriteLine($"{id}\t{sb}");
        }
    }
}

static void Arc(string[] a)
{
    string gameDir = a[1], name = a[2], outDir = a[3];
    string key = name == "GameData" ? "GameDataKeyCode.pem" : name + "KeyCode.pem";
    var arc = new Ds2Archive(Path.Combine(gameDir, name + "Ebl.bhd"), Path.Combine(gameDir, key), Path.Combine(gameDir, name + "Ebl.bdt"));
    Console.WriteLine($"{arc.Entries.Count} entries");
    var paths = a.Skip(4).SelectMany(p => p.StartsWith('@') ? File.ReadAllLines(p[1..]) : [p]).Where(p => p.Length > 0).Distinct();
    int found = 0;
    foreach (var p in paths)
    {
        var entry = arc.Find(p);
        if (entry == null) continue;
        found++;
        var outPath = Path.Combine(outDir, p.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllBytes(outPath, arc.Read(entry));
        Console.WriteLine($"  {entry.Size,10} {p}");
    }
    Console.WriteLine($"found {found}");
}

static void WStrings(string[] a)
{
    var b = File.ReadAllBytes(a[1]);
    var sb = new StringBuilder();
    for (int align = 0; align < 2; align++)
    {
        int start = -1;
        for (int i = align; i + 1 < b.Length; i += 2)
        {
            if (b[i + 1] == 0 && b[i] >= 0x20 && b[i] < 0x7F) { if (start < 0) start = i; continue; }
            if (start >= 0 && (i - start) / 2 >= 4) sb.Append($"{start:X}\t{Encoding.Unicode.GetString(b, start, i - start)}\n");
            start = -1;
        }
    }
    Console.Out.Write(sb.ToString());
}

static void Crop(string[] a)
{
    var img = Dds.Decode(File.ReadAllBytes(a[1]));
    int scale = int.Parse(a[3]);
    var rects = a.Skip(4).Select(r => r.Split(',').Select(int.Parse).ToArray()).ToList();
    int ow = rects.Sum(r => r[2] - r[0] + 2) * scale, oh = rects.Max(r => r[3] - r[1]) * scale;
    var o = new byte[ow * oh * 4];
    for (int i = 0; i < o.Length; i += 4) { o[i] = o[i + 1] = o[i + 2] = 90; o[i + 3] = 255; }
    int ox = 0;
    foreach (var r in rects)
    {
        for (int y = 0; y < (r[3] - r[1]) * scale; y++)
            for (int x = 0; x < (r[2] - r[0]) * scale; x++)
            {
                int s = ((r[1] + y / scale) * img.Width + r[0] + x / scale) * 4, d = (y * ow + ox + x) * 4;
                Blend(o, d, img.Rgba, s);
            }
        ox += (r[2] - r[0] + 2) * scale;
    }
    File.WriteAllBytes(a[2], Png.Write(ow, oh, o));
}

static void FontPatch(string[] a)
{
    byte[] src = File.ReadAllBytes(a[1]);
    var bnd = Bnd4.Read(Dcx.Decompress(src));
    int originalTextures = Ccm.Read(bnd.Files.First(f => f.Name.EndsWith(".ccm")).Data).TextureCount;
    var sw = Stopwatch.StartNew();
    var result = FontPatcher.Patch(src);
    Console.WriteLine($"{result.Log} in {sw.ElapsedMilliseconds} ms, {src.Length} -> {result.FontBndDcx.Length} bytes");
    File.WriteAllBytes(a[2], result.FontBndDcx);
    if (a.Length <= 3) return;
    Directory.CreateDirectory(a[3]);
    foreach (var (name, dds) in FontPatcher.NewPages(result.FontBndDcx, originalTextures))
    {
        var page = Dds.Decode(dds);
        var preview = OnBackground(page, 3, (70, 70, 70));
        File.WriteAllBytes(Path.Combine(a[3], name + ".png"), Png.Write(preview.Width, preview.Height, preview.Rgba));
    }
}

static void Sheet(string[] a)
{
    var (png, layout) = IconSheet.Generate(KeycapRenderer.ThemeByName(a[1]));
    Directory.CreateDirectory(a[2]);
    File.WriteAllBytes(Path.Combine(a[2], a[1] + ".png"), png);
    File.WriteAllText(Path.Combine(a[2], a[1] + ".txt"), layout);
    Console.WriteLine($"{a[1]}: {png.Length} bytes, {IconSheet.Load(png, layout).Icons.Count} icons");
}

static void KeycapPreview(string[] a)
{
    int lineHeight = a.Length > 2 ? int.Parse(a[2]) : 27, scale = a.Length > 3 ? int.Parse(a[3]) : 2;
    KeycapSpec[] sample =
    [
        new KeycapSpec.Key("↑"), new KeycapSpec.Key("←"), new KeycapSpec.Key("↓"), new KeycapSpec.Key("→"),
        new KeycapSpec.Key("Enter"), new KeycapSpec.Key("Q"), new KeycapSpec.Key("E"), new KeycapSpec.Key("Shift"),
        new KeycapSpec.Key("Space"), new KeycapSpec.Mouse(MouseButton.Left), new KeycapSpec.Mouse(MouseButton.Right),
        new KeycapSpec.Mouse(MouseButton.Middle), new KeycapSpec.Mouse(MouseButton.Middle, WheelDirection: 1),
        new KeycapSpec.MouseMove(),
    ];
    var rows = KeycapRenderer.Themes.Select(theme =>
    {
        KeycapRenderer.T = theme;
        return sample.Select(s => KeycapRenderer.Render(s, lineHeight)).ToList();
    }).ToList();

    const int gap = 6, pad = 10;
    int w = rows.Max(r => r.Sum(i => i.Width + gap)) + 2 * pad, h = rows.Count * (lineHeight + gap) + 2 * pad;
    var o = new byte[w * h * 4];
    for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {   // dark leather-like gradient, like the menu's help bar
            int i = (y * w + x) * 4; float t = (float)y / h;
            o[i] = (byte)(34 - 10 * t); o[i + 1] = (byte)(29 - 9 * t); o[i + 2] = (byte)(25 - 8 * t); o[i + 3] = 255;
        }
    int yy = pad;
    foreach (var row in rows)
    {
        int xx = pad;
        foreach (var img in row)
        {
            for (int y = 0; y < img.Height; y++)
                for (int x = 0; x < img.Width; x++)
                    Blend(o, ((yy + y) * w + xx + x) * 4, img.Rgba, (y * img.Width + x) * 4);
            xx += img.Width + gap;
        }
        yy += lineHeight + gap;
    }
    var big = Scale(new RgbaImage(w, h, o), scale);
    File.WriteAllBytes(a[1], Png.Write(big.Width, big.Height, big.Rgba));
}

// ---------------------------------------------------------------- image helpers

static void Blend(byte[] dst, int d, byte[] src, int s)
{
    int alpha = src[s + 3];
    for (int c = 0; c < 3; c++) dst[d + c] = (byte)((src[s + c] * alpha + dst[d + c] * (255 - alpha)) / 255);
}

static RgbaImage OnBackground(RgbaImage img, int scale, (byte R, byte G, byte B) bg)
{
    var o = new byte[img.Width * img.Height * 4];
    for (int i = 0; i < o.Length; i += 4) { o[i] = bg.R; o[i + 1] = bg.G; o[i + 2] = bg.B; o[i + 3] = 255; Blend(o, i, img.Rgba, i); }
    return Scale(new RgbaImage(img.Width, img.Height, o), scale);
}

static RgbaImage Scale(RgbaImage img, int scale)
{
    int w = img.Width * scale, h = img.Height * scale;
    var o = new byte[w * h * 4];
    for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            Array.Copy(img.Rgba, ((y / scale) * img.Width + x / scale) * 4, o, (y * w + x) * 4, 4);
    return new RgbaImage(w, h, o);
}
