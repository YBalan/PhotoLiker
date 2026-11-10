namespace PhotoLiker.Video
{
    /// <summary>The words of the exported tables, in the language of the program (the library has none of its own).</summary>
    public sealed record SpeechWorkbookLabels(
        string VideosSheet, string SpeechSheet,
        string Video, string Length, string Recorded, string Status, string Language, string Moments, string Segments, string MomentTimes, string Speech,
        string Start, string End, string Text,
        Func<SpeechStatus, string> StatusName);

    /// <summary>
    /// The tables of the "all videos" export: one sheet with a row for every video (what is said in it, in which language, where its moments are), and one with a row for every stretch
    /// of speech, which is what to filter and search in when the text of a video is long.
    /// </summary>
    public static class SpeechWorkbook
    {
        /// <summary>Builds the sheets from the rows of <see cref="VideoCatalog.SpeechTable"/>: every video, also the ones nothing is known of.</summary>
        public static IReadOnlyList<XlsxSheet> Build(IReadOnlyList<VideoSpeechRow> rows, SpeechWorkbookLabels labels)
        {
            var videos = new List<object?[]>(rows.Count);
            var speech = new List<object?[]>();

            foreach (var row in rows.OrderBy(r => r.Video.RelativePath, StringComparer.OrdinalIgnoreCase))
            {
                var video = row.Video;
                videos.Add(
                [
                    video.RelativePath.Replace('/', '\\'),
                    video.Info is { } info ? TimeSpan.FromSeconds(info.DurationSeconds) : null,
                    video.Date.Taken,
                    labels.StatusName(row.Status),
                    SpeechLanguages.Describe(row.Language),
                    row.MomentTimesMs.Count,
                    row.Segments.Count,
                    string.Join(", ", row.MomentTimesMs.Select(VideoTime.Format)),
                    row.Text,
                ]);

                foreach (var segment in row.Segments)
                {
                    speech.Add(
                    [
                        video.RelativePath.Replace('/', '\\'),
                        TimeSpan.FromMilliseconds(segment.StartMs),
                        TimeSpan.FromMilliseconds(segment.EndMs),
                        segment.Text.Trim(),
                    ]);
                }
            }

            return
            [
                new XlsxSheet(labels.VideosSheet,
                [
                    new XlsxColumn(labels.Video, 60),
                    new XlsxColumn(labels.Length, 11, XlsxFormat.Duration),
                    new XlsxColumn(labels.Recorded, 17, XlsxFormat.DateTime),
                    new XlsxColumn(labels.Status, 16),
                    new XlsxColumn(labels.Language, 18),
                    new XlsxColumn(labels.Moments, 10),
                    new XlsxColumn(labels.Segments, 10),
                    new XlsxColumn(labels.MomentTimes, 30),
                    new XlsxColumn(labels.Speech, 100),
                ], videos),
                new XlsxSheet(labels.SpeechSheet,
                [
                    new XlsxColumn(labels.Video, 60),
                    new XlsxColumn(labels.Start, 11, XlsxFormat.Duration),
                    new XlsxColumn(labels.End, 11, XlsxFormat.Duration),
                    new XlsxColumn(labels.Text, 110),
                ], speech),
            ];
        }
    }
}
