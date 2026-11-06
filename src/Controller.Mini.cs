using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Path = System.IO.Path;
using System.Media;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shell;
using System.Windows.Threading;

namespace Sayac
{
    partial class Controller
    {
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int index, int value);

        [StructLayout(LayoutKind.Sequential)]
        struct CursorPoint { public int X, Y; }
        [DllImport("user32.dll")] static extern bool GetCursorPos(out CursorPoint p);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
        DispatcherTimer clickTimer;

        void SetPassThrough(bool on)
        {
            const int ExStyle = -20, Transparent = 0x20;
            var handle = new WindowInteropHelper(mini).Handle;
            if (handle == IntPtr.Zero) return;
            int style = GetWindowLong(handle, ExStyle);
            int wanted = on ? style | Transparent : style & ~Transparent;
            if (wanted != style) SetWindowLong(handle, ExStyle, wanted);
        }

        public void ApplyClickThrough()
        {
            trayClick.Checked = settings.ClickThrough;
            bool on = settings.ClickThrough && fade > 0 && miniPanel == null;
            if (clickTimer == null)
            {
                clickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
                clickTimer.Tick += (a, b) => TrackPointer();
            }
            if (on) clickTimer.Start();
            else
            {
                clickTimer.Stop();
                SetPassThrough(false);
            }
        }

        // Boş alanda tıklama alttaki pencereye geçer; düğme ve sürgüler çalışır, Ctrl tüm pencereyi açar.
        void TrackPointer()
        {
            if (!mini.IsVisible) return;
            const int LeftButton = 1, Control = 0x11;
            CursorPoint p;
            if (!GetCursorPos(out p)) return;
            var point = mini.PointFromScreen(new Point(p.X, p.Y));
            bool inside = point.X >= 0 && point.Y >= 0 && point.X < mini.ActualWidth && point.Y < mini.ActualHeight;
            bool held = (GetAsyncKeyState(LeftButton) & 0x8000) != 0;
            if (held && interactive) return;
            bool ctrl = (GetAsyncKeyState(Control) & 0x8000) != 0;
            bool control = false;
            if (inside)
                for (var node = mini.InputHitTest(point) as DependencyObject; node != null; node = VisualTreeHelper.GetParent(node))
                    if (node is ButtonBase || node is Slider) { control = true; break; }
            interactive = inside && (control || ctrl);
            SetPassThrough(!interactive);
            mini.Opacity = interactive ? 1 : FadeLevels[fade];
        }

        bool interactive;

        void ToggleClickThrough()
        {
            settings.ClickThrough = !settings.ClickThrough;
            settings.Save();
            ApplyClickThrough();
        }


        const int AskSeconds = 20, AskAddSeconds = 300;
        string miniPanel;
        bool askDone;
        bool asking { get { return miniPanel == "ask"; } }

        void RunAsk(long left)
        {
            if (left > AskSeconds * 1000) { askDone = false; return; }
            if (askDone || !settings.AskMore || totalMs <= AskSeconds * 1000 || up) return;
            askDone = true;
            ShowAsk(true);
        }

        void ShowAsk(bool on)
        {
            if (on) SetMiniPanel("ask");
            else if (asking) SetMiniPanel(null);
        }

        // Mini sayaçta süre sol üste küçülerek kayar, seçilen panel altında belirir.
        void SetMiniPanel(string name)
        {
            if (miniPanel == name) return;
            miniPanel = name;
            var clock = (ScaleTransform)mini.FindName("MiniClockScale");
            var time = new Duration(TimeSpan.FromMilliseconds(320));
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            double size = name != null ? 0.52 : 1;
            clock.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(size, time) { EasingFunction = ease });
            clock.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(size, time) { EasingFunction = ease });
            string[][] panels = { new[] { "ask", "MiniAsk" }, new[] { "end", "MiniEnd" }, new[] { "quick", "MiniQuick" }, new[] { "paused", "MiniPausedBox" } };
            foreach (var item in panels)
            {
                var box = (FrameworkElement)mini.FindName(item[1]);
                bool show = item[0] == name;
                if (show) box.Visibility = Visibility.Visible;
                var fadeBox = new DoubleAnimation(show ? 1 : 0, time);
                if (!show) fadeBox.Completed += (a, b) => { if (box.Opacity < 0.01) box.Visibility = Visibility.Collapsed; };
                box.BeginAnimation(UIElement.OpacityProperty, fadeBox);
            }
            mini.Opacity = name != null || mini.IsMouseOver ? 1 : FadeLevels[fade];
            ApplyClickThrough();
            Apply();
        }

        void AnswerAsk(bool yes)
        {
            ShowAsk(false);
            if (yes) AddSeconds(AskAddSeconds);
        }


        void Pin()
        {
            pinned = true;
            if (!inTray) win.WindowState = WindowState.Minimized;
            Apply();
        }

        public void Expand()
        {
            pinned = false;
            inTray = false;
            win.Show();
            win.WindowState = WindowState.Normal;
            win.Activate();
            Apply();
        }


        void LoadFade()
        {
            try
            {
                int v;
                if (int.TryParse(File.ReadAllText(fadePath), out v) && v >= 0 && v < FadeLevels.Length) fade = v;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            mini.Opacity = FadeLevels[fade];
        }

        void NextFade()
        {
            fade = (fade + 1) % FadeLevels.Length;
            mini.Opacity = FadeLevels[fade];
            ApplyClickThrough();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(fadePath));
                File.WriteAllText(fadePath, fade.ToString());
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Rect { public int Left, Top, Right, Bottom; }

        // Köşeden çekerken mini sayacın en-boy oranı korunur, içerik birlikte ölçeklenir.
        IntPtr KeepMiniRatio(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WmSizing = 0x214, Left = 1, Right = 2, Top = 3, TopLeft = 4, TopRight = 5, Bottom = 6;
            if (msg != WmSizing) return IntPtr.Zero;
            var r = (Rect)Marshal.PtrToStructure(lParam, typeof(Rect));
            int edge = wParam.ToInt32();
            if (edge == Top || edge == Bottom)
                r.Right = r.Left + (int)Math.Round((r.Bottom - r.Top) * MiniRatio);
            else
            {
                int height = (int)Math.Round((r.Right - r.Left) / MiniRatio);
                if (edge == TopLeft || edge == TopRight) r.Top = r.Bottom - height;
                else r.Bottom = r.Top + height;
            }
            Marshal.StructureToPtr(r, lParam, false);
            handled = true;
            return new IntPtr(1);
        }

        void PlaceMini()
        {
            var area = SystemParameters.WorkArea;
            double width = Math.Max(mini.MinWidth, Math.Min(580, area.Width * 0.3));
            double left = area.Right - width - 16, top = area.Top + 16;
            try
            {
                var parts = File.ReadAllText(miniPlacePath).Split(' ');
                double l, t, w;
                var inv = CultureInfo.InvariantCulture;
                if (parts.Length == 3 && double.TryParse(parts[0], NumberStyles.Float, inv, out l)
                    && double.TryParse(parts[1], NumberStyles.Float, inv, out t) && double.TryParse(parts[2], NumberStyles.Float, inv, out w)
                    && w >= mini.MinWidth
                    && l >= SystemParameters.VirtualScreenLeft && t >= SystemParameters.VirtualScreenTop
                    && l + w <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
                    && t + 40 <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
                { left = l; top = t; width = w; }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            mini.Width = width;
            mini.Height = width / MiniRatio;
            mini.Left = left;
            mini.Top = top;
        }

        void SaveMiniPlace()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(miniPlacePath));
                File.WriteAllText(miniPlacePath, string.Format(CultureInfo.InvariantCulture, "{0} {1} {2}", mini.Left, mini.Top, mini.ActualWidth));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        void UpdateMini()
        {
            bool show = (win.WindowState == WindowState.Minimized || inTray) && (mode != Mode.Setup || pinned);
            if (show && !mini.IsVisible)
            {
                if (!miniPlaced) { PlaceMini(); miniPlaced = true; }
                mini.Show();
            }
            else if (!show && mini.IsVisible)
            {
                SaveMiniPlace();
                mini.Hide();
            }
        }
    }
}
