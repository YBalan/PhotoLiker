namespace PhotoLikerUI
{
    internal static class SearchStrings
    {
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
        public const string StatusIndexDone              = "Indexed {0:N0} photos with {1:N0} faces in {2}{3}.";
        public const string StatusIndexFailedPart        = " ({0:N0} could not be read)";
        public const string StatusIndexUpToDate          = "The index is up to date{0}.";
        public const string StatusIndexRemovedPart       = ", {0:N0} deleted photos forgotten";
        public const string StatusIndexStopped           = "Indexing stopped after {0:N0} photos. Start it again to continue.";
        public const string StatusIndexStopping          = "Stopping the indexing...";
        public const string StatusIndexError             = "Indexing failed: {0}";
        public const string StatusNoFolder               = "Open a folder of photos first.";

        public const string DownloadTitle                = "Download the search models";
        public const string DownloadQuestion             =
            "To search photos, the AI models have to be downloaded once (about 650 MB):\n\n" +
            "  - CLIP, for searching by description (Hugging Face)\n" +
            "  - InsightFace, for finding and recognizing faces (GitHub; free for non-commercial use)\n\n" +
            "Everything is analyzed on this computer, no photo leaves it. Download now and index the folder?";

        public const string ClosingWhileIndexing         = "The folder is still being indexed. Stop indexing and close PhotoLiker?\n\nWhat is finished is kept, the next run continues from there.";

        // Search box and results
        public const string SearchPlaceholder            = "Search: beach, birthday cake, a person's name...";
        public const string SearchButton                 = "Search";
        public const string SearchTooltip                = "Describe what you look for, or type the name of a person you have named";
        public const string SimilarButton                = "More like this";
        public const string SimilarTooltip               = "Find photos that look like the one shown";
        public const string CloseResultsButton           = "Close results";
        public const string CloseResultsTooltip          = "Back to the folder (Esc)";
        public const string FacesButton                  = "Faces";
        public const string FacesTooltip                 = "Show the faces of the photo. Click a face to name it or to find the person; hold Shift and drag over a face the program missed.";
        public const string StatusSearching              = "Searching...";
        public const string StatusNothingFound           = "Nothing found for {0}.";
        public const string StatusSearchError            = "Search failed: {0}";
        public const string StatusModelMissing           = "The search model is not on this computer yet: use '" + IndexButton + "' once to download it.";
        public const string StatusSimilarNotIndexed      = "This photo is not in the index yet.";
        public const string ResultsTitleFormat           = "Search: {0} - {1:N0} photos";
        public const string ResultsTitleWeakFormat       = "Search: {0} - no strong match, the {1:N0} closest photos";
        public const string ResultsPartialNote           = " (indexing is not finished)";
        public const string StatusResultsShown           = "{0}: {1:N0} of {2:N0} indexed photos.";

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

        public const string NamePersonTitle              = "Who is this?";
        public const string NamePersonPrompt             = "Name of the person:";
        public const string Ok                           = "OK";
        public const string Cancel                       = "Cancel";
    }
}
