using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace WindowSweaters;

/// Renders a sheet of sample sweaters to a PNG, without touching any real window.
/// `WindowSweaters.exe --swatch out.png [scale]`
internal static class Preview
{
    public static void Render(string path, string? scaleArg)
    {
        float scale = float.TryParse(scaleArg, System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 2f;
        Charts.Load(Settings.ChartsDir);
        Apps.Load(Settings.AppsConfPath);

        var samples = new (string Label, Func<(uint, Chart?)> Look)[]
        {
            ("Claude", () => Rule("Claude")),
            ("Google Chrome", () => Rule("Google Chrome")),
            ("Visual Studio Code", () => Rule("Visual Studio Code")),
            ("Microsoft Excel", () => Rule("Microsoft Excel")),
            ("Windows Explorer", () => Rule("Windows Explorer")),
            ("Slack", () => Rule("Slack")),
            ("Notepad (notes)", () => Rule("Notepad")),
            ("WhatsApp", () => Rule("WhatsApp")),
            ("Plain stockinette", () => (0xffc46a4eu, null)),
            ("Zigzag", () => (0xff4e8098u, Charts.Find("zigzag"))),
            ("Messages (dots)", () => Rule("Messages")),
            ("Picnic", () => (0xff7c9885u, Charts.Find("picnic"))),
        };

        int cols = 3, rows = (samples.Length + cols - 1) / cols;
        int band = (int)MathF.Round(12 * scale), tuck = (int)MathF.Round(14 * scale);
        float radius = 8 * scale;
        int ww = (int)(300 * scale), wh = (int)(170 * scale), gap = (int)(36 * scale);
        int cellW = ww + 2 * band + gap, cellH = wh + 2 * band + gap + (int)(18 * scale);

        using var sheet = new Bitmap(cols * cellW + gap, rows * cellH + gap, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(sheet);
        g.Clear(Color.FromArgb(0x3a, 0x4a, 0x5c));
        using var font = new Font("Segoe UI", 9 * scale);

        for (int i = 0; i < samples.Length; i++)
        {
            var (label, look) = samples[i];
            var (yarn, chart) = look();
            int x = gap + (i % cols) * cellW, y = gap + (i / cols) * cellH;
            var outer = new Rectangle(x, y, ww + 2 * band, wh + 2 * band);
            using (var ring = new Bitmap(outer.Width, outer.Height, PixelFormat.Format32bppPArgb))
            {
                using (var rg = Graphics.FromImage(ring))
                    Knit.DrawRing(rg, new RectangleF(band, band, ww, wh), radius, band, yarn, chart, tuck);
                // the same rounded mask the strips use
                using var mask = Knit.RoundedRect(new RectangleF(0, 0, outer.Width, outer.Height), radius + band);
                var state = g.Save();
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using var tb = new TextureBrush(ring, WrapMode.Clamp);
                tb.TranslateTransform(outer.X, outer.Y);
                g.TranslateTransform(outer.X, outer.Y);
                tb.TranslateTransform(-outer.X, -outer.Y);
                g.FillPath(tb, mask);
                g.Restore(state);
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var win = Knit.RoundedRect(new RectangleF(x + band, y + band, ww, wh), radius))
                g.FillPath(Brushes.WhiteSmoke, win);
            g.DrawString(label, font, Brushes.White, x, y + outer.Height + 4 * scale);
        }
        sheet.Save(path, ImageFormat.Png);
    }

    static (uint, Chart?) Rule(string name)
    {
        var app = new AppIdentity { Key = name.ToLowerInvariant(), Name = name, Names = [name] };
        var rule = Apps.Rule(app);
        return rule == null ? (Knit.ColorForApp(name), null) : (rule.Color, Charts.Find(rule.Chart));
    }
}
