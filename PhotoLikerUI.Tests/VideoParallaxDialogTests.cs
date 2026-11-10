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
    public void WhatTheSavedSettingsPutIn_IsNotAskedAbout()
    {
        MakePhotos();
        File.WriteAllText(_liked.File("photo-liker-video.json"), "{ \"DepthParallax\": true }");
        var asked = 0;

        Ui.Run(() => Create([], () => { asked++; return Task.FromResult(true); }), async dialog =>
        {
            var box = ParallaxBox(dialog);
            Assert.True(await Ui.Until(() => box.Checked, 20000), "the box is put back as it was");
            await Task.Delay(200);
            Assert.Equal(0, asked); // the model is asked for when a video is made, or when the person ticks it, not when the window opens
        });
    }

    [Fact]
    public void AVideoWithTheBoxTicked_IsHandedTheOriginalPhotos_AndTheChoiceIsKeptForNextTime()
    {
        MakePhotos();
        var made = new List<Captured>();

        Ui.Run(() => Create(made, () => Task.FromResult(true)), async dialog =>
        {
            await WaitUntilReady(dialog);
            Click(ParallaxBox(dialog));
            Ui.ButtonStartingWith(dialog, VideoStrings.GenerateButton).PerformClick();
            Assert.True(await Ui.Until(() => made.Count == 1, 30000), "the video is made");
        });

        var (options, sequence) = made.Single();
        Assert.True(options.DepthParallax);
        Assert.Equal(3, sequence.Files.Count);
        Assert.All(sequence.Files, file => Assert.Equal(_liked.Path, Path.GetDirectoryName(file))); // the photos themselves: nothing was copied for ffmpeg
        Assert.Contains("\"DepthParallax\": true", File.ReadAllText(_liked.File("photo-liker-video.json")));

        // and the next window of the folder has it ticked
        Ui.Run(() => Create([], () => Task.FromResult(true)), async dialog =>
        {
            Assert.True(await Ui.Until(() => ParallaxBox(dialog).Checked, 20000));
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
