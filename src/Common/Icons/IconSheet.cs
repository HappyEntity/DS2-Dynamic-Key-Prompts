using System.Globalization;
using System.Text;

using DynamicKeyPrompts.Formats;

namespace DynamicKeyPrompts.Icons;

/// A theme's icons as one editable sprite sheet: &lt;theme&gt;.png plus &lt;theme&gt;.txt with one line per
/// icon, "Name X Y Width Height" (pixels in the PNG). Icons are drawn large (CellHeight tall)
/// and scaled down to the font's line height when the font is built.
public sealed class IconSheet
{
    public const int CellHeight = 96;
    const int SheetWidth = 1024, Padding = 8;

    public readonly Dictionary<string, RgbaImage> Icons = new(StringComparer.OrdinalIgnoreCase);

    /// Renders a built-in theme to (png, layout text).
    public static (byte[] Png, string Layout) Generate(KeycapRenderer.Theme theme)
    {
        var prev = KeycapRenderer.T;
        KeycapRenderer.T = theme;
        try
        {
            var items = KeycapSet.All()
                .Select(e => (e.Name, Image: KeycapRenderer.Render(e.Spec, CellHeight)))
                .ToList();

            // Shelf packing in the set's natural order: mouse first, then letters, digits, keys.
            var placed = new List<(string Name, int X, int Y, RgbaImage Image)>();
            int x = Padding, y = Padding;
            foreach (var (name, img) in items)
            {
                if (x + img.Width + Padding > SheetWidth) { x = Padding; y += CellHeight + Padding; }
                placed.Add((name, x, y, img));
                x += img.Width + Padding;
            }
            int height = y + CellHeight + Padding;
            var rgba = new byte[SheetWidth * height * 4];
            var layout = new StringBuilder();
            layout.AppendLine($"# {theme.Name} icon sheet for Dynamic Key Prompts. One line per icon: Name X Y Width Height.");
            layout.AppendLine("# Edit the PNG freely; keep each icon inside its rectangle (or change the rectangle here).");
            layout.AppendLine("# Icons are scaled to the height of the game's text line, keeping their proportions.");
            foreach (var (name, px, py, img) in placed)
            {
                for (int r = 0; r < img.Height; r++)
                    Array.Copy(img.Rgba, r * img.Width * 4, rgba, ((py + r) * SheetWidth + px) * 4, img.Width * 4);
                layout.AppendLine($"{name} {px} {py} {img.Width} {img.Height}");
            }
            return (Png.Write(SheetWidth, height, rgba), layout.ToString());
        }
        finally { KeycapRenderer.T = prev; }
    }

    public static IconSheet Load(byte[] png, string layout)
    {
        var (w, h, rgba) = Png.Read(png);
        var sheet = new IconSheet();
        foreach (var raw in layout.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var p = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 5) continue;
            int x = int.Parse(p[1], CultureInfo.InvariantCulture), y = int.Parse(p[2], CultureInfo.InvariantCulture);
            int iw = int.Parse(p[3], CultureInfo.InvariantCulture), ih = int.Parse(p[4], CultureInfo.InvariantCulture);
            if (x < 0 || y < 0 || iw <= 0 || ih <= 0 || x + iw > w || y + ih > h) continue;
            var o = new byte[iw * ih * 4];
            for (int r = 0; r < ih; r++) Array.Copy(rgba, ((y + r) * w + x) * 4, o, r * iw * 4, iw * 4);
            sheet.Icons[p[0]] = new RgbaImage(iw, ih, o);
        }
        return sheet;
    }

    /// The icon scaled to the font's line height, or null if the sheet has no such icon.
    public RgbaImage? Get(string name, int lineHeight) =>
        Icons.TryGetValue(name, out var img) ? Png.ScaleToHeight(img.Width, img.Height, img.Rgba, lineHeight) : null;
}
