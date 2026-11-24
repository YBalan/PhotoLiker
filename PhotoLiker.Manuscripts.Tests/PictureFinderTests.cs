using System.Drawing;
using PhotoLiker.Drawing;
using PhotoLiker.Drawing.Ocr;
using PhotoLiker.Manuscripts.Pages;
using PhotoLiker.Stickers;

namespace PhotoLiker.Manuscripts.Tests
{
    /// <summary>Finding the pictures on a page from the map of the model of the stickers, outlining them, and cutting them out along the outline.</summary>
    public sealed class PictureFinderTests
    {
        private const int Side = 200;

        /// <summary>A map with 1 inside a disc and 0 outside it.</summary>
        private static void Disc(float[] map, int width, float cx, float cy, float radius)
        {
            for (var i = 0; i < map.Length; i++)
            {
                float x = i % width + 0.5f - cx, y = i / width + 0.5f - cy;
                if (x * x + y * y <= radius * radius)
                    map[i] = 1f;
            }
        }

        private static void Rectangle(float[] map, int width, int left, int top, int right, int bottom)
        {
            for (var y = top; y < bottom; y++)
            {
                for (var x = left; x < right; x++)
                    map[y * width + x] = 1f;
            }
        }

        /// <summary>The marks a hand leaves in a map: thin ragged strokes that are one piece with each other and not solid.</summary>
        private static void Scribble(float[] map, int width)
        {
            for (var x = 10; x < 190; x++)
            {
                map[20 * width + x] = 1f;
                map[21 * width + x] = 1f;
                map[(25 + x % 7) * width + x] = 1f;
            }
        }

        private static double Area(IReadOnlyList<PointF> polygon) => Math.Abs(PictureOutline.SignedArea(polygon));

        // ── Which pieces are pictures ───────────────────────────────────────

        [Fact]
        public void ASolidBlob_IsAPicture_AndItsOutlineHasTheAreaOfTheBlob()
        {
            var map = new float[Side * Side];
            Disc(map, Side, 120, 130, 40);

            var pictures = PictureFinder.PicturesIn(new SubjectMap(Side, Side, map), 400, 400); // the page is twice as large as the map

            var picture = Assert.Single(pictures);
            var bounds = picture.Bounds;
            Assert.InRange(bounds.Width, 2 * 80 - 6, 2 * 80 + 12); // 2 * 2r, with a little room for the rim
            Assert.InRange(bounds.Height, 2 * 80 - 6, 2 * 80 + 12);
            Assert.Equal(240f, picture.Centre.X, 4);
            Assert.Equal(260f, picture.Centre.Y, 4);
            Assert.InRange(Area(picture.Outline) / (Math.PI * 80 * 80), 0.95, 1.12);
        }

        [Fact]
        public void ThinRaggedWriting_IsNotAPicture()
        {
            var map = new float[Side * Side];
            Scribble(map, Side);

            Assert.Empty(PictureFinder.PicturesIn(new SubjectMap(Side, Side, map), Side, Side));
        }

        [Fact]
        public void ARing_IsNotAPicture_BecauseItIsNotSolid()
        {
            var map = new float[Side * Side];
            Disc(map, Side, 100, 100, 60);
            // Take out the middle: what is left is an edge only, as the model draws a thing whose inside it doubts
            for (var i = 0; i < map.Length; i++)
            {
                float x = i % Side + 0.5f - 100, y = i / Side + 0.5f - 100;
                if (x * x + y * y <= 50 * 50)
                    map[i] = 0f;
            }

            Assert.Empty(PictureFinder.PicturesIn(new SubjectMap(Side, Side, map), Side, Side));
        }

        [Fact]
        public void AShapeWithAHugeDent_IsNotAPicture_EvenWhenItFillsHalfOfItsRectangle()
        {
            // A U: two arms and a base, 20 wide, in a rectangle of 100 by 100. It fills 52 % of the rectangle (more than a thin ragged thing does) but only 52 % of its convex hull
            var map = new float[Side * Side];
            Rectangle(map, Side, 50, 50, 150, 70); // the top of the arms is the top of the rectangle
            Rectangle(map, Side, 50, 50, 70, 150);
            Rectangle(map, Side, 130, 50, 150, 150);
            Rectangle(map, Side, 50, 130, 150, 150);
            for (var y = 50; y < 70; y++)
            {
                for (var x = 70; x < 130; x++)
                    map[y * Side + x] = 0f; // the top bar is not there: it is a U, not a box
            }

            var pieces = PictureOutline.Pieces(map, Side, Side, PictureFinder.Threshold, out var labels);
            var piece = Assert.Single(pieces);
            Assert.InRange(piece.Fill, PictureFinder.MinimumFill, 0.6); // the fill rule alone would let it through
            Assert.True(PictureOutline.Solidity(piece, labels, Side) < PictureFinder.MinimumSolidity);

            Assert.Empty(PictureFinder.PicturesIn(new SubjectMap(Side, Side, map), Side, Side));
        }

        [Fact]
        public void AThinDiagonalBar_IsNotAPicture_EvenThoughItIsSolid()
        {
            // A band 36 wide along the diagonal: no dents at all, but it fills under a third of its rectangle
            var map = new float[Side * Side];
            PointF[] band = [new(27.3f, 52.7f), new(52.7f, 27.3f), new(172.7f, 147.3f), new(147.3f, 172.7f)];
            for (var i = 0; i < map.Length; i++)
            {
                if (InsideOf(band, i % Side + 0.5f, i / Side + 0.5f))
                    map[i] = 1f;
            }

            var piece = Assert.Single(PictureOutline.Pieces(map, Side, Side, PictureFinder.Threshold, out var labels));
            Assert.True(piece.Fill < PictureFinder.MinimumFill);
            Assert.True(PictureOutline.Solidity(piece, labels, Side) > 0.9);

            Assert.Empty(PictureFinder.PicturesIn(new SubjectMap(Side, Side, map), Side, Side));
        }

        private static bool InsideOf(PointF[] polygon, float x, float y)
        {
            var inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                if ((polygon[i].Y > y) != (polygon[j].Y > y) && x < (polygon[j].X - polygon[i].X) * (y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X)
                    inside = !inside;
            }

            return inside;
        }

        [Fact]
        public void TheTraceOfAPieceAtTheBorderOfTheMap_GoesToTheBorder_NotToTheMiddleOfItsLastPoint()
        {
            var map = new float[Side * Side];
            Rectangle(map, Side, 100, 110, Side, Side);
            var piece = Assert.Single(PictureOutline.Pieces(map, Side, Side, 0.5f, out var labels));

            var edge = PictureOutline.Trace(piece, labels, Side, Side);

            Assert.Equal(Side, edge.Max(p => p.X)); // the map is 200 points wide: its edge is at 200, not at 199.5
            Assert.Equal(Side, edge.Max(p => p.Y));
            Assert.Equal(100.5f, edge.Min(p => p.X)); // a side that is not at the border is through the middle of its points
            Assert.Equal(110.5f, edge.Min(p => p.Y));
        }

        [Fact]
        public void ASpeck_AndAPieceThatIsTheWholePage_AreNotPictures()
        {
            var speck = new float[Side * Side];
            Disc(speck, Side, 50, 50, 8); // 0.5 % of the map
            var page = new float[Side * Side];
            Rectangle(page, Side, 5, 5, 195, 195);

            Assert.Empty(PictureFinder.PicturesIn(new SubjectMap(Side, Side, speck), Side, Side));
            Assert.Empty(PictureFinder.PicturesIn(new SubjectMap(Side, Side, page), Side, Side));
        }

        [Fact]
        public void SeveralPictures_ComeLargestFirst_AndWritingBesideThemIsLeftOut()
        {
            var map = new float[Side * Side];
            Disc(map, Side, 50, 140, 25); // small
            Disc(map, Side, 140, 140, 40); // large
            Scribble(map, Side);

            var pictures = PictureFinder.PicturesIn(new SubjectMap(Side, Side, map), Side, Side);

            Assert.Equal(2, pictures.Count);
            Assert.True(pictures[0].Bounds.Width > pictures[1].Bounds.Width);
            Assert.InRange(pictures[0].Centre.X, 135, 145);
            Assert.InRange(pictures[1].Centre.X, 45, 55);
        }

        [Fact]
        public void APictureThePhotoCutsOff_ReachesTheEdgeOfThePage()
        {
            var map = new float[Side * Side];
            Rectangle(map, Side, 100, 110, Side, Side); // a sticker in the corner, cut by the photo

            var picture = Assert.Single(PictureFinder.PicturesIn(new SubjectMap(Side, Side, map), Side, Side));

            Assert.Equal(Side, picture.Bounds.Right, 1);
            Assert.Equal(Side, picture.Bounds.Bottom, 1);
            Assert.InRange(picture.Bounds.Left, 97, 103);
        }

        [Fact]
        public void TheOutlineOfANonConvexPiece_FollowsItsDent()
        {
            // An L: a square of 90 with a quarter of it taken out of a corner
            var map = new float[Side * Side];
            Rectangle(map, Side, 40, 40, 130, 130);
            for (var y = 40; y < 85; y++)
            {
                for (var x = 85; x < 130; x++)
                    map[y * Side + x] = 0f;
            }

            var picture = Assert.Single(PictureFinder.PicturesIn(new SubjectMap(Side, Side, map), Side, Side));

            Assert.InRange(Area(picture.Outline) / (90.0 * 90 - 45 * 45), 0.95, 1.12);
            Assert.True(picture.Outline.Length is >= 6 and <= 10, $"{picture.Outline.Length} corners");
        }

        // ── Painting the text over ──────────────────────────────────────────

        private static Bitmap32 Paper(int width, int height, byte blue, byte green, byte red)
        {
            var bgra = new byte[width * height * 4];
            for (var i = 0; i < bgra.Length; i += 4)
            {
                bgra[i] = blue;
                bgra[i + 1] = green;
                bgra[i + 2] = red;
                bgra[i + 3] = 255;
            }

            return new Bitmap32(width, height, bgra);
        }

        private static void Ink(Bitmap32 page, int left, int top, int right, int bottom)
        {
            for (var y = top; y < bottom; y++)
            {
                for (var x = left; x < right; x++)
                {
                    var i = (y * page.Width + x) * 4;
                    page.Bgra[i] = page.Bgra[i + 1] = page.Bgra[i + 2] = 30;
                }
            }
        }

        [Fact]
        public void ALineOfWriting_IsPaintedOverWithThePaper_AndNothingElseIsTouched()
        {
            var page = Paper(300, 200, 220, 230, 240);
            Ink(page, 60, 90, 240, 100); // the writing
            Ink(page, 20, 20, 40, 40); // a mark that is not in any line
            var line = TextRegion.FromCentre(new PointF(150, 95), 200, 24, 0);

            var (painted, paper) = PictureFinder.PaintedOver(page, [line]);

            Assert.Equal(((byte)220, (byte)230, (byte)240), paper); // the colour the line was painted with is the colour of the paper
            int At(int x, int y) => (y * 300 + x) * 3;
            Assert.Equal((220, 230, 240), (painted.Bgr[At(100, 95)], painted.Bgr[At(100, 95) + 1], painted.Bgr[At(100, 95) + 2])); // the ink is gone
            Assert.Equal(30, painted.Bgr[At(30, 30)]); // the mark outside the line is still there
            Assert.Equal(30, page.Bgra[(95 * 300 + 100) * 4]); // the page that was given is not changed
        }

        [Fact]
        public void ThePaperIsTheMiddleOfTheColoursOfTheLines_NotTheColourOfOneOfThem()
        {
            // Three bands of the page lit differently, a line in the middle of each: the paper is the middle one, whichever line comes first
            var page = Paper(300, 300, 0, 0, 0);
            byte[] shade = [100, 230, 220];
            for (var band = 0; band < 3; band++)
            {
                for (var y = band * 100; y < band * 100 + 100; y++)
                {
                    for (var x = 0; x < 300; x++)
                    {
                        var i = (y * 300 + x) * 4;
                        page.Bgra[i] = page.Bgra[i + 1] = page.Bgra[i + 2] = shade[band];
                    }
                }
            }

            var lines = new[] { 50, 150, 250 }.Select(y => TextRegion.FromCentre(new PointF(150, y), 200, 20, 0)).ToArray();

            var (_, paper) = PictureFinder.PaintedOver(page, lines);

            Assert.Equal(((byte)220, (byte)220, (byte)220), paper);
        }

        [Fact]
        public void ALineWhoseSurroundingsAreNotPaper_IsLeftAsItIs()
        {
            var page = Paper(60, 40, 220, 230, 240);
            var line = TextRegion.FromCentre(new PointF(30, 20), 4, 2, 0); // a line too small to have any paper round it to be taken for the colour

            var (painted, paper) = PictureFinder.PaintedOver(page, [line]);

            Assert.Equal(220, painted.Bgr[(20 * 60 + 30) * 3]);
            Assert.Equal(((byte)220, (byte)230, (byte)240), paper); // with no line painted, the paper is the middle colour of the page
        }

        // ── The whole of it ─────────────────────────────────────────────────

        [Fact]
        public void TheModelIsShownThePageWithItsTextPaintedOver_AndTheOutlineIsInThePixelsOfThePage()
        {
            var page = Paper(400, 400, 235, 235, 235);
            Ink(page, 40, 40, 360, 60); // a line of writing
            Ink(page, 220, 220, 340, 340); // and a picture: much darker than the paper, under the disc the model finds
            var line = TextRegion.FromCentre(new PointF(200, 50), 340, 30, 0);
            PhotoPixels? seen = null;

            using var finder = new PictureFinder(
                photo =>
                {
                    seen = photo;
                    var map = new float[Side * Side];
                    Disc(map, Side, 140, 140, 40);
                    return new SubjectMap(Side, Side, map);
                },
                _ => [line]);

            var pictures = finder.Find(page);

            Assert.NotNull(seen);
            Assert.Equal(235, seen!.Bgr[(50 * 400 + 200) * 3]); // the writing was gone when the model looked
            var picture = Assert.Single(pictures);
            Assert.Equal(280f, picture.Centre.X, 4); // 140 of 200 on a page of 400
            Assert.Equal(280f, picture.Centre.Y, 4);
        }

        [Fact]
        public void ABlobThatIsTheColourOfThePaper_IsNotAPicture_ButOneThatIsNotIs()
        {
            var map = new float[Side * Side];
            Disc(map, Side, 100, 100, 50);
            var sameAsPaper = new PhotoPixels(Side, Side, Enumerable.Repeat((byte)230, Side * Side * 3).ToArray());
            var picture = new byte[Side * Side * 3];
            Array.Fill(picture, (byte)230);
            for (var y = 50; y < 150; y++)
            {
                for (var x = 50; x < 150; x++)
                    picture[(y * Side + x) * 3 + 2] = 40; // red is gone: a distance of 190 from the paper
            }

            var paper = ((byte)230, (byte)230, (byte)230);
            var subject = new SubjectMap(Side, Side, map);

            Assert.Empty(PictureFinder.PicturesIn(subject, Side, Side, sameAsPaper, paper)); // the sheet itself, which the model took for the subject
            Assert.Single(PictureFinder.PicturesIn(subject, Side, Side, new PhotoPixels(Side, Side, picture), paper));
            Assert.Single(PictureFinder.PicturesIn(subject, Side, Side)); // with no colours to go by, the shape decides
        }

        [Fact]
        public void AThinStrip_IsNotAPicture_EvenThoughItIsSolidAndBigEnough()
        {
            // The edge of a sheet on a desk: 8 points wide and 150 tall in a map of 200
            var map = new float[Side * Side];
            Rectangle(map, Side, 150, 20, 158, 170);
            var piece = Assert.Single(PictureOutline.Pieces(map, Side, Side, PictureFinder.Threshold, out _));
            Assert.True(piece.Area >= PictureFinder.MinimumShare * Side * Side && piece.Fill > 0.9, "it is as solid and as big as a picture can be");

            Assert.Empty(PictureFinder.PicturesIn(new SubjectMap(Side, Side, map), Side, Side));
        }

        [Fact]
        public void ACancelledSearch_Stops()
        {
            using var finder = new PictureFinder(_ => new SubjectMap(Side, Side, new float[Side * Side]), _ => []);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();

            Assert.Throws<OperationCanceledException>(() => finder.Find(Paper(40, 40, 1, 2, 3), cancelled.Token));
        }

        [Fact]
        public void AModelThatIsNotThere_IsSaidSo()
        {
            Assert.Throws<FileNotFoundException>(() => new PictureFinder(Path.Combine(Path.GetTempPath(), "no-such-model.onnx"), Path.Combine(Path.GetTempPath(), "no-such-finder.onnx")));
        }

        // ── Cutting along the outline ───────────────────────────────────────

        [Fact]
        public void ThePictureIsCutAlongItsOutline_InsideItIsThePhoto_OutsideItIsSeeThrough()
        {
            var photo = Paper(200, 200, 10, 20, 200); // red paper
            // A diamond with its middle at (100, 100)
            PointF[] outline = [new(100, 40), new(160, 100), new(100, 160), new(40, 100)];

            var cut = PictureCutter.Cut(photo, outline)!;

            Assert.Equal(120, cut.Width); // the rectangle that holds the diamond
            Assert.Equal(120, cut.Height);
            int At(int x, int y) => (y * cut.Width + x) * 4;
            Assert.Equal(255, cut.Bgra[At(60, 60) + 3]); // the middle
            Assert.Equal((10, 20, 200), (cut.Bgra[At(60, 60)], cut.Bgra[At(60, 60) + 1], cut.Bgra[At(60, 60) + 2])); // the colour of the photo
            Assert.Equal(0, cut.Bgra[At(2, 2) + 3]); // a corner of the rectangle is outside the diamond
            Assert.Equal(0, cut.Bgra[At(117, 117) + 3]);
            var edge = cut.Bgra[At(29, 30) + 3]; // on the slanting side (the line x + y = 60 runs through it): soft, neither all in nor all out
            Assert.InRange(edge, 1, 254);
        }

        [Fact]
        public void ACutLargerThanTheLimit_IsMadeSmaller_InTheSameShape()
        {
            var photo = Paper(1000, 800, 50, 60, 70);
            PointF[] outline = [new(100, 100), new(900, 100), new(900, 700), new(100, 700)];

            var cut = PictureCutter.Cut(photo, outline, maximumSide: 400)!;

            Assert.Equal(400, cut.Width);
            Assert.Equal(300, cut.Height);
            Assert.Equal(255, cut.Bgra[(150 * 400 + 200) * 4 + 3]);
            Assert.Equal(50, cut.Bgra[(150 * 400 + 200) * 4]);
        }

        [Fact]
        public void AnOutlineThatIsOutsideThePhoto_OrIsNotAShape_GivesNothing()
        {
            var photo = Paper(100, 100, 1, 2, 3);

            Assert.Null(PictureCutter.Cut(photo, [new PointF(200, 200), new PointF(300, 200), new PointF(300, 300)]));
            Assert.Null(PictureCutter.Cut(photo, [new PointF(10, 10), new PointF(50, 50)]));
        }

        [Fact]
        public void APictureThatGoesPastTheEdgeOfThePhoto_IsCutAtTheEdge()
        {
            var photo = Paper(100, 100, 9, 9, 9);

            var cut = PictureCutter.Cut(photo, [new PointF(50, 50), new PointF(150, 50), new PointF(150, 150), new PointF(50, 150)])!;

            Assert.Equal(50, cut.Width);
            Assert.Equal(50, cut.Height);
            Assert.Equal(255, cut.Bgra[(25 * 50 + 25) * 4 + 3]);
        }

        [Fact]
        public void TheOutlineGoesToTheFileAndComesBack()
        {
            var picture = new PagePicture([new PointF(1.25f, 2), new PointF(30, 2), new PointF(30, 40.5f)]);

            var back = StoredPicture.From(picture).ToPicture()!;

            Assert.Equal(picture.Outline.Length, back.Outline.Length);
            Assert.Equal(picture.Outline[2], back.Outline[2]);
            Assert.Null(new StoredPicture { Outline = [1, 2, 3, 4] }.ToPicture());
            Assert.Null(new StoredPicture { Outline = [1, 2, 3, 4, 5] }.ToPicture());
            Assert.Null(new StoredPicture().ToPicture());
        }
    }
}
