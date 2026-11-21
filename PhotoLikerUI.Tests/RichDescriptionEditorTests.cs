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
}
