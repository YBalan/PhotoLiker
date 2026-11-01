using PhotoLiker.Search;

namespace PhotoLikerUI
{
    internal static class SearchStrings
    {
        // The search model (the combo box at the right end of the strip)
        public const string ModelLabel                   = "Model:";
        public const string ModelBoxTooltip              =
            "The model that matches words with photos. A model the photos of this folder have not been through yet needs them analyzed " +
            "('Update index'; the faces are kept, and so are the results of the other models: going back to a model costs nothing). " +
            "The choice is kept in the folder's settings file.";
        public const string ModelToDownloadFormat        = "Download: about {0}, once, when you index.";
        public const string ModelDownloadedFormat        = "Download: about {0}, already on this computer.";
        public const string IndexedForModelFormat        = "{0:N0} of {1:N0} photos for {2}";
        public const string IndexedForModelTooltipFormat =
            "{0:N0} of {1:N0} photos are analyzed for {2}; {3:N0} faces are known.\n" +
            "The others have not been through {2} yet: 'Update index' does them (the faces and the results of the other models are kept). " +
            "Until then a description or More like this only looks at the photos that are done; dates and people work on all of them.";
        public const string StatusModelEmptyFormat       = "Search model: {0}. It is used when this folder is indexed.{1}";
        public const string StatusModelReadyFormat       = "Search model: {0}. All {1:N0} photos are analyzed for it.";
        public const string StatusModelNoneFormat        =
            "Search model: {0}. The {1:N0} photos of this folder have not been analyzed for it yet: 'Update index' does that (the faces and the results of the other models are kept).{2}";
        public const string StatusModelPartFormat        =
            "Search model: {0}. {1:N0} of {2:N0} photos are analyzed for it: 'Update index' does the rest.{3}";
        public const string StatusModelDownloadNoteFormat = " Its files (about {0}) are downloaded then.";
        public const string StatusNothingForModelFormat  =
            "None of the photos of this folder is analyzed for {0} yet: 'Update index' does that (the faces are kept). Dates and people can be searched meanwhile.";
        public const string StatusModelMissingFormat     = "The search model {0} is not on this computer yet: indexing downloads it (about {1}).";
        public const string StatusOtherModelNoteFormat   = " {0:N0} more photos have not been analyzed for this model yet and were not searched: 'Update index' does them.";
        public const string StatusSimilarOtherModelFormat = "This photo has not been analyzed for {0} yet: 'Update index' does that.";

        /// <summary>The line of a model in the combo box: its name, what it is good for, who made it in brackets, and the languages it understands.</summary>
        public static string ModelName(ClipModelInfo model) => model.Id switch
        {
            "clip-vit-b32" => $"CLIP B/32 (fast) [{model.Vendor}] - {model.Languages}",
            "siglip2-b16" => $"SigLIP 2 B/16 (best) [{model.Vendor}] - {model.Languages}",
            "xlmr-vit-b32" => $"XLM-R B/32 (languages) [{model.Vendor}] - {model.Languages}",
            _ => $"{model.Title} [{model.Vendor}] - {model.Languages}",
        };

        /// <summary>What to know about a model before choosing it (the tooltip of the combo box).</summary>
        public static string ModelDescription(ClipModelInfo model) => model.Id switch
        {
            "clip-vit-b32" =>
                "CLIP B/32 (OpenAI): the fastest to index, and the smallest download. Understands English only.",
            "siglip2-b16" =>
                "SigLIP 2 B/16 (Google): finds the right photo most often, about 12 points better than CLIP B/32 in tests. English and Russian work well, " +
                "Ukrainian less well. About 4 times slower to index than CLIP B/32.",
            "xlmr-vit-b32" =>
                "XLM-R B/32 (LAION): as fast to index as CLIP B/32 and a little better in English. Understands about a hundred languages, " +
                "Ukrainian and Russian included.",
            _ => model.Title,
        };

        /// <summary>A size in megabytes the way it is said in a message: "610 MB", "1.5 GB".</summary>
        public static string Size(int megabytes) =>
            megabytes >= 1000 ? $"{megabytes / 1000.0:0.#} GB" : $"{Math.Max(10, (int)Math.Round(megabytes / 10.0) * 10)} MB";

        /// <summary>The question before models are downloaded: what is missing, for the model that is chosen.</summary>
        public static string DownloadQuestionFor(ClipModelInfo model, bool modelMissing, bool facesMissing, int totalMegabytes) =>
            $"To search photos, the AI models have to be downloaded once (about {Size(totalMegabytes)}):\n\n" +
            (modelMissing ? $"  - {model.Title}, for searching by description ({Size(model.Megabytes)}; Hugging Face)\n" : string.Empty) +
            (facesMissing ? $"  - InsightFace, for finding and recognizing faces ({Size(ModelFiles.FacePackMegabytes)}; GitHub; free for non-commercial use)\n" : string.Empty) +
            "\nEverything is analyzed on this computer, no photo leaves it. Download now and index the folder?";

        // Index button and progress
        public const string IndexButton                  = "Index photos";
        public const string UpdateIndexButton            = "Update index";
        public const string StopIndexButton              = "Stop indexing";
        public const string IndexTooltip                 = "Analyze the photos of this folder and its subfolders in the background, so that they can be searched. Safe to stop and continue later.";
        public const string StopIndexTooltip             = "Stop indexing. What is finished is kept, the next run continues from there.";
        public const string NotIndexed                   = "not indexed";
        public const string IndexedFormat                = "{0:N0} photos, {1:N0} faces";
        public const string IndexedWithFailuresFormat    = "{0:N0} photos, {1:N0} faces, {2:N0} unreadable";
        public const string IndexScanning                = "Looking for photos...";
        public const string IndexLoadingModels           = "Loading the models...";
        public const string IndexProgressFormat          = "{0:N0} / {1:N0}   {2:0.0}/s   {3} left";
        public const string IndexProgressNoEstimateFormat = "{0:N0} / {1:N0}";
        public const string IndexProgressTooltipFormat   = "{0:N0} of {1:N0} photos done, {2:N0} faces found so far";
        public const string IndexDatesProgressFormat     = "Reading dates: {0:N0} / {1:N0}   {2:0}/s   {3} left";
        public const string IndexDatesProgressNoEstimateFormat = "Reading dates: {0:N0} / {1:N0}";
        public const string StatusDatesRead              = "Read the dates of {0:N0} photos in {1}{2}.";
        public const string StatusIndexDone              = "Indexed {0:N0} photos with {1:N0} faces in {2}{3}.";
        public const string StatusIndexFailedPart        = " ({0:N0} could not be read)";
        public const string StatusIndexUpToDate          = "The index is up to date{0}.";
        public const string StatusIndexRemovedPart       = ", {0:N0} deleted photos forgotten";
        public const string StatusIndexStopped           = "Indexing stopped after {0:N0} photos. Start it again to continue.";
        public const string StatusIndexStopping          = "Stopping the indexing...";
        public const string StatusIndexError             = "Indexing failed: {0}";
        public const string StatusNoFolder               = "Open a folder of photos first.";

        public const string DownloadTitle                = "Download the search models";

        public const string ClosingWhileIndexing         = "The folder is still being indexed. Stop indexing and close PhotoLiker?\n\nWhat is finished is kept, the next run continues from there.";

        // Moving the index to another computer
        public const string IndexFileButton              = "I/E";
        public const string IndexFileTooltip             = "Import / Export the index: save it to a file to use it on another computer, or load an index that was made on another computer";
        public const string ExportIndexMenu              = "Export index...";
        public const string ImportIndexMenu              = "Import index...";
        public const string IndexFileFilter              = "PhotoLiker index (*.plindex)|*.plindex|All files (*.*)|*.*";
        public const string IndexFileExtension           = "plindex";
        public const string ExportIndexTitle             = "Export the index";
        public const string ImportIndexTitle             = "Import an index";
        public const string StatusExporting              = "Exporting the index...";
        public const string StatusExported               = "Index exported to {0}: {1:N0} photos, {2:N0} faces ({3:0.#} MB).";
        public const string StatusExportError            = "Export failed: {0}";
        public const string ImportQuestionFormat         = "Use this index for the folder\n{0}\n\nThe file holds {1:N0} photos with {2:N0} faces.{3}{4}";
        public const string ImportMadeForFormat          = "\nIt was made for {0}{1}.";
        public const string ImportReplaceNoteFormat      = "\n\nThe index this folder has now ({0:N0} photos) is replaced.";
        public const string StatusImported               = "Index imported: {0:N0} photos, {1:N0} faces. '" + UpdateIndexButton + "' brings it up to date with the photos.";
        public const string StatusImportError            = "Import failed: {0}";
        public const string StatusImportBusy             = "Stop indexing before importing an index.";

        // Search box and results
        public const string SearchPlaceholder            = "Search: beach, Mom, September 1, 2009...";
        public const string SearchButton                 = "Search";
        public const string SearchTooltip                =
            "Describe what you look for, or type the name of a person you have named.\n" +
            "A date limits the search to the photos taken then: September 1 (in every year), September 2009, 2009, 2009-2011.\n" +
            "Combine them: beach September 2009, Mom 2005, Mom playing guitar, Mom and Dad on the beach. Put the text in quotes to search for the words only.";
        public const string SimilarButton                = "More like this";
        public const string SimilarTooltip               = "Find photos that look like the one shown";
        public const string CloseResultsButton           = "Close results";
        public const string CloseResultsTooltip          = "Back to the folder (Esc)";
        public const string FacesButton                  = "Faces";
        public const string FacesTooltip                 = "Show the faces of the photo. Click a face to name it or to find the person; hold Shift and drag over a face the program missed.";
        public const string PrecisionWide                = "Wide";
        public const string PrecisionPrecise             = "Precise";
        public const string PrecisionTooltipFormat       =
            "How strict the search is: {0} of 100 (50 is the standard).\n" +
            "Left: more photos, looser matches. Right: fewer photos, closer matches. Moving it repeats the last search; the value is kept in the folder's settings file.\n" +
            "Now a description finds photos that score at least {1:0.00} and are within {2:0.00} of the best one, " +
            "More like this finds photos at least {3:0.00} alike, a person is a face at least {4:0.00} close.";
        public const string StatusSearching              = "Searching...";
        public const string StatusNothingFound           = "Nothing found for {0}.";
        public const string StatusSearchError            = "Search failed: {0}";
        public const string StatusSimilarNotIndexed      = "This photo is not in the index yet.";
        public const string ResultsTitleFormat           = "Search: {0} - {1:N0} photos";
        public const string ResultsTitleWeakFormat       = "Search: {0} - no strong match, the {1:N0} closest photos";
        public const string ResultsPartialNote           = " (indexing is not finished)";
        public const string StatusResultsShown           = "{0}: {1:N0} of {2:N0} indexed photos.";
        public const string StatusFileDatedNote          = " {0:N0} of them are dated by their file, because the camera's date is missing or wrong.";
        public const string StatusDatesPendingNote       = " The dates of {0:N0} photos have not been read yet: '" + UpdateIndexButton + "' reads them (it is quick).";

        // Faces
        public const string FaceWhoIsThis                = "Who is this?";
        public const string FaceLooksLikeFormat          = "Looks like {0}. Is it?";
        public const string FaceNewPerson                = "New person...";
        public const string FaceFindPhotos               = "Find photos of this person";
        public const string FaceFindPhotosOfFormat       = "Find photos of {0}";
        public const string FaceRemoveName               = "Remove the name";
        public const string FaceDelete                   = "This is not a face (delete)";
        public const string StatusFaceNamed              = "'{0}' saved.";
        public const string StatusFaceNameNotSaved       = "'{0}' is kept until PhotoLiker is closed only, the people file could not be written: {1}";
        public const string StatusFaceNameRemoved        = "Name removed.";
        public const string StatusFaceDeleted            = "Face deleted.";
        public const string StatusFaceTooSmall           = "Draw a bigger box around the face (Shift + drag).";
        public const string StatusFaceMarking            = "Looking at the face...";
        public const string StatusFaceMarkError          = "Could not mark the face: {0}";
        public const string StatusPeopleFileError        = "The people file could not be read, named faces are not available: {0}";
        public const string StatusSharedPeopleError      = "The people shared between folders could not be used: {0}";
        public const string StatusFaceNamedNotShared     = "'{0}' saved in this folder, but not shared with the other folders: {1}";

        public const string NamePersonTitle              = "Who is this?";
        public const string NamePersonPrompt             = "Name of the person:";
        public const string Ok                           = "OK";
        public const string Cancel                       = "Cancel";
    }
}
