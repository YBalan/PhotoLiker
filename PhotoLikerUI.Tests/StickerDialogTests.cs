using System.Drawing.Imaging;
using System.Reflection;
using PhotoLiker.Core;
using PhotoLiker.Effects.Models;
using PhotoLiker.Stickers;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The Telegram Sticker window opened for real: it reads a photo, finds the subject (here a finder that needs no model stands in for the model), shows the cut line on the photo and the sticker on a
/// backdrop, keeps what was set, and saves a WebP or a PNG that Telegram takes.
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class StickerDialogTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public StickerDialogTests() => new GlobalConfig { ColorizeBar = false }.Save();

    public void Dispose() => _folder.Dispose();

    private string SettingsFile => _folder.File("sticker-settings.json");

    /// <summary>A photo of a red disc on a grey ground (480 by 360, the disc 200 across, in the middle).</summary>
    private string Photo(string name = "photo.jpg", int width = 480, int height = 360)
    {
        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.FromArgb(120, 130, 125));
            graphics.FillEllipse(Brushes.Red, width / 2 - 100, height / 2 - 100, 200, 200);
        }

        var path = _folder.File(name);
        bitmap.Save(path, ImageFormat.Jpeg);
        return path;
    }

    /// <summary>A photo of coloured noise, which no picture format makes small: a sticker made of it is over the limit of Telegram unless it is a WebP that is written again with less.</summary>
    private string NoisyPhoto(string name = "noise.jpg", int size = 640)
    {
        var random = new Random(3);
        var pixels = new byte[size * size * 4];
        random.NextBytes(pixels);
        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppRgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        bitmap.UnlockBits(data);

        var path = _folder.File(name);
        var quality = new EncoderParameters(1);
        quality.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 100L);
        bitmap.Save(path, ImageCodecInfo.GetImageEncoders().First(codec => codec.FormatID == ImageFormat.Jpeg.Guid), quality);
        return path;
    }

    /// <summary>The model's map of the photo, drawn from the photo itself: 1 where it is red, 0 elsewhere, as sharp as the photo (a real map is coarser, and its edge is softer).</summary>
    private static SubjectMap RedDisc(PhotoPixels photo)
    {
        var values = new float[photo.Width * photo.Height];
        for (var i = 0; i < values.Length; i++)
            values[i] = photo.Bgr[i * 3 + 2] > 180 && photo.Bgr[i * 3 + 1] < 90 ? 1f : 0f;
        return new SubjectMap(photo.Width, photo.Height, values);
    }

    /// <summary>A map that has a core of 1 that falls away softly to 0, so that how much is kept depends on the sensitivity.</summary>
    private static SubjectMap SoftDisc(PhotoPixels photo)
    {
        const int width = 60, height = 45;
        var values = new float[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var distance = Math.Sqrt((x + 0.5 - width / 2.0) * (x + 0.5 - width / 2.0) + (y + 0.5 - height / 2.0) * (y + 0.5 - height / 2.0));
                values[y * width + x] = (float)Math.Clamp(1 - distance / 20.0, 0, 1);
            }
        }
        return new SubjectMap(width, height, values);
    }

    private int _modelAsked;
    private bool _modelAvailable = true;

    private StickerDialog Create(string photo, bool dark = false, Func<PhotoPixels, SubjectMap>? finder = null)
    {
        var dialog = new StickerDialog(photo, dark, SettingsFile, new EffectModels(_folder.File("models")), () =>
        {
            _modelAsked++;
            return Task.FromResult(_modelAvailable);
        })
        {
            FindSubject = finder ?? RedDisc,
        };
        return dialog;
    }

    private static async Task Made(StickerDialog dialog) =>
        Assert.True(await Ui.Until(() => !dialog.IsBusy && (dialog.CurrentFile is not null || dialog.PhotoFailed), 60000), $"the sticker was made ({dialog.StatusText})");

    private static async Task Remade(StickerDialog dialog) =>
        Assert.True(await Ui.Until(() => !dialog.IsBusy && !dialog.IsStale, 60000), $"the sticker was made again ({dialog.StatusText})");

    private static (byte B, byte G, byte R, byte A) At(StickerPicture picture, int x, int y)
    {
        var i = (y * picture.Width + x) * 4;
        return (picture.Bgra[i], picture.Bgra[i + 1], picture.Bgra[i + 2], picture.Bgra[i + 3]);
    }

    // ── Opening ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheWindow_ReadsThePhoto_FindsTheSubject_AndMakesTheSticker()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (StickerDialog)form;
            await Made(dialog);

            Assert.Contains("photo.jpg", dialog.Text);
            Assert.Equal((480, 360), (dialog.Photo!.Width, dialog.Photo.Height));
            Assert.Equal(new Size(480, 360), dialog.PhotoPane.Picture!.Size);          // the photo with its cut line
            var sticker = dialog.CurrentSticker!;
            Assert.Equal(512, Math.Max(sticker.Width, sticker.Height));
            Assert.Equal(new Size(sticker.Width, sticker.Height), dialog.StickerPane.Picture!.Size);
            Assert.True(dialog.SaveButton.Enabled);
            Assert.Equal(1, _modelAsked);
            Assert.Equal(string.Empty, dialog.StatusText);
            Assert.False(dialog.IsStale);

            // The subject is the disc: the middle of the photo is in the cutout, a corner is not
            var mask = dialog.CurrentMask!;
            Assert.True(mask[180 * 480 + 240] > 0.99f);
            Assert.True(mask[5 * 480 + 5] < 0.01f);
            Assert.True(At(sticker, sticker.Width / 2, sticker.Height / 2).A == 255);
            Assert.Equal(0, At(sticker, 0, 0).A);
        });
    }

    [Fact]
    public void TheSticker_IsTheFileThatWouldBeSaved_AndTheWindowSaysItFitsTelegram()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (StickerDialog)form;
            await Made(dialog);

            var file = dialog.CurrentFile!;
            var info = StickerFile.Inspect(file.Bytes)!;
            Assert.Equal(StickerFormat.WebP, file.Format);
            Assert.Equal(512, Math.Max(info.Width, info.Height));
            Assert.True(info.HasAlpha);
            Assert.Equal(StickerProblems.None, StickerFile.Check(info));
            Assert.StartsWith($"{info.Width} x {info.Height} pixels", dialog.InfoText);
            Assert.EndsWith("WebP: fits what Telegram takes.", dialog.InfoText);
        });
    }

    [Fact]
    public void ThePhotoPane_ShowsTheCutLine_WhatIsCutAwayIsDarkened()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (StickerDialog)form;
            await Made(dialog);

            using var shown = new Bitmap(dialog.PhotoPane.Picture!);
            var inside = shown.GetPixel(240, 180);
            var outside = shown.GetPixel(20, 20);
            Assert.True(inside.R > 200 && inside.G < 60, $"the disc keeps its colour: {inside}");   // red
            Assert.True(outside.R < 60 && outside.G < 60, $"the ground is darkened: {outside}");     // the grey (120, 130, 125) at a third of its brightness
            // The line is drawn where the disc ends: a pink point is on the row through the middle, at the disc's left edge (x = 140)
            var line = Enumerable.Range(130, 20).Select(x => shown.GetPixel(x, 180)).Any(color => color.R > 200 && color.B > 150 && color.G < 90);
            Assert.True(line, "the cut line is drawn at the edge of the disc");
        });
    }

    [Fact]
    public void ThePhoto_IsReadAtMost1600PixelsAcross()
    {
        var photo = Photo("big.jpg", 2400, 1600);
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (StickerDialog)form;
            await Made(dialog);

            Assert.Equal(1600, dialog.Photo!.Width);
            Assert.InRange(dialog.Photo.Height, 1066, 1067);   // 1600 x 2 / 3, as the decoder rounds it
            Assert.Equal(512, Math.Max(dialog.CurrentSticker!.Width, dialog.CurrentSticker.Height));
        });
    }

    [Fact]
    public void APhotoThatCannotBeRead_SaysSo_AndHasNothingToSave()
    {
        var path = _folder.File("broken.jpg");
        File.WriteAllText(path, "this is not a picture");
        Ui.Run(() => Create(path), async form =>
        {
            var dialog = (StickerDialog)form;
            Assert.True(await Ui.Until(() => dialog.PhotoFailed, 30000), "the failure was noticed");

            Assert.StartsWith("The photo could not be read", dialog.StatusText);
            Assert.Null(dialog.CurrentSticker);
            Assert.False(dialog.SaveButton.Enabled);
            dialog.AskSavePath = (_, _, _, _, _) => throw new InvalidOperationException("nothing to save, so nothing may be asked");
            Assert.False(await dialog.SaveAsync());
            Assert.Equal(0, _modelAsked); // and the model was not asked for either
        });
    }

    [Fact]
    public void TheWindow_HasTheStickerIcon_AndTheTitleOfTheFeature()
    {
        Ui.Run(() => Create(Photo()), async form =>
        {
            var dialog = (StickerDialog)form;
            Assert.StartsWith(StickerStrings.DialogTitle, dialog.Text);
            Assert.Same(DialogIcons.Sticker, dialog.Icon);
            await Made(dialog);
        });
    }

    // ── The cutout ──────────────────────────────────────────────────────────

    [Fact]
    public void TheWholePicture_NeedsNoModel_AndIsARoundedRectangle()
    {
        var photo = Photo();
        Ui.Run(() =>
        {
            new StickerSettings { Cutout = CutoutMode.WholePicture }.Save(SettingsFile);
            return Create(photo, finder: _ => throw new InvalidOperationException("the model must not be asked about the whole picture"));
        }, async form =>
        {
            var dialog = (StickerDialog)form;
            await Made(dialog);

            Assert.Equal(0, _modelAsked);
            var sticker = dialog.CurrentSticker!;
            Assert.Equal(512, sticker.Width);                       // 480 : 360, so the long side is the width
            Assert.InRange(sticker.Height, 390, 400);   // 480 : 360, and the room that the outline and the shadow need all round
            Assert.False(dialog.SensitivityNumeric.Enabled);        // what is the model's is greyed
            Assert.False(dialog.FollowEdgesNumeric.Enabled);
            Assert.True(At(sticker, sticker.Width / 2, sticker.Height / 2).A == 255);
        });
    }

    [Fact]
    public void WhenTheModelIsNotWanted_TheWholePictureIsTheStickerAndTheWindowSaysWhy_AndPickingTheSubjectAsksAgain()
    {
        var photo = Photo();
        _modelAvailable = false;
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (StickerDialog)form;
            Assert.True(await Ui.Until(() => !dialog.IsBusy && dialog.CurrentFile is not null, 60000), "a sticker was made without the model");

            Assert.Equal(1, _modelAsked);
            Assert.Equal(CutoutMode.WholePicture, dialog.ReadSettings().Cutout);
            Assert.Equal(512, dialog.CurrentSticker!.Width);
            Assert.Equal(StickerStrings.StatusModelMissing, dialog.StatusText);   // and the window says why
            Assert.True(dialog.CurrentMask![0] < 0.01f);                          // the rounded corner is cut off
            Assert.True(dialog.CurrentMask[180 * 480 + 240] == 1f);

            // Now the person wants the subject after all: the model is asked for again, and is there this time
            _modelAvailable = true;
            dialog.CutoutCombo.SelectedIndex = 0;
            Assert.True(await Ui.Until(() => _modelAsked == 2 && !dialog.IsBusy && dialog.CurrentMask![5 * 480 + 5] < 0.01f && dialog.CurrentMask[180 * 480 + 240] > 0.99f, 60000), "the subject was found");
            Assert.Equal(CutoutMode.Detect, dialog.ReadSettings().Cutout);
            Assert.True(dialog.SensitivityNumeric.Enabled);
            Assert.True(await Ui.Until(() => dialog.StatusText.Length == 0, 10000), "the reason is gone once the subject is there");
        });
    }

    [Fact]
    public void TheFallbackToTheWholePicture_IsNotKept_ButAChoiceOfThePersonIs()
    {
        var photo = Photo();
        _modelAvailable = false;
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (StickerDialog)form;
            Assert.True(await Ui.Until(() => !dialog.IsBusy && dialog.CurrentFile is not null, 60000), "a sticker was made without the model");
            Assert.Equal(CutoutMode.WholePicture, dialog.ReadSettings().Cutout);
            dialog.Close();
        });
        Assert.Equal(CutoutMode.Detect, StickerSettings.Load(SettingsFile).Cutout); // the window will ask for the model again next time

        _modelAvailable = true;                           // this time the model is there
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (StickerDialog)form;
            Assert.True(await Ui.Until(() => !dialog.IsBusy && dialog.CurrentFile is not null, 60000), "a sticker was made again");
            Assert.Equal(2, _modelAsked);                 // asked again, since the settings said Detect
            Assert.Equal(CutoutMode.Detect, dialog.ReadSettings().Cutout);

            dialog.CutoutCombo.SelectedIndex = 1;         // the person picks the whole picture themselves
            await Remade(dialog);
            dialog.Close();
        });
        Assert.Equal(CutoutMode.WholePicture, StickerSettings.Load(SettingsFile).Cutout); // that is kept
    }

    [Fact]
    public void WhenTheModelCannotRun_TheErrorIsInTheStatusLine_AndTheWholePictureStillWorks()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo, finder: _ => throw new InvalidOperationException("the model file is damaged")), async form =>
        {
            var dialog = (StickerDialog)form;
            Assert.True(await Ui.Until(() => !dialog.IsBusy && dialog.CurrentFile is not null, 60000), "a sticker was made");

            Assert.Equal(CutoutMode.WholePicture, dialog.ReadSettings().Cutout);
            Assert.Equal(512, dialog.CurrentSticker!.Width);
            Assert.Equal("The subject could not be found: the model file is damaged", dialog.StatusText);
        });
    }

    [Fact]
    public void ANoSubject_SaysSo_ShowsNoSticker_AndKeepsSaveOff()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo, finder: _ => new SubjectMap(8, 8, new float[64])), async form =>
        {
            var dialog = (StickerDialog)form;
            Assert.True(await Ui.Until(() => !dialog.IsBusy && dialog.StatusText == StickerStrings.StatusNoSubject, 60000), $"the window says nothing was found ({dialog.StatusText})");

            Assert.Null(dialog.CurrentSticker);
            Assert.Null(dialog.StickerPane.Picture);
            Assert.False(dialog.SaveButton.Enabled);
            Assert.Equal(string.Empty, dialog.InfoText);
            Assert.NotNull(dialog.PhotoPane.Picture); // the photo is still shown, all of it darkened: nothing is kept
        });
    }

    [Fact]
    public void TheSensitivity_DecidesHowMuchIsKept()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo, finder: SoftDisc), async form =>
        {
            var dialog = (StickerDialog)form;
            await Made(dialog);

            int Area() => dialog.CurrentMask!.Count(value => value >= 0.5f);
            dialog.SensitivityNumeric.Value = 20;
            await Remade(dialog);
            var careful = Area();
            dialog.SensitivityNumeric.Value = 80;
            await Remade(dialog);
            var generous = Area();

            Assert.True(generous > careful * 1.5, $"a higher sensitivity keeps more: {careful} against {generous}");
        });
    }

    [Fact]
    public void TheNewestSettings_AreTheOnesThatAreShown_WhenSeveralAreChangedInARow()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo, finder: SoftDisc), async form =>
        {
            var dialog = (StickerDialog)form;
            await Made(dialog);

            dialog.SensitivityNumeric.Value = 90;
            dialog.SensitivityNumeric.Value = 10;
            dialog.SensitivityNumeric.Value = 30;
            await Remade(dialog);

            var expected = Cutout.Shape(dialog.Photo!, SoftDisc(dialog.Photo!), new StickerSettings { Sensitivity = 30 });
            Assert.Equal(expected.Count(v => v >= 0.5f), dialog.CurrentMask!.Count(v => v >= 0.5f));
        });
    }

    // ── The outline, the shadow and the file ────────────────────────────────

    [Fact]
    public void TheOutline_IsOnByDefault_InTheColourThatWasPicked_AndOffWhenUnticked()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (StickerDialog)form;
            await Made(dialog);

            // The default: a white outline, a few pixels round the disc
            var sticker = dialog.CurrentSticker!;
            var row = sticker.Height / 2;
            var white = Enumerable.Range(0, sticker.Width).Count(x => At(sticker, x, row) is { A: 255, R: 255, G: 255, B: 255 });
            Assert.InRange(white, 10, 20); // 8 pixels on each side, give or take the soft edge

            // Picked: green
            dialog.PickColor = _ => Color.FromArgb(0, 200, 0);
            dialog.OutlineColorButton.PerformClick();
            Assert.Equal(Color.FromArgb(0, 200, 0), dialog.OutlineColor);
            Assert.Equal(Color.FromArgb(0, 200, 0), dialog.OutlineColorButton.BackColor);
            await Remade(dialog);
            sticker = dialog.CurrentSticker!;
            Assert.True(Enumerable.Range(0, sticker.Width).Count(x => At(sticker, x, row) is { A: 255, R: 0, G: 200, B: 0 }) >= 10, "the outline is green");

            // Picked and cancelled: nothing changes
            dialog.PickColor = _ => null;
            dialog.OutlineColorButton.PerformClick();
            Assert.Equal(Color.FromArgb(0, 200, 0), dialog.OutlineColor);

            // Unticked: no outline, and the width and the colour are greyed
            dialog.OutlineCheck.Checked = false;
            await Remade(dialog);
            sticker = dialog.CurrentSticker!;
            Assert.DoesNotContain(Enumerable.Range(0, sticker.Width), x => At(sticker, x, row) is { A: 255, R: 0, G: 200, B: 0 });
            Assert.False(dialog.OutlineWidthNumeric.Enabled);
            Assert.False(dialog.OutlineColorButton.Enabled);
        });
    }

    [Fact]
    public void TheOutlineWidth_IsTheWidthOfTheOutlineInPixelsOfTheSticker()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (StickerDialog)form;
            dialog.ShadowCheck.Checked = false;
            await Made(dialog);

            int Outline()
            {
                var sticker = dialog.CurrentSticker!;
                var row = sticker.Height / 2;
                return Enumerable.Range(0, sticker.Width).Count(x => At(sticker, x, row) is { A: 255, R: 255, G: 255, B: 255 });
            }

            dialog.OutlineWidthNumeric.Value = 4;
            await Remade(dialog);
            var thin = Outline();
            dialog.OutlineWidthNumeric.Value = 20;
            await Remade(dialog);
            var thick = Outline();

            Assert.InRange(thin, 4, 9);                 // 4 on each side, less the point of the soft edge
            Assert.InRange(thick - thin, 28, 34);       // 16 more on each side
        });
    }

    [Fact]
    public void TheShadow_IsUnderTheSticker_WhenOn()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (StickerDialog)form;
            await Made(dialog);

            bool HasShadow()
            {
                var sticker = dialog.CurrentSticker!;
                var column = sticker.Width / 2;
                return Enumerable.Range(0, sticker.Height).Any(y => At(sticker, column, y) is { A: > 0 and < 120, R: < 30, G: < 30, B: < 30 });
            }

            Assert.True(HasShadow());
            dialog.ShadowCheck.Checked = false;
            await Remade(dialog);
            Assert.False(HasShadow());
        });
    }

    [Fact]
    public void ThePng_IsMadeWithoutAQuality_AndTheQualityIsGreyedForIt()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (StickerDialog)form;
            await Made(dialog);
            Assert.True(dialog.QualityNumeric.Enabled);

            dialog.FormatCombo.SelectedIndex = 1;
            await Remade(dialog);

            Assert.Equal(StickerFormat.Png, dialog.CurrentFile!.Format);
            Assert.False(dialog.QualityNumeric.Enabled);
            Assert.False(dialog.QualitySlider.Enabled);
            Assert.EndsWith("PNG: fits what Telegram takes.", dialog.InfoText);
            var info = StickerFile.Inspect(dialog.CurrentFile.Bytes)!;
            Assert.Equal(StickerFormat.Png, info.Format);
            Assert.True(info.HasAlpha);
        });
    }

    [Fact]
    public void ALowerQuality_MakesASmallerWebP()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (StickerDialog)form;
            dialog.QualityNumeric.Value = 95;
            await Made(dialog);
            var good = dialog.CurrentFile!.Bytes.Length;

            dialog.QualityNumeric.Value = 10;
            await Remade(dialog);

            Assert.True(dialog.CurrentFile!.Bytes.Length < good);
        });
    }

    [Fact]
    public void APngThatIsOverTelegramsLimit_IsSaid_InRed_AndASavedOneSaysSoToo()
    {
        var photo = NoisyPhoto();
        var target = _folder.File("noise.png");
        Ui.Run(() =>
        {
            new StickerSettings { Cutout = CutoutMode.WholePicture, Format = StickerFormat.Png, Shadow = false }.Save(SettingsFile);
            return Create(photo);
        }, async form =>
        {
            var dialog = (StickerDialog)form;
            await Made(dialog);

            Assert.False(dialog.CurrentFile!.Fits);
            Assert.Contains("over the 512 KB that Telegram allows", dialog.InfoText);

            dialog.AskSavePath = (_, _, _, _, _) => (target, 2);
            Assert.True(await dialog.SaveAsync());
            Assert.True(File.Exists(target));
            Assert.Contains("over the 512 KB", dialog.StatusText);
        });
    }

    [Fact]
    public void AWebPOfNoise_IsWrittenAgainWithLessUntilItFits()
    {
        var photo = NoisyPhoto(size: 1600);
        Ui.Run(() =>
        {
            new StickerSettings { Cutout = CutoutMode.WholePicture, Format = StickerFormat.WebP, Quality = 100, Shadow = false }.Save(SettingsFile);
            return Create(photo);
        }, async form =>
        {
            var dialog = (StickerDialog)form;
            await Made(dialog);

            Assert.True(dialog.CurrentFile!.Fits, $"{dialog.CurrentFile.Bytes.Length} bytes");
            Assert.Contains("fits what Telegram takes", dialog.InfoText);
        });
    }

    // ── Saving ──────────────────────────────────────────────────────────────

    [Fact]
    public void Save_WritesTheWebPThatIsShown_ByTheNameThatWasPicked()
    {
        var photo = Photo();
        var target = _folder.File("out\\my-sticker.webp");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (StickerDialog)form;
            await Made(dialog);
            string? offeredName = null, offeredFilter = null;
            int offeredIndex = 0;
            dialog.AskSavePath = (title, filter, index, folder, name) =>
            {
                offeredName = name;
                offeredFilter = filter;
                offeredIndex = index;
                Assert.Equal(StickerStrings.SaveTitle, title);
                Assert.Equal(_folder.Path, folder);
                return (target, 1);
            };

            Assert.True(await dialog.SaveAsync());

            Assert.Equal("photo-sticker.webp", offeredName);
            Assert.Equal(1, offeredIndex);
            Assert.Contains("*.webp", offeredFilter);
            Assert.Contains("*.png", offeredFilter);
            Assert.Equal(target, dialog.SavedPath);
            Assert.Equal(dialog.CurrentFile!.Bytes, await File.ReadAllBytesAsync(target)); // exactly the file that was shown
            Assert.Equal($"Saved the sticker {target}", dialog.StatusText);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(target)!, "*.part"));
        });
    }

    [Fact]
    public void Save_ByAPngNameWhileTheFormatIsWebP_WritesAPng_AndNoExtensionTakesTheKindThatWasPicked()
    {
        var photo = Photo();
        var asPng = _folder.File("as-png.png");
        var plain = _folder.File("no-extension");
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (StickerDialog)form;
            await Made(dialog);

            dialog.AskSavePath = (_, _, _, _, _) => (asPng, 1);   // the list says WebP, the name says PNG: the name wins
            Assert.True(await dialog.SaveAsync());
            Assert.Equal(StickerFormat.Png, StickerFile.Inspect(asPng)!.Format);
            Assert.Equal(512, Math.Max(StickerFile.Inspect(asPng)!.Width, StickerFile.Inspect(asPng)!.Height));

            dialog.AskSavePath = (_, _, _, _, _) => (plain, 2);   // no extension: the second kind of the list is PNG
            Assert.True(await dialog.SaveAsync());
            Assert.Equal(plain + ".png", dialog.SavedPath);
            Assert.Equal(StickerFormat.Png, StickerFile.Inspect(plain + ".png")!.Format);
        });
    }

    [Fact]
    public void Save_AsksNothingMore_WhenCancelled_AndReplacesAFileThatIsThere()
    {
        var photo = Photo();
        var target = _folder.File("again.webp");
        File.WriteAllText(target, "an old sticker");
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (StickerDialog)form;
            await Made(dialog);

            dialog.AskSavePath = (_, _, _, _, _) => null;
            Assert.False(await dialog.SaveAsync());
            Assert.Null(dialog.SavedPath);
            Assert.Equal("an old sticker", File.ReadAllText(target));

            dialog.AskSavePath = (_, _, _, _, _) => (target, 1);
            Assert.True(await dialog.SaveAsync());
            Assert.Equal(StickerFormat.WebP, StickerFile.Inspect(target)!.Format);
        });
    }

    [Fact]
    public void Save_MakesTheStickerAgainFirst_WhenTheSettingsChangedAfterTheLastOne()
    {
        var photo = Photo();
        var target = _folder.File("fresh.png");
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (StickerDialog)form;
            await Made(dialog);
            var before = dialog.CurrentSticker!;

            dialog.OutlineWidthNumeric.Value = 30;   // not yet made again: the timer is still running
            Assert.True(dialog.IsStale);
            dialog.AskSavePath = (_, _, _, _, _) => (target, 2);
            Assert.True(await dialog.SaveAsync());

            Assert.NotSame(before, dialog.CurrentSticker);
            Assert.False(dialog.IsStale);
            var saved = StickerFile.Inspect(target)!;
            Assert.Equal((dialog.CurrentSticker!.Width, dialog.CurrentSticker.Height), (saved.Width, saved.Height));
        });
    }

    [Fact]
    public void Save_ToAFolderThatIsNotThere_SaysSo_AndWritesNothing()
    {
        var photo = Photo();
        var target = _folder.File("no\\such\\folder\\s.webp");
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (StickerDialog)form;
            await Made(dialog);
            dialog.AskSavePath = (_, _, _, _, _) => (target, 1);

            Assert.False(await dialog.SaveAsync());

            Assert.StartsWith("The sticker could not be saved", dialog.StatusText);
            Assert.Null(dialog.SavedPath);
        });
    }

    [Theory]
    [InlineData("a.png", 1, StickerFormat.Png)]
    [InlineData("a.PNG", 1, StickerFormat.Png)]
    [InlineData("a.webp", 2, StickerFormat.WebP)]
    [InlineData("a.WebP", 1, StickerFormat.WebP)]
    [InlineData("a", 1, StickerFormat.WebP)]
    [InlineData("a", 2, StickerFormat.Png)]
    [InlineData("a.jpg", 2, StickerFormat.Png)]
    [InlineData("a.jpg", 1, StickerFormat.WebP)]
    public void TheKindOfFile_IsTheExtension_ElseTheKindThatWasPicked(string name, int filter, StickerFormat expected) =>
        Assert.Equal(expected, StickerDialog.FormatOf(name, filter));

    // ── What is kept, the links of the sliders, the views ───────────────────

    [Fact]
    public void WhatWasSet_IsKeptWhenTheWindowIsClosed_AndComesBackWhenItIsOpenedAgain()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (StickerDialog)form;
            await Made(dialog);

            dialog.SensitivityNumeric.Value = 61;
            dialog.SmallPartsNumeric.Value = 4;
            dialog.FollowEdgesNumeric.Value = 77;
            dialog.EdgeNumeric.Value = -3;
            dialog.OutlineWidthNumeric.Value = 12;
            dialog.PickColor = _ => Color.FromArgb(255, 215, 0);
            dialog.OutlineColorButton.PerformClick();
            dialog.ShadowCheck.Checked = false;
            dialog.FormatCombo.SelectedIndex = 1;
            dialog.QualityNumeric.Value = 70;
            dialog.Close();
        });

        var saved = StickerSettings.Load(SettingsFile);
        Assert.Equal(61, saved.Sensitivity);
        Assert.Equal(4, saved.SmallParts);
        Assert.Equal(77, saved.FollowEdges);
        Assert.Equal(-3, saved.EdgeShift);
        Assert.Equal(12, saved.OutlineWidth);
        Assert.Equal("FFD700", saved.OutlineColor);
        Assert.False(saved.Shadow);
        Assert.Equal(StickerFormat.Png, saved.Format);
        Assert.Equal(70, saved.Quality);

        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (StickerDialog)form;
            var settings = dialog.ReadSettings();

            Assert.Equal(61, settings.Sensitivity);
            Assert.Equal(77, settings.FollowEdges);
            Assert.Equal(-3, settings.EdgeShift);
            Assert.Equal(12, settings.OutlineWidth);
            Assert.Equal(Color.FromArgb(255, 215, 0), dialog.OutlineColor);
            Assert.False(dialog.ShadowCheck.Checked);
            Assert.Equal(StickerFormat.Png, dialog.Format);
            Assert.Equal(70, settings.Quality);
            await Made(dialog);
            Assert.Equal(StickerFormat.Png, dialog.CurrentFile!.Format);
        });
    }

    [Fact]
    public void EverySlider_IsTheOtherFaceOfItsNumber()
    {
        Ui.Run(() => Create(Photo()), async form =>
        {
            var dialog = (StickerDialog)form;
            foreach (var (number, slider) in new[]
            {
                (dialog.SensitivityNumeric, dialog.SensitivitySlider), (dialog.SmallPartsNumeric, dialog.SmallPartsSlider), (dialog.FollowEdgesNumeric, dialog.FollowEdgesSlider),
                (dialog.EdgeNumeric, dialog.EdgeSlider), (dialog.OutlineWidthNumeric, dialog.OutlineWidthSlider), (dialog.QualityNumeric, dialog.QualitySlider),
            })
            {
                Assert.Equal((int)number.Minimum, slider.Minimum);
                Assert.Equal((int)number.Maximum, slider.Maximum);
                Assert.Equal((int)number.Value, slider.Value);

                number.Value = number.Maximum;
                Assert.Equal(slider.Maximum, slider.Value);          // the number moves the slider
                slider.Value = slider.Minimum;
                Assert.Equal(number.Minimum, number.Value);          // and the slider moves the number
                slider.Value = (slider.Minimum + slider.Maximum) / 2;
                Assert.Equal((decimal)slider.Value, number.Value);
            }

            await Made(dialog);
        });
    }

    [Fact]
    public void TheBackgrounds_AreOnlyTheViewOfTheSticker()
    {
        Ui.Run(() => Create(Photo()), async form =>
        {
            var dialog = (StickerDialog)form;
            await Made(dialog);
            var pane = dialog.StickerPane;
            var file = dialog.CurrentFile!.Bytes.ToArray();

            Color Corner()
            {
                using var shot = new Bitmap(pane.Width, pane.Height);
                pane.DrawToBitmap(shot, new Rectangle(0, 0, pane.Width, pane.Height));
                var rectangle = pane.PictureRectangle;
                return shot.GetPixel(rectangle.Left + 1, rectangle.Top + 1); // the corner of the sticker, which is see-through
            }

            dialog.BackgroundCombo.SelectedIndex = 1;
            Assert.Equal(StickerBackdrop.Dark, pane.Backdrop);
            Assert.Equal(StickerPane.PlainBackdrop(StickerBackdrop.Dark), Corner());

            dialog.BackgroundCombo.SelectedIndex = 2;
            Assert.Equal(StickerBackdrop.Light, pane.Backdrop);
            Assert.Equal(StickerPane.PlainBackdrop(StickerBackdrop.Light), Corner());

            dialog.BackgroundCombo.SelectedIndex = 0;
            Assert.Equal(StickerBackdrop.Checker, pane.Backdrop);
            Assert.True(Corner() is { R: 255 } or { R: 214 }, "a square of the checkerboard");

            Assert.Equal(file, dialog.CurrentFile.Bytes);          // none of this reached the file
            Assert.False(dialog.IsStale);                           // and none of it made the sticker again
        });
    }

    [Fact]
    public void TheWindow_IsMadeInTheDarkAndTheLightTheme_AndReadable()
    {
        foreach (var dark in new[] { false, true })
        {
            Ui.Run(() => Create(Photo(), dark), async form =>
            {
                var dialog = (StickerDialog)form;
                await Made(dialog);

                Assert.Equal(dark ? ThemeManager.DarkBack : ThemeManager.LightBack, dialog.BackColor);
                Assert.Equal(dark, dialog.StickerPane.Dark);
                Assert.NotEqual(dialog.StickerPane.Surround, dialog.BackColor);
                Assert.True(dialog.ClientSize.Width >= 900);
            });
        }
    }

    [Fact]
    public void ClosingTheWindowWhileItWorks_IsNoTrouble()
    {
        Ui.Run(() => Create(Photo(), finder: photo =>
        {
            Thread.Sleep(400);
            return RedDisc(photo);
        }), async form =>
        {
            var dialog = (StickerDialog)form;
            Assert.True(await Ui.Until(() => dialog.IsFinding, 30000), "the finding began");

            dialog.Close();
            await Task.Delay(800);

            Assert.True(dialog.IsDisposed || !dialog.Visible);
        });
    }

    // ── The pictures ────────────────────────────────────────────────────────

    [Fact]
    public void AStickerBitmap_KeepsItsStraightAlpha_AndAPhotoBitmapItsColours()
    {
        using var sticker = StickerDialog.ToBitmap(2, 1, [10, 20, 30, 128, 40, 50, 60, 255], 4);
        using var photo = StickerDialog.ToBitmap(2, 1, [10, 20, 30, 40, 50, 60], 3);

        Assert.Equal(Color.FromArgb(128, 30, 20, 10), sticker.GetPixel(0, 0));
        Assert.Equal(Color.FromArgb(255, 60, 50, 40), sticker.GetPixel(1, 0));
        Assert.Equal(Color.FromArgb(255, 30, 20, 10), photo.GetPixel(0, 0));
        Assert.Equal(Color.FromArgb(255, 60, 50, 40), photo.GetPixel(1, 0));
    }

    [Fact]
    public void ThePane_ShowsThePictureAsLargeAsFits_InTheMiddle_AndLetsGoOfTheOldOne()
    {
        using var pane = new StickerPane { Size = new Size(206, 106) };
        var wide = new Bitmap(400, 100);

        pane.Picture = wide;
        var rectangle = pane.PictureRectangle;

        Assert.Equal(194, rectangle.Width);                         // the room is 194 across, and 94 down
        Assert.InRange(rectangle.Height, 48, 49);                   // 4 : 1
        Assert.Equal(6 + (194 - rectangle.Width) / 2, rectangle.X);
        Assert.Equal(6 + (94 - rectangle.Height) / 2, rectangle.Y);

        var tall = new Bitmap(100, 400);
        pane.Picture = tall;
        Assert.ThrowsAny<Exception>(() => wide.GetPixel(0, 0)); // the old picture was let go of (a disposed bitmap can not be read)
        Assert.Equal(94, pane.PictureRectangle.Height);

        pane.Picture = null;
        Assert.Equal(Rectangle.Empty, pane.PictureRectangle);
    }

    // ── The button of the main window ───────────────────────────────────────

    /// <summary>A private field of the window, also when the window is a subclass of MainForm (the field is looked for in its bases).</summary>
    private static T Field<T>(object owner, string name)
    {
        for (var type = owner.GetType(); type is not null; type = type.BaseType)
        {
            if (type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly) is { } field)
                return (T)field.GetValue(owner)!;
        }
        throw new MissingFieldException(owner.GetType().Name, name);
    }

    private static ToolStrip Toolbar(Form form) =>
        Ui.Controls<ToolStrip>(form).Single(strip => strip is not StatusStrip && strip.Items.Cast<ToolStripItem>().Any(item => item.Name == "colorizeToolStripButton"));

    [Fact]
    public void TheMainWindow_HasTheStickerButton_BeforeDraw_AfterVideoToGif()
    {
        Ui.Run(() => new MainForm(), async form =>
        {
            var names = Toolbar(form).Items.Cast<ToolStripItem>().Select(item => item.Name).ToList();

            Assert.Equal("manuscriptToolStripButton", names[^1]);
            Assert.Equal("drawToolStripButton", names[^2]);
            Assert.Equal("stickerToolStripButton", names[^3]);
            Assert.Equal("videoToGifToolStripButton", names[^4]);
            var button = Toolbar(form).Items["stickerToolStripButton"]!;
            Assert.Equal(StickerStrings.ButtonText, button.Text);
            Assert.Equal(StickerStrings.ButtonTooltip, button.ToolTipText);
            Assert.NotNull(button.Image);
            Assert.Equal(ToolStripItemDisplayStyle.Image, button.DisplayStyle);
            await Task.CompletedTask;
        }, seconds: 60);
    }

    [Fact]
    public void WithNothingShown_TheButtonSaysSo_AndOpensNothing()
    {
        Ui.Run(() => new MainForm(_folder.Path), async form =>
        {
            await Task.Delay(300);
            ((ToolStripButton)Toolbar(form).Items["stickerToolStripButton"]!).PerformClick();

            Assert.Equal(StickerStrings.StatusNoPhoto, Field<ToolStripStatusLabel>(form, "toolStripStatusLabel1").Text);
            Assert.Null(Ui.Find("StickerDialog"));
        }, seconds: 60);
    }

    [Fact]
    public void ForAVideo_TheButtonSaysAVideoCannotBeASticker()
    {
        var video = _folder.File("clip.mp4");
        File.WriteAllBytes(video, [0, 0, 0, 24, 0x66, 0x74, 0x79, 0x70, 0x6D, 0x70, 0x34, 0x32, 0, 0, 0, 0]);
        Ui.Run(() => new MainForm(_folder.Path), async form =>
        {
            await Task.Delay(300);
            Field<PictureBox>(form, "pictureBox1").Tag = video;
            ((ToolStripButton)Toolbar(form).Items["stickerToolStripButton"]!).PerformClick();

            Assert.Equal(StickerStrings.StatusIsVideo, Field<ToolStripStatusLabel>(form, "toolStripStatusLabel1").Text);
            Assert.Null(Ui.Find("StickerDialog"));
        }, seconds: 60);
    }

    /// <summary>The window, with the question about downloading the model answered "no" (a test must not download 179 MB, nor wait for a person).</summary>
    private sealed class NoDownloadMainForm(string folder) : MainForm(folder)
    {
        public int Asked;
        public string? Feature;

        protected override bool ConfirmToolModelDownload(string feature, ToolModel model)
        {
            Asked++;
            Feature = feature;
            return false;
        }
    }

    [Fact]
    public void ForAPhoto_TheButtonOpensTheWindow_ForThatPhoto_WhichAsksForTheModelAndFallsBackToTheWholePicture()
    {
        var photo = Photo();
        File.Delete(MainForm.StickerSettingsFile); // what an earlier run (or the person) kept must not decide whether the model is asked for
        Ui.Run(() => new NoDownloadMainForm(_folder.Path), async form =>
        {
            var main = (NoDownloadMainForm)form;
            await Task.Delay(300);
            Field<PictureBox>(form, "pictureBox1").Tag = photo;

            form.BeginInvoke(() => ((ToolStripButton)Toolbar(form).Items["stickerToolStripButton"]!).PerformClick());
            Assert.True(await Ui.Until(() => Ui.Find("StickerDialog") is not null, 30000), "the window opened");

            var dialog = (StickerDialog)Ui.Find("StickerDialog")!;
            Assert.Contains("photo.jpg", dialog.Text);
            Assert.True(await Ui.Until(() => !dialog.IsBusy && dialog.CurrentFile is not null, 60000), "a sticker was made");
            Assert.Equal(1, main.Asked);                                     // the model was asked for, once
            Assert.Equal(ToolModelStrings.StickerFeature, main.Feature);     // for the stickers
            Assert.Equal(CutoutMode.WholePicture, dialog.ReadSettings().Cutout);
            Assert.Equal(StickerStrings.StatusModelMissing, dialog.StatusText);

            dialog.Close();
            Assert.True(await Ui.Until(() => Ui.Find("StickerDialog") is null, 10000), "the window closed");
        }, seconds: 90);
    }

    [Fact]
    public void TheMainWindow_MakesTheWindowWithTheCutoutModel_ThatIsAskedForBeforeItIsDownloaded()
    {
        Ui.Run(() => new MainForm(), async form =>
        {
            using var dialog = ((MainForm)form).CreateStickerDialog(Photo());

            Assert.Contains("photo.jpg", dialog.Text);
            Assert.Equal("isnet-general-use", ToolModels.Cutout.Id);
            Assert.Equal(179, ToolModels.Cutout.Megabytes);
            Assert.Equal(64, ToolModels.Cutout.Sha256.Length);
            Assert.EndsWith("isnet-general-use.onnx", ToolModels.Cutout.File.Url);
            await Task.CompletedTask;
        }, seconds: 60);
    }
}
