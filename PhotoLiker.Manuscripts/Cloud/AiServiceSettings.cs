using System.Text.Json;
using PhotoLiker.Core;

namespace PhotoLiker.Manuscripts.Cloud
{
    /// <summary>An AI service that has a key: which one, the key, and the model to ask.</summary>
    public sealed record AiAccount(AiVendor Vendor, string ApiKey, string Model);

    /// <summary>
    /// The AI services the person gave a key for (OpenAI, Gemini, Claude), and which of them (or the language model on this computer) corrects the text of a scan. One for the whole program, so it
    /// is kept in the data folder of the program (<see cref="AppPaths.DataFolder"/>) and not with a folder of photos, which can be shared or copied. A key never goes to the file as it is:
    /// <see cref="ISecretProtector"/> makes it unreadable to anyone but this Windows account. The model is not a secret and is written as it is.
    /// </summary>
    public sealed class AiServiceSettings
    {
        /// <summary>The name of the file in the data folder.</summary>
        public const string FileName = "ai-services.json";

        /// <summary>The longest key that is taken (the keys of the services are about a hundred characters).</summary>
        public const int MaximumKeyLength = 512;

        /// <summary>The longest name of a model that is taken.</summary>
        public const int MaximumModelLength = 100;

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        private readonly string _path;
        private readonly ISecretProtector _protector;
        private readonly Dictionary<AiVendor, Entry> _entries = [];

        private AiServiceSettings(string path, ISecretProtector protector)
        {
            _path = path;
            _protector = protector;
        }

        /// <summary>Where the settings are kept unless another place is given.</summary>
        public static string DefaultPath => Path.Combine(AppPaths.DataFolder, FileName);

        /// <summary>The service that corrects the text, or null for the language model on this computer. It only counts while the service has a key, see <see cref="Active"/>.</summary>
        public AiVendor? Engine { get; set; }

        /// <summary>The service that is to correct the text: the one that is picked, when it has a key; null means the model on this computer.</summary>
        public AiAccount? Active => Engine is { } vendor ? Get(vendor) : null;

        /// <summary>The settings in a file; none when the file is not there or can not be read (a person who lost their keys is asked for them again, nothing breaks).</summary>
        /// <param name="path">The file; null for <see cref="DefaultPath"/>.</param>
        /// <param name="protector">What keeps the keys; null for <see cref="WindowsSecretProtector"/>.</param>
        public static AiServiceSettings Load(string? path = null, ISecretProtector? protector = null)
        {
            var settings = new AiServiceSettings(path ?? DefaultPath, protector ?? new WindowsSecretProtector());
            try
            {
                if (!File.Exists(settings._path))
                    return settings;

                var shape = JsonSerializer.Deserialize<FileShape>(File.ReadAllText(settings._path), JsonOptions);
                if (shape is null)
                    return settings;

                settings.Engine = AiVendors.FromCode(shape.Engine);
                foreach (var (code, entry) in shape.Services ?? [])
                {
                    if (AiVendors.FromCode(code) is not { } vendor || entry is null || string.IsNullOrEmpty(entry.Key))
                        continue;

                    // A key made for another account stays as it is (and is written back as it is), but it is not a key to use
                    settings._entries[vendor] = new Entry { Model = entry.Model ?? string.Empty, Protected = entry.Key, Key = settings._protector.Unprotect(entry.Key) };
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                settings._entries.Clear();
                settings.Engine = null;
            }

            // A service that was picked but whose key can not be read now (the file of another Windows account) is not picked: the model on this computer it is, and the box says so
            if (settings.Engine is { } picked && !settings.HasKey(picked))
                settings.Engine = null;

            return settings;
        }

        /// <summary>Whether the service has a key that can be used.</summary>
        public bool HasKey(AiVendor vendor) => _entries.TryGetValue(vendor, out var entry) && entry.Key is not null;

        /// <summary>The service with its key and model; null when it has no key that can be used.</summary>
        public AiAccount? Get(AiVendor vendor) =>
            _entries.TryGetValue(vendor, out var entry) && entry.Key is not null ? new AiAccount(vendor, entry.Key, ModelOf(vendor)) : null;

        /// <summary>The model to ask of the service: the one that was set, else <see cref="AiVendors.DefaultModel"/>.</summary>
        public string ModelOf(AiVendor vendor) =>
            _entries.TryGetValue(vendor, out var entry) && !string.IsNullOrWhiteSpace(entry.Model) ? entry.Model : AiVendors.DefaultModel(vendor);

        /// <summary>Whether a text can be a key: one line of the characters a key has, no spaces.</summary>
        public static bool IsPlausibleKey(string? key) =>
            !string.IsNullOrEmpty(key) && key.Length <= MaximumKeyLength && key.All(character => character is > ' ' and <= '~');

        /// <summary>Whether a text can be the name of a model: letters, digits and . - _ : / only (it goes into an address).</summary>
        public static bool IsPlausibleModel(string? model) =>
            !string.IsNullOrEmpty(model) && model.Length <= MaximumModelLength && char.IsAsciiLetterOrDigit(model[0])
            && model.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' or ':' or '/');

        /// <summary>Sets the key and the model of a service (an empty model is the default one). Not written to the file until <see cref="Save"/>.</summary>
        /// <exception cref="ArgumentException">The key or the model is not one (see <see cref="IsPlausibleKey"/>, <see cref="IsPlausibleModel"/>).</exception>
        public void Set(AiVendor vendor, string key, string? model)
        {
            key = key.Trim();
            model = model?.Trim() ?? string.Empty;
            if (!IsPlausibleKey(key))
                throw new ArgumentException("A key is one line without spaces.", nameof(key));
            if (model.Length > 0 && !IsPlausibleModel(model))
                throw new ArgumentException("A model name has letters, digits and . - _ : / only.", nameof(model));

            _entries[vendor] = new Entry { Model = model, Key = key, Protected = _protector.Protect(key) };
        }

        /// <summary>Forgets the key of a service (and the model). A service that was picked to correct the text is not any more.</summary>
        public void Remove(AiVendor vendor)
        {
            _entries.Remove(vendor);
            if (Engine == vendor)
                Engine = null;
        }

        /// <summary>Writes the settings to the file, all at once (a file that is half written is never left behind).</summary>
        /// <exception cref="IOException">The file can not be written.</exception>
        public void Save()
        {
            var shape = new FileShape
            {
                Engine = Engine is { } engine ? AiVendors.Code(engine) : null,
                Services = _entries.ToDictionary(pair => AiVendors.Code(pair.Key), pair => (FileEntry?)new FileEntry { Model = pair.Value.Model, Key = pair.Value.Protected }),
            };

            var folder = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);

            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(shape, JsonOptions));
            File.Move(temporary, _path, overwrite: true);
        }

        private sealed class Entry
        {
            public string Model { get; init; } = string.Empty;

            /// <summary>The key as it is written to the file.</summary>
            public string Protected { get; init; } = string.Empty;

            /// <summary>The key, null when it could not be read back.</summary>
            public string? Key { get; init; }
        }

        private sealed class FileShape
        {
            public string? Engine { get; set; }

            public Dictionary<string, FileEntry?>? Services { get; set; }
        }

        private sealed class FileEntry
        {
            public string? Model { get; set; }

            public string? Key { get; set; }
        }
    }
}
