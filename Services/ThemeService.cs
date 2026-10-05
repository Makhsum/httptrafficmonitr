using System;
using System.IO;
using Newtonsoft.Json;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace HttpTrafficMonitor.Services
{
    public class ThemeService
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HttpTrafficMonitor", "settings.json");

        public bool IsDarkTheme { get; private set; } = true;

        // settings.json is kept here, so the user's other toolbar choices are saved next to the theme
        public bool RevealCredentialsToAgents { get; private set; }

        public void Initialize()
        {
            IsDarkTheme = LoadThemePreference();
            RevealCredentialsToAgents = LoadSettings().RevealCredentialsToAgents;
            ApplyTheme();
        }

        public void SetRevealCredentialsToAgents(bool reveal)
        {
            RevealCredentialsToAgents = reveal;
            try
            {
                var settings = LoadSettings();
                settings.RevealCredentialsToAgents = reveal;
                string dir = Path.GetDirectoryName(SettingsPath)!;
                Directory.CreateDirectory(dir);
                File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(settings));
            }
            catch { }
        }

        public void ToggleTheme()
        {
            IsDarkTheme = !IsDarkTheme;
            ApplyTheme();
            SaveThemePreference();
        }

        public void SetTheme(bool dark)
        {
            IsDarkTheme = dark;
            ApplyTheme();
            SaveThemePreference();
        }

        private void ApplyTheme()
        {
            var theme = IsDarkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light;
            ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, true);
        }

        private bool LoadThemePreference()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return true;
                string json = File.ReadAllText(SettingsPath);
                var settings = JsonConvert.DeserializeObject<AppSettings>(json);
                return settings?.DarkTheme ?? true;
            }
            catch { return true; }
        }

        private void SaveThemePreference()
        {
            try
            {
                var settings = LoadSettings();
                settings.DarkTheme = IsDarkTheme;
                string dir = Path.GetDirectoryName(SettingsPath)!;
                Directory.CreateDirectory(dir);
                File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(settings));
            }
            catch { }
        }

        private AppSettings LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return new AppSettings();
                string json = File.ReadAllText(SettingsPath);
                return JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
            }
            catch { return new AppSettings(); }
        }

        private class AppSettings
        {
            public bool DarkTheme { get; set; } = true;

            // Only the toolbar switch sets this; no route of the local API reaches it
            public bool RevealCredentialsToAgents { get; set; }
        }
    }
}
