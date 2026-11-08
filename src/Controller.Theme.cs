using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Sayac
{
    partial class Controller
    {
        int combo;
        long lastMinute = -1;
        Mode lastThemeMode = Mode.Setup;
        DateTime nextStepAt = DateTime.MinValue;
        readonly Random fxRandom = new Random();

        void InitTheme()
        {
            if (Theme.Neon)
            {
                Find<FrameworkElement>("XStats").Visibility = Visibility.Visible;
                Find<FrameworkElement>("DialBox").RenderTransform = new ScaleTransform(1, 1);
                Find<FrameworkElement>("Root").RenderTransform = new TranslateTransform();
                Find<FrameworkElement>("FxBig").RenderTransform = new ScaleTransform(1, 1);
                UpdateStats();
            }
        }

        // Her tikte ve her durum değişiminde çağrılır; yalnızca seçili temanın görünümünü günceller.
        void UpdateThemeView()
        {
            if (Theme.Focus) UpdateFocusView();
            if (!Theme.Neon) return;
            if (mode != lastThemeMode)
            {
                if (mode == Mode.Paused || mode == Mode.Setup) { combo = 0; lastMinute = -1; UpdateStats(); }
                lastThemeMode = mode;
            }
            if (mode != Mode.Running) return;
            long minute = Remaining() / 60000;
            if (lastMinute >= 0 && minute != lastMinute) MinuteBurst();
            lastMinute = minute;
        }

        // Odak (ADHD): çalışırken kadran yerine tek iş, büyük süre ve parçalı ilerleme görünür.
        void UpdateFocusView()
        {
            bool show = mode == Mode.Running || mode == Mode.Paused;
            Find<FrameworkElement>("FocusView").Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            Find<FrameworkElement>("DialBox").Visibility = show ? Visibility.Collapsed : Visibility.Visible;
            if (!show) return;
            Find<TextBlock>("FocusTask").Text = string.IsNullOrEmpty(intent) ? L.T("Odaklan") : intent;
            long left = Remaining();
            long seconds = (left + (up ? 0 : 999)) / 1000;
            Find<TextBlock>("FocusTime").Text = seconds >= 3600
                ? string.Format("{0}:{1:00}:{2:00}", seconds / 3600, seconds / 60 % 60, seconds % 60)
                : string.Format("{0:00}:{1:00}", seconds / 60, seconds % 60);
            var parts = Find<UniformGrid>("FocusParts");
            if (up)
            {
                parts.Visibility = Visibility.Collapsed;
                Find<TextBlock>("FocusLeft").Text = L.F("Kronometre · {0} dakika", seconds / 60);
            }
            else
            {
                parts.Visibility = Visibility.Visible;
                double done = totalMs > 0 ? 1 - (double)left / totalMs : 0;
                int filled = Math.Min(5, (int)Math.Floor(done * 5 + 0.0001));
                if (parts.Children.Count != 5)
                {
                    parts.Children.Clear();
                    for (int i = 0; i < 5; i++) parts.Children.Add(new Border { Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(2, 0, 2, 0) });
                }
                for (int i = 0; i < 5; i++)
                    ((Border)parts.Children[i]).Background = new SolidColorBrush(i < filled ? Theme.Accent : Theme.Map(Color.FromRgb(0x2B, 0x2A, 0x27)));
                Find<TextBlock>("FocusLeft").Text = L.F("Parça {0}/5 · yaklaşık {1} dakika kaldı", Math.Min(5, filled + 1), (left + 59999) / 60000);
            }
            if (DateTime.UtcNow < nextStepAt) return;
            nextStepAt = DateTime.UtcNow.AddSeconds(2);
            string next = notes == null ? null : notes.NextStep(intent);
            Find<FrameworkElement>("FocusNextBox").Visibility = next == null ? Visibility.Collapsed : Visibility.Visible;
            if (next != null) Find<TextBlock>("FocusNext").Text = L.F("Sonraki adım: {0}", next);
        }

        // Extreme: her dakika kadran zıplar, parçacık saçılır, kombo ve puan artar.
        void MinuteBurst()
        {
            combo++;
            AddXp(10);
            if (settings.LessMotion) return;
            Bump(Find<FrameworkElement>("DialBox"), 1.06);
            Burst(14, 70);
            Floating("+10");
        }

        void ExtremeFinish()
        {
            if (!Theme.Neon) return;
            combo++;
            AddXp(100);
            string today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            settings.Streak = Logic.NextStreak(settings.Streak, settings.LastFocusDay, today);
            settings.LastFocusDay = today;
            settings.Save();
            UpdateStats();
            if (settings.LessMotion) return;
            Shake();
            Burst(46, 160);
            Floating("+100");
            BigText(L.T("MÜKEMMEL!"));
        }

        void AddXp(int value)
        {
            settings.Xp += value;
            settings.Save();
            UpdateStats();
        }

        void UpdateStats()
        {
            if (!Theme.Neon) return;
            Find<TextBlock>("XStreak").Text = L.F("{0} gün", Math.Max(settings.Streak, 0));
            Find<TextBlock>("XCombo").Text = L.F("Kombo x{0}", combo);
            Find<TextBlock>("XLevel").Text = L.F("Sv {0} · {1}", Logic.Level(settings.Xp), settings.Xp);
        }

        Point DialCenter()
        {
            var dial = Find<FrameworkElement>("DialBox");
            return dial.TranslatePoint(new Point(dial.ActualWidth / 2, dial.ActualHeight / 2), Find<Canvas>("FxLayer"));
        }

        void Burst(int count, double reach)
        {
            var layer = Find<Canvas>("FxLayer");
            var center = DialCenter();
            for (int i = 0; i < count; i++)
            {
                var color = Theme.NeonColors[i % Theme.NeonColors.Length];
                var piece = new Rectangle { Width = 7, Height = 7, RadiusX = 2, RadiusY = 2, Fill = new SolidColorBrush(color) };
                var move = new TranslateTransform();
                var group = new TransformGroup();
                group.Children.Add(new RotateTransform(fxRandom.Next(360), 3.5, 3.5));
                group.Children.Add(move);
                piece.RenderTransform = group;
                Canvas.SetLeft(piece, center.X - 3.5);
                Canvas.SetTop(piece, center.Y - 3.5);
                layer.Children.Add(piece);
                double angle = fxRandom.NextDouble() * Math.PI * 2, distance = reach * (0.5 + fxRandom.NextDouble());
                var time = TimeSpan.FromMilliseconds(700 + fxRandom.Next(400));
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, Math.Cos(angle) * distance, time) { EasingFunction = ease });
                move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, Math.Sin(angle) * distance + 30, time) { EasingFunction = ease });
                var fade = new DoubleAnimation(1, 0, time);
                var target = piece;
                fade.Completed += (a, b) => layer.Children.Remove(target);
                piece.BeginAnimation(UIElement.OpacityProperty, fade);
            }
        }

        void Floating(string text)
        {
            var layer = Find<Canvas>("FxLayer");
            var center = DialCenter();
            var label = new TextBlock { Text = text, FontSize = 22, Foreground = new SolidColorBrush(Theme.NeonColors[4]) };
            var move = new TranslateTransform();
            label.RenderTransform = move;
            Canvas.SetLeft(label, center.X - 20);
            Canvas.SetTop(label, center.Y - 70);
            layer.Children.Add(label);
            var time = TimeSpan.FromMilliseconds(1100);
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -70, time) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            var fade = new DoubleAnimation(1, 0, time);
            fade.Completed += (a, b) => layer.Children.Remove(label);
            label.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        void BigText(string text)
        {
            var big = Find<TextBlock>("FxBig");
            big.Text = text;
            var scale = (ScaleTransform)big.RenderTransform;
            var pop = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(1500) };
            pop.KeyFrames.Add(new EasingDoubleKeyFrame(0.4, KeyTime.FromPercent(0)));
            pop.KeyFrames.Add(new EasingDoubleKeyFrame(1.15, KeyTime.FromPercent(0.25)));
            pop.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1)));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            var fade = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(1500) };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.2)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.7)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
            big.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        static void Bump(FrameworkElement element, double size)
        {
            var scale = element.RenderTransform as ScaleTransform;
            if (scale == null) return;
            var anim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(400) };
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(size, KeyTime.FromPercent(0.4)));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1)));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }

        void Shake()
        {
            var move = Find<FrameworkElement>("Root").RenderTransform as TranslateTransform;
            if (move == null) return;
            var anim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(450) };
            double[] steps = { -6, 6, -5, 4, -2, 0 };
            for (int i = 0; i < steps.Length; i++) anim.KeyFrames.Add(new LinearDoubleKeyFrame(steps[i], KeyTime.FromPercent((i + 1.0) / steps.Length)));
            move.BeginAnimation(TranslateTransform.XProperty, anim);
        }
    }
}
