using PhotoLiker.PhotoBook;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The Rich Text Box of the book window with its buttons, in a real window: what it writes as RTF is read back by the parser the PDF and the EPUB use, so this is where the two are tried
/// against each other (the RTF of a real Rich Text Box, with Cyrillic and every look the buttons give).
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class RichDescriptionEditorTests
{
    private static Form Host(out RichDescriptionEditor editor)
    {
        editor = new RichDescriptionEditor { Dock = DockStyle.Fill };
        var form = new Form { ClientSize = new Size(520, 260), StartPosition = FormStartPosition.CenterScreen, ShowInTaskbar = false };
        form.Controls.Add(editor);
        return form;
    }

    private static RichDocument Read(RichDescriptionEditor editor) => RtfParser.Parse(editor.Rtf);

    [Fact]
    public void PlainText_IsOneRunInTheUsualLook_Size12()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.Box.Text = "Hello world";

            var document = Read(editor);
            var paragraph = Assert.Single(document.Paragraphs);
            Assert.Equal("Hello world", paragraph.PlainText);
            var run = Assert.Single(paragraph.Runs);
            Assert.False(run.Style.Bold || run.Style.Italic || run.Style.Underline || run.Style.Strike);
            Assert.Equal(12, run.Style.PointSize, 3);
            Assert.Null(run.Style.Color);
            Assert.Equal(TextAlign.Left, paragraph.Align);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void Cyrillic_IsWhatTheRtfOfARealRichTextBoxReadsBackAs()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            const string text = "Привіт, світе! Це опис фото: «Київ», 5 червня.\nДругий рядок: ґанок, єнот, їжак.";
            editor.Box.Text = text;

            Assert.Equal(text.Replace("\n", "\n"), Read(editor).PlainText);
            Assert.Equal(2, Read(editor).Paragraphs.Count);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void Bold_Italic_Underline_Strike_AreInTheRuns()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.Box.Text = "one two three four";
            editor.Box.Select(4, 3);
            editor.ToggleStyle(FontStyle.Bold);
            editor.Box.Select(8, 5);
            editor.ToggleStyle(FontStyle.Italic);
            editor.Box.Select(14, 4);
            editor.ToggleStyle(FontStyle.Underline);
            editor.ToggleStyle(FontStyle.Strikeout);

            var runs = Assert.Single(Read(editor).Paragraphs).Runs;
            Assert.Equal("one two three four", string.Concat(runs.Select(run => run.Text)));
            Assert.True(runs.Single(run => run.Text.Contains("two")).Style.Bold);
            Assert.True(runs.Single(run => run.Text.Contains("three")).Style.Italic);
            var four = runs.Single(run => run.Text.Contains("four"));
            Assert.True(four.Style.Underline && four.Style.Strike);
            Assert.DoesNotContain(runs, run => run.Text.StartsWith("one") && (run.Style.Bold || run.Style.Italic));
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void ABoldWord_IsTakenOffByTheSameButton()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.Box.Text = "plain bold plain";
            editor.Box.Select(6, 4);
            editor.ToggleStyle(FontStyle.Bold);
            Assert.Contains(Read(editor).Paragraphs[0].Runs, run => run.Style.Bold);

            editor.ToggleStyle(FontStyle.Bold);

            Assert.DoesNotContain(Read(editor).Paragraphs[0].Runs, run => run.Style.Bold);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void ThePickThatHasMoreThanOneLook_IsChangedRunByRun_EachKeepingItsOwnLooks()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.Box.Text = "aaa bbb ccc";
            editor.Box.Select(4, 3);
            editor.ToggleStyle(FontStyle.Bold);
            editor.Box.SelectAll();

            editor.SetSize(20); // the whole text, in two looks, gets the size

            var runs = Assert.Single(Read(editor).Paragraphs).Runs;
            Assert.All(runs, run => Assert.Equal(20, run.Style.PointSize, 3));
            Assert.Equal("bbb", runs.Single(run => run.Style.Bold).Text);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void TheSizeAndTheColour_AreInTheRuns()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.Box.Text = "small big red";
            editor.Box.Select(6, 3);
            editor.SetSize(28);
            editor.Box.Select(10, 3);
            editor.ApplyColor(Color.FromArgb(200, 30, 30));

            var runs = Assert.Single(Read(editor).Paragraphs).Runs;
            Assert.Equal(28, runs.Single(run => run.Text.Contains("big")).Style.PointSize, 3);
            var red = runs.Single(run => run.Text.Contains("red"));
            Assert.Equal(0xC81E1E, red.Style.Color);
            Assert.Null(runs[0].Style.Color);
            await Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(1, TextAlign.Left)]    // the numbers of Rich Edit: 1 left, 2 right, 3 centre, 4 justified
    [InlineData(3, TextAlign.Center)]
    [InlineData(2, TextAlign.Right)]
    [InlineData(4, TextAlign.Justify)]
    public void TheAlignment_IsInTheParagraph_JustifiedToo(int number, TextAlign expected)
    {
        var alignment = (RichDescriptionEditor.RichAlignment)number;
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.Box.Text = "first\nsecond";
            editor.Box.Select(0, 3); // the first paragraph only
            editor.SetAlignment(alignment);

            var paragraphs = Read(editor).Paragraphs;
            Assert.Equal(expected, paragraphs[0].Align);
            Assert.Equal(TextAlign.Left, paragraphs[1].Align);
            Assert.Equal(alignment, editor.GetAlignment());
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void ClearFormatting_MakesThePickPlain_AndWithNothingPickedTheWholeText()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.Box.Text = "some text";
            editor.Box.SelectAll();
            editor.ToggleStyle(FontStyle.Bold);
            editor.ToggleStyle(FontStyle.Italic);
            editor.SetSize(30);
            editor.ApplyColor(Color.Blue);
            editor.SetAlignment(RichDescriptionEditor.RichAlignment.Center);

            editor.Box.Select(2, 0);
            editor.ClearFormatting();

            var paragraph = Assert.Single(Read(editor).Paragraphs);
            var run = Assert.Single(paragraph.Runs);
            Assert.False(run.Style.Bold || run.Style.Italic);
            Assert.Equal(12, run.Style.PointSize, 3);
            Assert.Equal(TextAlign.Left, paragraph.Align);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void ClearFormatting_TakesTheIndentOff_Too()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.Rtf = RtfWriter.Write(new RichDocument([new RichParagraph(TextAlign.Left, [new RichRun("indented text", default)]) { LeftIndent = 72, FirstLineIndent = 36 }]));
            Assert.Equal(72, Assert.Single(Read(editor).Paragraphs).LeftIndent);

            editor.Box.Select(2, 0);
            editor.ClearFormatting();

            var paragraph = Assert.Single(Read(editor).Paragraphs);
            Assert.Equal((0.0, 0.0), (paragraph.LeftIndent, paragraph.FirstLineIndent));
            await Task.CompletedTask;
        });
    }

    private static RichPicture Picture() => RichPicture.FromFile(SeeThroughPng(), 0.4)!;

    /// <summary>A small PNG with a see-through half (made here: the tests of the book library have their own).</summary>
    private static byte[] SeeThroughPng()
    {
        using var bitmap = new Bitmap(40, 30, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (var y = 0; y < 30; y++)
        {
            for (var x = 0; x < 40; x++)
                bitmap.SetPixel(x, y, x < 20 ? Color.FromArgb(0, 0, 0, 0) : Color.FromArgb(255, 200, 30, 30));
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return stream.ToArray();
    }

    [Fact]
    public void APictureInTheRtf_IsShown_AndGivenBackWithItsPlaceItsWidthIndentsAndTabs()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            var document = new RichDocument([
                RichParagraph.OfPicture(Picture(), TextAlign.Right),
                new RichParagraph(TextAlign.Left, [new RichRun("cat\tкіт", default)]) { LeftIndent = 36, TabStops = [150] },
                new RichParagraph(TextAlign.Left, [new RichRun("after", default)])]);

            editor.Rtf = RtfWriter.Write(document);

            var back = Read(editor);
            Assert.Equal(3, back.Paragraphs.Count);
            var picture = back.Paragraphs[0].Picture!;
            Assert.True(picture.IsPng);
            Assert.Equal((40, 30), (picture.PixelWidth, picture.PixelHeight));
            Assert.Equal(0.4, picture.WidthShare, 2);
            Assert.Equal(TextAlign.Right, back.Paragraphs[0].Align);
            Assert.Equal("cat\tкіт", back.Paragraphs[1].PlainText);
            Assert.Equal(36.0, back.Paragraphs[1].LeftIndent);
            Assert.Equal([150.0], back.Paragraphs[1].TabStops);
            Assert.False(editor.IsEmpty, "a picture is no blank");
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void ABoxThatCannotBeEdited_StillTakesTheRtfWithItsPictures_AndStaysReadOnly()
    {
        // A scan is at work on the box: it is read-only, and a read-only Rich Edit takes no picture of an RTF unless it is let to
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.SetEditable(false);

            editor.Rtf = RtfWriter.Write(new RichDocument([RichParagraph.OfPicture(Picture()), new RichParagraph(TextAlign.Left, [new RichRun("text", default)])]));

            Assert.True(editor.Box.ReadOnly);
            Assert.Equal(2, Read(editor).Paragraphs.Count);
            Assert.NotNull(Read(editor).Paragraphs[0].Picture);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void ABoxWithOnlyAPicture_IsNotEmpty_AndOneWithNothingIs()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            Assert.True(editor.IsEmpty);
            editor.Rtf = RtfWriter.Write(new RichDocument([RichParagraph.OfPicture(Picture())]));
            Assert.False(editor.IsEmpty);
            Assert.Equal("text", PhotoLiker.Manuscripts.ManuscriptText.PlainOf(RtfWriter.Write(RichDocument.FromPlainText("text"))));

            editor.Rtf = string.Empty;
            Assert.True(editor.IsEmpty);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void WhatIsSetAsRtf_IsGivenBackAsItWas()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.Box.Text = "Заголовок\nтекст";
            editor.Box.Select(0, 9);
            editor.ToggleStyle(FontStyle.Bold);
            editor.SetSize(24);
            var rtf = editor.Rtf;

            editor.Rtf = string.Empty;
            Assert.True(editor.IsEmpty);
            editor.Rtf = rtf;

            var document = Read(editor);
            Assert.Equal("Заголовок\nтекст", document.PlainText);
            Assert.True(document.Paragraphs[0].Runs[0].Style.Bold);
            Assert.Equal(24, document.Paragraphs[0].Runs[0].Style.PointSize, 3);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void PlainTextGivenAsRtf_IsPutInAsText()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.Rtf = "not rtf at all";

            Assert.Equal("not rtf at all", editor.PlainText);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void TheTextChangedEvent_IsRaisedByTypingAndByTheButtons()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            var raised = 0;
            editor.TextChanged += (_, _) => raised++;

            editor.Box.Text = "abc";
            var afterTyping = raised;
            editor.Box.SelectAll();
            editor.ToggleStyle(FontStyle.Bold);
            var afterBold = raised;
            editor.SetAlignment(RichDescriptionEditor.RichAlignment.Right);

            Assert.True(afterTyping > 0);
            Assert.True(afterBold > afterTyping, "making text bold is a change");
            Assert.True(raised > afterBold, "aligning it is a change");
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void TheButtons_ShowTheLookOfTheTextAtTheCursor()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.Box.Text = "plain bold";
            editor.Box.Select(6, 4);
            editor.ToggleStyle(FontStyle.Bold);

            editor.Box.Select(7, 0);
            var buttons = Ui.Controls<ToolStrip>(editor).Single().Items.OfType<ToolStripButton>().ToList();
            Assert.True(buttons.Single(button => button.Text == "B").Checked);
            Assert.False(buttons.Single(button => button.Text == "I").Checked);

            editor.Box.Select(1, 0);
            Assert.False(buttons.Single(button => button.Text == "B").Checked);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void AnEditorThatIsNotEditable_IsGreyAndDoesNotTakeText()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.SetEditable(false);

            Assert.True(editor.Box.ReadOnly);
            Assert.False(Ui.Controls<ToolStrip>(editor).Single().Enabled);

            editor.SetEditable(true);
            Assert.False(editor.Box.ReadOnly);
            Assert.Equal(Color.White, editor.Box.BackColor);
            await Task.CompletedTask;
        });
    }

    // ── The AI button, and the words it changes ─────────────────────────────

    [Fact]
    public void TheAiButton_IsNotThereUntilTheHostShowsIt_AndPressingItIsToldToTheHost()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            Assert.False(editor.FixVisible); // the descriptions of a book have no AI
            Assert.False(editor.FixButton.Visible);
            Assert.NotNull(editor.FixButton.Image);

            var raised = 0;
            editor.FixRequested += (_, _) => raised++;
            editor.FixVisible = true;
            Assert.True(editor.FixButton.Visible);

            editor.FixButton.PerformClick();
            Assert.Equal(1, raised);

            editor.SetEditable(false); // while the window works on the text, the button does nothing
            Assert.False(editor.FixButton.Enabled);
            await Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheAiButtonHasAPictureOfItsOwn_ForEachTheme(bool dark)
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.ApplyTheme(dark);

            var picture = Assert.IsType<Bitmap>(editor.FixButton.Image);
            var coloured = Enumerable.Range(0, picture.Width).SelectMany(x => Enumerable.Range(0, picture.Height).Select(y => picture.GetPixel(x, y))).Count(pixel => pixel.A > 128);
            Assert.InRange(coloured, 20, 120); // stars, not a filled square and not nothing
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void TheWordsThatAreReplaced_AreReplacedAtTheirPlaces_AndTheLookOfTheRestStays()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.Box.Text = "one wen two lbrary three";
            editor.Box.Select(21, 3); // "ree" of three: bold
            editor.ToggleStyle(FontStyle.Bold);
            editor.Box.Select(0, 0);

            editor.ApplyEdits([new PhotoLiker.Manuscripts.TextEdit(4, 3, "went"), new PhotoLiker.Manuscripts.TextEdit(12, 6, "library")]);

            Assert.Equal("one went two library three", editor.PlainText);
            var runs = Read(editor).Paragraphs.Single().Runs;
            Assert.Equal("ree", runs.Single(run => run.Style.Bold).Text);
            Assert.Equal(0, editor.Box.SelectionStart); // the caret stays
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void AReplacedWord_HasTheLookOfItsFirstLetter()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.Box.Text = "one wen two";
            editor.Box.Select(4, 3);
            editor.ToggleStyle(FontStyle.Bold);

            editor.ApplyEdits([new PhotoLiker.Manuscripts.TextEdit(4, 3, "went")]);

            var runs = Read(editor).Paragraphs.Single().Runs;
            Assert.Equal("went", runs.Single(run => run.Style.Bold).Text);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void AllTheChanges_AreOneStepOfUndo_AndRedoBringsThemBack()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            const string original = "one wen two lbrary three borowed four";
            editor.Box.Text = original;
            editor.Box.ClearUndo();

            editor.ApplyEdits([new PhotoLiker.Manuscripts.TextEdit(4, 3, "went"), new PhotoLiker.Manuscripts.TextEdit(12, 6, "library"), new PhotoLiker.Manuscripts.TextEdit(25, 7, "borrowed")]);
            Assert.Equal("one went two library three borrowed four", editor.PlainText);

            editor.Box.Undo();
            Assert.Equal(original, editor.PlainText); // one Ctrl+Z, not three
            editor.Box.Redo();
            Assert.Equal("one went two library three borrowed four", editor.PlainText);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void ABoxThatCannotBeEdited_TakesTheChanges_AndStaysThat()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.Box.Text = "one wen two";
            editor.SetEditable(false);

            editor.ApplyEdits([new PhotoLiker.Manuscripts.TextEdit(4, 3, "went")]);

            Assert.Equal("one went two", editor.PlainText);
            Assert.True(editor.Box.ReadOnly);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void TheTabsTheIndentsAndThePictures_StayWhenWordsAreReplaced()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.Rtf = RtfWriter.Write(new RichDocument(
            [
                new RichParagraph(TextAlign.Left, [new RichRun("Wensday\tpainting", default)]) { LeftIndent = 36 },
                RichParagraph.OfPicture(Picture()),
                new RichParagraph(TextAlign.Left, [new RichRun("the lbrary", default)]),
            ]));
            var text = editor.PlainText;

            editor.ApplyEdits([new PhotoLiker.Manuscripts.TextEdit(text.IndexOf("Wensday", StringComparison.Ordinal), 7, "Wednesday"), new PhotoLiker.Manuscripts.TextEdit(text.IndexOf("lbrary", StringComparison.Ordinal), 6, "library")]);

            var document = Read(editor);
            Assert.Equal("Wednesday\tpainting", document.Paragraphs[0].PlainText);
            Assert.Equal(36, document.Paragraphs[0].LeftIndent, 1);
            Assert.NotNull(document.Paragraphs[1].Picture);
            Assert.Equal("the library", document.Paragraphs[2].PlainText);
            await Task.CompletedTask;
        });
    }
}
