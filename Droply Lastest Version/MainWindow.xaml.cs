// =============================================================================
//  Droply - MainWindow.xaml.cs
// -----------------------------------------------------------------------------
//  Logique principale de la pastille flottante :
//   - Upload via gofile.io UNIQUEMENT (nouvelle API globale upload.gofile.io).
//     -> Pas de limite officielle de taille : la seule limite est théorique
//        (espace disque côté serveur / temps d'upload côté client).
//        Confirmé en pratique pour des fichiers de 100 GB et plus.
//   - Support multi-fichiers : drop de N fichiers -> upload séquentiel,
//     toutes les URLs sont copiées dans le presse-papier (une par ligne).
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
using System.Net;       // ← AJOUTER (pour ServicePointManager, DecompressionMethods)
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading; // ← AJOUTER (pour Timeout.InfiniteTimeSpan)
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

        /// <summary>Langue de l'interface : "fr" ou "en".</summary>
        public string Language { get; set; } = "fr";

        /// <summary>Token du bot Discord (sync cross-PC).</summary>
        public string DiscordBotToken { get; set; } = string.Empty;

        /// <summary>Identifiant du salon Discord utilisé pour la sync cross-PC.</summary>
        public string DiscordChannelId { get; set; } = string.Empty;

        /// Total cumulé d'octets uploadés depuis l'installation.
        public long TotalBytesUploaded { get; set; } = 0;

        /// Total d'octets uploadés sur la journée courante.
        public long BytesUploadedToday { get; set; } = 0;

        /// Date (yyyy-MM-dd) à laquelle BytesUploadedToday a été mis à jour.
        public string LastUploadDate { get; set; } = "";
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

        #region --- HttpClient (timeout infini, handler tuné gros fichiers) ---

        private static readonly HttpClient client = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                MaxRequestContentBufferSize = int.MaxValue,
            };

            var c = new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan, // ← LE FIX CLÉ
            };
            c.DefaultRequestHeaders.Clear();
            c.DefaultRequestHeaders.Add("User-Agent", "Droply/1.0");
            c.DefaultRequestHeaders.ExpectContinue = false;
            return c;
        }

        // API globale Gofile : routage régional automatique, aucune limite officielle
        // de taille de fichier (vérifié pour 100 GB+).
        private const string GofileUploadUrl = "https://upload.gofile.io/uploadfile";

        #endregion

        #region --- État interne ---

        public AppSettings CurrentSettings { get; private set; } = new AppSettings();
        private readonly string settingsPath;
        private DateTime _lastPositionCheck = DateTime.MinValue;
        private FrameworkElement? _currentVisibleState;
        private bool _isUploading = false; // verrou pour empêcher 2 batchs en parallèle

        private long _lastBytesSent;
        private DateTime _lastSpeedSample = DateTime.MinValue;
        private double _currentSpeedBps;       // octets/sec
        private long _currentTotalSize;      // taille du fichier en cours
        private long _currentBytesSent;      // octets déjà envoyés

        #endregion

        #region --- Constructeurs & initialisation ---

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
            if (_isUploading) return; // un batch d'upload est déjà en cours

            var validFiles = new List<string>();
            foreach (var f in files)
                if (File.Exists(f)) validFiles.Add(f);

            if (validFiles.Count == 0)
            {
                await ShowErrorAsync(Localization.Get("L.Error.NotFound"));
                return;
            }

            await UploadFilesAsync(validFiles);
        }

        #endregion

        #region --- Upload (1 ou N fichiers en séquentiel) ---

        /// <summary>
        /// Upload séquentiel d'un ou plusieurs fichiers vers Gofile.
        /// Toutes les URLs réussies sont concaténées dans le presse-papier
        /// (une URL par ligne).
        /// </summary>
        private async Task UploadFilesAsync(List<string> filePaths)
        {
            _isUploading = true;
            try
            {
                SetState("Uploading");
                ProgressBar.Value = 0;
                await Task.Delay(150);

                int total = filePaths.Count;

                string? folderId = null;
                string? guestToken = null;
                string? folderUrl = null;
                int successCount = 0;

                for (int i = 0; i < total; i++)
                {
                    var fileInfo = new FileInfo(filePaths[i]);
                    long size = fileInfo.Length;

                    GofileUploadResult? result = null;
                    try { result = await UploadToGofileAsync(filePaths[i], size, i, total, folderId, guestToken); }
                    catch { result = null; }

                    if (result != null)
                    {
                        successCount++;
                        // Stats : comptabilise les octets uploadés
                        try
                        {
                            string today = DateTime.Now.ToString("yyyy-MM-dd");
                            if (CurrentSettings.LastUploadDate != today)
                            {
                                CurrentSettings.LastUploadDate = today;
                                CurrentSettings.BytesUploadedToday = 0;
                            }
                            CurrentSettings.TotalBytesUploaded += size;
                            CurrentSettings.BytesUploadedToday += size;
                            TrySaveSettingsFile();
                        }
                        catch { }
                        if (folderId == null)  // 1er succès → on mémorise le dossier
                        {
                            folderId = result.FolderId;
                            guestToken = result.GuestToken;
                            folderUrl = result.FolderUrl;
                        }
                    }
                }

                if (successCount == 0 || string.IsNullOrEmpty(folderUrl))
                {
                    await ShowErrorAsync(Localization.Get("L.Error.Failed"));
                    return;
                }

                ProgressBar.Value = 100;
                await Task.Delay(200);

                try { Clipboard.SetText(folderUrl); } catch { }
                SetState("Success");

                await NotifyDiscordWebhookAsync("Gofile", folderUrl);

                await Task.Delay(3000);
                SetState("Idle");
            }
            finally
            {
                _isUploading = false;
                UploadInfoPopup.IsOpen = false;
            }
        }

        private sealed class GofileUploadResult
        {
            public string FolderUrl { get; set; } = "";
            public string FolderId { get; set; } = "";
            public string GuestToken { get; set; } = "";
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

        #region --- Provider unique : Gofile (toutes tailles, >100 GB OK) ---

        private async Task<GofileUploadResult?> UploadToGofileAsync(string filePath, long totalSize, int currentIndex, int totalFiles, string? folderId, string? guestToken)
        {
            using var fileStream = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 1024 * 1024, useAsync: true);

            _currentTotalSize = totalSize;
            _currentBytesSent = 0;
            _lastBytesSent = 0;
            _lastSpeedSample = DateTime.UtcNow;
            _currentSpeedBps = 0;

            using var progressContent = new ProgressableStreamContent(
                fileStream,
                bufferSize: 1024 * 1024,
                progress: bytesSent =>
                {
                    // Calcul du débit (octets/seconde) sur un échantillon glissant
                    var now = DateTime.UtcNow;
                    double dt = (now - _lastSpeedSample).TotalSeconds;
                    if (dt >= 0.5)
                    {
                        double bps = (bytesSent - _lastBytesSent) / dt;
                        _currentSpeedBps = (_currentSpeedBps == 0) ? bps
                                          : (_currentSpeedBps * 0.7 + bps * 0.3); // lissage
                        _lastBytesSent = bytesSent;
                        _lastSpeedSample = now;
                    }
                    _currentBytesSent = bytesSent;

                    double localPct = totalSize > 0 ? (double)bytesSent / totalSize : 0;
                    double batchPct = ((currentIndex + localPct) / totalFiles) * 95.0;
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (batchPct > ProgressBar.Value) ProgressBar.Value = batchPct;
                        if (UploadInfoPopup.IsOpen) UpdateUploadInfoPopup();
                    }));
                });
            progressContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            progressContent.Headers.ContentLength = totalSize;

            using var form = new MultipartFormDataContent();
            form.Add(progressContent, "file", Path.GetFileName(filePath));
            if (!string.IsNullOrEmpty(folderId))
                form.Add(new StringContent(folderId), "folderId");

            using var req = new HttpRequestMessage(HttpMethod.Post, GofileUploadUrl) { Content = form };
            if (!string.IsNullOrEmpty(guestToken))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", guestToken);

            using var response = await client.SendAsync(
                req, HttpCompletionOption.ResponseHeadersRead);

            double doneBatchPct = ((currentIndex + 1.0) / totalFiles) * 97.0;
            Dispatcher.Invoke(() => ProgressBar.Value = Math.Max(ProgressBar.Value, doneBatchPct));

            if (!response.IsSuccessStatusCode) return null;

            string responseText = await response.Content.ReadAsStringAsync();
            try
            {
                using var doc = JsonDocument.Parse(responseText);
                var root = doc.RootElement;
                if (!root.TryGetProperty("status", out var statusElement) ||
                    statusElement.GetString() != "ok" ||
                    !root.TryGetProperty("data", out var data))
                {
                    return null;
                }

                var result = new GofileUploadResult();

                if (data.TryGetProperty("parentFolder", out var pf))
                    result.FolderId = pf.GetString() ?? "";
                else if (data.TryGetProperty("parentFolderId", out var pfi))
                    result.FolderId = pfi.GetString() ?? "";

                if (data.TryGetProperty("guestToken", out var gt))
                    result.GuestToken = gt.GetString() ?? "";

                if (data.TryGetProperty("parentFolderCode", out var pfc))
                {
                    var code = pfc.GetString();
                    if (!string.IsNullOrEmpty(code)) result.FolderUrl = $"https://gofile.io/d/{code}";
                }
                if (string.IsNullOrEmpty(result.FolderUrl) &&
                    data.TryGetProperty("downloadPage", out var dp))
                {
                    result.FolderUrl = dp.GetString() ?? "";
                }
                if (string.IsNullOrEmpty(result.FolderUrl) &&
                    data.TryGetProperty("code", out var codeEl))
                {
                    var code = codeEl.GetString();
                    if (!string.IsNullOrEmpty(code)) result.FolderUrl = $"https://gofile.io/d/{code}";
                }

                return string.IsNullOrEmpty(result.FolderUrl) ? null : result;
            }
            catch (JsonException) { }
            return null;
        }

        #endregion

        #region --- StreamContent custom : progression réelle des bytes envoyés ---

        private sealed class ProgressableStreamContent : HttpContent
        {
            private readonly Stream _source;
            private readonly int _bufferSize;
            private readonly Action<long> _progress;

            public ProgressableStreamContent(Stream source, int bufferSize, Action<long> progress)
            {
                _source = source;
                _bufferSize = bufferSize;
                _progress = progress;
            }

            protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            {
                var buffer = new byte[_bufferSize];
                long uploaded = 0;
                int read;
                while ((read = await _source.ReadAsync(buffer.AsMemory(0, _bufferSize))) > 0)
                {
                    await stream.WriteAsync(buffer.AsMemory(0, read));
                    uploaded += read;
                    _progress(uploaded);
                }
                await stream.FlushAsync();
            }

            protected override bool TryComputeLength(out long length)
            {
                length = _source.CanSeek ? _source.Length : -1;
                return _source.CanSeek;
            }

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
            }
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

        private static string FmtBytes(double bytes)
        {
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            while (bytes >= 1024 && i < u.Length - 1) { bytes /= 1024; i++; }
            return $"{bytes:0.##} {u[i]}";
        }

        private void UpdateUploadInfoPopup()
        {
            SpeedText.Text = $"⬆ {FmtBytes(_currentSpeedBps)}/s";
            if (_currentSpeedBps > 1 && _currentTotalSize > 0)
            {
                double remaining = (_currentTotalSize - _currentBytesSent) / _currentSpeedBps;
                TimeSpan ts = TimeSpan.FromSeconds(remaining);
                string eta = ts.TotalHours >= 1
                    ? $"{(int)ts.TotalHours}h {ts.Minutes:00}m"
                    : ts.TotalMinutes >= 1
                        ? $"{ts.Minutes}m {ts.Seconds:00}s"
                        : $"{ts.Seconds}s";
                EtaText.Text = $"⏱ {eta}";
            }
            else EtaText.Text = "⏱ …";
        }

        private void DropBorder_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_isUploading) return;
            UpdateUploadInfoPopup();
            UploadInfoPopup.IsOpen = true;
        }

        private void DropBorder_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
            => UploadInfoPopup.IsOpen = false;

        #endregion
    }
}