using PhotoLiker.Search;

namespace PhotoLiker.Video.Tests;

/// <summary>The table of the videos with their speech (the window and the export read it) and hearing chosen videos again in another language.</summary>
[Collection(FfmpegCollection.Name)]
public sealed class SpeechTableTests : IDisposable
{
    private const string Model = "small";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "PLSpeechTable_" + Guid.NewGuid().ToString("N"));
    private string DbFile => Path.Combine(_root, "catalog", "videos.db");

    public SpeechTableTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>Writes good text in the language it is told, and in whatever it makes of the sound when left alone; remembers what it was asked.</summary>
    private sealed class LanguageRecognizer : ISpeechRecognizer
    {
        public List<string> Asked { get; } = [];

        public Task<SpeechTranscript> TranscribeAsync(string wavPath, string language, int threads, CancellationToken cancellationToken)
        {
            lock (Asked)
                Asked.Add(language);

            return Task.FromResult(language == "auto"
                ? new SpeechTranscript("nn", [new SpeechSegment(0, 2000, "ḥḥḥ ḤḤḤ")])
                : new SpeechTranscript(language, [new SpeechSegment(0, 2000, "Вот лошадка"), new SpeechSegment(4000, 6000, "Ещё раз")]));
        }

        public void Dispose() { }
    }

    private Task<SpeechIndexSummary> Hear(VideoCatalog catalog, ISpeechRecognizer recognizer, string language = "auto") =>
        SpeechIndexingJob.RunAsync(catalog, Path.Combine(_root, "models"), SpeechModels.Small, language, _root, null, [".mp4"], null, CancellationToken.None, null, _ => recognizer, null, null);

    private Task<SpeechIndexSummary> HearAgain(VideoCatalog catalog, ISpeechRecognizer recognizer, string language, params string[] files) =>
        SpeechIndexingJob.RedoAsync(catalog, Path.Combine(_root, "models"), SpeechModels.Small, language, files, null, CancellationToken.None, null, _ => recognizer);

    private string Make(string name, int seconds = 3, string sound = "loud") => RealFfmpeg.MakeVideo(_root, name, seconds, sound: sound)!;

    // ── the table ──────────────────────────────────────────────────────────

    [Fact]
    public async Task EveryVideoIsInTheTable_WithWhereItStandsAsFarAsItsSpeechGoes()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var loud = Make("a loud.mp4");
        Make("b silent.mp4", sound: "silent");
        Make("c mute.mp4", sound: "none");
        using var catalog = VideoCatalog.Open(_root, DbFile);
        await Hear(catalog, new LanguageRecognizer(), "ru");
        var added = Make("d new.mp4"); // not listened to yet
        await catalog.GetAsync(added);

        var table = catalog.SpeechTable(Model);

        Assert.Equal(["a loud.mp4", "b silent.mp4", "c mute.mp4", "d new.mp4"], table.Select(r => r.Video.RelativePath));
        Assert.Equal([SpeechStatus.Heard, SpeechStatus.NothingHeard, SpeechStatus.NoSound, SpeechStatus.NotRead], table.Select(r => r.Status));
        var first = table[0];
        Assert.Equal(loud, first.FullPath);
        Assert.Equal("ru", first.Language);
        Assert.Equal(["Вот лошадка", "Ещё раз"], first.Segments.Select(s => s.Text));
        Assert.Equal("Вот лошадка Ещё раз", first.Text);
        Assert.All(table.Skip(1), row => Assert.Empty(row.Segments));
        Assert.Equal(string.Empty, table[2].Language);   // nothing was heard in a video with no sound
        Assert.Equal(string.Empty, table[3].Language);
    }

    [Fact]
    public async Task ARunThatFailedOnAVideo_IsShownAsFailed_WithTheReason()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        Make("a.mp4");
        using var catalog = VideoCatalog.Open(_root, DbFile);
        var record = await catalog.GetAsync(Path.Combine(_root, "a.mp4"));
        catalog.SaveTranscript(record.Id, Model, string.Empty, [], "no sound could be taken from this video");

        var row = Assert.Single(catalog.SpeechTable(Model));

        Assert.Equal(SpeechStatus.Failed, row.Status);
        Assert.Equal("no sound could be taken from this video", row.Failure);
    }

    [Fact]
    public async Task AVideoThatCannotBeRead_IsShownAsUnreadable()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        File.WriteAllText(Path.Combine(_root, "broken.mp4"), "this is not a video");
        using var catalog = VideoCatalog.Open(_root, DbFile);
        await catalog.GetAsync(Path.Combine(_root, "broken.mp4"));

        var row = Assert.Single(catalog.SpeechTable(Model));

        Assert.Equal(SpeechStatus.Unreadable, row.Status);
        Assert.NotNull(row.Failure);
    }

    [Fact]
    public async Task TheMomentsOfAVideo_AreItsPlaces_OfTheModelThatFoundMost()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        Make("a.mp4");
        Make("b.mp4");
        using var catalog = VideoCatalog.Open(_root, DbFile);
        var a = await catalog.GetAsync(Path.Combine(_root, "a.mp4"));
        var b = await catalog.GetAsync(Path.Combine(_root, "b.mp4"));
        MomentData At(long ms) => new(ms, [1f, 0f], null);
        catalog.SaveAnalysis(a.Id, "clip-b32", [At(0), At(2000)]);
        catalog.SaveAnalysis(a.Id, "other", [At(0), At(1000), At(2500)]);   // this one found more
        catalog.SaveAnalysis(b.Id, "clip-b32", [], failure: "no pictures"); // nothing came out of it

        var table = catalog.SpeechTable(Model);

        Assert.Equal([0L, 1000L, 2500L], table[0].MomentTimesMs);
        Assert.Empty(table[1].MomentTimesMs);
    }

    [Fact]
    public void AnEmptyCatalog_HasNothingInTheTable()
    {
        using var catalog = VideoCatalog.Open(_root, DbFile);

        Assert.Empty(catalog.SpeechTable(Model));
    }

    // ── hearing chosen videos again ────────────────────────────────────────

    [Fact]
    public async Task OnlyTheAskedVideos_AreHeardAgain_InTheLanguageThatWasChosen_AndTheOthersAreLeftAlone()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        Make("a.mp4");
        var b = Make("b.mp4");
        Make("c.mp4");
        using var catalog = VideoCatalog.Open(_root, DbFile);
        var recognizer = new LanguageRecognizer();
        await Hear(catalog, recognizer, "auto");

        var summary = await HearAgain(catalog, recognizer, "uk", b);

        Assert.Equal(1, summary.Transcribed);
        Assert.Equal(1, summary.Reread);                          // it had been heard before
        Assert.Equal(["auto", "auto", "auto", "uk"], recognizer.Asked);
        var table = catalog.SpeechTable(Model);
        Assert.Equal(["nn", "uk", "nn"], table.Select(r => r.Language));
        Assert.Equal("ḥḥḥ ḤḤḤ", table[0].Text);                   // a and c: as they were
        Assert.Equal("Вот лошадка Ещё раз", table[1].Text);
        Assert.Equal("ḥḥḥ ḤḤḤ", table[2].Text);
    }

    [Fact]
    public async Task AVideoThatWasNeverHeard_CanBeHeardThroughTheSameWay_AndIsNotCountedAsHeardAgain()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var a = Make("a.mp4");
        using var catalog = VideoCatalog.Open(_root, DbFile);
        await catalog.GetAsync(a);
        var recognizer = new LanguageRecognizer();

        var summary = await HearAgain(catalog, recognizer, "ru", a);

        Assert.Equal(1, summary.Transcribed);
        Assert.Equal(0, summary.Reread);
        Assert.Equal(SpeechStatus.Heard, Assert.Single(catalog.SpeechTable(Model)).Status);
    }

    [Fact]
    public async Task EveryVideoThatHasSound_CanBeHeardAgainAtOnce_ThoseWithoutSoundAreLeftOut()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var a = Make("a.mp4");
        var b = Make("b.mp4");
        var mute = Make("c mute.mp4", sound: "none");
        using var catalog = VideoCatalog.Open(_root, DbFile);
        var recognizer = new LanguageRecognizer();
        await Hear(catalog, recognizer, "auto");
        await catalog.GetAsync(mute);

        var everything = catalog.SpeechTable(Model).Select(r => r.FullPath).ToArray();
        var summary = await HearAgain(catalog, recognizer, "en", everything);

        Assert.Equal(2, summary.Transcribed);
        Assert.Equal(2, summary.Reread);
        Assert.Equal(2, summary.Known);
        Assert.Equal(["en", "en"], catalog.SpeechTable(Model).Where(r => r.Status == SpeechStatus.Heard).Select(r => r.Language));
        Assert.Equal([a, b], catalog.SpeechTable(Model).Where(r => r.Status == SpeechStatus.Heard).Select(r => r.FullPath));
        Assert.Equal(SpeechStatus.NoSound, catalog.SpeechTable(Model)[2].Status);
    }

    [Fact]
    public async Task APathTheCatalogDoesNotKnow_IsIgnored_AndNothingIsHeardForNoVideo()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        Make("a.mp4");
        using var catalog = VideoCatalog.Open(_root, DbFile);
        var recognizer = new LanguageRecognizer();

        var summary = await HearAgain(catalog, recognizer, "ru", Path.Combine(_root, "elsewhere.mp4"));

        Assert.Equal(0, summary.Transcribed);
        Assert.Empty(recognizer.Asked);
    }

    [Fact]
    public async Task ALanguageChosenByHand_IsKept_SoUpdateIndexWithTheSameLanguageHearsNothingAgain()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var a = Make("a.mp4");
        using var catalog = VideoCatalog.Open(_root, DbFile);
        var recognizer = new LanguageRecognizer();
        await Hear(catalog, recognizer, "auto");
        await HearAgain(catalog, recognizer, "uk", a);

        var update = await Hear(catalog, recognizer, "uk");

        Assert.Equal(0, update.Transcribed);
        Assert.Equal(["auto", "uk"], recognizer.Asked);
    }

    [Fact]
    public async Task HearingAgainWithAuto_KeepsWhatTheModelHeard()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var a = Make("a.mp4");
        using var catalog = VideoCatalog.Open(_root, DbFile);
        var recognizer = new LanguageRecognizer();
        await Hear(catalog, recognizer, "ru");

        await HearAgain(catalog, recognizer, "auto", a);

        Assert.Equal("nn", Assert.Single(catalog.SpeechTable(Model)).Language);
    }

    [Fact]
    public async Task AStoppedRun_KeepsTheTextOfTheVideosItDidNotGetTo()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var a = Make("a.mp4");
        var b = Make("b.mp4");
        using var catalog = VideoCatalog.Open(_root, DbFile);
        await Hear(catalog, new LanguageRecognizer(), "auto");
        using var stop = new CancellationTokenSource();
        stop.Cancel();

        var summary = await SpeechIndexingJob.RedoAsync(
            catalog, Path.Combine(_root, "models"), SpeechModels.Small, "ru", [a, b], null, stop.Token, null, _ => new LanguageRecognizer()).ContinueWith(t => t.IsCanceled ? null : t.Result);

        Assert.Equal(["nn", "nn"], catalog.SpeechTable(Model).Select(r => r.Language));
        Assert.True(summary is null || summary.Cancelled);
    }

    // ── languages ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("ru", "Russian (ru)")]
    [InlineData(" UK ", "Ukrainian (uk)")]
    [InlineData("xx", "xx")]            // a code the model does not know is shown as it is
    [InlineData("", "")]
    [InlineData(null, "")]
    public void ALanguage_IsDescribedByItsNameAndCode(string? code, string described) => Assert.Equal(described, SpeechLanguages.Describe(code));

    [Fact]
    public void TheLikelyLanguagesComeFirst_AndEveryCodeIsThereOnce()
    {
        var all = SpeechLanguages.All;

        Assert.Equal(["ru", "uk", "en"], all.Take(3).Select(l => l.Code));
        Assert.Equal(all.Count, all.Select(l => l.Code).Distinct().Count());
        Assert.Equal(99, all.Count);
        Assert.DoesNotContain(all, l => l.Code == "auto");
        Assert.Null(SpeechLanguages.Find("auto"));
        Assert.Equal("Ukrainian", SpeechLanguages.Find("UK")!.Name);
    }
}
