namespace PhotoLiker.Video
{
    /// <summary>How the cores given to the speech recognition are used: how many videos are heard at the same time, and how many threads each of them gets.</summary>
    public readonly record struct SpeechLayout(int Workers, int ThreadsEach)
    {
        /// <summary>How many cores the layout keeps busy.</summary>
        public int Cores => Workers * ThreadsEach;
    }

    /// <summary>
    /// Shares the cores out between the videos. Whisper does not use many threads on one file well (measured on a 6 core / 12 thread processor with the small model: 4 threads
    /// 1.0 times the speed of the speech, 6 threads 1.2, 12 threads 1.4), and between two videos the processor sits idle while the sound of the next is taken out and its state
    /// is made; so up to <see cref="ThreadsPerVideo"/> cores go to one video, and with more cores than that the next video is heard at the same time. 12 threads as 3 videos
    /// of 4 were the fastest of the layouts that were tried (1.5 times), and each video heard at once costs about 350 MB of memory for the state of the model.
    /// </summary>
    public static class SpeechCores
    {
        /// <summary>The cores that one video gets before another video is heard beside it.</summary>
        public const int ThreadsPerVideo = 4;

        /// <summary>
        /// The layout for this many cores (below 1 counts as 1): 1 to 7 cores are one video with all of them, 8 to 11 two videos, 12 to 15 three, and so on, each with an equal
        /// share of the cores (the remainder is not used, as 11 cores make 2 videos of 5).
        /// </summary>
        public static SpeechLayout LayoutFor(int cores)
        {
            cores = Math.Max(1, cores);
            var workers = Math.Max(1, cores / ThreadsPerVideo);
            return new SpeechLayout(workers, Math.Max(1, cores / workers));
        }
    }
}
