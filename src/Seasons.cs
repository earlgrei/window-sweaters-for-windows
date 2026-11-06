namespace WindowSweaters;

internal enum Season { Winter, Spring, Summer, Fall, Holiday }

/// What the windows wear: sweaters in winter, flower rings in spring, inner tubes in summer,
/// twigs and vines in fall, and a twinkling garland in December.
internal static class Seasons
{
    public static bool FollowCalendar;
    public static Season Chosen = Season.Winter;
    public static bool Twinkle = true;

    public static Season Current => FollowCalendar ? ForDate(DateTime.Now) : Chosen;

    /// Meteorological seasons, northern hemisphere, with December kept for the garland.
    public static Season ForDate(DateTime d) => d.Month switch
    {
        12 => Season.Holiday,
        1 or 2 => Season.Winter,
        3 or 4 or 5 => Season.Spring,
        6 or 7 or 8 => Season.Summer,
        _ => Season.Fall,
    };

    public static bool Animated => Current == Season.Holiday && Twinkle;

    public static readonly (Season Season, string Key, string Title, string Months)[] All =
    [
        (Season.Winter, "winter", "Winter Sweaters", "January and February"),
        (Season.Spring, "spring", "Spring Flower Rings", "March to May"),
        (Season.Summer, "summer", "Summer Inner Tubes", "June to August"),
        (Season.Fall, "fall", "Fall Twigs & Vines", "September to November"),
        (Season.Holiday, "holiday", "Holiday Garland", "December"),
    ];

    public static string Key(Season s) => All.First(a => a.Season == s).Key;

    public static Season Parse(string? key)
    {
        foreach (var a in All) if (a.Key == key) return a.Season;
        return Season.Winter;
    }
}

/// The band's rounded ring, as a family of rounded rects inset from the outer edge.
/// Lets decorations be placed by arc length so they follow the corners continuously.
internal sealed class Ring(float width, float height, float orad)
{
    public float W => width;
    public float H => height;

    public (float X, float Y, float W, float H, float R) Inset(float d)
    {
        float w = Math.Max(1f, width - 2 * d), h = Math.Max(1f, height - 2 * d);
        float r = Math.Clamp(orad - d, 0f, Math.Min(w, h) / 2f);
        return (d, d, w, h, r);
    }

    public float Length(float d)
    {
        var (_, _, w, h, r) = Inset(d);
        return 2 * (w - 2 * r) + 2 * (h - 2 * r) + 2 * MathF.PI * r;
    }

    /// Point and unit tangent at arc length `s` along the path inset by `d`, running clockwise
    /// from the top edge. The inward normal is (-t.Y, t.X).
    public void At(float s, float d, out PointF p, out PointF t)
    {
        var (x, y, w, h, r) = Inset(d);
        float a = w - 2 * r, b = h - 2 * r, q = MathF.PI * r / 2f;
        float total = 2 * a + 2 * b + 4 * q;
        s %= total; if (s < 0) s += total;

        if (s < a) { p = new(x + r + s, y); t = new(1, 0); return; }
        s -= a;
        if (s < q) { Arc(x + w - r, y + r, -MathF.PI / 2, s, r, out p, out t); return; }
        s -= q;
        if (s < b) { p = new(x + w, y + r + s); t = new(0, 1); return; }
        s -= b;
        if (s < q) { Arc(x + w - r, y + h - r, 0, s, r, out p, out t); return; }
        s -= q;
        if (s < a) { p = new(x + w - r - s, y + h); t = new(-1, 0); return; }
        s -= a;
        if (s < q) { Arc(x + r, y + h - r, MathF.PI / 2, s, r, out p, out t); return; }
        s -= q;
        if (s < b) { p = new(x, y + h - r - s); t = new(0, -1); return; }
        s -= b;
        Arc(x + r, y + r, MathF.PI, s, r, out p, out t);
    }

    static void Arc(float cx, float cy, float start, float s, float r, out PointF p, out PointF t)
    {
        float th = start + (r > 0 ? s / r : 0);
        p = new(cx + r * MathF.Cos(th), cy + r * MathF.Sin(th));
        t = new(-MathF.Sin(th), MathF.Cos(th));
    }

    /// A closed polyline following the path inset by `d`, displaced along the normal by offset(s).
    public PointF[] Trace(float d, Func<float, float> offset, float step = 3f)
    {
        float L = Length(d);
        int n = Math.Max(24, (int)(L / step));
        var pts = new PointF[n];
        for (int i = 0; i < n; i++)
        {
            float s = i * L / n;
            At(s, d, out var p, out var t);
            float o = offset(s);
            pts[i] = new PointF(p.X - t.Y * o, p.Y + t.X * o);
        }
        return pts;
    }
}
