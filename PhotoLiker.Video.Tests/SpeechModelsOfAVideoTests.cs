namespace PhotoLiker.Video.Tests;

/// <summary>
/// A video heard by more than one speech model: the models keep their text side by side, the captions are of the model that is set (else the one that heard most), and the table of the
/// window knows which models heard each video.
/// </summary>
[Collection(FfmpegCollection.Name)]
public sealed class SpeechModelsOfAVideoTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PLSpeechModels_" + Guid.NewGuid().ToString("N"));
    private string DbFile => Path.Combine(_root, "catalog", "videos.db");

    public SpeechModelsOfAVideoTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static SpeechSegment[] Words(params string[] texts) => [.. texts.Select((text, i) => new SpeechSegment(i * 2000L, i * 2000L + 1500, text))];

    private async Task<(VideoCatalog Catalog, VideoRecord Video)> HeardByTwo()
    {
        var catalog = VideoCatalog.Open(_root, DbFile);
        var video = await catalog.GetAsync(RealFfmpeg.MakeVideo(_root, "a.mp4", seconds: 2)!);
        catalog.SaveTranscript(video.Id, "small", "ru", Words("small says"));
        catalog.SaveTranscript(video.Id, "base", "ru", Words("base says", "and a lot more", "than small"));
        return (catalog, video);
    }

    [Fact]
    public async Task TheModelThatIsSet_GivesTheWordsOfTheLine_EvenWhenAnotherHeardMore()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var (catalog, video) = await HeardByTwo();
        using var __ = catalog;
        var file = catalog.FullPathOf(video);

        Assert.Equal(["small says"], catalog.SpeechOf(file, "small").Select(s => s.Text));
        Assert.Equal(["base says", "and a lot more", "than small"], catalog.SpeechOf(file, "base").Select(s => s.Text));
    }

    [Fact]
    public async Task WhenTheModelThatIsSetHeardNothing_OrNoModelIsSaid_TheOneThatHeardMostGivesThem()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var (catalog, video) = await HeardByTwo();
        using var __ = catalog;
        var file = catalog.FullPathOf(video);

        Assert.Equal(3, catalog.SpeechOf(file, "tiny").Count); // tiny never heard it
        Assert.Equal(3, catalog.SpeechOf(file).Count);          // no model asked for
        catalog.SaveTranscript(video.Id, "tiny", "ru", []);     // heard it and found none: nothing to show either
        Assert.Equal(3, catalog.SpeechOf(file, "tiny").Count);
    }

    [Fact]
    public async Task HearingAVideoWithOneModelAgain_LeavesTheTextOfTheOtherModelAlone()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var (catalog, video) = await HeardByTwo();
        using var __ = catalog;

        catalog.SaveTranscript(video.Id, "base", "uk", Words("base again"));

        Assert.Equal(["base again"], catalog.SpeechOf(catalog.FullPathOf(video), "base").Select(s => s.Text));
        Assert.Equal(["small says"], catalog.SpeechOf(catalog.FullPathOf(video), "small").Select(s => s.Text));
    }

    [Fact]
    public async Task TheTableKnowsWhichModelsHeardEachVideo_WhateverModelItIsOf()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var (catalog, video) = await HeardByTwo();
        using var __ = catalog;
        var other = await catalog.GetAsync(RealFfmpeg.MakeVideo(_root, "b.mp4", seconds: 2)!);
        catalog.SaveTranscript(other.Id, "tiny", "en", []); // listened and found nothing
        catalog.SaveTranscript(other.Id, "small", string.Empty, [], "no sound could be taken from this video"); // a failure is not "heard by"
        await catalog.GetAsync(RealFfmpeg.MakeVideo(_root, "c.mp4", seconds: 2)!); // no model has heard it

        var small = catalog.SpeechTable("small");
        var tiny = catalog.SpeechTable("tiny");

        Assert.Equal(["a.mp4", "b.mp4", "c.mp4"], small.Select(r => r.Video.RelativePath));
        Assert.Equal([("base", 3), ("small", 1)], small[0].Models.Select(m => (m.ModelId, m.Segments)));
        Assert.Equal([("tiny", 0)], small[1].Models.Select(m => (m.ModelId, m.Segments)));
        Assert.Empty(small[2].Models);
        Assert.Equal(small.Select(r => r.Models.Count), tiny.Select(r => r.Models.Count)); // the same for whichever model the table is of

        // The row's own status and words are the ones of the model asked for
        Assert.Equal(["small says"], small[0].Segments.Select(s => s.Text));
        Assert.Equal(SpeechStatus.NotRead, tiny[0].Status);
        Assert.Equal(SpeechStatus.NothingHeard, tiny[1].Status);

        // And which one the captions use, for the model that is set
        Assert.Equal("small", small[0].CaptionModel("small"));
        Assert.Equal("base", small[0].CaptionModel("tiny"));
        Assert.Null(small[1].CaptionModel("small")); // it heard nothing
        Assert.Null(small[2].CaptionModel("small"));
    }
}
