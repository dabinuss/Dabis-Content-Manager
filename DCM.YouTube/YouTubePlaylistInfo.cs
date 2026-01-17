// DCM.YouTube/YouTubePlaylistInfo.cs

namespace DCM.YouTube;

public sealed class YouTubePlaylistInfo
{
    public YouTubePlaylistInfo(string id, string title)
    {
        Id = id;
        Title = title;
    }

    public string Id { get; }

    public string Title { get; }

    public override string ToString()
    {
        if (!string.IsNullOrWhiteSpace(Title))
        {
            return Title;
        }

        return base.ToString()!;
    }
}
