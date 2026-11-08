using System;
using System.Linq;
using System.Windows;

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

        public const int DockRight = 1, DockLeft = 2, DockTop = 3;

        // Not, Kor'un kenarına yeterince yaklaştıysa hangi tarafa yapışacağını verir; 0 yapışmaz demektir.
        // Mini sayaçta yalnızca üst kenara yapışılır.
        public static int NoteSnap(Rect note, Rect host, bool mini, double reach)
        {
            if (mini)
                return note.Right > host.Left + 20 && note.Left < host.Right - 20 && Math.Abs(note.Bottom - host.Top) <= reach ? DockTop : 0;
            if (note.Bottom <= host.Top + 20 || note.Top >= host.Bottom - 20) return 0;
            if (Math.Abs(note.Left - host.Right) <= reach) return DockRight;
            if (Math.Abs(note.Right - host.Left) <= reach) return DockLeft;
            return 0;
        }

        // Yapışık notun yeri. Ekranda yer yoksa diğer tarafa geçer.
        // Boyut 0 ise Kor ile eşitlenir: yanda yükseklik, mini sayaç üstünde genişlik.
        public static Rect NoteDock(int side, Rect host, Size size, Rect screen)
        {
            if (side == DockTop)
            {
                var r = NoteDock(side, host, size.Height, screen);
                if (size.Width > 0) r.Width = size.Width;
                return r;
            }
            var s = NoteDock(side, host, size.Width, screen);
            if (size.Height > 0) s.Height = size.Height;
            return s;
        }

        public static Rect NoteDock(int side, Rect host, double size, Rect screen)
        {
            if (side == DockTop)
            {
                double top = host.Top - size;
                if (top < screen.Top) top = host.Bottom;
                return new Rect(host.Left, top, host.Width, size);
            }
            bool right = side == DockRight;
            if (right && host.Right + size > screen.Right) right = false;
            else if (!right && host.Left - size < screen.Left) right = true;
            return new Rect(right ? host.Right : host.Left - size, host.Top, size, host.Height);
        }

        public const string BoxOpen = "\u2610", BoxDone = "\u2611";

        // 0: kutusuz satır, 1: işaretsiz, 2: işaretli.
        public static int ChecklistState(string line)
        {
            if (string.IsNullOrEmpty(line)) return 0;
            if (line.StartsWith(BoxOpen)) return 1;
            return line.StartsWith(BoxDone) ? 2 : 0;
        }

        // Arama ad, başlık ve metinde büyük küçük harf ayırmadan yapılır.
        public static bool NoteMatches(string query, params string[] fields)
        {
            query = (query ?? "").Trim();
            if (query.Length == 0) return true;
            var culture = System.Globalization.CultureInfo.CurrentCulture.CompareInfo;
            return fields.Any(f => f != null && culture.IndexOf(f, query, System.Globalization.CompareOptions.IgnoreCase) >= 0);
        }

        // Extreme: seri günlük odakla artar, bir gün atlanırsa baştan başlar.
        public static int NextStreak(int streak, string lastDay, string today)
        {
            if (lastDay == today) return Math.Max(1, streak);
            DateTime last, now;
            if (DateTime.TryParse(lastDay, out last) && DateTime.TryParse(today, out now) && (now - last).TotalDays == 1) return streak + 1;
            return 1;
        }

        public static int Level(int xp) { return Math.Max(0, xp) / 100 + 1; }

        public static bool NoteIsEmpty(string text) { return string.IsNullOrWhiteSpace(text); }

        // Sekme adı notun ilk dolu satırıdır.
        public static string NoteTitle(string text)
        {
            string line = (text ?? "").Split('\n').Select(x => x.Trim().TrimStart(BoxOpen[0], BoxDone[0]).Trim()).FirstOrDefault(x => x.Length > 0) ?? "";
            return line.Length > 40 ? line.Substring(0, 40).TrimEnd() + "…" : line;
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
