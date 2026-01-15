using DCM.Core.Models;

namespace DCM.Core.Configuration;

public interface IPresetRepository
{
    IEnumerable<UploadPreset> Load();
    void Save(IEnumerable<UploadPreset> presets);
}
