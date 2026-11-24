using PhotoLiker.Effects.Models;
using PhotoLiker.Manuscripts;
using PhotoLiker.Manuscripts.Fixing;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The language model that corrects the text, on its real files (the 1.8 GB model and its tokenizer, which are not in the repository: they are looked for in <c>PHOTOLIKER_MODELS</c> and in the models
/// folder of the program, as for <see cref="RealManuscriptsTests"/>). A test whose files are not there ends at once and counts as passed, so the machines that build the program, which have no model,
/// are not held up. The numbers they are compared with are what the tokenizers library of Hugging Face and the Python runtime of ONNX Runtime (the same version) gave for the same files.
/// </summary>
public sealed class RealTextFixerTests
{
    private static (string Model, string Tokenizer)? Files =>
        RealManuscriptsTests.Model(ToolModels.TextFixer) is { } model && RealManuscriptsTests.Model(ToolModels.TextFixerTokenizer) is { } tokenizer ? (model, tokenizer) : null;

    /// <summary>Texts and the numbers of their tokens, by the tokenizers library.</summary>
    private static readonly (string Text, int[] Ids)[] TokenCases =
    [
        ("Розкла на тижбенв\nЦвого тижня ми виймо", [33504, 19645, 132308, 13073, 10813, 128144, 9923, 5259, 5474, 198, 92799, 5474, 22107, 10813, 128144, 39151, 11310, 1802, 5805, 43838, 126242]),
        ("Понедилок\tчитанна\nВівторок\tМатемаитка", [16854, 17316, 13103, 63192, 14746, 197, 90253, 7336, 14191, 198, 16206, 26289, 5474, 36673, 14746, 197, 37991, 7972, 10090, 1478, 9358, 13132]),
        ("Today we wen to the lbrary, it's 3 o'clock; don't worry!!", [15364, 582, 37357, 311, 279, 326, 1323, 658, 11, 432, 594, 220, 18, 297, 62410, 26, 1513, 944, 10955, 2928]),
        ("  leading spaces and    multiple   spaces\n\n\nthree newlines", [220, 6388, 12621, 323, 262, 5248, 256, 12621, 1406, 27856, 501, 7969]),
        ("Ім'я: Ольга. 12345 числа 3,14.", [144113, 6442, 6, 4235, 25, 34348, 28018, 128440, 13, 220, 16, 17, 18, 19, 20, 40076, 30343, 220, 18, 11, 16, 19, 13]),
        ("Emoji 😀 and ünïcödé, “quotes” — dash … ellipsis", [92731, 90316, 323, 10489, 77, 37572, 66, 2956, 128505, 11, 1036, 53282, 854, 1959, 23911, 4593, 25607, 47402]),
        ("system\nYou correct the text", [8948, 198, 2610, 4396, 279, 1467]),
        ("assistant\n", [77091, 198]),
        ("\n", [198]),
        ("Сегодня мы ходили в лес и собрали много гриоов.", [19311, 127247, 87200, 44558, 9516, 126227, 5805, 25460, 22484, 7587, 124972, 22127, 11073, 128991, 24725, 30103, 1456, 6715, 13]),
        ("ʼ апостроф і ї є ґ Ї Є Ґ", [144803, 20396, 8005, 24276, 124120, 19355, 83854, 8839, 245, 8839, 242, 220, 144275, 1278, 229, 1278, 226, 220, 145708]),
        ("á combining and NFC é", [1953, 34171, 323, 47205, 3958]),
    ];

    private const string ShortPrompt = "<|im_start|>system\nYou are a helpful assistant.<|im_end|>\n<|im_start|>user\nName three colours of a rainbow, in English, separated by commas.<|im_end|>\n<|im_start|>assistant\n";
    private const string LongPrompt = "<|im_start|>system\nYou are a helpful assistant.<|im_end|>\n<|im_start|>user\nRepeat the following text exactly, and add nothing: The quick brown fox jumps over the lazy dog while the sun sets slowly behind the old stone bridge, and the children play by the river until their mothers call them home for supper, as they do every evening in the village.<|im_end|>\n<|im_start|>assistant\n";

    /// <summary>The tokens of a prompt: the marks of a chat are the special tokens, the rest is text (as the corrector builds a prompt).</summary>
    private static List<int> Prompt(ByteLevelTokenizer tokenizer, string text)
    {
        var tokens = new List<int>();
        var rest = text;
        while (rest.Length > 0)
        {
            var next = new[] { "<|im_start|>", "<|im_end|>" }.Select(mark => (Mark: mark, At: rest.IndexOf(mark, StringComparison.Ordinal))).Where(found => found.At >= 0).OrderBy(found => found.At).FirstOrDefault();
            if (next.Mark is null)
            {
                tokens.AddRange(tokenizer.Encode(rest));
                break;
            }

            tokens.AddRange(tokenizer.Encode(rest[..next.At]));
            tokens.Add(tokenizer.IdOf(next.Mark));
            rest = rest[(next.At + next.Mark.Length)..];
        }

        return tokens;
    }

    private static IReadOnlySet<int> Stop(ByteLevelTokenizer tokenizer) => new HashSet<int> { tokenizer.IdOf("<|im_end|>"), tokenizer.IdOf("<|endoftext|>") };

    [Fact]
    public void TheTokenizer_GivesTheNumbersOfTheTokenizersLibrary_ForTextInThreeLanguages_TabsLinesAndEmoji()
    {
        if (Files is not { } files)
            return;

        var tokenizer = ByteLevelTokenizer.Load(files.Tokenizer);
        foreach (var (text, ids) in TokenCases)
        {
            Assert.Equal(ids, tokenizer.Encode(text));
            Assert.Equal(text.Normalize(System.Text.NormalizationForm.FormC), tokenizer.Decode(ids));
        }
    }

    [Fact]
    public void TheMarksOfAChat_AreTheSpecialTokensOfTheModel_AndAPromptHasTheNumberOfTokensTheLibraryGives()
    {
        if (Files is not { } files)
            return;

        var tokenizer = ByteLevelTokenizer.Load(files.Tokenizer);

        // The same as the tokenizers library gives for the text with the marks in it: 33 tokens for the short prompt, the marks being 151644 and 151645
        var tokens = Prompt(tokenizer, ShortPrompt);
        Assert.Equal(33, tokens.Count);
        Assert.Equal(151644, tokens[0]);
        Assert.Equal(9, tokens.IndexOf(151645)); // system, new line, "You are a helpful assistant" and its full stop are 8 tokens
        Assert.Equal(151644, tokenizer.IdOf("<|im_start|>"));
        Assert.Equal(151645, tokenizer.IdOf("<|im_end|>"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheModel_AnswersAsTheReferenceRuntimeDoes_ForAShortAndForALongPrompt_AlsoWhenTheStartOfThePromptWasReadBefore(bool fromWhatWasRead)
    {
        if (Files is not { } files)
            return;

        var tokenizer = ByteLevelTokenizer.Load(files.Tokenizer);
        using var model = ChatModel.Open(files.Model);
        var stop = Stop(tokenizer);

        (string Prompt, int[] Answer)[] cases =
        [
            (ShortPrompt, [6033, 11, 21958, 11, 25462]), // Red, Orange, Yellow
            (LongPrompt, [785, 3974, 13876, 38835, 34208, 916, 279, 15678, 5562, 1393, 279, 7015, 7289, 13970, 4815, 279, 2310, 9798, 14164, 11, 323, 279, 2841, 1486]), // 74 tokens: more than one chunk
        ];
        foreach (var (prompt, answer) in cases)
        {
            var tokens = Prompt(tokenizer, prompt);
            if (!fromWhatWasRead)
            {
                Assert.Equal(answer, model.Generate(tokens, 24, stop));
                continue;
            }

            // The first 20 tokens are read once, and the prompt goes on from them: the answer is the same, and what was read is not used up
            using var read = model.Read(tokens.Take(20).ToList());
            Assert.Equal(20, read.Length);
            Assert.Equal(answer, model.Generate(tokens.Skip(20).ToList(), 24, stop, read));
            Assert.Equal(answer, model.Generate(tokens.Skip(20).ToList(), 24, stop, read));
        }
    }

    [Fact]
    public void TheModel_StopsWhenAsked_AndAnswersNoMoreThanItWasToldTo()
    {
        if (Files is not { } files)
            return;

        var tokenizer = ByteLevelTokenizer.Load(files.Tokenizer);
        using var model = ChatModel.Open(files.Model);
        var tokens = Prompt(tokenizer, LongPrompt);

        Assert.Equal(3, model.Generate(tokens, 3, Stop(tokenizer)).Count);
        using var stopped = new CancellationTokenSource();
        stopped.Cancel();
        Assert.Throws<OperationCanceledException>(() => model.Generate(tokens, 24, Stop(tokenizer), null, stopped.Token));
    }

    [Fact]
    public void TheCorrector_PutsRightWhatTheScanMisread_InEnglishAndInRussian_AndTheTabsAndLinesStay()
    {
        if (Files is not { } files)
            return;

        using var english = new LlmCorrector(files.Model, files.Tokenizer, ScanLanguage.English);
        const string table = "Monday\treading\nTuesday\tmaths\nWensday\tpainting\nThursdav\tmusic";
        Assert.Equal("Monday\treading\nTuesday\tmaths\nWednesday\tpainting\nThursday\tmusic", ManuscriptText.Apply(table, TextFixer.Fix(table, english)));

        using var russian = new LlmCorrector(files.Model, files.Tokenizer, ScanLanguage.Russian);
        const string story = "Сегодня мы ходили в лес и собрали много гриоов. Бабушка сказала что их нужно хорошо провариь.";
        Assert.Equal("Сегодня мы ходили в лес и собрали много грибов. Бабушка сказала, что их нужно хорошо проварить.", ManuscriptText.Apply(story, TextFixer.Fix(story, russian)));
    }

    [Fact]
    public void TheCorrector_LeavesATextThatIsRightAsItIs()
    {
        if (Files is not { } files)
            return;

        using var english = new LlmCorrector(files.Model, files.Tokenizer, ScanLanguage.English);
        const string text = "Today we went to the library and borrowed three books.";

        Assert.Empty(TextFixer.Fix(text, english));
    }
}
