using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace WindowSweaters;

/// A small premultiplied ARGB image, blitted straight into strip memory.
internal sealed class Sprite
{
    public required uint[] Px;
    public int W, H;
}

/// Cached sprites for the seasonal decorations. Drawn once with GDI+, then blitted by hand,
/// which is far cheaper than thousands of GDI+ draw calls per redraw.
internal static class Sprites
{
    static readonly Dictionary<string, Sprite> s_cache = [];

    public static void Flush() => s_cache.Clear();

    static Sprite Get(string key, int size, Action<Graphics, float> draw)
    {
        if (s_cache.TryGetValue(key, out var s)) return s;
        if (s_cache.Count > 6000) s_cache.Clear();
        s = Make(Math.Max(3, size), draw);
        s_cache[key] = s;
        return s;
    }

    static unsafe Sprite Make(int size, Action<Graphics, float> draw)
    {
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TranslateTransform(size / 2f, size / 2f);
            draw(g, size);
        }
        var px = new uint[size * size];
        var d = bmp.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        for (int y = 0; y < size; y++)
        {
            uint* row = (uint*)((byte*)d.Scan0 + y * d.Stride);
            for (int x = 0; x < size; x++) px[y * size + x] = row[x];
        }
        bmp.UnlockBits(d);
        return new Sprite { Px = px, W = size, H = size };
    }

    /// Premultiplied "over" into a w x h buffer, clipped.
    public static unsafe void Blit(uint* dst, int dw, int dh, Sprite s, int x, int y)
    {
        int x0 = Math.Max(0, x), y0 = Math.Max(0, y), x1 = Math.Min(dw, x + s.W), y1 = Math.Min(dh, y + s.H);
        if (x0 >= x1 || y0 >= y1) return;
        fixed (uint* src = s.Px)
        {
            for (int yy = y0; yy < y1; yy++)
            {
                uint* sp = src + (yy - y) * s.W + (x0 - x);
                uint* dp = dst + yy * dw + x0;
                for (int xx = x0; xx < x1; xx++, sp++, dp++)
                {
                    uint v = *sp, a = v >> 24;
                    if (a == 0) continue;
                    if (a == 255) { *dp = v; continue; }
                    uint ia = 255 - a, o = *dp;
                    uint rb = ((o & 0x00ff00ffu) * ia + 0x00800080u) >> 8 & 0x00ff00ffu;
                    uint ag = ((o >> 8) & 0x00ff00ffu) * ia + 0x00800080u & 0xff00ff00u;
                    *dp = v + rb + ag;
                }
            }
        }
    }

    // ---- colour helpers ----
    public static Color C(uint argb, int alpha = 255)
        => Color.FromArgb(alpha, (int)((argb >> 16) & 255), (int)((argb >> 8) & 255), (int)(argb & 255));

    public static uint Shade(uint c, float f)
    {
        uint Ch(int sh) => (uint)Math.Clamp(((c >> sh) & 255) * f, 0, 255);
        return 0xff000000u | (Ch(16) << 16) | (Ch(8) << 8) | Ch(0);
    }

    public static uint Mix(uint a, uint b, float t)
    {
        uint Ch(int sh) => (uint)Math.Clamp(((a >> sh) & 255) * (1 - t) + ((b >> sh) & 255) * t, 0, 255);
        return 0xff000000u | (Ch(16) << 16) | (Ch(8) << 8) | Ch(0);
    }

    public static int AngleIndex(float degrees, int steps)
        => ((int)MathF.Round(degrees / (360f / steps)) % steps + steps) % steps;

    // ---- spring ----
    public static Sprite Flower(int size, uint petal, uint centre, bool daisy, int turn) =>
        Get($"flower:{size}:{petal:x}:{centre:x}:{daisy}:{turn}", size, (g, s) =>
        {
            float R = s * 0.5f - 0.5f;
            int n = daisy ? 9 : 5;
            float len = R * 0.95f, wide = daisy ? R * 0.40f : R * 0.78f;
            using var fill = new SolidBrush(C(petal));
            using var edge = new Pen(C(Shade(petal, 0.7f), 120), Math.Max(0.6f, s * 0.035f));
            using var gloss = new SolidBrush(Color.FromArgb(80, 255, 255, 255));
            for (int i = 0; i < n; i++)
            {
                var st = g.Save();
                g.RotateTransform(turn * 13f + i * 360f / n);
                var r = new RectangleF(R * 0.05f, -wide / 2, len - R * 0.05f, wide);
                g.FillEllipse(fill, r);
                g.DrawEllipse(edge, r);
                g.FillEllipse(gloss, new RectangleF(R * 0.22f, -wide * 0.16f, len * 0.42f, wide * 0.32f));
                g.Restore(st);
            }
            float cr = R * (daisy ? 0.30f : 0.27f);
            using (var b = new SolidBrush(C(centre))) g.FillEllipse(b, -cr, -cr, 2 * cr, 2 * cr);
            using (var p = new Pen(C(Shade(centre, 0.65f), 160), Math.Max(0.6f, s * 0.035f))) g.DrawEllipse(p, -cr, -cr, 2 * cr, 2 * cr);
            using (var b = new SolidBrush(Color.FromArgb(120, 255, 255, 255))) g.FillEllipse(b, -cr * 0.55f, -cr * 0.6f, cr * 0.7f, cr * 0.55f);
        });

    /// A leaf whose base is at the sprite centre's left and tip to the right, rotated by `index` * 15°.
    public static Sprite Leaf(int size, uint colour, int index, bool lobed = false) =>
        Get($"leaf:{size}:{colour:x}:{index}:{lobed}", size, (g, s) =>
        {
            g.RotateTransform(index * 15f);
            float L = s * 0.46f, w = s * (lobed ? 0.24f : 0.19f);
            using var path = new GraphicsPath();
            if (lobed)
            {
                // a soft three-lobed leaf, maple-ish
                path.AddBezier(-L, 0, -L * 0.5f, -w * 0.6f, -L * 0.3f, -w * 1.9f, L * 0.05f, -w * 1.2f);
                path.AddBezier(L * 0.05f, -w * 1.2f, L * 0.2f, -w * 0.9f, L * 0.6f, -w * 1.1f, L, 0);
                path.AddBezier(L, 0, L * 0.6f, w * 1.1f, L * 0.2f, w * 0.9f, L * 0.05f, w * 1.2f);
                path.AddBezier(L * 0.05f, w * 1.2f, -L * 0.3f, w * 1.9f, -L * 0.5f, w * 0.6f, -L, 0);
            }
            else
            {
                path.AddBezier(-L, 0, -L * 0.4f, -w * 1.5f, L * 0.4f, -w * 1.2f, L, 0);
                path.AddBezier(L, 0, L * 0.4f, w * 1.2f, -L * 0.4f, w * 1.5f, -L, 0);
            }
            path.CloseFigure();
            using (var b = new SolidBrush(C(colour))) g.FillPath(b, path);
            using (var b = new SolidBrush(Color.FromArgb(55, 255, 255, 255)))
            {
                var st = g.Save();
                g.SetClip(new RectangleF(-L, -w * 2, 2 * L, w * 2));
                g.FillPath(b, path);
                g.Restore(st);
            }
            using (var p = new Pen(C(Shade(colour, 0.62f), 170), Math.Max(0.6f, s * 0.04f))) g.DrawLine(p, -L * 0.95f, 0, L * 0.8f, 0);
            using (var p = new Pen(C(Shade(colour, 0.7f), 110), Math.Max(0.5f, s * 0.025f))) g.DrawPath(p, path);
        });

    public static Sprite Berry(int size, uint colour) =>
        Get($"berry:{size}:{colour:x}", size, (g, s) =>
        {
            float r = s * 0.5f - 0.6f;
            using (var b = new SolidBrush(C(colour))) g.FillEllipse(b, -r, -r, 2 * r, 2 * r);
            using (var p = new Pen(C(Shade(colour, 0.6f), 170), Math.Max(0.5f, s * 0.07f))) g.DrawEllipse(p, -r, -r, 2 * r, 2 * r);
            using (var b = new SolidBrush(Color.FromArgb(170, 255, 255, 255))) g.FillEllipse(b, -r * 0.55f, -r * 0.6f, r * 0.6f, r * 0.5f);
        });

    // ---- holiday ----
    static readonly uint[] s_needles = [0xff173f24, 0xff1f4d2b, 0xff275e34, 0xff2f6c3a, 0xff3d7f45, 0xff5b9555];

    /// A tuft of pine needles splayed along `index` * 22.5°.
    public static Sprite Needles(int size, int index, int variant) =>
        Get($"needles:{size}:{index}:{variant}", size, (g, s) =>
        {
            var rnd = new Random(variant * 7919 + 17);
            g.RotateTransform(index * 22.5f);
            using (var twig = new Pen(Color.FromArgb(200, 0x5a, 0x3e, 0x26), Math.Max(0.7f, s * 0.05f)))
                g.DrawLine(twig, -s * 0.3f, 0, s * 0.3f, 0);
            for (int k = 0; k < 14; k++)
            {
                float along = (float)(rnd.NextDouble() - 0.5) * s * 0.55f;
                float side = k % 2 == 0 ? 1 : -1;
                float ang = (float)((25 + rnd.NextDouble() * 45) * side + (rnd.NextDouble() < 0.5 ? 0 : 180)) * MathF.PI / 180f;
                float len = s * (0.22f + 0.18f * (float)rnd.NextDouble());
                uint c = s_needles[rnd.Next(s_needles.Length)];
                using var p = new Pen(C(c), Math.Max(0.7f, s * 0.055f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(p, along, 0, along + MathF.Cos(ang) * len, MathF.Sin(ang) * len);
            }
        });

    public static readonly uint[] BulbColours = [0xffe8323c, 0xffffb02e, 0xff2fbf5a, 0xff2f7de8, 0xffe84aa6];

    /// A C9-style bulb pointing along `index` * 22.5°, lit to `level` of 7, with its glow.
    public static Sprite Bulb(int size, int colour, int level, int index) =>
        Get($"bulb:{size}:{colour}:{level}:{index}", size, (g, s) =>
        {
            uint c = BulbColours[colour];
            float k = level / 7f;
            float gr = s * 0.5f - 0.5f;
            using (var glowPath = new GraphicsPath())
            {
                glowPath.AddEllipse(-gr, -gr, 2 * gr, 2 * gr);
                using var glow = new PathGradientBrush(glowPath)
                {
                    CenterColor = C(Mix(c, 0xffffffffu, 0.25f), (int)(170 * k * k)),
                    SurroundColors = [C(c, 0)],
                };
                g.FillPath(glow, glowPath);
            }
            g.RotateTransform(index * 22.5f);
            using (var socket = new SolidBrush(Color.FromArgb(255, 0x1c, 0x33, 0x20)))
                g.FillRectangle(socket, -s * 0.2f, -s * 0.075f, s * 0.13f, s * 0.15f);
            uint body = Mix(Shade(c, 0.42f), Mix(c, 0xffffffffu, 0.28f), k);
            using (var path = new GraphicsPath())
            {
                float x0 = -s * 0.08f, x1 = s * 0.3f, hw = s * 0.12f;
                path.AddBezier(x0, 0, x0, -hw * 1.1f, x1 * 0.45f, -hw * 1.1f, x1, 0);
                path.AddBezier(x1, 0, x1 * 0.45f, hw * 1.1f, x0, hw * 1.1f, x0, 0);
                path.CloseFigure();
                using var b = new SolidBrush(C(body));
                g.FillPath(b, path);
                using var p = new Pen(C(Shade(c, 0.5f), 140), Math.Max(0.5f, s * 0.025f));
                g.DrawPath(p, path);
            }
            using (var hi = new SolidBrush(Color.FromArgb((int)(70 + 150 * k), 255, 255, 255)))
                g.FillEllipse(hi, s * 0.0f, -s * 0.075f, s * 0.13f, s * 0.05f);
        });
}

internal struct BulbSpot
{
    public int X, Y, Size, Colour, Angle;
    public uint Seed;
    public readonly Rectangle Bounds => new(X, Y, Size, Size);
}

/// Everything a seasonal ring is made of, in outer-rect coordinates.
internal sealed class DecorLayout
{
    public Action<Graphics>? Under, Over;
    public float Tube;           // > 0: shade the band as a round tube this thick
    public readonly List<(Sprite S, int X, int Y)> Items = [];
    public readonly List<BulbSpot> Bulbs = [];

    public void Place(Sprite s, float cx, float cy)
        => Items.Add((s, (int)MathF.Round(cx - s.W / 2f), (int)MathF.Round(cy - s.H / 2f)));
}

internal static class Decor
{
    static uint Hash(int i, int salt) => Knit.Mix((uint)i * 2654435761u ^ (uint)salt * 40503u);
    static float Rand(int i, int salt) => (Hash(i, salt) & 0xffff) / 65535f;
    static float Deg(PointF t) => MathF.Atan2(t.Y, t.X) * 180f / MathF.PI;

    /// Petals in the app's colour, kept light and lively enough to read as a flower.
    static uint Bloom(uint yarn, float lift)
    {
        Knit.RgbToHsl(yarn, out double h, out double s, out double l);
        return Knit.HslToRgb(h, Math.Max(s, 0.38), Math.Clamp(Math.Max(l, 0.58) + lift, 0.0, 0.86));
    }

    /// Pool-toy vinyl: saturated and bright. A grey app gets a classic lifebuoy red.
    static uint Vinyl(uint yarn)
    {
        Knit.RgbToHsl(yarn, out double h, out double s, out double l);
        return Knit.HslToRgb(h, Math.Max(s, 0.6), Math.Clamp(l, 0.46, 0.6));
    }

    public static DecorLayout Build(Season season, Ring ring, uint yarn, float band, float tuck)
    {
        var d = new DecorLayout();
        switch (season)
        {
            case Season.Spring: Spring(d, ring, yarn, band); break;
            case Season.Summer: Summer(d, ring, yarn, band, tuck); break;
            case Season.Fall: Fall(d, ring, yarn, band); break;
            case Season.Holiday: Holiday(d, ring, band); break;
        }
        return d;
    }

    static float Fit(float L, float target, int min, out int count)
    {
        count = Math.Max(min, (int)MathF.Round(L / target));
        return L / count;
    }

    static void Spring(DecorLayout d, Ring ring, uint yarn, float band)
    {
        float d0 = band * 0.5f, amp = band * 0.13f;
        float L = ring.Length(d0);
        float lambda = Fit(L, band * 2.4f, 3, out int waves);
        float Off(float s) => amp * MathF.Sin(2 * MathF.PI * s / lambda);
        d.Under = g =>
        {
            using var vine = new Pen(Sprites.C(0xff4f7a3a), Math.Max(1f, band * 0.1f)) { LineJoin = LineJoin.Round };
            g.DrawPolygon(vine, ring.Trace(d0, Off));
        };

        int leafSize = Math.Max(5, (int)MathF.Round(band * 0.62f));
        uint[] greens = [0xff5f9446, 0xff6fa552, 0xff4e8a40];
        for (int k = 0; k < waves * 2; k++)
        {
            float s = lambda * (0.25f + 0.5f * k);
            ring.At(s, d0, out var p, out var t);
            float side = k % 2 == 0 ? 1 : -1;
            float o = Off(s);
            var at = new PointF(p.X - t.Y * o, p.Y + t.X * o);
            float ang = Deg(t) + side * 38f + (k % 4 < 2 ? 0 : 180);
            float rad = ang * MathF.PI / 180f;
            var leaf = Sprites.Leaf(leafSize, greens[k % greens.Length], Sprites.AngleIndex(ang, 24));
            d.Place(leaf, at.X + MathF.Cos(rad) * leafSize * 0.36f, at.Y + MathF.Sin(rad) * leafSize * 0.36f);
        }

        int big = Math.Max(5, (int)MathF.Round(band * 0.9f)), bud = Math.Max(4, (int)MathF.Round(band * 0.5f));
        uint[] pastels = [0xfff4b8c8, 0xffc9b3e8, 0xfff6e08a];
        for (int k = 0; k < waves * 2; k++)
        {
            ring.At(k * lambda / 2, d0, out var p, out _);
            float r = Rand(k, 1);
            bool daisy = r is >= 0.58f and < 0.82f;
            uint petal = daisy ? 0xfff8f5ecu : r < 0.58f ? Bloom(yarn, (Rand(k, 2) - 0.5f) * 0.14f) : pastels[k % 3];
            uint centre = daisy ? 0xfff2c440u : 0xfff6dc7au;
            Knit.RgbToHsl(petal, out double ph, out double ps, out _);
            if (!daisy && ph is > 0.1 and < 0.19 && ps > 0.4) centre = 0xff8a5a2bu;   // a yellow flower gets a brown eye
            var sprite = k % 2 == 0 ? Sprites.Flower(big, petal, centre, daisy, (int)(Hash(k, 3) % 5))
                                    : Sprites.Flower(bud, petal, centre, false, (int)(Hash(k, 4) % 5));
            d.Place(sprite, p.X, p.Y);
        }
    }

    static void Summer(DecorLayout d, Ring ring, uint yarn, float band, float tuck)
    {
        // The tube rests partly under the window, like a window floating in a pool ring.
        float T = Math.Min(band * 1.35f, band + tuck - 1);
        uint stripe = Vinyl(yarn);
        d.Tube = T;
        d.Under = g =>
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            GraphicsPath PathAt(float off)
            {
                var (x, y, w, h, r) = ring.Inset(off);
                return Knit.RoundedRect(new RectangleF(x, y, w, h), r);
            }
            float mid = T / 2;
            using (var path = PathAt(mid))
            {
                float L = ring.Length(mid);
                int n = Math.Max(2, (int)MathF.Round(L / (band * 2.6f) / 2)) * 2;
                float seg = L / n;
                using (var white = new Pen(Color.FromArgb(255, 0xf8, 0xf5, 0xee), T)) g.DrawPath(white, path);
                using var colour = new Pen(Sprites.C(stripe), T) { DashPattern = [seg / T, seg / T], DashCap = DashCap.Flat };
                g.DrawPath(colour, path);
            }
        };
    }

    static void Fall(DecorLayout d, Ring ring, uint yarn, float band)
    {
        float d0 = band * 0.5f;
        float L = ring.Length(d0);
        float lambda = Fit(L, band * 3.0f, 3, out _);
        float amp = band * 0.15f;
        d.Under = g =>
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            void Twig(uint c, float width, float phase, float a, float wl)
            {
                using var p = new Pen(Sprites.C(c), Math.Max(1f, width)) { LineJoin = LineJoin.Round };
                g.DrawPolygon(p, ring.Trace(d0, s => a * MathF.Sin(2 * MathF.PI * s / wl + phase)));
            }
            Twig(0xff4d3520, band * 0.06f, MathF.PI / 2, amp * 0.7f, lambda / 2);
            Twig(0xff6e4b2e, band * 0.13f, 0, amp, lambda);
            Twig(0xff8c6a45, band * 0.09f, MathF.PI, amp, lambda);
            // little offshoots
            using var shoot = new Pen(Sprites.C(0xff6e4b2e), Math.Max(1f, band * 0.06f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            float step = Fit(L, band * 2.2f, 4, out int shoots);
            for (int k = 0; k < shoots; k++)
            {
                ring.At(k * step + step * 0.4f, d0, out var p, out var t);
                float side = k % 2 == 0 ? 1 : -1;
                float a = (Deg(t) + side * 40f) * MathF.PI / 180f;
                g.DrawLine(shoot, p.X, p.Y, p.X + MathF.Cos(a) * band * 0.32f, p.Y + MathF.Sin(a) * band * 0.32f);
            }
        };

        uint[] autumn = [0xffd2662e, 0xffb5452a, 0xffe3a83c, 0xff9c6b30, 0xffc98a2e, 0xff8f3b2a];
        float spacing = Fit(L, band * 1.25f, 4, out int clusters);
        for (int k = 0; k < clusters; k++)
        {
            int leaves = Rand(k, 5) < 0.6f ? 1 : 2;
            for (int j = 0; j < leaves; j++)
            {
                float s = k * spacing + (Rand(k, 6 + j) - 0.5f) * spacing * 0.5f;
                ring.At(s, d0, out var p, out var t);
                float side = (k + j) % 2 == 0 ? 1 : -1;
                int size = Math.Max(5, (int)MathF.Round(band * (0.58f + 0.14f * Rand(k, 8 + j))));
                float ang = Deg(t) + side * (25f + 45f * Rand(k, 10 + j)) + (Rand(k, 12 + j) < 0.5f ? 0 : 180);
                float rad = ang * MathF.PI / 180f;
                uint c = autumn[Hash(k * 3 + j, 14) % (uint)autumn.Length];
                var leaf = Sprites.Leaf(size, c, Sprites.AngleIndex(ang, 24), lobed: Rand(k, 16 + j) < 0.45f);
                float o = side * band * 0.08f;
                d.Place(leaf, p.X - t.Y * o + MathF.Cos(rad) * size * 0.3f, p.Y + t.X * o + MathF.Sin(rad) * size * 0.3f);
            }
            if (k % 4 == 2)
            {
                // a cluster of berries in the app's own colour
                ring.At(k * spacing + spacing * 0.5f, d0, out var p, out var t);
                Knit.RgbToHsl(yarn, out double h, out double sat, out double l);
                uint berry = Knit.HslToRgb(h, Math.Max(sat, 0.45), Math.Clamp(l, 0.35, 0.58));
                int bs = Math.Max(3, (int)MathF.Round(band * 0.22f));
                var sprite = Sprites.Berry(bs, berry);
                float n = bs * 0.45f;
                d.Place(sprite, p.X - t.Y * n, p.Y + t.X * n);
                d.Place(sprite, p.X + t.X * n, p.Y + t.Y * n);
                d.Place(sprite, p.X - t.X * n * 0.4f + t.Y * n * 0.6f, p.Y - t.Y * n * 0.4f - t.X * n * 0.6f);
            }
        }
    }

    static void Holiday(DecorLayout d, Ring ring, float band)
    {
        float d0 = band * 0.5f;
        float L = ring.Length(d0);

        // the garland: two rows of needle tufts
        int tuft = Math.Max(5, (int)MathF.Round(band * 0.8f));
        float step = Fit(L, band * 0.3f, 8, out int tufts);
        for (int row = 0; row < 2; row++)
            for (int k = 0; k < tufts; k++)
            {
                float s = (k + row * 0.5f) * step;
                ring.At(s, d0, out var p, out var t);
                float o = (row == 0 ? -0.17f : 0.17f) * band + (Rand(k, 20 + row) - 0.5f) * band * 0.14f;
                float ang = Deg(t) + (Rand(k, 22 + row) - 0.5f) * 60f;
                d.Place(Sprites.Needles(tuft, Sprites.AngleIndex(ang, 16), (int)(Hash(k, 24 + row) % 4)),
                        p.X - t.Y * o, p.Y + t.X * o);
            }

        // the light string, sagging between bulbs
        float spacing = Fit(L, band * 1.55f, 4, out int bulbs);
        float Sag(float s) => -band * 0.12f * MathF.Cos(2 * MathF.PI * s / spacing);
        d.Over = g =>
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var wire = new Pen(Color.FromArgb(230, 0x14, 0x2b, 0x18), Math.Max(1f, band * 0.055f)) { LineJoin = LineJoin.Round };
            g.DrawPolygon(wire, ring.Trace(d0, Sag));
        };

        int size = Math.Max(6, (int)MathF.Round(band * 1.2f));
        for (int k = 0; k < bulbs; k++)
        {
            float s = k * spacing;
            ring.At(s, d0, out var p, out var t);
            float o = Sag(s);
            float cx = p.X - t.Y * o, cy = p.Y + t.X * o;
            float outward = Deg(t) - 90f;
            d.Bulbs.Add(new BulbSpot
            {
                X = (int)MathF.Round(cx - size / 2f), Y = (int)MathF.Round(cy - size / 2f), Size = size,
                Colour = k % Sprites.BulbColours.Length, Angle = Sprites.AngleIndex(outward, 16), Seed = Hash(k, 99),
            });
        }
    }

    /// Brightness level 0..7 for a bulb at time t (seconds): slow, unsynchronised breathing,
    /// with a few bulbs that sparkle.
    public static int Level(in BulbSpot b, double t)
    {
        if (!Seasons.Twinkle) return 7;
        double period = 2.2 + (b.Seed % 1000) / 1000.0 * 3.8;
        double phase = ((b.Seed >> 10) % 1000) / 1000.0;
        double x = t / period + phase;
        double v = 0.5 + 0.5 * Math.Cos(2 * Math.PI * x);
        v = Math.Sqrt(v);                                   // mostly lit, with soft dips
        if ((b.Seed >> 20) % 6 == 0 && (x - Math.Floor(x)) < 0.12) v = ((int)(t * 4) & 1) == 0 ? 1 : 0.15;
        return (int)Math.Round((0.2 + 0.8 * v) * 7);
    }
}

/// Paints one strip of a ring — knit or seasonal — into raw premultiplied memory.
internal static class RingPainter
{
    public static unsafe void Paint(uint* bits, Rectangle r, int ow, int oh, int band, int tuck, float radius,
                                    uint yarn, Chart? chart, DecorLayout? decor)
    {
        using (var bmp = new Bitmap(r.Width, r.Height, r.Width * 4, PixelFormat.Format32bppPArgb, (IntPtr)bits))
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.TranslateTransform(-r.X, -r.Y);
            if (decor == null) Knit.DrawRing(g, new RectangleF(band, band, ow - 2 * band, oh - 2 * band), radius, band, yarn, chart, tuck);
            else decor.Under?.Invoke(g);
        }
        if (decor != null)
        {
            foreach (var (s, x, y) in decor.Items)
                if (x < r.Right && y < r.Bottom && x + s.W > r.X && y + s.H > r.Y)
                    Sprites.Blit(bits, r.Width, r.Height, s, x - r.X, y - r.Y);
            if (decor.Over != null)
            {
                using var bmp = new Bitmap(r.Width, r.Height, r.Width * 4, PixelFormat.Format32bppPArgb, (IntPtr)bits);
                using var g = Graphics.FromImage(bmp);
                g.TranslateTransform(-r.X, -r.Y);
                decor.Over(g);
            }
        }
        if (decor is { Tube: > 0 }) ShadeTube(bits, r, ow, oh, radius + band, decor.Tube);
        Mask(bits, r, ow, oh, radius + band);
    }

    static float Distance(float px, float py, float cx, float cy, float hx, float hy, float rad)
    {
        float qx = MathF.Abs(px - cx) - hx, qy = MathF.Abs(py - cy) - hy;
        float ox = MathF.Max(qx, 0f), oy = MathF.Max(qy, 0f);
        return MathF.Sqrt(ox * ox + oy * oy) + MathF.Min(MathF.Max(qx, qy), 0f) - rad;
    }

    /// Light a tube's round cross-section per pixel, by depth from the outer edge: darker at
    /// both rims, a glossy highlight near the outer crest. Smooth around every corner.
    static unsafe void ShadeTube(uint* p, Rectangle r, int ow, int oh, float rad, float T)
    {
        float cx = ow * 0.5f, cy = oh * 0.5f, hx = cx - rad, hy = cy - rad;
        for (int y = 0; y < r.Height; y++)
            for (int x = 0; x < r.Width; x++)
            {
                float u = -Distance(r.X + x + 0.5f, r.Y + y + 0.5f, cx, cy, hx, hy, rad) / T;
                if (u < 0f || u > 1f) continue;
                uint* px = p + y * r.Width + x;
                uint v = *px, a = v >> 24;
                if (a == 0) continue;
                float c = 2 * u - 1;
                float z = MathF.Sqrt(MathF.Max(0f, 1f - c * c));
                float f = 0.5f + 0.5f * MathF.Pow(z, 0.7f);
                float spec = MathF.Exp(-MathF.Pow((u - 0.3f) / 0.09f, 2)) * 0.75f;
                uint Ch(int sh) => (uint)Math.Clamp(((v >> sh) & 255) * f * (1 - spec) + a * spec, 0, a);
                *px = (a << 24) | (Ch(16) << 16) | (Ch(8) << 8) | Ch(0);
            }
    }

    /// Draw every bulb that touches this strip at its level for time t.
    public static unsafe void Bulbs(uint* bits, Rectangle r, DecorLayout decor, double t)
    {
        foreach (var b in decor.Bulbs)
        {
            if (!b.Bounds.IntersectsWith(r)) continue;
            var s = Sprites.Bulb(b.Size, b.Colour, Decor.Level(b, t), b.Angle);
            Sprites.Blit(bits, r.Width, r.Height, s, b.X - r.X, b.Y - r.Y);
        }
    }

    // Anti-aliased outer rounded edge: coverage from the rounded rect's signed distance.
    public static unsafe void Mask(uint* p, Rectangle r, int ow, int oh, float rad)
    {
        float cx = ow * 0.5f, cy = oh * 0.5f, hx = cx - rad, hy = cy - rad;
        for (int y = 0; y < r.Height; y++)
        {
            float qy = MathF.Abs(r.Y + y + 0.5f - cy) - hy;
            for (int x = 0; x < r.Width; x++)
            {
                float qx = MathF.Abs(r.X + x + 0.5f - cx) - hx;
                float ox = MathF.Max(qx, 0f), oy = MathF.Max(qy, 0f);
                float dist = MathF.Sqrt(ox * ox + oy * oy) + MathF.Min(MathF.Max(qx, qy), 0f) - rad;
                float a = 0.5f - dist;
                if (a >= 1f) continue;
                uint* px = p + y * r.Width + x;
                if (a <= 0f) { *px = 0; continue; }
                uint v = *px;
                uint A = (uint)(((v >> 24) & 255) * a), R = (uint)(((v >> 16) & 255) * a);
                uint G = (uint)(((v >> 8) & 255) * a), B = (uint)((v & 255) * a);
                *px = (A << 24) | (R << 16) | (G << 8) | B;
            }
        }
    }

    /// A straight piece of a ring for the menu.
    public static unsafe Bitmap Swatch(Season season, uint yarn, int w, int h)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
        try
        {
            int ow = w * 3, oh = h * 8;
            var decor = Decor.Build(season, new Ring(ow, oh, h), yarn, h, h * 0.6f);
            var r = new Rectangle(w, 0, w, h);
            Paint((uint*)data.Scan0, r, ow, oh, h, (int)(h * 0.6f), 0, yarn, null, decor);
            Bulbs((uint*)data.Scan0, r, decor, 0);
        }
        finally { bmp.UnlockBits(data); }
        return bmp;
    }
}
