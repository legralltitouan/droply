using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace QuickSend
{
    public partial class SettingsWindow : Window
    {
        private readonly MainWindow _mainWindow;
        private bool _isClosing = false;

        public SettingsWindow(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;

            // 1. Thème + langue avant InitializeComponent pour que les ressources existent
            ApplyTheme(_mainWindow.CurrentSettings.IsLightTheme);
            Localization.ApplyLanguage(_mainWindow.CurrentSettings.Language);

            InitializeComponent();

            this.MouseDown += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) this.DragMove(); };

            LoadCurrentSettings();
        }

        private void ApplyTheme(bool isLight)
        {
            if (isLight)
            {
                Application.Current.Resources["AppBgBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B3F5F5F7"));
                Application.Current.Resources["AppControlBgBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"));
                Application.Current.Resources["AppBorderBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E5E5EA"));
                Application.Current.Resources["AppTextBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1C1C1E"));
                Application.Current.Resources["AppSubTextBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8E8E93"));
                Application.Current.Resources["AppHoverBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EFEFF4"));
            }
            else
            {
                Application.Current.Resources["AppBgBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B31A1A1A"));
                Application.Current.Resources["AppControlBgBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#222222"));
                Application.Current.Resources["AppBorderBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2A2A"));
                Application.Current.Resources["AppTextBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0E0E0"));
                Application.Current.Resources["AppSubTextBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8A8A8A"));
                Application.Current.Resources["AppHoverBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#353535"));
            }
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

        private void ThemeCheck_Click(object sender, RoutedEventArgs e)
        {
            ApplyTheme(ThemeCheck.IsChecked == true);
        }

        // Live-preview du changement de langue (sans sauvegarder tant que l'utilisateur ne clique pas Enregistrer)
        private void LanguageCheck_Click(object sender, RoutedEventArgs e)
        {
            string lang = LanguageCheck.IsChecked == true ? "en" : "fr";
            Localization.ApplyLanguage(lang);
        }

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

        private void SaveAndClose_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow.CurrentSettings.RunAtStartup = StartupCheck.IsChecked ?? true;
            _mainWindow.CurrentSettings.IsLightTheme = ThemeCheck.IsChecked ?? false;
            _mainWindow.CurrentSettings.Language = LanguageCheck.IsChecked == true ? "en" : "fr";
            _mainWindow.CurrentSettings.DiscordWebhook = WebhookBox.Text.Trim();
            _mainWindow.CurrentSettings.DiscordBotToken = BotTokenBox.Text.Trim();
            _mainWindow.CurrentSettings.DiscordChannelId = ChannelIdBox.Text.Trim();
            DiscordSync.Start(_mainWindow); // redémarre le polling avec les nouveaux paramètres

            _mainWindow.SaveSettings();
            CloseWithAnimation();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            // Restaure thème + langue d'origine (annule le live-preview)
            _mainWindow.SetTheme(_mainWindow.CurrentSettings.IsLightTheme);
            Localization.ApplyLanguage(_mainWindow.CurrentSettings.Language);
            CloseWithAnimation();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow.SetTheme(_mainWindow.CurrentSettings.IsLightTheme);
            Localization.ApplyLanguage(_mainWindow.CurrentSettings.Language);
            CloseWithAnimation();
        }
    }
}