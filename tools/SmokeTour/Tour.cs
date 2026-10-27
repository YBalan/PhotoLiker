using System.ComponentModel;
using System.Drawing.Design;
using System.Drawing.Imaging;
using System.Reflection;
using PhotoLiker.Core;
using PhotoLikerUI;

namespace SmokeTour;

/// <summary>
/// One run through the app, in dark mode, the way a person would use it. Every step checks what happened and, when a
/// screenshot folder is given, takes the picture the README shows for it.
/// </summary>
internal sealed class Tour(DrivenForm form, string folder, string? screenshotFolder, Checks checks)
{
    private const int SidePanelWidth = 430;      // wider than the default so the setting names are readable
    private const int SettingsGridHeight = 450;   // tall enough for all thirteen rows

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
        await FitKeyAndGroupO(); // the folder of group O would show in the legend of the screenshots after it
        await FocusAfterEditing(); // it changes settings and clicks around, which would show in the screenshots
        await DeleteKey();         // last: the demo photos it moves away are not there for the steps after it
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
        CheckSearchStrip();

        checks.Note($"window {form.Width}x{form.Height}, {form.DeviceDpi} dpi");
    }

    /// <summary>The strip above the viewer: the index import / export first, icons instead of words on Search and Faces.</summary>
    private void CheckSearchStrip()
    {
        var items = form.Find<ToolStrip>("searchToolStrip").Items;
        checks.That(items[0].Name == "indexFileToolStripButton" && items[0].Text == "I/E", $"the strip starts with the I/E button ({items[0].Name}: {items[0].Text})");

        foreach (var name in new[] { "searchToolStripButton", "facesToolStripButton" })
            checks.That(items[name] is { DisplayStyle: ToolStripItemDisplayStyle.Image, Image: not null }, $"{name} shows an icon");
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
        checks.Step("2. Next / previous / first / last photo and like, with the keyboard");

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

        form.Press(Keys.End);
        var last = form.Config.Files.Count - 1;
        checks.That(await Until(() => form.Config.CurrentIndex == last && PhotoShown()), $"End shows the last photo ({form.Config.CurrentFileName})");
        form.Press(Keys.Home);
        checks.That(await Until(() => form.Config.CurrentIndex == 0 && PhotoShown()), $"Home shows the first photo ({form.Config.CurrentFileName})");

        // A new photo brings the settings grid back to its top, whatever row was looked at
        form.SettingsRow("ThumbnailSize").Select();
        checks.That(form.SettingsGrid.SelectedGridItem?.Label == "ThumbnailSize", "a row at the bottom of the settings grid is selected");
        await GoTo(1);
        checks.That(form.SettingsGrid.SelectedGridItem?.Label == form.VisibleSettingsCategories().First(), $"the settings grid is back at its top ({form.SettingsGrid.SelectedGridItem?.Label})");

        // CurrentIndex can be edited: the number typed in jumps to that photo, one outside the list to the nearest end
        checks.That(!form.SettingsProperty("CurrentIndex").IsReadOnly, "CurrentIndex is not read-only");
        await ChangeSetting("CurrentIndex", 3);
        checks.That(await Until(() => form.Config.CurrentIndex == 3 && PhotoShown()), $"typing 3 into CurrentIndex shows photo 3 ({form.Config.CurrentFileName})");
        checks.That(form.SettingsGrid.SelectedGridItem?.Label == "CurrentIndex", "the CurrentIndex row stays selected, ready for the next number");
        await ChangeSetting("CurrentIndex", 99);
        checks.That(await Until(() => form.Config.CurrentIndex == last && PhotoShown()), $"99 goes to the last photo, and the row shows {form.Config.CurrentIndex}");
        await ChangeSetting("CurrentIndex", -4);
        checks.That(await Until(() => form.Config.CurrentIndex == 0 && PhotoShown()), $"-4 goes to the first photo, and the row shows {form.Config.CurrentIndex}");

        await MonthJumps();
    }

    /// <summary>PageDown / PageUp: the demo photos are of July (0, 1), August (2) and September (3, 4), by the dates of their files.</summary>
    private async Task MonthJumps()
    {
        form.Press(Keys.PageDown);
        checks.That(await Until(() => form.Config.CurrentIndex == 2 && PhotoShown()) && form.Status.Contains("August 2025"), $"PageDown goes to the first photo of the next month ({form.Status})");
        form.Press(Keys.PageDown);
        checks.That(await Until(() => form.Config.CurrentIndex == 3 && PhotoShown()) && form.Status.Contains("September 2025"), $"PageDown again goes to September ({form.Status})");

        form.Press(Keys.PageDown);
        checks.That(form.Config.CurrentIndex == 3 && form.Status.Contains("last month"), $"there is no month after the last ({form.Status})");

        form.Press(Keys.PageUp);
        checks.That(await Until(() => form.Config.CurrentIndex == 2 && PhotoShown()), "PageUp goes back to August");
        form.Press(Keys.PageUp);
        checks.That(await Until(() => form.Config.CurrentIndex == 0 && PhotoShown()) && form.Status.Contains("July 2025"), $"PageUp goes to the first photo of July ({form.Status})");

        form.Press(Keys.PageUp);
        checks.That(form.Config.CurrentIndex == 0 && form.Status.Contains("first month"), $"there is no month before the first ({form.Status})");
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
            "LikedFolder", "GoThroughSubFolders", "Extensions", "SortOrder", "CacheSize", "ThumbnailSize"];
        checks.That(expected.All(rows.Contains), "all settings are listed");
        checks.That(!rows.Contains("WindowLeft") && !rows.Contains("IsDarkTheme") && !rows.Contains("Files"), "window state, theme and file list stay hidden");

        var undescribed = expected.Where(name => form.SettingsProperty(name).Description.Length < 30).ToList();
        checks.That(undescribed.Count == 0, $"every row has a description for the help pane{(undescribed.Count == 0 ? "" : ", missing: " + string.Join(", ", undescribed))}");
        checks.That(form.SettingsProperty("CurrentIndex").Description.Contains("PageDown"), "and the one of CurrentIndex tells about the keys");

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

    // ── 5. changing GoThroughSubFolders / Extensions / SortOrder relists ────────────────────────

    private async Task ReloadOnChange()
    {
        checks.Step("5. Changing GoThroughSubFolders, Extensions or SortOrder in the grid reloads the folder");

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

        await ChangeSortOrder();
    }

    /// <summary>
    /// The list is by the created date of the files, oldest first unless the folder says otherwise. The demo files are made
    /// so that the order of the file system (the folder's own photos, then the subfolder's) is not the order of the dates.
    /// </summary>
    private async Task ChangeSortOrder()
    {
        var paths = () => form.Config.Files.Select(f => f.OriginalFilePath).ToList();
        var oldestFirst = paths();
        var fileSystemOrder = PhotoScanner.Scan(folder, recursive: true, form.Config.Extensions, form.Config.LikedFolder).Select(f => f.OriginalFilePath).ToList();
        var shown = form.Config.CurrentFilePath;

        checks.That(form.Config.SortOrder == PhotoSortOrder.OldestFirst, "SortOrder is OldestFirst unless the folder's file says otherwise");
        checks.That(form.Config.Files.Zip(form.Config.Files.Skip(1)).All(pair => pair.First.CreationTimeUtc <= pair.Second.CreationTimeUtc), "so the list runs from the oldest file to the newest");
        checks.That(!fileSystemOrder.SequenceEqual(oldestFirst), "which is not the order the file system lists them in (the demo files are made that way)");

        await ChangeSetting("SortOrder", PhotoSortOrder.NewestFirst);
        checks.That(await Until(() => paths().SequenceEqual(Enumerable.Reverse(oldestFirst)) && PhotoShown()), "NewestFirst reloads the folder and turns the list around");
        checks.That(form.Config.CurrentFilePath == shown && form.Config.Files[form.Config.CurrentIndex].OriginalFilePath == shown,
            $"the photo that was shown is still shown, now at index {form.Config.CurrentIndex}");

        await ChangeSetting("SortOrder", PhotoSortOrder.None);
        checks.That(await Until(() => paths().SequenceEqual(fileSystemOrder) && PhotoShown()), "None lists the photos as the file system does");

        await ChangeSetting("SortOrder", PhotoSortOrder.OldestFirst);
        checks.That(await Until(() => paths().SequenceEqual(oldestFirst) && PhotoShown()), "and OldestFirst brings the first order back (the steps after this one rely on it)");
        checks.That(File.ReadAllText(Path.Combine(folder, "photo-liker-config.json")).Contains("\"SortOrder\": \"OldestFirst\""), "the order is saved by name in the folder's file");
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

            var undescribed = rows.Where(r => r.Description.Length < 30).Select(r => r.Name).ToList();
            checks.That(undescribed.Count == 0, $"all {rows.Count} rows have a description for the help pane{(undescribed.Count == 0 ? "" : ", missing: " + string.Join(", ", undescribed))}");
            checks.That(rows.First(r => r.Name == "Exposure Time").Description.Contains("shutter"), "an EXIF row explains itself (Exposure Time)");
            checks.That(rows.First(r => r.Name == "Coordinates").Description.Contains("decimal"), "and so does a decoded GPS row (Coordinates)");
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

    // ── 8. 0 fits the image, O is a group key like any other letter ─────────────────────────────

    private async Task FitKeyAndGroupO()
    {
        checks.Step("8. The 0 key fits the image to the viewer, O is a group key like any other letter");

        var name = NameAt(form.Config.CurrentIndex);

        form.Press(Keys.D0);
        checks.That(form.Status.Contains("fitted"), $"0 fits the image ({form.Status})");
        form.Press(Keys.NumPad0);
        checks.That(form.Status.Contains("fitted"), $"and so does 0 of the numpad ({form.Status})");

        var groupO = Path.Combine(_likedFolder, "O - Group", name);
        form.Press(Keys.O);
        checks.That(await Until(() => File.Exists(groupO)), "O saves a copy in 'O - Group'");
        checks.That(!form.Status.Contains("fitted"), $"and does not fit the image ({form.Status})");
        form.Press(Keys.O);
        checks.That(await Until(() => !File.Exists(groupO)), "O again takes the copy away");
    }

    // ── 9. the keys work again after the settings grid was used ─────────────────────────────────

    /// <summary>
    /// While a row of a grid is being edited its text box has the keyboard focus, and the arrow keys and letters belong to the
    /// text. Whatever a person does next, the photo has to get the keys back. Real clicks and real keys here:
    /// <see cref="DrivenForm.Press"/> hands a key to the form and would not notice where the focus is.
    /// </summary>
    private async Task FocusAfterEditing()
    {
        checks.Step("9. After the settings grid was used, the keys work on the photo again");

        ScreenCapture.BringToFront(Window);
        await Settle(300);

        // A. Click the value, type a number, Enter: what a person does to change CacheSize
        await GoTo(0);
        RealInput.Click(ValueCellOf("CacheSize"));
        await Settle(300);
        checks.That(RealInput.Focused()?.GetType().Name == "GridViewTextBox", $"a click on the value of CacheSize opens the edit, which has the focus ({RealInput.Focused()?.GetType().Name})");
        SendKeys.SendWait("{HOME}+{END}21{ENTER}");
        await Settle(300);
        checks.That(form.Config.CacheSize == 21, $"typing 21 and Enter changed CacheSize ({form.Config.CacheSize})");
        checks.That(await ArrowKeyMovesOn(), "an arrow key shows the next photo right after the edit");

        // B. The edit is still open, and a click on the photo takes the focus away from it
        RealInput.Click(ValueCellOf("CacheSize"));
        await Settle(300);
        RealInput.Click(CentreOf(form.Viewer));
        await Settle(300);
        checks.That(await ArrowKeyMovesOn(), "a click on the photo, with the edit still open, gives the keys back to it");

        // C. A click on a thumbnail does the same
        RealInput.Click(ValueCellOf("CacheSize"));
        await Settle(300);
        var strip = form.Find<FlowLayoutPanel>("previewFlowPanel");
        var thumb = strip.Controls.OfType<PictureBox>().First(t => (string)t.Tag! != form.Config.CurrentFilePath && strip.ClientRectangle.Contains(t.Bounds));
        RealInput.Click(CentreOf(thumb));
        await Settle(400);
        checks.That(form.Config.CurrentFilePath == (string)thumb.Tag!, "a click on a thumbnail shows that photo");
        checks.That(await ArrowKeyMovesOn(), "and the arrow keys move on from it");

        // D. A number typed into CurrentIndex jumps to that photo, and the keys are the photo's right after
        RealInput.Click(ValueCellOf("CurrentIndex"));
        await Settle(300);
        SendKeys.SendWait("{HOME}+{END}4{ENTER}");
        checks.That(await Until(() => form.Config.CurrentIndex == 4 && PhotoShown()), $"typing 4 into CurrentIndex shows photo 4 (index {form.Config.CurrentIndex})");
        await Settle(300);
        SendKeys.SendWait("{RIGHT}");
        await Settle(300);
        checks.That(form.Config.CurrentIndex == 5, $"and the Right arrow goes on from there (index {form.Config.CurrentIndex})");
        SendKeys.SendWait("{END}");
        await Settle(300);
        checks.That(form.Config.CurrentIndex == form.Config.Files.Count - 1, $"as does End (index {form.Config.CurrentIndex})");

        // E. A value picked from the drop-down list of a row (SortOrder reloads the folder): open the list with its button, click an item
        await GoTo(0);
        RealInput.Click(ValueCellOf("SortOrder"));
        await Settle(300);
        RealInput.Click(DropDownButtonOf("SortOrder"));
        await Settle(400);
        var list = RealInput.Focused() as ListBox;
        checks.That(list is not null, $"the button opens the list of the values ({RealInput.Focused()?.GetType().Name})");
        if (list is not null)
        {
            var item = list.Items.Cast<object>().ToList().FindIndex(i => i.ToString() == nameof(PhotoSortOrder.NewestFirst));
            RealInput.Click(list.PointToScreen(list.GetItemRectangle(item).Location + new Size(10, 4)));
        }
        checks.That(await Until(() => form.Config.SortOrder == PhotoSortOrder.NewestFirst && PhotoShown()), $"clicking NewestFirst in it sets SortOrder ({form.Config.SortOrder})");
        await Settle(300);
        checks.That(await ArrowKeyMovesOn(), "an arrow key moves on to another photo right after");

        // F. A double click on the value turns a value over, and the keys are the photo's too
        RealInput.Click(ValueCellOf("SortOrder"), times: 2);
        checks.That(await Until(() => form.Config.SortOrder == PhotoSortOrder.None && PhotoShown()), $"a double click on SortOrder turns it over ({form.Config.SortOrder})");
        await Settle(300);
        checks.That(await ArrowKeyMovesOn(), "an arrow key moves on to another photo right after");

        await ChangeSetting("SortOrder", PhotoSortOrder.OldestFirst);
        await ChangeSetting("CacheSize", 20);
        checks.That(await Until(() => form.Config.Files.Zip(form.Config.Files.Skip(1)).All(p => p.First.CreationTimeUtc <= p.Second.CreationTimeUtc) && PhotoShown()), "back to OldestFirst and CacheSize 20");
    }

    /// <summary>Presses the arrow key that has a photo to go to, and says whether another photo is shown now.</summary>
    private async Task<bool> ArrowKeyMovesOn()
    {
        var before = form.Config.CurrentIndex;
        SendKeys.SendWait(before < form.Config.Files.Count - 1 ? "{RIGHT}" : "{LEFT}");
        await Settle(300);
        return form.Config.CurrentIndex != before;
    }

    /// <summary>Where the value of a settings row is on the screen (the grid's coordinates are relative to its own window).</summary>
    private Rectangle ValueCell(string name)
    {
        var view = form.SettingsGrid.Controls.Cast<Control>().First(c => c.GetType().Name == "PropertyGridView");
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var row = (int)view.GetType().GetMethod("GetRowFromGridEntry", flags)!.Invoke(view, [form.SettingsRow(name)])!;
        var cell = (Rectangle)view.GetType().GetMethods(flags).First(m => m.Name == "GetRectangle" && m.GetParameters().Length == 2).Invoke(view, [row, 2])!; // 2: the value, not the name
        return view.RectangleToScreen(cell);
    }

    /// <summary>A little in from the left edge of the value: where a person clicks to edit it.</summary>
    private Point ValueCellOf(string name)
    {
        var cell = ValueCell(name);
        return new Point(cell.Left + 12, cell.Top + cell.Height / 2);
    }

    /// <summary>The button at the right end of the value of a row that has a list of values.</summary>
    private Point DropDownButtonOf(string name)
    {
        var cell = ValueCell(name);
        return new Point(cell.Right - 8, cell.Top + cell.Height / 2);
    }

    private static Point CentreOf(Control control) => control.PointToScreen(new Point(control.Width / 2, control.Height / 2));

    // ── 10. Delete moves the photo to the Deleted folder ────────────────────────────────────────

    private async Task DeleteKey()
    {
        checks.Step("10. Delete moves the photo to the Deleted folder, and the scan leaves that folder out");

        var deletedFolder = Path.Combine(folder, "Deleted");
        var total = form.Config.Files.Count;
        checks.That(form.Config.GoThroughSubFolders && total == 10, $"the list is the 10 photos of the folder and its subfolder ({total})");

        // A photo of the subfolder, in the middle of the list: the next one is shown after it
        var market = form.Config.Files.FindIndex(f => f.OriginalFilePath.EndsWith("06-market.jpg"));
        await GoTo(market);
        var next = form.Config.Files[market + 1].OriginalFilePath;
        form.Press(Keys.Delete);
        var movedMarket = Path.Combine(deletedFolder, DemoFolder.SubFolder, "06-market.jpg");
        checks.That(await Until(() => File.Exists(movedMarket) && form.Config.Files.Count == total - 1 && PhotoShown()), "Delete moves 06-market.jpg to 'Deleted/Day 2', keeping its subfolder, and takes it out of the list");
        checks.That(!File.Exists(Path.Combine(folder, DemoFolder.SubFolder, "06-market.jpg")), "it is gone from where it was");
        checks.That(form.Config.CurrentFilePath == next && form.Config.CurrentIndex == market, $"the photo after it is shown, at the same place ({form.Config.CurrentFileName}, index {form.Config.CurrentIndex})");
        checks.That(form.Status.Contains("moved to the Deleted folder") && form.Status.Contains($"In the list now: {total - 1}."), $"status: {form.Status}");
        checks.That(form.Find<FlowLayoutPanel>("previewFlowPanel").Controls.OfType<PictureBox>().All(t => (string)t.Tag! != Path.Combine(folder, DemoFolder.SubFolder, "06-market.jpg")), "and its thumbnail is gone");

        // The last photo: the one before it is shown
        form.Press(Keys.End);
        checks.That(await Until(() => form.Config.CurrentIndex == form.Config.Files.Count - 1 && PhotoShown()), "End goes to the last photo");
        var last = form.Config.CurrentFilePath;
        var beforeLast = form.Config.Files[^2].OriginalFilePath;
        form.Press(Keys.Delete);
        checks.That(await Until(() => !File.Exists(last) && form.Config.Files.Count == total - 2 && PhotoShown()), "Delete on the last photo moves it too");
        checks.That(form.Config.CurrentFilePath == beforeLast && form.Config.CurrentIndex == form.Config.Files.Count - 1, $"and the photo before it is shown ({form.Config.CurrentFileName})");

        // Listing the folder again does not bring them back: the Deleted folder is inside the folder, but the scan skips it
        await ChangeSetting("GoThroughSubFolders", false);
        await Until(() => form.Config.Files.Count == total - 2 - (DemoFolder.CountInSubFolder - 1));
        checks.That(form.Config.Files.All(f => !f.OriginalFilePath.StartsWith(deletedFolder)), "the top-level list holds nothing of the Deleted folder");
        await ChangeSetting("GoThroughSubFolders", true);
        checks.That(await Until(() => form.Config.Files.Count == total - 2 && PhotoShown()), $"with the subfolders on again there are {form.Config.Files.Count} photos, not the ones in Deleted");
        checks.That(form.Config.Files.All(f => !f.OriginalFilePath.StartsWith(deletedFolder)), "none of them is in the Deleted folder");

        // Delete until nothing is left: only the PNG files that are still there (the last photo above was one)
        await ChangeSetting("Extensions", new[] { ".png" });
        checks.That(await Until(() => form.Config.Files.All(f => f.OriginalFilePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) && PhotoShown()), "only the PNG files are listed");
        var pngs = form.Config.Files.Count;
        for (var i = 0; i < pngs; i++)
        {
            form.Press(Keys.Delete);
            var expected = pngs - i - 1;
            checks.That(await Until(() => form.Config.Files.Count == expected && (expected == 0 || PhotoShown())), $"Delete on a PNG leaves {expected}");
        }
        checks.That(form.Viewer.Image is null && form.Viewer.Tag is null && form.Config.TotalFiles == 0, "with the list empty nothing is shown");
        checks.That(form.Status.Contains("In the list now: 0."), $"status: {form.Status}");
        form.Press(Keys.Delete);
        form.Press(Keys.Right);
        form.Press(Keys.End);
        await Settle(300);
        checks.That(form.Config.TotalFiles == 0 && File.Exists(Path.Combine(deletedFolder, "09-panorama.png")) && File.Exists(Path.Combine(deletedFolder, "10-night.png")), "Delete, Right and End on the empty list do nothing, and both PNG files are in Deleted");

        await ChangeSetting("Extensions", new[] { ".jpg", ".png" });
        checks.That(await Until(() => form.Config.Files.Count == total - 3 && PhotoShown()), $"with the extensions back the list has {form.Config.Files.Count} photos (the three deleted ones are not in it)");
    }

    // ── what the tour leaves behind, checked after the window is closed ─────────────────────────

    public void VerifySavedSettings()
    {
        checks.Step("11. The settings file written when the window closed");

        var path = Path.Combine(folder, "photo-liker-config.json");
        var text = File.ReadAllText(path);
        using var json = System.Text.Json.JsonDocument.Parse(text);
        var root = json.RootElement;

        checks.That(root.GetProperty("CurrentFolder").GetString() == ".", "CurrentFolder is stored as '.'");
        checks.That(root.GetProperty("LikedFolder").GetString() == "Liked", "LikedFolder is stored as 'Liked'");
        checks.That(root.TryGetProperty("GoThroughtSubFolders", out var subfolders) && subfolders.GetBoolean(), "GoThroughtSubFolders (the spelling of the file) is true");
        checks.That(root.GetProperty("IsDarkTheme").GetBoolean(), "IsDarkTheme is true");
        checks.That(root.GetProperty("Extensions").EnumerateArray().Select(e => e.GetString()).SequenceEqual([".jpg", ".png"]), "Extensions are .jpg and .png");
        checks.That(root.GetProperty("SortOrder").GetString() == "OldestFirst", "SortOrder is stored by name: OldestFirst");
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
