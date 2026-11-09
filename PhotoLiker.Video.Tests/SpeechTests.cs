using PhotoLiker.Search;

namespace PhotoLiker.Video.Tests;

public class SpeechTextTests
{
    [Theory]
    [InlineData("Ну, давай плавай её.", new[] { "ну", "давай", "плавай", "ее" })]
    [InlineData("Мама, дивись: м'яч! М’ЯЧ", new[] { "мама", "дивись", "м'яч", "м'яч" })]
    [InlineData("  A baby -- in a bath?  ", new[] { "a", "baby", "in", "a", "bath" })]
    [InlineData("Ой-ой-ой.", new[] { "ой", "ой", "ой" })]
    [InlineData("12 слонів", new[] { "12", "слонів" })]
    [InlineData("'quoted' word", new[] { "quoted", "word" })]
    public void Words_AreLowerCase_WithoutPunctuation_AndYoIsYe(string text, string[] expected) =>
        Assert.Equal(expected, SpeechText.Words(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ... ?! ")]
    public void NoWords_InNothing(string? text) => Assert.Empty(SpeechText.Words(text));

    [Theory]
    [InlineData("лошадка", "лошадка", true)]
    [InlineData("лошадка", "лошадку", true)]   // another form of the word
    [InlineData("лошадка", "лошадке", true)]
    [InlineData("лошадка", "лошадь", true)]
    [InlineData("плавай", "плавает", true)]
    [InlineData("baby", "babies", true)]
    [InlineData("cat", "cats", true)]
    [InlineData("cat", "car", false)]
    [InlineData("дом", "дым", false)]
    [InlineData("кот", "которая", false)]       // a short word is not the start of a long one
    [InlineData("мама", "мамочка", true)]
    [InlineData("мама", "папа", false)]
    [InlineData("до", "дом", false)]            // too short to be anything but itself
    [InlineData("до", "до", true)]
    [InlineData("море", "поле", false)]
    public void ASearchedWord_MatchesTheSameWordInAnotherForm(string searched, string said, bool matches) =>
        Assert.Equal(matches, SpeechText.Matches(searched, said));

    [Theory]
    [InlineData("[музыка]", "")]
    [InlineData("(music)", "")]
    [InlineData("♪ Я тебя ищу ♪", "Я тебя ищу")]
    [InlineData("Субтитры сделал DimaTorzok", "")]
    [InlineData("Продолжение следует...", "")]
    [InlineData("Thanks for watching!", "")]
    [InlineData("  Ну, давай   плавай её.  ", "Ну, давай плавай её.")]
    [InlineData("", "")]
    public void WhatTheModelWritesForNoSpeech_IsTakenOut(string text, string expected) =>
        Assert.Equal(expected, SpeechText.Clean(text));
}

public class SpeechSearchTests
{
    private static VideoRecord Video(string name) => new(1, name, 100, 1, null, PhotoDate.NotRead, null);

    private static SpeechSet Set(string name, params (long Start, string Text)[] segments) =>
        new(Video(name), @"C:\v\" + name, [.. segments.Select(s => new SpeechSegment(s.Start, s.Start + 3000, s.Text))]);

    private static readonly SearchTuning Middle = SearchTuning.Default;

    [Fact]
    public void AWordThatIsSaid_FindsTheVideoAndTheMoment_InAnyForm()
    {
        var sets = new[]
        {
            Set("bath.avi", (0, "Ну, давай плавай её."), (10_000, "Смотри, лошадку нашли!"), (50_000, "Ой-ой-ой.")),
            Set("park.avi", (0, "Идём гулять."), (8_000, "Какая хорошая погода.")),
        };

        var found = SpeechSearch.Find(sets, "лошадка", Middle);

        var match = Assert.Single(found);
        Assert.Equal("bath.avi", match.Set.Video.RelativePath);
        Assert.Equal(10_000, match.BestMs);
        Assert.Equal([10_000L], match.Places);
        Assert.Equal("Смотри, лошадку нашли!", match.Snippet);
        Assert.Equal(1f, match.Fraction);
    }

    [Fact]
    public void SeveralPlaces_AreStretches_AndTheBestOfThemIsWhereItStarts()
    {
        var sets = new[] { Set("a.avi", (1_000, "лошадка раз"), (3_000, "лошадка два"), (60_000, "лошадка три")) };

        var match = Assert.Single(SpeechSearch.Find(sets, "лошадка", Middle));

        Assert.Equal([1_000L, 60_000L], match.Places); // the first two follow each other: one stretch
        Assert.Equal(1_000, match.BestMs);              // all equal: the first
    }

    [Fact]
    public void HowManyWordsAreNeeded_FollowsTheSlider()
    {
        // (words searched, at the wide end, in the middle, at the precise end)
        int[][] table = [[1, 1, 1, 1], [2, 1, 2, 2], [3, 2, 2, 3], [4, 2, 3, 4], [5, 3, 4, 5]];
        foreach (var row in table)
        {
            Assert.Equal(row[1], SpeechSearch.RequiredWords(row[0], SearchTuning.For(0)));
            Assert.Equal(row[2], SpeechSearch.RequiredWords(row[0], Middle));
            Assert.Equal(row[3], SpeechSearch.RequiredWords(row[0], SearchTuning.For(100)));
        }
    }

    [Fact]
    public void ManyWords_NeedMostOfThem_AtTheMiddle_AllOfThemAtThePreciseEnd_AndFewerAtTheWideEnd()
    {
        var sets = new[] { Set("a.avi", (0, "купаем малышку в ванне")) };

        // "купаем малышку ванне зеленой": 3 of the 4 words are there
        Assert.Single(SpeechSearch.Find(sets, "купаем малышку ванне зеленой", Middle));
        Assert.Empty(SpeechSearch.Find(sets, "купаем малышку ванне зеленой", SearchTuning.For(100)));
        Assert.Single(SpeechSearch.Find(sets, "купаем малышку ванне зеленой", SearchTuning.For(0)));
        Assert.Empty(SpeechSearch.Find(sets, "зеленой слонами пароходом", SearchTuning.For(0))); // none
    }

    [Fact]
    public void ASentenceThatTheModelCutInTwo_IsStillFound()
    {
        var sets = new[] { Set("a.avi", (0, "мы пошли купаться"), (3_000, "на речку и к озеру"), (40_000, "пора домой")) };

        // "купаться" is said in the one segment and "речку" in the next: only the two together have both
        var match = Assert.Single(SpeechSearch.Find(sets, "купаться речку", Middle));

        Assert.Equal(0, match.BestMs);
        Assert.Equal([0L], match.Places);
    }

    [Fact]
    public void TheShortWordsOfASearch_AreNotLookedFor_ExceptWhenTheyAreAllThereIs()
    {
        var sets = new[] { Set("a.avi", (0, "в доме тихо")), Set("b.avi", (0, "и на ты")) };

        Assert.Equal(["a.avi"], SpeechSearch.Find(sets, "в доме", Middle).Select(m => m.Set.Video.RelativePath));
        Assert.Equal(["b.avi"], SpeechSearch.Find(sets, "и на", Middle).Select(m => m.Set.Video.RelativePath)); // nothing longer to go by
    }

    [Fact]
    public void TheVideosThatMatchMost_ComeFirst()
    {
        var sets = new[] { Set("half.avi", (0, "купаем малышку")), Set("all.avi", (0, "купаем малышку в ванне")) };

        var found = SpeechSearch.Find(sets, "купаем малышку ванне", Middle);

        Assert.Equal(["all.avi", "half.avi"], found.Select(m => m.Set.Video.RelativePath));
        Assert.True(found[0].Fraction > found[1].Fraction);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("... !!")]
    public void NothingToLookFor_FindsNothing(string words) =>
        Assert.Empty(SpeechSearch.Find([Set("a.avi", (0, "что-то сказано"))], words, Middle));

    [Fact]
    public void TheRequiredFraction_FollowsTheSlider()
    {
        Assert.Equal(0.5f, SpeechSearch.RequiredFraction(SearchTuning.For(0)), 0.01f);
        Assert.Equal(1f, SpeechSearch.RequiredFraction(SearchTuning.For(100)), 0.01f);
        Assert.InRange(SpeechSearch.RequiredFraction(Middle), 0.6f, 0.8f);
    }

    [Fact]
    public void ASpokenMatch_ScoresAboveWhatAPictureThatLooksLikeTheWordsDoes()
    {
        Assert.True(SpeechSearch.ScoreBase > SearchTuning.Default.TextCutoff(0.4f) + 0.1f);
        Assert.True(SpeechSearch.ScoreBase + SpeechSearch.ScoreSpan <= 0.7f);
    }
}

public class SpeechCoresTests
{
    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 1, 2)]
    [InlineData(4, 1, 4)]
    [InlineData(6, 1, 6)]   // up to seven cores are one video with all of them
    [InlineData(7, 1, 7)]
    [InlineData(8, 2, 4)]
    [InlineData(11, 2, 5)]  // the remainder is not used
    [InlineData(12, 3, 4)]
    [InlineData(16, 4, 4)]
    [InlineData(32, 8, 4)]
    public void TheCores_AreSharedOutBetweenTheVideosThatAreHeardAtOnce(int cores, int workers, int threadsEach) =>
        Assert.Equal(new SpeechLayout(workers, threadsEach), SpeechCores.LayoutFor(cores));

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void NoCoresAtAll_CountAsOne(int cores) => Assert.Equal(new SpeechLayout(1, 1), SpeechCores.LayoutFor(cores));

    [Fact]
    public void ALayout_NeverUsesMoreCoresThanItWasGiven_AndMoreCoresNeverMeanLessWork()
    {
        var before = 0;
        for (var cores = 1; cores <= 64; cores++)
        {
            var layout = SpeechCores.LayoutFor(cores);

            Assert.InRange(layout.Cores, 1, cores);
            Assert.True(layout.Cores >= cores - layout.Workers + 1, $"{cores} cores: {layout.Cores} used"); // only the remainder of the division is left over
            Assert.True(layout.Cores >= before, $"{cores} cores use fewer than {cores - 1}");
            before = layout.Cores;
        }
    }
}

public class SpeechModelsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "PLSpeechModels_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    [Theory]
    [InlineData("tiny", "tiny")]
    [InlineData("BASE", "base")]
    [InlineData("small", "small")]
    [InlineData("nonsense", "small")] // a file from a newer version, a typo: the default
    [InlineData(null, "small")]
    public void AModel_IsFoundById_TheDefaultForAnUnknownOne(string? id, string expected) =>
        Assert.Equal(expected, SpeechModels.Find(id).Id);

    [Fact]
    public void TheModelsAreAskedForByTheirSize_AndTheOneThatWorksIsTheDefault()
    {
        Assert.Equal(["tiny", "base", "small"], SpeechModels.All.Select(m => m.Id));
        Assert.Equal(SpeechModels.Small, SpeechModels.Default);
        Assert.True(SpeechModels.Tiny.Megabytes < SpeechModels.Base.Megabytes && SpeechModels.Base.Megabytes < SpeechModels.Small.Megabytes);
    }

    [Fact]
    public void AModelFile_IsInTheWhisperFolderOfTheModels_AndIsNotThereUntilItIs()
    {
        var path = SpeechModels.PathOf(_folder, SpeechModels.Small);

        Assert.Equal(Path.Combine(_folder, "whisper", "ggml-small.bin"), path);
        Assert.False(SpeechModels.IsPresent(_folder, SpeechModels.Small));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[100]);
        Assert.False(SpeechModels.IsPresent(_folder, SpeechModels.Small)); // too small to be a model: a stopped download

        File.WriteAllBytes(path, new byte[2 << 20]);
        Assert.True(SpeechModels.IsPresent(_folder, SpeechModels.Small));
    }
}

public class SpeechAudioTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "PLSpeechAudio_" + Guid.NewGuid().ToString("N"));

    public SpeechAudioTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    // A 16 bit mono WAV of a square wave of this height (the header is 44 bytes, as ffmpeg writes a plain one)
    private string Wav(short height, int samples = 16000)
    {
        var path = Path.Combine(_folder, $"{height}.wav");
        using var stream = File.Create(path);
        stream.Write(new byte[44]);
        for (var i = 0; i < samples; i++)
            stream.Write(BitConverter.GetBytes(i % 2 == 0 ? height : (short)-height));
        return path;
    }

    [Theory]
    [InlineData(32767, 0)]       // as loud as can be
    [InlineData(16384, -6)]
    [InlineData(3277, -20)]
    [InlineData(328, -40)]
    public void TheLoudness_IsInDecibelsBelowTheLoudestSound(short height, double expectedDb) =>
        Assert.Equal(expectedDb, SpeechAudio.LoudnessDb(Wav(height)), 0.3);

    [Fact]
    public void Nothing_IsMinusAHundred_AndAnEmptyFileToo()
    {
        Assert.Equal(-100, SpeechAudio.LoudnessDb(Wav(0)));
        var empty = Path.Combine(_folder, "empty.wav");
        File.WriteAllBytes(empty, new byte[44]);
        Assert.Equal(-100, SpeechAudio.LoudnessDb(empty));
    }

    [Fact]
    public void TheSilenceLimit_IsAboveAFaintRoomButBelowSpeech() =>
        Assert.InRange(SpeechIndexingJob.SilenceDb, -60, -40);
}

[Collection(FfmpegCollection.Name)]
public sealed class SpeechJobTests : IDisposable
{
    private const string Model = "small";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "PLSpeechJob_" + Guid.NewGuid().ToString("N"));
    private string DbFile => Path.Combine(_root, "catalog", "videos.db");

    public SpeechJobTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>A recognizer that says what it is told and remembers which sounds it was given.</summary>
    private sealed class FakeRecognizer(Func<string, SpeechTranscript> hear) : ISpeechRecognizer
    {
        private readonly object _gate = new();

        public List<string> Heard { get; } = [];

        /// <summary>The threads each file was given.</summary>
        public List<int> Threads { get; } = [];

        public Task<SpeechTranscript> TranscribeAsync(string wavPath, string language, int threads, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                Heard.Add(language);
                Threads.Add(threads);
            }
            return Task.FromResult(hear(wavPath));
        }

        public void Dispose() { }
    }

    private static SpeechTranscript Says(params string[] lines) =>
        new("ru", [.. lines.Select((text, i) => new SpeechSegment(i * 4000L, i * 4000L + 3000, text))]);

    private Task<SpeechIndexSummary> Run(VideoCatalog catalog, FakeRecognizer recognizer, string language = "auto", Func<SpeechDownload, bool>? confirm = null, Func<int>? cores = null) =>
        SpeechIndexingJob.RunAsync(catalog, Path.Combine(_root, "models"), SpeechModels.Small, language, _root, null, [".mp4"], null, CancellationToken.None, confirm, _ => recognizer, cores);

    [Fact]
    public async Task TheSpeechOfEveryVideoWithSound_IsKept_AndNotReadAgain()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        RealFfmpeg.MakeVideo(_root, "a.mp4", seconds: 3);
        RealFfmpeg.MakeVideo(_root, "b.mp4", seconds: 3);
        using var catalog = VideoCatalog.Open(_root, DbFile);
        var recognizer = new FakeRecognizer(_ => Says("Смотри, лошадку нашли!", "Ну давай плавай"));

        var first = await Run(catalog, recognizer, "ru");
        var second = await Run(catalog, recognizer, "ru");

        Assert.Equal(2, first.Transcribed);
        Assert.Equal(4, first.Segments);
        Assert.Equal(2, first.Known);
        Assert.Equal(["ru", "ru"], recognizer.Heard);        // the language that was set is passed on
        Assert.Equal(0, second.Transcribed);                 // nothing to do the second time...
        Assert.Equal(2, recognizer.Heard.Count);             // ...and the model was not asked again
        Assert.Equal((2, 4), catalog.CountTranscribed(Model));
        var sets = catalog.SpeechSets(Model);
        Assert.Equal(2, sets.Count);
        Assert.Equal("Ну давай плавай", sets[0].Segments[1].Text);
        Assert.Equal(["Смотри, лошадку нашли!", "Ну давай плавай"], catalog.SpeechOf(sets[0].FullPath).Select(s => s.Text));
    }

    [Fact]
    public async Task ASilentVideo_IsNotGivenToTheModel_ButIsRemembered_AndOneWithNoSoundIsLeftOut()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        RealFfmpeg.MakeVideo(_root, "loud.mp4", seconds: 3, sound: "loud");
        RealFfmpeg.MakeVideo(_root, "silent.mp4", seconds: 3, sound: "silent");
        RealFfmpeg.MakeVideo(_root, "mute.mp4", seconds: 3, sound: "none");
        using var catalog = VideoCatalog.Open(_root, DbFile);
        var recognizer = new FakeRecognizer(_ => Says("что-то"));

        var summary = await Run(catalog, recognizer);
        var again = await Run(catalog, recognizer);

        Assert.Equal(1, summary.Transcribed);
        Assert.Equal(1, summary.Silent);
        Assert.Equal(2, summary.Known);                  // the one with no sound track is not a video with speech to read
        Assert.Single(recognizer.Heard);
        Assert.Equal(0, again.Silent);                   // remembered: not looked at again
        Assert.Equal((1, 1), catalog.CountTranscribed(Model));
    }

    [Fact]
    public async Task AVideoThatChanged_IsListenedToAgain()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var file = RealFfmpeg.MakeVideo(_root, "a.mp4", seconds: 3)!;
        using var catalog = VideoCatalog.Open(_root, DbFile);
        var recognizer = new FakeRecognizer(_ => Says("раз"));
        await Run(catalog, recognizer);

        File.Delete(file);
        RealFfmpeg.MakeVideo(_root, "a.mp4", seconds: 5);
        var summary = await Run(catalog, recognizer);

        Assert.Equal(1, summary.Transcribed);
        Assert.Equal(2, recognizer.Heard.Count);
    }

    [Fact]
    public async Task AModelThatIsNotThere_IsAskedFor_AndNothingIsReadWhenTheAnswerIsNo()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        RealFfmpeg.MakeVideo(_root, "a.mp4", seconds: 3);
        using var catalog = VideoCatalog.Open(_root, DbFile);
        SpeechDownload? asked = null;

        // no factory of a recognizer: the real one needs the file of the model
        var summary = await SpeechIndexingJob.RunAsync(
            catalog, Path.Combine(_root, "models"), SpeechModels.Small, "auto", _root, null, [".mp4"], null, CancellationToken.None,
            download => { asked = download; return false; });

        Assert.True(summary.DownloadDeclined);
        Assert.Equal(SpeechModels.Small, asked!.Model);
        Assert.Equal(0, catalog.CountTranscribed(Model).Videos);
    }

    /// <summary>A recognizer that takes its time, and notes how many files it is hearing at once and how many threads each was given.</summary>
    private sealed class SlowRecognizer(TimeSpan time, Action<int>? onCall = null, Func<int, Exception?>? failWith = null) : ISpeechRecognizer
    {
        private readonly object _gate = new();
        private int _now;
        private int _threadsNow;
        private int _calls;

        public int MostAtOnce { get; private set; }

        /// <summary>The most threads the files that were heard at the same time held together.</summary>
        public int MostThreadsAtOnce { get; private set; }

        public List<int> Threads { get; } = [];

        public async Task<SpeechTranscript> TranscribeAsync(string wavPath, string language, int threads, CancellationToken cancellationToken)
        {
            int call;
            lock (_gate)
            {
                call = _calls++;
                Threads.Add(threads);
                MostAtOnce = Math.Max(MostAtOnce, ++_now);
                _threadsNow += threads;
                MostThreadsAtOnce = Math.Max(MostThreadsAtOnce, _threadsNow);
            }

            try
            {
                onCall?.Invoke(call);
                if (failWith?.Invoke(call) is { } error)
                    throw error;

                await Task.Delay(time, cancellationToken);
            }
            finally
            {
                lock (_gate)
                {
                    _now--;
                    _threadsNow -= threads;
                }
            }
            return Says("раз");
        }

        public void Dispose() { }
    }

    private void MakeVideos(int count)
    {
        for (var i = 0; i < count; i++)
            RealFfmpeg.MakeVideo(_root, $"v{i:00}.mp4", seconds: 2);
    }

    [Fact]
    public async Task OneCore_HearsOneVideoAtATime_WithOneThread()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        MakeVideos(4);
        using var catalog = VideoCatalog.Open(_root, DbFile);
        var recognizer = new SlowRecognizer(TimeSpan.FromMilliseconds(150));

        var summary = await SpeechIndexingJob.RunAsync(catalog, Path.Combine(_root, "models"), SpeechModels.Small, "auto", _root, null, [".mp4"], null, CancellationToken.None, null, _ => recognizer, () => 1);

        Assert.Equal(4, summary.Transcribed);
        Assert.Equal(1, recognizer.MostAtOnce);
        Assert.All(recognizer.Threads, threads => Assert.Equal(SpeechCores.LayoutFor(1).ThreadsEach, threads));
    }

    [Fact]
    public async Task ManyCores_HearSeveralVideosAtOnce_EachWithItsShareOfTheThreads()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        const int cores = 12;
        var layout = SpeechCores.LayoutFor(cores);
        Assert.True(layout.Workers >= 2); // else there is nothing to see
        MakeVideos(layout.Workers + 2);
        using var catalog = VideoCatalog.Open(_root, DbFile);
        var recognizer = new SlowRecognizer(TimeSpan.FromMilliseconds(400));

        var summary = await SpeechIndexingJob.RunAsync(catalog, Path.Combine(_root, "models"), SpeechModels.Small, "auto", _root, null, [".mp4"], null, CancellationToken.None, null, _ => recognizer, () => cores);

        Assert.Equal(layout.Workers + 2, summary.Transcribed);
        Assert.Equal(layout.Workers, recognizer.MostAtOnce);
        Assert.All(recognizer.Threads, threads => Assert.Equal(layout.ThreadsEach, threads));
        Assert.Equal(layout.Workers + 2, catalog.CountTranscribed(Model).Videos);
    }

    [Fact]
    public async Task MoreCores_ChangedWhileItRuns_StartMoreVideosAtOnce()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var big = SpeechCores.LayoutFor(12);
        MakeVideos(big.Workers + 2);
        using var catalog = VideoCatalog.Open(_root, DbFile);
        var cores = 1;
        // the first video is under way with one core, and now the person raises the number
        var recognizer = new SlowRecognizer(TimeSpan.FromMilliseconds(1500), onCall: call => { if (call == 0) Volatile.Write(ref cores, 12); });

        var summary = await SpeechIndexingJob.RunAsync(catalog, Path.Combine(_root, "models"), SpeechModels.Small, "auto", _root, null, [".mp4"], null, CancellationToken.None, null, _ => recognizer, () => Volatile.Read(ref cores));

        Assert.Equal(big.Workers + 2, summary.Transcribed);
        Assert.Equal(SpeechCores.LayoutFor(1).ThreadsEach, recognizer.Threads[0]);   // it began with one core and was let finish with it
        Assert.True(recognizer.MostAtOnce >= 2, $"at most {recognizer.MostAtOnce} at once");
        Assert.Contains(big.ThreadsEach, recognizer.Threads.Skip(1));                 // the ones started after the change have their share of the bigger number
    }

    [Fact]
    public async Task FewerCores_ChangedWhileItRuns_LetTheBusyOnesFinish_AndStartTheNextOnesSmaller()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var big = SpeechCores.LayoutFor(12);
        Assert.True(big.Workers >= 2);
        MakeVideos(big.Workers + 3);
        using var catalog = VideoCatalog.Open(_root, DbFile);
        var cores = 12;
        // the first batch is started with all the cores; when the last of it has begun, the person lowers the number
        var recognizer = new SlowRecognizer(TimeSpan.FromMilliseconds(1200), onCall: call => { if (call == big.Workers - 1) Volatile.Write(ref cores, 1); });

        var summary = await SpeechIndexingJob.RunAsync(catalog, Path.Combine(_root, "models"), SpeechModels.Small, "auto", _root, null, [".mp4"], null, CancellationToken.None, null, _ => recognizer, () => Volatile.Read(ref cores));

        Assert.Equal(big.Workers + 3, summary.Transcribed);
        Assert.Equal(big.Workers, recognizer.MostAtOnce);                            // never more than the first batch: the rest came one at a time
        Assert.Equal(SpeechCores.LayoutFor(1).ThreadsEach, recognizer.Threads[^1]);   // the last was started after the change
        Assert.Equal(3, recognizer.Threads.Count(threads => threads == SpeechCores.LayoutFor(1).ThreadsEach));
    }

    [Fact]
    public async Task ABusyVideo_KeepsItsThreads_SoTheNextOneWaits_UntilItsShareFitsBesideThem()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        MakeVideos(3);
        using var catalog = VideoCatalog.Open(_root, DbFile);
        var cores = 7;
        // the first video holds all seven threads; with eight cores a four-thread video beside it would make eleven
        var recognizer = new SlowRecognizer(TimeSpan.FromMilliseconds(1500), onCall: call => { if (call == 0) Volatile.Write(ref cores, 8); });

        var summary = await SpeechIndexingJob.RunAsync(catalog, Path.Combine(_root, "models"), SpeechModels.Small, "auto", _root, null, [".mp4"], null, CancellationToken.None, null, _ => recognizer, () => Volatile.Read(ref cores));

        Assert.Equal(3, summary.Transcribed);
        Assert.Equal(SpeechCores.LayoutFor(7).ThreadsEach, recognizer.Threads[0]);
        Assert.True(recognizer.MostThreadsAtOnce <= 8, $"{recognizer.MostThreadsAtOnce} threads were held at once");
    }

    [Fact]
    public async Task TheProgress_CountsTheVideosThatAreHeard_AndEndsWithAllOfThem()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        MakeVideos(5);
        using var catalog = VideoCatalog.Open(_root, DbFile);
        var seen = new List<IndexProgress>();
        var recognizer = new SlowRecognizer(TimeSpan.FromMilliseconds(100));

        await SpeechIndexingJob.RunAsync(catalog, Path.Combine(_root, "models"), SpeechModels.Small, "auto", _root, null, [".mp4"], new Immediate(seen), CancellationToken.None, null, _ => recognizer, () => 12);

        var speech = seen.Where(p => p.Phase == IndexPhase.Speech).ToList();
        Assert.All(speech, p => Assert.Equal(5, p.Total));
        Assert.Equal(speech.Select(p => p.Done).Order(), speech.Select(p => p.Done)); // never goes back...
        Assert.Equal(5, speech[^1].Done);                                              // ...and ends with all
        Assert.Equal(string.Empty, speech[^1].Message);
    }

    private sealed class Immediate(List<IndexProgress> into) : IProgress<IndexProgress>
    {
        public void Report(IndexProgress value)
        {
            lock (into) into.Add(value);
        }
    }

    [Fact]
    public async Task ARecognizerThatFails_FailsTheJob_AndTheOtherVideosStop()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        MakeVideos(6);
        using var catalog = VideoCatalog.Open(_root, DbFile);
        var recognizer = new SlowRecognizer(TimeSpan.FromSeconds(30), failWith: call => call == 0 ? new InvalidOperationException("the model broke") : null);

        var started = System.Diagnostics.Stopwatch.StartNew();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SpeechIndexingJob.RunAsync(catalog, Path.Combine(_root, "models"), SpeechModels.Small, "auto", _root, null, [".mp4"], null, CancellationToken.None, null, _ => recognizer, () => 12));

        Assert.Equal("the model broke", error.Message);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(15), "the videos that were being heard did not stop"); // else they would have run their 30 s
    }

    [Fact]
    public async Task Stopping_KeepsWhatIsDone()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        RealFfmpeg.MakeVideo(_root, "a.mp4", seconds: 3);
        RealFfmpeg.MakeVideo(_root, "b.mp4", seconds: 3);
        RealFfmpeg.MakeVideo(_root, "c.mp4", seconds: 3);
        using var catalog = VideoCatalog.Open(_root, DbFile);
        using var stop = new CancellationTokenSource();
        var recognizer = new FakeRecognizer(_ =>
        {
            stop.Cancel(); // the first one is heard, and then the person presses Stop
            return Says("раз");
        });

        // one core: one video at a time, so the Stop pressed while the first is heard keeps the others from being started
        var summary = await SpeechIndexingJob.RunAsync(catalog, Path.Combine(_root, "models"), SpeechModels.Small, "auto", _root, null, [".mp4"], null, stop.Token, null, _ => recognizer, () => 1);

        Assert.True(summary.Cancelled);
        Assert.Equal(1, summary.Transcribed);
        Assert.Equal(1, catalog.CountTranscribed(Model).Videos);
    }
}

[Collection(FfmpegCollection.Name)]
public sealed class SpeechInTheSearchTests : IDisposable
{
    private static readonly ClipModelInfo Clip = ClipModels.Default;
    private static readonly float[] Words = [1f, 0f, 0f, 0f];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "PLSpeechSearch_" + Guid.NewGuid().ToString("N"));
    private string DbFile => Path.Combine(_root, "catalog", "videos.db");

    public SpeechInTheSearchTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static FolderIndex.SearchPlan Plan(string words, DateFilter? dates = null, params string[] names) => new(names, words, dates);

    private async Task<(VideoCatalog Catalog, VideoRecord Pictures, VideoRecord Both, VideoRecord Speech)> Catalog()
    {
        var catalog = VideoCatalog.Open(_root, DbFile);
        async Task<VideoRecord> Make(string name, string? recorded = null) => await catalog.GetAsync(RealFfmpeg.MakeVideo(_root, name, seconds: 2, recorded: recorded)!);
        var pictures = await Make("pictures.mp4");
        var both = await Make("both.mp4");
        var speech = await Make("speech.mp4", "2012-05-01T12:00:00Z");

        catalog.SaveAnalysis(pictures.Id, Clip.Id, [new MomentData(0, [0.35f, 0.9f, 0, 0], null)]);
        catalog.SaveAnalysis(both.Id, Clip.Id, [new MomentData(0, [0.31f, 0.9f, 0, 0], null), new MomentData(20_000, [0.05f, 0.99f, 0, 0], null)]);
        catalog.SaveAnalysis(speech.Id, Clip.Id, [new MomentData(0, [0.02f, 1f, 0, 0], null)]);
        catalog.SaveTranscript(both.Id, "small", "ru", [new SpeechSegment(8_000, 11_000, "Вот лошадка, смотри")]);
        catalog.SaveTranscript(speech.Id, "small", "ru", [new SpeechSegment(1_500, 4_000, "Какая красивая лошадка"), new SpeechSegment(30_000, 33_000, "лошадка убежала")]);
        return (catalog, pictures, both, speech);
    }

    [Fact]
    public async Task AWordThatIsSaid_FindsTheVideoWhateverThePicturesShow_AheadOfThePictures()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var (catalog, pictures, both, speech) = await Catalog();
        using var __ = catalog;

        var result = VideoSearch.Search(catalog, Clip, Plan("лошадка"), _ => Words, SearchTuning.Default, speechModelId: "small");

        Assert.False(result.Weak);
        Assert.Equal(["both.mp4", "speech.mp4", "pictures.mp4"], result.Hits.Select(h => Path.GetFileName(h.FullPath))); // said in the first two, so they come first; the third only looks right
        var said = result.Hits.Single(h => h.FullPath.EndsWith("speech.mp4"));
        Assert.Equal(1_500, said.TimeMs);
        Assert.Equal([1_500L, 30_000L], said.Moments);
        Assert.Equal([1_500L, 30_000L], said.Spoken); // both places were found in what is said
        Assert.Equal("Какая красивая лошадка", said.Said);
        Assert.Equal(SpeechSearch.ScoreBase + SpeechSearch.ScoreSpan, said.Score, 0.001f);
        Assert.Equal(3, result.SearchedVideos);
    }

    [Fact]
    public async Task AHitThatOnlyThePicturesFound_IsNotMarkedAsSpoken_AndAHitThatWasSaid_Is()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var (catalog, _, _, _) = await Catalog();
        using var __ = catalog;

        var result = VideoSearch.Search(catalog, Clip, Plan("лошадка"), _ => Words, SearchTuning.Default, speechModelId: "small");

        var pictures = result.Hits.Single(h => h.FullPath.EndsWith("pictures.mp4"));
        Assert.Null(pictures.Spoken);
        Assert.Null(pictures.Said);
        Assert.NotNull(pictures.Moments); // it is a video hit, found by what it shows
        Assert.All(result.Hits.Where(h => !h.FullPath.EndsWith("pictures.mp4")), h => Assert.NotEmpty(h.Spoken!));

        // without a speech model nothing is marked
        var seen = VideoSearch.Search(catalog, Clip, Plan("лошадка"), _ => Words, SearchTuning.Default);
        Assert.All(seen.Hits, h => Assert.Null(h.Spoken));
    }

    [Fact]
    public async Task AVideoFoundBothWays_IsOneHit_ThatStartsWhereItIsSaid_WithTheBestScore_AndEveryPlace()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var (catalog, _, _, _) = await Catalog();
        using var __ = catalog;

        // the words as a picture fit "both.mp4" at 0 s (0.31 passes) and "pictures.mp4" (0.35); the word is said in "both.mp4" at 8 s
        var result = VideoSearch.Search(catalog, Clip, Plan("лошадка"), _ => Words, SearchTuning.Default, speechModelId: "small");

        Assert.Equal(1, result.Hits.Count(h => h.FullPath.EndsWith("both.mp4")));
        var both = result.Hits.Single(h => h.FullPath.EndsWith("both.mp4"));
        Assert.Equal(8_000, both.TimeMs);
        Assert.Equal([0L, 8_000L], both.Moments); // the picture at 0 s and the speech at 8 s
        Assert.Equal([8_000L], both.Spoken); // only the second of them was found in what is said: the first one is the picture's
        Assert.Equal("Вот лошадка, смотри", both.Said);
        Assert.Equal(SpeechSearch.ScoreBase + SpeechSearch.ScoreSpan, both.Score, 0.001f);
        Assert.Equal("pictures.mp4", Path.GetFileName(result.Hits[^1].FullPath)); // what is only a picture that fits comes after what is said
    }

    [Fact]
    public async Task WithoutASpeechModel_OnlyThePicturesAreLookedAt()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var (catalog, _, _, _) = await Catalog();
        using var __ = catalog;

        var result = VideoSearch.Search(catalog, Clip, Plan("лошадка"), _ => Words, SearchTuning.Default);

        Assert.DoesNotContain(result.Hits, h => h.FullPath.EndsWith("speech.mp4")); // its pictures do not fit
        Assert.Equal(["both.mp4", "pictures.mp4"], result.Hits.Select(h => Path.GetFileName(h.FullPath)).Order());
    }

    [Fact]
    public async Task ADateLimitsWhatIsHeard_Too()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var (catalog, _, _, _) = await Catalog();
        using var __ = catalog;

        var result = VideoSearch.Search(catalog, Clip, Plan("лошадка", new DateFilter(2012, 2012, null, null)), _ => Words, SearchTuning.Default, speechModelId: "small");

        Assert.Equal(["speech.mp4"], result.Hits.Select(h => Path.GetFileName(h.FullPath)));
    }

    [Fact]
    public async Task AnotherSpeechModel_HasItsOwnTranscripts()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var (catalog, _, _, _) = await Catalog();
        using var __ = catalog;

        var result = VideoSearch.Search(catalog, Clip, Plan("лошадка"), _ => Words, SearchTuning.Default, speechModelId: "base");

        Assert.DoesNotContain(result.Hits, h => h.FullPath.EndsWith("speech.mp4")); // "base" has heard nothing yet
    }

    [Fact]
    public async Task ANamedPersonAndADateAlone_DoNotLookAtWhatIsSaid()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var (catalog, _, _, _) = await Catalog();
        using var __ = catalog;

        var person = VideoSearch.Search(catalog, Clip, Plan("лошадка", null, "Mom"), _ => Words, SearchTuning.Default, _ => [], speechModelId: "small");
        var date = VideoSearch.Search(catalog, Clip, Plan(string.Empty, new DateFilter(2012, 2012, null, null)), _ => throw new InvalidOperationException(), SearchTuning.Default, speechModelId: "small");

        Assert.Empty(person.Hits);
        Assert.Equal(["speech.mp4"], date.Hits.Select(h => Path.GetFileName(h.FullPath)));
    }

    // ── the catalog ────────────────────────────────────────────────────

    [Fact]
    public async Task TheTranscripts_AreKeptPerModel_ByVideo_AndGoWhenTheVideoChanges()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var (catalog, _, both, speech) = await Catalog();
        using var __ = catalog;

        Assert.Equal((2, 3), catalog.CountTranscribed("small"));
        Assert.Equal((0, 0), catalog.CountTranscribed("base"));
        Assert.Equal(new TranscriptState(VideoCatalog.TranscriptVersion, "ru", 2, null), catalog.TranscriptStates("small")[speech.Id]);

        catalog.SaveTranscript(both.Id, "small", "", [], "no sound could be taken");
        Assert.Equal((1, 2), catalog.CountTranscribed("small"));        // a failure has no speech to search
        Assert.Single(catalog.SpeechSets("small"));
        Assert.Equal("no sound could be taken", catalog.TranscriptStates("small")[both.Id].Failure);

        File.Delete(catalog.FullPathOf(speech));
        RealFfmpeg.MakeVideo(_root, "speech.mp4", seconds: 4);
        await catalog.GetAsync(Path.Combine(_root, "speech.mp4"));
        Assert.Empty(catalog.SpeechSets("small"));                       // the file is another: what was heard of the old one is gone
        Assert.Empty(catalog.SpeechOf(catalog.FullPathOf(speech)));
    }

    [Fact]
    public async Task AnotherConnection_SeesTheSpeechItStores()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        using var shown = VideoCatalog.Open(_root, DbFile);
        using var job = VideoCatalog.Open(_root, DbFile);
        var video = await job.GetAsync(RealFfmpeg.MakeVideo(_root, "a.mp4", seconds: 2)!);

        Assert.Empty(shown.SpeechSets("small"));
        job.SaveTranscript(video.Id, "small", "ru", [new SpeechSegment(0, 1000, "раз")]);

        Assert.Single(shown.SpeechSets("small"));
        Assert.Equal("раз", shown.SpeechOf(shown.FullPathOf(video)).Single().Text);
    }

    [Fact]
    public async Task TheSpeechOfAVideo_ForTheLineUnderThePlayer_IsOfTheModelThatHeardMost()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        using var catalog = VideoCatalog.Open(_root, DbFile);
        var video = await catalog.GetAsync(RealFfmpeg.MakeVideo(_root, "a.mp4", seconds: 2)!);
        catalog.SaveTranscript(video.Id, "base", "ru", [new SpeechSegment(0, 1000, "base heard")]);
        catalog.SaveTranscript(video.Id, "small", "ru", [new SpeechSegment(0, 1000, "small heard"), new SpeechSegment(2000, 3000, "and more")]);

        Assert.Equal(["small heard", "and more"], catalog.SpeechOf(catalog.FullPathOf(video)).Select(s => s.Text));
    }
}
