using System.Threading;
using System.Threading.Tasks;
using DCM.Core.Models;

namespace DCM.YouTube;

public interface IYouTubeUploadService
{
    Task<UploadResult> UploadAsync(
        UploadProject project,
        IProgress<UploadProgressInfo>? progress = null,
        CancellationToken cancellationToken = default);
}