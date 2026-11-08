using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using IoPath = System.IO.Path;

namespace Sayac
{
    // Veriler Kor.exe'den ayrı klasörde durur; güncelleme bu klasöre dokunmaz.
    static class Store
    {
        public static readonly string Folder = Environment.GetEnvironmentVariable("SAYAC_VERI")
            ?? IoPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sayac");
        static readonly string[] DataFiles = { "ayarlar.json", "odak.json", "saydamlik.txt", "son-sure.txt", "mini-konum.txt", "notlar.json" };
        const int KeepBackups = 5;
        public static bool Frozen;

        static T Read<T>(string path) where T : class
        {
            try { return new JavaScriptSerializer().Deserialize<T>(File.ReadAllText(path)); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            return null;
        }

        // Ana dosya bozulduysa bir önceki kayıt kullanılır.
        public static T Load<T>(string name) where T : class, new()
        {
            string path = IoPath.Combine(Folder, name);
            return Read<T>(path) ?? Read<T>(path + ".bak") ?? new T();
        }

        // Önce geçici dosyaya yazılır; yarım kalan yazma eski kaydı bozamaz.
        public static void Save(string name, object value)
        {
            if (Frozen) return;
            try
            {
                Directory.CreateDirectory(Folder);
                string path = IoPath.Combine(Folder, name), temp = path + ".tmp";
                File.WriteAllText(temp, new JavaScriptSerializer().Serialize(value));
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
                else File.Move(temp, path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public static void Backup()
        {
            try
            {
                string root = IoPath.Combine(Folder, "yedekler");
                string target = IoPath.Combine(root, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(target);
                foreach (var name in DataFiles)
                {
                    string file = IoPath.Combine(Folder, name);
                    if (File.Exists(file)) File.Copy(file, IoPath.Combine(target, name), true);
                }
                foreach (var old in Directory.GetDirectories(root).OrderByDescending(x => x).Skip(KeepBackups))
                    Directory.Delete(old, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        // Spotify girişi bu bilgisayara bağlı şifrelidir, dışa aktarılmaz.
        public static void Export(string path)
        {
            var bundle = new Dictionary<string, string>();
            foreach (var name in DataFiles)
            {
                string file = IoPath.Combine(Folder, name);
                if (File.Exists(file)) bundle[name] = File.ReadAllText(file);
            }
            File.WriteAllText(path, new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(bundle));
        }

        public static void Import(string path)
        {
            var bundle = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
            if (bundle == null || !bundle.Keys.Any(k => DataFiles.Contains(k)))
                throw new InvalidOperationException(L.T("Bu dosya Kor yedeği değil."));
            Backup();
            foreach (var pair in bundle)
                if (DataFiles.Contains(pair.Key)) File.WriteAllText(IoPath.Combine(Folder, pair.Key), pair.Value);
            Frozen = true;
        }
    }

    public class Routine
    {
        public string Name { get; set; }
        public int Seconds { get; set; }
        public string Uri { get; set; }
    }

    public class Settings
    {
        public bool QuietFinish { get; set; }
        public bool CoverColor { get; set; }
        public bool LastMinute { get; set; }
        public List<Routine> Routines { get; set; }
        public Dictionary<string, string> Keys { get; set; }
        public bool Sunset { get; set; }
        public bool ClickThrough { get; set; }
        public bool TrayOnClose { get; set; }
        public int Alarm { get; set; }
        public int AlarmVolume { get; set; }
        public bool AutoUpdate { get; set; }
        public bool CloseAsk { get; set; }
        public bool GuideHidden { get; set; }
        public bool AskMore { get; set; }
        public bool LinkMusic { get; set; }
        public bool LinkTimer { get; set; }
        public int KeysVersion { get; set; }
        public int PendingVolume { get; set; }
        public bool EndWithSong { get; set; }
        public int BeatMode { get; set; }
        public bool LessMotion { get; set; }
        public string SpotifyClientId { get; set; }
        public string Language { get; set; }
        public int MusicSource { get; set; }
        public bool BridgeOn { get; set; }
        public List<string> TrustedExtensions { get; set; }
        public int LastMode { get; set; }
        public double WinLeft { get; set; }
        public double WinTop { get; set; }
        public double WinWidth { get; set; }
        public double WinHeight { get; set; }
        public bool AskIntent { get; set; }
        public bool AskNote { get; set; }
        public int DailyGoal { get; set; }
        public int SongCount { get; set; }
        public string LastNote { get; set; }
        public List<int> Presets { get; set; }
        public List<int> Quick { get; set; }

        public Settings()
        {
            QuietFinish = CoverColor = LastMinute = Sunset = TrayOnClose = AutoUpdate = CloseAsk = AskMore = true;
            AlarmVolume = 60;
            PendingVolume = -1;
            TrustedExtensions = new List<string>();
            AskIntent = AskNote = true;
            BeatMode = 1;
            DailyGoal = 180;
            SongCount = 3;
            Presets = new List<int> { 1, 5, 10, 30 };
            Quick = new List<int> { 10, 20, 30 };
            Routines = new List<Routine>();
            Keys = Hotkeys.Defaults();
        }

        public void Save() { Store.Save("ayarlar.json", this); }
    }

    static class Startup
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        // Dosya taşınır veya adı değişirse başlangıç kaydı çalışan dosyayı göstermeye devam eder.
        public static void Refresh()
        {
            if (Environment.GetEnvironmentVariable("SAYAC_VERI") == null && Enabled) Enabled = true;
        }

        public static bool Enabled
        {
            get
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey))
                    return key != null && key.GetValue("Sayac") != null;
            }
            set
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) key.SetValue("Sayac", "\"" + Assembly.GetExecutingAssembly().Location + "\" --tepsi");
                    else key.DeleteValue("Sayac", false);
                }
            }
        }
    }

    public class TrackRef
    {
        public string Uri { get; set; }
        public string Cover { get; set; }
    }

    public class RunState
    {
        public bool Active { get; set; }
        public bool Paused { get; set; }
        public bool Up { get; set; }
        public long BaseMs { get; set; }
        public long TotalMs { get; set; }
        public long SavedTicks { get; set; }
        public int SongsLeft { get; set; }
        public string Intent { get; set; }
    }

    public class FocusDay
    {
        public double Seconds { get; set; }
        public Dictionary<string, double> Tracks { get; set; }
        public Dictionary<string, double> Topics { get; set; }
        public FocusDay() { Tracks = new Dictionary<string, double>(); Topics = new Dictionary<string, double>(); }
    }

    // Odak verileri yalnızca bu bilgisayarda, yerel dosyada tutulur.
    public class FocusStats
    {
        public Dictionary<string, FocusDay> Days { get; set; }
        public Dictionary<string, TrackRef> Refs { get; set; }
        public FocusStats() { Days = new Dictionary<string, FocusDay>(); Refs = new Dictionary<string, TrackRef>(); }

        public List<KeyValuePair<string, double>> Top(IEnumerable<FocusDay> days)
        {
            var sum = new Dictionary<string, double>();
            foreach (var d in days)
                foreach (var x in d.Tracks)
                {
                    double old;
                    sum.TryGetValue(x.Key, out old);
                    sum[x.Key] = old + x.Value;
                }
            return sum.OrderByDescending(x => x.Value).ToList();
        }

        public IEnumerable<FocusDay> Month()
        {
            string prefix = DateTime.Now.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            return Days.Where(x => x.Key.StartsWith(prefix)).Select(x => x.Value);
        }

        public IEnumerable<FocusDay> Week()
        {
            return Enumerable.Range(0, 7).Select(i => Day(DateTime.Now.AddDays(-i)));
        }

        public static string Key(DateTime d) { return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }

        public FocusDay Day(DateTime d)
        {
            FocusDay day;
            return Days.TryGetValue(Key(d), out day) ? day : new FocusDay();
        }

        public void Add(double seconds, string track, string uri, string cover, string topic)
        {
            if (!string.IsNullOrEmpty(topic))
            {
                string today = Key(DateTime.Now);
                if (!Days.ContainsKey(today)) Days[today] = new FocusDay();
                if (Days[today].Topics == null) Days[today].Topics = new Dictionary<string, double>();
                double spent;
                Days[today].Topics.TryGetValue(topic, out spent);
                Days[today].Topics[topic] = spent + seconds;
            }
            if (!string.IsNullOrEmpty(track) && uri != null) Refs[track] = new TrackRef { Uri = uri, Cover = cover };
            string key = Key(DateTime.Now);
            if (!Days.ContainsKey(key)) Days[key] = new FocusDay();
            var day = Days[key];
            day.Seconds += seconds;
            if (string.IsNullOrEmpty(track)) return;
            double old;
            day.Tracks.TryGetValue(track, out old);
            day.Tracks[track] = old + seconds;
        }

        public void Save() { Store.Save("odak.json", this); }
    }

    // Uygulama arka plandayken de çalışan kısayollar.
    public class Hotkeys
    {
        const int WmHotkey = 0x312;
        const uint NoRepeat = 0x4000;
        public const uint Alt = 1, Ctrl = 2, Shift = 4, Win = 8;
        public static readonly string[][] Actions =
        {
            new[] { "toggle", L.T("Sayacı başlat veya duraklat") },
            new[] { "reset", L.T("Sayacı sıfırla") },
            new[] { "add1", L.T("1 dakika ekle") },
            new[] { "snooze", L.T("Alarmı 5 dakika ertele") },
            new[] { "pin", L.T("Mini sayaç ve büyük pencere arasında geç") },
            new[] { "click", L.T("Mini sayaçta tıklamaları arkaya geçir veya geri al") },
            new[] { "music", L.T("Müziği oynat veya duraklat") },
            new[] { "next", L.T("Sonraki şarkı") },
            new[] { "prev", L.T("Önceki şarkı") },
            new[] { "notes", L.T("Notları aç veya kapat") },
        };

        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);

        IntPtr hwnd;
        readonly Settings settings;
        readonly Action<string> run;
        public readonly HashSet<string> Failed = new HashSet<string>();

        public Hotkeys(Settings s, Action<string> action) { settings = s; run = action; }

        static string Pack(uint mods, Key key) { return mods + "," + KeyInterop.VirtualKeyFromKey(key); }
        public static string Pack(uint mods, int vk) { return mods + "," + vk; }

        public static Dictionary<string, string> Defaults()
        {
            // Ctrl+Alt, Türkçe klavyede AltGr ile aynıdır (€, ₺ gibi karakterleri yazar).
            uint m = Ctrl | Alt | Shift;
            return new Dictionary<string, string> {
                { "toggle", Pack(m, Key.S) }, { "reset", Pack(m, Key.R) }, { "add1", Pack(m, Key.Up) },
                { "snooze", Pack(m, Key.E) }, { "pin", Pack(m, Key.M) }, { "click", Pack(m, Key.T) }, { "music", Pack(m, Key.P) },
                { "next", Pack(m, Key.Right) }, { "prev", Pack(m, Key.Left) }, { "notes", Pack(m, Key.N) } };
        }

        // Eski Ctrl+Alt varsayılanları yeni varsayılanlara taşınır, kullanıcının seçtikleri korunur.
        public static void Migrate(Settings s)
        {
            var fresh = Defaults();
            // Sonradan eklenen kısayollar eski ayarlara da eklenir.
            bool added = false;
            foreach (var pair in fresh) if (!s.Keys.ContainsKey(pair.Key)) { s.Keys[pair.Key] = pair.Value; added = true; }
            if (added) s.Save();
            if (s.KeysVersion >= 2) return;
            foreach (var pair in fresh)
            {
                uint mods; int vk; string old;
                if (Parse(pair.Value, out mods, out vk) && s.Keys.TryGetValue(pair.Key, out old)
                    && (old == Pack(Ctrl | Alt, vk) || (pair.Key == "toggle" && old == Pack(Ctrl | Alt, KeyInterop.VirtualKeyFromKey(Key.Space)))))
                    s.Keys[pair.Key] = pair.Value;
            }
            s.KeysVersion = 2;
            s.Save();
        }

        public static string Describe(string packed)
        {
            uint mods; int vk;
            if (!Parse(packed, out mods, out vk)) return L.T("Kapalı");
            var parts = new List<string>();
            if ((mods & Ctrl) != 0) parts.Add("Ctrl");
            if ((mods & Alt) != 0) parts.Add("Alt");
            if ((mods & Shift) != 0) parts.Add("Shift");
            if ((mods & Win) != 0) parts.Add("Win");
            var key = KeyInterop.KeyFromVirtualKey(vk);
            var names = new Dictionary<Key, string> { { Key.Space, L.T("Boşluk") }, { Key.Left, L.T("Sol ok") }, { Key.Right, L.T("Sağ ok") }, { Key.Up, L.T("Yukarı ok") }, { Key.Down, L.T("Aşağı ok") } };
            string name;
            parts.Add(names.TryGetValue(key, out name) ? name : key >= Key.D0 && key <= Key.D9 ? (key - Key.D0).ToString() : key.ToString());
            return string.Join(" + ", parts);
        }

        static bool Parse(string packed, out uint mods, out int vk)
        {
            mods = 0; vk = 0;
            if (string.IsNullOrEmpty(packed)) return false;
            var p = packed.Split(',');
            return p.Length == 2 && uint.TryParse(p[0], out mods) && int.TryParse(p[1], out vk);
        }

        public void Attach(IntPtr handle)
        {
            hwnd = handle;
            HwndSource.FromHwnd(hwnd).AddHook(Hook);
            Apply();
        }

        IntPtr Hook(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg != WmHotkey) return IntPtr.Zero;
            int id = w.ToInt32();
            if (id >= 0 && id < Actions.Length) { run(Actions[id][0]); handled = true; }
            return IntPtr.Zero;
        }

        public void Clear()
        {
            if (hwnd == IntPtr.Zero) return;
            for (int i = 0; i < Actions.Length; i++) UnregisterHotKey(hwnd, i);
        }

        public void Apply()
        {
            Clear();
            Failed.Clear();
            if (hwnd == IntPtr.Zero) return;
            for (int i = 0; i < Actions.Length; i++)
            {
                string packed; uint mods; int vk;
                if (settings.Keys.TryGetValue(Actions[i][0], out packed) && Parse(packed, out mods, out vk)
                    && !RegisterHotKey(hwnd, i, mods | NoRepeat, (uint)vk))
                    Failed.Add(Actions[i][0]);
            }
        }
    }

    // Rutinler, odak özeti ve ayarlar sayfaları.
    class FocusPanel
    {
        static readonly Brush Text = Paint(0xF4, 0xF1, 0xEA), Soft = Paint(0x8E, 0x8A, 0x82), Gold = Paint(0xD8, 0xC4, 0x9A), Line = Paint(0x2B, 0x2A, 0x27), Warn = Paint(0xE8, 0x9A, 0x7C);
        static Brush Paint(byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromRgb(r, g, b)); x.Freeze(); return x; }

        readonly Window win;
        readonly Settings settings;
        readonly FocusStats stats;
        readonly Hotkeys hotkeys;
        readonly SpotifyPanel spotify;
        readonly Controller controller;
        readonly FrameworkElement overlay;
        readonly StackPanel body;
        readonly Button[] tabs;
        int page;
        string capturing, keyMessage;
        Routine editing;

        public bool Open { get { return overlay.Visibility == Visibility.Visible; } }

        public FocusPanel(Window w, Settings s, FocusStats st, Hotkeys h, SpotifyPanel sp, Controller c)
        {
            win = w; settings = s; stats = st; hotkeys = h; spotify = sp; controller = c;
            overlay = (FrameworkElement)win.FindName("PanelOverlay");
            body = (StackPanel)win.FindName("PanelBody");
            tabs = new[] { (Button)win.FindName("TabRoutines"), (Button)win.FindName("TabStats"), (Button)win.FindName("TabSettings") };
            for (int i = 0; i < tabs.Length; i++)
            {
                int index = i;
                tabs[i].Click += (a, b) => Show(index);
            }
            ((Button)win.FindName("PanelBtn")).Click += (a, b) => { if (Open) Close(); else Show(page); };
            ((Button)win.FindName("PanelBack")).Click += (a, b) => Close();
        }

        public void Show(int index)
        {
            page = index;
            capturing = null;
            overlay.Visibility = Visibility.Visible;
            for (int i = 0; i < tabs.Length; i++) tabs[i].Foreground = i == page ? Gold : Soft;
            controller.Refresh();
            Render();
        }

        public void Close()
        {
            StopCapture();
            overlay.Visibility = Visibility.Collapsed;
            controller.RefreshRoutines();
            controller.Refresh();
        }

        void Render()
        {
            body.Children.Clear();
            if (page == 0) RenderRoutines();
            else if (page == 1) RenderStats();
            else RenderSettings();
        }

        // ---- Ortak parçalar
        TextBlock Label(string text, double size, Brush color, double top)
        {
            return new TextBlock { Text = text, FontSize = size, Foreground = color, Margin = new Thickness(0, top, 0, 0), TextWrapping = TextWrapping.Wrap };
        }

        Button Chip(string text, Action click)
        {
            var b = new Button { Content = text, Style = (Style)win.FindResource("Preset"), Margin = new Thickness(0, 0, 8, 0) };
            b.Click += (x, y) => click();
            return b;
        }

        TextBox Input(string value, double width)
        {
            var t = new TextBox { Text = value, Style = (Style)win.FindResource("Input") };
            if (width > 0) { t.Width = width; t.HorizontalAlignment = HorizontalAlignment.Left; }
            return t;
        }

        // ---- Rutinler
        Routine deletedRoutine;
        int deletedIndex;

        void RenderRoutines()
        {
            if (deletedRoutine != null)
            {
                var undo = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
                undo.Children.Add(new TextBlock { Text = "\"" + deletedRoutine.Name + L.T("\" silindi"), FontSize = 14, Foreground = Soft, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
                undo.Children.Add(Chip(L.T("Geri al"), () =>
                {
                    settings.Routines.Insert(Math.Min(deletedIndex, settings.Routines.Count), deletedRoutine);
                    deletedRoutine = null;
                    settings.Save();
                    controller.RefreshRoutines();
                    Render();
                }));
                body.Children.Add(undo);
            }
            foreach (var r in settings.Routines.ToList())
            {
                var routine = r;
                var row = new Grid { Margin = new Thickness(0, 0, 0, 14) };
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var info = new StackPanel();
                info.Children.Add(new TextBlock { Text = r.Name, FontSize = 16, Foreground = Text, TextWrapping = TextWrapping.Wrap });
                info.Children.Add(Label(Duration(r.Seconds) + (string.IsNullOrEmpty(r.Uri) ? "" : L.T(", müzikli")), 13, Soft, 2));
                row.Children.Add(info);
                var actions = new StackPanel { Orientation = Orientation.Horizontal };
                actions.Children.Add(Chip(L.T("Başlat"), () => { Close(); controller.StartRoutine(routine); }));
                actions.Children.Add(Chip(L.T("Düzenle"), () => { editing = routine; Render(); }));
                actions.Children.Add(Chip(L.T("Sil"), () =>
                {
                    deletedRoutine = routine;
                    deletedIndex = settings.Routines.IndexOf(routine);
                    settings.Routines.Remove(routine);
                    if (editing == routine) editing = null;
                    settings.Save();
                    Render();
                }));
                Grid.SetColumn(actions, 1);
                row.Children.Add(actions);
                body.Children.Add(row);
            }
            if (settings.Routines.Count == 0)
                body.Children.Add(Label(L.T("Henüz rutinin yok. Aşağıdan ilkini oluştur."), 14, Soft, 0));

            body.Children.Add(Label(editing == null ? L.T("Rutin oluştur") : L.T("Rutini düzenle"), 17, Text, 14));
            body.Children.Add(Label(L.T("Ad"), 13, Soft, 10));
            var name = Input(editing == null ? "" : editing.Name, 0);
            body.Children.Add(name);

            int total = editing == null ? 1500 : editing.Seconds;
            body.Children.Add(Label(L.T("Süre"), 13, Soft, 10));
            var time = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            var hh = Input((total / 3600).ToString(), 64);
            var mm = Input((total / 60 % 60).ToString(), 64);
            var ss = Input((total % 60).ToString(), 64);
            time.Children.Add(TimePicker(hh, 100, L.T("saat")));
            time.Children.Add(TimePicker(mm, 60, L.T("dakika")));
            time.Children.Add(TimePicker(ss, 60, L.T("saniye")));
            body.Children.Add(time);

            body.Children.Add(Label(L.T("Spotify listesi (isteğe bağlı)"), 13, Soft, 10));
            var link = Input(editing == null ? "" : editing.Uri ?? "", 0);
            body.Children.Add(link);
            var error = Label("", 13, Warn, 6);
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            bar.Children.Add(Chip(L.T("Çalan listeyi kullan"), () =>
            {
                if (spotify.ContextUri != null) { link.Text = spotify.ContextUri; error.Text = ""; }
                else error.Text = L.T("Şu an bir listeden çalmıyor. Spotify'da liste başlat veya bağlantısını yapıştır.");
            }));
            body.Children.Add(bar);
            body.Children.Add(error);

            var save = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            save.Children.Add(Chip(L.T("Rutini kaydet"), () =>
            {
                int h, m, s;
                int.TryParse(hh.Text, out h); int.TryParse(mm.Text, out m); int.TryParse(ss.Text, out s);
                int seconds = h * 3600 + m * 60 + s;
                string uri = Normalize(link.Text);
                if (name.Text.Trim().Length == 0) { error.Text = L.T("Rutine bir ad ver."); return; }
                if (seconds <= 0 || h < 0 || m < 0 || s < 0 || h > 99) { error.Text = L.T("Süre 1 saniye ile 99 saat arasında olmalı."); return; }
                if (uri == null) { error.Text = L.T("Bağlantı tanınmadı. Spotify liste, albüm veya sanatçı bağlantısı yapıştır."); return; }
                var target = editing ?? new Routine();
                target.Name = name.Text.Trim(); target.Seconds = seconds; target.Uri = uri;
                if (editing == null) settings.Routines.Add(target);
                editing = null;
                settings.Save();
                controller.RefreshRoutines();
                Render();
            }));
            if (editing != null) save.Children.Add(Chip(L.T("Vazgeç"), () => { editing = null; Render(); }));
            body.Children.Add(save);
        }

        // Ana ekrandaki gibi oklu seçim; kutuya yazmak ve fare tekerleği de çalışır.
        FrameworkElement TimePicker(TextBox box, int size, string unit)
        {
            Action<int> step = delta =>
            {
                int value;
                int.TryParse(box.Text, out value);
                box.Text = Logic.Wrap(Logic.Clamp(value, 0, size - 1), delta, size).ToString();
            };
            box.Margin = new Thickness(0);
            box.TextAlignment = TextAlignment.Center;
            box.HorizontalAlignment = HorizontalAlignment.Center;
            box.MouseWheel += (a, b) => step(b.Delta > 0 ? 1 : -1);
            var panel = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
            panel.Children.Add(Arrow("M0,7 L7,0 L14,7", unit + L.T(" artır"), () => step(1)));
            panel.Children.Add(box);
            panel.Children.Add(Arrow("M0,0 L7,7 L14,0", unit + L.T(" azalt"), () => step(-1)));
            panel.Children.Add(new TextBlock { Text = unit, FontSize = 13, Foreground = Soft, HorizontalAlignment = HorizontalAlignment.Center });
            return panel;
        }

        Button Arrow(string path, string name, Action click)
        {
            var button = new Button { Style = (Style)win.FindResource("Media"), HorizontalAlignment = HorizontalAlignment.Center };
            button.Content = new System.Windows.Shapes.Path { Data = Geometry.Parse(path), Stroke = Soft, StrokeThickness = 1.5 };
            System.Windows.Automation.AutomationProperties.SetName(button, name);
            button.Click += (a, b) => click();
            return button;
        }

        public static string Duration(int seconds)
        {
            var parts = new List<string>();
            if (seconds >= 3600) parts.Add(seconds / 3600 + L.T(" sa"));
            if (seconds / 60 % 60 > 0) parts.Add(seconds / 60 % 60 + L.T(" dk"));
            if (seconds % 60 > 0) parts.Add(seconds % 60 + L.T(" sn"));
            return string.Join(" ", parts);
        }

        // Boş metin boş liste demektir; tanınmayan metin için null döner.
        public static string Normalize(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return "";
            var m = Regex.Match(text, @"^spotify:(playlist|album|artist):([A-Za-z0-9]+)$");
            if (!m.Success) m = Regex.Match(text, @"open\.spotify\.com/(?:intl-[a-z]+/)?(playlist|album|artist)/([A-Za-z0-9]+)");
            return m.Success ? "spotify:" + m.Groups[1].Value + ":" + m.Groups[2].Value : null;
        }

        // ---- Odak özeti
        void RenderStats()
        {
            var today = stats.Day(DateTime.Now);
            int minutes = (int)(today.Seconds / 60);
            body.Children.Add(Label(L.T("Bugün"), 14, Soft, 0));
            body.Children.Add(Label(minutes >= 60 ? minutes / 60 + L.T(" sa ") + minutes % 60 + L.T(" dk") : minutes + L.T(" dk"), 44, Text, 0));
            body.Children.Add(Label(L.T("Odaklanırken en çok dinlediğin"), 14, Soft, 16));
            var top = today.Tracks.OrderByDescending(x => x.Value).FirstOrDefault();
            body.Children.Add(Label(top.Key == null ? L.T("Bugün sayaç çalışırken müzik dinlemedin.") : top.Key, 16, top.Key == null ? Soft : Text, 2));
            if (top.Key != null) body.Children.Add(Label((int)Math.Ceiling(top.Value / 60) + L.T(" dk"), 13, Soft, 2));

            if (today.Topics != null && today.Topics.Count > 0)
            {
                body.Children.Add(Label(L.T("Bugünkü konular"), 14, Soft, 16));
                foreach (var x in today.Topics.OrderByDescending(v => v.Value).Take(5))
                    body.Children.Add(new TextBlock { Text = x.Key + "  (" + (int)Math.Ceiling(x.Value / 60) + L.T(" dk)"), FontSize = 14, Foreground = Text, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            }
            if (settings.DailyGoal > 0)
                body.Children.Add(Label(L.T("Günlük hedef ") + settings.DailyGoal + L.T(" dk"), 13, Soft, 10));
            var month = stats.Top(stats.Month()).Take(5).ToList();
            body.Children.Add(Label(L.T("Bu ay en iyi odaklandığın şarkılar"), 14, Soft, 18));
            if (month.Count == 0) body.Children.Add(Label(L.T("Sayaç çalışırken müzik dinleyince burada listelenir."), 14, Soft, 2));
            foreach (var x in month)
                body.Children.Add(new TextBlock { Text = x.Key + "  (" + (int)Math.Ceiling(x.Value / 60) + L.T(" dk)"), FontSize = 14, Foreground = Text, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });

            body.Children.Add(Label(L.T("Son 7 gün"), 14, Soft, 22));
            var days = Enumerable.Range(0, 7).Select(i => DateTime.Now.Date.AddDays(i - 6)).ToList();
            double most = Math.Max(60, days.Max(d => stats.Day(d).Seconds));
            var chart = new UniformGrid7();
            foreach (var d in days)
            {
                double value = stats.Day(d).Seconds;
                var cell = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
                cell.Children.Add(new TextBlock { Text = value >= 60 ? ((int)(value / 60)).ToString() : "", FontSize = 12, Foreground = Soft, HorizontalAlignment = HorizontalAlignment.Center });
                cell.Children.Add(new Rectangle { Height = Math.Max(2, 80 * value / most), Width = 22, Margin = new Thickness(0, 3, 0, 4), Fill = d == DateTime.Now.Date ? Gold : Paint(0x5A, 0x57, 0x50) });
                cell.Children.Add(new TextBlock { Text = d.ToString("ddd", L.Culture), FontSize = 12, Foreground = Soft, HorizontalAlignment = HorizontalAlignment.Center });
                chart.Add(cell);
            }
            chart.Root.Margin = new Thickness(0, 8, 0, 0);
            body.Children.Add(chart.Root);
            var result = Label("", 13, Gold, 8);
            var buttons = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
            var listBtn = Chip(L.T("Bu ayın listesini Spotify'a kaydet"), async () =>
            {
                result.Text = L.T("Kaydediliyor");
                var uris = stats.Top(stats.Month()).Take(30)
                    .Where(x => stats.Refs.ContainsKey(x.Key) && stats.Refs[x.Key].Uri != null && stats.Refs[x.Key].Uri.StartsWith("spotify:track:"))
                    .Select(x => stats.Refs[x.Key].Uri).ToList();
                result.Text = await spotify.SaveList(L.T("Odak listem, ") + DateTime.Now.ToString("MMMM yyyy", L.Culture), uris);
            });
            listBtn.Margin = new Thickness(0, 0, 8, 8);
            buttons.Children.Add(listBtn);
            var cardBtn = Chip(L.T("Haftalık odak kartını kaydet"), async () =>
            {
                var dialog = new Microsoft.Win32.SaveFileDialog { FileName = "odak-karti-" + FocusStats.Key(DateTime.Now) + ".png", Filter = L.T("PNG görseli|*.png") };
                if (dialog.ShowDialog(win) != true) return;
                await FocusCard.Save(stats, dialog.FileName);
                result.Text = L.T("Kart kaydedildi.");
                Process.Start("explorer.exe", "/select,\"" + dialog.FileName + "\"");
            });
            cardBtn.Margin = new Thickness(0, 0, 8, 8);
            buttons.Children.Add(cardBtn);
            body.Children.Add(buttons);
            body.Children.Add(result);
            body.Children.Add(Label(L.T("Bu veriler yalnızca bu bilgisayarda saklanır. Çubuklardaki sayılar dakikadır."), 12, Soft, 14));
        }

        class UniformGrid7
        {
            public readonly Grid Root = new Grid { Height = 124 };
            int count;
            public void Add(UIElement e)
            {
                Root.ColumnDefinitions.Add(new ColumnDefinition());
                Grid.SetColumn(e, count++);
                Root.Children.Add(e);
            }
        }

        // ---- Ayarlar
        void Toggle(string title, string detail, bool value, Action<bool> change)
        {
            var box = new CheckBox { IsChecked = value, Style = (Style)win.FindResource("Switch"), Margin = new Thickness(0, 0, 0, 12) };
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = title, FontSize = 15, Foreground = Text });
            text.Children.Add(new TextBlock { Text = detail, FontSize = 13, Foreground = Soft, TextWrapping = TextWrapping.Wrap });
            box.Content = text;
            box.Click += (a, b) => { change(box.IsChecked == true); settings.Save(); };
            body.Children.Add(box);
        }

        int settingsPage;

        // Dakika listesini okur; geçersizse null döner.
        static List<int> Minutes(IEnumerable<TextBox> boxes)
        {
            var list = new List<int>();
            foreach (var box in boxes)
            {
                int v;
                if (!int.TryParse(box.Text.Trim(), out v) || v < 1 || v > 5999) return null;
                list.Add(v);
            }
            return list;
        }

        void MinuteEditor(string title, List<int> values, Action<List<int>> save)
        {
            body.Children.Add(Label(title, 15, Text, 12));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            var boxes = values.Select(v => Input(v.ToString(), 58)).ToList();
            foreach (var b in boxes) { b.Margin = new Thickness(0, 4, 8, 0); row.Children.Add(b); }
            var message = Label("", 13, Gold, 4);
            var chip = Chip(L.T("Kaydet"), () =>
            {
                var list = Minutes(boxes);
                if (list == null) { message.Text = L.T("Her kutuya 1 ile 5999 arasında dakika yaz."); return; }
                save(list);
                settings.Save();
                controller.RefreshPresets();
                message.Text = "Kaydedildi.";
            });
            chip.VerticalAlignment = VerticalAlignment.Bottom;
            row.Children.Add(chip);
            body.Children.Add(row);
            body.Children.Add(message);
        }

        void RenderSettings()
        {
            string[] names = { L.T("Genel"), L.T("Süreler ve alarm"), L.T("Müzik"), L.T("Mini sayaç"), L.T("Kısayollar") };
            var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                var chip = Chip(names[i], () => { settingsPage = index; Render(); });
                chip.Margin = new Thickness(0, 0, 8, 8);
                if (i == settingsPage) chip.Foreground = Gold;
                bar.Children.Add(chip);
            }
            body.Children.Add(bar);
            if (settingsPage == 0) SettingsGeneral();
            else if (settingsPage == 1) SettingsTime();
            else if (settingsPage == 2) SettingsMusic();
            else if (settingsPage == 3) SettingsMini();
            else SettingsKeys();
        }

        void SettingsGeneral()
        {
            body.Children.Add(Label(L.T("Dil"), 17, Text, 0));
            var languageRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 14) };
            var languages = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("", L.T("Otomatik")) };
            languages.AddRange(L.Available());
            foreach (var language in languages)
            {
                string code = language.Key;
                var chip = Chip(language.Value, () =>
                {
                    settings.Language = code;
                    settings.Save();
                    if (L.Pick(code) != L.Code && controller.Idle) controller.Restart();
                    else Render();
                });
                if (code == (settings.Language ?? "")) chip.Foreground = Gold;
                languageRow.Children.Add(chip);
            }
            body.Children.Add(languageRow);
            if (L.Pick(settings.Language) != L.Code) body.Children.Add(Label(L.T("Dil, Kor yeniden açılınca değişecek."), 13, Soft, 0));
            Toggle(L.T("Başlarken ne yaptığını sor"), L.T("Ana ekranda \"Ne yapıyorsun?\" alanı gösterir, Özet'te konulara göre süre görünür."), settings.AskIntent, v => { settings.AskIntent = v; controller.RefreshPresets(); });
            Toggle(L.T("Bitişte not sor"), L.T("Süre bitince \"Nerede kaldım?\" kutusu çıkar, not sonraki açılışta gösterilir."), settings.AskNote, v => settings.AskNote = v);
            Toggle(L.T("Bitmeden 5 dakika sor"), L.T("Son 20 saniyede süreyi 5 dakika uzatmak isteyip istemediğini sorar."), settings.AskMore, v => settings.AskMore = v);
            Toggle(L.T("Son dakika modu"), L.T("Son 60 saniyede kadran ve rakamlar hafifçe parlar."), settings.LastMinute, v => settings.LastMinute = v);
            Toggle(L.T("Gün batımı kadranı"), L.T("Kapak rengi yokken kadran günün saatine göre hafifçe değişir."), settings.Sunset, v => { settings.Sunset = v; controller.RefreshAccent(); });
            Toggle(L.T("Kapatırken sor"), L.T("Kapat düğmesinde tepsiye indirme veya tamamen kapatma seçeneği gösterir."), settings.CloseAsk, v => { settings.CloseAsk = v; Render(); });
            if (!settings.CloseAsk) Toggle(L.T("Kapat düğmesi tepsiye indirsin"), L.T("Kapalıysa uygulama tamamen kapanır."), settings.TrayOnClose, v => settings.TrayOnClose = v);
            Toggle(L.T("Hareketleri azalt"), L.T("Ritim tepkisini, kayan yazıları ve parlamayı kapatır."), settings.LessMotion, v => { settings.LessMotion = v; controller.RefreshRoutines(); controller.RefreshBeat(); });
            Toggle(L.T("Windows ile başla"), L.T("Bilgisayar açılınca uygulama tepside hazır bekler."), Startup.Enabled, v => Startup.Enabled = v);
            Toggle(L.T("Açılışta rehberi göster"), L.T("Rehberi tamamlayınca kendiliğinden kapanır."), !settings.GuideHidden, v => settings.GuideHidden = !v);
            var wipeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            wipeRow.Children.Add(Chip(L.T("Verilerimi sil ve başlangıçtan kaldır"), () => controller.DeleteEverything()));
            var guideRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            guideRow.Children.Add(Chip(L.T("Rehberi şimdi aç"), () => { Close(); controller.ShowGuide(); }));
            body.Children.Add(guideRow);
            body.Children.Add(wipeRow);

            body.Children.Add(Label(L.T("Güncelleme"), 17, Text, 10));
            body.Children.Add(Label(L.T("Sürüm ") + AppVersion.Current, 13, Soft, 2));
            var updateBox = new CheckBox { IsChecked = settings.AutoUpdate, Style = (Style)win.FindResource("Switch"), Margin = new Thickness(0, 8, 0, 0) };
            var updateText = new StackPanel();
            updateText.Children.Add(new TextBlock { Text = L.T("Otomatik güncelleme"), FontSize = 15, Foreground = Text });
            updateText.Children.Add(new TextBlock { Text = L.T("Her açılışta kontrol eder, varsa haber verip kurar ve yeniden başlar. Sayaç çalışırken beklenir."), FontSize = 13, Foreground = Soft, TextWrapping = TextWrapping.Wrap });
            updateBox.Content = updateText;
            updateBox.Click += (a, b) => { settings.AutoUpdate = updateBox.IsChecked == true; settings.Save(); };
            body.Children.Add(updateBox);
            var updateResult = Label("", 13, Gold, 6);
            var updateRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            updateRow.Children.Add(Chip(L.T("Güncellemeleri denetle"), async () =>
            {
                updateResult.Text = L.T("Denetleniyor");
                updateResult.Text = await controller.CheckForUpdate(true);
            }));
            body.Children.Add(updateRow);
            body.Children.Add(updateResult);

            body.Children.Add(Label(L.T("Ayar yedeği"), 17, Text, 14));
            body.Children.Add(Label(L.T("Güncellemeler ayarlarını silmez, kurulumdan önce otomatik yedek de alınır. Başka bilgisayara taşımak için dosyaya aktarabilirsin. Spotify girişi aktarılmaz."), 13, Soft, 4));
            var dataResult = Label("", 13, Gold, 6);
            var dataRow = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            var exportBtn = Chip(L.T("Ayarları dışa aktar"), () =>
            {
                settings.Save(); stats.Save();
                var dialog = new Microsoft.Win32.SaveFileDialog { FileName = "sayac-yedek-" + FocusStats.Key(DateTime.Now) + ".json", Filter = L.T("Kor yedeği|*.json") };
                if (dialog.ShowDialog(win) != true) return;
                try { Store.Export(dialog.FileName); dataResult.Text = L.T("Ayarlar dışa aktarıldı."); }
                catch (IOException) { dataResult.Text = L.T("Dosya yazılamadı. Başka bir konum seç."); }
                catch (UnauthorizedAccessException) { dataResult.Text = L.T("Dosya yazılamadı. Başka bir konum seç."); }
            });
            exportBtn.Margin = new Thickness(0, 0, 8, 8);
            dataRow.Children.Add(exportBtn);
            var importBtn = Chip(L.T("Yedekten geri yükle"), () =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog { Filter = L.T("Kor yedeği|*.json") };
                if (dialog.ShowDialog(win) != true) return;
                try { Store.Import(dialog.FileName); controller.Restart(); }
                catch (InvalidOperationException e) { dataResult.Text = e.Message; }
                catch (ArgumentException) { dataResult.Text = L.T("Bu dosya Kor yedeği değil."); }
                catch (IOException) { dataResult.Text = L.T("Dosya okunamadı."); }
                catch (UnauthorizedAccessException) { dataResult.Text = L.T("Dosya okunamadı."); }
            });
            importBtn.Margin = new Thickness(0, 0, 8, 8);
            dataRow.Children.Add(importBtn);
            body.Children.Add(dataRow);
            body.Children.Add(dataResult);

        }

        void SettingsTime()
        {
            body.Children.Add(Label(L.T("Süreler"), 17, Text, 10));
            MinuteEditor(L.T("Hazır süre düğmeleri (dakika)"), settings.Presets, v => settings.Presets = v);
            body.Children.Add(Label(L.T("Günlük hedef (dakika, 0 kapatır)"), 15, Text, 12));
            var goal = Input(settings.DailyGoal.ToString(), 90);
            goal.TextChanged += (a, b) =>
            {
                int v;
                if (int.TryParse(goal.Text.Trim(), out v) && v >= 0 && v <= 1440) { settings.DailyGoal = v; settings.Save(); controller.RefreshGoal(); }
            };
            body.Children.Add(goal);

            body.Children.Add(Label(L.T("Alarm sesi"), 17, Text, 10));
            var sounds = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            for (int i = 0; i < Chime.Names.Length; i++)
            {
                int index = i;
                var chip = Chip(Chime.Names[i], () => { settings.Alarm = index; settings.Save(); controller.RebuildChime(); controller.PreviewChime(); Render(); });
                chip.Margin = new Thickness(0, 0, 8, 8);
                if (i == settings.Alarm) chip.Foreground = Gold;
                sounds.Children.Add(chip);
            }
            body.Children.Add(sounds);
            body.Children.Add(Label(L.T("Ses düzeyi"), 13, Soft, 4));
            var level = new Slider { Style = (Style)win.FindResource("Thin"), Minimum = 10, Maximum = 100, Value = settings.AlarmVolume };
            level.PreviewMouseLeftButtonUp += (a, b) => win.Dispatcher.BeginInvoke(new Action(() =>
            {
                settings.AlarmVolume = (int)level.Value;
                settings.Save();
                controller.RebuildChime();
                controller.PreviewChime();
            }), System.Windows.Threading.DispatcherPriority.Input);
            body.Children.Add(level);

        }

        TextBlock bridgeStatus, bridgeWarning;
        System.Windows.Threading.DispatcherTimer statusTimer;

        // Ayarlar açıkken bağlantı durumu kendiliğinden güncellenir.
        void UpdateBridgeStatus()
        {
            if (bridgeStatus == null) return;
            bool connected = controller.BridgeConnected;
            bridgeStatus.Text = connected ? L.T("Durum: Eklenti bağlı") : L.T("Durum: Eklenti bağlı değil. Müzik sekmesini aç veya yenile.");
            bridgeStatus.Foreground = connected ? Gold : Soft;
            var lines = controller.BridgeProblems.Select(x => ConnectBridge.SourceName(x) + L.T(" okunamadı. Sayfayı yenile. Düzelmezse TalverinConnect'i yeniden kur.")).ToList();
            if (controller.BridgeOutdated) lines.Add(L.T("Eklenti eski sürümde. TalverinConnect'i kur düğmesine bas, sonra tarayıcının eklenti sayfasından yenile."));
            bridgeWarning.Text = string.Join("\n", lines);
            bridgeWarning.Visibility = lines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void SettingsMusic()
        {
            if (statusTimer == null)
            {
                statusTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                statusTimer.Tick += (a, b) => { if (Open && page == 2 && settingsPage == 2) UpdateBridgeStatus(); };
                statusTimer.Start();
            }
            bridgeStatus = null;
            body.Children.Add(Label(L.T("Müzik kaynağı"), 17, Text, 0));
            body.Children.Add(Label(L.T("Otomatik seçimde Spotify bağlantısı önceliklidir, yoksa tarayıcıdaki müzik kullanılır."), 13, Soft, 4));
            string[] sources = { L.T("Otomatik"), "Spotify", L.T("Tarayıcı") };
            var sourceRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 14) };
            for (int i = 0; i < sources.Length; i++)
            {
                int index = i;
                var chip = Chip(sources[i], () => { settings.MusicSource = index; settings.Save(); Render(); });
                if (i == settings.MusicSource) chip.Foreground = Gold;
                sourceRow.Children.Add(chip);
            }
            body.Children.Add(sourceRow);
            body.Children.Add(Label(L.T("Spotify bağlantısı"), 17, Text, 0));
            body.Children.Add(Label(L.T("Spotify Premium gerekir. Spotify geliştirici panelinde ücretsiz bir uygulama oluştur, Redirect URI olarak http://127.0.0.1:8888/callback ekle ve Client ID'yi buraya yapıştır."), 13, Soft, 4));
            var idBox = Input(settings.SpotifyClientId ?? "", 0);
            var idResult = Label("", 13, Warn, 4);
            idBox.TextChanged += (a, b) =>
            {
                string id = idBox.Text.Trim();
                bool valid = Logic.ValidClientId(id);
                idResult.Text = id.Length == 0 || valid ? "" : L.T("Client ID 32 karakter olmalı.");
                if (valid || id.Length == 0) { settings.SpotifyClientId = id; settings.Save(); }
            };
            body.Children.Add(idBox);
            body.Children.Add(idResult);
            var devRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 14) };
            devRow.Children.Add(Chip(L.T("Spotify geliştirici panelini aç"), () => Process.Start("https://developer.spotify.com/dashboard")));
            body.Children.Add(devRow);
            body.Children.Add(Label("TalverinConnect", 17, Text, 0));
            body.Children.Add(Label(L.T("İsteğe bağlı tarayıcı eklentisi. YouTube Music için önerilen yol budur: şarkı bilgisi, kontroller, sarma, ses ve beğenme çalışır."), 13, Soft, 4));
            Toggle(L.T("TalverinConnect bağlantısını aç"), L.T("Eklenti yalnızca bu bilgisayardaki Kor ile konuşur, veriler internete gönderilmez."), settings.BridgeOn, v => { settings.BridgeOn = v; controller.RefreshBridge(); Render(); });
            if (settings.BridgeOn)
            {
                if (controller.BridgeError != null) body.Children.Add(Label(controller.BridgeError, 13, Warn, 0));
                bridgeStatus = Label("", 13, Soft, 0);
                bridgeWarning = Label("", 13, Warn, 4);
                body.Children.Add(bridgeStatus);
                body.Children.Add(bridgeWarning);
                UpdateBridgeStatus();
                body.Children.Add(new Border { Height = 10 });
                var result = Label("", 13, Gold, 6);
                var installRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 0) };
                installRow.Children.Add(Chip(L.T("TalverinConnect'i kur"), () =>
                {
                    string folder = ConnectBridge.ExtractExtension();
                    Clipboard.SetText(folder);
                    string browser = ConnectBridge.OpenExtensionsPage();
                    result.Text = browser == null
                        ? L.T("Klasör yolu kopyalandı. Tarayıcının eklentiler sayfasını aç, sonra aşağıdaki adımları izle.")
                        : L.T("Eklenti sayfası açıldı, klasör yolu kopyalandı.");
                }));
                body.Children.Add(installRow);
                body.Children.Add(result);
                body.Children.Add(Label(L.T("1. Geliştirici modunu aç.  2. Paketlenmemiş öğe yükle'ye bas.  3. Pencerenin adres alanına Ctrl+V ile yapıştırıp klasörü seç. Müzik sekmesini açınca Kor'da çıkan onaya izin ver."), 13, Soft, 8));
                body.Children.Add(new Border { Height = 12 });
            }
            Toggle(L.T("Müziği durdurunca sayacı durdur"), L.T("Müzik durunca sayaç duraklar, müzik başlayınca devam eder. Spotify'dan yapılan değişiklik birkaç saniye gecikebilir."), settings.LinkMusic, v => settings.LinkMusic = v);
            Toggle(L.T("Sayacı durdurunca müziği durdur"), L.T("Sayaç duraklayınca müzik durur, sayaç başlayınca veya devam edince müzik de başlar."), settings.LinkTimer, v => settings.LinkTimer = v);
            Toggle(L.T("Sessiz bitiş"), L.T("Son 10 saniyede müziği kısar, çan bir kez çalar, sonra ses geri döner."), settings.QuietFinish, v => settings.QuietFinish = v);
            Toggle(L.T("Son şarkıyla bitir"), L.T("Süre dolarken şarkı çalıyorsa sayaç şarkı bitince biter."), settings.EndWithSong, v => settings.EndWithSong = v);
            Toggle(L.T("Kapak rengine uyum"), L.T("Kadran, çalan şarkının kapağından renk alır."), settings.CoverColor, v => { settings.CoverColor = v; controller.RefreshAccent(); });
            body.Children.Add(Label(L.T("Ritme tepki"), 15, Text, 10));
            body.Children.Add(Label(L.T("Kadran bu bilgisayardaki Spotify uygulamasının baslarına hafifçe tepki verir. Diğer sesler etkilemez, ses kaydedilmez."), 13, Soft, 2));
            var beatRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            string[] beatNames = { L.T("Kapalı"), L.T("Hafif"), L.T("Belirgin") };
            for (int i = 0; i < beatNames.Length; i++)
            {
                int index = i;
                var chip = Chip(beatNames[i], () => { settings.BeatMode = index; settings.Save(); controller.RefreshBeat(); Render(); });
                if (i == settings.BeatMode) chip.Foreground = Gold;
                beatRow.Children.Add(chip);
            }
            body.Children.Add(beatRow);
        }

        void SettingsMini()
        {
            Toggle(L.T("Saydamken tıklamalar arkaya geçsin"), L.T("Mini sayaç saydamken boş alanlara tıklayınca alttaki pencere tıklanır. Düğmeler çalışır. Taşımak veya boyutlandırmak için Ctrl tuşunu basılı tut."), settings.ClickThrough, v => { settings.ClickThrough = v; controller.ApplyClickThrough(); });
            MinuteEditor(L.T("Bitir sonrası hızlı başlatma (dakika)"), settings.Quick, v => settings.Quick = v);
        }

        void SettingsKeys()
        {
            body.Children.Add(Label(L.T("Genel kısayollar"), 17, Text, 14));
            body.Children.Add(Label(L.T("Başka uygulamadayken de çalışır. Değiştirmek için kısayola tıkla ve tuşlara bas. Esc vazgeçer, Delete kapatır."), 13, Soft, 4));
            if (keyMessage != null) body.Children.Add(Label(keyMessage, 13, Warn, 6));
            foreach (var action in Hotkeys.Actions)
            {
                string id = action[0];
                var row = new Grid { Margin = new Thickness(0, 10, 0, 0) };
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                label.Children.Add(new TextBlock { Text = action[1], FontSize = 14, Foreground = Text, TextWrapping = TextWrapping.Wrap });
                if (hotkeys.Failed.Contains(id)) label.Children.Add(Label(L.T("Başka bir program bu kısayolu kullanıyor. Farklı bir kısayol seç."), 12, Warn, 2));
                row.Children.Add(label);
                string shown = capturing == id ? L.T("Tuşlara bas") : Hotkeys.Describe(settings.Keys.ContainsKey(id) ? settings.Keys[id] : "");
                var button = Chip(shown, () => StartCapture(id));
                button.Margin = new Thickness(10, 0, 0, 0);
                button.SetValue(System.Windows.Automation.AutomationProperties.AutomationIdProperty, "Key_" + id);
                Grid.SetColumn(button, 1);
                row.Children.Add(button);
                body.Children.Add(row);
            }
            var reset = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
            reset.Children.Add(Chip(L.T("Varsayılan kısayollara dön"), () =>
            {
                settings.Keys = Hotkeys.Defaults();
                settings.Save();
                keyMessage = null;
                hotkeys.Apply();
                Render();
            }));
            body.Children.Add(reset);
        }

        void StartCapture(string id)
        {
            capturing = id;
            keyMessage = null;
            hotkeys.Clear();
            Render();
        }

        void StopCapture()
        {
            if (capturing == null) return;
            capturing = null;
            hotkeys.Apply();
        }

        // Pencereye gelen tuşları önce panel görür. true: sayaç kısayolları çalışmasın.
        public bool HandleKey(KeyEventArgs e)
        {
            if (!Open) return false;
            if (capturing == null)
            {
                if (e.Key == Key.Escape) { Close(); e.Handled = true; }
                return true;
            }
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt
                || key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin) return true;
            string id = capturing;
            if (key == Key.Escape) { StopCapture(); Render(); return true; }
            if (key == Key.Delete || key == Key.Back) settings.Keys[id] = "";
            else
            {
                uint mods = 0;
                var m = Keyboard.Modifiers;
                if ((m & ModifierKeys.Control) != 0) mods |= Hotkeys.Ctrl;
                if ((m & ModifierKeys.Alt) != 0) mods |= Hotkeys.Alt;
                if ((m & ModifierKeys.Shift) != 0) mods |= Hotkeys.Shift;
                if ((m & ModifierKeys.Windows) != 0) mods |= Hotkeys.Win;
                bool functionKey = key >= Key.F1 && key <= Key.F24 || key >= Key.MediaNextTrack && key <= Key.MediaPlayPause;
                if ((mods & ~Hotkeys.Shift) == 0 && !functionKey)
                {
                    keyMessage = L.T("Bu tuşu Ctrl, Alt veya Win ile birlikte kullan. Tek başına yazı yazmanı engellerdi.");
                    Render();
                    return true;
                }
                string packed = Hotkeys.Pack(mods, KeyInterop.VirtualKeyFromKey(key));
                var other = settings.Keys.FirstOrDefault(x => x.Key != id && x.Value == packed);
                if (other.Key != null)
                {
                    keyMessage = L.T("Bu kısayol zaten kullanılıyor. Başka bir tuş dene.");
                    Render();
                    return true;
                }
                settings.Keys[id] = packed;
            }
            keyMessage = null;
            settings.Save();
            StopCapture();
            Render();
            return true;
        }
    }

    // Haftalık odak görseli: yalnızca kullanıcı istediğinde dosyaya yazılır.
    static class FocusCard
    {
        public static async Task Save(FocusStats stats, string path)
        {
            var top = stats.Top(stats.Week()).FirstOrDefault();
            BitmapImage cover = null;
            TrackRef reference;
            if (top.Key != null && stats.Refs.TryGetValue(top.Key, out reference) && !string.IsNullOrEmpty(reference.Cover))
            {
                try
                {
                    using (var http = new HttpClient())
                    {
                        var data = await http.GetByteArrayAsync(reference.Cover);
                        cover = new BitmapImage();
                        cover.BeginInit();
                        cover.StreamSource = new MemoryStream(data);
                        cover.CacheOption = BitmapCacheOption.OnLoad;
                        cover.EndInit();
                    }
                }
                catch (HttpRequestException) { cover = null; }
            }
            var accent = (cover != null ? SpotifyPanel.Dominant(cover) : null) ?? Ring.DefaultColor;
            var gold = new SolidColorBrush(accent);
            var white = new SolidColorBrush(Color.FromRgb(0xF4, 0xF1, 0xEA));
            var soft = new SolidColorBrush(Color.FromRgb(0x8E, 0x8A, 0x82));
            int minutes = (int)(stats.Week().Sum(d => d.Seconds) / 60);
            var font = new FontFamily("Bahnschrift");

            var root = new Grid { Width = 1080, Height = 1350, Background = Brushes.Black };
            var ring = new Ring { Width = 380, Height = 380, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 110, 0, 0), LayoutTransform = new ScaleTransform(2, 2) };
            ring.SetColor(accent);
            root.Children.Add(ring);
            var center = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 370, 0, 0) };
            center.Children.Add(new TextBlock { Text = L.T("Bu hafta odaklandın"), FontFamily = font, FontSize = 34, Foreground = soft, HorizontalAlignment = HorizontalAlignment.Center });
            center.Children.Add(new TextBlock { Text = minutes / 60 + L.T(" sa ") + minutes % 60 + L.T(" dk"), FontFamily = font, FontWeight = FontWeights.Light, FontSize = 104, Foreground = white, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) });
            root.Children.Add(center);

            var song = new Grid { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(90, 0, 90, 110) };
            song.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            song.ColumnDefinitions.Add(new ColumnDefinition());
            if (cover != null) song.Children.Add(new Image { Source = cover, Width = 190, Height = 190, Margin = new Thickness(0, 0, 36, 0) });
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(new TextBlock { Text = top.Key == null ? L.T("Müziksiz bir hafta") : L.T("En çok dinlediğin"), FontFamily = font, FontSize = 30, Foreground = gold });
            if (top.Key != null) words.Children.Add(new TextBlock { Text = top.Key, FontFamily = font, FontSize = 42, Foreground = white, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
            Grid.SetColumn(words, 1);
            song.Children.Add(words);
            root.Children.Add(song);

            var size = new Size(1080, 1350);
            root.Measure(size);
            root.Arrange(new Rect(size));
            root.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1080, 1350, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(path)) encoder.Save(file);
        }
    }
}
