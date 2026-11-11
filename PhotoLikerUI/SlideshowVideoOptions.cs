using PhotoLiker.Video;

namespace PhotoLikerUI
{
    /// <summary>
    /// What the slideshow knows of one video to play it: the places a search found in it (milliseconds), how long it is, when it was recorded, and what is said in it (the speech the index
    /// read; null or empty when it was not read, which is also what a video without speech has).
    /// </summary>
    internal sealed record SlideshowVideoFacts(IReadOnlyList<long>? Found, long? LengthMs, DateTime? Taken, IReadOnlyList<SpeechSegment>? Speech = null);

    /// <summary>
    /// How the slideshow plays its videos (see <see cref="SlideshowSession"/>): without sound or with, which part of each (<see cref="Part"/>), by how many percent the other programs'
    /// sound is turned down while one plays with its sound, and what is known of each video. <paramref name="Duck"/> turns the others down to the level it is given and returns what puts
    /// them back; null is what the program does (<see cref="AudioDucking"/>), a test gives its own. With <paramref name="ShowCaptions"/> what is said in a video is written over it.
    /// </summary>
    internal sealed record SlideshowVideoOptions(
        bool Mute, SlideshowVideoPart Part, int LowerOtherAudio, Func<string, SlideshowVideoFacts> FactsOf, Func<float, IDisposable?>? Duck = null, bool ShowCaptions = false);
}
