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

namespace QuickSend
{
    public class AppSettings
    {
        public bool RunAtStartup { get; set; } = true;
        public bool IsLightTheme { get; set; } = false;
        public string DiscordWebhook { get; set; } = string.Empty;
        // Identifiant anonyme stable pour les uploads storage.to (preuve d'ownership)
        public string VisitorToken { get; set; } = string.Empty;

        public string Language { get; set; } = "fr";   // <-- NOUVEAU : "fr" ou "en"

        public string DiscordBotToken { get; set; } = string.Empty;
        public string DiscordChannelId { get; set; } = string.Empty;
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

        // ---- Limites par service (bytes) ----
        // ≤ 2 GB             -> gofile.io      (par défaut, stockage permanent)
        // 2 GB < x ≤ 25 GB   -> storage.to     (multipart présigné Cloudflare R2)
        // 25 GB < x ≤ 100 GB -> pixeldrain.com (seul à supporter cette taille)
        private const long GofileLimit = 2L * 1024 * 1024 * 1024;
        private const long StorageToLimit = 25L * 1024 * 1024 * 1024;
        private const long PixeldrainLimit = 100L * 1024 * 1024 * 1024;

        private const string GofileUploadUrl = "https://store1.gofile.io/uploadFile";
        private const string PixeldrainUploadUrl = "https://pixeldrain.com/api/file";
        private const string StorageToApiBase = "https://storage.to/api";

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
            DiscordSync.Start(this);
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

            // Génère un VisitorToken stable la première fois (pour storage.to)
            if (string.IsNullOrEmpty(CurrentSettings.VisitorToken))
            {
                CurrentSettings.VisitorToken = Guid.NewGuid().ToString("N");
                try
                {
                    File.WriteAllText(settingsPath, JsonSerializer.Serialize(CurrentSettings));
                }
                catch { }
            }

            // Applique la langue dès le chargement (les ressources DynamicResource se remplissent)
            Localization.ApplyLanguage(CurrentSettings.Language);

            ToggleStartup(CurrentSettings.RunAtStartup);
            SetTheme(CurrentSettings.IsLightTheme);
        }

        public void SaveSettings()
        {
            try
            {
                string json = JsonSerializer.Serialize(CurrentSettings);
                File.WriteAllText(settingsPath, json);

                Localization.ApplyLanguage(CurrentSettings.Language);


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
            if (taskbarHwnd == IntPtr.Zero) return;

            GetWindowRect(taskbarHwnd, out RECT rect);

            PresentationSource? source = PresentationSource.FromVisual(this);
            if (source == null || source.CompositionTarget == null) return;

            double dpiY = source.CompositionTarget.TransformToDevice.M22;
            double dpiX = source.CompositionTarget.TransformToDevice.M11;

            double logicalTaskbarTop = rect.Top / dpiY;
            double logicalTaskbarBottom = rect.Bottom / dpiY;
            double taskbarHeight = logicalTaskbarBottom - logicalTaskbarTop;
            double logicalScreenHeight = SystemParameters.PrimaryScreenHeight;

            bool isTaskbarVisible = logicalTaskbarTop < (logicalScreenHeight - 5);

            // FIX : on garde la fenêtre TOUJOURS Visible pour ne pas perdre le z-order
            // quand la taskbar (auto-hide) réapparaît via la touche Windows.
            // On la déplace simplement hors-écran si la taskbar est cachée.
            if (this.Visibility != Visibility.Visible) this.Visibility = Visibility.Visible;

            double targetTop, targetLeft;
            if (isTaskbarVisible)
            {
                targetTop = logicalTaskbarTop + (taskbarHeight - 42) / 2 - 10;
                targetLeft = (rect.Left / dpiX) + 10;
            }
            else
            {
                // Hors-écran : la fenêtre reste vivante mais invisible visuellement
                targetTop = logicalScreenHeight + 200;
                targetLeft = -500;
            }

            if (this.Top != targetTop) this.Top = targetTop;
            if (this.Left != targetLeft) this.Left = targetLeft;

            var helper = new WindowInteropHelper(this);
            SetWindowPos(helper.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
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

        // ----------------------------------------------------------------------
        //  ROUTAGE PRINCIPAL : sélection du service d'upload optimal selon la taille
        // ----------------------------------------------------------------------
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
                if (size <= GofileLimit)
                {
                    serviceUsed = "Gofile";
                    downloadUrl = await UploadToGofileAsync(filePath);
                }
                else if (size <= StorageToLimit)
                {
                    serviceUsed = "storage.to";
                    downloadUrl = await UploadToStorageToAsync(filePath, fileInfo);
                }
                else
                {
                    serviceUsed = "Pixeldrain";
                    downloadUrl = await UploadToPixeldrainAsync(filePath);
                }
            }
            catch (Exception)
            {
                downloadUrl = null;
            }

            if (!string.IsNullOrEmpty(downloadUrl))
            {
                ProgressBar.Value = 100;
                await Task.Delay(250);

                try { Clipboard.SetText(downloadUrl); } catch { }
                SetState("Success");

                if (!string.IsNullOrWhiteSpace(CurrentSettings.DiscordWebhook))
                {
                    try
                    {
                        var payload = new { content = $"{Localization.Get("L.Webhook.NewFile")} ({serviceUsed}) {DiscordSync.MachineTag} : {downloadUrl}" };
                        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                        await client.PostAsync(CurrentSettings.DiscordWebhook, content);
                    }
                    catch { }
                }

                await Task.Delay(3000);
                SetState("Idle");
                return;
            }

            await ShowErrorAsync(Localization.Get("L.Error.Failed"));
        }

        // ----------------------------------------------------------------------
        //  GOFILE.IO  (≤ 2 GB)
        // ----------------------------------------------------------------------
        private async Task<string?> UploadToGofileAsync(string filePath)
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

            HttpResponseMessage response = await client.PostAsync(GofileUploadUrl, form);
            string responseText = await response.Content.ReadAsStringAsync();

            isUploading = false;
            await progressTask;

            if (!response.IsSuccessStatusCode) return null;

            try
            {
                using var doc = JsonDocument.Parse(responseText);
                var root = doc.RootElement;
                if (root.TryGetProperty("status", out var statusElement) && statusElement.GetString() == "ok" &&
                    root.TryGetProperty("data", out var dataElement) && dataElement.TryGetProperty("downloadPage", out var downloadElement))
                {
                    string? url = downloadElement.GetString();
                    return string.IsNullOrEmpty(url) ? null : url;
                }
            }
            catch (JsonException) { }
            return null;
        }

        // ----------------------------------------------------------------------
        //  PIXELDRAIN  (25 GB < x ≤ 100 GB)
        //  POST https://pixeldrain.com/api/file (multipart) -> { "id": "..." }
        //  URL publique : https://pixeldrain.com/u/{id}
        // ----------------------------------------------------------------------
        private async Task<string?> UploadToPixeldrainAsync(string filePath)
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
                    await Task.Delay(200);
                    Dispatcher.Invoke(() => ProgressBar.Value = i);
                }
            });

            HttpResponseMessage response = await client.PostAsync(PixeldrainUploadUrl, form);
            string responseText = await response.Content.ReadAsStringAsync();

            isUploading = false;
            await progressTask;

            if (!response.IsSuccessStatusCode) return null;

            try
            {
                using var doc = JsonDocument.Parse(responseText);
                if (doc.RootElement.TryGetProperty("id", out var idElement))
                {
                    string? id = idElement.GetString();
                    if (!string.IsNullOrEmpty(id))
                    {
                        return $"https://pixeldrain.com/u/{id}";
                    }
                }
            }
            catch (JsonException) { }
            return null;
        }

        // ----------------------------------------------------------------------
        //  STORAGE.TO  (2 GB < x ≤ 25 GB)
        //  3 étapes : init -> PUT bytes (single ou multipart) -> confirm
        //  La progression réelle est calculée par parts uploadées (multipart).
        // ----------------------------------------------------------------------
        private async Task<string?> UploadToStorageToAsync(string filePath, FileInfo fileInfo)
        {
            string filename = Path.GetFileName(filePath);
            string contentType = "application/octet-stream";
            long size = fileInfo.Length;
            string visitor = CurrentSettings.VisitorToken;

            // ---- 1) /upload/init ----
            string r2Key;
            string uploadType;
            string? uploadId = null;
            long partSize = 0;
            int totalParts = 0;
            Dictionary<int, string> partUrls = new Dictionary<int, string>();
            string? singleUploadUrl = null;

            var initPayload = new { filename, content_type = contentType, size };
            using (var initReq = new HttpRequestMessage(HttpMethod.Post, $"{StorageToApiBase}/upload/init"))
            {
                initReq.Headers.TryAddWithoutValidation("X-Visitor-Token", visitor);
                initReq.Content = new StringContent(JsonSerializer.Serialize(initPayload), Encoding.UTF8, "application/json");

                using var initRes = await client.SendAsync(initReq);
                if (!initRes.IsSuccessStatusCode) return null;
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
                        {
                            if (int.TryParse(p.Name, out int pn))
                            {
                                partUrls[pn] = p.Value.GetString() ?? "";
                            }
                        }
                    }
                }
                else
                {
                    singleUploadUrl = root.GetProperty("upload_url").GetString();
                }
            }

            // ---- 2) PUT bytes ----
            var partsForComplete = new List<object>();

            if (uploadType == "single")
            {
                if (string.IsNullOrEmpty(singleUploadUrl)) return null;

                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var putReq = new HttpRequestMessage(HttpMethod.Put, singleUploadUrl);
                putReq.Content = new StreamContent(fs);
                putReq.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                putReq.Content.Headers.ContentLength = size;

                using var putRes = await client.SendAsync(putReq);
                if (!putRes.IsSuccessStatusCode) return null;
                Dispatcher.Invoke(() => ProgressBar.Value = 90);
            }
            else // multipart
            {
                if (string.IsNullOrEmpty(uploadId) || totalParts <= 0 || partSize <= 0) return null;

                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                byte[] buffer = new byte[partSize];

                for (int partNumber = 1; partNumber <= totalParts; partNumber++)
                {
                    // Calcule la taille réelle de cette part (la dernière peut être plus petite)
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

                    // Récupère l'URL pour cette part (ou en redemande)
                    if (!partUrls.TryGetValue(partNumber, out string? partUrl) || string.IsNullOrEmpty(partUrl))
                    {
                        partUrl = await StorageToFetchPartUrlAsync(uploadId, partNumber);
                        if (string.IsNullOrEmpty(partUrl)) return null;
                        partUrls[partNumber] = partUrl;
                    }

                    using var putReq = new HttpRequestMessage(HttpMethod.Put, partUrl);
                    putReq.Content = new ByteArrayContent(buffer, 0, read);
                    putReq.Content.Headers.ContentLength = read;

                    using var putRes = await client.SendAsync(putReq);
                    if (!putRes.IsSuccessStatusCode) return null;

                    // R2 renvoie l'ETag qu'on doit transmettre à complete-multipart
                    string etag = putRes.Headers.ETag?.Tag ?? "";
                    if (string.IsNullOrEmpty(etag))
                    {
                        if (putRes.Headers.TryGetValues("ETag", out var vals))
                        {
                            etag = vals.FirstOrDefault() ?? "";
                        }
                    }
                    if (string.IsNullOrEmpty(etag)) return null;

                    partsForComplete.Add(new { partNumber, etag });

                    // Progression réelle : 0 -> 90 % réservé à l'upload, 90 -> 100 % au confirm
                    int progress = (int)((double)partNumber / totalParts * 90.0);
                    Dispatcher.Invoke(() => ProgressBar.Value = progress);
                }

                // ---- 3a) /upload/complete-multipart ----
                var completePayload = new { upload_id = uploadId, parts = partsForComplete };
                using var compReq = new HttpRequestMessage(HttpMethod.Post, $"{StorageToApiBase}/upload/complete-multipart");
                compReq.Headers.TryAddWithoutValidation("X-Visitor-Token", visitor);
                compReq.Content = new StringContent(JsonSerializer.Serialize(completePayload), Encoding.UTF8, "application/json");

                using var compRes = await client.SendAsync(compReq);
                if (!compRes.IsSuccessStatusCode) return null;
            }

            // ---- 3b) /upload/confirm -> URL finale ----
            var confirmPayload = new { filename, size, content_type = contentType, r2_key = r2Key };
            using var confReq = new HttpRequestMessage(HttpMethod.Post, $"{StorageToApiBase}/upload/confirm");
            confReq.Headers.TryAddWithoutValidation("X-Visitor-Token", visitor);
            confReq.Content = new StringContent(JsonSerializer.Serialize(confirmPayload), Encoding.UTF8, "application/json");

            using var confRes = await client.SendAsync(confReq);
            if (!confRes.IsSuccessStatusCode) return null;
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

        // Demande une URL présignée supplémentaire pour une part donnée (storage.to multipart)
        private async Task<string?> StorageToFetchPartUrlAsync(string uploadId, int partNumber)
        {
            var payload = new { upload_id = uploadId, part_numbers = new[] { partNumber } };
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{StorageToApiBase}/upload/parts");
            req.Headers.TryAddWithoutValidation("X-Visitor-Token", CurrentSettings.VisitorToken);
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var res = await client.SendAsync(req);
            if (!res.IsSuccessStatusCode) return null;
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
    }
}