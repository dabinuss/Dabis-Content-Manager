// DCM.YouTube/IYouTubeUploadService.cs

using System.Threading;
using System.Threading.Tasks;
using DCM.Core.Models;

namespace DCM.YouTube;

public interface IYouTubeUploadService
{
    Task<UploadResult> UploadAsync(UploadProject project, CancellationToken cancellationToken = default);
}
