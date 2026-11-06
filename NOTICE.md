# Attribution

Window Sweaters for Windows is a port of **Window Sweaters for macOS** by Sara Gordić:
https://github.com/saragordic/window-sweaters (ported from commit `4c9376b`, 5 Nov 2026).

That project is itself derived from JankyBorders by Felix Kratz:
https://github.com/FelixKratz/JankyBorders

Both are licensed under the GNU General Public License, version 3, which is preserved
in LICENSE and applies to this port as a derivative work.

What carries over from the macOS app, translated from C/Objective-C to C#:

- the procedural knit renderer (seamless lit tiles, mitred sides, cuffs, solid corners,
  fitted repeats, round dots) — `src/Knit.cs`, from `src/knit.c`
- the colourwork chart collection and PNG chart loading — `src/Charts.cs`, from `src/chart.c`
- the 37 hand-picked app colourways and `apps.conf` format — `src/Apps.cs`, from `src/apps.c`
- icon-derived colourways and the shared Zigzag — `src/AutoYarn.cs`, from `src/autoyarn.m`
- the menu structure, choices and wording — `src/TrayApp.cs`, from `src/menubar.m`
- the yarn icon — `src/YarnIcon.cs`, from `assets/yarn-menu-icon.svg`

What is new for Windows: window tracking through WinEvent hooks, layered overlay strips
kept beneath each window in z-order, DWM frame and corner handling, per-monitor DPI,
the notification-area menu, and Windows aliases for File Explorer, Windows Terminal,
Notepad and the Office executables.

Application names in the collection belong to their respective owners and are used to
describe colourway inspiration. No affiliation or endorsement is implied.

The seasonal outfits (flower rings, inner tubes, twigs and vines, holiday garland) are new in
this port and are not part of the macOS app.
