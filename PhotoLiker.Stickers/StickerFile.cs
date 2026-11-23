namespace PhotoLiker.Stickers
{
    /// <summary>What is wrong with a file as a Telegram sticker.</summary>
    [Flags]
    public enum StickerProblems
    {
        None = 0,

        /// <summary>Not 512 pixels on the longer side, or the other side more than 512.</summary>
        Size = 1,

        /// <summary>Bigger than <see cref="StickerFile.MaxBytes"/>.</summary>
        Bytes = 2,

        /// <summary>No see-through background: Telegram takes it, but a sticker without one is a square picture.</summary>
        NoTransparency = 4,
    }

    /// <summary>What a sticker file is: read from its own header, not from how it was made.</summary>
    public sealed record StickerFileInfo(StickerFormat Format, int Width, int Height, bool HasAlpha, long Bytes);

    /// <summary>The limits Telegram sets for a static sticker, and a reader that tells what a file of a WebP or PNG is.</summary>
    public static class StickerFile
    {
        /// <summary>The largest file: Telegram says 512 KB, and this is a little under 512 x 1024 so that it fits whichever kilobyte is meant.</summary>
        public const long MaxBytes = 512_000;

        /// <summary>What is wrong with a file as a sticker (<see cref="StickerProblems.None"/>: nothing).</summary>
        public static StickerProblems Check(StickerFileInfo info)
        {
            ArgumentNullException.ThrowIfNull(info);

            var problems = StickerProblems.None;
            var longer = Math.Max(info.Width, info.Height);
            var shorter = Math.Min(info.Width, info.Height);
            if (longer != StickerRenderer.Side || shorter < 1)
                problems |= StickerProblems.Size;
            if (info.Bytes > MaxBytes)
                problems |= StickerProblems.Bytes;
            if (!info.HasAlpha)
                problems |= StickerProblems.NoTransparency;
            return problems;
        }

        /// <summary>What the bytes of a WebP or PNG file are, or null when they are neither (or too short to tell).</summary>
        public static StickerFileInfo? Inspect(byte[] file)
        {
            ArgumentNullException.ThrowIfNull(file);
            return InspectPng(file) ?? InspectWebP(file);
        }

        /// <summary>What a file is; null when it is not there, can not be read, or is neither a WebP nor a PNG.</summary>
        public static StickerFileInfo? Inspect(string path)
        {
            try
            {
                return Inspect(File.ReadAllBytes(path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static StickerFileInfo? InspectPng(byte[] file)
        {
            ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
            if (file.Length < 33 || !file.AsSpan(0, 8).SequenceEqual(signature) || file[12] != 'I' || file[13] != 'H' || file[14] != 'D' || file[15] != 'R')
                return null;

            var width = (file[16] << 24) | (file[17] << 16) | (file[18] << 8) | file[19];
            var height = (file[20] << 24) | (file[21] << 16) | (file[22] << 8) | file[23];
            var colourType = file[25];

            // Colour types 4 and 6 have an alpha of their own; a palette (3) is see-through when a tRNS chunk says so
            var hasAlpha = colourType is 4 or 6;
            if (colourType == 3)
            {
                var at = 8;
                while (at + 12 <= file.Length)
                {
                    var length = (file[at] << 24) | (file[at + 1] << 16) | (file[at + 2] << 8) | file[at + 3];
                    if (file[at + 4] == 't' && file[at + 5] == 'R' && file[at + 6] == 'N' && file[at + 7] == 'S')
                    {
                        hasAlpha = true;
                        break;
                    }
                    if (length < 0 || at + 12L + length > file.Length)
                        break;
                    at += 12 + length;
                }
            }
            return new StickerFileInfo(StickerFormat.Png, width, height, hasAlpha, file.Length);
        }

        private static StickerFileInfo? InspectWebP(byte[] file)
        {
            if (file.Length < 30 || !Is(file, 0, "RIFF") || !Is(file, 8, "WEBP"))
                return null;

            if (Is(file, 12, "VP8X"))
            {
                var hasAlpha = (file[20] & 0x10) != 0;
                var width = (file[24] | (file[25] << 8) | (file[26] << 16)) + 1;
                var height = (file[27] | (file[28] << 8) | (file[29] << 16)) + 1;
                return new StickerFileInfo(StickerFormat.WebP, width, height, hasAlpha, file.Length);
            }

            if (Is(file, 12, "VP8L") && file[20] == 0x2F)
            {
                // 14 bits of width - 1, 14 bits of height - 1, then one bit that says whether the alpha is used
                var bits = file[21] | ((uint)file[22] << 8) | ((uint)file[23] << 16) | ((uint)file[24] << 24);
                var width = (int)(bits & 0x3FFF) + 1;
                var height = (int)((bits >> 14) & 0x3FFF) + 1;
                var hasAlpha = ((bits >> 28) & 1) != 0;
                return new StickerFileInfo(StickerFormat.WebP, width, height, hasAlpha, file.Length);
            }

            if (Is(file, 12, "VP8 ") && file[23] == 0x9D && file[24] == 0x01 && file[25] == 0x2A)
            {
                var width = (file[26] | (file[27] << 8)) & 0x3FFF;
                var height = (file[28] | (file[29] << 8)) & 0x3FFF;
                return new StickerFileInfo(StickerFormat.WebP, width, height, false, file.Length);
            }

            return null;
        }

        private static bool Is(byte[] file, int at, string text)
        {
            for (var i = 0; i < text.Length; i++)
            {
                if (file[at + i] != text[i])
                    return false;
            }
            return true;
        }
    }
}
