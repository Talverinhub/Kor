using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Sayac
{
    partial class Controller
    {
        // Hedef öğe, başlık, yapılacak iş, sonucu. Hedefi olmayan adım ortada görünür.
        static readonly string[][] GuideSteps =
        {
            new[] { "", L.T("Hoş geldin"), "", L.T("Kısa bir tur: her bölüme tıklayınca ne olduğunu göstereceğim. Bir dakika sürer, istediğin an kapatabilirsin.") },
            new[] { "Dial", L.T("Süreyi ayarla"), L.T("Bir rakama tıkla ve yaz"), L.T("Okları veya fare tekerleğini de kullanabilirsin.") },
            new[] { "ModeBar", L.T("Modunu seç"), L.T("Sayaç, Kronometre veya Şarkı'ya tıkla"), L.T("Şarkı modunda sayaç, seçtiğin sayıda şarkı bitince durur.") },
            new[] { "Presets", L.T("Hazır süreler"), L.T("Bir süreye tıkla"), L.T("Kadran hemen o süreye geçer. Bu düğmeleri Ayarlar'dan değiştirebilirsin.") },
            new[] { "MainBtn", L.T("Başlat"), L.T("Buraya tıkla"), L.T("Geri sayım başlar. Sayaç çalışırken +1 dk ve +5 dk ile süre ekleyebilirsin.") },
            new[] { "PanelBtn", L.T("Rutinler, özet ve ayarlar"), L.T("Bu simgeye tıkla"), L.T("Sık kullandığın süreleri kaydet, odak özetini gör, alarmı ve kısayolları değiştir.") },
            new[] { "PinBtn", L.T("Mini sayaç"), L.T("Raptiyeye tıkla"), L.T("Sayaç ve müzik, ekranın köşesinde her zaman üstte duran küçük bir pencereye geçer.") },
            new[] { "SpotifyRow", L.T("Müziğin"), L.T("Şarkıyı buradan yönet"), L.T("Spotify için önce Ayarlar > Müzik'e Client ID'ni gir, sonra Spotify'a bağlan'a tıkla. Spotify Premium gerekir.") },
            new[] { "CloseBtn", L.T("Kapatma"), L.T("Çarpıya tıkla"), L.T("Kor'u tepsiye indirebilir veya tamamen kapatabilirsin. Tepside kısayollar çalışmaya devam eder.") },
            new[] { "", L.T("Her şey hazır"), "", L.T("Ctrl+Shift+Alt+S sayacı her yerden başlatır. Bu turu Ayarlar > Genel'den yeniden açabilirsin.") }
        };
        const double GuideGap = 16, GuidePad = 8;
        List<string[]> guideSteps;
        RectangleGeometry guideFull, guideHole;
        bool guideBound;

        FrameworkElement GuideTarget(string[] step)
        {
            return step[0].Length == 0 ? null : Find<FrameworkElement>(step[0]);
        }

        void BindGuide()
        {
            guideBound = true;
            var overlay = Find<Grid>("IntroOverlay");
            guideFull = new RectangleGeometry();
            guideHole = new RectangleGeometry { RadiusX = 14, RadiusY = 14 };
            Find<Path>("IntroDim").Data = new CombinedGeometry(GeometryCombineMode.Exclude, guideFull, guideHole);
            Find<Path>("IntroRing").Data = guideHole;
            overlay.SizeChanged += (a, b) => { if (overlay.Visibility == Visibility.Visible) PlaceGuide(false); };
            Find<Button>("IntroNext").Click += (a, b) => GuideMove(1);
            Find<Button>("IntroBack").Click += (a, b) => GuideMove(-1);
            Find<Button>("IntroSkip").Click += (a, b) => CloseGuide();
        }

        void GuideMove(int step)
        {
            introStep += step;
            if (introStep >= guideSteps.Count) CloseGuide();
            else if (introStep >= 0) ShowGuideStep();
            else introStep = 0;
        }

        void ShowGuideStep()
        {
            var step = guideSteps[introStep];
            Find<TextBlock>("IntroTitle").Text = step[1];
            var action = Find<TextBlock>("IntroAction");
            action.Text = step[2];
            action.Visibility = step[2].Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            Find<TextBlock>("IntroText").Text = step[3];
            Find<Button>("IntroNext").Content = introStep == guideSteps.Count - 1 ? L.T("Başla") : L.T("İleri");
            Find<Button>("IntroBack").Visibility = introStep > 0 ? Visibility.Visible : Visibility.Hidden;
            var dots = Find<StackPanel>("IntroDots");
            dots.Children.Clear();
            for (int i = 0; i < guideSteps.Count; i++)
                dots.Children.Add(new Ellipse
                {
                    Width = i == introStep ? 8 : 6, Height = i == introStep ? 8 : 6, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center,
                    Fill = new SolidColorBrush(i == introStep ? Color.FromRgb(216, 196, 154) : Color.FromRgb(74, 72, 67))
                });
            PlaceGuide(true);
        }

        void PlaceGuide(bool animate)
        {
            var overlay = Find<Grid>("IntroOverlay");
            var card = Find<FrameworkElement>("IntroCard");
            overlay.UpdateLayout();
            double width = overlay.ActualWidth, height = overlay.ActualHeight;
            guideFull.Rect = new System.Windows.Rect(0, 0, width, height);
            card.LayoutTransform = null;
            overlay.UpdateLayout();
            var target = GuideTarget(guideSteps[introStep]);
            bool spot = target != null && target.IsVisible;
            System.Windows.Rect hole = new System.Windows.Rect(width / 2, height / 2, 0, 0);
            if (spot)
            {
                hole = target.TransformToVisual(overlay).TransformBounds(new System.Windows.Rect(0, 0, target.ActualWidth, target.ActualHeight));
                hole.Inflate(GuidePad, GuidePad);
                hole.Intersect(new System.Windows.Rect(2, 2, Math.Max(0, width - 4), Math.Max(0, height - 4)));
            }
            bool below = height - hole.Bottom >= hole.Top;
            double room = spot ? Math.Max(hole.Top, height - hole.Bottom) - 2 * GuideGap : height - 2 * GuideGap;
            double scale = Logic.GuideScale(card.ActualWidth, card.ActualHeight, width - 2 * GuideGap, height - 2 * GuideGap, room);
            card.LayoutTransform = scale < 1 ? new ScaleTransform(scale, scale) : null;
            double cardWidth = card.ActualWidth * scale, cardHeight = card.ActualHeight * scale, top = (height - cardHeight) / 2;
            if (spot)
            {
                top = below ? hole.Bottom + GuideGap : hole.Top - GuideGap - cardHeight;
                top = Math.Max(GuideGap, Math.Min(top, height - cardHeight - GuideGap));
            }
            Canvas.SetLeft(card, (width - cardWidth) / 2);
            Canvas.SetTop(card, Math.Max(0, top));
            Find<UIElement>("IntroRing").Opacity = spot ? 1 : 0;
            guideHole.BeginAnimation(RectangleGeometry.RectProperty, null);
            if (animate && !settings.LessMotion)
            {
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                guideHole.BeginAnimation(RectangleGeometry.RectProperty, new RectAnimation(hole, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
                card.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
            }
            else guideHole.Rect = hole;
        }

        void GuideKey(System.Windows.Input.Key key)
        {
            if (key == System.Windows.Input.Key.Escape) CloseGuide();
            else if (key == System.Windows.Input.Key.Right || key == System.Windows.Input.Key.Enter) GuideMove(1);
            else if (key == System.Windows.Input.Key.Left) GuideMove(-1);
        }

        void CloseGuide()
        {
            Find<FrameworkElement>("IntroOverlay").Visibility = Visibility.Collapsed;
            settings.GuideHidden = true;
            settings.Save();
        }

        public void ShowGuide()
        {
            if (!guideBound) BindGuide();
            // Sayaç çalışırken gizli olan bölümler turda atlanır.
            guideSteps = GuideSteps.Where(x => !win.IsVisible || GuideTarget(x) == null || GuideTarget(x).IsVisible).ToList();
            introStep = 0;
            Find<FrameworkElement>("IntroOverlay").Visibility = Visibility.Visible;
            ShowGuideStep();
        }

        public void ShowIntro() { if (!settings.GuideHidden) ShowGuide(); }
    }
}
