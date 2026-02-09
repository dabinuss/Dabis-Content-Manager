using DCM.Core.Models;
using DCM.Core.Services;

namespace DCM.Tests;

public sealed class AssSubtitleGeneratorTests
{
    [Fact]
    public void Generate_Karaoke_WhenWordsPresent()
    {
        var generator = new ASSSubtitleGenerator();
        var settings = new ClipSubtitleSettings { WordByWordHighlight = true };

        var segment = new ClipSubtitleSegment
        {
            Start = TimeSpan.Zero,
            End = TimeSpan.FromMilliseconds(1200),
            Text = "Das ist ein Test",
            Words = new List<ClipSubtitleWord>
            {
                new()
                {
                    Text = "Das",
                    Start = TimeSpan.Zero,
                    End = TimeSpan.FromMilliseconds(300)
                },
                new()
                {
                    Text = "ist",
                    Start = TimeSpan.FromMilliseconds(300),
                    End = TimeSpan.FromMilliseconds(500)
                },
                new()
                {
                    Text = "ein",
                    Start = TimeSpan.FromMilliseconds(500),
                    End = TimeSpan.FromMilliseconds(700)
                },
                new()
                {
                    Text = "Test",
                    Start = TimeSpan.FromMilliseconds(700),
                    End = TimeSpan.FromMilliseconds(1050)
                }
            }
        };

        var ass = generator.Generate(new[] { segment }, settings, 1080, 1920);

        Assert.Contains("Dialogue: 0,0:00:00.00,0:00:01.20,Default,,0,0,0,karaoke,{\\pos(540,1344)}{\\k30}Das {\\k20}ist {\\k20}ein {\\k35}Test", ass);
    }

    [Fact]
    public void ColorToAss_HandlesArgbAndRgb()
    {
        var rgb = ASSSubtitleGenerator.ColorToAss("#FFFFFF");
        var argb = ASSSubtitleGenerator.ColorToAss("#80FF0000");

        Assert.Equal("&H00FFFFFF", rgb);
        Assert.Equal("&H800000FF", argb);
    }

    [Fact]
    public void Generate_IncludesHeaderAndStyles()
    {
        var generator = new ASSSubtitleGenerator();
        var settings = new ClipSubtitleSettings
        {
            FontFamily = "Arial Black",
            FontSize = 72,
            FillColor = "#FFFFFF",
            OutlineColor = "#000000",
            HighlightColor = "#FFFF00",
            PositionX = 0.5,
            PositionY = 0.7
        };

        var segment = new ClipSubtitleSegment
        {
            Start = TimeSpan.Zero,
            End = TimeSpan.FromSeconds(1),
            Text = "Test"
        };

        var ass = generator.Generate(new[] { segment }, settings, 1080, 1920);

        Assert.Contains("Title: Clip Subtitle", ass);
        Assert.Contains("PlayResX: 1080", ass);
        Assert.Contains("PlayResY: 1920", ass);
        Assert.Contains("Style: Default,Arial Black,72,&H00FFFFFF,&H0000FFFF,&H00000000", ass);
        Assert.Contains("{\\pos(540,1344)}Test", ass);
    }
}
