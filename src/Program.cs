// Entry point + "Start with Windows" shortcut helper.
using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace JitrDeskBar
{
    public static class Startup
    {
        private static string ShortcutPath()
        {
            var dir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            return Path.Combine(dir, "jitr-deskbar.lnk");
        }

        public static bool IsEnabled()
        {
            try { return File.Exists(ShortcutPath()); }
            catch { return false; }
        }

        // Returns the new state.
        public static bool Toggle()
        {
            try
            {
                var path = ShortcutPath();
                if (File.Exists(path)) { File.Delete(path); return false; }
                var t = Type.GetTypeFromProgID("WScript.Shell");
                dynamic shell = Activator.CreateInstance(t);
                dynamic lnk = shell.CreateShortcut(path);
                lnk.TargetPath = System.Reflection.Assembly.GetEntryAssembly().Location;
                lnk.WorkingDirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
                lnk.Description = "JITR DeskBar - per-desktop focus bar";
                lnk.Save();
                return true;
            }
            catch { return IsEnabled(); }
        }
    }

    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            bool created;
            using (new Mutex(true, "JitrDeskBarSingleton", out created))
            {
                if (!created) return; // already running
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var win = new BarWindow();
                win.Show();
                app.Run();
            }
        }
    }
}
