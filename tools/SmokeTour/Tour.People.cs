using PhotoLiker.Core;
using PhotoLiker.Search;

namespace SmokeTour;

/// <summary>
/// The People window, for the picture of the README and the user manual (<c>--only-people</c>): 28 invented people sorted into the groups Family, Friends
/// and Work, written to the tour's own data folder (not the real one), and two of them selected. No photo and no model is needed.
/// </summary>
internal sealed partial class Tour
{
    private const string PeopleDialogType = "PeopleDialog";

    /// <summary>Name, how many faces of the person are named, the groups.</summary>
    private static readonly (string Name, int Faces, string[] Groups)[] InventedPeople =
    [
        ("Anna", 3, ["Family", "Friends"]), ("Boris", 4, ["Friends"]), ("Chris", 4, ["Friends"]), ("Dad", 2, ["Family"]), ("Dana", 2, ["Friends"]),
        ("Eugene", 3, ["Friends"]), ("Fiona", 3, ["Friends"]), ("George", 1, ["Work"]), ("Grandma Olga", 3, ["Family"]), ("Hanna", 3, ["Work"]),
        ("Ivan", 2, ["Work"]), ("Julia", 5, []), ("Kate", 1, []), ("Leo", 5, []), ("Max", 2, []), ("Mom", 1, ["Family"]), ("Nina", 1, []),
        ("Oscar", 2, []), ("Paul", 1, []), ("Quinn", 3, []), ("Rita", 2, []), ("Sam", 4, []), ("Tom", 1, []), ("Uncle Pete", 2, ["Family"]),
        ("Vera", 3, []), ("Walt", 1, []), ("Xenia", 2, []), ("Yan", 2, []),
    ];

    public async Task RunPeople()
    {
        form.TopMost = true; // nothing else on the desktop may cover the window while it is photographed
        checks.Note($"window brought to the front: {ScreenCapture.BringToFront(Window)}");

        checks.Step("1. The People window: people sorted into groups, two of them selected");

        var shared = Path.Combine(AppPaths.DataFolder, "people.json");
        var groups = Path.Combine(AppPaths.DataFolder, "people-groups.json");
        var people = PeopleLibrary.Load(folder, shared, groups);
        foreach (var (name, faces, _) in InventedPeople)
        {
            for (var face = 0; face < faces; face++)
            {
                var look = new float[3];
                look[Math.Abs(name.GetHashCode()) % 3] = 1;
                people.Label(name, new IndexedFace(new IndexedPhoto($"{name}-{face}.jpg", 1, 1), 0.1f + face * 0.15f, 0.2f, 0.1f, 0.1f, 0.9f, manual: false, look));
            }
        }

        foreach (var group in InventedPeople.SelectMany(p => p.Groups).Distinct())
            people.SetGroupMembers(group, InventedPeople.Where(p => p.Groups.Contains(group)).Select(p => p.Name), member: true);

        var type = typeof(PhotoLikerUI.MainForm).Assembly.GetType("PhotoLikerUI." + PeopleDialogType)!;
        var dialog = (Form)Activator.CreateInstance(type, people, true)!;
        form.BeginInvoke(() => dialog.ShowDialog(form)); // modal: it only returns when the window is closed
        checks.That(await Until(() => dialog.Visible), "the window opens");
        if (!dialog.Visible)
            return;

        var list = AllControls(dialog).OfType<ListView>().Single(l => l.Columns.Count == 3);
        checks.That(list.Items.Count == InventedPeople.Length, $"it lists {InventedPeople.Length} people ({list.Items.Count})");

        foreach (ListViewItem item in list.Items)
            item.Selected = item.Text is "Anna" or "Boris";
        await Settle(600);

        var delete = AllControls(dialog).OfType<Button>().Single(b => b.Text.StartsWith("Delete person"));
        checks.That(delete.Enabled, "Delete person... is on while people are selected");

        var ticks = AllControls(dialog).OfType<CheckedListBox>().Single();
        checks.That(ticks.Items.Count == 3, $"the three groups are listed ({string.Join(" | ", ticks.Items.Cast<object>())})");

        await DialogShot(dialog, "28-people-groups");
        dialog.Close();
        checks.That(await Until(() => OpenDialog(PeopleDialogType) is null), "the window closes");
    }
}
