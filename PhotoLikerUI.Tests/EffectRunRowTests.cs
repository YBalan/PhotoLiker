using PhotoLiker.Core;
using System.ComponentModel;
using System.Drawing.Imaging;
using System.Globalization;
using System.Reflection;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The row EffectRun, in the real grid and in real slideshows: it shows the number or the word Random, takes what is typed, sits under the Effect row; and a random effect stays for that many photos
/// (the shown photos are counted, whether the timer or a key changed them), then another effect is chosen, never the one that was on; Random leaves the number to chance; other effects ignore it.
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class EffectRunRowTests : IDisposable
{
    private static readonly Rectangle ScreenBounds = new(0, 0, 640, 360);
    private static readonly SlideshowEffect[] NeverChosen = [SlideshowEffect.None, SlideshowEffect.Random, SlideshowEffect.Parallax, SlideshowEffect.Portrait, SlideshowEffect.DollyZoom];

    private readonly TempFolder _folder = new();

    public EffectRunRowTests() => SettingsPresentation.Register(); // as Program does

    public void Dispose() => _folder.Dispose();

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    // ── what the cell says ──────────────────────────────────────────────────

    [Theory]
    [InlineData(0, "Random")]
    [InlineData(1, "1")]
    [InlineData(5, "5")]
    [InlineData(100, "100")]
    [InlineData(150, "100")]    // a value out of the range is shown as the range keeps it
    [InlineData(-1, "Random 1-10")]
    [InlineData(-2, "Random 3-20")]
    [InlineData(-3, "Random")]
    public void TheCell_ShowsTheNumber_OrTheWordsOfARandomValue(int value, string shown) =>
        Assert.Equal(shown, EffectRunGridConverter.Display(value));

    [Theory]
    [InlineData("random", 0)]
    [InlineData("  RANDOM ", 0)]
    [InlineData("Random 1-10", -1)]
    [InlineData("random 1-10", -1)]
    [InlineData("Random 1–10", -1)]   // an en dash, as a word processor types it
    [InlineData("Random 3 - 20", -2)]
    [InlineData("RANDOM 3-20", -2)]
    [InlineData("Random 1-100", 0)]
    [InlineData("7", 7)]
    [InlineData(" 42 ", 42)]
    [InlineData("0", 0)]
    [InlineData("-5", 0)]
    [InlineData("-1", 0)]            // a number is a number: -1 is not the short Random
    [InlineData("-2", 0)]
    [InlineData("100", 100)]
    [InlineData("250", 100)]
    [InlineData("99999999999999", 100)]
    public void ATypedText_IsTheNumberKeptInTheRange_OrARandomValue(string typed, int expected) =>
        Assert.Equal(expected, EffectRunGridConverter.Parse(typed));

    [Theory]
    [InlineData("")]
    [InlineData("many")]
    [InlineData("3.5")]
    [InlineData("1e2")]
    [InlineData("Randon")]
    [InlineData("Random 2-9")]
    [InlineData("Random 1-")]
    [InlineData("Random 20-3")]
    public void ATextThatIsNeither_IsRefused_WithAMessageThatNamesIt(string typed)
    {
        var failure = Assert.Throws<FormatException>(() => EffectRunGridConverter.Parse(typed));

        Assert.Contains(typed, failure.Message);
        Assert.Contains("100", failure.Message);
        Assert.Contains("Random", failure.Message);
        Assert.Contains("Random 1-10", failure.Message);
        Assert.Contains("Random 3-20", failure.Message);
    }

    [Fact]
    public void TheDropDown_OffersTheRandomValuesAndSomeNumbers_AndAnyOtherNumberCanBeTyped()
    {
        var converter = new EffectRunGridConverter();
        var values = converter.GetStandardValues(null)!.Cast<int>().ToArray();

        Assert.True(converter.GetStandardValuesSupported(null));
        Assert.False(converter.GetStandardValuesExclusive(null)); // not exclusive: typing works
        Assert.Equal([0, -1, -2, 1, 2, 3, 5, 10, 20, 50, 100], values);
        Assert.Equal(["Random", "Random 1-10", "Random 3-20", "1", "2", "3", "5", "10", "20", "50", "100"], values.Select(value => converter.ConvertToString(value)));
        Assert.Equal("Random", converter.ConvertToString(0));
        Assert.Equal(0, converter.ConvertFromString("random"));
        Assert.Equal(-1, converter.ConvertFromString("Random 1-10"));
        Assert.Equal(-2, converter.ConvertFromString("Random 3-20"));
        Assert.Equal(12, converter.ConvertFromString("12"));
        Assert.Equal("12", converter.ConvertToString(null, CultureInfo.GetCultureInfo("uk-UA"), 12));
    }

    // ── the row of the real grid ────────────────────────────────────────────

    private static GridItem? Find(GridItem from, string name)
    {
        if (from.PropertyDescriptor?.Name == name)
            return from;
        foreach (GridItem child in from.GridItems)
        {
            if (Find(child, name) is { } found)
                return found;
        }
        return null;
    }

    private static GridItem RootOf(GridItem item)
    {
        while (item.Parent is { } parent)
            item = parent;
        return item;
    }

    [Fact]
    public void TheRow_IsInTheSlideshowGroup_UnderTheEffectRow_AndShowsFive_AndTakesRandomTyped()
    {
        Ui.Run(() => new MainForm(), async form =>
        {
            var grid = Ui.Controls<PropertyGrid>(form).Single(g => g.SelectedObject is Settings);
            var config = (Settings)grid.SelectedObject!;
            // The window opens with the settings file of the last folder, which an earlier test (or an earlier run of this one) may have left with another value: begin from the default
            // (that the default is 5 is for the tests of the setting itself)
            config.SlideshowEffectRun = Slideshow.DefaultEffectRun;

            var item = Find(RootOf(grid.SelectedGridItem!), nameof(Settings.SlideshowEffectRun));
            Assert.NotNull(item);
            grid.SelectedGridItem = item;
            await Task.Delay(100);

            Assert.Equal("EffectRun", item!.Label);
            Assert.Equal("6. Slideshow", item.Parent!.Label);
            Assert.Equal(5, item.Value);
            Assert.Equal("5", item.PropertyDescriptor!.Converter.ConvertToString(item.Value));
            Assert.Contains("random effect", item.PropertyDescriptor.Description);

            // Under the Effect row: the two rows are neighbours in the group
            var rows = item.Parent.GridItems.Cast<GridItem>().Select(row => row.PropertyDescriptor?.Name).ToList();
            Assert.Equal(rows.IndexOf(nameof(Settings.SlideshowEffect)) + 1, rows.IndexOf(nameof(Settings.SlideshowEffectRun)));

            // What is typed in the cell is the setting
            var converter = item.PropertyDescriptor.Converter;
            item.PropertyDescriptor.SetValue(config, converter.ConvertFromString("random")!);
            Assert.Equal(Slideshow.RandomEffectRun, config.SlideshowEffectRun);
            Assert.Equal("Random", converter.ConvertToString(config.SlideshowEffectRun));
            item.PropertyDescriptor.SetValue(config, converter.ConvertFromString("30")!);
            Assert.Equal(30, config.SlideshowEffectRun);
            item.PropertyDescriptor.SetValue(config, converter.ConvertFromString("Random 1-10")!);
            Assert.Equal(Slideshow.RandomOneToTenEffectRun, config.SlideshowEffectRun);
            Assert.Equal("Random 1-10", converter.ConvertToString(config.SlideshowEffectRun));
            item.PropertyDescriptor.SetValue(config, converter.ConvertFromString("random 3-20")!);
            Assert.Equal(Slideshow.RandomThreeToTwentyEffectRun, config.SlideshowEffectRun);
            Assert.Equal("Random 3-20", converter.ConvertToString(config.SlideshowEffectRun));

            config.SlideshowEffectRun = Slideshow.DefaultEffectRun; // the window saves its settings when it closes: leave the default behind
        }, seconds: 60);
    }

    // ── the main window gives the show the setting ──────────────────────────

    private void MakePhotosOnDisk(int count)
    {
        for (var i = 0; i < count; i++)
        {
            using var bitmap = new Bitmap(320, 200, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bitmap))
                g.Clear(Color.FromArgb(40 + i * 15, 90, 200 - i * 10));
            bitmap.Save(_folder.File($"photo{i}.jpg"), ImageFormat.Jpeg);
        }
    }

    private static List<Form> Screens() => Application.OpenForms.Cast<Form>().Where(f => f.GetType().Name == "SlideshowScreen").ToList();

    [Fact]
    public void TheShowOfTheMainWindow_HasTheNumberOfTheRow_AndTheDefaultWhenItIsNotSet()
    {
        MakePhotosOnDisk(3);

        Ui.Run(() => new MainForm(_folder.Path), async form =>
        {
            var config = Field<Settings>(form, "_currentConfig");
            Assert.True(await Ui.Until(() => config.Files.Count == 3, 20000), "the folder is listed");
            var start = typeof(MainForm).GetMethod("StartSlideshow", BindingFlags.NonPublic | BindingFlags.Instance)!;

            foreach (var (set, expected) in new[]
            {
                (5, 5), (7, 7), (Slideshow.RandomEffectRun, 0), (Slideshow.RandomOneToTenEffectRun, -1), (Slideshow.RandomThreeToTwentyEffectRun, -2), (100, 100), (900, 100), (5, 5),
            })
            {
                config.SlideshowEffect = SlideshowEffect.Random;
                config.SlideshowEffectRun = set;
                form.BeginInvoke(new Action(() => start.Invoke(form, null))); // Alt+Enter
                Assert.True(await Ui.Until(() => Screens().Count == 1, 20000), $"the show is on the screen (EffectRun {set})");

                var show = Field<SlideshowSession>(form, "_slideshow");
                Assert.Equal(expected, show.EffectRun);

                Screens()[0].Close(); // Esc
                Assert.True(await Ui.Until(() => Screens().Count == 0 && Field<object?>(form, "_slideshow") is null, 20000), "the show is over");
            }
        }, seconds: 150);
    }

    // ── the runs of a random effect ─────────────────────────────────────────

    private List<PhotoFile> MakePhotos(int count)
    {
        var photos = new List<PhotoFile>();
        for (var i = 0; i < count; i++)
        {
            using var bitmap = new Bitmap(320, 200);
            using (var g = Graphics.FromImage(bitmap))
                g.Clear(Color.FromArgb(40 + i * 12 % 200, 100, 180 - i * 7 % 120));

            var path = _folder.File($"run{i}.jpg");
            bitmap.Save(path, ImageFormat.Jpeg);
            photos.Add(new PhotoFile(path, string.Empty));
        }
        return photos;
    }

    private static SlideshowSession Session(List<PhotoFile> photos, SlideshowEffect effect, int? perEffect) =>
        new(photos, 0, SlideshowMode.OneByOne, 60, SlideshowWatermark.None, [ScreenBounds], 0, effect, video: null, people: null, depth: null,
            perEffect is { } number ? new SlideshowStyleOptions(SlideshowLook.None, Lut: null, Gpu: false, EffectRun: number) : null);

    private static void Show(SlideshowSession session, Func<SlideshowScreen, Task> body) =>
        Ui.Run(() => new Form(), async host =>
        {
            host.BeginInvoke(() => session.Run(host));
            Assert.True(await Ui.Until(() => session.Screens.Count == 1 && session.Screens[0].HasPhoto, 20000), "the first photo is shown");
            try
            {
                await body(session.Screens[0]);
            }
            finally
            {
                session.HandleKey(Keys.Escape);
            }
        });

    /// <summary>The effect used for each of the first <paramref name="count"/> photos: the first is on the screen already, the others are put there by the Right key one after the other.</summary>
    private static async Task<List<SlideshowEffect>> EffectsOfThePhotos(SlideshowSession session, List<PhotoFile> photos, int count)
    {
        var effects = new List<SlideshowEffect> { session.LastEffect };
        for (var i = 1; i < count; i++)
        {
            session.HandleKey(Keys.Right);
            var path = photos[i].OriginalFilePath;
            Assert.True(await Ui.Until(() => session.ShownPaths[0] == path, 20000), $"photo {i} is shown");
            effects.Add(session.LastEffect);
        }
        return effects;
    }

    /// <summary>The effects as runs: each effect with how many photos in a row it was used for.</summary>
    private static List<(SlideshowEffect Effect, int Length)> RunsOf(List<SlideshowEffect> effects)
    {
        var runs = new List<(SlideshowEffect, int)>();
        foreach (var effect in effects)
        {
            if (runs.Count > 0 && runs[^1].Item1 == effect)
                runs[^1] = (effect, runs[^1].Item2 + 1);
            else
                runs.Add((effect, 1));
        }
        return runs;
    }

    [Theory]
    [InlineData(3, 12)]
    [InlineData(4, 13)]
    [InlineData(5, 14)]    // the default
    [InlineData(1, 9)]
    public void ARandomEffect_StaysForThatManyPhotos_ThenAnotherOne_NeverTheSameTwiceInARow(int perEffect, int photoCount)
    {
        var photos = MakePhotos(photoCount);
        using var session = Session(photos, SlideshowEffect.Random, perEffect);

        Show(session, async _ =>
        {
            var effects = await EffectsOfThePhotos(session, photos, photoCount);
            var runs = RunsOf(effects);

            Assert.All(effects, effect => Assert.DoesNotContain(effect, NeverChosen));
            // Every run but the last is exactly that long; the last has the rest. Two runs in a row are never of the same effect, so they show up as two
            Assert.All(runs.Take(runs.Count - 1), run => Assert.Equal(perEffect, run.Length));
            Assert.InRange(runs[^1].Length, 1, perEffect);
            Assert.Equal((photoCount + perEffect - 1) / perEffect, runs.Count);
        });
    }

    [Fact]
    public void WithNoSettingAtAll_TheDefaultIsFive()
    {
        var photos = MakePhotos(11);
        using var session = Session(photos, SlideshowEffect.Random, perEffect: null);

        Show(session, async _ =>
        {
            Assert.Equal(5, session.EffectRun);
            var runs = RunsOf(await EffectsOfThePhotos(session, photos, 11));

            Assert.Equal([5, 5, 1], runs.Select(run => run.Length));
        });
    }

    [Fact]
    public void TheMostIsOneHundred_SoTwelvePhotosAreAllOfOneEffect()
    {
        var photos = MakePhotos(12);
        using var session = Session(photos, SlideshowEffect.Random, 100);

        Show(session, async _ =>
        {
            var effects = await EffectsOfThePhotos(session, photos, 12);

            Assert.Single(effects.Distinct());
            Assert.Equal(100 - 12, session.RunLeft);
        });
    }

    [Theory]
    [InlineData(Slideshow.RandomEffectRun, 1, 100)]
    [InlineData(Slideshow.RandomOneToTenEffectRun, 1, 10)]
    [InlineData(Slideshow.RandomThreeToTwentyEffectRun, 3, 20)]
    public void WithARandomValue_TheLengthOfEveryRunIsLeftToChance_ButStaysInItsRange_AndAnotherEffectFollows(int setting, int least, int most)
    {
        var photos = MakePhotos(24);
        using var session = Session(photos, SlideshowEffect.Random, setting);

        Show(session, async _ =>
        {
            Assert.Equal(setting, session.EffectRun);
            Assert.InRange(session.RunLeft + 1, least, most); // the first run has a length drawn already

            var runs = RunsOf(await EffectsOfThePhotos(session, photos, 24));

            // Every run but the last is a whole one, of a length in the range; the last was cut off by the end of the photos, so it can be shorter (never longer)
            Assert.All(runs.Take(runs.Count - 1), run => Assert.InRange(run.Length, least, most));
            Assert.InRange(runs[^1].Length, 1, most);
            for (var i = 1; i < runs.Count; i++)
                Assert.NotEqual(runs[i - 1].Effect, runs[i].Effect);
        });
    }

    [Theory]
    [InlineData(SlideshowEffect.Wipe)]
    [InlineData(SlideshowEffect.Fade)]
    [InlineData(SlideshowEffect.KenBurns)]
    public void AnEffectThatIsNotRandom_IsTheSameForEveryPhoto_WhateverTheRowSays(SlideshowEffect effect)
    {
        var photos = MakePhotos(8);
        using var session = Session(photos, effect, 2);

        Show(session, async _ =>
        {
            var effects = await EffectsOfThePhotos(session, photos, 8);

            Assert.All(effects, used => Assert.Equal(effect, used));
            Assert.Equal(0, session.RunLeft);
        });
    }

    [Fact]
    public void LeavingRandomAndComingBack_BeginsANewRun_NotTheRestOfTheOldOne()
    {
        var photos = MakePhotos(6);
        using var session = Session(photos, SlideshowEffect.Random, 10);

        Show(session, async _ =>
        {
            var first = session.LastEffect;
            Assert.Equal(9, session.RunLeft); // a run of ten, the first photo is on

            session.HandleKey(Keys.Down); // round to None
            Assert.Equal(SlideshowEffect.None, session.Effect);
            session.HandleKey(Keys.Up);   // and back to Random
            Assert.Equal(SlideshowEffect.Random, session.Effect);
            Assert.Equal(0, session.RunLeft);

            session.HandleKey(Keys.Right);
            Assert.True(await Ui.Until(() => session.ShownPaths[0] == photos[1].OriginalFilePath, 20000));
            Assert.Equal(9, session.RunLeft);                 // a new run of ten, not the old one with 8 left
            Assert.NotEqual(first, session.LastEffect);       // and another effect, never the one that was on
        });
    }

    [Fact]
    public void ChoosingRandomWithTheDownKey_BeginsANewRun_OfTheLengthOfTheRow()
    {
        var photos = MakePhotos(9);
        using var session = Session(photos, SlideshowEffect.DipToWhite, 4); // the effect before Random

        Show(session, async _ =>
        {
            Assert.Equal(SlideshowEffect.DipToWhite, session.LastEffect);
            session.HandleKey(Keys.Down);
            Assert.Equal(SlideshowEffect.Random, session.Effect);
            Assert.Equal(0, session.RunLeft); // nothing chosen yet

            var effects = new List<SlideshowEffect>();
            for (var i = 1; i <= 8; i++)
            {
                session.HandleKey(Keys.Right);
                var path = photos[i].OriginalFilePath;
                Assert.True(await Ui.Until(() => session.ShownPaths[0] == path, 20000));
                effects.Add(session.LastEffect);
                if (i == 1)
                    Assert.Equal(3, session.RunLeft); // 4 photos, the first is on
            }

            Assert.Equal(4, effects.Take(4).Distinct().Count() == 1 ? 4 : 0);
            Assert.Single(effects.Skip(4).Take(4).Distinct());
            Assert.NotEqual(effects[0], effects[4]);
            Assert.NotEqual(SlideshowEffect.DipToWhite, effects[0]); // never the one that was on
        });
    }
}
