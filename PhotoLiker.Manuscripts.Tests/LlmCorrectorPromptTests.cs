using PhotoLiker.Manuscripts.Fixing;

namespace PhotoLiker.Manuscripts.Tests
{
    /// <summary>What the language model is told: the language of the text, and a few examples of what a scanner gets wrong (the model itself is not run here).</summary>
    public sealed class LlmCorrectorPromptTests
    {
        [Theory]
        [InlineData(ScanLanguage.Ukrainian, "Ukrainian")]
        [InlineData(ScanLanguage.Russian, "Russian")]
        [InlineData(ScanLanguage.English, "English")]
        public void TheInstructions_SayWhichLanguageTheTextIsIn(ScanLanguage language, string name) =>
            Assert.Contains($"The text is in {name}.", LlmCorrector.SystemText(language));

        [Fact]
        public void ATextInMoreThanOneLanguage_IsToldSo()
        {
            var text = LlmCorrector.SystemText(ScanLanguage.Multilingual);

            Assert.Contains("Ukrainian, Russian or English", text);
            Assert.DoesNotContain("The text is in Multilingual", text);
        }

        [Theory]
        [InlineData(ScanLanguage.Ukrainian, 2)]
        [InlineData(ScanLanguage.Russian, 2)]
        [InlineData(ScanLanguage.English, 2)]
        [InlineData(ScanLanguage.Multilingual, 3)] // one of each language
        public void TheExamples_AreOfTheLanguage_AndAMixHasOneOfEach(ScanLanguage language, int count) =>
            Assert.Equal(count, LlmCorrector.Examples(language).Count);

        [Fact]
        public void TheInstructions_NameTheLettersThatAScanTakesForEachOther_InTheLettersOfTheLanguage()
        {
            Assert.Contains("t for f", LlmCorrector.SystemText(ScanLanguage.English));
            Assert.DoesNotContain("и for н", LlmCorrector.SystemText(ScanLanguage.English));
            Assert.Contains("и for н", LlmCorrector.SystemText(ScanLanguage.Ukrainian));
            Assert.DoesNotContain("t for f", LlmCorrector.SystemText(ScanLanguage.Russian));

            var mixed = LlmCorrector.SystemText(ScanLanguage.Multilingual);
            Assert.Contains("t for f", mixed);
            Assert.Contains("и for н", mixed);
        }

        [Fact]
        public void EveryExample_ShowsAWrongTextAndItsRightOne_WithTheSameLines()
        {
            foreach (var language in Enum.GetValues<ScanLanguage>())
            {
                foreach (var (wrong, right) in LlmCorrector.Examples(language))
                {
                    Assert.NotEqual(wrong, right);
                    Assert.Equal(wrong.Count(c => c == '\n'), right.Count(c => c == '\n')); // the lines are kept: that is what the model is shown
                    Assert.NotEmpty(WordAlignment.Edits(wrong, right)); // and the difference is one that is taken as a correction
                }
            }
        }

        [Fact]
        public void TheInstructions_KeepTheLinesAndTheMeaning_AndAskForTheTextOnly()
        {
            var text = LlmCorrector.SystemText(ScanLanguage.English);

            Assert.Contains("lines as they are", text);
            Assert.Contains("Do not translate", text);
            Assert.Contains("Answer with the corrected text only", text);
        }
    }
}
