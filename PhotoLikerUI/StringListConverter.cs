using System.ComponentModel;
using System.Globalization;

namespace PhotoLikerUI
{
    /// <summary>
    /// Shows a read-only string[] in the PropertyGrid as one line ("[Liked]; [3 - Group]") that expands into one
    /// read-only row per item. (The stock array converter shows "String[] Array" and lets the items be edited.)
    /// </summary>
    internal sealed class StringListConverter : ArrayConverter
    {
        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        {
            if (destinationType == typeof(string) && value is string[] items)
                return string.Join("; ", items.Select(item => $"[{item}]"));
            return base.ConvertTo(context, culture, value, destinationType);
        }

        public override PropertyDescriptorCollection? GetProperties(ITypeDescriptorContext? context, object? value, Attribute[]? attributes)
        {
            if (value is not string[] items)
                return base.GetProperties(context, value, attributes);

            var rows = new PropertyDescriptor[items.Length];
            for (var i = 0; i < rows.Length; i++)
                rows[i] = new ItemDescriptor(i);
            return new PropertyDescriptorCollection(rows);
        }

        private sealed class ItemDescriptor(int index) : PropertyDescriptor($"[{index}]", null)
        {
            public override Type ComponentType => typeof(string[]);
            public override Type PropertyType => typeof(string);
            public override bool IsReadOnly => true;

            public override object? GetValue(object? component) =>
                component is string[] items && index < items.Length ? items[index] : null;

            public override bool CanResetValue(object component) => false;
            public override void ResetValue(object component) { }
            public override void SetValue(object? component, object? value) { }
            public override bool ShouldSerializeValue(object component) => false;
        }
    }
}
