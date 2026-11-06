using System.ComponentModel;
using PhotoLiker.Video;

namespace PhotoLikerUI
{
    /// <summary>The SpeechModel row of the settings grid: a drop-down of the speech models there are (tiny, base, small), the value is the id.</summary>
    internal sealed class SpeechModelConverter : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;

        public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => true;

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context) =>
            new(SpeechModels.All.Select(model => model.Id).ToArray());
    }
}
