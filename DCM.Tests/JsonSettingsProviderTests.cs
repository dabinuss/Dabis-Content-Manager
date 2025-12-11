using DCM.Core.Configuration;
using DCM.Core.Models;
using Xunit;

namespace DCM.Tests;

public class JsonSettingsProviderTests : IDisposable
{
    private readonly string _testFolder;

    public JsonSettingsProviderTests()
    {
        _testFolder = Path.Combine(Path.GetTempPath(), $"dcm_settings_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testFolder);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testFolder))
            {
                Directory.Delete(_testFolder, true);
            }
        }
        catch
        {
            // Ignorieren bei Cleanup-Fehlern
        }
    }

    private JsonSettingsProvider CreateProviderWithCustomPath()
    {
        // Da JsonSettingsProvider den Standard-Pfad verwendet,
        // testen wir hier hauptsächlich das Verhalten mit der echten Implementierung
        return new JsonSettingsProvider(customAppDataFolder: _testFolder);
    }

    #region Load Tests

    [Fact]
    public void Load_NoExistingFile_ReturnsDefaultSettings()
    {
        var provider = CreateProviderWithCustomPath();

        // Der Provider verwendet den AppData-Ordner, also können wir nur das Standardverhalten testen
        var settings = provider.Load();

        Assert.NotNull(settings);
        Assert.NotNull(settings.Persona);
        Assert.NotNull(settings.Llm);
    }

    [Fact]
    public void Load_ReturnsSettingsWithDefaultValues()
    {
        var provider = CreateProviderWithCustomPath();
        var settings = provider.Load();

        // Standard-Werte prüfen
        Assert.Equal(PlatformType.YouTube, settings.DefaultPlatform);
        Assert.Equal(VideoVisibility.Unlisted, settings.DefaultVisibility);
        Assert.True(settings.AutoConnectYouTube);
        Assert.True(settings.AutoApplyDefaultTemplate);
        Assert.False(settings.ConfirmBeforeUpload);
        Assert.False(settings.OpenBrowserAfterUpload);
    }

    #endregion

    #region AppSettings Tests

    [Fact]
    public void AppSettings_DefaultValues_AreCorrect()
    {
        var settings = new AppSettings();

        Assert.Null(settings.LastVideoFolder);
        Assert.Null(settings.DefaultVideoFolder);
        Assert.Null(settings.DefaultThumbnailFolder);
        Assert.Equal(PlatformType.YouTube, settings.DefaultPlatform);
        Assert.Null(settings.DefaultPlaylistId);
        Assert.Null(settings.DefaultSchedulingTime);
        Assert.False(settings.ConfirmBeforeUpload);
        Assert.True(settings.AutoConnectYouTube);
        Assert.Equal(VideoVisibility.Unlisted, settings.DefaultVisibility);
        Assert.True(settings.AutoApplyDefaultTemplate);
        Assert.False(settings.OpenBrowserAfterUpload);
        Assert.NotNull(settings.Persona);
        Assert.NotNull(settings.Llm);
    }

    [Fact]
    public void AppSettings_CanSetAllProperties()
    {
        var settings = new AppSettings
        {
            LastVideoFolder = @"C:\Videos",
            DefaultVideoFolder = @"D:\Videos",
            DefaultThumbnailFolder = @"D:\Thumbnails",
            DefaultPlatform = PlatformType.YouTube,
            DefaultPlaylistId = "PLxyz123",
            DefaultSchedulingTime = "20:00",
            ConfirmBeforeUpload = true,
            AutoConnectYouTube = false,
            DefaultVisibility = VideoVisibility.Private,
            AutoApplyDefaultTemplate = false,
            OpenBrowserAfterUpload = true
        };

        Assert.Equal(@"C:\Videos", settings.LastVideoFolder);
        Assert.Equal(@"D:\Videos", settings.DefaultVideoFolder);
        Assert.Equal(@"D:\Thumbnails", settings.DefaultThumbnailFolder);
        Assert.Equal("PLxyz123", settings.DefaultPlaylistId);
        Assert.Equal("20:00", settings.DefaultSchedulingTime);
        Assert.True(settings.ConfirmBeforeUpload);
        Assert.False(settings.AutoConnectYouTube);
        Assert.Equal(VideoVisibility.Private, settings.DefaultVisibility);
        Assert.False(settings.AutoApplyDefaultTemplate);
        Assert.True(settings.OpenBrowserAfterUpload);
    }

    #endregion

    #region LlmSettings Tests

    [Fact]
    public void LlmSettings_DefaultValues_AreCorrect()
    {
        var settings = new LlmSettings();

        Assert.Equal(LlmMode.Off, settings.Mode);
        Assert.Null(settings.LocalModelPath);
        Assert.Equal(256, settings.MaxTokens);
        Assert.Equal(0.3f, settings.Temperature);
        Assert.Null(settings.TitleCustomPrompt);
        Assert.Null(settings.DescriptionCustomPrompt);
        Assert.Null(settings.TagsCustomPrompt);
    }

    [Fact]
    public void LlmSettings_IsLocalMode_ReturnsTrueForLocalMode()
    {
        var settings = new LlmSettings { Mode = LlmMode.Local };
        Assert.True(settings.IsLocalMode);
        Assert.False(settings.IsOff);
    }

    [Fact]
    public void LlmSettings_IsOff_ReturnsTrueForOffMode()
    {
        var settings = new LlmSettings { Mode = LlmMode.Off };
        Assert.True(settings.IsOff);
        Assert.False(settings.IsLocalMode);
    }

    #endregion

    #region ChannelPersona Tests

    [Fact]
    public void ChannelPersona_DefaultValues_AreNull()
    {
        var persona = new ChannelPersona();

        Assert.Null(persona.Name);
        Assert.Null(persona.ChannelName);
        Assert.Null(persona.Language);
        Assert.Null(persona.ToneOfVoice);
        Assert.Null(persona.ContentType);
        Assert.Null(persona.TargetAudience);
        Assert.Null(persona.AdditionalInstructions);
    }

    [Fact]
    public void ChannelPersona_CanSetAllProperties()
    {
        var persona = new ChannelPersona
        {
            Name = "Dabinuss",
            ChannelName = "Dabis Gaming",
            Language = "de-DE",
            ToneOfVoice = "locker und sarkastisch",
            ContentType = "Gaming Highlights",
            TargetAudience = "Casual Gamer 18-34",
            AdditionalInstructions = "Verwende viele Emojis"
        };

        Assert.Equal("Dabinuss", persona.Name);
        Assert.Equal("Dabis Gaming", persona.ChannelName);
        Assert.Equal("de-DE", persona.Language);
        Assert.Equal("locker und sarkastisch", persona.ToneOfVoice);
        Assert.Equal("Gaming Highlights", persona.ContentType);
        Assert.Equal("Casual Gamer 18-34", persona.TargetAudience);
        Assert.Equal("Verwende viele Emojis", persona.AdditionalInstructions);
    }

    #endregion
}
