namespace PhotoLiker.Manuscripts.Cloud
{
    /// <summary>The companies whose AI services the text of a scan can be corrected with, given a key (see <see cref="AiServiceSettings"/>).</summary>
    public enum AiVendor
    {
        /// <summary>OpenAI: the models of ChatGPT, Chat Completions API.</summary>
        OpenAI,

        /// <summary>Google: the Gemini models, Gemini API.</summary>
        Gemini,

        /// <summary>Anthropic: the Claude models, Messages API.</summary>
        Claude,
    }

    /// <summary>What is known about each <see cref="AiVendor"/>: how it is called, where its keys are made, and which model to start with.</summary>
    public static class AiVendors
    {
        /// <summary>Every vendor, in the order the windows list them.</summary>
        public static IReadOnlyList<AiVendor> All { get; } = [AiVendor.OpenAI, AiVendor.Gemini, AiVendor.Claude];

        /// <summary>The name of the service as the person knows it (a product name: it is not translated).</summary>
        public static string Name(AiVendor vendor) => vendor switch
        {
            AiVendor.OpenAI => "OpenAI (ChatGPT)",
            AiVendor.Gemini => "Google Gemini",
            _ => "Anthropic Claude",
        };

        /// <summary>The short word the settings file and the tests know the vendor by.</summary>
        public static string Code(AiVendor vendor) => vendor switch
        {
            AiVendor.OpenAI => "openai",
            AiVendor.Gemini => "gemini",
            _ => "claude",
        };

        /// <summary>The vendor of a code (see <see cref="Code"/>), or null for anything else.</summary>
        public static AiVendor? FromCode(string? code)
        {
            foreach (var vendor in All)
            {
                if (string.Equals(Code(vendor), code, StringComparison.OrdinalIgnoreCase))
                    return vendor;
            }

            return null;
        }

        /// <summary>
        /// The model a new key starts with. Models are renamed and retired every few months, so this is only a start: the window of the keys can ask the service for the list of its models (see
        /// <see cref="CloudChat.ListModelsAsync"/>), and any name can be typed.
        /// </summary>
        public static string DefaultModel(AiVendor vendor) => vendor switch
        {
            AiVendor.OpenAI => "gpt-6.2-sol",
            AiVendor.Gemini => "gemini-3.8-flash",
            _ => "claude-opus-5-5",
        };

        /// <summary>The page of the vendor where a key is made (https).</summary>
        public static string KeysPage(AiVendor vendor) => vendor switch
        {
            AiVendor.OpenAI => "https://platform.openai.com/api-keys",
            AiVendor.Gemini => "https://aistudio.google.com/apikey",
            _ => "https://platform.claude.com/settings/keys",
        };
    }
}
