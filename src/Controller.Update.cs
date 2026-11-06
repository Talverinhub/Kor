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
        const int UpdateEveryMinutes = 360;
        string pendingUpdate, pendingVersion, pendingNotes;
        bool installing, manualInstall, updateLater;
        int updateMinutes;

        public async void StartUpdateCheck()
        {
            if (!settings.AutoUpdate) return;
            await System.Threading.Tasks.Task.Delay(3000);
            await CheckForUpdate(false);
        }

        public async System.Threading.Tasks.Task<string> CheckForUpdate(bool manual)
        {
            manualInstall = manual;
            if (manual) updateLater = false;
            try
            {
                if (pendingUpdate == null)
                {
                    var info = await Updater.Check();
                    if (info == null) return L.T("Uygulama güncel.");
                    pendingVersion = info.Version;
                    pendingNotes = info.Notes;
                    tray.ShowBalloonTip(4000, L.T("Güncelleme var"), L.T("Sürüm ") + info.Version + L.T(" indiriliyor. ") + info.Notes, System.Windows.Forms.ToolTipIcon.None);
                    pendingUpdate = await Updater.Download(info);
                }
                bool now = mode == Mode.Setup;
                TryInstall();
                return now ? L.T("Güncelleme kuruluyor. Uygulama yeniden başlayacak.") : L.T("Güncelleme hazır. Sayaç bitince kurulacak.");
            }
            catch (System.Net.Http.HttpRequestException) { return L.T("Güncelleme sunucusuna ulaşılamadı. İnternet bağlantını kontrol et."); }
            catch (System.Threading.Tasks.TaskCanceledException) { return L.T("Güncelleme sunucusuna ulaşılamadı. İnternet bağlantını kontrol et."); }
            catch (InvalidOperationException e) { return e.Message; }
            catch (FormatException) { return L.T("Güncelleme bilgisi okunamadı, kurulmadı."); }
            catch (ArgumentException) { return L.T("Güncelleme bilgisi okunamadı, kurulmadı."); }
            catch (IOException) { return L.T("Güncelleme dosyası yazılamadı."); }
            catch (UnauthorizedAccessException) { return L.T("Güncelleme dosyası yazılamadı."); }
        }

        // Sayaç çalışırken kurulmaz. Pencere açıksa kullanıcıya Şimdi veya Sonra diye sorulur.
        void TryInstall()
        {
            if (pendingUpdate == null || installing || mode != Mode.Setup) return;
            if (manualInstall) { InstallNow(); return; }
            if (updateLater || panel.Open) return;
            if (win.IsVisible && win.WindowState != WindowState.Minimized)
            {
                Find<TextBlock>("UpdateText").Text = L.T("Sürüm ") + pendingVersion + L.T(" indirildi. ") + pendingNotes;
                Find<FrameworkElement>("UpdateOverlay").Visibility = Visibility.Visible;
                return;
            }
            InstallNow();
        }

        async void InstallNow()
        {
            if (installing || pendingUpdate == null || mode != Mode.Setup) return;
            installing = true;
            try
            {
                settings.Save();
                stats.Save();
                Store.Backup();
                Updater.Replace(pendingUpdate);
                Store.Frozen = true;
                hotkeys.Clear();
                tray.Visible = false;
                win.Hide();
                mini.Hide();
                Program.ReleaseLock();
                var started = Updater.Launch("--guncellendi");
                bool healthy = await System.Threading.Tasks.Task.Run(() => Updater.WaitHealthy(started));
                if (!healthy)
                {
                    Updater.Rollback(started, pendingVersion);
                    Updater.Launch("--geri-alindi");
                }
                Exit();
            }
            catch (IOException) { installing = false; pendingUpdate = null; tray.ShowBalloonTip(4000, L.T("Güncelleme kurulamadı"), L.T("Uygulama olduğu gibi çalışmaya devam ediyor."), System.Windows.Forms.ToolTipIcon.None); }
            catch (UnauthorizedAccessException) { installing = false; pendingUpdate = null; tray.ShowBalloonTip(4000, L.T("Güncelleme kurulamadı"), L.T("Kor.exe yazılamıyor. Yazılabilir bir klasöre taşı."), System.Windows.Forms.ToolTipIcon.None); }
        }

        public bool Idle { get { return mode == Mode.Setup; } }

        public void Restart()
        {
            System.Diagnostics.Process.Start(Assembly.GetExecutingAssembly().Location, "--yeniden");
            Exit();
        }

        public void ShowRolledBack()
        {
            tray.ShowBalloonTip(5000, L.T("Güncelleme geri alındı"), L.T("Yeni sürüm açılamadı, önceki sürüme dönüldü. Ayarların yerinde."), System.Windows.Forms.ToolTipIcon.None);
        }

        public void ShowUpdated()
        {
            tray.ShowBalloonTip(4000, L.T("Kor güncellendi"), L.T("Sürüm ") + AppVersion.Current + L.T(" kuruldu. Ayarların yerinde."), System.Windows.Forms.ToolTipIcon.None);
        }
    }
}
