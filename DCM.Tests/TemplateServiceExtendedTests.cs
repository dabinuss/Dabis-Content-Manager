using DCM.Core.Models;
using DCM.Core.Services;
using Xunit;

namespace DCM.Tests;

public class TemplateServiceExtendedTests
{
    private readonly TemplateService _service;

    public TemplateServiceExtendedTests()
    {
        _service = new TemplateService();
    }

    #region ApplyTemplate with Dictionary Tests

    [Fact]
    public void ApplyTemplate_Dictionary_NullTemplate_ThrowsArgumentNullException()
    {
        IDictionary<string, string?> values = new Dictionary<string, string?>();

        Assert.Throws<ArgumentNullException>(() => _service.ApplyTemplate(null!, values));
    }

    [Fact]
    public void ApplyTemplate_Dictionary_NullValues_ThrowsArgumentNullException()
    {
        IDictionary<string, string?> nullDict = null!;
        Assert.Throws<ArgumentNullException>(() => _service.ApplyTemplate("test", nullDict));
    }

    [Fact]
    public void ApplyTemplate_Dictionary_ReplacesMultiplePlaceholders()
    {
        var template = "{{A}} und {{B}} und {{C}}";
        IDictionary<string, string?> values = new Dictionary<string, string?>
        {
            ["A"] = "Eins",
            ["B"] = "Zwei",
            ["C"] = "Drei"
        };

        var result = _service.ApplyTemplate(template, values);

        Assert.Equal("Eins und Zwei und Drei", result);
    }

    [Fact]
    public void ApplyTemplate_Dictionary_CaseInsensitive()
    {
        var template = "{{title}} und {{TITLE}} und {{Title}}";
        IDictionary<string, string?> values = new Dictionary<string, string?>
        {
            ["TITLE"] = "Test"
        };

        var result = _service.ApplyTemplate(template, values);

        Assert.Equal("Test und Test und Test", result);
    }

    [Fact]
    public void ApplyTemplate_Dictionary_NullValue_ReplacesWithEmpty()
    {
        var template = "Start {{TEST}} Ende";
        IDictionary<string, string?> values = new Dictionary<string, string?>
        {
            ["TEST"] = null
        };

        var result = _service.ApplyTemplate(template, values);

        Assert.Equal("Start  Ende", result);
    }

    #endregion

    #region ApplyTemplate with UploadProject Tests

    [Fact]
    public void ApplyTemplate_Project_NullProject_ThrowsArgumentNullException()
    {
        UploadProject nullProject = null!;
        Assert.Throws<ArgumentNullException>(() => _service.ApplyTemplate("test", nullProject));
    }

    [Fact]
    public void ApplyTemplate_Project_ReplacesDatePlaceholder()
    {
        var project = new UploadProject
        {
            ScheduledTime = new DateTimeOffset(2024, 6, 15, 18, 0, 0, TimeSpan.Zero)
        };

        var template = "Datum: {{DATE}}";
        var result = _service.ApplyTemplate(template, project);

        Assert.Equal("Datum: 2024-06-15", result);
    }

    [Fact]
    public void ApplyTemplate_Project_ReplacesTagsPlaceholder()
    {
        var project = new UploadProject();
        project.Tags.Add("gaming");
        project.Tags.Add("tutorial");

        var template = "Tags: {{TAGS}}";
        var result = _service.ApplyTemplate(template, project);

        Assert.Equal("Tags: gaming, tutorial", result);
    }

    [Fact]
    public void ApplyTemplate_Project_ReplacesHashtagsPlaceholder()
    {
        var project = new UploadProject();
        project.Tags.Add("gaming");
        project.Tags.Add("deutsch");

        var template = "{{HASHTAGS}}";
        var result = _service.ApplyTemplate(template, project);

        Assert.Contains("#gaming", result);
        Assert.Contains("#deutsch", result);
    }

    [Fact]
    public void ApplyTemplate_Project_ReplacesVisibilityPlaceholder()
    {
        var project = new UploadProject
        {
            Visibility = VideoVisibility.Private
        };

        var template = "Sichtbarkeit: {{VISIBILITY}}";
        var result = _service.ApplyTemplate(template, project);

        Assert.Equal("Sichtbarkeit: Private", result);
    }

    [Fact]
    public void ApplyTemplate_Project_ReplacesPlatformPlaceholder()
    {
        var project = new UploadProject
        {
            Platform = PlatformType.YouTube
        };

        var template = "Plattform: {{PLATFORM}}";
        var result = _service.ApplyTemplate(template, project);

        Assert.Equal("Plattform: YouTube", result);
    }

    [Fact]
    public void ApplyTemplate_Project_ReplacesCreatedAtPlaceholder()
    {
        var project = new UploadProject();
        
        // CreatedAt wird beim Erstellen gesetzt, wir pruefen nur das Format
        var template = "Erstellt: {{CREATED_AT}}";
        var result = _service.ApplyTemplate(template, project);

        // Sollte ein Datum-Zeit-Format enthalten
        Assert.Matches(@"\d{4}-\d{2}-\d{2} \d{2}:\d{2}", result);
    }

    [Fact]
    public void ApplyTemplate_Project_ReplacesTranscriptSnippet()
    {
        var project = new UploadProject
        {
            TranscriptText = "Dies ist ein Transkript-Text der gekuerzt werden koennte."
        };

        var template = "Snippet: {{TRANSCRIPT_SNIPPET}}";
        var result = _service.ApplyTemplate(template, project);

        Assert.Contains("Transkript", result);
    }

    [Fact]
    public void ApplyTemplate_Project_LongTranscript_IsTruncated()
    {
        var project = new UploadProject
        {
            TranscriptText = new string('a', 500)
        };

        var template = "{{TRANSCRIPT_SNIPPET}}";
        var result = _service.ApplyTemplate(template, project);

        Assert.True(result.Length < 500);
        Assert.EndsWith("\u2026", result); // Unicode-Ellipsis
    }

    [Fact]
    public void ApplyTemplate_Project_ReplacesVideoFilePlaceholder()
    {
        var project = new UploadProject
        {
            VideoFilePath = @"C:\Videos\mein_video.mp4"
        };

        var template = "Datei: {{VIDEOFILE}}";
        var result = _service.ApplyTemplate(template, project);

        Assert.Equal("Datei: mein_video.mp4", result);
    }

    [Fact]
    public void ApplyTemplate_Project_ReplacesVideoPathPlaceholder()
    {
        var project = new UploadProject
        {
            VideoFilePath = @"C:\Videos\mein_video.mp4"
        };

        var template = "Pfad: {{VIDEOPATH}}";
        var result = _service.ApplyTemplate(template, project);

        Assert.Equal(@"Pfad: C:\Videos\mein_video.mp4", result);
    }

    [Fact]
    public void ApplyTemplate_Project_ReplacesScheduledDateAndTime()
    {
        var project = new UploadProject
        {
            ScheduledTime = new DateTimeOffset(2024, 12, 24, 18, 30, 0, TimeSpan.Zero)
        };

        var template = "Am {{SCHEDULEDDATE}} um {{SCHEDULEDTIME}}";
        var result = _service.ApplyTemplate(template, project);

        Assert.Equal("Am 2024-12-24 um 18:30", result);
    }

    [Fact]
    public void ApplyTemplate_Project_ReplacesDescriptionPlaceholder()
    {
        var project = new UploadProject
        {
            Description = "Dies ist die Beschreibung"
        };

        var template = "Beschreibung: {{DESCRIPTION}}";
        var result = _service.ApplyTemplate(template, project);

        Assert.Equal("Beschreibung: Dies ist die Beschreibung", result);
    }

    [Fact]
    public void ApplyTemplate_Project_ReplacesYearMonthDay()
    {
        var project = new UploadProject();

        var template = "{{YEAR}}-{{MONTH}}-{{DAY}}";
        var result = _service.ApplyTemplate(template, project);

        var today = DateTime.Now;
        var expected = $"{today.Year}-{today.Month:D2}-{today.Day:D2}";
        Assert.Equal(expected, result);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ApplyTemplate_EmptyTemplate_ReturnsEmpty()
    {
        var project = new UploadProject { Title = "Test" };

        var result = _service.ApplyTemplate("", project);

        Assert.Equal("", result);
    }

    [Fact]
    public void ApplyTemplate_NoPlaceholders_ReturnsUnchanged()
    {
        var project = new UploadProject { Title = "Test" };
        var template = "Keine Platzhalter hier";

        var result = _service.ApplyTemplate(template, project);

        Assert.Equal("Keine Platzhalter hier", result);
    }

    [Fact]
    public void ApplyTemplate_MalformedPlaceholders_AreIgnored()
    {
        var project = new UploadProject { Title = "Test" };
        var template = "{{TITLE}} {BROKEN} {{ ALSO_BROKEN}";

        var result = _service.ApplyTemplate(template, project);

        Assert.Contains("Test", result);
        Assert.Contains("{BROKEN}", result);
    }

    [Fact]
    public void ApplyTemplate_PlaceholderWithSpaces_Works()
    {
        var project = new UploadProject { Title = "Mein Titel" };
        var template = "{{ TITLE }}";

        var result = _service.ApplyTemplate(template, project);

        Assert.Equal("Mein Titel", result);
    }

    #endregion
}