using System.Numerics;
using System.Runtime.InteropServices;

namespace PhotoLiker.Search
{
    internal static class VectorMath
    {
        public static float Dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
        {
            var length = Math.Min(a.Length, b.Length);
            var step = Vector<float>.Count;
            var sum = Vector<float>.Zero;

            var i = 0;
            for (; i <= length - step; i += step)
                sum += new Vector<float>(a.Slice(i, step)) * new Vector<float>(b.Slice(i, step));

            var result = Vector.Sum(sum);
            for (; i < length; i++)
                result += a[i] * b[i];
            return result;
        }

        /// <summary>Scales the vector to length 1, so that the dot product of two vectors is their cosine similarity.</summary>
        public static void Normalize(Span<float> vector)
        {
            var norm = MathF.Sqrt(Dot(vector, vector));
            if (norm <= 0f)
                return;
            for (var i = 0; i < vector.Length; i++)
                vector[i] /= norm;
        }

        public static byte[] ToBytes(float[] vector) => MemoryMarshal.AsBytes<float>(vector).ToArray();

        public static float[] ToFloats(ReadOnlySpan<byte> bytes) => MemoryMarshal.Cast<byte, float>(bytes).ToArray();
    }
}
