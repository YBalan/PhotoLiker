using System.Drawing;
using PhotoLiker.Drawing;
using PhotoLiker.Manuscripts.Pages;
using PhotoLiker.Stickers;

namespace PhotoLiker.Manuscripts.Tests
{
    /// <summary>Finding the sheet of a manuscript in a photo from the map of the model of the stickers, and straightening it.</summary>
    public sealed class PageCutterTests
    {
        /// <summary>A map of <paramref name="width"/> by <paramref name="height"/> with 1 inside the polygon and 0 outside (a scanline fill: no model is needed to test what is made of its answer).</summary>
        private static float[] Filled(int width, int height, params PointF[] polygon)
        {
            var map = new float[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (Inside(polygon, x + 0.5f, y + 0.5f))
                        map[y * width + x] = 1f;
                }
            }

            return map;
        }

        private static bool Inside(PointF[] polygon, float x, float y)
        {
            var inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                if ((polygon[i].Y > y) != (polygon[j].Y > y) && x < (polygon[j].X - polygon[i].X) * (y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X)
                    inside = !inside;
            }

            return inside;
        }

        private static void AssertNear(PointF expected, PointF actual, float tolerance, string what) =>
            Assert.True(Math.Abs(expected.X - actual.X) <= tolerance && Math.Abs(expected.Y - actual.Y) <= tolerance, $"{what}: expected near ({expected.X}, {expected.Y}), got ({actual.X}, {actual.Y})");

        // ── The corners of the sheet ────────────────────────────────────────

        [Fact]
        public void ATiltedSheetOnADesk_GivesItsFourCorners_TopLeftFirst()
        {
            var map = Filled(400, 400, new PointF(80, 50), new PointF(330, 70), new PointF(310, 350), new PointF(60, 320));

            var quad = PageOutline.Find(map, 400, 400, 400, 400)!.Value;

            AssertNear(new PointF(80, 50), quad.TopLeft, 6, "top left");
            AssertNear(new PointF(330, 70), quad.TopRight, 6, "top right");
            AssertNear(new PointF(310, 350), quad.BottomRight, 6, "bottom right");
            AssertNear(new PointF(60, 320), quad.BottomLeft, 6, "bottom left");
        }

        [Fact]
        public void TheCorners_AreInThePixelsOfThePhoto_NotOfTheMap()
        {
            var map = Filled(100, 100, new PointF(20, 10), new PointF(80, 10), new PointF(80, 90), new PointF(20, 90));

            var quad = PageOutline.Find(map, 100, 100, 1000, 2000)!.Value;

            AssertNear(new PointF(200, 200), quad.TopLeft, 40, "top left"); // 20 * 10 and 10 * 20
            AssertNear(new PointF(800, 1800), quad.BottomRight, 40, "bottom right");
        }

        [Fact]
        public void TheModelDrawsTheSheetAsANetOfItsOutlineAndItsWriting_AndTheShapeRoundTheNetIsTheSheet()
        {
            // Only the outline of the sheet and a few lines inside it are sure; the paper itself is not
            var map = new float[400 * 400];
            void Line(int x0, int y0, int x1, int y1)
            {
                var steps = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
                for (var i = 0; i <= steps; i++)
                {
                    var x = x0 + (x1 - x0) * i / steps;
                    var y = y0 + (y1 - y0) * i / steps;
                    for (var d = 0; d < 3; d++)
                        map[(y + d) * 400 + x] = 1f;
                }
            }

            Line(60, 60, 340, 60);
            Line(340, 60, 340, 340);
            Line(340, 340, 60, 340);
            Line(60, 340, 60, 60);
            Line(100, 150, 300, 150);
            Line(100, 220, 300, 220);

            var quad = PageOutline.Find(map, 400, 400, 400, 400)!.Value;

            Assert.InRange(quad.Width, 270, 300);
            Assert.InRange(quad.Height, 270, 300);
        }

        [Fact]
        public void OfTwoPieces_TheLargerIsTheSheet_APenOnTheDeskIsNot()
        {
            var sheet = Filled(400, 400, new PointF(100, 80), new PointF(340, 80), new PointF(340, 360), new PointF(100, 360));
            var pen = Filled(400, 400, new PointF(10, 10), new PointF(70, 12), new PointF(70, 30), new PointF(10, 28));
            for (var i = 0; i < sheet.Length; i++)
                sheet[i] = Math.Max(sheet[i], pen[i]);

            var quad = PageOutline.Find(sheet, 400, 400, 400, 400)!.Value;

            AssertNear(new PointF(100, 80), quad.TopLeft, 6, "top left");
            AssertNear(new PointF(340, 360), quad.BottomRight, 6, "bottom right");
        }

        [Fact]
        public void NoSheet_WhenTheMapIsEmpty_ATinyPiece_OrASheetThatIsTheWholePhoto()
        {
            Assert.Null(PageOutline.Find(new float[100 * 100], 100, 100, 100, 100));

            var tiny = Filled(200, 200, new PointF(90, 90), new PointF(100, 90), new PointF(100, 100), new PointF(90, 100));
            Assert.Null(PageOutline.Find(tiny, 200, 200, 200, 200));

            var all = Filled(200, 200, new PointF(0, 0), new PointF(200, 0), new PointF(200, 200), new PointF(0, 200));
            Assert.Null(PageOutline.Find(all, 200, 200, 200, 200)); // there is no desk round it to cut away
        }

        [Fact]
        public void ASkewedShape_ThatIsNoSheet_IsNotCutOut()
        {
            // What the model outlines on a plain page can be the writing only: a quadrilateral with one side far longer than the one opposite it
            var map = Filled(400, 400, new PointF(20, 40), new PointF(380, 150), new PointF(390, 330), new PointF(60, 390));

            Assert.Null(PageOutline.Find(map, 400, 400, 400, 400));
        }

        [Fact]
        public void ASheetSeenAtAnAngle_IsPlausible_AndAnImpossibleQuadrilateralIsNot()
        {
            Assert.True(PageOutline.IsPlausible(new PageQuad(new(60, 40), new(360, 70), new(330, 270), new(30, 240)))); // tilted
            Assert.True(PageOutline.IsPlausible(new PageQuad(new(80, 0), new(320, 0), new(380, 400), new(20, 400)))); // keystone: the top is narrower, by half as much again
            Assert.False(PageOutline.IsPlausible(new PageQuad(new(0, 0), new(400, 0), new(40, 300), new(0, 300)))); // a sliver
            Assert.False(PageOutline.IsPlausible(new PageQuad(new(20, 40), new(380, 150), new(390, 330), new(60, 390))));
            Assert.False(PageOutline.IsPlausible(new PageQuad(new(5, 5), new(5, 5), new(5, 5), new(5, 5)))); // no size at all
        }

        [Fact]
        public void ARoundThing_IsNoSheet()
        {
            var points = Enumerable.Range(0, 60).Select(i => new PointF(200 + 120 * MathF.Cos(i * MathF.PI / 30), 200 + 120 * MathF.Sin(i * MathF.PI / 30))).ToArray();
            var disc = Filled(400, 400, points);

            // A circle has no four corners: the quadrilateral that is left of it has a quarter of its area less or more than the circle
            var quad = PageOutline.Find(disc, 400, 400, 400, 400);

            Assert.True(quad is null || quad.Value.Area <= 1.35f * MathF.PI * 120 * 120);
        }

        [Fact]
        public void TheCornersOfARoundedSheet_AreFoundWhereTheSidesMeet_NotInsideTheRoundedCorners()
        {
            // A rectangle with rounded corners (radius 30), so that its hull has many points near each corner
            var polygon = new List<PointF>();
            void Arc(float cx, float cy, float from)
            {
                for (var i = 0; i <= 8; i++)
                {
                    var angle = from + i * MathF.PI / 16;
                    polygon.Add(new PointF(cx + 30 * MathF.Cos(angle), cy + 30 * MathF.Sin(angle)));
                }
            }

            Arc(330, 330, 0);               // bottom right
            Arc(70, 330, MathF.PI / 2);     // bottom left
            Arc(70, 70, MathF.PI);          // top left
            Arc(330, 70, 3 * MathF.PI / 2); // top right
            var map = Filled(400, 400, [.. polygon]);

            var quad = PageOutline.Find(map, 400, 400, 400, 400)!.Value;

            // The sides are moved out to touch the shape, so the corners are the corners of the rectangle that the rounded one is cut from: (40, 40) and (360, 360)
            AssertNear(new PointF(40, 40), quad.TopLeft, 8, "top left");
            AssertNear(new PointF(360, 40), quad.TopRight, 8, "top right");
            AssertNear(new PointF(360, 360), quad.BottomRight, 8, "bottom right");
            AssertNear(new PointF(40, 360), quad.BottomLeft, 8, "bottom left");
        }

        [Fact]
        public void TheConvexHull_LeavesOutThePointsInside()
        {
            var hull = PageOutline.ConvexHull([new(0, 0), new(10, 0), new(10, 10), new(0, 10), new(5, 5), new(3, 4)]);

            Assert.Equal(4, hull.Count);
            Assert.DoesNotContain(new PointF(5, 5), hull);
        }

        [Fact]
        public void TheEnclosingQuad_ExtendsTheLongSidesOverTheCutCorners()
        {
            // A square with its corners cut off a little, each by a short extra side: eight sides, four of them real
            List<PointF> hull = [new(10, 0), new(90, 0), new(100, 10), new(100, 90), new(90, 100), new(10, 100), new(0, 90), new(0, 10)];

            var quad = PageOutline.EnclosingQuad(hull)!.Value;

            AssertNear(new PointF(0, 0), quad.TopLeft, 0.01f, "top left");
            AssertNear(new PointF(100, 0), quad.TopRight, 0.01f, "top right");
            AssertNear(new PointF(100, 100), quad.BottomRight, 0.01f, "bottom right");
            AssertNear(new PointF(0, 100), quad.BottomLeft, 0.01f, "bottom left");
            Assert.Null(PageOutline.EnclosingQuad([new(0, 0), new(10, 0), new(5, 8)])); // a triangle has no four sides
        }

        // ── Straightening ───────────────────────────────────────────────────

        private static Bitmap32 PhotoWithCorners(int width, int height, PageQuad quad, params Color[] colours)
        {
            var photo = Bitmap32.Filled(width, height, Color.White);
            var pixels = photo.Bgra;
            var corners = quad.Corners;
            var centre = new PointF(corners.Average(c => c.X), corners.Average(c => c.Y));
            for (var i = 0; i < 4; i++)
            {
                // A block of 7 by 7 points a little inside the corner, towards the middle of the sheet
                var cx = corners[i].X + (centre.X - corners[i].X) * 0.06f;
                var cy = corners[i].Y + (centre.Y - corners[i].Y) * 0.06f;
                for (var dy = -3; dy <= 3; dy++)
                {
                    for (var dx = -3; dx <= 3; dx++)
                    {
                        var x = (int)Math.Round(cx) + dx;
                        var y = (int)Math.Round(cy) + dy;
                        var o = (y * width + x) * 4;
                        pixels[o] = colours[i].B;
                        pixels[o + 1] = colours[i].G;
                        pixels[o + 2] = colours[i].R;
                        pixels[o + 3] = 255;
                    }
                }
            }

            return photo;
        }

        [Fact]
        public void ASheetThatIsATiltedAndSqueezedQuadrilateral_BecomesAnUprightRectangle_WithItsCornersWhereTheyShouldBe()
        {
            var quad = new PageQuad(new PointF(60, 40), new PointF(360, 70), new PointF(330, 270), new PointF(30, 240));
            var photo = PhotoWithCorners(400, 300, quad, Color.Red, Color.Lime, Color.Blue, Color.Yellow);

            var page = PagePerspective.Straighten(photo, quad);

            Assert.InRange(page.Width, 295, 306);  // the sides on average: 301 and 301
            Assert.InRange(page.Height, 197, 207);
            // The block of each corner is 6 percent of the way from the corner to the middle of the sheet, which is 3 percent of the way across the straight page
            Assert.Equal(Color.Red.ToArgb(), At(page, 0.03f, 0.03f).ToArgb());
            Assert.Equal(Color.Lime.ToArgb(), At(page, 0.97f, 0.03f).ToArgb());
            Assert.Equal(Color.Blue.ToArgb(), At(page, 0.97f, 0.97f).ToArgb());
            Assert.Equal(Color.Yellow.ToArgb(), At(page, 0.03f, 0.97f).ToArgb());
            Assert.Equal(Color.White.ToArgb(), At(page, 0.5f, 0.5f).ToArgb()); // the paper between
        }

        private static Color At(Bitmap32 page, float shareX, float shareY)
        {
            var c = page.At(Math.Clamp((int)(shareX * page.Width), 0, page.Width - 1), Math.Clamp((int)(shareY * page.Height), 0, page.Height - 1));
            return Color.FromArgb(255, c.R > 128 ? 255 : 0, c.G > 128 ? 255 : 0, c.B > 128 ? 255 : 0);
        }

        [Fact]
        public void AnAxisAlignedQuad_IsACropOfThePhoto_PixelForPixel()
        {
            var photo = Bitmap32.Filled(300, 200, Color.White);
            for (var y = 40; y < 140; y++)
            {
                for (var x = 50; x < 250; x++)
                {
                    var o = (y * 300 + x) * 4;
                    photo.Bgra[o] = (byte)(x - 50);
                    photo.Bgra[o + 1] = (byte)(y - 40);
                    photo.Bgra[o + 2] = 77;
                    photo.Bgra[o + 3] = 255;
                }
            }

            var page = PagePerspective.Straighten(photo, new PageQuad(new(50, 40), new(250, 40), new(250, 140), new(50, 140)));

            Assert.Equal((200, 100), (page.Width, page.Height));
            foreach (var (x, y) in new[] { (0, 0), (199, 0), (0, 99), (199, 99), (100, 50) })
            {
                var c = page.At(x, y);
                Assert.True(Math.Abs(c.B - x) <= 1 && Math.Abs(c.G - y) <= 1 && c.R == 77, $"({x}, {y}) is {c}");
            }
        }

        [Fact]
        public void ASheetThatIsLargerThanTheLimit_IsMadeSmaller_WithTheSameProportions()
        {
            var photo = Bitmap32.Filled(1000, 800, Color.White);

            var page = PagePerspective.Straighten(photo, new PageQuad(new(0, 0), new(1000, 0), new(1000, 800), new(0, 800)), maximumSide: 500);

            Assert.Equal((500, 400), (page.Width, page.Height));
        }

        [Fact]
        public void ACornerOutsideThePhoto_TakesTheEdgeOfThePhoto()
        {
            var photo = Bitmap32.Filled(100, 100, Color.Red);

            var page = PagePerspective.Straighten(photo, new PageQuad(new(-20, -20), new(120, -20), new(120, 120), new(-20, 120)));

            Assert.Equal(Color.Red.ToArgb(), page.At(0, 0).ToArgb());
            Assert.Equal(Color.Red.ToArgb(), page.At(page.Width - 1, page.Height - 1).ToArgb());
        }

        // ── The cutter ──────────────────────────────────────────────────────

        [Fact]
        public void TheCutter_FindsTheSheetWithTheMapOfItsDetector_AndGivesItStraight()
        {
            var photo = Bitmap32.Filled(400, 300, Color.FromArgb(120, 90, 60)); // the desk
            using var cutter = new PageCutter(pixels =>
            {
                Assert.Equal((400, 300), (pixels.Width, pixels.Height));
                return new SubjectMap(200, 150, Filled(200, 150, new PointF(40, 20), new PointF(170, 30), new PointF(160, 130), new PointF(30, 120)));
            });

            var cut = cutter.Find(photo);

            Assert.NotNull(cut.Page);
            Assert.NotNull(cut.Quad);
            Assert.InRange(cut.Page!.Width, 240, 280); // about 130 * 2 map points wide
            Assert.InRange(cut.Page.Height, 180, 220);
        }

        [Fact]
        public void TheCutter_GivesNothingWhenThereIsNoSheet_SoThePhotoIsUsedAsItIs()
        {
            var photo = Bitmap32.Filled(400, 300, Color.White);
            using var cutter = new PageCutter(_ => new SubjectMap(100, 100, new float[100 * 100]));

            var cut = cutter.Find(photo);

            Assert.Null(cut.Page);
            Assert.Null(cut.Quad);
        }

        [Fact]
        public void ASeeThroughPhoto_IsGivenToTheDetectorOverWhite()
        {
            var photo = new Bitmap32(2, 1, [0, 0, 0, 0, 10, 20, 30, 255]); // a see-through point, then a solid one
            PhotoPixels? seen = null;
            using var cutter = new PageCutter(pixels =>
            {
                seen = pixels;
                return new SubjectMap(10, 10, new float[100]);
            });

            cutter.Find(photo);

            Assert.Equal([255, 255, 255, 10, 20, 30], seen!.Bgr);
        }
    }
}
