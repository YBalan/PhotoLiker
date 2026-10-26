using System.Text;
using PhotoLiker.Core;

namespace PhotoLiker.Tests;

public class ExifValueFormatterTests
{
    // EXIF type codes, as GDI+ reports them
    private const int Ascii = 2, Short = 3, Long = 4, Rational = 5, SRational = 10, Undefined = 7;

    private static byte[] Rationals(params (int num, int den)[] values)
    {
        var bytes = new byte[values.Length * 8];
        for (var i = 0; i < values.Length; i++)
        {
            BitConverter.GetBytes(values[i].num).CopyTo(bytes, i * 8);
            BitConverter.GetBytes(values[i].den).CopyTo(bytes, i * 8 + 4);
        }
        return bytes;
    }

    [Fact]
    public void Ascii_IsTextWithoutTheTerminator()
    {
        Assert.Equal("Canon", ExifValueFormatter.Format(Ascii, Encoding.ASCII.GetBytes("Canon\0")));
    }

    [Fact]
    public void Short_IsReadAsUnsigned16Bit()
    {
        Assert.Equal("65535", ExifValueFormatter.Format(Short, BitConverter.GetBytes((ushort)65535)));
    }

    [Fact]
    public void Long_IsReadAsUnsigned32Bit()
    {
        Assert.Equal("4000000000", ExifValueFormatter.Format(Long, BitConverter.GetBytes(4_000_000_000u)));
    }

    [Fact]
    public void Rational_ShowsFractionAndValue()
    {
        var text = ExifValueFormatter.Format(Rational, Rationals((1, 250)));

        Assert.StartsWith("1/250 (", text);
        Assert.Contains(0.004.ToString("0.###"), text);
    }

    [Fact]
    public void Rational_WithZeroDenominator_IsInfinity()
    {
        Assert.Equal(ImageHelperStrings.Infinity, ExifValueFormatter.Format(Rational, Rationals((5, 0))));
    }

    [Fact]
    public void SignedRational_KeepsTheSign()
    {
        var text = ExifValueFormatter.Format(SRational, Rationals((-2, 3)));

        Assert.StartsWith("-2/3 (", text);
    }

    [Fact]
    public void SignedRational_WithZeroDenominator_IsInfinity()
    {
        Assert.Equal(ImageHelperStrings.Infinity, ExifValueFormatter.Format(SRational, Rationals((-1, 0))));
    }

    [Fact]
    public void OtherTypes_AreShownAsHexBytes()
    {
        Assert.Equal("01-AB-FF", ExifValueFormatter.Format(Undefined, [0x01, 0xAB, 0xFF]));
    }

    [Fact]
    public void ValueTooShortForItsType_FallsBackToHexBytes()
    {
        // A "Long" with only two bytes would throw when read as one
        Assert.Equal("01-02", ExifValueFormatter.Format(Long, [0x01, 0x02]));
    }

    [Fact]
    public void NoValue_IsEmpty()
    {
        Assert.Equal(string.Empty, ExifValueFormatter.Format(Undefined, null));
        Assert.Equal(string.Empty, ExifValueFormatter.Format(Ascii, null));
    }
}
