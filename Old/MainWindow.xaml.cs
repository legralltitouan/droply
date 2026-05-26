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
        // Correction de la signature : lpWindowName peut être null
        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

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

        // CS8618 : On initialise la propriété par défaut pour éviter qu'elle soit nulle
        public AppSettings CurrentSettings { get; private set; } = new AppSettings();
        private readonly string settingsPath;

        private DateTime _lastPositionCheck = DateTime.MinValue;

        // On indique explicitement que _currentVisibleState peut être nul (?)
        private FrameworkElement? _currentVisibleState;

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
            _currentVisibleState = IdleState;
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
                // On indique que le registre peut renvoyer null (?)
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(runKey, true);
                if (key != null)
                {
                    if (enable)
                    {
                        // On vérifie que le chemin du processus n'est pas null avant de l'ajouter
                        string? exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                        if (!string.IsNullOrEmpty(exePath))
                        {
                            key.SetValue("Droply", exePath);
                        }
                    }
                    else
                    {
                        key.DeleteValue("Droply", false);
                    }
                }
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

        // CS8622 : Ajout du '?' à object sender
        private void OnRendering(object? sender, EventArgs e)
        {
            if ((DateTime.Now - _lastPositionCheck).TotalMilliseconds < 33) return;
            _lastPositionCheck = DateTime.Now;

            IntPtr taskbarHwnd = FindWindow("Shell_TrayWnd", null);
            if (taskbarHwnd != IntPtr.Zero)
            {
                GetWindowRect(taskbarHwnd, out RECT rect);

                // CS8600 : PresentationSource peut renvoyer null
                PresentationSource? source = PresentationSource.FromVisual(this);
                if (source == null || source.CompositionTarget == null) return;

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
            DropBorder.ClearValue(Border.BorderBrushProperty);
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            DropBorder.ClearValue(Border.BorderBrushProperty);

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                // CS8600 : Ajout du '?' pour le tableau de fichiers
                string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[];
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

                // CS8625 : Ajout du string? pour recevoir la valeur qui peut être nulle
                if (response.IsSuccessStatusCode && ExtractDownloadLink(responseText, out string? downloadUrl) && !string.IsNullOrEmpty(downloadUrl))
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
            if (_currentVisibleState == null) _currentVisibleState = IdleState;

            FrameworkElement nextState = state switch
            {
                "Idle" => IdleState,
                "Uploading" => UploadState,
                "Success" => SuccessState,
                "Error" => ErrorState,
                _ => IdleState
            };

            if (state == "Error") ErrorText.Text = message;
            double targetWidth = (state == "Idle") ? 42 : 140;

            AnimateStateTransition(nextState, targetWidth);
        }

        private void AnimateStateTransition(FrameworkElement nextState, double targetWidth)
        {
            var widthAnimation = new DoubleAnimation
            {
                To = targetWidth,
                Duration = TimeSpan.FromMilliseconds(280),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            Timeline.SetDesiredFrameRate(widthAnimation, 60);
            DropBorder.BeginAnimation(FrameworkElement.WidthProperty, widthAnimation);

            if (_currentVisibleState != null && _currentVisibleState != nextState)
            {
                var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120));
                var oldState = _currentVisibleState;
                fadeOut.Completed += (s, e) => { oldState.Visibility = Visibility.Collapsed; };
                oldState.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }

            nextState.Visibility = Visibility.Visible;
            var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(180))
            {
                BeginTime = TimeSpan.FromMilliseconds(60)
            };
            nextState.BeginAnimation(UIElement.OpacityProperty, fadeIn);

            _currentVisibleState = nextState;
        }

        // CS8625 : Modification du type "out string downloadUrl" en "out string? downloadUrl"
        private static bool ExtractDownloadLink(string jsonResponse, out string? downloadUrl)
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
