using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Droply
{
    /// <summary>
    /// Notification discrète bas-gauche qui propose le téléchargement
    /// d'un fichier uploadé depuis un autre PC.
    /// </summary>
    public partial class IncomingFileToast : Window
    {
        private const int AutoDismissSeconds = 12;
        private readonly string _url;

        public IncomingFileToast(string url)
        {
            _url = url;
            InitializeComponent();

            // Position : bas-gauche, au-dessus de la taskbar
            Left = SystemParameters.WorkArea.Left + 16;
            Top = SystemParameters.WorkArea.Bottom - Height - 16;

            Loaded += OnLoaded;
        }

        public static void ShowToast(string url) => new IncomingFileToast(url).Show();

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Slide depuis la gauche + fade-in
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
            SlideXf.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(280))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

            // Auto-dismiss après N secondes
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(AutoDismissSeconds) };
            timer.Tick += (_, _) => { timer.Stop(); Dismiss(); };
            timer.Start();
        }

        private void Download_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(_url);
                Process.Start(new ProcessStartInfo(_url) { UseShellExecute = true });
            }
            catch { /* silencieux */ }
            Dismiss();
        }

        private void Ignore_Click(object sender, RoutedEventArgs e) => Dismiss();

        // Fade-out puis fermeture
        private void Dismiss()
        {
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
            fade.Completed += (_, _) => { try { Close(); } catch { } };
            BeginAnimation(OpacityProperty, fade);
        }
    }
}