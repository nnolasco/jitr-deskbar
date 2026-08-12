// Launcher actions: two side-by-side terminals (launch wt.exe, find the new
// windows, MoveWindow them into left/right halves), Chrome, Edge.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace JitrDeskBar
{
    public static class Launchers
    {
        private const string WT_CLASS = "CASCADIA_HOSTING_WINDOW_CLASS"; // Windows Terminal

        public static void TerminalsSideBySide()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    var before = new HashSet<IntPtr>(WtWindows());

                    for (int i = 0; i < 2; i++)
                    {
                        // -w new forces a fresh window even if wt is set to reuse windows
                        Process.Start(new ProcessStartInfo("wt.exe", "-w new") { UseShellExecute = true });
                        Thread.Sleep(500);
                    }

                    // Wait for the two new windows to exist (up to ~6s).
                    List<IntPtr> fresh = null;
                    for (int tries = 0; tries < 12; tries++)
                    {
                        Thread.Sleep(500);
                        fresh = new List<IntPtr>();
                        foreach (var h in WtWindows())
                            if (!before.Contains(h)) fresh.Add(h);
                        if (fresh.Count >= 2) break;
                    }
                    if (fresh == null || fresh.Count == 0) return;

                    var wa = Native.WorkAreaPx();
                    int halfW = (wa.Right - wa.Left) / 2;
                    int height = wa.Bottom - wa.Top;
                    if (fresh.Count >= 1)
                        Native.MoveWindow(fresh[0], wa.Left, wa.Top, halfW, height, true);
                    if (fresh.Count >= 2)
                        Native.MoveWindow(fresh[1], wa.Left + halfW, wa.Top, halfW, height, true);
                }
                catch { }
            });
        }

        public static void Chrome() { Browser("chrome", "Google\\Chrome\\Application\\chrome.exe"); }
        public static void Edge() { Browser("msedge", "Microsoft\\Edge\\Application\\msedge.exe"); }

        private static void Browser(string shellName, string relPath)
        {
            try
            {
                var candidates = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), relPath),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), relPath),
                };
                foreach (var c in candidates)
                {
                    if (File.Exists(c))
                    {
                        Process.Start(new ProcessStartInfo(c, "--new-window") { UseShellExecute = true });
                        return;
                    }
                }
                // PATH / app-alias fallback
                Process.Start(new ProcessStartInfo(shellName, "--new-window") { UseShellExecute = true });
            }
            catch { }
        }

        private static List<IntPtr> WtWindows()
        {
            var list = new List<IntPtr>();
            foreach (var h in Native.VisibleWindows())
                if (Native.WindowClass(h) == WT_CLASS) list.Add(h);
            return list;
        }
    }
}
