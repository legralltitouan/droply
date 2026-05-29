// =============================================================================
//  Droply - SettingsWindow.xaml.cs
// -----------------------------------------------------------------------------
//  Code-behind de la fenêtre de paramètres.
//   - Thème + langue appliqués AVANT InitializeComponent (les DynamicResource
//     doivent exister au moment du parsing XAML).
//   - Toggles avec live-preview (thème, langue) : pas de persistance tant que
//     l'utilisateur n'a pas cliqué "Enregistrer".
//   - Cancel / Close / X restaurent l'état d'origine via _mainWindow.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Droply
{
    public partial class SettingsWindow : Window
    {
        private readonly MainWindow _mainWindow;
        private bool _isClosing;

        public SettingsWindow(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;

            // 1) Thème + langue AVANT InitializeComponent pour que les ressources
            //    DynamicResource soient déjà peuplées quand le XAML est parsé.
            ApplyTheme(_mainWindow.CurrentSettings.IsLightTheme);
            Localization.ApplyLanguage(_mainWindow.CurrentSettings.Language);

            InitializeComponent();

            // Drag de la fenêtre depuis n'importe quelle zone (en plus de la barre de titre).
            this.MouseDown += (s, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed) this.DragMove();
            };

            LoadCurrentSettings();
        }

        /// <summary>
        /// Applique le thème clair/sombre à la fenêtre de paramètres.
        /// Palette légèrement différente de MainWindow.SetTheme (fond
        /// semi-transparent + couleurs adaptées au design "carte flottante").
        /// </summary>
        private void ApplyTheme(bool isLight)
        {
            var palette = isLight
                ? new Dictionary<string, string>
                {
                    ["AppBgBrush"] = "#B3F5F5F7",
                    ["AppControlBgBrush"] = "#FFFFFF",
                    ["AppBorderBrush"] = "#E5E5EA",
                    ["AppTextBrush"] = "#1C1C1E",
                    ["AppSubTextBrush"] = "#8E8E93",
                    ["AppHoverBrush"] = "#EFEFF4",
                }
                : new Dictionary<string, string>
                {
                    ["AppBgBrush"] = "#B31A1A1A",
                    ["AppControlBgBrush"] = "#222222",
                    ["AppBorderBrush"] = "#2A2A2A",
                    ["AppTextBrush"] = "#E0E0E0",
                    ["AppSubTextBrush"] = "#8A8A8A",
                    ["AppHoverBrush"] = "#353535",
                };

            var res = Application.Current.Resources;
            foreach (var kv in palette)
                res[kv.Key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(kv.Value));
        }

        private void LoadCurrentSettings()
        {
            StartupCheck.IsChecked = _mainWindow.CurrentSettings.RunAtStartup;
            ThemeCheck.IsChecked = _mainWindow.CurrentSettings.IsLightTheme;
            LanguageCheck.IsChecked = _mainWindow.CurrentSettings.Language == "en";
            WebhookBox.Text = _mainWindow.CurrentSettings.DiscordWebhook;
            BotTokenBox.Text = _mainWindow.CurrentSettings.DiscordBotToken;
            ChannelIdBox.Text = _mainWindow.CurrentSettings.DiscordChannelId;
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) this.DragMove();
        }

        /// <summary>Live-preview du thème (sans persister tant qu'on n'a pas cliqué Enregistrer).</summary>
        private void ThemeCheck_Click(object sender, RoutedEventArgs e)
        {
            ApplyTheme(ThemeCheck.IsChecked == true);
        }

        /// <summary>Live-preview du changement de langue (idem : non persisté ici).</summary>
        private void LanguageCheck_Click(object sender, RoutedEventArgs e)
        {
            string lang = LanguageCheck.IsChecked == true ? "en" : "fr";
            Localization.ApplyLanguage(lang);
        }

        /// <summary>Ferme la fenêtre avec un fondu + glissement vers le bas.</summary>
        private void CloseWithAnimation()
        {
            if (_isClosing) return;
            _isClosing = true;

            var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
            var slideDown = new DoubleAnimation(35, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            slideDown.Completed += (s, e) => this.Close();

            this.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            WindowTransform.BeginAnimation(TranslateTransform.YProperty, slideDown);
        }

        /// <summary>Restaure le thème + langue d'origine (annule le live-preview) puis ferme.</summary>
        private void RevertAndClose()
        {
            _mainWindow.SetTheme(_mainWindow.CurrentSettings.IsLightTheme);
            Localization.ApplyLanguage(_mainWindow.CurrentSettings.Language);
            CloseWithAnimation();
        }

        private void SaveAndClose_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow.CurrentSettings.RunAtStartup = StartupCheck.IsChecked ?? true;
            _mainWindow.CurrentSettings.IsLightTheme = ThemeCheck.IsChecked ?? false;
            _mainWindow.CurrentSettings.Language = LanguageCheck.IsChecked == true ? "en" : "fr";
            _mainWindow.CurrentSettings.DiscordWebhook = WebhookBox.Text.Trim();
            _mainWindow.CurrentSettings.DiscordBotToken = BotTokenBox.Text.Trim();
            _mainWindow.CurrentSettings.DiscordChannelId = ChannelIdBox.Text.Trim();

            // Redémarre le polling Discord avec les nouveaux paramètres (toast inclus).
            DiscordSync.Start(_mainWindow);

            _mainWindow.SaveSettings();
            CloseWithAnimation();
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double v = bytes;
            int u = 0;
            while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
            return $"{v:0.##} {units[u]}";
        }

        private void OpenStats_Click(object sender, RoutedEventArgs e)
        {
            // Reset jour si nécessaire
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (_mainWindow.CurrentSettings.LastUploadDate != today)
            {
                _mainWindow.CurrentSettings.LastUploadDate = today;
                _mainWindow.CurrentSettings.BytesUploadedToday = 0;
            }

            StatPcName.Text = Environment.MachineName;

            // Transition crossfade Main → Stats
            FadeSwap(MainPanel, StatsPanel, () =>
            {
                AnimateBytesCounter(StatTotal, _mainWindow.CurrentSettings.TotalBytesUploaded, 1400);
                AnimateBytesCounter(StatToday, _mainWindow.CurrentSettings.BytesUploadedToday, 1200);
            });
        }

        private void BackFromStats_Click(object sender, RoutedEventArgs e)
            => FadeSwap(StatsPanel, MainPanel, null);

        /// <summary>Crossfade entre 2 panneaux avec slide vertical doux.</summary>
        private void FadeSwap(FrameworkElement from, FrameworkElement to, Action? onMidpoint)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

            // Petit slide vers le haut + fade-out
            var slideOut = new DoubleAnimation(0, -12, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease };
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180));

            if (from.RenderTransform is not TranslateTransform)
                from.RenderTransform = new TranslateTransform();
            if (to.RenderTransform is not TranslateTransform)
                to.RenderTransform = new TranslateTransform();

            fadeOut.Completed += (_, _) =>
            {
                from.Visibility = Visibility.Collapsed;
                to.Visibility = Visibility.Visible;
                to.Opacity = 0;
                ((TranslateTransform)to.RenderTransform).Y = 12;

                onMidpoint?.Invoke();

                to.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
                ((TranslateTransform)to.RenderTransform).BeginAnimation(
                    TranslateTransform.YProperty,
                    new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
            };

            from.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            ((TranslateTransform)from.RenderTransform).BeginAnimation(TranslateTransform.YProperty, slideOut);
        }

        /// <summary>Anime un compteur d'octets de 0 → targetBytes avec une courbe cubic-out.</summary>
        private static void AnimateBytesCounter(TextBlock target, long targetBytes, int durationMs)
        {
            if (targetBytes <= 0)
            {
                target.Text = FormatBytes(0);
                return;
            }

            var start = DateTime.UtcNow;
            var duration = TimeSpan.FromMilliseconds(durationMs);
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16) // ~60 FPS
            };

            timer.Tick += (_, _) =>
            {
                double t = (DateTime.UtcNow - start).TotalMilliseconds / duration.TotalMilliseconds;
                if (t >= 1.0)
                {
                    target.Text = FormatBytes(targetBytes);
                    timer.Stop();
                    return;
                }
                // cubic ease-out : 1 - (1 - t)^3
                double eased = 1 - Math.Pow(1 - t, 3);
                long current = (long)(targetBytes * eased);
                target.Text = FormatBytes(current);
            };
            timer.Start();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => RevertAndClose();
        private void Close_Click(object sender, RoutedEventArgs e) => RevertAndClose();
    }
}