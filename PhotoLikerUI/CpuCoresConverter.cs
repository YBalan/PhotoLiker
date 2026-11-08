using System.ComponentModel;

namespace PhotoLikerUI
{
    /// <summary>The CpuCores row of the settings grid: a drop-down of 1 up to the number of cores this computer has.</summary>
    internal sealed class CpuCoresConverter : Int32Converter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;

        public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => true;

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context) =>
            new(Enumerable.Range(1, Settings.MaxCpuCores).ToArray());
    }
}
