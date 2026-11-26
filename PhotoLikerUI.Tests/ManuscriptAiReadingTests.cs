using System.Drawing.Imaging;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using PhotoLiker.Core;
using PhotoLiker.Drawing;
using PhotoLiker.Drawing.Ocr;
using PhotoLiker.Effects.Models;
using PhotoLiker.Manuscripts;
using PhotoLiker.Manuscripts.Cloud;
using PhotoLiker.Manuscripts.Pages;
using PhotoLiker.PhotoBook;
using TextLine = PhotoLiker.Drawing.Ocr.TextLine;

namespace PhotoLikerUI.Tests;

/// <summary>
/// Reading a page with an AI service ("AI service" in the Writing box): the person is asked before a photo leaves the computer, the photo is sent shrunk, the text that comes back becomes the text of
/// the page (with no boxes on the photo), and what goes wrong is told. Also the checkbox "Use context of previous pages", which gives the service the end of the text of the pages before (for the text
/// fix and for the reading), and what Undo does in the window. The Internet is a stand-in; no photo, text or key leaves the computer, and each test answers its own questions.
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class ManuscriptAiReadingTests : IDisposable
{
    private const string Key = "sk-test-0123456789abcdef";

    private readonly TempFolder _folder = new();

    public ManuscriptAiReadingTests() => new GlobalConfig { ColorizeBar = false }.Save();

    public void Dispose() => _folder.Dispose();

    private string Photo(string name, int width = 320, int height = 240)
    {
        var path = _folder.File(name);
        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.FromArgb(240, 236, 220));
            graphics.FillRectangle(Brushes.DarkRed, width * 3 / 4, height * 11 / 20, width / 6, height * 3 / 10); // a picture on the page, low at the right
        }

        bitmap.Save(path, ImageFormat.Jpeg);
        return path;
    }

    private AiServiceSettings Settings(AiVendor? picked = AiVendor.Claude)
    {
        var settings = AiServiceSettings.Load(_folder.File("ai.json"), new MaskingProtector());
        settings.Set(AiVendor.Claude, Key, "claude-x");
        settings.Set(AiVendor.OpenAI, Key, null);
        settings.Set(AiVendor.Gemini, Key, null);
        settings.Engine = picked;
        return settings;
    }

    private ManuscriptDialog Dialog(IReadOnlyList<string> photos, AiServiceSettings settings, StandInInternet? internet = null, bool pictures = false, Func<Task<bool>>? ensureFinder = null)
    {
        var dialog = new ManuscriptDialog(
            _folder.Path, photos.Take(1).ToList(), new EffectModels(Path.Combine(_folder.Path, "models")), dark: false, (_, _) => Task.FromResult(true), loadedPhotos: photos, aiServices: settings,
            ensureFinderAsync: ensureFinder);
        dialog.AskPagePaths = () => null;
        dialog.AskRemovePage = _ => true;
        dialog.AskReplaceText = () => true;
        dialog.AskBookPath = () => null;
        dialog.AskSendPhoto = _ => true;
        dialog.CloudHandler = internet ?? new StandInInternet(); // never the real Internet
        if (!pictures)
            dialog.ScanPicturesCheck.Checked = false;
        dialog.KindCombo.SelectedIndex = 2; // AI service
        return dialog;
    }

    private static string Json(string text) => JsonSerializer.Serialize(text);

    private static string ClaudeSays(string text) => "{\"content\":[{\"type\":\"text\",\"text\":" + Json(text) + "}],\"stop_reason\":\"end_turn\"}";

    private static string OpenAiSays(string text) => "{\"choices\":[{\"message\":{\"content\":" + Json(text) + "},\"finish_reason\":\"stop\"}]}";

    private static string GeminiSays(string text) => "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":" + Json(text) + "}]},\"finishReason\":\"STOP\"}]}";

    private static StandInInternet Reads(params string[] texts)
    {
        var internet = new StandInInternet();
        foreach (var text in texts)
            internet.Answer(HttpStatusCode.OK, ClaudeSays(text));
        return internet;
    }

    /// <summary>The photo that was sent in a request to Claude, as the picture it was.</summary>
    private static Bitmap SentPhoto(string body)
    {
        var data = (string)JsonNode.Parse(body)!["messages"]![0]!["content"]![0]!["source"]!["data"]!;
        return new Bitmap(new MemoryStream(Convert.FromBase64String(data)));
    }

    // ── The kind of writing ─────────────────────────────────────────────────

    [Fact]
    public void TheWritingBox_OffersTheAiServiceAsAThirdKind_ForEveryLanguage()
    {
        var a = Photo("a.jpg");

        Ui.Run(() => Dialog([a], Settings()), async form =>
        {
            var dialog = (ManuscriptDialog)form;

            Assert.Equal(3, dialog.KindCombo.Items.Count);
            Assert.Equal(ManuscriptStrings.KindAi, dialog.KindCombo.Items[2]!.ToString());
            Assert.Equal(ScanKind.AiService, dialog.Kind);
            Assert.Equal(4, dialog.LanguageCombo.Items.Count);
            await Task.CompletedTask;
        });
    }

    // ── Before the photo leaves ─────────────────────────────────────────────

    [Fact]
    public void WithoutAServicePicked_ScanPageSaysToPickOne_AndAsksNothing_AndSendsNothing()
    {
        var a = Photo("a.jpg");
        var internet = new StandInInternet();

        Ui.Run(() => Dialog([a], Settings(picked: null), internet), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            var asked = 0;
            dialog.AskSendPhoto = _ => { asked++; return true; };

            await dialog.ScanPageAsync();

            Assert.Equal(ManuscriptStrings.StatusAiScanNoService, dialog.StatusText);
            Assert.Equal(0, asked);
            Assert.Empty(internet.Requests);
            Assert.False(dialog.IsBusy);
        });
    }

    [Fact]
    public void ThePersonIsAskedBeforeThePhotoLeaves_WithTheNameOfTheService_AndANoSendsNothing()
    {
        var a = Photo("a.jpg");
        var internet = new StandInInternet();

        Ui.Run(() => Dialog([a], Settings(), internet), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            var asked = new List<string>();
            dialog.AskSendPhoto = name => { asked.Add(name); return false; };

            await dialog.ScanPageAsync();
            await dialog.ScanPageAsync();

            Assert.Equal(["Anthropic Claude", "Anthropic Claude"], asked); // a no is asked again the next time
            Assert.Equal(ManuscriptStrings.StatusAiScanDeclined, dialog.StatusText);
            Assert.Empty(internet.Requests);
            Assert.Null(dialog.Pages[0].Scanned);
            Assert.False(dialog.IsBusy);
        });
    }

    [Fact]
    public void AYes_HoldsForTheServiceUntilTheWindowIsClosed_SoAManyPageBookIsNotManyQuestions()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");

        Ui.Run(() => Dialog([a, b], Settings(), Reads("first", "second")), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            var asked = 0;
            dialog.AskSendPhoto = _ => { asked++; return true; };

            await dialog.ScanPageAsync();
            dialog.SelectPage(1);
            await dialog.ScanPageAsync();

            Assert.Equal(1, asked);
            Assert.Equal(["first", "second"], dialog.Pages.Select(page => page.Scanned));
        });
    }

    [Fact]
    public void AYesIsForTheServiceItWasGivenFor_AnotherServiceIsAskedAbout()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");
        var settings = Settings();
        var internet = new StandInInternet().Answer(HttpStatusCode.OK, ClaudeSays("by claude")).Answer(HttpStatusCode.OK, GeminiSays("by gemini"));

        Ui.Run(() => Dialog([a, b], settings, internet), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            var asked = new List<string>();
            dialog.AskSendPhoto = name => { asked.Add(name); return true; };

            await dialog.ScanPageAsync();
            settings.Engine = AiVendor.Gemini;
            dialog.FillAiCombo();
            dialog.SelectPage(1);
            await dialog.ScanPageAsync();

            Assert.Equal(["Anthropic Claude", "Google Gemini"], asked);
            Assert.Equal(["by claude", "by gemini"], dialog.Pages.Select(page => page.Scanned));
        });
    }

    // ── Reading a page ──────────────────────────────────────────────────────

    [Fact]
    public void ScanPage_SendsThePhoto_AndPutsTheTextIntoThePage_LineToAParagraph_WithNoBoxes()
    {
        var a = Photo("a.jpg");
        var internet = Reads("first line\nsecond line\n\nthird\tcolumn");

        Ui.Run(() => Dialog([a], Settings(), internet), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            dialog.SelectLanguage(ScanLanguage.Ukrainian);

            await dialog.ScanPageAsync();

            Assert.Equal("first line\nsecond line\n\nthird\tcolumn", dialog.Editor.PlainText);
            var page = dialog.Pages[0];
            Assert.Equal("first line\nsecond line\n\nthird\tcolumn", page.Scanned);
            Assert.Equal(nameof(ScanKind.AiService), page.Kind);
            Assert.Equal(nameof(ScanLanguage.Ukrainian), page.Language);
            Assert.Equal((320, 240), (page.Width, page.Height));
            Assert.NotNull(page.ScannedUtc);
            Assert.Null(page.Lines); // a service says what is written, not where
            Assert.Empty(dialog.View.Lines);
            Assert.Equal(PageState.Scanned, ManuscriptText.StateOf(page));
            Assert.Equal(string.Format(ManuscriptStrings.StatusAiReadFormat, 1, "Anthropic Claude", 3), dialog.StatusText);
            Assert.False(dialog.IsBusy);
            Assert.False(dialog.Editor.Box.ReadOnly);
        });

        // Kept with the folder
        var stored = ManuscriptFile.Load(_folder.Path)!.Pages![0];
        Assert.Equal(nameof(ScanKind.AiService), stored.Kind);
        Assert.Equal("first line\nsecond line\n\nthird\tcolumn", ManuscriptText.PlainOf(stored.Rtf));
    }

    [Theory]
    [InlineData(AiVendor.OpenAI, "api.openai.com")]
    [InlineData(AiVendor.Gemini, "generativelanguage.googleapis.com")]
    [InlineData(AiVendor.Claude, "api.anthropic.com")]
    public void ThePhotoGoesToTheServiceThatIsPicked_AndOnlyThere(AiVendor vendor, string host)
    {
        var a = Photo("a.jpg");
        var internet = new StandInInternet().Answer(HttpStatusCode.OK, vendor switch
        {
            AiVendor.OpenAI => OpenAiSays("read"),
            AiVendor.Gemini => GeminiSays("read"),
            _ => ClaudeSays("read"),
        });

        Ui.Run(() => Dialog([a], Settings(vendor), internet), async form =>
        {
            var dialog = (ManuscriptDialog)form;

            await dialog.ScanPageAsync();

            var request = Assert.Single(internet.Requests);
            Assert.Equal(host, request.Uri.Host);
            Assert.DoesNotContain(Key, request.Body);
            Assert.Equal("read", dialog.Editor.PlainText);
        });
    }

    [Fact]
    public void ThePhotoIsSentAsAJpeg_NoLargerThan2000PixelsOnASide_AndASmallOneIsNotMadeLarger()
    {
        var big = Photo("big.jpg", 3000, 2000);
        var small = Photo("small.jpg", 320, 240);
        var internet = Reads("big", "small");

        Ui.Run(() => Dialog([big, small], Settings(), internet), async form =>
        {
            var dialog = (ManuscriptDialog)form;

            await dialog.ScanPageAsync();
            dialog.SelectPage(1);
            await dialog.ScanPageAsync();

            using var first = SentPhoto(internet.Requests[0].Body);
            Assert.Equal(ImageFormat.Jpeg.Guid, first.RawFormat.Guid);
            Assert.Equal((2000, 1333), (first.Width, first.Height)); // the shape is kept
            using var second = SentPhoto(internet.Requests[1].Body);
            Assert.Equal((320, 240), (second.Width, second.Height));
            Assert.Equal((3000, 2000), (dialog.Pages[0].Width, dialog.Pages[0].Height)); // the page keeps the size of its photo, not of what was sent
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void ThePhotoIsSentUpright_ThePageThatIsOnItsSideIsReadTheRightWayUp()
    {
        // A photo that has the turn in its Exif only: what is sent is what the person sees
        var path = _folder.File("turned.jpg");
        using (var bitmap = new Bitmap(400, 200))
        {
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.Clear(Color.FromArgb(240, 236, 220));

            // Orientation 6: a quarter turn to the right is what shows it upright, 200 wide and 400 high (a PropertyItem has no constructor to call)
            var orientation = (PropertyItem)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(PropertyItem));
            orientation.Id = 0x0112;
            orientation.Type = 3;
            orientation.Len = 2;
            orientation.Value = BitConverter.GetBytes((ushort)6);
            bitmap.SetPropertyItem(orientation);
            bitmap.Save(path, ImageFormat.Jpeg);
        }

        var internet = Reads("read");

        Ui.Run(() => Dialog([path], Settings(), internet), async form =>
        {
            var dialog = (ManuscriptDialog)form;

            await dialog.ScanPageAsync();

            using var sent = SentPhoto(internet.Requests[0].Body);
            Assert.Equal((200, 400), (sent.Width, sent.Height));
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void TheLanguageOfThePage_IsToldToTheService()
    {
        var a = Photo("a.jpg");
        var internet = Reads("read");

        Ui.Run(() => Dialog([a], Settings(), internet), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            dialog.SelectLanguage(ScanLanguage.Russian);

            await dialog.ScanPageAsync();

            Assert.Equal(CloudReader.SystemText(ScanLanguage.Russian), (string?)JsonNode.Parse(internet.Requests[0].Body)!["system"]);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void ScanPage_OnAPageThatHasText_AsksBeforeReplacingIt_AsAnyScanDoes()
    {
        var a = Photo("a.jpg");
        var internet = Reads("new text");

        Ui.Run(() => Dialog([a], Settings(), internet), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            dialog.Editor.Box.Text = "what the person wrote";
            dialog.AskReplaceText = () => false;

            await dialog.ScanPageAsync();

            Assert.Equal("what the person wrote", dialog.Editor.PlainText);
            Assert.Empty(internet.Requests);

            dialog.AskReplaceText = () => true;
            await dialog.ScanPageAsync();
            Assert.Equal("new text", dialog.Editor.PlainText);
        });
    }

    [Fact]
    public void ScanAll_ReadsOnlyThePagesThatHaveNoText_OneRequestEach_AndSaysHowManyWereRead()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");
        var c = Photo("c.jpg");
        var internet = Reads("text of a", "text of c");

        Ui.Run(() => Dialog([a, b, c], Settings(), internet), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            dialog.Pages[1].Rtf = RtfWriter.Write(ManuscriptText.DocumentOf("already there"));
            var asked = 0;
            dialog.AskSendPhoto = _ => { asked++; return true; };

            await dialog.ScanAllAsync();

            Assert.Equal(2, internet.Requests.Count);
            Assert.Equal("text of a", dialog.Pages[0].Scanned);
            Assert.Null(dialog.Pages[1].Scanned); // left as it was
            Assert.Equal("text of c", dialog.Pages[2].Scanned);
            Assert.Equal(1, asked);
            Assert.Equal(string.Format(ManuscriptStrings.StatusAiScanAllDoneFormat, 2, "Anthropic Claude", 0), dialog.StatusText);
            Assert.False(dialog.IsBusy);
        });
    }

    // ── What goes wrong ─────────────────────────────────────────────────────

    [Fact]
    public void AServiceThatFails_EndsTheScan_WithThePageAndItsWords_AndThePagesAlreadyReadStayRead()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");
        var c = Photo("c.jpg");
        var internet = new StandInInternet().Answer(HttpStatusCode.OK, ClaudeSays("first page")).Answer(HttpStatusCode.Unauthorized, """{"error":{"message":"invalid x-api-key"}}""");

        Ui.Run(() => Dialog([a, b, c], Settings(), internet), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));

            await dialog.ScanAllAsync();

            Assert.Equal(2, internet.Requests.Count); // the third page was not tried
            Assert.Equal("first page", dialog.Pages[0].Scanned);
            Assert.Null(dialog.Pages[1].Scanned);
            Assert.StartsWith(string.Format(ManuscriptStrings.StatusAiScanFailedFormat, 2, string.Empty).TrimEnd(), dialog.StatusText);
            Assert.Contains("invalid x-api-key", dialog.StatusText);
            Assert.DoesNotContain(Key, dialog.StatusText);
            Assert.False(dialog.IsBusy);
            Assert.False(dialog.Editor.Box.ReadOnly);
            Assert.True(dialog.ScanPageButton.Enabled);
        });
    }

    [Fact]
    public void AServiceThatCannotSee_IsToldInTheStatusBar_AndThePageStaysAsItWas()
    {
        var a = Photo("a.jpg");
        var internet = new StandInInternet().Answer(HttpStatusCode.BadRequest, """{"error":{"message":"This model does not support image input."}}""");

        Ui.Run(() => Dialog([a], Settings(), internet), async form =>
        {
            var dialog = (ManuscriptDialog)form;

            await dialog.ScanPageAsync();

            Assert.Contains("does not support image input", dialog.StatusText);
            Assert.Null(dialog.Pages[0].Scanned);
            Assert.Null(dialog.Pages[0].Kind);
        });
    }

    [Fact]
    public void AReadingThatFindsNoText_SaysSo_AndKeepsTheTextThatWasThere()
    {
        var a = Photo("a.jpg");
        var internet = Reads("```\n```");

        Ui.Run(() => Dialog([a], Settings(), internet), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            dialog.Editor.Box.Text = "kept";

            await dialog.ScanPageAsync();

            Assert.Equal(string.Format(ManuscriptStrings.StatusAiReadNothingFormat, 1, "Anthropic Claude"), dialog.StatusText);
            Assert.Equal("kept", dialog.Editor.PlainText);
        });
    }

    [Fact]
    public void TheStopButton_StopsTheReading_AndNothingIsChanged()
    {
        var a = Photo("a.jpg");
        using var asked = new ManualResetEventSlim();
        var internet = new StandInInternet().Answer(async (_, cancel) =>
        {
            asked.Set();
            await Task.Delay(Timeout.Infinite, cancel);
            return new HttpResponseMessage();
        });

        Ui.Run(() => Dialog([a], Settings(), internet), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));

            var reading = dialog.ScanPageAsync();
            Assert.True(await Ui.Until(() => asked.IsSet));
            Assert.True(dialog.IsBusy);
            Assert.True(dialog.StopButton.Enabled);
            dialog.StopButton.PerformClick();
            await reading;

            Assert.Equal(ManuscriptStrings.StatusScanStopped, dialog.StatusText);
            Assert.Null(dialog.Pages[0].Scanned);
            Assert.False(dialog.IsBusy);
        });
    }

    [Fact]
    public void ThePhotoThatIsGone_IsSkipped_AndSaidSo_WithNothingSent()
    {
        var a = Photo("a.jpg");
        var internet = new StandInInternet();

        Ui.Run(() => Dialog([a], Settings(), internet), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            File.Delete(a);

            await dialog.ScanPageAsync();

            Assert.Empty(internet.Requests);
            Assert.False(dialog.IsBusy);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void TheKindsThatUseTheModelsOfThisComputer_AreNotChangedByAnAiServiceThatIsPicked()
    {
        var a = Photo("a.jpg");
        var internet = new StandInInternet();

        Ui.Run(() => Dialog([a], Settings(), internet), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            dialog.KindCombo.SelectedIndex = 0; // handwriting
            dialog.ScanOverride = (_, _, _, _, _) => new ScanResult([new TextLine("by the model", 0.9f, TextRegion.FromCentre(new PointF(160, 40), 200, 30, 0))]);

            await dialog.ScanPageAsync();

            Assert.Equal("by the model", dialog.Editor.PlainText);
            Assert.Empty(internet.Requests); // the service is for the kind that says so
            Assert.Equal(nameof(ScanKind.Handwriting), dialog.Pages[0].Kind);
        });
    }

    [Fact]
    public void APageThatHadBoxes_HasNoneAfterAServiceReadsIt_NeitherOnTheScreenNorInTheFile()
    {
        var a = Photo("a.jpg");
        var internet = Reads("read by the service");

        Ui.Run(() => Dialog([a], Settings(), internet), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));

            // First read by the models of this computer: the page has three boxes
            dialog.KindCombo.SelectedIndex = 0;
            dialog.ScanOverride = (_, _, _, _, _) => new ScanResult([new TextLine("one", 0.9f, TextRegion.FromCentre(new PointF(160, 40), 200, 30, 0)), new TextLine("two", 0.9f, TextRegion.FromCentre(new PointF(160, 80), 200, 30, 0))]);
            await dialog.ScanPageAsync();
            Assert.Equal(2, dialog.Pages[0].Lines!.Count);
            Assert.Equal(2, dialog.View.Lines.Count);

            // Then by a service: the old boxes are of the old reading, and are gone
            dialog.KindCombo.SelectedIndex = 2;
            await dialog.ScanPageAsync();

            Assert.Equal("read by the service", dialog.Editor.PlainText);
            Assert.Null(dialog.Pages[0].Lines);
            Assert.Empty(dialog.View.Lines);
        });

        Assert.Null(ManuscriptFile.Load(_folder.Path)!.Pages![0].Lines);
    }

    // ── The pictures of the page, the kind that is kept ─────────────────────

    [Fact]
    public void ThePicturesAreLookedForToo_AndPutBetweenTheLinesWhereTheyAreOnThePage_ByTheirHeight()
    {
        var a = Photo("a.jpg");
        var internet = Reads("one\ntwo\nthree");

        Ui.Run(() => Dialog([a], Settings(), internet, pictures: true), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            // The red block of the photo: at the right, from the height of 132 to 204 of 240
            dialog.PicturesOverride = (_, _) => [new PagePicture([new PointF(240, 132), new PointF(293, 132), new PointF(293, 204), new PointF(240, 204)])];

            await dialog.ScanPageAsync();

            var paragraphs = RtfParser.Parse(dialog.Pages[0].Rtf).Paragraphs;
            Assert.Equal(["one", "two", "[picture]", "three"], paragraphs.Select(paragraph => paragraph.Picture is not null ? "[picture]" : paragraph.PlainText));
            Assert.Single(dialog.Pages[0].Pictures!);
            Assert.EndsWith(" " + string.Format(ManuscriptStrings.StatusPicturesInTextFormat, 1), dialog.StatusText);
        });
    }

    [Fact]
    public void APageThatWasReadByAService_IsOpenedWithThatKindPicked_AfterAnotherPageWasLookedAt()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");

        Ui.Run(() => Dialog([a, b], Settings(), Reads("read by the service")), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            await dialog.ScanPageAsync();
            dialog.SelectPage(1);
            dialog.KindCombo.SelectedIndex = 0; // another kind, for the page that was not read yet

            dialog.SelectPage(0);

            Assert.Equal(ScanKind.AiService, dialog.Kind);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void TheKindThatWasPicked_IsKeptWithTheManuscript_AndPickedAgainWhenItIsOpened()
    {
        var a = Photo("a.jpg");

        Ui.Run(() => Dialog([a], Settings(), Reads("read")), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            await dialog.ScanPageAsync();
        });

        Ui.Run(() => new ManuscriptDialog(_folder.Path, [a], new EffectModels(Path.Combine(_folder.Path, "models")), dark: false, (_, _) => Task.FromResult(true), aiServices: Settings()), async form =>
        {
            var dialog = (ManuscriptDialog)form;

            Assert.Equal(ScanKind.AiService, dialog.Kind);
            await Task.CompletedTask;
        });
    }

    // ── Lines on the photo: the lines are marked on this computer, the service says what is in each box ──

    /// <summary>The boxes the finder of this computer puts on the 320 x 240 photo of a test: a line each, 240 wide and 24 high, their middles at the heights given.</summary>
    private static IReadOnlyList<TextRegion> Boxes(params int[] middles) =>
        [.. middles.Select(y => TextRegion.FromCentre(new PointF(160, y), 240, 24, 0))];

    private static void Finds(ManuscriptDialog dialog, IReadOnlyList<TextRegion> boxes) => dialog.FindLinesOverride = (_, _) => boxes;

    /// <summary>What a service is asked to answer: an entry with the number of the box and its text for each box that has text in it.</summary>
    private static string Marked(params (int Box, string Text)[] entries) =>
        "{\"lines\":[" + string.Join(",", entries.Select(entry => "{\"box\":" + entry.Box + ",\"text\":" + Json(entry.Text) + "}")) + "]}";

    /// <summary>Where a point of the 320 x 240 photo is on the view (the inverse of what the view draws).</summary>
    private static Point PointOf(ManuscriptView view, float photoX, float photoY)
    {
        var scale = view.ZoomOfPhoto;
        return new Point(
            (int)Math.Round((view.ClientSize.Width - 320 * scale) / 2f + photoX * scale),
            (int)Math.Round((view.ClientSize.Height - 240 * scale) / 2f + photoY * scale));
    }

    private static void Click(ManuscriptView view, Point point) =>
        typeof(ManuscriptView).GetMethod("ClickAt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(view, [point]);

    [Fact]
    public void ThePlacesCheckBox_IsOffUntilTurnedOn_AndOnlyForAServiceThatReadsAPhoto()
    {
        var a = Photo("a.jpg");

        Ui.Run(() => Dialog([a], Settings()), async form =>
        {
            var dialog = (ManuscriptDialog)form;

            Assert.Equal(ManuscriptStrings.AiPlaces, dialog.AiPlacesCheck.Text);
            Assert.Same(dialog.AiCombo.Parent, dialog.AiPlacesCheck.Parent);
            Assert.False(dialog.AiPlacesCheck.Checked);
            Assert.True(dialog.AiPlacesCheck.Enabled); // a service is picked and the Writing box says AI service

            dialog.KindCombo.SelectedIndex = 0; // the models on this computer give boxes of their own
            Assert.False(dialog.AiPlacesCheck.Enabled);

            dialog.KindCombo.SelectedIndex = 2;
            Assert.True(dialog.AiPlacesCheck.Enabled);

            dialog.AiCombo.SelectedIndex = 0; // the language model on this computer
            Assert.False(dialog.AiPlacesCheck.Enabled);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void WithThePlacesOn_TheLinesAreMarkedOnThePhoto_TheServiceSaysWhatIsInEachBox_AndThePageHasThoseBoxes_ThatAreKept()
    {
        var a = Photo("a.jpg");
        var internet = Reads(Marked((1, "first line"), (2, "second [?] line"), (3, "third line")));

        Ui.Run(() => Dialog([a], Settings(), internet), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            Finds(dialog, Boxes(40, 80, 120));
            dialog.AiPlacesCheck.Checked = true;

            await dialog.ScanPageAsync();

            // What was sent: the instructions for boxes, how many there are, and the photo with the boxes on it
            var request = JsonNode.Parse(Assert.Single(internet.Requests).Body)!;
            Assert.Equal(CloudReader.SystemText(ScanLanguage.Ukrainian, marked: true), (string?)request["system"]);
            Assert.Equal(CloudReader.UserText(null, 3), (string?)request["messages"]![0]!["content"]![1]!["text"]);
            using (var sent = SentPhoto(internet.Requests[0].Body))
            {
                var edge = sent.GetPixel(160, 28); // the top edge of the first box
                Assert.True(edge.R - edge.G > 60, $"a red box was expected there, the photo has {edge}");
            }

            // What the page is: the text of the boxes, laid out like the page, and the boxes
            Assert.Equal("first line\nsecond [?] line\nthird line", dialog.Editor.PlainText);
            Assert.Equal(string.Format(ManuscriptStrings.StatusAiReadPlacedFormat, 1, "Anthropic Claude", 3), dialog.StatusText);
            var page = dialog.Pages[0];
            Assert.Equal("AiService", page.Kind);
            var lines = page.Lines!.Select(line => line.ToLine()!).ToList();
            Assert.Equal(["first line", "second [?] line", "third line"], lines.Select(line => line.Text).ToArray());
            Assert.Equal([40d, 80d, 120d], lines.Select(line => Math.Round((double)line.Box.Centre.Y)).ToArray()); // where the finder put them, not where a service guessed
            Assert.True(lines[0].Confidence >= 0.8f, "green: the service marked nothing");
            Assert.True(lines[1].Confidence is >= 0.5f and < 0.8f, "orange: the service marked a word with [?]");
            Assert.Equal(3, dialog.View.Lines.Count);

            // The box and its line of the text go together, both ways
            Click(dialog.View, PointOf(dialog.View, 160, 80));
            Assert.Equal("second [?] line", dialog.Editor.Box.SelectedText);
            dialog.Editor.Box.Select(dialog.Editor.PlainText.IndexOf("third", StringComparison.Ordinal) + 1, 0);
            Assert.Equal(2, dialog.View.SelectedLine);
        });

        // The boxes are kept with the page
        var stored = ManuscriptFile.Load(_folder.Path)!;
        Assert.True(stored.AiPlaces);
        Assert.Equal(3, stored.Pages![0].Lines!.Count);
    }

    [Fact]
    public void ABoxTheServiceLeavesOut_ARuledLineOrAStain_IsNotALineOfThePage()
    {
        var a = Photo("a.jpg");
        var internet = Reads(Marked((1, "a"), (3, "c"), (4, "d")));

        Ui.Run(() => Dialog([a], Settings(), internet), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Finds(dialog, Boxes(40, 80, 120, 160));
            dialog.AiPlacesCheck.Checked = true;

            await dialog.ScanPageAsync();

            var lines = dialog.Pages[0].Lines!.Select(line => line.ToLine()!).ToList();
            Assert.Equal(["a", "c", "d"], lines.Select(line => line.Text).ToArray());
            Assert.Equal([40d, 120d, 160d], lines.Select(line => Math.Round((double)line.Box.Centre.Y)).ToArray()); // the boxes of the lines that were written, not the second one
            Assert.Equal("a\nc\nd", dialog.Editor.PlainText);
            Assert.Equal(string.Format(ManuscriptStrings.StatusAiReadPlacedFormat, 1, "Anthropic Claude", 3), dialog.StatusText);
        });
    }

    [Fact]
    public void TwoBoxesSideBySide_AreOneRowOfTheText_WithATab_AsInAScanOnThisComputer_AlsoWhenThePageIsOpenedAgain()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");
        var internet = Reads(Marked((1, "left"), (2, "right"), (3, "below")));
        IReadOnlyList<TextRegion> boxes =
        [
            TextRegion.FromCentre(new PointF(80, 40), 120, 24, 0),
            TextRegion.FromCentre(new PointF(240, 40), 120, 24, 0),
            TextRegion.FromCentre(new PointF(160, 130), 240, 24, 0),
        ];

        Ui.Run(() => Dialog([a, b], Settings(), internet), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            Finds(dialog, boxes);
            dialog.AiPlacesCheck.Checked = true;

            await dialog.ScanPageAsync();

            Assert.Equal("left\tright\nbelow", dialog.Editor.PlainText);
            dialog.Editor.Box.Select(dialog.Editor.PlainText.IndexOf("right", StringComparison.Ordinal) + 1, 0);
            Assert.Equal(0, dialog.View.SelectedLine); // the first box of the row

            dialog.SelectPage(1);
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            dialog.SelectPage(0);
            Assert.True(await Ui.Until(() => dialog.View.Lines.Count == 3), "the boxes are shown again");
            dialog.Editor.Box.Select(dialog.Editor.PlainText.IndexOf("below", StringComparison.Ordinal) + 1, 0);
            Assert.Equal(2, dialog.View.SelectedLine);
        });
    }

    [Fact]
    public void AnAnswerThatCannotBeUsedBoxByBox_GivesTheTextAndNoBoxes_AndTheStatusSaysSo()
    {
        var a = Photo("a.jpg");
        var internet = Reads("first line\nsecond line", Marked((9, "x"))); // plain text; then a number of a box that is not on the photo

        Ui.Run(() => Dialog([a], Settings(), internet), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Finds(dialog, Boxes(40, 80));
            dialog.AiPlacesCheck.Checked = true;

            await dialog.ScanPageAsync();

            Assert.Equal("first line\nsecond line", dialog.Editor.PlainText);
            Assert.Null(dialog.Pages[0].Lines);
            Assert.Empty(dialog.View.Lines);
            Assert.Equal(string.Format(ManuscriptStrings.StatusAiReadNoPlacesFormat, 1, "Anthropic Claude", 2), dialog.StatusText);

            await dialog.ScanPageAsync();

            Assert.Equal("x", dialog.Editor.PlainText);
            Assert.Null(dialog.Pages[0].Lines);
        });
    }

    [Fact]
    public void WhenNoLineIsFoundOnThePhoto_ItIsReadAsAWhole_WithoutBoxesOnIt()
    {
        var a = Photo("a.jpg");
        var internet = Reads("first line\nsecond line");

        Ui.Run(() => Dialog([a], Settings(), internet), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Finds(dialog, []);
            dialog.AiPlacesCheck.Checked = true;

            await dialog.ScanPageAsync();

            var request = JsonNode.Parse(Assert.Single(internet.Requests).Body)!;
            Assert.Equal(CloudReader.SystemText(ScanLanguage.Ukrainian), (string?)request["system"]); // the plain instructions: there is nothing to number
            Assert.Equal(CloudReader.UserText(null), (string?)request["messages"]![0]!["content"]![1]!["text"]);
            Assert.Equal("first line\nsecond line", dialog.Editor.PlainText);
            Assert.Null(dialog.Pages[0].Lines);
            Assert.Equal(string.Format(ManuscriptStrings.StatusAiReadNoPlacesFormat, 1, "Anthropic Claude", 2), dialog.StatusText);
        });
    }

    [Fact]
    public void ScanAllWithPlaces_SaysHowManyPagesWereReadWithoutBoxes()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");
        var internet = Reads(Marked((1, "one"), (2, "two")), "plain two");

        Ui.Run(() => Dialog([a, b], Settings(), internet), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Finds(dialog, Boxes(40, 80));
            dialog.AiPlacesCheck.Checked = true;

            await dialog.ScanAllAsync();

            Assert.NotNull(dialog.Pages[0].Lines);
            Assert.Null(dialog.Pages[1].Lines);
            Assert.Equal(string.Format(ManuscriptStrings.StatusAiScanAllDoneFormat, 2, "Anthropic Claude", 0) + " " + string.Format(ManuscriptStrings.StatusAiNoPlacesFormat, 1), dialog.StatusText);
        });
    }

    [Fact]
    public void WhenTheModelThatFindsTheLinesIsNotDownloaded_ThePagesAreReadWithoutBoxes_AndItIsAskedOncePerScan()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");
        var internet = Reads("plain a", "plain b");
        var asked = 0;

        Ui.Run(() => Dialog([a, b], Settings(), internet, ensureFinder: () => { asked++; return Task.FromResult(false); }), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            dialog.AiPlacesCheck.Checked = true;

            await dialog.ScanAllAsync();

            Assert.Equal(1, asked);
            foreach (var request in internet.Requests)
                Assert.Equal(CloudReader.SystemText(ScanLanguage.Ukrainian), (string?)JsonNode.Parse(request.Body)!["system"]);
            Assert.Null(dialog.Pages[0].Lines);
            Assert.EndsWith(ManuscriptStrings.StatusAiPlacesNoFinder, dialog.StatusText);
            Assert.True(dialog.AiPlacesCheck.Checked); // the person's choice stays: the next scan asks again
        });
    }

    [Fact]
    public void WithThePlacesOff_NothingIsMarked_TheRequestIsThePlainOne_AndThePageHasNoBoxes()
    {
        var a = Photo("a.jpg");
        var internet = Reads("first line\nsecond line");
        var finds = 0;

        Ui.Run(() => Dialog([a], Settings(), internet), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            dialog.FindLinesOverride = (_, _) => { finds++; return Boxes(40, 80); };

            await dialog.ScanPageAsync();

            Assert.Equal(0, finds);
            Assert.Equal(CloudReader.SystemText(ScanLanguage.Ukrainian), (string?)JsonNode.Parse(internet.Requests[0].Body)!["system"]);
            Assert.Null(dialog.Pages[0].Lines);
            Assert.Equal(string.Format(ManuscriptStrings.StatusAiReadFormat, 1, "Anthropic Claude", 2), dialog.StatusText);
        });
    }

    [Fact]
    public void ThePhotoThatIsSent_HasARedBoxAndItsNumberOnEachLine_AndWithoutBoxesItIsUntouched()
    {
        var photo = new Bitmap32(400, 300, Enumerable.Repeat((byte)255, 400 * 300 * 4).ToArray());
        TextRegion[] boxes = [TextRegion.FromCentre(new PointF(200, 100), 300, 30, 0)]; // 50..350 across, 85..115 down

        using var marked = new Bitmap(new MemoryStream(ManuscriptDialog.JpegForAi(photo, boxes)));
        using var plain = new Bitmap(new MemoryStream(ManuscriptDialog.JpegForAi(photo)));

        static bool Red(Color colour) => colour.R - colour.G > 100 && colour.R - colour.B > 100;
        Assert.True(Red(marked.GetPixel(200, 85)), "the top edge of the box");
        Assert.True(Red(marked.GetPixel(200, 115)), "the bottom edge of the box");
        Assert.False(Red(marked.GetPixel(200, 100)), "inside the box the page is as it was");
        var tag = Enumerable.Range(30, 22).SelectMany(x => Enumerable.Range(85, 20).Select(y => marked.GetPixel(x, y))).Count(Red);
        Assert.True(tag > 40, $"the number of the box is on a red tag at its left ({tag} red points)");
        Assert.All(Enumerable.Range(0, 400).Select(x => plain.GetPixel(x, 85)), colour => Assert.False(Red(colour)));
    }

    // ── Use context of previous pages ───────────────────────────────────────

    [Fact]
    public void TheContextCheckBox_IsOffUntilTurnedOn_OnlyForAServiceThatIsPicked_AndSaysWhat()
    {
        var a = Photo("a.jpg");

        Ui.Run(() => Dialog([a], Settings(picked: null)), async form =>
        {
            var dialog = (ManuscriptDialog)form;

            Assert.Equal(ManuscriptStrings.UseContext, dialog.UseContextCheck.Text);
            Assert.False(dialog.UseContextCheck.Checked);
            Assert.False(dialog.UseContextCheck.Enabled); // the model on this computer is not given the pages before

            dialog.AiCombo.SelectedIndex = 1; // a service
            Assert.True(dialog.UseContextCheck.Enabled);
            Assert.False(dialog.UseContextCheck.Checked);

            dialog.AiCombo.SelectedIndex = 0;
            Assert.False(dialog.UseContextCheck.Enabled);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void TheContextCheckBox_IsInTheRowOfTheAiBox_AndIsOffWhileTheWindowWorks()
    {
        var a = Photo("a.jpg");

        Ui.Run(() => Dialog([a], Settings()), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.Same(dialog.AiCombo.Parent, dialog.UseContextCheck.Parent);
            Assert.True(dialog.UseContextCheck.Enabled);
            (bool Context, bool Source) seen = default;
            var internet = new StandInInternet().Answer((_, _) =>
            {
                seen = dialog.Invoke(() => (dialog.UseContextCheck.Enabled, dialog.SourceCombo.Enabled));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ClaudeSays("x"), System.Text.Encoding.UTF8, "application/json") });
            });
            dialog.CloudHandler = internet;

            await dialog.ScanPageAsync();

            Assert.Equal((false, false), seen);
            Assert.True(dialog.UseContextCheck.Enabled);
        });
    }

    [Fact]
    public void TheContextCheckBox_IsKeptWithTheManuscript()
    {
        var a = Photo("a.jpg");

        Ui.Run(() => Dialog([a], Settings()), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            dialog.UseContextCheck.Checked = true;
            await Task.CompletedTask;
        });

        Assert.True(ManuscriptFile.Load(_folder.Path)!.UseContext);

        Ui.Run(() => new ManuscriptDialog(_folder.Path, [a], new EffectModels(Path.Combine(_folder.Path, "models")), dark: false, (_, _) => Task.FromResult(true), aiServices: Settings()), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(dialog.UseContextCheck.Checked);
            dialog.UseContextCheck.Checked = false;
            await Task.CompletedTask;
        });

        Assert.NotEqual(true, ManuscriptFile.Load(_folder.Path)!.UseContext);
    }

    [Fact]
    public void WithTheContextOn_TheTextFix_GivesTheServiceTheEndOfThePagesBefore_ForEveryPiece()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");
        var internet = new StandInInternet().Answer(HttpStatusCode.OK, ClaudeSays("Wednesday painting"));

        Ui.Run(() => Dialog([a, b], Settings(), internet), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            dialog.Pages[0].Rtf = RtfWriter.Write(ManuscriptText.DocumentOf("Granny Olha went to Lviv.\nShe took Wensday's bread."));
            dialog.SelectPage(1);
            dialog.Editor.Box.Text = "Wensday painting";
            dialog.UseContextCheck.Checked = true;

            await dialog.FixTextAsync();

            var system = (string?)JsonNode.Parse(internet.Requests[0].Body)!["system"];
            Assert.EndsWith("Granny Olha went to Lviv.\nShe took Wensday's bread.", system);
            Assert.Contains("For reference only", system);
            Assert.Equal("Wednesday painting", dialog.Editor.PlainText);
        });
    }

    [Fact]
    public void WithTheContextOff_NothingOfThePagesBeforeIsSent_ByTheTextFix()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");
        var internet = new StandInInternet().Answer(HttpStatusCode.OK, ClaudeSays("Wednesday painting"));

        Ui.Run(() => Dialog([a, b], Settings(), internet), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            dialog.Pages[0].Rtf = RtfWriter.Write(ManuscriptText.DocumentOf("Granny Olha went to Lviv."));
            dialog.SelectPage(1);
            dialog.Editor.Box.Text = "Wensday painting";

            await dialog.FixTextAsync();

            Assert.DoesNotContain("Granny", internet.Requests[0].Body);
            Assert.Equal(LlmCorrectorSystem(), (string?)JsonNode.Parse(internet.Requests[0].Body)!["system"]);
        });
    }

    private static string LlmCorrectorSystem() => PhotoLiker.Manuscripts.Fixing.LlmCorrector.SystemText(ScanLanguage.Ukrainian);

    [Fact]
    public void TheContext_IsThePagesBeforeThePageThatIsFixed_NotThePageItselfNorTheOnesAfterIt()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");
        var c = Photo("c.jpg");
        var internet = new StandInInternet().Answer(HttpStatusCode.OK, ClaudeSays("the middle page"));

        Ui.Run(() => Dialog([a, b, c], Settings(), internet), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            dialog.Pages[0].Rtf = RtfWriter.Write(ManuscriptText.DocumentOf("text of the first"));
            dialog.Pages[2].Rtf = RtfWriter.Write(ManuscriptText.DocumentOf("text of the third"));
            dialog.SelectPage(1);
            dialog.Editor.Box.Text = "the middle page";
            dialog.UseContextCheck.Checked = true;

            await dialog.FixTextAsync();

            var body = internet.Requests[0].Body;
            Assert.Contains("text of the first", body);
            Assert.DoesNotContain("text of the third", body);
        });
    }

    [Fact]
    public void WithTheContextOn_TheReadingOfAPage_GivesTheServiceThePagesBeforeAlongsideThePhoto()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");
        var internet = Reads("a page that goes on");

        Ui.Run(() => Dialog([a, b], Settings(), internet), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            dialog.Pages[0].Rtf = RtfWriter.Write(ManuscriptText.DocumentOf("Granny Olha went to Lviv."));
            dialog.SelectPage(1);
            dialog.UseContextCheck.Checked = true;

            await dialog.ScanPageAsync();

            var user = (string?)JsonNode.Parse(internet.Requests[0].Body)!["messages"]![0]!["content"]![1]!["text"];
            Assert.Equal(CloudReader.UserText("Granny Olha went to Lviv."), user);
        });
    }

    [Fact]
    public void WithTheContextOff_TheReadingGetsThePhotoOnly()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");
        var internet = Reads("text");

        Ui.Run(() => Dialog([a, b], Settings(), internet), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            dialog.Pages[0].Rtf = RtfWriter.Write(ManuscriptText.DocumentOf("Granny Olha went to Lviv."));
            dialog.SelectPage(1);

            await dialog.ScanPageAsync();

            Assert.DoesNotContain("Granny", internet.Requests[0].Body);
            Assert.Equal(CloudReader.UserText(null), (string?)JsonNode.Parse(internet.Requests[0].Body)!["messages"]![0]!["content"]![1]!["text"]);
        });
    }

    [Fact]
    public void WithTheContextOn_EachPageOfAScanAllIsGivenThePagesBeforeIt_IncludingThoseThatWereJustRead()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");
        var internet = Reads("text read from a", "text read from b");

        Ui.Run(() => Dialog([a, b], Settings(), internet), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            dialog.UseContextCheck.Checked = true;

            await dialog.ScanAllAsync();

            Assert.DoesNotContain("text read from a", internet.Requests[0].Body); // the first page has none before it
            Assert.Contains("text read from a", internet.Requests[1].Body); // the second is read with the first as its context
        });
    }

    // ── Undo and Redo in the window ─────────────────────────────────────────

    [Fact]
    public void AnotherPageThatIsOpened_CannotBeUndoneIntoThePageBefore()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");

        Ui.Run(() => Dialog([a, b], Settings()), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            dialog.Editor.Box.Text = "first";
            dialog.Editor.Box.Select(5, 0);
            dialog.Editor.Box.SelectedText = " page";
            Assert.True(dialog.Editor.UndoButton.Enabled);

            dialog.SelectPage(1);

            Assert.False(dialog.Editor.UndoButton.Enabled);
            Assert.False(dialog.Editor.RedoButton.Enabled);
            Assert.Equal(string.Empty, dialog.Editor.PlainText);
        });
    }

    [Fact]
    public void WhatAScanPutsInThePage_IsNotSomethingToUndo()
    {
        var a = Photo("a.jpg");

        Ui.Run(() => Dialog([a], Settings(), Reads("read")), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));

            await dialog.ScanPageAsync();

            Assert.False(dialog.Editor.UndoButton.Enabled);
            Assert.Equal("read", dialog.Editor.PlainText);
        });
    }

    [Fact]
    public void JoinLines_IsOneStepOfUndo_AndRedoBringsItAgain()
    {
        var a = Photo("a.jpg");

        Ui.Run(() => Dialog([a], Settings()), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            const string text = "one two\nthree four\n\nfive six\nseven eight";
            dialog.Editor.Box.Text = text;
            dialog.Editor.ClearHistory();

            dialog.JoinLines();
            Assert.Equal("one two three four\n\nfive six seven eight", dialog.Editor.PlainText);

            dialog.Editor.UndoButton.PerformClick();
            Assert.Equal(text, dialog.Editor.PlainText);

            dialog.Editor.RedoButton.PerformClick();
            Assert.Equal("one two three four\n\nfive six seven eight", dialog.Editor.PlainText);
        });
    }

    [Fact]
    public void TheAiFix_IsOneStepOfUndo_AndTheButtonsTakeItBackAndBringItAgain()
    {
        var a = Photo("a.jpg");
        var internet = new StandInInternet().Answer(HttpStatusCode.OK, ClaudeSays("Wednesday painting\nThursday music"));

        Ui.Run(() => Dialog([a], Settings(), internet), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            dialog.Editor.Box.Text = "Wensday painting\nThursdav music";
            dialog.Editor.ClearHistory();

            await dialog.FixTextAsync();
            Assert.Equal("Wednesday painting\nThursday music", dialog.Editor.PlainText);
            Assert.True(dialog.Editor.UndoButton.Enabled);

            dialog.Editor.UndoButton.PerformClick();
            Assert.Equal("Wensday painting\nThursdav music", dialog.Editor.PlainText);
            Assert.True(dialog.Editor.RedoButton.Enabled);

            dialog.Editor.RedoButton.PerformClick();
            Assert.Equal("Wednesday painting\nThursday music", dialog.Editor.PlainText);
        });
    }
}
