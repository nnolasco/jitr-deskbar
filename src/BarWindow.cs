// The bar itself. WPF built entirely in code (no XAML) so the in-box C# 5
// compiler can build it. Visual reference: references/final-states.jpg.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace JitrDeskBar
{
    public static class Icons
    {
        public const string Pencil = "M4,20 L4,16.5 L15,5.5 L18.5,9 L7.5,20 Z M13.5,7 L17,10.5";
        public const string Terminals = "M3,5 H21 V19 H3 Z M12,5 V19";
        // Real brand marks (filled, not stroked), path data from simple-icons v9 (CC0).
        public const string Chrome = "M12 0C8.21 0 4.831 1.757 2.632 4.501l3.953 6.848A5.454 5.454 0 0 1 12 6.545h10.691A12 12 0 0 0 12 0zM1.931 5.47A11.943 11.943 0 0 0 0 12c0 6.012 4.42 10.991 10.189 11.864l3.953-6.847a5.45 5.45 0 0 1-6.865-2.29zm13.342 2.166a5.446 5.446 0 0 1 1.45 7.09l.002.001h-.002l-5.344 9.257c.206.01.413.016.621.016 6.627 0 12-5.373 12-12 0-1.54-.29-3.011-.818-4.364zM12 16.364a4.364 4.364 0 1 1 0-8.728 4.364 4.364 0 0 1 0 8.728Z";
        public const string Edge = "M21.86 17.86q.14 0 .25.12.1.13.1.25t-.11.33l-.32.46-.43.53-.44.5q-.21.25-.38.42l-.22.23q-.58.53-1.34 1.04-.76.51-1.6.91-.86.4-1.74.64t-1.67.24q-.9 0-1.69-.28-.8-.28-1.48-.78-.68-.5-1.22-1.17-.53-.66-.92-1.44-.38-.77-.58-1.6-.2-.83-.2-1.67 0-1 .32-1.96.33-.97.87-1.8.14.95.55 1.77.41.82 1.02 1.5.6.68 1.38 1.21.78.54 1.64.9.86.36 1.77.56.92.2 1.8.2 1.12 0 2.18-.24 1.06-.23 2.06-.72l.2-.1.2-.05zm-15.5-1.27q0 1.1.27 2.15.27 1.06.78 2.03.51.96 1.24 1.77.74.82 1.66 1.4-1.47-.2-2.8-.74-1.33-.55-2.48-1.37-1.15-.83-2.08-1.9-.92-1.07-1.58-2.33T.36 14.94Q0 13.54 0 12.06q0-.81.32-1.49.31-.68.83-1.23.53-.55 1.2-.96.66-.4 1.35-.66.74-.27 1.5-.39.78-.12 1.55-.12.7 0 1.42.1.72.12 1.4.35.68.23 1.32.57.63.35 1.16.83-.35 0-.7.07-.33.07-.65.23v-.02q-.63.28-1.2.74-.57.46-1.05 1.04-.48.58-.87 1.26-.38.67-.65 1.39-.27.71-.42 1.44-.15.72-.15 1.38zM11.96.06q1.7 0 3.33.39 1.63.38 3.07 1.15 1.43.77 2.62 1.93 1.18 1.16 1.98 2.7.49.94.76 1.96.28 1 .28 2.08 0 .89-.23 1.7-.24.8-.69 1.48-.45.68-1.1 1.22-.64.53-1.45.88-.54.24-1.11.36-.58.13-1.16.13-.42 0-.97-.03-.54-.03-1.1-.12-.55-.1-1.05-.28-.5-.19-.84-.5-.12-.09-.23-.24-.1-.16-.1-.33 0-.15.16-.35.16-.2.35-.5.2-.28.36-.68.16-.4.16-.95 0-1.06-.4-1.96-.4-.91-1.06-1.64-.66-.74-1.52-1.28-.86-.55-1.79-.89-.84-.3-1.72-.44-.87-.14-1.76-.14-1.55 0-3.06.45T.94 7.55q.71-1.74 1.81-3.13 1.1-1.38 2.52-2.35Q6.68 1.1 8.37.58q1.7-.52 3.58-.52Z";
    }

    public class BarWindow : Window
    {
        private const double MARGIN_DIP = 8;
        private const double SNAP_DIP = 36; // drag-release snap distance to work-area edges

        private readonly Config _cfg;
        private readonly DesktopTracker _tracker = new DesktopTracker();
        private DesktopProfile _profile;
        private Guid _currentDesktop = Guid.Empty;
        private IntPtr _hwnd = IntPtr.Zero;

        // UI refs restyled on every profile change
        private Border _root;
        private TextBlock _titleText, _noteText, _sessText, _emailText;
        private TextBox _titleBox, _noteBox;
        private System.Windows.Shapes.Path _titlePencil, _notePencil;
        private StackPanel _metersPanel;
        private Ellipse _swatchDot;
        private readonly List<Border> _toolBorders = new List<Border>();
        private readonly List<System.Windows.Shapes.Path> _toolPaths = new List<System.Windows.Shapes.Path>();      // stroked
        private readonly List<System.Windows.Shapes.Path> _toolFillPaths = new List<System.Windows.Shapes.Path>();  // filled brand marks
        private Popup _palette;
        private System.Windows.Shapes.Path _closeX;
        private System.Windows.Forms.NotifyIcon _tray;
        // Elements whose clicks must not start a window drag
        private readonly HashSet<DependencyObject> _interactive = new HashSet<DependencyObject>();

        private int _editing;                 // >0 while a textbox is open -> no fade
        private UsageResult _usage;           // latest fetch (null = loading)
        private string _email;

        private DispatcherTimer _desktopTimer, _slowTimer, _sessionsTimer;

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        public BarWindow()
        {
            _cfg = Config.Load();

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            UseLayoutRounding = true;
            Title = "jitr-deskbar";

            BuildUi();
            PositionBar();

            SourceInitialized += OnSourceReady;
            MouseLeftButtonDown += OnDragStart;
        }

        // Drag anywhere that isn't a button/editor to move the bar; on release it
        // snaps to any work-area edge within SNAP_DIP (each axis independently, so
        // a top-right drop snaps to both). The dropped position persists (context
        // menu "Reset position" returns to auto).
        private void OnDragStart(object sender, MouseButtonEventArgs e)
        {
            var d = e.OriginalSource as DependencyObject;
            while (d != null && d != this)
            {
                if (_interactive.Contains(d)) return;
                if (d is TextBox) return;
                d = (d is Visual || d is System.Windows.Media.Media3D.Visual3D)
                    ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            try { DragMove(); } catch { return; } // blocks until the mouse is released

            var wa = SystemParameters.WorkArea;
            if (Math.Abs(Left - wa.Left) < SNAP_DIP) Left = wa.Left + MARGIN_DIP;
            if (Math.Abs((Left + Width) - wa.Right) < SNAP_DIP) Left = wa.Right - Width - MARGIN_DIP;
            if (Math.Abs(Top - wa.Top) < SNAP_DIP) Top = wa.Top + MARGIN_DIP;
            if (Math.Abs((Top + Height) - wa.Bottom) < SNAP_DIP) Top = wa.Bottom - Height - MARGIN_DIP;

            _cfg.CustomLeft = Left;
            _cfg.CustomTop = Top;
            _cfg.Save();
        }

        private void OnSourceReady(object sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            // Tool window: stays out of alt-tab
            SetWindowLong(_hwnd, GWL_EXSTYLE, GetWindowLong(_hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW);

            _currentDesktop = _tracker.CurrentDesktopId();
            if (_currentDesktop == Guid.Empty) _currentDesktop = _tracker.DesktopIdOf(_hwnd);
            ApplyProfile();
            SetupTray();

            _desktopTimer = MakeTimer(400, DesktopTick);
            _sessionsTimer = MakeTimer(5000, delegate { RefreshSessions(); });
            _slowTimer = MakeTimer(60000, delegate { RefreshSlow(); PositionBar(); });

            RefreshSlow();
            RefreshSessions();
        }

        private DispatcherTimer MakeTimer(int ms, Action tick)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += delegate { tick(); };
            t.Start();
            return t;
        }

        // ---------- geometry ----------

        private void PositionBar()
        {
            var wa = SystemParameters.WorkArea;
            Width = Math.Max(300, wa.Width * _cfg.WidthFraction);
            Height = _cfg.BarHeightDip;

            if (!double.IsNaN(_cfg.CustomLeft) && !double.IsNaN(_cfg.CustomTop))
            {
                // dragged position, clamped so a resolution change can't strand it off-screen
                var vs = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                                  SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
                Left = Math.Min(Math.Max(_cfg.CustomLeft, vs.Left - Width + 60), vs.Right - 60);
                Top = Math.Min(Math.Max(_cfg.CustomTop, vs.Top), vs.Bottom - 40);
                return;
            }
            Left = wa.Right - Width - MARGIN_DIP;
            Top = wa.Top + MARGIN_DIP;
        }

        // ---------- per-desktop switching ----------

        // The bar has WS_EX_TOOLWINDOW (keeps it out of the taskbar/alt-tab), and
        // Windows shows tool windows on EVERY virtual desktop -- so the bar never
        // moves; only its CONTENT swaps when the current desktop changes. The
        // switch signal is the registry id (see DesktopTracker.CurrentDesktopId);
        // asking about the bar's own window always answers "current" and told us
        // nothing (that bug shipped: one profile was shared by every desktop).
        private void DesktopTick()
        {
            if (!IsVisible) return;
            var g = _tracker.CurrentDesktopId();
            if (g == Guid.Empty || g == _currentDesktop) return;
            _currentDesktop = g;
            CancelEdits();
            ApplyProfile();
            RefreshSessions();
        }

        // ---------- UI construction ----------

        private void BuildUi()
        {
            _root = new Border
            {
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1.5),
                Padding = new Thickness(16, 8, 16, 8),
            };
            Content = _root;

            var overlay = new Grid();
            _root.Child = overlay;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            overlay.Children.Add(grid);

            // close (hide to tray) -- tiny X in the top-right corner
            _closeX = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M0,0 L8,8 M8,0 L0,8"),
                StrokeThickness = 1.5,
                Width = 9,
                Height = 9,
                Stretch = Stretch.Uniform,
            };
            var closeBtn = new Border
            {
                Child = _closeX,
                Padding = new Thickness(5),
                Margin = new Thickness(0, -2, -8, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(6),
                Cursor = Cursors.Hand,
                ToolTip = "Hide the bar (restore from the tray icon)",
            };
            closeBtn.MouseEnter += delegate { closeBtn.Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)); };
            closeBtn.MouseLeave += delegate { closeBtn.Background = Brushes.Transparent; };
            closeBtn.MouseLeftButtonUp += delegate { Hide(); };
            _interactive.Add(closeBtn);
            overlay.Children.Add(closeBtn);

            // -- identity block (title / note / sessions) --
            var idPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            Grid.SetColumn(idPanel, 0);
            grid.Children.Add(idPanel);

            var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
            _titleText = new TextBlock
            {
                FontSize = 24,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            _titleBox = MakeEditBox(22);
            _titlePencil = MakePencil(14);
            var titlePencilBtn = WrapIconButton(_titlePencil, delegate { StartEdit(_titleText, _titleBox); });
            titleRow.Children.Add(_titleText);
            titleRow.Children.Add(_titleBox);
            titleRow.Children.Add(titlePencilBtn);
            idPanel.Children.Add(titleRow);

            var noteRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 0) };
            _noteText = new TextBlock
            {
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            _noteBox = MakeEditBox(12);
            _notePencil = MakePencil(11);
            var notePencilBtn = WrapIconButton(_notePencil, delegate { StartEdit(_noteText, _noteBox); });
            noteRow.Children.Add(_noteText);
            noteRow.Children.Add(_noteBox);
            noteRow.Children.Add(notePencilBtn);
            idPanel.Children.Add(noteRow);

            _sessText = new TextBlock
            {
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Visibility = Visibility.Collapsed,
            };
            idPanel.Children.Add(_sessText);

            // -- tools block --
            var tools = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 14, 0),
            };
            Grid.SetColumn(tools, 1);
            grid.Children.Add(tools);

            tools.Children.Add(MakeTool(Icons.Terminals, "Terminals: two side-by-side terminal windows", delegate { Launchers.TerminalsSideBySide(); }));
            tools.Children.Add(MakeTool(Icons.Chrome, "New Chrome window", delegate { Launchers.Chrome(); }, true));
            tools.Children.Add(MakeTool(Icons.Edge, "New Edge window", delegate { Launchers.Edge(); }, true));

            // color swatch
            _swatchDot = new Ellipse { Width = 13, Height = 13, StrokeThickness = 1 };
            var swatch = MakeToolShell(_swatchDot, "Desktop color");
            swatch.MouseLeftButtonUp += delegate { OpenPalette(swatch); };
            tools.Children.Add(swatch);

            // -- account block --
            var acct = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MinWidth = 210, MaxWidth = 260 };
            Grid.SetColumn(acct, 2);
            grid.Children.Add(acct);

            _emailText = new TextBlock
            {
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 3),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            acct.Children.Add(_emailText);
            _metersPanel = new StackPanel();
            acct.Children.Add(_metersPanel);

            BuildContextMenu();
        }

        private TextBox MakeEditBox(double fontSize)
        {
            var box = new TextBox
            {
                FontSize = fontSize,
                MinWidth = 160,
                MaxWidth = 420,
                Visibility = Visibility.Collapsed,
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
                Foreground = Brushes.White,
                CaretBrush = Brushes.White,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4, 1, 4, 1),
            };
            return box;
        }

        private System.Windows.Shapes.Path MakePencil(double size)
        {
            return new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(Icons.Pencil),
                StrokeThickness = 1.5,
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
            };
        }

        private FrameworkElement WrapIconButton(System.Windows.Shapes.Path icon, Action onClick)
        {
            var b = new Border
            {
                Child = icon,
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
            };
            b.MouseLeftButtonUp += delegate { onClick(); };
            _interactive.Add(b);
            return b;
        }

        // Compact icon-only buttons -- the launcher names live in the tooltips so
        // the identity text (title/note/sessions) gets the width instead.
        // filled = true for brand-logo paths (drawn with Fill, no stroke).
        private Border MakeTool(string geometry, string tooltip, Action onClick, bool filled = false)
        {
            var icon = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(geometry),
                StrokeThickness = filled ? 0 : 1.4,
                Width = 13,
                Height = 13,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            if (filled) _toolFillPaths.Add(icon); else _toolPaths.Add(icon);
            var shell = MakeToolShell(icon, tooltip);
            shell.MouseLeftButtonUp += delegate { onClick(); };
            return shell;
        }

        private Border MakeToolShell(FrameworkElement icon, string tooltip)
        {
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(icon);
            var b = new Border
            {
                Child = stack,
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Width = 30,
                Height = 28,
                Margin = new Thickness(0, 0, 6, 0),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = tooltip,
            };
            b.MouseEnter += delegate { b.Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)); };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; };
            _toolBorders.Add(b);
            _interactive.Add(b);
            return b;
        }

        private void BuildContextMenu()
        {
            var menu = new ContextMenu();

            var refresh = new MenuItem { Header = "Refresh" };
            refresh.Click += delegate { RefreshSlow(); RefreshSessions(); PositionBar(); };
            menu.Items.Add(refresh);

            var hide = new MenuItem { Header = "Hide (restore from tray)" };
            hide.Click += delegate { Hide(); };
            menu.Items.Add(hide);

            var reset = new MenuItem { Header = "Reset position" };
            reset.Click += delegate { ResetPosition(); };
            menu.Items.Add(reset);

            var startup = new MenuItem { Header = "Start with Windows", IsCheckable = true, IsChecked = Startup.IsEnabled() };
            startup.Click += delegate { startup.IsChecked = Startup.Toggle(); };
            menu.Items.Add(startup);

            menu.Items.Add(new Separator());
            var exit = new MenuItem { Header = "Exit" };
            exit.Click += delegate { ExitApp(); };
            menu.Items.Add(exit);

            _root.ContextMenu = menu;
        }

        private void ResetPosition()
        {
            _cfg.CustomLeft = double.NaN;
            _cfg.CustomTop = double.NaN;
            _cfg.Save();
            PositionBar();
        }

        private void ToggleVisible()
        {
            if (IsVisible) Hide();
            else
            {
                Show();
                DesktopTick(); // catch up to whatever desktop we're on now
            }
        }

        private void ExitApp()
        {
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }
            Application.Current.Shutdown();
        }

        // Tray icon: left-click toggles the bar, right-click menu mirrors the
        // bar's own menu. This is what makes the X button safe -- the bar can
        // always be brought back without relaunching the exe.
        private void SetupTray()
        {
            var bmp = new System.Drawing.Bitmap(16, 16);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.Clear(System.Drawing.Color.Transparent);
                using (var br = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0x0E, 0xA5, 0xE9)))
                    g.FillRectangle(br, 1, 5, 14, 7);
            }
            _tray = new System.Windows.Forms.NotifyIcon
            {
                Icon = System.Drawing.Icon.FromHandle(bmp.GetHicon()),
                Text = "JITR DeskBar - click to show/hide",
                Visible = true,
            };
            _tray.MouseUp += delegate(object s, System.Windows.Forms.MouseEventArgs e)
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Left) ToggleVisible();
            };
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Show / hide bar", null, delegate { ToggleVisible(); });
            menu.Items.Add("Reset position", null, delegate { ResetPosition(); });
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { ExitApp(); });
            _tray.ContextMenuStrip = menu;
        }

        // ---------- editing ----------

        private void StartEdit(TextBlock text, TextBox box)
        {
            box.Text = (text == _titleText) ? _profile.Title : _profile.Note;
            text.Visibility = Visibility.Collapsed;
            box.Visibility = Visibility.Visible;
            _editing++;
            Activate();
            box.Focus();
            box.SelectAll();

            KeyEventHandler onKey = null;
            RoutedEventHandler onBlur = null;
            Action<bool> finish = delegate(bool commit)
            {
                box.KeyDown -= onKey;
                box.LostFocus -= onBlur;
                if (commit)
                {
                    var v = box.Text.Trim();
                    if (text == _titleText) _profile.Title = (v.Length > 0 ? v : "Desktop");
                    else _profile.Note = v;
                    _cfg.Save();
                }
                box.Visibility = Visibility.Collapsed;
                text.Visibility = Visibility.Visible;
                _editing--;
                ApplyProfile();
            };
            onKey = delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) { e.Handled = true; finish(true); }
                else if (e.Key == Key.Escape) { e.Handled = true; finish(false); }
            };
            onBlur = delegate { finish(true); };
            box.KeyDown += onKey;
            box.LostFocus += onBlur;
        }

        private void CancelEdits()
        {
            // desktop switched mid-edit: just drop the editors
            if (_editing > 0)
            {
                _titleBox.Visibility = Visibility.Collapsed;
                _noteBox.Visibility = Visibility.Collapsed;
                _titleText.Visibility = Visibility.Visible;
                _noteText.Visibility = Visibility.Visible;
                _editing = 0;
            }
        }

        // ---------- palette ----------

        private static readonly string[] PRESETS =
        {
            "#0EA5E9", "#6366F1", "#8B5CF6", "#EC4899", "#EF4444",
            "#F97316", "#F59E0B", "#22C55E", "#14B8A6", "#64748B",
        };

        private void OpenPalette(UIElement target)
        {
            if (_palette == null)
                _palette = new Popup { StaysOpen = false, Placement = PlacementMode.Bottom, AllowsTransparency = true };
            var wrap = new WrapPanel { Width = 5 * 30 + 8, Margin = new Thickness(6) };
            foreach (var hex in PRESETS)
            {
                var dot = new Ellipse
                {
                    Width = 22,
                    Height = 22,
                    Margin = new Thickness(4),
                    Fill = BrushOf(hex),
                    Stroke = Brushes.White,
                    StrokeThickness = 1,
                    Cursor = Cursors.Hand,
                };
                var chosen = hex;
                dot.MouseLeftButtonUp += delegate
                {
                    _profile.Color = chosen;
                    _cfg.Save();
                    ApplyProfile();
                    _palette.IsOpen = false;
                };
                wrap.Children.Add(dot);
            }
            var custom = new TextBlock
            {
                Text = "Custom...",
                Foreground = Brushes.White,
                FontSize = 12,
                Margin = new Thickness(8, 4, 8, 6),
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            custom.MouseLeftButtonUp += delegate
            {
                _palette.IsOpen = false;
                var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true };
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    _profile.Color = string.Format("#{0:X2}{1:X2}{2:X2}", dlg.Color.R, dlg.Color.G, dlg.Color.B);
                    _cfg.Save();
                    ApplyProfile();
                }
            };
            var panel = new StackPanel();
            panel.Children.Add(wrap);
            panel.Children.Add(custom);
            _palette.Child = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Child = panel,
            };
            _palette.PlacementTarget = target;
            _palette.IsOpen = true;
        }

        // ---------- theming ----------

        private static SolidColorBrush BrushOf(string hex)
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { return new SolidColorBrush(Color.FromRgb(0x0E, 0xA5, 0xE9)); }
        }

        private static SolidColorBrush WithAlpha(Brush b, byte alpha)
        {
            var c = ((SolidColorBrush)b).Color;
            return new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        }

        private void ApplyProfile()
        {
            _profile = _cfg.For(_currentDesktop);
            var accent = BrushOf(_profile.Color);

            _root.Background = new SolidColorBrush(Color.FromArgb(0xEB, 0x0B, 0x12, 0x20));
            _root.BorderBrush = accent;

            _titleText.Text = _profile.Title;
            _titleText.Foreground = accent;

            _noteText.Text = _profile.Note.Length > 0 ? _profile.Note : "add a note...";
            _noteText.Foreground = new SolidColorBrush(
                Color.FromArgb((byte)(_profile.Note.Length > 0 ? 0xFF : 0x99), 0x94, 0xA3, 0xB8));

            _sessText.Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));

            var iconBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1));
            var toolBorder = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55));
            foreach (var p in _toolPaths) p.Stroke = iconBrush;
            foreach (var p in _toolFillPaths) p.Fill = iconBrush;
            foreach (var b in _toolBorders) b.BorderBrush = toolBorder;
            _titlePencil.Stroke = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
            _notePencil.Stroke = _titlePencil.Stroke;
            _closeX.Stroke = _titlePencil.Stroke;

            _swatchDot.Fill = accent;
            _swatchDot.Stroke = WithAlpha(Brushes.White, 0x40);

            _titleBox.BorderBrush = accent;
            _noteBox.BorderBrush = accent;

            _emailText.Text = _email != null ? _email : "claude: not logged in";
            _emailText.Foreground = accent;

            RenderMeters();
        }

        private void RenderMeters()
        {
            _metersPanel.Children.Clear();
            var dim = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1));

            if (_usage == null || _usage.Error != null)
            {
                _metersPanel.Children.Add(new TextBlock
                {
                    Text = _usage == null ? "limits: loading..." : "limits: " + _usage.Error,
                    FontSize = 10,
                    Foreground = dim,
                });
                return;
            }

            int shown = 0;
            foreach (var row in _usage.Rows)
            {
                if (shown++ >= 4) break;
                var g = new Grid { Margin = new Thickness(0, 1, 0, 1), ToolTip = MeterTooltip(row) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });

                var label = new TextBlock { Text = row.Label, FontSize = 10, Foreground = dim, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                Grid.SetColumn(label, 0);
                g.Children.Add(label);

                Brush fill;
                if (row.UsedPct >= 90) fill = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
                else if (row.UsedPct >= 70) fill = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
                else fill = BrushOf(_profile.Color);

                var track = new Border
                {
                    Height = 5,
                    CornerRadius = new CornerRadius(2.5),
                    Background = WithAlpha(Brushes.White, 0x26),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var fillGrid = new Grid();
                fillGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(row.UsedPct, 1), GridUnitType.Star) });
                fillGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(100 - row.UsedPct, 0), GridUnitType.Star) });
                var fillBar = new Border { CornerRadius = new CornerRadius(2.5), Background = fill };
                if (row.UsedPct <= 0) fillBar.Visibility = Visibility.Hidden;
                Grid.SetColumn(fillBar, 0);
                fillGrid.Children.Add(fillBar);
                track.Child = fillGrid;
                Grid.SetColumn(track, 1);
                g.Children.Add(track);

                var pct = new TextBlock
                {
                    Text = row.UsedPct + "% used",
                    FontSize = 10,
                    Foreground = dim,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(pct, 2);
                g.Children.Add(pct);

                _metersPanel.Children.Add(g);
            }
        }

        private string MeterTooltip(UsageRow row)
        {
            var reset = Usage.ResetText(row.ResetsAt);
            var head = string.Format("{0}: {1}% used, {2}% remaining", row.Label, row.UsedPct, 100 - row.UsedPct);
            return reset.Length > 0 ? head + "\n" + reset : head;
        }

        // ---------- data refresh ----------

        private void RefreshSlow()
        {
            _email = Usage.AccountEmail();
            _emailText.Text = _email != null ? _email : "claude: not logged in";
            Usage.FetchAsync(delegate(UsageResult r)
            {
                Dispatcher.BeginInvoke((Action)delegate
                {
                    _usage = r;
                    RenderMeters();
                });
            });
        }

        private void RefreshSessions()
        {
            try
            {
                var names = Sessions.OnCurrentDesktop(_tracker, _hwnd);
                if (names.Count == 0)
                {
                    _sessText.Visibility = Visibility.Collapsed;
                    return;
                }
                _sessText.Text = string.Format("{0} session{1}: {2}",
                    names.Count, names.Count == 1 ? "" : "s", string.Join("  |  ", names));
                _sessText.Visibility = Visibility.Visible;
            }
            catch { _sessText.Visibility = Visibility.Collapsed; }
        }
    }
}
