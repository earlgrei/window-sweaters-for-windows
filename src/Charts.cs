using System.Drawing.Imaging;

namespace WindowSweaters;

/// A colourwork chart: one pixel = one colour cell, like a paper knitting chart.
///   transparent cell -> the window's own yarn colour
///   opaque cell      -> that literal colour, knitted as a contrast yarn
/// Width and height are the pattern repeat. Row 0 sits at the outer edge of the band.
internal sealed class Chart
{
    static int s_nextId;

    public int Id { get; } = Interlocked.Increment(ref s_nextId);
    public string Name = "";
    public int W, H;
    public uint[] Px = [];          // ARGB, row 0 = top of the chart as authored
    public bool SolidCorners;       // solid yarn patch on the rounded arcs
    public uint CornerColor;        // 0 follows the base yarn
    public uint CuffColor;          // optional outer cuff, one third of the band
    public bool RoundDots;          // soften 2x2 dot blocks into filled circles
    public bool FittedRepeat;       // whole repeats per edge so the mitres match
    public bool Sculpted;           // raised, path-rendered stitches
    public bool Defined;            // larger, clearer stitches for flat colour blocks
    public bool Generated;          // built at runtime from an app icon
    public bool Custom;             // a PNG the user drew

    public Chart CloneWithYarn(uint yarn, string name)
    {
        var px = new uint[Px.Length];
        for (int i = 0; i < Px.Length; i++) px[i] = (Px[i] >> 24) >= 128 ? yarn : 0;
        return new Chart
        {
            Name = name, W = W, H = H, Px = px,
            SolidCorners = SolidCorners, CornerColor = CornerColor != 0 ? yarn : 0,
            CuffColor = CuffColor != 0 ? yarn : 0, RoundDots = RoundDots,
            FittedRepeat = FittedRepeat, Sculpted = Sculpted, Defined = Defined, Generated = true,
        };
    }
}

internal static class Charts
{
    public static readonly List<Chart> All = [];

    /// Bumped by every Load: anything caching a generated chart must rebuild it.
    public static int Generation { get; private set; }

    public static Chart? Find(string? name)
        => string.IsNullOrEmpty(name) ? null : All.FirstOrDefault(c => c.Name == name);

    // Data, not bundled PNGs, so the collection works on first launch. Ported verbatim
    // from the macOS app (src/chart.c).
    static readonly (string Name, string[] Rows, uint[] Yarn)[] Collection =
    [
        ("atelier-finder", ["..aaaa....aaaa..", "..aaaa....aaaa..", "..aaaa....aaaa..",
            "aa....aaaa....aa", "aa....aaaa....aa", "b...b......b...b"], [0xffb7e4f7u, 0xff24558bu]),
        ("atelier-terminal", ["aa....aa", "aa....aa", "aa....aa", "aa....aa", "aa....aa", "aa....aa"], [0xffa5bda0u]),
        ("atelier-grok", ["aaa.b..b.aaa", "aaa.b..b.aaa", "aaa.b..b.aaa",
            "aaa.b..b.aaa", "aaa.b..b.aaa", "aaa.b..b.aaa"], [0xffeeece4u, 0xff868a88u]),
        ("atelier-teams", ["a....aa....a", "aa........aa", ".aa......aa.",
            "..aa....aa..", "...aa..aa...", "....aaaa...."], [0xfff4efeeu]),
        ("atelier-claude", ["............", "..a.....a...", ".aaa...aaa..",
            "..a.....a...", "............", "............"], [0xfff7e8c5u]),
        ("atelier-codex", ["aa.....b....", "aa.....b....", "aa.....b....",
            "aa.....b....", "aa.....b....", "aa.....b...."], [0xffe6e6ceu, 0xffe9bfcbu]),
        ("atelier-spotify", ["...aaa...aaa", "...aaa...aaa", "...aaa...aaa",
            "aaa...aaa...", "aaa...aaa...", "aaa...aaa..."], [0xffe5d586u]),
        ("atelier-notion", ["...aaa...aaa", "...aaa...aaa", "...aaa...aaa",
            "aaa...aaa...", "aaa...aaa...", "aaa...aaa..."], [0xff494947u]),
        ("atelier-whatsapp", ["aaaa....b...", "aaaa....b...", "aaaa....b...",
            "aaaa....b...", "aaaa....b...", "aaaa....b..."], [0xffdde9bcu, 0xffe3a2b8u]),
        ("atelier-figma", ["aabb....cc..", "aabb....cc..", "aabb....cc..",
            "....ddee....", "....ddee....", "....ddee...."], [0xffeaaf96u, 0xffdcd092u, 0xff8dbccfu, 0xffafc5a1u, 0xffded3e9u]),
        ("atelier-chrome", [
            "....aaaaaaaa........bbbbbbbb........dddddddd........aaaaaaaa........bbbbbbbb........dddddddd........cccccccc....",
            "....aaaaaaaa........bbbbbbbb........dddddddd........aaaaaaaa........bbbbbbbb........dddddddd........cccccccc....",
            "....aaaaaaaa........bbbbbbbb........dddddddd........aaaaaaaa........bbbbbbbb........dddddddd........cccccccc....",
            "aaaa........bbbbbbbb........dddddddd........aaaaaaaa........bbbbbbbb........dddddddd........cccccccc........aaaa",
            "aaaa........bbbbbbbb........dddddddd........aaaaaaaa........bbbbbbbb........dddddddd........cccccccc........aaaa",
            "aaaa........bbbbbbbb........dddddddd........aaaaaaaa........bbbbbbbb........dddddddd........cccccccc........aaaa"],
            [0xffd8675bu, 0xff6ba776u, 0xff4285f4u, 0xffedcc70u]),
        ("atelier-paper", ["..aaaa..", ".a...a..", ".a.b.a..", ".aaaaa..", ".aaa....", "........"], [0xfff5f5f2u, 0xff5f8bceu]),
        ("atelier-safari", ["..a.....a...", ".aaa...aaa..", "aabaa.aabaa.",
            ".aaa...aaa..", "..a.....a...", "............"], [0xfff6f2e8u, 0xffed7066u]),
        ("atelier-firefox", ["a.....a.....", "aa....aa....", ".aa....aa...",
            "..bb....bb..", "...bb....bb.", "....b.....b."], [0xffff873eu, 0xffffc167u]),
        ("atelier-cursor", ["a....aa....a", "aa........aa", ".aa......aa.",
            "..aa....aa..", "...aa..aa...", "....aaaa...."], [0xffc4c2b9u]),
        ("atelier-slack", [".a......b...", "aaa....bbb..", ".a......b...",
            "....c......d", "...ccc....dd", "....c......d"], [0xff63c5dfu, 0xff82c5a6u, 0xffedc45eu, 0xffe986a3u]),
        ("atelier-zoom", [".aaaa...aaaa", ".abba...abba", ".abba...abba",
            ".aaaa...aaaa", "............", "bbbbbbbbbbbb"], [0xfff6f8f4u, 0xff9ec7f5u]),
        ("atelier-telegram", ["a.....a.....", "aa....aa....", "aba...aba...",
            ".aba...aba..", "..aa....aa..", "...a.....a.."], [0xfff5f5ecu, 0xffaddde8u]),
        ("atelier-messages", ["................", "..aa......aa....", "..aa......aa....",
            "................", "......bb......bb", "......bb......bb"], [0xfff8f7e8u, 0xffb7db8du]),
        ("atelier-mail", ["a.....a.....", ".a...a.a...a", "..a.a...a.a.",
            "...a.....a..", "............", "bbbbbbbbbbbb"], [0xfff5f5f0u, 0xffb3d8eeu]),
        ("atelier-notes", ["aaaaaaaaaaaa", "aaaaaaaaaaaa", "............",
            "............", "............", "bbbbbbbbbbbb"], [0xffefc852u, 0xfffffcf3u]),
        ("atelier-calendar", ["aaaaaaaaaaaa", "aaaaaaaaaaaa", "............",
            "............", "............", "............"], [0xffe55c52u]),
        ("atelier-reminders", ["a.dd..b.dd..", "............", "c.dd..a.dd..",
            "............", "b.dd..c.dd..", "............"], [0xff579fdeu, 0xffe5787au, 0xffeba451u, 0xffccc6bau]),
        ("atelier-music", ["a.....a.....", "aa....aa....", ".aa....aa...",
            "..bb....bb..", "...bb....bb.", "....b.....b."], [0xfff7acc0u, 0xfffff0dbu]),
        ("atelier-photos", [".a...c...e..", "aaa.ccc.eee.", ".a...c...e..",
            "...b...d...f", "..bbb.ddd.ff", "...b...d...f"],
            [0xffefa470u, 0xffeccb67u, 0xff95ba78u, 0xff7bbbc9u, 0xff9991c1u, 0xffd98bacu]),
        ("atelier-preview", ["bbbbbbbbbbbb", "b.....b.....", "b..a..b..a..",
            "b.aaa.b.aaa.", "baaaaabaaaaa", "bbbbbbbbbbbb"], [0xfff2f4efu, 0xffaacfddu]),
        ("atelier-word", ["a....aa....a", "aa........aa", ".aa......aa.",
            "..bb....bb..", "...bb..bb...", "....bbbb...."], [0xfff3f1e6u, 0xff82a8deu]),
        ("atelier-excel", ["aa...baa...b", "aa...baa...b", "bbbbbbbbbbbb",
            "..aa.b..aa.b", "..aa.b..aa.b", "bbbbbbbbbbbb"], [0xff82b99au, 0xffdbead0u]),
        ("atelier-powerpoint", [".aaaa...aaaa", ".abba...abba", ".abba...abba",
            ".aaaa...aaaa", "............", "..b.....b..."], [0xffeea58au, 0xfff5d9b8u]),
        ("atelier-outlook", ["aaa...aaa...", "aba...aba...", "aab...aab...",
            "...aaa...aaa", "...aba...aba", "...aab...aab"], [0xff8ac6ecu, 0xfff5f5eau]),
        ("atelier-vscode", ["a....ba....b", ".a..ab.a..ab", "..aa.b..aa.b",
            "..aa.b..aa.b", ".a..ab.a..ab", "a....ba....b"], [0xffb6e1edu, 0xff17374fu]),
        ("atelier-photoshop", ["aaaa..aaaa..", "a..a..a..a..", "aaaa..aaaa..",
            "...bbb...bbb", "...b.b...b.b", "...bbb...bbb"], [0xff58b7e9u, 0xff8cbad0u]),
        ("atelier-illustrator", ["...baaaab...", "...baaaab...", "...baaaab...",
            "...baaaab...", "...baaaab...", "...baaaab..."], [0xfff5a13du, 0xfff8d2a0u]),
        ("atelier-granola", ["...aaaaaa...", "...aaaaaa...", "...aaaaaa...",
            "...aaaaaa...", "...aaaaaa...", "...aaaaaa..."], [0xff8ba66au]),
        ("atelier-chatgpt", ["a....aa....a", "aa........aa", ".aa......aa.",
            "..aa....aa..", "...aa..aa...", "....aaaa...."], [0xfff6f0deu]),
        ("atelier-ghostty", ["aa..........", "aa..........", "aa..........",
            "aa..........", "aa..........", "aa.........."], [0xffecebe4u]),
        ("braid", ["abb........a", "a.bb......aa", "...bb....aaa",
            "....bb..aaa.", ".....bbaaa..", "......bba...",
            ".....aabb...", "....aaa.bb..", "...aaa...bb.",
            "..aaa.....bb", "baaa.......b", "bba........."], [0xfff6f0deu, 0xfff078aau]),
        ("blockstripe", ["...aaa"], [0xffffffffu]),
        ("checker", ["..aa", "..aa", "aa..", "aa.."], [0xfff2eee4u]),
        ("seedling", ["....a.", "....a.", "...a.a", ".a....", ".a....", "a.a..."], [0xff96d6a0u]),
        ("trim", ["...a....a.", "..........", "bb...bb...",
            "..........", "c...cc...c", "cc...cc..."], [0xffffffffu, 0xfff58220u, 0xffd61e6eu]),
        ("picnic", ["...aaa...aaa", "...aaa...aaa", "...aaa...aaa",
            "aaa...aaa...", "aaa...aaa...", "aaa...aaa..."], [0xfff4e6bfu]),
        ("ribbon", ["aa.....b....", "aa.....b....", "aa.....b....",
            "aa.....b....", "aa.....b....", "aa.....b...."], [0xfff5e8cfu, 0xffe3a1b3u]),
        ("posy", ["............", ".aa.aa......", ".aaaaa......",
            "..aba.......", ".a...a......", "............"], [0xfff0d1dbu, 0xfff2dd9du]),
        ("twinkle", ["............", "..a.....a...", ".aaa...aaa..",
            "..a.....a...", "............", "............"], [0xfff5e6bfu]),
        ("candy-stripe", ["aaa...bbb...", "aaa...bbb...", "aaa...bbb...",
            "aaa...bbb...", "aaa...bbb...", "aaa...bbb..."], [0xfff5e8cfu, 0xffe3a1b3u]),
        ("zigzag", ["a....aa....a", "aa........aa", ".aa......aa.",
            "..aa....aa..", "...aa..aa...", "....aaaa...."], [0xfff6f0deu]),
    ];

    static readonly HashSet<string> Fitted =
        ["atelier-finder", "atelier-terminal", "atelier-grok", "atelier-granola", "atelier-illustrator", "atelier-chrome"];

    static void LoadCollection()
    {
        foreach (var (name, rows, yarn) in Collection)
        {
            int w = rows[0].Length, h = rows.Length;
            var px = new uint[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    char c = rows[y][x];
                    if (c is >= 'a' and <= 'f') px[y * w + x] = yarn[c - 'a'];
                }
            var chart = new Chart { Name = name, W = w, H = h, Px = px };
            if (name == "atelier-whatsapp") { chart.SolidCorners = true; chart.CornerColor = 0; }
            if (name is "atelier-notes" or "atelier-calendar")
            {
                // The outer rows become a curved cuff so they don't repeat into the inner corner.
                chart.CuffColor = yarn[0];
                for (int i = 0; i < px.Length; i++) if (px[i] == chart.CuffColor) px[i] = 0;
            }
            chart.RoundDots = name == "atelier-messages";
            chart.FittedRepeat = Fitted.Contains(name);
            chart.Sculpted = !chart.RoundDots;
            chart.Defined = true;
            All.Add(chart);
        }
    }

    /// Reload the collection, then the user's PNGs. A PNG with a built-in's name replaces it.
    public static void Load(string dir)
    {
        Generation++;
        All.Clear();
        LoadCollection();
        if (!Directory.Exists(dir)) return;
        foreach (var path in Directory.GetFiles(dir, "*.png").OrderBy(p => p, StringComparer.Ordinal))
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (name.Length is 0 or >= 64) continue;
            var chart = LoadPng(path, name);
            if (chart == null) continue;
            chart.Custom = true;
            int existing = All.FindIndex(c => c.Name == name);
            if (existing >= 0)
            {
                var old = All[existing];
                chart.SolidCorners = old.SolidCorners; chart.CornerColor = old.CornerColor;
                chart.CuffColor = old.CuffColor; chart.RoundDots = old.RoundDots;
                chart.FittedRepeat = old.FittedRepeat; chart.Sculpted = old.Sculpted; chart.Defined = old.Defined;
                All[existing] = chart;
            }
            else if (All.Count < 128) All.Add(chart);
        }
    }

    static unsafe Chart? LoadPng(string path, string name)
    {
        try
        {
            using var img = new Bitmap(path);
            int w = img.Width, h = img.Height;
            if (w < 1 || h < 1 || w > 256 || h > 256) return null;
            var px = new uint[w * h];
            var data = img.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < h; y++)
                {
                    uint* row = (uint*)((byte*)data.Scan0 + y * data.Stride);
                    for (int x = 0; x < w; x++) px[y * w + x] = row[x];
                }
            }
            finally { img.UnlockBits(data); }
            return new Chart { Name = name, W = w, H = h, Px = px, Sculpted = true, Defined = true };
        }
        catch { return null; }
    }
}
