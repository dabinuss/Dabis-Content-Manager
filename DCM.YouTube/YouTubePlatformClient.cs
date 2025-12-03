using DCM.Core;
using DCM.Core.Logging;
using DCM.Core.Models;
using DCM.Core.Services;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;

namespace DCM.YouTube;

public sealed class YouTubePlatformClient : IPlatformClient
{
    private readonly string _clientSecretsPath;
    private readonly string _tokenFolder;
    private readonly IAppLogger _logger;

    private UserCredential? _credential;
    private YouTubeService? _service;

    public YouTubePlatformClient(IAppLogger? logger = null)
    {
        _logger = logger ?? AppLogger.Instance;

        var baseFolder = Constants.AppDataFolder;
        _clientSecretsPath = Path.Combine(baseFolder, Constants.YouTubeClientSecretsFileName);
        _tokenFolder = Path.Combine(baseFolder, Constants.YouTubeTokensFolderName);

        _logger.Debug($"YouTubePlatformClient initialisiert, Secrets: {_clientSecretsPath}", "YouTube");
    }

    public PlatformType Platform => PlatformType.YouTube;
    public bool IsConnected => _service is not null;
    public string? ChannelTitle { get; private set; }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _logger.Info("YouTube-Verbindung wird hergestellt...", "YouTube");
        await EnsureServiceAsync(cancellationToken);
        _logger.Info($"YouTube verbunden als: {ChannelTitle ?? "Unbekannt"}", "YouTube");
    }

    public async Task<bool> TryConnectSilentAsync(CancellationToken cancellationToken = default)
    {
        if (_service is not null)
        {
            return true;
        }

        if (!File.Exists(_clientSecretsPath) || !HasStoredTokens())
        {
            _logger.Debug("Keine gespeicherten Tokens für Silent-Connect gefunden", "YouTube");
            return false;
        }

        try
        {
            _logger.Debug("Versuche Silent-Connect mit gespeicherten Tokens...", "YouTube");

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
            {
                _logger.Debug("Kein gespeichertes Token gefunden", "YouTube");
                return false;
            }

            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = secrets.Secrets,
                Scopes = scopes,
                DataStore = dataStore
            });

            var credential = new UserCredential(flow, "user", storedToken);

            if (credential.Token.IsStale)
            {
                _logger.Debug("Token ist abgelaufen, versuche Refresh...", "YouTube");
                if (!await credential.RefreshTokenAsync(cancellationToken))
                {
                    _logger.Warning("Token-Refresh fehlgeschlagen", "YouTube");
                    return false;
                }
                _logger.Debug("Token erfolgreich erneuert", "YouTube");
            }

            _credential = credential;

            _service = new YouTubeService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = Constants.ApplicationName
            });

            try
            {
                var req = _service.Channels.List("snippet");
                req.Mine = true;
                var resp = await req.ExecuteAsync(cancellationToken);
                ChannelTitle = resp.Items?.FirstOrDefault()?.Snippet?.Title;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Kanal-Titel konnte nicht abgerufen werden: {ex.Message}", "YouTube");
            }

            _logger.Info($"Silent-Connect erfolgreich: {ChannelTitle ?? "Unbekannt"}", "YouTube");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Silent-Connect fehlgeschlagen: {ex.Message}", "YouTube");
            return false;
        }
    }

    public Task DisconnectAsync()
    {
        _logger.Info("YouTube-Verbindung wird getrennt...", "YouTube");

        ChannelTitle = null;
        _service = null;
        _credential = null;

        if (Directory.Exists(_tokenFolder))
        {
            try
            {
                Directory.Delete(_tokenFolder, true);
                _logger.Debug("Token-Ordner gelöscht", "YouTube");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Token-Ordner konnte nicht gelöscht werden: {ex.Message}", "YouTube");
            }
        }

        _logger.Info("YouTube-Verbindung getrennt", "YouTube");
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<YouTubePlaylistInfo>> GetPlaylistsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureServiceAsync(cancellationToken);

        if (_service is null)
        {
            throw new InvalidOperationException("YouTube-Dienst nicht initialisiert.");
        }

        _logger.Debug("Lade YouTube-Playlists...", "YouTube");

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

        _logger.Debug($"Playlists geladen: {result.Count} gefunden", "YouTube");
        return result;
    }

    public async Task<UploadResult> UploadAsync(
        UploadProject project,
        IProgress<UploadProgressInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));
        project.Validate();

        if (!File.Exists(project.VideoFilePath))
        {
            var errorMsg = $"Videodatei nicht gefunden: {project.VideoFilePath}";
            _logger.Error(errorMsg, "YouTube");
            return UploadResult.Failed(errorMsg);
        }

        _logger.Info($"YouTube-Upload gestartet: {project.Title}", "YouTube");

        await EnsureServiceAsync(cancellationToken);

        if (_service is null)
        {
            var errorMsg = "YouTube-Dienst konnte nicht initialisiert werden.";
            _logger.Error(errorMsg, "YouTube");
            return UploadResult.Failed(errorMsg);
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

        if (project.ScheduledTime is DateTimeOffset scheduled &&
            scheduled > DateTimeOffset.Now.AddMinutes(1))
        {
            video.Status.PublishAtDateTimeOffset = scheduled;
            _logger.Debug($"Video geplant für: {scheduled:g}", "YouTube");
        }

        using var fileStream = new FileStream(project.VideoFilePath, FileMode.Open, FileAccess.Read);
        var totalBytes = fileStream.Length;

        _logger.Debug($"Video-Größe: {totalBytes / 1024 / 1024:F2} MB", "YouTube");

        var insertRequest = _service.Videos.Insert(
            video,
            "snippet,status",
            fileStream,
            "video/*"
        );

        insertRequest.ProgressChanged += uploadProgress =>
        {
            switch (uploadProgress.Status)
            {
                case UploadStatus.Starting:
                    _logger.Debug("Upload startet...", "YouTube");
                    progress?.Report(new UploadProgressInfo(0, "Upload startet...", totalBytes == 0));
                    break;
                case UploadStatus.Uploading:
                    var percent = totalBytes > 0
                        ? (double)uploadProgress.BytesSent / totalBytes * 100d
                        : 0d;
                    progress?.Report(new UploadProgressInfo(percent, "Video wird hochgeladen...", totalBytes == 0));
                    break;
                case UploadStatus.Completed:
                    _logger.Debug("Video-Upload abgeschlossen", "YouTube");
                    progress?.Report(new UploadProgressInfo(100, "Video-Upload abgeschlossen."));
                    break;
                case UploadStatus.Failed:
                    var errorMsg = uploadProgress.Exception?.Message ?? "Upload fehlgeschlagen.";
                    _logger.Error($"Upload fehlgeschlagen: {errorMsg}", "YouTube");
                    progress?.Report(new UploadProgressInfo(0, errorMsg));
                    break;
            }
        };

        var uploadProgress = await insertRequest.UploadAsync(cancellationToken);

        if (uploadProgress.Exception is not null)
        {
            _logger.Error($"Upload-Exception: {uploadProgress.Exception.Message}", "YouTube", uploadProgress.Exception);
            return UploadResult.Failed(uploadProgress.Exception.Message);
        }

        if (insertRequest.ResponseBody?.Id is null)
        {
            var errorMsg = "YouTube hat keine Video-ID zurückgegeben.";
            _logger.Error(errorMsg, "YouTube");
            return UploadResult.Failed(errorMsg);
        }

        var videoId = insertRequest.ResponseBody.Id;
        _logger.Info($"Video hochgeladen, ID: {videoId}", "YouTube");

        // Delay-Fix (YouTube braucht Zeit, sonst ignoriert es das Thumbnail)
        await Task.Delay(Constants.YouTubeThumbnailDelayMs, cancellationToken);

        if (!string.IsNullOrWhiteSpace(project.PlaylistId))
        {
            progress?.Report(new UploadProgressInfo(100, "Playlist wird aktualisiert..."));

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
                _logger.Debug($"Video zur Playlist hinzugefügt: {project.PlaylistId}", "YouTube");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Playlist-Update fehlgeschlagen: {ex.Message}", "YouTube");
            }
        }

        // Thumbnail
        if (!string.IsNullOrWhiteSpace(project.ThumbnailPath) &&
            File.Exists(project.ThumbnailPath))
        {
            progress?.Report(new UploadProgressInfo(100, "Thumbnail wird gesetzt..."));

            try
            {
                using var thumbStream = new FileStream(project.ThumbnailPath, FileMode.Open, FileAccess.Read);
                var ext = Path.GetExtension(project.ThumbnailPath);
                var contentType = GetThumbnailMimeType(ext);

                var thumbsRequest = _service.Thumbnails.Set(videoId, thumbStream, contentType);
                await thumbsRequest.UploadAsync(cancellationToken);
                _logger.Debug("Thumbnail erfolgreich gesetzt", "YouTube");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Thumbnail-Upload fehlgeschlagen: {ex.Message}", "YouTube");
            }
        }

        progress?.Report(new UploadProgressInfo(100, "Upload abgeschlossen."));

        var videoUrl = new Uri($"https://www.youtube.com/watch?v={videoId}");
        _logger.Info($"Upload abgeschlossen: {videoUrl}", "YouTube");
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
            var errorMsg = $"YouTube Client-Secrets fehlen: {_clientSecretsPath}";
            _logger.Error(errorMsg, "YouTube");
            throw new InvalidOperationException(errorMsg);
        }

        _logger.Debug("Lade YouTube Client-Secrets...", "YouTube");

        var secrets = await GoogleClientSecrets.FromFileAsync(_clientSecretsPath, cancellationToken);

        var scopes = new[]
        {
            YouTubeService.Scope.Youtube,
            YouTubeService.Scope.YoutubeUpload,
            YouTubeService.Scope.YoutubeReadonly
        };

        var dataStore = new FileDataStore(_tokenFolder, true);

        _logger.Debug("Starte OAuth-Autorisierung...", "YouTube");

        _credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            secrets.Secrets,
            scopes,
            "user",
            cancellationToken,
            dataStore);

        _service = new YouTubeService(new BaseClientService.Initializer
        {
            HttpClientInitializer = _credential,
            ApplicationName = Constants.ApplicationName
        });

        var req = _service.Channels.List("snippet");
        req.Mine = true;
        var resp = await req.ExecuteAsync(cancellationToken);

        ChannelTitle = resp.Items?.FirstOrDefault()?.Snippet?.Title;
        _logger.Debug($"YouTube-Service initialisiert, Kanal: {ChannelTitle}", "YouTube");
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
        if (!Directory.Exists(_tokenFolder))
        {
            return false;
        }

        return Directory.EnumerateFiles(_tokenFolder)
            .Any(f => Path.GetFileName(f).Contains("TokenResponse", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetThumbnailMimeType(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return "image/jpeg";
        }

        return extension.ToLowerInvariant() switch
        {
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            _ => "image/jpeg"
        };
    }
}