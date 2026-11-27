using System.ComponentModel;
using System.Drawing.Imaging;
using System.Reflection;
using Microsoft.Win32;
using PhotoLiker.Core;
using PhotoLiker.VectorFiles;
using PhotoLiker.VectorFiles.Tests;

namespace PhotoLikerUI.Tests;

/// <summary>
/// 3D models in the real window: an STL file is listed with the photos, shown as a model that the mouse turns, described in the grid under it, refused by the tools that work on the pixels of a
/// photo, and opened from the command line; and the button that puts Photo Liker in Windows' list of programs for .stl files. Windows' own list is never touched: the registration is written to a place
/// of the test's own, and Settings is not opened.
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class ModelViewerTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly string _registryRoot = @"Software\PhotoLikerViewerTests_" + Guid.NewGuid().ToString("N");

    public ModelViewerTests() => new GlobalConfig { ColorizeBar = false }.Save();

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_registryRoot, throwOnMissingSubKey: false);
        _folder.Dispose();
    }

    private sealed class TestMainForm(string folder, string? file, string registryRoot) : MainForm(folder, file)
    {
        public readonly List<string> Questions = [];
        public DialogResult Answer = DialogResult.Cancel;
        public int SettingsOpened;

        protected override StlAssociation CreateStlAssociation() => new(@"C:\Programs\Photo Liker\PhotoLikerUI.exe", registryRoot);

        protected override string? OpenDefaultAppsSettings()
        {
            SettingsOpened++;
            return null;
        }

        protected override DialogResult AskStlChoice(string question)
        {
            Questions.Add(question);
            return Answer;
        }
    }

    private TestMainForm Open(string? file = null) => new(_folder.Path, file, _registryRoot);

    /// <summary>The window is done with loading the folder, and shows the model of the test.</summary>
    private Task<bool> Ready(Form form) => ModelShown(form, _folder.File("bracket.stl"));

    // ── the files ──────────────────────────────────────────────────────

    private string MakeStl(string name, DateTime dateUtc, float[]? triangles = null) =>
        _folder.Write(name, TestMeshes.BinaryFile(triangles ?? TestMeshes.Box(0, 0, 0, 40, 20, 10), "part"), dateUtc);

    private string MakePhoto(string name, DateTime dateUtc)
    {
        using var bitmap = new Bitmap(320, 200);
        using (var g = Graphics.FromImage(bitmap))
            g.Clear(Color.SteelBlue);
        var path = _folder.File(name);
        bitmap.Save(path, ImageFormat.Jpeg);
        File.SetCreationTimeUtc(path, dateUtc);
        File.SetLastWriteTimeUtc(path, dateUtc);
        return path;
    }

    private static readonly DateTime Old = new(2015, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Newer = new(2016, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Newest = new(2017, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    // ── looking into the window ────────────────────────────────────────

    private static T Field<T>(object owner, string name) =>
        (T)typeof(MainForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;

    private static PictureBox Viewer(Form form) => Field<PictureBox>(form, "pictureBox1");

    private static ModelViewController Model(Form form) => Field<ModelViewController>(form, "_model");

    private static string Status(Form form) => Field<ToolStripStatusLabel>(form, "toolStripStatusLabel1").Text ?? string.Empty;

    private static Settings Config(Form form) => Field<Settings>(form, "_currentConfig");

    private static ToolStripButton Button(Form form, string name) =>
        Ui.Controls<ToolStrip>(form).SelectMany(strip => strip.Items.Cast<ToolStripItem>()).OfType<ToolStripButton>().Single(button => button.Name == name);

    private static void Show(Form form, string path) =>
        typeof(MainForm).GetMethod("LoadImage", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, [path]);

    /// <summary>The model is shown, and drawn at the size of the viewer.</summary>
    private static async Task<bool> ModelShown(Form form, string path)
    {
        if (!await Ui.Until(() => Viewer(form).Tag as string == path && Model(form).Active && Viewer(form).Image is not null, 30000))
            return false;

        await Model(form).SettledAsync();
        return Viewer(form).Image is { } image && image.Size == Viewer(form).ClientSize;
    }

    private static void Mouse(Control control, string handler, MouseButtons button, int x, int y, int delta = 0) =>
        typeof(Control).GetMethod(handler, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(control, [new MouseEventArgs(button, 1, x, y, delta)]);

    private static string Hash(Image image)
    {
        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Bmp);
        return Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(stream.ToArray()));
    }

    // ── a model among the photos ───────────────────────────────────────

    [Fact]
    public void AModel_IsListedWithThePhotos_AndShownAsAModelThatIsDrawnAtTheSizeOfTheViewer()
    {
        var stl = MakeStl("bracket.stl", Old);
        MakePhoto("holiday.jpg", Newer);

        Ui.Run(() => Open(), async form =>
        {
            Assert.True(await Ui.Until(() => Config(form).Files.Count == 2, 20000));
            Assert.Equal(1, Config(form).PhotoCount);
            Assert.Equal(1, Config(form).ModelCount);
            Assert.Equal(0, Config(form).VideoCount);

            Assert.True(await ModelShown(form, stl));
            Assert.Equal(string.Format(ModelStrings.StatusShownFormat, "bracket.stl", "12"), Status(form));

            // The middle of the viewer is the model, the corner is the stage
            var picture = (Bitmap)Viewer(form).Image!;
            Assert.NotEqual(ModelViewControllerTests.Opaque(ImageHelper.ModelStyle.Background), picture.GetPixel(picture.Width / 2, picture.Height / 2).ToArgb());
            Assert.Equal(ModelViewControllerTests.Opaque(ImageHelper.ModelStyle.Background), picture.GetPixel(3, picture.Height - 3).ToArgb());
        }, seconds: 90);
    }

    [Fact]
    public void TheGridUnderAModel_SaysHowManyTrianglesItHasAndHowBigItIs()
    {
        var stl = MakeStl("bracket.stl", Old);

        Ui.Run(() => Open(), async form =>
        {
            Assert.True(await ModelShown(form, stl));

            var grid = Field<PropertyGrid>(form, "imageMetaPropertyGrid");
            var wrapper = Assert.IsType<MetadataWrapper>(grid.SelectedObject);
            var rows = wrapper.GetProperties().Cast<PropertyDescriptor>().ToDictionary(row => row.Name, row => (string?)row.GetValue(null));
            Assert.Equal("12", rows[ModelMetadata.TrianglesName]);
            Assert.Equal("40 × 20 × 10 mm", rows[ModelMetadata.SizeName]);
            Assert.Equal("part", rows[ModelMetadata.ModelNameName]);
            Assert.Contains(ModelMetadata.FileSizeName, rows.Keys);
        }, seconds: 90);
    }

    [Fact]
    public void DraggingTheModel_TurnsIt_AndTheModelIsDrawnAgain()
    {
        var stl = MakeStl("bracket.stl", Old);

        Ui.Run(() => Open(), async form =>
        {
            Assert.True(await ModelShown(form, stl));
            var viewer = Viewer(form);
            var before = Hash(viewer.Image!);
            var start = Model(form).View;

            Mouse(viewer, "OnMouseDown", MouseButtons.Left, 100, 100);
            Assert.Equal(Cursors.SizeAll, viewer.Cursor);
            Mouse(viewer, "OnMouseMove", MouseButtons.Left, 160, 120);
            Mouse(viewer, "OnMouseUp", MouseButtons.Left, 160, 120);
            Assert.Equal(Cursors.Default, viewer.Cursor);
            await Model(form).SettledAsync();

            Assert.Equal(start.Yaw - 30, Model(form).View.Yaw, 9);
            Assert.Equal(start.Pitch + 10, Model(form).View.Pitch, 9);
            Assert.NotEqual(before, Hash(viewer.Image!));
            Assert.Equal(viewer.ClientSize, viewer.Image!.Size);

            // A double click starts over
            Mouse(viewer, "OnMouseDoubleClick", MouseButtons.Left, 20, viewer.Height - 20);
            Assert.Equal(MeshView.Isometric, Model(form).View);
        }, seconds: 90);
    }

    [Fact]
    public void TheWheel_ZoomsTheModel_WithoutCtrl_AndDoesNotEnlargeTheViewerOrScrollIt()
    {
        var stl = MakeStl("bracket.stl", Old);

        Ui.Run(() => Open(), async form =>
        {
            Assert.True(await ModelShown(form, stl));
            var viewer = Viewer(form);
            var dock = viewer.Dock;

            Mouse(viewer, "OnMouseWheel", MouseButtons.None, 50, 50, delta: 120);
            await Model(form).SettledAsync();

            Assert.Equal(1.15, Model(form).View.Zoom, 9);
            Assert.Equal(dock, viewer.Dock);        // the picture box itself stays as big as the viewer: it is the model that was zoomed
            Assert.Equal(viewer.ClientSize, viewer.Image!.Size);
        }, seconds: 90);
    }

    [Fact]
    public void TheButtonsOverTheModel_LookFromAnotherSide()
    {
        var stl = MakeStl("bracket.stl", Old);

        Ui.Run(() => Open(), async form =>
        {
            Assert.True(await ModelShown(form, stl));
            var viewer = Viewer(form);
            var top = Model(form).ButtonBounds().Single(button => button.Text == ModelStrings.ViewTop).Bounds;

            Mouse(viewer, "OnMouseDown", MouseButtons.Left, top.Left + top.Width / 2, top.Top + top.Height / 2);
            Mouse(viewer, "OnMouseUp", MouseButtons.Left, top.Left + top.Width / 2, top.Top + top.Height / 2);
            await Model(form).SettledAsync();

            Assert.Equal(MeshView.Top, Model(form).View);
            Assert.Equal(Cursors.Default, viewer.Cursor);
        }, seconds: 90);
    }

    [Fact]
    public void GoingToAPhoto_LetsGoOfTheModel_AndComingBackShowsItAgain()
    {
        var stl = MakeStl("bracket.stl", Old);
        var photo = MakePhoto("holiday.jpg", Newer);

        Ui.Run(() => Open(), async form =>
        {
            Assert.True(await ModelShown(form, stl));

            Show(form, photo);
            Assert.True(await Ui.Until(() => Viewer(form).Tag as string == photo && Viewer(form).Image is { Width: 320 }, 20000));
            Assert.False(Model(form).Active);

            Show(form, stl);
            Assert.True(await ModelShown(form, stl));
            Assert.Equal(MeshView.Isometric, Model(form).View); // a model is looked at from the start each time
        }, seconds: 90);
    }

    [Fact]
    public void GoingFromOneModelToAnother_ShowsTheOther()
    {
        var first = MakeStl("a.stl", Old);
        var second = MakeStl("b.stl", Newer, TestMeshes.Box(0, 0, 0, 5, 5, 50));

        Ui.Run(() => Open(), async form =>
        {
            Assert.True(await ModelShown(form, first));
            Show(form, second);

            Assert.True(await ModelShown(form, second));
            Assert.Equal(50, Model(form).Mesh!.Bounds.SizeZ);
        }, seconds: 90);
    }

    [Fact]
    public void AFileThatIsNoModel_SaysSo_InsteadOfShowingAnEmptyPicture()
    {
        File.WriteAllText(_folder.File("broken.stl"), "this is not an STL file at all, only a sentence of some length");

        Ui.Run(() => Open(), async form =>
        {
            Assert.True(await Ui.Until(() => Status(form).Contains("not an STL file", StringComparison.OrdinalIgnoreCase), 20000), Status(form));
            Assert.Equal(_folder.File("broken.stl"), Viewer(form).Tag);
            Assert.False(Model(form).Active);
        }, seconds: 90);
    }

    // ── the tools that work on photos ──────────────────────────────────

    [Theory]
    [InlineData("traceBitmapToolStripButton")]
    [InlineData("stickerToolStripButton")]
    [InlineData("drawToolStripButton")]
    public void TheToolsOfThePhotos_RefuseAModel(string button)
    {
        var stl = MakeStl("bracket.stl", Old);

        Ui.Run(() => Open(), async form =>
        {
            Assert.True(await ModelShown(form, stl));

            Button(form, button).PerformClick();
            await Task.Delay(300);

            Assert.Equal(ModelStrings.StatusNotAPhoto, Status(form));
            Assert.Null(Ui.Find("TraceBitmapDialog"));
            Assert.Null(Ui.Find("StickerDialog"));
            Assert.Null(Ui.Find("DrawingDialog"));
        }, seconds: 90);
    }

    [Fact]
    public void TheColorizeBar_IsNotBroughtToAModel()
    {
        new GlobalConfig { ColorizeBar = true }.Save(); // the bar that comes with every photo is on
        var stl = MakeStl("bracket.stl", Old);

        Ui.Run(() => Open(), async form =>
        {
            Assert.True(await ModelShown(form, stl));
            await Task.Delay(500); // the bar is made a moment after a photo is shown

            Assert.Null(Field<object?>(form, "_bar"));
        }, seconds: 90);
    }

    [Fact]
    public void SaveAs_OfAModel_IsACopyOfTheFile_AsItIsWithTheKindOfFileOfItsOwn()
    {
        var stl = MakeStl("bracket.stl", Old);

        var filter = MainForm.CopyFilter(stl);

        Assert.StartsWith("STL (*.stl)|*.stl|", filter);
        Assert.EndsWith("|*.*", filter);
    }

    // ── the setting ────────────────────────────────────────────────────

    [Fact]
    public void WithTheModelsTurnedOff_TheyAreNotListed()
    {
        MakeStl("bracket.stl", Old);
        MakePhoto("holiday.jpg", Newer);
        new Settings { CurrentFolder = _folder.Path, SupportModels = false }.Write();

        Ui.Run(() => Open(), async form =>
        {
            Assert.True(await Ui.Until(() => Config(form).Files.Count == 1, 20000));
            Assert.Equal(0, Config(form).ModelCount);
            Assert.False(Config(form).Files[0].IsModel);
        }, seconds: 90);
    }

    [Fact]
    public void TurningTheModelsOn_InTheGrid_ListsTheFolderAgain()
    {
        MakeStl("bracket.stl", Old);
        MakePhoto("holiday.jpg", Newer);
        new Settings { CurrentFolder = _folder.Path, SupportModels = false }.Write();

        Ui.Run(() => Open(), async form =>
        {
            Assert.True(await Ui.Until(() => Config(form).Files.Count == 1, 20000));

            Config(form).SupportModels = true;
            typeof(MainForm).GetMethod("SettingsPropertyGrid_PropertyValueChanged", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form,
                [null, new PropertyValueChangedEventArgs(FindItem(form, nameof(Settings.SupportModels)), false)]);

            Assert.True(await Ui.Until(() => Config(form).Files.Count == 2, 20000));
            Assert.Equal(1, Config(form).ModelCount);
        }, seconds: 90);
    }

    private static GridItem FindItem(Form form, string name)
    {
        var grid = Field<PropertyGrid>(form, "settingsPropertyGrid");
        var root = grid.SelectedGridItem!;
        while (root.Parent is not null)
            root = root.Parent;

        return Walk(root).First(item => item.PropertyDescriptor?.Name == name);

        static IEnumerable<GridItem> Walk(GridItem item)
        {
            yield return item;
            foreach (GridItem child in item.GridItems)
                foreach (var found in Walk(child))
                    yield return found;
        }
    }

    // ── opening a file ─────────────────────────────────────────────────

    [Fact]
    public void StartedWithAFile_ItShowsThatFile_NotTheFirstOfTheList()
    {
        var first = MakeStl("a.stl", Old);
        MakeStl("b.stl", Newer);
        var third = MakeStl("c.stl", Newest, TestMeshes.Box(0, 0, 0, 5, 5, 50));

        Ui.Run(() => Open(third), async form =>
        {
            Assert.True(await ModelShown(form, third));
            Assert.NotEqual(first, Viewer(form).Tag);
            Assert.Equal(50, Model(form).Mesh!.Bounds.SizeZ);
        }, seconds: 90);
    }

    [Fact]
    public void StartedWithAModel_InAFolderThatHasModelsOff_ShowsItAnyway()
    {
        MakePhoto("holiday.jpg", Old);
        var stl = MakeStl("bracket.stl", Newer);
        new Settings { CurrentFolder = _folder.Path, SupportModels = false }.Write();

        Ui.Run(() => Open(stl), async form =>
        {
            Assert.True(await ModelShown(form, stl));
            Assert.True(Config(form).SupportModels);
        }, seconds: 90);
    }

    [Fact]
    public void AFileNamedInOtherLetters_IsFoundAnyway()
    {
        MakeStl("a.stl", Old);
        var second = MakeStl("B.stl", Newer);

        // Windows may hand the program the path of a file with the drive letter in the other case
        var spelled = second.Length > 1 && char.IsLetter(second[0]) ? char.ToLowerInvariant(second[0]) + second[1..] : second.ToLowerInvariant();

        Ui.Run(() => Open(spelled), async form =>
        {
            Assert.True(await Ui.Until(() => Model(form).Active && Viewer(form).Tag as string == second, 30000), $"{Viewer(form).Tag}");
        }, seconds: 90);
    }

    // ── Windows' list of programs for .stl files ───────────────────────

    [Fact]
    public void TheStlButton_PutsTheProgramInWindowsList_AndOpensTheDefaultAppsOfSettings()
    {
        MakeStl("bracket.stl", Old);

        var stl = _folder.File("bracket.stl");
        Ui.Run(() => Open(), async form =>
        {
            Assert.True(await ModelShown(form, stl)); // the window has finished loading: nothing else writes to the status line
            var test = (TestMainForm)form;
            var button = Button(form, "stlToolStripButton");
            Assert.Equal(ModelStrings.ButtonText, button.Text);
            Assert.Equal(ModelStrings.TooltipOff, button.ToolTipText);
            Assert.False(button.Checked);

            button.PerformClick();

            Assert.Equal(1, test.SettingsOpened);
            Assert.Equal(ModelStrings.StatusRegisteredOpened, Status(form));
            Assert.Equal(ModelStrings.TooltipRegistered, button.ToolTipText);
            using var registered = Registry.CurrentUser.OpenSubKey($@"{_registryRoot}\Classes\PhotoLiker.StlFile\shell\open\command");
            Assert.NotNull(registered);
            Assert.Empty(test.Questions); // the first press asks nothing: it is what was asked for
            await Task.CompletedTask;
        }, seconds: 90);
    }

    [Fact]
    public void TheStlButton_WhenRegistered_AsksWhatToDo_AndYesOpensSettingsAgain()
    {
        MakeStl("bracket.stl", Old);

        Ui.Run(() => Open(), async form =>
        {
            Assert.True(await Ready(form)); // the window has finished loading: nothing else writes to the status line
            var test = (TestMainForm)form;
            var button = Button(form, "stlToolStripButton");
            button.PerformClick();
            test.Answer = DialogResult.Yes;

            button.PerformClick();

            Assert.Equal(ModelStrings.QuestionRegistered, Assert.Single(test.Questions));
            Assert.Equal(2, test.SettingsOpened);
            Assert.Equal(ModelStrings.StatusSettingsOpened, Status(form));
            Assert.Equal(ModelStrings.TooltipRegistered, button.ToolTipText);
            await Task.CompletedTask;
        }, seconds: 90);
    }

    [Fact]
    public void TheStlButton_WhenRegistered_AndNoIsAnswered_TakesTheProgramOutOfTheList()
    {
        MakeStl("bracket.stl", Old);

        Ui.Run(() => Open(), async form =>
        {
            Assert.True(await Ready(form)); // the window has finished loading: nothing else writes to the status line
            var test = (TestMainForm)form;
            var button = Button(form, "stlToolStripButton");
            button.PerformClick();
            test.Answer = DialogResult.No;

            button.PerformClick();

            Assert.Equal(ModelStrings.StatusUnregistered, Status(form));
            Assert.Equal(ModelStrings.TooltipOff, button.ToolTipText);
            Assert.Null(Registry.CurrentUser.OpenSubKey($@"{_registryRoot}\Classes\PhotoLiker.StlFile"));
            Assert.Equal(1, test.SettingsOpened); // the one of the first press
            await Task.CompletedTask;
        }, seconds: 90);
    }

    [Fact]
    public void TheStlButton_WhenRegistered_AndCancelIsAnswered_ChangesNothing()
    {
        MakeStl("bracket.stl", Old);

        Ui.Run(() => Open(), async form =>
        {
            Assert.True(await Ready(form)); // the window has finished loading: nothing else writes to the status line
            var test = (TestMainForm)form;
            var button = Button(form, "stlToolStripButton");
            button.PerformClick();
            test.Answer = DialogResult.Cancel;
            var status = Status(form);

            button.PerformClick();

            Assert.Equal(status, Status(form));
            Assert.Equal(1, test.SettingsOpened);
            Assert.NotNull(Registry.CurrentUser.OpenSubKey($@"{_registryRoot}\Classes\PhotoLiker.StlFile"));
            await Task.CompletedTask;
        }, seconds: 90);
    }

    [Fact]
    public void WhenWindowsHasChosenTheProgram_TheButtonSaysSo_AndTheQuestionIsAnother()
    {
        MakeStl("bracket.stl", Old);

        Ui.Run(() => Open(), async form =>
        {
            Assert.True(await Ready(form)); // the window has finished loading: nothing else writes to the status line
            var test = (TestMainForm)form;
            var button = Button(form, "stlToolStripButton");
            button.PerformClick();
            using (var choice = Registry.CurrentUser.CreateSubKey($@"{_registryRoot}\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.stl\UserChoice"))
                choice.SetValue("ProgId", StlAssociation.ProgId);
            typeof(MainForm).GetMethod("UpdateStlButton", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, null);
            Assert.True(button.Checked);
            Assert.Equal(ModelStrings.TooltipDefault, button.ToolTipText);

            test.Answer = DialogResult.Cancel;
            button.PerformClick();

            Assert.Equal(ModelStrings.QuestionDefault, Assert.Single(test.Questions));
            await Task.CompletedTask;
        }, seconds: 90);
    }

    [Fact]
    public void TheStlButton_SitsNextToTheContextMenuButton()
    {
        MakeStl("bracket.stl", Old);

        Ui.Run(() => Open(), async form =>
        {
            var strip = Ui.Controls<ToolStrip>(form).Single(s => s.Items.Cast<ToolStripItem>().Any(item => item.Name == "stlToolStripButton"));
            var items = strip.Items.Cast<ToolStripItem>().Select(item => item.Name).ToList();

            Assert.Equal(items.IndexOf("contextMenuToolStripButton") + 1, items.IndexOf("stlToolStripButton"));
            await Task.CompletedTask;
        }, seconds: 90);
    }
}
