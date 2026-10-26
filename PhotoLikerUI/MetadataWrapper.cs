namespace PhotoLikerUI
{
    using System.ComponentModel;

    /// <summary>Shows the metadata of a photo in a PropertyGrid: what <see cref="MetadataViewModel"/> holds, one read-only row per entry.</summary>
    public class MetadataWrapper : ICustomTypeDescriptor
    {
        private readonly MetadataViewModel _metadata;

        public string? MapLink => _metadata.MapLink;

        public MetadataWrapper(IReadOnlyList<MetadataEntry> entries)
            : this(new MetadataViewModel(entries)) { }

        public MetadataWrapper(MetadataViewModel metadata)
        {
            _metadata = metadata;
        }

        public PropertyDescriptorCollection GetProperties()
        {
            var props = _metadata.Entries
                .Select(e =>
                {
                    var category = ExifTagMap.GetCategory(e);
                    return new MetadataPropertyDescriptor(e.Name, e.Value, category.Name, category.IsBrowsable);
                })
                .Cast<PropertyDescriptor>()
                .ToList();

            foreach (var e in _metadata.GpsDecoded)
                props.Add(new MetadataPropertyDescriptor(
                    e.Name, e.Value, ImageHelperStrings.CategoryGPSDecoded, extraAttributes: ToAttributes(e.ExtraAttributeTypes)));

            props.Add(new MetadataPropertyDescriptor(
                ImageHelperStrings.AllMetadataProperty,
                _metadata.AllEntriesText,
                ImageHelperStrings.CategoryDebug,
                extraAttributes: [new EditorAttribute(typeof(MultilineTextViewEditor), typeof(System.Drawing.Design.UITypeEditor))]));

            return new PropertyDescriptorCollection([.. props]);
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

        public MetadataPropertyDescriptor(
            string name,
            string value,
            string category = ImageHelperStrings.CategoryExif,
            bool isBrowsable = true,
            Attribute[]? extraAttributes = null
            ) : base(name, BuildAttributes(category, isBrowsable, extraAttributes))
        {
            _value = value;
        }

        private static Attribute[] BuildAttributes(string category, bool isBrowsable, Attribute[]? extra)
        {
            Attribute[] baseAttrs = [new CategoryAttribute(category), new BrowsableAttribute(isBrowsable)];
            return extra is { Length: > 0 } ? [.. baseAttrs, .. extra] : baseAttrs;
        }

        public override Type PropertyType => typeof(string);
        public override void SetValue(object? component, object? value) { }
        public override object GetValue(object? component) => _value;
        public override bool IsReadOnly => true;
        public override Type ComponentType => typeof(object);
        public override bool CanResetValue(object? component) => false;
        public override void ResetValue(object? component) { }
        public override bool ShouldSerializeValue(object component) => false;
    }
}
