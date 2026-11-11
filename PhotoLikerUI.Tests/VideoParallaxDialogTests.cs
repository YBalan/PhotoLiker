using System.Drawing.Imaging;
using PhotoLiker.Core;
using PhotoLiker.GenerateVideo;

namespace PhotoLikerUI.Tests;

/// <summary>The "Depth parallax" box of the Generate Video window: it asks for the model when it is ticked, what it makes of the settings, and what the window hands to the code that makes the video.</summary>
[Collection(WindowsCollection.Name)]
public sealed class VideoParallaxDialogTests : IDisposable
{
    private readonly TempFolder _liked = new();

    public void Dispose() => _liked.Dispose();

    private void MakePhotos()
    {
        for (var i = 0; i < 3; i++)
        {
            using var bitmap = new Bitmap(160, 100);
            using (var g = Graphics.FromImage(bitmap))
                g.Clear(Color.FromArgb(40 + i * 60, 90, 200 - i * 50));
            bitmap.Save(_liked.File($"photo{i}.jpg"), ImageFormat.Jpeg);
        }
    }

    private sealed record Captured(VideoGenerationOptions Options, VideoSequence Sequence);

    private VideoGenerationDialog Create(List<Captured> made, Func<Task<bool>>? ensure) =>
        new(_liked.Path, [], _liked.Path, [".jpg"], PhotoSortOrder.OldestFirst, dark: false,
            (options, sequence, _, _) =>
            {
                made.Add(new Captured(options, sequence));
                File.WriteAllText(options.OutputPath, "video"); // the dialog looks for what was made
                return Task.CompletedTask;
            },
            ensure);

    /// <summary>What a click of the mouse does to a box: it flips and raises Click (a CheckBox has no PerformClick; its OnClick does both).</summary>
    private static void Click(CheckBox box) =>
        typeof(Control).GetMethod("OnClick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(box, [EventArgs.Empty]);

    private static CheckBox ParallaxBox(Form dialog) => Ui.Controls<CheckBox>(dialog).Single(box => box.Text == VideoStrings.DepthParallax);

    private static ComboBox CodecBox(Form dialog) => Ui.Controls<ComboBox>(dialog).Single(box => box.Items.Contains("libx265"));

    /// <summary>The quality box (CRF, 0 to 51): a value that a saved file gave it shows that the saved settings have been put in.</summary>
    private static NumericUpDown CrfBox(Form dialog) => Ui.Controls<NumericUpDown>(dialog).Single(box => box.Maximum == 51);

    private static async Task WaitUntilReady(Form dialog)
    {
        var generate = Ui.ButtonStartingWith(dialog, VideoStrings.GenerateButton);
        Assert.True(await Ui.Until(() => generate.Enabled, 20000), "the photos of the folder are listed and the button is on");
    }

    [Fact]
    public void TheBox_IsThere_AndUntickedAtFirst_WithItsOwnRow()
    {
        MakePhotos();
        var made = new List<Captured>();

        Ui.Run(() => Create(made, ensure: null), async dialog =>
        {
            var box = ParallaxBox(dialog);
            Assert.False(box.Checked);
            Assert.True(box.Enabled);
            Assert.Contains(Ui.Controls<Label>(dialog), label => label.Text == VideoStrings.LabelMotion);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void Ticking_AsksForTheModel_AndTheBoxStaysUnticked_WhenItIsRefused()
    {
        MakePhotos();
        var asked = 0;
        var answers = new Queue<bool>([false, true]);

        Ui.Run(() => Create([], async () => { asked++; await Task.Delay(30); return answers.Dequeue(); }), async dialog =>
        {
            var box = ParallaxBox(dialog);

            Click(box);
            Assert.True(await Ui.Until(() => asked == 1 && box.Enabled), "the question was asked and answered");
            Assert.False(box.Checked); // no model: not ticked

            Click(box);
            Assert.True(await Ui.Until(() => asked == 2 && box.Enabled));
            Assert.True(box.Checked);

            Click(box); // unticking asks nothing
            await Task.Delay(100);
            Assert.False(box.Checked);
            Assert.Equal(2, asked);
        });
    }

    [Fact]
    public void WhenGettingTheModelThrows_TheBoxGoesBackToUnticked_TheLogSaysWhy_AndNothingEscapesTheClick()
    {
        MakePhotos();
        var asked = 0;

        // An exception that nobody thought of (not a download error): it must not leave the click, which has nobody to catch it
        Ui.Run(() => Create([], () => { asked++; throw new InvalidOperationException("the model folder is gone"); }), async dialog =>
        {
            var box = ParallaxBox(dialog);

            Click(box);
            Assert.True(await Ui.Until(() => asked == 1 && box.Enabled, 10000), "the question was asked, and the box is usable again");
            Assert.False(box.Checked, "no model: not ticked");

            var log = Ui.Controls<TextBox>(dialog).Single(text => text.Multiline && text.ReadOnly);
            Assert.Contains(string.Format(VideoStrings.LogParallaxModelFailedFormat, "the model folder is gone"), log.Text);

            // and it can be tried again
            Click(box);
            Assert.True(await Ui.Until(() => asked == 2 && box.Enabled, 10000));
            Assert.False(box.Checked);
        });
    }

    [Fact]
    public void WhenGettingTheModelIsCancelled_TheBoxGoesBackToUnticked_WithNothingSaid()
    {
        MakePhotos();

        Ui.Run(() => Create([], () => throw new OperationCanceledException()), async dialog =>
        {
            var box = ParallaxBox(dialog);

            Click(box);
            Assert.True(await Ui.Until(() => box.Enabled, 10000));
            Assert.False(box.Checked);
            var log = Ui.Controls<TextBox>(dialog).Single(text => text.Multiline && text.ReadOnly);
            Assert.DoesNotContain(VideoStrings.LogParallaxModelFailedFormat.Split('{')[0], log.Text);
        });
    }

    [Fact]
    public void AParallaxThatAnOldFileSaved_IsNotPutBack_TheBoxAlwaysStartsUnticked_AndTheModelIsNotAskedFor()
    {
        MakePhotos();
        File.WriteAllText(_liked.File("photo-liker-video.json"), "{ \"DepthParallax\": true, \"Crf\": 31 }");
        var asked = 0;

        Ui.Run(() => Create([], () => { asked++; return Task.FromResult(true); }), async dialog =>
        {
            Assert.True(await Ui.Until(() => CrfBox(dialog).Value == 31, 20000), "the rest of the saved settings is put in");
            await Task.Delay(200);
            Assert.False(ParallaxBox(dialog).Checked);
            Assert.Equal(0, asked); // the model is asked for when a video is made, or when the person ticks it, not when the window opens
        });
    }

    [Fact]
    public void AVideoWithTheBoxTicked_IsHandedTheOriginalPhotos_AndTheChoiceIsNotKeptForNextTime()
    {
        MakePhotos();
        var made = new List<Captured>();

        Ui.Run(() => Create(made, () => Task.FromResult(true)), async dialog =>
        {
            await WaitUntilReady(dialog);
            CrfBox(dialog).Value = 31; // something that is kept, to know when the next window has what was saved
            Click(ParallaxBox(dialog));
            Ui.ButtonStartingWith(dialog, VideoStrings.GenerateButton).PerformClick();
            Assert.True(await Ui.Until(() => made.Count == 1, 30000), "the video is made");
        });

        var (options, sequence) = made.Single();
        Assert.True(options.DepthParallax);
        Assert.Equal(3, sequence.Files.Count);
        Assert.All(sequence.Files, file => Assert.Equal(_liked.Path, Path.GetDirectoryName(file))); // the photos themselves: nothing was copied for ffmpeg
        var saved = File.ReadAllText(_liked.File("photo-liker-video.json"));
        Assert.Contains("\"Crf\": 31", saved);
        Assert.DoesNotContain("DepthParallax", saved);

        // and the next window of the folder has what was kept, but not this: it starts unticked
        Ui.Run(() => Create([], () => Task.FromResult(true)), async dialog =>
        {
            Assert.True(await Ui.Until(() => CrfBox(dialog).Value == 31, 20000), "the saved settings are put in");
            await Task.Delay(200);
            Assert.False(ParallaxBox(dialog).Checked);
        });
    }

    // ── the codec ──────────────────────────────────────────────────────

    [Fact]
    public void TheCodec_IsLibx265_AtFirst_AndIsWhatAVideoIsMadeWith()
    {
        MakePhotos();
        var made = new List<Captured>();

        Ui.Run(() => Create(made, ensure: null), async dialog =>
        {
            Assert.Equal("libx265", CodecBox(dialog).SelectedItem);
            Assert.Equal(["libx264", "libx265"], CodecBox(dialog).Items.Cast<string>().ToArray()); // the other one is still there
            await WaitUntilReady(dialog);
            Ui.ButtonStartingWith(dialog, VideoStrings.GenerateButton).PerformClick();
            Assert.True(await Ui.Until(() => made.Count == 1, 30000));
        });

        Assert.Equal("libx265", made.Single().Options.VideoCodec);
    }

    [Fact]
    public void TheQuality_StartsAtTheUsualOneOfTheCodec_AndFollowsTheCodecUntilItIsSetByHand()
    {
        MakePhotos();
        var made = new List<Captured>();

        Ui.Run(() => Create(made, ensure: null), async dialog =>
        {
            var codec = CodecBox(dialog);
            var crf = CrfBox(dialog);
            Assert.Equal(28m, crf.Value); // libx265 at first

            codec.SelectedItem = "libx264";
            Assert.Equal(23m, crf.Value);
            codec.SelectedItem = "libx265";
            Assert.Equal(28m, crf.Value);

            crf.Value = 20; // set by hand: it stays when the codec changes
            codec.SelectedItem = "libx264";
            Assert.Equal(20m, crf.Value);
            codec.SelectedItem = "libx265";
            Assert.Equal(20m, crf.Value);

            crf.Value = 28; // and a hand that sets it to the usual one of the codec it is on leaves it following
            codec.SelectedItem = "libx264";
            Assert.Equal(23m, crf.Value);

            await WaitUntilReady(dialog);
            Ui.ButtonStartingWith(dialog, VideoStrings.GenerateButton).PerformClick();
            Assert.True(await Ui.Until(() => made.Count == 1, 30000));
        });

        Assert.Equal(("libx264", 23), (made.Single().Options.VideoCodec, made.Single().Options.Crf));
    }

    [Fact]
    public void WhatAFolderSaved_IsPutBack_AndAQualityThatWasNeverSet_FollowsTheSavedCodec()
    {
        MakePhotos();

        // A codec with no quality: the quality is the usual one of that codec
        File.WriteAllText(_liked.File("photo-liker-video.json"), "{ \"Codec\": \"libx264\" }");
        Ui.Run(() => Create([], ensure: null), async dialog =>
        {
            Assert.True(await Ui.Until(() => (string?)CodecBox(dialog).SelectedItem == "libx264", 20000));
            Assert.Equal(23m, CrfBox(dialog).Value);
        });

        // Both saved: both come back as they were, also a quality that is not the usual one of the codec
        File.WriteAllText(_liked.File("photo-liker-video.json"), "{ \"Codec\": \"libx265\", \"Crf\": 23 }");
        Ui.Run(() => Create([], ensure: null), async dialog =>
        {
            Assert.True(await Ui.Until(() => CrfBox(dialog).Value == 23, 20000), "the saved quality");
            Assert.Equal("libx265", CodecBox(dialog).SelectedItem);
        });

        // Saved as a quality that is the usual one of the saved codec (every video of the old version): it still follows, when the codec is changed in this window
        File.WriteAllText(_liked.File("photo-liker-video.json"), "{ \"Codec\": \"libx264\", \"Crf\": 23 }");
        Ui.Run(() => Create([], ensure: null), async dialog =>
        {
            Assert.True(await Ui.Until(() => (string?)CodecBox(dialog).SelectedItem == "libx264", 20000));
            Assert.Equal(23m, CrfBox(dialog).Value);
            CodecBox(dialog).SelectedItem = "libx265";
            Assert.Equal(28m, CrfBox(dialog).Value);
        });
    }

    [Fact]
    public void TheCodecAFolderSaved_IsPutBack_ItIsOnlyTheFirstValue_ThatIsLibx265()
    {
        MakePhotos();
        File.WriteAllText(_liked.File("photo-liker-video.json"), "{ \"Codec\": \"libx264\" }");

        Ui.Run(() => Create([], ensure: null), async dialog =>
        {
            Assert.True(await Ui.Until(() => (string?)CodecBox(dialog).SelectedItem == "libx264", 20000), "the codec of the last video of the folder");
        });
    }

    [Fact]
    public void AModelThatIsGoneWhenTheVideoIsMade_LeavesTheParallaxOut_AndSaysSo()
    {
        MakePhotos();
        var made = new List<Captured>();

        Ui.Run(() => Create(made, () => Task.FromResult(false)), async dialog =>
        {
            await WaitUntilReady(dialog);
            var box = ParallaxBox(dialog);
            box.Checked = true; // as if put back by the saved settings, then the model was deleted
            Ui.ButtonStartingWith(dialog, VideoStrings.GenerateButton).PerformClick();
            Assert.True(await Ui.Until(() => made.Count == 1, 30000));

            var log = Ui.Controls<TextBox>(dialog).Single(text => text.Multiline && text.ReadOnly).Text;
            Assert.Contains(VideoStrings.LogParallaxNoModel, log);
        });

        Assert.False(made.Single().Options.DepthParallax);
    }

    [Fact]
    public void AVideoWithoutTheBox_IsAsItWas()
    {
        MakePhotos();
        var made = new List<Captured>();

        Ui.Run(() => Create(made, () => Task.FromResult(true)), async dialog =>
        {
            await WaitUntilReady(dialog);
            Ui.ButtonStartingWith(dialog, VideoStrings.GenerateButton).PerformClick();
            Assert.True(await Ui.Until(() => made.Count == 1, 30000));
        });

        Assert.False(made.Single().Options.DepthParallax);
        Assert.Equal(3, made.Single().Sequence.Files.Count);
    }
}
