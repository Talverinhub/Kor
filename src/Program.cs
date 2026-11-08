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
    static class Program
    {
        public static T Load<T>(string name) where T : class
        {
            using (var xaml = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            using (var reader = new System.IO.StreamReader(xaml, System.Text.Encoding.UTF8))
                return (T)XamlReader.Parse(L.Xaml(Theme.Xaml(reader.ReadToEnd())));
        }

        const long MaxLogBytes = 200000;

        // Beklenmeyen hatalar yalnızca bu bilgisayardaki günlüğe yazılır.
        public static void LogError(Exception error)
        {
            try
            {
                Directory.CreateDirectory(Store.Folder);
                string path = Path.Combine(Store.Folder, "hata.log");
                if (File.Exists(path) && new FileInfo(path).Length > MaxLogBytes) File.Delete(path);
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + AppVersion.Current + " " + error + Environment.NewLine + Environment.NewLine);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        static System.Threading.Mutex appLock;

        public static void ReleaseLock()
        {
            if (appLock == null) return;
            appLock.ReleaseMutex();
            appLock = null;
        }

        [STAThread]
        static void Main(string[] args)
        {
            // Tek kopya çalışır; ikinci açılış var olan pencereyi öne getirir.
            bool separate = args.Length > 0 && (args[0] == "--start" || args[0] == "--kart");
            bool first;
            string tag = Environment.GetEnvironmentVariable("SAYAC_VERI") == null ? "" : "Deneme";
            var mutex = new System.Threading.Mutex(true, "SayacUygulamasi" + tag, out first);
            var wake = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, "SayacGoster" + tag);
            bool updated = args.Length > 0 && args[0] == "--guncellendi";
            bool rolledBack = args.Length > 0 && args[0] == "--geri-alindi";
            if (!first && (updated || rolledBack || (args.Length > 0 && args[0] == "--yeniden")))
            {
                // Eski kopyanın kapanması beklenir.
                try { first = mutex.WaitOne(15000); }
                catch (System.Threading.AbandonedMutexException) { first = true; }
            }
            if (!first && !separate) { wake.Set(); return; }
            if (first) appLock = mutex;
            if (first) Startup.Refresh();

            AppContext.SetSwitch("Switch.System.Windows.DoNotScaleForDpiChanges", false);
            var app = new Application();
            // Arayüzdeki beklenmeyen hata uygulamayı kapatmaz, sayaç çalışmaya devam eder.
            app.DispatcherUnhandledException += (a, b) => { LogError(b.Exception); b.Handled = true; };
            AppDomain.CurrentDomain.UnhandledException += (a, b) => { var error = b.ExceptionObject as Exception; if (error != null) LogError(error); };
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (a, b) => { LogError(b.Exception); b.SetObserved(); };
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            var startup = Store.Load<Settings>("ayarlar.json");
            L.Use(L.Pick(startup.Language));
            Theme.Use(startup.Theme);
            var win = Load<Window>("ui.xaml");
            app.MainWindow = win;
            var controller = new Controller(win);
            // Hızlı deneme için: Kor.exe --start 5
            if (args.Length == 2 && args[0] == "--start")
            {
                controller.SetSeconds(int.Parse(args[1]));
                win.Loaded += (a, b) => controller.Start();
            }
            if (args.Length == 2 && args[0] == "--kart")
            {
                var stats = Store.Load<FocusStats>("odak.json");
                win.Loaded += async (a, b) => { await FocusCard.Save(stats, args[1]); controller.Exit(); };
            }
            if (!separate)
            {
                var listener = new System.Threading.Thread(() =>
                {
                    while (true) { wake.WaitOne(); win.Dispatcher.BeginInvoke(new Action(controller.Expand)); }
                });
                listener.IsBackground = true;
                listener.Start();
            }
            win.Loaded += (a, b) => controller.ShowIntro();
            if (!separate)
            {
                if (!updated) Updater.Clean();
                controller.RestoreRun();
                controller.RestorePendingVolume();
                controller.StartUpdateCheck();
            }
            if (updated)
            {
                controller.ShowUpdated();
                win.Loaded += async (a, b) =>
                {
                    await System.Threading.Tasks.Task.Delay(1500);
                    Updater.SignalHealthy();
                    await System.Threading.Tasks.Task.Delay(8000);
                    Updater.Clean();
                };
            }
            if (rolledBack) controller.ShowRolledBack();
            bool hidden = args.Length > 0 && args[0] == "--tepsi";
            if (hidden) { controller.StartInTray(); app.Run(); }
            else app.Run(win);
            GC.KeepAlive(mutex);
        }
    }
}
