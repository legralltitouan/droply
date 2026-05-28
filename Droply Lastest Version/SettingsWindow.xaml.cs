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
using System.Windows;
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

        private void Cancel_Click(object sender, RoutedEventArgs e) => RevertAndClose();
        private void Close_Click(object sender, RoutedEventArgs e) => RevertAndClose();
    }
}