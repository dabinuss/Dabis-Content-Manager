using System;

namespace DCM.Core.Models;

public sealed class UploadPreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public PlatformType Platform { get; set; } = PlatformType.YouTube;

    public string? Description { get; set; }

    public bool IsDefault { get; set; }

    public string TitlePrefix { get; set; } = string.Empty;

    public string TagsCsv { get; set; } = string.Empty;

    public VideoVisibility Visibility { get; set; } = VideoVisibility.Unlisted;

    public string? PlaylistId { get; set; }

    public string? PlaylistTitle { get; set; }

    public string? CategoryId { get; set; }

    public string? Language { get; set; }

    public MadeForKidsSetting MadeForKids { get; set; } = MadeForKidsSetting.Default;

    public CommentStatusSetting CommentStatus { get; set; } = CommentStatusSetting.Default;

    public string DescriptionTemplate { get; set; } = string.Empty;

    public override string ToString()
    {
        if (!string.IsNullOrWhiteSpace(Name))
        {
            return Name;
        }

        return base.ToString()!;
    }
}
