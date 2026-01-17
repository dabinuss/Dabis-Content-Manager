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
    private const string UserKey = "user";

    private static readonly string[] Scopes =
    [
        YouTubeService.Scope.Youtube,
        YouTubeService.Scope.YoutubeUpload,
        YouTubeService.Scope.YoutubeReadonly
    ];

    // Empirisch: YouTube lehnt publishAt "zu nah" an jetzt oft mit invalidPublishAt ab.
    private static readonly TimeSpan MinScheduledPublishLeadTime = TimeSpan.FromMinutes(15);

    private readonly string _clientSecretsPath;
    private readonly string _tokenFolder;
    private readonly IAppLogger _logger;

    private readonly SemaphoreSlim _serviceInitLock = new(1, 1);

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

            var secrets = await LoadSecretsAsync(cancellationToken);
            var dataStore = new FileDataStore(_tokenFolder, true);

            var storedToken = await dataStore.GetAsync<TokenResponse>(UserKey);
            if (storedToken is null)
            {
                _logger.Debug("Kein gespeichertes Token gefunden", "YouTube");
                return false;
            }

            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = secrets.Secrets,
                Scopes = Scopes,
                DataStore = dataStore
            });

            var credential = new UserCredential(flow, UserKey, storedToken);

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
            _service = CreateService(credential);

            await TryLoadChannelTitleAsync(_service, cancellationToken);

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

    public async Task<IReadOnlyList<OptionEntry>> GetVideoCategoriesAsync(
        string regionCode,
        string? hl = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureServiceAsync(cancellationToken);

        if (_service is null)
        {
            throw new InvalidOperationException("YouTube-Dienst nicht initialisiert.");
        }

        if (string.IsNullOrWhiteSpace(regionCode))
        {
            throw new ArgumentException("RegionCode ist erforderlich.", nameof(regionCode));
        }

        _logger.Debug($"Lade YouTube-Kategorien ({regionCode}/{hl ?? "-"})...", "YouTube");

        var request = _service.VideoCategories.List("snippet");
        request.RegionCode = regionCode;
        if (!string.IsNullOrWhiteSpace(hl))
        {
            request.Hl = hl;
        }

        var response = await request.ExecuteAsync(cancellationToken);

        var result = response.Items?
            .Where(c => !string.IsNullOrWhiteSpace(c.Id))
            .Select(c => new OptionEntry
            {
                Code = c.Id ?? string.Empty,
                Name = c.Snippet?.Title ?? c.Id ?? string.Empty
            })
            .OrderBy(c => c.Name)
            .ToList() ?? new List<OptionEntry>();

        _logger.Debug($"Kategorien geladen: {result.Count} gefunden", "YouTube");
        return result;
    }

    public async Task<IReadOnlyList<OptionEntry>> GetI18nLanguagesAsync(
        string? hl = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureServiceAsync(cancellationToken);

        if (_service is null)
        {
            throw new InvalidOperationException("YouTube-Dienst nicht initialisiert.");
        }

        _logger.Debug($"Lade YouTube-Sprachen ({hl ?? "-"})...", "YouTube");

        var request = _service.I18nLanguages.List("snippet");
        if (!string.IsNullOrWhiteSpace(hl))
        {
            request.Hl = hl;
        }

        var response = await request.ExecuteAsync(cancellationToken);

        var result = response.Items?
            .Select(item =>
            {
                var code = item.Id ?? item.Snippet?.Hl ?? string.Empty;
                var name = item.Snippet?.Name ?? code;
                return string.IsNullOrWhiteSpace(code)
                    ? null
                    : new OptionEntry { Code = code, Name = name };
            })
            .Where(item => item is not null)
            .Cast<OptionEntry>()
            .OrderBy(c => c.Name)
            .ToList() ?? new List<OptionEntry>();

        _logger.Debug($"Sprachen geladen: {result.Count} gefunden", "YouTube");
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

        // --- Scheduling / Privacy ---
        // YouTube-Regel: status.publishAt kann nur gesetzt werden, wenn status.privacyStatus=private ist.
        // Außerdem wird publishAt "zu nah" an jetzt oft abgelehnt (invalidPublishAt).
        var privacyStatus = MapPrivacyStatus(project.Visibility);
        DateTimeOffset? publishAtUtc = null;

        if (project.ScheduledTime is DateTimeOffset scheduled)
        {
            var scheduledUtc = scheduled.ToUniversalTime();
            var minUtc = DateTimeOffset.UtcNow.Add(MinScheduledPublishLeadTime);

            if (scheduledUtc > minUtc)
            {
                publishAtUtc = scheduledUtc;

                if (!string.Equals(privacyStatus, "private", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Debug("Scheduled publish requested; overriding privacyStatus to private (YouTube requirement).", "YouTube");
                    privacyStatus = "private";
                }

                _logger.Debug($"Video geplant für (UTC): {scheduledUtc:u}", "YouTube");
            }
            else
            {
                _logger.Warning(
                    $"ScheduledTime ist zu nah oder in der Vergangenheit ({scheduled:g}). Upload läuft ohne Planung.",
                    "YouTube");

                progress?.Report(new UploadProgressInfo(0, "Geplanter Zeitpunkt ist zu nah/ungültig – Upload ohne Planung."));
            }
        }

        var video = new Video
        {
            Snippet = new VideoSnippet
            {
                Title = string.IsNullOrWhiteSpace(project.Title)
                    ? Path.GetFileNameWithoutExtension(project.VideoFilePath)
                    : project.Title,
                Description = project.Description,
                Tags = project.Tags.Count > 0 ? project.Tags : null,
                CategoryId = project.CategoryId,
                DefaultLanguage = project.Language
            },
            Status = new VideoStatus
            {
                PrivacyStatus = privacyStatus,
                PublishAtDateTimeOffset = publishAtUtc
            }
        };

        if (project.MadeForKids.HasValue && video.Status is not null)
        {
            TrySetBooleanProperty(video.Status, "SelfDeclaredMadeForKids", project.MadeForKids.Value);
            TrySetBooleanProperty(video.Status, "MadeForKids", project.MadeForKids.Value);
        }

        if (project.CommentStatus != CommentStatusSetting.Default)
        {
            _logger.Warning("Comment status preset is not supported by the YouTube API and will be ignored.", "YouTube");
        }

        using var fileStream = OpenVideoReadStream(project.VideoFilePath);
        var totalBytes = fileStream.Length;

        _logger.Debug($"Video-Größe: {totalBytes / 1024 / 1024:F2} MB", "YouTube");

        var insertRequest = _service.Videos.Insert(
            video,
            "snippet,status",
            fileStream,
            "video/*"
        );

        void OnProgressChanged(IUploadProgress uploadProgress)
        {
            switch (uploadProgress.Status)
            {
                case UploadStatus.Starting:
                    _logger.Debug("Upload startet...", "YouTube");
                    progress?.Report(new UploadProgressInfo(0, "Upload startet...", totalBytes == 0));
                    break;

                case UploadStatus.Uploading:
                {
                    var percent = totalBytes > 0
                        ? (double)uploadProgress.BytesSent / totalBytes * 100d
                        : 0d;

                    percent = Math.Max(0d, Math.Min(100d, percent));
                    progress?.Report(new UploadProgressInfo(percent, "Video wird hochgeladen...", totalBytes == 0));
                    break;
                }

                case UploadStatus.Completed:
                    _logger.Debug("Video-Upload abgeschlossen", "YouTube");
                    progress?.Report(new UploadProgressInfo(100, "Video-Upload abgeschlossen."));
                    break;

                case UploadStatus.Failed:
                {
                    var errorMsg = uploadProgress.Exception?.Message ?? "Upload fehlgeschlagen.";
                    _logger.Error($"Upload fehlgeschlagen: {errorMsg}", "YouTube");
                    progress?.Report(new UploadProgressInfo(0, errorMsg));
                    break;
                }
            }
        }

        insertRequest.ProgressChanged += OnProgressChanged;

        IUploadProgress uploadProgressResult;
        try
        {
            uploadProgressResult = await insertRequest.UploadAsync(cancellationToken);
        }
        finally
        {
            insertRequest.ProgressChanged -= OnProgressChanged;
        }

        if (uploadProgressResult.Exception is not null)
        {
            if (uploadProgressResult.Exception is Google.GoogleApiException gex && IsInvalidPublishAt(gex))
            {
                var msg =
                    $"YouTube hat den geplanten Veröffentlichungszeitpunkt abgelehnt (invalidPublishAt). " +
                    $"Tipp: Scheduling erfordert privacyStatus=private und der Zeitpunkt muss deutlich in der Zukunft liegen " +
                    $"(probier ≥ {(int)MinScheduledPublishLeadTime.TotalMinutes} Minuten; falls es weiter knallt: 60 Minuten).";

                _logger.Error(
                    $"Upload-Exception (invalidPublishAt). ScheduledTime: {project.ScheduledTime:g}, PrivacyStatus(sent): {privacyStatus}, PublishAt(UTC): {publishAtUtc:u}",
                    "YouTube",
                    gex);

                return UploadResult.Failed(msg);
            }

            _logger.Error($"Upload-Exception: {uploadProgressResult.Exception.Message}", "YouTube", uploadProgressResult.Exception);
            return UploadResult.Failed(uploadProgressResult.Exception.Message);
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
                using var thumbStream = OpenThumbnailReadStream(project.ThumbnailPath);
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

        await _serviceInitLock.WaitAsync(cancellationToken);
        try
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
            var secrets = await LoadSecretsAsync(cancellationToken);

            var dataStore = new FileDataStore(_tokenFolder, true);

            _logger.Debug("Starte OAuth-Autorisierung...", "YouTube");

            _credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                secrets.Secrets,
                Scopes,
                UserKey,
                cancellationToken,
                dataStore);

            _service = CreateService(_credential);

            await TryLoadChannelTitleAsync(_service, cancellationToken);

            _logger.Debug($"YouTube-Service initialisiert, Kanal: {ChannelTitle}", "YouTube");
        }
        finally
        {
            _serviceInitLock.Release();
        }
    }

    private Task<GoogleClientSecrets> LoadSecretsAsync(CancellationToken cancellationToken) =>
        GoogleClientSecrets.FromFileAsync(_clientSecretsPath, cancellationToken);

    private static YouTubeService CreateService(UserCredential credential) =>
        new(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = Constants.ApplicationName
        });

    private async Task TryLoadChannelTitleAsync(YouTubeService service, CancellationToken cancellationToken)
    {
        try
        {
            var req = service.Channels.List("snippet");
            req.Mine = true;
            var resp = await req.ExecuteAsync(cancellationToken);
            ChannelTitle = resp.Items?.FirstOrDefault()?.Snippet?.Title;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Kanal-Titel konnte nicht abgerufen werden: {ex.Message}", "YouTube");
        }
    }

    private static bool IsInvalidPublishAt(Google.GoogleApiException ex)
    {
        var errors = ex.Error?.Errors;
        if (errors is null || errors.Count == 0)
        {
            return false;
        }

        return errors.Any(e => string.Equals(e.Reason, "invalidPublishAt", StringComparison.OrdinalIgnoreCase));
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

        return Directory.EnumerateFiles(_tokenFolder).Any();
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

    private static void TrySetBooleanProperty(object target, string propertyName, bool value)
    {
        var property = target.GetType().GetProperty(propertyName);
        if (property is null || !property.CanWrite)
        {
            return;
        }

        if (property.PropertyType == typeof(bool?) || property.PropertyType == typeof(bool))
        {
            property.SetValue(target, value);
        }
    }

    private static FileStream OpenVideoReadStream(string path)
    {
        return new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            BufferSize = 1024 * 1024,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });
    }

    private static FileStream OpenThumbnailReadStream(string path)
    {
        return new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            BufferSize = 256 * 1024,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });
    }
}
