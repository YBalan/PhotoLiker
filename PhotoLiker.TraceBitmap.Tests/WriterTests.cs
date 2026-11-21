using System.Globalization;
using System.Xml.Linq;

namespace PhotoLiker.TraceBitmap.Tests;

public class SvgWriterTests
{
    private static TraceResult Square()
    {
        var path = new TracePath(new TracePoint(10, 10), [TraceSegment.Line(new TracePoint(30, 10)), TraceSegment.Line(new TracePoint(30, 30)), TraceSegment.Line(new TracePoint(10, 30)), TraceSegment.Line(new TracePoint(10, 10))]);
        return new TraceResult(40, 40, [new TraceLayer(new TraceColor(200, 30, 10), [path])]);
    }

    private static XElement Parse(string svg) => XDocument.Parse(svg).Root!;

    [Fact]
    public void TheSize_IsInMillimetres_AndTheViewBoxIsTheSameNumbers()
    {
        var svg = Parse(SvgWriter.Write(Square(), 100, 50));

        Assert.Equal("100mm", (string?)svg.Attribute("width"));
        Assert.Equal("50mm", (string?)svg.Attribute("height"));
        Assert.Equal("0 0 100 50", (string?)svg.Attribute("viewBox"));
        Assert.Equal("http://www.w3.org/2000/svg", svg.Name.NamespaceName);
    }

    [Fact]
    public void ThePath_IsScaledToTheSize_FilledEvenOdd_InTheColourOfTheLayer()
    {
        var svg = Parse(SvgWriter.Write(Square(), 80, 80)); // twice the pixels in both directions

        var path = Assert.Single(svg.Descendants(), e => e.Name.LocalName == "path");
        Assert.Equal("#c81e0a", (string?)path.Attribute("fill"));
        Assert.Equal("evenodd", (string?)path.Attribute("fill-rule"));
        Assert.Equal("M20 20L60 20 60 60 20 60 20 20Z", (string?)path.Attribute("d"));
    }

    [Fact]
    public void TheWidthAndTheHeight_ScaleSeparately()
    {
        var svg = Parse(SvgWriter.Write(Square(), 40, 100)); // the same width, the height two and a half times as big

        var path = svg.Descendants().Single(e => e.Name.LocalName == "path");
        Assert.Equal("M10 25L30 25 30 75 10 75 10 25Z", (string?)path.Attribute("d"));
    }

    [Fact]
    public void ACurve_IsWrittenWithItsTwoControlPoints()
    {
        var path = new TracePath(new TracePoint(0, 0), [TraceSegment.Curve(new TracePoint(5, 0), new TracePoint(10, 5), new TracePoint(10, 10)), TraceSegment.Line(new TracePoint(0, 0))]);
        var trace = new TraceResult(10, 10, [new TraceLayer(TraceColor.Black, [path])]);

        var d = (string?)Parse(SvgWriter.Write(trace, 10, 10)).Descendants().Single(e => e.Name.LocalName == "path").Attribute("d");

        Assert.Equal("M0 0C5 0 10 5 10 10L0 0Z", d);
    }

    [Fact]
    public void Numbers_UseAPoint_WhateverTheLanguageOfTheComputer()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("uk-UA");

            var svg = SvgWriter.Write(Square(), 33.333, 16.6667);

            Assert.Contains("width=\"33.333mm\"", svg);
            Assert.Contains("height=\"16.667mm\"", svg);
            Assert.DoesNotContain(",", Parse(svg).Descendants().Single(e => e.Name.LocalName == "path").Attribute("d")!.Value);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void ALayerWithNoPaths_IsLeftOut_AndLayersKeepTheirOrder()
    {
        var path = Square().Layers[0].Paths[0];
        var trace = new TraceResult(40, 40, [new TraceLayer(new TraceColor(1, 2, 3), [path]), new TraceLayer(TraceColor.Black, []), new TraceLayer(new TraceColor(4, 5, 6), [path])]);

        var paths = Parse(SvgWriter.Write(trace, 40, 40)).Descendants().Where(e => e.Name.LocalName == "path").ToList();

        Assert.Equal(["#010203", "#040506"], paths.Select(p => (string?)p.Attribute("fill")));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(-1, 10)]
    [InlineData(double.NaN, 10)]
    [InlineData(10, double.PositiveInfinity)]
    public void ASizeThatIsNothing_IsRefused(double width, double height) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SvgWriter.Write(Square(), width, height));

    [Fact]
    public void ARealTrace_IsAValidSvg_WithAPathForEachLayer()
    {
        var trace = BitmapTracer.Trace(Shapes.Ring(100, 40, 20), new TraceSettings());

        var svg = Parse(SvgWriter.Write(trace, 100, 100));

        var path = Assert.Single(svg.Descendants(), e => e.Name.LocalName == "path");
        Assert.Equal(2, ((string?)path.Attribute("d"))!.Count(c => c == 'M')); // the ring and its hole
    }
}

public class DxfWriterTests
{
    /// <summary>The DXF as (group code, value) pairs.</summary>
    private static List<(int Code, string Value)> Pairs(string dxf)
    {
        var lines = dxf.Split("\r\n");
        var pairs = new List<(int, string)>();
        for (var i = 0; i + 1 < lines.Length; i += 2)
            pairs.Add((int.Parse(lines[i].Trim(), CultureInfo.InvariantCulture), lines[i + 1]));
        return pairs;
    }

    private static TraceResult Square()
    {
        var path = new TracePath(new TracePoint(10, 10), [TraceSegment.Line(new TracePoint(30, 10)), TraceSegment.Line(new TracePoint(30, 30)), TraceSegment.Line(new TracePoint(10, 30)), TraceSegment.Line(new TracePoint(10, 10))]);
        return new TraceResult(40, 40, [new TraceLayer(TraceColor.Black, [path])]);
    }

    private static List<List<(double X, double Y)>> Polylines(string dxf)
    {
        var result = new List<List<(double, double)>>();
        var pairs = Pairs(dxf);
        for (var i = 0; i < pairs.Count; i++)
        {
            if (pairs[i] is (0, "POLYLINE"))
                result.Add([]);
            else if (pairs[i] is (0, "VERTEX"))
            {
                var x = double.Parse(pairs.Skip(i).First(p => p.Code == 10).Value, CultureInfo.InvariantCulture);
                var y = double.Parse(pairs.Skip(i).First(p => p.Code == 20).Value, CultureInfo.InvariantCulture);
                result[^1].Add((x, y));
            }
        }

        return result;
    }

    [Fact]
    public void TheFile_IsAnR12Drawing_InMillimetres_ThatEndsProperly()
    {
        var pairs = Pairs(DxfWriter.Write(Square(), 80, 80));

        Assert.Contains((1, "AC1009"), pairs);
        Assert.Contains((9, "$INSUNITS"), pairs);
        Assert.Equal((0, "EOF"), pairs[^1]);
        Assert.Equal(pairs.Count(p => p == (0, "SECTION")), pairs.Count(p => p == (0, "ENDSEC")));
        Assert.Contains((0, "SEQEND"), pairs);
    }

    [Fact]
    public void ThePolyline_IsClosed_WithTheCornersInMillimetres_TurnedOver()
    {
        var dxf = DxfWriter.Write(Square(), 80, 80); // twice the pixels, in a drawing 80 mm high

        var pairs = Pairs(dxf);
        var polyline = Assert.Single(Polylines(dxf));
        Assert.Contains((70, "1"), pairs); // closed
        Assert.Equal([(20.0, 60.0), (60.0, 60.0), (60.0, 20.0), (20.0, 20.0)], polyline);
    }

    [Fact]
    public void TheFirstCornerOfTheStart_IsNotRepeatedAtTheEnd()
    {
        var polyline = Assert.Single(Polylines(DxfWriter.Write(Square(), 40, 40)));

        Assert.Equal(4, polyline.Count);
    }

    [Fact]
    public void YIsTurnedOver_SoTheTopOfThePictureIsTheTopOfTheDrawing()
    {
        // Pixel row 10 of 20 is half way down; in a drawing 100 mm high that is 50 mm up from the bottom edge. Row 0 is the top: 100 mm up.
        var path = new TracePath(new TracePoint(0, 0), [TraceSegment.Line(new TracePoint(10, 0)), TraceSegment.Line(new TracePoint(10, 10)), TraceSegment.Line(new TracePoint(0, 0))]);
        var trace = new TraceResult(20, 20, [new TraceLayer(TraceColor.Black, [path])]);

        var polyline = Assert.Single(Polylines(DxfWriter.Write(trace, 100, 100)));

        Assert.Equal([(0.0, 100.0), (50.0, 100.0), (50.0, 50.0)], polyline);
    }

    [Fact]
    public void ACurve_IsCutIntoPieces_ThatStayWithinTheAccuracy()
    {
        // A quarter circle of a radius of 100 pixels as one Bezier (the usual four thirds handles)
        var k = 0.5522847498 * 100;
        var path = new TracePath(new TracePoint(100, 0), [TraceSegment.Curve(new TracePoint(100, k), new TracePoint(k, 100), new TracePoint(0, 100)), TraceSegment.Line(new TracePoint(0, 0)), TraceSegment.Line(new TracePoint(100, 0))]);
        var trace = new TraceResult(100, 100, [new TraceLayer(TraceColor.Black, [path])]);

        foreach (var accuracy in new[] { 0.5, 0.05 })
        {
            var points = DxfWriter.Flatten(path, 1, 1, accuracy);

            // Every corner is on the circle, and every piece is not further than the accuracy from it (the sagitta of a chord)
            Assert.All(points.Where(p => p.Y > 0 && p.X > 0), p => Assert.Equal(100, Math.Sqrt(p.X * p.X + p.Y * p.Y), 0.3));
            for (var i = 0; i + 1 < points.Count; i++)
            {
                var a = points[i];
                var b = points[i + 1];
                if (a.X <= 0 || b.Y <= 0 || a.Y <= 0 || b.X <= 0)
                    continue;

                var chord = a.DistanceTo(b);
                var sagitta = 100 - Math.Sqrt(100 * 100 - chord * chord / 4);
                Assert.True(sagitta <= accuracy + 0.05, $"a piece of {chord:0.00} is {sagitta:0.000} from the circle, over {accuracy}");
            }

            Assert.True(DxfWriter.Write(trace, 100, 100, accuracy).Length > 100);
        }

        Assert.True(DxfWriter.Flatten(path, 1, 1, 0.05).Count > DxfWriter.Flatten(path, 1, 1, 0.5).Count);
    }

    [Fact]
    public void EachColour_HasItsOwnLayer_NamedByItsNumberAndColour_WithTheNearestAutoCadColour()
    {
        var path = Square().Layers[0].Paths[0];
        var trace = new TraceResult(40, 40, [new TraceLayer(new TraceColor(255, 0, 0), [path]), new TraceLayer(new TraceColor(250, 3, 5), [path]), new TraceLayer(TraceColor.Black, [path])]);

        var pairs = Pairs(DxfWriter.Write(trace, 40, 40));

        var names = pairs.Where(p => p.Code == 2 && p.Value.StartsWith("TRACE_", StringComparison.Ordinal)).Select(p => p.Value).ToList();
        Assert.Equal(["TRACE_1_FF0000", "TRACE_2_FA0305", "TRACE_3_000000"], names);
        Assert.Equal(3, Polylines(DxfWriter.Write(trace, 40, 40)).Count);
        Assert.Equal(1, DxfWriter.ColorIndex(new TraceColor(255, 0, 0)));
        Assert.Equal(1, DxfWriter.ColorIndex(new TraceColor(250, 3, 5)));
        Assert.Equal(7, DxfWriter.ColorIndex(TraceColor.Black));
        Assert.Equal(5, DxfWriter.ColorIndex(new TraceColor(10, 10, 240)));
        Assert.Equal(251, DxfWriter.ColorIndex(new TraceColor(90, 92, 91)));
    }

    [Fact]
    public void Numbers_UseAPoint_WhateverTheLanguageOfTheComputer()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("uk-UA");

            var dxf = DxfWriter.Write(Square(), 33.5, 16.25);

            Assert.Contains("33.5\r\n", dxf);
            Assert.DoesNotContain("33,5", dxf);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void ARealTrace_GivesOnePolylineForEachOutline()
    {
        var trace = BitmapTracer.Trace(Shapes.Ring(100, 40, 20), new TraceSettings());

        var polylines = Polylines(DxfWriter.Write(trace, 100, 100, 0.05));

        Assert.Equal(2, polylines.Count);
        Assert.All(polylines, polyline => Assert.True(polyline.Count > 8));
        Assert.All(polylines.SelectMany(p => p), p => { Assert.InRange(p.X, 0, 100); Assert.InRange(p.Y, 0, 100); });
    }

    [Theory]
    [InlineData(0, 10, 1)]
    [InlineData(10, 0, 1)]
    [InlineData(10, 10, 0)]
    [InlineData(10, 10, double.NaN)]
    public void ASizeOrAnAccuracyThatIsNothing_IsRefused(double width, double height, double accuracy) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => DxfWriter.Write(Square(), width, height, accuracy));
}
