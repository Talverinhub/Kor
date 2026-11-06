using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;

namespace Sayac
{
    // Türkçe metinler anahtardır. Yeni dil için src/diller altına tek dosya eklemek yeter.
    public static class L
    {
        const string Prefix = "dil/";
        const string NameTag = "# name:";
        static Dictionary<string, string> map = new Dictionary<string, string>();

        public static string Code = "tr";
        public static CultureInfo Culture = new CultureInfo("tr");

        static string ReadResource(string name)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
                return reader.ReadToEnd();
        }

        // Kod ve görünen ad; dosyalardan bulunur.
        public static List<KeyValuePair<string, string>> Available()
        {
            var list = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("tr", "Türkçe") };
            foreach (string name in Assembly.GetExecutingAssembly().GetManifestResourceNames().Where(x => x.StartsWith(Prefix)).OrderBy(x => x))
            {
                string code = Path.GetFileNameWithoutExtension(name.Substring(Prefix.Length));
                string first = ReadResource(name).Split('\n')[0].Trim();
                list.Add(new KeyValuePair<string, string>(code, first.StartsWith(NameTag) ? first.Substring(NameTag.Length).Trim() : code));
            }
            return list;
        }

        public static string Pick(string saved)
        {
            return Logic.PickLanguage(saved, CultureInfo.InstalledUICulture.TwoLetterISOLanguageName, Available().Select(x => x.Key).ToArray());
        }

        public static void Use(string code)
        {
            Code = code;
            Culture = new CultureInfo(code);
            map = code == "tr" ? new Dictionary<string, string>() : Parse(ReadResource(Prefix + code + ".txt"));
        }

        public static Dictionary<string, string> Parse(string text)
        {
            var result = new Dictionary<string, string>();
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                int split = line.IndexOf(" => ", StringComparison.Ordinal);
                if (line.Length == 0 || line[0] == '#' || split <= 0) continue;
                result[line.Substring(0, split).Trim()] = line.Substring(split + 4).Trim();
            }
            return result;
        }

        // Baştaki ve sondaki boşluklar korunur, böylece parçalı metinler de çevrilir.
        public static string T(string text)
        {
            if (map.Count == 0 || string.IsNullOrEmpty(text)) return text;
            string key = text.Trim(), value;
            if (key.Length == 0 || !map.TryGetValue(key, out value)) return text;
            int start = text.IndexOf(key, StringComparison.Ordinal);
            return text.Substring(0, start) + value + text.Substring(start + key.Length);
        }

        public static string F(string text, params object[] values)
        {
            return string.Format(T(text), values);
        }

        public static string Xaml(string xaml)
        {
            if (map.Count == 0) return xaml;
            return Regex.Replace(xaml, "\\b(Content|Text|ToolTip|Title|AutomationProperties\\.Name)=\"([^\"{]+)\"", m =>
                m.Groups[1].Value + "=\"" + SecurityElement.Escape(T(WebUtility.HtmlDecode(m.Groups[2].Value))) + "\"");
        }
    }
}
