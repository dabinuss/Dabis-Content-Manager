using System;
using System.IO;
using System.Text.Json;

namespace DCM.Core.Configuration;

public sealed class JsonSettingsProvider : ISettingsProvider
{
    private const string FolderName = "DabisContentManager";
    private const string FileName = "settings.json";

    private static string GetFolderPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var folder = Path.Combine(appData, FolderName);

        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }

        return folder;
    }

    private static string GetFilePath() => Path.Combine(GetFolderPath(), FileName);

    public AppSettings Load()
    {
        var path = GetFilePath();

        if (!File.Exists(path))
        {
            return new AppSettings();
        }

        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var settings = JsonSerializer.Deserialize<AppSettings>(json, options);
        return settings ?? new AppSettings();
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
