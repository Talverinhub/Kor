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
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        void RoundCorners()
        {
            int round = 2;
            try { DwmSetWindowAttribute(new WindowInteropHelper(win).Handle, 33, ref round, 4); }
            catch (DllNotFoundException) { }
        }


        const double DefaultWidth = 440, DefaultHeight = 760, ScreenShare = 0.9;

        // Pencere her zaman kullanılabilir ekran alanına sığar; son boyut ve konum korunur.
        void FitWindow()
        {
            var area = SystemParameters.WorkArea;
            bool saved = settings.WinWidth > 0 && settings.WinHeight > 0;
            double width = saved ? settings.WinWidth : DefaultWidth, height = saved ? settings.WinHeight : DefaultHeight;
            if (!saved) height = Math.Min(height, area.Height * ScreenShare);
            win.Width = Math.Max(win.MinWidth, Math.Min(width, area.Width));
            win.Height = Math.Max(win.MinHeight, Math.Min(height, area.Height));
            if (!saved) return;
            double left = settings.WinLeft, top = settings.WinTop;
            double right = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth;
            double bottom = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
            if (left < SystemParameters.VirtualScreenLeft || top < SystemParameters.VirtualScreenTop || left + win.Width > right || top + win.Height > bottom) return;
            win.WindowStartupLocation = WindowStartupLocation.Manual;
            win.Left = left;
            win.Top = top;
        }

        void SaveWindow()
        {
            if (win.WindowState != WindowState.Normal || !win.IsVisible) return;
            settings.WinLeft = win.Left;
            settings.WinTop = win.Top;
            settings.WinWidth = win.ActualWidth;
            settings.WinHeight = win.ActualHeight;
            settings.Save();
        }
    }
}
