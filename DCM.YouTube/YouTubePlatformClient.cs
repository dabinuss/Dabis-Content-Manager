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
        {
            Directory.CreateDirectory(baseFolder);
        }

        // Diese Datei musst du aus der Google Cloud Console herunterladen
        // (OAuth-Client "Desktop App") und hier ablegen.
        _clientSecretsPath = Path.Combine(baseFolder, "youtube_client_secrets.json");

        // Hier speichert Google.Apis die Tokens.
        _tokenFolder = Path.Combine(baseFolder, "youtube_tokens");
    }

    public PlatformType Platform => PlatformType.YouTube;

    /// <summary>
    /// True, wenn aktuell ein YouTubeService initialisiert wurde.
    /// </summary>
    public bool IsConnected => _service is not null;

    /// <summary>
    /// Optionaler Channel-Name für die UI-Anzeige.
    /// </summary>
    public string? ChannelTitle { get; private set; }

    /// <summary>
    /// Startet (oder reaktiviert) den OAuth-Flow und initialisiert den YouTubeService.
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await EnsureServiceAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Löscht gespeicherte Tokens und beendet die aktuelle Sitzung.
    /// </summary>
    public Task DisconnectAsync()
    {
        ChannelTitle = null;
        _service = null;
        _credential = null;

        if (Directory.Exists(_tokenFolder))
        {
            Directory.Delete(_tokenFolder, recursive: true);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Liste der eigenen Playlists abrufen.
    /// </summary>
    public async Task<IReadOnlyList<YouTubePlaylistInfo>> GetPlaylistsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureServiceAsync(cancellationToken).ConfigureAwait(false);

        if (_service is null)
        {
            throw new InvalidOperationException("YouTube-Dienst ist nicht initialisiert.");
        }

        var result = new List<YouTubePlaylistInfo>();

        var request = _service.Playlists.List("snippet,contentDetails");
        request.Mine = true;
        request.MaxResults = 50;

        string? pageToken = null;

        do
        {
            request.PageToken = pageToken;
            var response = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);

            if (response.Items != null)
            {
                result.AddRange(response.Items
                    .Where(p => !string.IsNullOrWhiteSpace(p.Id))
                    .Select(p => new YouTubePlaylistInfo(
                        p.Id!,
                        p.Snippet?.Title ?? "(ohne Titel)")));
            }

            pageToken = response.NextPageToken;
        } while (!string.IsNullOrEmpty(pageToken));

        return result;
    }

    /// <summary>
    /// Echte Upload-Implementierung via YouTube Data API v3.
    /// </summary>
    public async Task<UploadResult> UploadAsync(UploadProject project, CancellationToken cancellationToken = default)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));

        project.Validate();

        if (!File.Exists(project.VideoFilePath))
        {
            return UploadResult.Failed($"Videodatei nicht gefunden: {project.VideoFilePath}");
        }

        await EnsureServiceAsync(cancellationToken).ConfigureAwait(false);

        if (_service is null)
        {
            return UploadResult.Failed("YouTube-Dienst konnte nicht initialisiert werden.");
        }

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

        // Optionales Scheduling – neue, nicht-obsolete Property
        if (project.ScheduledTime is DateTimeOffset scheduled &&
            scheduled > DateTimeOffset.Now.AddMinutes(1))
        {
            video.Status.PublishAtDateTimeOffset = scheduled;
        }

        using var fileStream = new FileStream(project.VideoFilePath, FileMode.Open, FileAccess.Read);

        var insertRequest = _service.Videos.Insert(video, "snippet,status", fileStream, "video/*");

        // explizite ChunkSize setzen ist nicht zwingend nötig – default reicht
        // insertRequest.ChunkSize = ResumableUpload.MinimumChunkSize; // entfernt

        var uploadProgress = await insertRequest.UploadAsync(cancellationToken).ConfigureAwait(false);

        if (uploadProgress.Exception is not null)
        {
            return UploadResult.Failed(uploadProgress.Exception.Message);
        }

        if (insertRequest.ResponseBody is null || string.IsNullOrWhiteSpace(insertRequest.ResponseBody.Id))
        {
            return UploadResult.Failed("YouTube hat keine Video-ID zurückgegeben.");
        }

        var videoId = insertRequest.ResponseBody.Id;

        // Optional: direkt in Playlist schieben
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

            var playlistInsert = _service.PlaylistItems.Insert(playlistItem, "snippet");
            try
            {
                await playlistInsert.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Playlist hinzufügen ist nice-to-have, kein Hard-Fail.
            }
        }

        var videoUrl = new Uri($"https://www.youtube.com/watch?v={videoId}");
        return UploadResult.Ok(videoId, videoUrl);
    }

    private async Task EnsureServiceAsync(CancellationToken cancellationToken)
    {
        if (_service is not null)
        {
            return;
        }

        if (!File.Exists(_clientSecretsPath))
        {
            throw new InvalidOperationException(
                $"YouTube Client-Secrets-Datei wurde nicht gefunden. Erwartet an: {_clientSecretsPath}");
        }

        var secrets = await GoogleClientSecrets
            .FromFileAsync(_clientSecretsPath, cancellationToken)
            .ConfigureAwait(false);

        var scopes = new[]
        {
            YouTubeService.Scope.Youtube,
            YouTubeService.Scope.YoutubeUpload,
            YouTubeService.Scope.YoutubeReadonly
        };

        var dataStore = new FileDataStore(_tokenFolder, fullPath: true);

        _credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                secrets.Secrets,
                scopes,
                "user",
                cancellationToken,
                dataStore)
            .ConfigureAwait(false);

        _service = new YouTubeService(new BaseClientService.Initializer
        {
            HttpClientInitializer = _credential,
            ApplicationName = _applicationName
        });

        // Channel-Name holen (für Statusanzeige)
        var channelsRequest = _service.Channels.List("snippet");
        channelsRequest.Mine = true;

        var channelsResponse = await channelsRequest.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        ChannelTitle = channelsResponse.Items?.FirstOrDefault()?.Snippet?.Title;
    }

    private static string MapPrivacyStatus(VideoVisibility visibility) =>
        visibility switch
        {
            VideoVisibility.Public => "public",
            VideoVisibility.Private => "private",
            VideoVisibility.Unlisted => "unlisted",
            _ => "unlisted"
        };
}
