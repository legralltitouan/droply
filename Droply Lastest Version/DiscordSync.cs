using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Droply
{
    /// <summary>
    /// Polling des messages d'un salon Discord (via Bot Token) pour détecter
    /// les uploads provenant d'un AUTRE PC et déclencher une notification toast.
    /// Les messages contenant notre propre MachineTag sont ignorés.
    /// </summary>
    public static class DiscordSync
    {
        private const int PollIntervalMs = 15_000;
        private static readonly HttpClient _http = new();
        private static readonly Regex _urlRegex = new(
            @"https?://(?:gofile\.io|pixeldrain\.com|storage\.to)/\S+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static CancellationTokenSource? _cts;
        private static ulong _lastSeenId;

        /// <summary>Tag inséré dans chaque message pour identifier le PC d'origine.</summary>
        public static string MachineTag => $"[droply:{Environment.MachineName}]";

        public static event Action<string>? OnRemoteUpload;

        /// <summary>Démarre (ou redémarre) le polling en arrière-plan.</summary>
        public static void Start(MainWindow main)
        {
            Stop();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            OnRemoteUpload = url => main.Dispatcher.Invoke(() => IncomingFileToast.ShowToast(url));

            _ = Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    try { await PollOnceAsync(main.CurrentSettings); } catch { /* silencieux */ }
                    try { await Task.Delay(PollIntervalMs, ct); } catch { return; }
                }
            }, ct);
        }

        public static void Stop()
        {
            _cts?.Cancel();
            _cts = null;
        }

        // Récupère les 10 derniers messages du salon et traite les nouveaux.
        private static async Task PollOnceAsync(AppSettings s)
        {
            if (string.IsNullOrWhiteSpace(s.DiscordBotToken) ||
                string.IsNullOrWhiteSpace(s.DiscordChannelId)) return;

            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://discord.com/api/v10/channels/{s.DiscordChannelId}/messages?limit=10");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bot", s.DiscordBotToken);

            using var res = await _http.SendAsync(req);
            if (!res.IsSuccessStatusCode) return;

            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return;

            // Discord renvoie du + récent au + ancien : on inverse pour traiter chronologiquement
            var messages = doc.RootElement.EnumerateArray().Reverse();
            string myTag = MachineTag;
            ulong maxId = _lastSeenId;
            bool isFirstRun = _lastSeenId == 0;

            foreach (var m in messages)
            {
                if (!m.TryGetProperty("id", out var idEl) ||
                    !ulong.TryParse(idEl.GetString(), out ulong msgId) ||
                    msgId <= _lastSeenId) continue;

                if (msgId > maxId) maxId = msgId;

                string content = m.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
                if (string.IsNullOrEmpty(content) || content.Contains(myTag)) continue;

                var match = _urlRegex.Match(content);
                if (!match.Success) continue;

                // 1er run : on enregistre juste les IDs sans notif (évite le spam d'historique)
                if (!isFirstRun) OnRemoteUpload?.Invoke(match.Value);
            }

            _lastSeenId = maxId;
        }
    }
}