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
        void SaveRun()
        {
            Store.Save("calisan.json", new RunState
            {
                Active = mode == Mode.Running || mode == Mode.Paused,
                Paused = mode == Mode.Paused,
                Up = up,
                BaseMs = up ? Remaining() : Remaining(),
                TotalMs = totalMs,
                SavedTicks = DateTime.UtcNow.Ticks,
                SongsLeft = songsLeft,
                Intent = intent
            });
        }

        // Yeniden başlatma veya çökme sonrası sayaç kaldığı yerden sürer.
        public void RestoreRun()
        {
            var run = Store.Load<RunState>("calisan.json");
            if (!run.Active || mode != Mode.Setup) return;
            up = run.Up;
            songsLeft = run.SongsLeft;
            intent = run.Intent;
            totalMs = run.TotalMs;
            long value = Logic.RestoredValue(up, run.Paused, run.BaseMs, run.SavedTicks, DateTime.UtcNow.Ticks);
            if (!up && value <= 0)
            {
                up = false;
                missed = L.F("{0}'de süren doldu.", DateTime.Now.AddMilliseconds(value).ToString("HH:mm"));
                Store.Save("calisan.json", new RunState());
                ShowNotice();
                return;
            }
            remainingAtResume = value;
            if (run.Paused)
            {
                mode = Mode.Paused;
                if (up) ShowElapsed(value); else ShowLeft(value);
                Apply();
            }
            else Resume();
        }

        void ShowElapsed(long ms)
        {
            long shown = ms / 1000;
            ShowDigits((int)(shown / 3600 % 100), (int)(shown / 60 % 60), (int)(shown % 60));
            ring.Set(1 - (ms % 60000) / 60000.0, 1);
        }

        public void StartUp(int songs)
        {
            if (mode != Mode.Setup) return;
            if (songs > 0 && !spotify.Playing) { missed = L.T("Şarkı modu için önce Spotify'da bir şarkı başlat."); ShowNotice(); return; }
            up = true;
            songsLeft = songs;
            totalMs = 1;
            remainingAtResume = 0;
            BeginCommon();
            Resume();
        }

        void BeginCommon()
        {
            missed = null;
            settings.LastNote = null;
            songExtended = false;
            intent = settings.AskIntent ? intentBox.Text.Trim() : "";
            if (notes != null) notes.Topic(intent);
            win.Focus();
            SetMiniPanel(null);
            ShowNotice();
        }

        void OnTrackChanged()
        {
            if (mode != Mode.Running || songsLeft <= 0) return;
            if (--songsLeft == 0) { up = false; Finish(); }
            else Apply();
        }

        bool ExtendForSong()
        {
            if (!settings.EndWithSong || songExtended || !spotify.Playing || spotify.SongLeft < 2000) return false;
            songExtended = true;
            totalMs = remainingAtResume = spotify.SongLeft;
            clock.Restart();
            Apply();
            return true;
        }


        void ShowLeft(long left)
        {
            long shown = Logic.ShownSeconds(left);
            ShowDigits((int)(shown / 3600), (int)(shown / 60 % 60), (int)(shown % 60));
            double fraction = left / (double)totalMs;
            ring.Set(fraction, 1);
            miniScale.ScaleX = fraction;
            win.TaskbarItemInfo.ProgressValue = fraction;
        }

        public async void RestorePendingVolume()
        {
            if (settings.PendingVolume < 0) return;
            await System.Threading.Tasks.Task.Delay(6000);
            spotify.SetVolume(settings.PendingVolume);
            settings.PendingVolume = -1;
            settings.Save();
        }

        public void AddSeconds(int seconds)
        {
            if (up) return;
            if (mode == Mode.Setup) { SetSeconds(Seconds + seconds); return; }
            if (mode == Mode.Ringing) return;
            long left = Remaining() + seconds * 1000L;
            remainingAtResume = left;
            totalMs += seconds * 1000L;
            if (mode == Mode.Running) clock.Restart();
            if (left > QuietSeconds * 1000) RestoreVolume();
            if (left > 60000) SetGlow(false);
            ShowLeft(left);
            SaveRun();
            Apply();
        }

        public void Snooze()
        {
            if (mode != Mode.Ringing) return;
            player.Stop();
            RestoreVolume();
            ringClock.Reset();
            miniTime.Opacity = 1;
            SaveNote();
            SetMiniPanel(null);
            songExtended = false;
            totalMs = remainingAtResume = SnoozeSeconds * 1000L;
            ShowLeft(totalMs);
            Resume();
        }


        void MainAction()
        {
            switch (mode)
            {
                case Mode.Setup:
                    if (setupMode == 0) Start(); else StartUp(setupMode == 2 ? settings.SongCount : 0);
                    LinkedMusic(true);
                    break;
                case Mode.Running: Pause(); LinkedMusic(false); break;
                case Mode.Paused: Resume(); LinkedMusic(true); break;
                case Mode.Ringing: Reset(); break;
            }
        }

        public void Start()
        {
            if (Seconds == 0) return;
            SaveSeconds();
            totalMs = Seconds * 1000L;
            remainingAtResume = totalMs;
            up = false;
            songsLeft = 0;
            BeginCommon();
            Resume();
        }

        void Resume()
        {
            mode = Mode.Running;
            if (miniPanel == "paused") SetMiniPanel(null);
            pausedByMusic = false;
            clock.Restart();
            focusClock.Restart();
            timer.Start();
            SaveRun();
            Apply();
        }

        void Pause()
        {
            remainingAtResume = Remaining();
            pausedByMusic = false;
            clock.Stop();
            timer.Stop();
            RestoreVolume();
            SetGlow(false);
            stats.Save();
            mode = Mode.Paused;
            SaveRun();
            if (miniPanel == null) SetMiniPanel("paused");
            Apply();
        }

        void Reset()
        {
            player.Stop();
            timer.Stop();
            RestoreVolume();
            SetGlow(false);
            stats.Save();
            SetMiniPanel(null);
            askDone = false;
            pausedByMusic = false;
            SaveNote();
            up = false;
            songsLeft = 0;
            songExtended = false;
            intent = null;
            clock.Reset();
            ringClock.Reset();
            mode = Mode.Setup;
            ShowSetupDigits();
            ring.Set(1, 1);
            miniScale.ScaleX = 1;
            miniTime.Opacity = 1;
            TryInstall();
            if (win.WindowState == WindowState.Minimized && !pinned) win.WindowState = WindowState.Normal;
            Store.Save("calisan.json", new RunState());
            RefreshGoal();
            ShowNotice();
            Apply();
        }

        long Remaining()
        {
            return up ? remainingAtResume + clock.ElapsedMilliseconds : Math.Max(0, remainingAtResume - clock.ElapsedMilliseconds);
        }

        void SaveNote()
        {
            if (noteBox.Text.Trim().Length > 0) { settings.LastNote = noteBox.Text.Trim(); settings.Save(); }
            noteBox.Text = "";
        }

        void Tick()
        {
            UpdateThemeView();
            if (mode == Mode.Ringing)
            {
                double t = ringClock.Elapsed.TotalSeconds;
                double pulse = 0.55 + 0.45 * Math.Cos(t * 2 * Math.PI / 1.6);
                ring.Set(1, pulse);
                miniTime.Opacity = pulse;
                if (fading && t >= 3.5) RestoreVolume();
                if (t >= AlarmLimitSeconds)
                {
                    missed = L.F("{0}'de süren doldu.", DateTime.Now.AddSeconds(-AlarmLimitSeconds).ToString("HH:mm"));
                    tray.ShowBalloonTip(5000, L.T("Alarm kaçırıldı"), missed, System.Windows.Forms.ToolTipIcon.None);
                    Reset();
                }
                return;
            }
            double spent = focusClock.Elapsed.TotalSeconds;
            focusClock.Restart();
            stats.Add(spent, spotify.Playing ? spotify.Track : null, spotify.TrackUri, spotify.CoverUrl, intent);
            sinceSave += spent;
            if (sinceSave >= 30) { sinceSave = 0; stats.Save(); SaveRun(); RefreshGoal(); }

            long left = Remaining();
            if (up)
            {
                ShowElapsed(left);
                return;
            }
            if (left == 0)
            {
                if (ExtendForSong()) return;
                Finish();
                return;
            }
            ShowLeft(left);
            RunFade(left);
            RunAsk(left);
            bool lastMinute = settings.LastMinute && !settings.LessMotion && left <= 60000;
            SetGlow(lastMinute);
            if (lastMinute) glow.Opacity = 0.55 + 0.3 * Math.Cos(left * 2 * Math.PI / 2000);
        }

        void Finish()
        {
            mode = Mode.Ringing;
            ExtremeFinish();
            clock.Stop();
            ringClock.Restart();
            ShowDigits(0, 0, 0);
            SetGlow(false);
            stats.Save();
            ShowAsk(false);
            Store.Save("calisan.json", new RunState());
            SetMiniPanel("end");
            // Sessiz bitişte çan bir kez çalar, müzik birazdan eski sesine döner.
            if (fading) player.Play(); else player.PlayLooping();
            miniTime.Text = L.T("Süre doldu");
            miniScale.ScaleX = 0;
            if (win.WindowState != WindowState.Minimized && !inTray)
            {
                win.Topmost = true;
                win.Activate();
                win.Topmost = false;
            }
            Apply();
        }
    }
}
