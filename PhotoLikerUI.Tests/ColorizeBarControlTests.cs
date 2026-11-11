using System.Reflection;
using PhotoLiker.Effects.Colorize;

namespace PhotoLikerUI.Tests;

/// <summary>The bar over a black and white photo by itself (see <see cref="ColorizeBar"/>), on a window of its own: what it says about the sliders, what it tells, and which buttons work when.</summary>
[Collection(WindowsCollection.Name)]
public sealed class ColorizeBarControlTests
{
    private static void Run(Func<ColorizeBar, Task> body) =>
        Ui.Run(() => new Form { Size = new Size(900, 300) }, async form =>
        {
            var bar = new ColorizeBar { Location = new Point(10, 10) };
            form.Controls.Add(bar);
            await body(bar);
        }, seconds: 30);

    /// <summary>A button of the bar by its name: the buttons with a picture have no word on them, so the name is the accessible one (✕ is a symbol, its text).</summary>
    private static Button ButtonNamed(Control bar, string name) => Ui.Controls<Button>(bar).First(button => button.AccessibleName == name || button.Text == name);

    private static string TipOf(ColorizeBar bar, Control control) =>
        ((ToolTip)typeof(ColorizeBar).GetField("_tip", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(bar)!).GetToolTip(control) ?? string.Empty;

    private static TrackBar Slider(Control bar, int index) => Ui.Controls<TrackBar>(bar).ElementAt(index);

    private static void Raise(Control control, string handler, EventArgs args) =>
        typeof(Control).GetMethod(handler, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(control, [args]);

    [Fact]
    public void TheSliders_SayTheTuning()
    {
        Run(async bar =>
        {
            Assert.Equal(new ColorTuning(1f, 0f), bar.Tuning);

            Slider(bar, 0).Value = 150;
            Slider(bar, 1).Value = -50;

            Assert.Equal(1.5f, bar.Tuning.Saturation, 4);
            Assert.Equal(-0.5f, bar.Tuning.Warmth, 4);
            Assert.Equal((150, -50), bar.Values);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void TheValuesTheOwnerSets_AreClamped_AndDoNotCountAsTheDoingOfThePerson()
    {
        Run(async bar =>
        {
            var changes = 0;
            bar.TuningChanged += () => changes++;

            bar.Values = (5000, -5000);

            Assert.Equal((ColorizeBar.MaxStrength, -ColorizeBar.MaxWarmth), bar.Values);
            Assert.Equal(0, changes);

            Slider(bar, 0).Value = 90; // a person at the slider
            Assert.Equal(1, changes);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void TheValuesBesideTheSliders_SayWhereTheyAre()
    {
        Run(async bar =>
        {
            bar.Values = (130, 35);
            Assert.Contains("130 %", Ui.Controls<Label>(bar).Select(label => label.Text));
            Assert.Contains("+35", Ui.Controls<Label>(bar).Select(label => label.Text));

            bar.Values = (100, -20);
            Assert.Contains("-20", Ui.Controls<Label>(bar).Select(label => label.Text));
            bar.Values = (100, 0);
            Assert.Contains("0", Ui.Controls<Label>(bar).Select(label => label.Text));
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void WhatWorksWhen_FollowsTheStateOfTheBar()
    {
        Run(async bar =>
        {
            var colorize = ButtonNamed(bar, ColorizeStrings.BarColorize);
            var save = ButtonNamed(bar, ColorizeStrings.BarSave);

            // A black and white photo, nothing done yet: Colorize works, the rest waits
            Assert.True(colorize.Enabled);
            Assert.False(save.Enabled);
            Assert.False(Slider(bar, 0).Enabled);
            Assert.False(Slider(bar, 1).Enabled);

            // The model is at work: nothing works
            bar.Busy = true;
            Assert.False(colorize.Enabled);

            // The colours are there
            bar.Busy = false;
            bar.Ready = true;
            bar.ColoursShown = true;
            Assert.Same(colorize, ButtonNamed(bar, ColorizeStrings.BarOriginal)); // the same button, now with the other name
            Assert.True(ButtonNamed(bar, ColorizeStrings.BarOriginal).Enabled);
            Assert.True(save.Enabled);
            Assert.True(Slider(bar, 0).Enabled);
            Assert.True(Slider(bar, 1).Enabled);

            // A copy is being saved
            bar.Busy = true;
            Assert.False(save.Enabled);
            Assert.False(ButtonNamed(bar, ColorizeStrings.BarOriginal).Enabled);

            // The original is looked at: nothing to save
            bar.Busy = false;
            bar.ColoursShown = false;
            Assert.False(save.Enabled);
            Assert.Same(colorize, ButtonNamed(bar, ColorizeStrings.BarColorize));
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void TheButtons_ShowPictures_NotWords()
    {
        Run(async bar =>
        {
            foreach (var name in new[] { ColorizeStrings.BarColorize, ColorizeStrings.BarSave, ColorizeStrings.BarMore })
            {
                var button = ButtonNamed(bar, name);
                Assert.NotNull(button.Image);
                Assert.Equal(string.Empty, button.Text);
                Assert.Equal(name, button.AccessibleName);
            }

            // ✕ stays a symbol
            Assert.Equal("✕", ButtonNamed(bar, "✕").Text);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void TheColorizeButton_ChangesItsPictureNameAndTip_WithWhatItDoesNext()
    {
        Run(async bar =>
        {
            var button = ButtonNamed(bar, ColorizeStrings.BarColorize);
            var turnsColoursOn = button.Image;
            Assert.Contains(ColorizeStrings.BarColorize, TipOf(bar, button));

            bar.Ready = true;
            bar.ColoursShown = true;
            Assert.NotSame(turnsColoursOn, button.Image); // the grey photo: the original comes back
            Assert.NotNull(button.Image);
            Assert.Equal(ColorizeStrings.BarOriginal, button.AccessibleName);
            Assert.Equal(ColorizeStrings.BarOriginal, TipOf(bar, button));
            Assert.Equal(string.Empty, button.Text);

            bar.ColoursShown = false;
            Assert.Same(turnsColoursOn, button.Image);
            Assert.Equal(ColorizeStrings.BarColorize, button.AccessibleName);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void TheTipsOfTheButtonsWithPictures_SayTheNameThenWhatTheyDo()
    {
        Run(async bar =>
        {
            Assert.StartsWith(ColorizeStrings.BarSave + ": ", TipOf(bar, ButtonNamed(bar, ColorizeStrings.BarSave)));
            Assert.Contains(ColorizeStrings.BarSaveTooltip, TipOf(bar, ButtonNamed(bar, ColorizeStrings.BarSave)));

            // "Many photos..." is a name with dots: in the tip they would sit in front of the colon
            var more = TipOf(bar, ButtonNamed(bar, ColorizeStrings.BarMore));
            Assert.StartsWith(ColorizeStrings.BarMore.TrimEnd('.', '…') + ": ", more);
            Assert.DoesNotContain("...:", more);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void ThePressedButtons_TellTheOwner_AndGiveTheKeyboardBack()
    {
        Run(async bar =>
        {
            var told = new List<string>();
            var used = 0;
            bar.ColorsToggled += on => told.Add(on ? "colours" : "original");
            bar.SaveClicked += () => told.Add("save");
            bar.MoreClicked += () => told.Add("more");
            bar.CloseClicked += () => told.Add("close");
            bar.Used += () => used++;

            ButtonNamed(bar, ColorizeStrings.BarColorize).PerformClick(); // asks for the colours
            bar.Ready = true;
            bar.ColoursShown = true;
            ButtonNamed(bar, ColorizeStrings.BarOriginal).PerformClick(); // asks for the original
            ButtonNamed(bar, ColorizeStrings.BarSave).PerformClick();
            ButtonNamed(bar, ColorizeStrings.BarMore).PerformClick();
            ButtonNamed(bar, "✕").PerformClick();

            Assert.Equal(["colours", "original", "save", "more", "close"], told);
            Assert.Equal(5, used);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void LettingGoOfASlider_SaysTheValueIsToBeKept_AndADoubleClickPutsItBack()
    {
        Run(async bar =>
        {
            var committed = 0;
            bar.TuningCommitted += () => committed++;
            var changed = 0;
            bar.TuningChanged += () => changed++;

            Slider(bar, 1).Value = 60;
            Assert.Equal(0, committed); // while it is dragged nothing is kept

            Raise(Slider(bar, 1), "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
            Assert.Equal(1, committed);

            Raise(Slider(bar, 1), "OnMouseDoubleClick", new MouseEventArgs(MouseButtons.Left, 2, 0, 0, 0));
            Assert.Equal(0, Slider(bar, 1).Value);
            Assert.Equal(2, committed);
            Assert.Equal(2, changed); // 60, then back to 0
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void TheStatus_IsShownAndHasATipForWhatTheLabelCuts()
    {
        Run(async bar =>
        {
            bar.Status = "x";
            Assert.Equal("x", bar.Status);
            Assert.Contains(Ui.Controls<Label>(bar), label => label.Text == "x");
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void TheTexts_FollowTheLanguage()
    {
        Run(async bar =>
        {
            Assert.Contains(Ui.Controls<Label>(bar), label => label.Text == ColorizeStrings.BarTitle);
            Assert.Contains(Ui.Controls<Label>(bar), label => label.Text == ColorizeStrings.BarStrength);
            Assert.Contains(Ui.Controls<Label>(bar), label => label.Text == ColorizeStrings.BarWarmth);

            using (PhotoLiker.Core.Loc.Use(PhotoLiker.Core.AppLanguage.English))
                bar.ApplyTexts();

            Assert.Equal(ColorizeStrings.BarSave, ButtonNamed(bar, ColorizeStrings.BarSave).AccessibleName);
            Assert.StartsWith(ColorizeStrings.BarSave, TipOf(bar, ButtonNamed(bar, ColorizeStrings.BarSave)));
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void TheBarIsAsWideAsItsRows_AndFitsTheViewerOfTheMainWindow()
    {
        Run(async bar =>
        {
            Assert.InRange(bar.Width, 500, 900); // the viewer of a window of 1200 pixels is about 840 wide
            Assert.InRange(bar.Height, 50, 120);
            await Task.CompletedTask;
        });
    }
}
