using DCM.Core.Models;
using DCM.Core.Services;

namespace DCM.Tests;

public class CandidateWindowGeneratorTests
{
    [Fact]
    public void GenerateWindows_EmptySegments_ReturnsEmpty()
    {
        var generator = new CandidateWindowGenerator();

        var result = generator.GenerateWindows(Array.Empty<ITimedSegment>());

        Assert.Empty(result);
    }

    [Fact]
    public void GenerateWindows_UsesConfiguredDurationBounds()
    {
        var generator = new CandidateWindowGenerator();
        var segments = new List<ITimedSegment>
        {
            new TimedSegment
            {
                Text = "A",
                Start = TimeSpan.FromSeconds(0),
                End = TimeSpan.FromSeconds(8)
            },
            new TimedSegment
            {
                Text = "B",
                Start = TimeSpan.FromSeconds(8),
                End = TimeSpan.FromSeconds(16)
            },
            new TimedSegment
            {
                Text = "C",
                Start = TimeSpan.FromSeconds(16),
                End = TimeSpan.FromSeconds(24)
            }
        };

        var result = generator.GenerateWindows(
            segments,
            minDuration: TimeSpan.FromSeconds(8),
            maxDuration: TimeSpan.FromSeconds(18),
            stepDuration: TimeSpan.FromSeconds(4));

        Assert.NotEmpty(result);
        Assert.All(result, window =>
        {
            Assert.InRange(window.Duration.TotalSeconds, 8, 18);
            Assert.False(string.IsNullOrWhiteSpace(window.Text));
        });
    }

    [Fact]
    public void GenerateWindows_DeduplicatesStronglyOverlappingWindows()
    {
        var generator = new CandidateWindowGenerator();
        var segments = new List<ITimedSegment>
        {
            new TimedSegment
            {
                Text = "Hook",
                Start = TimeSpan.FromSeconds(0),
                End = TimeSpan.FromSeconds(30)
            },
            new TimedSegment
            {
                Text = "Payoff",
                Start = TimeSpan.FromSeconds(30),
                End = TimeSpan.FromSeconds(60)
            }
        };

        var result = generator.GenerateWindows(
            segments,
            minDuration: TimeSpan.FromSeconds(15),
            maxDuration: TimeSpan.FromSeconds(90),
            stepDuration: TimeSpan.FromSeconds(10));

        // Ohne Deduplizierung würden hier mehrere nahezu identische Fenster entstehen.
        Assert.Single(result);
        Assert.Equal(TimeSpan.Zero, result[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(60), result[0].End);
    }
}
