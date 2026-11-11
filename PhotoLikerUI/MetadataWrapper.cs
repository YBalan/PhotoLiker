namespace PhotoLikerUI
{
    using System.ComponentModel;
    using PhotoLiker.Video;

    /// <summary>
    /// Shows the metadata of a photo in a PropertyGrid: what <see cref="MetadataViewModel"/> holds, one row per entry. The rows are read-only,
    /// except the dates (see <see cref="DateRowEdit"/>) when the photo can be saved again, and the "Recorded" row of a video: a new date is handed to <c>editDate</c>.
    /// </summary>
    public class MetadataWrapper : ICustomTypeDescriptor
    {
        private readonly MetadataViewModel _metadata;
        private readonly Action<int, DateTime>? _editDate;

        public string? MapLink => _metadata.MapLink;

        public MetadataWrapper(IReadOnlyList<MetadataEntry> entries, Action<int, DateTime>? editDate = null)
            : this(new MetadataViewModel(entries), editDate) { }

        public MetadataWrapper(MetadataViewModel metadata, Action<int, DateTime>? editDate = null)
        {
            _metadata = metadata;
            _editDate = editDate;
        }

        public PropertyDescriptorCollection GetProperties()
        {
            var props = _metadata.Entries
                .Select(e =>
                {
                    var category = ExifTagMap.GetCategory(e);
                    var editable = _editDate is not null && (DateRowEdit.IsEditable(e.TagId) || e.TagId == VideoMetadata.RecordedTagId);
                    return new MetadataPropertyDescriptor(
                        e.Name, e.Value, category.Name, category.IsBrowsable, description: MetadataDescriptions.For(e),
                        extraAttributes: editable ? [new EditorAttribute(typeof(DateValueEditor), typeof(System.Drawing.Design.UITypeEditor))] : null,
                        edit: editable ? text => EditDate(e, text) : null);
                })
                .Cast<PropertyDescriptor>()
                .ToList();

            foreach (var e in _metadata.GpsDecoded)
                props.Add(new MetadataPropertyDescriptor(
                    e.Name, e.Value, ImageHelperStrings.CategoryGPSDecoded, extraAttributes: ToAttributes(e.ExtraAttributeTypes),
                    description: MetadataDescriptions.ForDecoded(e.Name)));

            props.Add(new MetadataPropertyDescriptor(
                ImageHelperStrings.AllMetadataProperty,
                _metadata.AllEntriesText,
                ImageHelperStrings.CategoryDebug,
                extraAttributes: [new EditorAttribute(typeof(MultilineTextViewEditor), typeof(System.Drawing.Design.UITypeEditor))],
                description: MetadataDescriptions.AllMetadata));

            return new PropertyDescriptorCollection([.. props]);
        }

        /// <summary>A date was typed into a date row (or picked): it goes on to be saved, or the grid says why it is not a date.</summary>
        private void EditDate(MetadataEntry entry, string? text)
        {
            if (!DateText.TryParseTyped(text, out var date))
                throw new FormatException(string.Format(ImageHelperStrings.NotADateFormat, entry.Name));
            _editDate!(entry.TagId, date);
        }

        /// <summary>What the grid does with the extras of an entry (see <see cref="MetadataExtras"/>).</summary>
        private static Attribute[]? ToAttributes(string[]? extras) =>
            extras is not null && extras.Contains(MetadataExtras.MapLink)
                ? [new EditorAttribute(typeof(UrlLauncherEditor), typeof(System.Drawing.Design.UITypeEditor))]
                : null;

        public PropertyDescriptorCollection GetProperties(Attribute?[]? attributes) => GetProperties();

        // Minimal ICustomTypeDescriptor implementation
        public AttributeCollection GetAttributes() => AttributeCollection.Empty;
        public string GetClassName() => null!;
        public string GetComponentName() => null!;
        public TypeConverter GetConverter() => null!;
        public EventDescriptor GetDefaultEvent() => null!;
        public PropertyDescriptor GetDefaultProperty() => null!;
        public object GetEditor(Type editorBaseType) => null!;
        public EventDescriptorCollection GetEvents(Attribute?[]? attributes) => EventDescriptorCollection.Empty;
        public EventDescriptorCollection GetEvents() => EventDescriptorCollection.Empty;
        public object GetPropertyOwner(PropertyDescriptor? pd) => this;
    }

    public class MetadataPropertyDescriptor : PropertyDescriptor
    {
        private readonly string _value;
        private readonly Action<string?>? _edit;

        public MetadataPropertyDescriptor(
            string name,
            string value,
            string category = ImageHelperStrings.CategoryExif,
            bool isBrowsable = true,
            Attribute[]? extraAttributes = null,
            string? description = null,
            Action<string?>? edit = null
            ) : base(name, BuildAttributes(category, isBrowsable, extraAttributes, description))
        {
            _value = value;
            _edit = edit;
        }

        // The description is what the help pane under the grid shows for the selected row
        private static Attribute[] BuildAttributes(string category, bool isBrowsable, Attribute[]? extra, string? description)
        {
            List<Attribute> attributes = [new CategoryAttribute(category), new BrowsableAttribute(isBrowsable)];
            if (!string.IsNullOrEmpty(description))
                attributes.Add(new DescriptionAttribute(description));
            if (extra is { Length: > 0 })
                attributes.AddRange(extra);
            return [.. attributes];
        }

        public override Type PropertyType => typeof(string);
        public override void SetValue(object? component, object? value) => _edit?.Invoke(value as string);
        public override object GetValue(object? component) => _value;
        public override bool IsReadOnly => _edit is null;
        public override Type ComponentType => typeof(object);
        public override bool CanResetValue(object? component) => false;
        public override void ResetValue(object? component) { }
        public override bool ShouldSerializeValue(object component) => false;
    }
}
