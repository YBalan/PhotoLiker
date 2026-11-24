using PhotoLiker.Manuscripts.Fixing;

namespace PhotoLiker.Manuscripts.Tests
{
    /// <summary>
    /// Taking the answer of a language model word by word: only a word that is put right is taken, and the lines, tabs and blanks of the text are not touched, whatever the answer does to them. The
    /// model itself is stood in for by a corrector that answers what the test says.
    /// </summary>
    public sealed class TextFixerTests
    {
        private static string Apply(string text, IEnumerable<TextEdit> edits) => ManuscriptText.Apply(text, edits);

        private sealed class Corrector(Func<string, string> answer) : ITextCorrector
        {
            public List<string> Asked { get; } = [];

            public string Correct(string text, CancellationToken cancel)
            {
                cancel.ThrowIfCancellationRequested();
                Asked.Add(text);
                return answer(text);
            }
        }

        // ── Which words are taken ───────────────────────────────────────────

        [Fact]
        public void AWordThatIsPutRight_IsTaken_AtItsPlace()
        {
            const string text = "Today we wen to the lbrary";
            var edits = WordAlignment.Edits(text, "Today we went to the library");

            Assert.Equal([new TextEdit(9, 3, "went"), new TextEdit(20, 6, "library")], edits);
            Assert.Equal("Today we went to the library", Apply(text, edits));
        }

        [Fact]
        public void TheWordsKeepTheirPlace_WhateverTheOffset()
        {
            var edits = WordAlignment.Edits("a wen", "a went", offset: 100);

            Assert.Equal([new TextEdit(102, 3, "went")], edits);
        }

        [Fact]
        public void TheLines_TheTabsAndTheBlanks_AreNotTouched_WhateverTheAnswerDoesToThem()
        {
            const string text = "Понедилок\tчитанна\n\n   Вівторок\tМатемаитка";

            // The model joined the lines, took out the tabs and the blank line
            var edits = WordAlignment.Edits(text, "Понеділок читання Вівторок математика");

            Assert.Equal("Понеділок\tчитання\n\n   Вівторок\tматематика", Apply(text, edits));
        }

        [Fact]
        public void AWordThatIsNotInTheText_AndAWordThatWasLost_ChangeNothing()
        {
            const string text = "one two three four";

            Assert.Empty(WordAlignment.Edits(text, "one two surprise three four"));
            Assert.Empty(WordAlignment.Edits(text, "one three four"));
            Assert.Empty(WordAlignment.Edits(text, "Here is the corrected text: one two three four"));
        }

        [Fact]
        public void AWordThatBecameAnotherWord_IsNotACorrection()
        {
            // cat -> elephant is no misreading
            Assert.Empty(WordAlignment.Edits("the cat sat", "the elephant sat"));
            // more than the half of the letters are changed
            Assert.Empty(WordAlignment.Edits("Розкла", "Розлік"));
        }

        [Fact]
        public void TheNumbersOfATextAreNotChanged()
        {
            // 1945 -> 1946 is one digit of four: near enough for a word, not for a number
            Assert.Empty(WordAlignment.Edits("in 1945 we", "in 1946 we"));
            Assert.Empty(WordAlignment.Edits("room 12b", "room 13b"));
            Assert.Equal("in 1945, we", Apply("in 1945 we", WordAlignment.Edits("in 1945 we", "in 1945, we"))); // a mark is not a number
        }

        [Theory]
        [InlineData("a в b", "a у b")] // a word of one letter is never changed
        [InlineData("to do it", "to he it")] // a word of two letters is changed by one letter, not by both
        [InlineData("на на", "ті ті")]
        public void AWordOfOneLetter_IsNeverChanged_AndOneOfTwoNotByBothLetters(string text, string answer) => Assert.Empty(WordAlignment.Edits(text, answer));

        [Theory]
        [InlineData("the timetable ot lessons", "the timetable of lessons", "the timetable of lessons")] // the commonest slip: a letter of a short word
        [InlineData("it ls late", "it is late", "it is late")]
        [InlineData("вона йде нд річку", "вона йде на річку", "вона йде на річку")]
        public void OneLetterOfAWordOfTwoLetters_IsPutRight(string text, string answer, string expected) =>
            Assert.Equal(expected, Apply(text, WordAlignment.Edits(text, answer)));

        [Fact]
        public void AWordWithOtherMarksRoundIt_IsThePartnerOfItsOwnWord_NotOfAWordThatIsLikeIt()
        {
            // The answer left "bat" out. "cat," is "cat" with a mark, and that is a better partner than "bat" is (which would be corrected into "cat")
            Assert.Empty(WordAlignment.Edits("cat, bat", "cat"));
        }

        [Theory]
        [InlineData("сказала що", "сказала, що", "сказала, що")] // a comma that was missing is put
        [InlineData("в мами.", "в мами", "в мами.")] // a full stop that the answer left out stays
        [InlineData("на кухни.", "на кухню", "на кухню.")] // the word is corrected, its full stop stays
        [InlineData("лондон місто", "Лондон місто", "Лондон місто")] // a capital letter
        [InlineData("\"kuhni\" is", "\"kuhnya\" is", "\"kuhnya\" is")]
        public void TheMarksRoundAWord_AreThoseOfTheAnswer_WhenItHasThem_AndThoseOfTheTextWhenItHasNone(string text, string answer, string expected) =>
            Assert.Equal(expected, Apply(text, WordAlignment.Edits(text, answer)));

        [Fact]
        public void TheMarkOfAPicture_IsABlank_SoTheWordsOnItsSidesAreSeparateWords()
        {
            var mark = (char)0xFFFC; // what the text of a Rich Text Box has in the place of a picture
            var text = $"wen{mark}lbrary";

            Assert.Equal($"went{mark}library", Apply(text, WordAlignment.Edits(text, "went library")));
        }

        [Fact]
        public void NothingToDo_WhenTheTextOrTheAnswerHasNoWords()
        {
            Assert.Empty(WordAlignment.Edits(string.Empty, "word"));
            Assert.Empty(WordAlignment.Edits("word", "   "));
            Assert.Empty(WordAlignment.Edits("word", "word"));
        }

        [Fact]
        public void AWordThatWasLost_IsNotTakenForACorrectionOfItsNeighbour()
        {
            // "cat" of the answer is the "cat" of the text, not a correction of "bat" (which the answer left out)
            Assert.Empty(WordAlignment.Edits("bat cat", "cat"));
            Assert.Equal("farm fork", Apply("form fork", WordAlignment.Edits("form fork", "farm fork")));
        }

        // ── The pieces of a text ────────────────────────────────────────────

        [Fact]
        public void ABlankLineEndsAPiece_AndAPieceIsGivenWithItsPlace()
        {
            const string text = "first line\nsecond line\n\nthird\n\n\nfourth";
            var blocks = TextFixer.Blocks(text);

            Assert.Equal(["first line\nsecond line", "third", "fourth"], blocks.Select(block => text.Substring(block.Start, block.Length)));
        }

        [Fact]
        public void ALongParagraph_IsCutBetweenLines()
        {
            var text = string.Join("\n", Enumerable.Repeat("abcdefghij abcdefghij abcdefghij", 6)); // 6 lines of 32 letters
            var blocks = TextFixer.Blocks(text, maximum: 100);

            Assert.Equal(2, blocks.Count);
            Assert.All(blocks, block => Assert.True(block.Length <= 100));
            Assert.All(blocks, block => Assert.True(block.Start == 0 || text[block.Start - 1] == '\n')); // a piece starts where a line does
            Assert.Equal(text, string.Join("\n", blocks.Select(block => text.Substring(block.Start, block.Length))));
        }

        [Fact]
        public void ALineLongerThanTheMaximum_IsAPieceOfItsOwn()
        {
            var text = "short\n" + new string('x', 150) + "\nshort again";
            var blocks = TextFixer.Blocks(text, maximum: 100);

            Assert.Equal(3, blocks.Count);
            Assert.Equal(150, blocks[1].Length);
        }

        [Fact]
        public void APieceWithoutALetter_IsNotGivenToTheCorrector()
        {
            const string text = "12 + 34 = 46\n\nword\n\n... !!!";

            Assert.Equal(["word"], TextFixer.Blocks(text).Select(block => text.Substring(block.Start, block.Length)));
        }

        [Fact]
        public void NoPieces_ForNoText()
        {
            Assert.Empty(TextFixer.Blocks(string.Empty));
            Assert.Empty(TextFixer.Blocks("\n\n  \n"));
        }

        // ── Correcting ──────────────────────────────────────────────────────

        [Fact]
        public void EveryPieceIsGivenToTheCorrector_AndTheChangesAreAtTheirPlacesInTheWholeText()
        {
            const string text = "Wensday we wen out\n\nthe lbrary was shut\n\nThursdav";
            var corrector = new Corrector(piece => piece.Replace("Wensday", "Wednesday").Replace("wen ", "went ").Replace("lbrary", "library").Replace("Thursdav", "Thursday"));

            var edits = TextFixer.Fix(text, corrector);

            Assert.Equal(["Wensday we wen out", "the lbrary was shut", "Thursdav"], corrector.Asked);
            Assert.Equal("Wednesday we went out\n\nthe library was shut\n\nThursday", Apply(text, edits));
            Assert.Equal(4, edits.Count);
            Assert.True(edits.Zip(edits.Skip(1)).All(pair => pair.First.Index < pair.Second.Index)); // from the first to the last
        }

        [Fact]
        public void ACorrectorThatChangesNothing_GivesNoChanges() =>
            Assert.Empty(TextFixer.Fix("all is right here", new Corrector(piece => piece)));

        [Fact]
        public void ACorrectorThatTalks_IsNotTakenForTheText()
        {
            const string text = "the lbrary is shut";
            var corrector = new Corrector(_ => "Sure! Here is the corrected text:\n\nthe library is shut\n\nLet me know if you need more.");

            Assert.Equal("the library is shut", Apply(text, TextFixer.Fix(text, corrector)));
        }

        [Fact]
        public void TheProgress_IsToldAtTheStartAndAfterEachPiece()
        {
            var seen = new List<(int Done, int Total)>();

            TextFixer.Fix("one\n\ntwo\n\nthree", new Corrector(piece => piece), new SyncProgress<(int, int)>(seen.Add));

            Assert.Equal([(0, 3), (1, 3), (2, 3), (3, 3)], seen);
        }

        /// <summary>A corrector that does not look at the stop itself, so that it is the fixer that has to.</summary>
        private sealed class DeafCorrector(Action asked) : ITextCorrector
        {
            public int Count { get; private set; }

            public string Correct(string text, CancellationToken cancel)
            {
                Count++;
                asked();
                return text;
            }
        }

        [Fact]
        public void ACorrectorIsNotAskedAfterTheStop()
        {
            using var stop = new CancellationTokenSource();
            var corrector = new DeafCorrector(stop.Cancel);

            Assert.Throws<OperationCanceledException>(() => TextFixer.Fix("one\n\ntwo\n\nthree", corrector, null, stop.Token));
            Assert.Equal(1, corrector.Count); // the first was asked; the stop came before the second
        }

        private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
        {
            public void Report(T value) => report(value);
        }
    }
}
