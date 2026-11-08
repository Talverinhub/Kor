using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.Windows.Shapes.Path;

namespace Sayac
{
    public class NoteItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Title { get; set; }
        public string Text { get; set; }
        public string Content { get; set; }
        public int Color { get; set; }
        public string Updated { get; set; }
    }

    public class NotesData
    {
        public List<NoteItem> Notes { get; set; }
        public List<string> Open { get; set; }
        public string Active { get; set; }
        public bool Placed { get; set; }
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double SideWidth { get; set; }
        public double TopHeight { get; set; }
        public double SideHeight { get; set; }
        public double TopWidth { get; set; }
        public bool Docked { get; set; }
        public int Side { get; set; }
        public bool Visible { get; set; }

        public NotesData()
        {
            Notes = new List<NoteItem>();
            Open = new List<string>();
            Width = 320; Height = 420; SideWidth = 320; TopHeight = 220; Side = Logic.DockRight; Docked = true;
        }
    }

    // Kor'un yanında duran, sekmeli not defteri. Kor'a yaklaştırınca yapışır, uzaklaştırınca ayrılır.
    class NotePad
    {
        const string FileName = "notlar.json";
        const double Reach = 28;
        static readonly string[] ColorNames = { "Kor", "Kum", "Adaçayı", "Gül", "Gece" };
        static readonly Color[] Colors =
        {
            Theme.Map(Color.FromRgb(0x14, 0x13, 0x11)), Theme.Map(Color.FromRgb(0x26, 0x21, 0x16)), Theme.Map(Color.FromRgb(0x17, 0x21, 0x1C)),
            Theme.Map(Color.FromRgb(0x26, 0x1A, 0x1D)), Theme.Map(Color.FromRgb(0x15, 0x1A, 0x24))
        };
        static readonly SolidColorBrush Gold = new SolidColorBrush(Theme.Map(Color.FromRgb(0xD8, 0xC4, 0x9A)));
        static readonly SolidColorBrush Soft = new SolidColorBrush(Theme.Map(Color.FromRgb(0xB5, 0xB0, 0xA6)));
        static readonly SolidColorBrush Dim = new SolidColorBrush(Theme.Map(Color.FromRgb(0x8E, 0x8A, 0x82)));
        static readonly SolidColorBrush Bright = new SolidColorBrush(Theme.Map(Color.FromRgb(0xF4, 0xF1, 0xEA)));
        static readonly SolidColorBrush Edge = new SolidColorBrush(Theme.Map(Color.FromRgb(0x2B, 0x2A, 0x27)));
        static readonly SolidColorBrush ActiveTool = new SolidColorBrush(Theme.Map(Color.FromArgb(0x2E, 0xD8, 0xC4, 0x9A)));
        static readonly SolidColorBrush Mark = new SolidColorBrush(Theme.Map(Color.FromArgb(0x8C, 0xE6, 0xB8, 0x4A)));
        static readonly double[] Sizes = { 13, 15, 19 };
        static readonly SolidColorBrush Ink = new SolidColorBrush(Theme.Map(Color.FromRgb(0xE9, 0xE4, 0xDA)));
        static readonly FontFamily Symbols = new FontFamily("Segoe UI Symbol");

        // Silinen notlar bu oturum boyunca Ctrl+Z ile sırayla geri getirilebilir.
        class Deleted { public NoteItem Note; public int OpenIndex; }
        readonly Stack<Deleted> trash = new Stack<Deleted>();
        DateTime lastDelete = DateTime.MinValue, lastEdit = DateTime.MinValue;
        DispatcherTimer undoTimer;

        readonly Window main, mini, note;
        readonly NotesData data;
        readonly RichTextBox text;
        readonly DispatcherTimer saveTimer;
        NoteItem current;
        bool moving, dragging, loading, sizing;
        string confirmDelete, renaming;
        public event Action VisibilityChanged;

        public bool Visible { get { return data.Visible; } }

        T Part<T>(string name) where T : class { return (T)note.FindName(name); }

        public NotePad(Window mainWindow, Window miniWindow)
        {
            main = mainWindow; mini = miniWindow;
            note = Program.Load<Window>("note.xaml");
            text = Part<RichTextBox>("NoteText");
            data = Store.Load<NotesData>(FileName);
            if (data.Notes == null) data.Notes = new List<NoteItem>();
            if (data.Open == null) data.Open = new List<string>();
            data.Open = data.Open.Where(id => Find(id) != null).Distinct().ToList();
            saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
            saveTimer.Tick += (a, b) => { saveTimer.Stop(); Save(); };
            Bind();
        }

        NoteItem Find(string id) { return data.Notes.FirstOrDefault(n => n.Id == id); }

        void Bind()
        {
            var head = Part<FrameworkElement>("NoteHead");
            head.MouseLeftButtonDown += (a, b) =>
            {
                if (b.ClickCount > 1) return;
                dragging = true;
                try { note.DragMove(); }
                catch (InvalidOperationException) { }
                dragging = false;
                Part<Border>("NoteFrame").BorderBrush = Edge;
                Snap();
            };
            note.LocationChanged += (a, b) =>
            {
                if (dragging) Part<Border>("NoteFrame").BorderBrush = SnapTarget() > 0 ? Gold : Edge;
            };
            note.SourceInitialized += (a, b) =>
            {
                var source = System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(note).Handle);
                source.AddHook(SizeHook);
            };
            note.SizeChanged += (a, b) => { if (!moving && note.IsLoaded && !sizing) RememberSize(); };
            note.Activated += (a, b) => Part<FrameworkElement>("NoteBar").Visibility = data.Open.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            Part<Button>("NoteHomeNew").Click += (a, b) => AskName(null);
            note.Deactivated += (a, b) => { Part<FrameworkElement>("NoteBar").Visibility = Visibility.Collapsed; Save(); };
            note.PreviewKeyDown += OnKey;

            Part<Button>("NoteNew").Click += (a, b) => AskName(null);
            var name = Part<TextBox>("NoteName");
            name.TextChanged += (a, b) => Part<UIElement>("NoteNameHint").Visibility = name.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            name.KeyDown += (a, b) =>
            {
                if (b.Key == Key.Enter) { b.Handled = true; FinishName(true); }
                else if (b.Key == Key.Escape) { b.Handled = true; FinishName(false); }
            };
            name.LostKeyboardFocus += (a, b) => FinishName(name.Text.Trim().Length > 0);
            Part<FrameworkElement>("NoteTabRow").PreviewMouseWheel += (a, b) => { b.Handled = true; Cycle(b.Delta < 0 ? 1 : -1); };
            Part<Button>("NoteHide").Click += (a, b) => Hide();
            Part<Button>("NoteLibrary").Click += (a, b) => { confirmDelete = null; Part<TextBox>("NoteSearch").Text = ""; RenderLibrary(); Part<Popup>("NoteLibraryPop").IsOpen = true; };
            Part<Button>("NoteColor").Click += (a, b) => { RenderColors(); Part<Popup>("NoteColorPop").IsOpen = true; };

            Part<Button>("NoteBold").Click += (a, b) => Run(EditingCommands.ToggleBold);
            Part<Button>("NoteItalic").Click += (a, b) => Run(EditingCommands.ToggleItalic);
            Part<Button>("NoteMark").Click += (a, b) => ToggleMark();
            Part<Button>("NoteSmall").Click += (a, b) => SetSize(Sizes[0]);
            Part<Button>("NoteMedium").Click += (a, b) => SetSize(Sizes[1]);
            Part<Button>("NoteLarge").Click += (a, b) => SetSize(Sizes[2]);
            Part<Button>("NoteLeft").Click += (a, b) => Run(EditingCommands.AlignLeft);
            Part<Button>("NoteCenter").Click += (a, b) => Run(EditingCommands.AlignCenter);
            Part<Button>("NoteRight").Click += (a, b) => Run(EditingCommands.AlignRight);

            text.TextChanged += (a, b) => { UpdateHint(); if (!loading) { lastEdit = DateTime.UtcNow; SaveLater(); } };
            text.PreviewMouseLeftButtonDown += OnTextClick;
            Part<Button>("NoteCheck").Click += (a, b) => ToggleChecklist();
            Part<Button>("NoteUndoBtn").Click += (a, b) => Restore();
            var search = Part<TextBox>("NoteSearch");
            search.TextChanged += (a, b) =>
            {
                Part<UIElement>("NoteSearchHint").Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                RenderLibrary();
            };
            Part<Popup>("NoteLibraryPop").Opened += (a, b) => search.Dispatcher.BeginInvoke(new Action(() => search.Focus()), DispatcherPriority.Input);
            text.SelectionChanged += (a, b) => UpdateTools();
        }

        const int EnterSizeMove = 0x231, ExitSizeMove = 0x232;

        // Kullanıcı kenardan boyutlandırırken not yerinde kalır; bırakınca yapışıksa Kor'a göre yeniden hizalanır.
        IntPtr SizeHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == EnterSizeMove && !dragging) sizing = true;
            else if (msg == ExitSizeMove && sizing)
            {
                sizing = false;
                RememberSize();
                Follow();
            }
            return IntPtr.Zero;
        }

        void RememberSize()
        {
            var host = Host();
            if (data.Docked && host != null)
            {
                if (host == mini) { data.TopHeight = note.ActualHeight; data.TopWidth = note.ActualWidth; }
                else { data.SideWidth = note.ActualWidth; data.SideHeight = note.ActualHeight; }
            }
            else { data.Width = note.ActualWidth; data.Height = note.ActualHeight; data.Left = note.Left; data.Top = note.Top; }
            SaveLater();
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (Part<TextBox>("NoteName").IsKeyboardFocused || Part<TextBox>("NoteSearch").IsKeyboardFocused)
            {
                if (ctrl && e.Key == Key.Z && Part<TextBox>("NoteSearch").IsKeyboardFocused && !Part<TextBox>("NoteSearch").CanUndo && trash.Count > 0) { Restore(); e.Handled = true; }
                return;
            }
            if (ctrl && e.Key == Key.Z && trash.Count > 0 && (!text.CanUndo || lastDelete > lastEdit)) { Restore(); e.Handled = true; }
            else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None && ContinueChecklist()) e.Handled = true;
            else if (ctrl && e.Key == Key.H) { ToggleMark(); e.Handled = true; }
            else if (ctrl && e.Key == Key.T) { AskName(null); e.Handled = true; }
            else if (ctrl && e.Key == Key.Tab) { Cycle((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? -1 : 1); e.Handled = true; }
            else if (ctrl && e.Key >= Key.D1 && e.Key <= Key.D9) { Jump(e.Key - Key.D1); e.Handled = true; }
            else if (ctrl && e.Key == Key.W) { if (current != null) CloseTab(current.Id); e.Handled = true; }
            else if (e.Key == Key.Escape && !Part<TextBox>("NoteName").IsKeyboardFocused) { Hide(); e.Handled = true; }
        }

        // Görünürlük

        public void Toggle() { if (data.Visible) Hide(); else Show(); }

        public void Show()
        {
            if (current == null && data.Open.Count > 0) Load(Find(data.Active) ?? Find(data.Open[0]));
            data.Visible = true;
            RenderTabs();
            if (!data.Placed && Host() != null) { data.Placed = true; data.Docked = true; }
            if (!data.Docked) Place(new Rect(data.Left, data.Top, data.Width, data.Height));
            note.Show();
            note.Activate();
            Follow();
            if (data.Open.Count > 0) text.Focus();
            Save();
            if (VisibilityChanged != null) VisibilityChanged();
        }

        public void Hide()
        {
            Save();
            data.Visible = false;
            note.Hide();
            Store.Save(FileName, data);
            if (VisibilityChanged != null) VisibilityChanged();
        }

        // Kor kapanırken not kaydedilir ve pencere kapanır.
        public void Close()
        {
            Save();
            note.Close();
        }

        // Mini sayaca yapışıkken not da görev çubuğunun önünde kalır.
        public void RaiseIfOnTop(Action<Window> raise)
        {
            if (note.IsVisible && note.Topmost) raise(note);
        }

        // Odak modunda gösterilecek sıradaki iş: konunun notundaki ilk işaretsiz madde.
        public string NextStep(string topic)
        {
            Capture();
            var item = data.Notes.FirstOrDefault(n => !string.IsNullOrEmpty(topic) && string.Equals((n.Name ?? "").Trim(), topic.Trim(), StringComparison.CurrentCultureIgnoreCase))
                ?? Find(data.Active);
            if (item == null || item.Text == null) return null;
            foreach (string raw in item.Text.Split('\n'))
            {
                string line = raw.Trim();
                if (Logic.ChecklistState(line) == 1)
                {
                    string rest = line.Substring(1).Trim();
                    if (rest.Length > 0) return rest;
                }
            }
            return null;
        }

        public void RestoreWindow() { if (data.Visible) Show(); }

        // Sayaç bir konuyla başlarken o adı taşıyan not öne gelir; notlar açıksa yoksa oluşturulur.
        public void Topic(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) return;
            if (name.Length > 40) name = name.Substring(0, 40).TrimEnd();
            Capture();
            var item = data.Notes.FirstOrDefault(n => string.Equals((n.Name ?? "").Trim(), name, StringComparison.CurrentCultureIgnoreCase));
            if (item == null)
            {
                if (!data.Visible) return;
                item = Create();
                item.Name = name;
            }
            if (data.Visible) { Open(item); return; }
            if (!data.Open.Contains(item.Id)) data.Open.Add(item.Id);
            data.Active = item.Id;
            current = null;
            Store.Save(FileName, data);
        }

        // Yapışma

        Window Host()
        {
            if (main.IsVisible && main.WindowState == WindowState.Normal) return main;
            if (mini.IsVisible) return mini;
            return null;
        }

        static Rect Bounds(Window w) { return new Rect(w.Left, w.Top, w.ActualWidth, w.ActualHeight); }

        static Rect Screen()
        {
            return new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        }

        int SnapTarget()
        {
            var host = Host();
            if (host == null) return 0;
            return Logic.NoteSnap(Bounds(note), Bounds(host), host == mini, Reach);
        }

        void Snap()
        {
            int side = SnapTarget();
            data.Placed = true;
            if (side > 0)
            {
                data.Docked = true;
                if (side != Logic.DockTop) data.Side = side;
                Follow();
            }
            else
            {
                data.Docked = false;
                data.Left = note.Left; data.Top = note.Top;
            }
            Store.Save(FileName, data);
        }

        // Yapışıkken not, Kor taşınınca veya küçülünce onunla birlikte hareket eder.
        public void Follow()
        {
            if (!data.Visible) return;
            var host = Host();
            if (!note.IsVisible) note.Show();
            note.Topmost = data.Docked && host == mini;
            if (!data.Docked || host == null) return;
            if (sizing) return;
            bool top = host == mini;
            var size = top ? new Size(data.TopWidth, data.TopHeight) : new Size(data.SideWidth, data.SideHeight);
            Place(Logic.NoteDock(top ? Logic.DockTop : data.Side, Bounds(host), size, Screen()));
        }

        void Place(Rect r)
        {
            moving = true;
            note.Left = r.X; note.Top = r.Y;
            note.Width = Math.Max(note.MinWidth, r.Width);
            note.Height = Math.Max(note.MinHeight, r.Height);
            moving = false;
        }

        // Notlar ve sekmeler

        NoteItem Create()
        {
            var item = new NoteItem { Id = Guid.NewGuid().ToString("N"), Title = "", Content = "", Updated = DateTime.UtcNow.ToString("o") };
            data.Notes.Add(item);
            return item;
        }

        // + önce adı sorar; ad kutusu sekme satırında açılır.
        void AskName(string id)
        {
            renaming = id;
            var box = Part<TextBox>("NoteName");
            var item = id == null ? null : Find(id);
            box.Text = item == null ? "" : item.Name ?? "";
            Part<UIElement>("NoteNameBox").Visibility = Visibility.Visible;
            Part<FrameworkElement>("NoteNameBox").BringIntoView();
            box.Focus();
            box.SelectAll();
        }

        void FinishName(bool accept)
        {
            var panel = Part<UIElement>("NoteNameBox");
            if (panel.Visibility != Visibility.Visible) return;
            panel.Visibility = Visibility.Collapsed;
            string value = Part<TextBox>("NoteName").Text.Trim();
            string id = renaming;
            renaming = null;
            if (accept && id != null) { var item = Find(id); if (item != null) { item.Name = value; RenderTabs(); Save(); } }
            else if (accept) NewNote(value);
            text.Focus();
        }

        // Sekmeler arasında sırayla gezinir; son sekmeden sonra başa döner.
        void Cycle(int step)
        {
            if (current == null || data.Open.Count < 2) return;
            int index = (data.Open.IndexOf(current.Id) + step + data.Open.Count) % data.Open.Count;
            Open(Find(data.Open[index]));
        }

        void Jump(int index)
        {
            if (index < data.Open.Count) Open(Find(data.Open[index]));
        }

        void NewNote(string name)
        {
            Capture();
            var item = Create();
            item.Name = name;
            data.Open.Add(item.Id);
            Load(item);
            RenderTabs();
            text.Focus();
            Save();
        }

        void Open(NoteItem item)
        {
            if (!data.Open.Contains(item.Id)) data.Open.Add(item.Id);
            Capture();
            Load(item);
            RenderTabs();
            Save();
        }

        void CloseTab(string id)
        {
            Capture();
            var item = Find(id);
            if (item != null && Logic.NoteIsEmpty(item.Title) && Logic.NoteIsEmpty(item.Name)) data.Notes.Remove(item);
            int index = data.Open.IndexOf(id);
            data.Open.Remove(id);
            if (current != null && current.Id == id)
            {
                current = null;
                if (data.Open.Count == 0) { RenderTabs(); Save(); return; }
                Load(Find(data.Open[Math.Max(0, Math.Min(index, data.Open.Count) - 1)]));
            }
            RenderTabs();
            Save();
        }

        void Delete(NoteItem item)
        {
            Capture();
            trash.Push(new Deleted { Note = item, OpenIndex = data.Open.IndexOf(item.Id) });
            lastDelete = DateTime.UtcNow;
            ShowUndo(item);
            if (current == item) { current = null; data.Open.Remove(item.Id); data.Notes.Remove(item); if (data.Open.Count > 0) Load(Find(data.Open[0])); }
            else { data.Open.Remove(item.Id); data.Notes.Remove(item); }
            RenderTabs();
            RenderLibrary();
            Save();
        }

        void ShowUndo(NoteItem item)
        {
            Part<TextBlock>("NoteUndoText").Text = L.F("\u201C{0}\u201D silindi", Name(item));
            Part<UIElement>("NoteUndo").Visibility = Visibility.Visible;
            if (undoTimer == null)
            {
                undoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
                undoTimer.Tick += (a, b) => { undoTimer.Stop(); Part<UIElement>("NoteUndo").Visibility = Visibility.Collapsed; };
            }
            undoTimer.Stop();
            undoTimer.Start();
        }

        // Son silinen not, sekmesi açıksa eski yerine döner.
        void Restore()
        {
            if (trash.Count == 0) return;
            var entry = trash.Pop();
            Part<UIElement>("NoteUndo").Visibility = Visibility.Collapsed;
            if (Find(entry.Note.Id) == null) data.Notes.Add(entry.Note);
            if (!data.Open.Contains(entry.Note.Id))
            {
                int index = entry.OpenIndex < 0 ? data.Open.Count : Math.Min(entry.OpenIndex, data.Open.Count);
                data.Open.Insert(index, entry.Note.Id);
            }
            lastDelete = DateTime.MinValue;
            if (!data.Visible) { data.Active = entry.Note.Id; current = null; Show(); }
            else Open(entry.Note);
            if (Part<Popup>("NoteLibraryPop").IsOpen) RenderLibrary();
        }

        void Load(NoteItem item)
        {
            loading = true;
            current = item;
            data.Active = item.Id;
            var doc = new FlowDocument { PagePadding = new Thickness(0), FontFamily = text.FontFamily, FontSize = Sizes[1] };
            text.Document = doc;
            if (!string.IsNullOrEmpty(item.Content))
            {
                try
                {
                    using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(item.Content)))
                        new TextRange(doc.ContentStart, doc.ContentEnd).Load(stream, DataFormats.Xaml);
                }
                catch (Exception) { new TextRange(doc.ContentStart, doc.ContentEnd).Text = item.Title ?? ""; }
            }
            ApplyColor();
            UpdateHint();
            UpdateTools();
            loading = false;
        }

        // Açık notun içeriği kayda alınır; değişmediyse tarih korunur.
        void Capture()
        {
            if (current == null) return;
            var range = new TextRange(text.Document.ContentStart, text.Document.ContentEnd);
            string content;
            using (var stream = new MemoryStream())
            {
                range.Save(stream, DataFormats.Xaml);
                content = Encoding.UTF8.GetString(stream.ToArray());
            }
            if (content == current.Content) return;
            current.Content = content;
            current.Title = Logic.NoteTitle(range.Text);
            current.Text = range.Text.Length > 20000 ? range.Text.Substring(0, 20000) : range.Text;
            current.Updated = DateTime.UtcNow.ToString("o");
        }

        void SaveLater() { saveTimer.Stop(); saveTimer.Start(); }

        void Save()
        {
            saveTimer.Stop();
            Capture();
            Store.Save(FileName, data);
            if (current != null) RenderTabTitles();
        }

        void RenderTabs()
        {
            UpdateHome();
            var tabs = Part<StackPanel>("NoteTabs");
            tabs.Children.Clear();
            foreach (string id in data.Open)
            {
                var item = Find(id);
                if (item == null) continue;
                bool active = current == item;
                var title = new TextBlock
                {
                    Text = Name(item), FontSize = 13, MaxWidth = 120, TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = active ? Bright : Dim, VerticalAlignment = VerticalAlignment.Center, Tag = id
                };
                var close = new TextBlock
                {
                    Text = "×", FontSize = 15, Foreground = Dim, Margin = new Thickness(6, -1, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                    Opacity = active ? 1 : 0, Cursor = Cursors.Hand, ToolTip = L.T("Sekmeyi kapat")
                };
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(title);
                row.Children.Add(close);
                var line = new Border { Height = 2, CornerRadius = new CornerRadius(1), Background = active ? Gold : Brushes.Transparent, Margin = new Thickness(0, 6, 0, 0) };
                var stack = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
                stack.Children.Add(row);
                stack.Children.Add(line);
                var tab = new Border { Child = stack, Padding = new Thickness(8, 0, 6, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand };
                string tabId = id;
                tab.MouseEnter += (a, b) => close.Opacity = 1;
                tab.MouseLeave += (a, b) => close.Opacity = current != null && current.Id == tabId ? 1 : 0;
                tab.MouseLeftButtonDown += (a, b) =>
                {
                    b.Handled = true;
                    if (b.ClickCount == 2) { AskName(tabId); return; }
                    var n = Find(tabId);
                    if (n != null && n != current) Open(n);
                };
                tab.ToolTip = L.T("Yeniden adlandırmak için çift tıkla");
                if (active) tab.Loaded += (a, b) => tab.BringIntoView();
                tab.MouseDown += (a, b) => { if (b.ChangedButton == MouseButton.Middle) { b.Handled = true; CloseTab(tabId); } };
                close.MouseLeftButtonDown += (a, b) => { b.Handled = true; CloseTab(tabId); };
                tabs.Children.Add(tab);
            }
        }

        // Açık not kalmayınca not penceresi kapanmaz; son notların listelendiği ana sayfa görünür.
        void UpdateHome()
        {
            bool home = data.Open.Count == 0;
            if (home) current = null;
            Part<UIElement>("NoteHome").Visibility = home ? Visibility.Visible : Visibility.Collapsed;
            Part<UIElement>("NoteBody").Visibility = home ? Visibility.Collapsed : Visibility.Visible;
            if (home) Part<UIElement>("NoteBar").Visibility = Visibility.Collapsed;
            else if (note.IsActive) Part<UIElement>("NoteBar").Visibility = Visibility.Visible;
            if (!home) return;
            var list = Part<StackPanel>("NoteHomeList");
            list.Children.Clear();
            var recent = data.Notes.Where(n => !Logic.NoteIsEmpty(n.Title) || !Logic.NoteIsEmpty(n.Name)).OrderByDescending(n => n.Updated).Take(5).ToList();
            Part<UIElement>("NoteHomeLabel").Visibility = recent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var item in recent)
            {
                var target = item;
                var row = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 7, 10, 7), Background = Brushes.Transparent, Cursor = Cursors.Hand };
                var line = new DockPanel();
                DateTime when;
                if (DateTime.TryParse(item.Updated, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out when))
                {
                    var date = new TextBlock { Text = when.ToLocalTime().ToString("d MMM", L.Culture), Foreground = Dim, FontSize = 12, Margin = new Thickness(10, 0, 0, 0) };
                    DockPanel.SetDock(date, Dock.Right);
                    line.Children.Add(date);
                }
                line.Children.Add(new TextBlock { Text = Name(item), Foreground = Bright, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });
                row.Child = line;
                row.MouseEnter += (a, b) => row.Background = new SolidColorBrush(Theme.Map(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)));
                row.MouseLeave += (a, b) => row.Background = Brushes.Transparent;
                row.MouseLeftButtonDown += (a, b) => { Open(target); text.Focus(); };
                list.Children.Add(row);
            }
        }

        // Yazarken sekme adları yeniden çizilmeden güncellenir.
        void RenderTabTitles()
        {
            foreach (var tab in Part<StackPanel>("NoteTabs").Children.OfType<Border>())
            {
                var title = ((StackPanel)((StackPanel)tab.Child).Children[0]).Children[0] as TextBlock;
                var item = title == null ? null : Find(title.Tag as string);
                if (item != null) title.Text = Name(item);
            }
        }

        static string Name(NoteItem item)
        {
            if (!Logic.NoteIsEmpty(item.Name)) return item.Name;
            return Logic.NoteIsEmpty(item.Title) ? L.T("Yeni not") : item.Title;
        }

        void RenderLibrary()
        {
            var list = Part<StackPanel>("NoteLibraryList");
            list.Children.Clear();
            Capture();
            string query = Part<TextBox>("NoteSearch").Text;
            var notes = data.Notes.Where(n => (!Logic.NoteIsEmpty(n.Title) || !Logic.NoteIsEmpty(n.Name)) && Logic.NoteMatches(query, n.Name, n.Title, n.Text))
                .OrderByDescending(n => n.Updated).ToList();
            if (notes.Count == 0)
            {
                list.Children.Add(new TextBlock { Text = query.Trim().Length > 0 ? L.T("Eşleşen not yok.") : L.T("Kayıtlı not yok."), Foreground = Dim, FontSize = 13, Margin = new Thickness(8) });
                return;
            }
            foreach (var item in notes)
            {
                var note1 = item;
                var grid = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand };
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var info = new StackPanel { Margin = new Thickness(8, 6, 4, 6) };
                info.Children.Add(new TextBlock { Text = Name(item), Foreground = current == item ? Gold : Bright, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });
                DateTime when;
                if (DateTime.TryParse(item.Updated, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out when))
                    info.Children.Add(new TextBlock { Text = when.ToLocalTime().ToString("d MMM, HH:mm", L.Culture), Foreground = Dim, FontSize = 11, Margin = new Thickness(0, 2, 0, 0) });
                grid.Children.Add(info);
                bool asking = confirmDelete == item.Id;
                var trash = new Border
                {
                    Padding = new Thickness(8, 4, 8, 4), CornerRadius = new CornerRadius(6), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0),
                    Background = asking ? new SolidColorBrush(Theme.Map(Color.FromArgb(0x40, 0xE0, 0x6C, 0x5F))) : Brushes.Transparent, ToolTip = L.T("Sil"),
                    Child = asking
                        ? (UIElement)new TextBlock { Text = L.T("Sil?"), Foreground = new SolidColorBrush(Theme.Map(Color.FromRgb(0xF0, 0x9A, 0x8F))), FontSize = 12 }
                        : new Path { Data = Geometry.Parse("M0,2 H10 M3,2 V0 H7 V2 M1.5,2 L2.5,11 H7.5 L8.5,2"), Stroke = Dim, StrokeThickness = 1.1 }
                };
                Grid.SetColumn(trash, 1);
                grid.Children.Add(trash);
                trash.MouseLeftButtonDown += (a, b) =>
                {
                    b.Handled = true;
                    if (confirmDelete == note1.Id) { confirmDelete = null; Delete(note1); }
                    else { confirmDelete = note1.Id; RenderLibrary(); }
                };
                grid.MouseEnter += (a, b) => grid.Background = new SolidColorBrush(Theme.Map(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)));
                grid.MouseLeave += (a, b) => grid.Background = Brushes.Transparent;
                grid.MouseLeftButtonDown += (a, b) => { Part<Popup>("NoteLibraryPop").IsOpen = false; Open(note1); text.Focus(); };
                list.Children.Add(grid);
            }
        }

        void RenderColors()
        {
            var list = Part<StackPanel>("NoteColorList");
            list.Children.Clear();
            for (int i = 0; i < Colors.Length; i++)
            {
                int index = i;
                bool selected = current != null && current.Color == i;
                var dot = new Ellipse
                {
                    Width = 22, Height = 22, Margin = new Thickness(4), Cursor = Cursors.Hand, Fill = new SolidColorBrush(Lift(Colors[i])),
                    Stroke = selected ? Gold : new SolidColorBrush(Theme.Map(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF))), StrokeThickness = selected ? 2 : 1, ToolTip = L.T(ColorNames[i])
                };
                dot.MouseLeftButtonDown += (a, b) =>
                {
                    if (current == null) return;
                    current.Color = index;
                    ApplyColor();
                    Part<Popup>("NoteColorPop").IsOpen = false;
                    Store.Save(FileName, data);
                };
                list.Children.Add(dot);
            }
        }

        // Seçim noktaları zeminden biraz açık çizilir ki koyu renkler ayırt edilsin.
        static Color Lift(Color c) { return Color.FromRgb((byte)Math.Min(255, c.R + 30), (byte)Math.Min(255, c.G + 30), (byte)Math.Min(255, c.B + 30)); }

        void ApplyColor()
        {
            int index = current == null ? 0 : Logic.Clamp(current.Color, 0, Colors.Length - 1);
            Part<Border>("NoteFrame").Background = new SolidColorBrush(Colors[index]);
            Part<Ellipse>("NoteColorDot").Fill = new SolidColorBrush(Lift(Colors[index]));
        }

        // Yapılacaklar listesi: satır başındaki kutuya tıklayınca işaretlenir.

        static TextPointer Advance(TextPointer position, int count)
        {
            for (int i = 0; i < count && position != null; i++) position = position.GetNextInsertionPosition(LogicalDirection.Forward);
            return position;
        }

        static TextPointer Start(Paragraph p) { return p.ContentStart.GetInsertionPosition(LogicalDirection.Forward); }

        static int CheckState(Paragraph p)
        {
            string line = new TextRange(p.ContentStart, p.ContentEnd).Text;
            return Logic.ChecklistState(line);
        }

        IEnumerable<Paragraph> SelectedParagraphs()
        {
            var first = text.Selection.Start.Paragraph;
            var last = text.Selection.End.Paragraph;
            if (first == null)
            {
                var created = new Paragraph();
                text.Document.Blocks.Add(created);
                text.CaretPosition = created.ContentStart;
                yield return created;
                yield break;
            }
            for (Block block = first; block != null; block = block.NextBlock)
            {
                var p = block as Paragraph;
                if (p != null) yield return p;
                if (block == last) break;
            }
        }

        void ToggleChecklist()
        {
            var paragraphs = SelectedParagraphs().ToList();
            bool add = paragraphs.Any(p => CheckState(p) == 0);
            foreach (var p in paragraphs)
            {
                if (add && CheckState(p) == 0) AddBox(p);
                else if (!add && CheckState(p) != 0) RemoveBox(p);
            }
            text.Focus();
            UpdateTools();
        }

        void AddBox(Paragraph p)
        {
            bool empty = CheckState(p) == 0 && new TextRange(p.ContentStart, p.ContentEnd).Text.Length == 0;
            var start = Start(p);
            start.InsertTextInRun(Logic.BoxOpen + " ");
            StyleBox(p, false);
            if (empty) text.CaretPosition = p.ContentEnd;
        }

        void RemoveBox(Paragraph p)
        {
            SetDone(p, false);
            var start = Start(p);
            new TextRange(start, Advance(start, 2)).Text = "";
        }

        void StyleBox(Paragraph p, bool done)
        {
            var start = Start(p);
            var box = new TextRange(start, Advance(start, 1));
            box.Text = done ? Logic.BoxDone : Logic.BoxOpen;
            box = new TextRange(Start(p), Advance(Start(p), 1));
            box.ApplyPropertyValue(TextElement.FontFamilyProperty, Symbols);
            box.ApplyPropertyValue(TextElement.ForegroundProperty, Gold);
            box.ApplyPropertyValue(Inline.TextDecorationsProperty, new TextDecorationCollection());
        }

        void SetDone(Paragraph p, bool done)
        {
            StyleBox(p, done);
            var rest = new TextRange(Advance(Start(p), 1), p.ContentEnd);
            rest.ApplyPropertyValue(Inline.TextDecorationsProperty, done ? TextDecorations.Strikethrough : new TextDecorationCollection());
            rest.ApplyPropertyValue(TextElement.ForegroundProperty, done ? Dim : Ink);
        }

        void OnTextClick(object sender, MouseButtonEventArgs e)
        {
            var point = e.GetPosition(text);
            var position = text.GetPositionFromPoint(point, true);
            var p = position == null ? null : position.Paragraph;
            if (p == null || CheckState(p) == 0) return;
            var box = Start(p).GetCharacterRect(LogicalDirection.Forward);
            if (point.X < box.Left - 4 || point.X > box.Left + 22 || point.Y < box.Top - 2 || point.Y > box.Bottom + 2) return;
            e.Handled = true;
            SetDone(p, CheckState(p) == 1);
            SaveLater();
        }

        // Kutulu satırda Enter yeni kutu açar; boş kutuda Enter listeden çıkar.
        bool ContinueChecklist()
        {
            if (!text.IsKeyboardFocused || !text.Selection.IsEmpty) return false;
            var p = text.CaretPosition.Paragraph;
            if (p == null || CheckState(p) == 0) return false;
            if (new TextRange(p.ContentStart, p.ContentEnd).Text.Trim().Length <= 1) { RemoveBox(p); return true; }
            text.Dispatcher.BeginInvoke(new Action(() =>
            {
                var next = text.CaretPosition.Paragraph;
                if (next == null || next == p || CheckState(next) != 0) return;
                var all = new TextRange(next.ContentStart, next.ContentEnd);
                all.ApplyPropertyValue(Inline.TextDecorationsProperty, new TextDecorationCollection());
                all.ApplyPropertyValue(TextElement.ForegroundProperty, Ink);
                AddBox(next);
                text.CaretPosition = Advance(Start(next), 2) ?? next.ContentEnd;
            }), DispatcherPriority.Input);
            return false;
        }

        // Yazı araçları

        void Run(RoutedUICommand command)
        {
            command.Execute(null, text);
            text.Focus();
            UpdateTools();
        }

        void ToggleMark()
        {
            var selection = text.Selection;
            if (selection.IsEmpty) return;
            selection.ApplyPropertyValue(TextElement.BackgroundProperty, IsMarked(selection) ? Brushes.Transparent : Mark);
            text.Focus();
            UpdateTools();
        }

        static bool IsMarked(TextSelection selection)
        {
            var brush = selection.GetPropertyValue(TextElement.BackgroundProperty) as SolidColorBrush;
            return brush != null && brush.Color == Mark.Color;
        }

        void SetSize(double size)
        {
            text.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, size);
            text.Focus();
            UpdateTools();
        }

        void UpdateTools()
        {
            var selection = text.Selection;
            object weight = selection.GetPropertyValue(TextElement.FontWeightProperty);
            object style = selection.GetPropertyValue(TextElement.FontStyleProperty);
            object size = selection.GetPropertyValue(TextElement.FontSizeProperty);
            object align = selection.GetPropertyValue(Paragraph.TextAlignmentProperty);
            Tool("NoteBold", weight is FontWeight && (FontWeight)weight == FontWeights.Bold);
            Tool("NoteItalic", style is FontStyle && (FontStyle)style == FontStyles.Italic);
            Tool("NoteMark", IsMarked(selection));
            var para = selection.Start.Paragraph;
            Tool("NoteCheck", para != null && CheckState(para) != 0);
            double value = size is double ? (double)size : -1;
            Tool("NoteSmall", value == Sizes[0]);
            Tool("NoteMedium", value == Sizes[1]);
            Tool("NoteLarge", value == Sizes[2]);
            TextAlignment alignment = align is TextAlignment ? (TextAlignment)align : (TextAlignment)(-1);
            Tool("NoteLeft", alignment == TextAlignment.Left);
            Tool("NoteCenter", alignment == TextAlignment.Center);
            Tool("NoteRight", alignment == TextAlignment.Right);
        }

        void Tool(string name, bool on)
        {
            var button = Part<Button>(name);
            button.Foreground = on ? Gold : Soft;
            button.Background = on ? ActiveTool : Brushes.Transparent;
        }

        void UpdateHint()
        {
            bool empty = Logic.NoteIsEmpty(new TextRange(text.Document.ContentStart, text.Document.ContentEnd).Text);
            Part<UIElement>("NoteHint").Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
