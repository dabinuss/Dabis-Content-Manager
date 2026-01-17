// DCM.Core/Models/UploadResult.cs

using System;

namespace DCM.Core.Models;

public sealed class UploadResult
{
    public bool Success { get; init; }
    public string? PlatformVideoId { get; init; }
    public Uri? VideoUrl { get; init; }
    public string? ErrorMessage { get; init; }

    public static UploadResult Ok(string platformVideoId, Uri videoUrl) =>
        new()
        {
            Success = true,
            PlatformVideoId = platformVideoId,
            VideoUrl = videoUrl
        };

    public static UploadResult Failed(string errorMessage) =>
        new()
        {
            Success = false,
            ErrorMessage = errorMessage
        };
}
