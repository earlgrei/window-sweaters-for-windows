using System.Text.Json;
using Microsoft.Win32;

namespace WindowSweaters;

/// Menu choices survive a restart. Stored as JSON next to apps.conf and the charts folder.
internal sealed class Settings
{
    public bool On { get; set; } = true;
    public string Yarn { get; set; } = "stockinette";
    public int Basket { get; set; } = 3;
    public float Width { get; set; } = 12f;
    public float Gauge { get; set; } = 6f;
    public string Chart { get; set; } = "none";
    public bool PatternByApp { get; set; } = true;
    public int Anchor { get; set; }
    public bool AppsOnByDefault { get; set; } = true;
    public List<string> AppExceptions { get; set; } = [];
    public string Season { get; set; } = "winter";
    public bool FollowCalendar { get; set; }
    public bool Twinkle { get; set; } = true;

    public static string Dir
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Window Sweaters");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string FilePath => Path.Combine(Dir, "settings.json");
    public static string AppsConfPath => Path.Combine(Dir, "apps.conf");

    public static string ChartsDir
    {
        get
        {
            var dir = Path.Combine(Dir, "charts");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), s_json) ?? new Settings();
        }
        catch { }
        return new Settings();
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, s_json)); } catch { }
    }

    // ---- Start with Windows (HKCU Run key, per user, opt-in from the menu) ----
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "Window Sweaters";

    public static bool StartsWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunName) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(RunName, $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue(RunName, false);
        }
    }
}
