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
    static class Chime
    {
        // Yumuşak çan sesi, kodla üretilir; harici ses dosyası gerekmez.
        public static readonly string[] Names = { L.T("Yumuşak çan"), L.T("Kristal"), L.T("Derin"), L.T("Zil") };

        public static MemoryStream Build(int kind, int volume)
        {
            const int rate = 44100;
            int n = rate * 3;
            var samples = new double[n];
            double[][] allStrikes = { new[] { 0.0, 0.42 }, new[] { 0.0, 0.25 }, new[] { 0.0, 0.6 }, new[] { 0.0, 0.18, 0.36 } };
            double[][] allNotes = { new[] { 880.0, 1174.66 }, new[] { 1318.5, 1760.0 }, new[] { 392.0, 523.25 }, new[] { 1046.5, 1046.5, 1046.5 } };
            kind = Math.Max(0, Math.Min(allNotes.Length - 1, kind));
            double[] strikes = allStrikes[kind], notes = allNotes[kind];
            double gain = 0.5 * volume / 100.0;
            double[] partials = { 1, 2.01, 3.02, 4.2 };
            double[] gains = { 1, 0.45, 0.22, 0.1 };
            for (int s = 0; s < strikes.Length; s++)
            {
                int start = (int)(strikes[s] * rate);
                for (int i = start; i < n; i++)
                {
                    double t = (i - start) / (double)rate, v = 0;
                    for (int p = 0; p < partials.Length; p++)
                        v += gains[p] * Math.Exp(-t * (2.2 + p * 1.6)) * Math.Sin(2 * Math.PI * notes[s] * partials[p] * t);
                    samples[i] += v * Math.Min(1, t * 400);
                }
            }
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + n * 2);
            w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' }); w.Write(16);
            w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2);
            w.Write((short)2); w.Write((short)16);
            w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(n * 2);
            for (int i = 0; i < n; i++)
            {
                double fade = Math.Min(1, (n - i) / (rate * 0.3));
                w.Write((short)(Math.Max(-1, Math.Min(1, samples[i] * gain * fade)) * 32000));
            }
            w.Flush();
            ms.Position = 0;
            return ms;
        }
    }
}
