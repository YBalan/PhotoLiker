using System.ComponentModel;
using PhotoLiker.Core;

namespace SmokeTour;

/// <summary>
/// The Language row of the settings grid: English at the start, Ukrainian at once when the row is changed (the buttons, the strip of the
/// search, the help of the grid, the dialogs), kept in the global config, and English again when it is changed back. A run of its own
/// (<c>--only-language</c>) that photographs the window and the dialogs in Ukrainian.
/// </summary>
internal sealed partial class Tour
{
    public async Task RunLanguage()
    {
        form.TopMost = true;
        checks.Note($"window brought to the front: {ScreenCapture.BringToFront(Window)}");

        var jpegs = DemoFolder.CountTopLevel(".jpg");
        checks.Step("1. English at the start");
        checks.That(await Until(() => form.Config.Files.Count == jpegs && PhotoShown()), $"{jpegs} photos listed and the first one shown");
        form.Find<SplitContainer>("splitContainer1").SplitterDistance = SidePanelWidth;
        form.Find<SplitContainer>("splitContainer2").SplitterDistance = SettingsGridHeight;
        await Settle();

        var language = form.SettingsRow("Language");
        checks.That(Shown(language) == "English", $"the Language row says English ({Shown(language)})");
        checks.That(form.ToolbarItem("openSourceFolderToolStripButton").ToolTipText == "Open source folder in File Explorer", "the buttons are English");
        checks.That(form.ToolbarItem("generateVideoToolStripButton").Text == "Generate Video", "the Generate Video button too");
        checks.That(language.PropertyDescriptor!.Description.StartsWith("The language of the program"), "and so is the help of the row");
        await Shot("20-language-english");

        checks.Step("2. Changing the row to Ukrainian writes every text again");
        var converter = language.PropertyDescriptor.Converter;
        checks.That(converter.GetStandardValues()!.Cast<string>().SequenceEqual(["en", "ua"]), "the row offers the two languages, one for each file (photo-liker-en.txt, photo-liker-ua.txt)");
        checks.That((string?)converter.ConvertToString("ua") == "Українська", "and names Ukrainian in Ukrainian");
        language.PropertyDescriptor.SetValue(form.Config, converter.ConvertFromString("Українська"));

        checks.That(await Until(() => form.ToolbarItem("openSourceFolderToolStripButton").ToolTipText == "Відкрити папку з фото в Провіднику"), "the buttons of the toolbar are Ukrainian");
        await Settle();
        checks.That(form.ToolbarItem("generateVideoToolStripButton").Text == "Створити відео", "Generate Video");
        checks.That(form.ToolbarItem("alignDatesToolStripButton").Text == "Вирівняти дати", "the buttons made in code too (Align dates)");
        checks.That(form.ToolbarItem("findDuplicatesToolStripButton").ToolTipText.StartsWith("Знайти фото"), "(Find duplicates)");
        checks.That(form.StatusBarItem("rotateToolStripButton").ToolTipText.StartsWith("Повернути фото"), "the rotate buttons");
        checks.That(form.Find<ToolStrip>("searchToolStrip").Items["searchToolStripButton"]!.ToolTipText.StartsWith("Опишіть"), "the strip of the search");
        checks.That(form.Find<ToolStrip>("searchToolStrip").Items["searchModelToolStripLabel"]!.Text == "Модель:", "(the label of the model)");
        checks.That(form.Find<ToolStrip>("searchToolStrip").Items["searchModelToolStripComboBox"] is ToolStripComboBox { SelectedItem: { } model } && model.ToString()!.Contains("швидка"), "(and the lines of the model box)");
        checks.That(form.Config.Language == "ua" && Loc.Language.Code == "ua", "the language that is on is Ukrainian");

        var row = form.SettingsRow("LikedFolder");
        checks.That(row.PropertyDescriptor!.Description.StartsWith("Куди копіюються"), "the help of a row of the grid is Ukrainian");
        checks.That(form.SettingsRow("CurrentIndex").Label == "CurrentIndex", "while the names of the rows are not translated");
        checks.That(Shown(form.SettingsRow("Language")) == "Українська", "the Language row says Українська");
        checks.That(form.GpsLink.ToolTipText == "Відкрити місце в Google Maps", "and the Maps link of the status bar");

        var config = Path.Combine(AppPaths.DataFolder, "global-config.json");
        checks.That(await Until(() => File.Exists(config) && File.ReadAllText(config).Contains("\"Language\": \"ua\"")), "the language is kept in the global config for the next start");
        checks.That(!File.Exists(PhotoLiker.Core.Settings.GetFilePath(folder)) || !File.ReadAllText(PhotoLiker.Core.Settings.GetFilePath(folder)).Contains("Language"), "and not in the settings file of the folder");
        await Shot("21-language-ukrainian");

        checks.Step("3. The dialogs speak Ukrainian");
        await UkrainianAbout();
        await UkrainianDialog("alignDatesToolStripButton", "DateAlignmentDialog", "22-uk-align-dates");
        await UkrainianDialog("removeEmptyFoldersToolStripButton", "EmptyFoldersDialog", "23-uk-empty-folders");
        await UkrainianDialog("findDuplicatesToolStripButton", "DuplicatesDialog", "24-uk-duplicates");
        await UkrainianVideoDialog();

        checks.Step("4. A language file that is put in the folder is a language of the list");
        var folderOfUser = Path.Combine(AppPaths.DataFolder, "Languages");
        Directory.CreateDirectory(folderOfUser);
        var german = Path.Combine(folderOfUser, "photo-liker-de.txt");
        File.WriteAllLines(german, ["Name = Deutsch", "Culture = de-DE", "Plural = one-other", "ToolbarStrings.OpenSource = Quellordner öffnen"], new System.Text.UTF8Encoding(true));
        var offered = form.SettingsRow("Language").PropertyDescriptor!.Converter.GetStandardValues()!.Cast<string>().ToList();
        checks.That(offered.SequenceEqual(["en", "de", "ua"]), $"the Language row offers it ({string.Join(", ", offered)})");
        form.SettingsRow("Language").PropertyDescriptor!.SetValue(form.Config, "de");
        checks.That(await Until(() => form.ToolbarItem("openSourceFolderToolStripButton").Text == "Quellordner öffnen"), "and its texts are used");
        checks.That(form.ToolbarItem("generateVideoToolStripButton").Text == "Generate Video", "what the file does not say is English");
        checks.That(await Until(() => form.Status.Contains("Deutsch")), $"the status line tells how much of the file is missing ({form.Status})");
        File.Delete(german);

        checks.Step("5. Changing the row back brings English back");
        language = form.SettingsRow("Language");
        language.PropertyDescriptor!.SetValue(form.Config, "en");
        checks.That(await Until(() => form.ToolbarItem("openSourceFolderToolStripButton").ToolTipText == "Open source folder in File Explorer"), "the buttons are English again");
        checks.That(form.ToolbarItem("generateVideoToolStripButton").Text == "Generate Video", "Generate Video");
        checks.That(await Until(() => File.ReadAllText(config).Contains("\"Language\": \"en\"")), "and the config says so");
    }

    /// <summary>What the grid shows for the value of a row.</summary>
    private static string Shown(GridItem row) => row.PropertyDescriptor!.Converter.ConvertToString(row.Value) ?? string.Empty;

    private async Task UkrainianAbout()
    {
        var help = form.ToolbarItem("helpToolStripButton");
        form.BeginInvoke(() => help.PerformClick());
        Form? dialog = null;
        checks.That(await Until(() => (dialog = OpenDialog("AboutDialog")) is not null), "the Help button opens the About box");
        if (dialog is null)
            return;

        var text = AllControls(dialog).OfType<TextBox>().Single().Text;
        checks.That(dialog.Text == "Про PhotoLiker" && text.Contains("Пробіл") && text.Contains("Вподобати фото"), "the About box lists the keys in Ukrainian");
        var buttons = AllControls(dialog).OfType<Button>().Select(b => b.Text).ToList();
        checks.That(buttons.Count == 3 && buttons[0] == "User manual (English)" && buttons[1] == "Посібник користувача (українською)", $"and keeps the two manual buttons each in its own language ({string.Join(" | ", buttons)})");
        await DialogShot(dialog, "25-uk-about");
        dialog.Close();
        checks.That(await Until(() => OpenDialog("AboutDialog") is null), "it closes");
    }

    private async Task UkrainianDialog(string buttonName, string typeName, string shot)
    {
        var button = form.ToolbarItem(buttonName);
        form.BeginInvoke(() => button.PerformClick());
        Form? dialog = null;
        checks.That(await Until(() => (dialog = OpenDialog(typeName)) is not null), $"{typeName} opens");
        if (dialog is null)
            return;

        await Settle(1500); // it looks at the demo folder meanwhile
        var texts = AllControls(dialog).Select(c => c.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        checks.That(texts.Any(t => t.Any(c => c is >= 'А' and <= 'я')), $"its texts are Ukrainian ({texts.FirstOrDefault(t => t.Length > 12)?[..Math.Min(50, texts.First(t => t.Length > 12).Length)]}...)");
        checks.That(!texts.Any(t => t.StartsWith('[') && t.EndsWith(']') && t.Contains('.') && !t.Contains(' ')), "and none is the name of a text that is missing");
        await DialogShot(dialog, shot);

        dialog.Close();
        checks.That(await Until(() => OpenDialog(typeName) is null), "it closes");
    }

    private async Task UkrainianVideoDialog()
    {
        var button = form.ToolbarItem("generateVideoToolStripButton");
        form.BeginInvoke(() => button.PerformClick());
        Form? dialog = null;
        checks.That(await Until(() => (dialog = OpenDialog(VideoDialogType)) is not null), "Generate Video opens");
        if (dialog is null)
            return;

        await Settle(1200);
        var labels = AllControls(dialog).OfType<Label>().Where(l => l.Text.EndsWith(':')).Select(l => l.Text).ToList();
        checks.That(labels.Contains("Фото з:") && labels.Contains("Тривалість відео:"), $"the labels are Ukrainian ({string.Join(" ", labels.Take(4))}...)");

        // Every label has the room its text needs: nothing is cut off at the edge of its column
        var clipped = AllControls(dialog).OfType<Label>()
            .Where(l => l.Visible && l.Text.Length > 0 && !l.Text.Contains('\n') && l.Parent is not null && l.Width < TextRenderer.MeasureText(l.Text, l.Font).Width - 2)
            .Select(l => l.Text).ToList();
        checks.That(clipped.Count == 0, "no label is wider than its place: " + string.Join(" | ", clipped));

        await DialogShot(dialog, "26-uk-generate-video");
        Field<Button>(dialog, "_cancelButton").PerformClick();
        checks.That(await Until(() => OpenDialog(VideoDialogType) is null), "it closes");
    }
}
