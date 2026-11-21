namespace PhotoLiker.TraceBitmap
{
    /// <summary>
    /// Traces a picture: cuts it into the scans the settings ask for, follows the outlines of each scan and fits them with lines and curves. The result is the layers of shapes from the bottom one
    /// up, in the pixels of the picture; <see cref="SvgWriter"/> and <see cref="DxfWriter"/> write them as a file.
    /// </summary>
    public static class BitmapTracer
    {
        /// <param name="image">The picture to trace.</param>
        /// <param name="settings">How to cut it up; values outside their range are brought into it.</param>
        /// <param name="progress">Told how far the work is, 0 to 1 (from whatever thread the work runs on).</param>
        /// <param name="cancel">Stops the work with an <see cref="OperationCanceledException"/>.</param>
        public static TraceResult Trace(PixelImage image, TraceSettings settings, IProgress<double>? progress = null, CancellationToken cancel = default)
        {
            ArgumentNullException.ThrowIfNull(image);
            ArgumentNullException.ThrowIfNull(settings);
            settings = settings.Normalized();

            progress?.Report(0);
            var layers = new List<TraceLayer>();
            switch (settings.Mode)
            {
                case TraceMode.BrightnessCutoff:
                {
                    var luminance = Binarizer.Luminance(image);
                    cancel.ThrowIfCancellationRequested();
                    AddSingle(layers, Binarizer.BrightnessCutoff(luminance, image.Width, image.Height, settings.BrightnessCutoff), settings, cancel);
                    break;
                }

                case TraceMode.EdgeDetection:
                {
                    var luminance = Binarizer.Luminance(image);
                    cancel.ThrowIfCancellationRequested();
                    AddSingle(layers, Binarizer.Edges(luminance, image.Width, image.Height, settings.EdgeThreshold), settings, cancel);
                    break;
                }

                case TraceMode.ColorQuantization:
                {
                    var reduced = ColorQuantizer.Reduce(image, settings.QuantizeColors);
                    cancel.ThrowIfCancellationRequested();
                    AddSingle(layers, Binarizer.Boundaries(reduced.Indices, image.Width, image.Height), settings, cancel);
                    break;
                }

                default:
                    TraceScans(image, settings, layers, progress, cancel);
                    break;
            }

            progress?.Report(1);
            return new TraceResult(image.Width, image.Height, layers);
        }

        private static void AddSingle(List<TraceLayer> layers, Bilevel scan, TraceSettings settings, CancellationToken cancel)
        {
            if (settings.InvertImage)
                scan = scan.Inverted();

            layers.Add(new TraceLayer(TraceColor.Black, Outline(scan, settings, cancel)));
        }

        private static void TraceScans(PixelImage image, TraceSettings settings, List<TraceLayer> layers, IProgress<double>? progress, CancellationToken cancel)
        {
            if (settings.Smooth)
                image = Binarizer.Blurred(image);
            cancel.ThrowIfCancellationRequested();

            var luminance = Binarizer.Luminance(image);
            byte[] levels;
            TraceColor[] colors;
            switch (settings.Mode)
            {
                case TraceMode.BrightnessSteps:
                    levels = Binarizer.BrightnessLevels(luminance, settings.BrightnessSteps);
                    colors = [.. Enumerable.Range(0, settings.BrightnessSteps).Select(step => Binarizer.GrayOfBrightnessStep(step, settings.BrightnessSteps))];
                    break;

                case TraceMode.Grays:
                    levels = Binarizer.GrayLevels(luminance, settings.Grays);
                    colors = [.. Enumerable.Range(0, settings.Grays).Select(level => Binarizer.GrayOfBand(level, settings.Grays))];
                    break;

                default:
                    var reduced = ColorQuantizer.Reduce(image, settings.Colors);
                    levels = reduced.Indices;
                    colors = reduced.Palette;
                    break;
            }

            cancel.ThrowIfCancellationRequested();

            // The scans from the lightest (the bottom one: everything that is not paper, when stacked) up to the darkest. The lightest is the background, which can be left out.
            var scans = Enumerable.Range(0, colors.Length).Reverse().ToList();
            if (settings.RemoveBackground && scans.Count > 0)
                scans.RemoveAt(0);

            for (var i = 0; i < scans.Count; i++)
            {
                cancel.ThrowIfCancellationRequested();
                var level = scans[i];
                var scan = new Bilevel(image.Width, image.Height);
                for (var p = 0; p < levels.Length; p++)
                    scan.Pixels[p] = (settings.StackScans ? levels[p] <= level : levels[p] == level) ? (byte)1 : (byte)0;

                var paths = Outline(scan, settings, cancel);
                if (paths.Count > 0)
                    layers.Add(new TraceLayer(colors[level], paths));

                progress?.Report((i + 1.0) / scans.Count);
            }
        }

        /// <summary>The outlines of the black pixels as paths: the specks taken away, the outlines followed and fitted.</summary>
        internal static List<TracePath> Outline(Bilevel scan, TraceSettings settings, CancellationToken cancel)
        {
            if (settings.SuppressSpeckles)
                scan.SuppressSpeckles(settings.SpeckleSize);
            cancel.ThrowIfCancellationRequested();

            var paths = new List<TracePath>();
            foreach (var loop in ContourTracer.Trace(scan, cancel))
            {
                cancel.ThrowIfCancellationRequested();
                paths.Add(CurveFitter.Fit(loop, settings));
            }

            return paths;
        }
    }
}
