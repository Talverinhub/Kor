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
        readonly AudioBeat audioBeat = new AudioBeat();
        DispatcherTimer beatTimer;

        // Ses yalnızca kadran görünürken izlenir; tepside veya küçültülmüşken durur.
        ConnectBridge bridge;

        public string BridgeError { get { return bridge == null ? null : bridge.Error; } }
        public bool BridgeConnected { get { return bridge != null && bridge.Connected; } }
        public bool BridgeOutdated { get { return bridge != null && bridge.Outdated; } }
        public string[] BridgeProblems { get { return bridge == null ? new string[0] : bridge.Problems; } }

        string pendingOrigin;

        public void RefreshBridge()
        {
            if (bridge == null)
            {
                bridge = new ConnectBridge(origin => settings.TrustedExtensions.Contains(origin));
                bridge.ApprovalNeeded = origin => win.Dispatcher.BeginInvoke(new Action(() => AskConnect(origin)));
                bridge.ProblemFound = source => win.Dispatcher.BeginInvoke(new Action(() =>
                    tray.ShowBalloonTip(5000, ConnectBridge.SourceName(source) + L.T(" okunamadı"), L.T("Sayfayı yenile. Düzelmezse TalverinConnect'i Ayarlar, Müzik bölümünden yeniden kur."), System.Windows.Forms.ToolTipIcon.None)));
                bridge.ShowRequested = () => win.Dispatcher.BeginInvoke(new Action(Expand));
                bridge.StateArrived = spotify.RefreshNow;
                spotify.Bridge = bridge;
                spotify.SourceMode = () => settings.MusicSource;
                Find<Button>("ConnectAllow").Click += (a, b) => AnswerConnect(true);
                Find<Button>("ConnectDeny").Click += (a, b) => AnswerConnect(false);
            }
            if (settings.BridgeOn)
            {
                try { ConnectBridge.RefreshExtensionFiles(); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                bridge.Start();
            }
            else bridge.Stop();
            spotify.WatchBridge();
        }

        void AskConnect(string origin)
        {
            pendingOrigin = origin;
            if (!win.IsVisible || win.WindowState == WindowState.Minimized) Expand();
            Find<FrameworkElement>("ConnectOverlay").Visibility = Visibility.Visible;
        }

        void AnswerConnect(bool allow)
        {
            Find<FrameworkElement>("ConnectOverlay").Visibility = Visibility.Collapsed;
            if (pendingOrigin == null) return;
            if (allow && !settings.TrustedExtensions.Contains(pendingOrigin))
            {
                settings.TrustedExtensions.Add(pendingOrigin);
                settings.Save();
            }
            bridge.Decide(pendingOrigin, allow);
            pendingOrigin = null;
        }

        public void RefreshBeat()
        {
            AudioBeat.SourceProcess = spotify == null ? null : spotify.SoundProcess;
            bool battery = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline;
            bool on = settings != null && settings.BeatMode > 0 && !settings.LessMotion && spotify != null && spotify.Playing && win.IsVisible && win.WindowState != WindowState.Minimized;
            if (beatTimer == null)
            {
                beatTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
                beatTimer.Tick += (a, b) => ring.SetBeat(Math.Min(1, audioBeat.Level * (settings.BeatMode == 2 ? 1 : 0.55)) * (mode == Mode.Ringing ? 0 : 1));
            }
            beatTimer.Interval = TimeSpan.FromMilliseconds(battery ? 66 : 33);
            if (on) { audioBeat.Start(); beatTimer.Start(); }
            else
            {
                audioBeat.Stop();
                beatTimer.Stop();
                ring.SetBeat(0);
            }
        }

        void SetSetupModeSoon() { win.Loaded += (a, b) => { if (mode == Mode.Setup) SetSetupMode(setupMode); }; }

        const double SlimFullWidth = 600, PlayerHeight = 110, ControlsSmall = 128, ControlsFull = 256, CoverSpace = 62, SideMargins = 48;
        bool lastSlim;

        // Müzik durunca kapak, yazılar ve ses kontrollerle aynı satıra iner, çalınca yerine döner.
        void FitPlayer(double width, bool compact, bool side)
        {
            bool slim = spotify != null && spotify.IsConnected && !spotify.Playing;
            var info = Find<FrameworkElement>("SpInfo");
            var controls = Find<FrameworkElement>("SpControls");
            bool animate = slim != lastSlim && win.IsLoaded && win.IsVisible && !settings.LessMotion && info.Visibility == Visibility.Visible;
            var infoFrom = animate ? info.TranslatePoint(new Point(), spotifyRow) : new Point();
            var controlsFrom = animate ? controls.TranslatePoint(new Point(), spotifyRow) : new Point();
            lastSlim = slim;

            bool small = slim && width < SlimFullWidth;
            Grid.SetRow(controls, slim ? 0 : 1);
            controls.Margin = slim ? new Thickness(0) : new Thickness(0, 8, 0, 2);
            if (slim) Find<FrameworkElement>("SpSeekRow").Visibility = Visibility.Collapsed;
            var extras = small ? Visibility.Collapsed : Visibility.Visible;
            Find<FrameworkElement>("SpLike").Visibility = Find<FrameworkElement>("SpShuffle").Visibility = Find<FrameworkElement>("SpRepeat").Visibility = extras;
            double room = (width - SideMargins - (small ? ControlsSmall : ControlsFull)) / 2 - CoverSpace;
            Find<FrameworkElement>("SpText").MaxWidth = slim ? Math.Max(40, room) : double.PositiveInfinity;
            ((FrameworkElement)Find<FrameworkElement>("SpText")).HorizontalAlignment = slim ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
            spotifyRow.MinHeight = slim && !compact && !side ? PlayerHeight : 0;

            if (!animate) return;
            spotifyRow.UpdateLayout();
            Glide(info, infoFrom);
            Glide(controls, controlsFrom);
        }

        // Öğe eski konumundan yeni yerine yumuşakça kayar.
        void Glide(FrameworkElement element, Point from)
        {
            element.RenderTransform = null;
            var now = element.TranslatePoint(new Point(), spotifyRow);
            var move = new TranslateTransform(from.X - now.X, from.Y - now.Y);
            element.RenderTransform = move;
            var time = new Duration(TimeSpan.FromMilliseconds(360));
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
            move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, time) { EasingFunction = ease });
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, time) { EasingFunction = ease });
        }


        void RestoreVolume()
        {
            if (!fading) return;
            fading = false;
            spotify.SetVolume(fadeFrom);
            settings.PendingVolume = -1;
            settings.Save();
        }

        // Son 10 saniyede sesi üç adımda kısar; Spotify'a az istek gitsin diye adım sayısı küçüktür.
        void RunFade(long left)
        {
            if (settings.EndWithSong && !songExtended) return;
            if (!fading && settings.QuietFinish && left <= QuietSeconds * 1000 && spotify.Playing && spotify.Volume.HasValue)
            {
                fading = true;
                fadeFrom = spotify.Volume.Value;
                fadeStep = 0;
                settings.PendingVolume = fadeFrom;
                settings.Save();
            }
            if (!fading) return;
            double[] levels = { 0.7, 0.45, 0.25 };
            long[] marks = { 9000, 6000, 3000 };
            if (fadeStep < levels.Length && left <= marks[fadeStep])
                spotify.SetVolume((int)(fadeFrom * levels[fadeStep++]));
        }

        bool pausedByMusic;

        void LinkedMusic(bool on) { if (settings.LinkTimer) spotify.SetMusic(on); }

        void OnMusicChanged(bool playing)
        {
            RefreshBeat();
            Apply();
            if (!settings.LinkMusic) return;
            if (!playing && mode == Mode.Running)
            {
                Pause();
                pausedByMusic = true;
                Apply();
            }
            else if (playing && mode == Mode.Paused) Resume();
        }


        static void FitSpotify(Window w, double room, bool allowVolume)
        {
            bool seek = room >= 60, info = room >= 114;
            ((FrameworkElement)w.FindName("SpSeekRow")).Visibility = seek ? Visibility.Visible : Visibility.Collapsed;
            ((FrameworkElement)w.FindName("SpInfo")).Visibility = info ? Visibility.Visible : Visibility.Collapsed;
            ((FrameworkElement)w.FindName("SpVolumeBox")).Visibility = allowVolume ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
