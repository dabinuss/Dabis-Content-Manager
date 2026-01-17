namespace DCM.Core.Configuration;

public interface ISettingsProvider
{
    AppSettings Load();
    void Save(AppSettings settings);
}
