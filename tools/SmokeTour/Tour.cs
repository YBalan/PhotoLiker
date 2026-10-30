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
        await SavedList();         // it changes the folder from outside and restores it
        await Metadata();
        await ThemeToggle();
        await FitKeyAndGroupO(); // the folder of group O would show in the legend of the screenshots after it
        await FocusAfterEditing(); // it changes settings and clicks around, which would show in the screenshots
        await DeleteKey();         // the demo photos it moves away are not there for the steps after it
        await AlignDates();        // it gives the files other dates, which the order of the list and the months follow
        await GiveDates();         // photos without an EXIF date get one from a person
        await DoubleClicks();      // a double click on a row of the three dialogs opens the photo or the folder
        await UnreadableList();    // empty photo files are listed, and move to Deleted\Unreadable
        await RemoveEmptyFolders(); // it makes folders in the demo folder and removes them again
        await FindDuplicates();     // it makes photos in the demo folder, moves them away and brings them back
        await Rotate();             // last: it turns demo photos and makes two files in the demo folder, which it removes again
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
        var likedCopy = Path.Combine(_likedFolder, CopyNameAt(1));
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
        var name = CopyNameAt(2);
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

    // ── 5b. the saved list of the folder ────────────────────────────────────────────────────────

    /// <summary>
    /// The folder opens from the list saved at the last visit and is checked afterwards. To see which list opens, a photo is
    /// taken out of the saved file: the folder did not change, so the app does not look again, and F5 does.
    /// </summary>
    private async Task SavedList()
    {
        checks.Step("5b. A folder opens from its saved list, is checked for changes afterwards, and F5 lists it again");

        var key = new ListingKey(folder, form.Config.GoThroughSubFolders, form.Config.Extensions, form.Config.LikedFolder);
        var total = form.Config.Files.Count;
        var shown = form.Config.CurrentFilePath;
        checks.That(await Until(() => File.Exists(key.CacheFile)), "the list of the folder was saved when it was listed");

        // A photo that is not the shown one goes out of the saved list (the app has its own copy of the list in memory, which it does not write)
        var hidden = form.Config.Files.First(f => f.OriginalFilePath != shown).OriginalFilePath;
        var saved = PhotoListing.Load(key)!;
        saved.Remove(hidden);
        saved.Save();

        await ChangeSetting("SortOrder", PhotoSortOrder.NewestFirst);
        checks.That(await Until(() => form.Config.Files.Count == total - 1 && PhotoShown()),
            $"reloading opens the saved list: {form.Config.Files.Count} photos, one less than on disk ({total})");
        checks.That(form.Config.Files.All(f => f.OriginalFilePath != hidden) && File.Exists(hidden), "the photo left out is still on disk");
        checks.That(form.Config.CurrentFilePath == shown, "and the shown photo is still shown");

        form.Press(Keys.F5);
        checks.That(await Until(() => form.Config.Files.Count == total && PhotoShown() && form.Status.StartsWith("Loaded")), $"F5 lists the folder again: {form.Config.Files.Count} photos");
        checks.That(!form.Status.Contains("Checked"), $"it did not take the saved list ({form.Status})");

        // Changes made outside the app (a folder with two photos comes, then goes) are found by the check that follows the opening
        var added = Path.Combine(folder, "Zz Saved List");
        await Task.Delay(200); // the folder's time must differ from the one it was listed with
        Directory.CreateDirectory(added);
        File.Copy(hidden, Path.Combine(added, "one.jpg"));
        File.Copy(hidden, Path.Combine(added, "two.jpg"));

        await ChangeSetting("SortOrder", PhotoSortOrder.OldestFirst);
        checks.That(await Until(() => form.Config.Files.Count == total + 2 && form.Status.Contains("2 added")), $"the check finds the two new photos: {form.Config.Files.Count} photos, {form.Status}");
        checks.That(form.Config.CurrentFilePath == shown && form.Config.Files[form.Config.CurrentIndex].OriginalFilePath == shown, "the shown photo stays shown, now at its new place in the list");
        checks.That(PhotoShown(), "and it is still on screen");

        await Task.Delay(200);
        Directory.Delete(added, recursive: true);
        await ChangeSetting("SortOrder", PhotoSortOrder.NewestFirst);
        checks.That(await Until(() => form.Config.Files.Count == total && form.Status.Contains("2 removed")), $"and the check finds them gone again: {form.Config.Files.Count} photos, {form.Status}");

        await ChangeSetting("SortOrder", PhotoSortOrder.OldestFirst); // the steps after this one rely on it
        checks.That(await Until(() => form.Config.Files.Count == total && form.Config.CurrentFilePath == shown && PhotoShown()), "the folder is as it was before this step");
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

        // The dates of the file next to the dates of the photo
        var fileDate = DrivenForm.Row(form.MetadataGrid, "File Date");
        var imageRows = fileDate.Parent!.GridItems.Cast<GridItem>().Select(i => i.Label!).ToList();
        checks.That(fileDate.Parent.Label == "1. Image", $"the File Date row is listed with the Image rows: {string.Join(", ", imageRows)}");
        checks.That(fileDate.Value is string text && text.EndsWith("(≠ Date Taken (Original))"), $"and says that the file date is not the date taken ({fileDate.Value})");

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

        var name = CopyNameAt(form.Config.CurrentIndex);

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

    // ── 11. Align dates ─────────────────────────────────────────────────────────────────────────

    private async Task AlignDates()
    {
        checks.Step("11. Align dates: the dialog lists what would change, the button makes the file dates the date taken");

        var button = (ToolStripButton)form.ToolbarItem("alignDatesToolStripButton");
        checks.That(button.Image is not null && button.Enabled && button.ToolTipText!.Contains("date the photo was taken"), "the toolbar has an Align dates button with an icon");

        await ChangeSetting("GoThroughSubFolders", true);
        await ChangeSetting("Extensions", new[] { ".jpg", ".png" });
        await Until(() => form.Config.Files.Count > 0 && PhotoShown());
        var photos = form.Config.Files.Select(f => f.OriginalFilePath).ToList();
        var takenAt = photos.ToDictionary(p => p, p => FileDateRow.TakenOf(new FriendlyImageMetadata(p).Entries));
        checks.That(takenAt.Values.All(t => t is not null), $"all {photos.Count} photos left in the demo folder have a date taken");
        checks.That(photos.All(p => !DateAlign(File.GetCreationTime(p), takenAt[p]!.Value)), "and the dates of their files are others");

        // The dialog is modal: it is opened by a posted click, so that the tour can go on while it is up
        form.BeginInvoke(button.PerformClick);
        Form? dialog = null;
        checks.That(await Until(() => (dialog = FindDialog("DateAlignmentDialog")) is not null), "the dialog opens");
        if (dialog is null)
            return;

        checks.That(await Until(() => Buttons(dialog).Any(b => b.Text.StartsWith("Change ") && b.Enabled), 30000), "it looks at the photos and offers to change them");
        var apply = Buttons(dialog).First(b => b.Text.StartsWith("Change "));
        var list = Controls<ListView>(dialog).Single();
        checks.That(apply.Text == $"Change {photos.Count} photos" && list.VirtualListSize == photos.Count, $"all {photos.Count} photos will change and are listed ({apply.Text}, {list.VirtualListSize} rows)");
        checks.That(Controls<RadioButton>(dialog).Any(r => r.Text == $"Will change ({photos.Count})" && r.Checked), "the filter shows what will change");
        checks.That(Controls<Label>(dialog).Any(l => l.Text.StartsWith($"{photos.Count} of {photos.Count} photos will change")), "the summary says how many");
        checks.That(Controls<CheckBox>(dialog).Select(c => c.Checked).SequenceEqual([true, true, false]), "created and modified are ticked, the dates inside the photo are not");
        checks.That(photos.All(p => !DateAlign(File.GetCreationTime(p), takenAt[p]!.Value)), "nothing has been touched yet");

        dialog.TopMost = true; // the window behind it is topmost for the photographs, and would cover it
        ScreenCapture.BringToFront(dialog.Handle);
        await Settle(600);
        using (var picture = ScreenCapture.Capture(dialog.Handle))
            Save(picture, "11-align-dates");

        apply.PerformClick();
        checks.That(await Until(() => Controls<Label>(dialog).Any(l => l.Text.StartsWith("Done: ")), 30000), "it does the work");
        var status = Controls<Label>(dialog).FirstOrDefault(l => l.Text.StartsWith("Done: "))?.Text ?? string.Empty;
        checks.Note(status);
        checks.That(status.StartsWith($"Done: {photos.Count} photos changed"), "and reports how many were changed");
        checks.That(photos.All(p => DateAlign(File.GetCreationTime(p), takenAt[p]!.Value) && DateAlign(File.GetLastWriteTime(p), takenAt[p]!.Value)), "the files now have the date taken as created and modified date");
        checks.That(Directory.GetFiles(folder, "photo-liker-datefix-*.jsonl").Length == 1, "the run left its log in the photo folder");
        checks.That(Buttons(dialog).First(b => b.Text.StartsWith("Undo")).Enabled, "and Undo is on offer");
        checks.That(list.VirtualListSize == 0 && Buttons(dialog).Any(b => b.Text == "Nothing to change"), "nothing is left to change");

        Buttons(dialog).First(b => b.Text == "Close").PerformClick();
        checks.That(await Until(() => FindDialog("DateAlignmentDialog") is null), "the dialog closes");

        // The folder is listed again: oldest first now means by the date taken
        var byTaken = photos.OrderBy(p => takenAt[p]).ToList();
        checks.That(await Until(() => form.Config.Files.Count == photos.Count && PhotoShown() && form.Config.Files.Select(f => f.OriginalFilePath).SequenceEqual(byTaken)), "the list is sorted by the date taken");
        checks.That(await Until(() => DrivenForm.Row(form.MetadataGrid, "File Date").Value is string text && text.EndsWith("(= Date Taken (Original))")), $"and the File Date row of the photo shown says it is the date taken ({DrivenForm.Row(form.MetadataGrid, "File Date").Value})");
        (await Shot("12-aligned")).Dispose();

        // The second time there is nothing to do
        form.BeginInvoke(button.PerformClick);
        checks.That(await Until(() => (dialog = FindDialog("DateAlignmentDialog")) is not null), "opened again,");
        checks.That(await Until(() => dialog is not null && Controls<Label>(dialog).Any(l => l.Text.StartsWith("Nothing to change")), 30000), "it finds all the photos aligned");
        if (dialog is not null)
            Buttons(dialog).First(b => b.Text == "Close").PerformClick();
        await Until(() => FindDialog("DateAlignmentDialog") is null);
    }

    // ── 12. Remove empty folders ────────────────────────────────────────────────────────────────

    private async Task RemoveEmptyFolders()
    {
        checks.Step("12. Remove empty folders: the dialog lists them, the ticks follow the tree, the button removes only what is ticked");

        // A messy corner of the demo folder: two empty folders one inside the other, one with only Thumbs.db (hidden, as Windows makes it), one empty
        var thumbsOnly = Path.Combine(folder, "Only thumbs");
        Directory.CreateDirectory(thumbsOnly);
        var thumbs = Path.Combine(thumbsOnly, "Thumbs.db");
        File.WriteAllText(thumbs, "cache");
        File.SetAttributes(thumbs, FileAttributes.Hidden | FileAttributes.System);
        var emptyA = Path.Combine(folder, "Empty A");
        var day3 = Path.Combine(folder, "Day 3");
        var nothingYet = Path.Combine(day3, "Nothing yet");
        Directory.CreateDirectory(emptyA);
        Directory.CreateDirectory(nothingYet);

        // And what has to stay: a folder with a file that is not a photo, an empty group folder, an empty folder in Deleted
        var notes = Path.Combine(folder, "Keep me", "notes.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(notes)!);
        File.WriteAllText(notes, "not a photo, but not a thumbnail cache either");
        var emptyGroup = Path.Combine(_likedFolder, "Z - Group");
        var emptyInDeleted = Path.Combine(folder, "Deleted", "Empty in Deleted");
        Directory.CreateDirectory(emptyGroup);
        Directory.CreateDirectory(emptyInDeleted);
        var photosBefore = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories).Where(f => f.EndsWith(".jpg") || f.EndsWith(".png")).Count();

        var button = (ToolStripButton)form.ToolbarItem("removeEmptyFoldersToolStripButton");
        checks.That(button.Image is not null && button.Enabled, "the toolbar has a Remove empty folders button with an icon");
        form.BeginInvoke(button.PerformClick);
        Form? dialog = null;
        checks.That(await Until(() => (dialog = FindDialog("EmptyFoldersDialog")) is not null), "the dialog opens");
        if (dialog is null)
            return;

        var list = Controls<ListView>(dialog).First();
        var remove = () => Buttons(dialog).First(b => b.Text.StartsWith("Remove ") || b.Text == "Nothing to remove");
        checks.That(await Until(() => list.Items.Count == 4 && remove().Enabled, 30000), "it looks at the folders and offers to remove them");
        var rows = list.Items.Cast<ListViewItem>().ToList();
        checks.That(rows.Select(r => r.Text).SequenceEqual(["Day 3", "Day 3\\Nothing yet", "Empty A", "Only thumbs"]), $"it lists the four empty folders ({string.Join("; ", rows.Select(r => r.Text))})");
        checks.That(rows.Select(r => r.SubItems[1].Text).SequenceEqual(["1 empty folder", "nothing", "nothing", "Thumbs.db"]), $"and says what is in each ({string.Join("; ", rows.Select(r => r.SubItems[1].Text))})");
        checks.That(rows.All(r => r.Checked) && remove().Text == "Remove 4 folders", $"all are ticked ({remove().Text})");
        checks.That(Controls<Label>(dialog).Any(l => l.Text == "4 empty folders found, with 1 thumbnail file in them."), "the summary counts them and the thumbnail file");
        checks.That(Directory.Exists(emptyA) && File.Exists(thumbs), "nothing has been removed yet");

        dialog.TopMost = true; // the window behind it is topmost for the photographs, and would cover it
        ScreenCapture.BringToFront(dialog.Handle);
        await Settle(600);
        using (var picture = ScreenCapture.Capture(dialog.Handle))
            Save(picture, "13-empty-folders");

        // Un-ticking the folder inside takes the folder around it with it: that one could not be removed without it
        rows[1].Checked = false;
        checks.That(!rows[0].Checked && rows[2].Checked && rows[3].Checked && remove().Text == "Remove 2 folders", $"un-ticking 'Nothing yet' un-ticks 'Day 3' too ({remove().Text})");

        remove().PerformClick();
        checks.That(await Until(() => Controls<Label>(dialog).Any(l => l.Text.StartsWith("Removed "))), "it removes what is ticked");
        var status = Controls<Label>(dialog).First(l => l.Text.StartsWith("Removed ")).Text;
        checks.Note(status);
        checks.That(status == "Removed 2 folders and 1 thumbnail file.", "and the thumbnail file with its folder");
        checks.That(!Directory.Exists(emptyA) && !Directory.Exists(thumbsOnly), "'Empty A' and 'Only thumbs' are gone");
        checks.That(Directory.Exists(nothingYet) && Directory.Exists(day3), "'Day 3' and 'Nothing yet' were not ticked and stay");
        checks.That(list.Items.Count == 2, "the list holds the two that are left");

        // Ticking the outer one ticks the inner one: the outer one would not go otherwise
        Buttons(dialog).First(b => b.Text == "Untick all").PerformClick();
        checks.That(list.Items.Cast<ListViewItem>().All(i => !i.Checked) && remove().Text == "Nothing to remove", "Untick all leaves nothing to remove");
        list.Items[0].Checked = true;
        checks.That(list.Items[1].Checked && remove().Text == "Remove 2 folders", $"ticking 'Day 3' ticks the folder inside ({remove().Text})");
        remove().PerformClick();
        checks.That(await Until(() => !Directory.Exists(day3) && list.Items.Count == 0), "and both go");

        checks.That(File.Exists(notes) && Directory.Exists(emptyGroup) && Directory.Exists(emptyInDeleted), "a folder with a file that is not a thumbnail cache, the empty group folder and the empty folder in Deleted stay");
        checks.That(Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories).Count(f => f.EndsWith(".jpg") || f.EndsWith(".png")) == photosBefore, "and no photo was touched");
        checks.That(remove().Text == "Nothing to remove" && Controls<Label>(dialog).Any(l => l.Text == "There are no empty folders."), "the dialog has nothing left to offer");

        Buttons(dialog).First(b => b.Text == "Close").PerformClick();
        checks.That(await Until(() => FindDialog("EmptyFoldersDialog") is null), "the dialog closes");
        checks.That(form.Status == "Removed 4 empty folders.", $"status: {form.Status}");
    }

    // ── 13. Find duplicates ─────────────────────────────────────────────────────────────────────

    private async Task FindDuplicates()
    {
        checks.Step("13. Find duplicates: the folders decide which copy stays, a person can decide the rest, and the undo puts the copies back");

        // Three groups of photos (real small JPEGs, so the window can show them afterwards): a photo in a camera folder, in a date-named folder
        // under another name and in a Deleted folder; one in a folder with a name of its own and one in a camera folder; one in two camera folders
        string P(string relative) => Path.Combine(folder, "Dups", relative);
        void Write(string relative, byte[] bytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(P(relative))!);
            File.WriteAllBytes(P(relative), bytes);
        }
        var a = MakeJpeg(11);
        var b = MakeJpeg(22);
        var c = MakeJpeg(33);
        Write(@"107_2311\IMG_0001.jpg", a);
        Write(@"2025-08-14\Beach, renamed.jpg", a);
        Write(@"Deleted\107_2311\IMG_0001.jpg", a);
        Write(@"135___07\IMG_0007.jpg", b);
        Write("Natasha's camera\\135___07\\IMG_0007.jpg", b);
        Write(@"400CANON\IMG_0100.jpg", c);
        Write(@"401CANON\IMG_0100.jpg", c);
        Write(@"Liked\IMG_0001.jpg", a); // a copy on purpose, in a folder that is called Liked (not the one the settings name): not a duplicate
        var liked = P(@"Liked\IMG_0001.jpg");

        var button = (ToolStripButton)form.ToolbarItem("findDuplicatesToolStripButton");
        checks.That(button.Image is not null && button.Enabled, "the toolbar has a Find duplicates button with an icon");
        form.BeginInvoke(button.PerformClick);
        Form? dialog = null;
        checks.That(await Until(() => (dialog = FindDialog("DuplicatesDialog")) is not null), "the dialog opens");
        if (dialog is null)
            return;

        var list = Controls<ListView>(dialog).Single();
        var move = () => Buttons(dialog).First(x => x.Text.StartsWith("Move ") || x.Text == "Nothing to move");
        var status = () => Controls<Label>(dialog).Select(l => l.Text).ToList();
        checks.That(await Until(() => move().Enabled, 60000), "it compares the photos that have the same size and offers to move copies");
        checks.That(move().Text == "Move 3 photos", $"two groups are decided, with three copies to move ({move().Text})");
        checks.That(Controls<RadioButton>(dialog).Select(r => r.Text).SequenceEqual(["Will move (2)", "Need your choice (1)", "Left alone (0)", "All groups (3)"]),
            $"the filters count the groups ({string.Join(", ", Controls<RadioButton>(dialog).Select(r => r.Text))})");
        checks.That(status().Any(t => t.StartsWith("3 groups of identical photos: 3 photos will move to Deleted\\Duplicated")), "the summary says it");
        checks.That(status().Any(t => t.Contains("1 photo in Liked and group folders were left out")), $"and that the photo in a folder called Liked was left out ({status().FirstOrDefault(t => t.Contains("looked at"))})");

        string Cell(int row, int column) => list.Items[row].SubItems[column].Text;
        var rows = Enumerable.Range(0, list.VirtualListSize).Select(i => (Photo: Cell(i, 2), Action: Cell(i, 1), Why: Cell(i, 4))).ToList();
        checks.That(rows.Count == 5, $"the list shows the five photos of the two groups ({rows.Count})");
        string Stays(string group) => rows.First(r => r.Photo.Contains(group) && r.Action == "stays").Photo;
        checks.That(Stays("Beach, renamed").EndsWith("2025-08-14\\Beach, renamed.jpg") && rows.Count(r => r.Photo.Contains("107_2311") && r.Action.StartsWith("moves")) == 2,
            "the photo in the date-named folder stays, under its own name; the ones in the camera folder and in Deleted move");
        checks.That(rows.Any(r => r.Photo.EndsWith("Natasha's camera\\135___07\\IMG_0007.jpg") && r.Action == "stays") && rows.Any(r => r.Photo.EndsWith("Dups\\135___07\\IMG_0007.jpg") && r.Action.StartsWith("moves")),
            "a folder with a name of its own beats a camera folder, even when the camera folder is in it");
        checks.That(rows.Where(r => r.Action == "stays").All(r => r.Why.Contains("name")), $"every row says why ({string.Join(" | ", rows.Select(r => r.Why).Distinct())})");

        dialog.TopMost = true; // the window behind it is topmost for the photographs, and would cover it
        ScreenCapture.BringToFront(dialog.Handle);
        await Settle(600);
        using (var picture = ScreenCapture.Capture(dialog.Handle))
            Save(picture, "14-duplicates");

        // The group the folders can not decide waits for a person
        Controls<RadioButton>(dialog).First(r => r.Text.StartsWith("Need your choice")).Checked = true;
        checks.That(await Until(() => list.VirtualListSize == 2), "the group of two camera folders is listed under 'Need your choice'");
        checks.That(Cell(0, 1) == "? your choice" && Cell(1, 1) == "? your choice", "with a question mark on both copies");
        var pick = Enumerable.Range(0, 2).First(i => Cell(i, 2).Contains("400CANON"));
        list.SelectedIndices.Add(pick);
        Buttons(dialog).First(x => x.Text == "Keep this one").PerformClick();
        checks.That(await Until(() => move().Text == "Move 4 photos"), $"'Keep this one' on the 400CANON copy decides it ({move().Text})");

        // A group can be left alone, and touched again
        Controls<RadioButton>(dialog).First(r => r.Text.StartsWith("All groups")).Checked = true;
        await Until(() => list.VirtualListSize == 7);
        var natasha = Enumerable.Range(0, list.VirtualListSize).First(i => Cell(i, 2).Contains("IMG_0007"));
        list.SelectedIndices.Clear();
        list.SelectedIndices.Add(natasha);
        Buttons(dialog).First(x => x.Text == "Leave group alone").PerformClick();
        checks.That(await Until(() => move().Text == "Move 3 photos"), $"'Leave group alone' takes a group out of the run ({move().Text})");
        list.SelectedIndices.Clear();
        list.SelectedIndices.Add(Enumerable.Range(0, list.VirtualListSize).First(i => Cell(i, 2).Contains("IMG_0007")));
        Buttons(dialog).First(x => x.Text == "Leave group alone").PerformClick();
        checks.That(await Until(() => move().Text == "Move 4 photos"), $"and pressed again it is back in ({move().Text})");
        checks.That(File.Exists(P(@"107_2311\IMG_0001.jpg")) && File.Exists(P(@"401CANON\IMG_0100.jpg")), "nothing has been moved yet");

        move().PerformClick();
        checks.That(await Until(() => status().Any(t => t.StartsWith("Done: ")), 60000), "it moves the copies");
        var done = status().First(t => t.StartsWith("Done: "));
        checks.Note(done);
        checks.That(done.StartsWith("Done: 4 photos moved to Deleted\\Duplicated"), "and reports how many");
        var duplicated = Path.Combine(folder, "Deleted", "Duplicated", "Dups");
        checks.That(File.Exists(Path.Combine(duplicated, "107_2311", "IMG_0001.jpg")) && File.Exists(Path.Combine(duplicated, "Deleted", "107_2311", "IMG_0001.jpg"))
            && File.Exists(Path.Combine(duplicated, "135___07", "IMG_0007.jpg")) && File.Exists(Path.Combine(duplicated, "401CANON", "IMG_0100.jpg")),
            "the four copies are in Deleted\\Duplicated, each with the path it had");
        checks.That(!File.Exists(P(@"107_2311\IMG_0001.jpg")) && File.Exists(P(@"2025-08-14\Beach, renamed.jpg")) && File.Exists(P("Natasha's camera\\135___07\\IMG_0007.jpg")) && File.Exists(P(@"400CANON\IMG_0100.jpg")),
            "and the three that stay are where they were");
        checks.That(File.Exists(liked), "the copy in the group folder was not touched");
        checks.That(Directory.GetFiles(folder, "photo-liker-duplicates-*.jsonl").Length == 1, "the run left its log in the photo folder");
        checks.That(list.VirtualListSize == 0 && move().Text == "Nothing to move" && Buttons(dialog).First(x => x.Text.StartsWith("Undo")).Enabled, "nothing is left to move, and Undo is on offer");

        // Undo: the question is a message box, answered with the key of its Yes button
        form.BeginInvoke(Buttons(dialog).First(x => x.Text.StartsWith("Undo")).PerformClick);
        checks.That(await Until(() => ScreenCapture.FindDialogOfThisThread(Window, dialog.Handle) != IntPtr.Zero), "Undo asks first");
        await Settle(500);
        SendKeys.Send("y");
        checks.That(await Until(() => status().Any(t => t.StartsWith("Put back: 4 photos"))), $"and puts the four copies back ({status().FirstOrDefault(t => t.StartsWith("Put back"))})");
        checks.That(File.Exists(P(@"107_2311\IMG_0001.jpg")) && File.Exists(P(@"401CANON\IMG_0100.jpg")) && !Directory.Exists(duplicated), "they are where they were, and Deleted\\Duplicated holds nothing of them");
        checks.That(await Until(() => Controls<RadioButton>(dialog).Any(r => r.Text == "All groups (3)")), "it looks again, and finds the three groups once more");

        Buttons(dialog).First(x => x.Text == "Close").PerformClick();
        checks.That(await Until(() => FindDialog("DuplicatesDialog") is null), "the dialog closes");
        checks.That(await Until(() => form.Config.Files.Any(f => f.OriginalFilePath.Contains("Dups")) && PhotoShown()), "the main window lists the folder again, with the photos that are back");
    }

    /// <summary>A small picture that no other seed makes: a JPEG that the window can show, with a content of its own.</summary>
    private static byte[] MakeJpeg(int seed)
    {
        using var bitmap = new Bitmap(320, 240);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            var random = new Random(seed);
            graphics.Clear(Color.FromArgb(random.Next(256), random.Next(256), random.Next(256)));
            for (var i = 0; i < 40; i++)
            {
                using var brush = new SolidBrush(Color.FromArgb(random.Next(256), random.Next(256), random.Next(256)));
                graphics.FillEllipse(brush, random.Next(300), random.Next(220), random.Next(10, 120), random.Next(10, 120));
            }
        }
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Jpeg);
        return stream.ToArray();
    }

    // ── 11b. Photos without a usable EXIF date get one from a person ────────────────────────────

    private async Task GiveDates()
    {
        checks.Step("11b. Photos that have no usable EXIF date can be given one: from their folder name, or typed; then they are changed like the others");

        // Two photos with no EXIF at all, one in a folder with a date in its name
        string P(string relative) => Path.Combine(folder, "No date", relative);
        Directory.CreateDirectory(Path.GetDirectoryName(P(@"2015-05-01\a.jpg"))!);
        Directory.CreateDirectory(Path.GetDirectoryName(P(@"Somewhere\b.jpg"))!);
        File.WriteAllBytes(P(@"2015-05-01\a.jpg"), MakeJpeg(91));
        File.WriteAllBytes(P(@"Somewhere\b.jpg"), MakeJpeg(92));

        var button = (ToolStripButton)form.ToolbarItem("alignDatesToolStripButton");
        form.BeginInvoke(button.PerformClick);
        Form? dialog = null;
        checks.That(await Until(() => (dialog = FindDialog("DateAlignmentDialog")) is not null), "the dialog opens");
        if (dialog is null)
            return;

        var list = Controls<ListView>(dialog).Single();
        string Cell(int row, int column) => list.Items[row].SubItems[column].Text;
        string Label(string prefix) => Controls<Label>(dialog).Select(l => l.Text).FirstOrDefault(t => t.StartsWith(prefix)) ?? string.Empty;
        checks.That(await Until(() => Controls<RadioButton>(dialog).Any(r => r.Text == "No usable EXIF date (2)"), 60000), "it finds the two photos that have no usable EXIF date");
        Controls<RadioButton>(dialog).First(r => r.Text.StartsWith("No usable EXIF date")).Checked = true;
        checks.That(await Until(() => list.VirtualListSize == 2), "and lists them");
        checks.That(Buttons(dialog).First(b => b.Text == "Use a date...").Enabled, "there is a 'Use a date...' button");

        // A photo of the dated folder, with its own choice of where the date comes from
        list.SelectedIndices.Add(Enumerable.Range(0, 2).First(i => Cell(i, 0).Contains("2015-05-01")));
        form.BeginInvoke(Buttons(dialog).First(b => b.Text == "Use a date...").PerformClick);
        Form? chooser = null;
        checks.That(await Until(() => (chooser = FindDialog("AssignDateDialog")) is not null), "the button asks where the date comes from");
        if (chooser is null)
            return;

        var radios = Controls<RadioButton>(chooser);
        var labels = Controls<Label>(chooser).Select(l => l.Text).ToList();
        checks.That(radios.Any(r => r.Text == "The 1 selected" && r.Checked) && radios.Any(r => r.Text == "All 2 photos without a usable date"), "for the selected photo, or for all of them");
        checks.That(labels.Any(t => t.StartsWith("none of 1 has one")), $"it says that the photo has no EXIF date of its own to take ({string.Join(" | ", labels.Where(t => t.Contains("of 1")))})");
        checks.That(labels.Any(t => t.Contains("1 of 1, for example 01 May 2015 12:00:00")), "and that its folder gives 01 May 2015 at noon");
        checks.That(labels.Any(t => t.Contains("only their file dates can be set")), "and that EXIF has no place for a date in these photos");
        checks.That(radios.First(r => r.Text.StartsWith("The date in the name of its folder")).Checked == false, "the source is for the person to choose");
        radios.First(r => r.Text.StartsWith("The date in the name of its folder")).Checked = true;
        chooser.TopMost = true; // the window behind it is topmost for the photographs, and would cover it
        ScreenCapture.BringToFront(chooser.Handle);
        await Settle(500);
        using (var picture = ScreenCapture.Capture(chooser.Handle))
            Save(picture, "11b-use-a-date");
        checks.That(Buttons(chooser).First(b => b.Text == "Use this date").Enabled, "and the folder date can be used");
        Buttons(chooser).First(b => b.Text == "Use this date").PerformClick();
        checks.That(await Until(() => FindDialog("AssignDateDialog") is null), "the question closes");

        checks.That(Controls<Label>(dialog).Any(l => l.Text.StartsWith("Gave 1 photo a date taken")), $"it says so ({Label("Gave")})");
        checks.That(Controls<RadioButton>(dialog).Any(r => r.Text == "No usable EXIF date (1)") && Controls<RadioButton>(dialog).Any(r => r.Text == "Will change (1)"), "and the photo moves from 'No usable EXIF date' to 'Will change'");
        Controls<RadioButton>(dialog).First(r => r.Text.StartsWith("Will change")).Checked = true;
        await Until(() => list.VirtualListSize == 1);
        checks.That(Cell(0, 1) == "01 May 2015 12:00:00 (given)" && Cell(0, 4).EndsWith("date given"), $"the list shows the date and that it was given ({Cell(0, 1)} | {Cell(0, 4)})");

        // The other one: a date typed, for all the photos that are left without one
        Controls<RadioButton>(dialog).First(r => r.Text.StartsWith("No usable EXIF date")).Checked = true;
        await Until(() => list.VirtualListSize == 1);
        form.BeginInvoke(Buttons(dialog).First(b => b.Text == "Use a date...").PerformClick);
        checks.That(await Until(() => (chooser = FindDialog("AssignDateDialog")) is not null), "the button asks again, for the photo that is left");
        if (chooser is null)
            return;
        checks.That(Controls<RadioButton>(chooser).Any(r => r.Text == "All 1 photos without a usable date" && r.Checked), "now for all that are left (none is selected)");
        Controls<DateTimePicker>(chooser).Single().Value = new DateTime(2012, 7, 14, 9, 30, 0);
        checks.That(Controls<RadioButton>(chooser).First(r => r.Text == "This date:").Checked, "typing a date chooses it");
        Buttons(chooser).First(b => b.Text == "Use this date").PerformClick();
        await Until(() => FindDialog("AssignDateDialog") is null);
        checks.That(await Until(() => Controls<RadioButton>(dialog).Any(r => r.Text == "Will change (2)" ) && Controls<RadioButton>(dialog).Any(r => r.Text == "No usable EXIF date (0)")), "both photos are 'Will change' now");

        var apply = Buttons(dialog).First(b => b.Text.StartsWith("Change "));
        checks.That(apply.Text == "Change 2 photos", $"({apply.Text})");
        apply.PerformClick();
        checks.That(await Until(() => Controls<Label>(dialog).Any(l => l.Text.StartsWith("Done: 2 photos changed")), 30000), "and changed");
        bool Close(DateTime a, DateTime b) => Math.Abs((a - b).TotalSeconds) <= 2;
        checks.That(Close(File.GetCreationTime(P(@"2015-05-01\a.jpg")), new DateTime(2015, 5, 1, 12, 0, 0)) && Close(File.GetLastWriteTime(P(@"2015-05-01\a.jpg")), new DateTime(2015, 5, 1, 12, 0, 0)),
            "the photo of the dated folder has 01 May 2015, 12:00 as its file dates");
        checks.That(Close(File.GetCreationTime(P(@"Somewhere\b.jpg")), new DateTime(2012, 7, 14, 9, 30, 0)) && Close(File.GetLastWriteTime(P(@"Somewhere\b.jpg")), new DateTime(2012, 7, 14, 9, 30, 0)),
            "and the other one the date that was typed");

        Buttons(dialog).First(b => b.Text == "Close").PerformClick();
        await Until(() => FindDialog("DateAlignmentDialog") is null);
        await Until(() => form.Config.Files.Any(f => f.OriginalFilePath.Contains("No date")) && PhotoShown());
    }

    // ── 11c. A double click on a row opens the photo or the folder ──────────────────────────────

    /// <summary>
    /// A double click of the real mouse at this place of the control (the pointer moves there, as a person's would): the dialog is brought to
    /// the front first, since the click goes to whatever window is under the pointer. Messages sent to the list instead of real input would
    /// not do: the list looks at the state of the mouse buttons itself and ignores a click that no button is down for.
    /// </summary>
    private async Task DoubleClickAt(Form dialog, Control control, Point at)
    {
        dialog.TopMost = true; // the window behind it is topmost for the photographs, and would take the click
        ScreenCapture.BringToFront(dialog.Handle);
        await Settle(400);
        RealInput.Click(control.PointToScreen(at), 2);
        await Settle(300);
    }

    private async Task DoubleClicks()
    {
        checks.Step("11c. A double click on a row of the Align dates, Find duplicates and Remove empty folders lists opens its photo or folder");

        // Every target is deleted before the double click: the handler has to find the right path, and nothing is launched; what it
        // says when it can not open it shows which path that was
        string P(string relative) => Path.Combine(folder, relative);
        void Stage(string relative, byte[] bytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(P(relative))!);
            File.WriteAllBytes(P(relative), bytes);
        }

        // ── Align dates: a photo without a usable EXIF date is listed, with the path of its file
        Stage(@"Gone\only-here.jpg", MakeJpeg(81));
        var align = (ToolStripButton)form.ToolbarItem("alignDatesToolStripButton");
        form.BeginInvoke(align.PerformClick);
        Form? dialog = null;
        checks.That(await Until(() => (dialog = FindDialog("DateAlignmentDialog")) is not null), "the Align dates dialog opens");
        if (dialog is null)
            return;
        var list = Controls<ListView>(dialog).Single();
        checks.That(await Until(() => Controls<RadioButton>(dialog).Any(r => r.Text.StartsWith("No usable EXIF date (") && !r.Text.EndsWith("(0)")), 60000), "and finds photos without a usable date");
        Controls<RadioButton>(dialog).First(r => r.Text.StartsWith("No usable EXIF date")).Checked = true;
        await Until(() => list.VirtualListSize > 0);
        var row = Enumerable.Range(0, list.VirtualListSize).First(i => list.Items[i].Text.Contains("only-here.jpg"));
        File.Delete(P(@"Gone\only-here.jpg"));
        await DoubleClickAt(dialog, list, list.GetItemRect(row).Location + new Size(40, 6));
        checks.That(await Until(() => Controls<Label>(dialog).Any(l => l.Text == "Could not open 'only-here.jpg': it is not there any more")),
            $"a double click on the row tries to open that photo ({Controls<Label>(dialog).Select(l => l.Text).FirstOrDefault(t => t.StartsWith("Could not")) ?? "no message"})");
        Buttons(dialog).First(b => b.Text == "Close").PerformClick();
        await Until(() => FindDialog("DateAlignmentDialog") is null);

        // ── Find duplicates: the copy in the second folder
        var same = MakeJpeg(82);
        Stage(@"Gone\first\copy.jpg", same);
        Stage(@"Gone\second\copy of it.jpg", same);
        var duplicates = (ToolStripButton)form.ToolbarItem("findDuplicatesToolStripButton");
        form.BeginInvoke(duplicates.PerformClick);
        checks.That(await Until(() => (dialog = FindDialog("DuplicatesDialog")) is not null), "the Find duplicates dialog opens");
        if (dialog is null)
            return;
        list = Controls<ListView>(dialog).Single();
        checks.That(await Until(() => Buttons(dialog).Any(b => b.Text.StartsWith("Move ") && b.Enabled), 60000), "and offers to move a copy");
        row = Enumerable.Range(0, list.VirtualListSize).First(i => list.Items[i].SubItems[2].Text.Contains("copy of it.jpg"));
        File.Delete(P(@"Gone\second\copy of it.jpg"));
        await DoubleClickAt(dialog, list, list.GetItemRect(row).Location + new Size(40, 6));
        checks.That(await Until(() => Controls<Label>(dialog).Any(l => l.Text == "Could not open 'copy of it.jpg': it is not there any more")),
            $"a double click on the row of a copy tries to open that copy ({Controls<Label>(dialog).Select(l => l.Text).FirstOrDefault(t => t.StartsWith("Could not")) ?? "no message"})");
        Buttons(dialog).First(b => b.Text == "Close").PerformClick();
        await Until(() => FindDialog("DuplicatesDialog") is null);

        // ── Remove empty folders: the list has ticks, which a double click must leave as they are
        Directory.CreateDirectory(P(@"Gone2\Hollow"));
        var empty = (ToolStripButton)form.ToolbarItem("removeEmptyFoldersToolStripButton");
        form.BeginInvoke(empty.PerformClick);
        checks.That(await Until(() => (dialog = FindDialog("EmptyFoldersDialog")) is not null), "the Remove empty folders dialog opens");
        if (dialog is null)
            return;
        list = Controls<ListView>(dialog).First();
        checks.That(await Until(() => list.Items.Cast<ListViewItem>().Any(i => i.Text == @"Gone2\Hollow") && Buttons(dialog).Any(b => b.Text.StartsWith("Remove ") && b.Enabled), 60000), "and lists the empty folder");
        var hollow = list.Items.Cast<ListViewItem>().First(i => i.Text == @"Gone2\Hollow");
        Directory.Delete(P("Gone2"), recursive: true);
        var ticked = hollow.Checked;
        await DoubleClickAt(dialog, list, hollow.Bounds.Location + new Size(70, 6)); // to the right of the tick
        checks.That(await Until(() => Controls<Label>(dialog).Any(l => l.Text == "Could not open 'Hollow': it is not there any more")),
            $"a double click on the row tries to open that folder ({Controls<Label>(dialog).Select(l => l.Text).FirstOrDefault(t => t.StartsWith("Could not")) ?? "no message"})");
        checks.That(ticked && hollow.Checked, "and does not flip its tick, as a double click on a list with ticks does");
        Buttons(dialog).First(b => b.Text == "Close").PerformClick();
        await Until(() => FindDialog("EmptyFoldersDialog") is null);

        // Nothing of this step stays for the next ones
        if (Directory.Exists(P("Gone")))
            Directory.Delete(P("Gone"), recursive: true);
    }

    // ── 11d. Photos that can not be read ────────────────────────────────────────────────────────

    private async Task UnreadableList()
    {
        checks.Step("11d. Remove empty folders also lists photos that can not be read (here: empty files), unticked; the ticked ones move to Deleted\\Unreadable");

        string P(string relative) => Path.Combine(folder, relative);
        Directory.CreateDirectory(P("Broken"));
        Directory.CreateDirectory(P("Broken2"));
        File.WriteAllBytes(P(@"Broken\zero.jpg"), []);               // an empty photo file
        File.WriteAllBytes(P(@"Broken\fine.jpg"), MakeJpeg(93));     // a real photo beside it: it stays
        File.WriteAllBytes(P(@"Broken\notes.txt"), []);              // empty, but not a photo
        File.WriteAllBytes(P(@"Broken2\also zero.png"), []);         // the only file of its folder

        var button = (ToolStripButton)form.ToolbarItem("removeEmptyFoldersToolStripButton");
        form.BeginInvoke(button.PerformClick);
        Form? dialog = null;
        checks.That(await Until(() => (dialog = FindDialog("EmptyFoldersDialog")) is not null), "the dialog opens");
        if (dialog is null)
            return;

        var folders = Controls<ListView>(dialog).First(l => l.Columns.Count == 2);
        var photos = Controls<ListView>(dialog).First(l => l.Columns.Count == 3);
        var radios = Controls<RadioButton>(dialog);
        checks.That(await Until(() => radios.Any(r => r.Text == "Unreadable photos (2)"), 60000), $"it finds the two empty photo files ({string.Join(", ", radios.Select(r => r.Text))})");
        checks.That(photos.Visible && !folders.Visible, "and, with no empty folder to show, shows the photos");
        var rows = photos.Items.Cast<ListViewItem>().ToList();
        checks.That(rows.Select(r => r.Text).Order(StringComparer.Ordinal).SequenceEqual([@"Broken2\also zero.png", @"Broken\zero.jpg"]), $"it lists exactly them: not 'notes.txt' (no photo) and not 'fine.jpg' ({string.Join("; ", rows.Select(r => r.Text))})");
        var jpg = rows.First(r => r.Text.EndsWith("zero.jpg"));
        var png = rows.First(r => r.Text.EndsWith("also zero.png"));
        checks.That(rows.All(r => r.SubItems[2].Text == "The file is empty (0 bytes)."), "with the reason");
        checks.That(rows.All(r => !r.Checked), "none is ticked: a photo that can not be read is not thrown away by default");
        var move = () => Buttons(dialog).First(b => b.Text.StartsWith("Move ") || b.Text == "Tick photos to move");
        checks.That(move().Text == "Tick photos to move" && !move().Enabled, "and there is nothing to move yet");

        dialog.TopMost = true; // the window behind it is topmost for the photographs, and would cover it
        ScreenCapture.BringToFront(dialog.Handle);
        await Settle(500);
        using (var picture = ScreenCapture.Capture(dialog.Handle))
            Save(picture, "13b-unreadable-photos");

        Buttons(dialog).First(b => b.Text == "Tick all").PerformClick();
        checks.That(rows.All(r => r.Checked) && move().Text == "Move 2 photos to Deleted\\Unreadable" && move().Enabled, $"'Tick all' ticks them ({move().Text})");
        png.Checked = false;
        checks.That(move().Text == "Move 1 photo to Deleted\\Unreadable", $"one can be left out ({move().Text})");

        move().PerformClick();
        checks.That(await Until(() => Controls<Label>(dialog).Any(l => l.Text.StartsWith("Moved 1 photo to Deleted\\Unreadable"))), "it moves the ticked one");
        checks.That(File.Exists(P(@"Deleted\Unreadable\Broken\zero.jpg")) && !File.Exists(P(@"Broken\zero.jpg")), "to Deleted\\Unreadable, with the path it had");
        checks.That(File.Exists(P(@"Broken\fine.jpg")) && File.Exists(P(@"Broken2\also zero.png")) && File.Exists(P(@"Broken\notes.txt")), "and nothing else: the real photo, the one left out and the empty note stay");
        checks.That(photos.Items.Count == 1 && radios.Any(r => r.Text == "Unreadable photos (1)"), "the list holds the one that is left");

        // Moving the last photo of a folder leaves the folder empty: looking again finds it
        photos.Items[0].Checked = true;
        checks.That(photos.Items[0].Text == @"Broken2\also zero.png" && move().Text == "Move 1 photo to Deleted\\Unreadable", $"the one that was left out is ticked now ({move().Text})");
        move().PerformClick();
        checks.That(await Until(() => photos.Items.Count == 0 && File.Exists(P(@"Deleted\Unreadable\Broken2\also zero.png"))), "the other one moves too");
        Buttons(dialog).First(b => b.Text == "Look again").PerformClick();
        checks.That(await Until(() => radios.Any(r => r.Text == "Empty folders (1)") && radios.Any(r => r.Text == "Unreadable photos (0)"), 60000), "'Look again' finds the folder that is empty now");
        radios.First(r => r.Text.StartsWith("Empty folders")).Checked = true;
        await Until(() => folders.Visible && folders.Items.Count == 1);
        checks.That(folders.Items[0].Text == "Broken2" && folders.Items[0].Checked, "it is 'Broken2', ticked");
        Buttons(dialog).First(b => b.Text.StartsWith("Remove ")).PerformClick();
        checks.That(await Until(() => !Directory.Exists(P("Broken2"))), "and removed");

        Buttons(dialog).First(b => b.Text == "Close").PerformClick();
        checks.That(await Until(() => FindDialog("EmptyFoldersDialog") is null), "the dialog closes");
        checks.That(await Until(() => form.Config.Files.All(f => !f.OriginalFilePath.Contains("zero")) && form.Config.Files.Any(f => f.OriginalFilePath.EndsWith("fine.jpg")) && PhotoShown()), "and the main window lists the folder again, without the photos that moved");
    }

    private static Form? FindDialog(string type) => Application.OpenForms.OfType<Form>().FirstOrDefault(f => f.GetType().Name == type);

    private static bool DateAlign(DateTime a, DateTime b) => Math.Abs((a - b).TotalSeconds) <= 2;

    private static List<T> Controls<T>(Control root) where T : Control
    {
        var found = new List<T>();
        void Walk(Control control)
        {
            foreach (Control child in control.Controls)
            {
                if (child is T match)
                    found.Add(match);
                Walk(child);
            }
        }
        Walk(root);
        return found;
    }

    private static List<Button> Buttons(Control root) => Controls<Button>(root);

    // ── 15. Rotate ──────────────────────────────────────────────────────────────────────────────

    private async Task Rotate()
    {
        checks.Step("15. Rotate: the button in the right corner of the status bar turns the photo a quarter clockwise and saves it in its file");

        var button = (ToolStripButton)form.StatusBarItem("rotateToolStripButton");
        var strip = form.Find<StatusStrip>("statusStrip1");
        checks.That(button.Image is not null && button.Enabled && button.ToolTipText!.Contains("clockwise"), "the status bar has a Rotate button with an icon and a tooltip");
        checks.That(strip.Items.Cast<ToolStripItem>().Last() == button && button.Bounds.Right > strip.ClientSize.Width - 30,
            $"it is the last item, in the right corner (its right edge is at {button.Bounds.Right} of {strip.ClientSize.Width})");

        bool ShownAs(int width, int height) => PhotoShown() && form.Viewer.Image!.Width == width && form.Viewer.Image.Height == height;

        // A photo that has the orientation already (the demo photos say 1): two bytes change, and nothing else
        await GoTo(0);
        var path = form.Config.CurrentFilePath;
        var original = File.ReadAllBytes(path);
        var info = new FileInfo(path);
        var (written, created) = (info.LastWriteTimeUtc, info.CreationTimeUtc);
        var (width, height) = (form.Viewer.Image!.Width, form.Viewer.Image.Height);
        checks.That(width != height && JpegOrientation.Read(new MemoryStream(original)) == 1, $"the first photo is {width} x {height} and has the orientation 1");

        button.PerformClick();
        checks.That(await Until(() => ShownAs(height, width) && form.Status.Contains("rotated clockwise and saved")), $"the click shows the photo turned, {form.Viewer.Image?.Width} x {form.Viewer.Image?.Height} ({form.Status})");
        var turned = File.ReadAllBytes(path);
        checks.That(JpegOrientation.Read(new MemoryStream(turned)) == 6, "the file says 6 now (a quarter clockwise)");
        checks.That(turned.Length == original.Length && Enumerable.Range(0, turned.Length).Count(i => turned[i] != original[i]) <= 2, "it has the size it had, and not more than two bytes are other");
        checks.That(File.GetLastWriteTimeUtc(path) == written && File.GetCreationTimeUtc(path) == created, "the dates of the file are the ones it had");
        checks.That(form.Config.Files[0].Length == original.Length, "the list still knows it by its size");

        for (var i = 0; i < 3; i++)
        {
            var wasWidth = form.Viewer.Image!.Width;
            button.PerformClick();
            await Until(() => PhotoShown() && form.Viewer.Image!.Width != wasWidth);
        }
        checks.That(await Until(() => ShownAs(width, height)) && File.ReadAllBytes(path).SequenceEqual(original), "four clicks give the photo and the file back as they were");

        // A photo that has none: the flag is added, the file is a little larger, and its copy in the Liked folder follows it
        var date = new DateTime(2025, 7, 11, 9, 0, 0, DateTimeKind.Utc);
        var scan = Path.Combine(folder, "rot-scan.jpg");
        File.WriteAllBytes(scan, MakeJpeg(77));
        File.SetCreationTimeUtc(scan, date);
        File.SetLastWriteTimeUtc(scan, date);
        var png = Path.Combine(folder, "rot-note.png");
        using (var note = new Bitmap(40, 30))
            note.Save(png, ImageFormat.Png);

        var extensions = form.Config.Extensions;
        if (!extensions.Contains(".png"))
            await ChangeSetting("Extensions", new[] { ".jpg", ".png" });
        form.Press(Keys.F5);
        checks.That(await Until(() => form.Config.Files.Any(f => f.OriginalFilePath == scan) && form.Config.Files.Any(f => f.OriginalFilePath == png) && PhotoShown()), "a JPEG without any EXIF, and a PNG, are added to the folder and listed");

        await GoTo(form.Config.Files.FindIndex(f => f.OriginalFilePath == scan));
        var scanIndex = form.Config.CurrentIndex;
        var oldCopy = Path.Combine(_likedFolder, CopyNameAt(scanIndex));
        form.Press(Keys.Space);
        checks.That(await Until(() => File.Exists(oldCopy) && form.Config.LikedFile == oldCopy), "Space likes it: the copy is named with the id of the photo");

        var scanBytes = File.ReadAllBytes(scan);
        (width, height) = (form.Viewer.Image!.Width, form.Viewer.Image.Height);
        button.PerformClick();
        checks.That(await Until(() => ShownAs(height, width) && File.ReadAllBytes(scan).Length > scanBytes.Length), $"the click turns it ({form.Status})");
        checks.That(form.Status.Contains("no orientation flag"), "and the status says the flag was added");
        var scanTurned = File.ReadAllBytes(scan);
        checks.That(JpegOrientation.Read(new MemoryStream(scanTurned)) == 6 && scanTurned.Length == scanBytes.Length + 36, $"the file has the flag, and 36 bytes more ({scanTurned.Length - scanBytes.Length})");
        checks.That(File.GetLastWriteTimeUtc(scan) == date && File.GetCreationTimeUtc(scan) == date, "its dates are the ones it had");
        checks.That(form.Config.Files[scanIndex].Length == scanTurned.Length, "the list knows its new size");

        // The id of the copy is made from the size, which is other now: the copy has the new name, and is still the copy of the photo
        var newCopy = Path.Combine(_likedFolder, CopyNameAt(scanIndex));
        checks.That(newCopy != oldCopy && File.Exists(newCopy) && !File.Exists(oldCopy), $"the copy in the Liked folder was renamed ({Path.GetFileName(newCopy)})");
        checks.That(form.Config.LikedFile == newCopy && form.Config.SavedInFolders.SequenceEqual(["Liked"]), "the photo is still shown as liked");
        form.Press(Keys.Space);
        checks.That(await Until(() => !File.Exists(newCopy) && form.Config.LikedFile == string.Empty), "and Space takes that copy away again");

        for (var i = 0; i < 3; i++)
        {
            var wasWidth = form.Viewer.Image!.Width;
            button.PerformClick();
            await Until(() => PhotoShown() && form.Viewer.Image!.Width != wasWidth);
        }
        checks.That(await Until(() => ShownAs(width, height)) && JpegOrientation.Read(new MemoryStream(File.ReadAllBytes(scan))) == 1, "three more clicks bring it back upright; they are two bytes each, as the file has the flag now");

        // Not a JPEG: nothing is written
        await GoTo(form.Config.Files.FindIndex(f => f.OriginalFilePath == png));
        var pngBytes = File.ReadAllBytes(png);
        button.PerformClick();
        checks.That(await Until(() => form.Status.Contains("is not a JPEG")) && File.ReadAllBytes(png).SequenceEqual(pngBytes), $"a PNG is left alone ({form.Status})");

        // Nothing of this step stays
        File.Delete(scan);
        File.Delete(png);
        if (!extensions.Contains(".png"))
            await ChangeSetting("Extensions", extensions);
        form.Press(Keys.F5);
        checks.That(await Until(() => form.Config.Files.All(f => f.OriginalFilePath != scan && f.OriginalFilePath != png) && PhotoShown()), "the two files are removed again");
    }

    // ── what the tour leaves behind, checked after the window is closed ─────────────────────────

    public void VerifySavedSettings()
    {
        checks.Step("14. The settings file written when the window closed");

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

    /// <summary>The name of the copy of a photo in the Liked folder or a group folder: its name with an id.</summary>
    private string CopyNameAt(int index) => CopyName.For(form.Config.Files[index]);

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
