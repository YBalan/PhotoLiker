namespace PhotoLikerUI
{
    /// <summary>
    /// How the slideshow gets the depth of a photo for <see cref="SlideshowEffect.Parallax"/>: <see cref="Estimate"/> takes the pixels of a frame (0xAARRGGBB, row after row), its width and height,
    /// and gives how near each part of it is, or null when that could not be worked out. It is called on the threads that make the frames, several at a time, and takes half a second of a processor.
    /// A session is given these only when the depth model is on this computer.
    /// </summary>
    internal sealed record SlideshowDepthOptions(Func<uint[], int, int, DepthMap?> Estimate)
    {
        /// <summary>The options for the models of the effects: the model is opened when the first photo is asked for, and a failure to open it (or to run it) gives no depth, not an error.</summary>
        public static SlideshowDepthOptions Of(EffectModels models) => new((pixels, width, height) =>
        {
            try
            {
                return models.Depth.Estimate(pixels, width, height);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return null;
            }
        });
    }
}
