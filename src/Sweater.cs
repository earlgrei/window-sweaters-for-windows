using System.Drawing.Imaging;
using static WindowSweaters.Native;

namespace WindowSweaters;

/// One click-through, per-pixel-alpha layered window holding one side of a sweater.
internal sealed class Strip : NativeWindow, IDisposable
{
    IntPtr _dc, _bmp, _old, _bits;
    int _w, _h;
    public bool Visible { get; private set; }
    public Point Offset { get; set; }     // position inside the sweater's outer rect
    public Size Size { get; set; }

    public Strip()
    {
        CreateHandle(new CreateParams
        {
            Caption = "Window Sweaters",
            Style = unchecked((int)WS_POPUP),
            ExStyle = (int)(WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE),
            X = -32000, Y = -32000, Width = 1, Height = 1,
        });
    }

    /// A top-down 32bpp premultiplied DIB the size of this strip, reused while the size holds.
    public IntPtr Bits(int w, int h)
    {
        if (_bits != IntPtr.Zero && w == _w && h == _h) return _bits;
        FreeDib();
        var bmi = new BITMAPINFOHEADER
        {
            biSize = System.Runtime.InteropServices.Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32,
        };
        IntPtr screen = GetDC(IntPtr.Zero);
        _dc = CreateCompatibleDC(screen);
        _bmp = CreateDIBSection(screen, ref bmi, 0, out _bits, IntPtr.Zero, 0);
        ReleaseDC(IntPtr.Zero, screen);
        _old = SelectObject(_dc, _bmp);
        _w = w; _h = h;
        return _bits;
    }

    public void Present(int x, int y)
    {
        GdiFlush();
        var dst = new POINT(x, y);
        var size = new SIZE(_w, _h);
        var src = new POINT(0, 0);
        var blend = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };
        UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, ref size, _dc, ref src, 0, ref blend, ULW_ALPHA);
    }

    public void Show(IntPtr after)
    {
        SetWindowPos(Handle, after, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_SHOWWINDOW);
        Visible = true;
    }

    public void Hide()
    {
        if (!Visible) return;
        ShowWindow(Handle, SW_HIDE);
        Visible = false;
    }

    void FreeDib()
    {
        if (_dc != IntPtr.Zero) { SelectObject(_dc, _old); DeleteDC(_dc); _dc = IntPtr.Zero; }
        if (_bmp != IntPtr.Zero) { DeleteObject(_bmp); _bmp = IntPtr.Zero; }
        _bits = IntPtr.Zero; _w = _h = 0;
    }

    public void Dispose()
    {
        DestroyHandle();
        FreeDib();
    }
}

/// The knitted border around one window: four strips (top, bottom, left, right) sitting
/// directly beneath it in z-order. Each strip reaches `tuck` pixels under the window, so a
/// frame of lag while dragging never shows bare desktop.
internal sealed class Sweater : IDisposable
{
    static readonly bool s_win11 = Environment.OSVersion.Version.Build >= 22000;

    public readonly IntPtr Target;
    public AppIdentity App;
    readonly Strip[] _strips = [new(), new(), new(), new()];

    Rectangle _outer;
    int _band, _tuck, _style = -1;
    float _radius;
    uint _yarn;
    int _chartId = int.MinValue;
    public bool Shown { get; private set; }

    public Sweater(IntPtr target, AppIdentity app)
    {
        Target = target;
        App = app;
    }

    float CornerRadius(float scale)
    {
        if (!s_win11) return 0f;
        if (DwmGetWindowAttribute(Target, DWMWA_WINDOW_CORNER_PREFERENCE, out int pref, sizeof(int)) == 0)
        {
            if (pref == 1) return 0f;            // DWMWCP_DONOTROUND
            if (pref == 3) return 4f * scale;    // DWMWCP_ROUNDSMALL
        }
        return 8f * scale;
    }

    void ResolveLook(out uint yarn, out Chart? chart)
    {
        yarn = Knit.ColorForApp(App.Name);
        chart = Apps.PatternFor(App);
        var rule = Apps.Rule(App);
        if (rule != null) yarn = rule.Color;
        if (AutoYarn.ZigzagActive)
            AutoYarn.Zigzag(App, ref yarn, ref chart);
        else if (rule == null && AutoYarn.TryAuto(App, out uint autoYarn, out Chart? autoChart))
        {
            yarn = autoYarn;
            if (autoChart != null) chart = autoChart;
        }
    }

    /// Re-read the window's frame and bring the sweater up to date: a cheap move when only
    /// the position changed, a redraw when the size or style changed.
    public void Update(bool wear, bool force = false)
    {
        if (!wear) { Hide(); return; }
        var vis = VisibleBounds(Target);
        if (vis.Width < 64 || vis.Height < 48) { Hide(); return; }

        float scale = Math.Max(1u, GetDpiForWindow(Target)) / 96f;
        int band = Math.Max(2, (int)MathF.Round(Program.Settings.Width * scale));
        float radius = CornerRadius(scale);
        int tuck = Math.Max((int)MathF.Round(Knit.G.Tuck * scale), (int)MathF.Ceiling(radius) + 2);
        var outer = Rectangle.Inflate(vis, band, band);

        ResolveLook(out uint yarn, out Chart? chart);
        int chartId = chart?.Id ?? -1;

        bool redraw = force || !Shown || outer.Size != _outer.Size || band != _band || tuck != _tuck
                      || radius != _radius || yarn != _yarn || chartId != _chartId || _style != Tracker.StyleVersion;
        if (redraw)
        {
            Render(outer, band, tuck, radius, yarn, chart);
            _band = band; _tuck = tuck; _radius = radius; _yarn = yarn; _chartId = chartId; _style = Tracker.StyleVersion;
        }
        else if (outer.Location != _outer.Location)
        {
            Move(outer.Location);
        }
        _outer = outer;
    }

    void Move(Point origin)
    {
        IntPtr info = BeginDeferWindowPos(4);
        foreach (var s in _strips)
        {
            if (!s.Visible || info == IntPtr.Zero) continue;
            info = DeferWindowPos(info, s.Handle, IntPtr.Zero, origin.X + s.Offset.X, origin.Y + s.Offset.Y, 0, 0,
                SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        }
        if (info != IntPtr.Zero) EndDeferWindowPos(info);
    }

    unsafe void Render(Rectangle outer, int band, int tuck, float radius, uint yarn, Chart? chart)
    {
        int ow = outer.Width, oh = outer.Height;
        int t = Math.Min(band + tuck, Math.Min(ow, oh) / 2);
        var rects = new[]
        {
            new Rectangle(0, 0, ow, t),                 // top
            new Rectangle(0, oh - t, ow, t),            // bottom
            new Rectangle(0, t, t, oh - 2 * t),         // left
            new Rectangle(ow - t, t, t, oh - 2 * t),    // right
        };
        var win = new RectangleF(band, band, ow - 2 * band, oh - 2 * band);
        float orad = radius + band;

        for (int i = 0; i < 4; i++)
        {
            var r = rects[i];
            var strip = _strips[i];
            if (r.Width <= 0 || r.Height <= 0) { strip.Hide(); strip.Size = Size.Empty; continue; }
            IntPtr bits = strip.Bits(r.Width, r.Height);
            using (var bmp = new Bitmap(r.Width, r.Height, r.Width * 4, PixelFormat.Format32bppPArgb, bits))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.TranslateTransform(-r.X, -r.Y);
                Knit.DrawRing(g, win, radius, band, yarn, chart, tuck);
            }
            Mask((uint*)bits, r, ow, oh, orad);
            strip.Offset = r.Location;
            strip.Size = r.Size;
            strip.Present(outer.X + r.X, outer.Y + r.Y);
        }
        if (!Shown)
        {
            Shown = true;
            Restack();
        }
    }

    // Anti-aliased outer rounded edge: coverage from the rounded rect's signed distance.
    static unsafe void Mask(uint* p, Rectangle r, int ow, int oh, float rad)
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

    /// Keep the strips directly beneath the window, matching its topmost band.
    public void Restack()
    {
        if (!Shown) return;
        bool topmost = (ExStyle(Target) & WS_EX_TOPMOST) != 0;
        IntPtr after = Target;
        foreach (var s in _strips)
        {
            if (s.Size.Width <= 0 || s.Size.Height <= 0) continue;
            bool stripTop = (ExStyle(s.Handle) & WS_EX_TOPMOST) != 0;
            if (stripTop && !topmost)
                SetWindowPos(s.Handle, new IntPtr(-2) /* HWND_NOTOPMOST */, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
            if (!s.Visible || GetWindow(after, GW_HWNDNEXT) != s.Handle) s.Show(after);
            after = s.Handle;
        }
    }

    public void Hide()
    {
        foreach (var s in _strips) s.Hide();
        Shown = false;
    }

    public void Dispose()
    {
        foreach (var s in _strips) s.Dispose();
    }
}
