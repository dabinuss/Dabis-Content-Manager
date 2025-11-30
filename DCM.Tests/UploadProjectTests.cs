// DCM.Tests/UploadProjectTests.cs

using System;
using DCM.Core.Models;
using Xunit;

namespace DCM.Tests;

public class UploadProjectTests
{
    [Fact]
    public void SetTagsFromCsv_SplitsAndTrims()
    {
        var project = new UploadProject();

        project.SetTagsFromCsv(" tag1,tag2 , tag3 , , ");

        Assert.Equal(3, project.Tags.Count);
        Assert.Contains("tag1", project.Tags);
        Assert.Contains("tag2", project.Tags);
        Assert.Contains("tag3", project.Tags);
    }

    [Fact]
    public void GetTagsAsCsv_JoinsTags()
    {
        var project = new UploadProject();
        project.Tags.AddRange(new[] { "a", "b", "c" });

        var csv = project.GetTagsAsCsv();

        Assert.Equal("a, b, c", csv);
    }

    [Fact]
    public void Validate_Throws_WhenVideoPathMissing()
    {
        var project = new UploadProject
        {
            VideoFilePath = "",
            Title = "Test"
        };

        Assert.Throws<InvalidOperationException>(() => project.Validate());
    }

    [Fact]
    public void Validate_Throws_WhenTitleMissing()
    {
        var project = new UploadProject
        {
            VideoFilePath = "C:\\video.mp4",
            Title = ""
        };

        Assert.Throws<InvalidOperationException>(() => project.Validate());
    }

    [Fact]
    public void Validate_Throws_WhenScheduledTimeInPast()
    {
        var project = new UploadProject
        {
            VideoFilePath = "C:\\video.mp4",
            Title = "Test",
            ScheduledTime = DateTimeOffset.Now.AddHours(-2)
        };

        Assert.Throws<InvalidOperationException>(() => project.Validate());
    }

    [Fact]
    public void Validate_DoesNotThrow_ForValidProject()
    {
        var project = new UploadProject
        {
            VideoFilePath = "C:\\video.mp4",
            Title = "Test",
            ScheduledTime = DateTimeOffset.Now.AddHours(1)
        };

        var ex = Record.Exception(() => project.Validate());

        Assert.Null(ex);
    }
}
