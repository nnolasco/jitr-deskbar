// "Which Claude sessions are on THIS desktop?" -- scans the statusline tee files
// (~/.claude/jitr-status-<sessionId>.json, written every few seconds per live
// session) for fresh ones, then matches their session_name / cwd basename against
// the titles of windows on the current virtual desktop. Heuristic and fail-soft:
// no match simply means no sessions line.
using System;
using System.Collections.Generic;
using System.IO;

namespace JitrDeskBar
{
    public class LiveSession
    {
        public string Name;   // session_name (may be null)
        public string Cwd;    // working directory
    }

    public static class Sessions
    {
        private const double FRESH_MS = 120000; // statusline fires constantly while alive

        public static List<LiveSession> FreshSessions()
        {
            var outp = new List<LiveSession>();
            try
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var dir = Path.Combine(home, ".claude");
                var nowMs = (double)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                foreach (var f in Directory.GetFiles(dir, "jitr-status-*.json"))
                {
                    try
                    {
                        var root = MiniJson.ParseObject(File.ReadAllText(f));
                        var at = MiniJson.GetNumber(root, 0, "receivedAt");
                        if (at <= 0 || nowMs - at > FRESH_MS) continue;
                        outp.Add(new LiveSession
                        {
                            Name = MiniJson.GetString(root, "session_name"),
                            Cwd = MiniJson.GetString(root, "cwd"),
                        });
                    }
                    catch { }
                }
            }
            catch { }
            return outp;
        }

        // Claude Code stamps its terminal tab title with a "✳ " prefix plus the
        // session description, and a terminal window's title is its active tab's
        // title. So: primary signal = ✳-titled windows on the current desktop
        // (the title IS the session description); fallback = fresh statusline tee
        // files whose session_name / cwd basename appears in any title here (catches
        // recently-active sessions whose tab isn't the active one).
        public static List<string> OnCurrentDesktop(DesktopTracker tracker, IntPtr selfHwnd)
        {
            var names = new List<string>();

            var titles = new List<string>();
            foreach (var h in Native.VisibleWindows())
            {
                if (h == selfHwnd) continue;
                if (!tracker.IsOnCurrentDesktop(h)) continue;
                var t = Native.WindowTitle(h);
                if (t.Length == 0) continue;
                if (t[0] == '✳') // the "✳" marker Claude Code puts in session titles
                {
                    var name = t.TrimStart('✳', ' ');
                    if (name.Length > 0 && !names.Contains(name)) names.Add(name);
                }
                titles.Add(t.ToLowerInvariant());
            }

            var fresh = FreshSessions();
            foreach (var s in fresh)
            {
                string needleA = string.IsNullOrEmpty(s.Name) ? null : s.Name.ToLowerInvariant();
                string needleB = null;
                try
                {
                    if (!string.IsNullOrEmpty(s.Cwd))
                    {
                        var baseName = Path.GetFileName(s.Cwd.TrimEnd('\\', '/'));
                        if (!string.IsNullOrEmpty(baseName)) needleB = baseName.ToLowerInvariant();
                    }
                }
                catch { }

                foreach (var t in titles)
                {
                    if ((needleA != null && t.Contains(needleA)) ||
                        (needleB != null && t.Contains(needleB)))
                    {
                        var disp = !string.IsNullOrEmpty(s.Name) ? s.Name : needleB;
                        if (disp != null && !names.Contains(disp)) names.Add(disp);
                        break;
                    }
                }
            }
            return names;
        }
    }
}
