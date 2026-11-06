using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace WindowSweaters;

/// Icon-derived colourways, ported from autoyarn.m: By App sweaters for apps without an
/// authored rule, and the shared Zigzag's main yarn for every app.
internal static class AutoYarn
{
    sealed class Entry
    {
        public bool Ok;
        public uint Base, Contrast;
        public Chart? Chart;
        public string ChartKind = "";
        public uint ChartYarn;
        public int Generation;
    }

    static readonly Dictionary<string, Entry> s_entries = new(StringComparer.OrdinalIgnoreCase);

    public static void Clear() => s_entries.Clear();

    // The hand-picked palette averages ~0.49 saturation; raw icon colour is near 1.0.
    static uint Soften(uint v, double lshift = 0)
    {
        Knit.RgbToHsl(v, out double h, out double s, out double l);
        s = Math.Min(0.60, Math.Max(0.22, s * 0.48));
        l = Math.Min(0.72, Math.Max(0.30, l * 0.85 + 0.10 + lshift));
        return Knit.HslToRgb(h, s, l);
    }

    static Bitmap? IconFor(string? exePath)
    {
        if (exePath == null || !File.Exists(exePath)) return null;
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(exePath);
            return icon?.ToBitmap();
        }
        catch { return null; }
    }

    static unsafe bool IconPixels(Bitmap? icon, out uint baseColor, out uint contrast)
    {
        baseColor = contrast = 0;
        if (icon == null) return false;
        const int S = 48;
        var px = new uint[S * S];
        using (var bmp = new Bitmap(S, S, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(icon, 0, 0, S, S);
            }
            var d = bmp.LockBits(new Rectangle(0, 0, S, S), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < S; y++)
            {
                uint* row = (uint*)((byte*)d.Scan0 + y * d.Stride);
                for (int x = 0; x < S; x++) px[y * S + x] = row[x];
            }
            bmp.UnlockBits(d);
        }

        var counts = new int[4096];
        int kept = 0;
        foreach (uint v in px)
        {
            if (((v >> 24) & 255) < 217) continue;                       // transparent corners
            double r = ((v >> 16) & 255) / 255.0, g = ((v >> 8) & 255) / 255.0, b = (v & 255) / 255.0;
            double mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
            double sat = mx <= 0 ? 0 : (mx - mn) / mx;
            if (sat < 0.18 || mx < 0.12 || mx - mn < 0.10) continue;     // greys, whites, blacks
            counts[((int)(r * 15) << 8) | ((int)(g * 15) << 4) | (int)(b * 15)]++;
            kept++;
        }
        if (kept < S * S / 60) return false;                              // no committed colour

        static uint Bucket(int i) => 0xff000000u | ((uint)(((i >> 8) & 15) / 15.0 * 255) << 16)
                                     | ((uint)(((i >> 4) & 15) / 15.0 * 255) << 8) | (uint)((i & 15) / 15.0 * 255);
        var picked = new List<uint>();
        for (int pass = 0; pass < 3 && picked.Count < 3; pass++)
        {
            int best = -1, bc = 0;
            for (int i = 0; i < 4096; i++)
            {
                if (counts[i] == 0) continue;
                uint col = Bucket(i);
                bool far = true;
                Knit.RgbToHsl(col, out double ch, out _, out double cl);
                foreach (var p in picked)
                {
                    Knit.RgbToHsl(p, out double ph, out _, out double pl);
                    double dh = Math.Abs(ch - ph); if (dh > 0.5) dh = 1 - dh;
                    if (dh < 0.06 && Math.Abs(cl - pl) < 0.20) far = false;
                }
                if (far && counts[i] > bc) { bc = counts[i]; best = i; }
            }
            if (best < 0) break;
            picked.Add(Bucket(best));
        }
        if (picked.Count == 0) return false;

        Knit.RgbToHsl(picked[0], out _, out _, out double bl);
        int ci = -1;
        for (int j = 1; j < picked.Count; j++)
        {
            Knit.RgbToHsl(picked[j], out _, out _, out double l2);
            if (Math.Abs(l2 - bl) >= 0.20) { ci = j; break; }
        }
        baseColor = Soften(picked[0]);
        contrast = ci >= 0 ? Soften(picked[ci]) : Knit.Cream;
        Knit.RgbToHsl(baseColor, out double h0, out double s0, out double l0);
        Knit.RgbToHsl(contrast, out _, out _, out double lc);
        if (ci < 0 || Math.Abs(l0 - lc) < 0.20)
            contrast = l0 < 0.62 ? Knit.Cream : Knit.HslToRgb(h0, s0, l0 - 0.26);
        return true;
    }

    static Entry EntryFor(AppIdentity app)
    {
        if (s_entries.TryGetValue(app.Key, out var e)) return e;
        e = new Entry();
        using (var icon = IconFor(app.ExePath))
            e.Ok = IconPixels(icon, out e.Base, out e.Contrast);
        if (s_entries.Count > 256) s_entries.Clear();
        s_entries[app.Key] = e;
        return e;
    }

    static Chart? EntryChart(Entry e, string motif, string kind, uint yarn)
    {
        if (e.Chart == null || e.Generation != Charts.Generation || e.ChartKind != kind || e.ChartYarn != yarn)
        {
            var source = Charts.Find(motif);
            e.Chart = source?.CloneWithYarn(yarn, $"auto-{motif}");
            e.ChartKind = kind;
            e.ChartYarn = yarn;
            e.Generation = Charts.Generation;
        }
        return e.Chart;
    }

    // The By App motif is a stable hash of the app name, independent of colour.
    static string ByAppMotif(string app)
    {
        string[] lean = ["zigzag", "picnic", "twinkle"];
        uint hash = 2166136261u;
        foreach (char c in app) hash = (hash ^ (c & 0xffu)) * 16777619u;
        return lean[hash % 3];
    }

    /// For an app with no hand-picked sweater: its icon's colour, and in By App mode a motif.
    public static bool TryAuto(AppIdentity app, out uint yarn, out Chart? chart)
    {
        yarn = 0; chart = null;
        if (Apps.Rule(app) != null) return false;
        var e = EntryFor(app);
        if (!e.Ok) return false;
        yarn = e.Base;
        chart = Apps.PatternByApp ? EntryChart(e, ByAppMotif(app.Name), "by-app", e.Contrast) : null;
        return true;
    }

    public static bool ZigzagActive => !Apps.PatternByApp && Apps.ActiveChart?.Name == "zigzag";

    /// The shared Zigzag: every app in its icon's colour, with cream or deeper zigzags.
    public static void Zigzag(AppIdentity app, ref uint yarn, ref Chart? chart)
    {
        var rule = Apps.Rule(app);
        bool personal = rule is { Personal: true };
        Entry? e = personal ? null : EntryFor(app);
        if (e is { Ok: true }) yarn = e.Base;
        if (chart is { Custom: true }) return;          // a zigzag the user drew is shown as drawn
        uint contrast = Knit.ZigzagContrast(yarn);
        if (contrast == Knit.Cream) return;
        e ??= EntryFor(app);
        chart = EntryChart(e, "zigzag", "zigzag", contrast) ?? chart;
    }
}
