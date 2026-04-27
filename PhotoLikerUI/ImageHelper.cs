namespace PhotoLikerUI
{
    using System.Drawing.Imaging;

    public static class ImageHelper
    {
        public static Image LoadImageWithCorrectOrientation(string path)
        {
            var img = Image.FromFile(path);

            const int ExifOrientationId = 0x0112; // 274

            if (img.PropertyIdList.Contains(ExifOrientationId))
            {
                var prop = img.GetPropertyItem(ExifOrientationId);
                int orientation = BitConverter.ToUInt16(prop?.Value ?? new byte[2], 0);

                RotateFlipType rotateFlip = RotateFlipType.RotateNoneFlipNone;

                switch (orientation)
                {
                    case 2: rotateFlip = RotateFlipType.RotateNoneFlipX; break;
                    case 3: rotateFlip = RotateFlipType.Rotate180FlipNone; break;
                    case 4: rotateFlip = RotateFlipType.Rotate180FlipX; break;
                    case 5: rotateFlip = RotateFlipType.Rotate90FlipX; break;
                    case 6: rotateFlip = RotateFlipType.Rotate90FlipNone; break;
                    case 7: rotateFlip = RotateFlipType.Rotate270FlipX; break;
                    case 8: rotateFlip = RotateFlipType.Rotate270FlipNone; break;
                }

                if (rotateFlip != RotateFlipType.RotateNoneFlipNone)
                    img.RotateFlip(rotateFlip);
            }

            return img;
        }
    }
}
