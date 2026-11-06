using PhotoLiker.Search;

namespace PhotoLiker.Video
{
    /// <summary>Finds the faces in the picture of a moment, with the same models and the same rules as for a photo (<see cref="IndexingJob"/>).</summary>
    public static class MomentFaces
    {
        /// <summary>
        /// The faces of a picture: found by the detector, the biggest first, none smaller than <see cref="IndexingJob.MinFaceSize"/> pixels (of a picture whose longer
        /// side is <see cref="VideoFrames.LongSide"/>, not the 1280 of a photo: a face in the far distance of a video is not found), each with the vector the face model
        /// made of it. The boxes are fractions of the picture.
        /// </summary>
        public static IReadOnlyList<MomentFace> Find(DecodedPhoto picture, FaceDetector detector, FaceEmbedder embedder)
        {
            var (tensor, scale) = FaceDetector.Prepare(picture);
            var found = detector.Detect(tensor, scale)
                .Where(face => Math.Min(face.Width, face.Height) >= IndexingJob.MinFaceSize)
                .OrderByDescending(face => face.Width * face.Height)
                .Take(IndexingJob.MaxFacesPerPhoto)
                .ToList();
            if (found.Count == 0)
                return [];

            var crops = new float[found.Count * FaceAligner.Length];
            for (var i = 0; i < found.Count; i++)
                Array.Copy(FaceAligner.Align(picture, found[i].Landmarks), 0, crops, i * FaceAligner.Length, FaceAligner.Length);
            var embeddings = embedder.Embed(crops, found.Count);

            var faces = new List<MomentFace>(found.Count);
            for (var i = 0; i < found.Count; i++)
            {
                var face = found[i];
                var x = Math.Clamp(face.X / picture.Width, 0f, 1f);
                var y = Math.Clamp(face.Y / picture.Height, 0f, 1f);
                faces.Add(new MomentFace(x, y, Math.Min(face.Width / picture.Width, 1f - x), Math.Min(face.Height / picture.Height, 1f - y), face.Score, embeddings[i]));
            }
            return faces;
        }
    }
}
