using System.ComponentModel;
using System.Globalization;

namespace PhotoLikerUI
{
    /// <summary>
    /// The Language row of the settings grid: a list of the languages that are there (English, and one for every language file found, see
    /// <see cref="AppLanguage"/>), each named in itself ("English", "Українська"). The value of the setting is the language code ("en", "ua");
    /// the grid shows and takes the name. The files are looked at each time the list is asked for, so a file that was just added is in it.
    /// </summary>
    internal sealed class LanguageConverter : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;

        public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => true;

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context) =>
            new(AppLanguage.Available().Select(language => language.Code).ToArray());

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) =>
            destinationType == typeof(string) && value is string code ? AppLanguage.Find(code).NativeName : base.ConvertTo(context, culture, value, destinationType);

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
            value is string text
                ? AppLanguage.Available().FirstOrDefault(language => string.Equals(language.NativeName, text, StringComparison.CurrentCultureIgnoreCase))?.Code
                    ?? AppLanguage.Find(text).Code
                : base.ConvertFrom(context, culture, value);
    }
}
