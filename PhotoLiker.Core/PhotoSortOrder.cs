namespace PhotoLiker.Core
{
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>The order the photos of a folder are listed in, see <see cref="PhotoScanner.Scan"/>.</summary>
    public enum PhotoSortOrder
    {
        /// <summary>By the date the file was created, the oldest first.</summary>
        OldestFirst,

        /// <summary>By the date the file was created, the newest first.</summary>
        NewestFirst,

        /// <summary>As the file system lists them (by name on most local disks). No sorting, which is the quickest.</summary>
        None,

        /// <summary>By the name of the file, as Explorer lists them (see <see cref="PhotoNameOrder"/>). Last, so that the numbers of the others stay.</summary>
        ByName,
    }

    /// <summary>
    /// Reads the order by its name. A name this version doesn't know (the folder was last used by a newer version) or a
    /// typo gives the default, rather than failing the whole settings file.
    /// </summary>
    internal sealed class PhotoSortOrderConverter : JsonConverter<PhotoSortOrder>
    {
        public override PhotoSortOrder Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var order = PhotoSortOrder.OldestFirst;
            if (reader.TokenType == JsonTokenType.String &&
                Enum.TryParse<PhotoSortOrder>(reader.GetString(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
                order = parsed;

            reader.Skip();
            return order;
        }

        public override void Write(Utf8JsonWriter writer, PhotoSortOrder value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }
}
