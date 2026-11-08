using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Sayac
{
    partial class Controller
    {
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int ht, uint flags);
        static readonly IntPtr TopmostZ = new IntPtr(-1);
        const uint KeepPlace = 0x0001 | 0x0002 | 0x0010 | 0x0200;
        DispatcherTimer topmostTimer;

        // Görev çubuğuna tıklamak onu "her zaman üstte" pencerelerin de önüne alır.
        // Mini sayaç görünürken üstte olduğu, odak çalmadan kısa aralıklarla yeniden bildirilir.
        void KeepMiniOnTop()
        {
            topmostTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            topmostTimer.Tick += (a, b) =>
            {
                if (!mini.IsVisible) return;
                Raise(mini);
                if (notes != null) notes.RaiseIfOnTop(Raise);
            };
            mini.IsVisibleChanged += (a, b) =>
            {
                if (!mini.IsVisible) { topmostTimer.Stop(); return; }
                KeepInWorkArea();
                Raise(mini);
                topmostTimer.Start();
            };
            mini.Deactivated += (a, b) => Raise(mini);
        }

        // Mini sayaç görev çubuğunun altına taşmışsa çalışma alanının içine çekilir.
        void KeepInWorkArea()
        {
            var area = SystemParameters.WorkArea;
            if (mini.Left + mini.ActualWidth <= area.Left || mini.Left >= area.Right) return;
            double height = mini.ActualHeight > 0 ? mini.ActualHeight : mini.Height;
            if (mini.Top + height > area.Bottom) mini.Top = Math.Max(area.Top, area.Bottom - height);
            if (mini.Top < area.Top) mini.Top = area.Top;
        }

        static void Raise(Window window)
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero) SetWindowPos(handle, TopmostZ, 0, 0, 0, 0, KeepPlace);
        }
    }
}
