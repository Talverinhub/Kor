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
        TextBlock notice;
        TextBox intentBox, noteBox;
        bool up, songExtended;
        int songsLeft;
        string intent, missed;

        void ShowNotice()
        {
            var lines = new System.Collections.Generic.List<string>();
            if (missed != null) lines.Add(missed);
            if (!string.IsNullOrEmpty(settings.LastNote)) lines.Add(L.T("Son notun: ") + settings.LastNote);
            notice.Text = string.Join("\n", lines);
            Find<FrameworkElement>("NoticeRow").Visibility = lines.Count > 0 && mode == Mode.Setup ? Visibility.Visible : Visibility.Collapsed;
        }

        public void RefreshPresets()
        {
            presets.Children.Clear();
            foreach (int minutes in settings.Presets)
            {
                int seconds = minutes * 60;
                var chip = new Button { Content = minutes + L.T(" dk"), Style = (Style)win.FindResource("Preset") };
                chip.Click += (a, b) => SetSeconds(seconds);
                presets.Children.Add(chip);
            }
            var row = (Panel)mini.FindName("MiniQuickRow");
            row.Children.Clear();
            foreach (int minutes in settings.Quick)
            {
                int seconds = minutes * 60;
                var chip = new Button { Content = minutes + L.T(" dk"), Style = (Style)mini.FindResource("Ask"), Margin = new Thickness(0, 0, 6, 0) };
                chip.Click += (a, b) => { SetMiniPanel(null); SetSeconds(seconds); Start(); };
                row.Children.Add(chip);
            }
            Find<FrameworkElement>("IntentRow").Visibility = settings.AskIntent && mode == Mode.Setup ? Visibility.Visible : Visibility.Collapsed;
        }

        public void RefreshGoal()
        {
            if (settings.DailyGoal <= 0) { ring.ClearGoal(); return; }
            ring.SetGoal(stats.Day(DateTime.Now).Seconds / 60 / settings.DailyGoal, glow.Color);
        }


        public void Refresh() { Apply(); }

        int setupMode;
        bool intentOpen;
        bool CanRun { get { return mode != Mode.Setup || setupMode != 0 || Seconds > 0; } }

        void ShowSetupDigits()
        {
            if (setupMode == 0) ShowDigits(h, m, s); else ShowDigits(0, 0, 0);
        }

        void SetSetupMode(int value)
        {
            if (mode != Mode.Setup) return;
            setupMode = value;
            if (settings.LastMode != value) { settings.LastMode = value; settings.Save(); }
            ShowSetupDigits();
            Find<TextBlock>("SongText").Text = L.F("{0} şarkı sonra bitir", settings.SongCount);
            mainBtn.IsEnabled = CanRun;
            string[] tabs = { "ModeTimer", "ModeWatch", "ModeSongs" };
            for (int i = 0; i < tabs.Length; i++)
                Find<Button>(tabs[i]).Foreground = new SolidColorBrush(i == value ? Ring.DefaultColor : Color.FromRgb(0x8E, 0x8A, 0x82));
            Apply();
        }

        void ChangeSongs(int delta)
        {
            settings.SongCount = Math.Max(1, Math.Min(99, settings.SongCount + delta));
            settings.Save();
            Find<TextBlock>("SongText").Text = L.F("{0} şarkı sonra bitir", settings.SongCount);
        }

        long undoValue, undoTotal;
        bool undoUp;
        string undoIntent;
        DispatcherTimer undoTimer;

        // Çalışan sayaç sıfırlanınca birkaç saniye geri alınabilir.
        void ResetWithUndo()
        {
            bool active = mode == Mode.Running || mode == Mode.Paused;
            if (active) { undoValue = Remaining(); undoTotal = totalMs; undoUp = up; undoIntent = intent; }
            Reset();
            if (!active) return;
            var undo = Find<FrameworkElement>("UndoBtn");
            undo.Visibility = Visibility.Visible;
            if (undoTimer == null)
            {
                undoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
                undoTimer.Tick += (a, b) => { undoTimer.Stop(); undo.Visibility = Visibility.Collapsed; };
            }
            undoTimer.Stop();
            undoTimer.Start();
        }

        void UndoReset()
        {
            Find<FrameworkElement>("UndoBtn").Visibility = Visibility.Collapsed;
            undoTimer.Stop();
            if (mode != Mode.Setup) return;
            up = undoUp;
            intent = undoIntent;
            totalMs = undoTotal;
            remainingAtResume = undoValue;
            mode = Mode.Paused;
            if (up) ShowElapsed(undoValue); else ShowLeft(undoValue);
            if (miniPanel == null) SetMiniPanel("paused");
            SaveRun();
            Apply();
        }


        // Sığmayan rutin adı düğmenin içinde sonsuz döngüyle kayar.
        object ChipText(string name)
        {
            const double Room = 112, Speed = 35, Hold = 1.5;
            var text = new TextBlock { Text = name, FontFamily = win.FontFamily, FontSize = 14 };
            text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double extra = text.DesiredSize.Width - Room;
            if (extra <= 0 || settings.LessMotion) return name;
            // Yazının arkasından kopyası gelir; döngü başa dönerken atlama görünmez.
            const double Gap = 36;
            double step = text.DesiredSize.Width + Gap;
            var strip = new StackPanel { Orientation = Orientation.Horizontal };
            strip.Children.Add(text);
            strip.Children.Add(new TextBlock { Text = name, FontFamily = win.FontFamily, FontSize = 14, Margin = new Thickness(Gap, 0, 0, 0) });
            var box = new Canvas { Width = Room, Height = text.DesiredSize.Height, ClipToBounds = true };
            box.Children.Add(strip);
            var move = new TranslateTransform();
            strip.RenderTransform = move;
            var motion = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
            motion.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(Hold))));
            motion.KeyFrames.Add(new LinearDoubleKeyFrame(-step, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(Hold + step / Speed))));
            move.BeginAnimation(TranslateTransform.XProperty, motion);
            return box;
        }

        public void RefreshRoutines()
        {
            routineBar.Children.Clear();
            foreach (var r in settings.Routines.Take(2))
            {
                var routine = r;
                var chip = new Button { Content = ChipText(r.Name), Style = (Style)win.FindResource("Preset"), ToolTip = r.Name + ", " + FocusPanel.Duration(r.Seconds) };
                chip.Click += (a, b) => StartRoutine(routine);
                routineBar.Children.Add(chip);
            }
            var more = new Button { Content = settings.Routines.Count == 0 ? L.T("Rutin oluştur") : L.T("Rutinler"), Style = (Style)win.FindResource("Preset") };
            more.Click += (a, b) => panel.Show(0);
            routineBar.Children.Add(more);
        }

        public void StartRoutine(Routine r)
        {
            if (mode != Mode.Setup) Reset();
            SetSeconds(r.Seconds);
            if (!string.IsNullOrEmpty(r.Uri)) spotify.PlayContext(r.Uri);
            Start();
        }
    }
}
