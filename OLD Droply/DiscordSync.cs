using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace QuickSend
{
    public static class DiscordSync
    {
        private static readonly HttpClient http = new HttpClient();
        private static CancellationTokenSource? _cts;
        private static ulong _lastSeenId = 0;
        public static string MachineTag => $"[droply:{Environment.MachineName}]";

        public static event Action<string>? OnRemoteUpload; // arg = downloadUrl

        public static void Start(MainWindow main)
        {
            Stop();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            OnRemoteUpload += url =>
            {
                main.Dispatcher.Invoke(() => IncomingFileToast.Show(url));
            };

            _ = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try { await PollOnceAsync(main.CurrentSettings); }
                    catch { }
                    try { await Task.Delay(15000, token); } catch { return; }
                }
            }, token);
        }

        public static void Stop()
        {
            _cts?.Cancel();
            _cts = null;
        }

        private static async Task PollOnceAsync(AppSettings s)
        {
            if (string.IsNullOrWhiteSpace(s.DiscordBotToken) || string.IsNullOrWhiteSpace(s.DiscordChannelId))
                return;

            string url = $"https://discord.com/api/v10/channels/{s.DiscordChannelId}/messages?limit=10";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bot", s.DiscordBotToken);

            using var res = await http.SendAsync(req);
            if (!res.IsSuccessStatusCode) return;
            string body = await res.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return;

            // Discord renvoie les + récents en premier ; on les parcourt en ordre chronologique inverse
            var messages = doc.RootElement.EnumerateArray().ToList();
            messages.Reverse();

            string myTag = MachineTag;
            ulong newLastSeen = _lastSeenId;

            foreach (var m in messages)
            {
                if (!m.TryGetProperty("id", out var idEl)) continue;
                if (!ulong.TryParse(idEl.GetString(), out ulong msgId)) continue;
                if (msgId <= _lastSeenId) continue;
                if (msgId > newLastSeen) newLastSeen = msgId;

                string content = m.TryGetProperty("content", out var c) ? (c.GetString() ?? "") : "";
                if (string.IsNullOrEmpty(content)) continue;
                if (content.Contains(myTag)) continue; // notre propre upload

                // Détection d'une URL (gofile.io / pixeldrain.com / storage.to)
                var match = Regex.Match(content,
                    @"https?://(?:gofile\.io|pixeldrain\.com|storage\.to)/\S+",
                    RegexOptions.IgnoreCase);
                if (!match.Success) continue;

                // Premier passage : on enregistre juste le dernier ID, sans notif
                // (évite de spammer au démarrage avec tout l'historique)
                if (_lastSeenId != 0)
                {
                    OnRemoteUpload?.Invoke(match.Value);
                }
            }

            if (newLastSeen != _lastSeenId) _lastSeenId = newLastSeen;
        }
    }
}