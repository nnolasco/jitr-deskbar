// Per-desktop profiles persisted to ~/.jitr/deskbar.json. Atomic write (tmp+rename),
// fail-soft load: a corrupt file just means default profiles.
using System;
using System.Collections.Generic;
using System.IO;

namespace JitrDeskBar
{
    public class DesktopProfile
    {
        public string Title = "Desktop";
        public string Note = "";
        public string Color = "#0EA5E9";
    }

    public class Config
    {
        public double WidthFraction = 0.5;
        public double BarHeightDip = 100;
        // NaN = automatic (upper-right corner); set when the user drags the bar.
        public double CustomLeft = double.NaN;
        public double CustomTop = double.NaN;
        public Dictionary<string, DesktopProfile> Desktops = new Dictionary<string, DesktopProfile>();

        private static string PathOf()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(Path.Combine(home, ".jitr"), "deskbar.json");
        }

        public static Config Load()
        {
            var cfg = new Config();
            try
            {
                var root = MiniJson.ParseObject(File.ReadAllText(PathOf()));
                if (root == null) return cfg;
                cfg.WidthFraction = MiniJson.GetNumber(root, 0.5, "widthFraction");
                cfg.BarHeightDip = MiniJson.GetNumber(root, 100, "barHeightDip");
                cfg.CustomLeft = MiniJson.GetNumber(root, double.NaN, "customLeft");
                cfg.CustomTop = MiniJson.GetNumber(root, double.NaN, "customTop");
                var desks = MiniJson.Get(root, "desktops") as Dictionary<string, object>;
                if (desks != null)
                {
                    foreach (var kv in desks)
                    {
                        var p = new DesktopProfile();
                        var t = MiniJson.GetString(kv.Value, "title"); if (t != null) p.Title = t;
                        var n = MiniJson.GetString(kv.Value, "note"); if (n != null) p.Note = n;
                        var c = MiniJson.GetString(kv.Value, "color"); if (c != null) p.Color = c;
                        cfg.Desktops[kv.Key] = p;
                    }
                }
            }
            catch { }
            if (cfg.WidthFraction < 0.15 || cfg.WidthFraction > 1.0) cfg.WidthFraction = 0.5;
            if (cfg.BarHeightDip < 60 || cfg.BarHeightDip > 400) cfg.BarHeightDip = 100;
            return cfg;
        }

        public void Save()
        {
            try
            {
                var desks = new Dictionary<string, object>();
                foreach (var kv in Desktops)
                {
                    var p = new Dictionary<string, object>();
                    p["title"] = kv.Value.Title;
                    p["note"] = kv.Value.Note;
                    p["color"] = kv.Value.Color;
                    desks[kv.Key] = p;
                }
                var root = new Dictionary<string, object>();
                root["widthFraction"] = WidthFraction;
                root["barHeightDip"] = BarHeightDip;
                // NaN is not valid JSON -- the keys are simply absent in auto mode
                if (!double.IsNaN(CustomLeft)) root["customLeft"] = CustomLeft;
                if (!double.IsNaN(CustomTop)) root["customTop"] = CustomTop;
                root["desktops"] = desks;

                var path = PathOf();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, MiniJson.Serialize(root));
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch { }
        }

        // Profile for a desktop GUID, created on first sight.
        public DesktopProfile For(Guid desktopId)
        {
            var key = desktopId == Guid.Empty ? "default" : desktopId.ToString();
            DesktopProfile p;
            if (!Desktops.TryGetValue(key, out p))
            {
                p = new DesktopProfile();
                Desktops[key] = p;
            }
            return p;
        }
    }
}
