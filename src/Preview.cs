using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace WindowSweaters;

/// Renders a sheet of sample borders to a PNG, without touching any real window.
/// `WindowSweaters.exe --swatch out.png [scale] [winter|spring|summer|fall|holiday] [seconds]`
internal static class Preview
{
    public static unsafe void Render(string path, string? scaleArg, string? seasonArg = null, string? timeArg = null)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        float scale = float.TryParse(scaleArg, inv, out var s) ? s : 2f;
        double time = double.TryParse(timeArg, inv, out var t) ? t : 0;
        Charts.Load(Settings.ChartsDir);
        Apps.Load(Settings.AppsConfPath);
        Seasons.FollowCalendar = false;
        Seasons.Chosen = Seasons.Parse(seasonArg);
        var season = Seasons.Current;

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
        int ow = ww + 2 * band, oh = wh + 2 * band;
        int cellW = ow + gap, cellH = oh + gap + (int)(18 * scale);

        using var sheet = new Bitmap(cols * cellW + gap, rows * cellH + gap, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(sheet);
        g.Clear(Color.FromArgb(0x3a, 0x4a, 0x5c));
        using var font = new Font("Segoe UI", 9 * scale);

        for (int i = 0; i < samples.Length; i++)
        {
            var (label, look) = samples[i];
            var (yarn, chart) = look();
            int x = gap + (i % cols) * cellW, y = gap + (i / cols) * cellH;
            using (var ring = new Bitmap(ow, oh, PixelFormat.Format32bppPArgb))
            {
                var data = ring.LockBits(new Rectangle(0, 0, ow, oh), ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
                var full = new Rectangle(0, 0, ow, oh);
                var decor = season == Season.Winter ? null : Decor.Build(season, new Ring(ow, oh, radius + band), yarn, band, tuck);
                RingPainter.Paint((uint*)data.Scan0, full, ow, oh, band, tuck, radius, yarn, chart, decor);
                if (decor != null) RingPainter.Bulbs((uint*)data.Scan0, full, decor, time);
                ring.UnlockBits(data);
                g.DrawImageUnscaled(ring, x, y);
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var win = Knit.RoundedRect(new RectangleF(x + band, y + band, ww, wh), radius))
                g.FillPath(Brushes.WhiteSmoke, win);
            g.DrawString(label, font, Brushes.White, x, y + oh + 4 * scale);
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
