using DCM.Core.Models;
using DCM.Core.Services;
using Xunit;

namespace DCM.Tests;

public class UploadHistoryServiceTests : IDisposable
{
    private readonly string _testFilePath;
    private readonly UploadHistoryService _service;

    public UploadHistoryServiceTests()
    {
        // Temporäre Datei für Tests verwenden
        _testFilePath = Path.Combine(Path.GetTempPath(), $"dcm_test_history_{Guid.NewGuid():N}.json");
        _service = new UploadHistoryService(_testFilePath);
    }

    public void Dispose()
    {
        // Aufräumen nach Tests
        try
        {
            if (File.Exists(_testFilePath))
            {
                File.Delete(_testFilePath);
            }
            if (File.Exists(_testFilePath + ".bak"))
            {
                File.Delete(_testFilePath + ".bak");
            }
        }
        catch
        {
            // Ignorieren bei Cleanup-Fehlern
        }
    }

    #region GetAll Tests

    [Fact]
    public void GetAll_NoFile_ReturnsEmptyList()
    {
        var result = _service.GetAll();
        Assert.Empty(result);
    }

    [Fact]
    public void GetAll_AfterAddEntry_ReturnsEntry()
    {
        var project = new UploadProject
        {
            Title = "Test Video",
            VideoFilePath = @"C:\test.mp4",
            Platform = PlatformType.YouTube
        };
        var uploadResult = UploadResult.Ok("abc123", new Uri("https://youtube.com/watch?v=abc123"));

        _service.AddEntry(project, uploadResult);

        var result = _service.GetAll();
        Assert.Single(result);
        Assert.Equal("Test Video", result[0].VideoTitle);
        Assert.True(result[0].Success);
    }

    #endregion

    #region AddEntry Tests

    [Fact]
    public void AddEntry_SuccessfulUpload_StoresCorrectData()
    {
        var project = new UploadProject
        {
            Title = "Mein Video",
            VideoFilePath = @"C:\video.mp4",
            Platform = PlatformType.YouTube
        };
        var uploadResult = UploadResult.Ok("xyz789", new Uri("https://youtube.com/watch?v=xyz789"));

        _service.AddEntry(project, uploadResult);

        var entries = _service.GetAll();
        Assert.Single(entries);

        var entry = entries[0];
        Assert.Equal("Mein Video", entry.VideoTitle);
        Assert.Equal(PlatformType.YouTube, entry.Platform);
        Assert.Equal("https://youtube.com/watch?v=xyz789", entry.VideoUrl?.ToString());
        Assert.True(entry.Success);
        Assert.Null(entry.ErrorMessage);
    }

    [Fact]
    public void AddEntry_FailedUpload_StoresErrorMessage()
    {
        var project = new UploadProject
        {
            Title = "Fehlgeschlagenes Video",
            VideoFilePath = @"C:\video.mp4",
            Platform = PlatformType.YouTube
        };
        var uploadResult = UploadResult.Failed("Upload fehlgeschlagen: Netzwerkfehler");

        _service.AddEntry(project, uploadResult);

        var entries = _service.GetAll();
        Assert.Single(entries);

        var entry = entries[0];
        Assert.False(entry.Success);
        Assert.Equal("Upload fehlgeschlagen: Netzwerkfehler", entry.ErrorMessage);
    }

    [Fact]
    public void AddEntry_EmptyTitle_UsesFallbackFromFilename()
    {
        var project = new UploadProject
        {
            Title = "",
            VideoFilePath = @"C:\Videos\fallback_title.mp4",
            Platform = PlatformType.YouTube
        };
        var uploadResult = UploadResult.Ok("id123", new Uri("https://youtube.com/watch?v=id123"));

        _service.AddEntry(project, uploadResult);

        var entries = _service.GetAll();
        Assert.Single(entries);
        Assert.Equal("fallback_title", entries[0].VideoTitle);
    }

    [Fact]
    public void AddEntry_MultipleEntries_AllArePersisted()
    {
        for (int i = 0; i < 5; i++)
        {
            var project = new UploadProject
            {
                Title = $"Video {i}",
                VideoFilePath = $@"C:\video{i}.mp4",
                Platform = PlatformType.YouTube
            };
            var uploadResult = UploadResult.Ok($"id{i}", new Uri($"https://youtube.com/watch?v=id{i}"));
            _service.AddEntry(project, uploadResult);
        }

        var entries = _service.GetAll();
        Assert.Equal(5, entries.Count);
    }

    [Fact]
    public void AddEntry_NullProject_ThrowsArgumentNullException()
    {
        var uploadResult = UploadResult.Ok("id", new Uri("https://youtube.com/watch?v=id"));

        Assert.Throws<ArgumentNullException>(() => _service.AddEntry(null!, uploadResult));
    }

    [Fact]
    public void AddEntry_NullResult_ThrowsArgumentNullException()
    {
        var project = new UploadProject
        {
            Title = "Test",
            VideoFilePath = @"C:\test.mp4"
        };

        Assert.Throws<ArgumentNullException>(() => _service.AddEntry(project, null!));
    }

    #endregion

    #region Clear Tests

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var project = new UploadProject
        {
            Title = "Test",
            VideoFilePath = @"C:\test.mp4",
            Platform = PlatformType.YouTube
        };
        var uploadResult = UploadResult.Ok("id", new Uri("https://youtube.com/watch?v=id"));

        _service.AddEntry(project, uploadResult);
        Assert.NotEmpty(_service.GetAll());

        _service.Clear();

        Assert.Empty(_service.GetAll());
    }

    [Fact]
    public void Clear_NoExistingFile_DoesNotThrow()
    {
        var exception = Record.Exception(() => _service.Clear());
        Assert.Null(exception);
    }

    #endregion

    #region Persistence Tests

    [Fact]
    public void AddEntry_PersistsToFile()
    {
        var project = new UploadProject
        {
            Title = "Persistenz Test",
            VideoFilePath = @"C:\test.mp4",
            Platform = PlatformType.YouTube
        };
        var uploadResult = UploadResult.Ok("persist123", new Uri("https://youtube.com/watch?v=persist123"));

        _service.AddEntry(project, uploadResult);

        Assert.True(File.Exists(_testFilePath));
        var fileContent = File.ReadAllText(_testFilePath);
        Assert.Contains("Persistenz Test", fileContent);
    }

    [Fact]
    public void AddEntry_CreateBackupFile()
    {
        // Erste Eintrag erstellen
        var project1 = new UploadProject
        {
            Title = "Erster",
            VideoFilePath = @"C:\test1.mp4",
            Platform = PlatformType.YouTube
        };
        _service.AddEntry(project1, UploadResult.Ok("id1", new Uri("https://youtube.com/watch?v=id1")));

        // Zweiter Eintrag erstellen (sollte Backup anlegen)
        var project2 = new UploadProject
        {
            Title = "Zweiter",
            VideoFilePath = @"C:\test2.mp4",
            Platform = PlatformType.YouTube
        };
        _service.AddEntry(project2, UploadResult.Ok("id2", new Uri("https://youtube.com/watch?v=id2")));

        Assert.True(File.Exists(_testFilePath + ".bak"));
    }

    #endregion

    #region UploadHistoryEntry Tests

    [Fact]
    public void UploadHistoryEntry_Status_ReturnsErfolg_WhenSuccess()
    {
        var entry = new UploadHistoryEntry
        {
            Success = true
        };

        Assert.Equal("Erfolg", entry.Status);
    }

    [Fact]
    public void UploadHistoryEntry_Status_ReturnsFehler_WhenNotSuccess()
    {
        var entry = new UploadHistoryEntry
        {
            Success = false
        };

        Assert.Equal("Fehler", entry.Status);
    }

    #endregion
}