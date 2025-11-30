using System.Threading;
using System.Threading.Tasks;
using DCM.Core.Models;
using DCM.Core.Services;

namespace DCM.YouTube;

public sealed class YouTubePlatformClient : IPlatformClient
{
    private readonly IYouTubeUploadService _inner;

    public YouTubePlatformClient(IYouTubeUploadService inner)
    {
        _inner = inner;
    }

    public PlatformType Platform => PlatformType.YouTube;

    public Task<UploadResult> UploadAsync(UploadProject project, CancellationToken cancellationToken = default)
    {
        return _inner.UploadAsync(project, cancellationToken);
    }
}
