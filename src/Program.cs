namespace WindowSweaters;

internal static class Program
{
    public static Settings Settings = new();

    [STAThread]
    static int Main(string[] args)
    {
        if (args is ["--version" or "-v", ..])
        {
            Console.WriteLine("Window Sweaters for Windows 1.5.0");
            return 0;
        }
        if (args is ["--write-icon", var path, ..])
        {
            File.WriteAllBytes(path, YarnIcon.Ico(false, 16, 20, 24, 32, 40, 48, 64, 128, 256));
            return 0;
        }
        if (args is ["--swatch", var outPath, ..])
        {
            // Developer aid: render a sample sweater to a PNG (see README).
            Preview.Render(outPath, args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null, args.Length > 4 ? args[4] : null);
            return 0;
        }

        if (args is ["--menu-test", var report, ..])
        {
            // Developer aid: build the tray menu, click through every style choice, and
            // write the menu to a text file. Settings are restored afterwards.
            ApplicationConfiguration.Initialize();
            string? saved = File.Exists(Settings.FilePath) ? File.ReadAllText(Settings.FilePath) : null;
            try
            {
                using var app = new TrayApp(live: false);
                File.WriteAllText(report, app.MenuSelfTest());
            }
            finally
            {
                if (saved != null) File.WriteAllText(Settings.FilePath, saved);
                else File.Delete(Settings.FilePath);
            }
            return 0;
        }

        using var mutex = new Mutex(true, @"Local\WindowSweaters.SingleInstance", out bool first);
        if (!first) return 0;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp());
        return 0;
    }
}
