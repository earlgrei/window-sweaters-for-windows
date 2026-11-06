using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace WindowSweaters;

internal enum Stitch { Stockinette = 0, Rib = 1, Garter = 2 }

internal struct Gauge
{
    public float Rows, Aspect, RowOverlap, Yarn, Bow, Jitter, Ground, Shadow, Light, Depth, Ambient, Relief, Sheen, Tuck;
}

/// A seamless, cached piece of knitting. `Img` is exactly W x H device pixels.
internal sealed class Tile
{
    public required Bitmap Img;
    public int W, H;
}

/// Knitted window borders, ported from the macOS app's knit.c.
///
/// The band is the ring between the window's rounded rect and that rect grown by `band`.
/// The stitch pattern is periodic, so it's rendered once into a small seamless tile (built from
/// a lit height field), cached, and tiled into four mitred sides — the way a frame is knitted.
internal static class Knit
{
    public static Gauge G = new()
    {
        Rows = 6f, Aspect = 1.35f, RowOverlap = 0.12f, Yarn = 0.48f, Bow = 0.28f, Jitter = 0.035f,
        Ground = 0.98f, Shadow = 0.74f, Light = 1.18f, Ambient = 0.94f, Depth = 1.0f,
        Relief = 0.70f, Sheen = 0.10f, Tuck = 14f,
    };

    public static Stitch Stitch = Stitch.Stockinette;
    public static int Basket = 3;
    public static bool AnchorCentre;
    public const uint Cream = 0xfff6f0deu;

    public static readonly string[] StitchNames = ["stockinette", "rib", "garter"];

    public static readonly (string Name, uint[] Colors)[] Baskets =
    [
        ("dopamine", [0xffff2d95, 0xff00d9ff, 0xffffd400, 0xff7c3aff, 0xff00e676, 0xffff6b00, 0xffff1744, 0xff00b8d4]),
        ("neon", [0xfff50057, 0xff00e5ff, 0xffc6ff00, 0xff651fff, 0xff1de9b6, 0xffff9100]),
        ("punch", [0xffe8175d, 0xff0fb9b1, 0xfffec230, 0xff5f27cd, 0xff10ac84, 0xffee5a24]),
        ("wool", [0xffd1495b, 0xff4e8098, 0xffedae49, 0xff7c9885, 0xff9b6a8f, 0xffe8846b, 0xff3f7d6e, 0xffc46a4e, 0xff6d7ba8, 0xffb8935f]),
        ("sorbet", [0xfff08ca4, 0xff7fc6c4, 0xffffc978, 0xffb08ed4, 0xff8fce90, 0xffff9f7a, 0xff8ab6f0, 0xffe5a3d0]),
        ("forest", [0xff4a6b52, 0xff7d8f5c, 0xff3f6b6e, 0xff8a7248, 0xff5c6b8a, 0xff6b5344, 0xff2f5e4a]),
        ("mono", [0xff9aa0a6, 0xff7c8288, 0xffb4bac0, 0xff686e74, 0xff8d939a]),
    ];

    // murmur3 finalizer
    public static uint Mix(uint h)
    {
        h ^= h >> 16; h *= 0x85ebca6b;
        h ^= h >> 13; h *= 0xc2b2ae35;
        h ^= h >> 16;
        return h;
    }

    public static uint ColorForWindow(uint id)
    {
        var b = Baskets[Math.Clamp(Basket, 0, Baskets.Length - 1)].Colors;
        return b[Mix(id) % (uint)b.Length];
    }

    /// Stable fallback for an app without a curated colourway (FNV-1a of the lower-cased name).
    public static uint ColorForApp(string app)
    {
        uint hash = 2166136261u;
        foreach (char ch in app)
        {
            uint c = ch;
            if (c is >= 'A' and <= 'Z') c += 'a' - 'A';
            hash = (hash ^ (c & 0xff)) * 16777619u;
        }
        return ColorForWindow(hash);
    }

    // ---- colour helpers ----------------------------------------------------------
    public static void RgbToHsl(uint v, out double h, out double s, out double l)
    {
        double r = ((v >> 16) & 255) / 255.0, g = ((v >> 8) & 255) / 255.0, b = (v & 255) / 255.0;
        double mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
        l = (mx + mn) / 2; h = 0; s = 0;
        if (mx != mn)
        {
            double d = mx - mn;
            s = l > 0.5 ? d / (2 - mx - mn) : d / (mx + mn);
            h = mx == r ? (g - b) / d + (g < b ? 6 : 0) : mx == g ? (b - r) / d + 2 : (r - g) / d + 4;
            h /= 6;
        }
    }

    public static uint HslToRgb(double h, double s, double l)
    {
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
        Span<double> t = [h + 1.0 / 3, h, h - 1.0 / 3];
        Span<double> o = stackalloc double[3];
        for (int i = 0; i < 3; i++)
        {
            double c = t[i]; if (c < 0) c += 1; if (c > 1) c -= 1;
            o[i] = c < 1.0 / 6 ? p + (q - p) * 6 * c : c < 0.5 ? q : c < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - c) * 6 : p;
        }
        static uint B(double x) => (uint)Math.Clamp(x * 255, 0, 255);
        return 0xff000000u | (B(o[0]) << 16) | (B(o[1]) << 8) | B(o[2]);
    }

    /// The shared Zigzag's contrast yarn: cream, or on a pale yarn a deeper shade of that yarn.
    public static uint ZigzagContrast(uint baseColor)
    {
        RgbToHsl(baseColor, out double h, out double s, out double l);
        if (l < 0.62) return Cream;
        return HslToRgb(h, s, l - 0.26);
    }

    static Color ToColor(uint argb, float f = 1f)
    {
        int r = (int)Math.Min(255f, ((argb >> 16) & 255) * f);
        int g = (int)Math.Min(255f, ((argb >> 8) & 255) * f);
        int b = (int)Math.Min(255f, (argb & 255) * f);
        return Color.FromArgb(255, r, g, b);
    }

    static float Noise(int a, int b)
    {
        float s = MathF.Sin(a * 12.9898f + b * 78.233f) * 43758.5453f;
        return s - MathF.Floor(s);
    }

    // ---- paths ---------------------------------------------------------------------
    sealed class PathBuilder(GraphicsPath p)
    {
        PointF _cur;
        public void MoveTo(float x, float y) { p.StartFigure(); _cur = new PointF(x, y); }
        public void LineTo(float x, float y) { p.AddLine(_cur, new PointF(x, y)); _cur = new PointF(x, y); }
        public void QuadTo(float cx, float cy, float x, float y)
        {
            // GDI+ only has cubic Béziers; lift the quadratic exactly.
            var c1 = new PointF(_cur.X + 2f / 3f * (cx - _cur.X), _cur.Y + 2f / 3f * (cy - _cur.Y));
            var c2 = new PointF(x + 2f / 3f * (cx - x), y + 2f / 3f * (cy - y));
            var end = new PointF(x, y);
            p.AddBezier(_cur, c1, c2, end);
            _cur = end;
        }
    }

    // Lay ncols x nrows stitches plus a two-cell margin so the pattern wraps.
    // In tile space +y runs inward from the outer edge of the band, so the Vs point inward.
    static void Lay(PathBuilder path, int ncols, int nrows, float sw, float sh, float rowStep)
    {
        float bx = sw * 0.5f * G.Bow;
        float jit = sw * G.Jitter;

        if (Stitch == Stitch.Rib)
        {
            float h = (nrows + 4) * rowStep;
            for (int i = -2; i <= ncols + 1; i++)
            {
                float x = i * sw + sw * 0.5f;
                float w = (i & 1) != 0 ? sw * 0.16f : 0f;
                path.MoveTo(x + w, -2 * rowStep);
                path.LineTo(x + w, h);
            }
            return;
        }

        if (Stitch == Stitch.Garter)
        {
            for (int j = -2; j <= nrows + 1; j++)
            {
                float y = j * rowStep;
                float phase = (j & 1) != 0 ? sw * 0.5f : 0f;
                path.MoveTo(-2 * sw, y);
                for (int i = -2; i <= ncols + 1; i++)
                {
                    float x = i * sw + phase;
                    path.QuadTo(x + sw * 0.25f, y - sh * 0.30f, x + sw * 0.5f, y);
                    path.QuadTo(x + sw * 0.75f, y + sh * 0.30f, x + sw, y);
                }
            }
            return;
        }

        for (int j = -2; j <= nrows + 1; j++)
        {
            float y = j * rowStep;
            float jy = (Noise((j + nrows) % nrows, 3) - 0.5f) * jit * 2f;
            path.MoveTo(-2 * sw, y + jy);
            for (int i = -2; i <= ncols + 1; i++)
            {
                float x = i * sw;
                float jx = (Noise((i + ncols) % ncols, (j + nrows) % nrows) - 0.5f) * jit * 2f;
                path.QuadTo(x + bx + jx, y + sh * 0.55f + jy, x + sw * 0.5f + jx, y + sh + jy);
                path.QuadTo(x + sw - bx + jx, y + sh * 0.55f + jy, x + sw + jx, y + jy);
            }
        }
    }

    // ---- tiles ---------------------------------------------------------------------
    const int PatchChart = -2;

    static readonly Dictionary<(int Band2, uint Color, int Chart, int Stitch, int Rows100), Tile> s_cache = [];

    public static void FlushCache()
    {
        foreach (var t in s_cache.Values) t.Img.Dispose();
        s_cache.Clear();
    }

    static Tile GetTile(float band, uint color, Chart? chart, bool patch = false)
    {
        int band2 = (int)MathF.Round(band * 2f);
        band = band2 / 2f;
        var key = (band2, color, patch ? PatchChart : chart?.Id ?? -1, (int)Stitch, (int)MathF.Round(G.Rows * 100));
        if (s_cache.TryGetValue(key, out var t)) return t;
        if (s_cache.Count >= 96) FlushCache();
        t = MakeTile(band, color, patch ? null : chart, patch);
        s_cache[key] = t;
        return t;
    }

    static Tile MakeTile(float band, uint color, Chart? ch, bool patch)
    {
        bool sculpted = patch || (ch != null && ch.Sculpted);
        Gauge m = G;
        if (sculpted)
        {
            m.RowOverlap = 0.05f; m.Yarn = 0.40f;
            m.Ground = 0.94f; m.Ambient = 0.84f;
            m.Relief = 1.65f; m.Sheen = 0.28f;
        }
        float rows = Math.Max(m.Rows, 1.5f);
        float rowStep = band / rows;
        float sw = rowStep * m.Aspect;

        int ncols, nrows;
        if (ch != null)
        {
            int density = sculpted ? 1 : 2;
            sw /= density; rowStep /= density;
            ncols = ch.W * density; nrows = ch.H * density;
            ncols *= (int)Math.Max(1f, MathF.Ceiling(8f / (sw * ncols)));
            nrows *= (int)Math.Max(1f, MathF.Ceiling(8f / (rowStep * nrows)));
        }
        else { ncols = 8; nrows = 8; }

        int tw = Math.Max(8, (int)MathF.Round(sw * ncols)), th = Math.Max(8, (int)MathF.Round(rowStep * nrows));
        sw = (float)tw / ncols;
        rowStep = (float)th / nrows;
        float sh = rowStep / (1f - m.RowOverlap);
        float yarn = Math.Max(sw * m.Yarn, sculpted ? 0.8f : 0.35f);
        if (ch == null && !sculpted && Stitch == Stitch.Rib) yarn = sw * 0.55f;

        int S = rowStep < 4f ? 4 : 2;
        uint[] pixels = ch != null && !sculpted
            ? MakeFabric(color, ch, sw, rowStep, tw, th, S)
            : MakeSculpted(color, ch, m, sculpted, sw, sh, rowStep, ncols, nrows, yarn, tw, th, S);
        return new Tile { Img = Downsample(pixels, tw, th, S), W = tw, H = th };
    }

    // A compact fabric shader for colourwork, shading fine yarn directly into the tile.
    static uint[] MakeFabric(uint color, Chart ch, float sw, float rowStep, int tw, int th, int scale)
    {
        int width = tw * scale, height = th * scale;
        var pixels = new uint[width * height];
        float stitchScale = ch.Defined ? 2f : 1f;
        float relief = MathF.Min(ch.Defined ? 0.30f : 0.25f, MathF.Max(0f, G.Relief * (ch.Defined ? 0.32f : 0.20f)));
        for (int y = 0; y < height; y++)
        {
            float sy = (y + 0.5f) / (scale * rowStep);
            float stitchY = sy / stitchScale;
            int row = (int)MathF.Floor(stitchY);
            float v = stitchY - row;
            for (int x = 0; x < width; x++)
            {
                float sx = (x + 0.5f) / (scale * sw);
                int column = (int)MathF.Floor(sx);
                float stitchX = sx / stitchScale;
                int yarnColumn = (int)MathF.Floor(stitchX);
                float u = stitchX - yarnColumn;
                float colorWarp = ch.RoundDots ? 0f : 0.16f * (1f - 2f * MathF.Abs(2f * u - 1f));
                int colorRow = (int)MathF.Floor(sy + colorWarp);
                colorRow = (colorRow % (ch.H * 2) + ch.H * 2) % (ch.H * 2);
                uint yarnColor = ch.Px[(colorRow / 2) * ch.W + (column / 2) % ch.W];
                if (ch.RoundDots && (yarnColor >> 24) >= 128)
                {
                    int cx = (column / 2) % ch.W, cy = colorRow / 2;
                    bool left = ch.Px[cy * ch.W + (cx + ch.W - 1) % ch.W] == yarnColor;
                    bool above = ch.Px[((cy + ch.H - 1) % ch.H) * ch.W + cx] == yarnColor;
                    float dx = sx * 0.5f - MathF.Floor(sx * 0.5f) + (left ? 1f : 0f) - 1f;
                    float dy = sy * 0.5f - MathF.Floor(sy * 0.5f) + (above ? 1f : 0f) - 1f;
                    if (dx * dx + dy * dy > 0.94f) yarnColor = color;
                }
                if ((yarnColor >> 24) < 128) yarnColor = color;
                uint seed = Mix((uint)yarnColumn * 73856093u ^ (uint)row * 19349663u);
                float wobble = ((seed & 255u) / 255f - 0.5f) * G.Jitter;
                float leg = 0.42f * (1f - v) + 0.08f * v * (1f - v);
                float distance = MathF.Abs(MathF.Abs(u - 0.5f - wobble) - leg);
                float radius = MathF.Max(0.10f, G.Yarn * 0.5f);
                float ridge = MathF.Max(0f, 1f - distance * distance / (radius * radius));
                uint grain = Mix((uint)x * 73856093u ^ (uint)y * 19349663u);
                float fibre = ((grain & 255u) / 255f - 0.5f) * 0.014f;
                float shade = G.Ground - relief * (1f - ridge) + (G.Ambient - 0.94f) + G.Sheen * 0.08f * (0.5f - u) + fibre;
                shade = MathF.Max(0f, shade);
                uint r = (uint)MathF.Min(255f, ((yarnColor >> 16) & 255u) * shade);
                uint g = (uint)MathF.Min(255f, ((yarnColor >> 8) & 255u) * shade);
                uint b = (uint)MathF.Min(255f, (yarnColor & 255u) * shade);
                pixels[y * width + x] = 0xff000000u | (r << 16) | (g << 8) | b;
            }
        }
        return pixels;
    }

    // Plain knitting and raised colourwork: stroke the yarn, build a height field, light it.
    static unsafe uint[] MakeSculpted(uint color, Chart? ch, Gauge m, bool sculpted, float sw, float sh, float rowStep,
                                      int ncols, int nrows, float yarn, int tw, int th, int S)
    {
        int W = tw * S, H = th * S;

        var cols = new List<uint>();
        var paths = new List<GraphicsPath>();
        try
        {
            if (sculpted)
            {
                float bx = sw * 0.5f * m.Bow;
                var builders = new List<PathBuilder>();
                for (int j = -2; j <= nrows + 1; j++)
                    for (int i = -2; i <= ncols + 1; i++)
                    {
                        uint px = 0;
                        if (ch != null)
                        {
                            int cx = (i % ch.W + ch.W) % ch.W, cy = (j % ch.H + ch.H) % ch.H;
                            px = ch.Px[cy * ch.W + cx];
                        }
                        uint yc = (px >> 24) < 128 ? color : (px | 0xff000000u);
                        int k = cols.IndexOf(yc);
                        if (k < 0)
                        {
                            if (cols.Count == 16) continue;
                            cols.Add(yc); var gp = new GraphicsPath(); paths.Add(gp); builders.Add(new PathBuilder(gp));
                            k = cols.Count - 1;
                        }
                        float x = i * sw, y = j * rowStep;
                        var pb = builders[k];
                        pb.MoveTo(x, y);
                        pb.QuadTo(x + bx, y + sh * 0.55f, x + sw * 0.5f, y + sh);
                        pb.QuadTo(x + sw - bx, y + sh * 0.55f, x + sw, y);
                    }
            }
            else
            {
                cols.Add(color);
                var gp = new GraphicsPath(); paths.Add(gp);
                Lay(new PathBuilder(gp), ncols, nrows, sw, sh, rowStep);
            }

            // ---- flat colour ----
            var cbuf = new uint[W * H];
            using (var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.ScaleTransform(S, S);
                    using (var ground = new SolidBrush(ToColor(color, m.Ground))) g.FillRectangle(ground, -1, -1, tw + 2, th + 2);
                    for (int k = 0; k < cols.Count; k++)
                    {
                        using var pen = new Pen(ToColor(cols[k]), yarn) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                        g.DrawPath(pen, paths[k]);
                    }
                }
                CopyOut(bmp, cbuf);
            }

            // ---- height ----
            var hf = new float[W * H];
            using (var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.ScaleTransform(S, S);
                    g.Clear(Color.Black);
                    ReadOnlySpan<float> lw = [1.00f, 0.80f, 0.60f, 0.40f, 0.20f];
                    ReadOnlySpan<float> lv = [0.22f, 0.48f, 0.70f, 0.88f, 1.00f];
                    foreach (var p in paths)
                        for (int i = 0; i < lw.Length; i++)
                        {
                            int v = (int)(lv[i] * 255);
                            using var pen = new Pen(Color.FromArgb(255, v, v, v), yarn * lw[i]) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                            g.DrawPath(pen, p);
                        }
                }
                var hb = new uint[W * H];
                CopyOut(bmp, hb);
                for (int i = 0; i < hf.Length; i++) hf[i] = (hb[i] & 255) / 255f;
            }
            var tmp = new float[W * H];
            Blur(hf, W, H, tmp);
            Blur(hf, W, H, tmp);

            // ---- light it ----
            // In tile space y runs inward; this keeps the macOS look (light from the window side, upper left).
            const float lx = -0.45f, ly = 0.55f, lz = 0.70f;
            float relief = S * m.Relief;
            var obuf = new uint[W * H];
            for (int y = 0; y < H; y++)
            {
                int up = (y - 1 + H) % H, dn = (y + 1) % H;
                for (int x = 0; x < W; x++)
                {
                    int lft = (x - 1 + W) % W, rgt = (x + 1) % W;
                    float dzdx = (hf[y * W + rgt] - hf[y * W + lft]) * relief;
                    float dzdy = (hf[dn * W + x] - hf[up * W + x]) * relief;
                    float nx = -dzdx, ny = -dzdy;
                    float inv = 1f / MathF.Sqrt(nx * nx + ny * ny + 1f);
                    float ndl = MathF.Max(0f, (nx * lx + ny * ly + lz) * inv);
                    float h = hf[y * W + x];
                    float occ = sculpted ? 0.74f + 0.26f * h : 0.86f + 0.14f * h;
                    uint grain = Mix((uint)x * 73856093u ^ (uint)y * 19349663u);
                    float fibre = sculpted ? 1f : 0.993f + (grain & 255u) * (0.014f / 255f);
                    float shade = (m.Ambient + m.Sheen * ndl) * occ * fibre;
                    uint p = cbuf[y * W + x];
                    float peak = MathF.Max(p & 255, MathF.Max((p >> 8) & 255, (p >> 16) & 255));
                    float lift = sculpted ? MathF.Max(0f, 1f - peak / 48f) * h * 10f : 0f;
                    float cb = MathF.Min(255f, (p & 0xff) * shade + lift);
                    float cg = MathF.Min(255f, ((p >> 8) & 0xff) * shade + lift);
                    float cr = MathF.Min(255f, ((p >> 16) & 0xff) * shade + lift);
                    obuf[y * W + x] = 0xff000000u | ((uint)cr << 16) | ((uint)cg << 8) | (uint)cb;
                }
            }
            return obuf;
        }
        finally
        {
            foreach (var p in paths) p.Dispose();
        }
    }

    static unsafe void CopyOut(Bitmap bmp, uint[] dst)
    {
        var d = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < bmp.Height; y++)
            {
                uint* row = (uint*)((byte*)d.Scan0 + y * d.Stride);
                for (int x = 0; x < bmp.Width; x++) dst[y * bmp.Width + x] = row[x];
            }
        }
        finally { bmp.UnlockBits(d); }
    }

    // separable 3-tap blur, wrapping
    static void Blur(float[] a, int w, int h, float[] tmp)
    {
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int l = (x - 1 + w) % w, r = (x + 1) % w;
                tmp[y * w + x] = (a[y * w + l] + 2f * a[y * w + x] + a[y * w + r]) * 0.25f;
            }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int u = (y - 1 + h) % h, d = (y + 1) % h;
                a[y * w + x] = (tmp[u * w + x] + 2f * tmp[y * w + x] + tmp[d * w + x]) * 0.25f;
            }
    }

    // Box-filter the supersampled tile down to device pixels.
    static unsafe Bitmap Downsample(uint[] src, int tw, int th, int S)
    {
        int W = tw * S;
        var bmp = new Bitmap(tw, th, PixelFormat.Format32bppPArgb);
        var d = bmp.LockBits(new Rectangle(0, 0, tw, th), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        try
        {
            int n = S * S;
            for (int y = 0; y < th; y++)
            {
                uint* row = (uint*)((byte*)d.Scan0 + y * d.Stride);
                for (int x = 0; x < tw; x++)
                {
                    uint r = 0, g = 0, b = 0;
                    for (int sy = 0; sy < S; sy++)
                        for (int sx = 0; sx < S; sx++)
                        {
                            uint p = src[(y * S + sy) * W + x * S + sx];
                            r += (p >> 16) & 255; g += (p >> 8) & 255; b += p & 255;
                        }
                    row[x] = 0xff000000u | (r / (uint)n << 16) | (g / (uint)n << 8) | (b / (uint)n);
                }
            }
        }
        finally { bmp.UnlockBits(d); }
        return bmp;
    }

    /// A small flat piece of knitting for the menu.
    public static Bitmap Swatch(uint color, Chart? chart, int w, int h)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        var t = GetTile(h, color, chart);
        bool cuff = chart != null && chart.Name.StartsWith("atelier-", StringComparison.Ordinal);
        FillTiled(g, t, new RectangleF(0, 0, w, h), 0f, cuff ? 0f : MathF.Round(h * 0.5f - t.H * 0.5f), t.W);
        using var edge = new Pen(Color.FromArgb(60, 0, 0, 0));
        g.PixelOffsetMode = PixelOffsetMode.Default;
        g.DrawRectangle(edge, 0, 0, w - 1, h - 1);
        return bmp;
    }

    // ---- drawing -------------------------------------------------------------------
    public static GraphicsPath RoundedRect(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        rad = Math.Max(0f, Math.Min(rad, Math.Min(r.Width, r.Height) / 2f));
        if (rad < 0.5f) { p.AddRectangle(r); return p; }
        float d = rad * 2;
        p.AddArc(r.Left, r.Top, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    static void FillTiled(Graphics g, Tile t, RectangleF dest, float x0, float y0, float repeatWidth)
    {
        using var brush = new TextureBrush(t.Img, WrapMode.Tile);
        brush.TranslateTransform(x0, y0);
        if (MathF.Abs(repeatWidth - t.W) > 0.01f) brush.ScaleTransform(repeatWidth / t.W, 1f);
        g.FillRectangle(brush, dest);
    }

    /// Draw a knitted band in the ring between `win` and `win` grown by `band`.
    /// The outer rounded edge is NOT clipped here: the caller applies an anti-aliased
    /// alpha mask. The inner edge is hidden under the window itself (the tuck).
    public static void DrawRing(Graphics g, RectangleF win, float radius, float band, uint color, Chart? chart, float tuck)
    {
        if (win.Width <= 0 || win.Height <= 0) return;
        if (band < 2f) band = 2f;
        radius = Math.Max(0f, Math.Min(radius, Math.Min(win.Width, win.Height) * 0.5f));
        if (tuck < 1f) tuck = 1f;
        float half = Math.Min(win.Width, win.Height) * 0.5f - 2f;
        if (tuck > half) tuck = half > 1f ? half : 1f;

        var outer = RectangleF.Inflate(win, band, band);
        float orad = radius + band;
        float irad = radius > tuck ? radius - tuck : 0f;

        bool solidCorners = chart is { SolidCorners: true };
        uint cornerColor = solidCorners && chart!.CornerColor != 0 ? chart.CornerColor : color;
        var t = GetTile(band, color, chart);
        Tile? cuffTile = null;
        GraphicsPath? cuffRing = null;
        if (chart is { CuffColor: not 0 })
        {
            cuffTile = GetTile(band, chart.CuffColor, null, patch: true);
            float width = band / 3f;
            cuffRing = new GraphicsPath(FillMode.Alternate);
            using (var a = RoundedRect(outer, orad)) cuffRing.AddPath(a, false);
            using (var b = RoundedRect(RectangleF.Inflate(outer, -width, -width), orad - width)) cuffRing.AddPath(b, false);
        }
        Tile? patch = solidCorners ? GetTile(band, cornerColor, null, patch: true) : null;

        g.SmoothingMode = SmoothingMode.None;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        float d = band + tuck + irad * (1f - 0.70710678f) + 1f;
        float ow = outer.Width, oh = outer.Height;
        d = Math.Min(d, Math.Min(ow * 0.5f, oh * 0.5f));

        // Each side gets its own space: +x runs along the edge, +y runs inward.
        var sides = new (float Ox, float Oy, float Angle, float Len)[]
        {
            (outer.Left, outer.Top, 0f, ow),
            (outer.Right, outer.Top, 90f, oh),
            (outer.Right, outer.Bottom, 180f, ow),
            (outer.Left, outer.Bottom, 270f, oh),
        };

        bool cuff = chart != null && chart.Name.StartsWith("atelier-", StringComparison.Ordinal);
        using var backing = new SolidBrush(ToColor(cornerColor, 0.96f));

        try
        {
            foreach (var (ox, oy, angle, L) in sides)
            {
                if (L <= 0f) continue;
                var state = g.Save();
                g.TranslateTransform(ox, oy);
                g.RotateTransform(angle);

                // the mitre: a trapezoid cut back at 45 degrees so neighbours meet corner to corner
                using (var mitre = new GraphicsPath())
                {
                    mitre.AddPolygon([new PointF(0, 0), new PointF(L, 0), new PointF(L - d, d), new PointF(d, d)]);
                    g.SetClip(mitre, CombineMode.Intersect);
                }

                float px = AnchorCentre ? L * 0.5f - t.W * 0.5f : 0f;
                float py = cuff ? 0f : MathF.Round(band * 0.5f - t.H * 0.5f);
                var side = new RectangleF(0, 0, L, d);
                g.FillRectangle(backing, side);

                if (patch != null)
                {
                    float cap = Math.Min(orad, L * 0.5f);
                    for (int end = 0; end < 2; end++)
                        FillTiled(g, patch, new RectangleF(end == 1 ? L - cap : 0f, 0f, cap, d), 0f, py, patch.W);
                }
                if (solidCorners)
                {
                    float cap = Math.Min(orad, L * 0.5f);
                    float available = Math.Max(0f, L - 2f * cap);
                    if (available > 0f)
                    {
                        float repeats = Math.Max(1f, MathF.Round(available / t.W));
                        FillTiled(g, t, new RectangleF(cap, 0f, available, d), cap, py, available / repeats);
                    }
                }
                else
                {
                    bool fitted = chart is { FittedRepeat: true };
                    float repeatWidth = fitted ? L / Math.Max(1f, MathF.Round(L / t.W)) : t.W;
                    g.InterpolationMode = fitted ? InterpolationMode.Bilinear : InterpolationMode.NearestNeighbor;
                    FillTiled(g, t, side, fitted ? 0f : px, py, repeatWidth);
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                }

                if (cuffRing != null && cuffTile != null)
                {
                    // Clip the cuff in window coordinates so its width follows the arc.
                    var inner = g.Save();
                    var local = g.Transform;
                    g.RotateTransform(-angle);
                    g.TranslateTransform(-ox, -oy);
                    g.SetClip(cuffRing, CombineMode.Intersect);
                    g.Transform = local;
                    local.Dispose();
                    FillTiled(g, cuffTile, side, 0f, 0f, cuffTile.W);
                    g.Restore(inner);
                }
                g.Restore(state);
            }
        }
        finally
        {
            cuffRing?.Dispose();
        }
    }
}
