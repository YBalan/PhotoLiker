using System.ComponentModel;

namespace PhotoLikerUI
{
    /// <summary>
    /// How <see cref="Settings"/> look in the settings grid. Core's class only holds the values, so the categories,
    /// read-only rows, converters and editors of the grid are added here, by property name, when the grid asks
    /// for the properties. <see cref="Register"/> has to run before the first settings object is shown.
    /// </summary>
    internal static class SettingsPresentation
    {
        private const string InfoCategory = "1. Progress";
        private const string CurrentFileCategory = "2. Current File";
        private const string CommonCategory = "3. Common Settings";
        private const string PhotoCategory = "4. Photo Settings";
        private const string VideoCategory = "5. Video Settings";
        private const string SlideshowCategory = "6. Slideshow";
        private const string WindowCategory = "10. Window";

        // Declared before the table that uses them: static fields are initialized in the order they are written
        private static readonly Attribute Hidden = new BrowsableAttribute(false);
        private static readonly Attribute ReadOnly = new ReadOnlyAttribute(true);

        private static readonly Dictionary<string, Attribute[]> Attributes = new()
        {
            [nameof(Settings.CurrentFolder)]  = [Category(CurrentFileCategory), ReadOnly, Describe(() => SettingsStrings.CurrentFolder)],
            // 3. Common Settings: what holds for the whole program, photos and videos alike
            [nameof(Settings.Language)]       = [Category(CommonCategory), new TypeConverterAttribute(typeof(LanguageConverter)), Describe(() => SettingsStrings.LanguageDescription)],
            [nameof(Settings.LikedFolder)]    = [Category(CommonCategory), Editor<FolderPickerEditor>(), Describe(() => SettingsStrings.LikedFolder)],
            [nameof(Settings.CacheSize)]      = [Category(CommonCategory), Describe(() => SettingsStrings.CacheSize)],
            [nameof(Settings.ThumbnailSize)]  = [Category(CommonCategory), Describe(() => SettingsStrings.ThumbnailSize)],
            [nameof(Settings.IsDarkTheme)]    = [Category(CommonCategory), Hidden],
            [nameof(Settings.SearchPrecision)] = [Category(CommonCategory), Hidden], // the slider of the search strip
            [nameof(Settings.SearchModel)]    = [Category(CommonCategory), Hidden], // the combo box at the right end of the search strip
            [nameof(Settings.SearchPhotos)]   = [Category(CommonCategory), Hidden], // the Photos and Videos toggles of the search strip
            [nameof(Settings.SearchVideos)]   = [Category(CommonCategory), Hidden],

            // 4. Photo Settings: which files are listed and in what order
            [nameof(Settings.GoThroughSubFolders)] = [Category(PhotoCategory), Describe(() => SettingsStrings.GoThroughSubFolders)],
            [nameof(Settings.GoThroughSubFoldersAlternateSpelling)] = [Hidden],
            [nameof(Settings.Extensions)]     = [Category(PhotoCategory), new TypeConverterAttribute(typeof(PhotoExtsConverter)), Describe(() => SettingsStrings.Extensions)],
            [nameof(Settings.SortOrder)]      = [Category(PhotoCategory), Describe(() => SettingsStrings.SortOrder)],

            // 5. Video Settings: the videos, and what is said in them
            [nameof(Settings.SupportVideo)]   = [Category(VideoCategory), Describe(() => SettingsStrings.SupportVideo)],
            [nameof(Settings.VideoExtensions)] = [Category(VideoCategory), new TypeConverterAttribute(typeof(PhotoExtsConverter)), Describe(() => SettingsStrings.VideoExtensions)],
            [nameof(Settings.VideoSpeech)]    = [Category(VideoCategory), Describe(() => SettingsStrings.VideoSpeech)],
            [nameof(Settings.SpeechModel)]    = [Category(VideoCategory), new TypeConverterAttribute(typeof(SpeechModelConverter)), Describe(() => SettingsStrings.SpeechModel)],
            [nameof(Settings.SpeechLanguage)] = [Category(VideoCategory), Describe(() => SettingsStrings.SpeechLanguage)],
            [nameof(Settings.ListedExtensions)] = [Hidden],

            // The rows say what they are for under the heading of their category: Mode, Monitors, Timeout, Watermark, Effect
            [nameof(Settings.SlideshowMode)]  = [Category(SlideshowCategory), new DisplayNameAttribute("Mode"), Describe(() => SettingsStrings.SlideshowMode)],
            [nameof(Settings.SlideshowMonitors)] = [Category(SlideshowCategory), new DisplayNameAttribute("Monitors"), Describe(() => SettingsStrings.SlideshowMonitors)],
            [nameof(Settings.SlideshowTimeout)] = [Category(SlideshowCategory), new DisplayNameAttribute("Timeout"), Describe(() => SettingsStrings.SlideshowTimeout)],
            [nameof(Settings.SlideshowWatermark)] = [Category(SlideshowCategory), new DisplayNameAttribute("Watermark"), Describe(() => SettingsStrings.SlideshowWatermark)],
            [nameof(Settings.SlideshowShowPeople)] = [Category(SlideshowCategory), new DisplayNameAttribute("ShowPeople"), Describe(() => SettingsStrings.SlideshowShowPeople)],
            [nameof(Settings.SlideshowEffect)] = [Category(SlideshowCategory), new DisplayNameAttribute("Effect"), Describe(() => SettingsStrings.SlideshowEffect)],
            [nameof(Settings.SlideshowUseVideo)] = [Category(SlideshowCategory), new DisplayNameAttribute("UseVideo"), Describe(() => SettingsStrings.SlideshowUseVideo)],
            [nameof(Settings.SlideshowMuteVideo)] = [Category(SlideshowCategory), new DisplayNameAttribute("MuteVideo"), Describe(() => SettingsStrings.SlideshowMuteVideo)],
            [nameof(Settings.SlideshowVideoPart)] = [Category(SlideshowCategory), new DisplayNameAttribute("VideoPart"), Describe(() => SettingsStrings.SlideshowVideoPart)],
            [nameof(Settings.SlideshowLowerOtherAudio)] = [Category(SlideshowCategory), new DisplayNameAttribute("LowerOtherAudio"), Describe(() => SettingsStrings.SlideshowLowerOtherAudio)],

            [nameof(Settings.WindowLeft)]     = [Category(WindowCategory), Hidden],
            [nameof(Settings.WindowTop)]      = [Category(WindowCategory), Hidden],
            [nameof(Settings.WindowWidth)]    = [Category(WindowCategory), Hidden],
            [nameof(Settings.WindowHeight)]   = [Category(WindowCategory), Hidden],
            [nameof(Settings.ScreenIndex)]    = [Category(WindowCategory), Hidden],
            [nameof(Settings.WindowState)]    = [Category(WindowCategory), Hidden],

            [nameof(Settings.Files)]          = [Category(InfoCategory), Hidden],
            [nameof(Settings.TotalFiles)]     = [Category(InfoCategory), Describe(() => SettingsStrings.TotalFiles)],
            [nameof(Settings.CurrentIndex)]   = [Category(InfoCategory), Describe(() => SettingsStrings.CurrentIndex)], // editable: typing a number jumps to that photo

            [nameof(Settings.CurrentFileName)] = [Category(CurrentFileCategory), ReadOnly, Describe(() => SettingsStrings.CurrentFileName)],
            [nameof(Settings.CurrentFilePath)] = [Category(CurrentFileCategory), ReadOnly, Describe(() => SettingsStrings.CurrentFilePath)],
            [nameof(Settings.LikedFile)]       = [Category(CurrentFileCategory), ReadOnly, Describe(() => SettingsStrings.LikedFile)],
            [nameof(Settings.SavedInFolders)]  = [Category(CurrentFileCategory), ReadOnly, new TypeConverterAttribute(typeof(StringListConverter)), Describe(() => SettingsStrings.SavedInFolders)],
        };

        private static Attribute Category(string name) => new CategoryAttribute(name);

        // The help text is asked for when a row is selected, not when the table is made, so it follows the language that is on then
        private static Attribute Describe(Func<string> text) => new LocalizedDescription(text);

        private sealed class LocalizedDescription(Func<string> text) : DescriptionAttribute
        {
            public override string Description => text();
        }

        private static Attribute Editor<T>() where T : System.Drawing.Design.UITypeEditor =>
            new EditorAttribute(typeof(T), typeof(System.Drawing.Design.UITypeEditor));

        public static void Register() =>
            TypeDescriptor.AddProvider(new Provider(), typeof(Settings));

        private sealed class Provider() : TypeDescriptionProvider(TypeDescriptor.GetProvider(typeof(Settings)))
        {
            public override ICustomTypeDescriptor? GetTypeDescriptor(Type objectType, object? instance)
            {
                var inner = base.GetTypeDescriptor(objectType, instance);
                return inner is null ? null : new Descriptor(inner);
            }
        }

        private sealed class Descriptor(ICustomTypeDescriptor inner) : CustomTypeDescriptor(inner)
        {
            public override PropertyDescriptorCollection GetProperties() =>
                Decorate(base.GetProperties(), attributes: null);

            // The inner descriptor filters by the attributes the properties have by default, so ask for all of
            // them and filter here, after the attributes above are on.
            public override PropertyDescriptorCollection GetProperties(Attribute[]? attributes) =>
                Decorate(base.GetProperties(), attributes);
        }

        private static PropertyDescriptorCollection Decorate(PropertyDescriptorCollection properties, Attribute[]? attributes)
        {
            var decorated = new List<PropertyDescriptor>(properties.Count);
            foreach (PropertyDescriptor property in properties)
            {
                var result = Attributes.TryGetValue(property.Name, out var added)
                    ? TypeDescriptor.CreateProperty(typeof(Settings), property, added)
                    : property;

                if (attributes is not { Length: > 0 } || Passes(result, attributes))
                    decorated.Add(result);
            }

            return new PropertyDescriptorCollection([.. decorated], readOnly: true);
        }

        /// <summary>
        /// The filter the grid asks for (e.g. only browsable properties), applied the way TypeDescriptor applies
        /// it: a property without the attribute counts as having its default.
        /// </summary>
        private static bool Passes(PropertyDescriptor property, Attribute[] filter)
        {
            foreach (var wanted in filter)
            {
                var own = property.Attributes[wanted.GetType()];
                var passes = own is null ? wanted.IsDefaultAttribute() : wanted.Match(own);
                if (!passes)
                    return false;
            }

            return true;
        }
    }
}
