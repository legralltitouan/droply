using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace QuickSend
{
    public partial class MainWindow : Window
    {
        // --- IMPORT DES API NATIVES WINDOWS (Win32) ---
        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        // ----------------------------------------------

        private static readonly HttpClient client = new HttpClient();
        private const long MaxFileSizeMB = 20000;
        private const string ApiUrl = "https://store1.gofile.io/uploadFile";

        private DispatcherTimer _taskbarTimer;

        static MainWindow()
        {
            client.Timeout = TimeSpan.FromHours(2); // Timeout augmenté pour les très gros fichiers
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("User-Agent", "QuickSend/1.0");
        }

        public MainWindow()
        {
            InitializeComponent();
            SetState("Idle");
            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _taskbarTimer = new DispatcherTimer();
            _taskbarTimer.Interval = TimeSpan.FromMilliseconds(50);
            _taskbarTimer.Tick += TaskbarTimer_Tick;
            _taskbarTimer.Start();
        }

        private void TaskbarTimer_Tick(object sender, EventArgs e)
        {
            IntPtr taskbarHwnd = FindWindow("Shell_TrayWnd", null);
            if (taskbarHwnd != IntPtr.Zero)
            {
                GetWindowRect(taskbarHwnd, out RECT rect);

                PresentationSource source = PresentationSource.FromVisual(this);
                double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
                double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;

                double logicalTaskbarTop = rect.Top / dpiY;
                double logicalTaskbarBottom = rect.Bottom / dpiY;
                double taskbarHeight = logicalTaskbarBottom - logicalTaskbarTop;
                double logicalScreenHeight = SystemParameters.PrimaryScreenHeight;

                bool isTaskbarVisible = logicalTaskbarTop < (logicalScreenHeight - 5);

                if (isTaskbarVisible)
                {
                    if (this.Visibility != Visibility.Visible)
                        this.Visibility = Visibility.Visible;

                    this.Top = logicalTaskbarTop + (taskbarHeight - 42) / 2 - 10;
                    this.Left = (rect.Left / dpiX) + 10;

                    var helper = new WindowInteropHelper(this);
                    SetWindowPos(helper.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }
                else
                {
                    if (this.Visibility != Visibility.Hidden)
                        this.Visibility = Visibility.Hidden;
                }
            }
        }

        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                DropBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x28, 0x56, 0x98)); // Bleu design
            }
        }

        private void Window_DragLeave(object sender, DragEventArgs e)
        {
            DropBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x40, 0x47)); // Retour couleur initiale
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            DropBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x40, 0x47));

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0)
                {
                    string filePath = files[0];
                    await UploadFileAsync(filePath);
                }
            }
        }

        private async Task UploadFileAsync(string filePath)
        {
            if (!File.Exists(filePath))
            {
                await ShowErrorAsync("Introuvable");
                return;
            }

            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length > MaxFileSizeMB * 1024 * 1024)
            {
                await ShowErrorAsync("Trop lourd");
                return;
            }

            SetState("Uploading");
            ProgressBar.Value = 0;
            await Task.Delay(250);

            try
            {
                using var form = new MultipartFormDataContent();

                // CORRECTION CRITIQUE : On utilise un FileStream pour ne pas saturer la RAM avec les gros fichiers
                using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var streamContent = new StreamContent(fileStream);
                streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                form.Add(streamContent, "file", Path.GetFileName(filePath));

                bool isUploading = true;

                // Fausse progression pour animer la barre en attendant la fin de la requête HTTP
                var progressTask = Task.Run(async () =>
                {
                    for (int i = 0; i <= 90 && isUploading; i++)
                    {
                        await Task.Delay(100); // Ralenti un peu pour les plus gros fichiers
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
                    await Task.Delay(3000);
                    SetState("Idle");
                    return;
                }

                await ShowErrorAsync("Erreur API");
            }
            catch (Exception)
            {
                await ShowErrorAsync("Échec");
            }
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
                case "Idle":
                    IdleState.Visibility = Visibility.Visible;
                    targetWidth = 42;
                    break;
                case "Uploading":
                    UploadState.Visibility = Visibility.Visible;
                    targetWidth = 140;
                    break;
                case "Success":
                    SuccessState.Visibility = Visibility.Visible;
                    targetWidth = 140;
                    break;
                case "Error":
                    ErrorState.Visibility = Visibility.Visible;
                    ErrorText.Text = message;
                    targetWidth = 140;
                    break;
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
            DropBorder.BeginAnimation(FrameworkElement.WidthProperty, widthAnimation);
        }

        private static bool ExtractDownloadLink(string jsonResponse, out string downloadUrl)
        {
            downloadUrl = null;
            try
            {
                using var doc = JsonDocument.Parse(jsonResponse);
                var root = doc.RootElement;

                if (root.TryGetProperty("status", out var statusElement) &&
                    statusElement.GetString() == "ok" &&
                    root.TryGetProperty("data", out var dataElement) &&
                    dataElement.TryGetProperty("downloadPage", out var downloadElement))
                {
                    downloadUrl = downloadElement.GetString();
                    return !string.IsNullOrEmpty(downloadUrl);
                }
            }
            catch (JsonException)
            {
                return false;
            }

            return false;
        }
    }
}