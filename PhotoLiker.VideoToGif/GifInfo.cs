namespace PhotoLiker.VideoToGif
{
    /// <summary>
    /// What is in a GIF file, read from its blocks (the picture itself is not decoded): used to tell the person what was made, and by the tests to check that the GIF is what was asked for.
    /// </summary>
    /// <param name="Width">Of the screen the frames are drawn on.</param>
    /// <param name="FrameCount">How many pictures the file holds.</param>
    /// <param name="DurationSeconds">One play of it: the sum of the times each frame is shown.</param>
    /// <param name="LoopCount">The repeat count of the Netscape block: 0 is for ever, a number is that many repeats after the first play; null when there is no such block, which is a GIF that plays once.</param>
    /// <param name="GlobalColors">The size of the palette that holds for all frames (0 when there is none).</param>
    /// <param name="LocalPalettes">How many frames have a palette of their own.</param>
    public sealed record GifInfo(int Width, int Height, int FrameCount, double DurationSeconds, int? LoopCount, int GlobalColors, int LocalPalettes)
    {
        /// <summary>True for a GIF that plays over and over.</summary>
        public bool LoopsForever => LoopCount == 0;

        public static GifInfo Read(string path) => Read(File.ReadAllBytes(path));

        /// <exception cref="InvalidDataException">The bytes are not a GIF (or it is cut off).</exception>
        public static GifInfo Read(byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            if (bytes.Length < 13 || bytes[0] != 'G' || bytes[1] != 'I' || bytes[2] != 'F' || bytes[3] != '8' || (bytes[4] != '7' && bytes[4] != '9') || bytes[5] != 'a')
                throw new InvalidDataException("This is not a GIF file.");

            var width = bytes[6] | bytes[7] << 8;
            var height = bytes[8] | bytes[9] << 8;
            var flags = bytes[10];
            var position = 13;
            var globalColors = 0;
            if ((flags & 0x80) != 0)
            {
                globalColors = 1 << ((flags & 7) + 1);
                position += globalColors * 3;
            }

            int frames = 0, local = 0;
            int? loop = null;
            var centiseconds = 0;
            var delay = 0;
            while (position < bytes.Length)
            {
                var block = bytes[position++];
                switch (block)
                {
                    case 0x3B: // the end of the file
                        return new GifInfo(width, height, frames, centiseconds / 100.0, loop, globalColors, local);

                    case 0x21: // an extension: what kind, then its pieces
                    {
                        if (position >= bytes.Length)
                            throw Cut();

                        var label = bytes[position++];
                        if (label == 0xF9 && position + 5 < bytes.Length && bytes[position] == 4)
                            delay = bytes[position + 2] | bytes[position + 3] << 8; // the time the next frame is shown

                        if (label == 0xFF && position + 12 < bytes.Length && bytes[position] == 11 && System.Text.Encoding.ASCII.GetString(bytes, position + 1, 11) is "NETSCAPE2.0" or "ANIMEXTS1.0")
                        {
                            var data = position + 12;
                            if (data + 3 < bytes.Length && bytes[data] == 3 && bytes[data + 1] == 1)
                                loop = bytes[data + 2] | bytes[data + 3] << 8;
                        }

                        position = SkipSubBlocks(bytes, position);
                        break;
                    }

                    case 0x2C: // a frame: its place and size, a palette of its own perhaps, then the data
                    {
                        if (position + 9 > bytes.Length)
                            throw Cut();

                        var packed = bytes[position + 8];
                        position += 9;
                        if ((packed & 0x80) != 0)
                        {
                            local++;
                            position += (1 << ((packed & 7) + 1)) * 3;
                        }

                        position++; // the smallest code size of the compression
                        position = SkipSubBlocks(bytes, position);
                        frames++;
                        centiseconds += delay;
                        delay = 0;
                        break;
                    }

                    default:
                        throw new InvalidDataException($"This GIF has a block it does not know (0x{block:X2}).");
                }
            }

            throw Cut();
        }

        // The pieces of a block: a length, that many bytes, again, until a length of 0
        private static int SkipSubBlocks(byte[] bytes, int position)
        {
            while (true)
            {
                if (position >= bytes.Length)
                    throw Cut();

                var length = bytes[position++];
                if (length == 0)
                    return position;

                position += length;
            }
        }

        private static InvalidDataException Cut() => new("This GIF is cut off.");
    }
}
