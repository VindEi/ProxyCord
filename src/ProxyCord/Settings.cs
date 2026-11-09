using System;
using System.IO;
using System.Text.Json;

namespace ProxyCord
{
    public class AppSettings
    {
        private string _proxyEndpoint = "127.0.0.1:2080";

        public string ProxyEndpoint
        {
            get => string.IsNullOrWhiteSpace(_proxyEndpoint) 
                ? (ProxyPort > 0 ? $"127.0.0.1:{ProxyPort}" : "127.0.0.1:2080") 
                : _proxyEndpoint;
            set => _proxyEndpoint = value;
        }

        public int ProxyPort { get; set; } = 2080;
        public bool IsDarkMode { get; set; } = true;
        public bool RouteTcp { get; set; } = true;
        public bool RouteUdp { get; set; } = true;
        public bool CloseToTray { get; set; } = true;

        private static readonly string SettingsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProxyCord");
        private static readonly string SettingsFile = Path.Combine(SettingsFolder, "settings.json");
        private static readonly string LegacySettingsFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dis2ray", "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var json = File.ReadAllText(SettingsFile);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null) return settings;
                }
                else if (File.Exists(LegacySettingsFile))
                {
                    var json = File.ReadAllText(LegacySettingsFile);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null) return settings;
                }
            }
            catch { }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(SettingsFolder);
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFile, json);
            }
            catch { }
        }
    }
}
