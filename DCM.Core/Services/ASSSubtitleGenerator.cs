using System.Text;
using DCM.Core.Models;

namespace DCM.Core.Services;

/// <summary>
/// Generator für ASS-Untertitel mit optionalem Word-by-Word Highlighting.
/// </summary>
public sealed class ASSSubtitleGenerator
{
    private const string DefaultTitle = "Clip Subtitle";

    /// <summary>
    /// Generiert ASS-Untertitel aus vorbereiteten Clip-Segmenten.
    /// </summary>
    public string Generate(
        IReadOnlyList<ClipSubtitleSegment> segments,
        ClipSubtitleSettings settings,
        int playResX,
        int playResY,
        string? title = null)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        var safeSegments = segments ?? Array.Empty<ClipSubtitleSegment>();
        var safePlayResX = playResX > 0 ? playResX : 1080;
        var safePlayResY = playResY > 0 ? playResY : 1920;

        var sb = new StringBuilder();
        sb.AppendLine(GenerateHeader(safePlayResX, safePlayResY, title));
        sb.AppendLine();
        sb.AppendLine(GenerateStyles(settings, safePlayResY));
        sb.AppendLine();
        sb.AppendLine(GenerateEventsHeader());

        foreach (var segment in safeSegments)
        {
            if (segment.End <= segment.Start)
            {
                continue;
            }

            var line = GenerateDialogueLine(segment, settings, safePlayResX, safePlayResY);
            if (!string.IsNullOrEmpty(line))
            {
                sb.AppendLine(line);
            }
        }

        return sb.ToString();
    }

    public string GenerateHeader(int playResX, int playResY, string? title = null)
    {
        var safeTitle = string.IsNullOrWhiteSpace(title) ? DefaultTitle : title.Trim();
        return $@"[Script Info]
Title: {safeTitle}
ScriptType: v4.00+
PlayResX: {playResX}
PlayResY: {playResY}
WrapStyle: 0";
    }

    public string GenerateStyles(ClipSubtitleSettings settings, int playResY)
    {
        var safePosition = Math.Clamp(settings.PositionY, 0.0, 1.0);
        var marginV = (int)(playResY * (1.0 - safePosition));

        var primary = ColorToAss(settings.FillColor);
        var secondary = ColorToAss(settings.HighlightColor);
        var outline = ColorToAss(settings.OutlineColor);

        var shadowDepth = settings.ShadowDepth;
        var shadowColor = settings.ShadowColor;
        if (string.IsNullOrWhiteSpace(shadowColor))
        {
            shadowColor = "#00000000";
            shadowDepth = 0;
        }

        var back = ColorToAss(shadowColor);

        return $@"[V4+ Styles]
Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
Style: Default,{settings.FontFamily},{settings.FontSize},{primary},{secondary},{outline},{back},-1,0,0,0,100,100,0,0,1,{settings.OutlineWidth},{shadowDepth},2,20,20,{marginV},1";
    }

    public string GenerateDialogueEvents(
        IReadOnlyList<ClipSubtitleSegment> segments,
        ClipSubtitleSettings settings)
    {
        return GenerateDialogueEvents(segments, settings, 1080, 1920);
    }

    public string GenerateDialogueEvents(
        IReadOnlyList<ClipSubtitleSegment> segments,
        ClipSubtitleSettings settings,
        int playResX,
        int playResY)
    {
        var sb = new StringBuilder();
        sb.AppendLine(GenerateEventsHeader());

        foreach (var segment in segments)
        {
            if (segment.End <= segment.Start)
            {
                continue;
            }

            var line = GenerateDialogueLine(segment, settings, playResX, playResY);
            if (!string.IsNullOrEmpty(line))
            {
                sb.AppendLine(line);
            }
        }

        return sb.ToString();
    }

    public static string FormatTimestamp(TimeSpan ts)
    {
        var hours = (int)ts.TotalHours;
        return $"{hours}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds / 10:D2}";
    }

    public static string ColorToAss(string? hexColor)
    {
        if (string.IsNullOrWhiteSpace(hexColor))
        {
            return "&H00FFFFFF";
        }

        var color = hexColor.Trim().TrimStart('#');

        if (color.Length == 8)
        {
            var a = color.Substring(0, 2);
            var r = color.Substring(2, 2);
            var g = color.Substring(4, 2);
            var b = color.Substring(6, 2);
            return $"&H{a}{b}{g}{r}";
        }

        if (color.Length == 6)
        {
            var r = color.Substring(0, 2);
            var g = color.Substring(2, 2);
            var b = color.Substring(4, 2);
            return $"&H00{b}{g}{r}";
        }

        return "&H00FFFFFF";
    }

    private static string GenerateEventsHeader()
    {
        return @"[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text";
    }

    private static string GenerateDialogueLine(
        ClipSubtitleSegment segment,
        ClipSubtitleSettings settings,
        int playResX,
        int playResY)
    {
        var start = FormatTimestamp(segment.Start);
        var end = FormatTimestamp(segment.End);

        var hasWords = settings.WordByWordHighlight && segment.Words is { Count: > 0 };
        var effect = hasWords ? "karaoke" : string.Empty;
        var text = hasWords
            ? BuildKaraokeText(segment.Words!)
            : EscapeAssText(segment.Text);

        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var posX = (int)Math.Round(Math.Clamp(settings.PositionX, 0.0, 1.0) * playResX);
        var posY = (int)Math.Round(Math.Clamp(settings.PositionY, 0.0, 1.0) * playResY);
        var positionedText = $"{{\\pos({posX},{posY})}}{text}";

        return $"Dialogue: 0,{start},{end},Default,,0,0,0,{effect},{positionedText}";
    }

    private static string BuildKaraokeText(IReadOnlyList<ClipSubtitleWord> words)
    {
        var sb = new StringBuilder();

        for (var i = 0; i < words.Count; i++)
        {
            var word = words[i];
            var duration = word.End - word.Start;
            var centiseconds = (int)Math.Round(duration.TotalSeconds * 100, MidpointRounding.AwayFromZero);
            if (centiseconds < 1)
            {
                centiseconds = 1;
            }

            if (i > 0)
            {
                sb.Append(' ');
            }

            sb.Append("{\\k");
            sb.Append(centiseconds);
            sb.Append('}');
            sb.Append(EscapeAssText(word.Text));
        }

        return sb.ToString();
    }

    private static string EscapeAssText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text
            .Replace("\\", "\\\\")
            .Replace("{", "\\{")
            .Replace("}", "\\}")
            .Replace("\r\n", "\\N")
            .Replace("\n", "\\N")
            .Replace("\r", "\\N");
    }
}
