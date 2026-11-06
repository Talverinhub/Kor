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
        public void RebuildChime()
        {
            if (player != null) player.Stop();
            player = new SoundPlayer(Chime.Build(settings.Alarm, settings.AlarmVolume));
            player.Load();
        }

        public void PreviewChime() { if (mode != Mode.Ringing) player.Play(); }

        static Color Sunset(double hour)
        {
            double[] hours = { 0, 6, 12, 18, 22, 24 };
            Color[] colors = { Color.FromRgb(0xC9, 0xBC, 0xD6), Color.FromRgb(0xC3, 0xD3, 0xDC), Ring.DefaultColor, Color.FromRgb(0xE6, 0xB9, 0x8A), Color.FromRgb(0xC9, 0xBC, 0xD6), Color.FromRgb(0xC9, 0xBC, 0xD6) };
            int i = 0;
            while (i < hours.Length - 2 && hour >= hours[i + 1]) i++;
            double t = (hour - hours[i]) / (hours[i + 1] - hours[i]);
            Color a = colors[i], b = colors[i + 1];
            return Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        }


        public void RefreshAccent()
        {
            var color = settings.CoverColor && spotify.Accent.HasValue ? spotify.Accent.Value
                : settings.Sunset ? Sunset(DateTime.Now.TimeOfDay.TotalHours) : Ring.DefaultColor;
            ring.SetColor(color);
            glow.Color = color;
            ((System.Windows.Shapes.Rectangle)mini.FindName("MiniBar")).Fill = new SolidColorBrush(color);
        }

        void SetGlow(bool on)
        {
            if (on == glowing) return;
            glowing = on;
            var effect = on ? glow : null;
            hText.Effect = mText.Effect = sText.Effect = miniTime.Effect = effect;
            bool battery = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline;
            ring.Effect = battery ? null : effect;
        }
    }
}
