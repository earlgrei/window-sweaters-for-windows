using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;

namespace WindowSweaters;

/// The notification-area icon and its menu: the Windows counterpart of the macOS menu bar item.
internal sealed class TrayApp : ApplicationContext
{
    readonly NotifyIcon _icon;
    readonly ContextMenuStrip _menu = new();
    readonly Tracker _tracker = new();
    Settings S => Program.Settings;

    static readonly (string Name, string Title)[] Featured =
    [
        ("zigzag", "Zigzag"), ("picnic", "Picnic Checks"), ("ribbon", "Ribbon Stripes"),
        ("posy", "Little Bows"), ("twinkle", "Tiny Stars"), ("candy-stripe", "Candy Stripes"),
    ];

    static readonly (string Name, float Value)[] WidthChoices =
        [("Slim", 6), ("Narrow", 8), ("Medium", 12), ("Regular", 14), ("Wide", 18), ("Extra Wide", 28)];

    static readonly (string Name, float Rows)[] SizeChoices =
        [("Chunky", 3), ("Medium", 6), ("Fine", 10), ("Very Fine", 14)];

    public TrayApp(bool live = true)
    {
        Program.Settings = Settings.Load();
        Charts.Load(Settings.ChartsDir);
        Apps.Load(Settings.AppsConfPath);
        if (S.Chart != "none" && Charts.Find(S.Chart) == null) S.Chart = "none";
        EnsureMinimumGauge();
        ApplySettings();

        _menu.ShowCheckMargin = true;
        _menu.ShowImageMargin = true;
        _menu.Opening += (_, e) => { BuildMenu(); e.Cancel = false; };
        _icon = new NotifyIcon { ContextMenuStrip = _menu, Visible = live };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            // Left click opens the same menu, like the macOS menu bar item.
            typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(_icon, null);
        };
        UpdateIcon();

        if (!live) return;
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        _tracker.Start();
    }

    /// Build the menu, click every pattern, width and size choice, rebuild after each, and
    /// return the final menu as text. Throws on the first failure.
    internal string MenuSelfTest()
    {
        BuildMenu();
        var groups = new[] { "Pattern", "Border Width", "Stitch Size" };
        int clicks = 0;
        foreach (var group in groups)
        {
            int count = Find(group).DropDownItems.Count;
            for (int i = 0; i < count; i++)
            {
                BuildMenu();
                var items = Find(group).DropDownItems;
                if (i >= items.Count || items[i] is not ToolStripMenuItem { DropDownItems.Count: 0 } mi) continue;
                mi.PerformClick();
                clicks++;
            }
        }
        BuildMenu();
        Find("Pattern").DropDownItems.OfType<ToolStripMenuItem>().First(i => i.Text == "By App").PerformClick();
        BuildMenu();
        var sb = new System.Text.StringBuilder($"clicked {clicks} items\n");
        void Dump(ToolStripItemCollection items, string indent)
        {
            foreach (ToolStripItem it in items)
            {
                if (it is ToolStripSeparator) { sb.AppendLine(indent + "---"); continue; }
                var mi = (ToolStripMenuItem)it;
                sb.AppendLine($"{indent}{(mi.Checked ? "[x]" : "[ ]")} {mi.Text}{(mi.Image != null ? "  (swatch)" : "")}");
                Dump(mi.DropDownItems, indent + "    ");
            }
        }
        Dump(_menu.Items, "");
        return sb.ToString();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            _tracker.Dispose();
            _icon.Visible = false;
            _icon.Dispose();
            _menu.Dispose();
        }
        base.Dispose(disposing);
    }

    ToolStripMenuItem Find(string text) => _menu.Items.OfType<ToolStripMenuItem>().First(i => i.Text == text);

    void OnDisplayChanged(object? sender, EventArgs e) => _tracker.RefreshAll();

    // ---- settings ----------------------------------------------------------------------
    float MinimumRows()
    {
        bool colourwork = S.PatternByApp || S.Chart != "none";
        if (!colourwork) return 3f;
        if (!S.PatternByApp && Charts.Find(S.Chart) is { } c) return Math.Max(6, c.H);
        return 6f;
    }

    void EnsureMinimumGauge()
    {
        float min = MinimumRows();
        if (S.Gauge < min) S.Gauge = min;
    }

    void ApplySettings()
    {
        int stitch = Array.IndexOf(Knit.StitchNames, S.Yarn);
        Knit.Stitch = stitch < 0 ? Stitch.Stockinette : (Stitch)stitch;
        Knit.Basket = Math.Clamp(S.Basket, 0, Knit.Baskets.Length - 1);
        Knit.G.Rows = Math.Clamp(S.Gauge, 1.5f, 40f);
        Knit.AnchorCentre = S.Anchor == 1;
        S.Width = Math.Clamp(S.Width, 2f, 64f);
        Apps.PatternByApp = S.PatternByApp;
        Apps.ActiveChart = S.Chart == "none" ? null : Charts.Find(S.Chart);
        Apps.OnByDefault = S.AppsOnByDefault;
        Apps.Exceptions.Clear();
        Apps.Exceptions.AddRange(S.AppExceptions);
    }

    void Changed()
    {
        S.AppsOnByDefault = Apps.OnByDefault;
        S.AppExceptions = [.. Apps.Exceptions];
        S.Save();
        ApplySettings();
        UpdateIcon();
        _tracker.RefreshAll();
    }

    void UpdateIcon()
    {
        var old = _icon.Icon;
        _icon.Icon = YarnIcon.Tray(!S.On);
        old?.Dispose();
        bool noApps = !Apps.OnByDefault && Apps.Exceptions.Count == 0;
        string status = !S.On ? "sweaters are paused" : noApps ? "sweaters are off for all apps" : "sweaters are on";
        _icon.Text = "Window Sweaters — " + status;
    }

    void SelectChart(string name)
    {
        if (name == "by-app") S.PatternByApp = true;
        else { S.Chart = name; S.PatternByApp = false; }
        float min = name == "by-app" ? 6f : Math.Max(6, Charts.Find(name)?.H ?? 6);
        if (S.Gauge < min) S.Gauge = min;
        Changed();
    }

    void SelectPlain(Stitch stitch)
    {
        S.PatternByApp = false;
        S.Chart = "none";
        S.Yarn = Knit.StitchNames[(int)stitch];
        Changed();
    }

    void ReloadColourways()
    {
        Charts.Load(Settings.ChartsDir);
        Apps.Load(Settings.AppsConfPath);
        AutoYarn.Clear();
        Knit.FlushCache();
        if (S.Chart != "none" && Charts.Find(S.Chart) == null) S.Chart = "none";
        EnsureMinimumGauge();
        Changed();
    }

    // ---- menu ----------------------------------------------------------------------------
    static ToolStripMenuItem Item(string text, bool check, Action click, string? tip = null, Image? image = null)
    {
        var item = new ToolStripMenuItem(text) { Checked = check, ToolTipText = tip, Image = image };
        item.Click += (_, _) => click();
        return item;
    }

    static ToolStripMenuItem Submenu(ToolStripItemCollection parent, string text)
    {
        var item = new ToolStripMenuItem(text);
        parent.Add(item);
        return item;
    }

    static string ChartTitle(string name)
        => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.Replace('_', ' ').Replace('-', ' '));

    void BuildMenu()
    {
        var previous = _menu.Items.Cast<ToolStripItem>().ToArray();
        _menu.Items.Clear();
        foreach (var old in previous) old.Dispose();
        var items = _menu.Items;
        int sw = _menu.LogicalToDeviceUnits(36), sh = _menu.LogicalToDeviceUnits(14);
        _menu.ImageScalingSize = new Size(sw, sh);

        items.Add(Item("Show Sweater Borders", S.On, () => { S.On = !S.On; Changed(); }));

        // Apps
        var apps = Submenu(items, "Apps");
        var owners = _tracker.Owners();
        if (owners.Count == 0) apps.DropDownItems.Add(new ToolStripMenuItem("No Available Apps") { Enabled = false });
        foreach (var app in owners)
        {
            string key = app.Key;
            apps.DropDownItems.Add(Item(app.Name, !Apps.Hidden(key),
                () => { Apps.SetHidden(key, !Apps.Hidden(key)); Changed(); }, tip: key + ".exe"));
        }
        apps.DropDownItems.Add(new ToolStripSeparator());
        apps.DropDownItems.Add(Item("Turn On for All Apps", false, () => { Apps.SetAll(true); Changed(); }));
        apps.DropDownItems.Add(Item("Turn Off for All Apps", false, () => { Apps.SetAll(false); Changed(); },
            tip: "Then tick the apps you'd like to keep their sweaters. Apps you open later start off too."));

        // Pattern
        var pattern = Submenu(items, "Pattern");
        var p = pattern.DropDownItems;
        p.Add(Item("By App", S.PatternByApp, () => SelectChart("by-app"),
            tip: "Each app uses its own pattern and yarn colours."));
        p.Add(new ToolStripSeparator());
        foreach (var (title, stitch) in new[] { ("Stockinette", Stitch.Stockinette), ("Rib", Stitch.Rib) })
        {
            bool on = !S.PatternByApp && S.Chart == "none" && Knit.Stitch == stitch;
            p.Add(Item(title, on, () => SelectPlain(stitch), tip: "Plain knitting in each app's yarn colour.",
                image: SafeSwatch(0xffc46a4eu, null, sw, sh, stitch)));
        }
        p.Add(new ToolStripSeparator());
        foreach (var (name, title) in Featured)
        {
            var chart = Charts.Find(name);
            if (chart == null) continue;
            bool on = !S.PatternByApp && S.Chart == name;
            p.Add(Item(title, on, () => SelectChart(name),
                tip: name == "zigzag" ? "Uses colours from app icons, with cream or deeper zigzags for contrast."
                                      : "Each app keeps its own main yarn colour.",
                image: SafeSwatch(name == "zigzag" ? 0xffd58561u : 0xff4e8098u, chart, sw, sh)));
        }
        var custom = Charts.All.Where(c => c.Custom && !c.Generated && Featured.All(f => f.Name != c.Name)).ToList();
        if (custom.Count > 0)
        {
            p.Add(new ToolStripSeparator());
            var sub = Submenu(p, "Custom Patterns");
            foreach (var c in custom)
            {
                string name = c.Name;
                sub.DropDownItems.Add(Item(ChartTitle(name), !S.PatternByApp && S.Chart == name, () => SelectChart(name),
                    tip: $"Uses at least {Math.Max(6, c.H)} stitch rows to show the full pattern.",
                    image: SafeSwatch(0xff4e8098u, c, sw, sh)));
            }
        }
        if (!S.PatternByApp && S.Chart != "none" && Featured.All(f => f.Name != S.Chart) && custom.All(c => c.Name != S.Chart))
        {
            p.Add(new ToolStripSeparator());
            string current = S.Chart;
            p.Add(Item("Current: " + ChartTitle(current), true, () => SelectChart(current)));
        }

        // Border Width
        var widths = Submenu(items, "Border Width");
        bool standardWidth = false;
        foreach (var (name, value) in WidthChoices)
        {
            bool on = Math.Abs(S.Width - value) < 0.001f;
            standardWidth |= on;
            float v = value;
            widths.DropDownItems.Add(Item($"{name} · {value:g} px", on, () => { S.Width = v; Changed(); }));
        }
        if (!standardWidth)
        {
            widths.DropDownItems.Add(new ToolStripSeparator());
            widths.DropDownItems.Add(Item($"Custom · {S.Width:g} px", true, () => { }));
        }

        // Stitch Size
        var sizes = Submenu(items, "Stitch Size");
        float min = MinimumRows();
        bool standardSize = false;
        foreach (var (name, rows) in SizeChoices)
        {
            if (rows < min) continue;
            bool on = Math.Abs(S.Gauge - rows) < 0.001f;
            standardSize |= on;
            float r = rows;
            sizes.DropDownItems.Add(Item($"{name} · {rows:g} rows", on, () => { S.Gauge = r; Changed(); }));
        }
        if (!standardSize)
        {
            sizes.DropDownItems.Add(new ToolStripSeparator());
            sizes.DropDownItems.Add(Item($"Custom · {S.Gauge:g} rows", true, () => { }));
        }

        items.Add(new ToolStripSeparator());

        var colourways = Submenu(items, "Your Colourways");
        colourways.DropDownItems.Add(Item("Open Colourways Folder", false, () => Open(Settings.Dir)));
        colourways.DropDownItems.Add(Item("Edit apps.conf", false, () => Open("notepad.exe", Settings.AppsConfPath)));
        colourways.DropDownItems.Add(Item("Reload Colourways", false, ReloadColourways,
            tip: "Re-read apps.conf and the charts folder."));

        bool startup = false;
        try { startup = Settings.StartsWithWindows; } catch { }
        items.Add(Item("Start with Windows", startup, () =>
        {
            try { Settings.StartsWithWindows = !startup; } catch { }
        }));

        items.Add(new ToolStripSeparator());
        items.Add(Item("Quit Window Sweaters", false, Quit));
    }

    static Image? SafeSwatch(uint color, Chart? chart, int w, int h, Stitch? stitch = null)
    {
        var saved = Knit.Stitch;
        try
        {
            if (stitch is { } s) Knit.Stitch = s;
            return Knit.Swatch(color, chart, w, h);
        }
        catch { return null; }
        finally { Knit.Stitch = saved; }
    }

    static void Open(string target, string? args = null)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { Arguments = args ?? "", UseShellExecute = true });
        }
        catch { }
    }

    void Quit()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        _tracker.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
        ExitThread();
    }
}
