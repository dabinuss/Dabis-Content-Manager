// DCM.Tests/TemplateServiceTests.cs

using DCM.Core.Models;
using DCM.Core.Services;
using Xunit;

namespace DCM.Tests;

public class TemplateServiceTests
{
    [Fact]
    public void ApplyTemplate_ReplacesKnownPlaceholders()
    {
        var service = new TemplateService();
        var project = new UploadProject
        {
            Title = "Test-Video"
        };

        var template = "Titel: {{TITLE}}";
        var result = service.ApplyTemplate(template, project);

        Assert.Equal("Titel: Test-Video", result);
    }

    [Fact]
    public void ApplyTemplate_IgnoresUnknownPlaceholders()
    {
        var service = new TemplateService();
        var project = new UploadProject
        {
            Title = "X"
        };

        var template = "Foo {{UNKNOWN}} Bar";
        var result = service.ApplyTemplate(template, project);

        Assert.Equal("Foo  Bar", result); // UNKNOWN -> ""
    }
}
