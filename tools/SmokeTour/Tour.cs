using System.ComponentModel;
using System.Drawing.Design;
using System.Drawing.Imaging;
using System.Reflection;
using PhotoLikerUI;

namespace SmokeTour;

/// <summary>
/// One run through the app, in dark mode, the way a person would use it. Every step checks what happened and, when a
/// screenshot folder is given, takes the picture the README shows for it.
/// </summary>
internal sealed class Tour(DrivenForm form, string folder, string? screenshotFolder, Checks checks)
{
    private const int SidePanelWidth = 430;      // wider than the default so the setting names are readable
    private const int SettingsGridHeight = 450;   // tall enough for all twelve rows

    private readonly string _likedFolder = Path.Combine(folder, "Liked");

    private IntPtr Window => form.Handle;

    public async Task Run()
    {
        form.TopMost = true; // nothing else on the desktop may cover the window while it is photographed
        checks.Note($"window brought to the front: {ScreenCapture.BringToFront(Window)}");

        await OpenFolder();
        await Browse();
        await LikesAndGroups();
        await SettingsPanel();
        await ReloadOnChange();
        await Metadata();
        await ThemeToggle();
    }

    // ── 1. the folder opens, in dark mode ───────────────────────────────────────────────────────

    private async Task OpenFolder()
    {
        checks.Step("1. Open the demo folder (its settings file asks for dark theme, .jpg only, no subfolders)");

        var jpegs = DemoFolder.CountTopLevel(".jpg");
        checks.That(await Until(() => form.Config.Files.Count == jpegs && PhotoShown()), $"{jpegs} photos listed and the first one shown");
        checks.That(form.Config.CurrentFolder == folder, "the settings were read from the folder's own file");
        checks.That(form.Status.Contains($"Loaded {jpegs} photos"), $"status: {form.Status}");

        form.Find<SplitContainer>("splitContainer1").SplitterDistance = SidePanelWidth;
        form.Find<SplitContainer>("splitContainer2").SplitterDistance = SettingsGridHeight;
        await Settle();

        using var shot = await Shot("01-home");
        CheckDark(shot);

        checks.Note($"window {form.Width}x{form.Height}, {form.DeviceDpi} dpi");
    }

    private void CheckDark(Bitmap shot)
    {
        checks.That(form.Config.IsDarkTheme, "IsDarkTheme was read from the settings file");
        checks.That(form.ToolbarItem("themeToggleToolStripButton").Text!.Contains("Light"), "the theme button offers the way back to light");
        checks.That(form.BackColor.GetBrightness() < 0.25f, $"window background is dark (brightness {form.BackColor.GetBrightness():0.00})");
        checks.That(form.SettingsGrid.ViewBackColor.GetBrightness() < 0.25f && form.MetadataGrid.ViewBackColor.GetBrightness() < 0.25f, "both grids are dark");

        var gridPixels = ScreenCapture.Brightness(shot, AreaOf(form.SettingsGrid));
        var statusPixels = ScreenCapture.Brightness(shot, AreaOf(form.Find<StatusStrip>("statusStrip1")));
        checks.That(gridPixels < 90 && statusPixels < 90, $"and the pixels on screen are dark (settings grid {gridPixels:0}, status bar {statusPixels:0} of 255)");
    }

    // ── 2. arrow keys, like ─────────────────────────────────────────────────────────────────────

    private async Task Browse()
    {
        checks.Step("2. Next / previous photo and like, with the keyboard");

        await GoTo(1);
        checks.That(form.Config.CurrentFileName == NameAt(1), $"Right arrow shows the next photo ({form.Config.CurrentFileName})");
        (await Shot("02-next-photo")).Dispose();

        form.Press(Keys.Space);
        var likedCopy = Path.Combine(_likedFolder, NameAt(1));
        checks.That(await Until(() => File.Exists(likedCopy) && form.Config.LikedFile != string.Empty), "Space copies the photo to the Liked folder");
        checks.That(form.Config.SavedInFolders.SequenceEqual(["Liked"]), "SavedInFolders lists [Liked]");
        (await Shot("03-liked")).Dispose();

        await GoTo(0);
        checks.That(form.Config.CurrentIndex == 0, "Left arrow goes back");
        (await Shot("04-previous-photo")).Dispose();
    }

    // ── 3. groups ───────────────────────────────────────────────────────────────────────────────

    private async Task LikesAndGroups()
    {
        checks.Step("3. Groups: a key saves a copy under Liked, the same key takes it away again");

        await GoTo(2);
        var name = NameAt(2);
        var groupA = Path.Combine(_likedFolder, "A - Group", name);
        var group1 = Path.Combine(_likedFolder, "1 - Group", name);

        form.Press(Keys.A);
        checks.That(await Until(() => File.Exists(groupA)), "A saves a copy in 'A - Group' (created on the spot)");

        form.Press(Keys.A);
        checks.That(await Until(() => !File.Exists(groupA)), "A again removes the copy");

        form.Press(Keys.A);
        await Until(() => File.Exists(groupA));
        form.Press(Keys.D1);
        checks.That(await Until(() => File.Exists(group1)), "1 saves a copy in '1 - Group'");

        form.Press(Keys.Space);
        checks.That(await Until(() => File.Exists(Path.Combine(_likedFolder, name))), "Space likes the same photo as well");

        checks.That(await Until(() => form.Config.SavedInFolders.Length == 3), "three folders hold this photo");
        checks.That(form.Config.SavedInFolders.SequenceEqual(["Liked", "1 - Group", "A - Group"]),
            $"SavedInFolders: {string.Join("; ", form.Config.SavedInFolders.Select(f => $"[{f}]"))}");

        (await Shot("05-groups")).Dispose();
    }

    // ── 4. the settings grid and the folder picker ──────────────────────────────────────────────

    private async Task SettingsPanel()
    {
        checks.Step("4. The settings grid: what it lists, and the folder picker on LikedFolder");

        var rows = form.VisibleSettingsRows().ToList();
        string[] expected = ["TotalFiles", "CurrentIndex", "CurrentFolder", "CurrentFilePath", "CurrentFileName", "LikedFile", "SavedInFolders",
            "LikedFolder", "GoThroughSubFolders", "Extensions", "CacheSize", "ThumbnailSize"];
        checks.That(expected.All(rows.Contains), "all settings are listed");
        checks.That(!rows.Contains("WindowLeft") && !rows.Contains("IsDarkTheme") && !rows.Contains("Files"), "window state, theme and file list stay hidden");

        // The "..." button is only drawn on the selected row while the grid has the focus, as when it was clicked
        var view = form.SettingsGrid.Controls.Cast<Control>().First(c => c.GetType().Name == "PropertyGridView");
        view.Focus();
        form.SettingsRow("LikedFolder").Select();
        await Settle();
        checks.Note($"settings grid has the focus: {form.SettingsGrid.ContainsFocus}, window is active: {Form.ActiveForm == form}");

        var property = form.SettingsProperty("LikedFolder");
        var editor = (UITypeEditor?)property.GetEditor(typeof(UITypeEditor));
        checks.That(editor?.GetType().Name == "FolderPickerEditor", $"LikedFolder has the folder picker ({editor?.GetType().Name})");
        checks.That(editor?.GetEditStyle(null) == UITypeEditorEditStyle.Modal, "it is a modal editor: the grid shows the \"...\" button");

        (await Shot("06-settings", AreaOf(form.SettingsGrid))).Dispose();

        // Open the real dialog through the editor, photograph it, cancel it
        var before = form.Config.LikedFolder;
        var context = new GridContext(property, form.Config);
        var attempts = 0;
        var seen = false;
        var timer = new System.Windows.Forms.Timer { Interval = 700 };
        timer.Tick += (_, _) =>
        {
            attempts++;
            var dialog = ScreenCapture.FindDialogOfThisThread(Window);
            if (dialog == IntPtr.Zero)
            {
                if (attempts > 12)
                {
                    timer.Stop();
                    checks.Fail("the folder dialog never appeared");
                    SendKeys.Send("{ESC}"); // the editor would wait for it forever
                }
                return;
            }

            if (!seen)
            {
                seen = true; // one more tick, so it has finished drawing
                return;
            }

            timer.Stop();
            using var picture = ScreenCapture.Capture(dialog);
            // The navigation pane lists the pinned folders and drives of whoever runs this: not for a README
            ScreenCapture.Pixelate(picture, new Rectangle(0, picture.Height * 15 / 100, picture.Width * 33 / 100, picture.Height * 715 / 1000), 14);
            Save(picture, "07-folder-picker");
            ScreenCapture.Close(dialog);
        };
        timer.Start();
        var result = editor!.EditValue(context, new NoServices(), before);
        timer.Dispose();

        checks.That(Equals(result, before), "cancelling the dialog keeps the folder");
        checks.That(form.Config.LikedFolder == before, $"LikedFolder is still {before}");
    }

    // ── 5. changing GoThroughSubFolders / Extensions relists ────────────────────────────────────

    private async Task ReloadOnChange()
    {
        checks.Step("5. Changing GoThroughSubFolders or Extensions in the grid reloads the folder");

        var jpegs = DemoFolder.CountTopLevel(".jpg");
        var inSubFolder = DemoFolder.CountInSubFolder;
        var pngs = DemoFolder.CountTopLevel(".png");

        checks.That(form.Config.Files.Count == jpegs, $"before: {form.Config.Files.Count} photos");

        await ChangeSetting("GoThroughSubFolders", true);
        checks.That(await Until(() => form.Config.Files.Count == jpegs + inSubFolder && PhotoShown()),
            $"GoThroughSubFolders on: {form.Config.Files.Count} photos (expected {jpegs + inSubFolder})");

        var firstInSubFolder = form.Config.Files.FindIndex(f => f.OriginalFilePath.Contains(DemoFolder.SubFolder));
        await GoTo(firstInSubFolder);
        checks.That(form.Text.Contains(DemoFolder.SubFolder), $"window title names the subfolder: {form.Text}");
        (await Shot("08-subfolders-on")).Dispose();

        await ChangeSetting("Extensions", new[] { ".jpg", ".png" });
        checks.That(await Until(() => form.Config.Files.Count == jpegs + inSubFolder + pngs && PhotoShown()),
            $"Extensions '.jpg; .png': {form.Config.Files.Count} photos (expected {jpegs + inSubFolder + pngs})");
        checks.That(form.Status.Contains($"Loaded {jpegs + inSubFolder + pngs} photos"), $"status: {form.Status}");
        (await Shot("09-extensions")).Dispose();

        checks.That(File.Exists(Path.Combine(folder, "photo-liker-config.json")) && File.ReadAllText(Path.Combine(folder, "photo-liker-config.json")).Contains(".png"),
            "the new settings were saved right away");
    }

    // ── 6. EXIF and GPS ─────────────────────────────────────────────────────────────────────────

    private async Task Metadata()
    {
        checks.Step("6. EXIF and GPS of the shown photo");

        await GoTo(0);
        await Settle(600);

        var wrapper = form.MetadataGrid.SelectedObject as MetadataWrapper;
        checks.That(wrapper is not null, "the metadata grid is filled");
        if (wrapper is not null)
        {
            var rows = wrapper.GetProperties().Cast<PropertyDescriptor>().Where(p => p.IsBrowsable).ToList();
            string[] expected = ["Make", "Model", "Latitude (decimal)", "Longitude (decimal)", "Coordinates", "Map Link", "Altitude", "All Metadata"];
            checks.That(expected.All(name => rows.Any(r => r.Name == name)), "Make, Model, decoded GPS rows and the debug text are there");
            checks.That(rows.Any(r => r.Name == "Map Link" && r.Category == "2. GPS Decoded"), "Map Link is in the 'GPS Decoded' category");
            checks.That(rows.All(r => !r.Name.StartsWith("GPS ")), "the raw GPS tags stay hidden");
            checks.That(wrapper.MapLink?.StartsWith("https://maps.google.com/maps?q=38.") == true, $"map link: {wrapper.MapLink}");
        }

        checks.That(form.GpsLink.Visible && form.GpsLink.Text!.Contains("Maps"), $"status bar offers the map link ({form.GpsLink.Text})");

        (await Shot("10-metadata-gps")).Dispose();
    }

    // ── 7. back to light and to dark again ──────────────────────────────────────────────────────

    private async Task ThemeToggle()
    {
        checks.Step("7. The theme button switches to light and back");

        var toggle = form.ToolbarItem("themeToggleToolStripButton");
        toggle.PerformClick();
        await Settle(500);

        using (var light = await Shot("11-light-theme"))
        {
            var gridPixels = ScreenCapture.Brightness(light, AreaOf(form.SettingsGrid));
            checks.That(!form.Config.IsDarkTheme && toggle.Text!.Contains("Dark"), "light theme: setting off, button offers dark");
            checks.That(form.BackColor.GetBrightness() > 0.5f && gridPixels > 170, $"and the window is light (settings grid {gridPixels:0} of 255)");
        }

        toggle.PerformClick();
        await Settle(500);
        using var dark = await Shot(null);
        var darkPixels = ScreenCapture.Brightness(dark, AreaOf(form.SettingsGrid));
        checks.That(form.Config.IsDarkTheme && toggle.Text!.Contains("Light") && darkPixels < 90, $"dark again (settings grid {darkPixels:0} of 255)");
    }

    // ── what the tour leaves behind, checked after the window is closed ─────────────────────────

    public void VerifySavedSettings()
    {
        checks.Step("8. The settings file written when the window closed");

        var path = Path.Combine(folder, "photo-liker-config.json");
        var text = File.ReadAllText(path);
        using var json = System.Text.Json.JsonDocument.Parse(text);
        var root = json.RootElement;

        checks.That(root.GetProperty("CurrentFolder").GetString() == ".", "CurrentFolder is stored as '.'");
        checks.That(root.GetProperty("LikedFolder").GetString() == "Liked", "LikedFolder is stored as 'Liked'");
        checks.That(root.TryGetProperty("GoThroughtSubFolders", out var subfolders) && subfolders.GetBoolean(), "GoThroughtSubFolders (the spelling of the file) is true");
        checks.That(root.GetProperty("IsDarkTheme").GetBoolean(), "IsDarkTheme is true");
        checks.That(root.GetProperty("Extensions").EnumerateArray().Select(e => e.GetString()).SequenceEqual([".jpg", ".png"]), "Extensions are .jpg and .png");
        var current = root.GetProperty("CurrentFilePath").GetString()!;
        checks.That(!Path.IsPathRooted(current), $"the photo it stopped at is stored relative ({current})");
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private string NameAt(int index) => Path.GetFileName(form.Config.Files[index].OriginalFilePath);

    private bool PhotoShown() => form.Viewer.Image is not null && form.Viewer.Tag as string == form.Config.CurrentFilePath;

    /// <summary>Presses the arrow keys until the photo with this index is shown.</summary>
    private async Task GoTo(int index)
    {
        while (form.Config.CurrentIndex != index)
        {
            var before = form.Config.CurrentIndex;
            form.Press(index > before ? Keys.Right : Keys.Left);
            if (!await Until(() => form.Config.CurrentIndex != before && PhotoShown()))
            {
                checks.Fail($"the photo did not change from index {before}");
                return;
            }
        }

        await Until(PhotoShown);
    }

    /// <summary>Does what editing a value in the grid does: sets it, then lets the grid tell the form.</summary>
    private async Task ChangeSetting(string name, object value)
    {
        var row = form.SettingsRow(name);
        row.Select();
        form.SettingsProperty(name).SetValue(form.Config, value);

        var raise = typeof(PropertyGrid).GetMethod("OnPropertyValueChanged", BindingFlags.NonPublic | BindingFlags.Instance)!;
        raise.Invoke(form.SettingsGrid, [new PropertyValueChangedEventArgs(row, null)]);
        await Settle(200);
    }

    private Rectangle AreaOf(Control control)
    {
        var frame = ScreenCapture.FrameBounds(Window);
        var area = control.RectangleToScreen(control.ClientRectangle);
        area.Offset(-frame.X, -frame.Y);
        return area;
    }

    /// <summary>Photographs the window; saves it as <paramref name="name"/> (or just the <paramref name="crop"/> of it).</summary>
    private async Task<Bitmap> Shot(string? name, Rectangle? crop = null)
    {
        await Settle(450);
        var picture = ScreenCapture.Capture(Window);
        if (name is not null)
        {
            if (crop is { } area)
            {
                using var part = picture.Clone(Rectangle.Intersect(area, new Rectangle(Point.Empty, picture.Size)), picture.PixelFormat);
                Save(part, name);
            }
            else
            {
                Save(picture, name);
            }
        }
        return picture;
    }

    private void Save(Bitmap picture, string name)
    {
        if (screenshotFolder is null)
            return;

        Directory.CreateDirectory(screenshotFolder);
        var path = Path.Combine(screenshotFolder, name + ".png");
        picture.Save(path, ImageFormat.Png);
        checks.Note($"screenshot: {path} ({picture.Width}x{picture.Height})");
    }

    private static async Task<bool> Until(Func<bool> condition, int timeoutMs = 15000)
    {
        var waited = 0;
        while (!condition() && waited < timeoutMs)
        {
            await Task.Delay(50);
            waited += 50;
        }
        return condition();
    }

    private static Task Settle(int milliseconds = 300) => Task.Delay(milliseconds);

    private sealed class GridContext(PropertyDescriptor property, object instance) : ITypeDescriptorContext
    {
        public IContainer? Container => null;
        public object Instance => instance;
        public PropertyDescriptor PropertyDescriptor => property;
        public object? GetService(Type serviceType) => null;
        public bool OnComponentChanging() => true;
        public void OnComponentChanged() { }
    }

    private sealed class NoServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
