using System.Collections.Generic;
using System.Windows;

namespace QuickSend
{
    public static class Localization
    {
        private static readonly Dictionary<string, Dictionary<string, string>> _strings = new()
        {
            ["fr"] = new Dictionary<string, string>
            {
                ["L.Menu.Settings"] = "Paramètres",
                ["L.Menu.Quit"] = "Quitter",
                ["L.State.Uploading"] = "Uploading...",
                ["L.State.Copied"] = "Copied",
                ["L.Error.NotFound"] = "Introuvable",
                ["L.Error.TooLarge"] = "Trop lourd",
                ["L.Error.Failed"] = "Échec",
                ["L.Settings.Title"] = "Paramètres du Système",
                ["L.Settings.User"] = "Utilisateur",
                ["L.Settings.UserStatus"] = "Compte Local • En ligne",
                ["L.Settings.SectionGeneral"] = "PRÉFÉRENCES GÉNÉRALES",
                ["L.Settings.Startup"] = "Lancer au démarrage",
                ["L.Settings.StartupHint"] = "Ouvrir l'application à la connexion",
                ["L.Settings.LightMode"] = "Mode Clair",
                ["L.Settings.LightModeHint"] = "Utiliser le thème lumineux",
                ["L.Settings.Language"] = "Langue (English)",
                ["L.Settings.LanguageHint"] = "Basculer l'interface en anglais",
                ["L.Settings.SectionWebhook"] = "DISCORD WEBHOOK (OPTIONNEL)",
                ["L.Settings.WebhookHint"] = "Copiez l'URL du salon Webhook pour les notifications",
                ["L.Settings.Cancel"] = "Annuler",
                ["L.Settings.Save"] = "Enregistrer & Fermer",
                ["L.Webhook.NewFile"] = "📦 Nouveau fichier Droply",
                ["L.Settings.SectionDiscord"] = "DISCORD SYNC (OPTIONNEL)",
                ["L.Settings.BotToken"] = "Bot Token",
                ["L.Settings.ChannelId"] = "ID du salon",
                ["L.Toast.Title"] = "Fichier reçu depuis votre autre PC",
                ["L.Toast.Subtitle"] = "Un nouvel upload Droply vient d'arriver. Le télécharger ?",
                ["L.Toast.Download"] = "Télécharger",
                ["L.Toast.Ignore"] = "Ignorer",
            },
            ["en"] = new Dictionary<string, string>
            {
                ["L.Menu.Settings"] = "Settings",
                ["L.Menu.Quit"] = "Quit",
                ["L.State.Uploading"] = "Uploading...",
                ["L.State.Copied"] = "Copied",
                ["L.Error.NotFound"] = "Not found",
                ["L.Error.TooLarge"] = "Too large",
                ["L.Error.Failed"] = "Failed",
                ["L.Settings.Title"] = "System Settings",
                ["L.Settings.User"] = "User",
                ["L.Settings.UserStatus"] = "Local Account • Online",
                ["L.Settings.SectionGeneral"] = "GENERAL PREFERENCES",
                ["L.Settings.Startup"] = "Launch at startup",
                ["L.Settings.StartupHint"] = "Open the application at login",
                ["L.Settings.LightMode"] = "Light Mode",
                ["L.Settings.LightModeHint"] = "Use the light theme",
                ["L.Settings.Language"] = "Language (Français)",
                ["L.Settings.LanguageHint"] = "Switch the interface to French",
                ["L.Settings.SectionWebhook"] = "DISCORD WEBHOOK (OPTIONAL)",
                ["L.Settings.WebhookHint"] = "Paste the Webhook channel URL for notifications",
                ["L.Settings.Cancel"] = "Cancel",
                ["L.Settings.Save"] = "Save & Close",
                ["L.Webhook.NewFile"] = "📦 New Droply file",
                ["L.Settings.SectionDiscord"] = "DISCORD SYNC (OPTIONAL)",
                ["L.Settings.BotToken"] = "Bot Token",
                ["L.Settings.ChannelId"] = "Channel ID",
                ["L.Toast.Title"] = "File received from your other PC",
                ["L.Toast.Subtitle"] = "A new Droply upload just landed. Download it?",
                ["L.Toast.Download"] = "Download",
                ["L.Toast.Ignore"] = "Ignore",
            }
        };

        public static string CurrentLanguage { get; private set; } = "fr";

        public static string Get(string key)
        {
            if (_strings.TryGetValue(CurrentLanguage, out var dict) && dict.TryGetValue(key, out var v))
                return v;
            if (_strings["fr"].TryGetValue(key, out var fallback))
                return fallback;
            return key;
        }

        public static void ApplyLanguage(string lang)
        {
            if (!_strings.ContainsKey(lang)) lang = "fr";
            CurrentLanguage = lang;
            var res = Application.Current.Resources;
            foreach (var kv in _strings[lang])
                res[kv.Key] = kv.Value;
        }
    }
}