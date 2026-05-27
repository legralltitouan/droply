using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace QuickSend
{
    public static class IncomingFileToast
    {
        public static void Show(string url)
        {
            var win = new Window
            {
                Width = 340,
                Height = 110,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                ResizeMode = ResizeMode.NoResize
            };

            // Position bas-gauche (au-dessus de la taskbar et de l'icône Droply)
            win.Left = SystemParameters.WorkArea.Left + 16;
            win.Top = SystemParameters.WorkArea.Bottom - win.Height - 16;

            var border = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = (Brush)Application.Current.Resources["AppBgBrush"],
                BorderBrush = (Brush)Application.Current.Resources["AppBorderBrush"],
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 12, 14, 12),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.5, Color = Colors.Black }
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = Localization.Get("L.Toast.Title"),
                FontFamily = new FontFamily("Segoe UI"),
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Foreground = (Brush)Application.Current.Resources["AppTextBrush"]
            });
            stack.Children.Add(new TextBlock
            {
                Text = Localization.Get("L.Toast.Subtitle"),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["AppSubTextBrush"],
                Margin = new Thickness(0, 2, 0, 10),
                TextWrapping = TextWrapping.Wrap
            });

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ignoreBtn = new Button
            {
                Content = Localization.Get("L.Toast.Ignore"),
                Width = 80,
                Height = 26,
                Margin = new Thickness(0, 0, 8, 0),
                Background = (Brush)Application.Current.Resources["AppControlBgBrush"],
                Foreground = (Brush)Application.Current.Resources["AppTextBrush"],
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            var dlBtn = new Button
            {
                Content = Localization.Get("L.Toast.Download"),
                Width = 100,
                Height = 26,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ED8209")),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                FontWeight = FontWeights.SemiBold
            };

            ignoreBtn.Click += (s, e) => Dismiss(win);
            dlBtn.Click += (s, e) =>
            {
                try
                {
                    try { Clipboard.SetText(url); } catch { }
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                catch { }
                Dismiss(win);
            };

            btnRow.Children.Add(ignoreBtn);
            btnRow.Children.Add(dlBtn);
            stack.Children.Add(btnRow);
            border.Child = stack;
            win.Content = border;

            // Slide -in depuis la gauche
            var slideTransform = new TranslateTransform(-80, 0);
            border.RenderTransform = slideTransform;
            win.Opacity = 0;
            win.Show();

            win.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)));
            slideTransform.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(280)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

            // Auto-dismiss après 12s
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
            timer.Tick += (s, e) => { timer.Stop(); Dismiss(win); };
            timer.Start();
        }

        private static void Dismiss(Window win)
        {
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
            fade.Completed += (s, e) => { try { win.Close(); } catch { } };
            win.BeginAnimation(UIElement.OpacityProperty, fade);
        }
    }
}