using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Path = System.IO.Path;

namespace Sayac
{
    class SpotifyResult
    {
        public HttpStatusCode Status;
        public string Body;
        public int RetryAfter;
        public bool Ok { get { return (int)Status >= 200 && (int)Status < 300; } }
    }

    // Spotify oturumu (PKCE: uygulamada gizli anahtar tutulmaz) ve Web API çağrıları.
    class SpotifyApi
    {
        // Yerleşik kimlik kaynak kodda durmaz; varsa derleme sırasında spotify-kimlik.txt dosyasından eklenir.
        static readonly string DefaultClientId = ReadBuiltInId();

        static string ReadBuiltInId()
        {
            using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("spotify-kimlik.txt"))
            {
                if (stream == null) return null;
                using (var reader = new StreamReader(stream))
                {
                    string id = reader.ReadToEnd().Trim();
                    return Logic.ValidClientId(id) ? id : null;
                }
            }
        }

        public static Func<string> ConfiguredId = () => "";

        // Girilen kimlik yerleşik olanın önüne geçer.
        string ClientId
        {
            get
            {
                string configured = ConfiguredId();
                return string.IsNullOrEmpty(configured) ? DefaultClientId : configured;
            }
        }
        const int Port = 8888;
        const string Redirect = "http://127.0.0.1:8888/callback";
        const string Scopes = "user-read-playback-state user-modify-playback-state user-library-read user-library-modify playlist-modify-private";

        readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        readonly string tokenPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sayac", "spotify.dat");
        string accessToken, refreshToken;
        DateTime expires = DateTime.MinValue;

        public SpotifyApi()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            try
            {
                if (File.Exists(tokenPath))
                    refreshToken = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(tokenPath), null, DataProtectionScope.CurrentUser));
            }
            catch (CryptographicException) { }
            catch (IOException) { }
        }

        public bool Connected { get { return refreshToken != null; } }
        string scope;
        public bool HasExtras { get { return scope != null && scope.Contains("user-library-modify") && scope.Contains("playlist-modify-private"); } }

        public void Disconnect()
        {
            accessToken = refreshToken = null;
            try { File.Delete(tokenPath); }
            catch (IOException) { }
        }

        static string Base64Url(byte[] data)
        {
            return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        static string RandomText(int bytes)
        {
            var data = new byte[bytes];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(data);
            return Base64Url(data);
        }

        public async Task Login()
        {
            if (ClientId == null) throw new InvalidOperationException(L.T("Önce Ayarlar'daki Spotify bağlantısı alanına Client ID gir."));
            string verifier = RandomText(64), state = RandomText(16), challenge;
            using (var sha = SHA256.Create()) challenge = Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));

            var listener = new TcpListener(IPAddress.Loopback, Port);
            try { listener.Start(); }
            catch (SocketException) { throw new InvalidOperationException(L.T("8888 bağlantı noktası kullanımda. Onu kullanan programı kapatıp yeniden dene.")); }
            var result = new TaskCompletionSource<Dictionary<string, string>>();
            try
            {
                var accepting = AcceptLoop(listener, result);
                Process.Start("https://accounts.spotify.com/authorize?response_type=code&client_id=" + ClientId
                    + "&scope=" + Uri.EscapeDataString(Scopes) + "&redirect_uri=" + Uri.EscapeDataString(Redirect)
                    + "&state=" + state + "&code_challenge_method=S256&code_challenge=" + challenge);
                var first = await Task.WhenAny(result.Task, Task.Delay(TimeSpan.FromMinutes(3)));
                if (first != result.Task) throw new InvalidOperationException(L.T("İzin verilmedi, süre doldu. Yeniden dene."));
            }
            finally { listener.Stop(); }

            var query = result.Task.Result;
            string code;
            if (!query.ContainsKey("state") || query["state"] != state || !query.TryGetValue("code", out code))
                throw new InvalidOperationException(L.T("Spotify izni tamamlanmadı. Yeniden dene."));
            await RequestToken(new Dictionary<string, string> {
                { "grant_type", "authorization_code" }, { "code", code }, { "redirect_uri", Redirect },
                { "client_id", ClientId }, { "code_verifier", verifier } });
        }

        static async Task AcceptLoop(TcpListener listener, TaskCompletionSource<Dictionary<string, string>> result)
        {
            try
            {
                while (!result.Task.IsCompleted)
                {
                    var client = await listener.AcceptTcpClientAsync();
                    var ignored = Serve(client, result);
                }
            }
            catch (ObjectDisposedException) { }
            catch (SocketException) { }
        }

        // Tarayıcılar boş bağlantı da açabildiği için her bağlantı ayrı okunur.
        static async Task Serve(TcpClient client, TaskCompletionSource<Dictionary<string, string>> result)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    var reader = new StreamReader(stream, Encoding.ASCII);
                    string line = await reader.ReadLineAsync();
                    if (line == null) return;
                    var parts = line.Split(' ');
                    bool callback = parts.Length > 1 && parts[1].StartsWith("/callback");
                    string html = "<!doctype html><meta charset=utf-8><body style='background:#000;color:#D8C49A;font:20px sans-serif;text-align:center;padding-top:20vh'>"
                        + (callback ? L.T("Spotify bağlandı. Bu sekmeyi kapatabilirsin.") : "") + "</body>";
                    var body = Encoding.UTF8.GetBytes(html);
                    var head = Encoding.ASCII.GetBytes("HTTP/1.1 " + (callback ? "200 OK" : "404 Not Found") + "\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(head, 0, head.Length);
                    await stream.WriteAsync(body, 0, body.Length);
                    if (!callback) return;
                    var query = new Dictionary<string, string>();
                    int mark = parts[1].IndexOf('?');
                    if (mark >= 0)
                        foreach (var pair in parts[1].Substring(mark + 1).Split('&'))
                        {
                            var kv = pair.Split(new[] { '=' }, 2);
                            if (kv.Length == 2) query[kv[0]] = Uri.UnescapeDataString(kv[1]);
                        }
                    result.TrySetResult(query);
                }
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }

        async Task RequestToken(Dictionary<string, string> form)
        {
            var response = await http.PostAsync("https://accounts.spotify.com/api/token", new FormUrlEncodedContent(form));
            string text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.BadRequest && form["grant_type"] == "refresh_token") Disconnect();
                throw new InvalidOperationException(L.T("Spotify oturumu açılamadı. Yeniden bağlan."));
            }
            var json = SpotifyPanel.Parse(text);
            accessToken = (string)json["access_token"];
            expires = DateTime.UtcNow.AddSeconds(Convert.ToInt32(json["expires_in"]) - 60);
            if (json.ContainsKey("refresh_token")) refreshToken = (string)json["refresh_token"];
            if (json.ContainsKey("scope")) scope = json["scope"] as string;
            Directory.CreateDirectory(Path.GetDirectoryName(tokenPath));
            File.WriteAllBytes(tokenPath, ProtectedData.Protect(Encoding.UTF8.GetBytes(refreshToken), null, DataProtectionScope.CurrentUser));
        }

        Task Refresh()
        {
            return RequestToken(new Dictionary<string, string> {
                { "grant_type", "refresh_token" }, { "refresh_token", refreshToken }, { "client_id", ClientId } });
        }

        public async Task<SpotifyResult> Send(HttpMethod method, string path, string json = null)
        {
            if (accessToken == null || DateTime.UtcNow >= expires) await Refresh();
            var answer = await SendOnce(method, path, json);
            if (answer.Status == HttpStatusCode.Unauthorized)
            {
                await Refresh();
                answer = await SendOnce(method, path, json);
            }
            return answer;
        }

        async Task<SpotifyResult> SendOnce(HttpMethod method, string path, string json)
        {
            string root = path.StartsWith("/me/") || path.StartsWith("/playlists/") ? "https://api.spotify.com/v1" : "https://api.spotify.com/v1/me/player";
            var request = new HttpRequestMessage(method, root + path);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            if (method != HttpMethod.Get)
                request.Content = json == null ? (HttpContent)new ByteArrayContent(new byte[0]) : new StringContent(json, Encoding.UTF8, "application/json");
            var response = await http.SendAsync(request);
            var retry = response.Headers.RetryAfter;
            return new SpotifyResult
            {
                Status = response.StatusCode,
                Body = await response.Content.ReadAsStringAsync(),
                RetryAfter = retry != null && retry.Delta.HasValue ? (int)retry.Delta.Value.TotalSeconds : 5
            };
        }
    }

    // Aynı kontrollerin bir penceredeki kopyası (büyük pencere veya mini sayaç).
    class SpotifyView
    {
        public readonly Button Connect, Shuffle, Prev, Play, Next, Repeat, Like;
        public readonly FrameworkElement Player;
        public readonly Image Cover;
        public readonly TextBlock Title, Artist, Pos, Dur, RepeatOne;
        public readonly Slider Seek, Volume;
        public readonly System.Windows.Shapes.Path PlayIcon, ShuffleIcon, RepeatIcon, LikeIcon;

        public SpotifyView(Window w)
        {
            Connect = (Button)w.FindName("SpConnect"); Player = (FrameworkElement)w.FindName("SpPlayer");
            Shuffle = (Button)w.FindName("SpShuffle"); Prev = (Button)w.FindName("SpPrev"); Play = (Button)w.FindName("SpPlay");
            Next = (Button)w.FindName("SpNext"); Repeat = (Button)w.FindName("SpRepeat");
            Like = (Button)w.FindName("SpLike");
            LikeIcon = (System.Windows.Shapes.Path)w.FindName("SpLikeIcon");
            Cover = (Image)w.FindName("SpCover");
            Title = (TextBlock)w.FindName("SpTitle"); Artist = (TextBlock)w.FindName("SpArtist");
            Pos = (TextBlock)w.FindName("SpPos"); Dur = (TextBlock)w.FindName("SpDur"); RepeatOne = (TextBlock)w.FindName("SpRepeatOne");
            Seek = (Slider)w.FindName("SpSeek"); Volume = (Slider)w.FindName("SpVolume");
            PlayIcon = (System.Windows.Shapes.Path)w.FindName("SpPlayIcon");
            ShuffleIcon = (System.Windows.Shapes.Path)w.FindName("SpShuffleIcon");
            RepeatIcon = (System.Windows.Shapes.Path)w.FindName("SpRepeatIcon");
        }
    }

    // Spotify çubuğunun arayüz tarafı.
    class SpotifyPanel
    {
        const int PollEveryTicks = 12, IdlePollTicks = 48; // 250 ms adımlarla: çalarken 3, dururken 12 saniye
        static readonly string Offline = L.T("Spotify'a ulaşılamıyor. İnternetini kontrol et, yeniden deneniyor.");
        static readonly Geometry PlayGeo = Geometry.Parse("M0,0 L10,6 L0,12 Z");
        static readonly Geometry PauseGeo = Geometry.Parse("M0,0 H3.5 V12 H0 Z M6.5,0 H10 V12 H6.5 Z");
        static readonly Brush Gold = new SolidColorBrush(Theme.Map(Color.FromRgb(0xD8, 0xC4, 0x9A)));
        static readonly Brush Quiet = new SolidColorBrush(Theme.Map(Color.FromRgb(0x8E, 0x8A, 0x82)));

        readonly Window win, mini;
        readonly SpotifyApi api = new SpotifyApi();
        readonly SpotifyView[] views;
        readonly DispatcherTimer timer;
        readonly Stopwatch sincePoll = new Stopwatch();

        bool playing, shuffle, polling, volumeDrag, connecting;
        Slider seeking;
        string repeat = "off", coverUrl, message;
        long progress, duration;
        int ticks;
        DateTime skipUntil = DateTime.MinValue;

        public bool Playing { get { return IsConnected && playing; } }
        public string Track { get; private set; }
        public string ContextUri { get; private set; }
        public int? Volume { get; private set; }
        public Color? Accent { get; private set; }
        public Action AccentChanged;
        public Action<bool> PlayingChanged;
        public Action TrackChanged;
        public long SongLeft { get { return Math.Max(0, duration - Current()); } }

        // Uygulama kapanırken sesin eski düzeye dönmesi için yanıt beklenir.
        public void SetVolumeNow(int percent)
        {
            if (!api.Connected) return;
            try { Task.Run(() => api.Send(HttpMethod.Put, "/volume?volume_percent=" + percent)).Wait(2000); }
            catch (AggregateException) { }
        }
        bool announced;
        DateTime holdUntil = DateTime.MinValue;

        public void SetMusic(bool on)
        {
            if (on != playing && Track != null) TogglePlay();
        }

        void AnnouncePlaying()
        {
            if (playing == announced) return;
            announced = playing;
            if (PlayingChanged != null) PlayingChanged(playing);
        }
        public string TrackUri { get; private set; }
        public string CoverUrl { get { return coverUrl; } }
        public bool IsConnected { get { return api.Connected || External; } }
        public ConnectBridge Bridge;
        public string SoundProcess { get; private set; }
        string externalCover;
        Dictionary<string, object> lastExternal;
        bool wasExternal;

        public Func<int> SourceMode = () => 0;
        bool apiHasTrack, apiPlaying;
        DateTime seekHold = DateTime.MinValue, volumeHold = DateTime.MinValue, skipUntil2 = DateTime.MinValue, lastVolumeSent = DateTime.MinValue;
        long seekTarget;
        string skipFrom;
        const double SeekHoldSeconds = 3, SkipHoldSeconds = 3;

        void BeginSkip()
        {
            skipFrom = TrackUri;
            skipUntil2 = DateTime.UtcNow.AddSeconds(SkipHoldSeconds);
            SetArtist(L.T("Geçiliyor"));
        }

        // Şarkı değişene kadar eski ad durur, altında geçiş yazısı görünür.
        void ShowSkip()
        {
            if (DateTime.UtcNow < skipUntil2 && TrackUri == skipFrom) SetArtist(L.T("Geçiliyor"));
            else skipUntil2 = DateTime.MinValue;
        }

        // Yeni konum site cevap verene kadar ekranda sabit kalır.
        bool HoldSeek(long position)
        {
            if (DateTime.UtcNow >= seekHold) return false;
            if (!Logic.SeekSettled(position, seekTarget)) return true;
            seekHold = DateTime.MinValue;
            return false;
        }

        // Eklentiden yeni durum gelince sıradaki zamanlayıcıyı beklemeden ekrana yansıtır.
        public void RefreshNow() { win.Dispatcher.BeginInvoke(new Action(Tick), DispatcherPriority.Send); }

        // Otomatik kaynakta Spotify API önceliklidir; bağlı değilse veya çalan yoksa eklenti devreye girer.
        bool External { get { return ExternalState() != null; } }

        // Durum tek seferde okunur; sekme o anda kapanırsa boş değerle çökmez.
        Dictionary<string, object> ExternalState()
        {
            var state = Bridge == null ? null : Bridge.Fresh();
            if (state == null) return null;
            int source = SourceMode();
            if (source == 1) return null;
            bool use = source == 2 || Logic.UseBrowser(api.Connected, apiHasTrack, apiPlaying, (bool)state["playing"], wasExternal);
            return use ? state : null;
        }

        public void WatchBridge()
        {
            if (Bridge != null && Bridge.Running) timer.Start();
            else ShowConnection();
        }

        static long Number(Dictionary<string, object> s, string key)
        {
            object value;
            return s.TryGetValue(key, out value) && value != null ? Convert.ToInt64(value) : 0;
        }

        static string Text(Dictionary<string, object> s, string key)
        {
            object value;
            return s.TryGetValue(key, out value) ? value as string ?? "" : "";
        }

        void ShowExternal(Dictionary<string, object> s)
        {
            string title = Text(s, "title"), people = Text(s, "artist");
            SetTitle(title);
            SetArtist(people);
            // Komuttan hemen sonra gelen eski durum, düğmeyi geri zıplatmasın.
            if (DateTime.UtcNow >= holdUntil) playing = (bool)s["playing"];
            foreach (var v in views) v.PlayIcon.Data = playing ? PauseGeo : PlayGeo;
            AnnouncePlaying();
            // Süre yalnızca yeni durum gelince düzeltilir, arada uygulama kendisi ilerletir.
            bool fresh = !ReferenceEquals(s, lastExternal);
            lastExternal = s;
            if (fresh && seeking == null && !HoldSeek(Number(s, "position"))) { progress = Number(s, "position"); sincePoll.Restart(); }
            duration = Number(s, "duration");
            string mode = Text(s, "repeat");
            repeat = mode == "ONE" ? "track" : mode == "ALL" ? "context" : "off";
            var caps = s.ContainsKey("capabilities") ? s["capabilities"] as object[] : null;
            Func<string, bool> can = name => caps == null || caps.Contains(name);
            canShuffle = can("shuffle");
            canRepeat = can("repeat");
            if (s.ContainsKey("shuffle") && s["shuffle"] is bool) shuffle = (bool)s["shuffle"];
            ShowModes();
            foreach (var v in views)
            {
                v.Like.Opacity = can("like") ? 1 : 0.35;
                v.Like.IsEnabled = can("like");
                v.Seek.IsEnabled = can("seek");
            }
            liked = s.ContainsKey("liked") && (bool)s["liked"];
            ShowLike();
            string uri = "youtube:" + title + "/" + people;
            if (uri != TrackUri)
            {
                bool changed = TrackUri != null;
                TrackUri = uri;
                if (changed && TrackChanged != null) TrackChanged();
            }
            ShowSkip();
            Track = title + (people.Length > 0 ? ", " + people : "");
            ContextUri = null;
            SoundProcess = Bridge.Browser;
            if (!volumeDrag && DateTime.UtcNow >= volumeHold)
            {
                Volume = (int)Number(s, "volume");
                foreach (var v in views) { v.Volume.IsEnabled = true; v.Volume.Value = Volume.Value; }
            }
            string cover = Text(s, "cover");
            if (cover != externalCover && cover.StartsWith("http"))
            {
                externalCover = cover;
                coverUrl = cover;
                var image = new BitmapImage(new Uri(cover));
                foreach (var v in views) v.Cover.Source = image;
                if (image.IsDownloading) image.DownloadCompleted += (a, b) => { if (cover == coverUrl) SetAccent(Dominant(image)); };
                else SetAccent(Dominant(image));
            }
        }

        // Spotify komutlarının eklentideki karşılığı.
        bool SendExternal(string path)
        {
            if (!External) return false;
            if (path == "/pause" || path == "/play") Bridge.Send("toggle");
            else if (path == "/next") Bridge.Send("next");
            else if (path == "/previous") Bridge.Send("previous");
            else if (path.StartsWith("/shuffle")) Bridge.Send("shuffle");
            else if (path.StartsWith("/repeat")) Bridge.Send("repeat");
            else if (path.StartsWith("/seek"))
            {
                Bridge.Send("seek:" + path.Substring(path.IndexOf('=') + 1));
            }
            else if (path.StartsWith("/volume"))
            {
                volumeHold = DateTime.UtcNow.AddSeconds(1.2);
                Bridge.Send("volume:" + path.Substring(path.IndexOf('=') + 1));
            }
            return true;
        }
        string deviceId;
        bool canShuffle = true, canRepeat = true, liked;
        static readonly string NoModes = L.T("Bu çalma kaynağında Spotify buna izin vermiyor. Bir liste veya albümden çal.");

        void ShowLike()
        {
            foreach (var v in views) v.LikeIcon.Fill = liked ? Gold : Brushes.Transparent;
            foreach (var v in views) v.LikeIcon.Stroke = liked ? Gold : Quiet;
        }

        async Task CheckLike()
        {
            string uri = TrackUri;
            liked = false;
            ShowLike();
            if (uri == null || !uri.StartsWith("spotify:track:") || !api.HasExtras) return;
            try
            {
                var r = await api.Send(HttpMethod.Get, "/me/library/contains?uris=" + uri);
                if (r.Ok && uri == TrackUri) { liked = r.Body.Contains("true"); ShowLike(); }
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            catch (InvalidOperationException) { }
        }

        async Task ToggleLike()
        {
            if (TrackUri == null) return;
            if (External)
            {
                liked = !liked;
                ShowLike();
                Bridge.Send("like");
                return;
            }
            if (!api.HasExtras)
            {
                SetArtist(L.T("Beğenme için Spotify'a bir kez yeniden izin ver"));
                await Connect();
                await CheckLike();
                return;
            }
            liked = !liked;
            ShowLike();
            Command(liked ? HttpMethod.Put : HttpMethod.Delete, "/me/library?uris=" + TrackUri);
        }

        // Odak listesini gizli bir Spotify listesi olarak kaydeder, sonucu mesaj olarak döner.
        public async Task<string> SaveList(string name, List<string> uris)
        {
            if (uris.Count == 0) return L.T("Kaydedilecek şarkı yok. Sayaç çalışırken müzik dinleyince liste oluşur.");
            if (!api.Connected) return L.T("Önce Spotify'a bağlan.");
            if (!api.HasExtras) await Connect();
            if (!api.HasExtras) return L.T("Liste için Spotify izni verilmedi.");
            var json = new JavaScriptSerializer();
            try
            {
                var made = await api.Send(HttpMethod.Post, "/me/playlists", json.Serialize(new Dictionary<string, object> {
                    { "name", name }, { "public", false }, { "description", L.T("Kor uygulamasından") } }));
                if (!made.Ok) return L.T("Liste oluşturulamadı (hata ") + (int)made.Status + ").";
                string id = (string)Parse(made.Body)["id"];
                var added = await api.Send(HttpMethod.Post, "/playlists/" + id + "/items", json.Serialize(new Dictionary<string, object> { { "uris", uris } }));
                return added.Ok ? "\"" + name + L.T("\" Spotify'a kaydedildi.") : L.T("Şarkılar eklenemedi (hata ") + (int)added.Status + ").";
            }
            catch (HttpRequestException) { return Offline; }
            catch (TaskCanceledException) { return Offline; }
            catch (InvalidOperationException e) { return e.Message; }
        }

        public void TogglePlay()
        {
            if (!IsConnected) return;
            string path = playing ? "/pause" : "/play";
            SetPlaying(!playing);
            Command(HttpMethod.Put, path);
        }

        public void Next() { if (IsConnected) { BeginSkip(); Command(HttpMethod.Post, "/next"); } }
        public void Previous() { if (IsConnected) { BeginSkip(); Command(HttpMethod.Post, "/previous"); } }
        public void SetVolume(int percent) { if (IsConnected) Command(HttpMethod.Put, "/volume?volume_percent=" + percent); }

        public void PlayContext(string uri)
        {
            if (api.Connected) Command(HttpMethod.Put, "/play", "{\"context_uri\":\"" + uri + "\"}");
        }

        void SetAccent(Color? color)
        {
            Accent = color;
            if (AccentChanged != null) AccentChanged();
        }

        // Kapaktaki en canlı renk tonunu bulur; soluk kapaklarda varsayılan renk kalır.
        public static Color? Dominant(BitmapSource source)
        {
            var bitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int width = bitmap.PixelWidth, height = bitmap.PixelHeight, stride = width * 4;
            var data = new byte[stride * height];
            bitmap.CopyPixels(data, stride, 0);
            var weight = new double[12]; var hue = new double[12]; var sat = new double[12];
            int pixels = 0;
            for (int i = 0; i < data.Length; i += 16)
            {
                double b = data[i] / 255.0, g = data[i + 1] / 255.0, r = data[i + 2] / 255.0;
                double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
                pixels++;
                if (d < 0.08 || max < 0.15) continue;
                double h = max == r ? ((g - b) / d + 6) % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
                h *= 60;
                double s = d / max, w = s * max;
                int slot = (int)(h / 30) % 12;
                weight[slot] += w; hue[slot] += h * w; sat[slot] += s * w;
            }
            int best = Array.IndexOf(weight, weight.Max());
            if (pixels == 0 || weight[best] / pixels < 0.04) return null;
            double hh = hue[best] / weight[best], ss = Math.Max(0.35, Math.Min(0.7, sat[best] / weight[best])), v = 0.92;
            double c = v * ss, x = c * (1 - Math.Abs(hh / 60 % 2 - 1)), m = v - c;
            double[] rgb = hh < 60 ? new[] { c, x, 0 } : hh < 120 ? new[] { x, c, 0 } : hh < 180 ? new[] { 0, c, x }
                : hh < 240 ? new[] { 0, x, c } : hh < 300 ? new[] { x, 0, c } : new[] { c, 0, x };
            return Color.FromRgb((byte)((rgb[0] + m) * 255), (byte)((rgb[1] + m) * 255), (byte)((rgb[2] + m) * 255));
        }

        public static Dictionary<string, object> Parse(string text)
        {
            return (Dictionary<string, object>)new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(text);
        }

        public SpotifyPanel(Window w, Window miniWindow)
        {
            win = w; mini = miniWindow;
            views = new[] { new SpotifyView(win), new SpotifyView(mini) };
            foreach (var v in views) Bind(v);
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += (a, b) => Tick();
            ShowConnection();
        }

        void Bind(SpotifyView v)
        {
            v.Connect.Click += async (a, b) => await Connect();
            v.Play.Click += (a, b) => TogglePlay();
            v.Next.Click += (a, b) => Next();
            v.Prev.Click += (a, b) => Previous();
            v.Like.Click += async (a, b) => await ToggleLike();
            v.Shuffle.Click += (a, b) =>
            {
                if (!canShuffle) { SetArtist(NoModes); return; }
                shuffle = !shuffle;
                ShowModes();
                Command(HttpMethod.Put, "/shuffle?state=" + (shuffle ? "true" : "false"));
            };
            v.Repeat.Click += (a, b) =>
            {
                if (!canRepeat) { SetArtist(NoModes); return; }
                repeat = repeat == "off" ? "context" : repeat == "context" ? "track" : "off";
                ShowModes();
                Command(HttpMethod.Put, "/repeat?state=" + repeat);
            };
            v.Seek.PreviewMouseLeftButtonDown += (a, b) => seeking = v.Seek;
            v.Seek.PreviewMouseLeftButtonUp += (a, b) => win.Dispatcher.BeginInvoke(new Action(() =>
            {
                seeking = null;
                progress = (long)v.Seek.Value;
                seekTarget = progress;
                seekHold = DateTime.UtcNow.AddSeconds(SeekHoldSeconds);
                sincePoll.Restart();
                Command(HttpMethod.Put, "/seek?position_ms=" + progress);
            }), DispatcherPriority.Input);
            v.Volume.PreviewMouseLeftButtonDown += (a, b) => volumeDrag = true;
            v.Volume.ValueChanged += (a, b) =>
            {
                if (!volumeDrag || !Logic.ShouldSendVolume((DateTime.UtcNow - lastVolumeSent).TotalMilliseconds, External)) return;
                lastVolumeSent = DateTime.UtcNow;
                Command(HttpMethod.Put, "/volume?volume_percent=" + (int)v.Volume.Value);
            };
            v.Volume.PreviewMouseLeftButtonUp += (a, b) => win.Dispatcher.BeginInvoke(new Action(() =>
            {
                volumeDrag = false;
                Command(HttpMethod.Put, "/volume?volume_percent=" + (int)v.Volume.Value);
            }), DispatcherPriority.Input);

            var menu = new ContextMenu();
            var leave = new MenuItem { Header = L.T("Spotify bağlantısını kes") };
            leave.Click += (a, b) => { api.Disconnect(); ShowConnection(); };
            menu.Items.Add(leave);
            v.Player.ContextMenu = menu;
        }

        void ShowConnection()
        {
            bool on = IsConnected;
            foreach (var v in views)
            {
                v.Connect.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
                v.Player.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            }
            if (on) { timer.Start(); ticks = IdlePollTicks; }
            else if (Bridge == null || !Bridge.Running) timer.Stop();
        }

        public async Task Connect()
        {
            if (connecting) return;
            connecting = true;
            foreach (var v in views) { v.Connect.IsEnabled = false; v.Connect.Content = L.T("Tarayıcıda izin ver"); }
            try { await api.Login(); }
            catch (Exception e)
            {
                if (!(e is InvalidOperationException || e is HttpRequestException || e is TaskCanceledException || e is System.ComponentModel.Win32Exception)) throw;
                MessageBox.Show(e.Message, "Spotify");
            }
            foreach (var v in views) { v.Connect.IsEnabled = true; v.Connect.Content = L.T("Spotify'a bağlan"); }
            connecting = false;
            ShowConnection();
        }

        void Tick()
        {
            var externalState = ExternalState();
            bool external = externalState != null;
            if (external != wasExternal)
            {
                wasExternal = external;
                if (!external)
                {
                    foreach (var v in views) { v.Like.Opacity = 1; v.Like.IsEnabled = true; v.Seek.IsEnabled = true; }
                }
                if (!external) { SoundProcess = null; TrackUri = null; externalCover = null; coverUrl = null; ticks = IdlePollTicks; }
                ShowConnection();
                if (AccentChanged != null) AccentChanged();
            }
            if ((win.WindowState == WindowState.Minimized || !win.IsVisible) && !mini.IsVisible && !external) return;
            if (external)
            {
                ShowExternal(externalState);
                ShowProgress();
                if (api.Connected && ++ticks >= PollEveryTicks)
                {
                    ticks = 0;
                    var checkApi = Poll();
                }
                return;
            }
            if (++ticks >= (playing ? PollEveryTicks : IdlePollTicks) || (playing && Current() >= duration && duration > 0))
            {
                ticks = 0;
                var ignored = Poll();
            }
            ShowProgress();
        }

        long Current()
        {
            return Math.Min(duration, progress + (playing ? sincePoll.ElapsedMilliseconds : 0));
        }

        static string Clock(long ms)
        {
            long s = ms / 1000;
            return (s / 60) + ":" + (s % 60).ToString("00");
        }

        void ShowProgress()
        {
            long now = seeking != null ? (long)seeking.Value : Current();
            foreach (var v in views)
            {
                v.Seek.Maximum = Math.Max(1, duration);
                if (v.Seek != seeking) v.Seek.Value = now;
                v.Pos.Text = Clock(now);
                v.Dur.Text = Clock(duration);
            }
        }

        void SetPlaying(bool value)
        {
            if (value != playing) { progress = Current(); sincePoll.Restart(); holdUntil = DateTime.UtcNow.AddSeconds(External ? 1 : 2.5); }
            playing = value;
            foreach (var v in views) v.PlayIcon.Data = playing ? PauseGeo : PlayGeo;
            AnnouncePlaying();
        }

        void ShowModes()
        {
            foreach (var v in views)
            {
                v.ShuffleIcon.Stroke = shuffle ? Gold : Quiet;
                v.RepeatIcon.Stroke = repeat == "off" ? Quiet : Gold;
                v.RepeatOne.Visibility = repeat == "track" ? Visibility.Visible : Visibility.Collapsed;
                v.Shuffle.Opacity = canShuffle ? 1 : 0.35;
                v.Repeat.Opacity = canRepeat ? 1 : 0.35;
            }
        }

        void SetArtist(string text) { foreach (var v in views) v.Artist.Text = text; }
        void SetTitle(string text) { foreach (var v in views) v.Title.Text = text; }

        async Task Poll()
        {
            if (polling || !api.Connected || DateTime.UtcNow < skipUntil) return;
            polling = true;
            try
            {
                var r = await api.Send(HttpMethod.Get, "");
                apiHasTrack = r.Ok && !string.IsNullOrEmpty(r.Body) && r.Status != HttpStatusCode.NoContent;
                apiPlaying = false;
                if (apiHasTrack)
                {
                    var current = Parse(r.Body);
                    apiPlaying = current.ContainsKey("is_playing") && current["is_playing"] is bool && (bool)current["is_playing"] && current["item"] != null;
                    apiHasTrack = current["item"] != null;
                }
                if (External) return;
                if (r.Status == (HttpStatusCode)429) skipUntil = DateTime.UtcNow.AddSeconds(r.RetryAfter);
                else if (r.Status == HttpStatusCode.NoContent || string.IsNullOrEmpty(r.Body)) ShowIdle();
                else if (r.Ok) ShowState(Parse(r.Body));
                else
                {
                    SetTitle(L.T("Spotify yanıt vermedi"));
                    SetArtist(L.T("Hata kodu ") + (int)r.Status + L.T(". Biraz sonra yeniden denenecek."));
                }
            }
            catch (HttpRequestException) { SetArtist(Offline); }
            catch (TaskCanceledException) { SetArtist(Offline); }
            catch (InvalidOperationException e) { SetArtist(e.Message); ShowConnection(); }
            catch (Exception e) { SetArtist(L.T("Spotify yanıtı okunamadı: ") + e.GetType().Name + " " + e.Message); }
            finally { polling = false; }
        }

        void ShowIdle()
        {
            SetPlaying(false);
            progress = duration = 0;
            Track = null;
            TrackUri = null;
            liked = false;
            ShowLike();
            ContextUri = null;
            Volume = null;
            SetAccent(null);
            SetTitle(L.T("Çalan şarkı yok"));
            SetArtist(message ?? L.T("Bir cihazda Spotify'ı açıp şarkı başlat"));
            foreach (var v in views) v.Cover.Source = null;
            coverUrl = null;
        }

        void ShowState(Dictionary<string, object> s)
        {
            message = null;
            var item = s.ContainsKey("item") ? s["item"] as Dictionary<string, object> : null;
            if (item == null) { ShowIdle(); return; }
            // Komuttan hemen sonra gelen yanıt eski durumu taşıyabilir, kısa süre yok sayılır.
            if (DateTime.UtcNow >= holdUntil) playing = (bool)s["is_playing"];
            foreach (var v in views) v.PlayIcon.Data = playing ? PauseGeo : PlayGeo;
            AnnouncePlaying();
            long reported = s["progress_ms"] == null ? 0 : Convert.ToInt64(s["progress_ms"]);
            if (!HoldSeek(reported))
            {
                progress = reported;
                sincePoll.Restart();
            }
            duration = Convert.ToInt64(item["duration_ms"]);
            var actions = s.ContainsKey("actions") ? s["actions"] as Dictionary<string, object> : null;
            var banned = actions != null && actions.ContainsKey("disallows") ? actions["disallows"] as Dictionary<string, object> : null;
            canShuffle = banned == null || !banned.ContainsKey("toggling_shuffle") || !(bool)banned["toggling_shuffle"];
            canRepeat = banned == null || !(banned.ContainsKey("toggling_repeat_context") && (bool)banned["toggling_repeat_context"]);
            shuffle = (bool)s["shuffle_state"];
            repeat = (string)s["repeat_state"];
            ShowModes();

            SetTitle((string)item["name"]);
            var artists = item.ContainsKey("artists") ? item["artists"] as object[] : null;
            SetArtist(artists == null ? "" : string.Join(", ", artists.Select(x => (string)((Dictionary<string, object>)x)["name"])));
            string people = artists == null ? "" : string.Join(", ", artists.Select(x => (string)((Dictionary<string, object>)x)["name"]));
            Track = (string)item["name"] + (people.Length > 0 ? ", " + people : "");
            string newUri = item.ContainsKey("uri") ? item["uri"] as string : null;
            if (newUri != TrackUri)
            {
                bool changed = TrackUri != null;
                TrackUri = newUri;
                var check = CheckLike();
                if (changed && TrackChanged != null) TrackChanged();
            }
            ShowSkip();
            var context = s.ContainsKey("context") ? s["context"] as Dictionary<string, object> : null;
            ContextUri = context != null && context.ContainsKey("uri") ? context["uri"] as string : null;

            var album = item.ContainsKey("album") ? item["album"] as Dictionary<string, object> : null;
            var images = album != null ? album["images"] as object[] : null;
            if (images != null && images.Length > 0)
            {
                string url = (string)((Dictionary<string, object>)images[images.Length - 1])["url"];
                if (url != coverUrl)
                {
                    coverUrl = url;
                    var image = new BitmapImage(new Uri(url));
                    foreach (var v in views) v.Cover.Source = image;
                    if (image.IsDownloading) image.DownloadCompleted += (a, b) => { if (url == coverUrl) SetAccent(Dominant(image)); };
                    else SetAccent(Dominant(image));
                }
            }
            var device = s["device"] as Dictionary<string, object>;
            deviceId = device != null && device.ContainsKey("id") ? device["id"] as string : null;
            if (device != null && !volumeDrag)
            {
                bool can = device.ContainsKey("supports_volume") && (bool)device["supports_volume"] && device["volume_percent"] != null;
                foreach (var v in views)
                {
                    v.Volume.IsEnabled = can;
                    if (can) v.Volume.Value = Convert.ToInt32(device["volume_percent"]);
                }
                Volume = can ? (int?)Convert.ToInt32(device["volume_percent"]) : null;
            }
        }

        async void Command(HttpMethod method, string path, string json = null)
        {
            if (SendExternal(path)) return;
            try
            {
                if (deviceId != null && !path.StartsWith("/me/"))
                    path += (path.Contains("?") ? "&" : "?") + "device_id=" + deviceId;
                var r = await api.Send(method, path, json);
                if (r.Status == HttpStatusCode.Forbidden) SetArtist(L.T("Spotify bu işleme şu an izin vermiyor."));
                else if (r.Status == HttpStatusCode.NotFound) { message = L.T("Önce bir cihazda Spotify'ı aç"); SetArtist(message); }
                else if (r.Status == (HttpStatusCode)429) SetArtist(L.T("Çok hızlı işlem yapıldı. Biraz bekle."));
            }
            catch (HttpRequestException) { SetArtist(Offline); }
            catch (TaskCanceledException) { SetArtist(Offline); }
            catch (InvalidOperationException) { ShowConnection(); return; }
            await Task.Delay(400);
            await Poll();
        }
    }
}
