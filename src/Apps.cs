using System.Diagnostics;
using System.Globalization;

namespace WindowSweaters;

internal sealed record AppRule(string Match, uint Color, string Chart, bool Personal);

/// Who owns a window. `Key` is the lower-cased executable name (stable across updates and
/// localisation, the Windows counterpart of a macOS bundle id). `Names` are the strings
/// matched against colourway rules, most descriptive first.
internal sealed class AppIdentity
{
    public required string Key;
    public required string Name;
    public required string[] Names;
    public string? ExePath;
    public uint Pid;
}

/// Per-app colourways, ported from apps.c, plus Windows aliases.
internal static class Apps
{
    public static readonly List<AppRule> UserRules = [];
    public static bool PatternByApp = true;
    public static Chart? ActiveChart;          // global pattern when !PatternByApp; null = plain

    // The collection stays available even when apps.conf is absent. User rules win.
    static readonly AppRule[] Collection =
    [
        new("Finder", 0xff258de0u, "atelier-finder", false),
        new("Microsoft Teams", 0xff9283c1u, "atelier-teams", false),
        new("MSTeams", 0xff9283c1u, "atelier-teams", false),
        new("ms-teams", 0xff9283c1u, "atelier-teams", false),
        new("Teams", 0xff9283c1u, "atelier-teams", false),
        new("Claude", 0xffd58561u, "atelier-claude", false),
        new("Codex", 0xff0a84ffu, "atelier-chatgpt", false),
        new("Spotify", 0xff497641u, "atelier-spotify", false),
        new("Notion", 0xfff5f5f2u, "atelier-notion", false),
        new("WhatsApp", 0xff6ea77bu, "atelier-whatsapp", false),
        new("Figma", 0xffad7197u, "atelier-figma", false),
        new("Google Chrome", 0xfff4f0e6u, "atelier-chrome", false),
        new("Chrome", 0xfff4f0e6u, "atelier-chrome", false),
        new("Paper", 0xff83ade8u, "atelier-paper", false),
        new("Chromium", 0xfff4f0e6u, "atelier-chrome", false),
        new("Safari", 0xff268ed8u, "atelier-safari", false),
        new("Firefox", 0xff643a9au, "atelier-firefox", false),
        new("Cursor", 0xff26251eu, "atelier-cursor", false),
        new("Slack", 0xff542a52u, "atelier-slack", false),
        new("zoom.us", 0xff2877ebu, "atelier-zoom", false),
        new("Zoom", 0xff2877ebu, "atelier-zoom", false),
        new("Telegram", 0xff389eceu, "atelier-telegram", false),
        new("Messages", 0xff55af51u, "atelier-messages", false),
        new("Mail", 0xff2986ceu, "atelier-mail", false),
        new("Notes", 0xfff6f0d9u, "atelier-notes", false),
        new("Calendar", 0xfff7f3e9u, "atelier-calendar", false),
        new("Reminders", 0xfff6f3ebu, "atelier-reminders", false),
        new("Music", 0xffe64e70u, "atelier-music", false),
        new("Apple Music", 0xffe64e70u, "atelier-music", false),
        new("Photos", 0xfff8f0dbu, "atelier-photos", false),
        new("Preview", 0xff597bafu, "atelier-preview", false),
        new("Microsoft Word", 0xff2855a1u, "atelier-word", false),
        new("Microsoft Excel", 0xff28674fu, "atelier-excel", false),
        new("Microsoft PowerPoint", 0xffb9573du, "atelier-powerpoint", false),
        new("Microsoft Outlook", 0xff176bb7u, "atelier-outlook", false),
        new("Code", 0xff237cafu, "atelier-vscode", false),
        new("Visual Studio Code", 0xff237cafu, "atelier-vscode", false),
        new("Adobe Photoshop", 0xff182f45u, "atelier-photoshop", false),
        new("Adobe Illustrator", 0xff4e3029u, "atelier-illustrator", false),
        new("ChatGPT", 0xff0a84ffu, "atelier-chatgpt", false),
        new("Grok Bot", 0xff34383bu, "atelier-grok", false),
        new("Discord", 0xff5865f2u, "checker", false),
        new("Granola", 0xff292e2au, "atelier-granola", false),
        new("Terminal", 0xff303c35u, "atelier-terminal", false),
        new("ghostty", 0xff2b3350u, "atelier-ghostty", false),

        // ---- Windows counterparts, borrowing the closest macOS sweater ----
        new("Windows Explorer", 0xff258de0u, "atelier-finder", false),
        new("File Explorer", 0xff258de0u, "atelier-finder", false),
        new("explorer", 0xff258de0u, "atelier-finder", false),
        new("Windows Terminal", 0xff303c35u, "atelier-terminal", false),
        new("WindowsTerminal", 0xff303c35u, "atelier-terminal", false),
        new("Windows Command Processor", 0xff303c35u, "atelier-terminal", false),
        new("Windows PowerShell", 0xff303c35u, "atelier-terminal", false),
        new("PowerShell", 0xff303c35u, "atelier-terminal", false),
        new("Notepad", 0xfff6f0d9u, "atelier-notes", false),
        new("WINWORD", 0xff2855a1u, "atelier-word", false),
        new("EXCEL", 0xff28674fu, "atelier-excel", false),
        new("POWERPNT", 0xffb9573du, "atelier-powerpoint", false),
        new("OUTLOOK", 0xff176bb7u, "atelier-outlook", false),
        new("olk", 0xff176bb7u, "atelier-outlook", false),
        new("Telegram Desktop", 0xff389eceu, "atelier-telegram", false),
    ];

    public const string DefaultConf =
        "# Window Sweaters — per-app colourways\r\n" +
        "#\r\n" +
        "# <app name> = #RRGGBB [chart]\r\n" +
        "#\r\n" +
        "# The name is matched as a case-insensitive prefix of the app's description\r\n" +
        "# (as shown in Task Manager, e.g. \"Google Chrome\") or its .exe name\r\n" +
        "# (e.g. \"chrome\"). The optional chart name is a built-in chart or any file\r\n" +
        "# in the charts folder, without .png. Personal rules take priority.\r\n" +
        "#\r\n" +
        "# Edit, then choose Reload Colourways from the tray menu.\r\n" +
        "\r\n" +
        "# 37 apps already have their own yarn and chart. Uncomment to customise:\r\n" +
        "# Claude = #D58561 atelier-claude\r\n";

    public static void Load(string path)
    {
        UserRules.Clear();
        if (!File.Exists(path))
        {
            try { File.WriteAllText(path, DefaultConf); } catch { }
            return;
        }
        string[] lines;
        try { lines = File.ReadAllLines(path); } catch { return; }
        foreach (var raw in lines)
        {
            if (UserRules.Count >= 64) break;
            var s = raw.Trim();
            if (s.Length == 0 || s[0] == '#') continue;
            int eq = s.IndexOf('=');
            if (eq < 0) continue;
            var name = s[..eq].Trim();
            var val = s[(eq + 1)..].Trim();
            if (name.Length is 0 or >= 64 || val.Length < 7 || val[0] != '#') continue;
            if (val.Length > 7 && !char.IsWhiteSpace(val[7])) continue;
            if (!uint.TryParse(val.AsSpan(1, 6), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb)) continue;
            var tail = val[7..].Trim();
            string chart = "";
            if (tail.Length > 0 && tail[0] != '#')
            {
                var parts = tail.Split((char[])[' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries);
                chart = parts[0];
                if (chart.Length >= 64) continue;
                if (parts.Length > 1 && parts[1].Trim() is { Length: > 0 } rest && rest[0] != '#') continue;
            }
            UserRules.Add(new AppRule(name, 0xff000000u | rgb, chart, true));
        }
    }

    static AppRule? Longest(IEnumerable<AppRule> rules, string name, ref int bestLen)
    {
        AppRule? best = null;
        foreach (var r in rules)
        {
            int n = r.Match.Length;
            if (n > 0 && n > bestLen && name.StartsWith(r.Match, StringComparison.OrdinalIgnoreCase))
            {
                best = r; bestLen = n;
            }
        }
        return best;
    }

    /// Longest-prefix user match, then built-in, across the app's candidate names.
    public static AppRule? Rule(AppIdentity app)
    {
        foreach (var source in new[] { (IEnumerable<AppRule>)UserRules, Collection })
        {
            foreach (var name in app.Names)
            {
                int len = 0;
                var r = Longest(source, name, ref len);
                if (r != null) return r;
            }
        }
        return null;
    }

    public static Chart? PatternFor(AppIdentity app)
    {
        if (!PatternByApp) return ActiveChart;
        var rule = Rule(app);
        return rule is { Chart.Length: > 0 } ? Charts.Find(rule.Chart) : null;
    }

    // ---- which apps wear sweaters ---------------------------------------------------
    public static bool OnByDefault = true;
    public static readonly List<string> Exceptions = [];

    public static bool Hidden(string key)
    {
        bool listed = Exceptions.Contains(key, StringComparer.OrdinalIgnoreCase);
        return OnByDefault ? listed : !listed;
    }

    public static void SetHidden(string key, bool hidden)
    {
        bool listed = OnByDefault ? hidden : !hidden;
        Exceptions.RemoveAll(e => string.Equals(e, key, StringComparison.OrdinalIgnoreCase));
        if (listed) Exceptions.Add(key);
    }

    public static void SetAll(bool on)
    {
        OnByDefault = on;
        Exceptions.Clear();
    }

    // ---- identity --------------------------------------------------------------------
    static readonly Dictionary<uint, AppIdentity> s_byPid = [];

    public static AppIdentity Identify(IntPtr hwnd)
    {
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        // UWP apps are framed by ApplicationFrameHost; the real app owns a child CoreWindow.
        if (Native.ClassName(hwnd) == "ApplicationFrameWindow")
        {
            uint inner = 0;
            Native.EnumChildWindows(hwnd, (child, _) =>
            {
                Native.GetWindowThreadProcessId(child, out uint p);
                if (p != pid) { inner = p; return false; }
                return true;
            }, IntPtr.Zero);
            if (inner != 0) pid = inner;
        }
        // Paths are re-read each time, so a recycled pid can't inherit another app's sweater.
        string? path = Native.ProcessPath(pid);
        if (s_byPid.TryGetValue(pid, out var known) && known.ExePath == path) return known;

        string exe = path != null ? Path.GetFileNameWithoutExtension(path) : $"pid{pid}";
        string? description = null;
        if (path != null)
        {
            try { description = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim(); } catch { }
        }
        var names = new List<string>();
        if (!string.IsNullOrWhiteSpace(description)) names.Add(description!);
        names.Add(exe);
        var app = new AppIdentity
        {
            Key = exe.ToLowerInvariant(),
            Name = string.IsNullOrWhiteSpace(description) ? exe : description!,
            Names = [.. names],
            ExePath = path,
            Pid = pid,
        };
        if (s_byPid.Count > 512) s_byPid.Clear();
        s_byPid[pid] = app;
        return app;
    }

    public static void ForgetPid(uint pid) => s_byPid.Remove(pid);
}
