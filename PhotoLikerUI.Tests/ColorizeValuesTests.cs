using System.Drawing.Imaging;
using System.Reflection;
using PhotoLiker.Core;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The strength and the warmth of the colours: what is chosen in the Colorize window is kept, in the config of the program, and is what the next window (and the bar over a photo) starts from.
/// The windows are real; the model is not needed to open the window and set the boxes.
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class ColorizeValuesTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public ColorizeValuesTests() => DeleteConfig();

    public void Dispose()
    {
        _folder.Dispose();
        DeleteConfig();
    }

    private static void DeleteConfig()
    {
        try { File.Delete(Path.Combine(AppPaths.DataFolder, "global-config.json")); } catch (IOException) { }
    }

    private void MakePhotos()
    {
        for (var i = 0; i < 2; i++)
        {
            using var bitmap = new Bitmap(64, 48);
            using (var g = Graphics.FromImage(bitmap))
                g.Clear(Color.FromArgb(90 + i * 40, 90 + i * 40, 90 + i * 40));
            bitmap.Save(_folder.File($"grey{i}.jpg"), ImageFormat.Jpeg);
        }
    }

    private static void OpenColorize(MainForm form) =>
        form.BeginInvoke(() => typeof(MainForm).GetMethod("Colorize", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, null));

    private static NumericUpDown[] Boxes(Form dialog) => [.. Ui.Controls<NumericUpDown>(dialog)]; // the strength first, then the warmth

    private static async Task<Form> WaitForWindow(Func<bool> listed)
    {
        Assert.True(await Ui.Until(() => Ui.Find("ColorizeDialog") is not null && listed(), 30000), "the Colorize window is open");
        return Ui.Find("ColorizeDialog")!;
    }

    private static bool PhotosListed(MainForm form) =>
        typeof(MainForm).GetField("_currentConfig", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form) is Settings { Files.Count: > 0 };

    [Fact]
    public void WhatIsChosenInTheColorizeWindow_IsKept_AndTheNextWindowStartsFromIt()
    {
        MakePhotos();
        new Settings { CurrentFolder = _folder.Path }.Write();

        Ui.Run(() => new MainForm(_folder.Path), async form =>
        {
            var main = (MainForm)form;
            Assert.True(await Ui.Until(() => PhotosListed(main), 30000), "the photos are listed");

            // The first window starts from the colours as the model gives them, and what is typed there is kept when it is closed
            OpenColorize(main);
            var first = await WaitForWindow(() => true);
            var boxes = Boxes(first);
            Assert.Equal((100m, 0m), (boxes[0].Value, boxes[1].Value));
            boxes[0].Value = 140;
            boxes[1].Value = 25;
            first.Close();
            Assert.True(await Ui.Until(() => Ui.Find("ColorizeDialog") is null, 30000), "the window is closed");

            var config = GlobalConfig.Load();
            Assert.Equal(140, config.ColorizeStrength);
            Assert.Equal(25, config.ColorizeWarmth);

            // The next one starts from them
            OpenColorize(main);
            var second = await WaitForWindow(() => true);
            Assert.Equal((140m, 25m), (Boxes(second)[0].Value, Boxes(second)[1].Value));
            second.Close();
            Assert.True(await Ui.Until(() => Ui.Find("ColorizeDialog") is null, 30000));
        }, seconds: 180);
    }

    [Fact]
    public void AWindowClosedWithoutChangingAnything_KeepsTheValuesItStartedFrom()
    {
        MakePhotos();
        new Settings { CurrentFolder = _folder.Path }.Write();
        new GlobalConfig { ColorizeStrength = 60, ColorizeWarmth = -30 }.Save();

        Ui.Run(() => new MainForm(_folder.Path), async form =>
        {
            var main = (MainForm)form;
            Assert.True(await Ui.Until(() => PhotosListed(main), 30000));

            OpenColorize(main);
            var window = await WaitForWindow(() => true);
            Assert.Equal((60m, -30m), (Boxes(window)[0].Value, Boxes(window)[1].Value)); // from the config of the program
            window.Close();
            Assert.True(await Ui.Until(() => Ui.Find("ColorizeDialog") is null, 30000));
        }, seconds: 180);

        var kept = GlobalConfig.Load();
        Assert.Equal((60, -30), (kept.ColorizeStrength, kept.ColorizeWarmth));
    }
}
