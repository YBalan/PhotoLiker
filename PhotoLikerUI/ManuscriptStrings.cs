using System.Runtime.CompilerServices;

namespace PhotoLikerUI
{
    /// <summary>The texts of the Scan manuscripts window and its toolbar button. A name ending in Format is a format string; a name that begins with Hint is the tip of the control it is named after.</summary>
    internal static class ManuscriptStrings
    {
        private static string T([CallerMemberName] string key = "") => Loc.Get(nameof(ManuscriptStrings), key);

        // The button and the window
        public static string ButtonText => T();
        public static string ButtonTooltip => T();
        public static string DialogTitle => T();
        public static string StatusOpening => T();
        public static string StatusReady => T();
        public static string StatusErrorFormat => T();

        // The pages
        public static string PagesTitle => T();
        public static string AddPages => T();
        public static string AddPagesTip => T();
        public static string AddPagesTitle => T();
        public static string RemovePage => T();
        public static string RemovePageTip => T();
        public static string MovePageUpTip => T();
        public static string MovePageDownTip => T();
        public static string PageFormat => T();
        public static string StateNew => T();
        public static string StateScanned => T();
        public static string StateEdited => T();
        public static string PageCountFormat => T();
        public static string PageMissingFormat => T();
        public static string ConfirmRemovePageFormat => T();
        public static string FilterPhotos => T();
        public static string FilterAllFiles => T();
        public static string NoPages => T();

        // The kind of writing and the language
        public static string LabelKind => T();
        public static string KindHandwriting => T();
        public static string KindPrinted => T();
        public static string HintKind => T();
        public static string LabelLanguage => T();
        public static string LanguageUkrainian => T();
        public static string LanguageRussian => T();
        public static string LanguageEnglish => T();
        public static string LanguageMultilingual => T();
        public static string HintLanguage => T();

        // Scanning
        public static string ScanPage => T();
        public static string ScanPageTip => T();
        public static string ScanAll => T();
        public static string ScanAllTip => T();
        public static string RescanAll => T();
        public static string RescanAllTip => T();
        public static string ConfirmRescanAllFormat => T();
        public static string ConfirmRescanAllEditedFormat => T();
        public static string StatusRescanAllNothing => T();
        public static string Stop => T();
        public static string StopTip => T();
        public static string ShowLines => T();
        public static string ShowLinesTip => T();
        public static string JoinLines => T();
        public static string JoinLinesTip => T();
        public static string ConfirmReplaceText => T();
        public static string StatusPreparingModel => T();
        public static string StatusReadingFormat => T();
        public static string StatusReadingLineFormat => T();
        public static string StatusScannedFormat => T();
        public static string StatusScanNothing => T();
        public static string StatusScanFailedFormat => T();
        public static string StatusScanDeclined => T();
        public static string StatusScanStopped => T();
        public static string StatusScanAllDoneFormat => T();
        public static string StatusScanAllNothing => T();
        public static string StatusLinesJoinedFormat => T();
        public static string StatusNoLinesToJoin => T();

        // The photo
        public static string RotateLeft => T();
        public static string RotateRight => T();
        public static string RotateLeftTip => T();
        public static string RotateRightTip => T();
        public static string StatusRotatedFormat => T();
        public static string StatusRotateFailedFormat => T();
        public static string InfoFormat => T();
        public static string PhotoMissing => T();

        // The text
        public static string TextTitle => T();
        public static string TextHint => T();

        // The book
        public static string LabelTitle => T();
        public static string LabelAuthor => T();
        public static string IncludePhotos => T();
        public static string HintIncludePhotos => T();
        public static string CutPages => T();
        public static string HintCutPages => T();
        public static string StatusCuttingFormat => T();
        public static string StatusCutDeclined => T();
        public static string StatusBookMadeCutFormat => T();
        public static string SaveBook => T();
        public static string SaveBookTip => T();
        public static string SaveBookTitle => T();
        public static string FilterPdf => T();
        public static string FilterEpub => T();
        public static string Close => T();
        public static string StatusNoText => T();
        public static string StatusMakingBook => T();
        public static string StatusBookMadeFormat => T();
        public static string StatusBookFailedFormat => T();
        public static string StatusWorkSaveFailedFormat => T();

        // The pictures of the pages
        public static string FindPictures => T();
        public static string FindPicturesTip => T();
        public static string ScanPictures => T();
        public static string HintScanPictures => T();
        public static string StatusPagesCappedFormat => T();
        public static string StatusFindingPicturesFormat => T();
        public static string StatusPicturesFoundFormat => T();
        public static string StatusNoPicturesFormat => T();
        public static string StatusPicturesDeclined => T();
        public static string StatusPicturesFailedFormat => T();
        public static string StatusPreparingPictureModel => T();
        public static string StatusPicturesInTextFormat => T();
        public static string StatusLoadingPagesFormat => T();

        // Fixing the text with AI
        public static string FixText => T();
        public static string FixTextTip => T();
        public static string StatusFixPreparing => T();
        public static string StatusFixingFormat => T();
        public static string StatusFixedFormat => T();
        public static string StatusFixNothing => T();
        public static string StatusFixNoText => T();
        public static string StatusFixDeclined => T();
        public static string StatusFixStopped => T();
        public static string StatusFixFailedFormat => T();

        // Which AI does it: the model on this computer, or a service with a key
        public static string LabelAi => T();
        public static string HintAi => T();
        public static string AiLocal => T();
        public static string AiServices => T();
        public static string AiServicesTip => T();
        public static string StatusFixAskingFormat => T();

        // Where the pages are from, and the context of the pages before
        public static string HintPhotosFrom => T();
        public static string StatusSourceOpenedFormat => T();
        public static string UseContext => T();
        public static string HintUseContext => T();

        // Reading the photo with an AI service
        public static string KindAi => T();
        public static string StatusAiScanNoService => T();
        public static string ConfirmSendPhotoFormat => T();
        public static string StatusAiScanDeclined => T();
        public static string StatusAiReadingFormat => T();
        public static string StatusAiReadFormat => T();
        public static string StatusAiReadNothingFormat => T();
        public static string AiPlaces => T();
        public static string HintAiPlaces => T();
        public static string StatusAiReadPlacedFormat => T();
        public static string StatusAiReadNoPlacesFormat => T();
        public static string StatusAiNoPlacesFormat => T();
        public static string StatusAiPlacesNoFinder => T();
        public static string StatusAiScanAllDoneFormat => T();
        public static string StatusAiScanFailedFormat => T();
    }
}
