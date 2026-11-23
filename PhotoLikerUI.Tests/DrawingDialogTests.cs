using System.Drawing.Imaging;
using System.Reflection;
using PhotoLiker.Core;
using PhotoLiker.Drawing;
using PhotoLiker.Drawing.Ocr;
using PhotoLiker.Effects.Models;
using PhotoLiker.Search;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The Draw window opened for real: it shows a photo around which it is sized, the tools put strokes, notes and crops on it through the canvas (the mouse is driven the way the window's own handlers are),
/// every step can be taken back and brought back, Save writes over the photo (with its dates and its notes) and Save as makes another file, and Scan text reads the picture (here with a reader that needs no
/// model; the real models are tried where they are).
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class DrawingDialogTests : IDisposable
{
    private static readonly DateTime FileDate = new(2020, 5, 6, 7, 8, 9, DateTimeKind.Utc);
    private static readonly DateTime Taken = new(2009, 9, 1, 14, 30, 0);

    private readonly TempFolder _folder = new();

    public DrawingDialogTests() => new GlobalConfig { ColorizeBar = false }.Save();

    public void Dispose() => _folder.Dispose();

    /// <summary>A grey photo (480 by 360) that says it was taken on 1 Sep 2009, with the dates of a file made on 6 May 2020.</summary>
    private string Photo(string name = "photo.jpg", int width = 480, int height = 360, bool pattern = false)
    {
        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.FromArgb(128, 128, 128));
            if (pattern)
            {
                // Something that is not one colour all over, so that a picture that was coded again would not be the same bytes
                using var sky = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, width, height), Color.FromArgb(30, 90, 200), Color.FromArgb(240, 200, 60), 35f);
                graphics.FillRectangle(sky, 0, 0, width, height);
                graphics.FillEllipse(Brushes.DarkRed, width / 5, height / 4, width / 3, height / 2);
                graphics.FillRectangle(Brushes.White, width / 2, height / 8, width / 4, height / 6);
            }
        }

        var path = _folder.File(name);
        bitmap.Save(path, ImageFormat.Jpeg);
        JpegExif.Save(path, new ExifChange { Original = Taken });
        File.SetCreationTimeUtc(path, FileDate);
        File.SetLastWriteTimeUtc(path, FileDate);
        return path;
    }

    private int _modelsAsked;
    private bool _modelsAvailable = true;

    private DrawingDialog Create(string photo, bool dark = false)
    {
        var dialog = new DrawingDialog(photo, dark, new EffectModels(_folder.File("models")), () =>
        {
            _modelsAsked++;
            return Task.FromResult(_modelsAvailable);
        });
        Quiet(dialog);
        return dialog;
    }

    /// <summary>
    /// No window of the test may ask the person anything (the real questions would stay on the screen until somebody answered them): the window is closed without saving, and no place is picked for a copy.
    /// A test that is about a question puts its own answer in.
    /// </summary>
    private static void Quiet(DrawingDialog dialog)
    {
        dialog.AskSaveChanges = () => DialogResult.No;
        dialog.AskSaveAsPath = _ => null;
        dialog.AskColor = _ => null;
    }

    /// <summary>A stroke dragged over places of the picture (in its pixels) with the left button, as the mouse does it.</summary>
    private static void Drag(DrawingDialog dialog, params PointF[] places)
    {
        var canvas = dialog.Canvas;
        var points = places.Select(place => Point.Round(canvas.ToControl(place))).ToArray();
        canvas.PointerDown(points[0]);
        foreach (var point in points.Skip(1))
            canvas.PointerMove(point);
        canvas.PointerUp(points[^1]);
    }

    private static Color At(DrawingDialog dialog, int x, int y) => dialog.Session.Picture.At(x, y);

    private static bool IsClose(Color actual, Color expected, int tolerance = 12) =>
        Math.Abs(actual.R - expected.R) <= tolerance && Math.Abs(actual.G - expected.G) <= tolerance && Math.Abs(actual.B - expected.B) <= tolerance;

    private static void Press(Form form, Keys keys) =>
        typeof(DrawingDialog).GetMethod("ProcessCmdKey", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, [new Message(), keys]);

    /// <summary>
    /// A file the program has just written may still be open for a moment (the virus scanner of the machine, the indexer): the test reads it once nobody has it. On the machines that build the program a read
    /// right after the write is what fails now and then.
    /// </summary>
    private static async Task<byte[]> ReadWhenWritten(string path)
    {
        Assert.True(await Ui.FileIsWritten(path), $"{path} is still open");
        return File.ReadAllBytes(path);
    }

    /// <summary>The text on the clipboard, or null when it cannot be got (another program holds the clipboard, or the session has none): a test of ours is not to fail for that.</summary>
    private static async Task<string?> ClipboardText()
    {
        for (var attempt = 0; attempt < 25; attempt++)
        {
            try
            {
                if (Clipboard.ContainsText())
                    return Clipboard.GetText();
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // held by another program for a moment
            }
            await Task.Delay(100);
        }
        return null;
    }

    private static readonly Color Red = Color.FromArgb(230, 30, 40);
    private static readonly Color Blue = Color.FromArgb(30, 110, 230);

    // ── Opening ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheWindow_ShowsThePhotoItIsAround_WithNothingToUndo_AndThePenReady()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), form =>
        {
            var dialog = (DrawingDialog)form;
            Assert.Contains("photo.jpg", dialog.Text);
            Assert.Equal(new Size(480, 360), new Size(dialog.Session.Width, dialog.Session.Height));
            Assert.Equal("480 x 360 pixels", dialog.InfoText);
            Assert.Equal(DrawTool.Pen, dialog.CurrentTool);
            Assert.True(dialog.PenButton.Checked);
            Assert.False(dialog.UndoButton.Enabled);
            Assert.False(dialog.RedoButton.Enabled);
            Assert.True(dialog.SaveButton.Enabled);
            Assert.True(dialog.ScanButton.Enabled);
            Assert.False(dialog.ApplyCropButton.Visible);
            Assert.False(dialog.ScanPanel.Visible);
            Assert.False(dialog.HasUnsavedChanges);
            Assert.True(dialog.Canvas.PictureBounds.Width > 0 && dialog.Canvas.ViewScale > 0.99f, "a photo that fits is shown as large as it is");
            Assert.True(dialog.ColorButtons[0].Checked, "the first colour is the one picked");
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void TheWindow_IsAsLargeAsThePhotoNeeds_AndALargePhotoIsShownSmallerToFit()
    {
        var photo = Photo("big.jpg", 4000, 3000);
        Ui.Run(() => Create(photo), form =>
        {
            var dialog = (DrawingDialog)form;
            Assert.True(dialog.Canvas.ViewScale < 0.8f, $"the photo is shown smaller than it is: {dialog.Canvas.ViewScale}");
            Assert.True(dialog.Canvas.PictureBounds.Width <= dialog.Canvas.Width && dialog.Canvas.PictureBounds.Height <= dialog.Canvas.Height);
            Assert.Equal("4000 x 3000 pixels", dialog.InfoText);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void APhotoThatCannotBeRead_IsRefused_WithTheReason()
    {
        var path = _folder.File("broken.jpg");
        File.WriteAllText(path, "this is not a picture");

        var ex = Assert.Throws<NotSupportedException>(() => new DrawingDialog(path, false, new EffectModels(_folder.File("models")), () => Task.FromResult(true)));

        Assert.Contains("not a picture", ex.Message);
    }

    // ── Drawing ─────────────────────────────────────────────────────────────

    [Fact]
    public void ThePen_DrawsAStrokeInTheColourPicked_AndItCanBeTakenBack()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), form =>
        {
            var dialog = (DrawingDialog)form;
            Drag(dialog, new PointF(100, 100), new PointF(200, 100), new PointF(300, 140));

            Assert.True(IsClose(At(dialog, 150, 100), Red), $"the stroke is red where it was drawn: {At(dialog, 150, 100)}");
            Assert.True(IsClose(At(dialog, 150, 200), Color.FromArgb(128, 128, 128)), "the rest of the photo is as it was");
            Assert.True(dialog.UndoButton.Enabled);
            Assert.True(dialog.HasUnsavedChanges);
            var stroke = Assert.IsType<StrokeStep>(Assert.Single(dialog.Session.Steps));
            Assert.Equal(Red, stroke.Colour);
            Assert.Equal(5f, stroke.Width, 0.6f);

            dialog.UndoButton.PerformClick();
            Assert.True(IsClose(At(dialog, 150, 100), Color.FromArgb(128, 128, 128)), "taking it back leaves the photo");
            Assert.False(dialog.UndoButton.Enabled);
            Assert.True(dialog.RedoButton.Enabled);

            dialog.RedoButton.PerformClick();
            Assert.True(IsClose(At(dialog, 150, 100), Red), "bringing it back draws it again");
            Assert.False(dialog.RedoButton.Enabled);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void AColourOnTheBar_ChangesWhatIsDrawnNext_AndWhatWasDrawnStaysAsItWas()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), form =>
        {
            var dialog = (DrawingDialog)form;
            Drag(dialog, new PointF(50, 60), new PointF(150, 60));
            dialog.ColorButtons[4].PerformClick(); // blue
            Drag(dialog, new PointF(50, 120), new PointF(150, 120));

            Assert.True(IsClose(At(dialog, 100, 60), Red));
            Assert.True(IsClose(At(dialog, 100, 120), Blue));
            Assert.True(dialog.ColorButtons[4].Checked);
            Assert.False(dialog.ColorButtons[0].Checked);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void MoreColours_AsksForAColour_AndUsesIt()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), form =>
        {
            var dialog = (DrawingDialog)form;
            dialog.AskColor = _ => Color.FromArgb(10, 200, 100);
            dialog.MoreColorsButton.PerformClick();
            Drag(dialog, new PointF(50, 60), new PointF(150, 60));

            Assert.True(IsClose(At(dialog, 100, 60), Color.FromArgb(10, 200, 100)));
            Assert.True(dialog.MoreColorsButton.Checked);
            Assert.All(dialog.ColorButtons, button => Assert.False(button.Checked));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void MoreColours_Declined_ChangesNothing()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), form =>
        {
            var dialog = (DrawingDialog)form;
            dialog.AskColor = _ => null;
            dialog.MoreColorsButton.PerformClick();

            Assert.False(dialog.MoreColorsButton.Checked);
            Assert.True(dialog.ColorButtons[0].Checked);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void TheWidthSlider_SetsHowThickTheLineIs_AsItLooksOnTheScreen()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), form =>
        {
            var dialog = (DrawingDialog)form;
            dialog.WidthSlider.Value = 20;
            Drag(dialog, new PointF(50, 100), new PointF(300, 100));

            Assert.True(IsClose(At(dialog, 100, 92), Red), "10 pixels above the middle is inside a line of 20");
            Assert.True(IsClose(At(dialog, 100, 112), Color.FromArgb(128, 128, 128)), "12 below the middle is outside it");
            Assert.Equal(20, dialog.PenWidthValue);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void OnAPhotoShownSmaller_TheLineIsAsThickAsItLooksButTheStrokeIsInThePixelsOfThePhoto()
    {
        var photo = Photo("big.jpg", 2400, 1800);
        Ui.Run(() => Create(photo), form =>
        {
            var dialog = (DrawingDialog)form;
            var scale = dialog.Canvas.ViewScale;
            Assert.True(scale < 0.6f);
            Drag(dialog, new PointF(200, 300), new PointF(1200, 300));

            var stroke = Assert.IsType<StrokeStep>(Assert.Single(dialog.Session.Steps));
            Assert.Equal(5f / scale, stroke.Width, 1f);
            Assert.True(stroke.Points.All(point => point.Y is > 290 and < 310));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void TheHighlighter_IsWideAndLetsThePhotoShowThrough()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), form =>
        {
            var dialog = (DrawingDialog)form;
            dialog.HighlighterButton.PerformClick();
            Drag(dialog, new PointF(50, 100), new PointF(300, 100));

            var stroke = Assert.IsType<StrokeStep>(Assert.Single(dialog.Session.Steps));
            Assert.Equal(DrawingCanvas.HighlighterAlpha, stroke.Colour.A);
            Assert.Equal(5f * DrawingCanvas.HighlighterWidthFactor, stroke.Width, 0.6f);
            var inside = At(dialog, 150, 100);
            Assert.True(inside.R > 128 && inside.R < 230, $"red over grey shows both: {inside}");
            Assert.True(inside.G < 128, $"the red takes some green away: {inside}");
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void TheLine_IsStraightFromWhereTheButtonWentDownToWhereItWentUp_WhateverTheMouseDidBetween()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), form =>
        {
            var dialog = (DrawingDialog)form;
            dialog.LineButton.PerformClick();
            Drag(dialog, new PointF(50, 50), new PointF(300, 300), new PointF(60, 280), new PointF(400, 100));

            var stroke = Assert.IsType<StrokeStep>(Assert.Single(dialog.Session.Steps));
            Assert.Equal(2, stroke.Points.Count);
            Assert.Equal(50f, stroke.Points[0].X, 1f);
            Assert.Equal(400f, stroke.Points[1].X, 1f);
            Assert.True(IsClose(At(dialog, 225, 75), Red), "on the straight line");
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void AClickWithoutADrag_MakesADot()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), form =>
        {
            var dialog = (DrawingDialog)form;
            dialog.Canvas.PointerDown(Point.Round(dialog.Canvas.ToControl(new PointF(100, 100))));
            dialog.Canvas.PointerUp(Point.Round(dialog.Canvas.ToControl(new PointF(100, 100))));

            Assert.True(IsClose(At(dialog, 100, 100), Red));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void DrawingOutsideThePhoto_StaysInsideIt()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), form =>
        {
            var dialog = (DrawingDialog)form;
            Drag(dialog, new PointF(-80, 100), new PointF(600, 100));

            var stroke = Assert.IsType<StrokeStep>(Assert.Single(dialog.Session.Steps));
            Assert.All(stroke.Points, point => Assert.InRange(point.X, 0, 480));
            Assert.True(IsClose(At(dialog, 0, 100), Red));
            Assert.True(IsClose(At(dialog, 479, 100), Red));
            return Task.CompletedTask;
        });
    }

    // ── Turning ─────────────────────────────────────────────────────────────

    /// <summary>The pixels of the file as it is stored (GDI+ does not turn a photo by its Exif).</summary>
    private static byte[] StoredPixels(string path, out Size size)
    {
        using var bitmap = new Bitmap(path);
        size = bitmap.Size;
        var data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            var bytes = new byte[Math.Abs(data.Stride) * bitmap.Height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static int OrientationOf(string path)
    {
        using var stream = File.OpenRead(path);
        return JpegOrientation.Read(stream);
    }

    private static DateTime? TakenOf(string path)
    {
        using var stream = File.OpenRead(path);
        return ExifReader.Read(stream).Original;
    }

    [Fact]
    public void TheRotateButtons_TurnThePictureAQuarter_AsAStepThatCanBeTakenBack()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), form =>
        {
            var dialog = (DrawingDialog)form;
            Assert.Equal("Rotate left", dialog.RotateLeftButton.Text);
            Assert.Equal("Rotate right", dialog.RotateRightButton.Text);

            dialog.RotateRightButton.PerformClick();
            Assert.Equal(new Size(360, 480), new Size(dialog.Session.Width, dialog.Session.Height));
            Assert.Equal("360 x 480 pixels", dialog.InfoText);
            Assert.True(dialog.HasUnsavedChanges);
            Assert.True(dialog.UndoButton.Enabled);
            Assert.IsType<RotateStep>(Assert.Single(dialog.Session.Steps));

            dialog.UndoButton.PerformClick();
            Assert.Equal(new Size(480, 360), new Size(dialog.Session.Width, dialog.Session.Height));

            dialog.RotateLeftButton.PerformClick();
            Assert.Equal(-1, ((RotateStep)dialog.Session.Steps[0]).QuarterTurns);
            Assert.Equal(new Size(360, 480), new Size(dialog.Session.Width, dialog.Session.Height));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void CtrlPlusAndCtrlMinus_TurnThePictureAsTheyTurnAPhotoInTheMainWindow()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), form =>
        {
            var dialog = (DrawingDialog)form;

            Press(dialog, Keys.Control | Keys.Oemplus);
            Assert.Equal(new Size(360, 480), new Size(dialog.Session.Width, dialog.Session.Height));
            Press(dialog, Keys.Control | Keys.Add);
            Assert.Equal(new Size(480, 360), new Size(dialog.Session.Width, dialog.Session.Height));
            Press(dialog, Keys.Control | Keys.OemMinus);
            Assert.Equal(new Size(360, 480), new Size(dialog.Session.Width, dialog.Session.Height));
            Press(dialog, Keys.Control | Keys.Subtract);
            Assert.Equal(new Size(480, 360), new Size(dialog.Session.Width, dialog.Session.Height));
            Assert.Equal(4, dialog.Session.Steps.Count);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void ATurn_ClearsTheLinesAScanFound()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (DrawingDialog)form;
            dialog.ScanOverride = (_, _, _) => Found("Hello");
            dialog.ScanButton.PerformClick();
            Assert.True(await Ui.Until(() => dialog.ScanPanel.Visible && !dialog.IsBusy));
            Assert.Single(dialog.Canvas.Marks);

            dialog.RotateRightButton.PerformClick();

            Assert.Empty(dialog.Canvas.Marks); // they were where the picture was before the turn
        });
    }

    [Fact]
    public void Drawing_AfterATurn_IsOnTheTurnedPicture()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), form =>
        {
            var dialog = (DrawingDialog)form;
            dialog.RotateRightButton.PerformClick(); // 360 wide, 480 high
            Drag(dialog, new PointF(100, 420), new PointF(300, 420)); // a row that the photo, 360 high, did not have

            Assert.True(IsClose(At(dialog, 200, 420), Red));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void Save_OfAPhotoThatWasOnlyTurned_WritesTheTurnIntoItsExif_AndNothingElseChanges()
    {
        var photo = Photo(pattern: true);
        var pixelsBefore = StoredPixels(photo, out var sizeBefore);
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (DrawingDialog)form;
            dialog.RotateRightButton.PerformClick();

            Assert.True(await dialog.SaveAsync(), dialog.StatusText);

            await ReadWhenWritten(photo);
            Assert.Equal(6, OrientationOf(photo)); // "turn a quarter clockwise to view"
            var pixelsAfter = StoredPixels(photo, out var sizeAfter);
            Assert.Equal(sizeBefore, sizeAfter);          // the picture was not coded again: the same grid of pixels...
            Assert.Equal(pixelsBefore, pixelsAfter);      // ... and the very same pixels
            Assert.Equal(Taken, TakenOf(photo));          // the date taken stays
            Assert.Equal(FileDate, File.GetCreationTimeUtc(photo));
            Assert.Equal(FileDate, File.GetLastWriteTimeUtc(photo));

            var shown = PhotoLiker.Imaging.WicPhotoDecoder.DecodeFull(File.ReadAllBytes(photo));
            Assert.Equal((360, 480), (shown.Width, shown.Height)); // and everyone who turns a photo by its Exif sees it turned

            Assert.True(dialog.PhotoWasChanged);
            Assert.Equal(1, dialog.LosslessTurns);
            Assert.False(dialog.HasUnsavedChanges);
        });
    }

    [Fact]
    public void ATurnThatIsTakenBack_IsSavedAsNothingAtAll()
    {
        var photo = Photo(pattern: true);
        var before = File.ReadAllBytes(photo);
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (DrawingDialog)form;
            dialog.RotateRightButton.PerformClick();
            dialog.RotateLeftButton.PerformClick();

            Assert.True(await dialog.SaveAsync(), dialog.StatusText);

            Assert.Equal(before, await ReadWhenWritten(photo));
            Assert.False(dialog.PhotoWasChanged);
            Assert.Equal(FileDate, File.GetLastWriteTimeUtc(photo));
            Assert.False(dialog.HasUnsavedChanges);
        });
    }

    [Fact]
    public void TwoTurnsSavedOneAfterTheOther_AreAddedUp_AndATurnBackIsWrittenToo()
    {
        var photo = Photo(pattern: true);
        var pixelsBefore = StoredPixels(photo, out _);
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (DrawingDialog)form;
            dialog.RotateRightButton.PerformClick();
            Assert.True(await dialog.SaveAsync(), dialog.StatusText);
            Assert.Equal(6, OrientationOf(photo));

            dialog.RotateRightButton.PerformClick();
            Assert.True(await dialog.SaveAsync(), dialog.StatusText);
            Assert.Equal(3, OrientationOf(photo)); // two quarters: upside down
            Assert.Equal(2, dialog.LosslessTurns);

            dialog.UndoButton.PerformClick(); // the second turn
            dialog.UndoButton.PerformClick(); // and the first
            Assert.True(await dialog.SaveAsync(), dialog.StatusText);
            Assert.Equal(1, OrientationOf(photo));
            Assert.Equal(0, dialog.LosslessTurns);

            await ReadWhenWritten(photo);
            Assert.Equal(pixelsBefore, StoredPixels(photo, out _));
            Assert.Equal(FileDate, File.GetLastWriteTimeUtc(photo));
            Assert.Equal(Taken, TakenOf(photo));
        });
    }

    [Fact]
    public void Save_OfATurnAndADrawing_WritesThePixels_AndKeepsTheDateTakenAndTheDatesOfTheFile()
    {
        var photo = Photo(pattern: true);
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (DrawingDialog)form;
            dialog.RotateRightButton.PerformClick();
            Drag(dialog, new PointF(60, 300), new PointF(300, 300));

            Assert.True(await dialog.SaveAsync(), dialog.StatusText);

            await ReadWhenWritten(photo);
            StoredPixels(photo, out var size);
            Assert.Equal(new Size(360, 480), size);      // the pixels were turned, so the file is portrait as it is stored
            Assert.Equal(1, OrientationOf(photo));        // and says nothing of a turn
            Assert.Equal(Taken, TakenOf(photo));
            Assert.Equal(FileDate, File.GetCreationTimeUtc(photo));
            Assert.Equal(FileDate, File.GetLastWriteTimeUtc(photo));
            Assert.Null(dialog.LosslessTurns);
            Assert.True(dialog.PhotoWasChanged);
        });
    }

    [Fact]
    public void ATurnAfterTheDrawingWasSaved_IsWrittenAsPixelsToo_NotAddedToTheExifOfAFileThatWasCodedAgain()
    {
        var photo = Photo(pattern: true);
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (DrawingDialog)form;
            Drag(dialog, new PointF(60, 300), new PointF(300, 300));
            Assert.True(await dialog.SaveAsync(), dialog.StatusText);

            dialog.RotateRightButton.PerformClick();
            Assert.True(await dialog.SaveAsync(), dialog.StatusText);

            await ReadWhenWritten(photo);
            StoredPixels(photo, out var size);
            Assert.Equal(new Size(360, 480), size);
            Assert.Equal(1, OrientationOf(photo));
            Assert.Null(dialog.LosslessTurns);
        });
    }

    [Fact]
    public void ATurnedPhotoThatIsNotAJpeg_IsWrittenAsPixels()
    {
        var png = _folder.File("photo.png");
        using (var bitmap = new Bitmap(120, 80))
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.SteelBlue);
            bitmap.Save(png, ImageFormat.Png);
        }

        Ui.Run(() => Create(png), async form =>
        {
            var dialog = (DrawingDialog)form;
            dialog.RotateLeftButton.PerformClick();

            Assert.True(await dialog.SaveAsync(), dialog.StatusText);

            await ReadWhenWritten(png);
            using var saved = new Bitmap(png);
            Assert.Equal(new Size(80, 120), saved.Size);
            Assert.Null(dialog.LosslessTurns);
        });
    }

    [Fact]
    public void ATurnSavedAsACopy_WritesThePixelsOfTheCopy_AndLeavesThePhotoAlone()
    {
        var photo = Photo(pattern: true);
        var before = File.ReadAllBytes(photo);
        var copy = _folder.File("turned.jpg");
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (DrawingDialog)form;
            dialog.AskSaveAsPath = _ => copy;
            dialog.RotateRightButton.PerformClick();

            Assert.True(await dialog.SaveAsAsync(), dialog.StatusText);

            Assert.Equal(before, await ReadWhenWritten(photo));
            await ReadWhenWritten(copy);
            StoredPixels(copy, out var size);
            Assert.Equal(new Size(360, 480), size);
            Assert.Equal(Taken, TakenOf(copy));
            Assert.False(dialog.PhotoWasChanged);
        });
    }

    // ── Text ────────────────────────────────────────────────────────────────

    [Fact]
    public void TheTextTool_PutsTheTypedTextWhereItWasClicked_InTheColourPicked()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), form =>
        {
            var dialog = (DrawingDialog)form;
            dialog.ColorButtons[6].PerformClick(); // black
            dialog.TextButton.PerformClick();
            dialog.WidthSlider.Value = 40; // the size of the letters
            dialog.Canvas.PointerDown(Point.Round(dialog.Canvas.ToControl(new PointF(60, 80))));
            dialog.Canvas.PointerUp(Point.Round(dialog.Canvas.ToControl(new PointF(60, 80))));

            Assert.True(dialog.Canvas.IsTyping, "a box to type in is there");
            dialog.Canvas.NoteBox!.Text = "HELLO";
            dialog.Canvas.CommitNote();

            Assert.False(dialog.Canvas.IsTyping);
            var step = Assert.IsType<TextStep>(Assert.Single(dialog.Session.Steps));
            Assert.Equal("HELLO", step.Text);
            Assert.Equal(Color.FromArgb(255, 0, 0, 0), step.Colour);
            Assert.InRange(step.Position.X, 50, 62);
            Assert.InRange(step.Position.Y, 70, 82);

            // Some pixels in the line of the text are black now: the letters are there
            var dark = 0;
            for (var y = 80; y < 125; y++)
            {
                for (var x = 60; x < 240; x++)
                {
                    if (At(dialog, x, y).R < 40)
                        dark++;
                }
            }
            Assert.True(dark > 300, $"the letters are drawn ({dark} dark pixels)");
            Assert.True(dialog.HasUnsavedChanges);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void ANoteThatIsCancelled_OrSaysNothing_PutsNothingOnThePhoto()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), form =>
        {
            var dialog = (DrawingDialog)form;
            dialog.TextButton.PerformClick();
            var place = Point.Round(dialog.Canvas.ToControl(new PointF(60, 80)));

            dialog.Canvas.PointerDown(place);
            dialog.Canvas.PointerUp(place);
            dialog.Canvas.NoteBox!.Text = "typed";
            Press(dialog, Keys.Escape);
            Assert.False(dialog.Canvas.IsTyping);

            dialog.Canvas.PointerDown(place);
            dialog.Canvas.PointerUp(place);
            dialog.Canvas.NoteBox!.Text = "   ";
            Press(dialog, Keys.Control | Keys.Enter);

            Assert.Empty(dialog.Session.Steps);
            Assert.False(dialog.IsDisposed);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void ANoteBeingTyped_IsPutOnThePhotoWhenAnotherToolIsPicked()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), form =>
        {
            var dialog = (DrawingDialog)form;
            dialog.TextButton.PerformClick();
            var place = Point.Round(dialog.Canvas.ToControl(new PointF(60, 80)));
            dialog.Canvas.PointerDown(place);
            dialog.Canvas.PointerUp(place);
            dialog.Canvas.NoteBox!.Text = "kept";

            dialog.PenButton.PerformClick();

            Assert.False(dialog.Canvas.IsTyping);
            Assert.IsType<TextStep>(Assert.Single(dialog.Session.Steps));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void TheSliderIsTheSizeOfTheTextForTheTextTool_AndTheWidthOfTheLineForTheOthers()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), form =>
        {
            var dialog = (DrawingDialog)form;
            Assert.Equal("Width", dialog.WidthLabel.Text);
            dialog.WidthSlider.Value = 12;

            dialog.TextButton.PerformClick();
            Assert.Equal("Size", dialog.WidthLabel.Text);
            Assert.Equal(28, dialog.WidthSlider.Value);
            dialog.WidthSlider.Value = 50;

            dialog.PenButton.PerformClick();
            Assert.Equal("Width", dialog.WidthLabel.Text);
            Assert.Equal(12, dialog.WidthSlider.Value);
            Assert.Equal(12, dialog.PenWidthValue);
            return Task.CompletedTask;
        });
    }

    // ── Crop ────────────────────────────────────────────────────────────────

    [Fact]
    public void TheCropTool_CutsThePhotoToTheFrameThatWasDragged_AndItCanBeTakenBack()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), form =>
        {
            var dialog = (DrawingDialog)form;
            dialog.CropButton.PerformClick();
            Assert.True(dialog.CropButton.Checked);
            Assert.True(dialog.ApplyCropButton.Visible);
            Assert.False(dialog.ApplyCropButton.Enabled, "a frame that is the whole photo cuts nothing");

            Drag(dialog, new PointF(100, 60), new PointF(300, 200));
            Assert.Equal(new Rectangle(100, 60, 200, 140), dialog.Canvas.CropArea);
            Assert.True(dialog.ApplyCropButton.Enabled);
            Assert.Contains("200 x 140", dialog.StatusText);

            dialog.ApplyCropButton.PerformClick();
            Assert.Equal(new Size(200, 140), new Size(dialog.Session.Width, dialog.Session.Height));
            Assert.Equal("200 x 140 pixels", dialog.InfoText);
            Assert.Null(dialog.Canvas.CropArea);
            Assert.True(dialog.HasUnsavedChanges);

            dialog.UndoButton.PerformClick();
            Assert.Equal(new Size(480, 360), new Size(dialog.Session.Width, dialog.Session.Height));
            Assert.Equal("480 x 360 pixels", dialog.InfoText);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void TheFrame_CanBeMovedAndItsEdgesDragged_BeforeItIsApplied()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), form =>
        {
            var dialog = (DrawingDialog)form;
            dialog.CropButton.PerformClick();
            Drag(dialog, new PointF(100, 60), new PointF(300, 200));

            Drag(dialog, new PointF(200, 130), new PointF(230, 150)); // inside: moves the frame
            Assert.Equal(new Rectangle(130, 80, 200, 140), dialog.Canvas.CropArea);

            Drag(dialog, new PointF(330, 150), new PointF(380, 150)); // the right edge
            Assert.Equal(new Rectangle(130, 80, 250, 140), dialog.Canvas.CropArea);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void EnterKeepsTheFrame_AndEscapeThrowsItAway()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), form =>
        {
            var dialog = (DrawingDialog)form;
            dialog.CropButton.PerformClick();
            Drag(dialog, new PointF(100, 60), new PointF(300, 200));

            Press(dialog, Keys.Escape);
            Assert.Null(dialog.Canvas.CropArea);
            Assert.False(dialog.IsDisposed);
            Assert.Empty(dialog.Session.Steps);

            Drag(dialog, new PointF(100, 60), new PointF(300, 200));
            Press(dialog, Keys.Enter);
            Assert.Equal(new Size(200, 140), new Size(dialog.Session.Width, dialog.Session.Height));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void Drawing_AfterACrop_IsInThePixelsOfTheCroppedPhoto()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), form =>
        {
            var dialog = (DrawingDialog)form;
            dialog.CropButton.PerformClick();
            Drag(dialog, new PointF(100, 60), new PointF(300, 200));
            dialog.ApplyCropButton.PerformClick();

            dialog.PenButton.PerformClick();
            Drag(dialog, new PointF(10, 70), new PointF(190, 70));

            Assert.True(IsClose(At(dialog, 100, 70), Red));
            Assert.Equal(2, dialog.Session.Steps.Count);
            return Task.CompletedTask;
        });
    }

    // ── Undo and redo from the keyboard ─────────────────────────────────────

    [Fact]
    public void CtrlZ_AndCtrlY_TakeBackAndBringBack()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), form =>
        {
            var dialog = (DrawingDialog)form;
            Drag(dialog, new PointF(50, 60), new PointF(150, 60));

            Press(dialog, Keys.Control | Keys.Z);
            Assert.Empty(dialog.Session.Steps);

            Press(dialog, Keys.Control | Keys.Y);
            Assert.Single(dialog.Session.Steps);
            return Task.CompletedTask;
        });
    }

    // ── Saving ──────────────────────────────────────────────────────────────

    [Fact]
    public void Save_WritesTheDrawingOverThePhoto_KeepingItsDatesAndItsDateTaken()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (DrawingDialog)form;
            var before = File.ReadAllBytes(photo);
            Drag(dialog, new PointF(50, 100), new PointF(300, 100));

            Assert.True(await dialog.SaveAsync(), dialog.StatusText);

            Assert.NotEqual(before, await ReadWhenWritten(photo));
            using (var saved = new Bitmap(photo))
            {
                Assert.Equal(new Size(480, 360), saved.Size);
                Assert.True(IsClose(saved.GetPixel(150, 100), Red, 30), $"the stroke is in the file: {saved.GetPixel(150, 100)}");
                Assert.True(IsClose(saved.GetPixel(150, 250), Color.FromArgb(128, 128, 128), 12));
            }

            Assert.Equal(FileDate, File.GetCreationTimeUtc(photo));
            Assert.Equal(FileDate, File.GetLastWriteTimeUtc(photo));
            using (var stream = File.OpenRead(photo))
                Assert.Equal(Taken, ExifReader.Read(stream).Original);

            Assert.False(dialog.HasUnsavedChanges);
            Assert.True(dialog.PhotoWasChanged);
            Assert.Equal(photo, dialog.SavedPath);
            Assert.StartsWith("Saved over", dialog.StatusText);
            Assert.False(File.Exists(photo + ".part"));
        });
    }

    [Fact]
    public void Save_WritesACroppedPhotoAtItsNewSize()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (DrawingDialog)form;
            dialog.CropButton.PerformClick();
            Drag(dialog, new PointF(100, 60), new PointF(300, 200));
            dialog.ApplyCropButton.PerformClick();

            Assert.True(await dialog.SaveAsync(), dialog.StatusText);

            await ReadWhenWritten(photo);
            using var saved = new Bitmap(photo);
            Assert.Equal(new Size(200, 140), saved.Size);
        });
    }

    [Fact]
    public void Save_WithNothingChanged_SaysSo_AndLeavesTheFileAlone()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (DrawingDialog)form;
            var before = File.ReadAllBytes(photo);

            Assert.False(await dialog.SaveAsync());

            Assert.Equal("There is nothing to save: the picture is as it was.", dialog.StatusText);
            Assert.Equal(before, File.ReadAllBytes(photo));
            Assert.False(dialog.PhotoWasChanged);
        });
    }

    [Fact]
    public void Save_OfAFormatThatCannotBeWritten_SendsThePersonToSaveAs_AndDoesNotTouchTheFile()
    {
        var photo = Photo();
        var webp = _folder.File("photo.webp");
        File.Copy(photo, webp); // the name says WebP, which Windows reads but cannot write; what is in it is a picture it can read
        Ui.Run(() => Create(webp), async form =>
        {
            var dialog = (DrawingDialog)form;
            var before = File.ReadAllBytes(webp);
            Drag(dialog, new PointF(50, 100), new PointF(300, 100));

            Assert.False(await dialog.SaveAsync());

            Assert.Equal("A WEBP file cannot be written over: use Save as.", dialog.StatusText);
            Assert.Equal(before, File.ReadAllBytes(webp));
            Assert.True(dialog.HasUnsavedChanges);
        });
    }

    [Fact]
    public void SaveAs_MakesAnotherFile_AndTheFormatIsTheOneTheNameSays_AndThePhotoIsAsItWas()
    {
        var photo = Photo();
        var copy = _folder.File("copy.png");
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (DrawingDialog)form;
            var before = File.ReadAllBytes(photo);
            string? suggested = null;
            dialog.AskSaveAsPath = name =>
            {
                suggested = name;
                return copy;
            };
            Drag(dialog, new PointF(50, 100), new PointF(300, 100));

            Assert.True(await dialog.SaveAsAsync(), dialog.StatusText);

            Assert.Equal(Path.Combine(_folder.Path, "photo (edited).jpg"), suggested);
            Assert.Equal(before, await ReadWhenWritten(photo));
            var bytes = await ReadWhenWritten(copy);
            Assert.True(bytes.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47 }), "a PNG");
            using (var saved = new Bitmap(copy))
                Assert.True(IsClose(saved.GetPixel(150, 100), Red, 8));
            Assert.Equal(copy, dialog.SavedPath);
            Assert.False(dialog.PhotoWasChanged);
            Assert.False(dialog.HasUnsavedChanges);
        });
    }

    [Fact]
    public void SaveAs_WhenNoPlaceIsPicked_SavesNothing()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (DrawingDialog)form;
            Drag(dialog, new PointF(50, 100), new PointF(300, 100));

            Assert.False(await dialog.SaveAsAsync());

            Assert.Null(dialog.SavedPath);
            Assert.True(dialog.HasUnsavedChanges);
        });
    }

    [Fact]
    public void SaveAs_ToAnExtensionThatIsNoFormat_GetsTheFormatOfThePhotoAdded()
    {
        var photo = Photo();
        var target = _folder.File("copy.xyz");
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (DrawingDialog)form;
            dialog.AskSaveAsPath = _ => target;
            Drag(dialog, new PointF(50, 100), new PointF(300, 100));

            Assert.True(await dialog.SaveAsAsync(), dialog.StatusText);

            Assert.True(await Ui.FileIsWritten(target + ".jpg"));
            Assert.False(File.Exists(target));
        });
    }

    // ── Closing ─────────────────────────────────────────────────────────────

    [Fact]
    public void Closing_WithUnsavedChanges_AsksWhetherToSave_NoLeavesThePhoto_CancelStays()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), form =>
        {
            var dialog = (DrawingDialog)form;
            var before = File.ReadAllBytes(photo);
            Drag(dialog, new PointF(50, 100), new PointF(300, 100));

            var asked = 0;
            dialog.AskSaveChanges = () =>
            {
                asked++;
                return DialogResult.Cancel;
            };
            dialog.CloseButton.PerformClick();
            Assert.Equal(1, asked);
            Assert.False(dialog.IsDisposed);
            Assert.True(dialog.Visible, "Cancel keeps the window open");

            dialog.AskSaveChanges = () =>
            {
                asked++;
                return DialogResult.No;
            };
            dialog.CloseButton.PerformClick();
            Assert.Equal(2, asked);
            Assert.Equal(before, File.ReadAllBytes(photo));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void Closing_WithUnsavedChanges_AndYes_SavesOverThePhotoFirst()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (DrawingDialog)form;
            var before = File.ReadAllBytes(photo);
            Drag(dialog, new PointF(50, 100), new PointF(300, 100));
            dialog.AskSaveChanges = () => DialogResult.Yes;

            dialog.CloseButton.PerformClick();

            Assert.NotEqual(before, await ReadWhenWritten(photo));
            Assert.True(dialog.PhotoWasChanged);
            Assert.Equal(FileDate, File.GetLastWriteTimeUtc(photo));
        });
    }

    [Fact]
    public void Closing_WithNothingChanged_AsksNothing()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), form =>
        {
            var dialog = (DrawingDialog)form;
            dialog.AskSaveChanges = () => throw new InvalidOperationException("nothing was changed, so nothing is asked");

            dialog.CloseButton.PerformClick();
            return Task.CompletedTask;
        });
    }

    // ── Scan text ───────────────────────────────────────────────────────────

    private static ScanResult Found(params string[] lines) => new(lines.Select((text, i) => new TextLine(text, 0.95f, TextRegion.FromCentre(new PointF(120, 40 + 50 * i), 160, 30, 0))).ToList());

    [Fact]
    public void ScanText_ShowsTheTextAtTheSide_MarksTheLines_AndCopiesIt()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (DrawingDialog)form;
            dialog.ScanOverride = (_, _, _) => Found("Hello World", "Привіт");

            dialog.ScanButton.PerformClick();
            Assert.True(await Ui.Until(() => dialog.ScanPanel.Visible && !dialog.IsBusy), $"the text is shown ({dialog.StatusText})");

            Assert.Equal("Hello World\r\nПривіт", dialog.ScanBox.Text);
            Assert.Equal(2, dialog.Canvas.Marks.Count);
            Assert.Equal("Found 2 lines of text.", dialog.StatusText);
            Assert.Equal(1, _modelsAsked);

            // The clipboard belongs to the whole session: where it cannot be had the window says so in its status line, and there is nothing more to check
            dialog.CopyButton.PerformClick();
            if (dialog.StatusText == DrawingStrings.StatusScanCopied)
            {
                Assert.Equal("Hello World\r\nПривіт", await ClipboardText() ?? "Hello World\r\nПривіт");

                dialog.ScanBox.Select(0, 5);
                dialog.CopyButton.PerformClick();
                Assert.Equal("Hello", await ClipboardText() ?? "Hello");
            }

            dialog.HideButton.PerformClick();
            Assert.False(dialog.ScanPanel.Visible);
            Assert.Empty(dialog.Canvas.Marks);
        });
    }

    [Fact]
    public void ScanText_ReadsTheFrameOfTheCropTool_WhenThereIsOne()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (DrawingDialog)form;
            Rectangle? scanned = null;
            dialog.ScanOverride = (_, area, _) =>
            {
                scanned = area;
                return Found("x");
            };
            dialog.CropButton.PerformClick();
            Drag(dialog, new PointF(100, 60), new PointF(300, 200));

            dialog.ScanButton.PerformClick();
            Assert.True(await Ui.Until(() => dialog.ScanPanel.Visible && !dialog.IsBusy));

            Assert.Equal(new Rectangle(100, 60, 200, 140), scanned);
            Assert.Equal(new Size(480, 360), new Size(dialog.Session.Width, dialog.Session.Height)); // the frame was not applied
        });
    }

    [Fact]
    public void ScanText_OfAPictureWithNoText_SaysSo_AndShowsNoPanel()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (DrawingDialog)form;
            dialog.ScanOverride = (_, _, _) => new ScanResult([]);

            dialog.ScanButton.PerformClick();
            Assert.True(await Ui.Until(() => dialog.StatusText == "No text was found." && !dialog.IsBusy), dialog.StatusText);

            Assert.False(dialog.ScanPanel.Visible);
        });
    }

    [Fact]
    public void ScanText_WhenTheModelsAreDeclined_SaysSo_AndReadsNothing()
    {
        var photo = Photo();
        _modelsAvailable = false;
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (DrawingDialog)form;
            var read = false;
            dialog.ScanOverride = (_, _, _) =>
            {
                read = true;
                return Found("x");
            };

            dialog.ScanButton.PerformClick();
            Assert.True(await Ui.Until(() => !dialog.IsBusy && dialog.StatusText.StartsWith("Scanning needs")), dialog.StatusText);

            Assert.False(read);
            Assert.False(dialog.ScanPanel.Visible);
            Assert.True(dialog.ScanButton.Enabled);
        });
    }

    [Fact]
    public void ScanText_ThatFails_SaysWhy_AndTheWindowGoesOn()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (DrawingDialog)form;
            dialog.ScanOverride = (_, _, _) => throw new InvalidDataException("the model is damaged");

            dialog.ScanButton.PerformClick();
            Assert.True(await Ui.Until(() => !dialog.IsBusy && dialog.StatusText.Contains("damaged")), dialog.StatusText);

            Assert.StartsWith("The text could not be scanned", dialog.StatusText);
            Drag(dialog, new PointF(50, 100), new PointF(300, 100)); // the window still works
            Assert.Single(dialog.Session.Steps);
        });
    }

    [Fact]
    public void WhileAScanRuns_ThePictureCannotBeChanged_AndEscapeStopsTheScan()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (DrawingDialog)form;
            var started = new ManualResetEventSlim();
            dialog.ScanOverride = (_, _, cancel) =>
            {
                started.Set();
                cancel.WaitHandle.WaitOne(20000);
                cancel.ThrowIfCancellationRequested();
                return Found("never");
            };

            dialog.ScanButton.PerformClick();
            Assert.True(await Ui.Until(() => started.IsSet && dialog.IsBusy));
            Assert.True(dialog.ProgressShown);
            Assert.False(dialog.Bar.Enabled);

            Drag(dialog, new PointF(50, 100), new PointF(300, 100));
            Assert.Empty(dialog.Session.Steps); // the picture is being read: nothing is drawn on it

            Press(dialog, Keys.Escape);
            Assert.True(await Ui.Until(() => !dialog.IsBusy), "the scan stops");

            Assert.Equal("The scan was stopped.", dialog.StatusText);
            Assert.False(dialog.ProgressShown);
            Assert.False(dialog.IsDisposed);
        });
    }

    [Fact]
    public void ScanText_ReadsRealText_WithTheRealModels()
    {
        var finder = ToolModels.OcrFinder.PathIn(RealModelsFolder);
        var reader = ToolModels.OcrReader.PathIn(RealModelsFolder);
        if (!File.Exists(finder) || !File.Exists(reader))
            return; // the models are downloaded when Scan text is first used; where they are not, there is nothing to run

        using var bitmap = new Bitmap(900, 300);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.FromArgb(235, 225, 190));
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            using var big = new Font("Segoe UI", 40, FontStyle.Bold);
            using var normal = new Font("Arial", 30);
            graphics.DrawString("Hello World, Photo Liker", big, Brushes.Black, 30, 30);
            graphics.DrawString("Вітаю! Це тестове фото з написом", normal, Brushes.DarkRed, 30, 150);
        }
        var photo = _folder.File("text.jpg");
        bitmap.Save(photo, ImageFormat.Jpeg);

        try
        {
            NativeRuntime.EnsureOnnxRuntimeCanLoad();
        }
        catch (Exception)
        {
            return; // an old C++ runtime that a shell extension of another program put in this process: ONNX Runtime cannot run in it (the window says so)
        }

        Ui.Run(() =>
        {
            var dialog = new DrawingDialog(photo, false, new EffectModels(RealModelsFolder), () => Task.FromResult(true));
            Quiet(dialog);
            return dialog;
        }, async form =>
        {
            var dialog = (DrawingDialog)form;
            dialog.ScanButton.PerformClick();
            Assert.True(await Ui.Until(() => !dialog.IsBusy, 120000), dialog.StatusText);

            Assert.True(dialog.ScanPanel.Visible, dialog.StatusText);
            Assert.Contains("Hello World", dialog.ScanBox.Text);
            Assert.Contains("тестове", dialog.ScanBox.Text);
            Assert.Equal(2, dialog.Canvas.Marks.Count);
        }, seconds: 180);
    }

    private static string RealModelsFolder { get; } =
        Environment.GetEnvironmentVariable("PHOTOLIKER_TEST_MODELS") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoLiker", "models");

    // ── Looks ───────────────────────────────────────────────────────────────

    [Fact]
    public void TheWindow_OpensInTheDarkTheme_WithTheSurroundDark()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo, dark: true), form =>
        {
            var dialog = (DrawingDialog)form;
            Assert.True(dialog.Canvas.Surround.GetBrightness() < 0.2f);
            Assert.True(dialog.BackColor.GetBrightness() < 0.3f);
            return Task.CompletedTask;
        });
    }
}
