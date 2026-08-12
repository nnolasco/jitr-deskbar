// Win32 + virtual desktop COM interop. Only the DOCUMENTED IVirtualDesktopManager
// interface is used (stable across Windows builds), never the per-build private ones.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace JitrDeskBar
{
    [ComImport, Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a")]
    public class VirtualDesktopManagerCom { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
     Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b")]
    public interface IVirtualDesktopManager
    {
        [return: MarshalAs(UnmanagedType.Bool)]
        bool IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow);
        Guid GetWindowDesktopId(IntPtr topLevelWindow);
        void MoveWindowToDesktop(IntPtr topLevelWindow, [MarshalAs(UnmanagedType.LPStruct)] Guid desktopId);
    }

    public static class Native
    {
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern bool MoveWindow(IntPtr hWnd, int x, int y, int w, int h, bool repaint);

        [DllImport("user32.dll")]
        public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref RECT pvParam, uint fWinIni);

        public const uint SPI_GETWORKAREA = 0x0030;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        public static string WindowTitle(IntPtr hWnd)
        {
            var sb = new StringBuilder(512);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        public static string WindowClass(IntPtr hWnd)
        {
            var sb = new StringBuilder(256);
            GetClassName(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        // All visible top-level windows, in z-order.
        public static List<IntPtr> VisibleWindows()
        {
            var list = new List<IntPtr>();
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                if (IsWindowVisible(h)) list.Add(h);
                return true;
            }, IntPtr.Zero);
            return list;
        }

        // Primary-monitor work area in physical pixels (taskbar excluded).
        public static RECT WorkAreaPx()
        {
            var r = new RECT();
            SystemParametersInfo(SPI_GETWORKAREA, 0, ref r, 0);
            return r;
        }
    }

    // Thin fail-soft wrapper: every COM error degrades to "pretend we're on the
    // current desktop" so a shell hiccup never crashes the bar.
    public class DesktopTracker
    {
        private readonly IVirtualDesktopManager _mgr;

        public DesktopTracker()
        {
            IVirtualDesktopManager m = null;
            try { m = (IVirtualDesktopManager)new VirtualDesktopManagerCom(); }
            catch { }
            _mgr = m;
        }

        public bool Available { get { return _mgr != null; } }

        public bool IsOnCurrentDesktop(IntPtr hwnd)
        {
            if (_mgr == null) return true;
            try { return _mgr.IsWindowOnCurrentVirtualDesktop(hwnd); }
            catch { return true; }
        }

        public Guid DesktopIdOf(IntPtr hwnd)
        {
            if (_mgr == null) return Guid.Empty;
            try { return _mgr.GetWindowDesktopId(hwnd); }
            catch { return Guid.Empty; }
        }

        public bool MoveToDesktop(IntPtr hwnd, Guid desktopId)
        {
            if (_mgr == null || desktopId == Guid.Empty) return false;
            try { _mgr.MoveWindowToDesktop(hwnd, desktopId); return true; }
            catch { return false; }
        }

        // The CURRENT desktop's GUID. IsWindowOnCurrentVirtualDesktop is useless
        // for the bar itself (tool windows show on ALL desktops and always answer
        // "yes"), so we read the id the shell maintains in the registry -- it is
        // rewritten on every desktop switch.
        public Guid CurrentDesktopId()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\VirtualDesktops"))
                {
                    if (k == null) return Guid.Empty;
                    var raw = k.GetValue("CurrentVirtualDesktop") as byte[];
                    if (raw == null || raw.Length != 16) return Guid.Empty;
                    return new Guid(raw);
                }
            }
            catch { return Guid.Empty; }
        }
    }
}
