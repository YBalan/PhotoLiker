namespace PhotoLikerUI
{
    /// <summary>What the slideshow knows of one video to play it: the places a search found in it (milliseconds), how long it is, and when it was recorded.</summary>
    internal sealed record SlideshowVideoFacts(IReadOnlyList<long>? Found, long? LengthMs, DateTime? Taken);

    /// <summary>
    /// How the slideshow plays its videos (see <see cref="SlideshowSession"/>): without sound or with, which part of each (<see cref="Part"/>), by how many percent the other programs'
    /// sound is turned down while one plays with its sound, and what is known of each video. <paramref name="Duck"/> turns the others down to the level it is given and returns what puts
    /// them back; null is what the program does (<see cref="AudioDucking"/>), a test gives its own.
    /// </summary>
    internal sealed record SlideshowVideoOptions(
        bool Mute, SlideshowVideoPart Part, int LowerOtherAudio, Func<string, SlideshowVideoFacts> FactsOf, Func<float, IDisposable?>? Duck = null);
}
