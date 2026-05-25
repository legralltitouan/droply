using Microsoft.Win32;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace QuickSend
{
    public class AppSettings
    {
        public bool RunAtStartup { get; set; } = true;
        public bool IsLightTheme { get; set; } = false;
        public string DiscordWebhook { get; set; } = string.Empty;
    }

    public partial class MainWindow : Window
    {
        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;

        private static readonly HttpClient client = new HttpClient();
        private const long MaxFileSizeMB = 20000;
        private const string ApiUrl = "https://store1.gofile.io/uploadFile";

        public AppSettings CurrentSettings { get; private set; }
        private readonly string settingsPath;

        static MainWindow()
        {
            client.Timeout = TimeSpan.FromHours(2);
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("User-Agent", "Droply/1.0");
        }

        public MainWindow()
        {
            string appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Droply");
            Directory.CreateDirectory(appDataPath);
            settingsPath = Path.Combine(appDataPath, "settings.json");

            LoadSettings();

            InitializeComponent();
            SetState("Idle");
            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            CompositionTarget.Rendering += OnRendering;
        }

        public void LoadSettings()
        {
            try
            {
                if (File.Exists(settingsPath))
                {
                    string json = File.ReadAllText(settingsPath);
                    CurrentSettings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
                else
                {
                    CurrentSettings = new AppSettings();
                }
            }
            catch { CurrentSettings = new AppSettings(); }

            ToggleStartup(CurrentSettings.RunAtStartup);
            SetTheme(CurrentSettings.IsLightTheme);
        }

        public void SaveSettings()
        {
            try
            {
                string json = JsonSerializer.Serialize(CurrentSettings);
                File.WriteAllText(settingsPath, json);

                ToggleStartup(CurrentSettings.RunAtStartup);
                SetTheme(CurrentSettings.IsLightTheme);
            }
            catch { }
        }

        public void ToggleStartup(bool enable)
        {
            try
            {
                string runKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
                using RegistryKey key = Registry.CurrentUser.OpenSubKey(runKey, true);
                if (enable) key.SetValue("Droply", System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
                else key.DeleteValue("Droply", false);
            }
            catch { }
        }

        public void SetTheme(bool isLight)
        {
            var res = Application.Current.Resources;

            if (isLight)
            {
                res["AppBgBrush"] = new SolidColorBrush(Color.FromRgb(243, 243, 243));
                res["AppBorderBrush"] = new SolidColorBrush(Color.FromRgb(200, 200, 200));
                res["AppHoverBrush"] = new SolidColorBrush(Color.FromArgb(80, 200, 200, 200));
                res["AppTextBrush"] = new SolidColorBrush(Colors.Black);
                res["AppSubTextBrush"] = new SolidColorBrush(Color.FromRgb(100, 100, 100));
                res["AppControlBgBrush"] = new SolidColorBrush(Color.FromRgb(220, 220, 220));
            }
            else
            {
                res["AppBgBrush"] = new SolidColorBrush(Color.FromRgb(42, 44, 49));
                res["AppBorderBrush"] = new SolidColorBrush(Color.FromRgb(62, 64, 71));
                res["AppHoverBrush"] = new SolidColorBrush(Color.FromArgb(80, 54, 55, 55));
                res["AppTextBrush"] = new SolidColorBrush(Colors.White);
                res["AppSubTextBrush"] = new SolidColorBrush(Color.FromRgb(163, 163, 163));
                res["AppControlBgBrush"] = new SolidColorBrush(Color.FromRgb(28, 29, 33));
            }
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWin = new SettingsWindow(this);
            settingsWin.Show();
        }

        private void Quit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

        private void OnRendering(object sender, EventArgs e)
        {
            IntPtr taskbarHwnd = FindWindow("Shell_TrayWnd", null);
            if (taskbarHwnd != IntPtr.Zero)
            {
                GetWindowRect(taskbarHwnd, out RECT rect);
                PresentationSource source = PresentationSource.FromVisual(this);
                if (source == null) return;

                double dpiY = source.CompositionTarget.TransformToDevice.M22;
                double dpiX = source.CompositionTarget.TransformToDevice.M11;

                double logicalTaskbarTop = rect.Top / dpiY;
                double logicalTaskbarBottom = rect.Bottom / dpiY;
                double taskbarHeight = logicalTaskbarBottom - logicalTaskbarTop;
                double logicalScreenHeight = SystemParameters.PrimaryScreenHeight;

                bool isTaskbarVisible = logicalTaskbarTop < (logicalScreenHeight - 5);

                if (isTaskbarVisible)
                {
                    if (this.Visibility != Visibility.Visible) this.Visibility = Visibility.Visible;

                    double targetTop = logicalTaskbarTop + (taskbarHeight - 42) / 2 - 10;
                    double targetLeft = (rect.Left / dpiX) + 10;

                    if (this.Top != targetTop) this.Top = targetTop;
                    if (this.Left != targetLeft) this.Left = targetLeft;

                    var helper = new WindowInteropHelper(this);
                    SetWindowPos(helper.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }
                else
                {
                    if (this.Visibility != Visibility.Hidden) this.Visibility = Visibility.Hidden;
                }
            }
        }

        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                DropBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xCA, 0x70));
            }
        }

        private void Window_DragLeave(object sender, DragEventArgs e)
        {
            // CORRECTION: Restaure le lien dynamique avec le thème au lieu de créer un pinceau statique
            DropBorder.SetResourceReference(Border.BorderBrushProperty, "AppBorderBrush");
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            // CORRECTION: Restaure le lien dynamique ici aussi
            DropBorder.SetResourceReference(Border.BorderBrushProperty, "AppBorderBrush");

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0)
                {
                    await UploadFileAsync(files[0]);
                }
            }
        }

        private async Task UploadFileAsync(string filePath)
        {
            if (!File.Exists(filePath)) { await ShowErrorAsync("Introuvable"); return; }
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length > MaxFileSizeMB * 1024 * 1024) { await ShowErrorAsync("Trop lourd"); return; }

            SetState("Uploading");
            ProgressBar.Value = 0;
            await Task.Delay(250);

            try
            {
                using var form = new MultipartFormDataContent();
                using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var streamContent = new StreamContent(fileStream);
                streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                form.Add(streamContent, "file", Path.GetFileName(filePath));

                bool isUploading = true;
                var progressTask = Task.Run(async () =>
                {
                    for (int i = 0; i <= 90 && isUploading; i++)
                    {
                        await Task.Delay(100);
                        Dispatcher.Invoke(() => ProgressBar.Value = i);
                    }
                });

                HttpResponseMessage response = await client.PostAsync(ApiUrl, form);
                string responseText = await response.Content.ReadAsStringAsync();

                isUploading = false;
                await progressTask;
                ProgressBar.Value = 100;
                await Task.Delay(300);

                if (response.IsSuccessStatusCode && ExtractDownloadLink(responseText, out string downloadUrl))
                {
                    Clipboard.SetText(downloadUrl);
                    SetState("Success");

                    if (!string.IsNullOrWhiteSpace(CurrentSettings.DiscordWebhook))
                    {
                        try
                        {
                            var payload = new { content = $"📦 Nouveau fichier Droply : {downloadUrl}" };
                            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                            await client.PostAsync(CurrentSettings.DiscordWebhook, content);
                        }
                        catch { }
                    }

                    await Task.Delay(3000);
                    SetState("Idle");
                    return;
                }
                await ShowErrorAsync("Erreur API");
            }
            catch (Exception) { await ShowErrorAsync("Échec"); }
        }

        private async Task ShowErrorAsync(string errorMessage)
        {
            SetState("Error", errorMessage);
            await Task.Delay(3000);
            SetState("Idle");
        }

        private void SetState(string state, string message = "")
        {
            IdleState.Visibility = Visibility.Collapsed;
            UploadState.Visibility = Visibility.Collapsed;
            SuccessState.Visibility = Visibility.Collapsed;
            ErrorState.Visibility = Visibility.Collapsed;

            double targetWidth = 42;

            switch (state)
            {
                case "Idle": IdleState.Visibility = Visibility.Visible; targetWidth = 42; break;
                case "Uploading": UploadState.Visibility = Visibility.Visible; targetWidth = 140; break;
                case "Success": SuccessState.Visibility = Visibility.Visible; targetWidth = 140; break;
                case "Error": ErrorState.Visibility = Visibility.Visible; ErrorText.Text = message; targetWidth = 140; break;
            }

            AnimateWidth(targetWidth);
        }

        private void AnimateWidth(double targetWidth)
        {
            if (Math.Abs(DropBorder.Width - targetWidth) < 1) return;

            var widthAnimation = new DoubleAnimation
            {
                To = targetWidth,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };

            Timeline.SetDesiredFrameRate(widthAnimation, 60);
            DropBorder.BeginAnimation(FrameworkElement.WidthProperty, widthAnimation);
        }

        private static bool ExtractDownloadLink(string jsonResponse, out string downloadUrl)
        {
            downloadUrl = null;
            try
            {
                using var doc = JsonDocument.Parse(jsonResponse);
                var root = doc.RootElement;
                if (root.TryGetProperty("status", out var statusElement) && statusElement.GetString() == "ok" &&
                    root.TryGetProperty("data", out var dataElement) && dataElement.TryGetProperty("downloadPage", out var downloadElement))
                {
                    downloadUrl = downloadElement.GetString();
                    return !string.IsNullOrEmpty(downloadUrl);
                }
            }
            catch (JsonException) { return false; }
            return false;
        }
    }
}
