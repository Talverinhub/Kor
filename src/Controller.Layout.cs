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
        void Apply()
        {
            bool setup = mode == Mode.Setup, active = mode == Mode.Running || mode == Mode.Paused;
            double width = win.ActualWidth > 0 ? win.ActualWidth : win.Width;
            double height = win.ActualHeight > 0 ? win.ActualHeight : win.Height;
            // Pencere küçülünce yalnızca kalan süre kalır ve alanı doldurur.
            bool compact = height < 640 || width < 300;
            var full = compact ? Visibility.Collapsed : Visibility.Visible;
            dial.Width = compact ? double.NaN : 380;
            dial.Height = compact ? double.NaN : 380;
            ringHost.Visibility = full;
            statusRow.Visibility = full;
            bottomRow.Visibility = full;
            if (asking) statusRow.Visibility = Visibility.Visible;
            // Spotify hiç kaybolmaz: yer azaldıkça önce kapak, sonra sarma çubuğu gizlenir.
            bool side = compact && width >= 520 && height < 320;
            Find<ColumnDefinition>("SideCol").Width = side ? new GridLength(1.2, GridUnitType.Star) : new GridLength(0);
            Grid.SetRow(spotifyRow, side ? 1 : 4);
            Grid.SetColumn(spotifyRow, side ? 1 : 0);
            Grid.SetColumnSpan(spotifyRow, side ? 1 : 2);
            spotifyRow.Margin = side ? new Thickness(0, 0, 14, 10) : new Thickness(24, 0, 24, 14);
            double room = side ? height - 58 : compact ? height - 134 : 999;
            FitSpotify(win, room, !side && width >= 360);
            FitPlayer(width, compact, side);
            foreach (var u in units) u.Visibility = full;
            foreach (var c in colons) c.Margin = new Thickness(0, 0, 0, compact ? 6 : 26);
            foreach (var a in arrows) a.Visibility = compact ? Visibility.Collapsed : setup && setupMode == 0 ? Visibility.Visible : Visibility.Hidden;
            presets.Visibility = setup && setupMode == 0 && width >= 400 ? Visibility.Visible : Visibility.Collapsed;
            routineBar.Visibility = setup && setupMode == 0 ? Visibility.Visible : Visibility.Collapsed;
            statusBox.Visibility = setup ? Visibility.Collapsed : Visibility.Visible;
            addOne.Visibility = addFive.Visibility = active && !asking ? Visibility.Visible : Visibility.Collapsed;
            Find<FrameworkElement>("AskYes").Visibility = Find<FrameworkElement>("AskNo").Visibility = asking ? Visibility.Visible : Visibility.Collapsed;
            snoozeBtn.Visibility = mode == Mode.Ringing ? Visibility.Visible : Visibility.Collapsed;
            if (up) addOne.Visibility = addFive.Visibility = Visibility.Collapsed;
            Find<FrameworkElement>("IntentRow").Visibility = setup && settings.AskIntent ? Visibility.Visible : Visibility.Collapsed;
            Find<FrameworkElement>("NoteRow").Visibility = mode == Mode.Ringing && settings.AskNote ? Visibility.Visible : Visibility.Collapsed;
            bool wantIntent = setup && settings.AskIntent;
            bool showBox = wantIntent && (intentOpen || intentBox.Text.Length > 0);
            Find<FrameworkElement>("IntentRow").Visibility = showBox ? Visibility.Visible : Visibility.Collapsed;
            Find<FrameworkElement>("IntentAdd").Visibility = wantIntent && !showBox ? Visibility.Visible : Visibility.Collapsed;
            Find<FrameworkElement>("ModeBar").Visibility = setup ? Visibility.Visible : Visibility.Collapsed;
            Find<FrameworkElement>("SongBar").Visibility = setup && setupMode == 2 ? Visibility.Visible : Visibility.Collapsed;
            var topPlay = Find<FrameworkElement>("TopPlay");
            bool panelOpen = panel != null && panel.Open;
            topPlay.Visibility = compact && !panelOpen ? Visibility.Visible : Visibility.Collapsed;
            Find<FrameworkElement>("PanelBack").Visibility = panelOpen ? Visibility.Visible : Visibility.Collapsed;
            Find<System.Windows.Shapes.Path>("TopPlayIcon").Data = mode == Mode.Running ? PauseIcon : mode == Mode.Ringing ? StopIcon : PlayIcon;
            var label = Find<TextBlock>("IntentLabel");
            label.Text = intent ?? "";
            label.Visibility = !setup && !string.IsNullOrEmpty(intent) ? Visibility.Visible : Visibility.Collapsed;
            gapCol.Width = new GridLength(active ? 12 : 0);
            secCol.Width = active ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            resetBtn.Visibility = active ? Visibility.Visible : Visibility.Collapsed;

            miniIcon.Data = mode == Mode.Running ? PauseIcon : mode == Mode.Ringing ? StopIcon : PlayIcon;
            UpdateMini();

            var bar = win.TaskbarItemInfo;
            switch (mode)
            {
                case Mode.Setup:
                    mainBtn.Content = setupMode == 1 ? L.T("Kronometreyi başlat") : setupMode == 2 ? L.T("Şarkı sayacını başlat") : L.T("Başlat");
                    mainBtn.IsEnabled = CanRun;
                    bar.ProgressState = TaskbarItemProgressState.None;
                    break;
                case Mode.Running:
                    mainBtn.Content = L.T("Duraklat");
                    status.Text = asking ? L.T("5 dakika daha?")
                        : songsLeft > 0 ? songsLeft + L.T(" şarkı kaldı")
                        : up ? L.T("Kronometre")
                        : songExtended ? L.T("Şarkı bitince bitecek")
                        : L.T("Bitiş ") + DateTime.Now.AddMilliseconds(Remaining()).ToString("HH:mm");
                    bar.ProgressState = TaskbarItemProgressState.Normal;
                    break;
                case Mode.Paused:
                    mainBtn.Content = L.T("Devam et");
                    status.Text = pausedByMusic ? L.T("Müzik durdu, sayaç bekliyor") : L.T("Duraklatıldı");
                    bar.ProgressState = TaskbarItemProgressState.Paused;
                    break;
                case Mode.Ringing:
                    mainBtn.Content = L.T("Bitir");
                    status.Text = L.T("Süre doldu");
                    bar.ProgressState = TaskbarItemProgressState.Error;
                    bar.ProgressValue = 1;
                    break;
            }
        }
    }
}
