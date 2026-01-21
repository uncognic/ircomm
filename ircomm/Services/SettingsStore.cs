using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace ircomm.Services
{
    internal static class SettingsStore
    {
        private const string FileName = "settings.json";
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private static string GetFilePath()
        {
            var dir = AppContext.BaseDirectory;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return Path.Combine(dir, FileName);
        }

        public static Settings LoadSettings()
        {
            try
            {
                var path = GetFilePath();
                if (!File.Exists(path)) return new Settings();
                var json = File.ReadAllText(path);
                var s = JsonSerializer.Deserialize<Settings>(json, JsonOptions);
                return s ?? new Settings();
            }
            catch
            {
                Debug.WriteLine("Failed to load settings; returning defaults.");
                return new Settings();
            }
        }

        public static void SaveSettings(Settings settings)
        {
            try
            {
                var path = GetFilePath();
                var json = JsonSerializer.Serialize(settings, JsonOptions);
                File.WriteAllText(path, json);
            }
            catch
            {
                Debug.WriteLine("Failed to save settings");
            }
        }
    }
}