using static WindowSweaters.Native;
using Timer = System.Windows.Forms.Timer;

namespace WindowSweaters;

/// Follows every top-level window through WinEvent hooks, with a slow reconcile pass as a
/// safety net, and keeps one Sweater per eligible window.
internal sealed class Tracker : IDisposable
{
    public static int StyleVersion;

    readonly Dictionary<IntPtr, Sweater> _sweaters = [];
    readonly List<IntPtr> _hooks = [];
    readonly WinEventDelegate _proc;
    readonly Timer _reconcile = new() { Interval = 1000 };
    readonly Timer _restack = new() { Interval = 60 };
    readonly Timer _twinkle = new() { Interval = 200 };
    Season _season = Seasons.Current;
    readonly uint _ownPid = (uint)Environment.ProcessId;

    static readonly HashSet<string> s_skipClasses =
    [
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow",
        "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow", "TopLevelWindowForOverflowXamlIsland",
        "Shell_InputSwitchTopLevelWindow", "MultitaskingViewFrame", "ForegroundStaging", "#32768", "#32769",
        "TaskListThumbnailWnd", "Xaml_WindowedPopupClass", "SysShadow", "tooltips_class32",
    ];

    public Tracker()
    {
        _proc = OnEvent;
        _reconcile.Tick += (_, _) => Reconcile();
        _restack.Tick += (_, _) => { _restack.Stop(); RestackAll(); };
        _twinkle.Tick += (_, _) =>
        {
            double now = Sweater.Now;
            foreach (var s in _sweaters.Values) s.Twinkle(now);
        };
    }

    /// Run the holiday lights only while they are showing and twinkling.
    void UpdateAnimation()
    {
        bool on = _hooks.Count > 0 && Program.Settings.On && Seasons.Animated;
        if (on && !_twinkle.Enabled) _twinkle.Start();
        else if (!on && _twinkle.Enabled) _twinkle.Stop();
    }

    public void Start()
    {
        Hook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND);
        Hook(EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZEEND);
        Hook(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND);
        Hook(EVENT_OBJECT_DESTROY, EVENT_OBJECT_REORDER);
        Hook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE);
        Hook(EVENT_OBJECT_CLOAKED, EVENT_OBJECT_UNCLOAKED);
        Reconcile();
        _reconcile.Start();
        UpdateAnimation();
    }

    void Hook(uint min, uint max)
    {
        IntPtr h = SetWinEventHook(min, max, IntPtr.Zero, _proc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        if (h != IntPtr.Zero) _hooks.Add(h);
    }

    /// Static shape of a window that could ever wear a sweater.
    bool IsCandidate(IntPtr h)
    {
        if (!IsWindowVisible(h) || GetAncestor(h, GA_ROOT) != h) return false;
        long st = Style(h), ex = ExStyle(h);
        if ((st & WS_CHILD) != 0) return false;
        if ((ex & (WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE)) != 0) return false;
        if ((st & WS_CAPTION) != WS_CAPTION && (st & WS_THICKFRAME) == 0) return false;
        GetWindowThreadProcessId(h, out uint pid);
        if (pid == _ownPid) return false;
        return !s_skipClasses.Contains(ClassName(h));
    }

    /// Whether it should wear one right now.
    bool ShouldWear(Sweater s)
    {
        IntPtr h = s.Target;
        if (!Program.Settings.On || !IsWindowVisible(h) || IsIconic(h) || IsZoomed(h) || IsCloaked(h)) return false;
        if (Apps.Hidden(s.App.Key)) return false;
        var vis = VisibleBounds(h);
        var mon = MonitorBounds(h);
        if (!mon.IsEmpty && vis.Contains(mon)) return false;   // fullscreen
        return true;
    }

    Sweater? Track(IntPtr h)
    {
        if (_sweaters.TryGetValue(h, out var s)) return s;
        if (!IsCandidate(h)) return null;
        s = new Sweater(h, Apps.Identify(h));
        _sweaters[h] = s;
        return s;
    }

    void Drop(IntPtr h)
    {
        if (_sweaters.Remove(h, out var s)) s.Dispose();
    }

    void Refresh(Sweater s, bool force = false)
    {
        bool wasShown = s.Shown;
        s.Update(ShouldWear(s), force);
        if (s.Shown && !wasShown) ScheduleRestack();
    }

    void OnEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (evt == EVENT_OBJECT_REORDER)
        {
            // Top-level z-order changes are reported against the desktop; ignore apps reordering their own children.
            if (hwnd == GetDesktopWindow()) ScheduleRestack();
            return;
        }
        if (hwnd == IntPtr.Zero || idObject != OBJID_WINDOW || idChild != CHILDID_SELF) return;
        try
        {
            switch (evt)
            {
                case EVENT_OBJECT_DESTROY:
                    Drop(hwnd);
                    break;
                case EVENT_OBJECT_HIDE:
                case EVENT_OBJECT_CLOAKED:
                case EVENT_SYSTEM_MINIMIZESTART:
                    if (_sweaters.TryGetValue(hwnd, out var hidden)) hidden.Hide();
                    break;
                case EVENT_OBJECT_LOCATIONCHANGE:
                    if (_sweaters.TryGetValue(hwnd, out var moved)) Refresh(moved);
                    else if (GetAncestor(hwnd, GA_ROOT) == hwnd && Track(hwnd) is { } added) Refresh(added);
                    break;
                default:   // show, uncloak, restore, foreground, move/size start and end
                    if (Track(hwnd) is { } s) Refresh(s);
                    ScheduleRestack();
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    void ScheduleRestack()
    {
        RestackAll();
        // and once more after the window manager settles
        _restack.Stop();
        _restack.Start();
    }

    void RestackAll()
    {
        foreach (var s in _sweaters.Values) s.Restack();
    }

    /// Enumerate every top-level window: pick up anything the hooks missed, drop the dead.
    public void Reconcile()
    {
        // Following the calendar: change outfits when the season turns.
        if (Seasons.Current != _season) { RefreshAll(); return; }

        var seen = new HashSet<IntPtr>();
        EnumWindows((h, _) => { seen.Add(h); return true; }, IntPtr.Zero);
        foreach (var dead in _sweaters.Keys.Where(k => !seen.Contains(k) || !IsWindow(k)).ToList()) Drop(dead);
        foreach (var h in seen)
        {
            if (!_sweaters.ContainsKey(h) && IsCandidate(h)) Track(h);
        }
        foreach (var s in _sweaters.Values)
        {
            // UWP frames can gain their real app after they first appear.
            if (s.App.Key == "applicationframehost") s.App = Apps.Identify(s.Target);
            Refresh(s);
        }
        RestackAll();
    }

    /// Settings changed: redraw every sweater.
    public void RefreshAll()
    {
        _season = Seasons.Current;
        StyleVersion++;
        UpdateAnimation();
        foreach (var s in _sweaters.Values) Refresh(s, force: true);
        RestackAll();
    }

    /// The apps that currently have a window that could wear a sweater, for the Apps menu.
    public IReadOnlyList<AppIdentity> Owners()
        => _sweaters.Values.Select(s => s.App)
            .Where(a => a.Key != "applicationframehost")
            .GroupBy(a => a.Key).Select(g => g.First())
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    public void Dispose()
    {
        _reconcile.Stop();
        _restack.Stop();
        _twinkle.Stop();
        foreach (var h in _hooks) UnhookWinEvent(h);
        _hooks.Clear();
        foreach (var s in _sweaters.Values) s.Dispose();
        _sweaters.Clear();
    }
}
