using System.Collections.Generic;
using System.Threading.Tasks;
using DCM.Core.Models;

namespace DCM.Core.Services;

public interface IContentSuggestionService
{
    Task<IReadOnlyList<string>> SuggestTitlesAsync(UploadProject project, ChannelPersona persona);

    Task<string?> SuggestDescriptionAsync(UploadProject project, ChannelPersona persona);

    Task<IReadOnlyList<string>> SuggestTagsAsync(UploadProject project, ChannelPersona persona);
}
