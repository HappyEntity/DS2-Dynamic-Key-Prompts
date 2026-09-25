using DynamicKeyPrompts.Formats;

namespace DynamicKeyPrompts.Icons;

/// Adds keycap glyphs to a DS2 .fontbnd.dcx without touching any existing glyph or texture:
/// the keycaps go onto new 256×256 DXT5 pages appended after the font's own pages.
public static class FontPatcher
{
    const int PageSize = 256, Padding = 2;

    public sealed record Result(byte[] FontBndDcx, int Glyphs, int Pages, string Log);

    /// Icons come from the sheet (scaled to the font's line height); icons missing from it are
    /// rendered with the active theme.
    public static Result Patch(byte[] fontBndDcx, IconSheet? sheet = null)
    {
        var bnd = Bnd4.Read(Dcx.Decompress(fontBndDcx));
        var ccmEntry = bnd.Files.Single(f => f.Name.EndsWith(".ccm", StringComparison.OrdinalIgnoreCase));
        var ccm = Ccm.Read(ccmEntry.Data);
        var tpfs = bnd.Files.Where(f => f.Name.EndsWith(".tpf", StringComparison.OrdinalIgnoreCase)).ToList();
        if (tpfs.Count != ccm.TextureCount)
            throw new InvalidDataException($"font has {tpfs.Count} texture files but CCM says {ccm.TextureCount}");

        string baseName = Path.GetFileNameWithoutExtension(ccmEntry.Name); // FeFont_Small
        int lineHeight = ccm.LineHeight;

        var images = KeycapSet.All()
            .Select(k => (k.Code, Image: sheet?.Get(k.Name, lineHeight) ?? KeycapRenderer.Render(k.Spec, lineHeight)))
            .Where(k => k.Image.Width <= PageSize - 2 * Padding)
            .OrderByDescending(k => k.Image.Width) // simple shelf packing works better widest-first
            .ToList();

        // Shelf-pack onto pages.
        var pages = new List<byte[]>();
        byte[]? page = null;
        int px = 0, py = 0;
        int firstNewTex = ccm.TextureCount;
        ccm.Glyphs.RemoveAll(g => g.Code >= KeycapSet.Base && g.Code <= KeycapSet.Base + 0xFF);
        foreach (var (code, img) in images)
        {
            if (page == null || px + img.Width + Padding > PageSize)
            {
                px = Padding;
                py = page == null ? Padding : py + lineHeight + Padding;
            }
            if (page == null || py + lineHeight + Padding > PageSize)
            {
                page = new byte[PageSize * PageSize * 4];
                pages.Add(page);
                px = Padding; py = Padding;
            }
            Blit(page, img, px, py);
            short tex = (short)(firstNewTex + pages.Count - 1);
            var region = new Ccm.Region((short)px, (short)py, (short)(px + img.Width), (short)(py + img.Height));
            ccm.Glyphs.Add(new Ccm.Glyph(KeycapSet.Base + code, region, tex, 0, (short)img.Width, (short)(img.Width + 1)));
            px += img.Width + Padding;
        }

        // New texture files, named and numbered like the font's own pages.
        int nextId = bnd.Files.Max(f => f.Id) + 1;
        for (int i = 0; i < pages.Count; i++)
        {
            FillTransparent(pages[i]);
            string texName = $"{baseName}_{firstNewTex + i:0000}";
            byte[] dds = Dds.EncodeDxt5(PageSize, PageSize, pages[i]);
            bnd.Files.Add(new Bnd4.Entry(nextId + i, texName + ".tpf", Tpf.WriteSingle(texName, dds)));
        }
        ccm.TextureCount = (byte)(firstNewTex + pages.Count);

        int ccmIndex = bnd.Files.IndexOf(ccmEntry);
        bnd.Files[ccmIndex] = ccmEntry with { Data = ccm.Write() };

        return new Result(Dcx.Compress(bnd.Write()), images.Count, pages.Count,
            $"{baseName}: line height {lineHeight}, {images.Count} keycaps on {pages.Count} new page(s) from texture {firstNewTex}");
    }

    /// For inspection: the new pages as RGBA (decoded from the patched font).
    public static IEnumerable<(string Name, byte[] Dds)> NewPages(byte[] patchedFontBndDcx, int originalTextureCount)
    {
        var bnd = Bnd4.Read(Dcx.Decompress(patchedFontBndDcx));
        foreach (var f in bnd.Files.Where(f => f.Name.EndsWith(".tpf", StringComparison.OrdinalIgnoreCase)).Skip(originalTextureCount))
            foreach (var t in Tpf.Read(f.Data))
                yield return (t.Name, t.Dds);
    }

    static void Blit(byte[] page, RgbaImage img, int x0, int y0)
    {
        for (int y = 0; y < img.Height; y++)
            Array.Copy(img.Rgba, y * img.Width * 4, page, ((y0 + y) * PageSize + x0) * 4, img.Width * 4);
    }

    /// Transparent texels get the outline colour so filtering never bleeds a light halo.
    static void FillTransparent(byte[] page)
    {
        for (int i = 0; i < page.Length; i += 4)
            if (page[i + 3] == 0) { page[i] = 38; page[i + 1] = 38; page[i + 2] = 40; }
    }
}
