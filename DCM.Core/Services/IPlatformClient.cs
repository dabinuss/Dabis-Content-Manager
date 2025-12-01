using System.Threading;
using System.Threading.Tasks;
using DCM.Core.Models;

namespace DCM.Core.Services;

public interface IPlatformClient
{
    PlatformType Platform { get; }

    Task<UploadResult> UploadAsync(
        UploadProject project,
        IProgress<UploadProgressInfo>? progress = null,
        CancellationToken cancellationToken = default);
}