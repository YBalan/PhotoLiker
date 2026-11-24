using System.ComponentModel;
using System.Drawing.Imaging;
using System.Reflection;
using PhotoLiker.Core;
using PhotoLiker.Effects.Models;
using PhotoLiker.Search;

namespace PhotoLikerUI.Tests;

/// <summary>
/// Where the Draw window is opened from: Ctrl+Shift+D and the last button of the toolbar of the main window (and what follows when the window wrote over the photo), and the Make a book window,
/// where the Draw button and a double-click on a photo open it for that photo.
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class DrawingIntegrationTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public DrawingIntegrationTests() => new GlobalConfig { ColorizeBar = false }.Save();

    public void Dispose() => _folder.Dispose();

    private static T Field<T>(object owner, string name) =>
        (T)typeof(MainForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;

    private static string StatusOf(Form form) => Field<ToolStripStatusLabel>(form, "toolStripStatusLabel1").Text ?? string.Empty;

    private string Photo(string name, string? folder = null, int shade = 128)
    {
        var path = Path.Combine(folder ?? _folder.Path, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = new Bitmap(320, 240);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.Clear(Color.FromArgb(shade, shade, shade));
        bitmap.Save(path, ImageFormat.Jpeg);
        var date = new DateTime(2020, 5, 1 + (name[0] - 'a'), 12, 0, 0, DateTimeKind.Utc); // a.jpg is the oldest, b.jpg the next
        File.SetCreationTimeUtc(path, date);
        File.SetLastWriteTimeUtc(path, date);
        return path;
    }

    private static void Press(Form form, Keys keys) =>
        form.BeginInvoke(() =>
        {
            typeof(MainForm).GetMethod("ProcessCmdKey", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, [new Message(), keys]);
        });

    private static void Quiet(DrawingDialog dialog)
    {
        dialog.AskSaveChanges = () => DialogResult.No;
        dialog.AskSaveAsPath = _ => null;
    }

    private static void Scribble(DrawingDialog dialog)
    {
        var canvas = dialog.Canvas;
        canvas.PointerDown(Point.Round(canvas.ToControl(new PointF(40, 100))));
        canvas.PointerMove(Point.Round(canvas.ToControl(new PointF(200, 100))));
        canvas.PointerUp(Point.Round(canvas.ToControl(new PointF(200, 100))));
    }

    // ── The main window ─────────────────────────────────────────────────────

    [Fact]
    public void TheDrawButton_IsTheSecondToLastButtonOfTheToolbar_AfterTheTelegramSticker_AndBeforeScanManuscripts()
    {
        Ui.Run(() => new MainForm(), async form =>
        {
            var toolbar = Ui.Controls<ToolStrip>(form).Single(strip => strip is not StatusStrip && strip.Items.Cast<ToolStripItem>().Any(item => item.Name == "colorizeToolStripButton"));
            var names = toolbar.Items.Cast<ToolStripItem>().Select(item => item.Name).ToList();

            Assert.Equal("manuscriptToolStripButton", names[^1]);
            Assert.Equal("drawToolStripButton", names[^2]);
            Assert.Equal("stickerToolStripButton", names[^3]);
            var button = toolbar.Items["drawToolStripButton"]!;
            Assert.Equal(DrawingStrings.ButtonText, button.Text);
            Assert.Equal(DrawingStrings.ButtonTooltip, button.ToolTipText);
            Assert.Contains("Ctrl+Shift+D", button.ToolTipText);
            Assert.NotNull(button.Image);
            Assert.Equal(ToolStripItemDisplayStyle.Image, button.DisplayStyle);
            await Task.CompletedTask;
        }, seconds: 60);
    }

    [Fact]
    public void CtrlShiftD_OpensTheDrawWindow_ForThePhotoThatIsShown_AndTheButtonDoesToo()
    {
        var photo = Photo("a.jpg");

        Ui.Run(() => new MainForm(_folder.Path), async form =>
        {
            Assert.True(await Ui.Until(() => Field<PictureBox>(form, "pictureBox1").Tag is string), "the photo is shown");

            Press(form, Keys.Control | Keys.Shift | Keys.D);
            Assert.True(await Ui.Until(() => Ui.Find("DrawingDialog") is not null), "the window opened");
            var dialog = (DrawingDialog)Ui.Find("DrawingDialog")!;
            Quiet(dialog);
            Assert.Equal(photo, dialog.PhotoPath, ignoreCase: true);
            dialog.CloseButton.PerformClick();
            Assert.True(await Ui.Until(() => Ui.Find("DrawingDialog") is null), "the window closed");

            var toolbar = Ui.Controls<ToolStrip>(form).Single(strip => strip is not StatusStrip && strip.Items["drawToolStripButton"] is not null);
            form.BeginInvoke(() => toolbar.Items["drawToolStripButton"]!.PerformClick());
            Assert.True(await Ui.Until(() => Ui.Find("DrawingDialog") is not null), "the button opened it too");
            Quiet((DrawingDialog)Ui.Find("DrawingDialog")!);
            Ui.Find("DrawingDialog")!.Close();
        }, seconds: 90);
    }

    [Fact]
    public void WithNothingShown_TheDrawWindowIsNotOpened_AndTheStatusSaysSo()
    {
        Ui.Run(() => new MainForm(), async form =>
        {
            Press(form, Keys.Control | Keys.Shift | Keys.D);

            Assert.True(await Ui.Until(() => StatusOf(form) == DrawingStrings.StatusNoPhoto), StatusOf(form));
            Assert.Null(Ui.Find("DrawingDialog"));
        }, seconds: 60);
    }

    [Fact]
    public void WhenTheWindowWritesOverThePhoto_TheMainWindowSaysSo_AndTheCopyOfThePhotoInTheLikedFolderKeepsItsPlace()
    {
        var photo = Photo("a.jpg");
        var before = File.ReadAllBytes(photo);

        Ui.Run(() => new MainForm(_folder.Path), async form =>
        {
            Assert.True(await Ui.Until(() => Field<PictureBox>(form, "pictureBox1").Tag is string), "the photo is shown");

            Press(form, Keys.Control | Keys.Shift | Keys.D);
            Assert.True(await Ui.Until(() => Ui.Find("DrawingDialog") is not null), "the window opened");
            var dialog = (DrawingDialog)Ui.Find("DrawingDialog")!;
            Quiet(dialog);
            Scribble(dialog);
            Assert.True(await dialog.SaveAsync(), dialog.StatusText);
            dialog.CloseButton.PerformClick();

            Assert.True(await Ui.Until(() => StatusOf(form).StartsWith("Saved ") && Ui.Find("DrawingDialog") is null), StatusOf(form));
            Assert.True(await Ui.FileIsWritten(photo), "the photo is still open");
            Assert.NotEqual(before, File.ReadAllBytes(photo));
            Assert.Contains("a.jpg", StatusOf(form));
        }, seconds: 90);
    }

    [Fact]
    public void ADrawWindowThatSavedOnlyACopy_LeavesThePhotoAlone()
    {
        var photo = Photo("a.jpg");
        var before = File.ReadAllBytes(photo);
        var copy = _folder.File("a copy.png");

        Ui.Run(() => new MainForm(_folder.Path), async form =>
        {
            Assert.True(await Ui.Until(() => Field<PictureBox>(form, "pictureBox1").Tag is string), "the photo is shown");

            Press(form, Keys.Control | Keys.Shift | Keys.D);
            Assert.True(await Ui.Until(() => Ui.Find("DrawingDialog") is not null), "the window opened");
            var dialog = (DrawingDialog)Ui.Find("DrawingDialog")!;
            Quiet(dialog);
            dialog.AskSaveAsPath = _ => copy;
            Scribble(dialog);
            Assert.True(await dialog.SaveAsAsync(), dialog.StatusText);
            dialog.CloseButton.PerformClick();

            Assert.True(await Ui.Until(() => Ui.Find("DrawingDialog") is null));
            Assert.True(await Ui.FileIsWritten(photo) && await Ui.FileIsWritten(copy), "the files are still open");
            Assert.Equal(before, File.ReadAllBytes(photo));
        }, seconds: 90);
    }

    // ── The models of Scan text ─────────────────────────────────────────────

    /// <summary>The window with the question about downloading a model answered "no" and written down (a test must not download anything, nor wait for a person: the machines that build the program have no models).</summary>
    private sealed class AskingMainForm(string folder) : MainForm(folder)
    {
        public readonly List<string> Asked = [];

        protected override bool ConfirmToolModelDownload(string feature, ToolModel model)
        {
            Asked.Add($"{model.Id}|{feature}");
            return false;
        }
    }

    /// <summary>The models of Scan text are not in the models folder of the tests (which are in the data folder of the program, a temporary one while testing).</summary>
    private static void TakeTheScanModelsAway()
    {
        foreach (var model in new[] { ToolModels.OcrFinder, ToolModels.OcrReader })
        {
            var target = model.PathIn(Path.Combine(AppPaths.DataFolder, "models"));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); // on a machine that never had the model the folder is not there, and deleting in it fails
            File.Delete(target);
        }
    }

    [Fact]
    public void TheModelsOfScanText_AreAskedForOnce_ForTheFirst_AndNothingIsAskedAfterANo()
    {
        TakeTheScanModelsAway();

        Ui.Run(() => new AskingMainForm(_folder.Path), async form =>
        {
            var main = (AskingMainForm)form;
            var ensure = typeof(MainForm).GetMethod("EnsureScanTextModelsAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

            var ready = await (Task<bool>)ensure.Invoke(form, [])!;

            Assert.False(ready);
            Assert.Equal([$"{ToolModels.OcrFinder.Id}|{ToolModelStrings.ScanTextFeature}"], main.Asked);
        }, seconds: 60);
    }

    [Fact]
    public void ScanText_WithTheModelsDeclined_SaysSo_AndReadsNothing()
    {
        var photo = Photo("a.jpg");
        TakeTheScanModelsAway();

        Ui.Run(() => new AskingMainForm(_folder.Path), async form =>
        {
            var main = (AskingMainForm)form;
            using var dialog = main.CreateDrawingDialog(photo);
            Quiet(dialog);

            dialog.ScanButton.PerformClick();
            Assert.True(await Ui.Until(() => !dialog.IsBusy && dialog.StatusText == DrawingStrings.StatusScanDeclined), dialog.StatusText);

            Assert.Single(main.Asked);
            Assert.False(dialog.ScanPanel.Visible);
        }, seconds: 60);
    }

    // ── The Make a book window ──────────────────────────────────────────────

    private BookDialog Book(Func<string, IWin32Window, bool>? draw, params PhotoFile[] loaded) =>
        new(_folder.Path, loaded, _folder.Liked, new Settings().Extensions, PhotoSortOrder.OldestFirst, dark: false, draw);

    private static async Task Listed(BookDialog dialog, int count) =>
        Assert.True(await Ui.Until(() => !dialog.IsLoading && dialog.Entries.Count == count, 30000), $"the {count} photos were listed (there are {dialog.Entries.Count})");

    [Fact]
    public void TheBookWindow_HasADrawButtonNextToTheTurnButtons_WhenItCanDraw()
    {
        Photo("a.jpg", _folder.Liked);

        Ui.Run(() => Book((_, _) => false), async form =>
        {
            var dialog = (BookDialog)form;
            await Listed(dialog, 1);

            var strip = dialog.DrawButton.Owner!;
            var names = strip.Items.Cast<ToolStripItem>().Select(item => item.Name).ToList();
            Assert.Equal(names.IndexOf("rotateRightBookButton") + 1, names.IndexOf("drawBookButton"));
            Assert.Equal(BookStrings.Draw, dialog.DrawButton.Text);
            Assert.Equal(BookStrings.DrawTip, dialog.DrawButton.ToolTipText);
            Assert.True(dialog.DrawButton.Enabled, "a photo is picked");
        });
    }

    [Fact]
    public void TheBookWindow_WithNoWayToDraw_HasNoDrawButton()
    {
        Photo("a.jpg", _folder.Liked);

        Ui.Run(() => Book(null), async form =>
        {
            var dialog = (BookDialog)form;
            await Listed(dialog, 1);

            Assert.Null(dialog.DrawButton.Owner);
        });
    }

    [Fact]
    public void TheDrawButton_OpensTheDrawWindowForThePhotoThatIsPicked()
    {
        Photo("a.jpg", _folder.Liked);
        Photo("b.jpg", _folder.Liked);
        var asked = new List<(string Path, IWin32Window Owner)>();

        Ui.Run(() => Book((path, owner) =>
        {
            asked.Add((path, owner));
            return false;
        }), async form =>
        {
            var dialog = (BookDialog)form;
            await Listed(dialog, 2);

            dialog.View.Select(1);
            dialog.DrawButton.PerformClick();

            var (path, owner) = Assert.Single(asked);
            Assert.Equal("b.jpg", Path.GetFileName(path));
            Assert.Same(dialog, owner);
        });
    }

    [Fact]
    public void TheDrawButton_IsOffWithNothingPicked()
    {
        Photo("a.jpg", _folder.Liked);

        Ui.Run(() => Book((_, _) => false), async form =>
        {
            var dialog = (BookDialog)form;
            await Listed(dialog, 1);

            dialog.View.Select();
            Assert.False(dialog.DrawButton.Enabled);
        });
    }

    [Fact]
    public void ADoubleClickOnAPhoto_OpensTheDrawWindowForThatPhoto_WhateverIsPicked()
    {
        Photo("a.jpg", _folder.Liked);
        Photo("b.jpg", _folder.Liked);
        Photo("c.jpg", _folder.Liked);
        var asked = new List<string>();

        Ui.Run(() => Book((path, _) =>
        {
            asked.Add(Path.GetFileName(path));
            return false;
        }), async form =>
        {
            var dialog = (BookDialog)form;
            await Listed(dialog, 3);

            dialog.View.Select(0);
            var cell = dialog.View.CellBounds(2);
            dialog.View.Activate(new Point(cell.X + cell.Width / 2, cell.Y + cell.Height / 2));

            Assert.Equal(["c.jpg"], asked);
        });
    }

    [Fact]
    public void ADoubleClickOnTheEmptyRoom_OpensNothing()
    {
        Photo("a.jpg", _folder.Liked);
        var asked = 0;

        Ui.Run(() => Book((_, _) =>
        {
            asked++;
            return false;
        }), async form =>
        {
            var dialog = (BookDialog)form;
            await Listed(dialog, 1);

            dialog.View.Activate(new Point(dialog.View.Width - 5, dialog.View.Height - 5));

            Assert.Equal(0, asked);
        });
    }

    [Fact]
    public void WhenThePhotoWasWrittenOver_TheBookWindowReadsItsThumbnailAgain_AndTheBookTakesTheNewFile()
    {
        var path = Photo("a.jpg", _folder.Liked, shade: 40);
        var drawn = false;

        Ui.Run(() => Book((file, _) =>
        {
            // What the Draw window does: writes the photo over with another picture
            using var bitmap = new Bitmap(320, 240);
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.Clear(Color.FromArgb(220, 220, 220));
            bitmap.Save(file + ".new", ImageFormat.Jpeg);
            File.Move(file + ".new", file, overwrite: true);
            drawn = true;
            return true;
        }), async form =>
        {
            var dialog = (BookDialog)form;
            await Listed(dialog, 1);
            await Task.Delay(400); // the thumbnail is read

            dialog.View.Select(0);
            dialog.DrawButton.PerformClick();

            Assert.True(drawn);
            // The thumbnail is read again from the new file: it is light now, not dark
            var lightened = await Ui.Until(() =>
            {
                dialog.View.Invalidate();
                using var shot = new Bitmap(dialog.View.Width, dialog.View.Height);
                dialog.View.DrawToBitmap(shot, new Rectangle(Point.Empty, dialog.View.Size));
                var cell = dialog.View.CellBounds(0);
                return shot.GetPixel(cell.X + cell.Width / 2, cell.Y + 30).R > 150;
            }, 30000); // a machine that is slow with files takes a while to read the thumbnail again
            Assert.True(lightened, "the thumbnail shows the new picture");
        });
        Assert.True(File.Exists(path));
    }
}
