using System.Collections.Specialized;

namespace PhotoLikerUI
{
    /// <summary>
    /// Builds what "Copy" puts on the clipboard for a photo: the file itself, so Explorer (and messengers that
    /// attach files) can paste it, and the decoded picture, so image editors and messengers that take pictures can too.
    /// </summary>
    internal static class PhotoClipboard
    {
        private const string PreferredDropEffectFormat = "Preferred DropEffect";
        private const int DropEffectCopy = 1;

        /// <param name="image">The decoded photo, if it is loaded already; without it only the file is offered.</param>
        public static DataObject CreateDataObject(string filePath, Image? image)
        {
            var data = new DataObject();
            data.SetFileDropList(new StringCollection { filePath });

            // Without this Explorer decides between copy and move itself; a copy must never move the original.
            data.SetData(PreferredDropEffectFormat, new MemoryStream(BitConverter.GetBytes(DropEffectCopy)));

            if (image is not null)
                data.SetImage(image);

            return data;
        }
    }
}
