using System.Runtime.CompilerServices;
using PhotoLiker.Search;

namespace PhotoLikerUI
{
    internal static class SearchStrings
    {
        private static string T([CallerMemberName] string key = "") => Loc.Get(nameof(SearchStrings), key);

        // The search model (the combo box at the right end of the strip)
        public static string ModelLabel => T();
        public static string ModelBoxTooltip => T();
        public static string ModelToDownloadFormat => T();
        public static string ModelDownloadedFormat => T();
        public static string IndexedForModelFormat => T();
        public static string IndexedForModelTooltipFormat => T();
        public static string StatusModelEmptyFormat => T();
        public static string StatusModelReadyFormat => T();
        public static string StatusModelNoneFormat => T();
        public static string StatusModelPartFormat => T();
        public static string StatusModelDownloadNoteFormat => T();
        public static string StatusNothingForModelFormat => T();
        public static string StatusModelMissingFormat => T();
        public static string StatusOtherModelNoteFormat => T();
        public static string StatusSimilarOtherModelFormat => T();

        public static string SizeMegabytesFormat => T();
        public static string SizeGigabytesFormat => T();
        public static string DownloadQuestionIntroFormat => T();
        public static string DownloadQuestionModelFormat => T();
        public static string DownloadQuestionFacesFormat => T();
        public static string DownloadQuestionEnd => T();
        public static string DownloadQuestionEndNoIndexing => T();
        public static string StatusDownloadDeclined => T();
        public static string StatusModelDownloadedFormat => T();
        public static string DurationHoursFormat => T();
        public static string DurationMinutesFormat => T();
        public static string DurationSecondsFormat => T();

        // The three models the search library knows have texts of their own; a model it learns later shows its own title
        private static string ModelKey(ClipModelInfo model) => model.Id switch
        {
            "clip-vit-b32" => "Clip",
            "siglip2-b16" => "Siglip2",
            "xlmr-vit-b32" => "Xlmr",
            _ => string.Empty,
        };

        /// <summary>The line of a model in the combo box: its name, what it is good for, who made it in brackets, and the languages it understands.</summary>
        public static string ModelName(ClipModelInfo model) =>
            ModelKey(model) is { Length: > 0 } key
                ? string.Format(T($"ModelName{key}Format"), model.Vendor, T($"ModelLanguages{key}"))
                : $"{model.Title} [{model.Vendor}] - {model.Languages}";

        /// <summary>What to know about a model before choosing it (the tooltip of the combo box).</summary>
        public static string ModelDescription(ClipModelInfo model) =>
            ModelKey(model) is { Length: > 0 } key ? T($"ModelDescription{key}") : model.Title;

        /// <summary>A size in megabytes the way it is said in a message: "610 MB", "1.5 GB".</summary>
        public static string Size(int megabytes) =>
            megabytes >= 1000
                ? string.Format(SizeGigabytesFormat, megabytes / 1000.0)
                : string.Format(SizeMegabytesFormat, Math.Max(10, (int)Math.Round(megabytes / 10.0) * 10));

        /// <summary>The question before models are downloaded: what is missing and is going to be used.</summary>
        public static string DownloadQuestionFor(ModelDownload download) =>
            string.Format(DownloadQuestionIntroFormat, Size(download.Megabytes)) +
            (download.Clip is { } model ? string.Format(DownloadQuestionModelFormat, model.Title, Size(model.Megabytes)) : string.Empty) +
            (download.Faces ? string.Format(DownloadQuestionFacesFormat, Size(ModelFiles.FacePackMegabytes)) : string.Empty) +
            (download.WithIndexing ? DownloadQuestionEnd : DownloadQuestionEndNoIndexing);

        // Index button and progress
        public static string IndexButton => T();
        public static string UpdateIndexButton => T();
        public static string StopIndexButton => T();
        public static string IndexTooltip => T();
        public static string StopIndexTooltip => T();
        public static string NotIndexed => T();
        public static string IndexedFormat => T();
        public static string IndexedWithFailuresFormat => T();
        public static string IndexScanning => T();
        public static string IndexLoadingModels => T();
        public static string IndexProgressFormat => T();
        public static string IndexProgressNoEstimateFormat => T();
        public static string IndexProgressTooltipFormat => T();
        public static string IndexDatesProgressFormat => T();
        public static string IndexDatesProgressNoEstimateFormat => T();
        public static string StatusDatesRead => T();
        public static string StatusIndexDone => T();
        public static string StatusIndexFailedPart => T();
        public static string StatusIndexUpToDate => T();
        public static string StatusIndexRemovedPart => T();
        public static string StatusIndexStopped => T();
        public static string StatusIndexStopping => T();
        public static string StatusIndexError => T();
        public static string StatusNoFolder => T();

        public static string DownloadTitle => T();

        public static string ClosingWhileIndexing => T();

        // Moving the index to another computer
        public static string IndexFileButton => T();
        public static string IndexFileTooltip => T();
        public static string ExportIndexMenu => T();
        public static string ImportIndexMenu => T();
        public static string IndexFileFilter => T();
        public const string IndexFileExtension           = "plindex";
        public static string ExportIndexTitle => T();
        public static string ImportIndexTitle => T();
        public static string StatusExportingFormat => T();
        public static string StatusReadingIndexFormat => T();
        public static string StatusImportingFormat => T();
        public static string StatusExported => T();
        public static string StatusExportError => T();
        public static string ImportQuestionFormat => T();
        public static string ImportMadeForFormat => T();
        public static string ImportReplaceNoteFormat => T();
        public static string StatusImported => T();
        public static string StatusImportError => T();
        public static string StatusImportBusy => T();

        // Search box and results
        public static string SearchPlaceholder => T();
        public static string SearchButton => T();
        public static string SearchTooltip => T();
        public static string SimilarButton => T();
        public static string SimilarTooltip => T();
        public static string CloseResultsButton => T();
        public static string CloseResultsTooltip => T();
        public static string FacesButton => T();
        public static string FacesTooltip => T();
        public static string PeopleButton => T();
        public static string PeopleTooltip => T();
        public static string PrecisionWide => T();
        public static string PrecisionPrecise => T();
        public static string PrecisionTooltipFormat => T();
        public static string StatusSearching => T();
        public static string StatusNothingFound => T();
        public static string StatusSearchError => T();
        public static string StatusSimilarNotIndexed => T();
        public static string ResultsTitleFormat => T();
        public static string ResultsTitleWeakFormat => T();
        public static string ResultsPartialNote => T();
        public static string StatusResultsShown => T();
        public static string StatusFileDatedNote => T();
        public static string StatusDatesPendingNote => T();

        // Faces
        public static string FaceWhoIsThis => T();
        public static string FaceLooksLikeFormat => T();
        public static string FaceNewPerson => T();
        public static string FaceFindPhotos => T();
        public static string FaceFindPhotosOfFormat => T();
        public static string FaceRenameFormat => T();
        public static string StatusPersonRenamedFormat => T();
        public static string FaceRemoveName => T();
        public static string FaceDelete => T();
        public static string StatusFaceNamed => T();
        public static string StatusFaceNameNotSaved => T();
        public static string StatusFaceNameRemoved => T();
        public static string StatusFaceDeleted => T();
        public static string StatusFaceTooSmall => T();
        public static string StatusFaceMarking => T();
        public static string StatusFaceMarkError => T();
        public static string StatusPeopleFileError => T();
        public static string StatusSharedPeopleError => T();
        public static string StatusGroupsFileError => T();
        public static string StatusGroupsFolderCopyError => T();
        public static string StatusFaceNamedInGroupFormat => T();
        public static string StatusGroupNotSavedFormat => T();
        public static string StatusFaceNamedNotShared => T();

        public static string NamePersonTitle => T();
        public static string NamePersonPrompt => T();
        public static string NamePersonGroupPrompt => T();
        public static string Ok => T();
        public static string Cancel => T();
    }
}
