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
    // Kadran: 120 çentik, süre aktıkça saat yönünde söner.
    class Ring : FrameworkElement
    {
        const int Count = 120;
        double fraction = 1, level = 1;
        readonly Pen[] minor = new Pen[33], major = new Pen[33];

        public static readonly Color DefaultColor = Theme.Map(Color.FromRgb(0xD8, 0xC4, 0x9A));

        public Ring() { SetColor(DefaultColor); }

        Pen[][] neonMinor, neonMajor;

        void BuildNeon()
        {
            int groups = Sayac.Theme.NeonColors.Length;
            neonMinor = new Pen[groups][]; neonMajor = new Pen[groups][];
            for (int g = 0; g < groups; g++)
            {
                neonMinor[g] = new Pen[33]; neonMajor[g] = new Pen[33];
                for (int i = 0; i <= 32; i++)
                {
                    neonMinor[g][i] = MakePen(i / 32.0, 2.0, Sayac.Theme.NeonColors[g]);
                    neonMajor[g][i] = MakePen(i / 32.0, 3.2, Sayac.Theme.NeonColors[g]);
                }
            }
        }

        public void SetColor(Color color)
        {
            if (Sayac.Theme.Neon && neonMinor == null) BuildNeon();
            for (int i = 0; i <= 32; i++)
            {
                minor[i] = MakePen(i / 32.0, 1.4, color);
                major[i] = MakePen(i / 32.0, 2.2, color);
            }
            var white = Theme.Map(Color.FromRgb(255, 255, 255));
            var bright = Color.FromRgb((byte)(color.R + (white.R - color.R) * 0.45), (byte)(color.G + (white.G - color.G) * 0.45), (byte)(color.B + (white.B - color.B) * 0.45));
            for (int i = 0; i < BeatSteps; i++)
            {
                double mix = (i + 1.0) / BeatSteps;
                var c = Color.FromRgb((byte)(color.R + (bright.R - color.R) * mix), (byte)(color.G + (bright.G - color.G) * mix), (byte)(color.B + (bright.B - color.B) * mix));
                beatMinor[i] = MakePen(1, 1.4, c);
                beatMajor[i] = MakePen(1, 2.2, c);
            }
            InvalidateVisual();
        }

        const int BeatSteps = 8;
        readonly Pen[] beatMinor = new Pen[BeatSteps], beatMajor = new Pen[BeatSteps];
        double beat;

        // Yalnızca yanan çentikler tepki verir; kalan süre aynı okunur.
        public void SetBeat(double value)
        {
            if (Math.Abs(value - beat) < 0.01) return;
            beat = value;
            InvalidateVisual();
        }

        static Pen MakePen(double t, double width, Color on)
        {
            Color off = Theme.Map(Color.FromRgb(0x2A, 0x29, 0x26));
            var c = Color.FromRgb((byte)(off.R + (on.R - off.R) * t), (byte)(off.G + (on.G - off.G) * t), (byte)(off.B + (on.B - off.B) * t));
            var b = new SolidColorBrush(c); b.Freeze();
            var p = new Pen(b, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            p.Freeze();
            return p;
        }

        double goal;
        Pen goalPen, goalTrack;

        public void SetGoal(double value, Color color)
        {
            goal = Math.Max(0, Math.Min(1, value));
            var on = new SolidColorBrush(Color.FromArgb(170, color.R, color.G, color.B)); on.Freeze();
            var off = new SolidColorBrush(Theme.Map(Color.FromRgb(0x1E, 0x1D, 0x1B))); off.Freeze();
            goalPen = new Pen(on, 2.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            goalTrack = new Pen(off, 2.5);
            InvalidateVisual();
        }

        public void ClearGoal() { goalPen = null; InvalidateVisual(); }

        public void Set(double newFraction, double newLevel)
        {
            if (newFraction == fraction && newLevel == level) return;
            fraction = newFraction; level = newLevel;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double cx = ActualWidth / 2, cy = ActualHeight / 2, outer = Math.Min(cx, cy) - 3;
            double spent = (1 - fraction) * Count;
            if (goalPen != null)
            {
                double r = outer - 26;
                dc.DrawEllipse(null, goalTrack, new Point(cx, cy), r, r);
                if (goal >= 0.999) dc.DrawEllipse(null, goalPen, new Point(cx, cy), r, r);
                else if (goal > 0)
                {
                    double angle = goal * 2 * Math.PI;
                    var figure = new PathFigure { StartPoint = new Point(cx, cy - r), IsClosed = false };
                    figure.Segments.Add(new ArcSegment(new Point(cx + r * Math.Sin(angle), cy - r * Math.Cos(angle)), new Size(r, r), 0, goal > 0.5, SweepDirection.Clockwise, true));
                    var path = new PathGeometry();
                    path.Figures.Add(figure);
                    dc.DrawGeometry(null, goalPen, path);
                }
            }
            for (int i = 0; i < Count; i++)
            {
                double lit = Math.Max(0, Math.Min(1, i + 1 - spent)) * level;
                bool big = i % 10 == 0;
                bool react = lit > 0.95 && beat > 0.02;
                double len = (big ? 16 : 9) + (react ? beat * (big ? 7 : 5) : 0), a = i * 2 * Math.PI / Count;
                double sx = Math.Sin(a), cs = -Math.Cos(a);
                var pens = big ? major : minor;
                if (neonMinor != null) pens = (big ? neonMajor : neonMinor)[i * neonMinor.Length / Count];
                var pen = react ? (big ? beatMajor : beatMinor)[Math.Min(BeatSteps - 1, (int)(beat * BeatSteps))] : pens[(int)Math.Round(lit * 32)];
                dc.DrawLine(pen,
                    new Point(cx + sx * outer, cy + cs * outer),
                    new Point(cx + sx * (outer - len), cy + cs * (outer - len)));
            }
        }
    }
}
