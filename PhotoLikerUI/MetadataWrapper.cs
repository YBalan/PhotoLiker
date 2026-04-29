namespace PhotoLikerUI
{
    using System.ComponentModel;

    public class MetadataWrapper : ICustomTypeDescriptor
    {
        private readonly IReadOnlyList<MetadataEntry> _entries;
        private readonly List<MetadataEntry> _gpsDecoded;

        public IReadOnlyCollection<MetadataEntry> GPSDecoded => _gpsDecoded;
        public string? MapLink => _gpsDecoded?.FirstOrDefault(e => e.Name == ImageHelperStrings.GpsMapLink)?.Value;

        public MetadataWrapper(FriendlyImageMetadata meta)
            : this(meta.Entries) { }

        public MetadataWrapper(IReadOnlyList<MetadataEntry> entries)
        {
            _entries    = entries;
            _gpsDecoded = GpsDecoder.Decode(entries);
        }

        public PropertyDescriptorCollection GetProperties()
        {
            var props = _entries
                .Select(e => new MetadataPropertyDescriptor(
                    e.Name,
                    e.Value,
                    e.Name.StartsWith(ImageHelperStrings.UnknownPrefix)
                        ? ImageHelperStrings.CategoryUnknown
                        : ExifTagMap.GetCategory(e.TagId)))
                .Cast<PropertyDescriptor>()
                .ToList();

            foreach (var e in _gpsDecoded)
                props.Add(new MetadataPropertyDescriptor(
                    e.Name, e.Value, ImageHelperStrings.CategoryGPSDecoded, e.ExtraAttributes));

            var allText = string.Join(
                ImageHelperStrings.AllMetadataSeparator,
                _entries.Select(e => string.Format(ImageHelperStrings.AllMetadataLineFormat, e.Name, e.Value)));
            props.Add(new MetadataPropertyDescriptor(
                ImageHelperStrings.AllMetadataProperty,
                allText,
                ImageHelperStrings.CategoryDebug,
                [new EditorAttribute(typeof(MultilineTextViewEditor), typeof(System.Drawing.Design.UITypeEditor))]));

            return new PropertyDescriptorCollection([.. props]);
        }

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
            Attribute[]? extraAttributes = null)
            : base(name, BuildAttributes(category, extraAttributes))
        {
            _value = value;
        }

        private static Attribute[] BuildAttributes(string category, Attribute[]? extra)
        {
            Attribute[] baseAttrs = [new CategoryAttribute(category)];
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
