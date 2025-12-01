// DCM.YouTube/YouTubePlatformClient.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DCM.Core.Models;
using DCM.Core.Services;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;

namespace DCM.YouTube;

public sealed class YouTubePlatformClient : IPlatformClient
{
    private readonly string _applicationName = "DabisContentManager";
    private readonly string _clientSecretsPath;
    private readonly string _tokenFolder;

    private UserCredential? _credential;
    private YouTubeService? _service;

    public YouTubePlatformClient()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var baseFolder = Path.Combine(appData, "DabisContentManager");

        if (!Directory.Exists(baseFolder))
            Directory.CreateDirectory(baseFolder);

        _clientSecretsPath = Path.Combine(baseFolder, "youtube_client_secrets.json");
        _tokenFolder = Path.Combine(baseFolder, "youtube_tokens");
    }

    public PlatformType Platform => PlatformType.YouTube;
    public bool IsConnected => _service is not null;
    public string? ChannelTitle { get; private set; }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await EnsureServiceAsync(cancellationToken);
    }

    public async Task<bool> TryConnectSilentAsync(CancellationToken cancellationToken = default)
    {
        if (_service is not null)
            return true;

        if (!File.Exists(_clientSecretsPath) || !HasStoredTokens())
            return false;

        try
        {
            var secrets = await GoogleClientSecrets.FromFileAsync(_clientSecretsPath, cancellationToken);
            var scopes = new[]
            {
                YouTubeService.Scope.Youtube,
                YouTubeService.Scope.YoutubeUpload,
                YouTubeService.Scope.YoutubeReadonly
            };

            var dataStore = new FileDataStore(_tokenFolder, true);

            var storedToken = await dataStore.GetAsync<TokenResponse>("user");
            if (storedToken is null)
                return false;

            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = secrets.Secrets,
                Scopes = scopes,
                DataStore = dataStore
            });

            var credential = new UserCredential(flow, "user", storedToken);

            if (credential.Token.IsStale)
            {
                if (!await credential.RefreshTokenAsync(cancellationToken))
                    return false;
            }

            _credential = credential;

            _service = new YouTubeService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = _applicationName
            });

            try
            {
                var req = _service.Channels.List("snippet");
                req.Mine = true;
                var resp = await req.ExecuteAsync(cancellationToken);
                ChannelTitle = resp.Items?.FirstOrDefault()?.Snippet?.Title;
            }
            catch { }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public Task DisconnectAsync()
    {
        ChannelTitle = null;
        _service = null;
        _credential = null;

        if (Directory.Exists(_tokenFolder))
            Directory.Delete(_tokenFolder, true);

        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<YouTubePlaylistInfo>> GetPlaylistsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureServiceAsync(cancellationToken);

        if (_service is null)
            throw new InvalidOperationException("YouTube-Dienst nicht initialisiert.");

        var result = new List<YouTubePlaylistInfo>();

        var request = _service.Playlists.List("snippet,contentDetails");
        request.Mine = true;
        request.MaxResults = 50;

        string? pageToken = null;

        do
        {
            request.PageToken = pageToken;
            var response = await request.ExecuteAsync(cancellationToken);

            if (response.Items != null)
            {
                result.AddRange(
                    response.Items
                        .Where(p => !string.IsNullOrWhiteSpace(p.Id))
                        .Select(p => new YouTubePlaylistInfo(p.Id!, p.Snippet?.Title ?? "(ohne Titel)")));
            }

            pageToken = response.NextPageToken;
        }
        while (!string.IsNullOrEmpty(pageToken));

        return result;
    }

    public async Task<UploadResult> UploadAsync(UploadProject project, CancellationToken cancellationToken = default)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));
        project.Validate();

        if (!File.Exists(project.VideoFilePath))
            return UploadResult.Failed($"Videodatei nicht gefunden: {project.VideoFilePath}");

        await EnsureServiceAsync(cancellationToken);

        if (_service is null)
            return UploadResult.Failed("YouTube-Dienst konnte nicht initialisiert werden.");

        var video = new Video
        {
            Snippet = new VideoSnippet
            {
                Title = string.IsNullOrWhiteSpace(project.Title)
                    ? Path.GetFileNameWithoutExtension(project.VideoFilePath)
                    : project.Title,
                Description = project.Description,
                Tags = project.Tags.Count > 0 ? project.Tags : null
            },
            Status = new VideoStatus
            {
                PrivacyStatus = MapPrivacyStatus(project.Visibility)
            }
        };

        if (project.ScheduledTime is DateTimeOffset scheduled &&
            scheduled > DateTimeOffset.Now.AddMinutes(1))
        {
            video.Status.PublishAtDateTimeOffset = scheduled;
        }

        using var fileStream = new FileStream(project.VideoFilePath, FileMode.Open, FileAccess.Read);

        var insertRequest = _service.Videos.Insert(
            video,
            "snippet,status",
            fileStream,
            "video/*"
        );

        var uploadProgress = await insertRequest.UploadAsync(cancellationToken);

        if (uploadProgress.Exception is not null)
            return UploadResult.Failed(uploadProgress.Exception.Message);

        if (insertRequest.ResponseBody?.Id is null)
            return UploadResult.Failed("YouTube hat keine Video-ID zurückgegeben.");

        var videoId = insertRequest.ResponseBody.Id;

        // Delay-Fix (YouTube braucht Zeit, sonst ignoriert es das Thumbnail)
        await Task.Delay(1500, cancellationToken);

        if (!string.IsNullOrWhiteSpace(project.PlaylistId))
        {
            var playlistItem = new PlaylistItem
            {
                Snippet = new PlaylistItemSnippet
                {
                    PlaylistId = project.PlaylistId,
                    ResourceId = new ResourceId
                    {
                        Kind = "youtube#video",
                        VideoId = videoId
                    }
                }
            };

            try
            {
                var playlistInsert = _service.PlaylistItems.Insert(playlistItem, "snippet");
                await playlistInsert.ExecuteAsync(cancellationToken);
            }
            catch { }
        }

        // Thumbnail
        if (!string.IsNullOrWhiteSpace(project.ThumbnailPath) &&
            File.Exists(project.ThumbnailPath))
        {
            try
            {
                using var thumbStream = new FileStream(project.ThumbnailPath, FileMode.Open, FileAccess.Read);
                var ext = Path.GetExtension(project.ThumbnailPath);
                var contentType = GetThumbnailMimeType(ext);

                var thumbsRequest = _service.Thumbnails.Set(videoId, thumbStream, contentType);
                await thumbsRequest.UploadAsync(cancellationToken);
            }
            catch { }
        }

        var videoUrl = new Uri($"https://www.youtube.com/watch?v={videoId}");
        return UploadResult.Ok(videoId, videoUrl);
    }

    private async Task EnsureServiceAsync(CancellationToken cancellationToken)
    {
        if (_service is not null)
            return;

        if (!File.Exists(_clientSecretsPath))
            throw new InvalidOperationException($"YouTube Client-Secrets fehlen: {_clientSecretsPath}");

        var secrets = await GoogleClientSecrets.FromFileAsync(_clientSecretsPath, cancellationToken);

        var scopes = new[]
        {
            YouTubeService.Scope.Youtube,
            YouTubeService.Scope.YoutubeUpload,
            YouTubeService.Scope.YoutubeReadonly
        };

        var dataStore = new FileDataStore(_tokenFolder, true);

        _credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            secrets.Secrets,
            scopes,
            "user",
            cancellationToken,
            dataStore);

        _service = new YouTubeService(new BaseClientService.Initializer
        {
            HttpClientInitializer = _credential,
            ApplicationName = _applicationName
        });

        var req = _service.Channels.List("snippet");
        req.Mine = true;
        var resp = await req.ExecuteAsync(cancellationToken);

        ChannelTitle = resp.Items?.FirstOrDefault()?.Snippet?.Title;
    }

    private static string MapPrivacyStatus(VideoVisibility visibility) =>
        visibility switch
        {
            VideoVisibility.Public => "public",
            VideoVisibility.Private => "private",
            VideoVisibility.Unlisted => "unlisted",
            _ => "unlisted"
        };

    private bool HasStoredTokens()
    {
        if (!Directory.Exists(_tokenFolder)) return false;
        return Directory.EnumerateFiles(_tokenFolder)
            .Any(f => Path.GetFileName(f).Contains("TokenResponse", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetThumbnailMimeType(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return "image/jpeg";

        return extension.ToLowerInvariant() switch
        {
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            _ => "image/jpeg"
        };
    }
}

