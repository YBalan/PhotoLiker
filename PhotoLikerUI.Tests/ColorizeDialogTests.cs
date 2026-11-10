using System.Windows.Media;
using System.Windows.Media.Imaging;
using PhotoLiker.Effects.Colorize;
using PhotoLiker.Effects.Models;
using PhotoLiker.Search;

namespace PhotoLikerUI.Tests;

/// <summary>The Colorize window opened for real: which photos it offers, what it ticks, what it does when the model is refused or missing, and the copies a run makes (with the real model where there is one).</summary>
[Collection(WindowsCollection.Name)]
public sealed class ColorizeDialogTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly TempFolder _emptyModels = new();

    public void Dispose()
    {
        _folder.Dispose();
        _emptyModels.Dispose();
    }

    private static string ModelsFolder =>
        Environment.GetEnvironmentVariable("PHOTOLIKER_TEST_MODELS") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoLiker", "models");

    private string Jpeg(string name, Func<int, int, (byte B, byte G, byte R)> colour)
    {
        var pixels = new byte[160 * 120 * 3];
        for (var y = 0; y < 120; y++)
        for (var x = 0; x < 160; x++)
        {
            var (b, g, r) = colour(x, y);
            var i = (y * 160 + x) * 3;
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
        }
        var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(160, 120, 96, 96, PixelFormats.Bgr24, null, pixels, 480)));
        var path = _folder.File(name);
        using var file = File.Create(path);
        encoder.Save(file);
        return path;
    }

    private string Grey(string name) => Jpeg(name, (x, y) => (G(x, y), G(x, y), G(x, y)));

    private static byte G(int x, int y) => (byte)Math.Clamp(110 + 90 * Math.Sin(x / 9.0) * Math.Cos(y / 11.0), 0, 255);

    private string Sepia(string name) => Jpeg(name, (x, y) => ((byte)(G(x, y) * 0.66), (byte)(G(x, y) * 0.84), G(x, y)));

    private string Colourful(string name) => Jpeg(name, (x, y) => ((byte)(x % 7 * 36), (byte)(y % 5 * 50), (byte)((x + y) % 9 * 28)));

    private ColorizeDialog Create(IReadOnlyList<string> photos, string? current, Func<Task<bool>>? ensure = null, string? models = null) =>
        new(photos, current, new EffectModels(models ?? _emptyModels.Path), ensure ?? (() => Task.FromResult(true)), dark: false);

    private static ListView ListOf(Form dialog) => Ui.Controls<ListView>(dialog).Single();

    private static Button ColorizeButton(Form dialog) => Ui.Controls<Button>(dialog).First(button => button.Text == ColorizeStrings.ColorizeButton);

    private static string[] Names(ListView list) => [.. list.Items.Cast<ListViewItem>().Select(item => Path.GetFileName(item.Text)).Order()];

    private static string[] Ticked(ListView list) => [.. list.CheckedItems.Cast<ListViewItem>().Select(item => Path.GetFileName(item.Text)).Order()];

    private static async Task WaitForScan(Form dialog) =>
        Assert.True(await Ui.Until(() => ListOf(dialog).Items.Count > 0 && ColorizeButton(dialog).Enabled, 30000), "the photos were looked at");

    [Fact]
    public void TheListOfPhotos_IsLookedAt_AndOnlyTheBlackAndWhiteOnesAreOffered_Ticked()
    {
        var photos = new[] { Grey("grey1.jpg"), Grey("grey2.jpg"), Sepia("sepia.jpg"), Colourful("colour.jpg") };
        File.WriteAllText(_folder.File("clip.mp4"), "a video"); // a video is not a photo

        Ui.Run(() => Create([.. photos, _folder.File("clip.mp4")], current: null), async dialog =>
        {
            await WaitForScan(dialog);
            var list = ListOf(dialog);

            Assert.Equal(["grey1.jpg", "grey2.jpg", "sepia.jpg"], Names(list));
            Assert.Equal(["grey1.jpg", "grey2.jpg", "sepia.jpg"], Ticked(list)); // all of them ticked
            Assert.Equal(ColorizeStrings.ColourGrey, list.Items.Cast<ListViewItem>().First(i => i.Text.EndsWith("grey1.jpg")).SubItems[2].Text);
            Assert.Equal(ColorizeStrings.ColourToned, list.Items.Cast<ListViewItem>().First(i => i.Text.EndsWith("sepia.jpg")).SubItems[2].Text);
            Assert.Contains(Ui.Controls<Label>(dialog), label => label.Text.StartsWith("3 of 4 photos are black and white")); // of the four photos (the video is not one)
        });
    }

    [Fact]
    public void ACopyThatWasMadeBefore_IsNotOfferedAgain_AndAPhotoThatHasOne_IsOfferedUnticked()
    {
        var withCopy = Grey("has copy.jpg");
        var copy = Grey("has copy (colorized).jpg");
        var plain = Grey("plain.jpg");

        Ui.Run(() => Create([withCopy, copy, plain], current: null), async dialog =>
        {
            await WaitForScan(dialog);
            var list = ListOf(dialog);

            Assert.Equal(["has copy.jpg", "plain.jpg"], Names(list));      // the copy itself is not a photo to colorize
            Assert.Equal(["plain.jpg"], Ticked(list));                      // the photo that has a copy is not ticked
            var row = list.Items.Cast<ListViewItem>().First(i => i.Text.EndsWith("has copy.jpg"));
            Assert.Contains("has copy (colorized).jpg", row.SubItems[3].Text);
        });
    }

    [Fact]
    public void WhenNoneIsBlackAndWhite_ItSaysSo_AndThereIsNothingToDo()
    {
        var photos = new[] { Colourful("a.jpg"), Colourful("b.jpg") };

        Ui.Run(() => Create(photos, current: null), async dialog =>
        {
            Assert.True(await Ui.Until(() => Ui.Controls<Label>(dialog).Any(label => label.Text.StartsWith("None of the 2")), 30000));
            Assert.False(ColorizeButton(dialog).Enabled);
            Assert.Empty(ListOf(dialog).Items.Cast<ListViewItem>());
        });
    }

    [Fact]
    public void ThePhotoThatIsShown_IsTheFirstChoice_AndIsColorizedWhateverItsColour()
    {
        var colour = Colourful("shown.jpg");
        var other = Grey("other.jpg");

        Ui.Run(() => Create([colour, other], current: colour), async dialog =>
        {
            var list = ListOf(dialog);
            Assert.Equal(["shown.jpg"], Names(list));
            Assert.Equal(["shown.jpg"], Ticked(list));
            Assert.True(ColorizeButton(dialog).Enabled);
            Assert.True(await Ui.Until(() => list.Items[0].SubItems[2].Text == ColorizeStrings.ColourColour, 20000), "it says that the photo has colours already");

            // the other choice is the list
            Ui.Controls<RadioButton>(dialog).First(radio => radio.Text == ColorizeStrings.ScopeList).Checked = true;
            Assert.True(await Ui.Until(() => Names(list).SequenceEqual(["other.jpg"]), 30000));
        });
    }

    [Fact]
    public void WithoutAPhoto_ThereIsOnlyTheList()
    {
        var photo = Grey("a.jpg");

        Ui.Run(() => Create([photo], current: null), async dialog =>
        {
            var radios = Ui.Controls<RadioButton>(dialog).ToList();
            Assert.False(radios.First(r => r.Text.StartsWith("This photo")).Visible); // no photo is shown: nothing to choose
            Assert.True(radios.First(r => r.Text == ColorizeStrings.ScopeList).Checked);
            await WaitForScan(dialog);
        });
    }

    [Fact]
    public void TickingAndUnticking_DecidesWhatIsDone_AndTheButtonKnows()
    {
        var photos = new[] { Grey("a.jpg"), Grey("b.jpg") };

        Ui.Run(() => Create(photos, current: null), async dialog =>
        {
            await WaitForScan(dialog);
            var list = ListOf(dialog);
            var button = ColorizeButton(dialog);

            Ui.ButtonStartingWith(dialog, ColorizeStrings.TickNoneButton).PerformClick();
            Assert.Empty(list.CheckedItems.Cast<ListViewItem>());
            Assert.False(button.Enabled); // nothing is ticked: nothing to do

            list.Items[1].Checked = true;
            Assert.True(button.Enabled);
            Assert.Contains(Ui.Controls<Label>(dialog), label => label.Text == "1 of 2 ticked");

            Ui.ButtonStartingWith(dialog, ColorizeStrings.TickAllButton).PerformClick();
            Assert.Equal(2, list.CheckedItems.Count);
        });
    }

    [Fact]
    public void ARefusedModel_LeavesEveryPhotoAsItWas_AndSaysSo()
    {
        var photo = Grey("a.jpg");
        var asked = 0;

        Ui.Run(() => Create([photo], current: null, ensure: () => { asked++; return Task.FromResult(false); }), async dialog =>
        {
            await WaitForScan(dialog);
            ColorizeButton(dialog).PerformClick();

            Assert.True(await Ui.Until(() => ListOf(dialog).Items[0].SubItems[3].Text == ColorizeStrings.ResultNotDone, 20000));
            Assert.Equal(1, asked);
            Assert.True(ColorizeButton(dialog).Enabled); // it can be tried again
        });

        Assert.False(File.Exists(_folder.File("a (colorized).jpg")));
        Assert.Single(Directory.GetFiles(_folder.Path, "*.jpg"));
    }

    [Fact]
    public void GettingTheModelThrowing_IsAFailureOfEveryPhoto_NotACrash()
    {
        var photo = Grey("a.jpg");

        // Something nobody thought of, thrown out of the question about the model: the button has nobody to catch it
        Ui.Run(() => Create([photo], current: null, ensure: () => throw new InvalidOperationException("no way to ask")), async dialog =>
        {
            await WaitForScan(dialog);
            ColorizeButton(dialog).PerformClick();

            Assert.True(await Ui.Until(() => ListOf(dialog).Items[0].SubItems[3].Text.Contains("no way to ask"), 20000), "the photo says why");
            Assert.True(await Ui.Until(() => ColorizeButton(dialog).Enabled, 5000), "it can be tried again");
        });

        Assert.Single(Directory.GetFiles(_folder.Path, "*.jpg"));
    }

    [Fact]
    public void AModelThatIsNotThere_IsAFailureOfEveryPhoto_NotACrash()
    {
        var photos = new[] { Grey("a.jpg"), Grey("b.jpg") };

        Ui.Run(() => Create(photos, current: null), async dialog => // the model is "downloaded" (the callback says so) but its file is not in the folder
        {
            await WaitForScan(dialog);
            ColorizeButton(dialog).PerformClick();

            Assert.True(await Ui.Until(() => ListOf(dialog).Items.Cast<ListViewItem>().All(item => item.SubItems[3].Text.StartsWith("failed")), 20000));
            Assert.Equal(0, ((ColorizeDialog)dialog).Saved);
        });

        Assert.Equal(2, Directory.GetFiles(_folder.Path, "*.jpg").Length);
    }

    [Fact]
    public void WithTheRealModel_TheTickedPhotosGetTheirCopies_NextToThem_AndTheOthersDoNot()
    {
        if (!ToolModels.Colorizer.IsPresentIn(ModelsFolder)) return;
        var a = Grey("a.jpg");
        var b = Grey("b.jpg");
        var originals = new[] { File.ReadAllBytes(a), File.ReadAllBytes(b) };

        Ui.Run(() => Create([a, b], current: null, models: ModelsFolder), async dialog =>
        {
            await WaitForScan(dialog);
            var list = ListOf(dialog);
            list.Items.Cast<ListViewItem>().First(item => item.Text.EndsWith("b.jpg")).Checked = false;
            ColorizeButton(dialog).PerformClick();

            Assert.True(await Ui.Until(() => list.Items.Cast<ListViewItem>().First(item => item.Text.EndsWith("a.jpg")).SubItems[3].Text.StartsWith("saved as"), 60000));
            Assert.True(await Ui.Until(() => ColorizeButton(dialog).Enabled && ColorizeButton(dialog).Visible, 20000), "the run is over");
            Assert.Equal(1, ((ColorizeDialog)dialog).Saved);
            Assert.Equal(string.Empty, list.Items.Cast<ListViewItem>().First(item => item.Text.EndsWith("b.jpg")).SubItems[3].Text);
        });

        Assert.True(File.Exists(_folder.File("a (colorized).jpg")));
        Assert.False(File.Exists(_folder.File("b (colorized).jpg")));
        Assert.Equal(originals[0], File.ReadAllBytes(a)); // the photos are not touched
        Assert.Equal(originals[1], File.ReadAllBytes(b));
        var copy = WicPhotoDecoder.Decode(File.ReadAllBytes(_folder.File("a (colorized).jpg")), 400);
        Assert.Equal((160, 120), (copy.Width, copy.Height));
        Assert.False(BlackAndWhite.Measure(copy).IsBlackAndWhite, "it has colours now");
    }

    [Fact]
    public void ThePhotoThatWasColorized_IsToldItHasACopyNow_AndTheListIsNotOfferedTheCopy()
    {
        if (!ToolModels.Colorizer.IsPresentIn(ModelsFolder)) return;
        var photo = Grey("only.jpg");

        Ui.Run(() => Create([photo], current: photo, models: ModelsFolder), async dialog =>
        {
            var list = ListOf(dialog);
            ColorizeButton(dialog).PerformClick();
            Assert.True(await Ui.Until(() => list.Items[0].SubItems[3].Text.StartsWith("saved as only (colorized).jpg"), 60000));
        });

        // A second window for the same folder: the copy is not a photo to colorize, the photo has a copy
        Ui.Run(() => Create([photo, _folder.File("only (colorized).jpg")], current: null), async dialog =>
        {
            await Ui.Until(() => ListOf(dialog).Items.Count > 0, 30000);
            Assert.Equal(["only.jpg"], Names(ListOf(dialog)));
            Assert.Empty(ListOf(dialog).CheckedItems.Cast<ListViewItem>());
        });
    }
}
