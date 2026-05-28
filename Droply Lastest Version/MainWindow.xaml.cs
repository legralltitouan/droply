// =============================================================================
//  Droply - MainWindow.xaml.cs
// -----------------------------------------------------------------------------
//  Logique principale de la pastille flottante :
//   - Routage des uploads selon la taille du fichier
//       <= 2 GB  : gofile.io        (stockage permanent, simple multipart)
//       <= 25 GB : storage.to       (multipart présigné Cloudflare R2)
//       <= 100 GB: pixeldrain.com   (seul à supporter cette taille)
//   - Positionnement collé à la barre des tâches Windows (throttle 33 ms)
//     + comportement "toujours Visible" : la fenêtre est déplacée hors-écran
//       plutôt que masquée pour ne jamais perdre son z-order.
//   - Persistance des réglages (settings.json) + démarrage Windows (HKCU Run).
//   - Live-switch thème (clair/sombre) et langue (FR/EN).
//   - Notification webhook Discord + sync cross-PC via DiscordSync.
// =============================================================================

using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

namespace Droply
{
    public class AppSettings
    {
        /// <summary>Démarrage automatique avec Windows (clé HKCU\...\Run).</summary>
        public bool RunAtStartup { get; set; } = true;

        /// <summary>Thème clair (true) ou sombre (false).</summary>
        public bool IsLightTheme { get; set; } = false;

        /// <summary>Webhook Discord pour notifier chaque upload terminé.</summary>
        public string DiscordWebhook { get; set; } = string.Empty;

        /// <summary>Identifiant anonyme stable utilisé par storage.to (preuve d'ownership).</summary>
        public string VisitorToken { get; set; } = string.Empty;

        /// <summary>Langue de l'interface : "fr" ou "en".</summary>
        public string Language { get; set; } = "fr";

        /// <summary>Token du bot Discord (sync cross-PC).</summary>
        public string DiscordBotToken { get; set; } = string.Empty;

        /// <summary>Identifiant du salon Discord utilisé pour la sync cross-PC.</summary>
        public string DiscordChannelId { get; set; } = string.Empty;
    }

    public partial class MainWindow : Window
    {
        #region --- P/Invoke (Win32) ---

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
                                                int X, int Y, int cx, int cy, uint uFlags);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;

        #endregion

        #region --- HttpClient & limites des services ---

        private static readonly HttpClient client = new HttpClient();

        private const long GofileLimit = 2L * 1024 * 1024 * 1024;
        private const long StorageToLimit = 25L * 1024 * 1024 * 1024;
        private const long PixeldrainLimit = 100L * 1024 * 1024 * 1024;

        private const string GofileUploadUrl = "https://store1.gofile.io/uploadFile";
        private const string PixeldrainUploadUrl = "https://pixeldrain.com/api/file";
        private const string StorageToApiBase = "https://storage.to/api";

        #endregion

        #region --- État interne ---

        public AppSettings CurrentSettings { get; private set; } = new AppSettings();
        private readonly string settingsPath;
        private DateTime _lastPositionCheck = DateTime.MinValue;
        private FrameworkElement? _currentVisibleState;

        #endregion

        #region --- Constructeurs & initialisation ---

        static MainWindow()
        {
            client.Timeout = TimeSpan.FromHours(2);
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("User-Agent", "Droply/1.0");
        }

        public MainWindow()
        {
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Droply");
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
            DiscordSync.Start(this);
        }

        #endregion

        #region --- Persistance des préférences ---

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
            catch
            {
                CurrentSettings = new AppSettings();
            }

            if (string.IsNullOrEmpty(CurrentSettings.VisitorToken))
            {
                CurrentSettings.VisitorToken = Guid.NewGuid().ToString("N");
                TrySaveSettingsFile();
            }

            Localization.ApplyLanguage(CurrentSettings.Language);
            ToggleStartup(CurrentSettings.RunAtStartup);
            SetTheme(CurrentSettings.IsLightTheme);
        }

        public void SaveSettings()
        {
            TrySaveSettingsFile();
            Localization.ApplyLanguage(CurrentSettings.Language);
            ToggleStartup(CurrentSettings.RunAtStartup);
            SetTheme(CurrentSettings.IsLightTheme);
        }

        private void TrySaveSettingsFile()
        {
            try { File.WriteAllText(settingsPath, JsonSerializer.Serialize(CurrentSettings)); }
            catch { }
        }

        #endregion

        #region --- Démarrage Windows (HKCU\...\Run) ---

        public void ToggleStartup(bool enable)
        {
            try
            {
                const string runKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(runKey, true);
                if (key == null) return;

                if (enable)
                {
                    string? exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exePath))
                        key.SetValue("Droply", exePath);
                }
                else
                {
                    key.DeleteValue("Droply", false);
                }
            }
            catch { }
        }

        #endregion

        #region --- Thème clair / sombre (live) ---

        public void SetTheme(bool isLight)
        {
            var palette = isLight
                ? new Dictionary<string, Color>
                {
                    ["AppBgBrush"] = Color.FromRgb(243, 243, 243),
                    ["AppBorderBrush"] = Color.FromRgb(200, 200, 200),
                    ["AppHoverBrush"] = Color.FromArgb(80, 200, 200, 200),
                    ["AppTextBrush"] = Colors.Black,
                    ["AppSubTextBrush"] = Color.FromRgb(100, 100, 100),
                    ["AppControlBgBrush"] = Color.FromRgb(220, 220, 220),
                }
                : new Dictionary<string, Color>
                {
                    ["AppBgBrush"] = Color.FromRgb(42, 44, 49),
                    ["AppBorderBrush"] = Color.FromRgb(62, 64, 71),
                    ["AppHoverBrush"] = Color.FromArgb(80, 54, 55, 55),
                    ["AppTextBrush"] = Colors.White,
                    ["AppSubTextBrush"] = Color.FromRgb(163, 163, 163),
                    ["AppControlBgBrush"] = Color.FromRgb(28, 29, 33),
                };

            var res = Application.Current.Resources;
            foreach (var kv in palette)
                res[kv.Key] = new SolidColorBrush(kv.Value);
        }

        #endregion

        #region --- Menu contextuel ---

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWin = new SettingsWindow(this);
            settingsWin.Show();
        }

        private void Quit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

        #endregion

        #region --- Positionnement collé à la barre des tâches ---

        private void OnRendering(object? sender, EventArgs e)
        {
            if ((DateTime.Now - _lastPositionCheck).TotalMilliseconds < 33) return;
            _lastPositionCheck = DateTime.Now;

            IntPtr taskbarHwnd = FindWindow("Shell_TrayWnd", null);
            if (taskbarHwnd == IntPtr.Zero) return;
            GetWindowRect(taskbarHwnd, out RECT rect);

            PresentationSource? source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget == null) return;

            double dpiX = source.CompositionTarget.TransformToDevice.M11;
            double dpiY = source.CompositionTarget.TransformToDevice.M22;

            double logicalTaskbarTop = rect.Top / dpiY;
            double logicalTaskbarBottom = rect.Bottom / dpiY;
            double taskbarHeight = logicalTaskbarBottom - logicalTaskbarTop;
            double logicalScreenHeight = SystemParameters.PrimaryScreenHeight;

            bool isTaskbarVisible = logicalTaskbarTop < (logicalScreenHeight - 5);

            // On garde la fenêtre TOUJOURS Visible pour ne pas perdre le z-order
            // quand la taskbar (auto-hide) réapparaît via la touche Windows.
            if (this.Visibility != Visibility.Visible)
                this.Visibility = Visibility.Visible;

            double targetTop, targetLeft;
            if (isTaskbarVisible)
            {
                targetTop = logicalTaskbarTop + (taskbarHeight - 42) / 2 - 10;
                targetLeft = (rect.Left / dpiX) + 10;
            }
            else
            {
                // Hors-écran : la fenêtre reste vivante mais invisible visuellement.
                targetTop = logicalScreenHeight + 200;
                targetLeft = -500;
            }

            if (this.Top != targetTop) this.Top = targetTop;
            if (this.Left != targetLeft) this.Left = targetLeft;

            var helper = new WindowInteropHelper(this);
            SetWindowPos(helper.Handle, HWND_TOPMOST, 0, 0, 0, 0,
                         SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        #endregion

        #region --- Drag & Drop ---

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

            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;

            await UploadFileAsync(files[0]);
        }

        #endregion

        #region --- Routage principal des uploads ---

        private async Task UploadFileAsync(string filePath)
        {
            if (!File.Exists(filePath)) { await ShowErrorAsync(Localization.Get("L.Error.NotFound")); return; }

            var fileInfo = new FileInfo(filePath);
            long size = fileInfo.Length;
            if (size > PixeldrainLimit) { await ShowErrorAsync(Localization.Get("L.Error.TooLarge")); return; }

            SetState("Uploading");
            ProgressBar.Value = 0;
            await Task.Delay(250);

            string? downloadUrl = null;
            string serviceUsed = "";
            try
            {
                if (size <= GofileLimit) { serviceUsed = "Gofile"; downloadUrl = await UploadToGofileAsync(filePath); }
                else if (size <= StorageToLimit) { serviceUsed = "storage.to"; downloadUrl = await UploadToStorageToAsync(filePath, fileInfo); }
                else { serviceUsed = "Pixeldrain"; downloadUrl = await UploadToPixeldrainAsync(filePath); }
            }
            catch { downloadUrl = null; }

            if (string.IsNullOrEmpty(downloadUrl))
            {
                await ShowErrorAsync(Localization.Get("L.Error.Failed"));
                return;
            }

            ProgressBar.Value = 100;
            await Task.Delay(250);

            try { Clipboard.SetText(downloadUrl); } catch { }
            SetState("Success");

            await NotifyDiscordWebhookAsync(serviceUsed, downloadUrl);

            await Task.Delay(3000);
            SetState("Idle");
        }

        private async Task NotifyDiscordWebhookAsync(string serviceUsed, string downloadUrl)
        {
            if (string.IsNullOrWhiteSpace(CurrentSettings.DiscordWebhook)) return;
            try
            {
                var payload = new
                {
                    content = $"{Localization.Get("L.Webhook.NewFile")} ({serviceUsed}) {DiscordSync.MachineTag} : {downloadUrl}"
                };
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                await client.PostAsync(CurrentSettings.DiscordWebhook, content);
            }
            catch { }
        }

        #endregion

        #region --- Provider 1 : Gofile (<= 2 GB) ---

        private async Task<string?> UploadToGofileAsync(string filePath)
        {
            using var form = new MultipartFormDataContent();
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var streamContent = new StreamContent(fileStream);
            streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(streamContent, "file", Path.GetFileName(filePath));

            var progressFlag = new ProgressFlag();
            var progressTask = StartFakeProgressAsync(progressFlag, stepDelayMs: 100);

            HttpResponseMessage response = await client.PostAsync(GofileUploadUrl, form);
            string responseText = await response.Content.ReadAsStringAsync();

            progressFlag.Stop = true;
            await progressTask;

            if (!response.IsSuccessStatusCode) return null;
            try
            {
                using var doc = JsonDocument.Parse(responseText);
                var root = doc.RootElement;
                if (root.TryGetProperty("status", out var statusElement) && statusElement.GetString() == "ok" &&
                    root.TryGetProperty("data", out var dataElement) &&
                    dataElement.TryGetProperty("downloadPage", out var downloadElement))
                {
                    string? url = downloadElement.GetString();
                    return string.IsNullOrEmpty(url) ? null : url;
                }
            }
            catch (JsonException) { }
            return null;
        }

        #endregion

        #region --- Provider 2 : Pixeldrain (25 GB < x <= 100 GB) ---

        private async Task<string?> UploadToPixeldrainAsync(string filePath)
        {
            using var form = new MultipartFormDataContent();
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var streamContent = new StreamContent(fileStream);
            streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(streamContent, "file", Path.GetFileName(filePath));

            var progressFlag = new ProgressFlag();
            var progressTask = StartFakeProgressAsync(progressFlag, stepDelayMs: 200);

            HttpResponseMessage response = await client.PostAsync(PixeldrainUploadUrl, form);
            string responseText = await response.Content.ReadAsStringAsync();

            progressFlag.Stop = true;
            await progressTask;

            if (!response.IsSuccessStatusCode) return null;
            try
            {
                using var doc = JsonDocument.Parse(responseText);
                if (doc.RootElement.TryGetProperty("id", out var idElement))
                {
                    string? id = idElement.GetString();
                    if (!string.IsNullOrEmpty(id))
                        return $"https://pixeldrain.com/u/{id}";
                }
            }
            catch (JsonException) { }
            return null;
        }

        #endregion

        #region --- Provider 3 : storage.to (multipart R2 présigné) ---

        private async Task<string?> UploadToStorageToAsync(string filePath, FileInfo fileInfo)
        {
            string filename = Path.GetFileName(filePath);
            string contentType = "application/octet-stream";
            long size = fileInfo.Length;
            string visitor = CurrentSettings.VisitorToken;

            // 1) /upload/init
            string r2Key;
            string uploadType;
            string? uploadId = null;
            long partSize = 0;
            int totalParts = 0;
            string? singleUploadUrl = null;
            var partUrls = new Dictionary<int, string>();

            var initPayload = new { filename, content_type = contentType, size };
            using (var initRes = await PostJsonToStorageToAsync("/upload/init", initPayload, visitor))
            {
                if (initRes == null || !initRes.IsSuccessStatusCode) return null;

                string initBody = await initRes.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(initBody);
                var root = doc.RootElement;
                if (!root.TryGetProperty("success", out var ok) || !ok.GetBoolean()) return null;

                uploadType = root.GetProperty("type").GetString() ?? "single";
                r2Key = root.GetProperty("r2_key").GetString() ?? "";

                if (uploadType == "multipart")
                {
                    uploadId = root.GetProperty("upload_id").GetString();
                    partSize = root.GetProperty("part_size").GetInt64();
                    totalParts = root.GetProperty("total_parts").GetInt32();

                    if (root.TryGetProperty("initial_urls", out var urls) && urls.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var p in urls.EnumerateObject())
                            if (int.TryParse(p.Name, out int pn))
                                partUrls[pn] = p.Value.GetString() ?? "";
                    }
                }
                else
                {
                    singleUploadUrl = root.GetProperty("upload_url").GetString();
                }
            }

            // 2) PUT bytes
            var partsForComplete = new List<object>();

            if (uploadType == "single")
            {
                if (string.IsNullOrEmpty(singleUploadUrl)) return null;

                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var putReq = new HttpRequestMessage(HttpMethod.Put, singleUploadUrl) { Content = new StreamContent(fs) };
                putReq.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                putReq.Content.Headers.ContentLength = size;

                using var putRes = await client.SendAsync(putReq);
                if (!putRes.IsSuccessStatusCode) return null;
                Dispatcher.Invoke(() => ProgressBar.Value = 90);
            }
            else
            {
                if (string.IsNullOrEmpty(uploadId) || totalParts <= 0 || partSize <= 0) return null;

                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                byte[] buffer = new byte[partSize];

                for (int partNumber = 1; partNumber <= totalParts; partNumber++)
                {
                    long remaining = size - fs.Position;
                    int toRead = (int)Math.Min(partSize, remaining);
                    int read = 0;
                    while (read < toRead)
                    {
                        int n = await fs.ReadAsync(buffer.AsMemory(read, toRead - read));
                        if (n == 0) break;
                        read += n;
                    }
                    if (read == 0) return null;

                    if (!partUrls.TryGetValue(partNumber, out string? partUrl) || string.IsNullOrEmpty(partUrl))
                    {
                        partUrl = await StorageToFetchPartUrlAsync(uploadId, partNumber);
                        if (string.IsNullOrEmpty(partUrl)) return null;
                        partUrls[partNumber] = partUrl;
                    }

                    using var putReq = new HttpRequestMessage(HttpMethod.Put, partUrl) { Content = new ByteArrayContent(buffer, 0, read) };
                    putReq.Content.Headers.ContentLength = read;

                    using var putRes = await client.SendAsync(putReq);
                    if (!putRes.IsSuccessStatusCode) return null;

                    string etag = putRes.Headers.ETag?.Tag ?? "";
                    if (string.IsNullOrEmpty(etag) && putRes.Headers.TryGetValues("ETag", out var vals))
                        etag = vals.FirstOrDefault() ?? "";
                    if (string.IsNullOrEmpty(etag)) return null;

                    partsForComplete.Add(new { partNumber, etag });

                    int progress = (int)((double)partNumber / totalParts * 90.0);
                    Dispatcher.Invoke(() => ProgressBar.Value = progress);
                }

                // 3a) /upload/complete-multipart
                var completePayload = new { upload_id = uploadId, parts = partsForComplete };
                using var compRes = await PostJsonToStorageToAsync("/upload/complete-multipart", completePayload, visitor);
                if (compRes == null || !compRes.IsSuccessStatusCode) return null;
            }

            // 3b) /upload/confirm
            var confirmPayload = new { filename, size, content_type = contentType, r2_key = r2Key };
            using var confRes = await PostJsonToStorageToAsync("/upload/confirm", confirmPayload, visitor);
            if (confRes == null || !confRes.IsSuccessStatusCode) return null;

            string confBody = await confRes.Content.ReadAsStringAsync();
            try
            {
                using var doc = JsonDocument.Parse(confBody);
                if (doc.RootElement.TryGetProperty("file", out var fileEl) &&
                    fileEl.TryGetProperty("url", out var urlEl))
                {
                    return urlEl.GetString();
                }
            }
            catch (JsonException) { }
            return null;
        }

        private async Task<string?> StorageToFetchPartUrlAsync(string uploadId, int partNumber)
        {
            var payload = new { upload_id = uploadId, part_numbers = new[] { partNumber } };
            using var res = await PostJsonToStorageToAsync("/upload/parts", payload, CurrentSettings.VisitorToken);
            if (res == null || !res.IsSuccessStatusCode) return null;

            string body = await res.Content.ReadAsStringAsync();
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("part_urls", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in arr.EnumerateArray())
                    {
                        if (item.TryGetProperty("partNumber", out var pn) && pn.GetInt32() == partNumber &&
                            item.TryGetProperty("url", out var u))
                        {
                            return u.GetString();
                        }
                    }
                }
            }
            catch (JsonException) { }
            return null;
        }

        /// <summary>Helper : POST JSON sur l'API storage.to avec l'en-tête X-Visitor-Token.</summary>
        private async Task<HttpResponseMessage?> PostJsonToStorageToAsync(string relativePath, object payload, string visitorToken)
        {
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Post, $"{StorageToApiBase}{relativePath}");
                req.Headers.TryAddWithoutValidation("X-Visitor-Token", visitorToken);
                req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                return await client.SendAsync(req);
            }
            catch { return null; }
        }

        #endregion

        #region --- Progression simulée (Gofile / Pixeldrain) ---

        private sealed class ProgressFlag { public bool Stop; }

        private Task StartFakeProgressAsync(ProgressFlag flag, int stepDelayMs)
        {
            return Task.Run(async () =>
            {
                for (int i = 0; i <= 90 && !flag.Stop; i++)
                {
                    await Task.Delay(stepDelayMs);
                    Dispatcher.Invoke(() => ProgressBar.Value = i);
                }
            });
        }

        #endregion

        #region --- Machine à états visuelle ---

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
                fadeOut.Completed += (s, e) => oldState.Visibility = Visibility.Collapsed;
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

        #endregion
    }
}