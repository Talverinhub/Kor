using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Sayac
{
    partial class Controller
    {
        static readonly SolidColorBrush NoteOn = new SolidColorBrush(Theme.Map(Color.FromRgb(0xD8, 0xC4, 0x9A)));
        static readonly SolidColorBrush NoteOff = new SolidColorBrush(Theme.Map(Color.FromRgb(0x8E, 0x8A, 0x82)));

        void BindNotes()
        {
            notes = new NotePad(win, mini);
            KeepMiniOnTop();
            InitTheme();
            Find<Button>("NoteBtn").Click += (a, b) => notes.Toggle();
            ((Button)mini.FindName("MiniNote")).Click += (a, b) => notes.Toggle();
            notes.VisibilityChanged += UpdateNoteButtons;
            EventHandler follow = (a, b) => notes.Follow();
            DependencyPropertyChangedEventHandler shown = (a, b) => notes.Follow();
            SizeChangedEventHandler sized = (a, b) => notes.Follow();
            win.LocationChanged += follow;
            win.StateChanged += follow;
            win.IsVisibleChanged += shown;
            win.SizeChanged += sized;
            mini.LocationChanged += follow;
            mini.IsVisibleChanged += shown;
            mini.SizeChanged += sized;
            win.Loaded += (a, b) => { notes.RestoreWindow(); UpdateNoteButtons(); };
            win.Closed += (a, b) => notes.Close();
        }

        void UpdateNoteButtons()
        {
            var brush = notes.Visible ? NoteOn : NoteOff;
            Find<Path>("NoteBtnIcon").Stroke = brush;
            ((Path)mini.FindName("MiniNoteIcon")).Stroke = brush;
        }
    }
}
