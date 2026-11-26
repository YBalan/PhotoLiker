using System.Reflection;
using PhotoLiker.Core;

namespace PhotoLikerUI.Tests;

/// <summary>The words of the AI services window and of the AI box: each one is in the English file and in the Ukrainian one, with the same {0} places, so a text that is asked for is never shown as a name in brackets.</summary>
public sealed class AiServicesStringsTests
{
    /// <summary>The new texts that sit in the class of the Scan manuscripts window.</summary>
    private static readonly string[] ManuscriptNames =
    [
        "LabelAi", "HintAi", "AiLocal", "AiServices", "AiServicesTip", "StatusFixAskingFormat",
        "HintPhotosFrom", "StatusSourceOpenedFormat", "UseContext", "HintUseContext",
        "KindAi", "StatusAiScanNoService", "ConfirmSendPhotoFormat", "StatusAiScanDeclined", "StatusAiReadingFormat", "StatusAiReadFormat", "StatusAiReadNothingFormat",
        "StatusAiScanAllDoneFormat", "StatusAiScanFailedFormat",
        "AiPlaces", "HintAiPlaces", "StatusAiReadPlacedFormat", "StatusAiReadNoPlacesFormat", "StatusAiNoPlacesFormat", "StatusAiPlacesNoFinder",
        "RescanAll", "RescanAllTip", "ConfirmRescanAllFormat", "ConfirmRescanAllEditedFormat", "StatusRescanAllNothing",
    ];

    private static IEnumerable<string> Names() =>
        typeof(AiServicesStrings).GetProperties(BindingFlags.Public | BindingFlags.Static).Select(property => $"{nameof(AiServicesStrings)}.{property.Name}")
            .Concat(ManuscriptNames.Select(name => $"{nameof(ManuscriptStrings)}.{name}"));

    [Fact]
    public void EveryText_IsInEnglish_AndInUkrainian_WithTheSamePlaceholders()
    {
        var ukrainian = AppLanguage.Find("ua");
        Assert.Equal("Сервіси ШІ", ukrainian.Resolve("AiServicesStrings.Title")); // the file of the repository was found
        var report = ukrainian.Check();
        var names = Names().ToList();
        Assert.True(names.Count > 25);

        foreach (var name in names)
        {
            Assert.True(AppLanguage.EnglishTexts.ContainsKey(name), $"{name} is not in the English file");
            Assert.DoesNotContain(name, report.Missing);
            Assert.DoesNotContain(name, report.WrongPlaceholders);
            Assert.False(string.IsNullOrWhiteSpace(AppLanguage.EnglishTexts[name]), name);
        }
    }

    [Fact]
    public void ANameInTheFiles_ThatNoClassAsksFor_IsNotLeftBehind()
    {
        // Every text of the window that is in the English file is asked for by the class: a text that was written and never used is a name that was mistyped
        var asked = typeof(AiServicesStrings).GetProperties(BindingFlags.Public | BindingFlags.Static).Select(property => property.Name).ToHashSet();
        var inFile = AppLanguage.EnglishTexts.Keys.Where(name => name.StartsWith("AiServicesStrings.", StringComparison.Ordinal)).Select(name => name["AiServicesStrings.".Length..]);
        var unused = inFile.Where(name => !asked.Contains(name)).ToList();

        Assert.True(unused.Count == 0, "Not asked for by AiServicesStrings: " + string.Join(", ", unused));
    }

    [Fact]
    public void TheTextsOfTheClass_AreTheWordsOfTheFile_NotNamesInBrackets()
    {
        foreach (var property in typeof(AiServicesStrings).GetProperties(BindingFlags.Public | BindingFlags.Static))
            Assert.DoesNotContain("[", (string)property.GetValue(null)!, StringComparison.Ordinal);
    }
}
