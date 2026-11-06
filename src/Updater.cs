using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Sayac
{
    class UpdateInfo
    {
        public string Version;
        public string Sha256;
        public string Notes;
    }

    // Sürümler GitHub Releases'tan gelir. surum.json imzası uygulamadaki açık anahtarla doğrulanır,
    // Kor.exe de o dosyadaki özetle eşleşmezse kurulmaz.
    static class Updater
    {
        const string Releases = "https://github.com/Talverinhub/Kor/releases";

        static HttpClient Client()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            return new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        }

        static string PublicKey()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("imza.pub.xml"))
            using (var reader = new StreamReader(stream))
                return reader.ReadToEnd();
        }

        public static bool SignatureOk(byte[] data, byte[] signature, string publicKey)
        {
            using (var rsa = new RSACryptoServiceProvider())
            using (var sha = SHA256.Create())
            {
                rsa.PersistKeyInCsp = false;
                rsa.FromXmlString(publicKey);
                return rsa.VerifyData(data, sha, signature);
            }
        }

        public static async Task<UpdateInfo> Check()
        {
            using (var http = Client())
            {
                var answer = await http.GetAsync(Releases + "/latest/download/surum.json");
                if (answer.StatusCode == HttpStatusCode.NotFound) return null;
                answer.EnsureSuccessStatusCode();
                var manifest = await answer.Content.ReadAsByteArrayAsync();
                string signature = await http.GetStringAsync(Releases + "/latest/download/surum.json.sig");
                if (!SignatureOk(manifest, Convert.FromBase64String(signature.Trim()), PublicKey()))
                    throw new InvalidOperationException(L.T("Güncelleme imzası doğrulanamadı, kurulmadı."));
                var json = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(manifest));
                var info = new UpdateInfo { Version = (string)json["version"], Sha256 = (string)json["sha256"], Notes = json.ContainsKey("notes") ? json["notes"] as string : "" };
                return Logic.IsNewer(info.Version, AppVersion.Current, Skipped()) ? info : null;
            }
        }

        public static async Task<string> Download(UpdateInfo info)
        {
            using (var http = Client())
            {
                var data = await http.GetByteArrayAsync(Releases + "/download/v" + info.Version + "/Kor.exe");
                string hash;
                using (var sha = SHA256.Create()) hash = string.Concat(sha.ComputeHash(data).Select(b => b.ToString("x2")));
                if (!string.Equals(hash, info.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(L.T("İndirilen dosya doğrulanamadı, kurulmadı."));
                string folder = Path.Combine(Store.Folder, "guncelleme");
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, "Sayac-" + info.Version + ".exe");
                File.WriteAllBytes(path, data);
                return path;
            }
        }

        static string Exe { get { return Assembly.GetExecutingAssembly().Location; } }

        static string SkipFile { get { return Path.Combine(Store.Folder, "atlanan-surum.txt"); } }

        static string Skipped()
        {
            try { return File.Exists(SkipFile) ? File.ReadAllText(SkipFile).Trim() : ""; }
            catch (IOException) { return ""; }
        }

        public static string HealthName
        {
            get { return "SayacSaglikli" + (Environment.GetEnvironmentVariable("SAYAC_VERI") == null ? "" : "Deneme"); }
        }

        // Çalışan dosya taşınabilir ama üzerine yazılamaz; bu yüzden önce kenara alınır.
        public static void Replace(string path)
        {
            string old = Exe + ".old";
            if (File.Exists(old)) File.Delete(old);
            File.Move(Exe, old);
            try { File.Copy(path, Exe); }
            catch (Exception)
            {
                File.Move(old, Exe);
                throw;
            }
        }

        public static Process Launch(string args) { return Process.Start(Exe, args); }

        // Yeni sürüm sağlıklı açıldığını bildirene kadar eski kopya bekler.
        public static bool WaitHealthy(Process started)
        {
            using (var health = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.ManualReset, HealthName))
                for (int i = 0; i < 100; i++)
                {
                    if (health.WaitOne(250)) return true;
                    if (started.HasExited) return false;
                }
            return false;
        }

        public static void SignalHealthy()
        {
            using (var health = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.ManualReset, HealthName))
                health.Set();
        }

        public static void Rollback(Process started, string version)
        {
            try { if (!started.HasExited) { started.Kill(); started.WaitForExit(3000); } }
            catch (InvalidOperationException) { }
            File.Delete(Exe);
            File.Copy(Exe + ".old", Exe);
            File.WriteAllText(SkipFile, version ?? "");
        }

        public static void Clean()
        {
            try
            {
                string old = Exe + ".old";
                if (File.Exists(old)) File.Delete(old);
                string folder = Path.Combine(Store.Folder, "guncelleme");
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
