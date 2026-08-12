// Claude account + subscription usage: token from ~/.claude/.credentials.json,
// GET api.anthropic.com/api/oauth/usage (the undocumented endpoint behind
// Claude Code's /usage screen -- every failure renders as "n/a", never an error).
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;

namespace JitrDeskBar
{
    public class UsageRow
    {
        public string Label;      // Session | Weekly | <scoped model display name>
        public int UsedPct;       // 0..100
        public string ResetsAt;   // ISO string or null
    }

    public class UsageResult
    {
        public List<UsageRow> Rows;   // null when Error is set
        public string Error;          // "n/a (...)" style
    }

    public static class Usage
    {
        private static string ClaudeDir()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".claude");
        }

        // ~/.claude.json can be multi-MB (project history), so regex the one field
        // out instead of parsing the whole document.
        public static string AccountEmail()
        {
            try
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var raw = File.ReadAllText(Path.Combine(home, ".claude.json"));
                var m = Regex.Match(raw, "\"emailAddress\"\\s*:\\s*\"([^\"]+)\"");
                return m.Success ? m.Groups[1].Value : null;
            }
            catch { return null; }
        }

        private static string OauthToken()
        {
            try
            {
                var raw = File.ReadAllText(Path.Combine(ClaudeDir(), ".credentials.json"));
                var root = MiniJson.ParseObject(raw);
                var tok = MiniJson.GetString(root, "claudeAiOauth", "accessToken");
                var exp = MiniJson.GetNumber(root, 0, "claudeAiOauth", "expiresAt");
                if (exp > 0 && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() > (long)exp)
                    return null; // stale -> let claude refresh it; we show n/a meanwhile
                return string.IsNullOrEmpty(tok) ? null : tok;
            }
            catch { return null; }
        }

        // Fetch on a worker thread; onDone runs on that thread (caller marshals to UI).
        public static void FetchAsync(Action<UsageResult> onDone)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                onDone(FetchNow());
            });
        }

        private static UsageResult FetchNow()
        {
            var tok = OauthToken();
            if (tok == null) return new UsageResult { Error = "n/a (not logged in)" };
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var req = (HttpWebRequest)WebRequest.Create("https://api.anthropic.com/api/oauth/usage");
                req.Method = "GET";
                req.Timeout = 5000;
                req.ReadWriteTimeout = 5000;
                req.Headers["Authorization"] = "Bearer " + tok;
                req.Headers["anthropic-beta"] = "oauth-2025-04-20";
                req.ContentType = "application/json";
                using (var res = (HttpWebResponse)req.GetResponse())
                using (var rd = new StreamReader(res.GetResponseStream()))
                {
                    return ParseBody(rd.ReadToEnd());
                }
            }
            catch (WebException wex)
            {
                var http = wex.Response as HttpWebResponse;
                if (http != null) return new UsageResult { Error = "n/a (HTTP " + (int)http.StatusCode + ")" };
                return new UsageResult { Error = "n/a (offline)" };
            }
            catch { return new UsageResult { Error = "n/a" }; }
        }

        private static UsageResult ParseBody(string body)
        {
            try
            {
                var root = MiniJson.ParseObject(body);
                var limits = MiniJson.Get(root, "limits") as List<object>;
                if (limits == null) return new UsageResult { Error = "n/a (no limits)" };
                var rows = new List<UsageRow>();
                foreach (var l in limits)
                {
                    var kind = MiniJson.GetString(l, "kind");
                    var pctObj = MiniJson.Get(l, "percent");
                    if (!(pctObj is double)) continue;
                    string label;
                    if (kind == "session") label = "Session";
                    else if (kind == "weekly_all") label = "Weekly";
                    else if (kind == "weekly_scoped")
                    {
                        label = MiniJson.GetString(l, "scope", "model", "display_name");
                        if (label == null) label = "Weekly*";
                    }
                    else continue;
                    var pct = (int)Math.Round((double)pctObj);
                    if (pct < 0) pct = 0; if (pct > 100) pct = 100;
                    rows.Add(new UsageRow { Label = label, UsedPct = pct, ResetsAt = MiniJson.GetString(l, "resets_at") });
                }
                if (rows.Count == 0) return new UsageResult { Error = "n/a (no limits)" };
                return new UsageResult { Rows = rows };
            }
            catch { return new UsageResult { Error = "n/a (bad response)" }; }
        }

        public static string ResetText(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return "";
            try
            {
                var dt = DateTimeOffset.Parse(iso).ToLocalTime();
                var span = dt - DateTimeOffset.Now;
                string until;
                if (span.TotalMinutes <= 0) until = "now";
                else if (span.TotalDays >= 1) until = string.Format("{0}d {1}h", (int)span.TotalDays, span.Hours);
                else if (span.TotalHours >= 1) until = string.Format("{0}h {1}m", (int)span.TotalHours, span.Minutes);
                else until = string.Format("{0}m", (int)span.TotalMinutes);
                return string.Format("resets {0:M/d h:mm tt} ({1})", dt, until);
            }
            catch { return ""; }
        }
    }
}
