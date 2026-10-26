namespace PhotoLiker.Search.Tests;

public class FaceAlignerTests
{
    [Fact]
    public void EstimateSimilarity_FindsTheTransformThatWasApplied()
    {
        // The transform x' = a x - b y + tx, y' = b x + a y + ty applied to some landmarks...
        const double a = 0.8, b = 0.3, tx = 10, ty = -5;
        Point2[] source = [new(100, 120), new(180, 118), new(140, 170), new(110, 210), new(170, 208)];
        var target = source.Select(p => new Point2((float)(a * p.X - b * p.Y + tx), (float)(b * p.X + a * p.Y + ty))).ToArray();

        var (foundA, foundB, foundTx, foundTy) = FaceAligner.EstimateSimilarity(source, target);

        // ... is found again from the two sets of points
        Assert.Equal(a, foundA, 3);
        Assert.Equal(b, foundB, 3);
        Assert.Equal(tx, foundTx, 2);
        Assert.Equal(ty, foundTy, 2);
    }

    [Fact]
    public void Align_WithLandmarksAtTheirPlaces_CopiesThePhotoAsItIs()
    {
        // 112 x 112 photo: blue = column, green = row, red = 100. Landmarks exactly where the template wants them: no change.
        var bgr = new byte[FaceAligner.Size * FaceAligner.Size * 3];
        for (var y = 0; y < FaceAligner.Size; y++)
        {
            for (var x = 0; x < FaceAligner.Size; x++)
            {
                var p = (y * FaceAligner.Size + x) * 3;
                bgr[p] = (byte)x;
                bgr[p + 1] = (byte)y;
                bgr[p + 2] = 100;
            }
        }

        var aligned = FaceAligner.Align(new DecodedPhoto(FaceAligner.Size, FaceAligner.Size, bgr), FaceAligner.Template);

        var plane = FaceAligner.Size * FaceAligner.Size;
        var i = 60 * FaceAligner.Size + 30; // row 60, column 30
        Assert.Equal(FaceAligner.Length, aligned.Length);
        Assert.Equal((100 - 127.5f) / 127.5f, aligned[i], 3);        // red
        Assert.Equal((60 - 127.5f) / 127.5f, aligned[plane + i], 3); // green
        Assert.Equal((30 - 127.5f) / 127.5f, aligned[2 * plane + i], 3); // blue
    }

    [Fact]
    public void Align_ScalesAFaceThatIsTwiceAsBig()
    {
        // The same picture drawn twice as big: the landmarks are twice as far apart, the aligned face must be the same
        var small = new byte[FaceAligner.Size * FaceAligner.Size * 3];
        for (var y = 0; y < FaceAligner.Size; y++)
            for (var x = 0; x < FaceAligner.Size; x++)
                small[(y * FaceAligner.Size + x) * 3 + 2] = (byte)((x / 8 + y / 8) % 2 * 200);

        var bigSize = FaceAligner.Size * 2;
        var big = new byte[bigSize * bigSize * 3];
        for (var y = 0; y < bigSize; y++)
            for (var x = 0; x < bigSize; x++)
                big[(y * bigSize + x) * 3 + 2] = small[((y / 2) * FaceAligner.Size + x / 2) * 3 + 2];

        var expected = FaceAligner.Align(new DecodedPhoto(FaceAligner.Size, FaceAligner.Size, small), FaceAligner.Template);
        var actual = FaceAligner.Align(
            new DecodedPhoto(bigSize, bigSize, big),
            FaceAligner.Template.Select(p => new Point2(p.X * 2, p.Y * 2)).ToArray());

        // Compare the red plane away from the squares' edges, where the enlarging blurs
        var close = 0;
        for (var i = 0; i < FaceAligner.Size * FaceAligner.Size; i++)
            close += Math.Abs(expected[i] - actual[i]) < 0.2f ? 1 : 0;
        Assert.True(close > FaceAligner.Size * FaceAligner.Size * 0.85, $"only {close} pixels are alike");
    }

    [Fact]
    public void LandmarksFromBox_AreInsideTheBox_EyesAboveMouth()
    {
        var landmarks = FaceAligner.LandmarksFromBox(100, 200, 50, 80);

        Assert.All(landmarks, p =>
        {
            Assert.InRange(p.X, 100, 150);
            Assert.InRange(p.Y, 200, 280);
        });
        Assert.True(landmarks[0].X < landmarks[1].X);   // left eye is left of right eye
        Assert.True(landmarks[0].Y < landmarks[3].Y);   // eyes are above the mouth
    }
}

public class FaceDetectorTests
{
    private static RawOutput Zeros(int rows, int columns) => new(new float[rows * columns], rows, columns);

    /// <summary>The outputs of the detector for a photo without a face, in the order of the real model.</summary>
    private static List<RawOutput> EmptyOutputs() =>
    [
        Zeros(12800, 1), Zeros(3200, 1), Zeros(800, 1),
        Zeros(12800, 4), Zeros(3200, 4), Zeros(800, 4),
        Zeros(12800, 10), Zeros(3200, 10), Zeros(800, 10),
    ];

    [Fact]
    public void Decode_FindsNothing_WhenNoScoreIsHigh()
    {
        Assert.Empty(FaceDetector.Decode(EmptyOutputs(), scale: 1f, threshold: 0.5f));
    }

    [Fact]
    public void Decode_TurnsAnAnchorIntoABoxAndLandmarks_InPhotoCoordinates()
    {
        var outputs = EmptyOutputs();

        // Stride 8: anchor 1 of the cell in row 10, column 20 -> the cell's corner is at x = 160, y = 80
        var anchor = (10 * 80 + 20) * 2 + 1;
        outputs[0].Data[anchor] = 0.9f;
        float[] distances = [2, 3, 4, 5]; // left, top, right, bottom, in strides
        distances.CopyTo(outputs[3].Data, anchor * 4);
        for (var j = 0; j < 5; j++)
        {
            outputs[6].Data[anchor * 10 + 2 * j] = j;      // landmark j is j strides right ...
            outputs[6].Data[anchor * 10 + 2 * j + 1] = -j; // ... and j strides up of the corner
        }

        // The photo was enlarged by 0.5 before it went into the model
        var faces = FaceDetector.Decode(outputs, scale: 0.5f, threshold: 0.5f);

        var face = Assert.Single(faces);
        Assert.Equal(0.9f, face.Score);
        Assert.Equal((160 - 2 * 8) / 0.5f, face.X);
        Assert.Equal((80 - 3 * 8) / 0.5f, face.Y);
        Assert.Equal((2 + 4) * 8 / 0.5f, face.Width);
        Assert.Equal((3 + 5) * 8 / 0.5f, face.Height);
        Assert.Equal(new Point2((160 + 2 * 8) / 0.5f, (80 - 2 * 8) / 0.5f), face.Landmarks[2]);
    }

    [Fact]
    public void Decode_KeepsOnlyTheBestOfOverlappingBoxes()
    {
        var outputs = EmptyOutputs();
        foreach (var (anchor, score) in new[] { (1640, 0.95f), (1641, 0.80f) }) // neighbouring anchors, nearly the same box
        {
            outputs[0].Data[anchor] = score;
            float[] distances = [2, 3, 4, 5];
            distances.CopyTo(outputs[3].Data, anchor * 4);
        }

        var faces = FaceDetector.Decode(outputs, scale: 1f, threshold: 0.5f);

        Assert.Equal(0.95f, Assert.Single(faces).Score);
    }

    [Fact]
    public void Decode_RejectsOutputsOfAnotherModel()
    {
        Assert.Throws<InvalidDataException>(() => FaceDetector.Decode([Zeros(10, 3)], 1f, 0.5f));
    }

    [Fact]
    public void Prepare_PutsThePhotoInTheCorner_AndPadsWithBlack()
    {
        var white = new DecodedPhoto(100, 50, Enumerable.Repeat((byte)255, 100 * 50 * 3).ToArray());

        var (tensor, scale) = FaceDetector.Prepare(white);

        Assert.Equal(3 * FaceDetector.InputSize * FaceDetector.InputSize, tensor.Length);
        Assert.Equal(6.4f, scale, 3); // 100 x 50 became 640 x 320
        Assert.Equal((255 - 127.5f) / 128f, tensor[0], 3);                                  // inside the photo
        Assert.Equal((0 - 127.5f) / 128f, tensor[400 * FaceDetector.InputSize + 5], 3);     // below it
    }
}
