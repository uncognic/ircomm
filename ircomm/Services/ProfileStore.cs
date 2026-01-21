using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace ircomm.Services
{
    internal static class ProfileStore
    {
        private const string FileName = "profiles.json";
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private static string GetFilePath()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ircomm");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return Path.Combine(dir, FileName);
        }

        public static List<Profile> LoadProfiles()
        {
            try
            {
                var path = GetFilePath();
                if (!File.Exists(path)) return new List<Profile>();
                var json = File.ReadAllText(path);
                var list = JsonSerializer.Deserialize<List<Profile>>(json, JsonOptions);
                return list ?? new List<Profile>();
            }
            catch
            {
                return new List<Profile>();
            }
        }

        public static void SaveProfiles(IEnumerable<Profile> profiles)
        {
            try
            {
                var path = GetFilePath();
                var json = JsonSerializer.Serialize(profiles, JsonOptions);
                File.WriteAllText(path, json);
            }
            catch
            {
                Debug.WriteLine("Failed to save profiles");
            }
        }
    }
}