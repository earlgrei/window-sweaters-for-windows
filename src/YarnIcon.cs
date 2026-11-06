using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace WindowSweaters;

/// The yarn-ball-and-needles icon from the macOS menu bar (assets/yarn-menu-icon.svg),
/// drawn in colour so it reads on both light and dark taskbars.
internal static class YarnIcon
{
    public static Bitmap Draw(int size, bool paused)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        float k = size / 18f;
        g.ScaleTransform(k, k);

        Color ball = paused ? Color.FromArgb(0x9a, 0xa0, 0xa6) : Color.FromArgb(0xc4, 0x6a, 0x4e);
        Color dark = paused ? Color.FromArgb(0x5c, 0x62, 0x68) : Color.FromArgb(0x7a, 0x3b, 0x2a);
        Color strand = Color.FromArgb(0xf6, 0xf0, 0xde);
        Color needle = Color.FromArgb(0xb8, 0x93, 0x5f);

        using (var p = new Pen(needle, 1.35f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            g.DrawLine(p, 3.2f, 2.2f, 5.7f, 5.35f);
            g.DrawLine(p, 14.8f, 2.2f, 12.3f, 5.35f);
        }
        using (var b = new SolidBrush(dark))
        {
            g.FillEllipse(b, 3.2f - 1.4f, 2.2f - 1.4f, 2.8f, 2.8f);
            g.FillEllipse(b, 14.8f - 1.4f, 2.2f - 1.4f, 2.8f, 2.8f);
        }

        var ballRect = new RectangleF(9f - 6.2f, 10.6f - 6.2f, 12.4f, 12.4f);
        using (var b = new SolidBrush(ball)) g.FillEllipse(b, ballRect);
        using (var clip = new GraphicsPath())
        {
            clip.AddEllipse(ballRect);
            var state = g.Save();
            g.SetClip(clip);
            using var p = new Pen(strand, 1.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawBezier(p, 5.5f, 4.6f, 11f, 7.6f, 11f, 13.6f, 5.5f, 16.6f);
            g.DrawBezier(p, 9.5f, 4.2f, 14.3f, 7.7f, 14.3f, 13.5f, 9.5f, 17f);
            g.DrawBezier(p, 2.2f, 11.3f, 4.4f, 9.2f, 6.9f, 8.4f, 9.1f, 8.3f);
            g.DrawBezier(p, 3.5f, 14.8f, 5.1f, 13.3f, 7f, 12.6f, 9.2f, 12.7f);
            g.Restore(state);
        }
        using (var p = new Pen(dark, 1.5f)) g.DrawEllipse(p, ballRect);
        return bmp;
    }

    /// A multi-size .ico (PNG frames) in memory.
    public static byte[] Ico(bool paused, params int[] sizes)
    {
        var frames = sizes.Select(s =>
        {
            using var bmp = Draw(s, paused);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return (Size: s, Png: ms.ToArray());
        }).ToList();

        using var output = new MemoryStream();
        using var w = new BinaryWriter(output);
        w.Write((short)0); w.Write((short)1); w.Write((short)frames.Count);
        int offset = 6 + 16 * frames.Count;
        foreach (var (s, png) in frames)
        {
            w.Write((byte)(s >= 256 ? 0 : s)); w.Write((byte)(s >= 256 ? 0 : s));
            w.Write((byte)0); w.Write((byte)0);
            w.Write((short)1); w.Write((short)32);
            w.Write(png.Length); w.Write(offset);
            offset += png.Length;
        }
        foreach (var (_, png) in frames) w.Write(png);
        w.Flush();
        return output.ToArray();
    }

    public static Icon Tray(bool paused)
    {
        var size = SystemInformation.SmallIconSize;
        using var ms = new MemoryStream(Ico(paused, 16, 20, 24, 32, 40, 48, 64));
        return new Icon(ms, size);
    }
}
