using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using PhotoLiker.Core;

namespace SmokeTour;

/// <summary>
/// The pictures of the user manual that the README has no use for: the Generate Video dialog from the click on its button to the finished video
/// (with a cover, an audio file and the log), and the About box. A run of its own (<c>--only-dialogs</c>) on a fresh demo folder, because it
/// likes photos and makes a video in it.
/// </summary>
internal sealed partial class Tour
{
    private const string VideoDialogType = "VideoGenerationDialog";
    private const string AboutDialogType = "AboutDialog";

    public async Task RunDialogs()
    {
        form.TopMost = true; // nothing else on the desktop may cover the windows while they are photographed
        checks.Note($"window brought to the front: {ScreenCapture.BringToFront(Window)}");

        var jpegs = DemoFolder.CountTopLevel(".jpg");
        checks.Step("1. The demo folder opens, four photos are liked");
        checks.That(await Until(() => form.Config.Files.Count == jpegs && PhotoShown()), $"{jpegs} photos listed and the first one shown");

        // As the tour sets them: wide enough for the whole toolbar
        form.Find<SplitContainer>("splitContainer1").SplitterDistance = SidePanelWidth;
        form.Find<SplitContainer>("splitContainer2").SplitterDistance = SettingsGridHeight;
        await Settle();

        const int liked = 4;
        for (var index = 0; index < liked; index++)
        {
            await GoTo(index);
            form.Press(Keys.Space);
            var copy = Path.Combine(_likedFolder, CopyNameAt(index));
            checks.That(await Until(() => File.Exists(copy)), $"Space copies {NameAt(index)} to the Liked folder");
        }

        await GenerateVideoDialog(liked);
        await AboutBox();
    }

    // ── Generate Video ──────────────────────────────────────────────────────────────────────────

    private async Task GenerateVideoDialog(int likedCount)
    {
        checks.Step("2. Generate Video: the button, the dialog, a cover and an audio file, the finished video");

        var toolbar = form.Find<ToolStrip>("toolStrip1");
        var button = toolbar.Items["generateVideoToolStripButton"]!;
        await ShotOfToolbar(toolbar, button, "12-generate-video-button");

        form.BeginInvoke(() => button.PerformClick()); // the dialog is modal: the click only returns when it is closed
        Form? dialog = null;
        checks.That(await Until(() => (dialog = OpenDialog(VideoDialogType)) is not null), "the button opens the dialog");
        if (dialog is null)
            return;

        var sourceCombo = Field<ComboBox>(dialog, "_sourceCombo");
        checks.That(await Until(() => sourceCombo.Items.Cast<object>().Any(item => item.ToString() == $"Liked ({likedCount})")),
            $"the Liked folder is counted ({string.Join(" | ", sourceCombo.Items.Cast<object>())})");
        checks.That(sourceCombo.SelectedItem?.ToString() == $"Liked ({likedCount})", "and it is the one picked");

        var videoLength = Field<Label>(dialog, "_videoLengthLabel");
        checks.That(await Until(() => videoLength.Text.StartsWith("About")), $"the video length is shown ({videoLength.Text.Replace('\n', '/')})");
        await DialogShot(dialog, "13-generate-video-dialog");

        // A cover and a song, and a name for the video
        var media = Path.Combine(folder, "Media");
        Directory.CreateDirectory(media);
        var coverPath = Path.Combine(media, "Cover.jpg");
        var songPath = Path.Combine(media, "Holiday song.mp3");
        RenderCover(coverPath);
        checks.That(MakeSong(songPath, seconds: 205), "a song of 3:25 is made with the ffmpeg of the app");

        Field<TextBox>(dialog, "_coverTextBox").Text = coverPath;
        Field<TextBox>(dialog, "_audioTextBox").Text = songPath;
        Field<TextBox>(dialog, "_outputFileNameTextBox").Text = "Holiday video.mp4";
        Field<NumericUpDown>(dialog, "_frameTimeStartNumeric").Value = 1.0M;
        Field<NumericUpDown>(dialog, "_frameTimeEndNumeric").Value = 2.0M;

        var audioLength = Field<Label>(dialog, "_audioLengthLabel");
        checks.That(await Until(() => audioLength.Text == "Length: 3:25"), $"ffmpeg tells the length of the song ({audioLength.Text})");
        checks.That(await Until(() => Field<PictureBox>(dialog, "_coverPreviewBox").Image is not null), "the cover has a preview");
        checks.That(await Until(() => videoLength.Text.Contains("cut where the video ends")), $"the video length says what becomes of the song ({videoLength.Text.Replace('\n', '/')})");
        await DialogShot(dialog, "14-generate-video-settings");

        Field<Button>(dialog, "_generateButton").PerformClick();
        var play = Field<Button>(dialog, "_runButton");
        checks.That(await Until(() => play.Visible && play.Enabled, timeoutMs: 180_000), "the video is made (Play shows)");
        var output = (string?)dialog.GetType().GetProperty("GeneratedOutputPath")!.GetValue(dialog);
        checks.That(output is not null && File.Exists(output) && new FileInfo(output).Length > 0, $"the video file is there ({output})");
        checks.That(Field<TextBox>(dialog, "_outputLogTextBox").Text.Contains("Video generation completed"), "and the log says so");
        await DialogShot(dialog, "15-generate-video-done");

        Field<Button>(dialog, "_cancelButton").PerformClick();
        checks.That(await Until(() => OpenDialog(VideoDialogType) is null), "the dialog closes");
    }

    // ── About ───────────────────────────────────────────────────────────────────────────────────

    private async Task AboutBox()
    {
        checks.Step("3. About: the shortcuts and the buttons of the manual");

        var help = form.ToolbarItem("helpToolStripButton");
        form.BeginInvoke(() => help.PerformClick());
        Form? dialog = null;
        checks.That(await Until(() => (dialog = OpenDialog(AboutDialogType)) is not null), "the Help button opens the About box");
        if (dialog is null)
            return;

        var text = AllControls(dialog).OfType<TextBox>().Single().Text;
        checks.That(new[] { "Right / Left", "Space", "Delete", "1-9, A-Z", "Alt+1-9", "F5", "Ctrl+C" }.All(text.Contains), "it lists the keys");

        var buttons = AllControls(dialog).OfType<Button>().Select(b => b.Text).ToList();
        checks.That(buttons.Count == 3 && buttons[0].Contains("English") && buttons[1].Contains("українською") && buttons[2] == "OK", $"and has the buttons {string.Join(" | ", buttons)}");

        // The pages are copied beside the program by the project (and by the installer, which packs the same folder)
        checks.That(UserManual.Find(AppContext.BaseDirectory, UserManual.English) is not null, "the English manual is beside the program");
        checks.That(UserManual.Find(AppContext.BaseDirectory, UserManual.Ukrainian) is not null, "the Ukrainian manual is beside the program");

        checks.That(new[] { "F1", "F2", "F3" }.All(text.Contains), "the list has the help keys F1, F2 and F3");

        await DialogShot(dialog, "16-about");
        dialog.Close();
        checks.That(await Until(() => OpenDialog(AboutDialogType) is null), "it closes");

        await HelpKeys();
    }

    /// <summary>F1 opens the About box, F2 the Ukrainian manual, F3 the English one (the browser is not launched: the tour takes the call).</summary>
    private async Task HelpKeys()
    {
        var launcher = typeof(PhotoLikerUI.MainForm).Assembly.GetType("PhotoLikerUI.UserManualLauncher")!;
        var opener = launcher.GetProperty("Opener")!;
        var original = opener.GetValue(null);
        var opened = new List<string>();
        opener.SetValue(null, (Func<string, string?>)(path =>
        {
            opened.Add(path);
            return null;
        }));

        try
        {
            form.Press(Keys.F2);
            checks.That(opened.Count == 1 && Path.GetFileName(opened[0]) == "manual.ua.html", $"F2 opens the Ukrainian manual ({string.Join(", ", opened.Select(Path.GetFileName))})");
            form.Press(Keys.F3);
            checks.That(opened.Count == 2 && Path.GetFileName(opened[1]) == "manual.en.html", $"F3 opens the English manual ({string.Join(", ", opened.Select(Path.GetFileName))})");
            checks.That(opened.All(File.Exists), "and both are files that are there");
        }
        finally
        {
            opener.SetValue(null, original);
        }

        form.BeginInvoke(() => form.Press(Keys.F1)); // the box is modal: the key only returns when it is closed
        Form? about = null;
        checks.That(await Until(() => (about = OpenDialog(AboutDialogType)) is not null), "F1 opens the About box");
        about?.Close();
        checks.That(await Until(() => OpenDialog(AboutDialogType) is null), "and it closes");
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static Form? OpenDialog(string typeName) => Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.GetType().Name == typeName);

    private static IEnumerable<Control> AllControls(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(child => AllControls(child).Prepend(child));

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;

    private async Task DialogShot(Form dialog, string name)
    {
        // The window is TopMost (nothing may cover it), which the dialog above it is not: it has to be too, or the photo of the window is what is on screen there
        dialog.TopMost = true;
        ScreenCapture.BringToFront(dialog.Handle);
        await Settle(600);
        using var picture = ScreenCapture.Capture(dialog.Handle);
        Save(picture, name);
    }

    /// <summary>The toolbar of the window with a red ring round one button, so that a how-to can say "this one".</summary>
    private async Task ShotOfToolbar(ToolStrip toolbar, ToolStripItem button, string name)
    {
        using var window = await Shot(null);
        var area = AreaOf(toolbar);
        var right = toolbar.Items.Cast<ToolStripItem>().Where(item => item.Visible).Max(item => item.Bounds.Right) + 8;
        using var strip = window.Clone(new Rectangle(area.X, area.Y, Math.Min(right, area.Width), area.Height), window.PixelFormat);

        using (var graphics = Graphics.FromImage(strip))
        using (var pen = new Pen(Color.FromArgb(235, 60, 60), 3))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var ring = Rectangle.Inflate(button.Bounds, 3, 2);
            using var path = RoundedRectangle(ring, 6);
            graphics.DrawPath(pen, path);
        }

        Save(strip, name);
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>A title card for the video to open with.</summary>
    private static void RenderCover(string path)
    {
        const int w = 1920, h = 1080;
        using var bitmap = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using (var sky = new LinearGradientBrush(new Rectangle(0, 0, w, h), Color.FromArgb(25, 60, 110), Color.FromArgb(240, 150, 90), 90f))
                graphics.FillRectangle(sky, 0, 0, w, h);

            using var title = new Font("Segoe UI", 110, FontStyle.Bold, GraphicsUnit.Pixel);
            using var subtitle = new Font("Segoe UI", 52, FontStyle.Regular, GraphicsUnit.Pixel);
            var centre = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            graphics.DrawString("Holiday 2025", title, Brushes.White, new Rectangle(0, 340, w, 200), centre);
            graphics.DrawString("The Portuguese coast", subtitle, Brushes.WhiteSmoke, new Rectangle(0, 560, w, 100), centre);
        }

        bitmap.Save(path, ImageFormat.Jpeg);
    }

    /// <summary>A tone as an mp3 of the given length, made by the ffmpeg of the app (a stand-in for a song).</summary>
    private static bool MakeSong(string path, int seconds)
    {
        var ffmpeg = Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe");
        if (!File.Exists(ffmpeg))
            return false;

        var start = new ProcessStartInfo(ffmpeg)
        {
            Arguments = $"-hide_banner -loglevel error -y -f lavfi -i \"sine=frequency=330:duration={seconds}\" -c:a libmp3lame -q:a 6 \"{path}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(start)!;
        process.WaitForExit();
        return process.ExitCode == 0 && File.Exists(path);
    }
}
