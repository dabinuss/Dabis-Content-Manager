using System.Collections.Generic;
using DCM.Core.Models;

namespace DCM.Core.Configuration;

public interface ITemplateRepository
{
    IEnumerable<Template> Load();
    void Save(IEnumerable<Template> templates);
}
