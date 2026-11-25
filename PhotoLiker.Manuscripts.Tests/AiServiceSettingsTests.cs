using System.Text;
using PhotoLiker.Manuscripts.Cloud;

namespace PhotoLiker.Manuscripts.Tests
{
    /// <summary>The keys of the AI services and which service is picked: what is kept between runs, what is never written to the file as it is, and what a file that is damaged or from another account does.</summary>
    public sealed class AiServiceSettingsTests : IDisposable
    {
        private const string Key = "sk-test-0123456789abcdef";

        private readonly string _folder = Path.Combine(Path.GetTempPath(), "photoliker-ai-" + Guid.NewGuid().ToString("N"));
        private string FilePath => Path.Combine(_folder, AiServiceSettings.FileName);

        public void Dispose()
        {
            try
            {
                Directory.Delete(_folder, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
            }
        }

        private AiServiceSettings Load(ISecretProtector? protector = null) => AiServiceSettings.Load(FilePath, protector ?? new MaskingProtector());

        /// <summary>Hides a secret in a way that can be told from the secret itself and is undone (not a secret: it is DPAPI that keeps them in the program).</summary>
        private sealed class MaskingProtector(string mask = "M1") : ISecretProtector
        {
            public string Protect(string secret) => mask + ":" + Convert.ToBase64String(Encoding.UTF8.GetBytes(secret));

            public string? Unprotect(string protectedText) =>
                protectedText.StartsWith(mask + ":", StringComparison.Ordinal) ? Encoding.UTF8.GetString(Convert.FromBase64String(protectedText[(mask.Length + 1)..])) : null;
        }

        // ── Keeping ─────────────────────────────────────────────────────────

        [Fact]
        public void WithoutAFile_NothingIsSet_AndTheModelOnThisComputerIsUsed()
        {
            var settings = Load();

            Assert.Null(settings.Engine);
            Assert.Null(settings.Active);
            foreach (var vendor in AiVendors.All)
            {
                Assert.False(settings.HasKey(vendor));
                Assert.Null(settings.Get(vendor));
            }
        }

        [Fact]
        public void AKeyAndAModel_AreKeptForTheNextRun()
        {
            var settings = Load();
            settings.Set(AiVendor.Claude, Key, "claude-x");
            settings.Engine = AiVendor.Claude;
            settings.Save();

            var again = Load();

            Assert.Equal(new AiAccount(AiVendor.Claude, Key, "claude-x"), again.Get(AiVendor.Claude));
            Assert.Equal(AiVendor.Claude, again.Engine);
            Assert.Equal(again.Get(AiVendor.Claude), again.Active);
            Assert.False(again.HasKey(AiVendor.OpenAI));
        }

        [Fact]
        public void TheKey_IsNeverInTheFileAsItIs()
        {
            var settings = Load();
            settings.Set(AiVendor.OpenAI, Key, "gpt-x");
            settings.Save();

            var text = File.ReadAllText(FilePath);

            Assert.DoesNotContain(Key, text);
            Assert.DoesNotContain("0123456789abcdef", text);
            Assert.Contains("gpt-x", text); // the model is not a secret
        }

        [Fact]
        public void EachServiceKeepsItsOwnKeyAndModel()
        {
            var settings = Load();
            settings.Set(AiVendor.OpenAI, "key-openai", "m-openai");
            settings.Set(AiVendor.Gemini, "key-gemini", "m-gemini");
            settings.Set(AiVendor.Claude, "key-claude", "m-claude");
            settings.Save();

            var again = Load();

            Assert.Equal("key-openai", again.Get(AiVendor.OpenAI)!.ApiKey);
            Assert.Equal("m-gemini", again.Get(AiVendor.Gemini)!.Model);
            Assert.Equal("key-claude", again.Get(AiVendor.Claude)!.ApiKey);
        }

        [Fact]
        public void TheSpacesAroundAKey_AreNotPartOfIt()
        {
            var settings = Load();
            settings.Set(AiVendor.Gemini, "  " + Key + "\r\n", " gemini-x ");

            Assert.Equal(Key, settings.Get(AiVendor.Gemini)!.ApiKey);
            Assert.Equal("gemini-x", settings.Get(AiVendor.Gemini)!.Model);
        }

        [Fact]
        public void WithoutAModel_TheDefaultOneIsAsked()
        {
            var settings = Load();
            settings.Set(AiVendor.Claude, Key, "  ");

            Assert.Equal(AiVendors.DefaultModel(AiVendor.Claude), settings.Get(AiVendor.Claude)!.Model);
            Assert.Equal(AiVendors.DefaultModel(AiVendor.OpenAI), settings.ModelOf(AiVendor.OpenAI)); // even for a service with no key
        }

        [Fact]
        public void AServiceIsPicked_OnlyWhileItHasAKey()
        {
            var settings = Load();
            settings.Engine = AiVendor.Gemini;

            Assert.Null(settings.Active); // picked, but there is no key: the model on this computer works

            settings.Set(AiVendor.Gemini, Key, null);
            Assert.NotNull(settings.Active);
        }

        [Fact]
        public void TakingAKeyAway_AlsoUnpicksTheServiceAndIsKept()
        {
            var settings = Load();
            settings.Set(AiVendor.OpenAI, Key, null);
            settings.Set(AiVendor.Claude, "other-key", null);
            settings.Engine = AiVendor.OpenAI;
            settings.Save();

            settings.Remove(AiVendor.OpenAI);
            Assert.Null(settings.Engine); // at once, not only after the file is read again (that drops a service with no key as well)
            settings.Save();
            var again = Load();

            Assert.False(again.HasKey(AiVendor.OpenAI));
            Assert.Null(again.Engine);
            Assert.True(again.HasKey(AiVendor.Claude));
        }

        [Fact]
        public void TakingAnotherServicesKeyAway_KeepsThePickedOne()
        {
            var settings = Load();
            settings.Set(AiVendor.OpenAI, Key, null);
            settings.Set(AiVendor.Claude, "other-key", null);
            settings.Engine = AiVendor.OpenAI;

            settings.Remove(AiVendor.Claude);

            Assert.Equal(AiVendor.OpenAI, settings.Engine);
        }

        [Fact]
        public void Saving_LeavesNoHalfWrittenFileBehind_AndMakesTheFolder()
        {
            var settings = Load();
            settings.Set(AiVendor.Claude, Key, null);

            settings.Save();

            Assert.True(File.Exists(FilePath));
            Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
        }

        // ── What is not taken ───────────────────────────────────────────────

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("two words")]
        [InlineData("line\nbreak")]
        [InlineData("tab\tin")]
        [InlineData("не-ascii")]
        public void AKeyThatCanNotBeOne_IsNotSet(string key)
        {
            var settings = Load();

            Assert.Throws<ArgumentException>(() => settings.Set(AiVendor.OpenAI, key, null));
            Assert.False(settings.HasKey(AiVendor.OpenAI));
        }

        [Fact]
        public void AKeyThatIsTooLong_IsNotSet()
        {
            var settings = Load();

            Assert.Throws<ArgumentException>(() => settings.Set(AiVendor.OpenAI, new string('k', AiServiceSettings.MaximumKeyLength + 1), null));
            settings.Set(AiVendor.OpenAI, new string('k', AiServiceSettings.MaximumKeyLength), null);
        }

        [Theory]
        [InlineData("gpt-x y")]
        [InlineData("-gpt")]
        [InlineData("gpt?x=1")]
        [InlineData("gp\nt")]
        [InlineData("модель")]
        public void AModelThatCanNotBeOne_IsNotSet(string model)
        {
            var settings = Load();

            Assert.Throws<ArgumentException>(() => settings.Set(AiVendor.OpenAI, Key, model));
        }

        [Theory]
        [InlineData("gpt-6.2-sol", true)]
        [InlineData("claude-opus-5-5", true)]
        [InlineData("ft:gpt-4o:acme::id123", true)]
        [InlineData("models/gemini-x", true)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void WhichModelNames_AreTaken(string? model, bool taken) => Assert.Equal(taken, AiServiceSettings.IsPlausibleModel(model));

        // ── A file that is damaged, or made for someone else ────────────────

        [Theory]
        [InlineData("not json")]
        [InlineData("[]")]
        [InlineData("")]
        [InlineData("{\"services\": 5}")]
        public void AFileThatCanNotBeRead_IsNoSettings_NotACrash(string content)
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllText(FilePath, content);

            var settings = Load();

            Assert.Null(settings.Engine);
            Assert.False(settings.HasKey(AiVendor.OpenAI));
        }

        [Fact]
        public void AServiceThatIsPickedInTheFile_ButHasNoKey_IsNotPicked()
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllText(FilePath, """{"engine":"claude","services":{}}""");

            Assert.Null(Load().Engine);
            Assert.Null(Load().Active);
        }

        [Fact]
        public void AServiceThatIsPickedInTheFile_WithAKeyOfAnotherAccount_IsNotPicked()
        {
            var other = Load(new MaskingProtector("OTHER"));
            other.Set(AiVendor.Gemini, Key, null);
            other.Engine = AiVendor.Gemini;
            other.Save();

            Assert.Equal(AiVendor.Gemini, Load(new MaskingProtector("OTHER")).Engine); // for the account it was made for it is
            Assert.Null(Load(new MaskingProtector("MINE")).Engine); // for this one the model on this computer works
        }

        [Fact]
        public void AServiceTheProgramDoesNotKnow_IsLeftOut()
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllText(FilePath, """{"engine":"mistral","services":{"mistral":{"key":"x","model":"m"},"claude":{"key":"%s","model":"claude-x"}}}""".Replace("%s", new MaskingProtector().Protect(Key)));

            var settings = Load();

            Assert.Null(settings.Engine);
            Assert.Equal(Key, settings.Get(AiVendor.Claude)!.ApiKey);
        }

        [Fact]
        public void AKeyOfAnotherAccount_IsNoKey_ButIsNotLostWhenTheSettingsAreSavedAgain()
        {
            var settings = Load(new MaskingProtector("OTHER"));
            settings.Set(AiVendor.OpenAI, Key, "gpt-x");
            settings.Save();

            // This account can not read it
            var mine = Load(new MaskingProtector("MINE"));
            Assert.False(mine.HasKey(AiVendor.OpenAI));
            Assert.Null(mine.Get(AiVendor.OpenAI));
            mine.Engine = AiVendor.OpenAI;
            Assert.Null(mine.Active);

            // Setting another service's key and saving must not throw the other one away
            mine.Set(AiVendor.Claude, "claude-key", null);
            mine.Save();

            Assert.Equal(Key, Load(new MaskingProtector("OTHER")).Get(AiVendor.OpenAI)!.ApiKey);
            Assert.Equal("claude-key", Load(new MaskingProtector("MINE")).Get(AiVendor.Claude)!.ApiKey);
        }

        // ── Windows keeps the keys ──────────────────────────────────────────

        [Fact]
        public void Windows_KeepsASecretForTheAccount_AndGivesItBack()
        {
            if (!OperatingSystem.IsWindows())
                return;

            var protector = new WindowsSecretProtector();

            var kept = protector.Protect(Key);

            Assert.DoesNotContain(Key, kept);
            Assert.Equal(Key, protector.Unprotect(kept));
            Assert.NotEqual(kept, protector.Protect(Key)); // a new random salt each time: the same key does not look the same twice
        }

        [Theory]
        [InlineData("")]
        [InlineData("not base 64 !")]
        [InlineData("AAAA")]
        public void Windows_ATextThatIsNotASecretOfTheAccount_IsNotOne(string text)
        {
            if (!OperatingSystem.IsWindows())
                return;

            Assert.Null(new WindowsSecretProtector().Unprotect(text));
        }

        [Fact]
        public void Windows_TheSettingsOfTheProgram_KeepTheKeysThroughTheFile()
        {
            if (!OperatingSystem.IsWindows())
                return;

            var settings = AiServiceSettings.Load(FilePath);
            settings.Set(AiVendor.Gemini, Key, "gemini-x");
            settings.Save();

            Assert.DoesNotContain(Key, File.ReadAllText(FilePath));
            Assert.Equal(Key, AiServiceSettings.Load(FilePath).Get(AiVendor.Gemini)!.ApiKey);
        }
    }
}
