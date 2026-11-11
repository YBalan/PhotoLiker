using System.Runtime.InteropServices;
using System.Text;
using PhotoLiker.Search;

namespace PhotoLikerUI.Tests;

/// <summary>
/// Deleting people in the People window, with the real window and the real question box (answered by clicking its buttons): the button needs a selected person, No changes nothing,
/// Yes takes the people out of the list, the groups and the files, and the Delete key asks the same question for all the selected people.
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class PeopleDialogDeleteTests : IDisposable
{
    private const int IdYes = 6;
    private const int IdNo = 7;
    private const int ButtonClick = 0x00F5;
    private const int KeyDown = 0x0100;
    private const int VkDelete = 0x2E;

    private readonly TempFolder _photos = new();
    private readonly TempFolder _data = new();

    public void Dispose()
    {
        _photos.Dispose();
        _data.Dispose();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? title);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDlgItem(IntPtr dialog, int id);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int max);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    private string Shared => Path.Combine(_data.Path, "people.json");

    private string GroupsFile => Path.Combine(_data.Path, "people-groups.json");

    private PeopleLibrary Library() => PeopleLibrary.Load(_photos.Path, Shared, GroupsFile);

    private static IndexedFace Face(string photo, int axis, float x = 0.2f)
    {
        var look = new float[3];
        look[axis] = 1;
        return new IndexedFace(new IndexedPhoto(photo, 1, 1), x, 0.2f, 0.1f, 0.1f, 0.9f, manual: false, look);
    }

    /// <summary>Mom (2 faces), Dad (1), Aunt (1); Mom and Dad are in Family, Aunt is in no group.</summary>
    private PeopleLibrary Family()
    {
        var people = Library();
        people.Label("Mom", Face("a.jpg", 0));
        people.Label("Mom", Face("b.jpg", 0, x: 0.5f));
        people.Label("Dad", Face("c.jpg", 1));
        people.Label("Aunt", Face("d.jpg", 2));
        people.SetGroupMembers("Family", ["Mom", "Dad"], member: true);
        return people;
    }

    private static ListView ListOf(Form dialog) => Ui.Controls<ListView>(dialog).Single(list => list.Columns.Count == 3);

    private static Button DeleteButton(Form dialog) => Ui.Controls<Button>(dialog).Single(button => button.Text == PeopleStrings.DeletePersonButton);

    private static string[] Shown(Form dialog) => [.. ListOf(dialog).Items.Cast<ListViewItem>().Select(item => item.Text).Order()];

    private static void Select(Form dialog, params string[] names)
    {
        foreach (ListViewItem item in ListOf(dialog).Items)
            item.Selected = names.Contains(item.Text);
    }

    private static string StatusOf(Form dialog) => Ui.Controls<Label>(dialog).Select(label => label.Text).FirstOrDefault(text => text.StartsWith("People deleted")) ?? string.Empty;

    /// <summary>Waits for the question box of the window, returns what it asks, and presses one of its buttons.</summary>
    private static async Task<string> Answer(int button)
    {
        var title = PeopleStrings.DeletePersonTitle;
        Assert.True(await Ui.Until(() => FindWindow("#32770", title) != IntPtr.Zero, 10000), "the question box opens");
        var box = FindWindow("#32770", title);

        var text = new StringBuilder(2000);
        GetWindowText(GetDlgItem(box, 0xFFFF), text, text.Capacity);
        SendMessage(GetDlgItem(box, button), ButtonClick, IntPtr.Zero, IntPtr.Zero);
        return text.ToString();
    }

    [Fact]
    public void TheDeleteButton_IsOnlyOnWhilePeopleAreSelected()
    {
        var people = Family();

        Ui.Run(() => new PeopleDialog(people, dark: false), dialog =>
        {
            Assert.False(DeleteButton(dialog).Enabled, "nobody is selected");

            Select(dialog, "Dad");
            Assert.True(DeleteButton(dialog).Enabled);

            Select(dialog, "Dad", "Aunt");
            Assert.True(DeleteButton(dialog).Enabled, "several people can be deleted at once");

            Select(dialog);
            Assert.False(DeleteButton(dialog).Enabled);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void TheButtonAndTheSummary_FitNextToEachOther()
    {
        var people = Family();

        Ui.Run(() => new PeopleDialog(people, dark: false), dialog =>
        {
            var rename = Ui.Controls<Button>(dialog).Single(button => button.Text == PeopleStrings.RenamePersonButton);
            var delete = DeleteButton(dialog);
            var list = ListOf(dialog);
            var bounds = (Control c) => dialog.RectangleToClient(c.Parent!.RectangleToScreen(c.Bounds)); // the whole control, border included

            Assert.True(bounds(rename).Right <= bounds(delete).Left, "the two buttons do not overlap");
            Assert.True(bounds(delete).Right <= bounds(list).Right, $"the Delete button stays under the list: {bounds(delete)} / {bounds(list)}");
            Assert.True(bounds(delete).Top >= bounds(list).Bottom, "the buttons are under the list");
            Assert.True(delete.Width >= 100 && delete.Height >= 24, "the button is readable");
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void AnsweringNo_ChangesNothing_AndTheQuestionNamesThePersonAndHerFaces()
    {
        var people = Family();

        Ui.Run(() => new PeopleDialog(people, dark: false), async dialog =>
        {
            Select(dialog, "Mom");
            dialog.BeginInvoke(() => DeleteButton(dialog).PerformClick());

            var question = await Answer(IdNo);

            Assert.Contains("'Mom'", question);
            Assert.Contains("(2)", question); // her two faces
            await Task.Delay(300);
            Assert.Equal(["Aunt", "Dad", "Mom"], Shown(dialog));
            Assert.Equal(["Aunt", "Dad", "Mom"], people.Names);
            Assert.Equal(["Dad", "Mom"], people.Groups.MembersOf("Family"));
        });
    }

    [Fact]
    public void AnsweringYes_DeletesThePerson_FromTheListTheGroupsAndTheFiles()
    {
        var people = Family();

        Ui.Run(() => new PeopleDialog(people, dark: false), async dialog =>
        {
            Select(dialog, "Mom");
            dialog.BeginInvoke(() => DeleteButton(dialog).PerformClick());

            await Answer(IdYes);

            Assert.True(await Ui.Until(() => Shown(dialog).SequenceEqual(["Aunt", "Dad"])), "the list no longer has her: " + string.Join(", ", Shown(dialog)));
            Assert.Equal(["Aunt", "Dad"], people.Names);
            Assert.Equal(["Dad"], people.Groups.MembersOf("Family"));
            Assert.Equal(["Aunt", "Dad"], PeopleLibrary.Load(_photos.Path, sharedFile: null).Names); // the file of the folder
            Assert.Equal(["Dad"], PersonGroups.Load(SearchPaths.GroupsFile(_photos.Path)).MembersOf("Family")); // the copy of the groups in the folder
            Assert.False(DeleteButton(dialog).Enabled, "nobody is selected any more");
            Assert.Equal(string.Format(PeopleStrings.DeletedFormat, 1, 2), StatusOf(dialog));
        });
    }

    [Fact]
    public void TheDeleteKey_AsksOnceForAllTheSelectedPeople_AndDeletesThem()
    {
        var people = Family();

        Ui.Run(() => new PeopleDialog(people, dark: false), async dialog =>
        {
            var list = ListOf(dialog);
            list.Focus();
            Select(dialog, "Mom", "Aunt");
            PostMessage(list.Handle, KeyDown, VkDelete, IntPtr.Zero);

            var question = await Answer(IdYes);

            Assert.Contains("2", question); // the people
            Assert.Contains("3", question); // and their faces: Mom's two and Aunt's one
            Assert.True(await Ui.Until(() => Shown(dialog).SequenceEqual(["Dad"])), "only Dad is left: " + string.Join(", ", Shown(dialog)));
            Assert.Equal(["Dad"], people.Names);
            Assert.Equal(["Dad"], people.Groups.MembersOf("Family"));
            Assert.Equal(string.Format(PeopleStrings.DeletedFormat, 2, 3), StatusOf(dialog));
        });
    }

    [Fact]
    public void TheDeleteKey_WithNobodySelected_AsksNothing()
    {
        var people = Family();

        Ui.Run(() => new PeopleDialog(people, dark: false), async dialog =>
        {
            var list = ListOf(dialog);
            list.Focus();
            Select(dialog);
            PostMessage(list.Handle, KeyDown, VkDelete, IntPtr.Zero);

            await Task.Delay(600);

            Assert.Equal(IntPtr.Zero, FindWindow("#32770", PeopleStrings.DeletePersonTitle));
            Assert.Equal(["Aunt", "Dad", "Mom"], Shown(dialog));
        });
    }
}
