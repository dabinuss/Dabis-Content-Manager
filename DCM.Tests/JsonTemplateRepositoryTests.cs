using DCM.Core.Configuration;
using DCM.Core.Models;
using Xunit;

namespace DCM.Tests;

public class JsonTemplateRepositoryTests : IDisposable
{
    private readonly string _testFolder;

    public JsonTemplateRepositoryTests()
    {
        _testFolder = Path.Combine(Path.GetTempPath(), $"dcm_templates_test_{Guid.NewGuid():N}");
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
            // Ignorieren
        }
    }

    private JsonTemplateRepository CreateRepository() =>
        new JsonTemplateRepository(customAppDataFolder: _testFolder);

    #region Load Tests

    [Fact]
    public void Load_ReturnsNonEmptyList()
    {
        var repository = CreateRepository();

        var templates = repository.Load().ToList();

        // Sollte mindestens das Standard-Template enthalten
        Assert.NotEmpty(templates);
    }

    [Fact]
    public void Load_ContainsDefaultTemplate()
    {
        var repository = CreateRepository();

        var templates = repository.Load().ToList();

        Assert.Contains(templates, t => t.IsDefault && t.Platform == PlatformType.YouTube);
    }

    #endregion

    #region Template Tests

    [Fact]
    public void Template_NewTemplate_HasUniqueId()
    {
        var template1 = new Template();
        var template2 = new Template();

        Assert.NotEqual(template1.Id, template2.Id);
        Assert.False(string.IsNullOrEmpty(template1.Id));
    }

    [Fact]
    public void Template_DefaultValues_AreCorrect()
    {
        var template = new Template();

        Assert.NotNull(template.Id);
        Assert.Equal(string.Empty, template.Name);
        Assert.Equal(PlatformType.YouTube, template.Platform);
        Assert.Null(template.Description);
        Assert.Equal(string.Empty, template.Body);
        Assert.False(template.IsDefault);
    }

    [Fact]
    public void Template_CanSetAllProperties()
    {
        var template = new Template
        {
            Id = "custom-id",
            Name = "Mein Template",
            Platform = PlatformType.YouTube,
            Description = "Eine Beschreibung",
            Body = "Template-Inhalt mit {{TITLE}}",
            IsDefault = true
        };

        Assert.Equal("custom-id", template.Id);
        Assert.Equal("Mein Template", template.Name);
        Assert.Equal(PlatformType.YouTube, template.Platform);
        Assert.Equal("Eine Beschreibung", template.Description);
        Assert.Equal("Template-Inhalt mit {{TITLE}}", template.Body);
        Assert.True(template.IsDefault);
    }

    #endregion
}
