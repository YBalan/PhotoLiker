namespace PhotoLikerUI
{
    /// <summary>
    /// The pictures of a video whose photos have an effect of the slideshow (see <see cref="VideoEffects"/>): each photo is read as the slideshow reads it, made as large as the video lets it be (upright, with the
    /// bars round it black), and drawn frame by frame by <see cref="FrameCompositor"/> as its effect moves it: the drift (and the turn) of Ken Burns and Tilt, the camera that goes across it by its depth
    /// (Parallax, the dolly zoom: the depth model works the depth out), or the portrait blur by depth with the drift on top. A photo whose effect only changes it into the next (a fade, a wipe) stands still;
    /// the changes themselves are made by <see cref="EffectVideoChanges"/>. The cover does not move. The depth of a photo is kept for a while (up to <see cref="DepthKept"/> photos), so that photos that play over
    /// and over for a longer audio do not have to be worked out again.
    /// </summary>
    /// <param name="effects">The effect of every picture of the sequence (see <see cref="VideoEffects.Plan"/>).</param>
    internal sealed class EffectVideoPictures(EffectModels models, IReadOnlyList<SlideshowEffect> effects, Action<string> log) : IVideoPictureSource
    {
        /// <summary>
        /// The time the camera takes to go once across a photo that moves by its depth, as in the slideshow (2.8 s). A photo that is shown for less goes only part of the way, from the middle of it, so that the speed is
        /// the same for every photo (a photo of half a second would otherwise jerk across in half a second); one that is shown for longer goes slower.
        /// </summary>
        public const double CrossingSeconds = 3;

        private const int DepthKept = 400;

        private readonly object _gate = new();
        private readonly Dictionary<string, DepthMap> _depths = new(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<string> _order = new();

        public IVideoPicture Open(int index, string file, int width, int height, double seconds, bool still)
        {
            var effect = still || index < 0 || index >= effects.Count ? SlideshowEffect.None : effects[index];
            var frame = ImageHelper.LoadSlide(new PhotoFile(file, string.Empty), new Size(width, height));
            try
            {
                if (!SlideshowEffects.HasMotion(effect))
                    return new StillPicture(frame, width, height);

                if (!SlideshowEffects.UsesDepth(effect))
                    return new DriftPicture(frame, DriftOf(effect), width, height);

                DepthMap depth;
                try
                {
                    depth = DepthOf(file, frame);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    log(string.Format(VideoStrings.LogEffectDepthFailedFormat, file, ex.Message));
                    return new StillPicture(frame, width, height); // the photo is shown, without motion
                }

                if (SlideshowEffects.BlursByDepth(effect))
                {
                    // The portrait: what is not at the depth of the people is blurred in the pixels of the photo, which then drifts as a Ken Burns photo does
                    frame.EnsureDepth((_, _, _) => depth);
                    frame.ApplyPortrait();
                    return new DriftPicture(frame, DriftOf(effect), width, height);
                }

                IDepthPath path = SlideshowEffects.Dollies(effect) ? DollyPath.Random(Random.Shared.NextDouble) : ParallaxPath.Random(Random.Shared.NextDouble);
                return new MovingPicture(frame, depth, path, width, height, seconds);
            }
            catch
            {
                frame.Dispose();
                throw;
            }
        }

        /// <summary>The slow drift of a photo: at random (the faces the search found are not known here), and turning too for <see cref="SlideshowEffect.Tilt"/>.</summary>
        private static KenBurnsPath DriftOf(SlideshowEffect effect)
        {
            var path = KenBurnsPath.Random(Random.Shared.NextDouble);
            return SlideshowEffects.Tilts(effect) ? path.WithTilt(KenBurnsPath.RandomTilt(Random.Shared.NextDouble)) : path;
        }

        private DepthMap DepthOf(string file, SlideshowFrame frame)
        {
            lock (_gate)
            {
                if (_depths.TryGetValue(file, out var known))
                    return known;
            }

            var depth = models.Depth.Estimate(frame.Pixels, frame.Width, frame.Height);
            lock (_gate)
            {
                if (_depths.TryAdd(file, depth))
                {
                    _order.Enqueue(file);
                    if (_order.Count > DepthKept)
                        _depths.Remove(_order.Dequeue());
                }
            }
            return depth;
        }

        /// <summary>A photo as it is, in the middle of the frame (the cover; a photo that only changes into the next; a photo whose depth could not be worked out).</summary>
        private sealed class StillPicture(SlideshowFrame photo, int width, int height) : IVideoPicture
        {
            private readonly CompositeLayer[] _layers = [new(photo.Pixels, photo.Width, photo.Height, width / 2.0, height / 2.0, 1, FrameCompositor.FullWeight)];

            public void Render(double progress, uint[] frame) => FrameCompositor.Compose(frame, width, height, _layers);

            public void Dispose() => photo.Dispose();
        }

        /// <summary>A photo that drifts and zooms (and turns) the whole time it is shown, as the slideshow draws it.</summary>
        private sealed class DriftPicture(SlideshowFrame photo, KenBurnsPath path, int width, int height) : IVideoPicture
        {
            private readonly CompositeLayer[] _layers = new CompositeLayer[1];

            public void Render(double progress, uint[] frame)
            {
                _layers[0] = SlideshowScreen.LayerOf(photo, path.At(progress), SlideshowLayer.Plain, FrameCompositor.FullWeight, width, height);
                FrameCompositor.Compose(frame, width, height, _layers);
            }

            public void Dispose() => photo.Dispose();
        }

        /// <summary>A photo that moves by its depth: it stays in its place in the frame and what it shows moves inside it.</summary>
        private sealed class MovingPicture(SlideshowFrame photo, DepthMap depth, IDepthPath path, int width, int height, double seconds) : IVideoPicture
        {
            private readonly CompositeLayer[] _layers = new CompositeLayer[1];

            /// <summary>The part of the way across that the camera goes while the photo is shown, centred on the middle (all of it for a photo that is shown as long as a crossing takes or longer).</summary>
            private readonly double _share = Math.Clamp(seconds / CrossingSeconds, 0.05, 1);

            public void Render(double progress, uint[] frame)
            {
                var along = 0.5 + (Math.Clamp(progress, 0, 1) - 0.5) * _share;
                var view = path.At(along, photo.Width, photo.Height);
                _layers[0] = new CompositeLayer(
                    photo.Pixels, photo.Width, photo.Height, width / 2.0, height / 2.0, view.Zoom, FrameCompositor.FullWeight,
                    new DepthShift(depth.Values, depth.Width, depth.Height, view.ShiftX, view.ShiftY, ParallaxPath.Focus, view.Radial));
                FrameCompositor.Compose(frame, width, height, _layers);
            }

            public void Dispose() => photo.Dispose();
        }
    }
}
