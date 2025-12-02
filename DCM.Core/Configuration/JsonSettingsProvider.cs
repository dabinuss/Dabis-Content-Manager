using System.Text.Json;

namespace DCM.Core.Configuration;

public sealed class JsonSettingsProvider : ISettingsProvider
{
    private static string GetFilePath() => Path.Combine(Constants.GetAppDataFolder(), Constants.SettingsFileName);

    public AppSettings Load()
    {
        var path = GetFilePath();

        if (!File.Exists(path))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(path);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var settings = JsonSerializer.Deserialize<AppSettings>(json, options);
            return settings ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var path = GetFilePath();

        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        var json = JsonSerializer.Serialize(settings, options);
        File.WriteAllText(path, json);
    }
}