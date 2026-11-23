using System.Drawing.Imaging;
using System.Reflection;
using System.Xml.Linq;
using PhotoLiker.Core;
using PhotoLiker.TraceBitmap;

namespace PhotoLikerUI.Tests;

/// <summary>The Trace Bitmap window opened for real: it reads a photo, traces it, keeps the size of the drawing in millimetres, and saves an SVG or a DXF file.</summary>
[Collection(WindowsCollection.Name)]
public sealed class TraceBitmapDialogTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public TraceBitmapDialogTests() => new GlobalConfig { ColorizeBar = false }.Save();

    public void Dispose() => _folder.Dispose();

    private string SettingsFile => _folder.File("trace-settings.json");

    /// <summary>A photo of a black disc and a black bar on white (so that a brightness cutoff finds two shapes).</summary>
    private string Photo(string name = "photo.jpg", int width = 240, int height = 160)
    {
        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            graphics.FillEllipse(Brushes.Black, width / 5, height / 5, width / 2, height * 3 / 5);
            graphics.FillRectangle(Brushes.Black, width * 3 / 4, height / 5, width / 8, height * 3 / 5);
        }

        var path = _folder.File(name);
        bitmap.Save(path, ImageFormat.Jpeg);
        return path;
    }

    private TraceBitmapDialog Create(string photo, bool dark = false) => new(photo, dark, SettingsFile);

    private static async Task Traced(TraceBitmapDialog dialog) =>
        Assert.True(await Ui.Until(() => !dialog.IsReading && !dialog.IsTracing && dialog.CurrentTrace is not null, 30000), $"the photo was traced ({dialog.StatusText})");

    private static int Count(string text, string part) => (text.Length - text.Replace(part, string.Empty).Length) / part.Length;

    // ── Opening ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheWindow_ReadsThePhoto_TracesItAtOnce_AndShowsBoth()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (TraceBitmapDialog)form;
            await Traced(dialog);

            Assert.Contains("photo.jpg", dialog.Text);
            Assert.Equal(new Size(240, 160), dialog.Preview.Photo!.Size);
            Assert.Equal(240, dialog.CurrentTrace!.Width);
            Assert.Same(dialog.CurrentTrace, dialog.Preview.Trace);
            Assert.True(dialog.ShowTraceRadio.Checked);
            Assert.True(dialog.Preview.ShowTrace);
            Assert.Equal(2, dialog.CurrentTrace.PathCount); // the disc and the bar
            Assert.StartsWith("Traced: 2 outlines", dialog.StatusText);
            Assert.False(dialog.IsStale);
            Assert.Equal(Path.GetFileName(photo), Path.GetFileName(dialog.Text.Split(" - ")[^1]));
        });
    }

    [Fact]
    public void TheDrawing_StartsAtAHundredMillimetresLong_InTheProportionsOfThePhoto()
    {
        Ui.Run(() => Create(Photo(width: 240, height: 160)), async form =>
        {
            var dialog = (TraceBitmapDialog)form;
            await Traced(dialog);

            Assert.Equal((100.00, 66.67), dialog.DrawingSize);
        });
    }

    [Fact]
    public void ATallPhoto_HasItsHeightAsTheLongSide()
    {
        Ui.Run(() => Create(Photo(width: 160, height: 240)), async form =>
        {
            var dialog = (TraceBitmapDialog)form;
            await Traced(dialog);

            Assert.Equal((66.67, 100.00), dialog.DrawingSize);
        });
    }

    [Fact]
    public void APhotoThatIsBiggerThanTheDetail_IsTracedSmaller_AndTheDetailCanBeChanged()
    {
        var photo = Photo("big.jpg", 1000, 700);
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (TraceBitmapDialog)form;
            await Traced(dialog);
            Assert.Equal(1000, dialog.CurrentTrace!.Width); // under the default of 1500, so as it is

            dialog.DetailCombo.SelectedItem = 600;
            Assert.True(await Ui.Until(() => !dialog.IsReading && dialog.Preview.Photo!.Width == 600, 30000), "the photo was read again, smaller");
            Assert.Equal(TraceStrings.StatusStale, dialog.StatusText);
            Assert.True(dialog.IsStale);

            await dialog.TraceNowAsync();
            Assert.Equal(600, dialog.CurrentTrace!.Width);
            Assert.Equal(420, dialog.CurrentTrace.Height);
            Assert.False(dialog.IsStale);
            Assert.Equal((100.00, 70.00), dialog.DrawingSize); // the proportions did not change
        });
    }

    [Fact]
    public void APhotoThatCannotBeRead_SaysSo_AndHasNothingToSave()
    {
        var path = _folder.File("broken.jpg");
        File.WriteAllText(path, "this is not a picture");
        Ui.Run(() => Create(path), async form =>
        {
            var dialog = (TraceBitmapDialog)form;
            Assert.True(await Ui.Until(() => dialog.PhotoFailed, 30000), "the failure was noticed");

            Assert.StartsWith("The photo could not be read", dialog.StatusText);
            Assert.Null(dialog.CurrentTrace);
            dialog.AskSavePath = (_, _, _, _, _) => throw new InvalidOperationException("nothing to save, so nothing may be asked");
            Assert.False(await dialog.SaveAsync());
        });
    }

    // ── The scans ───────────────────────────────────────────────────────────

    [Fact]
    public void WhatIsPicked_DecidesWhatIsAvailable_AsInInkscape()
    {
        Ui.Run(() => Create(Photo()), async form =>
        {
            var dialog = (TraceBitmapDialog)form;
            await Traced(dialog);

            // A single scan: its number is on, the multiple-scan options are off
            Assert.True(dialog.CutoffRadio.Checked);
            Assert.True(dialog.CutoffNumeric.Enabled);
            Assert.False(dialog.EdgesNumeric.Enabled);
            Assert.False(dialog.StepsNumeric.Enabled);
            Assert.True(dialog.InvertCheck.Enabled);
            Assert.False(dialog.SmoothCheck.Enabled);
            Assert.False(dialog.StackCheck.Enabled);
            Assert.False(dialog.BackgroundCheck.Enabled);

            dialog.ColorsRadio.Checked = true;
            Assert.False(dialog.CutoffRadio.Checked); // one mode at a time
            Assert.False(dialog.CutoffNumeric.Enabled);
            Assert.True(dialog.ColorsNumeric.Enabled);
            Assert.False(dialog.InvertCheck.Enabled);
            Assert.True(dialog.SmoothCheck.Enabled);
            Assert.True(dialog.StackCheck.Enabled);
            Assert.True(dialog.BackgroundCheck.Enabled);

            dialog.EdgesRadio.Checked = true;
            Assert.True(dialog.EdgesNumeric.Enabled);
            Assert.False(dialog.ColorsNumeric.Enabled);
        });
    }

    [Fact]
    public void TheOptions_ArePassedToTheTrace_AsTheyAreInTheWindow()
    {
        Ui.Run(() => Create(Photo()), async form =>
        {
            var dialog = (TraceBitmapDialog)form;
            await Traced(dialog);

            dialog.GraysRadio.Checked = true;
            dialog.GraysNumeric.Value = 5;
            dialog.StackCheck.Checked = false;
            dialog.BackgroundCheck.Checked = false;
            dialog.SmoothCheck.Checked = false;
            dialog.SpecklesNumeric.Value = 9;
            dialog.CornersNumeric.Value = 0.5m;
            dialog.OptimizeNumeric.Value = 1.25m;
            dialog.CornersCheck.Checked = false;

            var settings = dialog.ReadTrace();

            Assert.Equal(TraceMode.Grays, settings.Mode);
            Assert.Equal(5, settings.Grays);
            Assert.False(settings.StackScans);
            Assert.False(settings.RemoveBackground);
            Assert.False(settings.Smooth);
            Assert.Equal(9, settings.SpeckleSize);
            Assert.True(settings.SuppressSpeckles);
            Assert.Equal(0.5, settings.CornerThreshold);
            Assert.False(settings.SmoothCorners);
            Assert.Equal(1.25, settings.OptimizeTolerance);
            Assert.True(settings.OptimizePaths);

            await dialog.TraceNowAsync();
            Assert.True(dialog.CurrentTrace!.Layers.Count >= 2);
            Assert.All(dialog.CurrentTrace.Layers, layer => Assert.Equal(layer.Color.R, layer.Color.B)); // grays
        });
    }

    [Fact]
    public void ChangingASetting_MakesTheTraceStale_AndUpdateTracesAgain()
    {
        Ui.Run(() => Create(Photo()), async form =>
        {
            var dialog = (TraceBitmapDialog)form;
            await Traced(dialog);
            var first = dialog.CurrentTrace;
            var traced = dialog.StatusText;

            dialog.CutoffNumeric.Value = 0.9m;
            Assert.True(dialog.IsStale);
            Assert.Equal(TraceStrings.StatusStale, dialog.StatusText);
            Assert.Same(first, dialog.CurrentTrace); // nothing is traced until it is asked for

            dialog.CutoffNumeric.Value = 0.45m; // back to what it was: the trace on the screen is right again
            Assert.False(dialog.IsStale);
            Assert.Equal(traced, dialog.StatusText);

            dialog.EdgesRadio.Checked = true;
            Assert.True(dialog.IsStale);
            dialog.UpdateButton.PerformClick();
            Assert.True(await Ui.Until(() => !dialog.IsTracing && !ReferenceEquals(dialog.CurrentTrace, first), 30000), "traced again");
            Assert.False(dialog.IsStale);
            Assert.Equal(TraceColor.Black, dialog.CurrentTrace!.Layers[0].Color);
        });
    }

    [Fact]
    public void LivePreview_TracesAgain_ByItself_AfterAChange()
    {
        Ui.Run(() => Create(Photo()), async form =>
        {
            var dialog = (TraceBitmapDialog)form;
            await Traced(dialog);
            var first = dialog.CurrentTrace;

            dialog.LiveCheck.Checked = true;
            dialog.StepsRadio.Checked = true;

            Assert.True(await Ui.Until(() => !ReferenceEquals(dialog.CurrentTrace, first) && !dialog.IsTracing && !dialog.IsStale, 30000), "traced again without a press");
            Assert.True(dialog.CurrentTrace!.Layers.Count >= 2);
        });
    }

    [Fact]
    public void WithoutLivePreview_NothingIsTracedByItself()
    {
        Ui.Run(() => Create(Photo()), async form =>
        {
            var dialog = (TraceBitmapDialog)form;
            await Traced(dialog);
            var first = dialog.CurrentTrace;

            dialog.StepsRadio.Checked = true;
            await Task.Delay(1500); // longer than the pause live preview waits

            Assert.Same(first, dialog.CurrentTrace);
            Assert.True(dialog.IsStale);
        });
    }

    [Fact]
    public void ATraceThatIsAskedForTwice_EndsWithTheSecond()
    {
        Ui.Run(() => Create(Photo()), async form =>
        {
            var dialog = (TraceBitmapDialog)form;
            await Traced(dialog);

            dialog.CutoffNumeric.Value = 0.2m;
            var first = dialog.TraceNowAsync();
            dialog.CutoffNumeric.Value = 0.8m;
            var second = dialog.TraceNowAsync();
            await Task.WhenAll(first, second);

            Assert.False(dialog.IsTracing);
            Assert.False(dialog.IsStale); // what is shown is the trace of the settings that are in the window (0.8), not of the first
            Assert.Same(dialog.CurrentTrace, dialog.Preview.Trace);
        });
    }

    [Fact]
    public void TheButtonsOfThePreview_SwitchBetweenThePhotoAndTheTrace()
    {
        Ui.Run(() => Create(Photo()), async form =>
        {
            var dialog = (TraceBitmapDialog)form;
            await Traced(dialog);

            dialog.ShowPhotoRadio.Checked = true;
            Assert.False(dialog.Preview.ShowTrace);
            Assert.False(dialog.ShowTraceRadio.Checked);

            dialog.ShowTraceRadio.Checked = true;
            Assert.True(dialog.Preview.ShowTrace);
        });
    }

    // ── The sliders beside the numbers ──────────────────────────────────────

    [Fact]
    public void ASlider_SetsItsNumber_AndMakesTheTraceStale()
    {
        Ui.Run(() => Create(Photo()), async form =>
        {
            var dialog = (TraceBitmapDialog)form;
            await Traced(dialog);
            Assert.Equal(45, dialog.CutoffSlider.Value); // 0.450 on a slider of 0 to 100

            dialog.CutoffSlider.Value = 20;

            Assert.Equal(0.20m, dialog.CutoffNumeric.Value);
            Assert.Equal(0.2, dialog.ReadTrace().BrightnessCutoff);
            Assert.True(dialog.IsStale);
            Assert.Equal(TraceStrings.StatusStale, dialog.StatusText);
        });
    }

    [Fact]
    public void ANumberThatIsTypedOrStepped_MovesItsSlider()
    {
        Ui.Run(() => Create(Photo()), async form =>
        {
            var dialog = (TraceBitmapDialog)form;
            await Traced(dialog);

            dialog.CutoffNumeric.Value = 0.7m;
            Assert.Equal(70, dialog.CutoffSlider.Value);

            dialog.CutoffNumeric.UpButton();
            Assert.Equal(0.71m, dialog.CutoffNumeric.Value);
            Assert.Equal(71, dialog.CutoffSlider.Value);

            dialog.CutoffNumeric.Value = 0;
            Assert.Equal(0, dialog.CutoffSlider.Value);
            dialog.CutoffNumeric.Value = 1;
            Assert.Equal(100, dialog.CutoffSlider.Value);
        });
    }

    [Fact]
    public void TheSlidersOfWholeNumbers_RunOverTheRangeOfTheirNumbers()
    {
        Ui.Run(() => Create(Photo()), async form =>
        {
            var dialog = (TraceBitmapDialog)form;
            await Traced(dialog);

            Assert.Equal((TraceSettings.MinScans, TraceSettings.MaxScans), (dialog.ColorsSlider.Minimum, dialog.ColorsSlider.Maximum));
            Assert.Equal(8, dialog.ColorsSlider.Value);

            dialog.ColorsSlider.Value = 12;
            Assert.Equal(12m, dialog.ColorsNumeric.Value);
            Assert.Equal(12, dialog.ReadTrace().Colors);

            dialog.StepsSlider.Value = TraceSettings.MaxScans;
            dialog.GraysSlider.Value = TraceSettings.MinScans;
            dialog.QuantizeSlider.Value = 5;
            Assert.Equal((decimal)TraceSettings.MaxScans, dialog.StepsNumeric.Value);
            Assert.Equal((decimal)TraceSettings.MinScans, dialog.GraysNumeric.Value);
            Assert.Equal(5m, dialog.QuantizeNumeric.Value);
        });
    }

    [Fact]
    public void TheCornerAndTheToleranceSliders_HaveHundredthsOfAStep()
    {
        Ui.Run(() => Create(Photo()), async form =>
        {
            var dialog = (TraceBitmapDialog)form;
            await Traced(dialog);

            Assert.Equal(100, dialog.CornersSlider.Value); // 1.00
            Assert.Equal(20, dialog.OptimizeSlider.Value); // 0.20
            Assert.Equal(134, dialog.CornersSlider.Maximum); // 1.34, the end of the range of the number
            Assert.Equal(500, dialog.OptimizeSlider.Maximum); // 5.00

            dialog.CornersSlider.Value = 134;
            Assert.Equal(1.34m, dialog.CornersNumeric.Value);
            dialog.OptimizeSlider.Value = 125;
            Assert.Equal(1.25m, dialog.OptimizeNumeric.Value);
            Assert.Equal(1.25, dialog.ReadTrace().OptimizeTolerance);
        });
    }

    [Fact]
    public void ANumberBeyondTheEndOfItsSlider_KeepsItsValue_WhileTheSliderStaysAtItsEnd()
    {
        Ui.Run(() => Create(Photo()), async form =>
        {
            var dialog = (TraceBitmapDialog)form;
            await Traced(dialog);

            dialog.SpecklesNumeric.Value = 500; // the speckle slider ends at 100, the number goes on to 1000

            Assert.Equal(100, dialog.SpecklesSlider.Value);
            Assert.Equal(500m, dialog.SpecklesNumeric.Value);
            Assert.Equal(500, dialog.ReadTrace().SpeckleSize);

            dialog.SpecklesSlider.Value = 30;
            Assert.Equal(30m, dialog.SpecklesNumeric.Value);
        });
    }

    [Fact]
    public void ASlider_IsAvailableWhenItsNumberIs()
    {
        Ui.Run(() => Create(Photo()), async form =>
        {
            var dialog = (TraceBitmapDialog)form;
            await Traced(dialog);
            Assert.True(dialog.CutoffSlider.Enabled);
            Assert.False(dialog.ColorsSlider.Enabled);
            Assert.True(dialog.SpecklesSlider.Enabled);

            dialog.ColorsRadio.Checked = true;
            Assert.True(dialog.ColorsSlider.Enabled);
            Assert.False(dialog.CutoffSlider.Enabled);

            dialog.SpecklesCheck.Checked = false;
            Assert.False(dialog.SpecklesSlider.Enabled);
            dialog.CornersCheck.Checked = false;
            dialog.OptimizeCheck.Checked = false;
            Assert.False(dialog.CornersSlider.Enabled);
            Assert.False(dialog.OptimizeSlider.Enabled);
        });
    }

    [Fact]
    public void LivePreview_TracesAgain_AfterASliderWasMoved()
    {
        Ui.Run(() => Create(Photo()), async form =>
        {
            var dialog = (TraceBitmapDialog)form;
            await Traced(dialog);
            var first = dialog.CurrentTrace;
            dialog.LiveCheck.Checked = true;

            dialog.CutoffSlider.Value = 10;
            dialog.CutoffSlider.Value = 15;
            dialog.CutoffSlider.Value = 25; // a drag is many small moves: one trace follows, not three

            Assert.True(await Ui.Until(() => !ReferenceEquals(dialog.CurrentTrace, first) && !dialog.IsTracing && !dialog.IsStale, 30000), "traced again");
            Assert.Equal(0.25, dialog.ReadTrace().BrightnessCutoff);
        });
    }

    [Fact]
    public void TheSliders_TakeTheColourOfThePanel_InTheDarkWindow()
    {
        Ui.Run(() => Create(Photo(), dark: true), async form =>
        {
            var dialog = (TraceBitmapDialog)form;
            await Traced(dialog);

            Assert.Equal(ThemeManager.DarkBack, dialog.CutoffSlider.BackColor);
            Assert.Equal(ThemeManager.DarkBack, dialog.OptimizeSlider.BackColor);
        });
    }
    // ── The size ────────────────────────────────────────────────────────────

    [Fact]
    public void WhileTheProportionsAreKept_TheOtherSideFollows()
    {
        Ui.Run(() => Create(Photo(width: 240, height: 160)), async form =>
        {
            var dialog = (TraceBitmapDialog)form;
            await Traced(dialog);
            Assert.True(dialog.KeepProportionsButton.Checked);

            dialog.WidthNumeric.Value = 150;
            Assert.Equal(100.00m, dialog.HeightNumeric.Value);

            dialog.HeightNumeric.Value = 30;
            Assert.Equal(45.00m, dialog.WidthNumeric.Value);
        });
    }

    [Fact]
    public void WithoutKeepingTheProportions_TheSidesAreIndependent_AndKeepingThemAgainFollowsTheWidth()
    {
        Ui.Run(() => Create(Photo(width: 240, height: 160)), async form =>
        {
            var dialog = (TraceBitmapDialog)form;
            await Traced(dialog);

            dialog.KeepProportionsButton.Checked = false;
            dialog.WidthNumeric.Value = 120;
            dialog.HeightNumeric.Value = 20;
            Assert.Equal(120m, dialog.WidthNumeric.Value);
            Assert.Equal(20m, dialog.HeightNumeric.Value);

            dialog.KeepProportionsButton.Checked = true;
            Assert.Equal(120m, dialog.WidthNumeric.Value);
            Assert.Equal(80.00m, dialog.HeightNumeric.Value);
        });
    }

    [Fact]
    public void TheButtonThatKeepsTheProportions_ShowsAChain_AndSaysWhatItDoes()
    {
        Ui.Run(() => Create(Photo()), async form =>
        {
            var dialog = (TraceBitmapDialog)form;
            await Traced(dialog);

            var linked = dialog.KeepProportionsButton.Image;
            Assert.NotNull(linked);
            dialog.KeepProportionsButton.Checked = false;
            Assert.NotNull(dialog.KeepProportionsButton.Image);
            Assert.NotSame(linked, dialog.KeepProportionsButton.Image);
        });
    }

    // ── Saving ──────────────────────────────────────────────────────────────

    [Fact]
    public void Save_AsksWithTheNameOfThePhoto_AndTheKindOfFileThatIsPicked()
    {
        var photo = Photo("granny.jpg");
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (TraceBitmapDialog)form;
            await Traced(dialog);
            var asked = new List<(string Title, string Filter, int Index, string Folder, string Name)>();
            dialog.AskSavePath = (title, filter, index, folder, name) =>
            {
                asked.Add((title, filter, index, folder, name));
                return null; // the person cancels
            };

            Assert.False(await dialog.SaveAsync());
            dialog.FormatCombo.SelectedIndex = 1;
            Assert.False(await dialog.SaveAsync());

            Assert.Equal(2, asked.Count);
            Assert.Equal(TraceStrings.SaveTitle, asked[0].Title);
            Assert.Equal("SVG drawing (*.svg)|*.svg|DXF drawing (*.dxf)|*.dxf", asked[0].Filter);
            Assert.Equal((1, "granny.svg"), (asked[0].Index, asked[0].Name));
            Assert.Equal((2, "granny.dxf"), (asked[1].Index, asked[1].Name));
            Assert.Equal(_folder.Path, asked[0].Folder);
            Assert.Null(dialog.SavedPath);
        });
    }

    [Fact]
    public void ASavedSvg_HasTheSizeInMillimetres_AndOnePathForEachColour()
    {
        var target = _folder.File("out.svg");
        Ui.Run(() => Create(Photo()), async form =>
        {
            var dialog = (TraceBitmapDialog)form;
            await Traced(dialog);
            dialog.WidthNumeric.Value = 120; // 120 x 80 in the proportions of the photo
            dialog.AskSavePath = (_, _, _, _, _) => (target, 1);

            Assert.True(await dialog.SaveAsync());

            Assert.Equal(target, dialog.SavedPath);
            var svg = XDocument.Parse(File.ReadAllText(target)).Root!;
            Assert.Equal("120mm", (string?)svg.Attribute("width"));
            Assert.Equal("80mm", (string?)svg.Attribute("height"));
            Assert.Equal("0 0 120 80", (string?)svg.Attribute("viewBox"));
            var path = Assert.Single(svg.Descendants(), element => element.Name.LocalName == "path");
            Assert.Equal(2, ((string?)path.Attribute("d"))!.Count(c => c == 'M')); // the disc and the bar
            Assert.StartsWith("Saved the vector drawing", dialog.StatusText);
            Assert.NotEqual(new byte[] { 0xEF, 0xBB, 0xBF }, File.ReadAllBytes(target)[..3]); // no byte order mark
        });
    }

    [Fact]
    public void ASavedDxf_IsInMillimetres_WithOnePolylineForEachOutline()
    {
        var target = _folder.File("out.dxf");
        Ui.Run(() => Create(Photo()), async form =>
        {
            var dialog = (TraceBitmapDialog)form;
            await Traced(dialog);
            dialog.AskSavePath = (_, _, _, _, _) => (target, 2);
            dialog.AccuracyNumeric.Value = 0.1m;

            Assert.True(await dialog.SaveAsync());

            var text = File.ReadAllText(target);
            Assert.Contains("AC1009", text);
            Assert.Equal(2, Count(text, "  0\r\nPOLYLINE\r\n")); // the disc and the bar
            Assert.Equal(2, Count(text, "  0\r\nSEQEND\r\n"));
            Assert.Contains(" 10\r\n100\r\n", text); // $EXTMAX x: the width, 100 mm
            Assert.Equal(1, dialog.FormatCombo.SelectedIndex); // what was written is what the format says
        });
    }

    [Fact]
    public void ANameWithNoExtension_GetsTheOneOfTheKindThatWasPicked_AndAnExtensionDecidesTheFormat()
    {
        var bare = _folder.File("bare");
        var odd = _folder.File("odd.dxf");
        Ui.Run(() => Create(Photo()), async form =>
        {
            var dialog = (TraceBitmapDialog)form;
            await Traced(dialog);

            dialog.AskSavePath = (_, _, _, _, _) => (bare, 2);
            Assert.True(await dialog.SaveAsync());
            Assert.Equal(bare + ".dxf", dialog.SavedPath);
            Assert.Contains("AC1009", File.ReadAllText(bare + ".dxf"));

            dialog.AskSavePath = (_, _, _, _, _) => (odd, 1); // the kind says SVG, the name says DXF: the name wins
            Assert.True(await dialog.SaveAsync());
            Assert.Contains("AC1009", File.ReadAllText(odd));
        });
    }

    [Fact]
    public void Save_TracesAgainFirst_WhenTheSettingsChangedAfterTheLastTrace()
    {
        var target = _folder.File("fresh.svg");
        Ui.Run(() => Create(Photo()), async form =>
        {
            var dialog = (TraceBitmapDialog)form;
            await Traced(dialog);
            var first = dialog.CurrentTrace;

            dialog.GraysRadio.Checked = true; // changed, and Update was not pressed
            Assert.True(dialog.IsStale);
            dialog.AskSavePath = (_, _, _, _, _) => (target, 1);

            Assert.True(await dialog.SaveAsync());

            Assert.NotSame(first, dialog.CurrentTrace);
            var svg = XDocument.Parse(File.ReadAllText(target)).Root!;
            Assert.True(svg.Descendants().Count(element => element.Name.LocalName == "path") >= 2, "the file has the gray layers, not the one black shape of the old trace");
        });
    }

    [Fact]
    public void ASaveThatCannotBeWritten_SaysWhy_AndLeavesTheWindowWorking()
    {
        var blocked = _folder.File("blocked");
        File.WriteAllText(blocked, "a file where a folder is needed");
        Ui.Run(() => Create(Photo()), async form =>
        {
            var dialog = (TraceBitmapDialog)form;
            await Traced(dialog);
            dialog.AskSavePath = (_, _, _, _, _) => (Path.Combine(blocked, "out.svg"), 1);

            Assert.False(await dialog.SaveAsync());

            Assert.StartsWith("The drawing could not be saved", dialog.StatusText);
            Assert.Null(dialog.SavedPath);
            Assert.True(dialog.SaveButton.Enabled);
        });
    }

    [Fact]
    public void TheFormatOfAName_IsItsExtension_OrElseTheKindThatWasPicked()
    {
        Assert.Equal(TraceFileFormat.Svg, TraceBitmapDialog.FormatOf("a.svg", 2));
        Assert.Equal(TraceFileFormat.Dxf, TraceBitmapDialog.FormatOf("a.DXF", 1));
        Assert.Equal(TraceFileFormat.Dxf, TraceBitmapDialog.FormatOf("a", 2));
        Assert.Equal(TraceFileFormat.Svg, TraceBitmapDialog.FormatOf("a.txt", 1));
        Assert.Equal(TraceFileFormat.Svg, TraceBitmapDialog.FormatOf("a", 0));
    }

    [Fact]
    public void TheAccuracyOfCurves_IsOnlyOfferedForDxf()
    {
        Ui.Run(() => Create(Photo()), async form =>
        {
            var dialog = (TraceBitmapDialog)form;
            await Traced(dialog);
            Assert.False(dialog.AccuracyNumeric.Enabled);

            dialog.FormatCombo.SelectedIndex = 1;
            Assert.True(dialog.AccuracyNumeric.Enabled);
        });
    }

    // ── What is kept ────────────────────────────────────────────────────────

    [Fact]
    public void TheValuesOfTheWindow_AreKeptWhenItIsClosed_AndComeBackTheNextTime()
    {
        var photo = Photo();
        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (TraceBitmapDialog)form;
            await Traced(dialog);
            dialog.ColorsRadio.Checked = true;
            dialog.ColorsNumeric.Value = 5;
            dialog.StackCheck.Checked = false;
            dialog.SpecklesNumeric.Value = 6;
            dialog.WidthNumeric.Value = 180; // the longest side
            dialog.FormatCombo.SelectedIndex = 1;
            dialog.AccuracyNumeric.Value = 0.25m;
            dialog.KeepProportionsButton.Checked = false;
            dialog.LiveCheck.Checked = true;
            dialog.DetailCombo.SelectedItem = 1000;
        });

        Assert.True(File.Exists(SettingsFile), "the values were written when the window closed");

        Ui.Run(() => Create(photo), async form =>
        {
            var dialog = (TraceBitmapDialog)form;
            await Traced(dialog);

            Assert.True(dialog.ColorsRadio.Checked);
            Assert.Equal(5m, dialog.ColorsNumeric.Value);
            Assert.False(dialog.StackCheck.Checked);
            Assert.Equal(6m, dialog.SpecklesNumeric.Value);
            Assert.Equal(TraceFileFormat.Dxf, dialog.Format);
            Assert.Equal(0.25m, dialog.AccuracyNumeric.Value);
            Assert.False(dialog.KeepProportionsButton.Checked);
            Assert.True(dialog.LiveCheck.Checked);
            Assert.Equal(1000, dialog.Detail);
            Assert.Equal(5, dialog.ColorsSlider.Value); // the sliders show the numbers that were read, not the defaults
            Assert.Equal(6, dialog.SpecklesSlider.Value);
            Assert.Equal(180m, Math.Max(dialog.WidthNumeric.Value, dialog.HeightNumeric.Value)); // the same longest side for the next photo
        });
    }

    [Fact]
    public void ABrokenSettingsFile_IsIgnored()
    {
        File.WriteAllText(SettingsFile, "{ this is not json");
        Ui.Run(() => Create(Photo()), async form =>
        {
            var dialog = (TraceBitmapDialog)form;
            await Traced(dialog);

            Assert.True(dialog.CutoffRadio.Checked);
            Assert.Equal(0.45m, dialog.CutoffNumeric.Value);
        });
    }

    [Fact]
    public void TheDarkWindow_HasDarkColours_AndTheSameValues()
    {
        Ui.Run(() => Create(Photo(), dark: true), async form =>
        {
            var dialog = (TraceBitmapDialog)form;
            await Traced(dialog);

            Assert.True(dialog.BackColor.R < 80, "a dark window");
            Assert.True(dialog.Preview.Surround.R < 60, "a dark surround of the preview");
            Assert.Equal(0.45m, dialog.CutoffNumeric.Value);
        });
    }

    [Fact]
    public void TheWindow_IsNotBiggerThanTheScreenItOpensOn()
    {
        Ui.Run(() => Create(Photo()), async form =>
        {
            var dialog = (TraceBitmapDialog)form;
            var area = Screen.FromControl(dialog).WorkingArea;

            Assert.True(dialog.Width <= area.Width && dialog.Height <= area.Height, $"{dialog.Size} on a screen of {area.Size}");
            Assert.True(dialog.MinimumSize.Width <= area.Width && dialog.MinimumSize.Height <= area.Height);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void ThePreview_ZoomsWithTheWheel_MovesWithTheMouse_AndFitsAgain()
    {
        Ui.Run(() => Create(Photo()), async form =>
        {
            var dialog = (TraceBitmapDialog)form;
            await Traced(dialog);
            var preview = dialog.Preview;
            var fitted = preview.Zoom;
            Assert.True(fitted > 0);

            var middle = new Point(preview.Width / 2, preview.Height / 2);
            Assert.False(preview.Moving);
            Wheel(preview, middle, 120);
            Assert.True(preview.Zoom > fitted);
            Assert.True(preview.Moving, "drawn rough while it is being zoomed");
            Assert.True(await Ui.Until(() => !preview.Moving, 5000), "and drawn well again a moment after the last move");
            Wheel(preview, middle, -120);
            Assert.Equal(fitted, preview.Zoom, 3);

            Wheel(preview, middle, 120);
            preview.FitToWindow();
            Assert.Equal(fitted, preview.Zoom, 3);
        });
    }

    private static void Wheel(Control control, Point at, int delta) =>
        typeof(Control).GetMethod("OnMouseWheel", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(control, [new MouseEventArgs(MouseButtons.None, 0, at.X, at.Y, delta)]);

    // ── The button of the main window ───────────────────────────────────────

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;

    private static ToolStrip Toolbar(Form form) =>
        Ui.Controls<ToolStrip>(form).Single(strip => strip is not StatusStrip && strip.Items.Cast<ToolStripItem>().Any(item => item.Name == "colorizeToolStripButton"));

    [Fact]
    public void TheMainWindow_HasTheTraceButton_AfterMakeABook_AndBeforeVideoToGifAndTheSticker()
    {
        Ui.Run(() => new MainForm(), async form =>
        {
            var names = Toolbar(form).Items.Cast<ToolStripItem>().Select(item => item.Name).ToList();

            Assert.Equal("stickerToolStripButton", names[^1]);
            Assert.Equal("videoToGifToolStripButton", names[^2]);
            Assert.Equal("traceBitmapToolStripButton", names[^3]);
            Assert.Equal("makeBookToolStripButton", names[^4]);
            var button = Toolbar(form).Items["traceBitmapToolStripButton"]!;
            Assert.Equal(TraceStrings.ButtonText, button.Text);
            Assert.Equal(TraceStrings.ButtonTooltip, button.ToolTipText);
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
            ((ToolStripButton)Toolbar(form).Items["traceBitmapToolStripButton"]!).PerformClick();
            await Task.Delay(300);

            Assert.Equal(TraceStrings.StatusNoPhoto, Field<ToolStripStatusLabel>(form, "toolStripStatusLabel1").Text);
            Assert.Null(Ui.Find("TraceBitmapDialog"));
        }, seconds: 60);
    }

    [Fact]
    public void ForAVideo_TheButtonSaysItCannotBeTraced()
    {
        // The video lives OUTSIDE the folder the window opens: a video inside it would be read for its poster in the background, and
        // when that read fails (these 8 bytes are no video) the window writes "Error loading image" into the status label - on a slow
        // machine (the release CI) after the click, over the very message this test waits for
        using var elsewhere = new TempFolder();
        var video = elsewhere.File("clip.mp4");
        File.WriteAllBytes(video, [0, 0, 0, 24, 102, 116, 121, 112]);
        Ui.Run(() => new MainForm(_folder.Path), async form =>
        {
            await Task.Delay(300);
            Field<PictureBox>(form, "pictureBox1").Tag = video;

            ((ToolStripButton)Toolbar(form).Items["traceBitmapToolStripButton"]!).PerformClick();
            await Task.Delay(300);

            Assert.Equal(TraceStrings.StatusIsVideo, Field<ToolStripStatusLabel>(form, "toolStripStatusLabel1").Text);
            Assert.Null(Ui.Find("TraceBitmapDialog"));
        }, seconds: 60);
    }

    [Fact]
    public void ForAPhoto_TheButtonOpensTheWindow_ForThatPhoto()
    {
        var photo = Photo();
        Ui.Run(() => new MainForm(_folder.Path), async form =>
        {
            var viewer = Field<PictureBox>(form, "pictureBox1");
            typeof(MainForm).GetMethod("LoadImage", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, [photo]);
            Assert.True(await Ui.Until(() => viewer.Tag as string == photo && viewer.Image is not null, 20000), "the photo is shown");

            form.BeginInvoke(() => ((ToolStripButton)Toolbar(form).Items["traceBitmapToolStripButton"]!).PerformClick());
            Assert.True(await Ui.Until(() => Ui.Find("TraceBitmapDialog") is not null, 30000), "the window opened");

            var dialog = (TraceBitmapDialog)Ui.Find("TraceBitmapDialog")!;
            Assert.Contains("photo.jpg", dialog.Text);
            await Traced(dialog);
            dialog.Close();
            Assert.True(await Ui.Until(() => Ui.Find("TraceBitmapDialog") is null, 10000), "the window closed");
        }, seconds: 90);
    }
}
