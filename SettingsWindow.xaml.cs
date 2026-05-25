using System.Windows;
using System.Windows.Input;

namespace QuickSend
{
    public partial class SettingsWindow : Window
    {
        private readonly MainWindow _mainWindow;

        public SettingsWindow(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;

            this.MouseDown += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) this.DragMove(); };

            LoadCurrentSettings();
        }

        private void LoadCurrentSettings()
        {
            StartupCheck.IsChecked = _mainWindow.CurrentSettings.RunAtStartup;
            ThemeCheck.IsChecked = _mainWindow.CurrentSettings.IsLightTheme;
            WebhookBox.Text = _mainWindow.CurrentSettings.DiscordWebhook;
        }

        private void ThemeCheck_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow.SetTheme(ThemeCheck.IsChecked ?? false);
        }

        private void SaveAndClose_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow.CurrentSettings.RunAtStartup = StartupCheck.IsChecked ?? true;
            _mainWindow.CurrentSettings.IsLightTheme = ThemeCheck.IsChecked ?? false;
            _mainWindow.CurrentSettings.DiscordWebhook = WebhookBox.Text.Trim();

            _mainWindow.SaveSettings();

            this.Close();
        }
    }
}