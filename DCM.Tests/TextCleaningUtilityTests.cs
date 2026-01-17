using DCM.Core.Services;
using Xunit;

namespace DCM.Tests;

public class TextCleaningUtilityTests
{
#region RemoveQuotes Tests

    [Fact]
    public void RemoveQuotes_RemovesDoubleQuotes()
    {
        var result = TextCleaningUtility.RemoveQuotes("\"Hello\"");
        Assert.Equal("Hello", result);
    }

    [Fact]
    public void RemoveQuotes_RemovesSingleQuotes()
    {
        var result = TextCleaningUtility.RemoveQuotes("'Hello'");
        Assert.Equal("Hello", result);
    }

    [Fact]
    public void RemoveQuotes_RemovesGermanQuotes()
    {
        // Verwendung von Unicode-Escape-Sequenzen fuer deutsche Anfuehrungszeichen
        var input = "\u201EHello\u201C"; // „Hello"
        var result = TextCleaningUtility.RemoveQuotes(input);
        Assert.Equal("Hello", result);
    }

    [Fact]
    public void RemoveQuotes_RemovesFrenchQuotes()
    {
        var input = "\u00ABHello\u00BB"; // «Hello»
        var result = TextCleaningUtility.RemoveQuotes(input);
        Assert.Equal("Hello", result);
    }

    [Fact]
    public void RemoveQuotes_NoQuotes_ReturnsUnchanged()
    {
        var result = TextCleaningUtility.RemoveQuotes("Hello");
        Assert.Equal("Hello", result);
    }

    [Fact]
    public void RemoveQuotes_EmptyString_ReturnsEmpty()
    {
        var result = TextCleaningUtility.RemoveQuotes("");
        Assert.Equal("", result);
    }

    [Fact]
    public void RemoveQuotes_QuotesDirectlyAroundText_Works()
    {
        // Die Methode entfernt Anfuehrungszeichen nur wenn sie direkt am Anfang/Ende stehen
        var result = TextCleaningUtility.RemoveQuotes("\"Test\"");
        Assert.Equal("Test", result);
    }

    [Fact]
    public void RemoveQuotes_MultipleQuotes_RemovesAll()
    {
        // Bei mehreren Quotes werden alle am Anfang/Ende entfernt
        var result = TextCleaningUtility.RemoveQuotes("\"\"Test\"\"");
        Assert.Equal("Test", result);
    }

    [Fact]
    public void RemoveQuotes_MixedQuotes_RemovesAll()
    {
        // Gemischte Quote-Typen am Anfang und Ende
        var result = TextCleaningUtility.RemoveQuotes("\"'Test'\"");
        Assert.Equal("Test", result);
    }

    #endregion

    #region CleanTitleLine Tests

    [Fact]
    public void CleanTitleLine_RemovesNumberWithDot()
    {
        var result = TextCleaningUtility.CleanTitleLine("1. Mein Titel");
        Assert.Equal("Mein Titel", result);
    }

    [Fact]
    public void CleanTitleLine_RemovesNumberWithParenthesis()
    {
        var result = TextCleaningUtility.CleanTitleLine("2) Anderer Titel");
        Assert.Equal("Anderer Titel", result);
    }

    [Fact]
    public void CleanTitleLine_RemovesNumberWithColon()
    {
        var result = TextCleaningUtility.CleanTitleLine("3: Noch ein Titel");
        Assert.Equal("Noch ein Titel", result);
    }

    [Fact]
    public void CleanTitleLine_RemovesAsterisk()
    {
        var result = TextCleaningUtility.CleanTitleLine("* Aufzaehlung");
        Assert.Equal("Aufzaehlung", result);
    }

    [Fact]
    public void CleanTitleLine_RemovesDash()
    {
        var result = TextCleaningUtility.CleanTitleLine("- Bindestrich");
        Assert.Equal("Bindestrich", result);
    }

    [Fact]
    public void CleanTitleLine_RemovesBullet()
    {
        var result = TextCleaningUtility.CleanTitleLine("\u2022 Bullet"); // •
        Assert.Equal("Bullet", result);
    }

    [Fact]
    public void CleanTitleLine_RemovesQuotes()
    {
        var result = TextCleaningUtility.CleanTitleLine("\"Titel in Anfuehrung\"");
        Assert.Equal("Titel in Anfuehrung", result);
    }

    [Fact]
    public void CleanTitleLine_TrimsWhitespace()
    {
        var result = TextCleaningUtility.CleanTitleLine("  Leerzeichen  ");
        Assert.Equal("Leerzeichen", result);
    }

    [Fact]
    public void CleanTitleLine_EmptyString_ReturnsEmpty()
    {
        var result = TextCleaningUtility.CleanTitleLine("");
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void CleanTitleLine_HandlesWhitespaceOnly_ReturnsEmpty()
    {
        var result = TextCleaningUtility.CleanTitleLine("   ");
        Assert.Equal(string.Empty, result);
    }

    #endregion

    #region RemoveNumberPrefix Tests

    [Fact]
    public void RemoveNumberPrefix_RemovesDotPrefix()
    {
        var result = TextCleaningUtility.RemoveNumberPrefix("1. Test");
        Assert.Equal("Test", result.Trim());
    }

    [Fact]
    public void RemoveNumberPrefix_RemovesMultiDigitPrefix()
    {
        var result = TextCleaningUtility.RemoveNumberPrefix("12. Nummer");
        Assert.Equal("Nummer", result.Trim());
    }

    [Fact]
    public void RemoveNumberPrefix_RemovesParenthesisPrefix()
    {
        var result = TextCleaningUtility.RemoveNumberPrefix("1) Klammer");
        Assert.Equal("Klammer", result.Trim());
    }

    [Fact]
    public void RemoveNumberPrefix_RemovesColonPrefix()
    {
        var result = TextCleaningUtility.RemoveNumberPrefix("99: Doppelpunkt");
        Assert.Equal("Doppelpunkt", result.Trim());
    }

    [Fact]
    public void RemoveNumberPrefix_NoPrefix_ReturnsUnchanged()
    {
        var result = TextCleaningUtility.RemoveNumberPrefix("Kein Prefix");
        Assert.Equal("Kein Prefix", result.Trim());
    }

    [Fact]
    public void RemoveNumberPrefix_EmptyString_ReturnsEmpty()
    {
        var result = TextCleaningUtility.RemoveNumberPrefix("");
        Assert.Equal("", result);
    }

    [Fact]
    public void RemoveNumberPrefix_WhitespaceOnly_ReturnsEmpty()
    {
        var result = TextCleaningUtility.RemoveNumberPrefix("   ");
        Assert.Equal("", result.Trim());
    }

    #endregion

    #region CleanTagPrefix Tests

    [Fact]
    public void CleanTagPrefix_RemovesHashtag()
    {
        var result = TextCleaningUtility.CleanTagPrefix("#gaming");
        Assert.Equal("gaming", result);
    }

    [Fact]
    public void CleanTagPrefix_RemovesDoubleHashtag()
    {
        var result = TextCleaningUtility.CleanTagPrefix("##hashtag");
        Assert.Equal("hashtag", result);
    }

    [Fact]
    public void CleanTagPrefix_RemovesBullet()
    {
        var result = TextCleaningUtility.CleanTagPrefix("* bullet");
        Assert.Equal("bullet", result);
    }

    [Fact]
    public void CleanTagPrefix_RemovesNumberPrefix()
    {
        var result = TextCleaningUtility.CleanTagPrefix("1. numbered");
        Assert.Equal("numbered", result);
    }

    [Fact]
    public void CleanTagPrefix_NoPrefix_ReturnsUnchanged()
    {
        var result = TextCleaningUtility.CleanTagPrefix("normal");
        Assert.Equal("normal", result);
    }

    [Fact]
    public void CleanTagPrefix_EmptyString_ReturnsEmpty()
    {
        var result = TextCleaningUtility.CleanTagPrefix("");
        Assert.Equal("", result);
    }

    #endregion

    #region IsMetaLine Tests

    [Fact]
    public void IsMetaLine_EmptyString_ReturnsTrue()
    {
        Assert.True(TextCleaningUtility.IsMetaLine(""));
    }

    [Fact]
    public void IsMetaLine_WhitespaceOnly_ReturnsTrue()
    {
        Assert.True(TextCleaningUtility.IsMetaLine("   "));
    }

    [Fact]
    public void IsMetaLine_SessionBracket_ReturnsTrue()
    {
        Assert.True(TextCleaningUtility.IsMetaLine("[Session: abc123]"));
    }

    [Fact]
    public void IsMetaLine_SystemTag_ReturnsTrue()
    {
        Assert.True(TextCleaningUtility.IsMetaLine("<|system|>"));
    }

    [Fact]
    public void IsMetaLine_HashComment_ReturnsTrue()
    {
        Assert.True(TextCleaningUtility.IsMetaLine("#Kommentar"));
    }

    [Fact]
    public void IsMetaLine_TripleDash_ReturnsTrue()
    {
        Assert.True(TextCleaningUtility.IsMetaLine("---"));
    }

    [Fact]
    public void IsMetaLine_TitelPrefix_ReturnsTrue()
    {
        Assert.True(TextCleaningUtility.IsMetaLine("Titel: Test"));
    }

    [Fact]
    public void IsMetaLine_BeschreibungPrefix_ReturnsTrue()
    {
        Assert.True(TextCleaningUtility.IsMetaLine("Beschreibung: Test"));
    }

    [Fact]
    public void IsMetaLine_TagsPrefix_ReturnsTrue()
    {
        Assert.True(TextCleaningUtility.IsMetaLine("Tags: test, test2"));
    }

    [Fact]
    public void IsMetaLine_SessionPrefix_ReturnsTrue()
    {
        Assert.True(TextCleaningUtility.IsMetaLine("Session: xyz"));
    }

    [Fact]
    public void IsMetaLine_NormalText_ReturnsFalse()
    {
        Assert.False(TextCleaningUtility.IsMetaLine("Dies ist normaler Text"));
    }

    [Fact]
    public void IsMetaLine_NormalSentence_ReturnsFalse()
    {
        Assert.False(TextCleaningUtility.IsMetaLine("Ein ganz normaler Satz."));
    }

    #endregion

    #region IsErrorResponse Tests

    [Fact]
    public void IsErrorResponse_EmptyString_ReturnsTrue()
    {
        Assert.True(TextCleaningUtility.IsErrorResponse(""));
    }

    [Fact]
    public void IsErrorResponse_WhitespaceOnly_ReturnsTrue()
    {
        Assert.True(TextCleaningUtility.IsErrorResponse("   "));
    }

    [Fact]
    public void IsErrorResponse_BracketError_ReturnsTrue()
    {
        Assert.True(TextCleaningUtility.IsErrorResponse("[Fehler]"));
    }

    [Fact]
    public void IsErrorResponse_XmlError_ReturnsTrue()
    {
        Assert.True(TextCleaningUtility.IsErrorResponse("<error>"));
    }

    [Fact]
    public void IsErrorResponse_KeinTranskript_ReturnsTrue()
    {
        Assert.True(TextCleaningUtility.IsErrorResponse("Kein Transkript vorhanden"));
    }

    [Fact]
    public void IsErrorResponse_NichtMoeglich_ReturnsTrue()
    {
        // Die Methode sucht nach "nicht möglich" mit Umlaut
        Assert.True(TextCleaningUtility.IsErrorResponse("Nicht m\u00F6glich zu generieren"));
    }

    [Fact]
    public void IsErrorResponse_LlmFehler_ReturnsTrue()
    {
        Assert.True(TextCleaningUtility.IsErrorResponse("LLM Fehler aufgetreten"));
    }

    [Fact]
    public void IsErrorResponse_NormalResponse_ReturnsFalse()
    {
        Assert.False(TextCleaningUtility.IsErrorResponse("Dies ist eine normale Antwort"));
    }

    [Fact]
    public void IsErrorResponse_TitleResponse_ReturnsFalse()
    {
        Assert.False(TextCleaningUtility.IsErrorResponse("Hier ist dein Titel"));
    }

    [Fact]
    public void IsErrorResponse_NichtVerfuegbar_ReturnsTrue()
    {
        // Testet "nicht verfügbar" Pattern
        Assert.True(TextCleaningUtility.IsErrorResponse("Daten sind nicht verf\u00FCgbar"));
    }

    #endregion

    #region TruncateTranscript Tests

    [Fact]
    public void TruncateTranscript_ShortText_ReturnsUnchanged()
    {
        var input = "Kurzer Text";
        var result = TextCleaningUtility.TruncateTranscript(input, 100);
        Assert.Equal("Kurzer Text", result);
    }

    [Fact]
    public void TruncateTranscript_LongText_TruncatesWithEllipsis()
    {
        var input = "Dies ist ein sehr langer Text der gekuerzt werden muss";
        var result = TextCleaningUtility.TruncateTranscript(input, 20);
        Assert.Equal(20 + " [...]".Length, result.Length);
        Assert.EndsWith(" [...]", result);
    }

    [Fact]
    public void TruncateTranscript_EmptyString_ReturnsEmpty()
    {
        var result = TextCleaningUtility.TruncateTranscript("", 100);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void TruncateTranscript_WhitespaceOnly_ReturnsEmpty()
    {
        var result = TextCleaningUtility.TruncateTranscript("   ", 100);
        Assert.Equal(string.Empty, result);
    }

    #endregion

    #region IsValidSingleWordTag Tests

    [Fact]
    public void IsValidSingleWordTag_ValidWord_ReturnsTrue()
    {
        Assert.True(TextCleaningUtility.IsValidSingleWordTag("gaming"));
    }

    [Fact]
    public void IsValidSingleWordTag_WordWithNumbers_ReturnsTrue()
    {
        Assert.True(TextCleaningUtility.IsValidSingleWordTag("Test123"));
    }

    [Fact]
    public void IsValidSingleWordTag_GermanWord_ReturnsTrue()
    {
        Assert.True(TextCleaningUtility.IsValidSingleWordTag("ueberpruefung"));
    }

    [Fact]
    public void IsValidSingleWordTag_SingleChar_ReturnsFalse()
    {
        Assert.False(TextCleaningUtility.IsValidSingleWordTag("a"));
    }

    [Fact]
    public void IsValidSingleWordTag_EmptyString_ReturnsFalse()
    {
        Assert.False(TextCleaningUtility.IsValidSingleWordTag(""));
    }

    [Fact]
    public void IsValidSingleWordTag_TwoWords_ReturnsFalse()
    {
        Assert.False(TextCleaningUtility.IsValidSingleWordTag("two words"));
    }

    [Fact]
    public void IsValidSingleWordTag_Underscore_ReturnsFalse()
    {
        Assert.False(TextCleaningUtility.IsValidSingleWordTag("under_score"));
    }

    [Fact]
    public void IsValidSingleWordTag_Hyphen_ReturnsFalse()
    {
        Assert.False(TextCleaningUtility.IsValidSingleWordTag("hyphen-ated"));
    }

    [Fact]
    public void IsValidSingleWordTag_OnlyNumbers_ReturnsFalse()
    {
        Assert.False(TextCleaningUtility.IsValidSingleWordTag("123"));
    }

    #endregion

    #region ExtractSingleWords Tests

    [Fact]
    public void ExtractSingleWords_ExtractsFromString()
    {
        var input = "gaming streaming youtube";
        var result = TextCleaningUtility.ExtractSingleWords(input).ToList();

        Assert.Contains("gaming", result);
        Assert.Contains("streaming", result);
        Assert.Contains("youtube", result);
    }

    [Fact]
    public void ExtractSingleWords_SplitsOnVariousSeparators()
    {
        var input = "word1_word2-word3/word4";
        var result = TextCleaningUtility.ExtractSingleWords(input).ToList();

        Assert.True(result.Count >= 1);
    }

    [Fact]
    public void ExtractSingleWords_EmptyString_ReturnsEmpty()
    {
        var result = TextCleaningUtility.ExtractSingleWords("").ToList();
        Assert.Empty(result);
    }

    [Fact]
    public void ExtractSingleWords_RemovesHashtags()
    {
        var input = "#gaming";
        var result = TextCleaningUtility.ExtractSingleWords(input).ToList();

        Assert.Contains("gaming", result);
        Assert.DoesNotContain("#gaming", result);
    }

    #endregion

    #region NormalizeForComparison Tests

    [Fact]
    public void NormalizeForComparison_LowercasesText()
    {
        var result = TextCleaningUtility.NormalizeForComparison("Hello World!");
        Assert.Equal("hello world", result);
    }

    [Fact]
    public void NormalizeForComparison_AllUppercase_Lowercases()
    {
        var result = TextCleaningUtility.NormalizeForComparison("TEST");
        Assert.Equal("test", result);
    }

    [Fact]
    public void NormalizeForComparison_CollapsesSpaces()
    {
        var result = TextCleaningUtility.NormalizeForComparison("Multiple   Spaces");
        Assert.Equal("multiple spaces", result);
    }

    [Fact]
    public void NormalizeForComparison_RemovesSpecialChars()
    {
        var result = TextCleaningUtility.NormalizeForComparison("Special@#$Characters");
        Assert.Equal("special characters", result);
    }

    [Fact]
    public void NormalizeForComparison_EmptyString_ReturnsEmpty()
    {
        var result = TextCleaningUtility.NormalizeForComparison("");
        Assert.Equal("", result);
    }

    #endregion

    #region CalculateSimilarity Tests

    [Fact]
    public void CalculateSimilarity_IdenticalStrings_ReturnsOne()
    {
        var result = TextCleaningUtility.CalculateSimilarity("test string", "test string");
        Assert.Equal(1.0, result);
    }

    [Fact]
    public void CalculateSimilarity_CompletelyDifferent_ReturnsZero()
    {
        var result = TextCleaningUtility.CalculateSimilarity("abc", "xyz");
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void CalculateSimilarity_PartialMatch_ReturnsBetweenZeroAndOne()
    {
        var result = TextCleaningUtility.CalculateSimilarity("hello world", "hello there");
        Assert.True(result > 0.0 && result < 1.0);
    }

    [Fact]
    public void CalculateSimilarity_EmptyStrings_ReturnsZero()
    {
        var result = TextCleaningUtility.CalculateSimilarity("", "test");
        Assert.Equal(0.0, result);
    }

    #endregion

    #region SanitizeCustomPrompt Tests

    [Fact]
    public void SanitizeCustomPrompt_RemovesDangerousPatterns()
    {
        var input = "<|system|> Test <|end|>";
        var result = TextCleaningUtility.SanitizeCustomPrompt(input);

        Assert.DoesNotContain("<|", result);
        Assert.DoesNotContain("|>", result);
    }

    [Fact]
    public void SanitizeCustomPrompt_TruncatesLongInput()
    {
        var input = new string('a', 500);
        var result = TextCleaningUtility.SanitizeCustomPrompt(input, 100);

        Assert.Equal(100, result.Length);
    }

    [Fact]
    public void SanitizeCustomPrompt_NullInput_ReturnsEmpty()
    {
        var result = TextCleaningUtility.SanitizeCustomPrompt(null);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void SanitizeCustomPrompt_EmptyInput_ReturnsEmpty()
    {
        var result = TextCleaningUtility.SanitizeCustomPrompt("");
        Assert.Equal(string.Empty, result);
    }

    #endregion
}