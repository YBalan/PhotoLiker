using System.Drawing.Imaging;
using PhotoLiker.Core;
using PhotoLiker.GenerateVideo;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The Effect box of the Generate Video window: what it offers, that it asks for the depth model when an effect that needs it is chosen, that it is kept with the folder of the photos (each folder its own),
/// and what the window hands to the code that makes the video. Nothing here needs a model or ffmpeg: the code that asks for the model and makes the video is the test's own.
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class VideoEffectDialogTests : IDisposable
{
    private readonly TempFolder _liked = new();
    private readonly TempFolder _other = new();

    public void Dispose()
    {
        _liked.Dispose();
        _other.Dispose();
    }

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

    private VideoGenerationDialog Create(List<Captured> made, Func<Task<bool>>? ensure, string? currentFolder = null) =>
        new(currentFolder ?? _liked.Path, [], _liked.Path, [".jpg"], PhotoSortOrder.OldestFirst, dark: false,
            (options, sequence, _, _) =>
            {
                made.Add(new Captured(options, sequence));
                File.WriteAllText(options.OutputPath, "video"); // the dialog looks for what was made
                return Task.CompletedTask;
            },
            ensure);

    /// <summary>What a person does to a box: picks an item (the box is told, as it is by a click on the list; setting the index alone tells nobody).</summary>
    private static void Choose(ComboBox box, SlideshowEffect effect)
    {
        box.SelectedIndex = box.Items.Cast<object>().ToList().FindIndex(item => item.ToString() == effect.ToString());
        typeof(ComboBox).GetMethod("OnSelectionChangeCommitted", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(box, [EventArgs.Empty]);
    }

    private static ComboBox EffectBox(Form dialog) =>
        Ui.Controls<ComboBox>(dialog).Single(box => box.Items.Cast<object>().Any(item => item.ToString() == nameof(SlideshowEffect.KenBurns)));

    private static ComboBox SourceBox(Form dialog) =>
        Ui.Controls<ComboBox>(dialog).Single(box => box.Items.Count > 0 && box.Items[0]?.ToString()?.StartsWith(VideoStrings.SourceCurrent) == true);

    private static string Shown(ComboBox box) => box.SelectedItem?.ToString() ?? string.Empty;

    private static ComboBox CodecBox(Form dialog) => Ui.Controls<ComboBox>(dialog).Single(box => box.Items.Contains("libx265"));

    /// <summary>The quality box (CRF, 0 to 51): a value that a saved file gave it shows that the saved settings have been put in.</summary>
    private static NumericUpDown CrfBox(Form dialog) => Ui.Controls<NumericUpDown>(dialog).Single(box => box.Maximum == 51);

    private static string Log(Form dialog) => Ui.Controls<TextBox>(dialog).Single(text => text.Multiline && text.ReadOnly).Text;

    private static async Task WaitUntilReady(Form dialog)
    {
        var generate = Ui.ButtonStartingWith(dialog, VideoStrings.GenerateButton);
        Assert.True(await Ui.Until(() => generate.Enabled, 20000), "the photos of the folder are listed and the button is on");
    }

    private void SaveSettings(string folder, string json) => File.WriteAllText(Path.Combine(folder, "photo-liker-video.json"), json);

    // ── the box ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheBox_IsThere_StartsOnNone_AndOffersNone_Random_AndEveryOtherEffect_InThatOrder_WithItsOwnRow()
    {
        MakePhotos();

        Ui.Run(() => Create([], ensure: null), async dialog =>
        {
            var box = EffectBox(dialog);
            Assert.Equal(nameof(SlideshowEffect.None), Shown(box));
            Assert.True(box.Enabled);
            Assert.Equal(ComboBoxStyle.DropDownList, box.DropDownStyle); // an effect of the list, not a word typed
            Assert.Contains(Ui.Controls<Label>(dialog), label => label.Text == VideoStrings.LabelEffect);

            var shown = box.Items.Cast<object>().Select(item => item.ToString()).ToList();
            Assert.Equal([nameof(SlideshowEffect.None), nameof(SlideshowEffect.Random)], shown.Take(2));
            Assert.Equal(Enum.GetNames<SlideshowEffect>().Order(), shown.Order());   // every effect of the slideshow, once
            Assert.DoesNotContain(Ui.Controls<CheckBox>(dialog), check => check.Text.Contains("parallax", StringComparison.OrdinalIgnoreCase)); // the box that was here is gone
            await Task.CompletedTask;
        });
    }

    // ── the model of the effects that need it ───────────────────────────────

    [Theory]
    [InlineData(SlideshowEffect.Parallax)]
    [InlineData(SlideshowEffect.Portrait)]
    [InlineData(SlideshowEffect.DollyZoom)]
    public void ChoosingAnEffectThatNeedsTheModel_AsksForIt_AndTheBoxGoesBackWhenItIsRefused(SlideshowEffect effect)
    {
        MakePhotos();
        var asked = 0;
        var answers = new Queue<bool>([false, true]);

        Ui.Run(() => Create([], async () => { asked++; await Task.Delay(30); return answers.Dequeue(); }), async dialog =>
        {
            var box = EffectBox(dialog);

            Choose(box, effect);
            Assert.True(await Ui.Until(() => asked == 1 && box.Enabled), "the question was asked and answered");
            Assert.Equal(nameof(SlideshowEffect.None), Shown(box)); // no model: back to what it was

            Choose(box, effect);
            Assert.True(await Ui.Until(() => asked == 2 && box.Enabled));
            Assert.Equal(effect.ToString(), Shown(box));

            Choose(box, SlideshowEffect.Wipe); // an effect that needs none asks nothing
            await Task.Delay(100);
            Assert.Equal(nameof(SlideshowEffect.Wipe), Shown(box));
            Assert.Equal(2, asked);
        });
    }

    [Fact]
    public void WhenTheModelIsRefused_TheBoxGoesBackToTheEffectItHad_NotAlwaysToNone()
    {
        MakePhotos();

        Ui.Run(() => Create([], () => Task.FromResult(false)), async dialog =>
        {
            var box = EffectBox(dialog);
            Choose(box, SlideshowEffect.Cube);
            Choose(box, SlideshowEffect.Parallax);
            Assert.True(await Ui.Until(() => box.Enabled));

            Assert.Equal(nameof(SlideshowEffect.Cube), Shown(box));
        });
    }

    [Theory]
    [InlineData(SlideshowEffect.None)]
    [InlineData(SlideshowEffect.Random)]
    [InlineData(SlideshowEffect.KenBurns)]
    [InlineData(SlideshowEffect.Tilt)]
    [InlineData(SlideshowEffect.Fade)]
    [InlineData(SlideshowEffect.Ripple)]
    [InlineData(SlideshowEffect.DipToWhite)]
    public void ChoosingAnEffectThatNeedsNoModel_AsksForNothing(SlideshowEffect effect)
    {
        MakePhotos();
        var asked = 0;

        Ui.Run(() => Create([], () => { asked++; return Task.FromResult(true); }), async dialog =>
        {
            var box = EffectBox(dialog);
            Choose(box, SlideshowEffect.Wipe); // away from the first one, so that choosing None is a choice too
            Choose(box, effect);
            await Task.Delay(100);

            Assert.Equal(effect.ToString(), Shown(box));
            Assert.Equal(0, asked);
        });
    }

    [Fact]
    public void WhenGettingTheModelThrows_TheBoxGoesBack_TheLogSaysWhy_AndNothingEscapesTheChoice()
    {
        MakePhotos();
        var asked = 0;

        // An exception that nobody thought of (not a download error): it must not leave the handler, which has nobody to catch it
        Ui.Run(() => Create([], () => { asked++; throw new InvalidOperationException("the model folder is gone"); }), async dialog =>
        {
            var box = EffectBox(dialog);

            Choose(box, SlideshowEffect.Parallax);
            Assert.True(await Ui.Until(() => asked == 1 && box.Enabled, 10000), "the question was asked, and the box is usable again");
            Assert.Equal(nameof(SlideshowEffect.None), Shown(box));
            Assert.Contains(string.Format(VideoStrings.LogEffectModelFailedFormat, "the model folder is gone"), Log(dialog));

            // and it can be tried again
            Choose(box, SlideshowEffect.Portrait);
            Assert.True(await Ui.Until(() => asked == 2 && box.Enabled, 10000));
            Assert.Equal(nameof(SlideshowEffect.None), Shown(box));
        });
    }

    [Fact]
    public void WhenGettingTheModelIsCancelled_TheBoxGoesBack_WithNothingSaid()
    {
        MakePhotos();

        Ui.Run(() => Create([], () => throw new OperationCanceledException()), async dialog =>
        {
            var box = EffectBox(dialog);

            Choose(box, SlideshowEffect.DollyZoom);
            Assert.True(await Ui.Until(() => box.Enabled, 10000));
            Assert.Equal(nameof(SlideshowEffect.None), Shown(box));
            Assert.DoesNotContain(VideoStrings.LogEffectModelFailedFormat.Split('{')[0], Log(dialog));
        });
    }

    // ── what is kept with the folder ────────────────────────────────────────

    [Fact]
    public void ADepthParallaxThatAnOldFileSaved_IsNotTheEffect_TheBoxStartsOnNone_AndTheModelIsNotAskedFor()
    {
        MakePhotos();
        SaveSettings(_liked.Path, "{ \"DepthParallax\": true, \"Crf\": 31 }");
        var asked = 0;

        Ui.Run(() => Create([], () => { asked++; return Task.FromResult(true); }), async dialog =>
        {
            Assert.True(await Ui.Until(() => CrfBox(dialog).Value == 31, 20000), "the rest of the saved settings is put in");
            await Task.Delay(200);
            Assert.Equal(nameof(SlideshowEffect.None), Shown(EffectBox(dialog)));
            Assert.Equal(0, asked);
        });
    }

    [Theory]
    [InlineData(SlideshowEffect.Wipe)]
    [InlineData(SlideshowEffect.Random)]
    [InlineData(SlideshowEffect.KenBurns)]
    [InlineData(SlideshowEffect.Parallax)]
    public void TheEffectTheFolderSaved_IsPutBack_AndTheModelIsNotAskedFor_WhenTheWindowOpens(SlideshowEffect saved)
    {
        MakePhotos();
        SaveSettings(_liked.Path, $"{{ \"Effect\": \"{saved}\", \"Crf\": 31 }}");
        var asked = 0;

        Ui.Run(() => Create([], () => { asked++; return Task.FromResult(true); }), async dialog =>
        {
            Assert.True(await Ui.Until(() => CrfBox(dialog).Value == 31, 20000), "the saved settings are put in");
            Assert.Equal(saved.ToString(), Shown(EffectBox(dialog)));
            await Task.Delay(200);
            Assert.Equal(0, asked); // the model is asked for when an effect is chosen, or when a video is made, not when the window opens
        });
    }

    [Theory]
    [InlineData("Sideways")]
    [InlineData("99")]
    [InlineData("")]
    public void AnEffectThatIsNoEffectOfThisVersion_IsNone(string name)
    {
        MakePhotos();
        SaveSettings(_liked.Path, $"{{ \"Effect\": \"{name}\", \"Crf\": 31 }}");

        Ui.Run(() => Create([], ensure: null), async dialog =>
        {
            Assert.True(await Ui.Until(() => CrfBox(dialog).Value == 31, 20000));
            Assert.Equal(nameof(SlideshowEffect.None), Shown(EffectBox(dialog)));
        });
    }

    [Fact]
    public void AVideoWithAnEffect_IsHandedTheOriginalPhotos_AndTheEffectIsKeptForTheNextWindowOfTheFolder()
    {
        MakePhotos();
        var made = new List<Captured>();

        Ui.Run(() => Create(made, () => Task.FromResult(true)), async dialog =>
        {
            await WaitUntilReady(dialog);
            Choose(EffectBox(dialog), SlideshowEffect.Wipe);
            Ui.ButtonStartingWith(dialog, VideoStrings.GenerateButton).PerformClick();
            Assert.True(await Ui.Until(() => made.Count == 1, 30000), "the video is made");
        });

        var (options, sequence) = made.Single();
        Assert.Equal(SlideshowEffect.Wipe, options.Effect);
        Assert.Equal(3, sequence.Files.Count);
        Assert.All(sequence.Files, file => Assert.Equal(_liked.Path, Path.GetDirectoryName(file))); // the photos themselves: nothing was copied for ffmpeg
        var saved = File.ReadAllText(_liked.File("photo-liker-video.json"));
        Assert.Contains("\"Effect\": \"Wipe\"", saved);
        Assert.DoesNotContain("DepthParallax", saved);

        // and the next window of the folder starts on it
        Ui.Run(() => Create([], () => Task.FromResult(true)), async dialog =>
        {
            Assert.True(await Ui.Until(() => Shown(EffectBox(dialog)) == nameof(SlideshowEffect.Wipe), 20000), "the saved effect is put in");
        });
    }

    [Fact]
    public void ANoneThatWasChosen_IsKeptToo_SoTheNextWindowIsNotGivenTheEffectOfAnEarlierVideo()
    {
        MakePhotos();
        SaveSettings(_liked.Path, "{ \"Effect\": \"Wipe\", \"Crf\": 30 }");
        var made = new List<Captured>();

        Ui.Run(() => Create(made, ensure: null), async dialog =>
        {
            Assert.True(await Ui.Until(() => Shown(EffectBox(dialog)) == nameof(SlideshowEffect.Wipe), 20000));
            await WaitUntilReady(dialog);
            Choose(EffectBox(dialog), SlideshowEffect.None);
            Ui.ButtonStartingWith(dialog, VideoStrings.GenerateButton).PerformClick();
            Assert.True(await Ui.Until(() => made.Count == 1, 30000));
        });

        Assert.Equal(SlideshowEffect.None, made.Single().Options.Effect);
        Assert.Contains("\"Effect\": \"None\"", File.ReadAllText(_liked.File("photo-liker-video.json")));

        Ui.Run(() => Create([], ensure: null), async dialog =>
        {
            Assert.True(await Ui.Until(() => CrfBox(dialog).Value == 30, 20000));
            Assert.Equal(nameof(SlideshowEffect.None), Shown(EffectBox(dialog)));
        });
    }

    [Fact]
    public void EachSource_HasItsOwnEffect_AndOneThatHasNoneSavedStartsOnNone()
    {
        MakePhotos();
        SaveSettings(_liked.Path, "{ \"Effect\": \"Wipe\" }");
        SaveSettings(_other.Path, "{ \"Effect\": \"Iris\" }");

        // The loaded photos go to the folder they are in (the first folder), the liked folder is the second
        Ui.Run(() => Create([], ensure: null, currentFolder: _other.Path), async dialog =>
        {
            var effect = EffectBox(dialog);
            var sources = SourceBox(dialog);
            Assert.True(await Ui.Until(() => Shown(effect) == nameof(SlideshowEffect.Wipe), 20000), "the liked folder is the source that is picked first");

            sources.SelectedIndex = 0; // the loaded photos, whose folder is the other one
            Assert.True(await Ui.Until(() => Shown(effect) == nameof(SlideshowEffect.Iris), 20000), "the effect of the other folder");

            sources.SelectedIndex = 1;
            Assert.True(await Ui.Until(() => Shown(effect) == nameof(SlideshowEffect.Wipe), 20000), "and back");

            // A folder that has nothing saved is not given the effect of the folder before it
            File.Delete(Path.Combine(_other.Path, "photo-liker-video.json"));
            sources.SelectedIndex = 0;
            Assert.True(await Ui.Until(() => Shown(effect) == nameof(SlideshowEffect.None), 20000), "no settings: none");
        });
    }

    // ── what the video is made with ─────────────────────────────────────────

    [Fact]
    public void AModelThatIsGoneWhenTheVideoIsMade_LeavesTheEffectOut_AndSaysSo()
    {
        MakePhotos();
        SaveSettings(_liked.Path, "{ \"Effect\": \"Parallax\" }"); // put back by the saved settings, then the model was deleted
        var made = new List<Captured>();

        Ui.Run(() => Create(made, () => Task.FromResult(false)), async dialog =>
        {
            await WaitUntilReady(dialog);
            Assert.True(await Ui.Until(() => Shown(EffectBox(dialog)) == nameof(SlideshowEffect.Parallax), 20000));
            Ui.ButtonStartingWith(dialog, VideoStrings.GenerateButton).PerformClick();
            Assert.True(await Ui.Until(() => made.Count == 1, 30000));

            Assert.Contains(VideoStrings.LogEffectNoModel, Log(dialog));
        });

        Assert.Equal(SlideshowEffect.None, made.Single().Options.Effect);
    }

    [Fact]
    public void AnEffectThatNeedsNoModel_IsMadeWithoutAskingForOne_WhateverTheAnswerWouldBe()
    {
        MakePhotos();
        var asked = 0;
        var made = new List<Captured>();

        Ui.Run(() => Create(made, () => { asked++; return Task.FromResult(false); }), async dialog =>
        {
            await WaitUntilReady(dialog);
            Choose(EffectBox(dialog), SlideshowEffect.CrossZoom);
            Ui.ButtonStartingWith(dialog, VideoStrings.GenerateButton).PerformClick();
            Assert.True(await Ui.Until(() => made.Count == 1, 30000));

            Assert.DoesNotContain(VideoStrings.LogEffectNoModel, Log(dialog));
            Assert.Contains(string.Format(VideoStrings.LogEffectOnFormat, SlideshowEffect.CrossZoom), Log(dialog)); // the log says what is made
        });

        Assert.Equal(SlideshowEffect.CrossZoom, made.Single().Options.Effect);
        Assert.Equal(0, asked);
    }

    [Fact]
    public void AVideoWithAnEffectThatNeedsTheModel_AsksForItAgainWhenItIsMade_AndIsMadeWithTheEffectWhenItIsThere()
    {
        MakePhotos();
        var asked = 0;
        var made = new List<Captured>();

        Ui.Run(() => Create(made, () => { asked++; return Task.FromResult(true); }), async dialog =>
        {
            await WaitUntilReady(dialog);
            Choose(EffectBox(dialog), SlideshowEffect.DollyZoom);
            Assert.True(await Ui.Until(() => asked == 1 && EffectBox(dialog).Enabled));
            Ui.ButtonStartingWith(dialog, VideoStrings.GenerateButton).PerformClick();
            Assert.True(await Ui.Until(() => made.Count == 1, 30000));
        });

        Assert.Equal(SlideshowEffect.DollyZoom, made.Single().Options.Effect);
        Assert.Equal(2, asked); // when it was chosen, and when the video was made
    }

    [Fact]
    public void AVideoWithoutAnEffect_IsAsItWas_AndAsksForNoModel()
    {
        MakePhotos();
        var asked = 0;
        var made = new List<Captured>();

        Ui.Run(() => Create(made, () => { asked++; return Task.FromResult(true); }), async dialog =>
        {
            await WaitUntilReady(dialog);
            Ui.ButtonStartingWith(dialog, VideoStrings.GenerateButton).PerformClick();
            Assert.True(await Ui.Until(() => made.Count == 1, 30000));
            Assert.DoesNotContain(VideoStrings.LogEffectOnFormat.Split('{')[0], Log(dialog));
        });

        Assert.Equal(SlideshowEffect.None, made.Single().Options.Effect);
        Assert.Equal(3, made.Single().Sequence.Files.Count);
        Assert.Equal(0, asked);
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
        SaveSettings(_liked.Path, "{ \"Codec\": \"libx264\" }");
        Ui.Run(() => Create([], ensure: null), async dialog =>
        {
            Assert.True(await Ui.Until(() => (string?)CodecBox(dialog).SelectedItem == "libx264", 20000));
            Assert.Equal(23m, CrfBox(dialog).Value);
        });

        // Both saved: both come back as they were, also a quality that is not the usual one of the codec
        SaveSettings(_liked.Path, "{ \"Codec\": \"libx265\", \"Crf\": 23 }");
        Ui.Run(() => Create([], ensure: null), async dialog =>
        {
            Assert.True(await Ui.Until(() => CrfBox(dialog).Value == 23, 20000), "the saved quality");
            Assert.Equal("libx265", CodecBox(dialog).SelectedItem);
        });

        // Saved as a quality that is the usual one of the saved codec (every video of the old version): it still follows, when the codec is changed in this window
        SaveSettings(_liked.Path, "{ \"Codec\": \"libx264\", \"Crf\": 23 }");
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
        SaveSettings(_liked.Path, "{ \"Codec\": \"libx264\" }");

        Ui.Run(() => Create([], ensure: null), async dialog =>
        {
            Assert.True(await Ui.Until(() => (string?)CodecBox(dialog).SelectedItem == "libx264", 20000), "the codec of the last video of the folder");
        });
    }
}
