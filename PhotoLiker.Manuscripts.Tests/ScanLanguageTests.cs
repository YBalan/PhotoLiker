namespace PhotoLiker.Manuscripts.Tests
{
    public sealed class ScanLanguageTests
    {
        [Theory]
        [InlineData(ScanKind.Handwriting)]
        [InlineData(ScanKind.Printed)]
        public void EveryKindOfWriting_IsReadInEveryLanguage_EnglishAndTheMixOfThemToo(ScanKind kind)
        {
            Assert.Equal([ScanLanguage.Ukrainian, ScanLanguage.Russian, ScanLanguage.English, ScanLanguage.Multilingual], ScanLanguages.For(kind));
            Assert.All(Enum.GetValues<ScanLanguage>(), language => Assert.True(ScanLanguages.IsSupported(kind, language)));
        }

        [Theory]
        [InlineData(ScanLanguage.Ukrainian, ScanLanguage.Ukrainian)]
        [InlineData(ScanLanguage.Russian, ScanLanguage.Russian)]
        [InlineData(ScanLanguage.English, ScanLanguage.Ukrainian)] // the Ukrainian network knows the Latin letters
        [InlineData(ScanLanguage.Multilingual, ScanLanguage.Ukrainian)]
        public void TheHandwritingNetwork_IsRussianForRussian_AndTheUkrainianOneForTheRest(ScanLanguage language, ScanLanguage network) =>
            Assert.Equal(network, ScanLanguages.HandwritingModelOf(language));

        [Theory]
        [InlineData(ScanLanguage.Ukrainian, "ua")]
        [InlineData(ScanLanguage.Russian, "ru")]
        [InlineData(ScanLanguage.English, "en")]
        [InlineData(ScanLanguage.Multilingual, "mul")]
        public void TheCode_IsTheOneAnEpubKnowsTheLanguageBy(ScanLanguage language, string code) => Assert.Equal(code, ScanLanguages.Code(language));

        [Theory]
        [InlineData("ы")]
        [InlineData("і")]
        [InlineData("a")]
        [InlineData("Ж")]
        [InlineData("5")]
        public void AMixOfLanguages_IsHeldToNone(string character) => Assert.True(ScanLanguages.Allows(ScanLanguage.Multilingual, character));

        [Fact]
        public void InAMixOfLanguages_AWordThatHasACyrillicLetterIsCyrillicThrough_ButALoneIIsTheEnglishOne()
        {
            Assert.Equal("Дорога to школа", ScanLanguages.Normalize(ScanLanguage.Multilingual, "Дoрoга to шкoла"));
            Assert.Equal("пиріг", ScanLanguages.Normalize(ScanLanguage.Multilingual, "пиpiг"));
            Assert.Equal("I live in Киів", ScanLanguages.Normalize(ScanLanguage.Multilingual, "I live in Киiв"));
            Assert.Equal("cat кіт", ScanLanguages.Normalize(ScanLanguage.Multilingual, "cat кiт"));
            Assert.Equal("Я i ти", ScanLanguages.Normalize(ScanLanguage.Multilingual, "Я i ти")); // no way to tell it from "I" here, so it stays
        }

        [Theory]
        [InlineData("ы")]
        [InlineData("Э")]
        [InlineData("ъ")]
        [InlineData("ё")]
        public void Ukrainian_HasNoLettersOnlyRussianHas(string letter)
        {
            Assert.False(ScanLanguages.Allows(ScanLanguage.Ukrainian, letter));
            Assert.True(ScanLanguages.Allows(ScanLanguage.Russian, letter));
        }

        [Theory]
        [InlineData("і")]
        [InlineData("ї")]
        [InlineData("є")]
        [InlineData("Ґ")]
        public void Russian_HasNoLettersOnlyUkrainianHas(string letter)
        {
            Assert.False(ScanLanguages.Allows(ScanLanguage.Russian, letter));
            Assert.True(ScanLanguages.Allows(ScanLanguage.Ukrainian, letter));
        }

        [Theory]
        [InlineData("а")]
        [InlineData("Я")]
        [InlineData("д")]
        public void EnglishHasNoCyrillicAtAll_AndTheOthersKeepTheLettersTheyShare(string letter)
        {
            Assert.False(ScanLanguages.Allows(ScanLanguage.English, letter));
            Assert.True(ScanLanguages.Allows(ScanLanguage.Ukrainian, letter));
            Assert.True(ScanLanguages.Allows(ScanLanguage.Russian, letter));
        }

        [Theory]
        [InlineData("Дoрoга бабyсю", "Дорога бабусю")]          // Latin o and y among Cyrillic letters
        [InlineData("Мaмa ввecти", "Мама ввести")]
        [InlineData("пиpiг", "пиріг")]                           // the Latin i of a Ukrainian word is the Ukrainian letter
        [InlineData("Я i ти", "Я і ти")]                         // a lone Latin i between words is the conjunction
        [InlineData("Wi-Fi та iPhone", "Wi-Fi та iPhone")]        // words that are Latin through stay
        [InlineData("2026 рік", "2026 рік")]
        [InlineData("", "")]
        public void ALatinLookalikeAmongCyrillicLetters_IsPutRight_InUkrainianText(string read, string expected) =>
            Assert.Equal(expected, ScanLanguages.Normalize(ScanLanguage.Ukrainian, read));

        [Fact]
        public void InRussianText_TheLookalikesAreSwapped_ButTheLatinIStays_ItIsNoLetterOfTheirs()
        {
            Assert.Equal("Хорошо", ScanLanguages.Normalize(ScanLanguage.Russian, "Xopoшo"));
            Assert.Equal("Я i ты", ScanLanguages.Normalize(ScanLanguage.Russian, "Я i ты"));
            Assert.Equal("Привiт", ScanLanguages.Normalize(ScanLanguage.Russian, "Привiт"));
        }

        [Fact]
        public void TheLinesOfAScan_KeepTheirBoxesAndSureness_WhenTheirTextIsPutRight()
        {
            var box = PhotoLiker.Drawing.Ocr.TextRegion.FromCentre(new System.Drawing.PointF(50, 10), 80, 12, 0);
            var result = new PhotoLiker.Drawing.Ocr.ScanResult([new PhotoLiker.Drawing.Ocr.TextLine("Дoрoга", 0.77f, box)]);

            var normal = ManuscriptScanner.Normalized(result, ScanLanguage.Ukrainian);

            Assert.Equal("Дорога", normal.Lines[0].Text);
            Assert.Equal(0.77f, normal.Lines[0].Confidence);
            Assert.Equal(box, normal.Lines[0].Box);
            Assert.Same(result, ManuscriptScanner.Normalized(result, ScanLanguage.English));
        }

        [Fact]
        public void EnglishText_IsNeverChanged()
        {
            Assert.Equal("Hello i world", ScanLanguages.Normalize(ScanLanguage.English, "Hello i world"));
        }

        [Theory]
        [InlineData("a")]
        [InlineData("Z")]
        [InlineData("7")]
        [InlineData(",")]
        [InlineData(" ")]
        [InlineData("-")]
        public void DigitsMarksAndLatinLettersAreAllowedInEveryLanguage(string character)
        {
            foreach (var language in Enum.GetValues<ScanLanguage>())
                Assert.True(ScanLanguages.Allows(language, character), $"{character} in {language}");
        }
    }
}
