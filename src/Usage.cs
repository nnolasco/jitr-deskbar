// Claude account + subscription usage. Session + Weekly are read LOCALLY when statusline
// tee files exist (~/.claude/jitr-status-<sessionId>.json, written by the JITR statusline
// integration every turn -- their rate_limits block is the same data as Claude Code's
// /usage screen). The per-model scoped weekly row (e.g. "Fable 5") exists ONLY on
// api.anthropic.com/api/oauth/usage -- and machines without the tee need that endpoint
// for ALL rows. That endpoint rate-limits the whole account into persistent 429s under
// any real polling (Anthropic closed the reports as "not planned"), so it is fetched at
// most once per 10 minutes ACCOUNT-WIDE: all consumers (this bar, jitr-term, jitr-lite)
// share ~/.claude/jitr-usage-endpoint.json and claim the next poll by stamping
// attemptedAt before requesting; any failure keeps the last good rows. Never poll faster
// or per-process. jitr-term's lib/usage.mjs implements the SAME cache-file contract --
// keep the schema ({attemptedAt, fetchedAt, lastStatus, rows:[{kind,label,used,resets}]})
// in sync. Every failure renders as "n/a", never an error.
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
        public string Label;      // Session | Weekly | <other rate_limits key>
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

        // Fetch on a worker thread; onDone runs on that thread (caller marshals to UI).
        public static void FetchAsync(Action<UsageResult> onDone)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                onDone(FetchNow());
            });
        }

        // A tee file this old means no claude session ran in a day.
        private const double StaleMs = 24.0 * 3600 * 1000;
        // One endpoint poll attempt per 10 min shared across ALL processes.
        private const double EndpointPollMs = 10.0 * 60 * 1000;

        private static string EndpointCachePath()
        {
            return Path.Combine(ClaudeDir(), "jitr-usage-endpoint.json");
        }

        private static UsageResult FetchNow()
        {
            var local = LocalRows();
            MaybePollEndpoint(); // inline: we're already on a worker thread, 5s timeout
            if (local.Error == null)
            {
                // Tee files present (JITR machine): Session/Weekly are local and fresh;
                // only the per-model scoped row comes from the endpoint cache.
                foreach (var row in CachedRows(true)) local.Rows.Add(row);
                return local;
            }
            // No tee files (deskbar without the JITR statusline integration): all rows
            // come from the endpoint cache, refreshed at the shared 10-minute cadence.
            var cached = CachedRows(false);
            if (cached.Count > 0) return new UsageResult { Rows = cached };
            return local;
        }

        private static UsageResult LocalRows()
        {
            try
            {
                var dir = ClaudeDir();
                if (!Directory.Exists(dir)) return new UsageResult { Error = "n/a (claude not set up)" };
                var files = new List<FileInfo>();
                foreach (var p in Directory.GetFiles(dir, "jitr-status-*.json"))
                    files.Add(new FileInfo(p));
                files.Sort(delegate(FileInfo a, FileInfo b) { return b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc); });
                // Some files may predate rate_limits (old claude version) or be caught
                // mid-rename, so try a few newest before giving up.
                for (int i = 0; i < files.Count && i < 5; i++)
                {
                    if ((DateTime.UtcNow - files[i].LastWriteTimeUtc).TotalMilliseconds > StaleMs)
                        break; // sorted desc: the rest are older still
                    var r = ParseTee(files[i].FullName);
                    if (r != null) return r;
                }
                return new UsageResult { Error = "n/a (no recent claude session)" };
            }
            catch { return new UsageResult { Error = "n/a" }; }
        }

        // ---------- endpoint rows: shared 10-min poll (see header) ----------

        private static Dictionary<string, object> LoadEndpointCache()
        {
            try
            {
                var d = MiniJson.ParseObject(File.ReadAllText(EndpointCachePath()));
                return d != null ? d : new Dictionary<string, object>();
            }
            catch { return new Dictionary<string, object>(); }
        }

        private static void SaveEndpointCache(Dictionary<string, object> cache)
        {
            try
            {
                var path = EndpointCachePath();
                var tmp = path + ".tmp-" + System.Diagnostics.Process.GetCurrentProcess().Id;
                File.WriteAllText(tmp, MiniJson.Serialize(cache));
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            catch { /* best-effort; worst case another process claims too */ }
        }

        private static double NowMs()
        {
            return (DateTimeOffset.UtcNow - Epoch).TotalMilliseconds;
        }

        // Rows from the shared cache (optionally only the model-scoped ones), skipping any
        // whose reset already passed (that window rolled over; last week's percentage
        // would mislead).
        private static List<UsageRow> CachedRows(bool scopedOnly)
        {
            var rows = new List<UsageRow>();
            var list = MiniJson.Get(LoadEndpointCache(), "rows") as List<object>;
            if (list == null) return rows;
            foreach (var item in list)
            {
                if (scopedOnly && MiniJson.GetString(item, "kind") != "weekly_scoped") continue;
                var label = MiniJson.GetString(item, "label");
                var pctObj = MiniJson.Get(item, "used");
                if (label == null || !(pctObj is double)) continue;
                var resets = MiniJson.GetString(item, "resets");
                if (resets != null)
                {
                    try { if (DateTimeOffset.Parse(resets) < DateTimeOffset.UtcNow) continue; }
                    catch { /* unparseable reset -- keep the row */ }
                }
                var pct = (int)Math.Round((double)pctObj);
                if (pct < 0) pct = 0; if (pct > 100) pct = 100;
                rows.Add(new UsageRow { Label = label, UsedPct = pct, ResetsAt = resets });
            }
            return rows;
        }

        private static string OauthToken()
        {
            try
            {
                var root = MiniJson.ParseObject(File.ReadAllText(Path.Combine(ClaudeDir(), ".credentials.json")));
                var tok = MiniJson.GetString(root, "claudeAiOauth", "accessToken");
                var exp = MiniJson.GetNumber(root, 0, "claudeAiOauth", "expiresAt");
                if (exp > 0 && NowMs() > exp) return null; // stale -> let claude refresh it
                return string.IsNullOrEmpty(tok) ? null : tok;
            }
            catch { return null; }
        }

        // If nobody attempted a poll in the last 10 minutes, claim the slot (stamp
        // attemptedAt BEFORE requesting so parallel processes back off) and fetch all
        // limit rows. On any failure the previous rows survive.
        private static void MaybePollEndpoint()
        {
            try
            {
                var cache = LoadEndpointCache();
                var attempted = MiniJson.GetNumber(cache, 0, "attemptedAt");
                if (attempted > 0 && NowMs() - attempted < EndpointPollMs) return;
                var tok = OauthToken();
                if (tok == null) return;
                cache["attemptedAt"] = NowMs();
                SaveEndpointCache(cache);

                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var req = (HttpWebRequest)WebRequest.Create("https://api.anthropic.com/api/oauth/usage");
                req.Method = "GET";
                req.Timeout = 5000;
                req.ReadWriteTimeout = 5000;
                req.Headers["Authorization"] = "Bearer " + tok;
                req.Headers["anthropic-beta"] = "oauth-2025-04-20";
                req.ContentType = "application/json";
                string body;
                using (var res = (HttpWebResponse)req.GetResponse())
                using (var rd = new StreamReader(res.GetResponseStream()))
                {
                    body = rd.ReadToEnd();
                }
                var root = MiniJson.ParseObject(body);
                var limits = MiniJson.Get(root, "limits") as List<object>;
                if (limits == null) { cache["lastStatus"] = "bad response"; SaveEndpointCache(cache); return; }
                var rows = new List<object>();
                foreach (var l in limits)
                {
                    var kind = MiniJson.GetString(l, "kind");
                    var pctObj = MiniJson.Get(l, "percent");
                    if (kind == null || !(pctObj is double)) continue;
                    string label;
                    if (kind == "session") label = "Session";
                    else if (kind == "weekly_all") label = "Weekly";
                    else if (kind == "weekly_scoped")
                    {
                        label = MiniJson.GetString(l, "scope", "model", "display_name");
                        if (label == null) label = "Weekly*";
                    }
                    else continue;
                    var row = new Dictionary<string, object>();
                    row["kind"] = kind;
                    row["label"] = label;
                    row["used"] = Math.Min(100.0, Math.Max(0.0, Math.Round((double)pctObj)));
                    row["resets"] = MiniJson.GetString(l, "resets_at");
                    rows.Add(row);
                }
                cache["fetchedAt"] = NowMs();
                cache["lastStatus"] = 200;
                cache["rows"] = rows;
                SaveEndpointCache(cache);
            }
            catch (WebException wex)
            {
                try
                {
                    var cache = LoadEndpointCache();
                    var http = wex.Response as HttpWebResponse;
                    cache["lastStatus"] = http != null ? (object)(double)(int)http.StatusCode : "offline";
                    SaveEndpointCache(cache);
                }
                catch { /* keep last-good rows */ }
            }
            catch { /* keep last-good rows */ }
        }

        private static readonly DateTimeOffset Epoch = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);

        private static UsageResult ParseTee(string path)
        {
            try
            {
                var root = MiniJson.ParseObject(File.ReadAllText(path));
                var rl = MiniJson.Get(root, "rate_limits") as Dictionary<string, object>;
                if (rl == null) return null; // no rate_limits in this file -> try next-newest
                var rows = new List<UsageRow>();
                foreach (var kv in rl)
                {
                    var pctObj = MiniJson.Get(kv.Value, "used_percentage");
                    if (!(pctObj is double)) continue;
                    string label;
                    if (kv.Key == "five_hour") label = "Session";
                    else if (kv.Key == "seven_day") label = "Weekly";
                    else label = kv.Key.Replace('_', ' ');
                    var resetsSec = MiniJson.GetNumber(kv.Value, 0, "resets_at");
                    string resetsIso = null;
                    bool rolled = false;
                    if (resetsSec > 0)
                    {
                        var resets = Epoch.AddSeconds(resetsSec);
                        // A reset time in the past means the window rolled over with no
                        // session running to refresh the file -- the stored percentage
                        // belongs to the previous window; show 0 and no reset line.
                        rolled = resets < DateTimeOffset.UtcNow;
                        if (!rolled) resetsIso = resets.ToString("o");
                    }
                    var pct = rolled ? 0 : (int)Math.Round((double)pctObj);
                    if (pct < 0) pct = 0; if (pct > 100) pct = 100;
                    rows.Add(new UsageRow { Label = label, UsedPct = pct, ResetsAt = resetsIso });
                }
                return rows.Count > 0 ? new UsageResult { Rows = rows } : null;
            }
            catch { return null; } // torn mid-rename read -> caller tries the next file
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
