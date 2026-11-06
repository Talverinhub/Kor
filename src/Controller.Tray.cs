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
        void BuildTray()
        {
            tray = new System.Windows.Forms.NotifyIcon
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location),
                Text = "Kor",
                Visible = true
            };
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add(L.T("Sayacı aç"), null, (a, b) => Expand());
            menu.Items.Add(L.T("Başlat veya duraklat"), null, (a, b) => RunHotkey("toggle"));
            trayClick = new System.Windows.Forms.ToolStripMenuItem(L.T("Saydamken tıklamalar arkaya geçsin")) { Checked = settings.ClickThrough };
            trayClick.Click += (a, b) => ToggleClickThrough();
            menu.Items.Add(trayClick);
            menu.Items.Add(L.T("Çıkış"), null, (a, b) => Exit());
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (a, b) => { if (b.Button == System.Windows.Forms.MouseButtons.Left) Expand(); };
        }

        FrameworkElement closeOverlay;

        void ShowCloseChoice()
        {
            Find<TextBlock>("CloseText").Text = mode == Mode.Setup
                ? L.T("Tepsiye indirirsen kısayollar ve hazır sayaç çalışmaya devam eder.")
                : L.T("Sayaç çalışıyor. Tepsiye indirirsen devam eder, tamamen kapatırsan durur.");
            Find<CheckBox>("CloseRemember").IsChecked = false;
            closeOverlay.Visibility = Visibility.Visible;
        }

        void FinishClose(bool toTray)
        {
            closeOverlay.Visibility = Visibility.Collapsed;
            if (Find<CheckBox>("CloseRemember").IsChecked == true)
            {
                settings.CloseAsk = false;
                settings.TrayOnClose = toTray;
                settings.Save();
            }
            if (toTray) GoTray(); else Exit();
        }

        bool wipeData;

        public void DeleteEverything()
        {
            var answer = MessageBox.Show(win, L.T("Ayarlar, rutinler, odak özeti ve Spotify girişi bu bilgisayardan silinecek. Uygulama başlangıçtan kaldırılıp kapanacak. Bu işlem geri alınamaz."), L.T("Verileri sil"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;
            Startup.Enabled = false;
            Store.Frozen = true;
            wipeData = true;
            Exit();
        }

        void WipeFolder()
        {
            try { if (Directory.Exists(Store.Folder)) Directory.Delete(Store.Folder, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public void Exit()
        {
            exiting = true;
            win.Close();
        }

        void GoTray()
        {
            inTray = true;
            win.Hide();
            Apply();
            if (trayHintShown) return;
            trayHintShown = true;
            tray.ShowBalloonTip(3000, L.T("Kor tepside"), L.T("Çıkmak için simgeye sağ tıkla."), System.Windows.Forms.ToolTipIcon.None);
        }

        public void StartInTray()
        {
            inTray = true;
            new WindowInteropHelper(win).EnsureHandle();
        }
    }
}
