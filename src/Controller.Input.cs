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
    partial class Controller
    {
        int LoadSeconds()
        {
            try { int v; if (int.TryParse(File.ReadAllText(savePath), out v) && v > 0) return v; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return 300;
        }

        void SaveSeconds()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(savePath));
                File.WriteAllText(savePath, Seconds.ToString());
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        int Seconds { get { return h * 3600 + m * 60 + s; } }

        public void SetSeconds(int total)
        {
            if (mode != Mode.Setup) return;
            total = Math.Min(total, 99 * 3600 + 59 * 60 + 59);
            h = total / 3600; m = total / 60 % 60; s = total % 60;
            ShowDigits(h, m, s);
            mainBtn.IsEnabled = CanRun;
        }

        void ShowDigits(int hh, int mm, int ss)
        {
            hText.Text = hh.ToString("00"); mText.Text = mm.ToString("00"); sText.Text = ss.ToString("00");
            miniTime.Text = Logic.ClockText(hh, mm, ss);
            if (tray != null) tray.Text = mode == Mode.Setup ? "Kor" : "Kor: " + miniTime.Text + (up ? "" : L.T(" kaldı"));
        }

        void Step(Border field, int delta)
        {
            if (mode != Mode.Setup || setupMode != 0) return;
            if (field == hField) h = Logic.Wrap(h, delta, 100);
            else if (field == mField) m = Logic.Wrap(m, delta, 60);
            else s = Logic.Wrap(s, delta, 60);
            typedFresh = true;
            SetSeconds(Seconds);
        }

        void TypeDigit(Border field, int digit)
        {
            int current = field == hField ? h : field == mField ? m : s;
            int next = typedFresh ? digit : (current % 10) * 10 + digit;
            int max = field == hField ? 99 : 59;
            if (next > max) next = digit;
            typedFresh = false;
            if (field == hField) h = next; else if (field == mField) m = next; else s = next;
            SetSeconds(Seconds);
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (Find<FrameworkElement>("IntroOverlay").Visibility == Visibility.Visible)
            {
                GuideKey(e.Key);
                e.Handled = true;
                return;
            }
            if (closeOverlay.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.Escape) closeOverlay.Visibility = Visibility.Collapsed;
                e.Handled = true;
                return;
            }
            if (panel.HandleKey(e)) return;
            var focused = Keyboard.FocusedElement;
            if (focused is TextBox && e.Key != Key.Enter && e.Key != Key.Escape) return;
            if (focused is ButtonBase && (e.Key == Key.Space || e.Key == Key.Enter)) return;
            if (e.Key == Key.Space || e.Key == Key.Enter)
            {
                if (CanRun) MainAction();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape)
            {
                if (mode != Mode.Setup) ResetWithUndo();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.P) { Pin(); e.Handled = true; return; }
            if (mode != Mode.Setup || setupMode != 0) return;
            var field = Keyboard.FocusedElement as Border;
            if (field != hField && field != mField && field != sField) return;
            if (e.Key == Key.Up) { Step(field, 1); e.Handled = true; }
            else if (e.Key == Key.Down) { Step(field, -1); e.Handled = true; }
            else if (e.Key == Key.Left || e.Key == Key.Right)
            {
                var order = new[] { hField, mField, sField };
                int i = Array.IndexOf(order, field) + (e.Key == Key.Right ? 1 : -1);
                order[(i + 3) % 3].Focus();
                e.Handled = true;
            }
            else if (e.Key >= Key.D0 && e.Key <= Key.D9) { TypeDigit(field, e.Key - Key.D0); e.Handled = true; }
            else if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9) { TypeDigit(field, e.Key - Key.NumPad0); e.Handled = true; }
        }


        void RunHotkey(string id)
        {
            switch (id)
            {
                case "toggle": if (CanRun) MainAction(); break;
                case "reset": if (mode != Mode.Setup) ResetWithUndo(); break;
                case "add1": AddSeconds(60); break;
                case "snooze": Snooze(); break;
                case "pin": if (win.WindowState == WindowState.Minimized || inTray) Expand(); else Pin(); break;
                case "click": ToggleClickThrough(); break;
                case "music": spotify.TogglePlay(); break;
                case "next": spotify.Next(); break;
                case "prev": spotify.Previous(); break;
            }
        }
    }
}
