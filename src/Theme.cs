using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace Sayac
{
    // Temalar Kor'un renklerini açılışta karşılıklarıyla değiştirir; düzen aynı kalır.
    // Odak ve Extreme ayrıca kendi görünümlerini açar.
    public static class Theme
    {
        public static readonly string[] Names = { "kor", "lavanta", "odak", "extreme" };
        public static string Name = "kor";
        static readonly Dictionary<int, int> map = new Dictionary<int, int>();

        public static bool Focus { get { return Name == "odak"; } }
        public static bool Neon { get { return Name == "extreme"; } }

        public static readonly Color[] NeonColors =
        {
            Color.FromRgb(0xFF, 0x4F, 0xD8), Color.FromRgb(0xB6, 0x6C, 0xFF), Color.FromRgb(0x7F, 0xF2, 0xFF),
            Color.FromRgb(0x7C, 0xFF, 0x8A), Color.FromRgb(0xFF, 0xE1, 0x4D), Color.FromRgb(0xFF, 0x8A, 0x3D)
        };

        // Kor rengi ve her temadaki karşılığı. Sıra: zemin, yüzeyler, çizgiler, yazılar, vurgu.
        static readonly string[] Kor =
        {
            "000000", "141311", "141413", "1A1917", "1C1C1C", "1E1D1B", "262522", "2A2925", "2A2926", "2B2A27", "34322D", "3A3832",
            "5A5750", "6E6A63", "8E8A82", "9A958C", "A8A399", "B5B0A6", "E9E4DA", "F4F1EA", "D8C49A", "EBDBB8", "BFAB82", "E6B84A"
        };
        static readonly Dictionary<string, string[]> Palettes = new Dictionary<string, string[]>
        {
            { "lavanta", new[] {
                "120E1A", "1A1426", "1A1426", "201830", "1D1629", "221A31", "281F38", "2B2240", "2A2338", "2E2540", "362B4A", "3A3050",
                "5C5274", "6E6488", "8A80A3", "9A90B3", "A89FC0", "BFB4D6", "E8E1F5", "F1ECFA", "C9B6F2", "DCCDF7", "AD98DE", "B48CF0" } },
            { "odak", new[] {
                "15171A", "1A1D21", "1A1D21", "1E2226", "1D2024", "1E2226", "24292E", "262B30", "262A2F", "2A2E33", "31363C", "3A4047",
                "5A626B", "6B737C", "8C949E", "99A1AA", "A9B0B8", "C7CDD3", "E6EAED", "F2F4F6", "5DCAA5", "7FD8BA", "47B08D", "E6C34A" } },
            { "extreme", new[] {
                "07040F", "0F0820", "0F0820", "140A2A", "130A28", "1A0F35", "1F1240", "221445", "1C0F38", "2A1450", "331A60", "3A1E6E",
                "5E4E86", "7A6A9E", "9D8BC4", "AE9ED0", "C2B4E0", "E9D8FF", "F5EEFF", "FFFFFF", "FF4FD8", "FF7AE6", "E03CBD", "FFE14D" } }
        };

        static int Key(string hex) { return Convert.ToInt32(hex, 16); }

        public static void Use(string name)
        {
            Name = Array.IndexOf(Names, name) >= 0 ? name : "kor";
            map.Clear();
            string[] palette;
            if (!Palettes.TryGetValue(Name, out palette)) return;
            for (int i = 0; i < Kor.Length; i++) map[Key(Kor[i])] = Key(palette[i]);
        }

        public static Color Map(Color c)
        {
            int value;
            if (!map.TryGetValue((c.R << 16) | (c.G << 8) | c.B, out value)) return c;
            return Color.FromArgb(c.A, (byte)(value >> 16), (byte)(value >> 8), (byte)value);
        }

        public static Color Accent { get { return Map(Color.FromRgb(0xD8, 0xC4, 0x9A)); } }
        public static Color Surface { get { return Map(Color.FromRgb(0x1E, 0x1D, 0x1B)); } }

        // Arayüz dosyalarındaki #RRGGBB ve #AARRGGBB renkleri çevrilir.
        public static string Xaml(string xaml)
        {
            if (map.Count == 0) return xaml;
            return Regex.Replace(xaml, "#([0-9A-Fa-f]{2})?([0-9A-Fa-f]{6})\\b", m =>
            {
                int value;
                if (!map.TryGetValue(Key(m.Groups[2].Value), out value)) return m.Value;
                return "#" + m.Groups[1].Value + value.ToString("X6");
            });
        }

        public static string Label(string name)
        {
            switch (name)
            {
                case "lavanta": return L.T("Lavanta");
                case "odak": return L.T("Odak (ADHD)");
                case "extreme": return L.T("Extreme");
                default: return "Kor";
            }
        }
    }
}
