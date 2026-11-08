using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Sayac
{
    static class TestRunner
    {
        static int failed, passed;

        static void Check(string name, bool ok)
        {
            if (ok) passed++;
            else { failed++; Console.WriteLine("HATA: " + name); }
        }

        static void Equal<T>(string name, T actual, T expected)
        {
            Check(name + " (beklenen " + expected + ", gelen " + actual + ")", Equals(actual, expected));
        }

        [STAThread]
        static int Main()
        {
            Versions();
            Times();
            Links();
            Keys();
            Storage();
            Stats();
            Signature();
            Bridge();
            Console.WriteLine(passed + " test geçti, " + failed + " hata");
            return failed == 0 ? 0 : 1;
        }

        static void Versions()
        {
            Check("yeni sürüm", Logic.IsNewer("1.0.2.2", "1.0.2.1", ""));
            Check("dört hane üç haneden yeni", Logic.IsNewer("1.0.1.1", "1.0.1", ""));
            Check("aynı sürüm yeni değil", !Logic.IsNewer("1.0.2.1", "1.0.2.1", ""));
            Check("eski sürüme inilmez", !Logic.IsNewer("1.0.1", "1.0.2", ""));
            Check("sayısal karşılaştırma", Logic.IsNewer("1.0.10", "1.0.9", ""));
            Check("geri alınan sürüm atlanır", !Logic.IsNewer("9.9.9", "1.0.0", "9.9.9"));
        }

        static void Times()
        {
            long now = new DateTime(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc).Ticks;
            long tenAgo = now - TimeSpan.FromSeconds(10).Ticks;
            Equal("geri sayım kapalıyken azalır", Logic.RestoredValue(false, false, 200000, tenAgo, now), 190000L);
            Equal("duraklatılmış değişmez", Logic.RestoredValue(false, true, 200000, tenAgo, now), 200000L);
            Equal("kronometre artar", Logic.RestoredValue(true, false, 5000, tenAgo, now), 15000L);
            Check("süresi dolan negatif olur", Logic.RestoredValue(false, false, 5000, tenAgo, now) <= 0);
            Equal("0,1 sn 1 gösterir", Logic.ShownSeconds(100), 1L);
            Equal("tam saniye", Logic.ShownSeconds(5000), 5L);
            Equal("sıfır", Logic.ShownSeconds(0), 0L);
            Equal("saat yoksa kısa", Logic.ClockText(0, 5, 3), "05:03");
            Equal("saat varsa uzun", Logic.ClockText(1, 2, 3), "01:02:03");
            Equal("dakika başa sarar", Logic.Wrap(59, 1, 60), 0);
            Equal("dakika geri sarar", Logic.Wrap(0, -1, 60), 59);
            Equal("sınır", Logic.Clamp(120, 1, 99), 99);
            Equal("süre metni", FocusPanel.Duration(3725), "1 sa 2 dk 5 sn");
        }

        static void Links()
        {
            Equal("liste bağlantısı", FocusPanel.Normalize("https://open.spotify.com/playlist/37i9dQ?si=abc"), "spotify:playlist:37i9dQ");
            Equal("dilli bağlantı", FocusPanel.Normalize("https://open.spotify.com/intl-tr/album/abc123"), "spotify:album:abc123");
            Equal("uri", FocusPanel.Normalize("spotify:artist:xyz"), "spotify:artist:xyz");
            Equal("boş", FocusPanel.Normalize("  "), "");
            Check("geçersiz", FocusPanel.Normalize("https://example.com/x") == null);
        }

        static void Keys()
        {
            var s = new Settings();
            s.KeysVersion = 0;
            s.Keys["next"] = Hotkeys.Pack(Hotkeys.Ctrl | Hotkeys.Alt, 39);
            s.Keys["toggle"] = Hotkeys.Pack(Hotkeys.Ctrl | Hotkeys.Alt, 32);
            s.Keys["music"] = Hotkeys.Pack(Hotkeys.Win, 80);
            Hotkeys.Migrate(s);
            Equal("eski varsayılan taşınır", s.Keys["next"], Hotkeys.Defaults()["next"]);
            Equal("eski boşluk taşınır", s.Keys["toggle"], Hotkeys.Defaults()["toggle"]);
            Equal("özel kısayol korunur", s.Keys["music"], Hotkeys.Pack(Hotkeys.Win, 80));
            Equal("sürüm işaretlenir", s.KeysVersion, 2);
            Check("varsayılanlarda AltGr yok", Hotkeys.Defaults().Values.All(v => v.StartsWith("7,")));
            Equal("kısayol metni", Hotkeys.Describe(Hotkeys.Pack(Hotkeys.Ctrl | Hotkeys.Shift, 83)), "Ctrl + Shift + S");
            Equal("kapalı kısayol", Hotkeys.Describe(""), "Kapalı");
        }

        static void Storage()
        {
            Directory.CreateDirectory(Store.Folder);
            var first = new Settings { DailyGoal = 111 };
            Store.Save("deneme.json", first);
            Equal("kayıt okunur", Store.Load<Settings>("deneme.json").DailyGoal, 111);
            Store.Save("deneme.json", new Settings { DailyGoal = 222 });
            File.WriteAllText(Path.Combine(Store.Folder, "deneme.json"), "{bozuk");
            Equal("bozuk dosyada yedek kullanılır", Store.Load<Settings>("deneme.json").DailyGoal, 111);
            Equal("olmayan dosya varsayılan verir", Store.Load<Settings>("yok.json").DailyGoal, 180);
            Store.Save("ayarlar.json", new Settings { DailyGoal = 333 });
            string file = Path.Combine(Store.Folder, "disari.json");
            Store.Export(file);
            Store.Save("ayarlar.json", new Settings { DailyGoal = 444 });
            Store.Import(file);
            Store.Frozen = false;
            Equal("içe aktarma", Store.Load<Settings>("ayarlar.json").DailyGoal, 333);
            File.WriteAllText(file, "{\"x\":\"y\"}");
            bool refused = false;
            try { Store.Import(file); }
            catch (InvalidOperationException) { refused = true; }
            Check("yabancı yedek reddedilir", refused);
        }

        static void Stats()
        {
            var stats = new FocusStats();
            stats.Add(120, "A", "spotify:track:a", null, "Ders");
            stats.Add(60, "B", null, null, "Ders");
            stats.Add(240, "B", "spotify:track:b", null, null);
            var today = stats.Day(DateTime.Now);
            Equal("toplam süre", today.Seconds, 420.0);
            Equal("konu süresi", today.Topics["Ders"], 180.0);
            var top = stats.Top(stats.Week());
            Equal("en çok dinlenen", top[0].Key, "B");
            Equal("şarkı süresi", top[0].Value, 300.0);
            Check("şarkı bağlantısı kaydedilir", stats.Refs["A"].Uri == "spotify:track:a");
        }

        static void Bridge()
        {
            Check("eklenti kaynağı", ConnectBridge.Authorized("chrome-extension://abc"));
            Check("web sitesi reddedilir", !ConnectBridge.Authorized("https://evil.example"));
            Check("kaynaksız istek reddedilir", !ConnectBridge.Authorized(null));
            Equal("WebSocket anahtarı", ConnectBridge.AcceptKey("dGhlIHNhbXBsZSBub25jZQ=="), "s3pPLMBiTxaQ9kYGzzhZRbK+xOo=");
            var small = ConnectBridge.EncodeFrame("abc");
            Check("kısa çerçeve", small[0] == 0x81 && small[1] == 3 && small[2] == (byte)'a');
            var large = ConnectBridge.EncodeFrame(new string('x', 300));
            Check("uzun çerçeve", large[1] == 126 && large[2] == 1 && large[3] == 44);
            var bridge = new ConnectBridge(x => true);
            Check("ilk sorun bildirilir", bridge.SetProblem("youtube-music", "yok"));
            Check("aynı sorun tekrar bildirilmez", !bridge.SetProblem("youtube-music", "yok"));
            Equal("sorun listede", bridge.Problems.Length, 1);
            bridge.SetProblem("youtube-music", "");
            Equal("düzelen sorun düşer", bridge.Problems.Length, 0);
            var owner = new object();
            bridge.SetProblem("diger-kaynak", "yok", owner, 5);
            bridge.ClearProblems(owner, 9);
            Equal("başka sekme sorunu silmez", bridge.Problems.Length, 1);
            bridge.ClearProblems(owner, 5);
            Equal("sekme kapanınca sorun silinir", bridge.Problems.Length, 0);
            bridge.SetProblem("diger-kaynak", "yok", owner, 5);
            bridge.ClearProblems(owner);
            Equal("bağlantı kopunca sorun silinir", bridge.Problems.Length, 0);
            Check("eski protokol güncelleme ister", ConnectBridge.NeedsUpdate(0, "9.0.0", "2.1.0"));
            Check("eski sürüm güncelleme ister", ConnectBridge.NeedsUpdate(1, "2.0.0", "2.1.0"));
            Check("güncel sürüm uyarmaz", !ConnectBridge.NeedsUpdate(1, "2.1.0", "2.1.0"));
            Equal("eklenti sürümü", ConnectBridge.BundledVersion(), "2.2.4");
            Check("Spotify API yoksa tarayıcı", Logic.UseBrowser(false, false, false, false, false));
            Check("Spotify şarkısı yoksa tarayıcı", Logic.UseBrowser(true, false, false, false, false));
            Check("Spotify çalıyorsa Spotify", !Logic.UseBrowser(true, true, true, true, true));
            Check("Spotify durmuş tarayıcı çalıyorsa tarayıcı", Logic.UseBrowser(true, true, false, true, false));
            Check("ikisi de durmuşsa son kaynak kalır", Logic.UseBrowser(true, true, false, false, true));
            Check("ikisi de durmuşsa Spotify kalır", !Logic.UseBrowser(true, true, false, false, false));
            Check("tarayıcı sesi sık gönderilir", Logic.ShouldSendVolume(70, true));
            Check("Spotify sesi sık gönderilmez", !Logic.ShouldSendVolume(70, false));
            Check("Spotify sesi aralıkla gönderilir", Logic.ShouldSendVolume(400, false));
            Check("hedefe yakın sarma biter", Logic.SeekSettled(61000, 60000));
            Check("uzak konum beklenir", !Logic.SeekSettled(30000, 60000));
            Check("geçerli durum", ConnectBridge.ValidState(new Dictionary<string, object> { { "playing", true }, { "title", "A" }, { "position", 5 } }));
            Check("eksik durum reddedilir", !ConnectBridge.ValidState(new Dictionary<string, object> { { "title", "A" } }));
            Check("yanlış tür reddedilir", !ConnectBridge.ValidState(new Dictionary<string, object> { { "playing", "evet" }, { "title", "A" } }));
            Check("yanlış sayı reddedilir", !ConnectBridge.ValidState(new Dictionary<string, object> { { "playing", true }, { "title", "A" }, { "position", "x" } }));
            Equal("Türkçe Windows", "tr", Logic.PickLanguage(null, "tr", new[] { "tr", "en" }));
            Equal("diğer Windows dilleri", "en", Logic.PickLanguage("", "fr", new[] { "tr", "en" }));
            Equal("kayıtlı dil önceliklidir", "en", Logic.PickLanguage("en", "tr", new[] { "tr", "en" }));
            Equal("yeni dil dosyası kendiliğinden seçilir", "de", Logic.PickLanguage(null, "de", new[] { "tr", "en", "de" }));
            L.Use("en");
            Equal("çeviri", "Start", L.T("Başlat"));
            Equal("boşluklar korunur", "5 min", "5" + L.T(" dk"));
            Equal("eksik çeviri Türkçe kalır", "Yok böyle", L.T("Yok böyle"));
            Equal("biçimli çeviri", "Finish after 3 songs", L.F("{0} şarkı sonra bitir", 3));
            Check("XAML çevrilir", L.Xaml("<a Content=\"Başlat\"/>").Contains("Content=\"Start\""));
            Check("dil dosyası bulunur", L.Available().Any(x => x.Key == "en" && x.Value == "English"));
            L.Use("tr");
            Equal("Türkçe metin değişmez", "Başlat", L.T("Başlat"));
            Equal("rehber kartı yer varsa küçülmez", 1.0, Logic.GuideScale(340, 300, 600, 800, 400));
            Equal("rehber kartı vurgunun yanına sığar", 0.8, Logic.GuideScale(340, 300, 600, 800, 240));
            Equal("okunurluk korunur", 0.75, Logic.GuideScale(340, 300, 600, 800, 100));
            Check("kart pencereden taşmaz", Logic.GuideScale(340, 300, 200, 150, 50) * 300 <= 150);
            var host = new System.Windows.Rect(100, 100, 400, 800);
            Equal("not sağa yapışır", Logic.DockRight, Logic.NoteSnap(new System.Windows.Rect(510, 150, 300, 400), host, false, 28));
            Equal("not sola yapışır", Logic.DockLeft, Logic.NoteSnap(new System.Windows.Rect(-190, 150, 300, 400), host, false, 28));
            Equal("uzaktaki not yapışmaz", 0, Logic.NoteSnap(new System.Windows.Rect(600, 150, 300, 400), host, false, 28));
            Equal("mini sayaçta yan yapışma yok", 0, Logic.NoteSnap(new System.Windows.Rect(510, 150, 300, 400), host, true, 28));
            Equal("mini sayacın üstüne yapışır", Logic.DockTop, Logic.NoteSnap(new System.Windows.Rect(150, -195, 300, 300), host, true, 28));
            var screen = new System.Windows.Rect(0, 0, 1920, 1080);
            Equal("sağda yer varsa sağda durur", 500.0, Logic.NoteDock(Logic.DockRight, host, 300, screen).X);
            Equal("sağda yer yoksa sola geçer", 1300.0, Logic.NoteDock(Logic.DockRight, new System.Windows.Rect(1600, 100, 300, 800), 300, screen).X);
            Equal("yapışık not Kor ile aynı boyda", 800.0, Logic.NoteDock(Logic.DockLeft, host, 300, screen).Height);
            Equal("seçilen yükseklik korunur", 500.0, Logic.NoteDock(Logic.DockRight, host, new System.Windows.Size(300, 500), screen).Height);
            Equal("yükseklik seçilmediyse Kor kadar", 800.0, Logic.NoteDock(Logic.DockRight, host, new System.Windows.Size(300, 0), screen).Height);
            Equal("mini üstünde seçilen genişlik korunur", 260.0, Logic.NoteDock(Logic.DockTop, host, new System.Windows.Size(260, 200), screen).Width);
            Equal("mini üstünde genişlik eşitlenir", 400.0, Logic.NoteDock(Logic.DockTop, host, 200, screen).Width);
            Equal("not başlığı ilk dolu satır", "Alışveriş", Logic.NoteTitle("\n  Alışveriş  \nsüt"));
            Check("boş not boştur", Logic.NoteIsEmpty(" \r\n "));
            Equal("kutusuz satır", 0, Logic.ChecklistState("Süt al"));
            Equal("işaretsiz kutu", 1, Logic.ChecklistState(Logic.BoxOpen + " Süt al"));
            Equal("işaretli kutu", 2, Logic.ChecklistState(Logic.BoxDone + " Süt al"));
            Equal("başlıkta kutu görünmez", "Süt al", Logic.NoteTitle(Logic.BoxOpen + " Süt al"));
            Check("arama büyük küçük harf ayırmaz", Logic.NoteMatches("RAPOR", null, "Bugün", "rapor taslağı"));
            Check("boş arama hepsini gösterir", Logic.NoteMatches(" ", "x"));
            Check("eşleşmeyen arama", !Logic.NoteMatches("kahve", "Plan", "rapor"));
            Theme.Use("lavanta");
            Equal("tema altını lilaya çevirir", "#C9B6F2", "#" + Theme.Map(System.Windows.Media.Color.FromRgb(0xD8, 0xC4, 0x9A)).ToString().Substring(3));
            Check("tema şeffaflığı korur", Theme.Xaml("<a Fill=\"#8CE6B84A\"/>").Contains("#8CB48CF0"));
            Check("bilinmeyen renk değişmez", Theme.Xaml("<a Fill=\"#26FFFFFF\"/>").Contains("#26FFFFFF"));
            Theme.Use("yok");
            Equal("bilinmeyen tema Kor olur", "kor", Theme.Name);
            Equal("Kor teması renk değiştirmez", "#D8C49A", "#" + Theme.Map(System.Windows.Media.Color.FromRgb(0xD8, 0xC4, 0x9A)).ToString().Substring(3));
            Equal("seri dün varsa artar", 5, Logic.NextStreak(4, "2026-11-07", "2026-11-08"));
            Equal("seri aynı gün değişmez", 4, Logic.NextStreak(4, "2026-11-08", "2026-11-08"));
            Equal("ara verilince seri baştan", 1, Logic.NextStreak(4, "2026-11-05", "2026-11-08"));
            Equal("seviye", 4, Logic.Level(340));
            Check("geçerli Spotify kimliği", Logic.ValidClientId("0123456789abcdef0123456789abcdef"));
            Check("kısa Spotify kimliği reddedilir", !Logic.ValidClientId("abc"));
            Check("yanlış karakter reddedilir", !Logic.ValidClientId("z0000000000000000000000000000000"));
            Equal("kaynak adı", ConnectBridge.SourceName("youtube-music"), "YouTube Music");
            string folder = ConnectBridge.ExtractExtension();
            Check("eklenti dosyaları çıkarılır", ConnectBridge.Files.All(f => File.Exists(Path.Combine(folder, f.Replace('/', Path.DirectorySeparatorChar)))));
            string installedManifest = Path.Combine(folder, "manifest.json");
            File.WriteAllText(installedManifest, "{\"version\":\"1.0.0\"}");
            Check("eski klasör yenilenir", ConnectBridge.RefreshExtensionFiles());
            Check("güncel klasör yenilenmez", !ConnectBridge.RefreshExtensionFiles());
            Check("eklenti Spotify sitesine erişmez", !File.ReadAllText(Path.Combine(folder, "manifest.json")).Contains("open.spotify.com"));
            Check("eklenti adı", File.ReadAllText(Path.Combine(folder, "manifest.json")).Contains("TalverinConnect"));
        }

        static void Signature()
        {
            using (var rsa = new RSACryptoServiceProvider(2048))
            using (var other = new RSACryptoServiceProvider(2048))
            using (var sha = SHA256.Create())
            {
                rsa.PersistKeyInCsp = other.PersistKeyInCsp = false;
                var data = Encoding.UTF8.GetBytes("{\"version\":\"1.0.0\"}");
                var signature = rsa.SignData(data, sha);
                Check("doğru imza kabul edilir", Updater.SignatureOk(data, signature, rsa.ToXmlString(false)));
                Check("yabancı anahtar reddedilir", !Updater.SignatureOk(data, signature, other.ToXmlString(false)));
                var changed = Encoding.UTF8.GetBytes("{\"version\":\"9.0.0\"}");
                Check("değişen dosya reddedilir", !Updater.SignatureOk(changed, signature, rsa.ToXmlString(false)));
            }
        }
    }
}
