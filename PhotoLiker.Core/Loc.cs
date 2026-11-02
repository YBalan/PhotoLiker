namespace PhotoLiker.Core
{
    /// <summary>
    /// The language the program speaks and where its text comes from. The text classes of every assembly (<c>AboutStrings</c> ...) look their texts
    /// up through <see cref="Get(string, string)"/>, so nothing else needs to know which language is on. The language is one for the whole program
    /// (it is stored in the global config, not per photo folder). Where the texts come from is described at <see cref="AppLanguage"/>.
    /// </summary>
    public static class Loc
    {
        private static volatile AppLanguage? _language;
        private static readonly AsyncLocal<AppLanguage?> Scoped = new();

        /// <summary>
        /// The language that is on: the one set by <see cref="Use"/> for the code that is running, else the one of the program (English until it
        /// is set). Setting it raises <see cref="Changed"/> when it is another language, or the same one read again from a file that changed.
        /// </summary>
        public static AppLanguage Language
        {
            get => Scoped.Value ?? _language ?? AppLanguage.English;
            set
            {
                if (ReferenceEquals(_language, value))
                    return;

                _language = value;
                Changed?.Invoke();
            }
        }

        /// <summary>Raised on the thread that changed <see cref="Language"/>; a window re-writes its texts when it hears it.</summary>
        public static event Action? Changed;

        /// <summary>
        /// Puts a language on for the code that is running (and what it starts) until the result is disposed, without touching the
        /// language of the program: for tests, which run side by side and must not change each other's texts.
        /// </summary>
        public static IDisposable Use(AppLanguage language)
        {
            var before = Scoped.Value;
            Scoped.Value = language;
            return new Restore(before);
        }

        /// <summary>The text of a name ("AboutStrings.Heading") in the language that is on; see <see cref="AppLanguage.Resolve"/>.</summary>
        public static string Get(string name) => Language.Resolve(name);

        /// <summary>The text <paramref name="member"/> of the class <paramref name="area"/>.</summary>
        public static string Get(string area, string member) => Language.Resolve($"{area}.{member}");

        private sealed class Restore(AppLanguage? before) : IDisposable
        {
            public void Dispose() => Scoped.Value = before;
        }
    }
}
