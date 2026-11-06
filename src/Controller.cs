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
    enum Mode { Setup, Running, Paused, Ringing }

    partial class Controller
    {
        const int AlarmLimitSeconds = 120;
        readonly Window win;
        readonly Ring ring = new Ring();
        readonly DigitRow hText, mText, sText;
        readonly TextBlock status;
        readonly Border hField, mField, sField;
        readonly RepeatButton[] arrows;
        readonly Panel presets;
        readonly Button mainBtn, resetBtn;
        readonly ColumnDefinition gapCol, secCol;
        readonly DispatcherTimer timer;
        readonly Stopwatch clock = new Stopwatch(), ringClock = new Stopwatch();
        SoundPlayer player;
        readonly string savePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sayac", "son-sure.txt");

        Mode mode = Mode.Setup;
        int h, m, s;
        long totalMs, remainingAtResume;
        bool typedFresh, pinned, miniPlaced;
        readonly SpotifyPanel spotify;
        static readonly double[] FadeLevels = { 1, 0.7, 0.4 };
        readonly string fadePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sayac", "saydamlik.txt");
        int fade;
        System.Windows.Forms.NotifyIcon tray;
        System.Windows.Forms.ToolStripMenuItem trayClick;
        bool inTray, exiting, trayHintShown;
        int introStep;
        const int QuietSeconds = 10, SnoozeSeconds = 300;
        readonly Settings settings;
        readonly FocusStats stats;
        readonly Hotkeys hotkeys;
        readonly FocusPanel panel;
        readonly Panel routineBar;
        readonly FrameworkElement statusBox, addOne, addFive, snoozeBtn;
        readonly Stopwatch focusClock = new Stopwatch();
        readonly DropShadowEffect glow = new DropShadowEffect { ShadowDepth = 0, BlurRadius = 16, Opacity = 0.6, Color = Ring.DefaultColor };
        bool fading, glowing;
        int fadeFrom, fadeStep;
        double sinceSave;
        const double MiniRatio = 580.0 / 128.0;
        readonly string miniPlacePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sayac", "mini-konum.txt");
        readonly FrameworkElement spotifyRow;
        readonly Window mini;
        readonly DigitRow miniTime;
        readonly ScaleTransform miniScale;
        readonly System.Windows.Shapes.Path miniIcon;
        readonly FrameworkElement dial, ringHost, statusRow, bottomRow, root;
        readonly FrameworkElement[] units, colons;
        static readonly Geometry PlayIcon = Geometry.Parse("M0,0 L10,6 L0,12 Z");
        static readonly Geometry PauseIcon = Geometry.Parse("M0,0 H3.5 V12 H0 Z M6.5,0 H10 V12 H6.5 Z");
        static readonly Geometry StopIcon = Geometry.Parse("M0,0 H11 V11 H0 Z");

        readonly System.Collections.Generic.Dictionary<string, object> found = new System.Collections.Generic.Dictionary<string, object>();

        // Öğeler bir kez aranır, sonraki güncellemeler hazır kaydı kullanır.
        T Find<T>(string name) where T : class
        {
            object item;
            if (!found.TryGetValue(name, out item)) found[name] = item = win.FindName(name);
            return (T)item;
        }

        public Controller(Window w)
        {
            win = w;
            Find<Border>("RingHost").Child = ring;
            hText = DigitRow.Swap(Find<TextBlock>("HText")); mText = DigitRow.Swap(Find<TextBlock>("MText")); sText = DigitRow.Swap(Find<TextBlock>("SText"));
            hField = Find<Border>("HField"); mField = Find<Border>("MField"); sField = Find<Border>("SField");
            status = Find<TextBlock>("Status");
            presets = Find<Panel>("Presets");
            mainBtn = Find<Button>("MainBtn"); resetBtn = Find<Button>("ResetBtn");
            gapCol = Find<ColumnDefinition>("GapCol"); secCol = Find<ColumnDefinition>("SecCol");
            arrows = new[] { Find<RepeatButton>("HUp"), Find<RepeatButton>("HDown"), Find<RepeatButton>("MUp"), Find<RepeatButton>("MDown"), Find<RepeatButton>("SUp"), Find<RepeatButton>("SDown") };

            arrows[0].Click += (a, b) => Step(hField, 1); arrows[1].Click += (a, b) => Step(hField, -1);
            arrows[2].Click += (a, b) => Step(mField, 1); arrows[3].Click += (a, b) => Step(mField, -1);
            arrows[4].Click += (a, b) => Step(sField, 1); arrows[5].Click += (a, b) => Step(sField, -1);

            foreach (var f in new[] { hField, mField, sField })
            {
                var field = f;
                field.MouseLeftButtonDown += (a, b) => { if (mode == Mode.Setup) field.Focus(); };
                field.MouseWheel += (a, b) => { field.Focus(); Step(field, b.Delta > 0 ? 1 : -1); };
                field.GotKeyboardFocus += (a, b) => typedFresh = true;
            }
            Find<Button>("MinBtn").Click += (a, b) => win.WindowState = WindowState.Minimized;
            Find<Button>("CloseBtn").Click += (a, b) => win.Close();
            mainBtn.Click += (a, b) => MainAction();
            resetBtn.Click += (a, b) => ResetWithUndo();
            win.PreviewKeyDown += OnKey;
            win.Closed += (a, b) => player.Stop();
            win.SourceInitialized += (a, b) => RoundCorners();

            dial = Find<FrameworkElement>("Dial"); ringHost = Find<FrameworkElement>("RingHost");
            statusRow = Find<FrameworkElement>("StatusRow"); bottomRow = Find<FrameworkElement>("BottomRow");
            root = Find<FrameworkElement>("Root");
            units = new[] { Find<FrameworkElement>("U1"), Find<FrameworkElement>("U2"), Find<FrameworkElement>("U3") };
            colons = new[] { Find<FrameworkElement>("C1"), Find<FrameworkElement>("C2") };

            mini = Program.Load<Window>("mini.xaml");
            miniTime = DigitRow.Swap((TextBlock)mini.FindName("MiniTime"));
            miniScale = (ScaleTransform)mini.FindName("MiniScale");
            miniIcon = (System.Windows.Shapes.Path)mini.FindName("MiniIcon");
            ((Button)mini.FindName("MiniMain")).Click += (a, b) => { if (CanRun) MainAction(); };
            ((Button)mini.FindName("MiniExpand")).Click += (a, b) => Expand();
            mini.MouseLeftButtonDown += (a, b) =>
            {
                if (b.ClickCount == 2) Expand();
                else mini.DragMove();
            };

            Find<Button>("PinBtn").Click += (a, b) => Pin();
            win.SizeChanged += (a, b) => Apply();
            win.StateChanged += (a, b) =>
            {
                if (win.WindowState != WindowState.Minimized) pinned = false;
                root.Margin = new Thickness(win.WindowState == WindowState.Maximized ? 8 : 0);
                RefreshBeat();
                Apply();
            };
            win.Closed += (a, b) =>
            {
                if (mini.IsVisible) SaveMiniPlace();
                mini.Close();
            };

            timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
            timer.Tick += (a, b) => Tick();

            spotifyRow = Find<FrameworkElement>("SpotifyRow");
            spotify = new SpotifyPanel(win, mini);
            settings = Store.Load<Settings>("ayarlar.json");
            foreach (var pair in Hotkeys.Defaults())
                if (!settings.Keys.ContainsKey(pair.Key)) settings.Keys[pair.Key] = pair.Value;
            SpotifyApi.ConfiguredId = () => settings.SpotifyClientId;
            Hotkeys.Migrate(settings);
            FitWindow();
            stats = Store.Load<FocusStats>("odak.json");
            hotkeys = new Hotkeys(settings, RunHotkey);
            routineBar = Find<Panel>("RoutineBar");
            statusBox = Find<FrameworkElement>("StatusBox");
            addOne = Find<FrameworkElement>("AddOne"); addFive = Find<FrameworkElement>("AddFive"); snoozeBtn = Find<FrameworkElement>("SnoozeBtn");
            Find<Button>("AddOne").Click += (a, b) => AddSeconds(60);
            Find<Button>("AddFive").Click += (a, b) => AddSeconds(300);
            Find<Button>("SnoozeBtn").Click += (a, b) => Snooze();
            Find<Button>("AskYes").Click += (a, b) => AnswerAsk(true);
            Find<Button>("AskNo").Click += (a, b) => AnswerAsk(false);
            ((Button)mini.FindName("MiniYes")).Click += (a, b) => AnswerAsk(true);
            ((Button)mini.FindName("MiniNo")).Click += (a, b) => AnswerAsk(false);
            panel = new FocusPanel(win, settings, stats, hotkeys, spotify, this);
            spotify.AccentChanged = () => { RefreshAccent(); RefreshBeat(); Apply(); };
            if (settings.BridgeOn) RefreshBridge();
            spotify.PlayingChanged = OnMusicChanged;
            RebuildChime();
            BuildTray();
            RefreshAccent();
            var hourly = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            hourly.Tick += (a, b) =>
            {
                RefreshAccent();
                RefreshGoal();
                RefreshBeat();
                TryInstall();
                if (++updateMinutes >= UpdateEveryMinutes && settings.AutoUpdate)
                {
                    updateMinutes = 0;
                    var ignored = CheckForUpdate(false);
                }
            };
            hourly.Start();
            mini.SourceInitialized += (a, b) => ApplyClickThrough();
            win.Closing += (a, b) =>
            {
                SaveWindow();
                if (!exiting)
                {
                    b.Cancel = true;
                    if (settings.CloseAsk) ShowCloseChoice();
                    else if (settings.TrayOnClose) GoTray();
                    else win.Dispatcher.BeginInvoke(new Action(Exit));
                    return;
                }
                tray.Visible = false;
                tray.Dispose();
            };
            win.SourceInitialized += (a, b) => hotkeys.Attach(new WindowInteropHelper(win).Handle);
            win.Closed += (a, b) => { stats.Save(); hotkeys.Clear(); audioBeat.Stop(); if (bridge != null) bridge.Stop(); };
            win.IsVisibleChanged += (a, b) => RefreshBeat();
            win.Closing += (a, b) => { if (exiting && fading) { fading = false; spotify.SetVolumeNow(fadeFrom); settings.PendingVolume = -1; settings.Save(); } };
            Find<Button>("CloseTray").Click += (a, b) => FinishClose(true);
            Find<Button>("CloseExit").Click += (a, b) => FinishClose(false);
            Find<Button>("CloseCancel").Click += (a, b) => closeOverlay.Visibility = Visibility.Collapsed;
            closeOverlay = Find<FrameworkElement>("CloseOverlay");
            // Windows kapanırken uygulama soru sorup kapanmayı engellemez.
            Application.Current.SessionEnding += (a, b) => exiting = true;
            notice = Find<TextBlock>("Notice");
            intentBox = Find<TextBox>("IntentBox");
            noteBox = Find<TextBox>("NoteBox");
            intentBox.TextChanged += (a, b) => Find<TextBlock>("IntentHint").Visibility = intentBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            noteBox.TextChanged += (a, b) => Find<TextBlock>("NoteHint").Visibility = noteBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            spotify.TrackChanged = OnTrackChanged;
            Find<Button>("UpdateNow").Click += (a, b) => { Find<FrameworkElement>("UpdateOverlay").Visibility = Visibility.Collapsed; InstallNow(); };
            Find<Button>("UpdateLater").Click += (a, b) => { Find<FrameworkElement>("UpdateOverlay").Visibility = Visibility.Collapsed; updateLater = true; };
            ((Button)mini.FindName("MiniFinish")).Click += (a, b) => { pinned = true; Reset(); SetMiniPanel("quick"); };
            ((Button)mini.FindName("MiniSnooze")).Click += (a, b) => Snooze();
            Find<Button>("ModeTimer").Click += (a, b) => SetSetupMode(0);
            Find<Button>("ModeWatch").Click += (a, b) => SetSetupMode(1);
            Find<Button>("ModeSongs").Click += (a, b) => SetSetupMode(2);
            Find<Button>("SongLess").Click += (a, b) => ChangeSongs(-1);
            Find<Button>("SongMore").Click += (a, b) => ChangeSongs(1);
            Find<Button>("UndoBtn").Click += (a, b) => UndoReset();
            Find<Button>("TopPlay").Click += (a, b) => { if (CanRun) MainAction(); };
            Find<Button>("IntentAdd").Click += (a, b) => { intentOpen = true; Apply(); intentBox.Focus(); };
            win.PreviewMouseUp += (a, b) =>
            {
                // Fareyle tıklanan düğme odakta kalmaz, Boşluk tuşu sayacı yönetmeye devam eder.
                win.Dispatcher.BeginInvoke(new Action(() => { if (Keyboard.FocusedElement is ButtonBase) win.Focus(); }), DispatcherPriority.Input);
            };
            setupMode = Logic.Clamp(settings.LastMode, 0, 2);
            Find<Button>("NoticeClose").Click += (a, b) =>
            {
                missed = null;
                settings.LastNote = null;
                settings.Save();
                ShowNotice();
            };
            win.Closed += (a, b) => { if (wipeData) WipeFolder(); };
            SetSetupModeSoon();
            RefreshRoutines();
            RefreshPresets();
            RefreshGoal();
            ShowNotice();
            FitSpotify(mini, 999, true);
            mini.SourceInitialized += (a, b) => HwndSource.FromHwnd(new WindowInteropHelper(mini).Handle).AddHook(KeepMiniRatio);
            LoadFade();
            ((Button)mini.FindName("MiniFade")).Click += (a, b) => NextFade();
            // Fare üzerindeyken kontroller rahat görülsün diye mini sayaç tam görünür olur.
            mini.MouseEnter += (a, b) => mini.Opacity = 1;
            mini.MouseLeave += (a, b) => mini.Opacity = FadeLevels[fade];

            SetSeconds(LoadSeconds());
            Apply();
        }

    }
}
