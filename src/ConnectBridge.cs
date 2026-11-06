using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using IoPath = System.IO.Path;

namespace Sayac
{
    // TalverinConnect eklentisiyle bu bilgisayar içinde WebSocket üzerinden konuşur.
    class ConnectBridge
    {
        public const int Port = 8899, Protocol = 1;
        const int MaxFrame = 65536, MaxClients = 8, WriteTimeoutMs = 1000;
        public static readonly string[] Files = { "manifest.json", "background.js", "core/connect.js", "connectors/youtube-music.js", "popup.html", "popup.js", "icons/icon-16.png", "icons/icon-32.png", "icons/icon-48.png", "icons/icon-128.png" };
        // Mağaza yayınından sonra eklentinin resmi kimliği buraya yazılır; resmi eklenti onay istemez.
        public static readonly string[] OfficialOrigins = { };

        class Client
        {
            public TcpClient Tcp;
            public Stream Stream;
            public string Origin;
            public bool Approved, Outdated;
            public readonly object Gate = new object();
            public readonly Dictionary<long, Dictionary<string, object>> Tabs = new Dictionary<long, Dictionary<string, object>>();
        }

        readonly Func<string, bool> trusted;
        readonly List<Client> clients = new List<Client>();
        TcpListener listener;
        volatile bool running;
        Client activeClient;
        long activeTab;

        public Action<string> ApprovalNeeded;
        public Action<string> ProblemFound;
        public Action ShowRequested;
        public Action StateArrived;
        public static bool IsOfficial(string origin) { return OfficialOrigins.Contains(origin); }

        // Eklenti eski sürümdeyse kullanıcıya güncelleme önerilir.
        public static bool NeedsUpdate(int protocol, string version, string bundled)
        {
            return protocol < Protocol || (version != null && Logic.IsNewer(bundled, version, ""));
        }

        public static string BundledVersion()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("eklenti/manifest.json"))
            using (var reader = new StreamReader(stream))
                return (string)((Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(reader.ReadToEnd()))["version"];
        }

        public bool Outdated { get { lock (clients) return clients.Any(c => c.Outdated); } }
        readonly Dictionary<string, string> problems = new Dictionary<string, string>();
        readonly Dictionary<string, object> problemOwners = new Dictionary<string, object>();
        readonly Dictionary<string, long> problemTabs = new Dictionary<string, long>();

        public static string SourceName(string source)
        {
            return source == "youtube-music" ? "YouTube Music" : source == "spotify-web" ? "Spotify Web" : source;
        }

        public bool Connected { get { lock (clients) return clients.Any(c => c.Approved); } }

        public string[] Problems { get { lock (clients) return problems.Keys.ToArray(); } }

        // Sorun yalnızca ilk görüldüğünde bildirilir, düzelince listeden düşer.
        public bool SetProblem(string source, string error, object owner = null, long tab = 0)
        {
            lock (clients)
            {
                if (string.IsNullOrEmpty(error)) { problems.Remove(source); return false; }
                bool isNew = !problems.ContainsKey(source);
                problems[source] = error;
                problemOwners[source] = owner;
                problemTabs[source] = tab;
                return isNew;
            }
        }

        // Sekme kapanınca veya eklenti kopunca ona ait sorun kayıtları silinir.
        public void ClearProblems(object owner, long? tab = null)
        {
            lock (clients)
                foreach (var source in problems.Keys.ToArray())
                    if (ReferenceEquals(problemOwners[source], owner) && (tab == null || problemTabs[source] == tab))
                        problems.Remove(source);
        }
        public volatile string Browser;
        public string Error { get; private set; }
        public bool Running { get { return running; } }

        public ConnectBridge(Func<string, bool> isTrusted) { trusted = isTrusted; }

        // Sıradan web siteleri eklenti kaynağı taşıyamaz, bağlantı burada reddedilir.
        public static bool Authorized(string origin)
        {
            return origin != null && (origin.StartsWith("chrome-extension://") || origin.StartsWith("moz-extension://"));
        }

        // Eksik veya yanlış türde gelen durum arayüze ulaşmaz.
        public static bool ValidState(Dictionary<string, object> state)
        {
            object playing, title;
            if (!state.TryGetValue("playing", out playing) || !(playing is bool)) return false;
            if (!state.TryGetValue("title", out title) || !(title is string)) return false;
            if (!state.ContainsKey("liked") || !(state["liked"] is bool)) state["liked"] = false;
            foreach (var key in new[] { "position", "duration", "volume" })
            {
                object value;
                if (state.TryGetValue(key, out value) && value != null && !(value is int) && !(value is long) && !(value is decimal) && !(value is double)) return false;
            }
            return true;
        }

        public static string AcceptKey(string key)
        {
            using (var sha = SHA1.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        }

        public static byte[] EncodeFrame(string text)
        {
            var body = Encoding.UTF8.GetBytes(text);
            var frame = new List<byte> { 0x81 };
            if (body.Length < 126) frame.Add((byte)body.Length);
            else { frame.Add(126); frame.Add((byte)(body.Length >> 8)); frame.Add((byte)body.Length); }
            frame.AddRange(body);
            return frame.ToArray();
        }

        // Birden çok sekmede çalan öncelikli, sonra en son güncellenen seçilir.
        public Dictionary<string, object> Fresh()
        {
            lock (clients)
            {
                Dictionary<string, object> best = null;
                foreach (var client in clients.Where(c => c.Approved))
                    foreach (var tab in client.Tabs)
                    {
                        if (best == null || Rank(tab.Value) > Rank(best))
                        {
                            best = tab.Value;
                            activeClient = client;
                            activeTab = tab.Key;
                        }
                    }
                return best;
            }
        }

        static long Rank(Dictionary<string, object> state)
        {
            return (long)state["_seen"] + ((bool)state["playing"] ? TimeSpan.TicksPerDay * 3650 : 0);
        }

        public void Send(string command)
        {
            var parts = command.Split(new[] { ':' }, 2);
            Client client; long tab;
            lock (clients) { client = activeClient; tab = activeTab; }
            if (client == null) return;
            Write(client, new JavaScriptSerializer().Serialize(new Dictionary<string, object> {
                { "t", "command" }, { "tab", tab }, { "name", parts[0] }, { "value", parts.Length > 1 ? parts[1] : "" } }));
        }

        public void Decide(string origin, bool approved)
        {
            List<Client> waiting;
            lock (clients) waiting = clients.Where(c => c.Origin == origin && !c.Approved).ToList();
            foreach (var client in waiting)
            {
                if (approved) Welcome(client);
                else client.Tcp.Close();
            }
        }

        void Welcome(Client client)
        {
            client.Approved = true;
            Write(client, "{\"t\":\"welcome\",\"protocol\":" + Protocol + ",\"update\":" + (client.Outdated ? "true" : "false") + ",\"latest\":\"" + BundledVersion() + "\",\"lang\":\"" + L.Code + "\"}");
        }

        public void Start()
        {
            if (running) return;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, Port);
                listener.Start();
                running = true;
                Error = null;
                new Thread(Accept) { IsBackground = true, Name = "TalverinConnect" }.Start();
            }
            catch (SocketException) { Error = Port + L.T(" bağlantı noktası kullanımda."); }
        }

        public void Stop()
        {
            running = false;
            if (listener != null) listener.Stop();
            lock (clients)
            {
                foreach (var client in clients) client.Tcp.Close();
                clients.Clear();
                activeClient = null;
            }
        }

        void Accept()
        {
            while (running)
            {
                try
                {
                    var tcp = listener.AcceptTcpClient();
                    int count;
                    lock (clients) count = clients.Count;
                    if (count >= MaxClients) { tcp.Close(); continue; }
                    tcp.NoDelay = true;
                    new Thread(() => Serve(tcp)) { IsBackground = true }.Start();
                }
                catch (SocketException) { }
                catch (ObjectDisposedException) { return; }
                catch (InvalidOperationException) { return; }
            }
        }

        void Serve(TcpClient tcp)
        {
            var client = new Client { Tcp = tcp };
            try
            {
                using (tcp)
                using (var stream = tcp.GetStream())
                {
                    stream.WriteTimeout = WriteTimeoutMs;
                    client.Stream = stream;
                    if (!Handshake(client)) return;
                    lock (clients) clients.Add(client);
                    string text;
                    while ((text = ReadFrame(client)) != null)
                    {
                        // Tek bozuk mesaj bağlantıyı veya uygulamayı düşürmez.
                        try { Handle(client, text); }
                        catch (ArgumentException) { }
                        catch (FormatException) { }
                        catch (OverflowException) { }
                        catch (InvalidCastException) { }
                    }
                }
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
            catch (SocketException) { }
            catch (OverflowException) { }
            finally
            {
                lock (clients)
                {
                    clients.Remove(client);
                    if (activeClient == client) activeClient = null;
                }
                ClearProblems(client);
            }
        }

        bool Handshake(Client client)
        {
            var head = new StringBuilder();
            while (!head.ToString().EndsWith("\r\n\r\n") && head.Length < 8192)
            {
                int b = client.Stream.ReadByte();
                if (b < 0) return false;
                head.Append((char)b);
            }
            var lines = head.ToString().Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines.Skip(1))
            {
                int mark = line.IndexOf(':');
                if (mark > 0) headers[line.Substring(0, mark).Trim()] = line.Substring(mark + 1).Trim();
            }
            string origin, key;
            headers.TryGetValue("Origin", out origin);
            if (lines.Length == 0 || !lines[0].StartsWith("GET /baglan ") || !Authorized(origin) || !headers.TryGetValue("Sec-WebSocket-Key", out key))
            {
                var denied = Encoding.ASCII.GetBytes("HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                client.Stream.Write(denied, 0, denied.Length);
                return false;
            }
            client.Origin = origin;
            var reply = Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + AcceptKey(key) + "\r\n\r\n");
            client.Stream.Write(reply, 0, reply.Length);
            return true;
        }

        static void Fill(Stream stream, byte[] buffer, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = stream.Read(buffer, read, count - read);
                if (n <= 0) throw new IOException();
                read += n;
            }
        }

        string ReadFrame(Client client)
        {
            while (true)
            {
                var head = new byte[2];
                Fill(client.Stream, head, 2);
                int opcode = head[0] & 15;
                long length = head[1] & 127;
                if (length == 126)
                {
                    var extended = new byte[2];
                    Fill(client.Stream, extended, 2);
                    length = extended[0] << 8 | extended[1];
                }
                else if (length == 127) return null;
                if (length > MaxFrame || (head[1] & 128) == 0) return null;
                var mask = new byte[4];
                Fill(client.Stream, mask, 4);
                var body = new byte[length];
                Fill(client.Stream, body, (int)length);
                for (int i = 0; i < body.Length; i++) body[i] ^= mask[i % 4];
                if (opcode == 8) return null;
                if (opcode == 1) return Encoding.UTF8.GetString(body);
            }
        }

        void Handle(Client client, string text)
        {
            var message = new JavaScriptSerializer().DeserializeObject(text) as Dictionary<string, object>;
            if (message == null || !message.ContainsKey("t")) return;
            string type = message["t"] as string;
            if (type == "hello")
            {
                Browser = message.ContainsKey("browser") ? message["browser"] as string : null;
                int protocol = message.ContainsKey("protocol") ? Convert.ToInt32(message["protocol"]) : 0;
                client.Outdated = NeedsUpdate(protocol, message.ContainsKey("version") ? message["version"] as string : null, BundledVersion());
                // Dil gizli değildir; eklenti onay beklerken de doğru dilde görünür.
                Write(client, "{\"t\":\"lang\",\"lang\":\"" + L.Code + "\"}");
                if (IsOfficial(client.Origin) || trusted(client.Origin)) Welcome(client);
                else if (ApprovalNeeded != null) ApprovalNeeded(client.Origin);
                return;
            }
            if (type == "show" && client.Approved)
            {
                if (ShowRequested != null) ShowRequested();
                return;
            }
            if (!client.Approved || !message.ContainsKey("tab")) return;
            if (type == "health")
            {
                string source = message.ContainsKey("source") ? message["source"] as string : null;
                string error = message.ContainsKey("error") ? message["error"] as string : "";
                if (source != null && SetProblem(source, error, client, Convert.ToInt64(message["tab"])) && ProblemFound != null) ProblemFound(source);
                return;
            }
            long tab = Convert.ToInt64(message["tab"]);
            if (type == "gone") ClearProblems(client, tab);
            lock (clients)
            {
                object active;
                if (type == "gone" || (type == "state" && (!message.TryGetValue("active", out active) || !(active is bool) || !(bool)active)))
                    client.Tabs.Remove(tab);
                else if (type == "state" && ValidState(message))
                {
                    message["_seen"] = DateTime.UtcNow.Ticks;
                    client.Tabs[tab] = message;
                }
            }
            if (StateArrived != null) StateArrived();
        }

        static void Write(Client client, string text)
        {
            var frame = EncodeFrame(text);
            try { lock (client.Gate) client.Stream.Write(frame, 0, frame.Length); }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        // Eklentiyi yüklemeye hazır klasöre çıkarır.
        public static string ExtractExtension()
        {
            string folder = IoPath.Combine(Store.Folder, "TalverinConnect");
            foreach (var file in Files)
            {
                string target = IoPath.Combine(folder, file.Replace('/', IoPath.DirectorySeparatorChar));
                Directory.CreateDirectory(IoPath.GetDirectoryName(target));
                using (var source = Assembly.GetExecutingAssembly().GetManifestResourceStream("eklenti/" + file))
                using (var output = File.Create(target))
                    source.CopyTo(output);
            }
            return folder;
        }

        // Klasör daha önce kurulduysa ve eskiyse yeni dosyalar kendiliğinden yazılır.
        public static bool RefreshExtensionFiles()
        {
            string manifest = IoPath.Combine(Store.Folder, "TalverinConnect", "manifest.json");
            if (!File.Exists(manifest)) return false;
            var installed = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(manifest));
            if (!Logic.IsNewer(BundledVersion(), (string)installed["version"], "")) return false;
            ExtractExtension();
            return true;
        }

        static readonly string[][] Browsers =
        {
            new[] { "brave", "brave.exe", "brave://extensions" },
            new[] { "chrome", "chrome.exe", "chrome://extensions" },
            new[] { "msedge", "msedge.exe", "edge://extensions" },
        };

        static string InstalledPath(string exe)
        {
            const string key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\";
            foreach (var hive in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
                using (var entry = hive.OpenSubKey(key + exe))
                    if (entry != null && entry.GetValue(null) is string) return (string)entry.GetValue(null);
            return null;
        }

        // Açık olan tarayıcı tercih edilir; yoksa kurulu olan ilk tarayıcının eklenti sayfası açılır.
        public static string OpenExtensionsPage()
        {
            var order = Browsers.OrderByDescending(b => System.Diagnostics.Process.GetProcessesByName(b[0]).Length > 0);
            foreach (var browser in order)
            {
                string path = InstalledPath(browser[1]);
                if (path == null) continue;
                System.Diagnostics.Process.Start(path, browser[2]);
                return browser[0];
            }
            return null;
        }
    }
}
