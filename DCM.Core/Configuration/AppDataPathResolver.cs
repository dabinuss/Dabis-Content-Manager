namespace DCM.Core.Configuration;

internal static class AppDataPathResolver
{
    public static string ResolveBaseFolder(string? customAppDataFolder)
    {
        if (string.IsNullOrWhiteSpace(customAppDataFolder))
        {
            return Constants.AppDataFolder;
        }

        Directory.CreateDirectory(customAppDataFolder);
        return customAppDataFolder;
    }
}
