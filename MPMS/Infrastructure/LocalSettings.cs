using System.Text.Json;
using System.IO;

namespace MPMS.Infrastructure;

public static class LocalSettings
{
    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MPMS",
        "settings.json");

    private static Dictionary<string, string> _settings = new();
    private static readonly object _lock = new();

    static LocalSettings()
    {
        LoadSettings();
    }

    private static void LoadSettings()
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                _settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
            }
        }
        catch
        {
            _settings = new();
        }
    }

    private static void SaveSettings()
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(_settings);
            File.WriteAllText(SettingsFilePath, json);
        }
        catch
        {
            // Ignore save errors
        }
    }

    public static string Get(string key, string defaultValue = "")
    {
        lock (_lock)
        {
            return _settings.TryGetValue(key, out var value) ? value : defaultValue;
        }
    }

    public static void Set(string key, string value)
    {
        lock (_lock)
        {
            _settings[key] = value;
            SaveSettings();
        }
    }

    public static bool GetBool(string key, bool defaultValue = false)
    {
        var value = Get(key);
        return bool.TryParse(value, out var result) ? result : defaultValue;
    }

    public static void SetBool(string key, bool value)
    {
        Set(key, value.ToString());
    }
}
