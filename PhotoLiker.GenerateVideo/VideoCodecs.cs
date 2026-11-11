namespace PhotoLiker.GenerateVideo
{
    /// <summary>
    /// The video codecs the Generate Video window offers, and the quality (CRF) each of them starts with. CRF is not the same scale for the two: the number that gives a picture
    /// of the same look is about 5 higher for H.265 than for H.264, so a window that moves from one codec to the other moves the quality along (unless it was set by hand).
    /// </summary>
    public static class VideoCodecs
    {
        public const string H264 = "libx264";
        public const string H265 = "libx265";

        /// <summary>The codec a window starts with when nothing is saved: H.265 makes a smaller file for the same look (and not every player reads it).</summary>
        public const string Default = H265;

        public const int MinCrf = 0;
        public const int MaxCrf = 51;

        /// <summary>The usual CRF of H.264 (ffmpeg's own default).</summary>
        public const int H264Crf = 23;

        /// <summary>The usual CRF of H.265 (ffmpeg's own default): about the look of 23 in H.264, in a smaller file.</summary>
        public const int H265Crf = 28;

        /// <summary>The CRF a codec starts with: 28 for libx265, 23 for libx264 and for any codec this does not know.</summary>
        public static int DefaultCrf(string? codec) =>
            string.Equals(codec?.Trim(), H265, StringComparison.OrdinalIgnoreCase) ? H265Crf : H264Crf;

        /// <summary>
        /// The CRF after the codec was changed from <paramref name="from"/> to <paramref name="to"/>: the CRF the new codec starts with if it was still the one the old codec starts with
        /// (nobody changed it), else the same CRF (it was chosen).
        /// </summary>
        public static int CrfAfterCodecChange(string? from, string? to, int crf) =>
            crf == DefaultCrf(from) ? DefaultCrf(to) : crf;
    }
}
