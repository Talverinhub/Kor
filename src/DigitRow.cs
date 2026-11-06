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
    // Rakamlar her hanede aynı genişlikte durur, böylece sayılar değişirken yazı oynamaz.
    class DigitRow : StackPanel
    {
        readonly TextBlock model;
        readonly double digitWidth, colonWidth;
        string text = "";

        DigitRow(TextBlock source)
        {
            model = source;
            Orientation = Orientation.Horizontal;
            HorizontalAlignment = HorizontalAlignment.Center;
            var face = new Typeface(source.FontFamily, source.FontStyle, source.FontWeight, source.FontStretch);
            Func<string, double> width = s => new FormattedText(s, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, source.FontSize, Brushes.White).WidthIncludingTrailingWhitespace;
            digitWidth = "0123456789".Max(c => width(c.ToString()));
            colonWidth = width(":");
        }

        public static DigitRow Swap(TextBlock source)
        {
            var row = new DigitRow(source);
            var border = source.Parent as Border;
            if (border != null) border.Child = row; else ((Viewbox)source.Parent).Child = row;
            row.Text = source.Text;
            return row;
        }

        TextBlock Cell(double cellWidth)
        {
            return new TextBlock { Width = cellWidth, TextAlignment = TextAlignment.Center, FontFamily = model.FontFamily, FontSize = model.FontSize, FontWeight = model.FontWeight, Foreground = model.Foreground };
        }

        public string Text
        {
            get { return text; }
            set
            {
                if (value == text) return;
                bool numeric = value.All(c => char.IsDigit(c) || c == ':');
                if (!numeric)
                {
                    Children.Clear();
                    var whole = Cell(double.NaN);
                    whole.Text = value;
                    Children.Add(whole);
                }
                else
                {
                    if (Children.Count != value.Length || value.Length != text.Length || !text.All(c => char.IsDigit(c) || c == ':'))
                    {
                        Children.Clear();
                        foreach (char c in value) Children.Add(Cell(c == ':' ? colonWidth : digitWidth));
                    }
                    for (int i = 0; i < value.Length; i++) ((TextBlock)Children[i]).Text = value[i].ToString();
                }
                text = value;
            }
        }
    }
}
