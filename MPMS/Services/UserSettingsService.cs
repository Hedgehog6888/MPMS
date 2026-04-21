using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace MPMS.Services;

public interface IUserSettingsService
{
    string GetValue(string key, string defaultValue = "");
    void SetValue(string key, string value);
    Task SaveAsync();
}

public class UserSettingsService : IUserSettingsService
{
    private readonly string _filePath;
    private Dictionary<string, string> _settings = new();

    public UserSettingsService()
    {
        _filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MPMS", "user_prefs.json");
        Load();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                _settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
            }
        }
        catch
        {
            _settings = new();
        }
    }

    public string GetValue(string key, string defaultValue = "")
    {
        return _settings.TryGetValue(key, out var value) ? value : defaultValue;
    }

    public void SetValue(string key, string value)
    {
        _settings[key] = value;
        _ = SaveAsync();
    }

    public async Task SaveAsync()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_filePath, json);
        }
        catch
        {
            // Ignore errors in settings saving
        }
    }
}
