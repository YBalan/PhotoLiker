using System.Runtime.CompilerServices;
namespace PhotoLikerUI
{
    /// <summary>Texts of the People and groups window and of the small windows that ask for a group.</summary>
    internal static class PeopleStrings
    {
        private static string T([CallerMemberName] string key = "") => Loc.Get(nameof(PeopleStrings), key);

        public static string Title => T();
        public static string Intro => T();

        public static string ShowLabel => T();
        public static string ShowAll => T();
        public static string ShowNoGroup => T();
        public static string FindLabel => T();
        public static string FindPlaceholder => T();

        public static string ColumnPerson => T();
        public static string ColumnFaces => T();
        public static string ColumnGroups => T();
        public static string GroupSeparator => T();

        public static string GroupsHeading => T();
        public static string GroupsTooltip => T();
        public static string GroupsHintNoSelection => T();
        public static string GroupsHintNoGroups => T();

        public static string NewGroupButton => T();
        public static string NewGroupTooltip => T();
        public static string RenamePersonButton => T();
        public static string RenamePersonTooltip => T();
        public static string DeletePersonButton => T();
        public static string DeletePersonTooltip => T();
        public static string RenameGroupButton => T();
        public static string DeleteGroupButton => T();
        public static string CloseButton => T();

        public static string NewGroupTitle => T();
        public static string NewGroupPrompt => T();
        public static string RenamePersonTitle => T();
        public static string RenamePersonPromptFormat => T();
        public static string RenamePersonErrorFormat => T();
        public static string DeletePersonTitle => T();
        public static string DeletePersonQuestionFormat => T();
        public static string DeletePeopleQuestionFormat => T();
        public static string DeletePersonErrorFormat => T();
        public static string DeletedFormat => T();
        public static string MergeTitle => T();
        public static string MergeQuestionFormat => T();
        public static string RenameGroupTitle => T();
        public static string RenameGroupPromptFormat => T();
        public static string DeleteGroupTitle => T();
        public static string DeleteGroupQuestionFormat => T();

        public static string SummaryFormat => T();
        public static string NoPeopleYet => T();
        public static string SaveErrorFormat => T();
        public static string FolderCopyErrorFormat => T();
        public static string NameProblemTitle => T();

        // Export and import of the people and the groups
        public static string ExportButton => T();
        public static string ExportTooltip => T();
        public static string ImportButton => T();
        public static string ImportTooltip => T();
        public static string ExportTitle => T();
        public static string ImportTitle => T();
        public static string PeopleFileFilter => T();
        public static string ExportDefaultNameFormat => T();
        public static string NothingToExport => T();
        public static string ExportedFormat => T();
        public static string ExportErrorFormat => T();
        public static string ImportErrorFormat => T();
        public static string ImportQuestionFormat => T();
        public static string ImportMadeFromFormat => T();
        public static string ImportOtherModelsNote => T();
        public static string ImportDoneFormat => T();
        public static string ImportKnownFormat => T();
        public static string ImportUnusableFormat => T();
        public static string ImportNothingNew => T();
    }
}
