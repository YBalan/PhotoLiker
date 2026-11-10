using PhotoLiker.Core;

namespace PhotoLikerUI.Tests;

/// <summary>The main window made for real: the buttons that were added (Colorize) are there, in their place, and do what they say when pressed with nothing loaded.</summary>
[Collection(WindowsCollection.Name)]
public sealed class MainFormToolbarTests
{
    private static ToolStrip ToolbarOf(Form form) => Ui.Controls<ToolStrip>(form).Single(strip => strip is not StatusStrip && strip.Items.Cast<ToolStripItem>().Any(item => item.Name == "colorizeToolStripButton"));

    private static string StatusOf(Form form) =>
        typeof(MainForm).GetField("toolStripStatusLabel1", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(form) is ToolStripStatusLabel label ? label.Text : string.Empty;

    [Fact]
    public void TheMainWindow_Opens_WithTheColorizeButtonAfterFindDuplicates()
    {
        Ui.Run(() => new MainForm(), async form =>
        {
            var toolbar = ToolbarOf(form);
            var names = toolbar.Items.Cast<ToolStripItem>().Select(item => item.Name).ToList();

            // In the group of the tools, after Find duplicates (and Video speech, which is put there too), before the Register Context Menu button
            var duplicates = names.IndexOf("findDuplicatesToolStripButton");
            var colorize = names.IndexOf("colorizeToolStripButton");
            Assert.True(duplicates >= 0 && colorize > duplicates, "after Find duplicates");
            Assert.All(names.Skip(duplicates + 1).Take(colorize - duplicates - 1), name => Assert.Contains("ideoSpeech", name));
            Assert.DoesNotContain(names.Skip(duplicates).Take(colorize - duplicates), name => name.StartsWith("toolStripSeparator"));
            var button = toolbar.Items["colorizeToolStripButton"]!;
            Assert.Equal(ColorizeStrings.ButtonText, button.Text);
            Assert.Equal(ColorizeStrings.ButtonTooltip, button.ToolTipText);
            Assert.NotNull(button.Image);
            Assert.Equal(ToolStripItemDisplayStyle.Image, button.DisplayStyle);
            await Task.CompletedTask;
        }, seconds: 60);
    }

    [Fact]
    public void PressingColorize_WithNoPhotosLoaded_SaysSo_AndOpensNothing()
    {
        Ui.Run(() => new MainForm(), async form =>
        {
            ((ToolStripButton)ToolbarOf(form).Items["colorizeToolStripButton"]!).PerformClick();
            await Task.Delay(300);

            Assert.Null(Ui.Find("ColorizeDialog"));
            Assert.Equal(ColorizeStrings.StatusNoPhotos, StatusOf(form));
        }, seconds: 60);
    }

    [Fact]
    public void TheButtonsTextAndTooltip_FollowTheLanguage()
    {
        Ui.Run(() => new MainForm(), async form =>
        {
            var button = ToolbarOf(form).Items["colorizeToolStripButton"]!;
            var english = button.ToolTipText;

            var apply = typeof(MainForm).GetMethod("ApplyColorizeLanguage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            using (Loc.Use(AppLanguage.English))
                apply.Invoke(form, null);

            Assert.Equal(english, button.ToolTipText);
            Assert.Equal(ColorizeStrings.ButtonText, button.Text);
            await Task.CompletedTask;
        }, seconds: 60);
    }
}
