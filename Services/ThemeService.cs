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

        public void Initialize()
        {
            IsDarkTheme = LoadThemePreference();
            ApplyTheme();
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
        }
    }
}
