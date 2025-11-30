// DCM.YouTube/FakeYouTubeUploadService.cs

using System;
using System.Threading;
using System.Threading.Tasks;
using DCM.Core.Models;

namespace DCM.YouTube;

public sealed class FakeYouTubeUploadService : IYouTubeUploadService
{
    private readonly TimeSpan _fakeDelay;

    public FakeYouTubeUploadService(TimeSpan? fakeDelay = null)
    {
        _fakeDelay = fakeDelay ?? TimeSpan.FromSeconds(2);
    }

    public async Task<UploadResult> UploadAsync(UploadProject project, CancellationToken cancellationToken = default)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));

        project.Validate();

        await Task.Delay(_fakeDelay, cancellationToken);

        var fakeId = Guid.NewGuid().ToString("N");
        var videoUrl = new Uri($"https://youtube.com/watch?v={fakeId}");

        return UploadResult.Ok(fakeId, videoUrl);
    }
}
