using System.Text.RegularExpressions;
using PhotoLiker.Core;

namespace PhotoLiker.Tests;

/// <summary>Tests that touch the language of the whole program, or the folders language files are looked for in, run alone: every other test reads English texts.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProgramLanguageCollection
{
    public const string Name = "program language";
}

public class LanguageFileFormatTests
{
    [Theory]
    [InlineData("photo-liker-ua.txt", "ua")]
    [InlineData("PHOTO-LIKER-UA.TXT", "ua")]
    [InlineData(@"C:\x\photo-liker-pt-br.txt", "pt-br")]
    [InlineData("photo-liker-.txt", null)]
    [InlineData("photo-liker-ua.json", null)]
    [InlineData("liker-ua.txt", null)]
    [InlineData("photo-liker-u a.txt", null)]
    public void TheCode_IsInTheNameOfTheFile(string path, string? code) => Assert.Equal(code, LanguageFile.CodeOf(path));

    [Fact]
    public void ALine_IsANameAndATextAfterTheFirstEqualsSign_AndTheRestIsIgnored()
    {
        var problems = new List<string>();
        var (header, texts) = LanguageFile.Parse(
            "\uFEFF# a comment\r\n\r\nName = Deutsch\r\nPlural = one-other\r\nAboutStrings.Heading = A = B\r\n   # indented comment\r\nno equals sign\r\n  AboutStrings.OkButton   =   OK  \r\n", problems);

        Assert.Equal("Deutsch", header["Name"]);
        Assert.Equal("one-other", header["Plural"]);
        Assert.Equal("A = B", texts["AboutStrings.Heading"]);
        Assert.Equal("OK", texts["AboutStrings.OkButton"]);
        Assert.Equal(2, texts.Count);
        Assert.Single(problems);
        Assert.Contains("line 7", problems[0]);
    }

    [Fact]
    public void ANameWithADot_IsAText_AndOneWithoutIsAboutTheLanguage()
    {
        var (header, texts) = LanguageFile.Parse("Name = X\nCulture = de\nA.B = c\n");

        Assert.Equal(["Culture", "Name"], header.Keys.Order());
        Assert.Equal(["A.B"], texts.Keys);
    }

    [Fact]
    public void ANameThatIsThereTwice_IsAProblem_AndTheLastOneCounts()
    {
        var problems = new List<string>();
        var (_, texts) = LanguageFile.Parse("A.B = one\nA.B = two\n", problems);

        Assert.Equal("two", texts["A.B"]);
        Assert.Contains("twice", Assert.Single(problems));
    }

    [Theory]
    [InlineData(@"a\nb", "a\nb")]
    [InlineData(@"a\tb", "a\tb")]
    [InlineData(@"a\\b", @"a\b")]
    [InlineData(@"\s(given)", " (given)")]
    [InlineData(@"one,\s", "one, ")]
    [InlineData(@"Deleted\Duplicated", @"Deleted\Duplicated")]
    [InlineData(@"Deleted\\Duplicated", @"Deleted\Duplicated")]
    [InlineData(@"ends with a backslash\", @"ends with a backslash\")]
    [InlineData("no escapes", "no escapes")]
    public void TheEscapes_AreReadAsWhatTheyStandFor(string line, string text) => Assert.Equal(text, LanguageFile.Unescape(line));

    [Theory]
    [InlineData("two\nlines")]
    [InlineData(" (given)")]
    [InlineData("one, ")]
    [InlineData(@"Deleted\Duplicated")]
    [InlineData("tab\there")]
    [InlineData("   ")]
    [InlineData("middle  spaces stay")]
    public void WhatIsEscaped_IsReadBackTheSame(string text)
    {
        var (_, texts) = LanguageFile.Parse("A.B = " + LanguageFile.Escape(text));

        Assert.Equal(text, texts["A.B"]);
    }
}

public class PluralTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(1000, 1)]
    public void OneOther_HasTwoForms(int number, int form) => Assert.Equal(form, Plural.Index(number, PluralRule.OneOther));

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    [InlineData(10, 2)]
    [InlineData(11, 2)]
    [InlineData(12, 2)]
    [InlineData(13, 2)]
    [InlineData(14, 2)]
    [InlineData(15, 2)]
    [InlineData(20, 2)]
    [InlineData(21, 0)]
    [InlineData(22, 1)]
    [InlineData(25, 2)]
    [InlineData(101, 0)]
    [InlineData(111, 2)]
    [InlineData(112, 2)]
    [InlineData(122, 1)]
    public void EastSlavic_HasThreeForms(int number, int form) => Assert.Equal(form, Plural.Index(number, PluralRule.EastSlavic));

    [Fact]
    public void Count_PutsTheNumberBeforeTheFormOfTheLanguage()
    {
        using (Loc.Use(AppLanguage.Find("en")))
        {
            Assert.Equal("1 folder", Plural.Count(1, "folder|folders"));
            Assert.Equal("3 folders", Plural.Count(3, "folder|folders"));
            Assert.Equal($"{1234:N0} folders", Plural.Count(1234, "folder|folders"));
        }

        using (Loc.Use(AppLanguage.Find("ua")))
        {
            Assert.Equal("1 папка", Plural.Count(1, "папка|папки|папок"));
            Assert.Equal("3 папки", Plural.Count(3, "папка|папки|папок"));
            Assert.Equal("5 папок", Plural.Count(5, "папка|папки|папок"));
            Assert.Equal("21 папка", Plural.Count(21, "папка|папки|папок"));
        }
    }

    [Fact]
    public void ALanguage_WithFewerFormsThanItsRule_UsesTheLastOne()
    {
        using var _ = Loc.Use(AppLanguage.Find("ua"));

        Assert.Equal("5 фото", Plural.Count(5, "фото"));
        Assert.Equal("2 фото", Plural.Count(2, "фото|фото"));
    }
}

public class AppLanguageTests
{
    [Theory]
    [InlineData("en", "en")]
    [InlineData("ua", "ua")]
    [InlineData("UA", "ua")]
    [InlineData(" ua ", "ua")]
    [InlineData("", "en")]
    [InlineData(null, "en")]
    [InlineData("xx", "en")]
    [InlineData("uk", "en")] // the code of the culture is not the code of the file
    public void Find_TakesTheCode_AndFallsBackToEnglish(string? code, string expected) =>
        Assert.Equal(expected, AppLanguage.Find(code).Code);

    [Fact]
    public void TheLanguagesOfTheRepository_AreEnglishFirstAndThenUkrainian()
    {
        var languages = AppLanguage.Available();

        Assert.Equal(["en", "ua"], languages.Select(language => language.Code));
        Assert.Equal(["English", "Українська"], languages.Select(language => language.NativeName));
    }

    [Fact]
    public void AFile_SaysTheCultureAndTheRuleOfItsLanguage()
    {
        var ukrainian = AppLanguage.Find("ua");
        Assert.Equal("uk-UA", ukrainian.Culture.Name);
        Assert.Equal(PluralRule.EastSlavic, ukrainian.Plural);

        var english = AppLanguage.Find("en");
        Assert.Equal("en", english.Culture.Name);
        Assert.Equal(PluralRule.OneOther, english.Plural);
    }

    [Fact]
    public void English_IsBuiltIn_AndNeedsNoFile()
    {
        Assert.NotEmpty(AppLanguage.EnglishTexts);
        Assert.Equal("No photos found.", AppLanguage.EnglishTexts["AppStrings.StatusNoPhotosFound"]);
    }

    [Fact]
    public void English_IsTheDefaultOfTheConfig()
    {
        Assert.Equal("en", new GlobalConfig().Language);
    }

    [Fact]
    public void TheConfig_KeepsTheLanguage_AndAnOldConfigWithoutOneIsEnglish()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new GlobalConfig { Language = "ua" });
        Assert.Equal("ua", System.Text.Json.JsonSerializer.Deserialize<GlobalConfig>(json)!.Language);

        var old = System.Text.Json.JsonSerializer.Deserialize<GlobalConfig>("""{ "LastFolder": "C:\\Photos" }""")!;
        Assert.Equal("en", old.Language);
    }

    [Fact]
    public void TheLanguage_IsNotWrittenToTheSettingsOfAFolder()
    {
        // It is one for the whole program: a folder shared between computers must not carry the language of one of them
        var json = System.Text.Json.JsonSerializer.Serialize(new Settings());
        Assert.DoesNotContain("\"Language\"", json); // the row of the grid called Language, not SpeechLanguage
    }
}

public class LocTests
{
    [Fact]
    public void ByDefault_TheLanguageIsEnglish_AndTheTextsAreEnglish()
    {
        Assert.Equal("en", Loc.Language.Code);
        Assert.Equal("No photos found.", AppStrings.StatusNoPhotosFound);
    }

    [Fact]
    public void Use_PutsALanguageOnForTheCodeThatRuns_AndPutsTheOldOneBack()
    {
        using (Loc.Use(AppLanguage.Find("ua")))
        {
            Assert.Equal("ua", Loc.Language.Code);
            Assert.Equal("Фото не знайдено.", AppStrings.StatusNoPhotosFound);

            using (Loc.Use(AppLanguage.Find("en")))
                Assert.Equal("No photos found.", AppStrings.StatusNoPhotosFound);

            Assert.Equal("Фото не знайдено.", AppStrings.StatusNoPhotosFound);
        }

        Assert.Equal("en", Loc.Language.Code);
    }

    [Fact]
    public async Task Use_FollowsWhatTheCodeStarts_ButNotAnotherFlowOfControl()
    {
        using var _ = Loc.Use(AppLanguage.Find("ua"));

        // A task started from here is part of this code
        Assert.Equal("Фото не знайдено.", await Task.Run(() => AppStrings.StatusNoPhotosFound));

        // One that does not inherit the context (another test, a thread of the pool) reads the language of the program
        Task<string> other;
        using (ExecutionContext.SuppressFlow())
            other = Task.Run(() => AppStrings.StatusNoPhotosFound);

        Assert.Equal("No photos found.", await other);
    }

    [Fact]
    public void AName_ThatNoLanguageHas_ShowsItself_NotAnEmptyText()
    {
        Assert.Equal("[Nothing.Like.This]", Loc.Get("Nothing", "Like.This"));

        using var _ = Loc.Use(AppLanguage.Find("ua"));
        Assert.Equal("[Nothing.Like.This]", Loc.Get("Nothing.Like.This"));
    }

    [Fact]
    public void MonthNavigation_SpeaksTheLanguage()
    {
        Assert.Equal("October 2026", MonthNavigation.Describe(202610));
        Assert.Equal("no date", MonthNavigation.Describe(null));

        using var _ = Loc.Use(AppLanguage.Find("ua"));
        Assert.Equal("без дати", MonthNavigation.Describe(null));
        Assert.StartsWith("жовт", MonthNavigation.Describe(202610), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheDescriptionsOfTheMetadataRows_AreSentencesInEveryLanguage()
    {
        foreach (var language in AppLanguage.Available())
        {
            using var _ = Loc.Use(language);
            var tags = MetadataDescriptions.Tags;

            Assert.NotEmpty(tags);
            foreach (var (id, text) in tags)
            {
                Assert.True(text.Length >= 20, $"{language.Code} 0x{id:X4} is too short to explain anything: '{text}'");
                Assert.True(text.EndsWith('.'), $"{language.Code} 0x{id:X4} is not a sentence: '{text}'");
            }

            Assert.Equal(tags.Count, tags.Values.Distinct().Count());
            Assert.All(MetadataDescriptions.Decoded.Values, text => Assert.True(text.EndsWith('.'), $"{language.Code}: {text}"));
        }
    }

    [Fact]
    public void TheUkrainianDescriptions_AreNotTheEnglishOnes()
    {
        Dictionary<int, string> english;
        using (Loc.Use(AppLanguage.Find("en")))
            english = MetadataDescriptions.Tags.ToDictionary(pair => pair.Key, pair => pair.Value);

        using var _ = Loc.Use(AppLanguage.Find("ua"));
        var untranslated = MetadataDescriptions.Tags.Where(pair => english[pair.Key] == pair.Value).Select(pair => $"0x{pair.Key:X4}").ToList();

        Assert.True(untranslated.Count == 0, "the same text in Ukrainian: " + string.Join(", ", untranslated));
    }

    [Fact]
    public void GpsTexts_FollowTheLanguage_ButTheNamesOfTheRowsDoNot()
    {
        using var _ = Loc.Use(AppLanguage.Find("ua"));

        Assert.Equal("над рівнем моря", ImageHelperStrings.GpsAltitudeAboveSea);
        Assert.Equal("Latitude (decimal)", ImageHelperStrings.GpsLatitude);
        Assert.Equal("All Metadata", ImageHelperStrings.AllMetadataProperty);
    }
}

/// <summary>Language files that a person puts in a folder: found, used, and checked. They are made in a folder of their own, which the program is told to look in.</summary>
[Collection(ProgramLanguageCollection.Name)]
public class LanguageFolderTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "PhotoLikerLanguages_" + Guid.NewGuid().ToString("N"));
    private readonly string? _before = Environment.GetEnvironmentVariable(AppLanguage.FolderVariable);

    public LanguageFolderTests()
    {
        Directory.CreateDirectory(_folder);
        Environment.SetEnvironmentVariable(AppLanguage.FolderVariable, _folder);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(AppLanguage.FolderVariable, _before);
        Loc.Language = AppLanguage.Find("en");
        try { Directory.Delete(_folder, recursive: true); } catch { }
    }

    private void Write(string code, string content) =>
        File.WriteAllText(Path.Combine(_folder, LanguageFile.FileNameOf(code)), content, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

    [Fact]
    public void WithoutFiles_EnglishIsTheOnlyLanguage()
    {
        var languages = AppLanguage.Available();

        Assert.Equal(["en"], languages.Select(language => language.Code));
        Assert.Equal("No photos found.", languages[0].Resolve("AppStrings.StatusNoPhotosFound"));
        Assert.Equal("en", AppLanguage.Find("ua").Code); // no file, no such language
    }

    [Fact]
    public void AFile_MakesALanguage_AndTakingItAwayRemovesIt()
    {
        Write("de", "Name = Deutsch\nCulture = de-DE\nPlural = one-other\nAppStrings.StatusNoPhotosFound = Keine Fotos gefunden.\n");

        var languages = AppLanguage.Available();
        Assert.Equal(["en", "de"], languages.Select(language => language.Code));

        var german = AppLanguage.Find("DE");
        Assert.Equal("Deutsch", german.NativeName);
        Assert.Equal("de-DE", german.Culture.Name);
        using (Loc.Use(german))
            Assert.Equal("Keine Fotos gefunden.", AppStrings.StatusNoPhotosFound);

        File.Delete(Path.Combine(_folder, LanguageFile.FileNameOf("de")));
        Assert.Equal(["en"], AppLanguage.Available().Select(language => language.Code));
    }

    [Fact]
    public void AFileThatIsChanged_IsReadAgain()
    {
        Write("de", "Name = Deutsch\nAppStrings.StatusNoPhotosFound = Eins.\n");
        Assert.Equal("Eins.", AppLanguage.Find("de").Resolve("AppStrings.StatusNoPhotosFound"));

        Write("de", "Name = Deutsch\nAppStrings.StatusNoPhotosFound = Zwei, länger.\n");
        Assert.Equal("Zwei, länger.", AppLanguage.Find("de").Resolve("AppStrings.StatusNoPhotosFound"));
    }

    [Fact]
    public void ATextThatIsLeftOut_IsShownInEnglish_AndReported()
    {
        Write("de", "Name = Deutsch\nAppStrings.StatusNoPhotosFound = Keine Fotos gefunden.\n");
        var german = AppLanguage.Find("de");

        Assert.Equal("Keine Fotos gefunden.", german.Resolve("AppStrings.StatusNoPhotosFound"));
        Assert.Equal("Error loading folder: {0}", german.Resolve("AppStrings.StatusFolderLoadError"));
        Assert.Contains("AppStrings.StatusFolderLoadError", german.Check().Missing);
        Assert.False(german.Check().IsClean);
    }

    [Fact]
    public void ATextWithOtherPlaceholders_IsShownInEnglish_AndReported()
    {
        Write("de", "Name = Deutsch\nAppStrings.StatusFolderLoaded = {0} Fotos\nAppStrings.StatusFolderLoadError = Fehler: {0}\n");
        var german = AppLanguage.Find("de");

        Assert.Equal("Loaded {0} photos from '{1}'", german.Resolve("AppStrings.StatusFolderLoaded")); // {1} is missing: it would show wrong
        Assert.Equal("Fehler: {0}", german.Resolve("AppStrings.StatusFolderLoadError"));
        Assert.Equal(["AppStrings.StatusFolderLoaded"], german.Check().WrongPlaceholders);
    }

    [Fact]
    public void APlaceholder_MayMoveInTheText()
    {
        Write("de", "Name = Deutsch\nAppStrings.StatusFolderLoaded = Aus '{1}' wurden {0} Fotos geladen\n");

        Assert.Equal("Aus '{1}' wurden {0} Fotos geladen", AppLanguage.Find("de").Resolve("AppStrings.StatusFolderLoaded"));
    }

    [Fact]
    public void ANameTheProgramDoesNotKnow_IsReported()
    {
        Write("de", "Name = Deutsch\nNothing.Like.This = x\n");

        Assert.Equal(["Nothing.Like.This"], AppLanguage.Find("de").Check().Unknown);
    }

    [Fact]
    public void ALineThatCanNotBeRead_IsReported_AndTheRestIsUsed()
    {
        Write("de", "Name = Deutsch\nthis line has no equals sign\nAppStrings.StatusNoPhotosFound = Keine Fotos gefunden.\n");
        var german = AppLanguage.Find("de");

        Assert.Equal("Keine Fotos gefunden.", german.Resolve("AppStrings.StatusNoPhotosFound"));
        Assert.Contains("line 2", Assert.Single(german.Check().FileProblems));
    }

    [Fact]
    public void AFileWithoutAHeader_IsStillALanguage_NamedByItsCode()
    {
        Write("pl", "AppStrings.StatusNoPhotosFound = Nie znaleziono zdjęć.\n");
        var polish = AppLanguage.Find("pl");

        Assert.Equal("PL", polish.NativeName);
        Assert.Same(System.Globalization.CultureInfo.InvariantCulture, polish.Culture);
        Assert.Equal(PluralRule.OneOther, polish.Plural);
    }

    [Fact]
    public void AFileForEnglish_ReplacesTheBuiltInTexts_ButNotTheOnesItLacks()
    {
        Write("en", "Name = English (mine)\nAppStrings.StatusNoPhotosFound = Nothing here.\n");
        var english = AppLanguage.Find("en");

        Assert.Equal("English (mine)", english.NativeName);
        Assert.Equal("Nothing here.", english.Resolve("AppStrings.StatusNoPhotosFound"));
        Assert.Equal("Error loading folder: {0}", english.Resolve("AppStrings.StatusFolderLoadError"));
    }

    [Fact]
    public void TheForms_OfANoun_FollowTheRuleOfTheFile()
    {
        Write("xx", "Name = Test\nPlural = east-slavic\n");
        using var _ = Loc.Use(AppLanguage.Find("xx"));

        Assert.Equal("5 b", Plural.Count(5, "a|a|b"));
        Assert.Equal("2 a", Plural.Count(2, "a|a|b"));
    }

    [Fact]
    public void TheLanguageSetting_IsTheLanguageThatIsOn_AndAFileThatIsGoneIsEnglish()
    {
        Write("de", "Name = Deutsch\nAppStrings.StatusNoPhotosFound = Keine Fotos gefunden.\n");

        var settings = new Settings { Language = "de" };
        Assert.Equal("de", Loc.Language.Code);
        Assert.Equal("de", settings.Language);
        Assert.Equal("Keine Fotos gefunden.", AppStrings.StatusNoPhotosFound);

        settings.Language = "no-such-language";
        Assert.Equal("en", Loc.Language.Code);
        Assert.Equal("en", new Settings().Language);
    }

    [Fact]
    public void ChangingTheLanguage_RaisesChanged_OnlyForAnotherLanguage()
    {
        Write("de", "Name = Deutsch\n");
        Loc.Language = AppLanguage.Find("en");

        var raised = 0;
        void Count() => raised++;
        Loc.Changed += Count;
        try
        {
            Loc.Language = AppLanguage.Find("en"); // already on
            Assert.Equal(0, raised);

            Loc.Language = AppLanguage.Find("de");
            Assert.Equal(1, raised);

            Loc.Language = AppLanguage.Find("en");
            Assert.Equal(2, raised);
        }
        finally
        {
            Loc.Changed -= Count;
        }
    }
}

/// <summary>The language files of the repository, which are what is shipped: complete, in the shape English has, and what the code asks for is in them.</summary>
public class LanguageFilesOfTheRepositoryTests
{
    private static readonly string Root = TestEnvironment.RepositoryRoot;

    public static IEnumerable<object[]> Codes => Directory.EnumerateFiles(TestEnvironment.Languages, "photo-liker-*.txt")
        .Select(path => new object[] { LanguageFile.CodeOf(path)! });

    [Theory]
    [MemberData(nameof(Codes))]
    public void EveryLanguage_HasExactlyTheTextsOfEnglish_WithTheirPlaceholders(string code)
    {
        var report = AppLanguage.Find(code).Check();

        Assert.True(report.IsClean,
            $"{code}: missing {string.Join(", ", report.Missing.Take(5))}; unknown {string.Join(", ", report.Unknown.Take(5))}; " +
            $"other placeholders {string.Join(", ", report.WrongPlaceholders.Take(5))}; file {string.Join(", ", report.FileProblems.Take(5))}");
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void EveryLanguage_SaysWhatItIs(string code)
    {
        var (header, _) = LanguageFile.Parse(File.ReadAllText(Path.Combine(TestEnvironment.Languages, LanguageFile.FileNameOf(code))));
        var language = AppLanguage.Find(code);

        Assert.False(string.IsNullOrWhiteSpace(header.GetValueOrDefault("Name")), $"{code} has no Name");
        Assert.True(header.ContainsKey("Plural"), $"{code} has no Plural");
        Assert.True(header.TryGetValue("Culture", out var culture) && language.Culture.Name.Equals(culture, StringComparison.OrdinalIgnoreCase), $"{code} has no Culture the system knows");
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void TheFormsOfANoun_AreTheNumberTheRuleOfTheLanguageNeeds(string code)
    {
        var language = AppLanguage.Find(code);
        var expected = language.Plural == PluralRule.EastSlavic ? 3 : 2;

        // A text with | in it that is no file filter is the forms of a noun for Plural.Count
        var nouns = AppLanguage.EnglishTexts.Where(pair => pair.Value.Contains('|') && !pair.Key.EndsWith("Filter", StringComparison.Ordinal)).Select(pair => pair.Key).ToList();
        Assert.NotEmpty(nouns);

        foreach (var key in nouns)
        {
            var forms = language.Resolve(key).Split('|').Length;
            Assert.True(forms == expected, $"{code} {key}: {forms} forms, the rule needs {expected}");
        }
    }

    [Fact]
    public void TheEnglishFile_IsTheOneThatIsBuiltIn()
    {
        var (_, texts) = LanguageFile.Parse(File.ReadAllText(Path.Combine(TestEnvironment.Languages, LanguageFile.FileNameOf("en"))));

        Assert.Equal(texts.OrderBy(pair => pair.Key, StringComparer.Ordinal), AppLanguage.EnglishTexts.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.True(texts.Count > 700);
    }

    [Theory]
    [InlineData("PhotoLiker.Core")]
    [InlineData("PhotoLiker.Search")]
    [InlineData("PhotoLiker.GenerateVideo")]
    [InlineData("PhotoLiker.Video")]
    [InlineData("PhotoLikerUI")]
    public void TheCodeAsksForNothingThatIsNotThere_AndEveryTextIsAskedFor(string project)
    {
        var asked = new HashSet<string>();
        var classes = new HashSet<string>();

        // public static string Name => T();   inside   static class XStrings { ... T() => Loc.Get(nameof(XStrings), key) }
        var property = new Regex(@"(?:public|private|internal) static string (?<name>\w+) => T\(\);");
        var klass = new Regex(@"static class (?<class>\w+)");
        var helper = new Regex(@"Loc\.Get\(nameof\((?<class>\w+)\), key\)");

        foreach (var file in Directory.EnumerateFiles(Path.Combine(Root, project), "*.cs", SearchOption.TopDirectoryOnly))
        {
            var text = File.ReadAllText(file);
            if (!text.Contains("=> T();"))
                continue;

            var name = klass.Match(text).Groups["class"].Value;
            classes.Add(name);
            Assert.Equal(name, helper.Match(text).Groups["class"].Value); // the helper looks in its own class

            foreach (Match match in property.Matches(text))
            {
                var key = $"{name}.{match.Groups["name"].Value}";
                asked.Add(key);
                Assert.True(AppLanguage.EnglishTexts.ContainsKey(key), $"{Path.GetFileName(file)} asks for {key}, which photo-liker-en.txt does not have");
            }
        }

        // Which assembly a text belongs to is the class that offers it: the texts of the classes of the other projects are not asked for here
        var ofThisProject = AppLanguage.EnglishTexts.Keys.Where(key => classes.Contains(key.Split('.')[0])).ToList();
        string[] dynamic = ["AboutStrings.Shortcut", "MetadataDescriptions.Tag", "MetadataDescriptions.Decoded", "SearchStrings.ModelName", "SearchStrings.ModelLanguages", "SearchStrings.ModelDescription"];
        var unused = ofThisProject.Where(key => !asked.Contains(key) && !dynamic.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal))).ToList();

        Assert.True(unused.Count == 0, $"{project}: nobody asks for: " + string.Join(", ", unused));
    }

    [Fact]
    public void EveryTextOfTheFile_BelongsToAClassThatOffersIt()
    {
        var offered = new HashSet<string>();
        var klass = new Regex(@"static class (?<class>\w+)");
        foreach (var project in new[] { "PhotoLiker.Core", "PhotoLiker.Search", "PhotoLiker.GenerateVideo", "PhotoLiker.Video", "PhotoLikerUI" })
            foreach (var file in Directory.EnumerateFiles(Path.Combine(Root, project), "*.cs", SearchOption.TopDirectoryOnly))
            {
                var text = File.ReadAllText(file);
                if (text.Contains("=> T();"))
                    offered.Add(klass.Match(text).Groups["class"].Value);
            }

        var orphans = AppLanguage.EnglishTexts.Keys.Where(key => !offered.Contains(key.Split('.')[0])).ToList();
        Assert.True(orphans.Count == 0, "no class offers: " + string.Join(", ", orphans.Take(10)));
    }

    [Fact]
    public void TheMessagesThatNameAButton_NameItAsItIsCalledInTheSameLanguage()
    {
        // The messages name the buttons that start the work ("Update index"); a button renamed in one place only would leave them wrong
        var ukrainian = AppLanguage.Find("ua");
        var update = ukrainian.Resolve("SearchStrings.UpdateIndexButton");

        Assert.Equal("Оновити індекс", update);
        Assert.Equal("Індексувати фото", ukrainian.Resolve("SearchStrings.IndexButton"));

        var translated = AppLanguage.EnglishTexts.Keys.Where(key => ukrainian.Resolve(key) != AppLanguage.EnglishTexts[key]).ToList();
        Assert.Contains(translated, key => ukrainian.Resolve(key).Contains(update) && !key.EndsWith("UpdateIndexButton", StringComparison.Ordinal));
        Assert.DoesNotContain(translated, key => ukrainian.Resolve(key).Contains("Update index", StringComparison.OrdinalIgnoreCase));
    }
}
