using System;
using System.Linq;

namespace Sayac
{
    // Arayüzden bağımsız kararlar. Testler bu sınıfı doğrudan çalıştırır.
    static class Logic
    {
        public static bool IsNewer(string remote, string current, string skipped)
        {
            if (remote == skipped) return false;
            return new Version(remote) > new Version(current);
        }

        // Kapalıyken geçen süre geri sayımdan düşülür, kronometreye eklenir.
        public static long RestoredValue(bool up, bool paused, long baseMs, long savedTicks, long nowTicks)
        {
            long away = paused ? 0 : (long)TimeSpan.FromTicks(nowTicks - savedTicks).TotalMilliseconds;
            return up ? baseMs + away : baseMs - away;
        }

        // Otomatik kaynak: Spotify çalıyorsa o, Spotify durmuşken tarayıcı çalıyorsa tarayıcı gösterilir.
        // İkisi de duruyorsa son kullanılan kaynak korunur.
        public static bool UseBrowser(bool apiConnected, bool apiHasTrack, bool apiPlaying, bool browserPlaying, bool usingBrowser)
        {
            if (!apiConnected || !apiHasTrack) return true;
            if (apiPlaying) return false;
            return browserPlaying || usingBrowser;
        }

        // Canlı ses: tarayıcı sık, Spotify API istek sınırı için seyrek güncellenir.
        public static bool ShouldSendVolume(double elapsedMs, bool browser) { return elapsedMs >= (browser ? 60 : 350); }

        // Sarma sonrası gelen konum hedefe ulaştıysa bekleme biter.
        public static bool SeekSettled(long position, long target) { return Math.Abs(position - target) < 1500; }

        // Kayıtlı seçim, sonra Windows dili, sonra İngilizce.
        public static string PickLanguage(string saved, string windows, string[] available)
        {
            if (!string.IsNullOrEmpty(saved) && available.Contains(saved)) return saved;
            return available.Contains(windows) ? windows : "en";
        }

        // Rehber kartı önce vurgunun yanına sığdırılır; okunmaz olacaksa vurgunun üstüne binebilir ama pencereden taşmaz.
        public static double GuideScale(double cardWidth, double cardHeight, double maxWidth, double maxHeight, double room)
        {
            if (cardWidth <= 0 || cardHeight <= 0) return 1;
            double whole = Math.Min(1, Math.Min(maxWidth / cardWidth, maxHeight / cardHeight));
            double beside = Math.Min(whole, room / cardHeight);
            return Math.Max(0.1, Math.Max(beside, Math.Min(0.75, whole)));
        }

        public static bool ValidClientId(string id)
        {
            return id != null && id.Length == 32 && id.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
        }

        public static long ShownSeconds(long leftMs) { return (leftMs + 999) / 1000; }

        public static string ClockText(int hours, int minutes, int seconds)
        {
            return (hours > 0 ? hours.ToString("00") + ":" : "") + minutes.ToString("00") + ":" + seconds.ToString("00");
        }

        public static int Wrap(int value, int delta, int size) { return (value + delta + size) % size; }

        public static int Clamp(int value, int low, int high) { return Math.Max(low, Math.Min(high, value)); }
    }
}
