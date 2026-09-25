using System.Runtime.InteropServices;

using DynamicKeyPrompts.Formats;

namespace DynamicKeyPrompts.Icons;

/// What one generated glyph shows.
public abstract record KeycapSpec
{
    /// A keyboard key: a plate with the label (look depends on the theme).
    public sealed record Key(string Label) : KeycapSpec;
    /// A mouse with the pressed button highlighted; optional wheel arrow / "2" badge.
    public sealed record Mouse(MouseButton Button, int WheelDirection = 0, bool DoubleClick = false) : KeycapSpec;
    /// "Move the mouse" (camera on PC).
    public sealed record MouseMove : KeycapSpec;
}

public enum MouseButton { None, Left, Right, Middle }

/// Renders keycap glyphs matching the look of the game's own button icons.
/// Everything is drawn 4× larger and box-filtered down, text comes from GDI (Arial Bold).
public static unsafe class KeycapRenderer
{
    const int SS = 4;

    public sealed record Theme(
        string Name,
        (float R, float G, float B) PlateTop, (float R, float G, float B) PlateBottom, float PlateAlpha,
        (float R, float G, float B) Border, (float R, float G, float B) EdgeLight, float EdgeLightStrength,
        (float R, float G, float B) TextFill, (float R, float G, float B) TextOutline, float TextOutlineWidth,
        (float R, float G, float B) AccentTop, (float R, float G, float B) AccentBottom);

    public static readonly Theme[] Themes =
    [
        // Dark stone plate, thin bronze rim, parchment letters — the palette of the game's menus.
        new("dark", (66, 58, 48), (26, 23, 20), 1f, (150, 122, 80), (205, 180, 130), 0.35f,
            (232, 220, 192), (14, 12, 10), 0.05f, (226, 186, 104), (160, 112, 44)),
        // Only a bronze frame and letters over a see-through dark fill.
        new("minimal", (20, 17, 14), (8, 7, 6), 0.55f, (176, 152, 108), (215, 195, 150), 0.2f,
            (232, 220, 192), (8, 7, 6), 0.045f, (226, 186, 104), (160, 112, 44)),
        // Light metal, like the RB/LT pad icons.
        new("silver", (238, 238, 236), (132, 134, 138), 1f, (38, 38, 40), (255, 255, 255), 0.55f,
            (255, 255, 255), (40, 40, 42), 0.07f, (255, 236, 90), (214, 160, 0)),
    ];

    /// Active theme (set before rendering).
    public static Theme T { get; set; } = Themes[0];

    public static Theme ThemeByName(string name) =>
        Themes.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? Themes[0];

    /// Renders a glyph exactly lineHeight tall (the game stretches glyph regions to the line height).
    public static RgbaImage Render(KeycapSpec spec, int lineHeight)
    {
        int margin = Math.Max(1, (int)MathF.Round(lineHeight * 0.04f));
        int plateH = lineHeight - 2 * margin;
        var c = spec switch
        {
            KeycapSpec.Key k => DrawKey(k.Label, plateH, lineHeight, margin),
            KeycapSpec.Mouse m => DrawMouse(m, plateH, lineHeight, margin),
            KeycapSpec.MouseMove => DrawMouseMove(plateH, lineHeight, margin),
            _ => throw new ArgumentException("spec"),
        };
        return c.Downsample();
    }

    // ------------------------------------------------------------------ keys

    static Canvas DrawKey(string label, int plateH, int lineHeight, int margin)
    {
        bool single = label.Length == 1;
        int fontPx = (int)MathF.Round(plateH * SS * (single ? 0.72f : 0.50f));
        var text = TextMask.Render(label, fontPx);
        // Wide single symbols (arrows) are shrunk to keep the key square.
        float maxSingle = plateH * SS * 0.62f;
        if (single && text.Width > maxSingle)
            text = TextMask.Render(label, (int)(fontPx * maxSingle / text.Width));
        int padX = (int)(plateH * SS * (single ? 0.18f : 0.16f));
        int plateW = Math.Max(plateH * SS, text.Width + 2 * padX);
        plateW = (plateW + SS - 1) / SS * SS;
        int w = plateW + 2 * margin * SS, h = lineHeight * SS;
        var cv = new Canvas(w, h);

        float x0 = margin * SS, y0 = margin * SS, x1 = x0 + plateW, y1 = y0 + plateH * SS;
        float radius = plateH * SS * 0.24f;
        PaintPlate(cv, x0, y0, x1, y1, radius);

        int tx = (int)((x0 + x1 - text.Width) / 2), ty = (int)((y0 + y1 - text.Height) / 2);
        PaintOutlinedText(cv, text, tx, ty, outline: plateH * SS * T.TextOutlineWidth);
        return cv;
    }

    static void PaintPlate(Canvas cv, float x0, float y0, float x1, float y1, float radius)
    {
        float border = SS * 1.1f;
        for (int y = 0; y < cv.H; y++)
            for (int x = 0; x < cv.W; x++)
            {
                float d = RoundRectSdf(x + 0.5f, y + 0.5f, x0, y0, x1, y1, radius);
                float cover = Math.Clamp(0.5f - d, 0, 1);
                if (cover <= 0) continue;
                float t = Math.Clamp((y - y0) / (y1 - y0), 0, 1);
                var fill = Lerp(T.PlateTop, T.PlateBottom, t * t * 0.6f + t * 0.4f);
                float alpha = T.PlateAlpha;
                // Soft highlight just inside the rim along the top edge.
                if (d > -border * 2.2f && d < -border && t < 0.5f)
                {
                    fill = Lerp(fill, T.EdgeLight, T.EdgeLightStrength * (1 - t * 2));
                    alpha = Math.Max(alpha, T.EdgeLightStrength * (1 - t * 2));
                }
                if (d > -border) { fill = T.Border; alpha = 1; }
                cv.Blend(x, y, fill, cover * alpha);
            }
    }

    static void PaintOutlinedText(Canvas cv, TextMask text, int tx, int ty, float outline)
    {
        int r = Math.Max(1, (int)MathF.Ceiling(outline));
        var dil = text.Dilate(r);
        for (int y = 0; y < text.Height + 2 * r; y++)
            for (int x = 0; x < text.Width + 2 * r; x++)
            {
                float a = dil[y * (text.Width + 2 * r) + x] / 255f;
                if (a > 0) cv.Blend(tx + x - r, ty + y - r, T.TextOutline, a);
            }
        for (int y = 0; y < text.Height; y++)
            for (int x = 0; x < text.Width; x++)
            {
                float a = text.Mask[y * text.Width + x] / 255f;
                if (a > 0) cv.Blend(tx + x, ty + y, T.TextFill, a);
            }
    }

    // ------------------------------------------------------------------ mouse

    static Canvas DrawMouse(KeycapSpec.Mouse m, int plateH, int lineHeight, int margin)
    {
        int bodyW = (int)(plateH * SS * 0.64f);
        int extra = m.WheelDirection != 0 ? (int)(plateH * SS * 0.42f) : m.DoubleClick ? (int)(plateH * SS * 0.22f) : 0;
        int w = bodyW + extra + 2 * margin * SS + SS * 2, h = lineHeight * SS;
        w = (w + SS - 1) / SS * SS;
        var cv = new Canvas(w, h);
        float x0 = margin * SS + SS, y0 = margin * SS, x1 = x0 + bodyW, y1 = y0 + plateH * SS;
        PaintMouseBody(cv, x0, y0, x1, y1, m.Button);

        if (m.WheelDirection != 0)
        {
            float ax = x1 + extra * 0.55f, ay = (y0 + y1) / 2, s = extra * 0.42f;
            PaintArrow(cv, ax, ay, s, up: m.WheelDirection > 0);
        }
        if (m.DoubleClick)
        {
            using var two = TextMask.Render("2", (int)(plateH * SS * 0.55f));
            PaintOutlinedText(cv, two, (int)(x1 - two.Width * 0.55f), (int)(y1 - two.Height * 0.95f), plateH * SS * 0.07f);
        }
        return cv;
    }

    static Canvas DrawMouseMove(int plateH, int lineHeight, int margin)
    {
        int bodyW = (int)(plateH * SS * 0.60f), side = (int)(plateH * SS * 0.34f);
        int w = bodyW + 2 * side + 2 * margin * SS, h = lineHeight * SS;
        w = (w + SS - 1) / SS * SS;
        var cv = new Canvas(w, h);
        float x0 = margin * SS + side, y0 = margin * SS + plateH * SS * 0.06f, x1 = x0 + bodyW, y1 = margin * SS + plateH * SS * 0.94f;
        PaintMouseBody(cv, x0, y0, x1, y1, MouseButton.None);
        float s = side * 0.62f, cy = (y0 + y1) / 2;
        PaintArrowH(cv, x0 - side * 0.5f, cy, s, left: true);
        PaintArrowH(cv, x1 + side * 0.5f, cy, s, left: false);
        return cv;
    }

    static void PaintMouseBody(Canvas cv, float x0, float y0, float x1, float y1, MouseButton pressed)
    {
        float border = SS * 1.1f, radius = (x1 - x0) * 0.5f;
        float split = y0 + (y1 - y0) * 0.44f, mid = (x0 + x1) / 2;
        float wheelW = (x1 - x0) * 0.16f, wheelY0 = y0 + (y1 - y0) * 0.13f, wheelY1 = y0 + (y1 - y0) * 0.33f;
        for (int y = 0; y < cv.H; y++)
            for (int x = 0; x < cv.W; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float d = RoundRectSdf(px, py, x0, y0, x1, y1, radius);
                float cover = Math.Clamp(0.5f - d, 0, 1);
                if (cover <= 0) continue;
                float t = Math.Clamp((py - y0) / (y1 - y0), 0, 1);
                var col = Lerp(T.PlateTop, T.PlateBottom, t);
                bool left = px < mid;
                bool inButtons = py < split;
                if (inButtons && (pressed == MouseButton.Left && left || pressed == MouseButton.Right && !left))
                    col = Lerp(T.AccentTop, T.AccentBottom, Math.Clamp((py - y0) / (split - y0), 0, 1));
                // Seams between the buttons.
                if (Math.Abs(py - split) < SS * 0.6f || inButtons && Math.Abs(px - mid) < SS * 0.6f) col = T.Border;
                // Wheel.
                float wd = RoundRectSdf(px, py, mid - wheelW / 2, wheelY0, mid + wheelW / 2, wheelY1, wheelW / 2);
                if (wd < -SS * 0.8f && pressed == MouseButton.Middle) col = T.AccentTop;
                else if (wd < 0) col = T.Border;
                if (d > -border) col = T.Border;
                cv.Blend(x, y, col, cover);
            }
    }

    static void PaintArrow(Canvas cv, float cx, float cy, float s, bool up)
    {
        float dir = up ? -1 : 1;
        // Triangle tip at cy + dir * s, base at cy - dir * s * 0.2, plus a stem.
        for (int y = 0; y < cv.H; y++)
            for (int x = 0; x < cv.W; x++)
            {
                float px = x + 0.5f - cx, py = (y + 0.5f - cy) * dir; // py grows towards the tip
                bool head = py <= s && py >= -s * 0.1f && Math.Abs(px) <= (s - py) * 0.9f;
                bool stem = py < 0 && py > -s && Math.Abs(px) < s * 0.3f;
                if (!head && !stem) continue;
                cv.Blend(x, y, Lerp(T.AccentTop, T.AccentBottom, (py + s) / (2 * s)), 1, accent: true);
            }
        cv.OutlineAccent();
    }

    static void PaintArrowH(Canvas cv, float cx, float cy, float s, bool left)
    {
        float dir = left ? -1 : 1;
        for (int y = 0; y < cv.H; y++)
            for (int x = 0; x < cv.W; x++)
            {
                float px = (x + 0.5f - cx) * dir, py = y + 0.5f - cy;
                if (px > s * 0.6f || px < -s * 0.6f || Math.Abs(py) > (s * 0.6f - px) * 0.85f) continue;
                cv.Blend(x, y, Lerp(T.AccentTop, T.AccentBottom, (py + s) / (2 * s)), 1, accent: true);
            }
        cv.OutlineAccent();
    }

    // ------------------------------------------------------------------ helpers

    static float RoundRectSdf(float px, float py, float x0, float y0, float x1, float y1, float r)
    {
        float cx = (x0 + x1) / 2, cy = (y0 + y1) / 2, hx = (x1 - x0) / 2 - r, hy = (y1 - y0) / 2 - r;
        float qx = Math.Abs(px - cx) - hx, qy = Math.Abs(py - cy) - hy;
        float ox = Math.Max(qx, 0), oy = Math.Max(qy, 0);
        return MathF.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0) - r;
    }

    static (float R, float G, float B) Lerp((float R, float G, float B) a, (float R, float G, float B) b, float t) =>
        (a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);

    /// Premultiplied float RGBA canvas at 4× resolution.
    sealed class Canvas(int w, int h)
    {
        public readonly int W = w, H = h;
        readonly float[] _px = new float[w * h * 4];
        readonly bool[] _accent = new bool[w * h];

        public void Blend(int x, int y, (float R, float G, float B) c, float a, bool accent = false)
        {
            if ((uint)x >= (uint)W || (uint)y >= (uint)H || a <= 0) return;
            int i = (y * W + x) * 4;
            float k = 1 - a;
            _px[i] = c.R * a + _px[i] * k; _px[i + 1] = c.G * a + _px[i + 1] * k; _px[i + 2] = c.B * a + _px[i + 2] * k;
            _px[i + 3] = a + _px[i + 3] * k;
            if (accent) _accent[y * W + x] = true;
        }

        /// Dark rim around the yellow accent shapes (arrows), drawn under them.
        public void OutlineAccent()
        {
            int r = SS;
            var rim = new bool[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (_accent[y * W + x]) continue;
                    for (int dy = -r; dy <= r && !rim[y * W + x]; dy++)
                        for (int dx = -r; dx <= r; dx++)
                        {
                            int X = x + dx, Y = y + dy;
                            if ((uint)X < (uint)W && (uint)Y < (uint)H && _accent[Y * W + X] && dx * dx + dy * dy <= r * r) { rim[y * W + x] = true; break; }
                        }
                }
            for (int i = 0; i < rim.Length; i++)
                if (rim[i] && _px[i * 4 + 3] < 0.99f) Blend(i % W, i / W, T.TextOutline, 1);
            Array.Clear(_accent);
        }

        public RgbaImage Downsample()
        {
            int w = W / SS, h = H / SS;
            var o = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float r = 0, g = 0, b = 0, a = 0;
                    for (int sy = 0; sy < SS; sy++)
                        for (int sx = 0; sx < SS; sx++)
                        {
                            int i = ((y * SS + sy) * W + x * SS + sx) * 4;
                            r += _px[i]; g += _px[i + 1]; b += _px[i + 2]; a += _px[i + 3];
                        }
                    float n = SS * SS;
                    a /= n;
                    int d = (y * w + x) * 4;
                    if (a > 0.001f)
                    {
                        o[d] = (byte)Math.Clamp(r / n / a, 0, 255);
                        o[d + 1] = (byte)Math.Clamp(g / n / a, 0, 255);
                        o[d + 2] = (byte)Math.Clamp(b / n / a, 0, 255);
                    }
                    else { o[d] = (byte)T.Border.R; o[d + 1] = (byte)T.Border.G; o[d + 2] = (byte)T.Border.B; }
                    o[d + 3] = (byte)Math.Clamp(a * 255 + 0.5f, 0, 255);
                }
            return new RgbaImage(w, h, o);
        }
    }

    /// Anti-aliased text coverage from GDI.
    sealed class TextMask : IDisposable
    {
        public int Width, Height;
        public byte[] Mask = [];

        public static TextMask Render(string text, int pixelHeight)
        {
            nint dc = CreateCompatibleDC(0);
            nint font = CreateFontW(-pixelHeight, 0, 0, 0, 700, 0, 0, 0, 1 /*DEFAULT_CHARSET*/, 0, 0, 4 /*ANTIALIASED_QUALITY*/, 0, "Arial");
            nint oldFont = SelectObject(dc, font);
            GetTextExtentPoint32W(dc, text, text.Length, out var size);
            int w = size.cx + 4, h = size.cy + 2;
            var bmi = new BITMAPINFO { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            nint bmp = CreateDIBSection(dc, ref bmi, 0, out nint bits, 0, 0);
            nint oldBmp = SelectObject(dc, bmp);
            SetBkMode(dc, 1 /*TRANSPARENT*/);
            SetTextColor(dc, 0x00FFFFFF);
            TextOutW(dc, 2, 1, text, text.Length);
            GdiFlush();
            var raw = new byte[w * h];
            byte* p = (byte*)bits;
            for (int i = 0; i < w * h; i++) raw[i] = p[i * 4 + 1];
            SelectObject(dc, oldBmp); DeleteObject(bmp);
            SelectObject(dc, oldFont); DeleteObject(font);
            DeleteDC(dc);
            return Trim(raw, w, h);
        }

        /// Crops to the inked area, keeping the text's vertical centre meaningful.
        static TextMask Trim(byte[] m, int w, int h)
        {
            int x0 = w, x1 = -1, y0 = h, y1 = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (m[y * w + x] > 8) { x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y); }
            if (x1 < 0) return new TextMask { Width = 1, Height = 1, Mask = new byte[1] };
            int tw = x1 - x0 + 1, th = y1 - y0 + 1;
            var o = new byte[tw * th];
            for (int y = 0; y < th; y++) Array.Copy(m, (y0 + y) * w + x0, o, y * tw, tw);
            return new TextMask { Width = tw, Height = th, Mask = o };
        }

        public byte[] Dilate(int r)
        {
            int w = Width + 2 * r, h = Height + 2 * r;
            var o = new byte[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int best = 0;
                    for (int dy = -r; dy <= r && best < 255; dy++)
                        for (int dx = -r; dx <= r; dx++)
                        {
                            if (dx * dx + dy * dy > r * r) continue;
                            int sx = x - r + dx, sy = y - r + dy;
                            if ((uint)sx < (uint)Width && (uint)sy < (uint)Height) best = Math.Max(best, Mask[sy * Width + sx]);
                        }
                    o[y * w + x] = (byte)best;
                }
            return o;
        }

        public void Dispose() { }
    }

    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFO
    {
        public int biSize, biWidth, biHeight; public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        public int bmiColors;
    }

    [DllImport("gdi32.dll")] static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    static extern nint CreateFontW(int h, int w, int esc, int orient, int weight, uint italic, uint underline, uint strike,
        uint charset, uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string face);
    [DllImport("gdi32.dll")] static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern bool GetTextExtentPoint32W(nint dc, string s, int n, out SIZE size);
    [DllImport("gdi32.dll")] static extern nint CreateDIBSection(nint dc, ref BITMAPINFO bmi, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")] static extern int SetBkMode(nint dc, int mode);
    [DllImport("gdi32.dll")] static extern uint SetTextColor(nint dc, uint color);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern bool TextOutW(nint dc, int x, int y, string s, int n);
    [DllImport("gdi32.dll")] static extern bool GdiFlush();
}
