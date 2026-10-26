namespace PhotoLiker.Search.Tests;

public class ClipTokenizerTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly ClipTokenizer _tokenizer;

    public ClipTokenizerTests()
    {
        // A tiny vocabulary: "photo" is built from p h o t o by four merges, "a" is a single letter
        var vocab = _folder.File("vocab.json");
        File.WriteAllText(vocab, """
            { "a</w>": 320, "photo</w>": 1125, "p": 79, "h": 71, "o": 78, "t": 83, "o</w>": 345,
              "ph": 500, "pho": 501, "to</w>": 502 }
            """);
        var merges = _folder.File("merges.txt");
        File.WriteAllLines(merges, ["#version: 0.2", "p h", "ph o", "t o</w>", "pho to</w>"]);

        _tokenizer = new ClipTokenizer(vocab, merges);
    }

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void Words_AreMergedByTheMergeRules_AndWrappedInStartAndEnd()
    {
        Assert.Equal([ClipTokenizer.StartOfText, 320, 1125, ClipTokenizer.EndOfText], _tokenizer.Encode("a photo"));
    }

    [Fact]
    public void CaseAndExtraWhitespace_DoNotMatter()
    {
        Assert.Equal(_tokenizer.Encode("a photo"), _tokenizer.Encode("  A   PHOTO "));
    }

    [Fact]
    public void LongText_IsCutToTheModelLimit_KeepingTheEnd()
    {
        var ids = _tokenizer.Encode(string.Join(' ', Enumerable.Repeat("a", 200)));

        Assert.Equal(ClipTokenizer.MaxLength, ids.Length);
        Assert.Equal(ClipTokenizer.StartOfText, ids[0]);
        Assert.Equal(ClipTokenizer.EndOfText, ids[^1]);
    }

    [Fact]
    public void EmptyText_IsJustStartAndEnd()
    {
        Assert.Equal([ClipTokenizer.StartOfText, ClipTokenizer.EndOfText], _tokenizer.Encode("   "));
    }
}
