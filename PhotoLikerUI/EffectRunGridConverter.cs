using System.ComponentModel;
using System.Globalization;

namespace PhotoLikerUI
{
    /// <summary>
    /// How the row EffectRun shows its number in the settings grid: the number, and the word <c>Random</c> where the setting leaves it to chance (<see cref="Slideshow.RandomEffectRun"/>). A text that is
    /// typed in the cell gives that number kept in the range (1 to 100; 0 or below is random), or <c>Random</c> (any case); a text that is none of these is refused with a message the grid shows. The
    /// drop-down offers <c>Random</c> and a few numbers, and any other number can still be typed.
    /// </summary>
    internal sealed class EffectRunGridConverter : TypeConverter
    {
        /// <summary>The numbers the drop-down offers, after <c>Random</c>.</summary>
        private static readonly int[] Offered = [1, 2, 3, 5, 10, 20, 50, 100];

        /// <summary>The text of a value in the grid.</summary>
        public static string Display(int value) =>
            Slideshow.EffectRunOf(value) is var kept && kept == Slideshow.RandomEffectRun
                ? nameof(SlideshowEffect.Random)
                : kept.ToString(CultureInfo.InvariantCulture);

        /// <summary>The value a text stands for; throws <see cref="FormatException"/> for one that is neither <c>Random</c> nor a whole number.</summary>
        public static int Parse(string? text)
        {
            var typed = text?.Trim() ?? string.Empty;
            if (string.Equals(typed, nameof(SlideshowEffect.Random), StringComparison.OrdinalIgnoreCase))
                return Slideshow.RandomEffectRun;

            if (!decimal.TryParse(typed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) &&
                !decimal.TryParse(typed, NumberStyles.Integer, CultureInfo.CurrentCulture, out number))
                throw new FormatException(string.Format(SlideshowStrings.EffectRunInvalidFormat, typed, Slideshow.MostEffectRun));

            return Slideshow.EffectRunOf((int)Math.Clamp(number, int.MinValue, int.MaxValue));
        }

        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) => destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
            value is string text ? Parse(text) : base.ConvertFrom(context, culture, value);

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) =>
            destinationType == typeof(string) && value is int number ? Display(number) : base.ConvertTo(context, culture, value, destinationType);

        public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;

        /// <summary>Not exclusive: any number from 1 to 100 can be typed as well as chosen.</summary>
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => false;

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context) =>
            new(new[] { Slideshow.RandomEffectRun }.Concat(Offered).ToArray());
    }
}
