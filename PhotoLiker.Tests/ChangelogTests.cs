using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using PhotoLiker.Core;

namespace PhotoLiker.Tests;

public class ChangelogParseTests
{
    private const string Text = """
        # Changelog

        Prose for people, ignored.

        ## Unreleased

        - Added the thing that is not released.

        ## 2.1.0 - 2026-11-30

        - Added a feature.
        * Fixed a bug.
        Prose in a section is ignored as well.

        ## v2.0.1

        - Fixed a crash.

        ## [2.0.0] - 2026-10-01

        - Added the first version.
        -
        - Fixed something   with spaces around it.
        """;

    [Fact]
    public void TheSections_AreReadInTheOrderTheyAreWritten()
    {
        var sections = Changelog.Parse(Text);

        Assert.Equal([null, new Version(2, 1, 0, 0), new Version(2, 0, 1, 0), new Version(2, 0, 0, 0)], sections.Select(section => section.Version)); // read the way the update check reads versions: four numbers
    }

    [Fact]
    public void ALineThatStartsWithADashOrAStar_IsOneChange_AndTheRestIsIgnored()
    {
        var sections = Changelog.Parse(Text);

        Assert.Equal(["Added the thing that is not released."], sections[0].Changes);
        Assert.Equal(["Added a feature.", "Fixed a bug."], sections[1].Changes);
        Assert.Equal(["Fixed a crash."], sections[2].Changes);
    }

    [Fact]
    public void AChange_IsTrimmed_AndAnEmptyOneIsNone()
    {
        var last = Changelog.Parse(Text)[^1];

        Assert.Equal(["Added the first version.", "Fixed something   with spaces around it."], last.Changes);
    }

    [Theory]
    [InlineData("## 2.1.0", 2, 1, 0)]
    [InlineData("## 2.1.0 - 2026-11-30", 2, 1, 0)]
    [InlineData("## v2.1.0 (the big one)", 2, 1, 0)]
    [InlineData("## [2.1.0]", 2, 1, 0)]
    [InlineData("##    2.1.0   ", 2, 1, 0)]
    [InlineData("## 10.0.12", 10, 0, 12)]
    public void AHeading_NamesTheVersionBySomeOfItsUsualWritings(string heading, int major, int minor, int build)
    {
        var section = Assert.Single(Changelog.Parse(heading + "\n- A change.\n"));

        Assert.Equal(new Version(major, minor, build, 0), section.Version);
    }

    [Theory]
    [InlineData("## Unreleased")]
    [InlineData("## unreleased")]
    [InlineData("## [Unreleased]")]
    public void TheUnreleasedHeading_HasNoVersion(string heading)
    {
        var section = Assert.Single(Changelog.Parse(heading + "\n- A change.\n"));

        Assert.Null(section.Version);
        Assert.Equal(["A change."], section.Changes);
    }

    [Theory]
    [InlineData("## Notes\n- Not a change.")]
    [InlineData("## 2\n- Not a change.")]
    [InlineData("## \n- Not a change.")]
    [InlineData("- A change before any heading.")]
    [InlineData("# 2.1.0\n- A title is not a section.")]
    [InlineData("### 2.1.0\n- Neither is a deeper heading.")]
    public void WhatIsNotUnderAVersionOrUnreleased_IsNotAChange(string text) => Assert.Empty(Changelog.Parse(text));

    [Fact]
    public void ADeeperHeadingInASection_DoesNotEndIt()
    {
        var section = Assert.Single(Changelog.Parse("## 2.1.0\n### Added\n- One.\n### Fixed\n- Two.\n"));

        Assert.Equal(["One.", "Two."], section.Changes);
    }

    [Fact]
    public void AHeadingThatIsNothing_EndsTheSectionBeforeIt()
    {
        var sections = Changelog.Parse("## 2.1.0\n- One.\n## Notes\n- Not a change.\n## 2.0.0\n- Two.\n");

        Assert.Equal(["One."], sections[0].Changes);
        Assert.Equal(["Two."], sections[1].Changes);
        Assert.Equal(2, sections.Count);
    }

    [Fact]
    public void WindowsLineEndings_AndAByteOrderMark_AreNoTrouble()
    {
        var sections = Changelog.Parse((char)0xFEFF + "## 2.1.0\r\n- One.\r\n- Two.\r\n");

        Assert.Equal(["One.", "Two."], Assert.Single(sections).Changes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nothing to see")]
    public void ATextWithoutSections_HasNone(string? text) => Assert.Empty(Changelog.Parse(text));
}

public class ChangelogBetweenTests
{
    private static ChangelogSection Section(string? version, params string[] changes) =>
        new(version is null ? null : Version.Parse(version), changes);

    private static readonly ChangelogSection[] Sections =
    [
        Section(null, "Not released yet."),
        Section("2.2.0", "Two point two."),
        Section("2.1.0", "Two point one a.", "Two point one b."),
        Section("2.0.1", "Two point oh one."),
        Section("2.0.0", "Two point oh."),
    ];

    private static IEnumerable<string?> Versions(IEnumerable<ChangelogSection> sections) => sections.Select(section => section.Version?.ToString(3));

    [Fact]
    public void EveryVersionAfterTheInstalledOne_UpToTheLatest_NewestFirst()
    {
        var news = Changelog.Between(Sections, new Version(2, 0, 0), new Version(2, 2, 0));

        Assert.Equal(["2.2.0", "2.1.0", "2.0.1"], Versions(news));
    }

    [Fact]
    public void TheInstalledVersion_IsNotNewToIt()
    {
        var news = Changelog.Between(Sections, new Version(2, 2, 0), new Version(2, 2, 0));

        Assert.Empty(news);
    }

    [Fact]
    public void AVersionAfterTheLatest_AndTheUnreleasedChanges_AreLeftOut()
    {
        var news = Changelog.Between(Sections, new Version(2, 0, 0), new Version(2, 1, 0));

        Assert.Equal(["2.1.0", "2.0.1"], Versions(news));
    }

    [Fact]
    public void AnOrderOfTheFileThatIsNotNewestFirst_IsSortedNewestFirst()
    {
        var news = Changelog.Between([Section("2.0.1", "a."), Section("2.1.0", "b."), Section("2.10.0", "c.")], new Version(2, 0, 0), new Version(2, 10, 0));

        Assert.Equal(["2.10.0", "2.1.0", "2.0.1"], Versions(news)); // by number: 2.10 is after 2.9, not before
    }

    [Fact]
    public void ABuildThatIsNoRelease_GetsTheLatestVersionAlone()
    {
        var news = Changelog.Between(Sections, installed: null, new Version(2, 1, 0));

        Assert.Equal(["2.1.0"], Versions(news));
    }

    [Fact]
    public void AVersionWithoutChanges_IsLeftOut()
    {
        var news = Changelog.Between([Section("2.1.0"), Section("2.0.1", "Something.")], new Version(2, 0, 0), new Version(2, 1, 0));

        Assert.Equal(["2.0.1"], Versions(news));
    }

    [Fact]
    public void TheNumbersOfAVersion_AreComparedAsTheUpdateCheckDoes()
    {
        // 2.1 is 2.1.0: the installed 2.1 is not older than the section of 2.1.0
        var news = Changelog.Between([Section("2.1.0", "Something.")], new Version(2, 1), new Version(2, 1, 0));

        Assert.Empty(news);
    }
}

public class ChangelogFormatTests
{
    [Fact]
    public void WithoutAHeading_TheChangesAreOneList()
    {
        var text = Changelog.Format([new ChangelogSection(new Version(2, 1, 0), ["One.", "Two."])]);

        Assert.Equal("• One." + Environment.NewLine + "• Two.", text);
    }

    [Fact]
    public void WithAHeading_EachVersionStartsWithItsLine_AndABlankLineIsBetweenThem()
    {
        var text = Changelog.Format(
            [new ChangelogSection(new Version(2, 1, 0), ["One."]), new ChangelogSection(new Version(2, 0, 1), ["Two.", "Three."])],
            version => "Version " + version.ToString(3));

        var nl = Environment.NewLine;
        Assert.Equal($"Version 2.1.0{nl}• One.{nl}{nl}Version 2.0.1{nl}• Two.{nl}• Three.", text);
    }

    [Fact]
    public void NothingToSay_IsAnEmptyText() => Assert.Equal(string.Empty, Changelog.Format([]));
}

public class ChangelogReleaseTests
{
    private const string Repository = "https://github.com/YBalan/PhotoLiker/releases/download/v2.1.0/";

    private static string Json(params string[] assets) => $$"""{ "tag_name": "v2.1.0", "assets": [ {{string.Join(",", assets)}} ] }""";

    private static string Asset(string name, string? url = null) =>
        $$"""{ "name": "{{name}}", "size": 100, "browser_download_url": "{{url ?? Repository + name}}" }""";

    [Fact]
    public void TheChangelog_IsTheAssetThatIsNamedLikeOne_AmongTheOthers()
    {
        var release = UpdateChecker.Parse(Json(Asset("PhotoLiker-2.1.0-win-x64.zip"), Asset("CHANGELOG.md"), Asset("PhotoLiker-Setup-2.1.0.exe")));

        Assert.Equal(Repository + "CHANGELOG.md", release.ChangelogUrl);
        Assert.Equal("PhotoLiker-Setup-2.1.0.exe", release.InstallerName); // and the setup is still found after it
    }

    [Fact]
    public void AReleaseWithoutAChangelog_HasNone()
    {
        Assert.Null(UpdateChecker.Parse(Json(Asset("PhotoLiker-Setup-2.1.0.exe"))).ChangelogUrl);
        Assert.Null(UpdateChecker.Parse("""{ "tag_name": "v2.1.0" }""").ChangelogUrl);
    }

    [Fact]
    public void AChangelogThatIsNotAtTheReleasesOfTheProject_IsNotUsed()
    {
        Assert.Null(UpdateChecker.Parse(Json(Asset("CHANGELOG.md", "https://evil.example/CHANGELOG.md"))).ChangelogUrl);
        Assert.Null(UpdateChecker.Parse(Json(Asset("CHANGELOG.md", "https://github.com/someone/else/releases/download/v2.1.0/CHANGELOG.md"))).ChangelogUrl);
    }

    [Fact]
    public void AFileThatIsNamedLikeTheChangelogButIsNotIt_IsNotUsed()
    {
        Assert.Null(UpdateChecker.Parse(Json(Asset("CHANGELOG.md.sig"), Asset("OLD-CHANGELOG.md"))).ChangelogUrl);
    }

    [Fact]
    public void TheFirstSetup_IsStillTheOneThatIsOffered_WhateverIsAmongTheAssets()
    {
        var release = UpdateChecker.Parse(Json(Asset("PhotoLiker-Setup-2.1.0.exe"), Asset("CHANGELOG.md"), Asset("PhotoLiker-Setup-other.exe")));

        Assert.Equal("PhotoLiker-Setup-2.1.0.exe", release.InstallerName);
    }
}

public class ChangelogDownloadTests
{
    private sealed class FakeGitHub(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(answer(request));
        }
    }

    private static ReleaseInfo Release(string? changelogUrl) => new(
        new Version(2, 1, 0), "v2.1.0", "https://github.com/YBalan/PhotoLiker/releases/tag/v2.1.0", null, null, 0, null, changelogUrl);

    private const string Url = "https://github.com/YBalan/PhotoLiker/releases/download/v2.1.0/CHANGELOG.md";

    private static HttpResponseMessage Text(string text, Encoding? encoding = null) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent((encoding ?? new UTF8Encoding(false)).GetBytes(text)) };

    [Fact]
    public async Task TheChangelog_IsDownloadedFromTheReleaseAndRead()
    {
        var fake = new FakeGitHub(_ => Text("# Changelog\n\n## 2.1.0 - 2026-11-30\n\n- Added a feature.\n- Fixed a bug.\n\n## 2.0.0\n\n- First.\n"));
        var checker = new UpdateChecker(new HttpClient(fake));

        var sections = await checker.GetChangelogAsync(Release(Url), CancellationToken.None);

        var request = Assert.Single(fake.Requests);
        Assert.Equal(Url, request.RequestUri!.ToString());
        Assert.NotEmpty(request.Headers.UserAgent);
        Assert.Equal([new Version(2, 1, 0, 0), new Version(2, 0, 0, 0)], sections.Select(section => section.Version));
        Assert.Equal(["Added a feature.", "Fixed a bug."], sections[0].Changes);
    }

    [Fact]
    public async Task AReleaseWithoutAChangelog_AsksForNothing()
    {
        var fake = new FakeGitHub(_ => Text("## 2.1.0\n- A change.\n"));
        var checker = new UpdateChecker(new HttpClient(fake));

        Assert.Empty(await checker.GetChangelogAsync(Release(null), CancellationToken.None));
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task TextWithAByteOrderMark_AndNonAsciiLetters_IsReadWhole()
    {
        var fake = new FakeGitHub(_ => Text("## 2.1.0\n- Added é and я.\n", new UTF8Encoding(true)));
        var checker = new UpdateChecker(new HttpClient(fake));

        var section = Assert.Single(await checker.GetChangelogAsync(Release(Url), CancellationToken.None));

        Assert.Equal(["Added é and я."], section.Changes);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task GitHubRefusing_IsAnHttpError(HttpStatusCode status)
    {
        var checker = new UpdateChecker(new HttpClient(new FakeGitHub(_ => new HttpResponseMessage(status))));

        await Assert.ThrowsAsync<HttpRequestException>(() => checker.GetChangelogAsync(Release(Url), CancellationToken.None));
    }

    [Fact]
    public async Task AFileThatIsMuchTooBig_IsNotRead()
    {
        var content = new byte[UpdateChecker.MaxChangelogBytes + 1];
        Array.Fill(content, (byte)'a');
        var checker = new UpdateChecker(new HttpClient(new FakeGitHub(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) })));

        await Assert.ThrowsAsync<InvalidDataException>(() => checker.GetChangelogAsync(Release(Url), CancellationToken.None));
    }

    [Fact]
    public async Task AFileThatSaysItIsMuchTooBig_IsNotEvenRead()
    {
        // Its headers say 1 MB and a byte; what follows is small and would be a good changelog, so only the headers can have stopped it
        var content = new ByteArrayContent(new UTF8Encoding(false).GetBytes("## 2.1.0\n- A change.\n"));
        content.Headers.ContentLength = UpdateChecker.MaxChangelogBytes + 1L;
        var checker = new UpdateChecker(new HttpClient(new FakeGitHub(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = content })));

        await Assert.ThrowsAsync<InvalidDataException>(() => checker.GetChangelogAsync(Release(Url), CancellationToken.None));
    }

    [Fact]
    public async Task AFileThatSaysNotHowBigItIs_IsStoppedAtTheLimit()
    {
        // No length in its headers (a chunked answer): the stream is cut off when it passes the limit, not read to the end
        var endless = new EndlessContent();
        var checker = new UpdateChecker(new HttpClient(new FakeGitHub(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = endless })));

        await Assert.ThrowsAsync<InvalidDataException>(() => checker.GetChangelogAsync(Release(Url), CancellationToken.None));
        Assert.InRange(endless.Sent, UpdateChecker.MaxChangelogBytes, UpdateChecker.MaxChangelogBytes + (1 << 15));
    }

    [Fact]
    public async Task ACancelledRequest_IsCancelled()
    {
        var checker = new UpdateChecker(new HttpClient(new FakeGitHub(_ => Text("## 2.1.0\n- A change.\n"))));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => checker.GetChangelogAsync(Release(Url), cancelled.Token));
    }

    /// <summary>A body that never ends and has no length.</summary>
    private sealed class EndlessContent : HttpContent
    {
        public long Sent { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) => throw new NotSupportedException();

        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new Endless(this));

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        private sealed class Endless(EndlessContent owner) : Stream
        {
            public override int Read(byte[] buffer, int offset, int count)
            {
                Array.Fill(buffer, (byte)'a', offset, count);
                owner.Sent += count;
                return count;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}

/// <summary>
/// The CHANGELOG.md of the repository, which is what the next release is written from. A line that does not follow the format is not an error to the program (it is skipped), which is
/// exactly why it is an error here: a sentence that the release script or the About box cannot see would be lost without a word.
/// </summary>
public class ChangelogFileTests
{
    private const int MaxSentence = 160;

    private static readonly string[] Lines = File.ReadAllLines(Path.Combine(TestEnvironment.RepositoryRoot, Changelog.FileName));

    private static IEnumerable<string> Headings => Lines.Where(line => line.StartsWith("## ", StringComparison.Ordinal));

    private static IEnumerable<string> Sentences => Changelog.Parse(string.Join('\n', Lines)).SelectMany(section => section.Changes);

    [Fact]
    public void EveryHeading_IsAVersionWithItsDate_OrUnreleased()
    {
        foreach (var heading in Headings)
        {
            Assert.True(
                heading == "## " + Changelog.UnreleasedHeading || Regex.IsMatch(heading, @"^## \d+\.\d+\.\d+ - \d{4}-\d{2}-\d{2}$"),
                $"'{heading}' is neither '## Unreleased' nor '## <major>.<minor>.<patch> - <yyyy-MM-dd>'");
        }
    }

    [Fact]
    public void EveryHeading_IsSeenByTheProgram()
    {
        Assert.Equal(Headings.Count(), Changelog.Parse(string.Join('\n', Lines)).Count);
    }

    [Fact]
    public void TheVersions_GoFromTheNewestToTheOldest_EachOnce_WithUnreleasedFirst()
    {
        var versions = Changelog.Parse(string.Join('\n', Lines)).Select(section => section.Version).ToList();

        Assert.True(versions.Count(version => version is null) <= 1, "Unreleased is there once at most");
        Assert.True(versions.IndexOf(null) is -1 or 0, "Unreleased is the first section");

        var numbered = versions.OfType<Version>().ToList();
        Assert.Equal(numbered.OrderByDescending(version => version), numbered);
        Assert.Equal(numbered.Count, numbered.Distinct().Count());
    }

    [Fact]
    public void EverySection_HasAtLeastOneSentence()
    {
        foreach (var section in Changelog.Parse(string.Join('\n', Lines)))
            Assert.True(section.Changes.Count > 0, $"the section of {section.Version?.ToString(3) ?? "Unreleased"} has no sentence");
    }

    [Fact]
    public void EveryLineOfASection_IsASentence_OrBlank()
    {
        var inSection = false;
        foreach (var line in Lines)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
                inSection = true;
            else if (inSection)
                Assert.True(line.Trim().Length == 0 || line.StartsWith("- ", StringComparison.Ordinal), $"'{line}' is in a section but is not '- a sentence' (it would be lost)");
        }
    }

    [Fact]
    public void EverySentence_IsOnePlainSentence_OfAReasonableLength()
    {
        foreach (var sentence in Sentences)
        {
            Assert.EndsWith(".", sentence);
            Assert.True(sentence.Length <= MaxSentence, $"'{sentence}' is {sentence.Length} characters: one sentence of {MaxSentence} at most");
            Assert.True(char.IsUpper(sentence[0]), $"'{sentence}' does not start with a capital letter");
            Assert.DoesNotMatch(@"[`*\[\]<>]", sentence); // the About box shows the text as it is: no markdown, no links
            Assert.DoesNotMatch(@"\.\s+[A-Z]", sentence); // a second sentence in the line
        }
    }
}
