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

    // ── Undo and Redo ───────────────────────────────────────────────────────

    [Fact]
    public void UndoAndRedo_AreTheFirstButtons_AndOffWhileThereIsNothingToTakeBackOrBring()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            var tools = Ui.Controls<ToolStrip>(editor).Single();

            Assert.Same(editor.UndoButton, tools.Items[0]);
            Assert.Same(editor.RedoButton, tools.Items[1]);
            Assert.IsType<ToolStripSeparator>(tools.Items[2]);
            Assert.Equal(BookStrings.Undo, editor.UndoButton.ToolTipText);
            Assert.Equal(BookStrings.Redo, editor.RedoButton.ToolTipText);
            Assert.True(editor.UndoVisible);
            Assert.False(editor.UndoButton.Enabled);
            Assert.False(editor.RedoButton.Enabled);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void WhatIsTyped_IsTakenBackByUndo_AndBroughtAgainByRedo()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.Box.Text = "start";
            editor.ClearHistory();
            editor.Box.Select(5, 0);
            editor.Box.SelectedText = " and more";
            Assert.True(editor.UndoButton.Enabled);
            Assert.False(editor.RedoButton.Enabled);

            editor.UndoButton.PerformClick();

            Assert.Equal("start", editor.PlainText);
            Assert.False(editor.UndoButton.Enabled);
            Assert.True(editor.RedoButton.Enabled);

            editor.RedoButton.PerformClick();

            Assert.Equal("start and more", editor.PlainText);
            Assert.True(editor.UndoButton.Enabled);
            Assert.False(editor.RedoButton.Enabled);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void TheLookOfTheText_IsTakenBackToo()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.Box.Text = "one two";
            editor.ClearHistory();
            editor.Box.Select(0, 3);
            editor.ToggleStyle(FontStyle.Bold);
            Assert.Contains(Read(editor).Paragraphs.Single().Runs, run => run.Style.Bold);

            editor.UndoButton.PerformClick();

            Assert.DoesNotContain(Read(editor).Paragraphs.Single().Runs, run => run.Style.Bold);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void AnUndoButtonPressed_GivesTheFocusBackToTheText_SoTheTypingGoesOn()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.Box.Text = "a";
            editor.ClearHistory();
            editor.Box.Select(1, 0);
            editor.Box.SelectedText = "b";

            editor.UndoButton.PerformClick();

            Assert.True(await Ui.Until(() => editor.Box.Focused));
        });
    }

    [Fact]
    public void ATextThatIsPutIn_IsNotSomethingToUndo_AndTheTextOfBeforeIsNotBroughtBack()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.Rtf = RtfWriter.Write(new RichDocument([new RichParagraph(TextAlign.Left, [new RichRun("the first page", default)])]));
            editor.Box.Select(0, 0);
            editor.Box.SelectedText = "typed ";
            Assert.True(editor.UndoButton.Enabled);

            // Another page is put in: nothing of the first can be undone into it
            editor.Rtf = RtfWriter.Write(new RichDocument([new RichParagraph(TextAlign.Left, [new RichRun("the second page", default)])]));

            Assert.False(editor.UndoButton.Enabled);
            Assert.False(editor.RedoButton.Enabled);
            editor.UndoButton.PerformClick(); // does nothing
            Assert.Equal("the second page", editor.PlainText);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void ClearHistory_ForgetsWhatCanBeUndoneAndWhatCanBeBroughtAgain()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.Box.Text = "one";
            editor.ClearHistory();
            editor.Box.Select(3, 0);
            editor.Box.SelectedText = " two";
            editor.Box.Undo();
            Assert.True(editor.RedoButton.Enabled);

            editor.ClearHistory();

            Assert.False(editor.UndoButton.Enabled);
            Assert.False(editor.RedoButton.Enabled);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void ManyChangesInOneStep_AreTakenBackByOneUndo_AndBroughtAgainByOneRedo()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.Box.Text = "aa bb cc";
            editor.ClearHistory();

            using (editor.UndoStep())
            {
                editor.Box.Select(6, 2);
                editor.Box.SelectedText = "CC";
                editor.Box.Select(3, 2);
                editor.Box.SelectedText = "BB";
                editor.Box.Select(0, 2);
                editor.Box.SelectedText = "AA";
            }

            Assert.Equal("AA BB CC", editor.PlainText);

            editor.UndoButton.PerformClick();
            Assert.Equal("aa bb cc", editor.PlainText);

            editor.RedoButton.PerformClick();
            Assert.Equal("AA BB CC", editor.PlainText);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void TheWordsTheHostReplaces_AreTakenBackByOneUndoButton_AndBroughtAgainByRedo()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.Box.Text = "one wen two lbrary";
            editor.ClearHistory();

            editor.ApplyEdits([new PhotoLiker.Manuscripts.TextEdit(4, 3, "went"), new PhotoLiker.Manuscripts.TextEdit(12, 6, "library")]);
            Assert.Equal("one went two library", editor.PlainText);

            editor.UndoButton.PerformClick();
            Assert.Equal("one wen two lbrary", editor.PlainText);

            editor.RedoButton.PerformClick();
            Assert.Equal("one went two library", editor.PlainText);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void TheButtonsCanBeLeftOut_ByAHost()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.UndoVisible = false;

            Assert.False(editor.UndoButton.Visible);
            Assert.False(editor.RedoButton.Visible);
            Assert.False(Ui.Controls<ToolStrip>(editor).Single().Items.OfType<ToolStripSeparator>().First().Visible);

            editor.UndoVisible = true;
            Assert.True(editor.UndoButton.Visible);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void WhileTheWindowWorksOnTheText_UndoAndRedoAreOff_WithTheOtherButtons()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.Box.Text = "a";
            editor.ClearHistory();
            editor.Box.Select(1, 0);
            editor.Box.SelectedText = "b";
            Assert.True(editor.UndoButton.Enabled);

            editor.SetEditable(false);

            Assert.False(editor.UndoButton.Enabled && Ui.Controls<ToolStrip>(editor).Single().Enabled);
            await Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheArrowsAreInTheColoursOfTheTheme(bool dark)
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.ApplyTheme(dark);

            Assert.Equal(dark ? ThemeManager.DarkFore : ThemeManager.LightFore, editor.UndoButton.ForeColor);
            Assert.Equal(editor.UndoButton.ForeColor, editor.RedoButton.ForeColor);
            Assert.Equal("↶", editor.UndoButton.Text);
            Assert.Equal("↷", editor.RedoButton.Text);
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

    // ── Lists ───────────────────────────────────────────────────────────────

    private static void PickAll(RichDescriptionEditor editor) => editor.Box.SelectAll();

    private static string[] Markers(RichDescriptionEditor editor) => [.. Read(editor).Paragraphs.Select(paragraph => paragraph.Marker ?? string.Empty)]; // (empty: not an item of a list)

    [Fact]
    public void TheButtonsOfTheLists_AndOfTheCharacters_AreAfterTheAlignment_AndHaveTheirTips()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            var names = Ui.Controls<ToolStrip>(editor).Single().Items.Cast<ToolStripItem>().Select(item => item.Name).ToList();

            Assert.True(names.IndexOf("bulletsButton") > 0);
            Assert.Equal(names.IndexOf("bulletsButton") + 1, names.IndexOf("numbersButton"));
            Assert.Equal(names.IndexOf("numbersButton") + 1, names.IndexOf("showCharactersButton"));
            Assert.Equal(BookStrings.BulletList, editor.BulletsButton.ToolTipText);
            Assert.Equal(BookStrings.NumberedList, editor.NumbersButton.ToolTipText);
            Assert.Equal(BookStrings.ShowCharacters, editor.ShowCharactersButton.ToolTipText);
            Assert.NotNull(editor.BulletsButton.Image);
            Assert.NotNull(editor.NumbersButton.Image);
            Assert.False(editor.BulletsButton.Checked || editor.NumbersButton.Checked || editor.ShowCharactersButton.Checked);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void TheBulletsButton_MakesTheParagraphsABulletedList_WhichIsReadBackWithItsMarkers()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.Box.Text = "one\ntwo\nthree";
            PickAll(editor);

            editor.BulletsButton.PerformClick();

            var document = Read(editor);
            Assert.Equal(["•", "•", "•"], Markers(editor));
            Assert.Equal("one\ntwo\nthree", document.PlainText); // the text of the box is the same: the bullets are not in it
            Assert.Equal("one\ntwo\nthree", editor.PlainText);
            Assert.All(document.Paragraphs, paragraph => Assert.Equal((36.0, -18.0), (paragraph.LeftIndent, paragraph.FirstLineIndent)));
            Assert.True(editor.BulletsButton.Checked);
            Assert.False(editor.NumbersButton.Checked);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void TheNumbersButton_NumbersTheItemsOneAfterTheOther()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.Box.Text = "one\ntwo\nthree";
            PickAll(editor);

            editor.NumbersButton.PerformClick();

            Assert.Equal(["1.", "2.", "3."], Markers(editor));
            Assert.All(Read(editor).Paragraphs, paragraph => Assert.True(paragraph.IsNumbered));
            Assert.True(editor.NumbersButton.Checked);
            Assert.False(editor.BulletsButton.Checked);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void ThePressedButtonOfAList_TakesTheParagraphsOutOfTheList_AndTheIndentGoes()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.Box.Text = "one\ntwo";
            PickAll(editor);
            editor.NumbersButton.PerformClick();
            Assert.Equal(["1.", "2."], Markers(editor));

            editor.NumbersButton.PerformClick();

            Assert.Equal(["", ""], Markers(editor));
            Assert.All(Read(editor).Paragraphs, paragraph => Assert.Equal((0.0, 0.0), (paragraph.LeftIndent, paragraph.FirstLineIndent)));
            Assert.False(editor.NumbersButton.Checked);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void ABulletedListThatTheNumbersButtonIsPressedFor_BecomesANumberedOne()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.Box.Text = "one\ntwo";
            PickAll(editor);
            editor.BulletsButton.PerformClick();

            editor.NumbersButton.PerformClick();

            Assert.Equal(["1.", "2."], Markers(editor));
            Assert.True(editor.NumbersButton.Checked);
            Assert.False(editor.BulletsButton.Checked);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void OnlyTheParagraphsThatArePicked_AreMadeItemsOfTheList()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.Box.Text = "one\ntwo\nthree";
            editor.Box.Select(5, 0); // in "two"

            editor.BulletsButton.PerformClick();

            Assert.Equal(["", "•", ""], Markers(editor));
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void TheButtons_ShowTheListTheCaretIsIn()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.Box.Text = "one\ntwo\nthree";
            editor.Box.Select(5, 0);
            editor.NumbersButton.PerformClick();

            editor.Box.Select(1, 0); // in "one", which is not an item
            Assert.False(editor.NumbersButton.Checked);
            editor.Box.Select(6, 0); // in "two" again
            Assert.True(editor.NumbersButton.Checked);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void EnterAtTheEndOfAnItem_MakesTheNextItem_WithTheNextNumber()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.Box.Text = "one";
            PickAll(editor);
            editor.NumbersButton.PerformClick();
            editor.Box.Select(3, 0);

            editor.Box.SelectedText = "\ntwo"; // what Enter and the typing of "two" do

            Assert.Equal(["1.", "2."], Markers(editor));
            Assert.Equal("one\ntwo", Read(editor).PlainText);
            await Task.CompletedTask;
        });
    }

    private static bool PressEnter(RichDescriptionEditor editor)
    {
        var keys = new KeyEventArgs(Keys.Enter);
        typeof(Control).GetMethod("OnKeyDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(editor.Box, [keys]);
        return keys.Handled;
    }

    [Fact]
    public void EnterInAnItemWithNothingWrittenInIt_EndsTheList_InsteadOfMakingAnotherItem()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.Box.Text = "one";
            PickAll(editor);
            editor.BulletsButton.PerformClick();
            editor.Box.Select(3, 0);
            editor.Box.SelectedText = "\n"; // the empty item that Enter makes
            Assert.Equal(["•", "•"], Markers(editor));

            Assert.True(PressEnter(editor)); // Enter again, in the empty item

            Assert.Equal(["•", ""], Markers(editor));
            Assert.Equal("one\n", editor.PlainText); // no third line was typed: the empty item is now a paragraph that is not in the list
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void EnterInAnItemWithTextInIt_IsLeftToTheBox_AndInAParagraphThatIsNotAnItemToo()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.Box.Text = "one\ntwo";
            editor.Box.Select(0, 0);
            Assert.False(PressEnter(editor)); // not a list

            editor.Box.Select(4, 0);
            editor.BulletsButton.PerformClick();
            editor.Box.Select(6, 0);
            Assert.False(PressEnter(editor)); // an item with "two" in it
            Assert.Equal(["", "•"], Markers(editor));
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void TheListOfARtfThatIsPutIn_IsAListInTheBox_AndIsGivenBackAsOne()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.Rtf = RtfWriter.Write(new RichDocument([
                new RichParagraph(TextAlign.Left, [new RichRun("intro", default)]),
                new RichParagraph(TextAlign.Left, [new RichRun("four", default)]) { LeftIndent = 36, FirstLineIndent = -18, Marker = "4." },
                new RichParagraph(TextAlign.Left, [new RichRun("five", default)]) { LeftIndent = 36, FirstLineIndent = -18, Marker = "5." },
                new RichParagraph(TextAlign.Left, [new RichRun("dot", default)]) { LeftIndent = 36, FirstLineIndent = -18, Marker = "•" },
            ]));

            Assert.Equal(["", "4.", "5.", "•"], Markers(editor)); // numbering goes on from the number the item had
            editor.Box.Select(editor.PlainText.IndexOf("five", StringComparison.Ordinal), 0);
            Assert.True(editor.NumbersButton.Checked);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void ClearFormatting_TakesTheListOff_Too()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.Box.Text = "one\ntwo";
            PickAll(editor);
            editor.BulletsButton.PerformClick();

            editor.ClearFormatting();

            Assert.Equal(["", ""], Markers(editor));
            Assert.False(editor.BulletsButton.Checked);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void TheButtonsOfTheLists_RaiseTheChangeOfTheText_AndAreTakenBackByUndo()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.Box.Text = "one";
            editor.ClearHistory();
            var raised = 0;
            editor.TextChanged += (_, _) => raised++;

            PickAll(editor);
            editor.BulletsButton.PerformClick();

            Assert.True(raised > 0);
            Assert.Equal(["•"], Markers(editor));

            editor.UndoButton.PerformClick();

            Assert.Equal([""], Markers(editor));
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void WordsThatAreReplaced_StayInTheirItems()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.Box.Text = "one wen\ntwo";
            PickAll(editor);
            editor.NumbersButton.PerformClick();

            editor.ApplyEdits([new PhotoLiker.Manuscripts.TextEdit(4, 3, "went")]);

            Assert.Equal(["1.", "2."], Markers(editor));
            Assert.Equal("one went\ntwo", Read(editor).PlainText);
            await Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheButtonsOfTheLists_HavePicturesInTheColoursOfTheTheme(bool dark)
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.ApplyTheme(dark);

            foreach (var button in new[] { editor.BulletsButton, editor.NumbersButton })
            {
                var picture = Assert.IsType<Bitmap>(button.Image);
                var pixels = Enumerable.Range(0, picture.Width).SelectMany(x => Enumerable.Range(0, picture.Height).Select(y => picture.GetPixel(x, y))).Where(pixel => pixel.A > 128).ToList();
                Assert.InRange(pixels.Count, 20, 140);
                var expected = dark ? ThemeManager.DarkFore : SystemColors.ControlText;
                Assert.All(pixels, pixel => Assert.True(Math.Abs(pixel.R - expected.R) < 40 && Math.Abs(pixel.G - expected.G) < 40 && Math.Abs(pixel.B - expected.B) < 40));
            }

            Assert.NotEqual(Convert.ToBase64String(ImageBytes(editor.BulletsButton.Image!)), Convert.ToBase64String(ImageBytes(editor.NumbersButton.Image!)));
            await Task.CompletedTask;
        });
    }

    private static byte[] ImageBytes(Image image)
    {
        using var stream = new MemoryStream();
        image.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return stream.ToArray();
    }

    // ── The characters that are not seen ────────────────────────────────────

    /// <summary>The picture the box makes of itself, and how many of its pixels are the colour of the marks.</summary>
    private static (Bitmap Picture, int Marks) Photograph(RichDescriptionEditor editor)
    {
        var picture = new Bitmap(editor.Box.Width, editor.Box.Height);
        editor.Box.DrawToBitmap(picture, new Rectangle(Point.Empty, editor.Box.Size));
        var count = 0;
        for (var x = 0; x < picture.Width; x++)
        {
            for (var y = 0; y < picture.Height; y++)
            {
                var pixel = picture.GetPixel(x, y);
                if (pixel.B - pixel.R >= 45 && pixel.B >= 110)
                    count++; // blue: the colour of the marks, or the edge of one; the text is black and the picture of the tests red
            }
        }

        return (picture, count);
    }

    [Fact]
    public void TheCharactersButton_IsDownWhileTheyAreShown_AndUpAgainWhenPressedAgain()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            Assert.False(editor.ShowAllCharacters);

            editor.ShowCharactersButton.PerformClick();
            Assert.True(editor.ShowAllCharacters);
            Assert.True(editor.ShowCharactersButton.Checked);

            editor.ShowCharactersButton.PerformClick();
            Assert.False(editor.ShowAllCharacters);
            Assert.False(editor.ShowCharactersButton.Checked);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void TheMarks_AreDrawnOverTheText_AndAreNotAPartOfIt()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.Box.Text = "one two\tthree\nsecond line";
            var rtf = editor.Rtf;
            await Task.Delay(100);
            var without = Photograph(editor).Marks; // the colour of the marks, while they are off

            editor.ShowAllCharacters = true;
            await Task.Delay(100);
            var (picture, marks) = Photograph(editor);
            picture.Save(Path.Combine(Path.GetTempPath(), "marks-on.png"));

            Assert.True(marks > without + 40, $"{without} pixels of the marks without them, {marks} with them");
            Assert.Equal("one two\tthree\nsecond line", editor.PlainText); // they are only drawn
            Assert.Equal(rtf, editor.Rtf);

            editor.ShowAllCharacters = false;
            await Task.Delay(100);
            Assert.True(Photograph(editor).Marks <= without + 3);
        });
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int fromX, int fromY, int operation);

    /// <summary>What the window has on the screen right now (copied out of its own device), not what it makes when it is asked for a picture of itself: the way it paints itself is what is looked at.</summary>
    private static Bitmap OnTheScreen(Control control)
    {
        var picture = new Bitmap(control.ClientSize.Width, control.ClientSize.Height);
        using var graphics = Graphics.FromImage(picture);
        using var window = Graphics.FromHwnd(control.Handle);
        var from = window.GetHdc();
        var to = graphics.GetHdc();
        try
        {
            BitBlt(to, 0, 0, picture.Width, picture.Height, from, 0, 0, 0x00CC0020);
        }
        finally
        {
            graphics.ReleaseHdc(to);
            window.ReleaseHdc(from);
        }

        return picture;
    }

    private static int BlueIn(Bitmap picture, Rectangle area)
    {
        var count = 0;
        for (var x = area.Left; x < area.Right && x < picture.Width; x++)
        {
            for (var y = area.Top; y < area.Bottom && y < picture.Height; y++)
            {
                var pixel = picture.GetPixel(x, y);
                if (pixel.B - pixel.R >= 45 && pixel.B >= 110)
                    count++;
            }
        }

        return count;
    }

    [Fact]
    public void TheMarks_AreThereWhenTheWindowPaintsItself_AlsoAfterTheTextIsScrolled()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.Box.Text = string.Join("\n", Enumerable.Range(1, 90).Select(number => $"line {number} has a few words"));
            editor.Box.Select(0, 0);
            editor.ShowAllCharacters = true;
            editor.Box.Invalidate();
            editor.Box.Update();
            await Task.Delay(150);

            var area = new Rectangle(0, 0, editor.Box.ClientSize.Width, editor.Box.ClientSize.Height);
            var top = new Rectangle(0, 0, area.Width, area.Height / 3);
            var bottom = new Rectangle(0, area.Height * 2 / 3, area.Width, area.Height / 3);
            using (var first = OnTheScreen(editor.Box))
            {
                Assert.True(BlueIn(first, top) > 30, "the first lines have their marks");
                Assert.True(BlueIn(first, bottom) > 30, "so have the last lines of the first screen");
            }

            // Scrolled to the end and back to the middle: the part that moved keeps its marks, the part that was uncovered has its own, and none is drawn twice (that is a deeper blue)
            editor.Box.Select(editor.Box.TextLength, 0);
            editor.Box.ScrollToCaret();
            editor.Box.Update();
            await Task.Delay(150);
            using (var end = OnTheScreen(editor.Box))
            using (var fresh = Photograph(editor).Picture)
            {
                Assert.True(BlueIn(end, top) > 30);
                Assert.True(BlueIn(end, bottom) > 30);
                var onTheScreen = BlueIn(end, area);
                var wholeAgain = BlueIn(fresh, area);
                Assert.True(Math.Abs(onTheScreen - wholeAgain) <= wholeAgain * 0.15, $"{onTheScreen} pixels of the marks on the screen, {wholeAgain} when the window draws all of itself");
            }

            // Typing in a line paints that line again, with its marks
            editor.Box.Select(editor.Box.TextLength, 0);
            editor.Box.SelectedText = " more words";
            editor.Box.Update();
            await Task.Delay(150);
            using var typed = OnTheScreen(editor.Box);
            using var again = Photograph(editor).Picture;
            var shown = BlueIn(typed, area);
            var expected = BlueIn(again, area);
            Assert.True(Math.Abs(shown - expected) <= expected * 0.15, $"{shown} pixels of the marks on the screen, {expected} when the window draws all of itself");
        });
    }

    [Fact]
    public void ABlankBox_ShowsTheMarkOfItsOneParagraph()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            await Task.Delay(100);
            var without = Photograph(editor).Marks;

            editor.ShowAllCharacters = true;
            await Task.Delay(100);

            Assert.True(Photograph(editor).Marks > without + 5);
        });
    }

    [Fact]
    public void TheMarksOfAListAndAPicture_AreOnTheItemsAndNotOnThePicture()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.Rtf = RtfWriter.Write(new RichDocument([
                new RichParagraph(TextAlign.Left, [new RichRun("a b", default)]),
                RichParagraph.OfPicture(Picture()),
                new RichParagraph(TextAlign.Left, [new RichRun("item", default)]) { LeftIndent = 36, FirstLineIndent = -18, Marker = "•" },
            ]));
            editor.ShowAllCharacters = true;
            await Task.Delay(150);

            var (picture, marks) = Photograph(editor);
            picture.Save(Path.Combine(Path.GetTempPath(), "marks-list.png"));

            Assert.True(marks > 20);
        });
    }

    [Fact]
    public void ABoxThatCannotBeEdited_DoesNotThrow_WhenItIsPaintedWithMarks()
    {
        RichDescriptionEditor editor = null!;
        Ui.Run(() => Host(out editor), async form =>
        {
            editor.Box.Text = new string(' ', 20000); // more spaces than the painting draws
            editor.ShowAllCharacters = true;
            editor.SetEditable(false);
            await Task.Delay(150);

            Assert.True(Photograph(editor).Marks > 0);
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
