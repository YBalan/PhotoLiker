using PhotoLiker.Search;

namespace PhotoLikerUI
{
    /// <summary>
    /// Asks for another name for a person and renames them (see <see cref="PeopleLibrary.RenamePerson"/>). A name that somebody else has would make
    /// two people one, so that is asked about first. Used by the People window and by the menu of a face.
    /// </summary>
    internal static class PersonRenamePrompt
    {
        /// <returns>The name the person has now; null if nothing was renamed (cancelled, or the name was not changed).</returns>
        public static string? Ask(IWin32Window owner, PeopleLibrary people, string person, bool dark)
        {
            string? renamed = null;
            TextPromptDialog? prompt = null;

            using var dialog = new TextPromptDialog(PeopleStrings.RenamePersonTitle, string.Format(PeopleStrings.RenamePersonPromptFormat, person), person, name =>
            {
                if (string.Equals(name, person, StringComparison.Ordinal))
                    return null; // the same name: nothing to do

                if (people.WouldMerge(person, name))
                {
                    people.TryFindName(name, out var existing);
                    var answer = MessageBox.Show((IWin32Window?)prompt ?? owner, string.Format(PeopleStrings.MergeQuestionFormat, person, existing),
                        PeopleStrings.MergeTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                    if (answer != DialogResult.Yes)
                        return string.Empty; // stays open, to try another name
                }

                try
                {
                    renamed = people.RenamePerson(person, name);
                    return null;
                }
                catch (ArgumentException ex)
                {
                    return ex.Message;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return string.Format(PeopleStrings.RenamePersonErrorFormat, ex.Message);
                }
            });
            prompt = dialog;
            ThemeManager.Apply(dialog, dark);

            return dialog.ShowDialog(owner) == DialogResult.OK ? renamed : null;
        }
    }
}
