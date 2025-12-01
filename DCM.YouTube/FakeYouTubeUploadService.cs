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

    public async Task<UploadResult> UploadAsync(
        UploadProject project,
        IProgress<UploadProgressInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));

        project.Validate();

        progress?.Report(new UploadProgressInfo(0, "Upload wird gestartet..."));

        const int steps = 5;
        for (var i = 1; i <= steps; i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(_fakeDelay.TotalMilliseconds / steps), cancellationToken);
            var percent = (double)i / steps * 100d;
            progress?.Report(new UploadProgressInfo(percent, "Upload (Fake) läuft..."));
        }

        var fakeId = Guid.NewGuid().ToString("N");
        var videoUrl = new Uri($"https://youtube.com/watch?v={fakeId}");

        progress?.Report(new UploadProgressInfo(100, "Upload abgeschlossen."));

        return UploadResult.Ok(fakeId, videoUrl);
    }
}