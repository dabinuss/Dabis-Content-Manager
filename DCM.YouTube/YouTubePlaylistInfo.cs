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
}
