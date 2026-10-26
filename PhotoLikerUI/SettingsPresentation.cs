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
        private const string SettingsCategory = "3. Settings";
        private const string WindowCategory = "10. Window";

        // Declared before the table that uses them: static fields are initialized in the order they are written
        private static readonly Attribute Hidden = new BrowsableAttribute(false);
        private static readonly Attribute ReadOnly = new ReadOnlyAttribute(true);

        private static readonly Dictionary<string, Attribute[]> Attributes = new()
        {
            [nameof(Settings.CurrentFolder)]  = [Category(CurrentFileCategory), ReadOnly],
            [nameof(Settings.LikedFolder)]    = [Category(SettingsCategory), Editor<FolderPickerEditor>()],
            [nameof(Settings.GoThroughSubFolders)] = [Category(SettingsCategory)],
            [nameof(Settings.GoThroughSubFoldersAlternateSpelling)] = [Hidden],
            [nameof(Settings.Extensions)]     = [Category(SettingsCategory), new TypeConverterAttribute(typeof(PhotoExtsConverter))],
            [nameof(Settings.CacheSize)]      = [Category(SettingsCategory)],
            [nameof(Settings.ThumbnailSize)]  = [Category(SettingsCategory)],
            [nameof(Settings.IsDarkTheme)]    = [Category(SettingsCategory), Hidden],

            [nameof(Settings.WindowLeft)]     = [Category(WindowCategory), Hidden],
            [nameof(Settings.WindowTop)]      = [Category(WindowCategory), Hidden],
            [nameof(Settings.WindowWidth)]    = [Category(WindowCategory), Hidden],
            [nameof(Settings.WindowHeight)]   = [Category(WindowCategory), Hidden],
            [nameof(Settings.ScreenIndex)]    = [Category(WindowCategory), Hidden],
            [nameof(Settings.WindowState)]    = [Category(WindowCategory), Hidden],

            [nameof(Settings.Files)]          = [Category(InfoCategory), Hidden],
            [nameof(Settings.TotalFiles)]     = [Category(InfoCategory)],
            [nameof(Settings.CurrentIndex)]   = [Category(InfoCategory), ReadOnly],

            [nameof(Settings.CurrentFileName)] = [Category(CurrentFileCategory), ReadOnly],
            [nameof(Settings.CurrentFilePath)] = [Category(CurrentFileCategory), ReadOnly],
            [nameof(Settings.LikedFile)]       = [Category(CurrentFileCategory), ReadOnly],
            [nameof(Settings.SavedInFolders)]  = [Category(CurrentFileCategory), ReadOnly, new TypeConverterAttribute(typeof(StringListConverter))],
        };

        private static Attribute Category(string name) => new CategoryAttribute(name);

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
